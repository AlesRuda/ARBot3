using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Maps.OsmNav.Navigation;

public sealed record NavigationFix(
    Edge? CurrentEdge,
    Node? TargetNode,
    double OffRouteDist,
    bool Arrived,
    bool NoRoute);
