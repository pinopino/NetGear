using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace NetGear.Libuv
{
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
        初始化对应TYPE的handle，绑定到事件循环上loop（loop的handle_queue队尾多一个此handle）；该init只是挂在handle，并没有激活它

        handle_start 开启handle：uv_TYPE_start(uv_TYPE_t* handle, uv_TYPE_cb cb)
        把handle自己的消息queue挂到loop中对应TYPE的对应消息队列上；填入一个callback；此时handle会被激活，会开始收到事件通知

        uv_run 执行loop：uv_run(uv_loop_t* loop, uv_run_mode mode)

        uv_loop_close 关闭loop
     */
    // This class needs a bunch of work to make sure it's thread safe
    public class UvThread : ICriticalNotifyCompletion, IDisposable
    {
        private readonly Thread _thread = new Thread(OnStart)
        {
            Name = "Libuv event loop"
        };
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim();
        private readonly WorkQueue<Work> _workQueue = new WorkQueue<Work>();

        private bool _stopping;
        private UvAsyncHandle _postHandle; // 其它线程通过该handler以一种线程安全的方式与当前线程交互

        public UvThread()
        {
            WriteReqPool = new WriteReqPool(this);
        }

        public Uv Uv { get; private set; }

        public UvLoopHandle Loop { get; private set; }

        public WriteReqPool WriteReqPool { get; }

        public void Post(Action<object> callback, object state)
        {
            if (_stopping)
            {
                return;
            }

            EnsureStarted();

            var work = new Work
            {
                Callback = callback,
                State = state
            };

            _workQueue.Add(work);

            _postHandle.Send();
        }

        // Awaiter impl
        public bool IsCompleted => Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId;

        public UvThread GetAwaiter() => this;

        public void GetResult()
        { }

        private static void OnStart(object state)
        {
            ((UvThread)state).RunLoop();
        }

        private void RunLoop()
        {
            Uv = new Uv();

            Loop = new UvLoopHandle(); // 初始化default_loop
            Loop.Init(Uv);

            _postHandle = new UvAsyncHandle();
            // 任何其他线程通过_postHandle.send调用都会触发这里onpost回调被执行
            // 相当于通知当前线程有活干了
            _postHandle.Init(Loop, OnPost, null);

            _running.Set();

            Uv.run(Loop, 0);

            // 下面的代码应该是loop整个完成之后才会执行的
            _postHandle.Reference();
            _postHandle.Dispose();

            Uv.run(Loop, 0);

            Loop.Dispose();
        }

        private void OnPost()
        {
            foreach (var work in _workQueue.DequeAll())
            {
                work.Callback(work.State);
            }

            if (_stopping)
            {
                WriteReqPool.Dispose();

                _postHandle.Unreference();
            }
        }

        private void EnsureStarted()
        {
            if (!_running.IsSet)
            {
                _thread.Start(this);

                _running.Wait();
            }
        }

        private void Stop()
        {
            if (!_stopping)
            {
                _stopping = true;

                _postHandle.Send();

                _thread.Join();

                // REVIEW: Can you restart the thread?
            }
        }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void OnCompleted(Action continuation)
        {
            Post(state => ((Action)state)(), continuation);
        }

        public void Dispose()
        {
            Stop();
        }

        private struct Work
        {
            public object State;
            public Action<object> Callback;
        }
    }
}
