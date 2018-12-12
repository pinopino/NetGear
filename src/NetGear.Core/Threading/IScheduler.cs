using System;

namespace NetGear.Core.Threading
{
    public struct Work
    {
        public Action<object> Callback;
        public object State;
    }

    public interface IScheduler
    {
        void QueueTask(Action<object> action, object state);
    }
}
