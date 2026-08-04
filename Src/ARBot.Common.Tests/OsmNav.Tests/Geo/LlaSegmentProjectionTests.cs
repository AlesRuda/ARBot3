using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Tests.OsmNav.Geo;

/// <summary>
/// Mapmatching primitivum (projekce bodu na úsek), o které se opírá <c>NearestEdge</c>/<c>NearestNode</c>
/// po sjednocení na LLA (dříve OsmNav <c>GeoMath.ProjectOntoSegment</c>, nyní
/// <see cref="LLA.ProjectOntoSegment"/>).
/// </summary>
public class LlaSegmentProjectionTests
{
    // Segment podél rovnoběžky ~50°N, délka ~ pár set metrů.
    private static readonly LLA A = LLA.FromDegrees(50.0000, 14.0000);
    private static readonly LLA B = LLA.FromDegrees(50.0000, 14.0100);

    [Test]
    public void Projection_PointAboveMiddle_LandsInMiddle()
    {
        var p = LLA.FromDegrees(50.0005, 14.0050); // nad středem
        var (point, dist, t) = p.ProjectOntoSegment(A, B);
        Assert.That(t, Is.InRange(0.45, 0.55));
        Assert.That(dist, Is.InRange(40, 80));          // ~55 m severně
        Assert.That(Conversions.Rad2Deg(point.Latitude), Is.EqualTo(50.0000).Within(1e-4));
    }

    [Test]
    public void Projection_PointBeforeStart_ClampsToA()
    {
        var p = LLA.FromDegrees(50.0000, 13.9990);
        var (_, _, t) = p.ProjectOntoSegment(A, B);
        Assert.That(t, Is.EqualTo(0.0).Within(1e-3));
    }

    [Test]
    public void Projection_PointAfterEnd_ClampsToB()
    {
        var p = LLA.FromDegrees(50.0000, 14.0200);
        var (_, _, t) = p.ProjectOntoSegment(A, B);
        Assert.That(t, Is.EqualTo(1.0).Within(1e-3));
    }
}
