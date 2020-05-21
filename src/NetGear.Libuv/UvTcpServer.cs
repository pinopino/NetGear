using NetGear.Core;
using System;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public abstract class UvTcpServer : DuplexPipeServer
    {
        public ILibuvTrace Log { set; get; }

        protected UvTcpServer(ILibuvTrace log = null)
        {
            Log = log;
        }

        public override async Task StartAsync(IEndPointInformation endPoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            _transport = new UvTransport(endPoint);

            await _transport.BindAsync();

            OnServerStarted(endPoint);
        }
    }
}
