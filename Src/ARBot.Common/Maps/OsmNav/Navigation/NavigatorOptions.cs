using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Navigation;

public sealed record NavigatorOptions(
    double ArrivalRadiusMeters = 12.0);
