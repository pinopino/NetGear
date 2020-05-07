using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Core
{
    public abstract class SimplPipeline : IDisposable
    {
        private IDuplexPipe _pipe;
        private PipeReader _reader;
        private PipeWriter _writer;
        private readonly SemaphoreSlim _singleWriter = new SemaphoreSlim(1);

        protected SimplPipeline(IDuplexPipe pipe)
        {
            _pipe = pipe;
            _reader = _pipe.Input;
            _writer = _pipe.Output;
        }

        #region 读方法
        protected async Task StartReceiveLoopAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var readResult = await _reader.ReadAsync(cancellationToken);
                    if (readResult.IsCanceled)
                        break;

                    var makingProgress = false;
                    var buffer = readResult.Buffer;
                    while (TryParseFrame(ref buffer, out var payload, out var messageId))
                    {
                        makingProgress = true;
                        await OnReceiveAsync(payload, messageId);
                    }
                    _reader.AdvanceTo(buffer.Start, buffer.End);

                    if (!makingProgress && readResult.IsCompleted)
                        break;
                }

                try { _reader.Complete(); } catch { }
            }
            catch (Exception ex)
            {
                try { _reader.Complete(ex); } catch { }
            }
        }

        private bool TryParseFrame(ref ReadOnlySequence<byte> input,
            out ReadOnlySequence<byte> payload, out int messageId)
        {
            if (input.Length < 8)
            {   // not enough data for the header
                payload = default;
                messageId = default;
                return false;
            }

            int length;
            if (input.First.Length >= 8)
            {   // already 8 bytes in the first segment
                length = ParseFrameHeader(
                    input.First.Span, out messageId);
            }
            else
            {   // copy 8 bytes into a local span
                Span<byte> local = stackalloc byte[8];
                input.Slice(0, 8).CopyTo(local);
                length = ParseFrameHeader(
                    local, out messageId);
            }

            // do we have the "length" bytes?
            if (input.Length < length + 8)
            {
                payload = default;
                return false;
            }

            // success!
            payload = input.Slice(8, length);
            input = input.Slice(payload.End);
            return true;
        }

        protected abstract ValueTask OnReceiveAsync(ReadOnlySequence<byte> payload, int messageId);
        #endregion

        #region 写方法
        protected ValueTask WriteAsync(IMemoryOwner<byte> payload, int messageId)
        {
            using (payload)
            {
                return WriteAsync(payload.Memory, messageId);
            }
        }

        protected ValueTask WriteAsync(ReadOnlyMemory<byte> payload, int messageId)
        {
            if (!_singleWriter.Wait(0))
                return WriteAsyncSlowPath(payload, messageId);

            bool release = true;
            try
            {
                WriteFrameHeader(_writer, payload.Length, messageId);
                var writeResult = _writer.WriteAsync(payload);
                if (writeResult.IsCompletedSuccessfully)
                    return default;

                release = false;
                return AwaitFlushAndRelease(writeResult);
            }
            finally
            {
                if (release)
                    _singleWriter.Release();
            }
        }

        private async ValueTask WriteAsyncSlowPath(ReadOnlyMemory<byte> payload, int messageId)
        {
            await _singleWriter.WaitAsync();
            try
            {
                WriteFrameHeader(_writer, payload.Length, messageId);
                await _writer.WriteAsync(payload);
            }
            finally
            {
                _singleWriter.Release();
            }
        }

        private async ValueTask AwaitFlushAndRelease(ValueTask<FlushResult> flush)
        {
            try { await flush; }
            finally { _singleWriter.Release(); }
        }

        /// <summary>
        /// 8 bytes composed of two little-endian 32-bit integers. The first 4 bytes 
        /// contains the payload length in bytes; the second 4 bytes is the messageId 
        /// used to correlate requests and responses
        /// </summary>
        private void WriteFrameHeader(PipeWriter writer, int length, int messageId)
        {
            var span = writer.GetSpan(8);
            BinaryPrimitives.WriteInt32LittleEndian(span, length);
            BinaryPrimitives.WriteInt32LittleEndian(span.Slice(4), messageId);
            writer.Advance(8);
        }
        #endregion

        private int ParseFrameHeader(ReadOnlySpan<byte> input, out int messageId)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(input);
            messageId = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(4));

            return length;
        }

        public void Dispose() => Close();

        public void Close() {/* burn the pipe*/}
    }
}
