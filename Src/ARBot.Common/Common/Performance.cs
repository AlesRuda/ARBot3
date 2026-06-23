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

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            foreach(var kv in this)
            {
                sb.AppendLine(string.Format("{0} - {1}", kv.Key, new TimeSpan((long)kv.Value.Avg).TotalMilliseconds));
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
