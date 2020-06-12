using NetGear.Core;
using NetGear.Pipelines;
using System;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public abstract class UvTcpServer : DuplexPipeServer
    {
        public ILibuvTrace Log { set; get; }
        public int ThreadCount { set; get; }

        protected UvTcpServer(int threadCount = 1, ILibuvTrace log = null)
        {
            ThreadCount = threadCount;
            Log = log;
        }

        public override async Task StartAsync(IEndPointInformation endPoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            _transport = new UvTransport(endPoint, this, ThreadCount, Log);

            await _transport.BindAsync();

            OnServerStarted(endPoint);
        }
    }
}
