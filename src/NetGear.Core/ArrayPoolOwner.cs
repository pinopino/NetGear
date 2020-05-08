using System;
using System.Buffers;
using System.Threading;

namespace NetGear.Core
{
    /// <summary>
    /// A thin wrapper around a leased array; when disposed, the array
    /// is returned to the pool; the caller is responsible for not retaining
    /// a reference to the array (via .Memory / .ArraySegment) after using Dispose()
    /// </summary>
    public sealed class ArrayPoolOwner<T> : IMemoryOwner<T>
    {
        private readonly int _length;
        private static int _leakCount;
        private T[] _oversized;

        public ArrayPoolOwner(T[] oversized, int length)
        {
            _length = length;
            _oversized = oversized;
        }

        public Memory<T> Memory => new Memory<T>(GetArray(), 0, _length);

        private T[] GetArray() =>
            Interlocked.CompareExchange(ref _oversized, null, null)
            ?? throw new ObjectDisposedException(ToString());

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            var arr = Interlocked.Exchange(ref _oversized, null);
            if (arr != null) ArrayPool<T>.Shared.Return(arr);
        }

        ~ArrayPoolOwner() { Interlocked.Increment(ref _leakCount); }

        internal static int LeakCount() => Thread.VolatileRead(ref _leakCount);
    }
}
