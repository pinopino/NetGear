using NetGear.Core.Common;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public class DuplexPipeClient : DuplexPipe
    {
        private int _nextMessageId;
        private Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>> _awaitingResponses;

        public event Action<IMemoryOwner<byte>> Broadcast;

        private DuplexPipeClient(IDuplexPipe pipe)
            : base(pipe)
        {
            _awaitingResponses = new Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>>();
            StartReceiveLoopAsync().FireAndForget();
        }

        public static async Task<DuplexPipeClient> ConnectAsync(IPEndPoint endPoint)
        {
            var socketConnection = await SocketConnection.ConnectAsync(endPoint,
                onConnected: async conn => await Console.Out.WriteLineAsync($"已连接至服务端@{endPoint}"));
            return new DuplexPipeClient(socketConnection);
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
