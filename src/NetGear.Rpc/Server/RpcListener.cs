using NetGear.Core.Connection;
using NetGear.Core.Listener;
using System.Net.Sockets;

namespace NetGear.Rpc.Server
{
    public sealed class RpcListener : BaseListener
    {
        bool _debug;
        int _bufferSize;
        RpcServer _server;


        public RpcListener(int maxConnectionCount, int bufferSize, RpcServer server, bool debug = false)
            : base(maxConnectionCount, bufferSize, debug)
        {
            _debug = debug;
            _server = server;
            _bufferSize = bufferSize;
        }

        protected override BaseConnection CreateConnection(SocketAsyncEventArgs e)
        {
            return new RpcConnection(_connectedCount, _server, e.AcceptSocket, this, _bufferSize, _debug);
        }
    }
}
