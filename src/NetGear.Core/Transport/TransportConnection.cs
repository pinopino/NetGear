// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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
        private readonly object _heartbeatLock = new object();
        private List<(Action<object> handler, object state)> _heartbeatHandlers;

        public TransportConnection()
        { }

        public override string ConnectionId { get; set; }
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
        
        public int RemotePort { get; set; }
        public IPAddress RemoteAddress { get; set; }
        public int LocalPort { get; set; }
        public IPAddress LocalAddress { get; set; }

        public virtual PipeReader Input { get; }
        public virtual PipeWriter Output { get; }

        public virtual MemoryPool<byte> MemoryPool => throw new NotImplementedException();
        public CancellationToken ConnectionClosed { get; set; }

        public void TickHeartbeat()
        {
            lock (_heartbeatLock)
            {
                if (_heartbeatHandlers == null)
                {
                    return;
                }

                foreach (var (handler, state) in _heartbeatHandlers)
                {
                    handler(state);
                }
            }
        }

        public void OnHeartbeat(Action<object> action, object state)
        {
            lock (_heartbeatLock)
            {
                if (_heartbeatHandlers == null)
                {
                    _heartbeatHandlers = new List<(Action<object> handler, object state)>();
                }

                _heartbeatHandlers.Add((action, state));
            }
        }

        public virtual void Abort(ConnectionAbortedException abortReason)
        {
            // We expect this to be overridden, but this helps maintain back compat
            // with implementations of ConnectionContext that predate the addition of
            // ConnectionContext.Abort()
        }

        public virtual void Abort() => Abort(new ConnectionAbortedException("The connection was aborted by the application."));
    }
}
