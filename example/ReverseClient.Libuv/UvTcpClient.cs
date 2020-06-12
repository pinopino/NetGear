using NetGear.Core;
using NetGear.Core.Common;
using NetGear.Pipelines;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public class UvTcpClient : DuplexPipe
    {
        private int _nextMessageId;
        private Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>> _awaitingResponses;

        private static readonly Action<UvConnectRequest, int, Exception, object> _connectCallback = ConnectCallback;
        private static readonly Action<object> _startConnect = state => ((UvTcpClient)state).DoConnect();

        private readonly TaskCompletionSource<UvTcpClient> _connectTcs;
        private readonly IPEndPoint _ipEndPoint;
        private readonly UvThread _thread;
        private UvTcpHandle _connectSocket;
        private Action<UvTcpClient> _onConnect;

        public ILibuvTrace Log { set; get; }
        public event Action<IMemoryOwner<byte>> Broadcast;

        private UvTcpClient(IDuplexPipe pipe, ILibuvTrace log)
            : base(pipe)
        {
            Log = log;
            _thread = new UvThread(Log);
            _connectTcs = new TaskCompletionSource<UvTcpClient>();
        }

        public async Task<UvTcpClient> ConnectAsync(IPEndPoint endPoint)
        {
            _thread.Post(_startConnect, this);

            var connection = await _connectTcs.Task;

            // Get back onto the current context
            await Task.Yield();

            StartReceiveLoopAsync().FireAndForget();

            return connection;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> message) => WriteAsync(message, 0);

        public async Task<IMemoryOwner<byte>> SendReceiveAsync(IMemoryOwner<byte> message)
        {
            using (message)
            {
                return await SendReceiveAsync(message.Memory);
            }
        }

        public Task<IMemoryOwner<byte>> SendReceiveAsync(ReadOnlyMemory<byte> message)
        {
            var tcs = new TaskCompletionSource<IMemoryOwner<byte>>();
            int messageId;
            lock (_awaitingResponses)
            {
                do
                {
                    messageId = ++_nextMessageId;
                } while (messageId == 0 || _awaitingResponses.ContainsKey(messageId));
                _awaitingResponses.Add(messageId, tcs);
            }

            var writeResult = WriteAsync(message, messageId);
            if (writeResult.IsCompletedSuccessfully)
                return tcs.Task;

            return AwaitWrite(writeResult, tcs.Task);
        }

        private async Task<IMemoryOwner<byte>> AwaitWrite(ValueTask writing, Task<IMemoryOwner<byte>> responseForWrite)
        {
            await writing;
            return await responseForWrite;
        }

        protected override ValueTask OnReceiveAsync(ReadOnlySequence<byte> payload, int messageId)
        {
            if (messageId != 0)
            {
                // request/response
                TaskCompletionSource<IMemoryOwner<byte>> tcs;
                lock (_awaitingResponses)
                {
                    if (_awaitingResponses.TryGetValue(messageId, out tcs))
                    {
                        _awaitingResponses.Remove(messageId);
                    }
                    else
                    {
                        tcs = null;
                        messageId = 0;
                    }
                }
                if (tcs != null)
                {
                    IMemoryOwner<byte> lease = null;
                    try
                    {
                        lease = payload.Lease();
                        if (tcs.TrySetResult(lease))
                            lease = null;
                    }
                    finally
                    {
                        if (lease != null)
                            try { lease.Dispose(); } catch { }
                    }
                }
            }

            if (messageId == 0)
            {
                // unsolicited
                Broadcast?.Invoke(payload.Lease());
            }

            return default;
        }

        private void DoConnect()
        {
            _connectSocket = new UvTcpHandle(Log);
            _connectSocket.Init(_thread.Loop, null);

            var clientConnectionPipe = new UvPipeHandle(Log);
            var connectReq = new UvConnectRequest(Log);
            connectReq.Init(_thread);
            connectReq.Connect(clientConnectionPipe, "name", _connectCallback, this);
        }

        private static void ConnectCallback(UvConnectRequest req, int status, Exception exception, object state)
        {
            throw new NotImplementedException();
        }
    }
}
