﻿using System;
using System.Collections.Generic;

namespace NetGear.Libuv
{
    public class WriteReqPool
    {
        private const int _maxPooledWriteReqs = 1024;

        private readonly UvThread _thread;
        private readonly Queue<UvWriteReq> _pool = new Queue<UvWriteReq>(_maxPooledWriteReqs);
        private bool _disposed;

        public WriteReqPool(UvThread thread)
        {
            _thread = thread;
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
                req = new UvWriteReq();
                req.Init(_thread.Loop);
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
