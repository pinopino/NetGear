// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NetGear.Core.Buffer;
using System.Buffers;

namespace NetGear.Libuv
{
    public static class KestrelMemoryPool
    {
        public static MemoryPool<byte> Create()
        {
#if DEBUG
            return new DiagnosticMemoryPool(CreateSlabMemoryPool());
#else
            return CreateSlabMemoryPool();
#endif
        }

        public static MemoryPool<byte> CreateSlabMemoryPool()
        {
            return new SlabMemoryPool();
        }

        public static readonly int MinimumSegmentSize = 4096;
    }
}
