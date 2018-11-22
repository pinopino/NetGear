using NetGear.Core.Connection;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetGear.Core.Client
{
    public class EAPStreamedClientConnection : EAPStreamedConnection
    {
        int _id;
        bool _debug;
        bool _disposed;
        bool _connected;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        IPEndPoint _remoteEndPoint;

        public EAPStreamedClientConnection(int id, string address, int port, int bufferSize, bool debug = false)
            : base(id, new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), debug)
        {
            _id = id;
            _debug = debug;
            _disposed = false;
            _connected = false;
            _bufferSize = bufferSize;
            _connectTimeout = 5 * 1000;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
            
            _readEventArgs = new SocketAsyncEventArgs();
            _readEventArgs.UserToken = new Token();
            _readEventArgs.Completed += Read_Completed;
            _readEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);

            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.UserToken = new Token();
            _sendEventArgs.Completed += Send_Completed;
            _sendEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
        }

        private void On_ReadBytesComplete(object sender, ArraySegment<byte> e)
        {
            Console.WriteLine("收到服务端返回：" + System.Text.Encoding.UTF8.GetString(e.Array));
        }

        public override void Start()
        {
            // just do nothing
            OnReadBytesComplete += On_ReadBytesComplete;
        }

        public void Connect()
        {
            if (_connected)
                return;

            var connectEventArgs = new SocketAsyncEventArgs
            {
                RemoteEndPoint = _remoteEndPoint
            };
            connectEventArgs.Completed += (sender, e) =>
            {
                _connected = true;
            };

            if (_socket.ConnectAsync(connectEventArgs))
            {
                while (!_connected)
                {
                    if (!SpinWait.SpinUntil(() => _connected, _connectTimeout))
                    {
                        throw new TimeoutException("Unable to connect within " + _connectTimeout + "ms");
                    }
                }
            }
            if (connectEventArgs.SocketError != SocketError.Success)
            {
                Close();
                throw new SocketException((int)connectEventArgs.SocketError);
            }
            if (!_socket.Connected)
            {
                Close();
                throw new SocketException((int)SocketError.NotConnected);
            }

            // 至此，已经成功连接到远程服务端
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
                _readEventArgs.Dispose();
                _sendEventArgs.Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;

            // 调用基类dispose
            base.Dispose();
        }
    }
}

