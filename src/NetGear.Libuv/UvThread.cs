using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public class UvThread : ICriticalNotifyCompletion
    {
        private struct Work
        {
            public Action<object, object> CallbackAdapter;
            public object Callback;
            public object State;
            public TaskCompletionSource<object> Completion;
        }

        private struct CloseHandle
        {
            public Action<IntPtr> Callback;
            public IntPtr Handle;
        }

        private class CallbackAdapter<T>
        {
            public static readonly Action<object, object> PostCallbackAdapter = (callback, state) => ((Action<T>)callback).Invoke((T)state);
            public static readonly Action<object, object> PostAsyncCallbackAdapter = (callback, state) => ((Action<T>)callback).Invoke((T)state);
        }

        private Uv _uv;
        private readonly Thread _thread;
        private readonly UvLoopHandle _loop;
        private readonly UvAsyncHandle _post; // 其它线程通过该handle以一种线程安全的方式与当前线程交互
        private readonly TaskCompletionSource<object> _threadTcs; // 代表线程整个生命周期

        private readonly ILibuvTrace _log;
        private readonly ManualResetEventSlim _running = new ManualResetEventSlim();
        private Queue<Work> _workAdding = new Queue<Work>(1024);
        private Queue<Work> _workRunning = new Queue<Work>(1024);
        private Queue<CloseHandle> _closeHandleAdding = new Queue<CloseHandle>(256);
        private Queue<CloseHandle> _closeHandleRunning = new Queue<CloseHandle>(256);

        // maximum times the work queues swapped and are processed in a single pass
        // as completing a task may immediately have write data to put on the network
        // otherwise it needs to wait till the next pass of the libuv loop
        private readonly int _maxLoops = 8;
        private readonly object _workSync = new object();
        private readonly object _closeHandleSync = new object();
        private readonly object _startSync = new object();
        private bool _stopImmediate = false;
        private bool _initCompleted = false;
        private Exception _closeError;

        public UvThread(ILibuvTrace log)
        {
            _log = log;
            _loop = new UvLoopHandle(_log);
            _post = new UvAsyncHandle(_log);
            _thread = new Thread(ThreadStart);
            _thread.Name = "Libuv event loop";

            _threadTcs = new TaskCompletionSource<object>();
            QueueCloseHandle = PostCloseHandle;
            QueueCloseAsyncHandle = EnqueueCloseHandle;
            WriteReqPool = new WriteReqPool(this, _log);
            MemoryPool = KestrelMemoryPool.Create();
        }

        public UvLoopHandle Loop { get { return _loop; } }

        public WriteReqPool WriteReqPool { get; }

        public MemoryPool<byte> MemoryPool { get; }

        public Exception FatalError => _closeError;

        public Action<Action<IntPtr>, IntPtr> QueueCloseHandle { get; }

        private Action<Action<IntPtr>, IntPtr> QueueCloseAsyncHandle { get; }

#if DEBUG
        public List<WeakReference> Requests { get; } = new List<WeakReference>();
#endif

        public Task StartAsync()
        {
            var startTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread.Start(startTcs);
            return startTcs.Task;
        }

        private void ThreadStart(object parameter)
        {
            lock (_startSync)
            {
                var startTcs = (TaskCompletionSource<int>)parameter;
                try
                {
                    _uv = new Uv();
                    _loop.Init(_uv);
                    // 任何其他线程通过_postHandle.send调用都会触发这里onpost回调被执行
                    // 相当于通知当前线程有活干了
                    _post.Init(_loop, OnPost, EnqueueCloseHandle);
                    _initCompleted = true;
                    startTcs.SetResult(0); // init/start完毕
                }
                catch (Exception ex)
                {
                    startTcs.SetException(ex);
                    return;
                }
            }

            try
            {
                _loop.Run();
                if (_stopImmediate)
                {
                    // thread-abort form of exit, resources will be leaked
                    return;
                }

                // run the loop one more time to delete the open handles
                _post.Reference();
                _post.Dispose();

                // We need this walk because we call ReadStop on accepted connections when there's back pressure
                // Calling ReadStop makes the handle as in-active which means the loop can
                // end while there's still valid handles around. This makes loop.Dispose throw
                // with an EBUSY. To avoid that, we walk all of the handles and dispose them.
                // 说明：简单说就是handle（这里就是loophandle也就是我们的这个事件循环）可能会被临时置为
                // in-active这会导致挂在其上的正常的handle没法跑，所以这里手动walk一次所有的handle手动
                // 全部跑一次
                Walk(ptr =>
                {
                    var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                    // handle can be null because UvMemory.FromIntPtr looks up a weak reference
                    handle?.Dispose();
                });

                // Ensure the Dispose operations complete in the event loop.
                _loop.Run();

                _loop.Dispose();
            }
            catch (Exception ex)
            {
                _closeError = ex;
                // Request shutdown so we can rethrow this exception
                // in Stop which should be observable.
                //_appLifetime.StopApplication();
            }
            finally
            {
                //try
                //{
                //    MemoryPool.Dispose();
                //}
                //catch (Exception ex)
                //{
                //    _closeError = _closeError == null ? ex : new AggregateException(_closeError, ex);
                //}
                WriteReqPool.Dispose();
                _threadTcs.SetResult(null);

#if DEBUG
                // Check for handle leaks after disposing everything
                CheckUvReqLeaks();
#endif
            }
        }

        private void OnPost()
        {
            var loopsRemaining = _maxLoops;
            bool wasWork;
            do
            {
                wasWork = DoPostWork();
                wasWork = DoPostCloseHandle() || wasWork;
                loopsRemaining--;
            }
            while (wasWork && loopsRemaining > 0);
        }

        private bool DoPostWork()
        {
            Queue<Work> queue;
            lock (_workSync)
            {
                queue = _workAdding;
                _workAdding = _workRunning;
                _workRunning = queue;
            }

            bool wasWork = queue.Count > 0;
            while (queue.Count != 0)
            {
                var work = queue.Dequeue();
                try
                {
                    work.CallbackAdapter(work.Callback, work.State);
                    work.Completion?.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    if (work.Completion != null)
                    {
                        work.Completion.TrySetException(ex);
                    }
                    else
                    {
                        _log.LogError(0, ex, $"{nameof(UvThread)}.{nameof(DoPostWork)}");
                        throw;
                    }
                }
            }

            return wasWork;
        }

        private bool DoPostCloseHandle()
        {
            Queue<CloseHandle> queue;
            lock (_closeHandleSync)
            {
                queue = _closeHandleAdding;
                _closeHandleAdding = _closeHandleRunning;
                _closeHandleRunning = queue;
            }

            bool wasWork = queue.Count > 0;
            while (queue.Count != 0)
            {
                var closeHandle = queue.Dequeue();
                try
                {
                    closeHandle.Callback(closeHandle.Handle);
                }
                catch (Exception ex)
                {
                    _log.LogError(0, ex, $"{nameof(UvThread)}.{nameof(DoPostCloseHandle)}");
                    throw;
                }
            }

            return wasWork;
        }

        private void PostCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
            EnqueueCloseHandle(callback, handle);
            _post.Send();
        }

        private void EnqueueCloseHandle(Action<IntPtr> callback, IntPtr handle)
        {
            var closeHandle = new CloseHandle { Callback = callback, Handle = handle };
            lock (_closeHandleSync)
            {
                _closeHandleAdding.Enqueue(closeHandle);
            }
        }

        public void Walk(Action<IntPtr> callback)
        {
            Walk((ptr, arg) => callback(ptr), IntPtr.Zero);
        }

        private void Walk(Uv.uv_walk_cb callback, IntPtr arg)
        {
            _uv.walk(
                _loop,
                callback,
                arg);
        }

