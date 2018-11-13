using NetGear.Core.Threading;
using System;
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

    public class ConnectionAbortedException : OperationCanceledException
    {
        public ConnectionAbortedException()
            : this("连接终止")
        {
        }

        public ConnectionAbortedException(string message)
            : base(message)
        {
        }

        public ConnectionAbortedException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }

    public abstract class BaseConnection : IDisposable
    {
        int _id;
        bool _debug;
        bool _disposed;

        protected const int NOT_STARTED = 1;
        protected const int STARTED = 2;
        protected const int SHUTTING_DOWN = 3;
        protected const int SHUTDOWN = 4;
        protected volatile int _execStatus;
        protected Socket _socket;

        internal int Id { get { return _id; } }
        #region 事件
        internal event EventHandler<ConnectionInfo> OnConnectionClosed;
        #endregion
        public static IScheduler Scheduler;

        static BaseConnection()
        {
            var concurrency = Math.Min(Environment.ProcessorCount, 12);
            Scheduler = new IOCompletionPortTaskScheduler(concurrency, concurrency);
        }

        public BaseConnection(int id, Socket socket, bool debug = false)
        {
            _id = id;
            _debug = debug;
            _disposed = false;
            _execStatus = NOT_STARTED;
            _socket = socket;
        }

        ~BaseConnection()
        {
            //必须为false
            Dispose(false);
        }

        public abstract void Start();

        public void Close()
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

        public void DoClose()
        {
            Close();
            Dispose();
            OnConnectionClosed?.Invoke(this, new ConnectionInfo { Num = _id, Description = string.Empty, Time = DateTime.Now });
        }

        public void DoAbort(string reason)
        {
            Close();
            Dispose();
            // todo: 直接被设置到task的result里面了，在listener的线程中抓不到这个异常
            // 类似的其它异常也需要注意这种情况
            throw new ConnectionAbortedException(reason);
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
            }

            // 清理非托管资源

            // 让类型知道自己已经被释放
            _disposed = true;
        }
    }
}
