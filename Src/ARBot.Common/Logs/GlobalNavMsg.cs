using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: stav globalni navigace v jednom cyklu (viz doc/global-navigation-runtime.md).
    /// Mala, posila se kazdy cyklus - v zaznamu je pak zpetne videt cely pribeh navigace.
    /// Geometrii trasy nese <see cref="GraphNavigationMsg"/>, ta je vetsi a chodi rid ceji.
    /// </summary>
    public class GlobalNavMsg : Message, IHasCaptureTime
    {
        /// <summary>Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).</summary>
        public const int FormatVersion = 1;

        /// <summary>Stav navigace (hodnota <c>GlobalNavStatus</c>).</summary>
        public int Status;

        /// <summary>Je zadany cil?</summary>
        public bool HasGoal;

        /// <summary>Cil ve stupnich.</summary>
        public double GoalLatDeg, GoalLonDeg;

        /// <summary>Poloha robota ve stupnich (poza z fuze prevedena pres GeoReference).</summary>
        public double LatDeg, LonDeg;

        /// <summary>Mrkev predana lokalni vrstve [m, world ENU]; plati jen kdyz <see cref="HasCarrot"/>.</summary>
        public double CarrotX, CarrotY;

        /// <summary>Byla mrkev v tomto cyklu predana?</summary>
        public bool HasCarrot;

        /// <summary>Vzdalenost robota od site [m] (z <c>NavigationFix.OffRouteDist</c>).</summary>
        public double OffRouteDist;

        /// <summary>Pocet hran trasy k cili (0 = trasa neexistuje).</summary>
        public int RouteEdgeCount;

        /// <summary>
        /// Delka zbyvajici trasy [m] — soucet <c>LengthMeters</c> hran, kterymi trasa vede.
        ///
        /// <para><b>U CILE je presna</b> (od 26. 8. 2026): pulky rozriznute cilove hrany nesou
        /// skutecnou geometrickou delku. Do te doby mely nulu, takze posledni usek k cili se
        /// nezapocital vubec.</para>
        ///
        /// <para><b>Na ZACATKU je nadhodnocena</b> az o delku jedne hrany: <c>Router.Plan</c> vraci
        /// <b>cele</b> hrany, takze prvni z nich se zapocita i tou casti, ktera je uz za robotem.
        /// Je to vlastnost toho, ze trasa je seznam HRAN, ne polyline — na rozhodovani to nema vliv
        /// (gatuje se dosazitelnost), ale jako „vzdalenost do cile" to cislo mirne prestreluje.</para>
        /// </summary>
        public double RouteLengthM;

        /// <summary>
        /// Potencial postupu φ [s] - pri priblizovani k cili klesa, a to i kdyz robot prekazku
        /// objizdi (pole je goal-rooted). Proti vzdusne vzdalenosti poctiva mira postupu.
        /// </summary>
        public double Phi;

        /// <summary>Pocet uzavrenych / penalizovanych hran, kterym se robot vyhyba.</summary>
        public int ClosureCount;

        /// <summary>Cas cyklu.</summary>
        public DateTime TimeStamp;

        /// <inheritdoc/>
        public DateTime CaptureTime => TimeStamp;

        public GlobalNavMsg() : base("GlobalNavMsg", FormatVersion)
        {
        }

        /// <inheritdoc/>
        public override Message Build() => new GlobalNavMsg();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Status);
            bw.Write(HasGoal);
            bw.Write(GoalLatDeg);
            bw.Write(GoalLonDeg);
            bw.Write(LatDeg);
            bw.Write(LonDeg);
            bw.Write(HasCarrot);
            bw.Write(CarrotX);
            bw.Write(CarrotY);
            bw.Write(OffRouteDist);
            bw.Write(RouteEdgeCount);
            bw.Write(RouteLengthM);
            bw.Write(Phi);
            bw.Write(ClosureCount);
            bw.Write(TimeStamp.Ticks);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            Status = br.ReadInt32();
            HasGoal = br.ReadBoolean();
            GoalLatDeg = br.ReadDouble();
            GoalLonDeg = br.ReadDouble();
            LatDeg = br.ReadDouble();
            LonDeg = br.ReadDouble();
            HasCarrot = br.ReadBoolean();
            CarrotX = br.ReadDouble();
            CarrotY = br.ReadDouble();
            OffRouteDist = br.ReadDouble();
            RouteEdgeCount = br.ReadInt32();
            RouteLengthM = br.ReadDouble();
            Phi = br.ReadDouble();
            ClosureCount = br.ReadInt32();
            TimeStamp = new DateTime(br.ReadInt64());
        }

        public override string ToString()
            => $"GlobalNavMsg: Status={Status}, HasGoal={HasGoal}, Carrot=({CarrotX:F1},{CarrotY:F1}), "
             + $"OffRoute={OffRouteDist:F1} m, Route={RouteEdgeCount} hran / {RouteLengthM:F0} m";
    }
}
