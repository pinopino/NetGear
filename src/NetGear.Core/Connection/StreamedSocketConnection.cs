using NetGear.Core.Common;
using ProtoBuf;
using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core.Connection
{
    public class StreamedSocketConnection : BaseConnection
    {
        class StreamDecoder
        {
            private enum ParseEnum
            {
                Received = 1,
                Process_Body = 2,
                Find_Body = 3
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
            StreamedSocketConnection _connection;

            public StreamDecoder(StreamedSocketConnection connection, bool debug = false)
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
                                    _parseStatus = ParseEnum.Process_Body;
                                }
                                else
                                {
                                    _connection.Abort("e.SocketError != SocketError.Success");
                                    return;
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
        byte[] _largebuffer;
        StreamDecoder _decoder;

        protected SocketAsyncEventArgs _readEventArgs;
        protected SocketAsyncEventArgs _sendEventArgs;
        protected SocketAwaitable _readAwait;
        protected SocketAwaitable _sendAwait;

        public StreamedSocketConnection(int id, Socket socket, bool debug = false)
            : base(id, socket, debug)
        {
            _disposed = false;
            _decoder = new StreamDecoder(this, debug);
        }

        ~StreamedSocketConnection()
        {
            Dispose(false);
        }

        public override void Start()
        {
            Action<object> action = (state) =>
            {
                try
                {
                    Interlocked.CompareExchange(ref _execStatus, STARTED, NOT_STARTED);
                    var willRaiseEvent = _socket.ReceiveAsync(_readEventArgs);
                    if (!willRaiseEvent)
                    {
                        ProcessReceive(_readEventArgs);
                    }
                }
                catch (SocketException ex)
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

        public async Task<int> ReadInt32()
        {
            await FillBuffer(4);
            return (int)(_readEventArgs.Buffer[0] | _readEventArgs.Buffer[1] << 8 | _readEventArgs.Buffer[2] << 16 | _readEventArgs.Buffer[3] << 24);
        }

        public async Task<ArraySegment<byte>> ReadBytes(int count)
        {
            if (count > _readEventArgs.Buffer.Length)
            {
                await FillLargeBuffer(count);
                return _largebuffer;
            }
            else
            {
                await FillBuffer(count);
                return new ArraySegment<byte>(_readEventArgs.Buffer, 0, count);
            }
        }

        public async Task<string> ReadString()
        {
            var length = await ReadInt32();
            var bytes = await ReadBytes(length);

            return Encoding.UTF8.GetString(bytes);
        }

        public async Task<T> ReadObject<T>(SerializeType serializeType = SerializeType.ProtoBuff)
        {
            switch (serializeType)
            {
                case SerializeType.Json:
                    {
                        var length = await ReadInt32();
                        var bytes = await ReadBytes(length);

                        return bytes.Array.ToDeserializedObject<T>();
                    }
                case SerializeType.ProtoBuff:
                    {
                        var length = await ReadInt32();
                        var bytes = await ReadBytes(length);

                        T obj;
                        using (MemoryStream stream = new MemoryStream(bytes.Array, 0, length))
                        {
                            obj = Serializer.Deserialize<T>(stream);
                        }
                        return obj;
                    }
                default:
                    throw new NotImplementedException();
            }
        }

        public async Task Write(byte[] buffer, int offset, int count, bool rentFromPool)
        {
            var sent = 0;
            var need = 0;
            var remain = count;
            while (remain > 0)
            {
                need = remain > _sendEventArgs.Buffer.Length ? _sendEventArgs.Buffer.Length : remain;
                _sendEventArgs.SetBuffer(0, need);
                Buffer.BlockCopy(buffer, offset + sent, _sendEventArgs.Buffer, 0, need);
                await _socket.SendAsync(_sendAwait);
                sent += _sendEventArgs.BytesTransferred;
                remain -= _sendEventArgs.BytesTransferred;
            }

            if (rentFromPool)
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }
        }

        public async Task Write(bool value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = (byte)(value ? 1 : 0);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(byte value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = value;
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(double value)
        {
            _sendEventArgs.SetBuffer(0, 8);
            UnsafeDoubleBytes(value);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(short value)
        {
            _sendEventArgs.SetBuffer(0, 2);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(int value)
        {
            _sendEventArgs.SetBuffer(0, 4);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(long value)
        {
            _sendEventArgs.SetBuffer(0, 8);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            _sendEventArgs.Buffer[4] = (byte)(value >> 32);
            _sendEventArgs.Buffer[5] = (byte)(value >> 40);
            _sendEventArgs.Buffer[6] = (byte)(value >> 48);
            _sendEventArgs.Buffer[7] = (byte)(value >> 56);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(float value)
        {
            _sendEventArgs.SetBuffer(0, 1);
            UnsafeFloatBytes(value);
            await _socket.SendAsync(_sendAwait);
        }

        public async Task Write(decimal value)
        {
            throw new NotImplementedException();
        }

        public async Task Write(string value)
        {
            var body_bytes = Encoding.UTF8.GetBytes(value);
            var length = 0;
            var bytes = CalcBytes(body_bytes, out length);

            await Write(bytes, 0, length, true);
        }

        public async Task Write(object value, SerializeType serializeType = SerializeType.ProtoBuff)
        {
            switch (serializeType)
            {
                case SerializeType.Json:
                    {
                        var body_bytes = value.ToSerializedBytes();
                        var length = 0;
                        var bytes = CalcBytes(body_bytes, out length);

                        await Write(bytes, 0, length, true);
                    };
                    break;
                case SerializeType.ProtoBuff:
                    {
                        // todo: 此处使用的byte[]是stream内部自己分配的，可能会导致gc问题
                        using (MemoryStream stream = new MemoryStream())
                        {
                            stream.WriteByte(0);
                            stream.WriteByte(0);
                            stream.WriteByte(0);
                            stream.WriteByte(0);
                            Serializer.Serialize(stream, value);
                            var body_bytes = stream.GetBuffer();
                            var length_bytes = BitConverter.GetBytes(((int)stream.Position) - 4);
                            body_bytes[0] = length_bytes[0];
                            body_bytes[1] = length_bytes[1];
                            body_bytes[2] = length_bytes[2];
                            body_bytes[3] = length_bytes[3];
                            await Write(body_bytes, 0, (int)stream.Position, false);
                        }
                    }
                    break;
            }
        }

        private byte[] CalcBytes(byte[] body_bytes, out int length)
        {
            var head = body_bytes.Length;
            var head_bytes = BitConverter.GetBytes(head);
            length = head_bytes.Length + body_bytes.Length;
            var bytes = ArrayPool<byte>.Shared.Rent(length);

            Buffer.BlockCopy(head_bytes, 0, bytes, 0, head_bytes.Length);
            Buffer.BlockCopy(body_bytes, 0, bytes, head_bytes.Length, body_bytes.Length);

            return bytes;
        }

        private async Task FillBuffer(int count)
        {
            var read = 0;
            do
            {
                _readEventArgs.SetBuffer(read, count - read);
                await _socket.ReceiveAsync(_readAwait);
                if (_readEventArgs.BytesTransferred == 0)
                {
                    // FIN here
                    // todo: 添加处理逻辑
                    break;
                }
            }
            while ((read += _readEventArgs.BytesTransferred) < count);
        }

        private async Task FillLargeBuffer(int count)
        {
            ReleaseLargeBuffer();
            _largebuffer = ArrayPool<byte>.Shared.Rent(count);
            var read = 0;
            var need = 0;
            var remain = count;
            while (remain > 0)
            {
                need = remain > _readEventArgs.Buffer.Length ? _readEventArgs.Buffer.Length : remain;
                _readEventArgs.SetBuffer(0, need);
                await _socket.ReceiveAsync(_readAwait);
                if (_readEventArgs.BytesTransferred == 0)
                {
                    // FIN here
                    // todo: 添加处理逻辑
                    break;
                }
                var tmp = _readEventArgs.BytesTransferred < need ? _readEventArgs.BytesTransferred : need;
                Buffer.BlockCopy(_readEventArgs.Buffer, 0, _largebuffer, read, tmp);
                read += tmp;
                remain -= tmp;
            }
        }

        private unsafe void UnsafeDoubleBytes(double value)
        {
            ulong TmpValue = *(ulong*)&value;
            _sendEventArgs.Buffer[0] = (byte)TmpValue;
            _sendEventArgs.Buffer[1] = (byte)(TmpValue >> 8);
            _sendEventArgs.Buffer[2] = (byte)(TmpValue >> 16);
            _sendEventArgs.Buffer[3] = (byte)(TmpValue >> 24);
            _sendEventArgs.Buffer[4] = (byte)(TmpValue >> 32);
            _sendEventArgs.Buffer[5] = (byte)(TmpValue >> 40);
            _sendEventArgs.Buffer[6] = (byte)(TmpValue >> 48);
            _sendEventArgs.Buffer[7] = (byte)(TmpValue >> 56);
        }

        private unsafe void UnsafeFloatBytes(float value)
        {
            uint TmpValue = *(uint*)&value;
            _sendEventArgs.Buffer[0] = (byte)TmpValue;
            _sendEventArgs.Buffer[1] = (byte)(TmpValue >> 8);
            _sendEventArgs.Buffer[2] = (byte)(TmpValue >> 16);
            _sendEventArgs.Buffer[3] = (byte)(TmpValue >> 24);
        }

        private void ReleaseLargeBuffer()
        {
            if (_largebuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_largebuffer, true);
                _largebuffer = null;
            }
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
                ReleaseLargeBuffer();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
            base.Dispose();
        }
    }
}
