using NetGear.Core.Client;
using NetGear.Core.Common;
using System;

namespace NetGear.Rpc.Client
{
    public sealed class RpcConnection : StreamedClientConnection, IPooledWapper
    {
        int _id;
        // 对于池化的对象来说，_disposed几乎没有什么作用，因为回到池后它还会再生，dispose可没有这种语义
        bool _disposed;        
        ObjectPool<RpcConnection> _pool;
        public DateTime LastGetTime { set; get; }

        public RpcConnection(ObjectPool<RpcConnection> pool, int id, string address, int port, int bufferSize, bool debug = false)
            : base(id, address, port, bufferSize, debug)
        {
            if (pool == null)
                throw new ArgumentNullException("pool");

            _id = id;
            _pool = pool;
            _disposed = false;
        }

        ~RpcConnection()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                if (_pool.IsDisposed)
                {
                    // 让类型知道自己已经被释放
                    _disposed = true;
                    base.Dispose();
                }
                else
                {
                    _pool.Put(this);
                }
            }

            // 清理非托管资源
        }
    }
}