#if DEBUG
        private void CheckUvReqLeaks()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Detect leaks in UvRequest objects
            foreach (var request in Requests)
            {
                Debug.Assert(request.Target == null, $"{request.Target?.GetType()} object is still alive.");
            }
        }
#endif

        public async Task StopAsync(TimeSpan timeout)
        {
            lock (_startSync)
            {
                if (!_initCompleted)
                {
                    return;
                }
            }

            Debug.Assert(!_threadTcs.Task.IsCompleted, "The loop thread was completed before calling uv_unref on the post handle.");

            var stepTimeout = TimeSpan.FromTicks(timeout.Ticks / 3);

            try
            {
                Post(t => t.AllowStop());
                if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                {
                    Post(t => t.OnStopRude());
                    if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                    {
                        Post(t => t.OnStopImmediate());
                        if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                        {
                            _log.LogCritical($"{nameof(UvThread)}.{nameof(StopAsync)} failed to terminate libuv thread.");
                        }
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                if (!await WaitAsync(_threadTcs.Task, stepTimeout).ConfigureAwait(false))
                {
                    _log.LogCritical($"{nameof(UvThread)}.{nameof(StopAsync)} failed to terminate libuv thread.");
                }
            }

            if (_closeError != null)
            {
                ExceptionDispatchInfo.Capture(_closeError).Throw();
            }
        }

        private void AllowStop()
        {
            _post.Unreference();
        }

        private void OnStopRude()
        {
            Walk(ptr =>
            {
                var handle = UvMemory.FromIntPtr<UvHandle>(ptr);
                if (handle != _post)
                {
                    // handle can be null because UvMemory.FromIntPtr looks up a weak reference
                    handle?.Dispose();
                }
            });
        }

        private void OnStopImmediate()
        {
            _stopImmediate = true;
            _loop.Stop();
        }

        private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
        {
            return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
        }

        private void Post(Action<UvThread> callback)
        {
            Post(callback, this);
        }

        public void Post<T>(Action<T> callback, T state)
        {
            // Handle is closed to don't bother scheduling anything
            if (_post.IsClosed)
            {
                return;
            }

            var work = new Work
            {
                CallbackAdapter = CallbackAdapter<T>.PostCallbackAdapter,
                Callback = callback,
                // TODO: This boxes
                State = state
            };

            lock (_workSync)
            {
                _workAdding.Enqueue(work);
            }

            try
            {
                _post.Send();
            }
            catch (ObjectDisposedException)
            {
                // There's an inherent race here where we're in the middle of shutdown
            }
        }

        public Task PostAsync<T>(Action<T> callback, T state)
        {
            // Handle is closed to don't bother scheduling anything
            if (_post.IsClosed)
            {
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            var work = new Work
            {
                CallbackAdapter = CallbackAdapter<T>.PostAsyncCallbackAdapter,
                Callback = callback,
                State = state,
                Completion = tcs
            };

            lock (_workSync)
            {
                _workAdding.Enqueue(work);
            }

            try
            {
                _post.Send();
            }
            catch (ObjectDisposedException)
            {
                // There's an inherent race here where we're in the middle of shutdown
            }
            return tcs.Task;
        }

        #region Awaiter impl
        public bool IsCompleted => Thread.CurrentThread.ManagedThreadId == _thread.ManagedThreadId;

        public UvThread GetAwaiter() => this;

        public void GetResult()
        { }

        public void UnsafeOnCompleted(Action continuation)
        {
            OnCompleted(continuation);
        }

        public void OnCompleted(Action continuation)
        {
            Post(state => ((Action)state)(), continuation);
        }
        #endregion
    }
}
