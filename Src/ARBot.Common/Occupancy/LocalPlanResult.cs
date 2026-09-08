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

        /// <summary>
        /// UNIK: robot stoji v blokovane bunce NEBO tesne u prekazky (odstup pod SafeDist, od
        /// 3. 9. 2026 - drive to resila "eskapovaci zona") a plan vede k nejblizsi bunce, odkud muze
        /// pokracovat bezne planovani; tam zastavi. Neni to selhani - robot jede (pomalu, rychlostni
        /// obalka ho zdrzi sama).
        /// <para>Ven se smi jen pres bunky blokovane <b>semantikou</b>; pres geometricky blokovane
        /// nikdy. Viz doc/occupancy-and-local-planning.md.</para>
        /// </summary>
        EscapingBlocked = 7,

        /// <summary>
        /// Cil lezi v NEPRUJEZDNE bunce (<see cref="CellState.Blocked"/> - trava nebo prekazka).
        /// Plan vede k nejblizsi bezpecne bunce a tam ZASTAVI (koncova rychlost 0). Robot k cili
        /// nedojede nikdy - to je rozdil proti <see cref="Partial"/>, kde je cil legitimne jen za
        /// horizontem. Do 3. 9. 2026 se to hlasilo jako Partial a na konci drahy jako
        /// <see cref="AlreadyAtGoal"/>, coz maskovalo mrkev polozenou do travy.
        /// <para>Co s tim, rozhoduje producent cile (mise, globalni navigace), ne planovac -
        /// ten nevi, jestli cesta konci, nebo mrkev jen prestrelila zatacku.</para>
        /// </summary>
        GoalBlocked = 8,

        /// <summary>
        /// Cil je volna bunka, ale s odstupem pod SafeDist - lezi prilis blizko okraje. Plan vede
        /// k nejblizsi bezpecne bunce a tam zastavi. Cil je v zasade spravny, jen tesny; lecba je
        /// u producenta cile jina nez u <see cref="GoalBlocked"/>, proto zvlast.
        /// </summary>
        GoalUnsafe = 9,
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

        /// <summary>Lze podle vysledku ridit? (Plan existuje a ma alespon 2 body.) U GoalBlocked/GoalUnsafe
        /// draha vede k nejblizsi bezpecne bunce, ne k cili - ridit se podle ni ale da.</summary>
        public bool HasPath => (Status == LocalPlanStatus.Ok || Status == LocalPlanStatus.Partial
                                || Status == LocalPlanStatus.EscapingBlocked
                                || Status == LocalPlanStatus.GoalBlocked || Status == LocalPlanStatus.GoalUnsafe)
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

        // --- Diagnostika rychlostni obalky: PROC plan predepisuje zrovna takovou rychlost ---
        // Rychlost uzlu = max(MinCostSpeed, min(VClear(odstup, priblizovani), VBrake(freeAhead))).
        // Kdyz robot leze, je potreba vedet, ktery z clenu ji srazi - odstup od prekazek, mireni
        // NA prekazku, nebo hranice potvrzene sjizdne plochy - a hlavne KDE NA DRAZE se to stane.
        // Proto je rozpad PO UZLECH, ne jen minimum pres plan: "leze uz u sebe" a "za dva metry se
        // cesta zuzuje" jsou uplne jine situace, ktere jedno cislo splacne dohromady.
        // Viz doc/occupancy-and-local-planning.md.
        //
        // Vsechna pole maji delku jako WayPoints a index i patri i-temu waypointu; kdyz draha
        // nevznikla, jsou null.

        /// <summary>Odstup od nejblizsi neprujezdne bunky u uzlu [m] (minimum pres okno uzlu).</summary>
        public float[] EnvClearanceM { get; private set; }

        /// <summary>Priblizovani k prekazce 0..1 ve vzorku, ktery u uzlu urcil strop z odstupu:
        /// zaporny prumet smeru drahy do gradientu pole odstupu. 0 = jede podel, 1 = primo na ni.</summary>
        public float[] EnvClosing { get; private set; }

        /// <summary>Vzdalenost k prvni bunce, ktera neni <see cref="CellState.Free"/>, merena od
        /// uzlu DOPREDU [m]. Male cislo = brzdna obalka drzi rychlost dole.</summary>
        public float[] EnvFreeAheadM { get; private set; }

        /// <summary>Strop z ODSTUPU u uzlu [m/s] (<c>VEnvelope</c>, tedy uz zkombinovany podelny
        /// a kolmy clen; ktery z nich to byl, jde dopocitat z odstupu a priblizovani).</summary>
        public float[] EnvVClearance { get; private set; }

        /// <summary>Strop z BRZDNE OBALKY u uzlu [m/s] (<c>VBrake</c>).</summary>
        public float[] EnvVBrake { get; private set; }

        /// <summary>Naplni rozpad obalky (vola <c>LocalPathPlanner</c>).</summary>
        public void SetEnvelope(float[] clearance, float[] closing, float[] freeAhead,
                                float[] vClearance, float[] vBrake)
        {
            EnvClearanceM = clearance;
            EnvClosing = closing;
            EnvFreeAheadM = freeAhead;
            EnvVClearance = vClearance;
            EnvVBrake = vBrake;
        }

        /// <summary>Nese vysledek rozpad obalky?</summary>
        public bool HasEnvelope => EnvVClearance != null && EnvVClearance.Length > 0;

        /// <summary>Nejmensi <c>freeAhead</c> podel drahy [m].</summary>
        public double MinFreeAheadM => MinOverInner(EnvFreeAheadM);

        /// <summary>Nejnizsi strop z ODSTUPU podel drahy [m/s].</summary>
        public double MinVClear => MinOverInner(EnvVClearance);

        /// <summary>Nejnizsi strop z BRZDNE OBALKY podel drahy [m/s].</summary>
        public double MinVBrake => MinOverInner(EnvVBrake);

        /// <summary>Nejnizsi predepsana rychlost mezilehleho uzlu [m/s] (uz vcetne podlahy
        /// <c>MinCostSpeed</c>). Rovna-li se podlaze, robot jede nejpomaleji, jak plan dovoluje.</summary>
        public double MinWayPointSpeed
        {
            get
            {
                if (WayPoints == null || WayPoints.Length < 2) return double.NaN;
                double min = double.MaxValue;
                for (int i = 0; i < WayPoints.Length - 1; i++)
                    if (WayPoints[i].Speed < min) min = WayPoints[i].Speed;
                return min;
            }
        }

        /// <summary>Ktery clen rychlostni obalky vazal (diagnosticky popis pro log).</summary>
        public string SpeedLimitedBy
            => !HasEnvelope ? "(bez rozpadu)"
             : MinVBrake <= MinVClear ? "VBrake (hranice potvrzeneho)" : "VClear (odstup od prekazek)";

        /// <summary>
        /// Minimum pres MEZILEHLE uzly (bez posledniho). Posledni uzel je z definice konec drahy -
        /// ma tam freeAhead 0 a Speed 0, takze by minimum vzdycky vyslo tam a nic by nereklo.
        /// </summary>
        private static double MinOverInner(float[] v)
        {
            if (v == null || v.Length < 2) return double.NaN;
            double min = double.MaxValue;
            for (int i = 0; i < v.Length - 1; i++) if (v[i] < min) min = v[i];
            return min;
        }

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
            // Rozpad obalky PO UZLECH (zprava verze 2) - bez nej se ze zaznamu nedalo rict, PROC
            // plan predepsal takovou rychlost; slo to jen rekonstruovat z gridu. Viz LocalPlanMsg.
            EnvClearanceM = EnvClearanceM,
            EnvClosing = EnvClosing,
            EnvFreeAheadM = EnvFreeAheadM,
            EnvVClearance = EnvVClearance,
            EnvVBrake = EnvVBrake,
        };
    }
}
