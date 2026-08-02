namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Jeden úsek naplánované dráhy (mezi dvěma waypointy). Geometrie pro lokalizaci a řízení.
    /// Viz <c>doc/path-following.md</c>.
    /// </summary>
    public sealed class PathSegment
    {
        /// <summary>Počáteční bod úseku (world ENU) [m].</summary>
        public double StartX;
        public double StartY;
        /// <summary>Jednotkový směrový vektor úseku.</summary>
        public double DirX;
        public double DirY;
        /// <summary>Délka úseku [m].</summary>
        public double Length;
        /// <summary>Arc-length na počátku úseku (kumulativní od začátku trasy) [m].</summary>
        public double CumStart;
    }
}
