// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace NetGear.Libuv
{
    public class UvConnectRequest : UvRequest
    {
        private readonly static Uv.uv_connect_cb _uv_connect_cb = (req, status) => UvConnectCb(req, status);

        private Action<UvConnectRequest, int, UvException, object> _callback;
        private object _state;

        public UvConnectRequest(ILibuvTrace logger)
            : base(logger)
        { }

        public override void Init(UvThread thread)
        {
            DangerousInit(thread.Loop);

            base.Init(thread);
        }

        public void DangerousInit(UvLoopHandle loop)
        {
            var requestSize = loop.Libuv.req_size(Uv.RequestType.CONNECT);
            CreateMemory(
                loop.Libuv,
                loop.ThreadId,
                requestSize);
        }

        public void Connect(
            UvTcpHandle socket,
            IPEndPoint endpoint,
            Action<UvConnectRequest, int, UvException, object> callback,
            object state)
        {
            _callback = callback;
            _state = state;

            SockAddr addr;
            var addressText = endpoint.Address.ToString();

            UvException error1;
            _uv.ip4_addr(addressText, endpoint.Port, out addr, out error1);

            if (error1 != null)
            {
                UvException error2;
                _uv.ip6_addr(addressText, endpoint.Port, out addr, out error2);
                if (error2 != null)
                {
                    throw error1;
                }
            }

            Libuv.tcp_connect(this, socket, ref addr, _uv_connect_cb);
        }

        public void Connect(
            UvPipeHandle pipe,
            string name,
            Action<UvConnectRequest, int, UvException, object> callback,
            object state)
        {
            _callback = callback;
            _state = state;

            Libuv.pipe_connect(this, pipe, name, _uv_connect_cb);
        }

        private static void UvConnectCb(IntPtr ptr, int status)
        {
            var req = FromIntPtr<UvConnectRequest>(ptr);

            var callback = req._callback;
            req._callback = null;

            var state = req._state;
            req._state = null;

            UvException error = null;
            if (status < 0)
            {
                req.Libuv.Check(status, out error);
            }

            try
            {
                callback(req, status, error, state);
            }
            catch (Exception ex)
            {
                req._log.LogError(0, ex, "UvConnectRequest");
                throw;
            }
        }
    }
}
