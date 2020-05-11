using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public class UvTcpConnection : IDuplexPipe, IDisposable
    {
        private sealed class WrappedReader : PipeReader
        {
            private readonly PipeReader _reader;
            private readonly UvTcpConnection _connection;

            public WrappedReader(PipeReader reader, UvTcpConnection connection)
            {
                _reader = reader;
                _connection = connection;
            }

            public override void Complete(Exception exception = null)
            {
                _connection.InputReaderCompleted(exception);
                _reader.Complete(exception);
            }

            public override void AdvanceTo(SequencePosition consumed)
                => _reader.AdvanceTo(consumed);

            public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
                => _reader.AdvanceTo(consumed, examined);

            public override void CancelPendingRead()
                => _reader.CancelPendingRead();

            public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
                => _reader.ReadAsync(cancellationToken);

            public override bool TryRead(out ReadResult result)
                => _reader.TryRead(out result);

            // note - consider deprecated: https://github.com/dotnet/corefx/issues/38362
            [Obsolete]
            public override void OnWriterCompleted(Action<Exception, object> callback, object state)
                => _reader.OnWriterCompleted(callback, state);
        }

        private sealed class WrappedWriter : PipeWriter
        {
            private readonly PipeWriter _writer;
            private readonly UvTcpConnection _connection;

            public WrappedWriter(PipeWriter writer, UvTcpConnection connection)
            {
                _writer = writer;
                _connection = connection;
            }

            public override void Complete(Exception exception = null)
            {
                _connection.OutputWriterCompleted(exception);
                _writer.Complete(exception);
            }

            public override void Advance(int bytes)
                => _writer.Advance(bytes);

            public override void CancelPendingFlush()
                => _writer.CancelPendingFlush();

            public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
                => _writer.FlushAsync(cancellationToken);

            public override Memory<byte> GetMemory(int sizeHint = 0)
                => _writer.GetMemory(sizeHint);

            public override Span<byte> GetSpan(int sizeHint = 0)
                => _writer.GetSpan(sizeHint);

            public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
                => _writer.WriteAsync(source, cancellationToken);

            // note - consider deprecated: https://github.com/dotnet/corefx/issues/38362
            [Obsolete]
            public override void OnReaderCompleted(Action<Exception, object> callback, object state)
                => _writer.OnReaderCompleted(callback, state);
        }

        private void InputReaderCompleted(Exception exception)
        {
            throw new NotImplementedException();
        }

        private void OutputWriterCompleted(Exception exception)
        {
            throw new NotImplementedException();
        }

        private const int EOF = -4095;

        private static readonly Action<UvStreamHandle, int, object> _readCallback = ReadCallback;
        private static readonly Func<UvStreamHandle, int, object, Uv.uv_buf_t> _allocCallback = AllocCallback;
        private static readonly Action<UvWriteReq, int, object> _writeCallback = WriteCallback;

        private readonly Pipe _sendToUV, _receiveFromUV;
        private readonly PipeReader _input;
        private readonly PipeWriter _output;
        private readonly UvThread _thread;
        private readonly UvTcpHandle _handle;
        private int _pendingWrites;
        private TaskCompletionSource<object> _drainWrites;
        private Task _sendingTask;

        public UvTcpConnection(UvThread thread, UvTcpHandle handle)
        {
            _thread = thread;
            _handle = handle;

            _input = new WrappedReader(_receiveFromUV.Reader, this);
            _output = new WrappedWriter(_sendToUV.Writer, this);

            StartReading();
            _sendingTask = ProcessWrites();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        { }

        public PipeReader Input => this._input;

        public PipeWriter Output => this._output;

        private async Task ProcessWrites()
        {
            Exception error = null;
            try
            {
                while (true)
                {
                    var result = await _sendToUV.Reader.ReadAsync();
                    var buffer = result.Buffer;

                    try
                    {
                        // Make sure we're on the libuv thread
                        await _thread;

                        if (buffer.IsEmpty && result.IsCompleted)
                        {
                            break;
                        }

                        if (!buffer.IsEmpty)
                        {
                            BeginWrite(buffer);
                        }
                    }
                    finally
                    {
                        _sendToUV.Reader.AdvanceTo(buffer.End);
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                _sendToUV.Reader.Complete(error);
                _sendToUV.Writer.Complete(error);

                // Drain the pending writes
                if (_pendingWrites > 0)
                {
                    _drainWrites = new TaskCompletionSource<object>();
                    await _drainWrites.Task;
                }

                _handle.Dispose();
            }
        }

        private void BeginWrite(ReadOnlySequence<byte> buffer)
        {
            var writeReq = _thread.WriteReqPool.Allocate();

            _pendingWrites++;

            writeReq.Write(_handle, buffer, _writeCallback, this);
        }

        private static void WriteCallback(UvWriteReq writeReq, int status, object state)
        {
            ((UvTcpConnection)state).EndWrite(writeReq);
        }

        private void EndWrite(UvWriteReq writeReq)
        {
            _pendingWrites--;

            _thread.WriteReqPool.Return(writeReq);

            if (_drainWrites != null)
            {
                if (_pendingWrites == 0)
                {
                    _drainWrites.TrySetResult(null);
                }
            }
        }

        private void StartReading()
        {
            _handle.ReadStart(_allocCallback, _readCallback, this);
        }

        private static void ReadCallback(UvStreamHandle handle, int status, object state)
        {
            ((UvTcpConnection)state).OnRead(handle, status);
        }

        private void OnRead(UvStreamHandle handle, int status)
        {
            if (status == 0)
            {
                // A zero status does not indicate an error or connection end. It indicates
                // there is no data to be read right now.
                // See the note at http://docs.libuv.org/en/v1.x/stream.html#c.uv_read_cb.
                _receiveFromUV.Writer.Advance(0);
                Task.Run(() => StartReading());
                return;
            }

            var normalRead = status > 0;
            var normalDone = status == EOF;
            var errorDone = !(normalDone || normalRead);
            var readCount = normalRead ? status : 0;

            if (!normalRead)
            {
                handle.ReadStop();
            }

            IOException error = null;
            if (errorDone)
            {
                Exception uvError;
                handle.Libuv.Check(status, out uvError);
                error = new IOException(uvError.Message, uvError);

                // REVIEW: Should we treat ECONNRESET as an error?
                // Ignore the error for now 
                _receiveFromUV.Writer.Complete(error);
            }
            else
            {
                _receiveFromUV.Writer.Advance(readCount);

                var task = _receiveFromUV.Writer.FlushAsync();

                if (!task.IsCompleted)
                {
                    // If there's back pressure
                    handle.ReadStop();

                    // Resume reading when task continues
                    task.AsTask().ContinueWith((t, state) => ((UvTcpConnection)state).StartReading(), this);
                }
            }
        }

        private static Uv.uv_buf_t AllocCallback(UvStreamHandle handle, int status, object state)
        {
            return ((UvTcpConnection)state).OnAlloc(handle, status);
        }

        private unsafe Uv.uv_buf_t OnAlloc(UvStreamHandle handle, int status)
        {
            var _inputBuffer = _receiveFromUV.Writer.GetMemory(1);
            void* pointer = _inputBuffer.Pin().Pointer;

            return handle.Libuv.buf_init((IntPtr)pointer, _inputBuffer.Length);
        }
    }
}
