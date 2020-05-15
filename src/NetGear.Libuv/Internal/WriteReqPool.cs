using System;
using System.Collections.Generic;

namespace NetGear.Libuv
{
    /// <summary>
    /// libuv write请求复用池
    /// </summary>
    public class WriteReqPool
    {
        private const int _maxPooledWriteReqs = 1024;

        private readonly UvThread _thread;
        private readonly Queue<UvWriteReq> _pool = new Queue<UvWriteReq>(_maxPooledWriteReqs);
        private readonly ILibuvTrace _log;
        private bool _disposed;

        public WriteReqPool(UvThread thread, ILibuvTrace log)
        {
            _thread = thread;
            _log = log;
        }

        public UvWriteReq Allocate()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            UvWriteReq req;
            if (_pool.Count > 0)
            {
                req = _pool.Dequeue();
            }
            else
            {
                req = new UvWriteReq(_log);
                req.Init(_thread);
            }

            return req;
        }

        public void Return(UvWriteReq req)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().Name);
            }

            if (_pool.Count < _maxPooledWriteReqs)
            {
                _pool.Enqueue(req);
            }
            else
            {
                req.Dispose();
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                while (_pool.Count > 0)
                {
                    _pool.Dequeue().Dispose();
                }
            }
        }
    }
}
