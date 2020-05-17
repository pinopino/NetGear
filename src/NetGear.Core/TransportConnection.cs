// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net;
using System.Threading;

namespace NetGear.Core
{
    public abstract partial class TransportConnection : ConnectionContext, IDuplexPipe
    {
        private IDictionary<object, object> _items;

        public TransportConnection()
        { }

        public override string ConnectionId { get; set; }
        public virtual long TotalBytesWritten { get; }
        public virtual MemoryPool<byte> MemoryPool { get; }

        public IPAddress RemoteAddress { get; set; }
        public int RemotePort { get; set; }
        public IPAddress LocalAddress { get; set; }
        public int LocalPort { get; set; }

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

        // DO NOT remove this override to ConnectionContext.Abort. Doing so would cause
        // any TransportConnection that does not override Abort or calls base.Abort
        // to stack overflow when IConnectionLifetimeFeature.Abort() is called.
        // That said, all derived types should override this method should override
        // this implementation of Abort because canceling pending output reads is not
        // sufficient to abort the connection if there is backpressure.
        public override void Abort(ConnectionAbortedException abortReason)
        {
            Input.CancelPendingRead();
        }
    }
}
