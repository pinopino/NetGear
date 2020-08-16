using NetGear.Core.Common;
using NetGear.Core.Instrument;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public partial class SocketConnection
    {
        private long _totalBytesReceived;
        private SocketAwaitableEventArgs _readerArgs;

        /// <summary>
        /// The total number of bytes read from the socket
        /// </summary>
        public long BytesRead => Interlocked.Read(ref _totalBytesReceived);

        /// <summary>
        /// The number of bytes received in the last read
        /// </summary>
        public int LastReceived { private set; get; }

        private async Task DoReceiveAsync()
        {
            Exception error = null;
            DebugLog("starting receive loop");
            try
            {
                // 说明：recv方向上是recv from socket and push to pipe，所以这里回调应该是
                // 执行在WriterScheduler上
                _readerArgs = new SocketAwaitableEventArgs(InlineReads ? null : _receiveOptions.WriterScheduler);
                while (true)
                {
                    if (ZeroLengthReads && Socket.Available == 0)
                    {
                        DebugLog($"awaiting zero-length receive...");

                        CounterHelper.Incr(Counter.OpenReceiveReadAsync);
                        DoReceive(Socket, _readerArgs, default, Name);
                        CounterHelper.Incr(_readerArgs.IsCompleted ? Counter.SocketZeroLengthReceiveSync : Counter.SocketZeroLengthReceiveAsync);
                        await _readerArgs;
                        CounterHelper.Decr(Counter.OpenReceiveReadAsync);
                        DebugLog($"zero-length receive complete; now {Socket.Available} bytes available");

                        // this *could* be because data is now available, or it *could* be because of
                        // the EOF; we can't really trust Available, so now we need to do a non-empty
                        // read to find out which
                    }

                    var buffer = _receiveFromSocket.Writer.GetMemory(1);
                    DebugLog($"leased {buffer.Length} bytes from pipe");
                    try
                    {
                        DebugLog($"initiating socket receive...");
                        CounterHelper.Incr(Counter.OpenReceiveReadAsync);

                        DoReceive(Socket, _readerArgs, buffer, Name);
                        CounterHelper.Incr(_readerArgs.IsCompleted ? Counter.SocketReceiveSync : Counter.SocketReceiveAsync);
                        DebugLog(_readerArgs.IsCompleted ? "receive is sync" : "receive is async");
                        var bytesReceived = await _readerArgs;
                        LastReceived = bytesReceived;
                        CounterHelper.Decr(Counter.OpenReceiveReadAsync);

                        Debug.Assert(bytesReceived == _readerArgs.BytesTransferred);
                        DebugLog($"received {bytesReceived} bytes ({_readerArgs.BytesTransferred}, {_readerArgs.SocketError})");

                        if (bytesReceived <= 0)
                        {
                            _receiveFromSocket.Writer.Advance(0);
                            TrySetShutdown(PipeShutdownKind.ReadEndOfStream);
                            break;
                        }

                        _receiveFromSocket.Writer.Advance(bytesReceived);
                        Interlocked.Add(ref _totalBytesReceived, bytesReceived);
                    }
                    finally
                    {
                        // commit?
                    }

                    DebugLog("flushing pipe");
                    CounterHelper.Incr(Counter.OpenReceiveFlushAsync);
                    var flushTask = _receiveFromSocket.Writer.FlushAsync();
                    CounterHelper.Incr(flushTask.IsCompleted ? Counter.SocketPipeFlushSync : Counter.SocketPipeFlushAsync);

                    FlushResult result;
                    if (flushTask.IsCompletedSuccessfully)
                    {
                        result = flushTask.Result;
                        DebugLog("pipe flushed (sync)");
                    }
                    else
                    {
                        result = await flushTask;
                        DebugLog("pipe flushed (async)");
                    }
                    CounterHelper.Decr(Counter.OpenReceiveFlushAsync);

                    if (result.IsCompleted)
                    {
                        TrySetShutdown(PipeShutdownKind.ReadFlushCompleted);
                        break;
                    }
                    if (result.IsCanceled)
                    {
                        TrySetShutdown(PipeShutdownKind.ReadFlushCanceled);
                        break;
                    }
                }
            }
            catch (SocketException ex) when (IsConnectionResetError(ex.SocketErrorCode))
            {
                TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = new ConnectionResetException(ex.Message, ex);
            }
            catch (SocketException ex) when (IsConnectionAbortError(ex.SocketErrorCode))
            {
                TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                if (!_socketDisposed)
                {
                    // Calling Dispose after ReceiveAsync can cause an "InvalidArgument" error on *nix.
                    error = new ConnectionAbortedException();
                }
            }
            catch (SocketException ex)
            {
                TrySetShutdown(PipeShutdownKind.ReadSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = ex;
            }
            catch (ObjectDisposedException)
            {
                TrySetShutdown(PipeShutdownKind.ReadDisposed);
                DebugLog($"fail: disposed");
                if (!_socketDisposed)
                {
                    error = new ConnectionAbortedException();
                }
            }
            catch (IOException ex)
            {
                TrySetShutdown(PipeShutdownKind.ReadIOException);
                DebugLog($"fail - io: {ex.Message}");
                error = ex;
            }
            catch (Exception ex)
            {
                TrySetShutdown(PipeShutdownKind.ReadException);
                DebugLog($"fail: {ex.Message}");
                error = new IOException(ex.Message, ex);
            }
            finally
            {
                Shutdown(error);
                error = error ?? _shutdownReason;

                // close the *writer* half of the receive pipe; we won't
                // be writing any more, but callers can still drain the
                // pipe if they choose
                DebugLog($"marking {nameof(Input)} as complete");
                try { _receiveFromSocket.Writer.Complete(error); } catch { }
                TrySetShutdown(error, PipeShutdownKind.InputWriterCompleted);

                var args = _readerArgs;
                _readerArgs = null;
                if (args != null) try { args.Dispose(); } catch { }
            }

            DebugLog(error == null ? "exiting with success" : $"exiting with failure: {error.Message}");
        }

        private static void DoReceive(Socket socket, SocketAwaitableEventArgs args, Memory<byte> buffer, string name)
        {
            if (buffer.IsEmpty)
            {
                // zero-length; retain existing buffer if possible
                if (args.Buffer == null)
                {
                    args.SetBuffer(Array.Empty<byte>(), 0, 0);
                }
                else
                {   // it doesn't matter that the old buffer may still be in
                    // use somewhere; we're reading zero bytes!
                    args.SetBuffer(args.Offset, 0);
                }
            }
            else
            {
                var segment = buffer.GetArray();
                args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            }
            Common.Debugger.Log(name, $"## {nameof(socket.ReceiveAsync)} <={buffer.Length}");

            if (!socket.ReceiveAsync(args))
                args.Complete();
        }
    }
}