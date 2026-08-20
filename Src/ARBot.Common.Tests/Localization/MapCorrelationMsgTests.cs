using System;
using System.IO;
using ARBot.Common.Communication;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy zpravy korelace s mapou (viz doc/map-correlation-localization.md).
/// Zpravu vyrabi domena metodou ToLogMessage() - konvence CLAUDE.md.
/// </summary>
public class MapCorrelationMsgTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    private static MapCorrelationResult Result()
    {
        var scan = new ScanResult
        {
            Dx = 0.15, Dy = -0.62, Phi = 0.021,
            Score = 0.87, CoarseStrideScoreAtPeak = 0.85, Candidates = 1375,
        };
        var cov = CorrelationCovariance.ForTest(0.12, 2.4, Math.PI / 2, 0.018);
        var r = MapCorrelationResult.From(T0, scan, cov, evidenceCells: 4211, rivalAlongTight: 0.31,
                                          rivalAlongLoose: 0.22, new MapCorrelatorConfig());
        r.ProcessingTime = TimeSpan.FromMilliseconds(12.5);
        return r;
    }

    [Test]
    public void ToLogMessage_OpisujeVsechnyUdaje()
    {
        var msg = Result().ToLogMessage();

        Assert.That(msg.TimeStamp, Is.EqualTo(T0));
        Assert.That(msg.Dx, Is.EqualTo(0.15).Within(1e-9));
        Assert.That(msg.Dy, Is.EqualTo(-0.62).Within(1e-9));
        Assert.That(msg.Phi, Is.EqualTo(0.021).Within(1e-9));
        Assert.That(msg.Score, Is.EqualTo(0.87).Within(1e-9));
        Assert.That(msg.SecondBestScore, Is.EqualTo(0.31).Within(1e-9));
        // Konkurent podel VOLNE osy je jine cislo nez ten podel URCENE - proto ma vlastni pole:
        // bez nej neni v telemetrii poznat, jestli volnou osu vynechal strop sigma, nebo konkurent.
        Assert.That(msg.SecondBestScoreLoose, Is.EqualTo(0.22).Within(1e-9));
        Assert.That(msg.SigmaTight, Is.EqualTo(0.12).Within(1e-9));
        Assert.That(msg.SigmaLoose, Is.EqualTo(2.4).Within(1e-9));
        Assert.That(msg.SigmaPhi, Is.EqualTo(0.018).Within(1e-9));
        Assert.That(msg.EvidenceCells, Is.EqualTo(4211));
        Assert.That(msg.Candidates, Is.EqualTo(1375));
        Assert.That(msg.ProcessingMs, Is.EqualTo(12.5).Within(1e-6));
        Assert.That(msg.Reason, Is.EqualTo((byte)MapCorrelationReason.Ok));
        Assert.That(msg.Emitted, Is.True);
        Assert.That(msg.EmitTightAxis, Is.True);
        Assert.That(msg.EmitLooseAxis, Is.True);
        Assert.That(msg.EmitHeading, Is.True);
    }

    [Test]
    public void JeOdvozenaZprava_NeniPrimarni()
    {
        // Odvozena zprava nesmi nest marker primarniho vstupu (jinak by ji replay bral jako senzor).
        Assert.That(new MapCorrelationMsg() is IPrimaryMessage, Is.False);
    }

    [Test]
    public void CasPorizeniJeCasSnapshotu()
    {
        var msg = Result().ToLogMessage();

        Assert.That(((IHasCaptureTime)msg).CaptureTime, Is.EqualTo(T0));
    }

    [Test]
    public void SerializaceJeObousmerna()
    {
        var original = Result().ToLogMessage();

        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new MapCorrelationMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(loaded.TimeStamp, Is.EqualTo(original.TimeStamp));
        Assert.That(loaded.Dx, Is.EqualTo(original.Dx).Within(1e-9));
        Assert.That(loaded.Dy, Is.EqualTo(original.Dy).Within(1e-9));
        Assert.That(loaded.Phi, Is.EqualTo(original.Phi).Within(1e-9));
        Assert.That(loaded.Score, Is.EqualTo(original.Score).Within(1e-9));
        Assert.That(loaded.SecondBestScore, Is.EqualTo(original.SecondBestScore).Within(1e-9));
        Assert.That(loaded.SecondBestScoreLoose, Is.EqualTo(original.SecondBestScoreLoose).Within(1e-9));
        Assert.That(loaded.SigmaTight, Is.EqualTo(original.SigmaTight).Within(1e-9));
        Assert.That(loaded.SigmaLoose, Is.EqualTo(original.SigmaLoose).Within(1e-9));
        Assert.That(loaded.TightAxisAngle, Is.EqualTo(original.TightAxisAngle).Within(1e-9));
        Assert.That(loaded.SigmaPhi, Is.EqualTo(original.SigmaPhi).Within(1e-9));
        Assert.That(loaded.EvidenceCells, Is.EqualTo(original.EvidenceCells));
        Assert.That(loaded.Candidates, Is.EqualTo(original.Candidates));
        Assert.That(loaded.Emitted, Is.EqualTo(original.Emitted));
        Assert.That(loaded.EmitTightAxis, Is.EqualTo(original.EmitTightAxis));
        Assert.That(loaded.EmitLooseAxis, Is.EqualTo(original.EmitLooseAxis));
        Assert.That(loaded.EmitHeading, Is.EqualTo(original.EmitHeading));
        Assert.That(loaded.Reason, Is.EqualTo(original.Reason));
        Assert.That(loaded.ProcessingMs, Is.EqualTo(original.ProcessingMs).Within(1e-6));
    }

    [Test]
    public void NekonecnaSigma_PrezijeSerializaci()
    {
        // Pri Reason = NoPeak jsou sigmy PositiveInfinity - zaznam to musi snest.
        var r = MapCorrelationResult.From(T0, new ScanResult { Score = 0.9, CoarseStrideScoreAtPeak = 0.9 },
                                          CorrelationCovariance.NoPeak(), 5000,
                                          rivalAlongTight: double.NegativeInfinity,
                                          rivalAlongLoose: double.NegativeInfinity, new MapCorrelatorConfig());
        var original = r.ToLogMessage();

        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);
        buffer.Position = 0;
        var loaded = new MapCorrelationMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(double.IsPositiveInfinity(loaded.SigmaTight), Is.True);
        Assert.That(loaded.Reason, Is.EqualTo((byte)MapCorrelationReason.NoPeak));
    }

    [Test]
    public void JeVKataloguZprav()
    {
        // Bez registrace by se zprava pri replay preskocila jako neznamy typ.
        var catalog = MessageCatalog.CommonDefaults();

        Assert.That(catalog.Contains(new MapCorrelationMsg().MsgName), Is.True);
    }
}
