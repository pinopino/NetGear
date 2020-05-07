using System;
using System.Buffers;
using System.Runtime.InteropServices;

namespace NetGear.Core.Common
{
    internal static class MemoryExtension
    {
        internal static ArraySegment<byte> GetArray(this Memory<byte> buffer) => GetArray((ReadOnlyMemory<byte>)buffer);

        internal static ArraySegment<byte> GetArray(this ReadOnlyMemory<byte> buffer)
        {
            if (!MemoryMarshal.TryGetArray<byte>(buffer, out var segment))
                throw new InvalidOperationException("MemoryMarshal.TryGetArray<byte> could not provide an array");
            return segment;
        }

        public static IMemoryOwner<T> Lease<T>(this ReadOnlySequence<T> source)
        {
            if (source.IsEmpty)
                return ArrayPoolOwner<T>.Empty;

            int len = checked((int)source.Length);
            var arr = ArrayPool<T>.Shared.Rent(len);
            source.CopyTo(arr);
            return new ArrayPoolOwner<T>(arr, len);
        }
    }
}
