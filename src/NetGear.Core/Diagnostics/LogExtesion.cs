using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace NetGear.Core.Diagnostics
{
    public static class LogExtesion
    {
        [Conditional("VERBOSE")]
        public static void LogVerbose(this ILogger logger, string name, string message, [CallerMemberName] string caller = null)
        {
#if VERBOSE
            var thread = System.Threading.Thread.CurrentThread;
            var threadName = thread.Name;
            if (string.IsNullOrWhiteSpace(threadName)) threadName = thread.ManagedThreadId.ToString();

            var s = $"[{threadName}, {name}, {caller}]: {message}";
            _logger.LogDebug(s);
#endif
        }
    }
}
