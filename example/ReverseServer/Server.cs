using NetGear.Core;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
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
