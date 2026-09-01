#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Osm;

/// <summary>Strategie: které cesty/bariéry akceptovat a jak počítat cenu hrany.</summary>
public sealed class TravelProfile
{
    public required string Name { get; init; }
    public required IReadOnlySet<string> AllowedHighways { get; init; }
    public bool RespectOneway { get; init; } = true;
    public IReadOnlySet<string> BlockingBarriers { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BlockedAccess { get; init; } = new HashSet<string> { "private", "no" };
    public required double MaxSpeedMetersPerSecond { get; init; }
    public required Func<OsmWayRaw, double, double> CostFunction { get; init; }

    public bool AcceptsWay(OsmWayRaw way)
    {
        if (!way.Tags.TryGetValue("highway", out string? hw) || !AllowedHighways.Contains(hw))
            return false;
        if (way.Tags.TryGetValue("access", out string? acc) && BlockedAccess.Contains(acc))
            return false;
        return true;
    }

    public bool IsOneway(OsmWayRaw way)
    {
        if (!RespectOneway) return false;
        string v = way.Tags.GetValueOrDefault("oneway", "no");
        return v is "yes" or "true" or "1";
    }

    public bool BlocksNode(OsmNodeRaw node) =>
        node.Tags.TryGetValue("barrier", out string? b) && BlockingBarriers.Contains(b);

    public double EdgeCost(OsmWayRaw way, double lengthMeters) => CostFunction(way, lengthMeters);

    private static Func<OsmWayRaw, double, double> ByMaxSpeed(double mps) =>
        (_, len) => len / mps;

    public static TravelProfile Car() => new()
    {
        Name = "car",
        AllowedHighways = new HashSet<string>
        {
            "motorway", "trunk", "primary", "secondary", "tertiary",
            "unclassified", "residential", "living_street", "service",
            "motorway_link", "trunk_link", "primary_link", "secondary_link", "tertiary_link",
        },
        RespectOneway = true,
        BlockingBarriers = new HashSet<string> { "gate", "bollard", "block", "barrier_gate" },
        MaxSpeedMetersPerSecond = 13.9, // ~50 km/h
        CostFunction = ByMaxSpeed(13.9),
    };

    public static TravelProfile Pedestrian() => new()
    {
        Name = "pedestrian",
        AllowedHighways = new HashSet<string>
        {
            "primary", "secondary", "tertiary", "unclassified", "residential",
            "living_street", "service", "pedestrian", "footway", "path", "steps", "track",
        },
        RespectOneway = false,
        BlockingBarriers = new HashSet<string> { "wall", "fence" },
        MaxSpeedMetersPerSecond = 1.4, // ~5 km/h
        CostFunction = ByMaxSpeed(1.4),
    };

    /// <summary>
    /// Profil <b>našeho robota</b> — podle něj se načítá navigační mapa (<c>map=</c>) i náhled
    /// ve World pohledu.
    ///
    /// <para><b>Proč nestačí <see cref="Pedestrian"/>, kterým se to načítalo do 1. 9. 2026.</b>
    /// Robot chodec není a lišil se v obou směrech naráz:</para>
    /// <list type="bullet">
    /// <item><b>Chyběly cyklostezky.</b> <c>cycleway</c> nebyl v <c>AllowedHighways</c>, takže se
    /// tiše zahazoval — na <c>haje.osm</c> 9 cest z 387, a byla to jediná systematická ztráta
    /// (v OSM se kreslí modře čárkovaně, takže byly vidět na podkladu a chyběly v síti).</item>
    /// <item><b>Přebývaly schody.</b> <c>steps</c> se naopak přijímal, takže plánovač mohl vést
    /// trasu po schodech, které kolový robot nevyjede (9 cest na <c>haje.osm</c>, 37 na
    /// <c>HajeRovne.osm</c>).</item>
    /// </list>
    ///
    /// <para><b>Bariéry.</b> U chodce byl <c>BlockingBarriers</c> prakticky mrtvý kód:
    /// <see cref="BlocksNode"/> se dívá jen na UZLY, ale <c>wall</c> a <c>fence</c> jsou v datech
    /// výhradně cesty. Skutečné bodové překážky (<c>gate</c>, <c>bollard</c>, <c>block</c>,
    /// <c>lift_gate</c>) proto procházely bez povšimnutí.</para>
    ///
    /// <para>Blokují se tu jen bariéry, které jsou pro kolový robot <b>opravdu nepřekonatelné</b>.
    /// Závory a sloupky se ZÁMĚRNĚ neblokují: rozchod robota je 0,41 m, takže mezerou projede —
    /// a co neprojede, zastaví lokální vyhýbání (occupancy grid). Blokovat je globálně by v parku
    /// plném bran rozpojilo síť a trasa by se nenašla vůbec. Viz doc/osm-nav.md.</para>
    /// </summary>
    public static TravelProfile Robot() => new()
    {
        Name = "robot",
        AllowedHighways = new HashSet<string>
        {
            "primary", "secondary", "tertiary", "unclassified", "residential",
            "living_street", "service", "pedestrian", "footway", "path", "track",
            "cycleway",          // proti chodci NAVIC - po cyklostezce robot jet muze
            // "steps" ZAMERNE chybi - kolovy robot schody nevyjede
        },
        RespectOneway = false,
        BlockingBarriers = new HashSet<string>
        {
            "stile", "turnstile", "kissing_gate", "cycle_barrier",   // nutno prekrocit / protahnout se
            "wall", "fence", "hedge",                                // pro uplnost (v datech to byvaji cesty)
        },
        MaxSpeedMetersPerSecond = 1.0,
        CostFunction = ByMaxSpeed(1.0),
    };

    public static TravelProfile Bicycle() => new()
    {
        Name = "bicycle",
        AllowedHighways = new HashSet<string>
        {
            "primary", "secondary", "tertiary", "unclassified", "residential",
            "living_street", "service", "cycleway", "path", "track",
        },
        RespectOneway = true,
        BlockingBarriers = new HashSet<string> { "gate", "bollard" },
        MaxSpeedMetersPerSecond = 4.2, // ~15 km/h
        CostFunction = ByMaxSpeed(4.2),
    };
}
