using NetGear.Pipelines;
using System;
using System.Buffers;
using System.Threading.Tasks;

namespace ReverseServer
{
    public class Server : DuplexPipeServer
    {
        protected override ValueTask<IMemoryOwner<byte>> OnReceiveForReplyAsync(IMemoryOwner<byte> message)
        {
            var memory = message.Memory;
            memory.Span.Reverse();
            return new ValueTask<IMemoryOwner<byte>>(message);
        }
    }
}
