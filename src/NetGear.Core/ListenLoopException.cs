using System;
using System.Runtime.Serialization;

namespace NetGear.Core
{
    /// <summary>
    /// 监听循环出现为止错误了，通常表明服务端异常
    /// </summary>
    [Serializable]
    public sealed class ListenLoopException : InvalidOperationException
    {
        /// <summary>
        /// Create a new instance of ListenLoopException
        /// </summary>
        public ListenLoopException() : this("The connection was aborted") { }

        /// <summary>
        /// Create a new instance of ListenLoopException
        /// </summary>
        public ListenLoopException(string message) : base(message) { }

        /// <summary>
        /// Create a new instance of ListenLoopException
        /// </summary>
        public ListenLoopException(string message, Exception inner) : base(message, inner) { }

        private ListenLoopException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
