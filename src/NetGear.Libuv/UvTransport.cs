using Microsoft.Extensions.Logging;
using NetGear.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public class UvTransport : ITransport
    {
        private int _threadCount;
        public ILibuvTrace Log { set; get; }
        public List<UvThread> Threads { get; }
        private readonly List<IAsyncDisposable> _listeners;
        private readonly IEndPointInformation _endPointInformation;
        private readonly IConnectionDispatcher _dispatcher;

        public UvTransport(IEndPointInformation endPoint, IConnectionDispatcher dispatcher, int threadCount = 1, ILibuvTrace log = null)
        {
            _endPointInformation = endPoint;
            _dispatcher = dispatcher;
            _threadCount = threadCount;
            Log = log;
            Threads = new List<UvThread>();
            _listeners = new List<IAsyncDisposable>();
        }

        public async Task BindAsync()
        {
            if (Threads.Count != _threadCount)
            {
                for (var index = 0; index < _threadCount; index++)
                {
                    Threads.Add(new UvThread(Log));
                }

                foreach (var thread in Threads)
                {
                    await thread.StartAsync().ConfigureAwait(false);
                }
            }

            try
            {
                if (_threadCount == 1)
                {
                    var listener = new UvListener(Threads[0], _endPointInformation, Log);
                    listener.Dispatcher = _dispatcher;
                    _listeners.Add(listener);
                    await listener.StartAsync().ConfigureAwait(false);
                }
                else
                {
                    var pipeName = (PlatformApis.IsWindows ? @"\\.\pipe\kestrel_" : "/tmp/kestrel_") + Guid.NewGuid().ToString("n");
                    var pipeMessage = Guid.NewGuid().ToByteArray();

                    var listenerPrimary = new UvListenerPrimary(Threads[0], _endPointInformation, Log);
                    listenerPrimary.Dispatcher = _dispatcher;
                    _listeners.Add(listenerPrimary);
                    await listenerPrimary.StartAsync(pipeName, pipeMessage).ConfigureAwait(false);

                    foreach (var thread in Threads.Skip(1))
                    {
                        var listenerSecondary = new UvListenerSecondary(thread, Log);
                        listenerSecondary.Dispatcher = _dispatcher;
                        _listeners.Add(listenerSecondary);
                        await listenerSecondary.StartAsync(pipeName, pipeMessage).ConfigureAwait(false);
                    }
                }
            }
            catch (UvException ex) when (ex.StatusCode == UvConstants.EADDRINUSE)
            {
                await UnbindAsync().ConfigureAwait(false);
                throw new AddressInUseException(ex.Message, ex);
            }
            catch
            {
                await UnbindAsync().ConfigureAwait(false);
                throw;
            }
        }

        public async Task UnbindAsync()
        {
            // 说明：unbind时清理掉listener资源，threads没有动，可以支持rebind
            var disposeTasks = _listeners.Select(listener => listener.DisposeAsync()).ToArray();

            if (!await WaitAsync(Task.WhenAll(disposeTasks), TimeSpan.FromSeconds(5)).ConfigureAwait(false))
            {
                Log.LogError(0, null, "Disposing listeners failed");
            }

            _listeners.Clear();
        }

        public async Task StopAsync()
        {
            try
            {
                await Task.WhenAll(Threads.Select(thread => thread.StopAsync(TimeSpan.FromSeconds(5))).ToArray())
                    .ConfigureAwait(false);
            }
            catch (AggregateException aggEx)
            {
                // An uncaught exception was likely thrown from the libuv event loop.
                // The original error that crashed one loop may have caused secondary errors in others.
                // Make sure that the stack trace of the original error is logged.
                foreach (var ex in aggEx.InnerExceptions)
                {
                    Log.LogCritical("Failed to gracefully close Kestrel.", ex);
                }

                throw;
            }

            Threads.Clear();
        }

        private static async Task<bool> WaitAsync(Task task, TimeSpan timeout)
        {
            return await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false) == task;
        }
    }
}
