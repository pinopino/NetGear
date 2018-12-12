using NetGear.Core;
using NetGear.Core.Connection;
using NetGear.Core.Listener;
using System;
using System.Net;
using System.Net.Sockets;

namespace Echo.Server
{
    public sealed class EchoListener : BaseListener
    {
        bool _debug;

        public EchoListener(int maxConnectionCount, int bufferSize, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;            
        }

        protected override BaseConnection CreateConnection(SocketAsyncEventArgs e)
        {
            return new EchoConnection(_connectedCount, e.AcceptSocket, this, _debug);
        }
    }

    public sealed class EchoConnection : EAPStreamedConnection
    {
        bool _disposed;
        EchoListener _listener;

        public EchoConnection(int id, Socket socket, EchoListener listener, bool debug)
            : base(id, socket, debug)
        {
            _disposed = false;
            _listener = listener;

            _readEventArgs = _listener.SocketAsyncReadEventArgsPool.Get();
            _readEventArgs.UserToken = new Token();
            _readEventArgs.Completed += Read_Completed;

            _sendEventArgs = _listener.SocketAsyncSendEventArgsPool.Get();
            _sendEventArgs.UserToken = new Token();
            _sendEventArgs.Completed += Send_Completed;
        }

        private void EchoConnection_OnReadBytesComplete(object sender, ArraySegment<byte> e)
        {
            Console.WriteLine("收到消息：" + System.Text.Encoding.UTF8.GetString(e.Array, 0, e.Count));
            BeginWrite(e.Array, 0, e.Count, false);
            BeginReadBytes(12);
        }

        public override void Start()
        {
            OnReadBytesComplete += EchoConnection_OnReadBytesComplete;
            try
            {
                BeginReadBytes(12);
            }
            catch (SocketException ex)
            {
                Abort("远程连接被关闭");
            }
            catch (Exception ex)
            {
                Close();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _readEventArgs.Completed -= Read_Completed;
                _sendEventArgs.Completed -= Send_Completed;
                _readEventArgs.UserToken = null;
                _sendEventArgs.UserToken = null;
                ((PooledSocketAsyncEventArgs)_readEventArgs).Dispose();
                ((PooledSocketAsyncEventArgs)_sendEventArgs).Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;

            // 调用基类dispose
            base.Dispose();
        }
    }

    public class EchoServer
    {
        bool _debug;
        int _bufferSize = 512;
        int _maxConnectionCount = 500;
        IPEndPoint _endPoint;
        BaseListener _listener;

        public EchoServer(string address, int port, bool debug = false)
        {
            _debug = debug;
            _listener = new EchoListener(_maxConnectionCount, _bufferSize, debug);
            _endPoint = new IPEndPoint(IPAddress.Parse(address), port);
        }

        public void Start()
        {
            ServerStarting();
            _listener.Start(_endPoint);
            ServerStarted();
            _listener.OnConnectionCreated += (sender, info) => { Console.WriteLine("新建立连接：" + info); };
            _listener.OnConnectionAborted += (sender, info) => { Console.WriteLine("连接被终止：" + info); };
            _listener.OnConnectionClosed += (sender, info) => { Console.WriteLine("连接关闭：" + info); };
        }

        public void Stop()
        {
            ServerStopping();
            _listener.Stop();
            ServerStopped();
        }

        private void ServerStarting()
        {
            Console.WriteLine("server开启中...");
        }

        private void ServerStarted()
        {
            Console.WriteLine("server启动成功");
        }

        private void ServerStopping()
        {
            Console.WriteLine("server关闭中...");
        }

        private void ServerStopped()
        {
            Console.WriteLine("server已关闭");
        }
    }
}
