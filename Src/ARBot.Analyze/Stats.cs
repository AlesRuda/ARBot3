using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ARBot.Analyze
{
    /// <summary>Popisna statistika jedne veliciny — percentily, ne prumer.</summary>
    /// <remarks>
    /// <b>Proc percentily.</b> Rozdeleni chyb hranove lokalizace ma tezke chvosty (par cyklu
    /// s prolozenim mimo cestu utahne prumer o rad), takze prumer nerika nic uzitecneho.
    /// </remarks>
    public sealed class Stats
    {
        private readonly List<double> v = new List<double>();

        public string Name { get; }
        public Stats(string name) { Name = name; }

        public void Add(double x) { if (!double.IsNaN(x)) v.Add(x); }
        public int Count => v.Count;

        public double Percentile(double p)
        {
            if (v.Count == 0) return double.NaN;
            var s = v.OrderBy(x => x).ToList();
            double idx = p / 100.0 * (s.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            return lo == hi ? s[lo] : s[lo] + (s[hi] - s[lo]) * (idx - lo);
        }

        public double Median => Percentile(50);
        public double Mean => v.Count == 0 ? double.NaN : v.Average();
        public double Min => v.Count == 0 ? double.NaN : v.Min();
        public double Max => v.Count == 0 ? double.NaN : v.Max();

        /// <summary>Jednoradkovy souhrn: n, p50, p90, min, max, prumer.</summary>
        public string Line(string unit = "", double scale = 1.0)
        {
            if (v.Count == 0) return $"{Name,-28} n=0";
            return string.Format(CultureInfo.InvariantCulture,
                "{0,-28} n={1,4}  p50={2,8:F3}  p90={3,8:F3}  min={4,8:F3}  max={5,8:F3}  avg={6,8:F3} {7}",
                Name, v.Count, Median * scale, Percentile(90) * scale, Min * scale, Max * scale,
                Mean * scale, unit);
        }
    }
}
