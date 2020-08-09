using Microsoft.Extensions.Logging;
using NetGear.Core;
using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public partial class UvConnection : TransportConnection, IDuplexPipe, IDisposable
    {
        private static readonly int MinAllocBufferSize = KestrelMemoryPool.MinimumSegmentSize / 2;

        private static readonly Action<UvStreamHandle, int, object> _readCallback =
            (handle, status, state) => ReadCallback(handle, status, state);

        private static readonly Func<UvStreamHandle, int, object, Uv.uv_buf_t> _allocCallback =
            (handle, suggestedsize, state) => AllocCallback(handle, suggestedsize, state);

        private readonly UvStreamHandle _socket;
        private readonly CancellationTokenSource _connectionClosedTokenSource = new CancellationTokenSource();
        private volatile ConnectionAbortedException _abortReason;
        private long _totalBytesWritten;
        private MemoryHandle _bufferHandle;
        private readonly Pipe _sendToUV, _receiveFromUV;
        private readonly PipeReader _input;
        private readonly PipeWriter _output;

        public UvConnection(UvStreamHandle socket,
            UvThread thread,
            IPEndPoint remoteEndPoint, IPEndPoint localEndPoint,
            PipeOptions sendPipeOptions = null, PipeOptions receivePipeOptions = null,
            string name = null,
            ILibuvTrace log = null)
        {
            _socket = socket;

            RemoteAddress = remoteEndPoint?.Address;
            RemotePort = remoteEndPoint?.Port ?? 0;

            LocalAddress = localEndPoint?.Address;
            LocalPort = localEndPoint?.Port ?? 0;

            _sendToUV = new Pipe(sendPipeOptions ?? PipeOptions.Default);
            _receiveFromUV = new Pipe(receivePipeOptions ?? PipeOptions.Default);
            _input = _receiveFromUV.Reader;
            _output = _sendToUV.Writer;

            ConnectionClosed = _connectionClosedTokenSource.Token;
            Log = log;
            Thread = thread;
        }

        private ILibuvTrace Log { get; }
        private UvThread Thread { get; }
        public override MemoryPool<byte> MemoryPool => Thread.MemoryPool;
        public override PipeReader Input => this._input;
        public override PipeWriter Output => this._output;

        public async Task Start()
        {
            try
            {
                StartReading();

                Exception inputError = null;
                Exception outputError = null;

                try
                {
                    // This *must* happen after socket.ReadStart
                    // The socket output consumer is the only thing that can close the connection. If the
                    // output pipe is already closed by the time we start then it's fine since, it'll 
                    // close gracefully afterwards.
                    await WriteOutputAsync();
                }
                catch (UvException ex)
                {
                    // The connection reset/error has already been logged by LibuvOutputConsumer
                    if (ex.StatusCode == UvConstants.ECANCELED)
                    {
                        // Connection was aborted.
                    }
                    else if (UvConstants.IsConnectionReset(ex.StatusCode))
                    {
                        // Don't cause writes to throw for connection resets.
                        outputError = new ConnectionResetException(ex.Message, ex);
                    }
                    else
                    {
                        inputError = ex;
                        outputError = ex;
                    }
                }
                finally
                {
                    // Now, complete the input so that no more reads can happen
                    _receiveFromUV.Writer.Complete(inputError ?? _abortReason ?? new ConnectionAbortedException());
                    _sendToUV.Reader.Complete(outputError);

                    // Make sure it isn't possible for a paused read to resume reading after calling uv_close
                    // on the stream handle
                    _receiveFromUV.Writer.CancelPendingFlush();

                    // Send a FIN
                    Log.ConnectionWriteFin(ConnectionId);

                    // We're done with the socket now
                    _socket.Dispose();
                    ThreadPool.QueueUserWorkItem(state => ((UvConnection)state).CancelConnectionClosedToken(), this);
                }
            }
            catch (Exception e)
            {
                Log.LogCritical(0, e, $"{nameof(UvConnection)}.{nameof(Start)}() {ConnectionId}");
            }
        }

        public override void Abort(ConnectionAbortedException abortReason)
        {
            _abortReason = abortReason;
            _receiveFromUV.Reader.CancelPendingRead();

            // This cancels any pending I/O.
            Thread.Post(s => s.Dispose(), _socket);
        }

        private Exception LogAndWrapReadError(UvException uvError)
        {
            if (uvError.StatusCode == UvConstants.ECANCELED)
            {
                // The operation was canceled by the server not the client. No need for additional logs.
                return new ConnectionAbortedException(uvError.Message, uvError);
            }
            else if (UvConstants.IsConnectionReset(uvError.StatusCode))
            {
                // Log connection resets at a lower (Debug) level.
                Log.ConnectionReset(ConnectionId);
                return new ConnectionResetException(uvError.Message, uvError);
            }
            else
            {
                Log.ConnectionError(ConnectionId, uvError);
                return new IOException(uvError.Message, uvError);
            }
        }

        private void LogWriteInfo(int status, Exception error)
        {
            if (error == null)
            {
                Log.ConnectionWriteCallback(ConnectionId, status);
            }
            else
            {
                // Log connection resets at a lower (Debug) level.
                if (status == UvConstants.ECANCELED)
                {
                    // Connection was aborted.
                }
                else if (UvConstants.IsConnectionReset(status))
                {
                    Log.ConnectionReset(ConnectionId);
                }
                else
                {
                    Log.ConnectionError(ConnectionId, error);
                }
            }
        }

        private void CancelConnectionClosedToken()
        {
            try
            {
                _connectionClosedTokenSource.Cancel();
            }
            catch (Exception ex)
            {
                Log.LogError(0, ex, $"Unexpected exception in {nameof(UvConnection)}.{nameof(CancelConnectionClosedToken)}.");
            }
        }

        // Only called after connection middleware is complete which means the ConnectionClosed token has fired.
        public void Dispose()
        {
            _connectionClosedTokenSource.Dispose();
        }
    }
}
