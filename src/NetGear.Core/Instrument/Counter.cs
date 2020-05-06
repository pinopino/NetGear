namespace NetGear.Core.Instrument
{
    internal enum Counter
    {
        SocketGetBufferList,

        SocketSendAsyncSingleSync,
        SocketSendAsyncSingleAsync,
        SocketSendAsyncMultiSync,
        SocketSendAsyncMultiAsync,

        SocketPipeReadReadSync,
        SocketPipeReadReadAsync,
        SocketPipeFlushSync,
        SocketPipeFlushAsync,

        SocketReceiveSync,
        SocketReceiveAsync,
        SocketZeroLengthReceiveSync,
        SocketZeroLengthReceiveAsync,
        SocketSendAsyncSync,
        SocketSendAsyncAsync,

        SocketAwaitableCallbackNone,
        SocketAwaitableCallbackDirect,
        SocketAwaitableCallbackSchedule,

        ThreadPoolWorkerStarted,
        ThreadPoolPushedToMainThreadPool,
        ThreadPoolScheduled,
        ThreadPoolExecuted,

        PipeStreamWrite,
        PipeStreamWriteAsync,
        PipeStreamWriteByte,
        PipeStreamBeginWrite,
        PipeStreamWriteSpan,
        PipeStreamWriteAsyncMemory,

        PipeStreamRead,
        PipeStreamReadAsync,
        PipeStreamReadByte,
        PipeStreamBeginRead,
        PipeStreamReadSpan,
        PipeStreamReadAsyncMemory,

        PipeStreamFlush,
        PipeStreamFlushAsync,

        OpenReceiveReadAsync,
        OpenReceiveFlushAsync,
        OpenSendReadAsync,
        OpenSendWriteAsync,
        SocketConnectionCollectedWithoutDispose,
    }
}
