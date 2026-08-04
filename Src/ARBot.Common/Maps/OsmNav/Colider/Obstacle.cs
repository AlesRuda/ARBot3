using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Překážka reprezentovaná kruhem: střed v lokálních metrech a poloměr z odhadu
/// velikosti. <see cref="Id"/> slouží k identifikaci napříč snímky.
/// </summary>
public readonly record struct Obstacle(long Id, Point2D Center, double RadiusMeters);
