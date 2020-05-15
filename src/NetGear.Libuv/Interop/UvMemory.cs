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

        // 说明：
        // gc的简单过程：
        //  1. 挂起所有线程
        //  2. 执行mark，区分可达与不可达对象，不可达即为垃圾；
        //     含有析构函数的对象需要特殊处理；
        //  3. 执行compact，整理压缩堆空间，涉及到对象内存地址的改变
        // 
        // Weak：gc扫描gchandle table，如果该weak项引用了一个不可达对象，那么该handle的Target会被置为null
        // WeakTrackResurrection：gc扫描终结list，如果weaktr项引用了一个不可达对象，那么该handle的Target会被置为null
        // Normal：gc扫描gchandle table，所有normal项引用的对象都被视为可达对象
        // Pinned：gc扫描gchandle table，所有pinned项引用的对象都被视为可达对象
        // 
        // 通常使用场景为：You do have to use the GCHandle type explicitly when you need to pass the pointer to a 
        // managed object to native code; then the native function returns, but native code might still need to use 
        // the object later.The most common example of this is when performing asynchronous I/O operations.
        // 
        // 基本上来说，如果跟非托管代码交互，对面只是需要一个指针以便可以访问该托管对象，那么可以使用normal；
        // 而如果对面不光是要这个对象的地址还需要操作该对象的内存（比如一个string），那么我们需要使用pinned；
        // 
        // 
        protected UvMemory(ILibuvTrace logger, GCHandleType handleType = GCHandleType.Weak)
            : base(IntPtr.Zero, true) // ownsHandle为true即当前UvMemory对象被gc时对应的native资源会被close
        {
            _log = logger;
            _handleType = handleType;
        }

        public Uv Libuv => _uv;

        public override bool IsInvalid
        {
            // 说明：通常如果当前托管对象持有的handle没有对应着一个有效的native资源，
            // 那么就应该返回true
            get
            {
                // 看样子不同于SafeHandleZeroOrMinusOneIsInvalid，libuv的世界中-1应该是被
                // 当作有意义的值了的
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
            // 说明：
            // 函数干的事情还是比较简单，就是为SafeHandle（也就相应的为对应的libuv句柄）分配指定大小的内存；
            // 托管堆上拿到该safehandle的gchandle，也就能拿到非托管那边对应的intptr了
            // （GCHandle.Alloc返回的是clr table中该gchandle的index）
            *(IntPtr*)handle = GCHandle.ToIntPtr(GCHandle.Alloc(this, _handleType));
        }

        unsafe protected static void DestroyMemory(IntPtr memory)
        {
            // 说明：释放的时候托管世界中clr需要做两件事情，1. free掉gchandle 2. 释放掉非托管内存
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
