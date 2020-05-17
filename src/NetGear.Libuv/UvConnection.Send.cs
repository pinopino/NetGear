using System.Threading;
using System.Threading.Tasks;

namespace NetGear.Libuv
{
    public partial class UvConnection
    {
        public async Task WriteOutputAsync()
        {
            var pool = Thread.WriteReqPool;

            while (true)
            {
                var result = await _sendToUV.Reader.ReadAsync();

                var buffer = result.Buffer;
                var consumed = buffer.End;

                try
                {
                    if (result.IsCanceled)
                    {
                        break;
                    }

                    if (!buffer.IsEmpty)
                    {
                        var writeReq = pool.Allocate();

                        try
                        {
                            if (_socket.IsClosed)
                            {
                                break;
                            }

                            var writeResult = await writeReq.WriteAsync(_socket, buffer);

                            // This is not interlocked because there could be a concurrent writer.
                            // Instead it's to prevent read tearing on 32-bit systems.
                            Interlocked.Add(ref _totalBytesWritten, buffer.Length);

                            LogWriteInfo(writeResult.Status, writeResult.Error);

                            if (writeResult.Error != null)
                            {
                                consumed = buffer.Start;
                                throw writeResult.Error;
                            }
                        }
                        finally
                        {
                            // Make sure we return the writeReq to the pool
                            pool.Return(writeReq);

                            // Null out writeReq so it doesn't get caught by CheckUvReqLeaks.
                            // It is rooted by a TestSink scope through Pipe continuations in
                            // ResponseTests.HttpsConnectionClosedWhenResponseDoesNotSatisfyMinimumDataRate
                            writeReq = null;
                        }
                    }

                    if (result.IsCompleted)
                    {
                        break;
                    }
                }
                finally
                {
                    _sendToUV.Reader.AdvanceTo(consumed);
                }
            }
        }
    }
}
