using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Navigation;

/// <param name="ArrivalRadiusMeters">
/// Vzdalenost pozy od cile, pod kterou se hlasi dojezd [m].
/// <para>Nema byt "co nejmensi", ale <b>mensi nez stanoviste</b>: misto nakladky/vykladky je plocha
/// o metrech, zatimco robot dojede s chybou EKF/GPS radu metru. Snizeno z puvodnich 12 m -
/// viz doc/global-navigation-runtime.md.</para>
/// </param>
public sealed record NavigatorOptions(
    double ArrivalRadiusMeters = 3.0);
