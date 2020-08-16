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

        // TODO：这里之前是override，但是因为我需要有个口子可以设定transport，
        // 所以需要从这里传入一个配置项；很杯具的是libuv可不需要Pipeline这个概念，
        // 所以暂时先去掉override让编译可以通过吧。
        public async Task StartAsync(IEndPointInformation endPoint)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            _transport = new UvTransport(endPoint, this, ThreadCount, Log);

            await _transport.BindAsync();

            OnServerStarted(endPoint);
        }
    }
}
