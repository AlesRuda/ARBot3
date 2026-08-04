using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Překážka, která může ovlivnit robota, s metrikami pro prioritizaci reakce.
/// </summary>
/// <param name="Obstacle">Dotčená překážka.</param>
/// <param name="TimeToCollisionSeconds">Čas do dosažení kolizního segmentu [s].</param>
/// <param name="DistanceAlongPathMeters">Vzdálenost kolize podél dráhy [m].</param>
/// <param name="LateralClearanceMeters">Nominální boční odstup [m] (bez nejistoty); záporný = překryv.</param>
/// <param name="Severity">Závažnost hrozby.</param>
public readonly record struct ObstacleThreat(
    Obstacle Obstacle,
    double TimeToCollisionSeconds,
    double DistanceAlongPathMeters,
    double LateralClearanceMeters,
    ThreatSeverity Severity);
