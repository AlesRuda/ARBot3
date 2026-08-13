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

        /// <summary>Delka zbyvajici trasy [m].</summary>
        public double RouteLengthM;

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
            TimeStamp = new DateTime(br.ReadInt64());
        }

        public override string ToString()
            => $"GlobalNavMsg: Status={Status}, HasGoal={HasGoal}, Carrot=({CarrotX:F1},{CarrotY:F1}), "
             + $"OffRoute={OffRouteDist:F1} m, Route={RouteEdgeCount} hran / {RouteLengthM:F0} m";
    }
}
