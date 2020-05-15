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
        protected readonly ILibuvTrace _log;
        private readonly GCHandleType _handleType;

        // ˵����
        // gc�ļ򵥹��̣�
        //  1. ���������߳�
        //  2. ִ��mark�����ֿɴ��벻�ɴ���󣬲��ɴＴΪ������
        //     �������������Ķ�����Ҫ���⴦��
        //  3. ִ��compact������ѹ���ѿռ䣬�漰�������ڴ��ַ�ĸı�
        // 
        // Weak��gcɨ��gchandle table�������weak��������һ�����ɴ������ô��handle��Target�ᱻ��Ϊnull
        // WeakTrackResurrection��gcɨ���ս�list�����weaktr��������һ�����ɴ������ô��handle��Target�ᱻ��Ϊnull
        // Normal��gcɨ��gchandle table������normal�����õĶ��󶼱���Ϊ�ɴ����
        // Pinned��gcɨ��gchandle table������pinned�����õĶ��󶼱���Ϊ�ɴ����
        // 
        // ͨ��ʹ�ó���Ϊ��You do have to use the GCHandle type explicitly when you need to pass the pointer to a 
        // managed object to native code; then the native function returns, but native code might still need to use 
        // the object later.The most common example of this is when performing asynchronous I/O operations.
        // 
        // ��������˵����������йܴ��뽻��������ֻ����Ҫһ��ָ���Ա���Է��ʸ��йܶ�����ô����ʹ��normal��
        // ��������治����Ҫ�������ĵ�ַ����Ҫ�����ö�����ڴ棨����һ��string������ô������Ҫʹ��pinned��
        // 
        // 
        protected UvMemory(ILibuvTrace logger, GCHandleType handleType = GCHandleType.Weak)
            : base(IntPtr.Zero, true) // ownsHandleΪtrue����ǰUvMemory����gcʱ��Ӧ��native��Դ�ᱻclose
        {
            _log = logger;
            _handleType = handleType;
        }

        public Uv Libuv => _uv;

        public override bool IsInvalid
        {
            // ˵����ͨ�������ǰ�йܶ�����е�handleû�ж�Ӧ��һ����Ч��native��Դ��
            // ��ô��Ӧ�÷���true
            get
            {
                // �����Ӳ�ͬ��SafeHandleZeroOrMinusOneIsInvalid��libuv��������-1Ӧ���Ǳ�
                // �����������ֵ�˵�
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
            // ˵����
            // �����ɵ����黹�ǱȽϼ򵥣�����ΪSafeHandle��Ҳ����Ӧ��Ϊ��Ӧ��libuv���������ָ����С���ڴ棻
            // �йܶ����õ���safehandle��gchandle��Ҳ�����õ����й��Ǳ߶�Ӧ��intptr��
            // ��GCHandle.Alloc���ص���clr table�и�gchandle��index��
            *(IntPtr*)handle = GCHandle.ToIntPtr(GCHandle.Alloc(this, _handleType));
        }

        unsafe protected static void DestroyMemory(IntPtr memory)
        {
            // ˵�����ͷŵ�ʱ���й�������clr��Ҫ���������飬1. free��gchandle 2. �ͷŵ����й��ڴ�
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

        public IntPtr InternalGetHandle()
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
