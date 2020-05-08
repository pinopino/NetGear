using System;
using System.Threading.Tasks;

namespace NetGear.Core.Common
{
    internal static class TaskExtension
    {
        internal static void FireAndForget(this Task task)
           => task?.ContinueWith(t => GC.KeepAlive(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
    }
}
