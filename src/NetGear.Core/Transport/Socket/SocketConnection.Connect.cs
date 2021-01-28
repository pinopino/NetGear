using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetGear.Core.Diagnostics;
using System;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public partial class SocketConnection
    {
        private static readonly bool IsWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly bool IsMacOS = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

        /// <summary>
        /// Open a new or existing socket as a client
        /// </summary>
        public static Task<SocketConnection> ConnectAsync(
            EndPoint endpoint,
            PipeOptions pipeOptions = null,
            SocketConnectionOptions connectionOptions = SocketConnectionOptions.None,
            Func<SocketConnection, Task> onConnected = null,
            Socket socket = null,
            string name = null,
            ILogger logger = null)
            => ConnectAsync(endpoint, pipeOptions, pipeOptions, connectionOptions, onConnected, socket, name, logger);

        /// <summary>
        /// Open a new or existing socket as a client
        /// </summary>
        public static async Task<SocketConnection> ConnectAsync(
            EndPoint endpoint,
            PipeOptions sendPipeOptions,
            PipeOptions receivePipeOptions,
            SocketConnectionOptions connectionOptions = SocketConnectionOptions.None,
            Func<SocketConnection, Task> onConnected = null,
            Socket socket = null,
            string name = null,
            ILogger logger = null)
        {
            var addressFamily = endpoint.AddressFamily == AddressFamily.Unspecified ?
                AddressFamily.InterNetwork : endpoint.AddressFamily;
            var protocolType = addressFamily == AddressFamily.Unix ?
                ProtocolType.Unspecified : ProtocolType.Tcp;

            if (socket == null)
                socket = new Socket(addressFamily, SocketType.Stream, protocolType);
            if (sendPipeOptions == null)
                sendPipeOptions = PipeOptions.Default;
            if (receivePipeOptions == null)
                receivePipeOptions = PipeOptions.Default;

            SetRecommendedClientOptions(socket);

            logger = logger ?? NullLoggerFactory.Instance.CreateLogger("NetGear.Core.SocketConnection");
            using (var args = new SocketAwaitableEventArgs((connectionOptions & SocketConnectionOptions.InlineConnect) == 0 ? PipeScheduler.ThreadPool : null))
            {
                args.RemoteEndPoint = endpoint;
                _logger.LogVerbose(name, $"connecting to {endpoint}...");

                if (!socket.ConnectAsync(args))
                    args.Complete();
                await args;
            }

            _logger.LogVerbose(name, "connected");

            var connection = Create(socket, sendPipeOptions, receivePipeOptions, connectionOptions, name, logger);

            if (onConnected != null)
                await onConnected(connection).ConfigureAwait(false);

            return connection;
        }

        /// <summary>
        /// Create a SocketConnection instance over an existing socket；
        /// 进出两个方向都采用完全一样的设置。通常用于客户端，服务端不会也不应该这样子搞
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions pipeOptions = null,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null, ILogger logger = null)
        {
            return new SocketConnection(socket, pipeOptions, pipeOptions, socketConnectionOptions, name, logger);
        }

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null, ILogger logger = null)
        {
            return new SocketConnection(socket, sendPipeOptions, receivePipeOptions, socketConnectionOptions, name, logger);
        }

        private static bool IsConnectionResetError(SocketError errorCode)
        {
            // A connection reset can be reported as SocketError.ConnectionAborted on Windows.
            // ProtocolType can be removed once https://github.com/dotnet/corefx/issues/31927 is fixed.
            return errorCode == SocketError.ConnectionReset ||
                   errorCode == SocketError.Shutdown ||
                   (errorCode == SocketError.ConnectionAborted && IsWindows);
        }

        private static bool IsConnectionAbortError(SocketError errorCode)
        {
            // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
            return errorCode == SocketError.OperationAborted ||
                   errorCode == SocketError.Interrupted ||
                   (errorCode == SocketError.InvalidArgument && !IsWindows);
        }
    }
}
