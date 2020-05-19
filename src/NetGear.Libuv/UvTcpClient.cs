//using NetGear.Core;
//using NetGear.Core.Common;
//using System;
//using System.Buffers;
//using System.Collections.Generic;
//using System.Net;
//using System.Threading.Tasks;

//namespace NetGear.Libuv
//{
//    public class UvTcpClient : SimplPipeline
//    {
//        private int _nextMessageId;
//        private Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>> _awaitingResponses;
//        public event Action<IMemoryOwner<byte>> Broadcast;

//        private static readonly Action<UvConnectRequest, int, Exception, object> _connectCallback = ConnectCallback;
//        private static readonly Action<object> _startConnect = state => ((UvTcpClient)state).DoConnect();

//        private readonly TaskCompletionSource<UvTcpConnection> _connectTcs;
//        private readonly IPEndPoint _ipEndPoint;
//        private readonly UvThread _thread;

//        private UvTcpHandle _connectSocket;
//        private Action<UvTcpConnection> _onConnect;

//        private UvTcpClient(UvThread thread, IPEndPoint endPoint) : base()
//        {
//            _thread = thread;
//            _ipEndPoint = endPoint;
//            _connectTcs = new TaskCompletionSource<UvTcpConnection>();
//        }

//        public async Task<UvTcpConnection> ConnectAsync()
//        {
//            _thread.Post(_startConnect, this);

//            var connection = await _connectTcs.Task;

//            // Get back onto the current context
//            await Task.Yield();

//            StartReceiveLoopAsync().FireAndForget();

//            return connection;
//        }

//        public ValueTask SendAsync(ReadOnlyMemory<byte> message) => WriteAsync(message, 0);

//        public async Task<IMemoryOwner<byte>> SendReceiveAsync(IMemoryOwner<byte> message)
//        {
//            using (message)
//            {
//                return await SendReceiveAsync(message.Memory);
//            }
//        }

//        public Task<IMemoryOwner<byte>> SendReceiveAsync(ReadOnlyMemory<byte> message)
//        {
//            var tcs = new TaskCompletionSource<IMemoryOwner<byte>>();
//            int messageId;
//            lock (_awaitingResponses)
//            {
//                do
//                {
//                    messageId = ++_nextMessageId;
//                } while (messageId == 0 || _awaitingResponses.ContainsKey(messageId));
//                _awaitingResponses.Add(messageId, tcs);
//            }

//            var writeResult = WriteAsync(message, messageId);
//            if (writeResult.IsCompletedSuccessfully)
//                return tcs.Task;

//            return AwaitWrite(writeResult, tcs.Task);
//        }

//        private async Task<IMemoryOwner<byte>> AwaitWrite(ValueTask writing, Task<IMemoryOwner<byte>> responseForWrite)
//        {
//            await writing;
//            return await responseForWrite;
//        }

//        protected override ValueTask OnReceiveAsync(ReadOnlySequence<byte> payload, int messageId)
//        {
//            if (messageId != 0)
//            {
//                // request/response
//                TaskCompletionSource<IMemoryOwner<byte>> tcs;
//                lock (_awaitingResponses)
//                {
//                    if (_awaitingResponses.TryGetValue(messageId, out tcs))
//                    {
//                        _awaitingResponses.Remove(messageId);
//                    }
//                    else
//                    {
//                        tcs = null;
//                        messageId = 0;
//                    }
//                }
//                if (tcs != null)
//                {
//                    IMemoryOwner<byte> lease = null;
//                    try
//                    {
//                        lease = payload.Lease();
//                        if (tcs.TrySetResult(lease))
//                            lease = null;
//                    }
//                    finally
//                    {
//                        if (lease != null)
//                            try { lease.Dispose(); } catch { }
//                    }
//                }
//            }

//            if (messageId == 0)
//            {
//                // unsolicited
//                Broadcast?.Invoke(payload.Lease());
//            }

//            return default;
//        }

//        private void DoConnect()
//        {
//            _connectSocket = new UvTcpHandle();
//            _connectSocket.Init(_thread.Loop, null);

//            var connectReq = new UvConnectRequest();
//            connectReq.Init(_thread.Loop);
//            connectReq.Connect(_connectSocket, _ipEndPoint, _connectCallback, this);
//        }

//        public void OnConnection(Action<UvTcpConnection> callback)
//        {
//            _onConnect = callback;
//        }

//        private static void ConnectCallback(UvConnectRequest req, int status, Exception exception, object state)
//        {
//            var client = (UvTcpClient)state;

//            var connection = new UvTcpConnection(client._thread, client._connectSocket);

//            client._connectTcs.TrySetResult(connection);

//            client._onConnect?.Invoke(connection);
//        }
//    }
//}
