using NetGear.Core.Threading;
using System;
using System.Buffers;
using System.Net.Sockets;
using System.Threading;

namespace NetGear.Core.Connection
{
    public class ConnectionInfo
    {
        public int Num { set; get; }
        public string Description { set; get; }
        public DateTime Time { set; get; }

        public override string ToString()
        {
            return string.Format("Id：{0}，描述：[{1}]，时间：{2}", Num, Description == string.Empty ? "空" : Description, Time);
        }
    }

    public class ConnectionAbortedInfo
    {
        public string AbortReason { set; get; }
        public ConnectionInfo Connection { set; get; }

        public override string ToString()
        {
            return string.Format("Id：{0}，Abort原因：[{1}]，时间：{2}", Connection.Num, AbortReason, Connection.Time);
        }
    }

    public abstract class BaseConnection : IDisposable
    {
        int _id;
        bool _debug;
        bool _disposed;
        int _bufferSize;

        protected const int NOT_STARTED = 1;
        protected const int STARTED = 2;
        protected const int SHUTTING_DOWN = 3;
        protected const int SHUTDOWN = 4;
        protected volatile int _execStatus;
        protected Socket _socket;

        internal int Id { get { return _id; } }
        #region 事件
        internal event EventHandler<ConnectionInfo> OnConnectionClosed;
        internal event EventHandler<ConnectionAbortedInfo> OnConnectionAborted;
        #endregion
        private static IScheduler[] _schedulers;
        private static int _concurrency;
        protected IScheduler _scheduler;
        // todo: 如果不是在conn.ctor的时候初始化saea，而是在每次执行io时从池中获取saea，
        // 感觉上已经有点可以做IO合并的基础了？
        protected SocketAsyncEventArgs _readEventArgs;
        protected SocketAsyncEventArgs _sendEventArgs;

        static BaseConnection()
        {
            _concurrency = Math.Min(Environment.ProcessorCount, 16);
            _schedulers = new IOQueue[_concurrency];
            for (int i = 0; i < _concurrency; i++)
            {
                _schedulers[i] = new IOQueue();
            }
        }

        public BaseConnection(int id, Socket socket, int bufferSize, bool debug)
        {
            _id = id;
            _debug = debug;
            _disposed = false;
            _execStatus = NOT_STARTED;
            _socket = socket;
            _bufferSize = bufferSize;
            _scheduler = _schedulers[_id % _concurrency];
            InitSAEA();
        }

        ~BaseConnection()
        {
            // 必须为false
            Dispose(false);
        }

        public abstract void Start();

        /// <summary>
        /// 在继承类中覆写该方法时务必确定ReleaseSAEA()方法的逻辑能够与之配对
        /// </summary>
        protected virtual void InitSAEA()
        {
            _readEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs = new SocketAsyncEventArgs();
            _readEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
            _sendEventArgs.SetBuffer(ArrayPool<byte>.Shared.Rent(_bufferSize), 0, _bufferSize);
        }

        /// <summary>
        /// 在继承类中覆写该方法时务必确定InitSAEA()方法的逻辑能够与之配对
        /// </summary>
        protected virtual void ReleaseSAEA()
        {
            ArrayPool<byte>.Shared.Return(_readEventArgs.Buffer, true);
            ArrayPool<byte>.Shared.Return(_sendEventArgs.Buffer, true);
            _readEventArgs.Dispose();
            _sendEventArgs.Dispose();
        }

        public void Close()
        {
            DoClose();
            Dispose();
            OnConnectionClosed?.Invoke(this, new ConnectionInfo { Num = _id, Description = string.Empty, Time = DateTime.Now });
        }

        private void DoClose()
        {
            Interlocked.CompareExchange(ref _execStatus, SHUTTING_DOWN, STARTED);
            // close the socket associated with the client
            try
            {
                _socket.Shutdown(SocketShutdown.Send);
            }
            // throws if client process has already closed
            catch
            {
            }
            _socket.Close();
            Interlocked.CompareExchange(ref _execStatus, SHUTDOWN, SHUTTING_DOWN);
        }

        public void Abort(string reason)
        {
            DoClose();
            Dispose();
            OnConnectionAborted?.Invoke(this, new ConnectionAbortedInfo
            {
                AbortReason = reason,
                Connection = new ConnectionInfo { Num = _id, Description = string.Empty, Time = DateTime.Now }
            });
        }

        protected void Print(string message)
        {
            if (_debug)
            {
                Console.WriteLine(message);
            }
        }

        public void Dispose()
        {
            // 必须为true
            Dispose(true);
            // 通知垃圾回收机制不再调用终结器（析构器）
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }
            if (disposing)
            {
                // 清理托管资源
                _socket.Dispose();
                ReleaseSAEA();
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
