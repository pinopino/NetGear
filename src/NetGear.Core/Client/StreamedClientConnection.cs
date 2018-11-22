using NetGear.Core.Connection;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetGear.Core.Client
{
    public class StreamedClientConnection : StreamedConnection
    {
        int _id;
        bool _debug;
        bool _connected;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        IPEndPoint _remoteEndPoint;

        public StreamedClientConnection(int id, string address, int port, int bufferSize, bool debug = false)
            : base(id, new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), debug)
        {
            _id = id;
            _debug = debug;
            _connected = false;
            _bufferSize = bufferSize;
            _connectTimeout = 5 * 1000;
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
        }

        public override void Start()
        {
            // just do nothing
        }

        protected override void InitSAEA()
        {
            _readEventArgs = new SocketAsyncEventArgs();
            _readEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
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
    }
}

