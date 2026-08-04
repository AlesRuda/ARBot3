using ARBot.Common.Maps.OsmNav.Colider;

namespace ARBot.Common.Tests.OsmNav.Colider;

public class MotionArcTests
{
    // ---- rovný úsek ----

    [Test]
    public void Straight_ProjectOnAxis_ZeroLateral()
    {
        var arc = MotionArc.Straight(new Point2D(0, 0), heading: 0, length: 5,
            startDistance: 0, startTime: 0, endTime: 2.5, startSigma: 0.1, endSigma: 0.2);

        var p = arc.Project(new Point2D(2, 0));

        Assert.That(p.Lateral, Is.EqualTo(0).Within(1e-6));
        Assert.That(p.ArcLength, Is.EqualTo(2).Within(1e-6));
        Assert.That(p.Time, Is.EqualTo(1.0).Within(1e-6));          // 2/5 · 2.5 s
        Assert.That(p.Sigma, Is.EqualTo(0.14).Within(1e-6));        // lerp(0.1, 0.2, 0.4)
    }

    [Test]
    public void Straight_ProjectOffAxis_LateralEqualsOffset()
    {
        var arc = MotionArc.Straight(new Point2D(0, 0), heading: 0, length: 5,
            startDistance: 0, startTime: 0, endTime: 2.5, startSigma: 0, endSigma: 0);

        Assert.That(arc.Project(new Point2D(2, 1)).Lateral, Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void Straight_ProjectBeyondEnd_ClampsToEnd()
    {
        var arc = MotionArc.Straight(new Point2D(0, 0), heading: 0, length: 5,
            startDistance: 0, startTime: 0, endTime: 2.5, startSigma: 0, endSigma: 0);

        var p = arc.Project(new Point2D(10, 0));

        Assert.That(p.Lateral, Is.EqualTo(5).Within(1e-6));         // vzdálenost ke koncovému bodu (5,0)
        Assert.That(p.ArcLength, Is.EqualTo(5).Within(1e-6));
    }

    [Test]
    public void Straight_End_IsAlongHeading()
    {
        var arc = MotionArc.Straight(new Point2D(1, 1), heading: Math.PI / 2, length: 3,
            startDistance: 0, startTime: 0, endTime: 1, startSigma: 0, endSigma: 0);

        Assert.That(arc.IsStraight);
        Assert.That(arc.End.X, Is.EqualTo(1).Within(1e-6));
        Assert.That(arc.End.Y, Is.EqualTo(4).Within(1e-6));
    }

    // ---- oblouk ----

    [Test]
    public void Curved_QuarterLeftTurn_EndPoseCorrect()
    {
        // start v (0,0) směr +X, poloměr 2 vlevo, čtvrtotáčka CCW
        var arc = MotionArc.Curved(new Point2D(0, 0), startHeading: 0, radiusSigned: 2,
            sweepAngle: Math.PI / 2, startDistance: 0, startTime: 0, endTime: 1, startSigma: 0, endSigma: 0);

        Assert.That(arc.IsStraight, Is.False);
        Assert.That(arc.End.X, Is.EqualTo(2).Within(1e-6));
        Assert.That(arc.End.Y, Is.EqualTo(2).Within(1e-6));
        Assert.That(arc.EndHeading, Is.EqualTo(Math.PI / 2).Within(1e-6));
        Assert.That(arc.Radius, Is.EqualTo(2).Within(1e-6));
        Assert.That(arc.Length, Is.EqualTo(Math.PI).Within(1e-6));   // |R|·|Δφ| = 2·π/2
    }

    [Test]
    public void Curved_PointAt_LiesOnCircle()
    {
        var arc = MotionArc.Curved(new Point2D(0, 0), startHeading: 0, radiusSigned: 2,
            sweepAngle: Math.PI / 2, startDistance: 0, startTime: 0, endTime: 1, startSigma: 0, endSigma: 0);

        var mid = arc.PointAt(0.5);
        double d = (mid - arc.Center).Length;
        Assert.That(d, Is.EqualTo(2).Within(1e-6));
        Assert.That(mid.Y > 0);   // oblouk se boulí do +Y (zatáčka vlevo)
    }

    [Test]
    public void Curved_ProjectOutsideRadius_LateralEqualsRadialGap()
    {
        var arc = MotionArc.Curved(new Point2D(0, 0), startHeading: 0, radiusSigned: 2,
            sweepAngle: Math.PI / 2, startDistance: 0, startTime: 0, endTime: 1, startSigma: 0, endSigma: 0);

        // bod na paprsku středem přes střed oblouku, ale ve vzdálenosti 3 (poloměr 2) → odstup 1
        var mid = arc.PointAt(0.5);
        var dir = (mid - arc.Center);
        var outside = arc.Center + new Point2D(dir.X / dir.Length * 3, dir.Y / dir.Length * 3);

        Assert.That(arc.Project(outside).Lateral, Is.EqualTo(1.0).Within(1e-6));
    }

    [Test]
    public void Curved_ProjectWithinSweep_ArcLengthProportional()
    {
        var arc = MotionArc.Curved(new Point2D(0, 0), startHeading: 0, radiusSigned: 2,
            sweepAngle: Math.PI / 2, startDistance: 0, startTime: 0, endTime: 1, startSigma: 0, endSigma: 0);

        var mid = arc.PointAt(0.5);
        Assert.That(arc.Project(mid).ArcLength, Is.EqualTo(arc.Length / 2).Within(1e-4));
    }

    [Test]
    public void Curved_RightTurn_EndBelowAxis()
    {
        var arc = MotionArc.Curved(new Point2D(0, 0), startHeading: 0, radiusSigned: -2,
            sweepAngle: -Math.PI / 2, startDistance: 0, startTime: 0, endTime: 1, startSigma: 0, endSigma: 0);

        Assert.That(arc.End.X, Is.EqualTo(2).Within(1e-6));
        Assert.That(arc.End.Y, Is.EqualTo(-2).Within(1e-6));
        Assert.That(arc.PointAt(0.5).Y < 0);   // boulí se do -Y (zatáčka vpravo)
    }
}
