using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{

    public class Performance : Dictionary<string, Performance.PerformanceItem>
    {
        public static Performance Perf = new Performance();

        public PerformanceToken CurrentToken = null;

        public class PerformanceItem
        {
            public double Sum { get; private set; }
            public double Sum2 { get; private set; }
            public double Cnt { get; private set; }
            public double Avg => Sum / Cnt;

            public void Add(double ticks)
            {
                Sum += ticks;
                Sum2 += ticks * ticks;
                Cnt++;
            }
        }

        public void Add(string name, double ticks)
        {
            PerformanceItem i;
            if (!TryGetValue(name, out i))
                Add(name, i = new PerformanceItem());
            i.Add(ticks);
        }

        /// <summary>
        /// Prevod surovych tiku <see cref="Stopwatch"/> na milisekundy.
        ///
        /// <para><b>Nesmi to byt <c>new TimeSpan(ticks)</c>:</b> <see cref="Stopwatch.ElapsedTicks"/>
        /// jsou tiky v jednotkach <see cref="Stopwatch.Frequency"/>, ne 100ns tiky
        /// <see cref="TimeSpan"/>. Na Windows to vychazi nastejno jen shodou okolnosti (QPC 10 MHz),
        /// na Linux/ARM64 je Frequency 1 GHz a namerene casy by byly <b>100x delsi</b>.
        /// Stejna zamena zpusobila, ze cely <see cref="TimeBase"/> bezel na OrangePi 100x rychleji
        /// (nalezeno 31. 8. 2026, viz doc/decisions.md).</para>
        /// </summary>
        private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach(var kv in this)
            {
                sb.AppendLine(string.Format("{0} - {1}", kv.Key, kv.Value.Avg * TickToMs));
            }
            return sb.ToString();
        }
    }
    public class PerformanceToken : IDisposable
    {
        public static Stopwatch SW { get; private set; }
        public string Name { get; private set; }
        public PerformanceToken Parent { get; private set; }
        public Performance Session { get; private set; }
        private long ticks;

        static PerformanceToken()
        {
            SW = new Stopwatch();
            SW.Start();
        }

        public PerformanceToken(string name) : this(Performance.Perf, name)
        {
        }
        public PerformanceToken(Performance session, string name)
        {
            Session = session;
            lock (session)
            {
                Parent = session.CurrentToken;
                session.CurrentToken = this;
                Name = (Parent != null ? Parent.Name + "." : "") + name;
            }
            ticks = SW.ElapsedTicks;
        }

        public void Dispose()
        {
            lock (Session)
            {
                Session.Add(Name, SW.ElapsedTicks- ticks);
                Session.CurrentToken = Parent;
                Parent = null;
            }
        }
    }
}
