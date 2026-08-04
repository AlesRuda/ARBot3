using System;
using System.Collections.Generic;
using System.Linq;
namespace ARBot.Common.Maps.OsmNav.Colider;

/// <summary>Závažnost hrozby (vzestupně). Pořadí hodnot určuje třídění.</summary>
public enum ThreatSeverity
{
    /// <summary>Označeno jen kvůli nafouknutí koridoru o nejistotu; nominálně je odstup kladný.</summary>
    Watch = 0,

    /// <summary>Na kolizním kurzu v horizontu, ale robot ještě stihne zabrzdit.</summary>
    Imminent = 1,

    /// <summary>Na kolizním kurzu blíž než brzdná dráha — brzděním už nezastaví.</summary>
    Unavoidable = 2,
}
