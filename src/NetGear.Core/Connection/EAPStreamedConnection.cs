using NetGear.Core.Common;
using ProtoBuf;
using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace NetGear.Core.Connection
{
    public abstract class EAPStreamedConnection : BaseConnection
    {
        public class Token
        {
            public int Op;
            public string Tag;

            public int Count;

            public int Read;
            public int Send;
            public int Offset;
            public int Remain;

            public byte[] Bytes;
            public bool RentFromPool;

            public Action<int> Continuation;

            public void Reset()
            {
                Op = 0;
                Tag = string.Empty;
                Count = 0;
                Read = 0;
                Remain = 0;
                Continuation = null;
            }
        }

        bool _disposed;
        byte[] _largebuffer;

        #region 事件
        protected event EventHandler<int> OnReadInt32Complete;
        protected event EventHandler<ArraySegment<byte>> OnReadBytesComplete;
        protected event EventHandler<string> OnReadStringComplete;
        protected event EventHandler<object> OnReadObjectComplete;
        protected event EventHandler OnWriteComplete;
        #endregion

        public EAPStreamedConnection(int id, Socket socket, bool debug = false)
            : base(id, socket, debug)
        {
            _disposed = false;
            _readEventArgs.UserToken = new Token();
            _readEventArgs.Completed += _readEventArgs_Completed;
            _sendEventArgs.UserToken = new Token();
            _sendEventArgs.Completed += _sendEventArgs_Completed;
        }

        ~EAPStreamedConnection()
        {
            Dispose(false);
        }

        private void _readEventArgs_Completed(object sender, SocketAsyncEventArgs e)
        {
            var op = ((Token)e.UserToken).Op;
            switch (op)
            {
                case 1: // fillbuffer
                    {
                        if (e.BytesTransferred == 0)
                        {
                            // FIN here
                            // todo: 添加处理逻辑
                            return;
                        }

                        var read = ((Token)e.UserToken).Read;
                        var count = ((Token)e.UserToken).Count;
                        read += e.BytesTransferred;
                        if (read < count)
                        {
                            BeginFillBuffer(count, read);
                        }
                        else
                        {
                            if (((Token)e.UserToken).Continuation != null)
                            {
                                var length = (int)(e.Buffer[0] | e.Buffer[1] << 8 | e.Buffer[2] << 16 | e.Buffer[3] << 24);
                                ((Token)e.UserToken).Continuation(length);
                            }
                            else
                            {
                                InvokeCallBack(count, e);
                            }
                        }
                    }
                    break;
                case 2: // filllargebuffer
                    {
                        if (e.BytesTransferred == 0)
                        {
                            // FIN here
                            // todo: 添加处理逻辑
                            return;
                        }

                        var count = ((Token)e.UserToken).Count;
                        var read = ((Token)e.UserToken).Read;
                        var remain = ((Token)e.UserToken).Remain;
                        var need = remain > _readEventArgs.Buffer.Length ? _readEventArgs.Buffer.Length : remain;
                        var tmp = e.BytesTransferred < need ? e.BytesTransferred : need;
                        Buffer.BlockCopy(e.Buffer, 0, _largebuffer, read, tmp);
                        read += tmp;
                        remain -= tmp;
                        if (remain > 0)
                        {
                            BeginFillLargeBuffer(count, read, remain);
                        }
                        else
                        {
                            InvokeCallBack(count, e);
                        }
                    }
                    break;
            }
        }

        private void _sendEventArgs_Completed(object sender, SocketAsyncEventArgs e)
        {
            var op = ((Token)e.UserToken).Op;
            switch (op)
            {
                case 1:
                    {
                        var count = ((Token)e.UserToken).Count;
                        var send = ((Token)e.UserToken).Send;
                        var remain = ((Token)e.UserToken).Remain;
                        var offset = ((Token)e.UserToken).Offset;
                        var buffer = ((Token)e.UserToken).Bytes;
                        send += e.BytesTransferred;
                        remain -= e.BytesTransferred;
                        if (remain > 0)
                        {
                            var need = remain > e.Buffer.Length ? e.Buffer.Length : remain;
                            e.SetBuffer(0, need);
                            Buffer.BlockCopy(buffer, offset + send, e.Buffer, 0, need);
                            _socket.SendAsync(e);
                        }
                        else
                        {
                            var rentFromPool = ((Token)e.UserToken).RentFromPool;
                            if (rentFromPool)
                            {
                                ArrayPool<byte>.Shared.Return(buffer, true);
                            }
                        }
                    }
                    break;
                case 2:
                    {
                        if (e.SocketError != SocketError.Success)
                        {
                            throw new Exception();
                        }
                    }
                    break;
            }
            OnWriteComplete?.Invoke(null, null);
        }

        private void InvokeCallBack(int count, SocketAsyncEventArgs e)
        {
            if (count == sizeof(int))
            {
                var val = (int)(e.Buffer[0] | e.Buffer[1] << 8 | e.Buffer[2] << 16 | e.Buffer[3] << 24);
                OnReadInt32Complete?.Invoke(null, val);
            }
            else
            {
                if (count > _readEventArgs.Buffer.Length)
                {
                    OnReadBytesComplete(null, _largebuffer);
                }
                else
                {
                    OnReadBytesComplete(null, new ArraySegment<byte>(e.Buffer, 0, count));
                }
            }
        }

        public void BeginReadInt32()
        {
            ((Token)_readEventArgs.UserToken).Reset();
            BeginFillBuffer(4, 0);
        }

        public void BeginReadBytes(int count)
        {
            ((Token)_readEventArgs.UserToken).Reset();
            if (count > _readEventArgs.Buffer.Length)
            {
                BeginFillLargeBuffer(count, 0, count);
            }
            else
            {
                BeginFillBuffer(count, 0);
            }
        }

        public void BeginReadString()
        {
            ((Token)_readEventArgs.UserToken).Reset();
            BeginFillBuffer(4, 0, length => BeginReadBytes(length));
        }

        public void BeginReadObject()
        {
            ((Token)_readEventArgs.UserToken).Reset();
            BeginFillBuffer(4, 0, length => BeginReadBytes(length));
        }

        private void BeginFillBuffer(int count, int read, Action<int> continuation = null)
        {
            ((Token)_readEventArgs.UserToken).Op = 1;
            ((Token)_readEventArgs.UserToken).Count = count;
            ((Token)_readEventArgs.UserToken).Read = read;
            if (continuation != null)
                ((Token)_readEventArgs.UserToken).Continuation = continuation;
            _readEventArgs.SetBuffer(read, count - read);
            _socket.ReceiveAsync(_readEventArgs);
        }

        private void BeginFillLargeBuffer(int count, int read, int remain)
        {
            if (count == remain)
            {
                ReleaseLargeBuffer();
                _largebuffer = ArrayPool<byte>.Shared.Rent(count);
            }

            ((Token)_readEventArgs.UserToken).Op = 2;
            ((Token)_readEventArgs.UserToken).Count = count;
            ((Token)_readEventArgs.UserToken).Read = read;
            ((Token)_readEventArgs.UserToken).Remain = remain;
            var need = remain > _readEventArgs.Buffer.Length ? _readEventArgs.Buffer.Length : remain;
            _readEventArgs.SetBuffer(0, need);
            _socket.ReceiveAsync(_readEventArgs);
        }

        public void BeginWrite(byte[] buffer, int offset, int count, bool rentFromPool)
        {
            ((Token)_sendEventArgs.UserToken).Op = 1;
            ((Token)_sendEventArgs.UserToken).Bytes = buffer;
            ((Token)_sendEventArgs.UserToken).Offset = offset;
            ((Token)_sendEventArgs.UserToken).Count = count;
            ((Token)_sendEventArgs.UserToken).Send = 0;
            ((Token)_sendEventArgs.UserToken).Remain = count;
            
            var need = count > _sendEventArgs.Buffer.Length ? _sendEventArgs.Buffer.Length : count;
            _sendEventArgs.SetBuffer(0, need);
            Buffer.BlockCopy(buffer, offset, _sendEventArgs.Buffer, 0, need);
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(bool value)
        {
            ((Token)_sendEventArgs.UserToken).Reset();
            ((Token)_sendEventArgs.UserToken).Op = 1;
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = (byte)(value ? 1 : 0);
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(byte value)
        {
            ((Token)_sendEventArgs.UserToken).Reset();
            ((Token)_sendEventArgs.UserToken).Op = 1;
            _sendEventArgs.SetBuffer(0, 1);
            _sendEventArgs.Buffer[0] = value;
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(double value)
        {
            ((Token)_sendEventArgs.UserToken).Reset();
            ((Token)_sendEventArgs.UserToken).Op = 1;
            _sendEventArgs.SetBuffer(0, 8);
            UnsafeDoubleBytes(value);
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(short value)
        {
            ((Token)_sendEventArgs.UserToken).Reset();
            ((Token)_sendEventArgs.UserToken).Op = 1;
            _sendEventArgs.SetBuffer(0, 2);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(int value)
        {
            ((Token)_sendEventArgs.UserToken).Reset();
            ((Token)_sendEventArgs.UserToken).Op = 1;
            _sendEventArgs.SetBuffer(0, 4);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(long value)
        {
            ((Token)_sendEventArgs.UserToken).Reset();
            ((Token)_sendEventArgs.UserToken).Op = 1;
            _sendEventArgs.SetBuffer(0, 8);
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            _sendEventArgs.Buffer[4] = (byte)(value >> 32);
            _sendEventArgs.Buffer[5] = (byte)(value >> 40);
            _sendEventArgs.Buffer[6] = (byte)(value >> 48);
            _sendEventArgs.Buffer[7] = (byte)(value >> 56);
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(float value)
        {
            ((Token)_sendEventArgs.UserToken).Reset();
            ((Token)_sendEventArgs.UserToken).Op = 1;
            _sendEventArgs.SetBuffer(0, 4);
            UnsafeFloatBytes(value);
            _socket.SendAsync(_sendEventArgs);
        }

        public void BeginWrite(decimal value)
        {
            throw new NotImplementedException();
        }

        public void BeginWrite(string value)
        {
            var body_bytes = Encoding.UTF8.GetBytes(value);
            var length = 0;
            var bytes = CalcBytes(body_bytes, out length);
            BeginWrite(bytes, 0, length, true);
        }

        public void BeginWrite(object value, SerializeType serializeType)
        {
            switch (serializeType)
            {
                case SerializeType.Json:
                    {
                        var body_bytes = value.ToSerializedBytes();
                        var length = 0;
                        var bytes = CalcBytes(body_bytes, out length);

                        BeginWrite(bytes, 0, length, true);
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
                            BeginWrite(body_bytes, 0, (int)stream.Position, false);
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
