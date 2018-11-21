using NetGear.Core.Connection;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace NetGear.Core.Client
{
    public class StreamedSocketClientConnection : StreamedSocketConnection
    {
        int _id;
        bool _debug;
        bool _connected;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        IPEndPoint _remoteEndPoint;

        public StreamedSocketClientConnection(int id, string address, int port, int bufferSize, bool debug = false)
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

