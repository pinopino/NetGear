using System;
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
    }
}
