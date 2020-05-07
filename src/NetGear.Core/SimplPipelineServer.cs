using NetGear.Core.Common;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public abstract class SimplPipelineServer : IDisposable
    {
        private class Client : SimplPipeline
        {
            public Task RunAsync(CancellationToken cancellationToken)
                => StartReceiveLoopAsync(cancellationToken);

            private readonly SimplPipelineServer _server;
            public Client(IDuplexPipe pipe, SimplPipelineServer server)
                : base(pipe) => _server = server;

            public ValueTask SendAsync(ReadOnlyMemory<byte> message)
            {
                return WriteAsync(message, 0);
            }

            protected override async ValueTask OnReceiveAsync(ReadOnlySequence<byte> payload, int messageId)
            {
                using (var msg = payload.Lease())
                {
                    var response = await _server.OnReceiveForReplyAsync(msg);
                    await WriteAsync(response, messageId);
                }
            }
        }

        private List<Client> _clients = new List<Client>();

        protected abstract ValueTask<IMemoryOwner<byte>> OnReceiveForReplyAsync(IMemoryOwner<byte> message);

        public async ValueTask<int> BroadcastAsync(IMemoryOwner<byte> message)
        {
            using (message)
            {
                return await BroadcastAsync(message.Memory);
            }
        }

        public async ValueTask<int> BroadcastAsync(ReadOnlyMemory<byte> message)
        {
            int count = 0;
            foreach (var client in _clients)
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
            throw new NotImplementedException();
        }
    }
}
