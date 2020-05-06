using NetGear.Core.Common;
using NetGear.Core.Instrument;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public partial class SocketConnection
    {
        private long _totalBytesSent;
        private SocketAwaitableEventArgs _writerArgs;

        /// <summary>
        /// The total number of bytes sent to the socket
        /// </summary>
        public long BytesSent => Interlocked.Read(ref _totalBytesSent);

        long IMeasuredDuplexPipe.TotalBytesSent => BytesSent;

        private async Task DoSendAsync()
        {
            Exception error = null;
            DebugLog("starting send loop");
            try
            {
                while (true)
                {
                    DebugLog("awaiting data from pipe...");
                    if (_sendToSocket.Reader.TryRead(out var result))
                    {
                        CounterHelper.Incr(Counter.SocketPipeReadReadSync);
                    }
                    else
                    {
                        CounterHelper.Incr(Counter.OpenSendReadAsync);
                        var read = _sendToSocket.Reader.ReadAsync();
                        CounterHelper.Incr(read.IsCompleted ? Counter.SocketPipeReadReadSync : Counter.SocketPipeReadReadAsync);
                        result = await read;
                        CounterHelper.Decr(Counter.OpenSendReadAsync);
                    }
                    var buffer = result.Buffer;

                    if (result.IsCanceled || (result.IsCompleted && buffer.IsEmpty))
                    {
                        DebugLog(result.IsCanceled ? "cancelled" : "complete");
                        break;
                    }

                    try
                    {
                        if (!buffer.IsEmpty)
                        {
                            if (_writerArgs == null) 
                                _writerArgs = new SocketAwaitableEventArgs(InlineWrites ? null : _sendOptions.ReaderScheduler);
                            
                            DebugLog($"sending {buffer.Length} bytes over socket...");
                            CounterHelper.Incr(Counter.OpenSendWriteAsync);
                            DoSend(Socket, _writerArgs, buffer, Name);
                            CounterHelper.Incr(_writerArgs.IsCompleted ? Counter.SocketSendAsyncSync : Counter.SocketSendAsyncAsync);
                            Interlocked.Add(ref _totalBytesSent, await _writerArgs);
                            CounterHelper.Decr(Counter.OpenSendWriteAsync);
                        }
                        else if (result.IsCompleted)
                        {
                            DebugLog("completed");
                            break;
                        }
                    }
                    finally
                    {
                        DebugLog("advancing");
                        _sendToSocket.Reader.AdvanceTo(buffer.End);
                    }
                }
                TrySetShutdown(PipeShutdownKind.WriteEndOfStream);
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted)
            {
                TrySetShutdown(PipeShutdownKind.WriteSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = null;
            }
            catch (SocketException ex)
            {
                TrySetShutdown(PipeShutdownKind.WriteSocketError, ex.SocketErrorCode);
                DebugLog($"fail: {ex.SocketErrorCode}");
                error = ex;
            }
            catch (ObjectDisposedException)
            {
                TrySetShutdown(PipeShutdownKind.WriteDisposed);
                DebugLog("fail: disposed");
                error = null;
            }
            catch (IOException ex)
            {
                TrySetShutdown(PipeShutdownKind.WriteIOException);
                DebugLog($"fail - io: {ex.Message}");
                error = ex;
            }
            catch (Exception ex)
            {
                TrySetShutdown(PipeShutdownKind.WriteException);
                DebugLog($"fail: {ex.Message}");
                error = new IOException(ex.Message, ex);
            }
            finally
            {
                // Make sure to close the connection only after the _aborted flag is set.
                // Without this, the RequestsCanBeAbortedMidRead test will sometimes fail when
                // a BadHttpRequestException is thrown instead of a TaskCanceledException.
                _sendAborted = true;
                try
                {
                    DebugLog($"shutting down socket-send");
                    Socket.Shutdown(SocketShutdown.Send);
                }
                catch { }

                // close *both halves* of the send pipe; we're not
                // listening *and* we don't want anyone trying to write
                DebugLog($"marking {nameof(Output)} as complete");
                try { _sendToSocket.Writer.Complete(error); } catch { }
                try { _sendToSocket.Reader.Complete(error); } catch { }
                TrySetShutdown(error, this, PipeShutdownKind.OutputReaderCompleted);

                var args = _writerArgs;
                _writerArgs = null;
                if (args != null) try { args.Dispose(); } catch { }
            }
            DebugLog(error == null ? "exiting with success" : $"exiting with failure: {error.Message}");
            //return error;
        }

        private static void DoSend(Socket socket, SocketAwaitableEventArgs args, in ReadOnlySequence<byte> buffer, string name)
        {
            if (buffer.IsSingleSegment)
            {
                DoSend(socket, args, buffer.First, name);
                return;
            }

            if (args.Buffer != null)
            {
                args.SetBuffer(null, 0, 0);
            }

            var bufferList = GetBufferList(args, buffer);
            args.BufferList = bufferList;

            Common.Debugger.Log(name, $"## {nameof(socket.SendAsync)} {buffer.Length}");
            if (socket.SendAsync(args))
            {
                CounterHelper.Incr(Counter.SocketSendAsyncMultiAsync);
            }
            else
            {
                CounterHelper.Incr(Counter.SocketSendAsyncMultiSync);
                args.Complete();
            }
        }

        private static void DoSend(Socket socket, SocketAwaitableEventArgs args, ReadOnlyMemory<byte> memory, string name)
        {
            // clear any existing buffer list
            RecycleSpareBuffer(args);

            var segment = memory.GetArray();
            args.SetBuffer(segment.Array, segment.Offset, segment.Count);
            Common.Debugger.Log(name, $"## {nameof(socket.SendAsync)} {memory.Length}");
            if (socket.SendAsync(args))
            {
                CounterHelper.Incr(Counter.SocketSendAsyncSingleAsync);
            }
            else
            {
                CounterHelper.Incr(Counter.SocketSendAsyncSingleSync);
                args.Complete();
            }
        }

        private static List<ArraySegment<byte>> GetBufferList(SocketAsyncEventArgs args, in ReadOnlySequence<byte> buffer)
        {
            CounterHelper.Incr(Counter.SocketGetBufferList);
            Debug.Assert(!buffer.IsEmpty);
            Debug.Assert(!buffer.IsSingleSegment);

            var list = (args?.BufferList as List<ArraySegment<byte>>) ?? GetSpareBuffer();

            if (list == null)
            {
                list = new List<ArraySegment<byte>>();
            }
            else
            {
                // Buffers are pooled, so it's OK to root them until the next multi-buffer write.
                list.Clear();
            }

            foreach (var b in buffer)
            {
                list.Add(b.GetArray());
            }

            return list;
        }
    }
}
