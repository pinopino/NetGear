using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public abstract class DuplexPipeServer : IDisposable
    {
        private class SimplListener : SocketListener
        {
            private DuplexPipeServer _server;
            public SimplListener(DuplexPipeServer server)
                => _server = server;

            protected override Task OnClientConnectedAsync(in ClientConnection connection)
            {
                var client = new Client(connection.Transport, _server);
                _server.AddClient(client);
                Console.WriteLine($"新连接已建立<{connection.RemoteEndPoint}>，当前总连接数：{_server.ClientsCount}");
                return client.RunAsync();
            }

            protected override void OnStarted(EndPoint endPoint)
            {
                Console.WriteLine($"服务端开始监听@{endPoint}");
            }

            protected override void OnClientDisconnected(in ClientConnection client)
            {
                Console.WriteLine($"连接<{client.RemoteEndPoint}>已断开@{DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")}");
            }

            protected override void OnClientFaulted(in ClientConnection client, Exception exception)
            {
                Console.WriteLine($"连接<{client.RemoteEndPoint}>异常，消息：{exception.Message}");
            }

            protected override void OnServerFaulted(Exception exception)
            {
                Console.WriteLine($"服务端异常，消息：{exception.Message}");
            }
        }

        private class Client : DuplexPipe
        {
            public Task RunAsync(CancellationToken cancellationToken = default)
                => StartReceiveLoopAsync(cancellationToken);

            private readonly DuplexPipeServer _server;
            public Client(IDuplexPipe pipe, DuplexPipeServer server)
                : base(pipe) => _server = server;

            public ValueTask SendAsync(ReadOnlyMemory<byte> message)
            {
                return WriteAsync(message, 0);
            }

            // 说明：函数内部逻辑如此扭曲原因参考SimplPipeline.cs中WriteAsync方法
            protected sealed override ValueTask OnReceiveAsync(ReadOnlySequence<byte> payload, int messageId)
            {
                // 因为不关心该方法的task结果，所以这里直接void
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

                // 没有去await valuetask而是采用这样的方式为的是可以省掉await带来的开销
                // 当然这样做的前提是我们对该task完成后的值并不关心
                void DisposeOnCompletion(ValueTask task, ref IMemoryOwner<byte> message)
                {
                    task.AsTask().ContinueWith((t, s) => ((IMemoryOwner<byte>)s)?.Dispose(), message);
                    message = null; // caller no longer owns it, logically; don't wipe on exit
                }

                var msg = payload.Lease();
                try
                {
                    // 说明：
                    // SimplPipelineClient也给了个send方法，所以这里我们需要处理messageid为0的情况
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
        private readonly SimplListener _listener;
        // value为客户端连接上来的时间戳，对于客户端来说服务端可以做很多事情。这里只是
        // 做了个示例，比如我们记录下时间戳如果同一个客户端多次上来又断掉可能就要小心了。
        private readonly ConcurrentDictionary<Client, long> _clients;

        protected DuplexPipeServer()
        {
            _listener = new SimplListener(this);
            _clients = new ConcurrentDictionary<Client, long>();
        }

        public void Start(IPEndPoint endPoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            _listener.Start(endPoint);
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

        // 说明：
        // 注意这两个函数的微妙区别，正常的请求响应循环一问一答这种就是ReceiveForReply
        // 而偶然收到客户端独立发过来的消息就是Receive，当然不加以区别问题也不大
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
