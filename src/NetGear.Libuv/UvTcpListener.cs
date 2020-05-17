using Microsoft.Extensions.Logging;
using NetGear.Core;
using System;
using System.Net;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public class UvTcpListener : IAsyncDisposable
    {
        private Action<UvStreamHandle, int, UvException, object> _onConnectionCallback;
        private Action<object> _startListeningCallback = state => ((UvTcpListener)state).Listen();

        private bool _closed;
        private readonly IPEndPoint _endpoint;
        protected readonly UvThread _thread;
        private UvTcpHandle _listenSocket;

        public UvTcpHandle ListenSocket => this._listenSocket;
        public ILibuvTrace Log => throw new NotImplementedException();

        public UvTcpListener(UvThread thread, IPEndPoint endpoint)
        {
            _thread = thread;
            _endpoint = endpoint;
            _onConnectionCallback = OnConnectionCallback;
        }

        public Task StartAsync()
        {
            return _thread.PostAsync(_startListeningCallback, this);
        }

        private void Listen()
        {
            _listenSocket = new UvTcpHandle(Log);
            try
            {
                _listenSocket.Init(_thread.Loop, _thread.QueueCloseHandle);
                _listenSocket.NoDelay(true);
                _listenSocket.Bind(_endpoint);
                _listenSocket.Listen(UvConstants.ListenBacklog, _onConnectionCallback, this);
            }
            catch
            {
                _listenSocket?.Dispose();
                throw;
            }
        }

        private void OnConnectionCallback(UvStreamHandle listenSocket, int status, UvException error, object state)
        {
            var listener = (UvTcpListener)state;

            if (error != null)
            {
                listener.Log.LogError(0, error, "Listener.ConnectionCallback");
            }
            else if (!listener._closed)
            {
                UvTcpHandle acceptSocket = null;
                try
                {
                    acceptSocket = new UvTcpHandle(Log);
                    acceptSocket.Init(listener._thread.Loop, listener._thread.QueueCloseHandle);
                    acceptSocket.NoDelay(true);

                    listenSocket.Accept(acceptSocket);

                    DispatchConnection(acceptSocket);
                }
                catch (UvException ex) when (UvConstants.IsConnectionReset(ex.StatusCode))
                {
                    Log.ConnectionReset("(null)");
                    acceptSocket?.Dispose();
                }
                catch (UvException ex)
                {
                    Log.LogError(0, ex, "Listener.OnConnection");
                    acceptSocket?.Dispose();
                }
            }
        }

        protected virtual void DispatchConnection(UvTcpHandle socket)
        {
            // REVIEW: This task should be tracked by the server for graceful shutdown
            // Today it's handled specifically for http but not for aribitrary middleware
            _ = HandleConnectionAsync(socket);
        }

        private async Task HandleConnectionAsync(UvTcpHandle socket)
        {
            try
            {
                IPEndPoint remoteEndPoint = null;
                IPEndPoint localEndPoint = null;

                try
                {
                    remoteEndPoint = socket.GetPeerIPEndPoint();
                    localEndPoint = socket.GetSockIPEndPoint();
                }
                catch (UvException ex) when (UvConstants.IsConnectionReset(ex.StatusCode))
                {
                    Log.ConnectionReset("(null)");
                    socket.Dispose();
                    return;
                }

                var connection = new LibuvConnection(socket, Log, _thread, remoteEndPoint, localEndPoint);
                await connection.Start();

                connection.Dispose();
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex, $"Unexpected exception in {nameof(UvTcpListener)}.{nameof(HandleConnectionAsync)}.");
            }
        }

        public virtual async Task DisposeAsync()
        {
            // Ensure the event loop is still running.
            // If the event loop isn't running and we try to wait on this Post
            // to complete, then LibuvTransport will never be disposed and
            // the exception that stopped the event loop will never be surfaced.
            if (_thread.FatalError == null && _listenSocket != null)
            {
                await _thread.PostAsync(listener =>
                {
                    listener.ListenSocket.Dispose();

                    listener._closed = true;

                }, this).ConfigureAwait(false);
            }

            _listenSocket = null;
        }
    }
}
