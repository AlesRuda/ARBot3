using ARBot.Common.Coordinates;

namespace ARBot.Common.Tests.OsmNav.Geo;

/// <summary>
/// Vzdálenostní primitivum, o které se opírá OsmNav po sjednocení na LLA
/// (dříve OsmNav <c>GeoMath.HaversineMeters</c>, nyní <see cref="GreatCircle.Distance"/>).
/// </summary>
public class GreatCircleDistanceTests
{
    private static readonly GreatCircle Gc = new();

    [Test]
    public void Distance_KnownDistance_WithinOnePercent()
    {
        // Praha Hlavní nádraží → Brno hl. n. ≈ 185 km
        var praha = LLA.FromDegrees(50.0833, 14.4353);
        var brno = LLA.FromDegrees(49.1907, 16.6127);
        Assert.That(Gc.Distance(praha, brno), Is.InRange(183_000, 187_000));
    }

    [Test]
    public void Distance_SamePoint_IsZero()
    {
        var p = LLA.FromDegrees(50.0, 14.0);
        Assert.That(Gc.Distance(p, p), Is.EqualTo(0.0).Within(1e-3));
    }
}
