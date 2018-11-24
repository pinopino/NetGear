using NetGear.Core.Threading;
using System;
using System.Net.Sockets;

namespace NetGear.Core
{
    public sealed class GSocketAsyncEventArgs<TToken> : SocketAsyncEventArgs
    {
        IScheduler _scheduler;

        public TToken Token
        {
            set { UserToken = value; }
            get { return (TToken)UserToken; }
        }

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
            if (_scheduler != null)
            {
                _scheduler.QueueTask(p => base.OnCompleted((SocketAsyncEventArgs)p), e);
            }
            else
            {
                base.OnCompleted(e);
            }
        }
    }
}
