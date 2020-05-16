// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;

namespace NetGear.Libuv
{
    /// <summary>
    /// ��Ӧlibuv�ķ��йܾ������Ϊ������Ҫ�ṩ���������������������������
    /// </summary>
    public abstract class UvHandle : UvMemory
    {
        private static readonly Uv.uv_close_cb _destroyMemory = (handle) => DestroyMemory(handle);
        private Action<Action<IntPtr>, IntPtr> _queueCloseHandle;

        /* ˵����
            libuv�����߼���

            uv_loop_init ��ʼ��loop��Ҳ����ֱ��ʹ��Ĭ���ṩ��uv_default_loop

            handle_init ��ʼ����Ҫ��handle��
            === Handle types. ===
            typedef struct uv_loop_s uv_loop_t;
            typedef struct uv_handle_s uv_handle_t;
            typedef struct uv_stream_s uv_stream_t;
            typedef struct uv_tcp_s uv_tcp_t;
            typedef struct uv_udp_s uv_udp_t;
            typedef struct uv_pipe_s uv_pipe_t;
            ...
            handleҲ��Ҫ��ʼ��
            uv_TYPE_init(uv_loop_t*, uv_TYPE_t*)
            ��ʼ����ӦTYPE��handle���󶨵��¼�ѭ����loop��loop��handle_queue��β��һ����handle������initֻ��
            ����handle�ϣ���û�м�����

            handle_start ����handle��uv_TYPE_start(uv_TYPE_t* handle, uv_TYPE_cb cb)
            ��handle�Լ�����Ϣqueue�ҵ�loop�ж�ӦTYPE�Ķ�Ӧ��Ϣ�����ϣ�����һ��callback����ʱhandle�ᱻ���
            �Ὺʼ�յ��¼�֪ͨ

            uv_run ִ��loop��uv_run(uv_loop_t* loop, uv_run_mode mode)

            uv_loop_close �ر�loop
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
            // ˵����
            // �����ֹ�����Ҫ��ɣ�
            //  1. libuv�Լ�������handle�߼������Դ˴�����uv.close�����Լ�ȥ��
            //  2. �й��������clr�������߼���Ҫ�����˴�����DestroyMemory�����
            var memory = handle;
            if (memory != IntPtr.Zero)
            {
                handle = IntPtr.Zero;

                // ˵����
                // �����ǰ�����ڶ�Ӧ��uv�߳�����ôֱ�ӡ�inline��ִ��release�������ɣ�
                // �·���else��֧ȷ���˷ǰ�uv�߳����post release��������Ӧ�߳��ϡ�
                // ������һ���������Ϊʲô����ַǶ�Ӧ���̣߳�
                // �ҵ�һ���²����ReleaseHandle�Ǵ�SafeHandle�������ⶫ��Ϊ�˱�֤��Դ
                // �İ�ȫ�ͷţ���ȫ�п���������ɶ����������߳���ִ�еģ������ԭʼע��
                // Ҳӡ֤����һ�㡣
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
