// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace NetGear.Libuv
{
    public class UvPipeHandle : UvStreamHandle
    {
        public UvPipeHandle(ILibuvTrace logger)
            : base(logger)
        { }

        public void Init(UvLoopHandle loop, Action<Action<IntPtr>, IntPtr> queueCloseHandle, bool ipc = false)
        {
            CreateHandle(
                loop.Libuv,
                loop.ThreadId,
                loop.Libuv.handle_size(Uv.HandleType.NAMED_PIPE), queueCloseHandle);

            _uv.pipe_init(loop, this, ipc);
        }

        public void Open(IntPtr fileDescriptor)
        {
            _uv.pipe_open(this, fileDescriptor);
        }

        public void Bind(string name)
        {
            _uv.pipe_bind(this, name);
        }

        public int PendingCount()
        {
            return _uv.pipe_pending_count(this);
        }
    }
}
