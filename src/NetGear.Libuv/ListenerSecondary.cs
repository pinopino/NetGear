// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;
using NetGear.Core;
using System;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    /// <summary>
    /// A secondary listener is delegated requests from a primary listener via a named pipe or
    /// UNIX domain socket.
    /// </summary>
    public class ListenerSecondary : IAsyncDisposable
    {
        private string _pipeName;
        private byte[] _pipeMessage;
        private IntPtr _ptr;
        private Uv.uv_buf_t _buf;
        private bool _closed;
        private UvThread _thread;

        public ListenerSecondary(UvThread thread)
        {
            _ptr = Marshal.AllocHGlobal(4);
        }

        UvPipeHandle DispatchPipe { get; set; }

        public ILibuvTrace Log => throw new NotImplementedException();

        public Task StartAsync(
            string pipeName,
            byte[] pipeMessage)
        {
            _pipeName = pipeName;
            _pipeMessage = pipeMessage;
            _buf = _thread.Loop.Libuv.buf_init(_ptr, 4);

            DispatchPipe = new UvPipeHandle(Log);

            var tcs = new TaskCompletionSource<int>(this, TaskCreationOptions.RunContinuationsAsynchronously);
            _thread.Post(StartCallback, tcs);
            return tcs.Task;
        }

        private static void StartCallback(TaskCompletionSource<int> tcs)
        {
            var listener = (ListenerSecondary)tcs.Task.AsyncState;
            listener.StartedCallback(tcs);
        }

        private void StartedCallback(TaskCompletionSource<int> tcs)
        {
            var connect = new UvConnectRequest(Log);
            try
            {
                DispatchPipe.Init(_thread.Loop, _thread.QueueCloseHandle, true);
                connect.Init(_thread);
                connect.Connect(
                    DispatchPipe,
                    _pipeName,
                    (connect2, status, error, state) => ConnectCallback(connect2, status, error, (TaskCompletionSource<int>)state),
                    tcs);
            }
            catch (Exception ex)
            {
                DispatchPipe.Dispose();
                connect.Dispose();
                tcs.SetException(ex);
            }
        }

        private static void ConnectCallback(UvConnectRequest connect, int status, UvException error, TaskCompletionSource<int> tcs)
        {
            var listener = (ListenerSecondary)tcs.Task.AsyncState;
            _ = listener.ConnectedCallback(connect, status, error, tcs);
        }

        private async Task ConnectedCallback(UvConnectRequest connect, int status, UvException error, TaskCompletionSource<int> tcs)
        {
            connect.Dispose();
            if (error != null)
            {
                tcs.SetException(error);
                return;
            }

            var writeReq = new UvWriteReq(Log);

            try
            {
                DispatchPipe.ReadStart(
                    (handle, status2, state) => ((ListenerSecondary)state)._buf,
                    (handle, status2, state) => ((ListenerSecondary)state).ReadStartCallback(handle, status2),
                    this);

                writeReq.Init(_thread);
                var result = await writeReq.WriteAsync(
                     DispatchPipe,
                     new ArraySegment<ArraySegment<byte>>(new[] { new ArraySegment<byte>(_pipeMessage) }));

                if (result.Error != null)
                {
                    tcs.SetException(result.Error);
                }
                else
                {
                    tcs.SetResult(0);
                }
            }
            catch (Exception ex)
            {
                DispatchPipe.Dispose();
                tcs.SetException(ex);
            }
            finally
            {
                writeReq.Dispose();
            }
        }

        private void ReadStartCallback(UvStreamHandle handle, int status)
        {
            if (status < 0)
            {
                if (status != UvConstants.EOF)
                {
                    _thread.Loop.Libuv.Check(status, out var ex);
                    Log.LogError(0, ex, "DispatchPipe.ReadStart");
                }

                DispatchPipe.Dispose();
                return;
            }

            if (_closed || DispatchPipe.PendingCount() == 0)
            {
                return;
            }

            var acceptSocket = CreateAcceptSocket();

            try
            {
                DispatchPipe.Accept(acceptSocket);

                // REVIEW: This task should be tracked by the server for graceful shutdown
                // Today it's handled specifically for http but not for aribitrary middleware
                _ = HandleConnectionAsync(acceptSocket);
            }
            catch (UvException ex) when (UvConstants.IsConnectionReset(ex.StatusCode))
            {
                Log.ConnectionReset("(null)");
                acceptSocket.Dispose();
            }
            catch (UvException ex)
            {
                Log.LogError(0, ex, "DispatchPipe.Accept");
                acceptSocket.Dispose();
            }
        }

        private UvTcpHandle CreateAcceptSocket()
        {
            var socket = new UvTcpHandle(Log);

            try
            {
                socket.Init(_thread.Loop, _thread.QueueCloseHandle);
                socket.NoDelay(true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }

            return socket;
        }

        private async Task HandleConnectionAsync(UvTcpHandle socket)
        {
            try
            {
                IPEndPoint remoteEndPoint = null;
                IPEndPoint localEndPoint = null;

                try
                {
                    remoteEndPoint = socket.GetPeerIPEndPoint();
                    localEndPoint = socket.GetSockIPEndPoint();
                }
                catch (UvException ex) when (UvConstants.IsConnectionReset(ex.StatusCode))
                {
                    Log.ConnectionReset("(null)");
                    socket.Dispose();
                    return;
                }

                var connection = new LibuvConnection(socket, Log, _thread, remoteEndPoint, localEndPoint);
                await connection.Start();

                connection.Dispose();
            }
            catch (Exception ex)
            {
                Log.LogCritical(ex, $"Unexpected exception in {nameof(UvTcpListener)}.{nameof(HandleConnectionAsync)}.");
            }
        }

        private void FreeBuffer()
        {
            var ptr = Interlocked.Exchange(ref _ptr, IntPtr.Zero);
            if (ptr != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(ptr);
            }
        }

        public async Task DisposeAsync()
        {
            // Ensure the event loop is still running.
            // If the event loop isn't running and we try to wait on this Post
            // to complete, then LibuvTransport will never be disposed and
            // the exception that stopped the event loop will never be surfaced.
            if (_thread.FatalError == null)
            {
                await _thread.PostAsync(listener =>
                {
                    listener.DispatchPipe.Dispose();
                    listener.FreeBuffer();

                    listener._closed = true;

                }, this).ConfigureAwait(false);
            }
            else
            {
                FreeBuffer();
            }
        }
    }
}
