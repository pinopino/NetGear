// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading;

namespace NetGear.Core
{
    public abstract class TransportConnection : ConnectionContext
    {
        private IDictionary<object, object> _items;

        public TransportConnection()
        { }

        public override string ConnectionId { get; set; }
        public virtual MemoryPool<byte> MemoryPool { get; }

        public int RemotePort { get; set; }
        public IPAddress RemoteAddress { get; set; }
        public int LocalPort { get; set; }
        public IPAddress LocalAddress { get; set; }

        public override IDictionary<object, object> Items
        {
            get
            {
                // Lazily allocate connection metadata
                return _items ?? (_items = new ConnectionItems());
            }
            set
            {
                _items = value;
            }
        }

        public CancellationToken ConnectionClosed { get; set; }

        public virtual PipeReader Input { get; }

        public virtual PipeWriter Output { get; }

        public virtual void Abort(ConnectionAbortedException abortReason)
        {
            // We expect this to be overridden, but this helps maintain back compat
            // with implementations of ConnectionContext that predate the addition of
            // ConnectionContext.Abort()
        }

        public virtual void Abort() => Abort(new ConnectionAbortedException("The connection was aborted by the application."));
    }
}
