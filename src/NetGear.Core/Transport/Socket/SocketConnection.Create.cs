using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;

namespace NetGear.Core
{
    public partial class SocketConnection
    {
        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        internal static SocketConnection Create(Socket socket, PipeScheduler scheduler,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
            => Create(socket, scheduler, PipeOptions.Default.Pool, socketConnectionOptions, name);

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        internal static SocketConnection Create(Socket socket, PipeScheduler scheduler, MemoryPool<byte> pool,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            var option = new PipeOptions(pool, scheduler);
            return new SocketConnection(socket, option, option, socketConnectionOptions, name);
        }

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        internal static SocketConnection Create(Socket socket, PipeOptions pipeOptions = null,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            return new SocketConnection(socket, pipeOptions, pipeOptions, socketConnectionOptions, name);
        }

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        internal static SocketConnection Create(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            return new SocketConnection(socket, sendPipeOptions, receivePipeOptions, socketConnectionOptions, name);
        }
    }
}
