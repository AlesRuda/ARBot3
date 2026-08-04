using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>
/// Aktuální řízení z regulátoru. <see cref="RequestedSpeed"/> [m/s],
/// <see cref="RequestedYawRate"/> [rad/s]. <see cref="TimeToFullStopSeconds"/> = doba
/// případného brzdného zásahu do úplného zastavení. Volitelně omezení dynamiky:
/// <see cref="MaxAcceleration"/> [m/s²] pro náběh rychlosti a
/// <see cref="BrakingDeceleration"/> [m/s²] (má přednost před dobou zastavení).
/// </summary>
public readonly record struct ControlCommand(
    double RequestedSpeed,
    double RequestedYawRate,
    double TimeToFullStopSeconds,
    double? MaxAcceleration = null,
    double? BrakingDeceleration = null);
