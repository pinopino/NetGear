using System;
using System.Collections.Concurrent;
using System.Threading;

namespace NetGear.Pipelines
{
    public partial class DuplexPipeServer
    {
        #region ClientConnection Management
        protected class ClientReference
        {
            private readonly WeakReference<Client> _weakReference;

            public ClientReference(Client client)
            {
                _weakReference = new WeakReference<Client>(client);
                ConnectionId = client.Connection.ConnectionId;
            }

            public string ConnectionId { get; }

            public bool TryGetClient(out Client client)
            {
                return _weakReference.TryGetTarget(out client);
            }
        }

        private DateTimeOffset _now;
        private long _nowTicks;
        private static long _lastConnectionId = long.MinValue;
        private readonly ConcurrentDictionary<long, ClientReference> _clientReferences;

        public DateTimeOffset UtcNow => new DateTimeOffset(UtcNowTicks, TimeSpan.Zero);

        public long UtcNowTicks => Volatile.Read(ref _nowTicks);

        public DateTimeOffset UtcNowUnsynchronized => _now;

        public int ClientsCount
        {
            get
            {
                if (_disposed)
                    throw new ObjectDisposedException(ToString());

                return _clientReferences.Count;
            }
        }

        protected void AddClient(long id, Client client)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            if (!_clientReferences.TryAdd(id, new ClientReference(client)))
            {
                throw new ArgumentException(nameof(id));
            }
        }

        protected void RemoveClient(long id)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            if (!_clientReferences.TryRemove(id, out _))
            {
                throw new ArgumentException(nameof(id));
            }
        }

        protected void Walk(Action<Client> callback)
        {
            if (_disposed)
                throw new ObjectDisposedException(ToString());

            foreach (var kvp in _clientReferences)
            {
                var reference = kvp.Value;

                if (reference.TryGetClient(out var client))
                {
                    callback(client);
                }
                else if (_clientReferences.TryRemove(kvp.Key, out reference))
                {
                    // 弱引用挂了
                    // It's safe to modify the ConcurrentDictionary in the foreach.
                    // The connection reference has become unrooted because the application never completed.
                    //_trace.ApplicationNeverCompleted(reference.ConnectionId);
                }

                // If both conditions are false, the connection was removed during the heartbeat.
            }
        }

        public void OnHeartbeat(DateTimeOffset now)
        {
            _now = now;
            Volatile.Write(ref _nowTicks, now.Ticks);

            Walk(HeartbeatCallback);
        }

        private void HeartbeatCallback(Client client)
        {
            client.Connection.TickHeartbeat();
        }
        #endregion
    }
}
