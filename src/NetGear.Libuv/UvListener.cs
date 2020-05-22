using Microsoft.Extensions.Logging;
using NetGear.Core;
using System;
using System.Net;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public partial class UvListener : IAsyncDisposable
    {
        private bool _closed;
        public UvThread Thread { get; set; }
        public UvStreamHandle ListenSocket { set; get; }
        public ILibuvTrace Log { set; get; }
        public IEndPointInformation EndPointInformation { get; set; }
        public IConnectionDispatcher Dispatcher { set; get; }

        public UvListener(UvThread thread, IEndPointInformation endpoint, ILibuvTrace log = null)
        {
            Thread = thread;
            EndPointInformation = endpoint;
            Log = log;
        }

        public Task StartAsync()
        {
            return Thread.PostAsync(listener => listener.StartListen(), this);
        }

        private void StartListen()
        {
            try
            {
                ListenSocket = CreateListenSocket();
                ListenSocket.Listen(UvConstants.ListenBacklog, ConnectionCallback, this);
            }
            catch
            {
                ListenSocket?.Dispose();
                throw;
            }
        }

        private void ConnectionCallback(UvStreamHandle listenSocket, int status, UvException error, object state)
        {
            var listener = (UvListener)state;

            if (error != null)
            {
                listener.Log.LogError(0, error, "Listener.ConnectionCallback");
            }
            else if (!listener._closed)
            {
                UvStreamHandle acceptSocket = null;
                try
                {
                    acceptSocket = CreateAcceptSocket();
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

        protected virtual void DispatchConnection(UvStreamHandle socket)
        {
            // REVIEW: This task should be tracked by the server for graceful shutdown
            // Today it's handled specifically for http but not for aribitrary middleware
            _ = HandleConnectionAsync(socket);
        }

        protected async Task HandleConnectionAsync(UvStreamHandle socket)
        {
            IPEndPoint remoteEndPoint = null;
            IPEndPoint localEndPoint = null;

            try
            {
                if (socket is UvTcpHandle tcpHandle)
                {
                    try
                    {
                        remoteEndPoint = tcpHandle.GetPeerIPEndPoint();
                        localEndPoint = tcpHandle.GetSockIPEndPoint();
                    }
                    catch (UvException ex) when (UvConstants.IsConnectionReset(ex.StatusCode))
                    {
                        Log.ConnectionReset("(null)");
                        socket.Dispose();
                        return;
                    }
                }

                var connection = new UvConnection(socket, Thread, remoteEndPoint, localEndPoint, log: Log);
                await connection.Start();

                await Dispatcher.OnConnection(connection);

                connection.Dispose();
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex, $"Unexpected exception in {nameof(UvListener)}.{nameof(HandleConnectionAsync)}.");
            }
        }

        public virtual async Task DisposeAsync()
        {
            // Ensure the event loop is still running.
            // If the event loop isn't running and we try to wait on this Post
            // to complete, then LibuvTransport will never be disposed and
            // the exception that stopped the event loop will never be surfaced.
            if (Thread.FatalError == null && ListenSocket != null)
            {
                await Thread.PostAsync(listener =>
                {
                    listener.ListenSocket.Dispose();

                    listener._closed = true;

                }, this).ConfigureAwait(false);
            }

            ListenSocket = null;
        }
    }
}
