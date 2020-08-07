using NetGear.Core.Common;
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
            Socket socket = null, string name = null)
            => ConnectAsync(endpoint, pipeOptions, pipeOptions, connectionOptions, onConnected, socket, name);

        /// <summary>
        /// Open a new or existing socket as a client
        /// </summary>
        public static async Task<SocketConnection> ConnectAsync(
            EndPoint endpoint,
            PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions connectionOptions = SocketConnectionOptions.None,
            Func<SocketConnection, Task> onConnected = null,
            Socket socket = null, string name = null)
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

            using (var args = new SocketAwaitableEventArgs((connectionOptions & SocketConnectionOptions.InlineConnect) == 0 ? PipeScheduler.ThreadPool : null))
            {
                args.RemoteEndPoint = endpoint;
                Debugger.Log(name, $"connecting to {endpoint}...");

                if (!socket.ConnectAsync(args))
                    args.Complete();
                await args;
            }

            Debugger.Log(name, "connected");

            var connection = Create(socket, sendPipeOptions, receivePipeOptions, connectionOptions, name);

            if (onConnected != null)
                await onConnected(connection).ConfigureAwait(false);

            return connection;
        }

        internal static void SetFastLoopbackOption(Socket socket)
        {
            // SIO_LOOPBACK_FAST_PATH (https://msdn.microsoft.com/en-us/library/windows/desktop/jj841212%28v=vs.85%29.aspx)
            // Speeds up localhost operations significantly. OK to apply to a socket that will not be hooked up to localhost,
            // or will be subject to WFP filtering.
            const int SIO_LOOPBACK_FAST_PATH = -1744830448;

            // windows only
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                // Win8/Server2012+ only
                var osVersion = Environment.OSVersion.Version;
                if (osVersion.Major > 6 || (osVersion.Major == 6 && osVersion.Minor >= 2))
                {
                    byte[] optionInValue = BitConverter.GetBytes(1);
                    socket.IOControl(SIO_LOOPBACK_FAST_PATH, optionInValue, null);
                }
            }
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
