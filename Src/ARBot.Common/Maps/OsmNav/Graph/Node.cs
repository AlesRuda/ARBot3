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

    /// <summary>
    /// Šířka cesty v tomto uzlu [m]. Cesta pak může být na začátku a konci různě široká (interpolace
    /// podél hrany) a v křižovatce se hrany hladce napojí (všechny sdílí šířku uzlu). 0 = neurčeno.
    /// </summary>
    public double Width { get; }

    public Node(long id, LLA location, double widthMeters = 0.0)
    {
        Id = id;
        Location = location;
        Width = widthMeters;
    }
}
