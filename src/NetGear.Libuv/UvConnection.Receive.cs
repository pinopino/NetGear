using System;
using System.IO.Pipelines;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public partial class UvConnection
    {
        private void StartReading()
        {
            try
            {
                _socket.ReadStart(_allocCallback, _readCallback, this);
            }
            catch (UvException ex)
            {
                // ReadStart() can throw a UvException in some cases (e.g. socket is no longer connected).
                // This should be treated the same as OnRead() seeing a negative status.
                _receiveFromUV.Writer.Complete(LogAndWrapReadError(ex));
            }
        }

        // Called on Libuv thread
        private static Uv.uv_buf_t AllocCallback(UvStreamHandle handle, int suggestedSize, object state)
        {
            return ((UvConnection)state).OnAlloc(handle, suggestedSize);
        }

        private unsafe Uv.uv_buf_t OnAlloc(UvStreamHandle handle, int suggestedSize)
        {
            var currentWritableBuffer = _receiveFromUV.Writer.GetMemory(MinAllocBufferSize);
            _bufferHandle = currentWritableBuffer.Pin();

            return handle.Libuv.buf_init((IntPtr)_bufferHandle.Pointer, currentWritableBuffer.Length);
        }

        private static void ReadCallback(UvStreamHandle handle, int status, object state)
        {
            ((UvConnection)state).OnRead(handle, status);
        }

        private void OnRead(UvStreamHandle handle, int status)
        {
            // Cleanup state from last OnAlloc. This is safe even if OnAlloc wasn't called.
            _bufferHandle.Dispose();
            if (status == 0)
            {
                // EAGAIN/EWOULDBLOCK so just return the buffer.
                // http://docs.libuv.org/en/v1.x/stream.html#c.uv_read_cb
            }
            else if (status > 0)
            {
                Log.ConnectionRead(ConnectionId, status);

                // 说明：这里马上就调用flush了，实际可以压缩几次advance再批量flush
                _receiveFromUV.Writer.Advance(status);
                var flushTask = _receiveFromUV.Writer.FlushAsync();

                if (!flushTask.IsCompleted)
                {
                    // We wrote too many bytes to the reader, so pause reading and resume when
                    // we hit the low water mark.
                    _ = ApplyBackpressureAsync(flushTask);
                }
            }
            else
            {
                // Given a negative status, it's possible that OnAlloc wasn't called.
                _socket.ReadStop(); // 说明：先停下来

                Exception error = null;

                if (status == UvConstants.EOF)
                {
                    Log.ConnectionReadFin(ConnectionId);
                }
                else
                {
                    handle.Libuv.Check(status, out var uvError);
                    error = LogAndWrapReadError(uvError);
                }

                // Complete after aborting the connection
                // 标记写入端完成
                _receiveFromUV.Writer.Complete(error);
            }
        }

        private async Task ApplyBackpressureAsync(ValueTask<FlushResult> flushTask)
        {
            Log.ConnectionPause(ConnectionId);
            _socket.ReadStop(); // 说明：先停下来

            var result = await flushTask; // 开始等待flush

            // 再次起来的时候得先看看reader是不是已经结束或者cancel了
            // If the reader isn't complete or cancelled then resume reading
            if (!result.IsCompleted && !result.IsCanceled)
            {
                Log.ConnectionResume(ConnectionId);
                StartReading();
            }
        }
    }
}
