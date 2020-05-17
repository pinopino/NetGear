using Microsoft.Extensions.Logging;
using NetGear.Core;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public abstract class UvTcpServer : IDisposable
    {
        private class Client : SimplPipeline
        {
            public Task RunAsync(CancellationToken cancellationToken = default)
                => StartReceiveLoopAsync(cancellationToken);

            private readonly UvTcpServer _server;
            public Client(IDuplexPipe pipe, UvTcpServer server)
                : base(pipe) => _server = server;

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
                    msg?.Dispose();
                }

                return default;
            }
        }

        private bool _disposed;
        private readonly ConcurrentDictionary<Client, long> _clients;
        private readonly List<IAsyncDisposable> _listeners = new List<IAsyncDisposable>();
        protected int _threadCount;
        public List<UvThread> Threads { get; }
        public ILibuvTrace Log => throw new NotImplementedException();

        protected UvTcpServer(int threadCount = 1)
        {
            _threadCount = threadCount;
            _clients = new ConcurrentDictionary<Client, long>();
            Threads = new List<UvThread>();
        }

        public async Task Start(IPEndPoint endPoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            for (var index = 0; index < _threadCount; index++)
            {
                Threads.Add(new UvThread(Log));
            }

            foreach (var thread in Threads)
            {
                await thread.StartAsync().ConfigureAwait(false);
            }

            try
            {
                if (_threadCount == 1)
                {
                    var listener = new UvTcpListener(Threads[0], endPoint);
                    _listeners.Add(listener);
                    await listener.StartAsync().ConfigureAwait(false);
                }
                else
                {

                }
            }
            catch (UvException ex) when (ex.StatusCode == UvConstants.EADDRINUSE)
            {
                await StopAsync().ConfigureAwait(false);
                throw new AddressInUseException(ex.Message, ex);
            }
            catch
            {
                await StopAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task StopAsync()
        {
            var disposeTasks = _listeners.Select(listener => listener.DisposeAsync()).ToArray();

            if (!await WaitAsync(Task.WhenAll(disposeTasks), TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                Log.LogError(0, null, "Disposing listeners failed");
            }

            _listeners.Clear();
        }

        private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
        {
            return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
        }

        private void Listener_OnConnection(LibuvConnection connection)
        {
            var client = new Client(connection, this);
            AddClient(client);
            Console.WriteLine($"新连接已建立<{connection.EndPoint}>，当前总连接数：{ClientsCount}");
            client.RunAsync();
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
