﻿using NetGear.Core.Common;
using ProtoBuf;
using System;
using System.Buffers;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NetGear.Core.Connection
{
    public abstract class StreamedSocketConnection : BaseConnection
    {
        public class Token
        {
            public int Op;
            public string Tag;

            public int Count;

            public int Read;            
            public int Remain;

            public Action<int> continuation;
        }

        bool _disposed;
        byte[] _largebuffer;

        // todo: 如果不是在conn.ctor的时候初始化saea，而是在每次执行io时从池中获取saea，
        // 感觉上已经有点可以做IO合并的基础了？
        protected SocketAsyncEventArgs _readEventArgs;
        protected SocketAsyncEventArgs _sendEventArgs;
        protected SocketAwaitable _readAwait;
        protected SocketAwaitable _sendAwait;

        public StreamedSocketConnection(int id, Socket socket, bool debug = false)
            : base(id, socket, debug)
        {
            _disposed = false;
            _readEventArgs.Completed += _readEventArgs_Completed;
        }

        protected event EventHandler<int> OnReadInt32Complete;
        protected event EventHandler<ArraySegment<byte>> OnReadBytesComplete;
        protected event EventHandler<string> OnReadStringComplete;
        protected event EventHandler<object> OnReadObjectComplete;

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
                            if (((Token)e.UserToken).continuation != null)
                            {
                                var length = (int)(e.Buffer[0] | e.Buffer[1] << 8 | e.Buffer[2] << 16 | e.Buffer[3] << 24);
                                ((Token)e.UserToken).continuation(length);
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
                case 3: // readstring
                    {
                        
                    }
                    break;
                case 4: // readobject
                    {

                    }
                    break;
            }
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
            BeginFillBuffer(4, 0);
        }

        public void BeginReadBytes(int count)
        {
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
            BeginFillBuffer(4, 0, length => BeginReadBytes(length));
        }

        public void BeginReadObject()
        {
            // todo...
        }

        private void BeginFillBuffer(int count, int read, Action<int> continuation = null)
        {
            ((Token)_readEventArgs.UserToken).Op = 1;
            ((Token)_readEventArgs.UserToken).Count = count;
            ((Token)_readEventArgs.UserToken).Read = read;
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

        ~StreamedSocketConnection()
        {
            Dispose(false);
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
