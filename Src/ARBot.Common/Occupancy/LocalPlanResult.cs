using System;
using ARBot.Common.Regulators;

namespace ARBot.Common.Occupancy
{
    /// <summary>Duvod, proc lokalni plan nevznikl (nebo vznikl jen castecne).</summary>
    public enum LocalPlanStatus
    {
        /// <summary>Plan vede az do zadaneho cile.</summary>
        Ok = 0,

        /// <summary>Cil je mimo grid nebo za horizontem - plan vede k nejlepsi dosazitelne bunce
        /// ve smeru k cili. Bezna situace pri jizde k dalekemu cili.</summary>
        Partial = 1,

        /// <summary>Robot uz je v cili (v ramci tolerance) - neni co planovat.</summary>
        AlreadyAtGoal = 2,

        /// <summary>Robot je mimo grid (grid nebyl vycentrovan na jeho polohu).</summary>
        RobotOutsideGrid = 3,

        /// <summary>Robot stoji v neprujezdne bunce - nelze bezpecne odjet.</summary>
        RobotBlocked = 4,

        /// <summary>Z pozice robotu nevede zadna prujezdna cesta (vse v dosahu je neprujezdne).</summary>
        NoRoute = 5,

        /// <summary>
        /// NOUZOVE ZASTAVENI: novy plan nevznikl a draha, po ktere robot prave jede, uz podle
        /// AKTUALNI mapy koliduje (v dosahu brzdne drahy). Regulator se zahodil -&gt; robot stoji.
        /// </summary>
        AbortedCollision = 6,
    }

    /// <summary>
    /// Vysledek lokalniho planovani (<see cref="LocalPathPlanner"/>).
    /// <see cref="WayPoints"/> jsou vstupem pro <see cref="IPathPlanner.Plan"/>.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public sealed class LocalPlanResult
    {
        /// <summary>Stav planovani.</summary>
        public LocalPlanStatus Status;

        /// <summary>Lze podle vysledku ridit? (Plan existuje a ma alespon 2 body.)</summary>
        public bool HasPath => (Status == LocalPlanStatus.Ok || Status == LocalPlanStatus.Partial)
                               && WayPoints != null && WayPoints.Length >= 2;

        /// <summary>Waypointy pro <see cref="IPathPlanner.Plan"/> (prvni = aktualni poloha robotu);
        /// null, kdyz plan nevznikl.</summary>
        public RegulatorWayPoint[] WayPoints;

        /// <summary>Pozadovany cil, jak ho zadal volajici [m, world ENU].</summary>
        public double RequestedGoalX;
        /// <summary>Pozadovany cil, jak ho zadal volajici [m, world ENU].</summary>
        public double RequestedGoalY;

        /// <summary>Cil, ke kteremu plan skutecne vede (po oriznuti na grid / horizont) [m].</summary>
        public double ReachedGoalX;
        /// <summary>Cil, ke kteremu plan skutecne vede (po oriznuti na grid / horizont) [m].</summary>
        public double ReachedGoalY;

        /// <summary>Cena nalezene drahy [s] (jizdni cas vcetne pocatecniho otoceni).</summary>
        public double CostSeconds;

        /// <summary>Delka nalezene drahy [m].</summary>
        public double LengthM;

        /// <summary>Pocet bunek expandovanych v A* (diagnostika vykonu).</summary>
        public int ExpandedCells;

        /// <summary>Nejmensi odstup od neprujezdneho podel cele drahy [m] (diagnostika bezpecnosti).</summary>
        public double MinClearanceM;

        /// <summary>Doba vypoctu [ms] (integrace snimku + EDT + A*), plni <c>LocalNavigator</c>.</summary>
        public double ComputeMs;

        /// <summary>Cas pozy, ze ktere se planovalo.</summary>
        public DateTime TimeStamp;

        /// <summary>
        /// Prevod na zpravu pro vizualizaci a zaznam (konverzi vlastni domena - viz CLAUDE.md).
        /// </summary>
        public Logs.LocalPlanMsg ToLogMessage() => new Logs.LocalPlanMsg
        {
            Status = (int)Status,
            RequestedGoalX = RequestedGoalX,
            RequestedGoalY = RequestedGoalY,
            ReachedGoalX = ReachedGoalX,
            ReachedGoalY = ReachedGoalY,
            CostSeconds = CostSeconds,
            LengthM = LengthM,
            MinClearanceM = MinClearanceM,
            ExpandedCells = ExpandedCells,
            ComputeMs = ComputeMs,
            WayPoints = WayPoints,
            TimeStamp = TimeStamp,
        };
    }
}
