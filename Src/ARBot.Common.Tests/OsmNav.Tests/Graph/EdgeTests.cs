using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.OsmNav.Graph;

public class EdgeTests
{
    [Test]
    public void Edge_ExposesEndpointsAndIndex()
    {
        var a = new Node(1, LLA.FromDegrees(50.0, 14.0));
        var b = new Node(2, LLA.FromDegrees(50.0, 14.01));
        var e = new Edge(0, a, b, 700.0, 42);

        Assert.That(e.Index, Is.EqualTo(0));
        Assert.That(e.From, Is.SameAs(a));
        Assert.That(e.To, Is.SameAs(b));
        Assert.That(e.LengthMeters, Is.EqualTo(700.0));
        Assert.That(e.WayId, Is.EqualTo(42));
    }
}
