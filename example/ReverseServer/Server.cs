using NetGear.Core;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace ReverseServer
{
    public class Server : SimplPipelineServer
    {
        protected override ValueTask<IMemoryOwner<byte>> OnReceiveForReplyAsync(IMemoryOwner<byte> message)
        {
            var memory = message.Memory;
            Reverse(memory.Span);
            return new ValueTask<IMemoryOwner<byte>>(ArrayPoolOwner<byte>.Empty);
        }

        private void Reverse(Span<byte> span)
        {
            // TODO
        }
    }
}
