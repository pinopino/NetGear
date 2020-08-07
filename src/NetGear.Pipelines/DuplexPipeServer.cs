﻿using NetGear.Core;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Pipelines
{
    public abstract class DuplexPipeServer : IConnectionDispatcher, IDisposable
    {
        /// <summary>
        /// The state of a client connection
        /// </summary>
        protected readonly struct ClientConnection
        {
            internal ClientConnection(IDuplexPipe connection, EndPoint remoteEndPoint)
            {
                Connection = connection;
                RemoteEndPoint = remoteEndPoint;
            }

            /// <summary>
            /// The transport to use for this connection
            /// </summary>
            public IDuplexPipe Connection { get; }

            /// <summary>
            /// The remote endpoint that the client connected from
            /// </summary>
            public EndPoint RemoteEndPoint { get; }
        }

        protected class Client : DuplexPipe
        {
            private readonly DuplexPipeServer _server;

            public EndPoint RemoteEndPoint { get; }

            public Client(IDuplexPipe pipe, EndPoint remoteEndPoint, DuplexPipeServer server)
                : base(pipe)
            {
                _server = server;
                RemoteEndPoint = remoteEndPoint;
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
        protected ITransport _transport;
        private readonly Action<object> _runClientAsync;
        private readonly ConcurrentDictionary<Client, long> _clients;
        private readonly TaskCompletionSource<object> _stoppedTcs;

        protected DuplexPipeServer()
        {
            _clients = new ConcurrentDictionary<Client, long>();
            _stoppedTcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);

            _runClientAsync = async boxed =>
            {
                var info = (ClientConnection)boxed;
                var client = new Client(info.Connection, info.RemoteEndPoint, this);
                AddClient(client);

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
                    RemoveClient(client);
                    OnClientDisconnected(client);
                }
            };
        }

        public virtual async Task StartAsync(IEndPointInformation endPoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            if (_hasStarted)
                throw new InvalidOperationException("server has already started");
            _hasStarted = true;

            _transport = new SocketTransport(endPoint, this, null);

            await _transport.BindAsync().ConfigureAwait(false);

            OnServerStarted(endPoint);
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

        public virtual Task OnConnection(IDuplexPipe connection)
        {
            _runClientAsync(connection);
            return Task.CompletedTask;
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
            Console.WriteLine($"新连接已建立<{client.RemoteEndPoint}>，当前总连接数：{ClientsCount}");
        }

        /// <summary>
        /// Invoked when a client has disconnected
        /// </summary>
        protected virtual void OnClientDisconnected(Client client)
        {
            Console.WriteLine($"连接<{client.RemoteEndPoint}>已断开@{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")}");
        }

        /// <summary>
        /// Invoked when a client has faulted
        /// </summary>
        protected virtual void OnClientFaulted(Client client, Exception exception)
        {
            Console.WriteLine($"连接<{client.RemoteEndPoint}>异常，消息：{exception.Message}");
        }

        public int ClientsCount
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(ToString());

                return _clients.Count;
            }
        }

        private void AddClient(Client client)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            _clients.TryAdd(client, DateTime.UtcNow.Ticks);
        }

        private void RemoveClient(Client client)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            _clients.TryRemove(client, out _);
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
            foreach (var client in _clients.Keys)
            {
                try
                {
                    await client.SendAsync(message);
                    count++;
                }
                catch { } // ignore failures on specific clients
            }

            return count;
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var client in _clients.Keys)
            {
                client.Dispose();
            }
            _clients.Clear();
        }
    }
}