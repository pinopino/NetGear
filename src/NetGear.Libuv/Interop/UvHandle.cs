// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;

namespace NetGear.Libuv
{
    /// <summary>
    /// 对应libuv的非托管句柄，作为基类主要提供两个句柄操作函数，创建和销毁
    /// </summary>
    public abstract class UvHandle : UvMemory
    {
        private static readonly Uv.uv_close_cb _destroyMemory = (handle) => DestroyMemory(handle);
        private Action<Action<IntPtr>, IntPtr> _queueCloseHandle;

        /* 说明：
            libuv基本逻辑：

            uv_loop_init 初始化loop；也可以直接使用默认提供的uv_default_loop

            handle_init 初始化需要的handle：
            === Handle types. ===
            typedef struct uv_loop_s uv_loop_t;
            typedef struct uv_handle_s uv_handle_t;
            typedef struct uv_stream_s uv_stream_t;
            typedef struct uv_tcp_s uv_tcp_t;
            typedef struct uv_udp_s uv_udp_t;
            typedef struct uv_pipe_s uv_pipe_t;
            ...
            handle也需要初始化
            uv_TYPE_init(uv_loop_t*, uv_TYPE_t*)
            初始化对应TYPE的handle，绑定到事件循环上loop（loop的handle_queue队尾多一个此handle）；该init只是
            挂在handle上，并没有激活它

            handle_start 开启handle：uv_TYPE_start(uv_TYPE_t* handle, uv_TYPE_cb cb)
            把handle自己的消息queue挂到loop中对应TYPE的对应消息队列上；填入一个callback；此时handle会被激活，
            会开始收到事件通知

            uv_run 执行loop：uv_run(uv_loop_t* loop, uv_run_mode mode)

            uv_loop_close 关闭loop
        */
        protected UvHandle(ILibuvTrace logger)
            : base(logger)
        { }

        protected void CreateHandle(
            Uv uv,
            int threadId,
            int size,
            Action<Action<IntPtr>, IntPtr> queueCloseHandle)
        {
            _queueCloseHandle = queueCloseHandle;
            CreateMemory(uv, threadId, size);
        }

        protected override bool ReleaseHandle()
        {
            // 说明：
            // 两部分工作需要完成：
            //  1. libuv自己有清理handle逻辑，所以此处调用uv.close让它自己去搞
            //  2. 托管世界这边clr有清理逻辑需要做，此处调用DestroyMemory来完成
            var memory = handle;
            if (memory != IntPtr.Zero)
            {
                handle = IntPtr.Zero;

                // 说明：
                // 如果当前就是在对应的uv线程上那么直接“inline”执行release动作即可；
                // 下方的else分支确保了非绑定uv线程如何post release动作到对应线程上。
                // 所以下一个问题就是为什么会出现非对应绑定线程？
                // 我的一个猜测就是ReleaseHandle是从SafeHandle而来，这东西为了保证资源
                // 的安全释放，完全有可能最后是由独立的析构线程来执行的；下面的原始注释
                // 也印证了这一点。
                if (Thread.CurrentThread.ManagedThreadId == ThreadId)
                {
                    _uv.close(memory, _destroyMemory);
                }
                else if (_queueCloseHandle != null)
                {
                    // This can be called from the finalizer.
                    // Ensure the closure doesn't reference "this".
                    var uv = _uv;
                    _queueCloseHandle(memory2 => uv.close(memory2, _destroyMemory), memory);
                }
                else
                {
                    Debug.Assert(false, "UvHandle not initialized with queueCloseHandle action");
                    return false;
                }
            }
            return true;
        }

        public void Reference()
        {
            _uv.@ref(this);
        }

        public void Unreference()
        {
            _uv.unref(this);
        }
    }
}
