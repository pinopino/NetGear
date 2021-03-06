﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO.Pipelines;
using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Pipelines
{
    public abstract class DuplexPipe : IDisposable
    {
        private static ILogger _logger;
        private IDuplexPipe _pipe;
        private readonly SemaphoreSlim _singleWriter = new SemaphoreSlim(1);

        internal PipeReader Input { get { return _pipe.Input; } }
        internal PipeWriter Output { get { return _pipe.Output; } }

        protected DuplexPipe(IDuplexPipe pipe, ILogger logger = null)
        {
            if (pipe == null)
                throw new ArgumentNullException(nameof(pipe));

            _pipe = pipe;
            _logger = logger ?? NullLoggerFactory.Instance.CreateLogger("NetGear.Pipelines.DuplexPipe");
        }

        protected DuplexPipe()
        { }

        protected void SetPipe(IDuplexPipe pipe)
        {
            if (pipe == null)
                throw new ArgumentNullException(nameof(pipe));

            _pipe = pipe;
        }

        #region 读方法
        protected async Task StartReceiveLoopAsync(CancellationToken cancellationToken = default)
        {
            var reader = _pipe?.Input ?? throw new ObjectDisposedException(ToString());
            try
            {
                var makingProgress = false;
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (!(makingProgress && reader.TryRead(out var readResult)))
                        readResult = await reader.ReadAsync(cancellationToken);

                    if (readResult.IsCanceled)
                        break;

                    var buffer = readResult.Buffer;
                    makingProgress = false;
                    // handle as many frames from the data as we can
                    // (note: an alternative strategy is handle one frame
                    // and release via AdvanceTo as soon as possible)
                    while (TryParseFrame(ref buffer, out var payload, out var messageId))
                    {
                        makingProgress = true;
                        await OnReceiveAsync(payload, messageId);
                    }
                    // record that we comsumed up to the (now updated) buffer.Start,
                    // and tried to look at everything - hence buffer.End
                    reader.AdvanceTo(buffer.Start, buffer.End);

                    // exit the loop electively, or because we've consumed everything
                    // that we can usefully consume
                    if (!makingProgress && readResult.IsCompleted)
                        break;
                }

                try { reader.Complete(); } catch { }
            }
            catch (Exception ex)
            {
                try { reader.Complete(ex); } catch { }
            }
        }

        protected abstract ValueTask OnReceiveAsync(ReadOnlySequence<byte> payload, int messageId);
        #endregion

        #region 写方法
        protected ValueTask WriteAsync(IMemoryOwner<byte> memory, int messageId)
        {
            async ValueTask AwaitPending(IMemoryOwner<byte> mmemory, ValueTask write)
            {
                using (mmemory)
                {
                    await write;
                }
            }

            try
            {
                var writeResult = WriteAsync(memory.Memory, messageId);
                if (writeResult.IsCompletedSuccessfully)
                    return default;

                var final = AwaitPending(memory, writeResult);
                memory = null;
                return final;
            }
            finally
            {
                if (memory != null)
                    try { memory.Dispose(); } catch { }
            }
        }

        protected ValueTask WriteAsync(ReadOnlyMemory<byte> payload, int messageId)
        {
            if (!_singleWriter.Wait(0))
                return WriteAsyncSlowPath(payload, messageId);

            bool release = true;
            try
            {
                var writer = _pipe?.Output ?? throw new ObjectDisposedException(ToString());
                WriteFrameHeader(writer, payload.Length, messageId);
                var writeResult = writer.WriteAsync(payload);
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
                var writer = _pipe?.Output ?? throw new ObjectDisposedException(ToString());
                WriteFrameHeader(writer, payload.Length, messageId);
                await writer.WriteAsync(payload);
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
        #endregion

        #region 协议解析
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

        private int ParseFrameHeader(ReadOnlySpan<byte> input, out int messageId)
        {
            var length = BinaryPrimitives.ReadInt32LittleEndian(input);
            messageId = BinaryPrimitives.ReadInt32LittleEndian(input.Slice(4));

            return length;
        }
        #endregion

        public void Dispose() => Close();

        public void Close(Exception ex = null)
        {
            var pipe = _pipe;
            _pipe = null;

            if (pipe != null)
            {
                // burn the pipe to the ground
                try { pipe.Input.Complete(ex); } catch { }
                try { pipe.Input.CancelPendingRead(); } catch { }
                try { pipe.Output.Complete(ex); } catch { }
                try { pipe.Output.CancelPendingFlush(); } catch { }

                if (pipe is IDisposable d)
                    try { d.Dispose(); } catch { }
            }
            try { _singleWriter.Dispose(); } catch { }
        }
    }
}
