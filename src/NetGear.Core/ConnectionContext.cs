// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;

namespace NetGear.Core
{
    public abstract class ConnectionContext
    {
        public abstract string ConnectionId { get; set; }

        public abstract IDictionary<object, object> Items { get; set; }

        public virtual void Abort(ConnectionAbortedException abortReason)
        {
            // We expect this to be overridden, but this helps maintain back compat
            // with implementations of ConnectionContext that predate the addition of
            // ConnectionContext.Abort()
        }

        public virtual void Abort() => Abort(new ConnectionAbortedException("The connection was aborted by the application."));
    }
}
