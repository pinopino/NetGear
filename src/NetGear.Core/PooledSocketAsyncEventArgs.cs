using NetGear.Core.Common;
using System;
using System.Net.Sockets;

namespace NetGear.Core
{
    public sealed class PooledSocketAsyncEventArgs : SocketAsyncEventArgs, IPooledWapper
    {
        // 对于池化的对象来说，_disposed几乎没有什么作用，因为回到池后它还会再生，dispose可没有这种语义
        private bool _disposed;
        private ObjectPool<PooledSocketAsyncEventArgs> _pool;
        public DateTime LastGetTime { set; get; }

        public PooledSocketAsyncEventArgs(ObjectPool<PooledSocketAsyncEventArgs> pool)
        {
            if (pool == null)
                throw new ArgumentNullException("pool");

            _pool = pool;
            _disposed = false;
        }

        ~PooledSocketAsyncEventArgs()
        {
            // 必须为false
            Dispose(false);
        }

        public new void Dispose()
        {
            // 必须为true
            Dispose(true);
            // 通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
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
