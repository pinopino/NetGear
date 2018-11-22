using NetGear.Core.Listener;
using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NetGear.Core.Connection
{
    public sealed class SocketConnection : BaseConnection
    {
        class FixHeaderDecoder
        {
            private enum ParseEnum
            {
                Received = 1,
                Process_Head = 2,
                Process_Body = 3,
                Find_Body = 4
            }

            bool _debug;
            ParseEnum _parseStatus;
            byte[] headBuffer = null;
            byte[] bodyBuffer = null;
            int maxMessageLength = 512;
            int headLength = 4;

            int offset = 0;
            int messageLength = 0;
            int prefixBytesDoneCount = 0;
            int prefixBytesDoneThisOp = 0;
            int messageBytesDoneCount = 0;
            int messageBytesDoneThisOp = 0;
            int remainingBytesToProcess = 0;
            SocketConnection _connection;

            public FixHeaderDecoder(SocketConnection connection, bool debug = false)
            {
                _debug = debug;
                _parseStatus = ParseEnum.Received;
                _connection = connection;
            }

            public void ProcessReceive(SocketAsyncEventArgs e)
            {
                Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
                while (true)
                {
                    #region ParseLogic
                    switch (_parseStatus)
                    {
                        case ParseEnum.Received:
                            {
                                prefixBytesDoneThisOp = 0;
                                messageBytesDoneThisOp = 0;

                                var read = e.BytesTransferred;
                                if (e.SocketError == SocketError.Success)
                                {
                                    // 接收到FIN
                                    if (read == 0)
                                    {
                                        _connection.Close();
                                        return;
                                    }

                                    remainingBytesToProcess = read;
                                    _parseStatus = ParseEnum.Process_Head;
                                }
                                else
                                {
                                    _connection.Abort("e.SocketError != SocketError.Success");
                                    return;
                                }
                            }
                            break;
                        case ParseEnum.Process_Head:
                            {
                                if (prefixBytesDoneCount < headLength)
                                {
                                    if (prefixBytesDoneCount == 0)
                                    {
                                        headBuffer = ArrayPool<byte>.Shared.Rent(headLength);
                                    }

                                    if (remainingBytesToProcess >= headLength - prefixBytesDoneCount)
                                    {
                                        Buffer.BlockCopy(
                                            e.Buffer,
                                            0 + offset,
                                            headBuffer,
                                            prefixBytesDoneCount,
                                            headLength - prefixBytesDoneCount);

                                        prefixBytesDoneThisOp = headLength - prefixBytesDoneCount;
                                        prefixBytesDoneCount += prefixBytesDoneThisOp;
                                        remainingBytesToProcess = remainingBytesToProcess - prefixBytesDoneThisOp;
                                        messageLength = BitConverter.ToInt32(headBuffer, 0);
                                        ArrayPool<byte>.Shared.Return(headBuffer, true);
                                        if (messageLength == 0 || messageLength > maxMessageLength)
                                        {
                                            _connection.Abort("消息长度为0或超过最大限制，直接丢弃");
                                            return;
                                        }

                                        _parseStatus = ParseEnum.Process_Body;
                                    }
                                    else
                                    {
                                        Buffer.BlockCopy(
                                            e.Buffer,
                                            0 + offset,
                                            headBuffer,
                                            prefixBytesDoneCount,
                                            remainingBytesToProcess);

                                        prefixBytesDoneThisOp = remainingBytesToProcess;
                                        prefixBytesDoneCount += prefixBytesDoneThisOp;
                                        remainingBytesToProcess = 0;

                                        offset = 0;

                                        _parseStatus = ParseEnum.Received;
                                        // 开始新一次recv
                                        _connection.DoReceive(e);
                                        return;
                                    }
                                }
                                else
                                {
                                    _parseStatus = ParseEnum.Process_Body;
                                }
                            }
                            break;
                        case ParseEnum.Process_Body:
                            {
                                if (messageBytesDoneCount == 0)
                                {
                                    bodyBuffer = ArrayPool<byte>.Shared.Rent(messageLength);
                                }

                                if (remainingBytesToProcess >= messageLength - messageBytesDoneCount)
                                {
                                    Buffer.BlockCopy(
                                        e.Buffer,
                                        prefixBytesDoneThisOp + offset,
                                        bodyBuffer,
                                        messageBytesDoneCount,
                                        messageLength - messageBytesDoneCount);

                                    messageBytesDoneThisOp = messageLength - messageBytesDoneCount;
                                    messageBytesDoneCount += messageBytesDoneThisOp;
                                    remainingBytesToProcess = remainingBytesToProcess - messageBytesDoneThisOp;

                                    _parseStatus = ParseEnum.Find_Body;
                                }
                                else
                                {
                                    Buffer.BlockCopy(
                                        e.Buffer,
                                        prefixBytesDoneThisOp + offset,
                                        bodyBuffer,
                                        messageBytesDoneCount,
                                        remainingBytesToProcess);

                                    messageBytesDoneThisOp = remainingBytesToProcess;
                                    messageBytesDoneCount += messageBytesDoneThisOp;
                                    remainingBytesToProcess = 0;

                                    offset = 0;

                                    _parseStatus = ParseEnum.Received;
                                    // 开始新一次recv
                                    _connection.DoReceive(e);
                                    return;
                                }
                            }
                            break;
                        case ParseEnum.Find_Body:
                            {
                                _connection.MessageReceived(bodyBuffer, messageLength);
                                if (remainingBytesToProcess == 0)
                                {
                                    messageLength = 0;
                                    prefixBytesDoneCount = 0;
                                    messageBytesDoneCount = 0;
                                    _parseStatus = ParseEnum.Received;
                                    // 开始新一次recv
                                    _connection.DoReceive(e);
                                    return;
                                }
                                else
                                {
                                    offset += (headLength + messageLength);

                                    messageLength = 0;
                                    prefixBytesDoneCount = 0;
                                    prefixBytesDoneThisOp = 0;
                                    messageBytesDoneCount = 0;
                                    messageBytesDoneThisOp = 0;
                                    _parseStatus = ParseEnum.Process_Head;
                                }
                            }
                            break;
                    }
                    #endregion
                }
            }

            private void Print(string message)
            {
                if (_debug)
                {
                    Console.WriteLine(message);
                }
            }
        }

        bool _disposed;
        SocketListener _listener;
        FixHeaderDecoder _decoder;

        public SocketConnection(int id, Socket socket, SocketListener listener, bool debug = false)
            : base(id, socket, debug)
        {
            _disposed = false;
            _listener = listener;
            _decoder = new FixHeaderDecoder(this, debug);
            _readEventArgs = _listener.SocketAsyncReadEventArgsPool.Get();
            _sendEventArgs = _listener.SocketAsyncSendEventArgsPool.Get();
            _readEventArgs.Completed += IO_Completed;
            _sendEventArgs.Completed += IO_Completed;
        }

        ~SocketConnection()
        {
            // 必须为false
            Dispose(false);
        }

        public override void Start()
        {
            Action<object> action = (state) =>
            {
                try
                {
                    Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
                    Interlocked.CompareExchange(ref _execStatus, STARTED, NOT_STARTED);
                    var willRaiseEvent = _socket.ReceiveAsync(_readEventArgs);
                    if (!willRaiseEvent)
                    {
                        ProcessReceive(_readEventArgs);
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || ex.SocketErrorCode == SocketError.InvalidArgument)
                {
                    Abort("远程连接被关闭");
                }
                catch
                {
                    Close();
                }
            };
            _scheduler.QueueTask(action, null);
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (_execStatus == STARTED)
                _decoder.ProcessReceive(e);
        }

        private void DoReceive(SocketAsyncEventArgs e)
        {
            var willRaiseEvent = _socket.ReceiveAsync(e);
            if (!willRaiseEvent)
            {
                ProcessReceive(e);
            }
        }

        internal void MessageReceived(byte[] messageData, int length)
        {
            _listener.MessageReceived(this, messageData, length);
        }

        internal void InnerSend(Package package)
        {
            if (package.RentFromPool)
                _sendEventArgs.UserToken = package.MessageData; // 预先保存下来，使用完毕需要回收到ArrayPool中

            // todo: 缓冲区一次发送不完的情况处理
            if (package.NeedHead)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(package.DataLength), 0, _sendEventArgs.Buffer, 0, 4);
                Buffer.BlockCopy(package.MessageData, 0, _sendEventArgs.Buffer, 4, package.DataLength);
                // todo: abort和这里的send会有一个race condition，目前考虑的解决办法是abort那里自旋一段时间等
                // 当次发送完毕了再予以关闭
                _sendEventArgs.SetBuffer(0, package.DataLength + 4);
            }
            else
            {
                Buffer.BlockCopy(package.MessageData, 0, _sendEventArgs.Buffer, 0, package.DataLength);
                // todo: abort和这里的send会有一个race condition，目前考虑的解决办法是abort那里自旋一段时间等
                // 当次发送完毕了再予以关闭
                _sendEventArgs.SetBuffer(0, package.DataLength);
            }

            var willRaiseEvent = _socket.SendAsync(_sendEventArgs);
            if (!willRaiseEvent)
            {
                ProcessSend(_sendEventArgs);
            }
        }

        private void ProcessSend(SocketAsyncEventArgs e)
        {
            if (e.UserToken != null)
                ArrayPool<byte>.Shared.Return((byte[])e.UserToken);
        }

        private void IO_Completed(object sender, SocketAsyncEventArgs e)
        {
            Action<object> action = (state) =>
            {
                try
                {
                    Print("当前线程id：" + Thread.CurrentThread.ManagedThreadId);
                    switch (e.LastOperation)
                    {
                        case SocketAsyncOperation.Receive:
                            ProcessReceive(e);
                            break;
                        case SocketAsyncOperation.Send:
                            ProcessSend(e);
                            break;
                        default:
                            throw new ArgumentException("未知的e.LastOperation");
                    }
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.OperationAborted || ex.SocketErrorCode == SocketError.InvalidArgument)
                {
                    Abort("远程连接被关闭");
                }
                catch
                {
                    Close();
                }
            };
            _scheduler.QueueTask(action, e);
        }

        internal byte[] GetMessageBytes(string message, out int length)
        {
            var body = message;
            var body_bytes = Encoding.UTF8.GetBytes(body);
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            length = head_bytes.Length + body_bytes.Length;
            var bytes = ArrayPool<byte>.Shared.Rent(length);

            Buffer.BlockCopy(head_bytes, 0, bytes, 0, head_bytes.Length);
            Buffer.BlockCopy(body_bytes, 0, bytes, head_bytes.Length, body_bytes.Length);

            return bytes;
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _readEventArgs.UserToken = null;
                _readEventArgs.Completed -= IO_Completed;
                _sendEventArgs.UserToken = null;
                _sendEventArgs.Completed -= IO_Completed;
                ((PooledSocketAsyncEventArgs)_readEventArgs).Dispose();
                ((PooledSocketAsyncEventArgs)_sendEventArgs).Dispose();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;

            // 调用基类dispose
            base.Dispose();
        }
    }
}
