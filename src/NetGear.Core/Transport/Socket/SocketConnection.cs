using NetGear.Core.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public sealed partial class SocketConnection : TransportConnection, IDuplexPipe, IDisposable
    {
        private sealed class WrappedReader : PipeReader
        {
            private readonly PipeReader _reader;
            private readonly SocketConnection _connection;

            public WrappedReader(PipeReader reader, SocketConnection connection)
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
            private readonly SocketConnection _connection;

            public WrappedWriter(PipeWriter writer, SocketConnection connection)
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

        private int _socketShutdownKind;
        private volatile bool _sendAborted, _receiveAborted;
        private readonly Pipe _sendToSocket, _receiveFromSocket;
        private readonly PipeOptions _receiveOptions, _sendOptions;
        private readonly PipeReader _input;
        private readonly PipeWriter _output;
        private static List<ArraySegment<byte>> _spareBuffer;
        private volatile ConnectionAbortedException _abortReason;

        private string Name { get; }

        private ISocketsTrace Log { get; }

        /// <summary>
        /// The underlying socket for this connection
        /// </summary>
        public Socket Socket { get; }

        private SocketConnection(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions socketConnectionOptions, string name = null)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            if (string.IsNullOrWhiteSpace(name))
                name = GetType().Name;
            Name = name.Trim();
            if (sendPipeOptions == null)
                sendPipeOptions = PipeOptions.Default;
            if (receivePipeOptions == null)
                receivePipeOptions = PipeOptions.Default;

            Socket = socket;
            SocketConnectionOptions = socketConnectionOptions;
            _sendToSocket = new Pipe(sendPipeOptions); // read from this pipe and send to socket
            _receiveFromSocket = new Pipe(receivePipeOptions); // recv from socket and push to this pipe
            _receiveOptions = receivePipeOptions;
            _sendOptions = sendPipeOptions;

            _input = new WrappedReader(_receiveFromSocket.Reader, this);
            _output = new WrappedWriter(_sendToSocket.Writer, this);

            // 说明：
            // saea例子中是类似开启了一个逻辑上的while(true)，底层读写以及紧挨着的上层协议解析
            // 部分都是被割裂在回调中的；相比之下这里因为有了一根pipe，整个逻辑表达是非常流畅的。
            // 我们可以直接扔出两个while(true)，同步直接靠pipe的一端完成，而pipe的另一端又是由更
            // 上层的业务（包括frame的解析等）来驱使驱动；可以说增加的pipe这一层indirection是非常
            // 合适的，一切都可以以一种很自然的方式来思考！
            sendPipeOptions.ReaderScheduler.Schedule(s_DoSendAsync, this);
            receivePipeOptions.ReaderScheduler.Schedule(s_DoReceiveAsync, this);
        }

        private static readonly Action<object> s_DoReceiveAsync = DoReceiveAsync;
        private static void DoReceiveAsync(object s) => ((SocketConnection)s).DoReceiveAsync().FireAndForget();

        private static readonly Action<object> s_DoSendAsync = DoSendAsync;
        private static void DoSendAsync(object s) => ((SocketConnection)s).DoSendAsync().FireAndForget();

        public override PipeReader Input => this._input;

        public override PipeWriter Output => this._output;

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions pipeOptions = null,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            return new SocketConnection(socket, pipeOptions, pipeOptions, socketConnectionOptions, name);
        }

        /// <summary>
        /// Create a SocketConnection instance over an existing socket
        /// </summary>
        public static SocketConnection Create(Socket socket, PipeOptions sendPipeOptions, PipeOptions receivePipeOptions,
            SocketConnectionOptions socketConnectionOptions = SocketConnectionOptions.None, string name = null)
        {
            return new SocketConnection(socket, sendPipeOptions, receivePipeOptions, socketConnectionOptions, name);
        }

        public override void Abort(ConnectionAbortedException abortReason)
        {
            _abortReason = abortReason;
            Input.CancelPendingRead();

            // Try to gracefully close the socket to match libuv behavior.
            if (!_receiveAborted)
            {
                try
                {
                    Socket.Shutdown(SocketShutdown.Receive);
                    TrySetShutdown(_abortReason, this, PipeShutdownKind.InputWriterCompleted);
                }
                catch { }
            }

            if (!_sendAborted)
            {
                try
                {
                    Socket.Shutdown(SocketShutdown.Send);
                    TrySetShutdown(_abortReason, this, PipeShutdownKind.OutputReaderCompleted);
                }
                catch { }
            }

            try
            {
                Socket.Close();
                Socket.Dispose();
            }
            catch { }
        }

        private void InputReaderCompleted(Exception ex)
        {
            TrySetShutdown(ex, this, PipeShutdownKind.InputReaderCompleted);
            try
            {
                this.Socket.Shutdown(SocketShutdown.Receive);
            }
            catch
            { }
        }

        private void OutputWriterCompleted(Exception ex)
        {
            TrySetShutdown(ex, this, PipeShutdownKind.OutputWriterCompleted);
        }

        private static List<ArraySegment<byte>> GetSpareBuffer()
        {
            var existing = Interlocked.Exchange(ref _spareBuffer, null);
            existing?.Clear();
            return existing;
        }

        private static void RecycleSpareBuffer(SocketAwaitableEventArgs args)
        {
            // note: the BufferList getter is much less expensive then the setter.
            if (args?.BufferList is List<ArraySegment<byte>> list)
            {
                args.BufferList = null; // see #26 - don't want it being reused by the next piece of IO
                Interlocked.Exchange(ref _spareBuffer, list);
            }
        }

        /// <summary>
        /// When possible, determines how the pipe first reached a close state
        /// </summary>
        public PipeShutdownKind ShutdownKind => (PipeShutdownKind)Thread.VolatileRead(ref _socketShutdownKind);

        /// <summary>
        /// When the ShutdownKind relates to a socket error, may contain the socket error code
        /// </summary>
        public SocketError SocketError { get; private set; }

        private bool TrySetShutdown(Exception ex, SocketConnection connection, PipeShutdownKind kind)
        {
            try
            {
                return ex is SocketException se ? connection.TrySetShutdown(kind, se.SocketErrorCode)
                    : connection.TrySetShutdown(kind);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Try to signal the pipe shutdown reason as being due to an application protocol event
        /// </summary>
        /// <param name="kind">The kind of shutdown; only protocol-related reasons will succeed</param>
        /// <returns>True if successful</returns>
        public bool TrySetProtocolShutdown(PipeShutdownKind kind)
        {
            switch (kind)
            {
                case PipeShutdownKind.ProtocolExitClient:
                case PipeShutdownKind.ProtocolExitServer:
                    return TrySetShutdown(kind);
                default:
                    return false;
            }
        }

        private bool TrySetShutdown(PipeShutdownKind kind, SocketError socketError)
        {
            bool win = TrySetShutdown(kind);
            if (win) SocketError = socketError;
            return win;
        }

        private bool TrySetShutdown(PipeShutdownKind kind) => kind != PipeShutdownKind.None
           && Interlocked.CompareExchange(ref _socketShutdownKind, (int)kind, 0) == 0;

        /// <summary>
        /// Set recommended socket options for client sockets
        /// </summary>
        /// <param name="socket">The socket to set options against</param>
        public static void SetRecommendedClientOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch (Exception ex) { Console.WriteLine(nameof(SocketConnection), ex.Message); }

            try { SetFastLoopbackOption(socket); } catch (Exception ex) { Console.WriteLine(nameof(SocketConnection), ex.Message); }
        }

        /// <summary>
        /// Set recommended socket options for server sockets
        /// </summary>
        /// <param name="socket">The socket to set options against</param>
        public static void SetRecommendedServerOptions(Socket socket)
        {
            if (socket.AddressFamily == AddressFamily.Unix) return;

            try { socket.NoDelay = true; } catch (Exception ex) { Console.WriteLine(nameof(SocketConnection), ex.Message); }
        }

        public override string ToString() => Name;

        #region Debug
        [Conditional("VERBOSE")]
        private void DebugLog(string message, [CallerMemberName] string caller = null,
            [CallerLineNumber] int lineNumber = 0)
            => Common.Debugger.Log(Name, message, $"{caller}#{lineNumber}");
        #endregion

        public void Dispose()
        {
            TrySetShutdown(PipeShutdownKind.PipeDisposed);
#if DEBUG
            GC.SuppressFinalize(this);
#endif
            try { Socket.Shutdown(SocketShutdown.Receive); } catch { }
            try { Socket.Shutdown(SocketShutdown.Send); } catch { }
            try { Socket.Close(); } catch { }
            try { Socket.Dispose(); } catch { }

            // make sure that the async operations end ... can be twitchy!
            try { _readerArgs?.Abort(); } catch { }
            try { _writerArgs?.Abort(); } catch { }
        }
    }
}
