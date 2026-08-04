using ARBot.Common;
using ARBot.Common.Common;

namespace ARBot.Common.Tests.OsmNav.Colider;

/// <summary>
/// Ověřuje algebru bod/vektor sdílených typů <see cref="Point2D"/> (float, pozice) a
/// <see cref="Vector2D"/> (double, posun), na které stojí <c>Colider</c> po sjednocení
/// s <see cref="Point2D"/> z <c>ARBot.Common</c> (dřív měl Colider vlastní double-verzi).
/// </summary>
public class Point2DTests
{
    [Test]
    public void PointDifference_IsVector_Componentwise()
    {
        var a = new Point2D(5, 7);
        var b = new Point2D(2, 3);
        Vector2D d = a - b;                 // Point2D − Point2D → Vector2D
        Assert.That(d.X, Is.EqualTo(3).Within(1e-6));
        Assert.That(d.Y, Is.EqualTo(4).Within(1e-6));
    }

    [Test]
    public void VectorLength_OfThreeFourVector_IsFive()
    {
        var v = new Vector2D(3, 4);
        Assert.That(v.Length, Is.EqualTo(5).Within(1e-9));
    }

    [Test]
    public void PointDistance_FromOrigin_IsEuclidean()
    {
        var p = new Point2D(3, 4);
        Assert.That(p.Distance, Is.EqualTo(5).Within(1e-6));
    }

    [Test]
    public void VectorAngle_AlongPositiveX_IsZero()
    {
        var v = new Vector2D(1, 0);
        Assert.That(v.Angle, Is.EqualTo(0).Within(1e-9));
    }

    [Test]
    public void VectorAngle_AlongPositiveY_IsHalfPi()
    {
        var v = new Vector2D(0, 1);
        Assert.That(v.Angle, Is.EqualTo(Math.PI / 2).Within(1e-9));
    }

    [Test]
    public void VectorScalarMultiply_ScalesBothComponents()
    {
        var v = new Vector2D(2, -3) * 2.0;
        Assert.That(v.X, Is.EqualTo(4).Within(1e-9));
        Assert.That(v.Y, Is.EqualTo(-6).Within(1e-9));
    }

    [Test]
    public void PointPlusVector_TranslatesPoint()
    {
        var p = new Point2D(1, 1) + new Vector2D(2, 3);   // Point2D + Vector2D → Point2D
        Assert.That(p.X, Is.EqualTo(3).Within(1e-6));
        Assert.That(p.Y, Is.EqualTo(4).Within(1e-6));
    }
}
