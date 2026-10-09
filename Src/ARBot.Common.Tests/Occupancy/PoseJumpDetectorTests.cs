using System;
using ARBot.Common.Occupancy;

namespace ARBot.Common.Tests.Occupancy;

/// <summary>
/// Testy detekce skoku pozy (viz doc/map-correlation-localization.md, "Zpetna vazba na grid").
/// Skok = poza se posunula vic, nez vysvetli rychlost.
/// </summary>
public class PoseJumpDetectorTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void PrvniPoza_NeniSkok()
    {
        var d = new PoseJumpDetector();

        Assert.That(d.Check(10, 20, theta: 0, v: 1.0, omega: 0, T0), Is.False);
    }

    [Test]
    public void PohybOdpovidajiciRychlosti_NeniSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 2.0, omega: 0, T0);

        // Za 0,5 s pri 2 m/s se ceka 1 m; ujel presne 1 m.
        Assert.That(d.Check(1.0, 0, theta: 0, v: 2.0, omega: 0, T0.AddSeconds(0.5)), Is.False);
    }

    [Test]
    public void PosunNadToleranci_JeSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 0.0, omega: 0, T0);

        // Stoji, a presto se posunul o 2 m.
        Assert.That(d.Check(2.0, 0, theta: 0, v: 0.0, omega: 0, T0.AddSeconds(0.1)), Is.True);
    }

    /// <summary>
    /// Popis skoku do Trace (lp-mazani-gridu-bez-stopy): po skoku detektor rekne, CO videl -
    /// druh, velikost, kolik vysvetli rychlost a za jak dlouho. Bez toho se smazani gridu
    /// dohledavalo jen z propadu znamych bunek ve snapshotech.
    /// </summary>
    [Test]
    public void SkokPosunem_PopisUvedePosunAVysvetleni()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 0.5, omega: 0, T0);
        Assert.That(d.Check(2.0, 0, theta: 0, v: 0.5, omega: 0, T0.AddSeconds(0.1)), Is.True);

        Assert.Multiple(() =>
        {
            Assert.That(d.LastJumpKind, Is.EqualTo("posun"));
            Assert.That(d.LastJumpDescription, Does.Contain("posun 2.00 m"));
            Assert.That(d.LastJumpDescription, Does.Contain("rychlost vysvetli 0.05 m"));
            Assert.That(d.LastJumpDescription, Does.Contain("za 0.10 s"));
        });
    }

    [Test]
    public void SkokKurzem_PopisUvedeKurz()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0, omega: 0, T0);
        Assert.That(d.Check(0, 0, theta: 10 * Math.PI / 180, v: 0, omega: 0, T0.AddSeconds(0.1)), Is.True);

        Assert.That(d.LastJumpKind, Is.EqualTo("kurz"));
        Assert.That(d.LastJumpDescription, Does.Contain("kurz 10.0 deg"));
    }

    [Test]
    public void SkokPosunemIKurzem_DruhObsahujeObe()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0, omega: 0, T0);
        Assert.That(d.Check(3, 0, theta: 0.5, v: 0, omega: 0, T0.AddSeconds(0.1)), Is.True);

        Assert.That(d.LastJumpKind, Is.EqualTo("posun+kurz"));
    }

    [Test]
    public void SkokNaSnimkuSCasemPozadu_PopisToRekne()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0, omega: 0, T0);
        Assert.That(d.Check(2, 0, theta: 0, v: 0, omega: 0, T0.AddSeconds(-0.05)), Is.True);

        Assert.That(d.LastJumpDescription, Does.Contain("cas pozadu"));
    }

    [Test]
    public void MalaKorekce_NeniSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 0.0, omega: 0, T0);

        // Typicka korekce korelatoru - jednotky cm.
        Assert.That(d.Check(0.05, 0.03, theta: 0, v: 0.0, omega: 0, T0.AddSeconds(0.1)), Is.False);
    }

    [Test]
    public void PohybVzad_SePosuzujePodleVzdalenosti()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 1.0, omega: 0, T0);

        // Absolutni hodnota rychlosti - couvani neni skok.
        Assert.That(d.Check(-0.5, 0, theta: 0, v: -1.0, omega: 0, T0.AddSeconds(0.5)), Is.False);
    }

    [Test]
    public void CasPozadu_NeniSkok()
    {
        // Snimek z druhe kamery muze prijit s casem drive nez predchozi; to neni skok pozy.
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 1.0, omega: 0, T0.AddSeconds(1));

        Assert.That(d.Check(0.1, 0, theta: 0, v: 1.0, omega: 0, T0), Is.False);
    }

    /// <summary>
    /// REGRESE (lok-skok-pozy-nedetekce, Robotour 19. 9. 2026): skok, ktery pripadne na snimek
    /// s casem POZADU (prehozene snimky dvou kamer), se driv spolkl - detektor pozu jen zapamatoval
    /// a dalsi snimek uz porovnal s pozou po skoku. Grid se nesmazal.
    /// </summary>
    [Test]
    public void SkokNaSnimkuSCasemPozadu_JeSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 1.0, omega: 0, T0.AddSeconds(1.000));

        // Snimek druhe kamery o 10 ms drive, ale uz po korekci o 2 m.
        Assert.That(d.Check(2.0, 0, theta: 0, v: 1.0, omega: 0, T0.AddSeconds(0.990)), Is.True);
        // Dalsi snimek navazuje na pozu po skoku - podruhe se grid mazat nesmi.
        Assert.That(d.Check(2.03, 0, theta: 0, v: 1.0, omega: 0, T0.AddSeconds(1.023)), Is.False);
    }

    /// <summary>Totez pro skok kurzu (konvergence kurzu, korekce kurzu z koridoru).</summary>
    [Test]
    public void SkokKurzuNaSnimkuSCasemPozadu_JeSkok()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0.0, omega: 0, T0.AddSeconds(1.000));

        double theta = 20.0 * Math.PI / 180.0;
        Assert.That(d.Check(0, 0, theta: theta, v: 0.0, omega: 0, T0.AddSeconds(0.990)), Is.True);
    }

    /// <summary>Stare chovani (A/B): u casu pozadu se skok nekontroluje a spolkne se.</summary>
    [Test]
    public void BezKontrolyCasuPozadu_SkokSeSpolkne()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5, CheckBackwardTime = false };
        d.Check(0, 0, theta: 0, v: 1.0, omega: 0, T0.AddSeconds(1.000));

        Assert.That(d.Check(2.0, 0, theta: 0, v: 1.0, omega: 0, T0.AddSeconds(0.990)), Is.False);
        Assert.That(d.Check(2.03, 0, theta: 0, v: 1.0, omega: 0, T0.AddSeconds(1.023)), Is.False);
    }

    /// <summary>
    /// Pohyb mezi prehozenymi snimky vysvetli rychlost za <c>|dt|</c> - jizda s prehozenymi
    /// snimky skok hlasit nesmi.
    /// </summary>
    [Test]
    public void PrehozeneSnimkyZaJizdy_NeniSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        // 1,5 m/s, kamery A a B s rozestupem 15 ms a casto v obracenem poradi.
        double[] t = { 0.000, 0.033, 0.018, 0.066, 0.051, 0.100, 0.084 };
        foreach (double s in t)
            Assert.That(d.Check(1.5 * s, 0, theta: 0, v: 1.5, omega: 0, T0.AddSeconds(s)), Is.False, $"t={s}");
    }

    [Test]
    public void Reset_ZapomeneStav()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, theta: 0, v: 0.0, omega: 0, T0);
        d.Reset();

        // Po resetu je dalsi poza znovu "prvni", takze skok nehlasi.
        Assert.That(d.Check(50.0, 0, theta: 0, v: 0.0, omega: 0, T0.AddSeconds(0.1)), Is.False);
    }

    // ==================== Rotace ====================
    // Grid je world-kotveny, takze obsah posouva i ROTACE - a to o R*dTheta, tedy pri dohledu 6 m
    // uz pri par stupnich vic, nez povoli translacni tolerance. Detektor proto musi hlidat i kurz.

    /// <summary>
    /// REGRESE (naměřeno 19. 8. 2026, zaznam 20260819-233057.rec): pri startu neni kurz v EKF
    /// inicializovany (<c>InitializePosition</c> nastavuje jen X/Y), takze jde od 0 ke skutecne
    /// hodnote - na te mape 170°. Robot pritom STOJI, takze <c>moved = 0</c> a puvodni detektor
    /// skok nehlasil; grid si nechal snimky ulozene s obracenym kurzem a prvni korelace z nich
    /// vysla se spatnym znamenkem. Viz doc/map-correlation-localization.md.
    /// </summary>
    [Test]
    public void RotaceNevysvetlenaOmegou_JeSkok()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0.0, omega: 0, T0);

        // Stoji na miste, ale kurz se prehodil o 170° - to zadna omega nevysvetli.
        double theta = 170.0 * Math.PI / 180.0;
        Assert.That(d.Check(0, 0, theta: theta, v: 0.0, omega: 0, T0.AddSeconds(0.1)), Is.True);
    }

    /// <summary>Bezne zataceni skok NENI - rotaci vysvetli <c>omega</c>.</summary>
    [Test]
    public void RotaceOdpovidajiciOmeze_NeniSkok()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0.0, omega: 1.0, T0);

        // omega = 1 rad/s po 0,5 s = 0,5 rad; presne to, co se stalo.
        Assert.That(d.Check(0, 0, theta: 0.5, v: 0.0, omega: 1.0, T0.AddSeconds(0.5)), Is.False);
    }

    /// <summary>Otaceni doprava (negativni omega) se posuzuje podle VELIKOSTI, ne znamenka.</summary>
    [Test]
    public void RotaceVzad_SePosuzujePodleVelikosti()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0.0, omega: -1.0, T0);

        Assert.That(d.Check(0, 0, theta: -0.5, v: 0.0, omega: -1.0, T0.AddSeconds(0.5)), Is.False);
    }

    /// <summary>
    /// Prechod pres ±180° je zmena o 2°, ne o 358° - bez normalizace by detektor hlasil skok
    /// pokazde, kdyz robot miri na zapad (a prave tam mirí na HajeRovne).
    /// </summary>
    [Test]
    public void PrechodPres180Stupnu_NeniSkok()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 179.0 * Math.PI / 180.0, v: 0.0, omega: 0, T0);

        Assert.That(d.Check(0, 0, theta: -179.0 * Math.PI / 180.0, v: 0.0, omega: 0,
                            T0.AddSeconds(0.1)), Is.False);
    }

    /// <summary>
    /// Sum kurzu z EKF (namereno ~0,7° za 100 ms u stojiciho robotu) skok hlasit NESMI - jinak
    /// by se grid zahazoval porad a nikdy by se nenaplnil.
    /// </summary>
    [Test]
    public void SumKurzu_NeniSkok()
    {
        var d = new PoseJumpDetector();
        d.Check(0, 0, theta: 0, v: 0.0, omega: 0, T0);

        double sum = 0.7 * Math.PI / 180.0;
        Assert.That(d.Check(0, 0, theta: sum, v: 0.0, omega: 0, T0.AddSeconds(0.1)), Is.False);
    }

    /// <summary>Tolerance rotace musi jit nastavit stejne jako translacni.</summary>
    [Test]
    public void ToleranceRotace_JeNastavitelna()
    {
        var d = new PoseJumpDetector { ToleranceRad = 30.0 * Math.PI / 180.0 };
        d.Check(0, 0, theta: 0, v: 0.0, omega: 0, T0);

        double deset = 10.0 * Math.PI / 180.0;
        Assert.That(d.Check(0, 0, theta: deset, v: 0.0, omega: 0, T0.AddSeconds(0.1)), Is.False,
                    "10° pod zvednutou toleranci 30° neni skok");
    }
}
