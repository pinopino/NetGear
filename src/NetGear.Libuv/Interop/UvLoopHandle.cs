// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;

namespace NetGear.Libuv
{
    public class UvLoopHandle : UvMemory
    {
        public UvLoopHandle(ILibuvTrace logger)
            : base(logger)
        { }

        public void Init(Uv uv)
        {
            // 说明：loop此时还没有关联到具体的某一个uv线程，这里只是
            // 初始化了一个loop结构体拿到了一个句柄而已
            CreateMemory(
                uv,
                Thread.CurrentThread.ManagedThreadId,
                uv.loop_size());

            _uv.loop_init(this);
        }

        public void Run(int mode = 0)
        {
            _uv.run(this, mode);
        }

        public void Stop()
        {
            _uv.stop(this);
        }

        public long Now()
        {
            return _uv.now(this);
        }

        unsafe protected override bool ReleaseHandle()
        {
            var memory = handle;
            if (memory != IntPtr.Zero)
            {
                // loop_close clears the gcHandlePtr
                var gcHandlePtr = *(IntPtr*)memory;

                _uv.loop_close(this);
                handle = IntPtr.Zero;

                DestroyMemory(memory, gcHandlePtr);
            }

            return true;
        }
    }
}
