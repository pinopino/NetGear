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
            // ˵����loop��ʱ��û�й����������ĳһ��uv�̣߳�����ֻ��
            // ��ʼ����һ��loop�ṹ���õ���һ���������
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
