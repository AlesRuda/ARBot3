using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Poskytuje prenejsi cas
    /// </summary>
    public static class TimeBase
    {
        private static Stopwatch sw = new Stopwatch();
        private static long start;

        static TimeBase()
        {
            start = DateTime.Now.Ticks;
            sw.Start();
        }
        /// <summary>
        /// Ziskava aktualni presnejsi cas.
        /// </summary>
        public static DateTime Now => new DateTime(start + sw.ElapsedTicks);
    }
}
