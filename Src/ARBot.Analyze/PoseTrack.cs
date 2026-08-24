using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// Poza robota v case ze zaznamu — <b>odhad</b> (<see cref="RobotStateMsg"/>) i <b>skutecnost</b>
    /// (<see cref="GroundTruthMsg"/>, existuje jen u virtualniho HW).
    ///
    /// <para><b>Chyba lokalizace = skutecnost minus odhad pri SHODNEM razitku.</b> Obe zpravy
    /// emituje <c>ControlLoop</c> na temze tiku se stejnym casem, takze se nic neinterpoluje.
    /// Zprav je ale radove mensi hustota nez snimku kamer, takze pro cas snimku se hleda
    /// <b>nejblizsi</b> tik.</para>
    /// </summary>
    public sealed class PoseTrack
    {
        private readonly List<DateTime> times = new List<DateTime>();
        private readonly List<RobotStateMsg> est = new List<RobotStateMsg>();
        private readonly Dictionary<DateTime, GroundTruthMsg> truth = new Dictionary<DateTime, GroundTruthMsg>();

        /// <summary>Ma zaznam ground truth (virtualni HW)?</summary>
        public bool HasTruth => truth.Count > 0;

        public PoseTrack(RecordFile rec)
        {
            foreach (var e in rec.Index)
            {
                if (e.MsgName == "RobotStateMsg" && rec.Read(e) is RobotStateMsg r)
                {
                    times.Add(r.TimeStamp);
                    est.Add(r);
                }
                else if (e.MsgName == "GroundTruthMsg" && rec.Read(e) is GroundTruthMsg g)
                {
                    truth[g.TimeStamp] = g;
                }
            }
            // Index se zapisuje v poradi prichodu; pro binarni hledani je potreba cas.
            var order = Enumerable.Range(0, times.Count).OrderBy(i => times[i]).ToList();
            var t2 = order.Select(i => times[i]).ToList();
            var e2 = order.Select(i => est[i]).ToList();
            times.Clear(); times.AddRange(t2);
            est.Clear(); est.AddRange(e2);
        }

        /// <summary>Odhad pozy nejblizsi danemu casu (<c>null</c>, kdyz zaznam zadny nema).</summary>
        public RobotStateMsg Nearest(DateTime t)
        {
            if (est.Count == 0) return null;
            int i = times.BinarySearch(t);
            if (i >= 0) return est[i];
            i = ~i;
            if (i == 0) return est[0];
            if (i >= est.Count) return est[est.Count - 1];
            return (t - times[i - 1]) <= (times[i] - t) ? est[i - 1] : est[i];
        }

        /// <summary>
        /// Chyba lokalizace v case nejblizsiho tiku: <c>(dx, dy, dtheta)</c> jako
        /// skutecnost minus odhad. Vraci <c>false</c>, kdyz zaznam ground truth nema.
        /// </summary>
        public bool TryError(DateTime t, out double dx, out double dy, out double dTheta)
        {
            dx = dy = dTheta = 0;
            var e = Nearest(t);
            if (e == null || !truth.TryGetValue(e.TimeStamp, out var g)) return false;
            dx = g.X - e.X;
            dy = g.Y - e.Y;
            dTheta = Math.Atan2(Math.Sin(g.Theta - e.Theta), Math.Cos(g.Theta - e.Theta));
            return true;
        }
    }
}
