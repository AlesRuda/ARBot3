using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Vision.Synthetic;
using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests;

/// <summary>
/// Testy virtualni kamery (viz doc/virtual-hw.md). Na rozdil od <c>D435Camera</c> nepotrebuje
/// zadny hardware - bezi kdekoliv vcetne CI.
/// </summary>
public class VirtualCameraTest
{
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Rovna vozovka sirky 4 m podel osy vychod-zapad.</summary>
    private static RoadScene Scene(GeoReference origin)
    {
        var a = new Node(1, origin.ToLLA(-50, 0), 4.0);
        var b = new Node(2, origin.ToLLA(100, 0), 4.0);

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 150.0, wayId: 1, traversalCost: 150.0);
        return builder.Build() is var net ? new RoadScene(net, origin) : null!;
    }

    private static VirtualCamera CreateCamera(string name = "Left")
    {
        var origin = Origin();
        var mount = Conversions.CameraToWorldTransform(
            0, Conversions.Deg2Rad(-20), 0, new Vector3(0, 0, 0.5f));

        return new VirtualCamera(name, Scene(origin), new SyntheticSceneOptions(), mount,
                                 _ => new RobotState { X = 0, Y = 0, Theta = 0 });
    }

    /// <summary>Pocka na prvni snimek z pozadi smycky kamery.</summary>
    private static CameraFrame? WaitForFrame(VirtualCamera cam, TimeSpan timeout)
    {
        CameraFrame? result = null;
        using var arrived = new ManualResetEventSlim(false);

        void Handler(object? sender, CameraFrame frame)
        {
            result = frame;
            arrived.Set();
        }

        cam.MeasurementArived += Handler;
        try
        {
            arrived.Wait(timeout);
        }
        finally
        {
            cam.MeasurementArived -= Handler;
        }
        return result;
    }

    [Test]
    public void ProducesFrameWithConfiguredResolutions()
    {
        using var cam = CreateCamera();

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));

        Assert.That(frame, Is.Not.Null, "virtualni kamera ma snimky produkovat i bez HW");
        Assert.Multiple(() =>
        {
            Assert.That(frame!.Name, Is.EqualTo("Left"));
            Assert.That(frame.ImageRGB!.Width, Is.EqualTo(640));
            Assert.That(frame.ImageRGB.Height, Is.EqualTo(480));
            Assert.That(frame.ImageDepth!.Width, Is.EqualTo(480));
            Assert.That(frame.ImageDepth.Height, Is.EqualTo(270));
        });
    }

    /// <summary>
    /// Projekce je k dispozici okamzite - narozdil od D435, ktera do pripojeni pipeline vyhazuje.
    /// </summary>
    [Test]
    public void ProvidesProjectionsWithoutHardware()
    {
        using var cam = CreateCamera();

        var depthProjection = cam.CreateDepthProjector();
        var colorProjection = cam.CreateProjector();

        Assert.Multiple(() =>
        {
            Assert.That(depthProjection, Is.Not.Null);
            Assert.That(colorProjection, Is.Not.Null);
            Assert.That(depthProjection.Camera2DToCamera3D.GetLength(0), Is.EqualTo(270));
            Assert.That(depthProjection.Camera2DToCamera3D.GetLength(1), Is.EqualTo(480));
        });
    }

    /// <summary>Synchronni procesor snimku se ma volat stejne jako u realne kamery.</summary>
    [Test]
    public void InvokesFrameProcessorOnEachFrame()
    {
        using var cam = CreateCamera();
        int processed = 0;
        cam.FrameProcessor = new CountingProcessor(() => Interlocked.Increment(ref processed));

        WaitForFrame(cam, TimeSpan.FromSeconds(5));

        Assert.That(Volatile.Read(ref processed), Is.GreaterThan(0));
    }

    [Test]
    public void StampujeOdhadPozyDoSnimku()
    {
        // Poza v ramci snimku je metadatum pro vizualizaci: bez ni by se hranicni body musely
        // kreslit "posledni znamou" pozou, coz pri kamerach s ruznym casem snimku posouva starsi
        // sadu o desitky centimetru. Viz CameraFrame.PoseAtCaptureX.
        using var cam = CreateCamera();
        cam.EstimatedPoseAt = t => new RobotState { X = 12.5, Y = -3.25, Theta = 1.75, TimeStamp = t };

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame!.HasPose, Is.True);
        Assert.That(frame.PoseAtCaptureX, Is.EqualTo(12.5).Within(1e-9));
        Assert.That(frame.PoseAtCaptureY, Is.EqualTo(-3.25).Within(1e-9));
        Assert.That(frame.PoseAtCaptureTheta, Is.EqualTo(1.75).Within(1e-9));
    }

    [Test]
    public void StampujePoziciKCasuSnimku_neAktualni()
    {
        // Argument MUSI byt cas snimku, ne "teď" - jinak by se hranice promitala pozou z jineho
        // okamziku, presne ta vada, kterou to ma opravit.
        using var cam = CreateCamera();
        DateTime? asked = null;
        cam.EstimatedPoseAt = t => { asked = t; return new RobotState { X = 1, Y = 2, Theta = 3 }; };

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));

        Assert.That(frame, Is.Not.Null);
        Assert.That(asked, Is.EqualTo(frame!.TimeStamp));
    }

    [Test]
    public void BezZdrojePozy_snimekProjdeBezPozy()
    {
        // Chybejici poza je metadatum, ne chyba: snimek je porad platne senzoricke mereni a nesmi
        // se zahodit. Na realnem robotu by to znamenalo vyhazovat obraz kvuli diagnostice.
        using var cam = CreateCamera();
        cam.EstimatedPoseAt = null;

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));

        Assert.That(frame, Is.Not.Null, "snimek se nesmi zahodit kvuli chybejici poze");
        Assert.That(frame!.HasPose, Is.False);
    }

    [Test]
    public void ZdrojPozyVratilNull_snimekProjdeBezPozy()
    {
        using var cam = CreateCamera();
        cam.EstimatedPoseAt = _ => null;

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame!.HasPose, Is.False);
    }

    [Test]
    public void ZdrojPozyHodilVyjimku_snimekProjde()
    {
        // Diagnosticke metadatum nesmi shodit vlakno kamery.
        using var cam = CreateCamera();
        cam.EstimatedPoseAt = _ => throw new InvalidOperationException("test");

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));

        Assert.That(frame, Is.Not.Null);
        Assert.That(frame!.HasPose, Is.False);
    }

    /// <summary>Procesor, ktery jen pocita zpracovane snimky.</summary>
    private sealed class CountingProcessor : ARBot.Common.Vision.ICameraFrameProcessor
    {
        private readonly Action onProcess;
        public CountingProcessor(Action onProcess) => this.onProcess = onProcess;
        public void Process(CameraFrame frame) => onProcess();
    }
}
