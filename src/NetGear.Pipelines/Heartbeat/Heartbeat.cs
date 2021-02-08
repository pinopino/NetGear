﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace NetGear.Pipelines
{
    public class Heartbeat : IDisposable
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

        private readonly IHeartbeatHandler[] _callbacks;
        private readonly TimeSpan _interval;
        private Timer _timer;
        private int _executingOnHeartbeat;

        public Heartbeat(IHeartbeatHandler[] callbacks)
        {
            _callbacks = callbacks;
            _interval = Interval;
        }

        public void Start()
        {
            OnHeartbeat();
            _timer = new Timer(OnHeartbeat, state: this, dueTime: _interval, period: _interval);
        }

        private static void OnHeartbeat(object state)
        {
            ((Heartbeat)state).OnHeartbeat();
        }

        // Called by the Timer (background) thread
        internal void OnHeartbeat()
        {
            var now = DateTimeOffset.UtcNow;

            if (Interlocked.Exchange(ref _executingOnHeartbeat, 1) == 0)
            {
                try
                {
                    foreach (var callback in _callbacks)
                    {
                        callback.OnHeartbeat(now);
                    }
                }
                catch (Exception ex)
                {
                    //_trace.LogError(0, ex, $"{nameof(Heartbeat)}.{nameof(OnHeartbeat)}");
                }
                finally
                {
                    Interlocked.Exchange(ref _executingOnHeartbeat, 0);
                }
            }
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}