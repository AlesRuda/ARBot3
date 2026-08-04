using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Maps.OsmNav.Graph;

/// <summary>Uzel mapy (křižovatka / koncový bod / bod lomu cesty).</summary>
public sealed class Node
{
    public long Id { get; }
    public LLA Location { get; }

    public Node(long id, LLA location)
    {
        Id = id;
        Location = location;
    }
}
