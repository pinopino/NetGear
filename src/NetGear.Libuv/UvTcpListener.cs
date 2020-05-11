using System;
using System.Net;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public class UvTcpListener : IDisposable
    {
        public event Action<UvTcpConnection> OnConnection;
        private Action<UvStreamHandle, int, Exception, object> _onConnectionCallback;
        private Action<object> _startListeningCallback = state => ((UvTcpListener)state).Listen();
        private Action<object> _stopListeningCallback = state => ((UvTcpListener)state).Shutdown();

        private readonly IPEndPoint _endpoint;
        private readonly UvThread _thread;
        private UvTcpHandle _listenSocket;


        private TaskCompletionSource<object> _startedTcs = new TaskCompletionSource<object>();

        public UvTcpListener(UvThread thread, IPEndPoint endpoint)
        {
            _thread = thread;
            _endpoint = endpoint;
            _onConnectionCallback = OnConnectionCallback;
        }

        public Task StartAsync()
        {
            // TODO: Make idempotent
            _thread.Post(_startListeningCallback, this);

            return _startedTcs.Task;
        }

        public void Dispose()
        {
            // TODO: Make idempotent
            _thread.Post(_stopListeningCallback, this);
        }

        private void Shutdown()
        {
            _listenSocket.Dispose();
        }

        private void Listen()
        {
            // TODO: Error handling
            _listenSocket = new UvTcpHandle();
            _listenSocket.Init(_thread.Loop, null);
            _listenSocket.NoDelay(true);
            _listenSocket.Bind(_endpoint);
            _listenSocket.Listen(10, _onConnectionCallback, this);

            // Don't complete the task on the UV thread
            Task.Run(() => _startedTcs.TrySetResult(null));
        }

        private void OnConnectionCallback(UvStreamHandle listenSocket, int status, Exception error, object state)
        {
            var listener = (UvTcpListener)state;

            var acceptSocket = new UvTcpHandle();

            try
            {
                acceptSocket.Init(listener._thread.Loop, null);
                acceptSocket.NoDelay(true);
                listenSocket.Accept(acceptSocket);
                var connection = new UvTcpConnection(listener._thread, acceptSocket);
                OnConnection?.Invoke(connection);
            }
            catch (UvException)
            {
                acceptSocket.Dispose();
            }
        }
    }
}
