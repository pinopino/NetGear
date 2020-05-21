// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Net;

namespace NetGear.Core
{
    /// <summary>
    /// Describes either an <see cref="IPEndPoint"/>, Unix domain socket path, or a file descriptor for an already open
    /// socket that Kestrel should bind to or open.
    /// </summary>
    public class ListenOptions : IEndPointInformation
    {
        private FileHandleType _handleType;

        public ListenOptions(IPEndPoint endPoint)
        {
            Type = ListenType.IPEndPoint;
            IPEndPoint = endPoint;
        }

        public ListenOptions(string socketPath)
        {
            Type = ListenType.SocketPath;
            SocketPath = socketPath;
        }

        public ListenOptions(ulong fileHandle)
            : this(fileHandle, FileHandleType.Auto)
        { }

        public ListenOptions(ulong fileHandle, FileHandleType handleType)
        {
            Type = ListenType.FileHandle;
            FileHandle = fileHandle;
            switch (handleType)
            {
                case FileHandleType.Auto:
                case FileHandleType.Tcp:
                case FileHandleType.Pipe:
                    _handleType = handleType;
                    break;
                default:
                    throw new NotSupportedException();
            }
        }

        /// <summary>
        /// The type of interface being described: either an <see cref="IPEndPoint"/>, Unix domain socket path, or a file descriptor.
        /// </summary>
        public ListenType Type { get; }

        public FileHandleType HandleType
        {
            get => _handleType;
            set
            {
                if (value == _handleType)
                {
                    return;
                }
                if (Type != ListenType.FileHandle || _handleType != FileHandleType.Auto)
                {
                    throw new InvalidOperationException();
                }

                switch (value)
                {
                    case FileHandleType.Tcp:
                    case FileHandleType.Pipe:
                        _handleType = value;
                        break;
                    default:
                        throw new ArgumentException(nameof(HandleType));
                }
            }
        }

        // IPEndPoint is mutable so port 0 can be updated to the bound port.
        /// <summary>
        /// The <see cref="IPEndPoint"/> to bind to.
        /// Only set if the <see cref="ListenOptions"/> <see cref="Type"/> is <see cref="ListenType.IPEndPoint"/>.
        /// </summary>
        public IPEndPoint IPEndPoint { get; set; }

        /// <summary>
        /// The absolute path to a Unix domain socket to bind to.
        /// Only set if the <see cref="ListenOptions"/> <see cref="Type"/> is <see cref="ListenType.SocketPath"/>.
        /// </summary>
        public string SocketPath { get; }

        /// <summary>
        /// A file descriptor for the socket to open.
        /// Only set if the <see cref="ListenOptions"/> <see cref="Type"/> is <see cref="ListenType.FileHandle"/>.
        /// </summary>
        public ulong FileHandle { get; }

        /// <summary>
        /// Set to false to enable Nagle's algorithm for all connections.
        /// </summary>
        /// <remarks>
        /// Defaults to true.
        /// </remarks>
        public bool NoDelay { get; set; } = true;

        /// <summary>
        /// Gets the name of this endpoint to display on command-line when the web server starts.
        /// </summary>
        internal virtual string GetDisplayName()
        {
            var ishttps = false;
            var scheme = ishttps
                ? "https"
                : "http";

            switch (Type)
            {
                case ListenType.IPEndPoint:
                    return $"{scheme}://{IPEndPoint}";
                case ListenType.SocketPath:
                    return $"{scheme}://unix:{SocketPath}";
                case ListenType.FileHandle:
                    return $"{scheme}://<file handle>";
                default:
                    throw new InvalidOperationException();
            }
        }

        public override string ToString() => GetDisplayName();
    }
}
