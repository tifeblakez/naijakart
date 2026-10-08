using System;
using System.Threading;

namespace NaijaKart.Core.Util
{
    /// <summary>Process-local monotonic id source for transactions, events and hazards.</summary>
    public static class IdGenerator
    {
        private static long _counter;

        public static long Next() => Interlocked.Increment(ref _counter);

        public static string NextString(string prefix) => prefix + "_" + Next().ToString("x");
    }
}
