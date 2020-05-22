using Microsoft.Extensions.Logging;
using NetGear.Core;
using System;

namespace NetGear.Libuv
{
    public partial class UvListener
    {
        /// <summary>
        /// Creates the socket used to listen for incoming connections
        /// </summary>
        private UvStreamHandle CreateListenSocket()
        {
            switch (EndPointInformation.Type)
            {
                case ListenType.IPEndPoint:
                    return ListenTcp(useFileHandle: false);
                case ListenType.SocketPath:
                    return ListenPipe(useFileHandle: false);
                case ListenType.FileHandle:
                    return ListenHandle();
                default:
                    throw new NotSupportedException();
            }
        }

        private UvTcpHandle ListenTcp(bool useFileHandle)
        {
            var socket = new UvTcpHandle(Log);

            try
            {
                socket.Init(Thread.Loop, Thread.QueueCloseHandle);
                socket.NoDelay(EndPointInformation.NoDelay);

                if (!useFileHandle)
                {
                    socket.Bind(EndPointInformation.IPEndPoint);

                    // If requested port was "0", replace with assigned dynamic port.
                    EndPointInformation.IPEndPoint = socket.GetSockIPEndPoint();
                }
                else
                {
                    socket.Open((IntPtr)EndPointInformation.FileHandle);
                }
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            return socket;
        }

        private UvPipeHandle ListenPipe(bool useFileHandle)
        {
            var pipe = new UvPipeHandle(Log);

            try
            {
                pipe.Init(Thread.Loop, Thread.QueueCloseHandle, false);

                if (!useFileHandle)
                {
                    pipe.Bind(EndPointInformation.SocketPath);
                }
                else
                {
                    pipe.Open((IntPtr)EndPointInformation.FileHandle);
                }
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            return pipe;
        }

        private UvStreamHandle ListenHandle()
        {
            switch (EndPointInformation.HandleType)
            {
                case FileHandleType.Auto:
                    break;
                case FileHandleType.Tcp:
                    return ListenTcp(useFileHandle: true);
                case FileHandleType.Pipe:
                    return ListenPipe(useFileHandle: true);
                default:
                    throw new NotSupportedException();
            }

            UvStreamHandle handle;
            try
            {
                handle = ListenTcp(useFileHandle: true);
                EndPointInformation.HandleType = FileHandleType.Tcp;
                return handle;
            }
            catch (UvException exception) when (exception.StatusCode == UvConstants.ENOTSUP)
            {
                Log.LogDebug(0, exception, "Listener.ListenHandle");
            }

            handle = ListenPipe(useFileHandle: true);
            EndPointInformation.HandleType = FileHandleType.Pipe;
            return handle;
        }

        #region listener context
        /// <summary>
        /// Creates a socket which can be used to accept an incoming connection.
        /// </summary>
        protected UvStreamHandle CreateAcceptSocket()
        {
            switch (EndPointInformation.Type)
            {
                case ListenType.IPEndPoint:
                    return AcceptTcp();
                case ListenType.SocketPath:
                    return AcceptPipe();
                case ListenType.FileHandle:
                    return AcceptHandle();
                default:
                    throw new InvalidOperationException();
            }
        }

        private UvTcpHandle AcceptTcp()
        {
            var socket = new UvTcpHandle(Log);

            try
            {
                socket.Init(Thread.Loop, Thread.QueueCloseHandle);
                socket.NoDelay(EndPointInformation.NoDelay);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            return socket;
        }

        private UvPipeHandle AcceptPipe()
        {
            var pipe = new UvPipeHandle(Log);

            try
            {
                pipe.Init(Thread.Loop, Thread.QueueCloseHandle);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            return pipe;
        }

        private UvStreamHandle AcceptHandle()
        {
            switch (EndPointInformation.HandleType)
            {
                case FileHandleType.Auto:
                    throw new InvalidOperationException("Cannot accept on a non-specific file handle, listen should be performed first.");
                case FileHandleType.Tcp:
                    return AcceptTcp();
                case FileHandleType.Pipe:
                    return AcceptPipe();
                default:
                    throw new NotSupportedException();
            }
        }
        #endregion
    }
}
