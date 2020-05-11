// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetGear.Libuv
{
    /// <summary>
    /// Summary description for UvMemory
    /// </summary>
    public abstract class UvMemory : SafeHandle
    {
        protected Uv _uv;
        protected int _threadId;

        protected UvMemory()
            : base(IntPtr.Zero, true) // ˵����ownsHandleΪtrue����ǰUvMemory����gcʱ��Ӧ��native��Դ�ᱻclose
        { }

        public Uv Libuv => _uv;

        public override bool IsInvalid
        {
            // ˵����ͨ�������ǰ�йܶ�����е�handleû�ж�Ӧһ����Ч��native��Դ��
            // ��ô��Ӧ�÷���true
            get
            {
                // ��֪��libuv����������-1�ǲ���Ҳ���������ﷵ��true��
                // return hander == (IntPtr)(-1);
                return handle == IntPtr.Zero;
            }
        }

        public int ThreadId
        {
            get
            {
                return _threadId;
            }
            private set
            {
                _threadId = value;
            }
        }

        unsafe protected void CreateMemory(Uv uv, int threadId, int size)
        {
            _uv = uv;
            ThreadId = threadId;

            handle = Marshal.AllocCoTaskMem(size);
            *(IntPtr*)handle = GCHandle.ToIntPtr(GCHandle.Alloc(this, GCHandleType.Weak));
        }

        unsafe protected static void DestroyMemory(IntPtr memory)
        {
            var gcHandlePtr = *(IntPtr*)memory;
            DestroyMemory(memory, gcHandlePtr);
        }

        protected static void DestroyMemory(IntPtr memory, IntPtr gcHandlePtr)
        {
            if (gcHandlePtr != IntPtr.Zero)
            {
                var gcHandle = GCHandle.FromIntPtr(gcHandlePtr);
                gcHandle.Free();
            }
            Marshal.FreeCoTaskMem(memory);
        }

        internal IntPtr InternalGetHandle()
        {
            return handle;
        }

        public void Validate(bool closed = false)
        {
            Debug.Assert(closed || !IsClosed, "Handle is closed");
            Debug.Assert(!IsInvalid, "Handle is invalid");
            Debug.Assert(_threadId == Thread.CurrentThread.ManagedThreadId, "ThreadId is incorrect");
        }

        unsafe public static THandle FromIntPtr<THandle>(IntPtr handle)
        {
            GCHandle gcHandle = GCHandle.FromIntPtr(*(IntPtr*)handle);
            return (THandle)gcHandle.Target;
        }
    }
}
