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

        // --- Rozpad rychlostni obalky PO UZLECH (verze 2) ---
        // Kazdy waypoint uz nese vyslednou Speed; tohle je jeji ROZPAD, tedy PROC je zrovna takova.
        // Po uzlech, ne jako minimum pres plan: rozdil mezi "leze uz u sebe" a "za dva metry se
        // cesta zuzuje" je pro lecbu podstatny a jedno cislo ho splacne. Do verze 1 se rozpad
        // pocital, ale zustaval v LocalPlanResult, tedy jen v pameti bezici aplikace; ze zaznamu
        // se dal jen REKONSTRUOVAT z gridu (ARBot.Analyze envelope).
        // Uklada se jako float - jsou to metry a rychlosti, kde je krok gridu 5 cm, takze dvojnasobna
        // presnost by jen zdvojnasobila misto (5 hodnot na uzel, ~18 planu/s).
        // Viz doc/occupancy-and-local-planning.md.

        /// <summary>Odstup od nejblizsi neprujezdne bunky u i-teho waypointu [m]; null u verze 1.</summary>
        public float[] EnvClearanceM;

        /// <summary>Priblizovani k prekazce 0..1 ve vzorku, ktery u i-teho waypointu urcil strop
        /// z odstupu (0 = jede podel prekazky, 1 = primo na ni); null u verze 1.</summary>
        public float[] EnvClosing;

        /// <summary>Vzdalenost k prvni bunce, ktera neni <c>Free</c>, merena od i-teho waypointu
        /// DOPREDU [m]; null u verze 1. Male cislo = brzdna obalka vaze.</summary>
        public float[] EnvFreeAheadM;

        /// <summary>Strop z ODSTUPU u i-teho waypointu [m/s]; null u verze 1.</summary>
        public float[] EnvVClearance;

        /// <summary>Strop z BRZDNE OBALKY u i-teho waypointu [m/s]; null u verze 1.</summary>
        public float[] EnvVBrake;

        /// <summary>Nese zprava rozpad obalky? (Verze 1 ho nema.)</summary>
        public bool HasEnvelope => EnvVClearance != null && EnvVClearance.Length > 0;

        /// <summary>Nejmensi vzdalenost k hranici potvrzene sjizdneho podel drahy [m].</summary>
        public double MinFreeAheadM => MinOverInner(EnvFreeAheadM);

        /// <summary>Nejnizsi strop z ODSTUPU od prekazek podel drahy [m/s].</summary>
        public double MinVClear => MinOverInner(EnvVClearance);

        /// <summary>Nejnizsi strop z BRZDNE OBALKY podel drahy [m/s].</summary>
        public double MinVBrake => MinOverInner(EnvVBrake);

        /// <summary>Nejnizsi predepsana rychlost MEZILEHLEHO uzlu [m/s] (uz vcetne podlahy
        /// <c>MinCostSpeed</c>). Rovna-li se podlaze, robot leze.</summary>
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

        /// <summary>Ktery clen obalky vazal - jen kdyz <see cref="HasEnvelope"/>.</summary>
        public string SpeedLimitedBy
            => !HasEnvelope ? "(neznamo - zaznam verze 1)"
             : MinVBrake <= MinVClear ? "VBrake (hranice potvrzeneho)" : "VClear (odstup od prekazek)";

        /// <summary>
        /// Minimum pres MEZILEHLE uzly (bez posledniho). Posledni uzel je z definice konec drahy -
        /// ma tam freeAhead 0, takze by minimum vzdycky vyslo tam a nic by nereklo.
        /// </summary>
        private static double MinOverInner(float[] v)
        {
            if (v == null || v.Length < 2) return double.NaN;
            double min = double.MaxValue;
            for (int i = 0; i < v.Length - 1; i++) if (v[i] < min) min = v[i];
            return min;
        }
        /// <summary>Waypointy drahy; null nebo prazdne, kdyz plan nevznikl.</summary>
        public RegulatorWayPoint[] WayPoints;
        /// <summary>Cas pozy, ze ktere se planovalo.</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        /// <summary>Typovany pohled na <see cref="Status"/>.</summary>
        public LocalPlanStatus PlanStatus => (LocalPlanStatus)Status;

        /// <summary>Verze formatu serializace (viz doc/record-replay.md -> Verzovani zprav).
        /// <para><b>Verze 2</b> (2026-09-07) pridala <b>rozpad rychlostni obalky</b>
        /// (<see cref="MinFreeAheadM"/>, <see cref="MinVClear"/>, <see cref="MinVBrake"/>,
        /// <see cref="MinWayPointSpeed"/>).</para></summary>
        public const int FormatVersion = 2;

        public LocalPlanMsg() : base("LocalPlanMsg", FormatVersion)
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

                // Verze 2: rozpad obalky TOHOTO uzlu. Zapisuje se u waypointu, takze delka pole
                // nemuze rozejit s poctem uzlu; kdyz rozpad chybi, jde tam NaN ("nevim"), ne nula.
                bw.Write(At(EnvClearanceM, i));
                bw.Write(At(EnvClosing, i));
                bw.Write(At(EnvFreeAheadM, i));
                bw.Write(At(EnvVClearance, i));
                bw.Write(At(EnvVBrake, i));
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

            // Verze 2 pridala rozpad obalky u kazdeho uzlu. Starsi zaznamy ho nemaji - pole
            // zustanou null a rozbor si rozpad musi REKONSTRUOVAT z gridu (ARBot.Analyze envelope),
            // ne tvrdit, ze byl nulovy.
            if (Verze >= 2)
            {
                EnvClearanceM = new float[n];
                EnvClosing = new float[n];
                EnvFreeAheadM = new float[n];
                EnvVClearance = new float[n];
                EnvVBrake = new float[n];
            }
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

                if (Verze >= 2)
                {
                    EnvClearanceM[i] = br.ReadSingle();
                    EnvClosing[i] = br.ReadSingle();
                    EnvFreeAheadM[i] = br.ReadSingle();
                    EnvVClearance[i] = br.ReadSingle();
                    EnvVBrake[i] = br.ReadSingle();
                }
            }
        }

        /// <summary>Prvek pole, nebo NaN, kdyz pole chybi nebo je kratsi (rozpad se neposila).</summary>
        private static float At(float[] v, int i)
            => v != null && i < v.Length ? v[i] : float.NaN;

        public override Message Build() => new LocalPlanMsg();

        public override string ToString()
            => $"LocalPlanMsg {PlanStatus} n={WayPoints?.Length ?? 0} len={LengthM:F2}m " +
               $"clr={MinClearanceM:F2}m cost={CostSeconds:F1}s {ComputeMs:F1}ms";
    }
}
