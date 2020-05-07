using NetGear.Core.Common;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public class SimplPipelineClient : SimplPipeline
    {
        private int _nextMessageId;
        private Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>> _awaitingResponses;

        public delegate void BroadcastHandler(IMemoryOwner<byte> memory);
        public event BroadcastHandler OnBroadcast;

        public SimplPipelineClient(IDuplexPipe pipe)
            : base(pipe)
        {
            _awaitingResponses = new Dictionary<int, TaskCompletionSource<IMemoryOwner<byte>>>();
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
                }
                tcs?.TrySetResult(payload.Lease());
            }
            else
            {
                // unsolicited
                OnBroadcast?.Invoke(payload.Lease());
            }

            return default;
        }

        public ValueTask<IMemoryOwner<byte>> SendReceiveAsync(IMemoryOwner<byte> message)
        {
            using (message)
            {
                return SendReceiveAsync(message.Memory);
            }
        }

        public ValueTask<IMemoryOwner<byte>> SendReceiveAsync(ReadOnlyMemory<byte> message)
        {
            var tcs = new TaskCompletionSource<IMemoryOwner<byte>>();
            int messageId;
            lock (_awaitingResponses)
            {
                messageId = ++_nextMessageId;
                if (messageId == 0)
                    messageId = 1;
                _awaitingResponses.Add(messageId, tcs);
            }

            var writeResult = WriteAsync(message, messageId);
            if (writeResult.IsCompletedSuccessfully)
                return new ValueTask<IMemoryOwner<byte>>(tcs.Task);

            return new ValueTask<IMemoryOwner<byte>>(AwaitWrite(writeResult, tcs));
        }

        private async Task<IMemoryOwner<byte>> AwaitWrite(ValueTask writing, TaskCompletionSource<IMemoryOwner<byte>> tcs)
        {
            await writing;
            return await tcs.Task;
        }
    }
}
