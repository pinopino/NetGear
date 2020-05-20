using NetGear.Core.Common;
using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public class SocketListener : IDisposable
    {
        private int _backlog;
        private Socket _listener;
        private PipeOptions _sendPipeOptions;
        private PipeOptions _receivePipeOptions;

        public event Action<IPEndPoint> OnListenStarted;
        public event Action<Exception> OnListenFaulted;
        public event Action<SocketConnection> OnListenAccept;

        public SocketListener(
            int listenBacklog,
            PipeOptions sendPipeOptions,
            PipeOptions receivePipeOptions)
        {
            if (_listener != null)
                throw new InvalidOperationException("listener already running");

            _backlog = listenBacklog;
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _sendPipeOptions = sendPipeOptions;
            _receivePipeOptions = receivePipeOptions;
        }

        public void Start(IPEndPoint endPoint)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));

            _listener.Bind(endPoint);
            _listener.Listen(_backlog);

            Scheduler(_receivePipeOptions?.ReaderScheduler,
                _ => ListenForConnectionsAsync().FireAndForget(), null);

            OnListenStarted(endPoint);
        }

        private async Task ListenForConnectionsAsync()
        {
            try
            {
                while (true)
                {
                    var clientSocket = await _listener.AcceptAsync().ConfigureAwait(false);
                    SocketConnection.SetRecommendedServerOptions(clientSocket);

                    var connection = SocketConnection.Create(clientSocket, _sendPipeOptions, _receivePipeOptions);
                    Scheduler(_receivePipeOptions?.ReaderScheduler,
                        state => OnListenAccept((SocketConnection)state),
                        connection);
                }
            }
            catch (NullReferenceException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                OnListenFaulted(ex);
            }
        }

        private static void Scheduler(PipeScheduler scheduler, Action<object> callback, object state)
        {
            if (scheduler == PipeScheduler.Inline)
                scheduler = null;

            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }

        /// <summary>
        /// Stop listening as a server
        /// </summary>
        public void Stop()
        {
            var socket = _listener;
            _listener = null;
            if (socket != null)
            {
                try { socket.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Release any resources associated with this instance
        /// </summary>
        public void Dispose()
        {
            Stop();
            Dispose(true);
        }

        /// <summary>
        /// Release any resources associated with this instance
        /// </summary>
        protected virtual void Dispose(bool disposing) { }
    }
}
