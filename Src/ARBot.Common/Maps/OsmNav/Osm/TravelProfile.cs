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
