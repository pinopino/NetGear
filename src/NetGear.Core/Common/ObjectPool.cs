using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core.Common
{
    public interface IPooledWapper : IDisposable
    {
        DateTime LastGetTime { set; get; }
    }

    public sealed class ObjectPool<T> : IDisposable where T : IPooledWapper
    {
        bool _debug;
        bool _disposed;
        int _minRetained;
        int _maxRetained;
        SemaphoreSlim _solts;
        ConcurrentBag<T> _objects;
        Func<ObjectPool<T>, T> _objectGenerator;
        public bool IsDisposed { get { return _disposed; } }

        public ObjectPool(int maxRetained, int minRetained, Func<ObjectPool<T>, T> objectGenerator, bool debug = false)
        {
            if (objectGenerator == null)
                throw new ArgumentNullException("objectGenerator");
            if (maxRetained < 1)
                throw new ArgumentException("maxRetained不能为负");
            if (minRetained < 1)
                throw new ArgumentException("minRetained不能为负");
            if (maxRetained < minRetained)
                throw new ArgumentException("maxRetained不能小于minRetained");

            _debug = debug;
            _disposed = false;
            _minRetained = minRetained;
            _maxRetained = maxRetained;
            _objectGenerator = objectGenerator;
            _objects = new ConcurrentBag<T>();
            _solts = new SemaphoreSlim(_maxRetained, _maxRetained);

            // 预先初始化
            if (_minRetained > 0)
            {
                Parallel.For(0, _minRetained, i => _objects.Add(_objectGenerator(this)));
            }
        }

        ~ObjectPool()
        {
            Dispose(false);
        }

        public T Get()
        {
            _solts.Wait();
            T item;
            if (!_objects.TryTake(out item))
            {
                item = _objectGenerator(this);
            }
            return item;
        }

        public void Put(T item)
        {
            // ConcurrentBag是支持null对象的，所以这里只能自己判断
            if (item == null)
            {
                throw new ArgumentNullException("item");
            }

            _objects.Add(item);
            _solts.Release();
        }

        public void Dispose()
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
                foreach (var item in _objects)
                {
                    item.Dispose();
                }
                _solts.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
