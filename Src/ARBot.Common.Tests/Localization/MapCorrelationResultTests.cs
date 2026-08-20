using System;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy pravidel "kdy korelator mlci" (viz doc/map-correlation-localization.md).
/// Poradi pravidel je soucasti kontraktu - proto se testuje i ono.
/// </summary>
public class MapCorrelationResultTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Sken s dobrym skore, jednoznacny, s malym posunem.</summary>
    private static ScanResult GoodScan()
        => new ScanResult
        {
            Dx = 0.1, Dy = 0.5, Phi = 0.01,
            Score = 0.9, CoarseStrideScoreAtPeak = 0.88,
            Candidates = 100,
        };

    /// <summary>Kovariance s dobre urcenou jednou osou a spatne urcenou druhou.</summary>
    private static CorrelationCovariance GoodCov(double sigmaTight = 0.1, double sigmaLoose = 2.0,
                                                 double sigmaPhi = 0.02)
        => CorrelationCovariance.ForTest(sigmaTight, sigmaLoose, Math.PI / 2, sigmaPhi);

    [Test]
    public void DobryVstup_JeOkAPosleVse()
    {
        var cfg = new MapCorrelatorConfig();
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(), evidenceCells: 5000, rivalAlongTight: 0.2, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok));
        Assert.That(r.EmitTightAxis, Is.True);
        Assert.That(r.EmitLooseAxis, Is.True);
        Assert.That(r.EmitHeading, Is.True);
        Assert.That(r.Emitted, Is.True);
    }

    [Test]
    public void MaloDukazu_Mlci()
    {
        var cfg = new MapCorrelatorConfig { MinEvidenceCells = 400 };
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(), evidenceCells: 399, rivalAlongTight: 0.2, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.TooFewEvidence));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void NizkeSkore_Mlci()
    {
        var cfg = new MapCorrelatorConfig { MinScore = 0.25 };
        var scan = GoodScan();
        scan.Score = 0.24;
        scan.CoarseStrideScoreAtPeak = 0.24;

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.LowScore));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void BlizkyKonkurent_JeNejednoznacne()
    {
        var cfg = new MapCorrelatorConfig { AmbiguityMargin = 0.10 };
        var scan = GoodScan();
        scan.CoarseStrideScoreAtPeak = 0.88;

        // Konkurent PODEL URCENE OSY je skore blizko maxima (rozdil 0,03 < 0,10) => nejednoznacne.

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, rivalAlongTight: 0.85, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ambiguous));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void ZadnyKonkurentPodelOsy_Nevadi()
    {
        var cfg = new MapCorrelatorConfig();
        var scan = GoodScan();

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, double.NegativeInfinity, double.NegativeInfinity, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok));
    }

    [Test]
    public void PrilisVelkyPosun_Mlci()
    {
        var cfg = new MapCorrelatorConfig { MaxOffsetM = 2.0 };
        var scan = GoodScan();
        scan.Dx = 1.8; scan.Dy = 1.8;   // norma 2,55 m

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.OffsetTooLarge));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void ZadneMaximum_Mlci()
    {
        var cfg = new MapCorrelatorConfig();
        var r = MapCorrelationResult.From(T0, GoodScan(), CorrelationCovariance.NoPeak(), 5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.NoPeak));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void SigmaNadStropem_VynechaJenTuOsu()
    {
        var cfg = new MapCorrelatorConfig { SigmaCeilingM = 1.0 };
        // Podelna osa je horsi nez strop, pricna ne - typicky pripad na prime ceste.
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(sigmaTight: 0.1, sigmaLoose: 4.0),
                                          5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok));
        Assert.That(r.EmitTightAxis, Is.True);
        Assert.That(r.EmitLooseAxis, Is.False, "Neurcena osa se posilat nesmi.");
        Assert.That(r.Emitted, Is.True, "Cyklus stale poslal pricnou korekci.");
    }

    [Test]
    public void KonkurentPodelVolneOsy_VynechaJenTuOsu()
    {
        // Konkurent podel VOLNE osy nediskvalifikuje cely cyklus - rika jen, ze prave tahle osa je
        // nespolehliva. Bez tohoto pravidla by sla do fuze podelna korekce, kterou nehlida nic
        // (konkurent se meri podel URCENE osy) - a to je nebezpecne prave tam, kde SigmaLoose vyjde
        // omylem konecna. Viz "falesna podelna jistota" v doc/map-correlation-localization.md.
        var cfg = new MapCorrelatorConfig { AmbiguityMargin = 0.10 };
        var scan = GoodScan();
        scan.CoarseStrideScoreAtPeak = 0.88;

        // Obe osy pod stropem, ale volna ma blizkeho konkurenta (0,85 > 0,88 - 0,10).
        var r = MapCorrelationResult.From(T0, scan, GoodCov(sigmaTight: 0.1, sigmaLoose: 0.3),
                                          5000, rivalAlongTight: 0.2, rivalAlongLoose: 0.85, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok),
                    "Konkurent podel VOLNE osy nesmi shodit cely cyklus.");
        Assert.That(r.EmitTightAxis, Is.True, "Urcena osa je v poradku a ma se poslat.");
        Assert.That(r.EmitLooseAxis, Is.False, "Nespolehliva volna osa se ma vynechat.");
        Assert.That(r.Emitted, Is.True);
    }

    [Test]
    public void SigmaKurzuNadStropem_VynechaJenKurz()
    {
        var cfg = new MapCorrelatorConfig { SigmaCeilingHeadingRad = 0.01 };
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(sigmaPhi: 0.05), 5000, 0.2, 0.2, cfg);

        Assert.That(r.EmitHeading, Is.False);
        Assert.That(r.EmitTightAxis, Is.True);
    }

    [Test]
    public void PoradiPravidel_MaloDukazuPredNizkymSkore()
    {
        // Kdyz plati oba duvody, hlasi se ten prvni - jinak by se v telemetrii ztratil
        // rozdil mezi "nemam data" a "mam data a nesouhlasi".
        var cfg = new MapCorrelatorConfig { MinEvidenceCells = 400, MinScore = 0.25 };
        var scan = GoodScan();
        scan.Score = 0.0;
        scan.CoarseStrideScoreAtPeak = 0.0;

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), evidenceCells: 10, rivalAlongTight: 0.2, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.TooFewEvidence));
    }

    [Test]
    public void OpisujeVstupyDoVysledku()
    {
        var cfg = new MapCorrelatorConfig();
        var scan = GoodScan();
        var r = MapCorrelationResult.From(T0, scan, GoodCov(0.1, 2.0, 0.02), 5000, 0.31, 0.2, cfg);

        Assert.That(r.TimeStamp, Is.EqualTo(T0));
        Assert.That(r.Dx, Is.EqualTo(scan.Dx));
        Assert.That(r.Dy, Is.EqualTo(scan.Dy));
        Assert.That(r.Phi, Is.EqualTo(scan.Phi));
        Assert.That(r.Score, Is.EqualTo(scan.Score));
        Assert.That(r.SecondBestScore, Is.EqualTo(0.31), "Do vysledku jde konkurent PODEL OSY, ne pole ze skenu.");
        Assert.That(r.Candidates, Is.EqualTo(scan.Candidates));
        Assert.That(r.EvidenceCells, Is.EqualTo(5000));
        Assert.That(r.SigmaTight, Is.EqualTo(0.1));
        Assert.That(r.SigmaLoose, Is.EqualTo(2.0));
        Assert.That(r.SigmaPhi, Is.EqualTo(0.02));
    }
}
