using NetGear.Core.Common;
using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public abstract class SocketListener : IDisposable
    {
        /// <summary>
        /// The state of a client connection
        /// </summary>
        protected readonly struct ClientConnection
        {
            internal ClientConnection(IDuplexPipe transport, EndPoint remoteEndPoint)
            {
                Transport = transport;
                RemoteEndPoint = remoteEndPoint;
            }

            /// <summary>
            /// The transport to use for this connection
            /// </summary>
            public IDuplexPipe Transport { get; }

            /// <summary>
            /// The remote endpoint that the client connected from
            /// </summary>
            public EndPoint RemoteEndPoint { get; }
        }

        private int _backlog;
        private Socket _listener;
        private PipeOptions _sendPipeOptions;
        private PipeOptions _receivePipeOptions;
        private readonly Action<object> RunClientAsync;

        public SocketListener(
            AddressFamily addressFamily = AddressFamily.InterNetwork,
            SocketType socketType = SocketType.Stream,
            ProtocolType protocolType = ProtocolType.Tcp,
            int listenBacklog = 20,
            PipeOptions sendPipeOptions = null,
            PipeOptions receivePipeOptions = null)
        {
            if (_listener != null)
                throw new InvalidOperationException("listener already running");

            _backlog = listenBacklog;
            _listener = new Socket(addressFamily, socketType, protocolType);

            _sendPipeOptions = sendPipeOptions;
            _receivePipeOptions = receivePipeOptions;

            RunClientAsync = async boxed =>
            {
                var client = (ClientConnection)boxed;
                try
                {
                    await OnClientConnectedAsync(client).ConfigureAwait(false);
                    try { client.Transport.Input.Complete(); } catch { }
                    try { client.Transport.Output.Complete(); } catch { }
                    OnClientDisconnected(in client);
                }
                catch (Exception ex)
                {
                    try { client.Transport.Input.Complete(ex); } catch { }
                    try { client.Transport.Output.Complete(ex); } catch { }
                    OnClientFaulted(in client, ex);
                }
                finally
                {
                    if (client.Transport is IDisposable d)
                    {
                        try { d.Dispose(); } catch { }
                    }
                }
            };
        }

        public void Start(IPEndPoint endPoint)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));

            _listener.Bind(endPoint);
            _listener.Listen(_backlog);

            Scheduler(_receivePipeOptions?.ReaderScheduler,
                _ => ListenForConnectionsAsync().FireAndForget(), null);

            OnStarted(endPoint);
        }

        private static void Scheduler(PipeScheduler scheduler, Action<object> callback, object state)
        {
            if (scheduler == PipeScheduler.Inline)
                scheduler = null;

            (scheduler ?? PipeScheduler.ThreadPool).Schedule(callback, state);
        }

        private async Task ListenForConnectionsAsync()
        {
            try
            {
                while (true)
                {
                    var clientSocket = await _listener.AcceptAsync().ConfigureAwait(false);
                    SocketConnection.SetRecommendedServerOptions(clientSocket);
                    var pipe = SocketConnection.Create(clientSocket, _sendPipeOptions, _receivePipeOptions);

                    Scheduler(_receivePipeOptions?.ReaderScheduler, RunClientAsync,
                        new ClientConnection(pipe, clientSocket.RemoteEndPoint)); // boxed, but only once per client
                }
            }
            catch (NullReferenceException)
            { }
            catch (ObjectDisposedException)
            { }
            catch (Exception ex)
            {
                OnServerFaulted(ex);
            }
        }

        /// <summary>
        /// Invoked when the server has faulted
        /// </summary>
        protected virtual void OnServerFaulted(Exception exception)
        { }

        /// <summary>
        /// Invoked when a client has disconnected
        /// </summary>
        protected virtual void OnClientDisconnected(in ClientConnection client)
        { }

        /// <summary>
        /// Invoked when a client has faulted
        /// </summary>
        protected virtual void OnClientFaulted(in ClientConnection client, Exception exception)
        { }

        /// <summary>
        /// Invoked when the server starts
        /// </summary>
        protected virtual void OnStarted(EndPoint endPoint)
        { }

        /// <summary>
        /// Invoked when a new client connects
        /// </summary>
        protected abstract Task OnClientConnectedAsync(in ClientConnection client);

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
