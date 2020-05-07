using System;
using System.Buffers;
using System.Threading;

namespace NetGear.Core
{
    internal sealed class ArrayPoolOwner<T> : IMemoryOwner<T>
    {
        private readonly int _length;
        private T[] _oversized;

        public static IMemoryOwner<T> Empty => new ArrayPoolOwner<T>(new T[0], 0);

        internal ArrayPoolOwner(T[] oversized, int length)
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
            var arr = Interlocked.Exchange(ref _oversized, null);
            if (arr != null) ArrayPool<T>.Shared.Return(arr);
        }
    }
}
