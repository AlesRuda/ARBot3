using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Predikovaný průběh dráhy robota jako posloupnost úseků s konstantní křivostí
/// (viz <see cref="MotionArc"/>) — typicky jen pár kusů (jízda + brzdění).
/// </summary>
public sealed record PredictedTrajectory(
    IReadOnlyList<MotionArc> Arcs,
    double HorizonSeconds,
    double HorizonMeters);
