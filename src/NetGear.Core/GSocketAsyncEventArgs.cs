using NetGear.Core.Threading;
using System;
using System.Net.Sockets;

namespace NetGear.Core
{
    public class Token
    {
        public int Op;
        public int Count;
        public int Read;
        public int Send;
        public int Offset;
        public byte[] Bytes;
        public bool RentFromPool;
        public Action<int> Continuation;

        public void Reset()
        {
            Op = 0;
            Count = 0;
            Read = 0;
            Send = 0;
            Offset = 0;
            Bytes = null;
            RentFromPool = false;
            Continuation = null;
        }
    }

    public class GSocketAsyncEventArgs : SocketAsyncEventArgs
    {
        IScheduler _scheduler;

        public IScheduler Scheduler
        {
            set { _scheduler = value; }
            get { return _scheduler; }
        }

        public new Token UserToken
        {
            set { base.UserToken = value; }
            get { return (Token)base.UserToken; }
        }

        public new event EventHandler<GSocketAsyncEventArgs> Completed;

        public GSocketAsyncEventArgs()
        {
        }

        public GSocketAsyncEventArgs(IScheduler scheduler)
        {
            if (scheduler == null)
                throw new ArgumentNullException("scheduler");

            _scheduler = scheduler;
        }

        protected override void OnCompleted(SocketAsyncEventArgs e)
        {
            if (this.Completed != null)
            {
                var g = e as GSocketAsyncEventArgs;
                if (g != null)
                {
                    if (_scheduler != null)
                    {
                        _scheduler.QueueTask(p => this.Completed(this, g), g);
                    }
                    else
                    {
                        this.Completed(this, g);
                    }
                }
            }
        }
    }
}
