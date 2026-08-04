using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Odhad stavu robota (např. z EKF) v lokálním metrickém rámci.
/// <see cref="Heading"/> [rad] = matematický úhel CCW od osy +X,
/// <see cref="Speed"/> [m/s], <see cref="YawRate"/> [rad/s] (kladné = CCW).
/// </summary>
public readonly record struct RobotState(
    Point2D Position,
    double Heading,
    double Speed,
    double YawRate,
    PoseCovariance Covariance);
