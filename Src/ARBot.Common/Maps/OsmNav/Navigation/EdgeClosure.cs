using System;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Maps.OsmNav.Navigation
{
    /// <summary>
    /// Trvala identita hrany. <see cref="Edge.Index"/> plati jen pro jednu instanci
    /// <see cref="Graph.RoadNetwork"/> - po znovunacteni mapy by uz ukazoval jinam, proto se
    /// uzavrene hrany drzi pod timto klicem. Viz doc/global-navigation-runtime.md.
    /// </summary>
    public readonly record struct EdgeKey(long WayId, long FromId, long ToId)
    {
        public static EdgeKey Of(Edge e) => new EdgeKey(e.WayId, e.From.Id, e.To.Id);

        public override string ToString() => $"{WayId}:{FromId}->{ToId}";
    }

    /// <summary>Duvod, proc se s hranou zachazi jinak.</summary>
    public enum ClosureReason
    {
        /// <summary>Bloudeni (detektor B) - jen zdrazena, ne zakazana.</summary>
        NoProgress = 0,

        /// <summary>Cesta je prehrazena (detektor C) - zakazana.</summary>
        RoadBlocked = 1,

        /// <summary>Zasek bez pohybu (detektor A po vycerpani zotaveni).</summary>
        NoMotion = 2,
    }

    /// <summary>
    /// Zaznam o uzavrene / penalizovane hrane. Autoritativni seznam drzi
    /// <see cref="GlobalNavigator"/>, ne jen overlay v poli - da se tak znovu aplikovat po
    /// prestavbe site, poslat do zpravy a zobrazit v UI.
    /// </summary>
    public sealed class EdgeClosure
    {
        /// <summary>Trvala identita hrany.</summary>
        public EdgeKey Key { get; init; }

        /// <summary>Duvod posledniho zasahu.</summary>
        public ClosureReason Reason { get; set; }

        /// <summary>Kdy k zasahu doslo.</summary>
        public DateTime At { get; set; }

        /// <summary>Kolikrat uz byla hrana uzavrena (TTL ji vraci na soft penalizaci).</summary>
        public int Count { get; set; }

        /// <summary>true = hrana je zakazana (nekonecna cena); false = jen zdrazena.</summary>
        public bool Hard { get; set; }

        /// <summary>Puvodni cena hrany, aby slo penalizaci spocitat i po zmene.</summary>
        public double BaseCost { get; set; }

        public override string ToString()
            => $"{Key} {(Hard ? "uzavrena" : "penalizovana")} ({Reason}, {Count}x)";
    }
}
