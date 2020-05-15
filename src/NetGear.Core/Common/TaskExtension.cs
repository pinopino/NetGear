using System;
using System.Threading.Tasks;

namespace NetGear.Core.Common
{
    public static class TaskExtension
    {
        public static void FireAndForget(this Task task)
           => task?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
    }
}
