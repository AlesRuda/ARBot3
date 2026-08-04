using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Poziční část kovariance odhadu stavu (z EKF), ve směrodatných odchylkách.
/// <see cref="SigmaX"/>/<see cref="SigmaY"/> [m], <see cref="SigmaHeading"/> [rad].
/// </summary>
public readonly record struct PoseCovariance(double SigmaX, double SigmaY, double SigmaHeading)
{
    /// <summary>Konzervativní skalární poziční nejistota [m] (v1 = větší z os).</summary>
    public double PositionSigma => Math.Max(SigmaX, SigmaY);
}
