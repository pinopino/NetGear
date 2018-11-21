using NetGear.Core.Connection;
using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core.Client
{
    public class SocketClientConnection : BaseConnection
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
            SocketClientConnection _connection;

            public FixHeaderDecoder(SocketClientConnection connection, bool debug = false)
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

        int _id;
        bool _debug;
        bool _connected;
        int _bufferSize;
        int _connectTimeout; // 单位毫秒
        IPEndPoint _remoteEndPoint;
        FixHeaderDecoder _decoder;

        public SocketClientConnection(int id, string address, int port, int bufferSize, bool debug = false)
            : base(id, new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp), debug)
        {
            _id = id;
            _debug = debug;
            _connected = false;
            _bufferSize = bufferSize;
            _connectTimeout = 5 * 1000;
            _decoder = new FixHeaderDecoder(this, debug);
            _remoteEndPoint = new IPEndPoint(IPAddress.Parse(address), port);
        }

        public override void Start()
        {
            // just do nothing
        }

        protected override void InitSAEA()
        {
            _readEventArgs = new GSocketAsyncEventArgs();
            _readEventArgs.Completed += IO_Completed;
            _readEventArgs.SetBuffer(_bufferSize);
            _sendEventArgs = new GSocketAsyncEventArgs();
            _sendEventArgs.Completed += IO_Completed;
            _sendEventArgs.SetBuffer(_bufferSize);
        }

        protected override void ReleaseSAEA()
        {
            _readEventArgs.Completed -= IO_Completed;
            _sendEventArgs.Completed -= IO_Completed;
            base.ReleaseSAEA();
        }

        public void Connect()
        {
            if (_connected)
                return;

            var connectEventArgs = new SocketAsyncEventArgs
            {
                RemoteEndPoint = _remoteEndPoint
            };
            connectEventArgs.Completed += (sender, e) =>
            {
                _connected = true;
            };

            if (_socket.ConnectAsync(connectEventArgs))
            {
                while (!_connected)
                {
                    if (!SpinWait.SpinUntil(() => _connected, _connectTimeout))
                    {
                        throw new TimeoutException("Unable to connect within " + _connectTimeout + "ms");
                    }
                }
            }
            if (connectEventArgs.SocketError != SocketError.Success)
            {
                Close();
                throw new SocketException((int)connectEventArgs.SocketError);
            }
            if (!_socket.Connected)
            {
                Close();
                throw new SocketException((int)SocketError.NotConnected);
            }

            // 至此，已经成功连接到远程服务端
            Task.Run(() =>
            {
                var willRaiseEvent = _socket.ReceiveAsync(_readEventArgs);
                if (!willRaiseEvent)
                {
                    ProcessReceive(_readEventArgs);
                }
            });
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

        protected virtual void MessageReceived(byte[] messageData, int length)
        {
            Console.WriteLine("收到服务端返回：" + Encoding.UTF8.GetString(messageData, 0, length));
        }

        public void Send(byte[] messageData, int length, bool rentFromPool = true)
        {
            if (rentFromPool)
                _sendEventArgs.UserToken = messageData; // 预先保存下来，使用完毕需要回收到ArrayPool中

            Buffer.BlockCopy(BitConverter.GetBytes(length), 0, _sendEventArgs.Buffer, 0, 4);
            Buffer.BlockCopy(messageData, 0, _sendEventArgs.Buffer, 4, length);
            _sendEventArgs.SetBuffer(0, length + 4);

            var willRaiseEvent = _socket.SendAsync(_sendEventArgs);
            if (!willRaiseEvent)
            {
                ProcessSend(_sendEventArgs);
            }
        }

        public void Send(string message)
        {
            var length = 0;
            var bytes = GetMessageBytes(message, out length);
            _sendEventArgs.UserToken = bytes; // 预先保存下来，使用完毕需要回收到ArrayPool中

            Buffer.BlockCopy(bytes, 0, _sendEventArgs.Buffer, 0, length);
            _sendEventArgs.SetBuffer(0, length);

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

        private byte[] GetMessageBytes(string message, out int length)
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
    }
}

