using System;
using System.Linq;
using System.Numerics;
using ARBot.Common.Configuration;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Fusion;

/// <summary>
/// <b>Podlaha sigmy kurzu z kompasu</b> (12. 9. 2026) — `FusionConfig.CompassHeadingStdFloor`.
///
/// <para><b>Proč vznikla.</b> Do téhle změny bral <see cref="DefaultMeasurementMapper"/> jako σ
/// měření <c>IMU/heading</c> <b>přímo</b> <c>YprU</c> ze senzoru. VN100 hlásí <c>YprU</c> p50
/// <b>0,057–0,061°</b>, jenže jeho změřená chyba proti GPS kurzu je <b>−4,90° / −2,79°</b> (dva
/// záznamy ze zařízení 12. 9. 2026) — tedy je proti své skutečné chybě <b>~60–90× přesvědčenější</b>.
/// Fúze proto kurz z kompasu <b>nevážila, přebírala</b> (<c>odhad − IMU yaw</c> = 0,00° ± 0,06)
/// a jeho chyba šla 1:1 do mapy i do mrkve.</para>
///
/// <para><b>Proč to <c>YprU</c> nemůže vědět:</b> popisuje krátkodobý šum atitudového řešení, ne
/// jeho <b>bias vůči severu</b>. Ten je v tělesovém rámci (pootočení senzoru, zbytek magnetické
/// kalibrace, šikmé jetí) a otáčením ani časem nezmizí.</para>
///
/// <para>⚠️ <b>Co tyhle testy NEtvrdí:</b> že je tím filtr poctivý. Chyba kompasu je časově
/// korelovaná a filtr ji bere jako bílý šum, takže si ji ze 100 vzorků za sekundu „vyprůměruje".
/// Skutečná léčba je bias kompasu jako stav EKF. Viz doc/ekf-fusion.md a doc/imu-and-frames.md.</para>
/// </summary>
public class KompasSigmaTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 12, 12, 47, 0, DateTimeKind.Utc);

    /// <summary>VN100 s daným <c>YprU</c> (yaw 1σ) [rad]; <c>null</c> = senzor ho neposílá.</summary>
    private static IMUState Vn100(double? ypruRad)
        => new IMUState
        {
            Name = "VN100 IMU (binary)",
            HasAbsoluteHeading = true,
            Rotation = new YawPitchRoll(0.5f, 0, 0).ToQuaternion(YawPitchRoll.Euler.zxy),
            OrientationUncertainty = ypruRad.HasValue
                ? new Vector3((float)ypruRad.Value, 0.001f, 0.001f)
                : (Vector3?)null,
            Confidence = 1,
            TimeStamp = T0,
        };

    /// <summary>
    /// Sigma mereni IMU/heading [rad] — z kovariance, tu merenie vystavuje.
    ///
    /// <para>Tolerance v testech nize jsou 1e-7/1e-9, ne strojova nula: <c>OrientationUncertainty</c>
    /// je <c>Vector3</c>, tedy FLOAT, takze se na ceste double -&gt; float -&gt; double ztrati
    /// presnost. Utahovat to vic by dalo test, ktery pada na zaokrouhleni misto na chybe.</para>
    /// </summary>
    private static double SigmaKurzu(IMUState imu, FusionConfig cfg)
    {
        var m = new DefaultMeasurementMapper(cfg)
            .ToMeasurements(imu)
            .OfType<HeadingMeasurement>()
            .Single(x => x.Source == "IMU/heading");
        return Math.Sqrt(m.NoiseCovariance[0, 0]);
    }

    /// <summary>YprU, které VN100 skutečně hlásí (p50 ze záznamů 12. 9. 2026): 0,059°.</summary>
    private const double YpruSkutecne = 0.059 * Math.PI / 180.0;

    [Test]
    public void PodlahaSeSkladaKVADRATICKY_SeSigmouZeSenzoru()
    {
        // Ne maximem: kdyz YprU vyskoci (magneticka porucha), sigma ma rust dal.
        var cfg = new FusionConfig { CompassHeadingStdFloor = 0.087 };

        double std = SigmaKurzu(Vn100(0.04), cfg);

        Assert.That(std, Is.EqualTo(Math.Sqrt(0.04 * 0.04 + 0.087 * 0.087)).Within(1e-7));
        Assert.That(std, Is.GreaterThan(0.087), "slozeni musi byt vetsi nez sama podlaha");
    }

    [Test]
    public void PriSkutecnemYprU_RozhodujePodlaha_ANafoukneSigmuRadove()
    {
        // To je cely smysl te zmeny: 0,059 stupne -> jednotky stupnu.
        var cfg = new FusionConfig();

        double bez = SigmaKurzu(Vn100(YpruSkutecne), new FusionConfig { CompassHeadingStdFloor = 0 });
        double s = SigmaKurzu(Vn100(YpruSkutecne), cfg);

        Assert.Multiple(() =>
        {
            Assert.That(bez, Is.EqualTo(YpruSkutecne).Within(1e-9),
                        "s vypnutou podlahou musi zustat presne stare chovani");
            Assert.That(s / bez, Is.GreaterThan(50),
                        "zmerena chyba kompasu je ~60-90x vetsi nez YprU - o tolik ma sigma vyrust");
            Assert.That(s, Is.EqualTo(cfg.CompassHeadingStdFloor).Within(1e-4),
                        "YprU je proti podlaze v sumu, takze rozhoduje podlaha");
        });
    }

    [Test]
    public void PodlahaNula_VraciPRESNEStareChovani()
    {
        // Dulezite pro A/B nad zaznamy: imuheadingstd=0 musi dat totez, co bylo pred 12. 9. 2026.
        var cfg = new FusionConfig { CompassHeadingStdFloor = 0 };

        Assert.That(SigmaKurzu(Vn100(0.0012), cfg), Is.EqualTo(0.0012).Within(1e-9));
    }

    [Test]
    public void BezYprU_SeVezmeCompassHeadingStd_APodlahaPlatiTAKY()
    {
        // ASCII driver YprU neposila. I tam se ale musi podlaha uplatnit - jinak by stacilo
        // prepnout driver a tise se vratit ke stare slepe duvere.
        var cfg = new FusionConfig { CompassHeadingStd = 0.05, CompassHeadingStdFloor = 0.087 };

        double std = SigmaKurzu(Vn100(null), cfg);

        Assert.That(std, Is.EqualTo(Math.Sqrt(0.05 * 0.05 + 0.087 * 0.087)).Within(1e-7));
    }

    [Test]
    public void VychoziPodlaha_ODPOVIDA_ZmerenemuBiasuNaZarizeni()
    {
        // Straz na hodnotu: 5 stupnu je RMS zmereneho biasu (4,90 a 2,79 -> 3,99) zaokrouhlena
        // nahoru, protoze beh od behu kolisa o ~2 stupne a mistni porucha pole pridava jednotky.
        // Kdyby nekdo default srazil pod zmereny bias, je to zpatky slepa duvera.
        double rmsBiasuDeg = Math.Sqrt((4.90 * 4.90 + 2.79 * 2.79) / 2);

        Assert.Multiple(() =>
        {
            Assert.That(FusionConfig.CompassHeadingStdFloorDeg, Is.GreaterThanOrEqualTo(rmsBiasuDeg),
                        "podlaha nesmi byt mensi nez zmereny bias kompasu");
            Assert.That(FusionConfig.CompassHeadingStdFloorDeg, Is.LessThanOrEqualTo(15),
                        "a zase ne tak velka, aby kompas prestal nest informaci");
            Assert.That(new FusionConfig().CompassHeadingStdFloor,
                        Is.EqualTo(FusionConfig.CompassHeadingStdFloorDeg * Math.PI / 180).Within(1e-12),
                        "pole je v RADIANECH, konstanta ve stupnich - prevod se nesmi rozejit");
        });
    }

    [Test]
    public void DefaultParametru_SEDI_NaKonfiguraciFuze()
    {
        // Dve mista se stejnym cislem se jednou rozejdou; registr ho proto cte z FusionConfig.
        Assert.That(double.Parse(ParamRegistry.ImuHeadingStd.Def.Default,
                                 System.Globalization.CultureInfo.InvariantCulture),
                    Is.EqualTo(FusionConfig.CompassHeadingStdFloorDeg).Within(1e-12));
    }

    // ---------------- Kadence (imuheadinghz) ----------------
    //
    // ⚠️ Druha polovina teze vady. Podlaha sigmy resi, jak moc se veri JEDNOMU vzorku; tohle resi,
    // ze jich filtr bere 100 za sekundu jako NEZAVISLE — jenze chyba kompasu je bias, tedy pres
    // cely beh temer konstantni (pulrozdil mezi opacnymi smery jizdy jen ∓1°), takze sto odectu
    // teze konstanty nese informaci JEDNOHO. Tataz past jako MinPeriod u korelace s mapou.

    /// <summary>VN100 se zadanym casem od <see cref="T0"/>.</summary>
    private static IMUState Vn100V(double sekund)
    {
        var i = Vn100(YpruSkutecne);
        i.TimeStamp = T0.AddSeconds(sekund);
        i.AngularVelocity = new Vector3(0, 0, 0.1f);
        return i;
    }

    /// <summary>Kolik mereni daneho druhu vznikne z rady vzorku po <paramref name="dt"/>.</summary>
    private static (int Kurzu, int Gyra) Spocti(FusionConfig cfg, int pocet, double dt)
    {
        var mapper = new DefaultMeasurementMapper(cfg);
        int kurzu = 0, gyra = 0;
        for (int k = 0; k < pocet; k++)
            foreach (var m in mapper.ToMeasurements(Vn100V(k * dt)))
            {
                if (m is HeadingMeasurement h && h.Source == "IMU/heading") kurzu++;
                if (m is ScalarStateMeasurement sc && sc.Source == "IMU/gyro") gyra++;
            }
        return (kurzu, gyra);
    }

    [Test]
    public void Skrceni_PUSTIKurzJEDNOUZaPeriodu()
    {
        // 100 Hz po dobu 1 s pri periode 1 s: prvni vzorek + ten na konci okna.
        var cfg = new FusionConfig { CompassHeadingMinPeriodSec = 1.0 };

        var (kurzu, _) = Spocti(cfg, 101, 0.01);

        Assert.That(kurzu, Is.EqualTo(2), "prvni vzorek a pak jeden za sekundu");
    }

    [Test]
    public void Skrceni_NESKRTIGYRO()
    {
        // ⚠️ To je to podstatne: mezi odecty kompasu nese kurz GYRO, ne odometrie. Jeho chyba je
        // prevazne bila, takze u nej predpoklad nezavislosti zhruba plati a skrtit ho by znamenalo
        // zahodit tu jedinou uhlovou rychlost, ktera za neco stoji.
        var cfg = new FusionConfig { CompassHeadingMinPeriodSec = 1.0 };

        var (kurzu, gyra) = Spocti(cfg, 200, 0.01);

        Assert.Multiple(() =>
        {
            Assert.That(gyra, Is.EqualTo(200), "gyro musi projit KAZDY vzorek");
            Assert.That(kurzu, Is.LessThan(5), "kurz naopak skrceny");
        });
    }

    [Test]
    public void SkrceniNula_VraciPRESNEStareChovani()
    {
        var cfg = new FusionConfig { CompassHeadingMinPeriodSec = 0 };

        var (kurzu, gyra) = Spocti(cfg, 200, 0.01);

        Assert.That(kurzu, Is.EqualTo(200));
        Assert.That(gyra, Is.EqualTo(200));
    }

    [Test]
    public void Skrceni_SkokCasuVZAD_RESETUJE()
    {
        // Seek pri prehravani zaznamu, nebo novy zaznam. Bez resetu by se kurz prestal vydavat,
        // dokud by se cas nedotahl zpatky - a to muze byt cela minuta ticha.
        var cfg = new FusionConfig { CompassHeadingMinPeriodSec = 1.0 };
        var mapper = new DefaultMeasurementMapper(cfg);

        int Kurzu(double sekund) => mapper.ToMeasurements(Vn100V(sekund))
            .OfType<HeadingMeasurement>().Count(h => h.Source == "IMU/heading");

        Assert.Multiple(() =>
        {
            Assert.That(Kurzu(100.0), Is.EqualTo(1), "prvni vzorek projde vzdy");
            Assert.That(Kurzu(100.5), Is.EqualTo(0), "pul sekundy po nem uz ne");
            Assert.That(Kurzu(10.0), Is.EqualTo(1), "skok vzad musi skrceni resetovat");
            Assert.That(Kurzu(10.5), Is.EqualTo(0), "a od nove kotvy se skrti dal");
        });
    }

    [Test]
    public void VychoziKadence_JeJEDNOUZaSekundu()
    {
        // Konzervativni zacatek: pomer informace kompas : GPS kurz spadne z ~220:1 na ~2,2:1,
        // ale kompas zustava kotvou. Data argumentuji spis pro 0,1 Hz - az po zmereni na HW.
        Assert.That(new FusionConfig().CompassHeadingMinPeriodSec, Is.EqualTo(1.0).Within(1e-12));
        Assert.That(double.Parse(ParamRegistry.ImuHeadingHz.Def.Default,
                                 System.Globalization.CultureInfo.InvariantCulture),
                    Is.EqualTo(1.0).Within(1e-12),
                    "default parametru se cte z FusionConfig - dve mista se jednou rozejdou");
    }

    [Test]
    public void DvaMapperyNadToutezPosloupnosti_DajiTOTEZ()
    {
        // ⚠️ Skrceni udelalo z mapperu STAVOVY objekt (pamatuje si razitko posledniho kurzu),
        // a to je presne ta vlastnost, ktera umi rozbit prehravani zaznamu. Zaruka, na ktere
        // record/replay stoji, je: stav je ciste funkci POSLOUPNOSTI RAZITEK, takze dve cerstve
        // instance nad toutez posloupnosti vydaji totez. Sdilet JEDNU instanci mezi dvema behy
        // uz zaruka NENI - a produkce to nedela (mapper vznika jednou na ARBotRuntime).
        var cfg = new FusionConfig { CompassHeadingMinPeriodSec = 1.0 };
        var a = new DefaultMeasurementMapper(cfg);
        var b = new DefaultMeasurementMapper(cfg);

        var razitka = new[] { 0.0, 0.3, 0.7, 1.1, 1.4, 2.2, 2.3, 3.9, 4.0, 4.05 };
        var vA = razitka.Select(t => a.ToMeasurements(Vn100V(t))
                                      .OfType<HeadingMeasurement>().Count()).ToArray();
        var vB = razitka.Select(t => b.ToMeasurements(Vn100V(t))
                                      .OfType<HeadingMeasurement>().Count()).ToArray();

        Assert.That(vB, Is.EqualTo(vA));
        Assert.That(vA.Sum(), Is.GreaterThan(1), "test by nic nemeril, kdyby neproslo nic");
        Assert.That(vA.Sum(), Is.LessThan(razitka.Length), "ani kdyby proslo vsechno");
    }

    [TestCase("0")]
    [TestCase("1")]
    [TestCase("0.1")]
    [TestCase("100")]
    public void ParserKadence_BereRozumneHodnoty(string text)
        => Assert.That(ParamParsers.ImuHeadingHz(text).Ok, Is.True, text);

    [TestCase("-1")]
    [TestCase("201")]
    [TestCase("nesmysl")]
    public void ParserKadence_ODMITNE(string text)
        => Assert.That(ParamParsers.ImuHeadingHz(text).Ok, Is.False, text);

    // ---------------- Parser: jednotky ----------------

    [TestCase("0")]
    [TestCase("5")]
    [TestCase("0.1")]
    [TestCase("45")]
    public void Parser_BereRozumneHodnotyVeStupnich(string text)
        => Assert.That(ParamParsers.ImuHeadingStd(text).Ok, Is.True, text);

    [TestCase("0.087", Description = "vypada to na radiany")]
    [TestCase("0.05", Description = "min nez samotne YprU")]
    [TestCase("-1")]
    [TestCase("46")]
    [TestCase("nesmysl")]
    public void Parser_ODMITNE_PodezreleHodnoty(string text)
        => Assert.That(ParamParsers.ImuHeadingStd(text).Ok, Is.False, text);

    [Test]
    public void Parser_UHodnotyVRadianech_ReknePROC()
    {
        // Hlaska je tu to podstatne: "neplatna hodnota" by cloveka nechala hadat, proc 0,087
        // neprosla, kdyz je v rozsahu.
        var r = ParamParsers.ImuHeadingStd("0.087");

        Assert.That(r.Error, Does.Contain("radian").IgnoreCase);
        Assert.That(r.Error, Does.Contain("STUPN").IgnoreCase);
    }
}
