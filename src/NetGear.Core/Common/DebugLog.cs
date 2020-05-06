using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NetGear.Core.Common
{
    internal static class Debugger
    {
#if DEBUG
        internal static System.IO.TextWriter Logger = System.IO.TextWriter.Null;
#endif

        [Conditional("VERBOSE")]
        internal static void Log(string name, string message, [CallerMemberName] string caller = null)
        {
#if VERBOSE
            var log = Logger;
            if (log != null)
            {
                var thread = System.Threading.Thread.CurrentThread;
                var threadName = thread.Name;
                if (string.IsNullOrWhiteSpace(threadName)) threadName = thread.ManagedThreadId.ToString();

                var s = $"[{threadName}, {name}, {caller}]: {message}";
                lock (log)
                {
                    try { log.WriteLine(s); }
                    catch { }
                }
            }
#endif
        }
    }
}
