using System;
using ARBot.Common.Logs;
using ARBot.Common.Regulators;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Rychlostni obalka naplanovane drahy jako funkce VZDALENOSTI od robota: pro kazdy waypoint
    /// kumulativni delka drahy <see cref="S"/> [m] a strop rychlosti <see cref="V"/> [m/s], ktery
    /// mu planovac predepsal (<c>RegulatorWayPoint.Speed</c>). Kresli ji World pohled jako maly
    /// graf v rohu mapy a podle tehoz stropu barvi useky planu - viz doc/world-view.md.
    ///
    /// <para><b>Proc vzdalenost, ne cas:</b> obalka je geometricka vlastnost drahy (odstup od
    /// prekazek, hranice potvrzene sjizdneho), takze se cte "za kolik metru mne co pribrzdi".
    /// Cas by zavisel na tom, jak rychle robot skutecne pojede.</para>
    ///
    /// <para>Je to cisty vypocet nad <see cref="LocalPlanMsg"/> (bez UI), aby sel testovat. Delky se
    /// pocitaji z lokalnich metrickych souradnic waypointu (world ENU), ne z Mercatoru - ten je
    /// v metrech jen priblizne.</para>
    /// </summary>
    public sealed class PlanSpeedProfile
    {
        /// <summary>Kumulativni vzdalenost od robota po drahe [m]; <c>S[0] = 0</c> (prvni waypoint je robot).</summary>
        public double[] S { get; }

        /// <summary>Strop rychlosti v uzlu [m/s]. Posledni uzel muze byt 0 = zastaveni na konci.</summary>
        public double[] V { get; }

        /// <summary>Horni mez osy rychlosti [m/s] - strop rizeni (<c>Profile.MaxAllowedSpeed</c>), nebo vic, kdyz ho plan prekracuje.</summary>
        public double VMax { get; }

        /// <summary>Aktualni rychlost robota z fuze [m/s] (NaN = neznama) - kresli se jako znacka v s = 0.</summary>
        public double RobotV { get; }

        /// <summary>Stav planu, ze ktereho profil je.</summary>
        public LocalPlanStatus Status { get; }

        /// <summary>Nejmensi odstup od neprujezdneho podel drahy [m] (z planu).</summary>
        public double MinClearanceM { get; }

        /// <summary>Pocet uzlu.</summary>
        public int Count => S.Length;

        /// <summary>Celkova delka drahy [m].</summary>
        public double LengthM => S.Length == 0 ? 0 : S[S.Length - 1];

        /// <summary>Konci draha zastavenim (posledni strop 0)?</summary>
        public bool StopsAtEnd => V.Length > 0 && V[V.Length - 1] <= 0;

        /// <summary>Nejnizsi strop mezi MEZILEHLYMI uzly [m/s] (koncova nula se nepocita) - "kde to brzdi nejvic".</summary>
        public double MinIntermediateV
        {
            get
            {
                double min = double.MaxValue;
                int last = V.Length - 1;
                for (int i = 0; i < last; i++) min = Math.Min(min, V[i]);
                return min == double.MaxValue ? double.NaN : min;
            }
        }

        private PlanSpeedProfile(double[] s, double[] v, double vMax, double robotV,
                                 LocalPlanStatus status, double minClearance)
        {
            S = s;
            V = v;
            VMax = vMax;
            RobotV = robotV;
            Status = status;
            MinClearanceM = minClearance;
        }

        /// <summary>
        /// Postavi profil ze zpravy planu. Vraci <c>null</c>, kdyz plan nema drahu (mene nez dva
        /// waypointy) - pak neni co kreslit.
        /// </summary>
        /// <param name="plan">Zprava planu.</param>
        /// <param name="robotV">Aktualni rychlost robota [m/s], NaN kdyz neni znama.</param>
        /// <param name="vMax">Strop rizeni [m/s] pro meritko osy; kdyz ho nektery uzel prekracuje, osa se roztahne.</param>
        public static PlanSpeedProfile From(LocalPlanMsg plan, double robotV, double vMax)
        {
            if (plan == null) return null;
            return From(plan.WayPoints, robotV, vMax, plan.PlanStatus, plan.MinClearanceM);
        }

        /// <summary>Totez nad samotnymi waypointy (pro testy a pro pripad, kdy zprava jeste neni).</summary>
        public static PlanSpeedProfile From(RegulatorWayPoint[] wps, double robotV, double vMax,
                                            LocalPlanStatus status, double minClearance)
        {
            if (wps == null || wps.Length < 2) return null;

            var s = new double[wps.Length];
            var v = new double[wps.Length];
            double top = vMax > 0 ? vMax : 0;
            for (int i = 0; i < wps.Length; i++)
            {
                if (i > 0)
                {
                    double dx = wps[i].X - wps[i - 1].X, dy = wps[i].Y - wps[i - 1].Y;
                    s[i] = s[i - 1] + Math.Sqrt(dx * dx + dy * dy);
                }
                v[i] = Math.Max(0, wps[i].Speed);
                if (v[i] > top) top = v[i];
            }
            if (top <= 0) top = 1;   // degenerovany plan (vsude 0) - at ma osa nenulovy rozsah

            return new PlanSpeedProfile(s, v, top, robotV, status, minClearance);
        }

        /// <summary>
        /// Strop rychlosti USEKU <c>k → k+1</c> pro obarveni v mape: rychlost, se kterou se z uzlu
        /// <c>k</c> odjizdi. Koncova nula posledniho uzlu usek nebarvi - brzdeni k ni je uz
        /// zapocitane v predchazejicich uzlech (brzdna obalka).
        /// </summary>
        public double SegmentV(int k)
        {
            if (k < 0 || k >= V.Length - 1) throw new ArgumentOutOfRangeException(nameof(k));
            return V[k];
        }

        /// <summary>
        /// Normalizovana rychlost 0..1 vuci <see cref="VMax"/> - vstup pro barevnou skalu
        /// (stejnou v mape i v grafu, aby si barvy odpovidaly).
        /// </summary>
        public double Normalized(double v) => VMax <= 0 ? 0 : Math.Clamp(v / VMax, 0, 1);
    }
}
