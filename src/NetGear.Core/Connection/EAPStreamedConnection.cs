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
        }

        ~EAPStreamedConnection()
        {
            Dispose(false);
        }

        protected void Read_Completed(object sender, GSocketAsyncEventArgs e)
        {
            try
            {
                if (e.BytesTransferred == 0)
                {
                    // FIN here
                    // todo: 添加处理逻辑
                    return;
                }

                var count = e.UserToken.Count;
                var read = e.UserToken.Read;
                var continuation = e.UserToken.Continuation;
                read += e.BytesTransferred;
                var remain = count - read;
                if (count <= e.Buffer.Length)
                {
                    if (remain > 0)
                    {
                        e.UserToken.Read = read;
                        e.SetBuffer(read, remain);
                        var willRaiseEvent = _socket.ReceiveAsync(e);
                        if (!willRaiseEvent)
                        {
							// 说明：小心此处可能引起的stack-dive，不过目前更愿意保持这种写法
							// 相当于inline掉了这次调用自然效率会更高些
							// todo: 可以试着记录堆栈go deeper的次数，超过设置的值时再考虑post
							// 到IOQueue上去
                            Read_Completed(sender, e);
                        }
                    }
                    else
                    {
                        if (continuation != null)
                        {
                            var length = (int)(e.Buffer[0] | e.Buffer[1] << 8 | e.Buffer[2] << 16 | e.Buffer[3] << 24);
                            continuation(length);
                        }
                        else
                        {
                            InvokeReadCallBack(count, e);
                        }
                    }
                }
                else
                {
                    var need = remain > e.Buffer.Length ? e.Buffer.Length : remain;
                    var tmp = e.BytesTransferred < need ? e.BytesTransferred : need;
                    Buffer.BlockCopy(e.Buffer, 0, _largebuffer, e.UserToken.Read, e.BytesTransferred);
                    if (remain > 0)
                    {
                        e.UserToken.Read = read;
                        e.SetBuffer(0, need);
                        var willRaiseEvent = _socket.ReceiveAsync(e);
                        if (!willRaiseEvent)
                        {
                            Read_Completed(sender, e);
                        }
                    }
                    else
                    {
                        if (continuation != null)
                        {
                            var length = (int)(e.Buffer[0] | e.Buffer[1] << 8 | e.Buffer[2] << 16 | e.Buffer[3] << 24);
                            continuation(length);
                        }
                        else
                        {
                            InvokeReadCallBack(count, e);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Print(ex.Message);
            }
        }

        protected void Send_Completed(object sender, GSocketAsyncEventArgs e)
        {
            try
            {
                var buffer = e.UserToken.Bytes;
                var offset = e.UserToken.Offset;
                var count = e.UserToken.Count;
                var send = e.UserToken.Send;
                var remain = count - send;
                send += e.BytesTransferred;
                remain -= e.BytesTransferred;
                if (remain > 0)
                {
                    var need = remain > e.Buffer.Length ? e.Buffer.Length : remain;
                    e.SetBuffer(0, need);
                    if (buffer != null)
                    {
                        Buffer.BlockCopy(buffer, offset + send, e.Buffer, 0, need);
                    }
                    var willRaiseEvent = _socket.SendAsync(e);
                    if (!willRaiseEvent)
                    {
                        Send_Completed(sender, e);
                    }
                }
                else
                {
                    var rentFromPool = e.UserToken.RentFromPool;
                    if (rentFromPool)
                    {
                        ArrayPool<byte>.Shared.Return(buffer, true);
                    }
                    OnWriteComplete?.Invoke(null, null);
                }
            }
            catch (Exception ex)
            {
                Print(ex.Message);
            }
        }

        private void InvokeReadCallBack(int count, GSocketAsyncEventArgs e)
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
            BeginFillBuffer(4);
        }

        public void BeginReadString()
        {
            BeginFillBuffer(4, length => BeginReadBytes(length));
        }

        public void BeginReadObject()
        {
            BeginFillBuffer(4, length => BeginReadBytes(length));
        }

        public void BeginReadBytes(int count)
        {
            BeginFillBuffer(count);
        }

        public void BeginWrite(bool value)
        {
            _sendEventArgs.Buffer[0] = (byte)(value ? 1 : 0);
            BeginWrite(null, 0, 1, false);
        }

        public void BeginWrite(byte value)
        {
            _sendEventArgs.Buffer[0] = value;
            BeginWrite(null, 0, 1, false);
        }

        public void BeginWrite(double value)
        {
            UnsafeDoubleBytes(value);
            BeginWrite(null, 0, 8, false);
        }

        public void BeginWrite(short value)
        {
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            BeginWrite(null, 0, 2, false);
        }

        public void BeginWrite(int value)
        {
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            BeginWrite(null, 0, 4, false);
        }

        public void BeginWrite(long value)
        {
            _sendEventArgs.Buffer[0] = (byte)value;
            _sendEventArgs.Buffer[1] = (byte)(value >> 8);
            _sendEventArgs.Buffer[2] = (byte)(value >> 16);
            _sendEventArgs.Buffer[3] = (byte)(value >> 24);
            _sendEventArgs.Buffer[4] = (byte)(value >> 32);
            _sendEventArgs.Buffer[5] = (byte)(value >> 40);
            _sendEventArgs.Buffer[6] = (byte)(value >> 48);
            _sendEventArgs.Buffer[7] = (byte)(value >> 56);
            BeginWrite(null, 0, 8, false);
        }

        public void BeginWrite(float value)
        {
            UnsafeFloatBytes(value);
            BeginWrite(null, 0, 4, false);
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

        public void BeginWrite(byte[] buffer, int offset, int count, bool rentFromPool)
        {
            _sendEventArgs.UserToken.Reset();
            var need = count > _sendEventArgs.Buffer.Length ? _sendEventArgs.Buffer.Length : count;
            if (buffer != null)
            {
                _sendEventArgs.UserToken.Bytes = buffer;
                _sendEventArgs.UserToken.Offset = offset;
                _sendEventArgs.UserToken.Count = count;
                _sendEventArgs.UserToken.Send = 0;
                Buffer.BlockCopy(buffer, offset, _sendEventArgs.Buffer, 0, need);
            }
            _sendEventArgs.SetBuffer(0, need);
            var willRaiseEvent = _socket.SendAsync(_sendEventArgs);
            if (!willRaiseEvent)
            {
                Send_Completed(null, _sendEventArgs);
            }
        }

        private void BeginFillBuffer(int count, Action<int> continuation = null)
        {
            _readEventArgs.UserToken.Reset();
            var large = count > _readEventArgs.Buffer.Length;
            if (large)
            {
                ReleaseLargeBuffer();
                _largebuffer = ArrayPool<byte>.Shared.Rent(count);
            }

            var need = large ? _readEventArgs.Buffer.Length : count;
            _readEventArgs.SetBuffer(0, need);
            var willRaiseEvent = _socket.ReceiveAsync(_readEventArgs);
            if (!willRaiseEvent)
            {
                Read_Completed(null, _readEventArgs);
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

            // 调用基类dispose
            base.Dispose();
        }
    }
}
