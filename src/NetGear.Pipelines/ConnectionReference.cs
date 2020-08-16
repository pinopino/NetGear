// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using NetGear.Core;
using System;

namespace NetGear.Pipelines.Server
{
    public class ClientReference
    {
        private readonly WeakReference<TransportConnection> _weakReference;

        public ClientReference(TransportConnection connection)
        {
            _weakReference = new WeakReference<TransportConnection>(connection);
            ConnectionId = connection.ConnectionId;
        }

        public string ConnectionId { get; }

        public bool TryGetConnection(out TransportConnection connection)
        {
            return _weakReference.TryGetTarget(out connection);
        }
    }
}
