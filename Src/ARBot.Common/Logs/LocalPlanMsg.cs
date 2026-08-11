using System;
using System.IO;
using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: vysledek lokalniho planovani (<see cref="LocalPlanResult"/>) - cil, waypointy
    /// a diagnostika. Umoznuje ve View zpetne videt, kudy chtel robot jet a proc pripadne nejel.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    [Serializable()]
    public class LocalPlanMsg : Message, IHasCaptureTime
    {
        /// <summary>Stav planovani (<see cref="LocalPlanStatus"/> jako int, aby zprava prezila
        /// pripadne doplneni hodnot vyctu).</summary>
        public int Status;
        /// <summary>Pozadovany cil [m, world ENU].</summary>
        public double RequestedGoalX;
        /// <summary>Pozadovany cil [m, world ENU].</summary>
        public double RequestedGoalY;
        /// <summary>Cil, ke kteremu plan skutecne vede (po oriznuti na grid/horizont) [m].</summary>
        public double ReachedGoalX;
        /// <summary>Cil, ke kteremu plan skutecne vede [m].</summary>
        public double ReachedGoalY;
        /// <summary>Cena drahy [s] (jizdni cas vcetne pocatecniho otoceni).</summary>
        public double CostSeconds;
        /// <summary>Delka drahy [m].</summary>
        public double LengthM;
        /// <summary>Nejmensi odstup od neprujezdneho podel drahy [m].</summary>
        public double MinClearanceM;
        /// <summary>Pocet bunek expandovanych v A* (diagnostika vykonu).</summary>
        public int ExpandedCells;
        /// <summary>Doba planovani [ms] (integrace + EDT + A*).</summary>
        public double ComputeMs;
        /// <summary>Waypointy drahy; null nebo prazdne, kdyz plan nevznikl.</summary>
        public RegulatorWayPoint[] WayPoints;
        /// <summary>Cas pozy, ze ktere se planovalo.</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        /// <summary>Typovany pohled na <see cref="Status"/>.</summary>
        public LocalPlanStatus PlanStatus => (LocalPlanStatus)Status;

        public LocalPlanMsg() : base("LocalPlanMsg", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Status);
            bw.Write(RequestedGoalX);
            bw.Write(RequestedGoalY);
            bw.Write(ReachedGoalX);
            bw.Write(ReachedGoalY);
            bw.Write(CostSeconds);
            bw.Write(LengthM);
            bw.Write(MinClearanceM);
            bw.Write(ExpandedCells);
            bw.Write(ComputeMs);
            Write(bw, TimeStamp);

            int n = WayPoints?.Length ?? 0;
            bw.Write(n);
            for (int i = 0; i < n; i++)
            {
                var w = WayPoints[i];
                bw.Write(w.X);
                bw.Write(w.Y);
                bw.Write(w.Speed);
                bw.Write(w.MaxPositionError);
                bw.Write(w.MaxSpeedError);
                bw.Write(w.Orientation.HasValue);
                if (w.Orientation.HasValue) bw.Write(w.Orientation.Value);
                bw.Write(w.MaxOrientationError);
            }
        }

        public override void FromData(BinaryReader br)
        {
            Status = br.ReadInt32();
            RequestedGoalX = br.ReadDouble();
            RequestedGoalY = br.ReadDouble();
            ReachedGoalX = br.ReadDouble();
            ReachedGoalY = br.ReadDouble();
            CostSeconds = br.ReadDouble();
            LengthM = br.ReadDouble();
            MinClearanceM = br.ReadDouble();
            ExpandedCells = br.ReadInt32();
            ComputeMs = br.ReadDouble();
            TimeStamp = ReadDateTime(br);

            int n = br.ReadInt32();
            WayPoints = new RegulatorWayPoint[n];
            for (int i = 0; i < n; i++)
            {
                var w = new RegulatorWayPoint
                {
                    X = br.ReadDouble(),
                    Y = br.ReadDouble(),
                    Speed = br.ReadDouble(),
                    MaxPositionError = br.ReadDouble(),
                    MaxSpeedError = br.ReadDouble(),
                };
                w.Orientation = br.ReadBoolean() ? br.ReadDouble() : (double?)null;
                w.MaxOrientationError = br.ReadDouble();
                WayPoints[i] = w;
            }
        }

        public override Message Build() => new LocalPlanMsg();

        public override string ToString()
            => $"LocalPlanMsg {PlanStatus} n={WayPoints?.Length ?? 0} len={LengthM:F2}m " +
               $"clr={MinClearanceM:F2}m cost={CostSeconds:F1}s {ComputeMs:F1}ms";
    }
}
