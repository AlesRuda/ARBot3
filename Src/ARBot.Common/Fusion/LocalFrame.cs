using System;
using ARBot.Common.Common;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Soustava, ve ktere pracuje <b>lokalni vrstva</b> (occupancy grid, lokalni planovac, regulator
    /// drahy). Prepina ji parametr <c>localframe=</c>; tema <c>lp-grid-odometricka-soustava</c>
    /// v <c>doc/ukoly.yaml</c>, popis v <c>doc/occupancy-and-local-planning.md</c>.
    /// </summary>
    public enum LocalFrame : byte
    {
        /// <summary>Svet (globalni poza z fuze). Puvodni chovani: korekce pozy posouvaji grid.</summary>
        World = 0,

        /// <summary>
        /// Odometricka soustava (<see cref="OdomPose"/>): spojita, korekce do ni neskacou, takze
        /// obsah gridu se proti robotu neposouva. Cil a zobrazeni prevadi
        /// <see cref="FrameTransform"/> odom → svet.
        /// </summary>
        Odom = 1,
    }

    /// <summary>
    /// Rigidni transformace z lokalni soustavy do sveta: <c>svet = R(DTheta)·lokal + (DX, DY)</c>.
    /// U <see cref="LocalFrame.World"/> je to identita. Meni se jen korekcemi pozy, mezi nimi stoji.
    /// </summary>
    public readonly record struct FrameTransform(double DX, double DY, double DTheta)
    {
        /// <summary>Identita (lokalni soustava = svet).</summary>
        public static FrameTransform Identity => default;

        /// <summary>Je to identita? (Pak se nic neprevadi ani neprevzorkovava.)</summary>
        public bool IsIdentity => DX == 0 && DY == 0 && DTheta == 0;

        /// <summary>Bod z lokalni soustavy do sveta.</summary>
        public (double X, double Y) ToWorld(double x, double y)
        {
            if (IsIdentity) return (x, y);
            double c = Math.Cos(DTheta), s = Math.Sin(DTheta);
            return (c * x - s * y + DX, s * x + c * y + DY);
        }

        /// <summary>Bod ze sveta do lokalni soustavy (inverze <see cref="ToWorld"/>).</summary>
        public (double X, double Y) ToLocal(double x, double y)
        {
            if (IsIdentity) return (x, y);
            double c = Math.Cos(DTheta), s = Math.Sin(DTheta);
            double dx = x - DX, dy = y - DY;
            return (c * dx + s * dy, -s * dx + c * dy);
        }

        /// <summary>Orientace z lokalni soustavy do sveta.</summary>
        public double AngleToWorld(double theta) => IsIdentity ? theta : Conversions.NormalizeOrientation(theta + DTheta);
    }
}
