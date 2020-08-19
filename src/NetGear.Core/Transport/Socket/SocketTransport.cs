using Microsoft.Extensions.Logging;
using NetGear.Core.Common;
using System;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public class SocketTransport : ITransport
    {
        private readonly struct PipeOptionsPair
        {
            public PipeOptions SendOpts { get; }
            public PipeOptions ReceiveOpts { get; }

            public PipeOptionsPair(PipeOptions sendOpts, PipeOptions receiveOpts)
            {
                SendOpts = sendOpts;
                ReceiveOpts = receiveOpts;
            }
        }

        private int _backlog;
        private readonly int _numSchedulers;
        private volatile bool _unbinding;
        private Socket _listener;
        private Task _listenTask;
        private Exception _listenException;
        private MemoryPool<byte> _pool;
        private readonly PipeOptionsPair[] _cachedPipeOpts;
        private readonly ISocketsTrace _trace;
        private readonly IEndPointInformation _endPointInformation;
        private readonly IConnectionDispatcher _dispatcher;

        public SocketTransport(IEndPointInformation endPointInformation,
            IConnectionDispatcher dispatcher,
            int listenBacklog,
            int ioQueueCount,
            MemoryPool<byte> pool = null)
        {
            if (endPointInformation == null)
                throw new ArgumentNullException(nameof(endPointInformation));
            if (endPointInformation.Type != ListenType.IPEndPoint)
                throw new InvalidOperationException(nameof(endPointInformation.IPEndPoint));
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (listenBacklog < 0)
                throw new InvalidOperationException(nameof(listenBacklog));

            _endPointInformation = endPointInformation;
            _dispatcher = dispatcher;
            _backlog = listenBacklog;
            _pool = pool;

            if (ioQueueCount > 0)
            {
                _numSchedulers = ioQueueCount;
                _cachedPipeOpts = new PipeOptionsPair[_numSchedulers];

                for (var i = 0; i < _numSchedulers; i++)
                    _cachedPipeOpts[i] = new PipeOptionsPair(
                        GetSendPipeOptions(_pool, new IOQueue()),
                        GetReceivePipeOptions(_pool, new IOQueue()));
            }
            else
            {
                _numSchedulers = 1;
                _cachedPipeOpts = new PipeOptionsPair[] { new PipeOptionsPair(
                    GetSendPipeOptions(_pool, new IOQueue()),
                    GetReceivePipeOptions(_pool, new IOQueue())) };
            }
        }

        public SocketTransport(IEndPointInformation endPointInformation,
            IConnectionDispatcher dispatcher,
            int listenBacklog,
            PipeOptions sendOptions,
            PipeOptions receiveOptions)
        {
            if (endPointInformation == null)
                throw new ArgumentNullException(nameof(endPointInformation));
            if (endPointInformation.Type != ListenType.IPEndPoint)
                throw new InvalidOperationException(nameof(endPointInformation.IPEndPoint));
            if (dispatcher == null)
                throw new ArgumentNullException(nameof(dispatcher));
            if (listenBacklog < 0)
                throw new InvalidOperationException(nameof(listenBacklog));

            _endPointInformation = endPointInformation;
            _dispatcher = dispatcher;
            _backlog = listenBacklog;

            _numSchedulers = 1;
            _cachedPipeOpts = new PipeOptionsPair[] { new PipeOptionsPair(sendOptions, receiveOptions) };
        }

        private async Task ListenForConnectionsAsync()
        {
            try
            {
                while (true)
                {
                    for (var i = 0; i < _numSchedulers; i++)
                    {
                        var clientSocket = await _listener.AcceptAsync();
                        SocketConnection.SetRecommendedServerOptions(clientSocket);

                        var connection = SocketConnection.Create(clientSocket, _cachedPipeOpts[i].SendOpts, _cachedPipeOpts[i].ReceiveOpts);
                        _dispatcher.OnConnection(connection).FireAndForget();
                    }
                }
            }
            catch (NullReferenceException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                if (_unbinding)
                {
                    // Means we must be unbinding. Eat the exception.
                }
                else
                {
                    var mark = $"Unexpected exception in {nameof(SocketTransport)}.{nameof(ListenForConnectionsAsync)}.";
                    _trace.LogCritical(ex, mark);
                    _listenException = new ListenLoopException(mark, ex);

                    // Request shutdown so we can rethrow this exception
                    // in Stop which should be observable.
                    _dispatcher.StopAsync().FireAndForget(); // 说明：stop里面调用unbind，以便重新抛出异常
                }
            }
        }

        public Task BindAsync()
        {
            if (_listener != null)
                throw new InvalidOperationException("listener already bound");

            var endPoint = _endPointInformation.IPEndPoint;
            var listenSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            EnableRebinding(listenSocket);

            // Kestrel expects IPv6Any to bind to both IPv6 and IPv4
            if (endPoint.Address == IPAddress.IPv6Any)
            {
                listenSocket.DualMode = true;
            }

            try
            {
                listenSocket.Bind(endPoint);
            }
            catch (SocketException e) when (e.SocketErrorCode == SocketError.AddressAlreadyInUse)
            {
                throw new AddressInUseException(e.Message, e);
            }

            // If requested port was "0", replace with assigned dynamic port.
            if (_endPointInformation.IPEndPoint.Port == 0)
            {
                _endPointInformation.IPEndPoint = (IPEndPoint)listenSocket.LocalEndPoint;
            }

            listenSocket.Listen(_backlog);
            _listener = listenSocket;
            _listenTask = Task.Run(ListenForConnectionsAsync);

            return Task.CompletedTask;
        }

        public async Task UnbindAsync()
        {
            if (_listener != null)
            {
                _unbinding = true;
                _listener.Dispose();

                if (_listenTask == null)
                    throw new InvalidOperationException("listenTask can not be null");

                await _listenTask.ConfigureAwait(false);

                _unbinding = false;
                _listener = null;
                _listenTask = null;

                if (_listenException != null)
                {
                    var exInfo = ExceptionDispatchInfo.Capture(_listenException);
                    _listenException = null;
                    exInfo.Throw();
                }
            }
        }

        public Task StopAsync()
        {
            return Task.CompletedTask;
        }

        private static void Scheduler(PipeScheduler scheduler, Action<object> callback, object state)
        {
            if (scheduler == PipeScheduler.Inline)
                scheduler = null;

            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }

        private static PipeOptions GetReceivePipeOptions(MemoryPool<byte> memoryPool, PipeScheduler writerScheduler)
            => new PipeOptions
            (
                pool: memoryPool,
                readerScheduler: PipeScheduler.ThreadPool,
                writerScheduler: writerScheduler,
                useSynchronizationContext: false
            );

        private static PipeOptions GetSendPipeOptions(MemoryPool<byte> memoryPool, PipeScheduler readerScheduler)
            => new PipeOptions
            (
                pool: memoryPool,
                readerScheduler: readerScheduler,
                writerScheduler: PipeScheduler.ThreadPool,
                useSynchronizationContext: false
            );

        [DllImport("libc", SetLastError = true)]
        private static extern int setsockopt(int socket, int level, int option_name, IntPtr option_value, uint option_len);

        private const int SOL_SOCKET_OSX = 0xffff;
        private const int SO_REUSEADDR_OSX = 0x0004;
        private const int SOL_SOCKET_LINUX = 0x0001;
        private const int SO_REUSEADDR_LINUX = 0x0002;

        // Without setting SO_REUSEADDR on macOS and Linux, binding to a recently used endpoint can fail.
        // https://github.com/dotnet/corefx/issues/24562
        private unsafe void EnableRebinding(Socket listenSocket)
        {
            var optionValue = 1;
            var setsockoptStatus = 0;

            if (PlatformApis.IsLinux)
            {
                setsockoptStatus = setsockopt(listenSocket.Handle.ToInt32(), SOL_SOCKET_LINUX, SO_REUSEADDR_LINUX,
                                              (IntPtr)(&optionValue), sizeof(int));
            }
            else if (PlatformApis.IsDarwin)
            {
                setsockoptStatus = setsockopt(listenSocket.Handle.ToInt32(), SOL_SOCKET_OSX, SO_REUSEADDR_OSX,
                                              (IntPtr)(&optionValue), sizeof(int));
            }

            if (setsockoptStatus != 0)
            {
                _trace.LogInformation("Setting SO_REUSEADDR failed with errno '{errno}'.", Marshal.GetLastWin32Error());
            }
        }
    }
}
