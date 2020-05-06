using System;
using System.Runtime.Serialization;

namespace NetGear.Core
{
    /// <summary>
    /// Indicates that a connection was aborted
    /// </summary>
    [Serializable]
    public sealed class ConnectionAbortedException : OperationCanceledException
    {
        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException() : this("The connection was aborted") { }

        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException(string message) : base(message) { }

        /// <summary>
        /// Create a new instance of ConnectionAbortedException
        /// </summary>
        public ConnectionAbortedException(string message, Exception inner) : base(message, inner) { }

        private ConnectionAbortedException(SerializationInfo info, StreamingContext context) : base(info, context) { }
    }
}
