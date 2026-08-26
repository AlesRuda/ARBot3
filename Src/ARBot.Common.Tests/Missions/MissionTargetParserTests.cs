using System;
using System.Globalization;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Missions;

namespace ARBot.Common.Tests.Missions;

/// <summary>
/// Testy parseru cile z QR kodu (format <c>geo:</c>, viz doc/robotour-mission.md). Format je
/// prevzaty 1:1 z predchozi generace robotu (ARBot2, <c>ReadQRLLA</c>), ktera ho v soutezi
/// pouzivala.
///
/// <para><b>Nejdulezitejsi test je ten na locale.</b> Pod ceskym locale by
/// <c>double.Parse("49.2103")</c> dalo 492103 — je to jedina chyba v tomhle retezu, kterou lze
/// udelat TICHE a FATALNE (robot by odjel do vesmiru).</para>
/// </summary>
public class MissionTargetParserTests
{
    private static readonly IMissionTargetParser Parser = new GeoUriTargetParser();

    /// <summary>Stupne z LLA (ta je v radianech, jak system vyzaduje).</summary>
    private static (double latDeg, double lonDeg) Deg(Coordinates.LLA lla)
        => (Conversions.Rad2Deg(lla.Latitude), Conversions.Rad2Deg(lla.Longitude));

    [Test]
    public void ZakladniFormat_DaSouradniceVRadianech()
    {
        var lla = Parser.Parse("geo:49.2103,16.5991");

        Assert.That(lla, Is.Not.Null);
        var (lat, lon) = Deg(lla!);
        Assert.Multiple(() =>
        {
            Assert.That(lat, Is.EqualTo(49.2103).Within(1e-9));
            Assert.That(lon, Is.EqualTo(16.5991).Within(1e-9));
        });
    }

    /// <summary>Mezery i sufixy svetovych stran jsou pripustne — kod muze byt vysazeny jakkoli.</summary>
    [Test]
    public void MezeryASufixyNS_JsouPripustne()
    {
        var lla = Parser.Parse("geo: 49.2103 N, 16.5991 E");

        Assert.That(lla, Is.Not.Null);
        var (lat, lon) = Deg(lla!);
        Assert.Multiple(() =>
        {
            Assert.That(lat, Is.EqualTo(49.2103).Within(1e-9));
            Assert.That(lon, Is.EqualTo(16.5991).Within(1e-9));
        });
    }

    /// <summary>Sufix <c>s</c>/<c>w</c> urcuje ZNAMENKO.</summary>
    [Test]
    public void SufixySW_DavajiZapornaZnamenka()
    {
        var lla = Parser.Parse("geo:12.34S,45.67W");

        Assert.That(lla, Is.Not.Null);
        var (lat, lon) = Deg(lla!);
        Assert.Multiple(() =>
        {
            Assert.That(lat, Is.EqualTo(-12.34).Within(1e-9));
            Assert.That(lon, Is.EqualTo(-45.67).Within(1e-9));
        });
    }

    /// <summary>Bez sufixu se bere hodnota, jak je — tedy i s minusem.</summary>
    [Test]
    public void BezSufixu_MinusPlatiJakJe()
    {
        var lla = Parser.Parse("geo:-12.34,-45.67");

        Assert.That(lla, Is.Not.Null);
        var (lat, lon) = Deg(lla!);
        Assert.Multiple(() =>
        {
            Assert.That(lat, Is.EqualTo(-12.34).Within(1e-9));
            Assert.That(lon, Is.EqualTo(-45.67).Within(1e-9));
        });
    }

    /// <summary>Porovnani je case-insensitive (kod muze byt vysazeny velkymi pismeny).</summary>
    [Test]
    public void VelkaPismena_JsouPripustna()
    {
        var lla = Parser.Parse("GEO:49.2103N,16.5991E");

        Assert.That(lla, Is.Not.Null);
        Assert.That(Deg(lla!).latDeg, Is.EqualTo(49.2103).Within(1e-9));
    }

    /// <summary>
    /// <b>Klicovy test:</b> cisla se parsuji vzdy <see cref="CultureInfo.InvariantCulture"/>.
    /// Pod ceskym locale by desetinna tecka spadla do <c>492103</c> a robot by odjel do vesmiru —
    /// a spadlo by to TICHE, protoze 492103 je platne cislo.
    /// </summary>
    [Test]
    public void PodCeskymLocale_SeTeckaPorozumiSpravne()
    {
        var puvodni = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("cs-CZ");

            var lla = Parser.Parse("geo:49.2103,16.5991");

            Assert.That(lla, Is.Not.Null);
            Assert.That(Deg(lla!).latDeg, Is.EqualTo(49.2103).Within(1e-9),
                        "pod cs-CZ by naivni double.Parse dal 492103");
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = puvodni;
        }
    }

    /// <summary>Cokoli jineho nez <c>geo:</c> se zamitne — nesrozumitelny kod nikdy neposune misi.</summary>
    [TestCase("http://example.com/49.2103,16.5991", Description = "jiny prefix")]
    [TestCase("49.2103,16.5991", Description = "bez prefixu")]
    [TestCase("geo:", Description = "prazdne telo")]
    [TestCase("geo:49.2103", Description = "chybi delka")]
    [TestCase("geo:sem,tam", Description = "necislo")]
    [TestCase("geo:49.2103,16.5991,7.5", Description = "tri slozky")]
    [TestCase("", Description = "prazdny text")]
    [TestCase(null, Description = "null")]
    public void NevyhovujiciText_SeZamitne(string? text)
    {
        Assert.That(Parser.Parse(text!), Is.Null);
    }

    /// <summary>
    /// Souradnice mimo rozsah Zeme se zamitnou. Bez teto kontroly by preklep v kodu (napr. chybejici
    /// desetinna tecka) prosel jako platny cil nekde v Antarktide.
    /// </summary>
    [TestCase("geo:492103,16.5991", Description = "sirka mimo +-90")]
    [TestCase("geo:49.2103,1650991", Description = "delka mimo +-180")]
    [TestCase("geo:-90.5,0", Description = "sirka pod -90")]
    public void SouradniceMimoRozsahZeme_SeZamitnou(string text)
    {
        Assert.That(Parser.Parse(text), Is.Null);
    }
}
