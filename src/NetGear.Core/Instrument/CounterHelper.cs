using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetGear.Core.Instrument
{
    internal static class CounterHelper
    {
#if DEBUG
        private static readonly int[] _counters = new int[Enum.GetValues(typeof(Counter)).Length];
        private static readonly Dictionary<string, int> _execCount = new Dictionary<string, int>();

        internal static void ResetCounters()
        {
            Array.Clear(_counters, 0, _counters.Length);
            lock (_execCount) { _execCount.Clear(); }
        }

        internal static string GetCounterSummary()
        {
            var enums = (Counter[])Enum.GetValues(typeof(Counter));
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < enums.Length; i++)
            {
                var count = Thread.VolatileRead(ref _counters[(int)enums[i]]);
                if (count != 0) sb.Append(enums[i]).Append(":\t").Append(count).AppendLine();
            }
            lock (_execCount)
            {
                foreach (var pair in _execCount)
                {
                    sb.Append(pair.Key).Append(":\t").Append(pair.Value).AppendLine();
                }
            }
            return sb.ToString();
        }
#endif
        [Conditional("DEBUG")]
        internal static void Incr(Counter counter)
        {
#if DEBUG
            Interlocked.Increment(ref _counters[(int)counter]);
#endif
        }

        [Conditional("DEBUG")]
        internal static void Decr(Counter counter)
        {
#if DEBUG
            Interlocked.Decrement(ref _counters[(int)counter]);
#endif
        }

        [Conditional("DEBUG")]
        internal static void Incr(MethodInfo method)
        {
#if DEBUG
            lock (_execCount)
            {
                var name = $"{method.DeclaringType.FullName}.{method.Name}";
                if (!_execCount.TryGetValue(name, out var count)) count = 0;
                _execCount[name] = count + 1;
            }
#endif
        }
    }
}
