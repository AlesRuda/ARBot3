using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Rozměry obalu robota (kapsle): délka a poloměry vzadu/vpředu (mapuje na <see cref="Collider"/>).
/// </summary>
public readonly record struct RobotFootprint(double Length, double RearRadius, double FrontRadius)
{
    /// <summary>Konzervativní boční poloměr obalu [m] (větší z konců).</summary>
    public double BoundingRadius => Math.Max(RearRadius, FrontRadius);
}
