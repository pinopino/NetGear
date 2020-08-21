using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetGear.Core;
using NetGear.Pipelines.Server;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Pipelines
{
    public abstract partial class DuplexPipeServer : IConnectionDispatcher, IHeartbeatHandler, IDisposable
    {
        protected class Client : DuplexPipe
        {
            private readonly DuplexPipeServer _server;

            public string ID => Connection.ConnectionId;

            public TransportConnection Connection { private set; get; }

            public Client(DuplexPipeServer server, TransportConnection connection)
                : base(connection as SocketConnection)
            {
                _server = server;
                Connection = connection;
            }

            public Task RunAsync(CancellationToken cancellationToken = default)
                => StartReceiveLoopAsync(cancellationToken);

            public ValueTask SendAsync(ReadOnlyMemory<byte> message)
            {
                return WriteAsync(message, 0);
            }

            protected sealed override ValueTask OnReceiveAsync(ReadOnlySequence<byte> payload, int messageId)
            {
                async void AwaitServerToReply(ValueTask<IMemoryOwner<byte>> pendingResponse, int msgId,
                    IMemoryOwner<byte> message)
                {
                    try
                    {
                        using (message)
                        {
                            var response = await pendingResponse;
                            await WriteAsync(response, msgId);
                        }
                    }
                    catch { }
                }

                void DisposeOnCompletion(ValueTask task, ref IMemoryOwner<byte> message)
                {
                    task.AsTask().ContinueWith((t, s) => ((IMemoryOwner<byte>)s)?.Dispose(), message);
                    message = null; // caller no longer owns it, logically; don't wipe on exit
                }

                var msg = payload.Lease();
                try
                {
                    if (messageId == 0)
                    {
                        var pending = _server.OnReceiveAsync(msg);
                        if (!pending.IsCompletedSuccessfully)
                            DisposeOnCompletion(pending, ref msg);
                    }
                    else
                    {
                        var pending = _server.OnReceiveForReplyAsync(msg);
                        if (pending.IsCompletedSuccessfully)
                        {
                            var writeResult = WriteAsync(pending.Result, messageId);
                            if (!writeResult.IsCompletedSuccessfully)
                                DisposeOnCompletion(writeResult, ref msg);
                        }
                        else
                        {
                            AwaitServerToReply(pending, messageId, msg);
                            msg = null;
                        }
                    }
                }
                finally
                {
                    if (msg != null)
                        try { msg.Dispose(); } catch { }
                }

                return default;
            }
        }

        private int _stopping;
        private bool _hasStarted;
        protected bool _disposed;
        protected ILogger _logger;
        protected ITransport _transport;
        private readonly ConnectionDelegate _runClientAsync;
        private readonly TaskCompletionSource<object> _stoppedTcs;
        private Heartbeat _heartbeat;

        protected DuplexPipeServer()
        {
            _clientReferences = new ConcurrentDictionary<long, ClientReference>();
            _stoppedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            _runClientAsync = async conn =>
            {
                var id = Interlocked.Increment(ref _lastConnectionId);
                var client = new Client(this, conn);
                AddClient(id, client);

                try
                {
                    OnClientConnected(client);
                    await client.RunAsync();
                    try { client.Input.Complete(); } catch { }
                    try { client.Output.Complete(); } catch { }
                }
                catch (Exception ex)
                {
                    try { client.Input.Complete(ex); } catch { }
                    try { client.Output.Complete(ex); } catch { }
                    OnClientFaulted(client, ex);
                }
                finally
                {
                    client.Dispose();
                    RemoveClient(id);
                    OnClientDisconnected(client);
                }
            };
        }

        public Task StartAsync(IPEndPoint endPoint, int listenBacklog = 512, bool isHeartbeat = false)
            => StartAsync(endPoint, listenBacklog, isHeartbeat, null, null);

        public virtual async Task StartAsync(IPEndPoint endPoint,
            int listenBacklog,
            bool isHeartbeat,
            PipeOptions outputPipeOptions,
            PipeOptions inputPipeOptions,
            ILogger logger = null)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            if (_hasStarted)
                throw new InvalidOperationException("server has already started");
            _hasStarted = true;

            _logger = logger ?? NullLoggerFactory.Instance.CreateLogger("NetGear.Pipelines.DuplexPipeServer");
            var endPointInfo = new ListenOptions(endPoint);
            if (outputPipeOptions == null || inputPipeOptions == null)
            {
                _transport = new SocketTransport(endPointInfo,
                    this,
                    listenBacklog,
                    Environment.ProcessorCount * 2,
                    MemoryPool<byte>.Shared);
            }
            else
            {
                _transport = new SocketTransport(endPointInfo,
                    this,
                    listenBacklog,
                    outputPipeOptions,
                    inputPipeOptions);
            }

            if (isHeartbeat)
                (_heartbeat ?? (_heartbeat = new Heartbeat(new IHeartbeatHandler[] { this }))).Start();

            await _transport.BindAsync().ConfigureAwait(false);

            OnServerStarted(endPointInfo);
        }

        public virtual async Task StopAsync()
        {
            if (Interlocked.Exchange(ref _stopping, 1) == 1)
            {
                await _stoppedTcs.Task.ConfigureAwait(false);
                return;
            }

            try
            {
                await _transport.UnbindAsync();
                await _transport.StopAsync();
            }
            catch (ListenLoopException ex)
            {
                OnServerFaulted(ex.InnerException);
                _stoppedTcs.TrySetException(ex);
            }
            catch (Exception ex)
            {
                _stoppedTcs.TrySetException(ex);
                throw;
            }

            _stoppedTcs.TrySetResult(null);
        }

        // 这个方法我始终觉得不应该为public
        public Task OnConnection(TransportConnection connection)
        {
            connection.ConnectionId = CorrelationIdGenerator.GetNextId();
            return _runClientAsync(connection);
        }

        /// <summary>
        /// Invoked when the server starts
        /// </summary>
        protected virtual void OnServerStarted(IEndPointInformation endPoint)
        {
            Console.WriteLine($"服务端开始监听@{endPoint}");
        }

        /// <summary>
        /// Invoked when the server has faulted
        /// </summary>
        protected virtual void OnServerFaulted(Exception exception)
        {
            Console.WriteLine($"服务端异常，消息：{exception.Message}");
        }

        /// <summary>
        /// Invoked when a new client connects
        /// </summary>
        protected virtual void OnClientConnected(Client client)
        {
            Console.WriteLine($"新连接已建立<{client.Connection.RemoteAddress}>，当前总连接数：{ClientsCount}");
        }

        /// <summary>
        /// Invoked when a client has disconnected
        /// </summary>
        protected virtual void OnClientDisconnected(Client client)
        {
            Console.WriteLine($"连接<{client.Connection.RemoteAddress}>已断开@{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")}");
        }

        /// <summary>
        /// Invoked when a client has faulted
        /// </summary>
        protected virtual void OnClientFaulted(Client client, Exception exception)
        {
            Console.WriteLine($"连接<{client.Connection.RemoteAddress}>异常，消息：{exception.Message}");
        }

        protected virtual ValueTask OnReceiveAsync(IMemoryOwner<byte> message) => default;

        protected abstract ValueTask<IMemoryOwner<byte>> OnReceiveForReplyAsync(IMemoryOwner<byte> message);

        public async ValueTask<int> BroadcastAsync(IMemoryOwner<byte> message)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            using (message)
            {
                return await BroadcastAsync(message.Memory);
            }
        }

        public async ValueTask<int> BroadcastAsync(ReadOnlyMemory<byte> message)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            int count = 0;
            foreach (var clientRef in _clientReferences.Values)
            {
                try
                {
                    if (clientRef.TryGetClient(out Client client))
                    {
                        await client.SendAsync(message);
                        count++;
                    }
                }
                catch { } // ignore failures on specific clients
            }

            return count;
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var clientRef in _clientReferences.Values)
            {
                if (clientRef.TryGetClient(out Client client))
                    client.Dispose();
            }
            _clientReferences.Clear();
        }
    }
}
