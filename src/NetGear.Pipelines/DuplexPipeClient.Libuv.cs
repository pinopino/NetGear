using NetGear.Core;
using NetGear.Core.Common;
using NetGear.Libuv;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NetGear.Pipelines
{
    public class UvDuplexPipeClient : DuplexPipe
    {
        private int _nextMessageId;
        private Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>> _awaitingResponses;

        private static readonly Action<UvConnectRequest, int, UvException, object> _connectCallback = ConnectCallback;
        private static readonly Action<object> _startConnect = state => ((UvDuplexPipeClient)state).DoConnect();

        private readonly TaskCompletionSource<UvConnection> _connectTcs;
        private readonly IEndPointInformation _endPoint;
        private readonly UvThread _thread;
        private UvTcpHandle _connectSocket;
        private Action<UvDuplexPipeClient> _onConnect;

        public ILibuvTrace Log { set; get; }
        public event Action<IMemoryOwner<byte>> Broadcast;

        public UvDuplexPipeClient(IEndPointInformation endPoint)
        {
            _awaitingResponses = new Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>>();
            _connectTcs = new TaskCompletionSource<UvConnection>();
            _endPoint = endPoint;

            _thread = new UvThread(Log);
            _thread.Post(_startConnect, this);
        }

        private void DoConnect()
        {
            _connectSocket = new UvTcpHandle(Log);
            _connectSocket.Init(_thread.Loop, null);

            var connectReq = new UvConnectRequest(Log);
            connectReq.Init(_thread);
            connectReq.Connect(_connectSocket, _endPoint.IPEndPoint, _connectCallback, this);
        }

        private static void ConnectCallback(UvConnectRequest req, int status, UvException exception, object state)
        {
            var client = (UvDuplexPipeClient)state;

            var connection = new UvConnection(client._connectSocket, client._thread,
                client._connectSocket.GetPeerIPEndPoint(), client._connectSocket.GetSockIPEndPoint());

            client._connectTcs.TrySetResult(connection);
        }

        public async void ConnectAsync(IEndPointInformation endPoint)
        {
            await _connectTcs.Task;
            SetPipe(_connectTcs.Task.Result);
            StartReceiveLoopAsync().FireAndForget();
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
                        messageId = 0; // treat as Broadcast
                    }
                }

                if (tcs != null)
                {
                    // TrySetResult可能会返回false此时lease是没有设置上去的
                    // 我们需要自己手动释放掉已经借出的lease
                    IMemoryOwner<byte> lease = null;
                    try
                    {   // only if we successfully hand it over
                        // to the TCS is it considered "not our
                        // problem anymore" - otherwise: we need
                        // to dispose
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
    }
}
