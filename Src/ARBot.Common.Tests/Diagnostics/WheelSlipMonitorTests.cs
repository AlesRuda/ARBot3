using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Common.Tests.Diagnostics;

/// <summary>
/// <b>Hlidac prokluzu kol</b> (<see cref="WheelSlipMonitor"/>, tema <c>lok-fuze-rucni-otoceni</c>).
///
/// <para>Do 11. 10. 2026 nesla kola 24 % integralu uhlove rychlosti fuze a prokluz nebo otoceni
/// robotem rukou sly do kurzu (1. 10. 2026 −19°), aniz by to kdokoli hlasil. Rozhodnuti autora:
/// rozdil gyro / kola hlasit jako prokluz, fuzi nemenit.</para>
///
/// <para>Simulace: gyro 100 Hz, kola 91 Hz (jina faze, jako na robotu). Cas je cas DAT.</para>
/// </summary>
public class WheelSlipMonitorTests
{
    private static readonly DateTime T0 = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);
    private const double B = 0.41;   // rozchod (Profile.Rozchod)

    private sealed class Scena
    {
        public Func<double, double> Gyro = _ => 0.0;     // uhlova rychlost tela [rad/s]
        public Func<double, double> Kola = _ => 0.0;     // uhlova rychlost z kol [rad/s]
        public Func<double, double> V = _ => 0.0;        // dopredna rychlost [m/s]
        public Func<double, bool> GyroChybi = _ => false;
        public Func<double, bool> KolaChybi = _ => false;
    }

    private sealed class Vysledek
    {
        public readonly List<(double T, string Line)> Lines = new();
        public readonly List<(double T, WheelSlipMsg Msg)> Msgs = new();
        public WheelSlipMonitor Monitor;
        public IEnumerable<WheelSlipMsg> Konce => Msgs.Select(m => m.Msg).Where(m => m.Level == 0);
    }

    private static IMUState Imu(DateTime t, double w)
        => new IMUState { TimeStamp = t, HasAbsoluteHeading = true, AngularVelocity = new Vector3(0, 0, (float)w) };

    private static MotorStateBase Motor(DateTime t, double w, double v)
    {
        // OdoOmegaSign = +1: w = (vR - vL) / B
        return new MotorStateBase(false, 0, 0, 12, 0, 0, v - w * B / 2, v + w * B / 2) { TimeStamp = t };
    }

    /// <summary>Gyro po 10 ms a kola po 11 ms (s posunem 3 ms) od 0 do <paramref name="doSec"/>.</summary>
    private static Vysledek Prehraj(Scena s, double doSec, WheelSlipConfig cfg = null, DateTime? t0 = null)
    {
        var v = new Vysledek();
        var zac = t0 ?? T0;
        double tNow = 0;
        v.Monitor = new WheelSlipMonitor(cfg, line => v.Lines.Add((tNow, line)));
        int ig = 0, io = 0;
        while (true)
        {
            double tg = ig * 0.010, to = 0.003 + io * 0.011;
            if (tg > doSec && to > doSec) break;
            Message m;
            if (tg <= to)
            {
                tNow = tg; ig++;
                if (s.GyroChybi(tg)) continue;
                m = Imu(zac.AddSeconds(tg), s.Gyro(tg));
            }
            else
            {
                tNow = to; io++;
                if (s.KolaChybi(to)) continue;
                m = Motor(zac.AddSeconds(to), s.Kola(to), s.V(to));
            }
            var o = v.Monitor.Process(m);
            if (o != null) v.Msgs.Add((tNow, o));
        }
        return v;
    }

    private const double Deg = Math.PI / 180;

    [Test]
    public void Shoda_RovneIVZatacce_NicNehlasi()
    {
        // Zatacka 40 deg/s s systematickym smykem podvozku 7 % (kola hlasi vic) - to prokluz neni.
        var s = new Scena
        {
            V = _ => 0.8,
            Gyro = t => t < 5 ? 0.0 : 40 * Deg,
            Kola = t => t < 5 ? 0.0 : 40 * Deg * 1.07,
        };
        var v = Prehraj(s, 12);
        Assert.Multiple(() =>
        {
            Assert.That(v.Msgs, Is.Empty);
            Assert.That(v.Lines, Is.Empty);
        });
    }

    [Test]
    public void RychlaZatackaSeSmykem_PomernyPrahProkluzNehlasi()
    {
        // Zatacka 1 rad/s, kola hlasi o 20 % vic: rozdil 0,2 rad/s je nad pevnou casti prahu
        // (0,15), takze bez pomerne casti by se hlasil kazdy rychly oblouk (nalez kontroly).
        var s = new Scena { V = _ => 0.6, Gyro = t => t >= 2 && t < 5 ? 1.0 : 0.0, Kola = t => t >= 2 && t < 5 ? 1.2 : 0.0 };
        Assert.That(Prehraj(s, 7).Msgs, Is.Empty, "vychozi prahy");
        Assert.That(Prehraj(s, 7, new WheelSlipConfig { RelThreshold = 0 }).Konce, Is.Not.Empty,
                    "bez pomerne casti by se ohlasil - test tedy pomerny prah opravdu hlida");
    }

    [Test]
    public void MaleOtoceni_PodDesetStupnu_NicNehlasi()
    {
        // 0,6 rad/s po 0,25 s = 8,6 deg: prumer okna je nad prahem dele nez MinOnSec, ale rozdil
        // nedosahne 10 deg. Nad 50 zaznamy byly takove epizody nejasne (nalez kontroly: brana nemela test).
        var s = new Scena { Gyro = t => t >= 2 && t < 2.25 ? 0.6 : 0.0 };
        Assert.That(Prehraj(s, 5).Msgs, Is.Empty);
        Assert.That(Prehraj(s, 5, new WheelSlipConfig { MinAngleRad = 0 }).Konce, Is.Not.Empty,
                    "bez brany by se ohlasil - test tedy branu opravdu hlida");
    }

    [Test]
    public void Hystereze_EpizodaTrvaPodPrahemAzDoPolovinyPrahu()
    {
        // Po silnem otoceni (1 rad/s) zbyde 0,15 rad/s: pod prahem (0,1875), nad jeho polovinou.
        // Epizoda trva dal a zapocita i tuhle cast; bez hystereze by po MinOffSec skoncila.
        var s = new Scena { Gyro = t => t >= 2 && t < 3 ? 1.0 : t >= 3 && t < 5 ? 0.15 : 0.0 };
        var konce = Prehraj(s, 8).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That(konce[0].GyroAngle, Is.EqualTo(1.0 + 0.15 * 2).Within(0.05));
    }

    [Test]
    public void OtaceniRukou_KolaStoji_JednaEpizoda()
    {
        // 1. 10. 2026 15:27:34: obsluha otocila stojicim robotem o ~60-70 deg, kola stala.
        var s = new Scena { Gyro = t => t >= 2 && t < 4 ? 1.0 : 0.0 };
        var v = Prehraj(s, 8);
        var konce = v.Konce.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(konce, Has.Count.EqualTo(1));
            Assert.That((WheelSlipKind)konce[0].Kind, Is.EqualTo(WheelSlipKind.WheelsStill));
            Assert.That(konce[0].GyroAngle, Is.EqualTo(2.0).Within(0.05), "∫gyro celé epizody vcetne okna pred detekci");
            Assert.That(konce[0].OdoAngle, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(konce[0].EpisodeSec, Is.EqualTo(2.0).Within(0.15), "delka = kdy se rozdil nascital, ne doba prumeru nad prahem");
            Assert.That((konce[0].EpisodeStart - T0).TotalSeconds, Is.EqualTo(2.0).Within(0.1), "zacatek prokluzu, ne okamzik ohlaseni");
            Assert.That(v.Msgs.First().Msg.Level, Is.EqualTo((byte)WheelSlipLevel.Slip), "prvni zprava = zacatek");
            Assert.That(v.Msgs.First().T, Is.InRange(2.3, 2.8), "ohlasi se az po MinOnSec nad prahem");
            Assert.That(v.Msgs, Has.Count.InRange(3, 5), "zacatek, pak 1 Hz, konec - ne kazde vyhodnoceni");
            Assert.That(v.Lines, Has.Count.EqualTo(1));
            Assert.That(v.Lines[0].Line, Does.StartWith("PROKLUZ kol"));
            Assert.That(v.Lines[0].Line, Does.Contain("kola stoji, robot se otaci"));
            Assert.That(v.Lines[0].Line.All(c => c < 128), Is.True, "radky do Trace jsou ASCII");
        });
    }

    [Test]
    public void KolaHrabou_RobotStoji_Prokluz()
    {
        // 1. 10. 2026 15:27:18: kola −49 deg, telo +6 deg - kola hrabou na miste.
        var s = new Scena { Kola = t => t >= 1 && t < 3 ? -0.4 : 0.0, Gyro = t => t >= 1 && t < 3 ? 0.05 : 0.0 };
        var konce = Prehraj(s, 6).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That((WheelSlipKind)konce[0].Kind, Is.EqualTo(WheelSlipKind.WheelsMore));
    }

    [Test]
    public void KolaProtiRobotu()
    {
        var s = new Scena { Kola = t => t >= 1 && t < 3 ? -0.5 : 0.0, Gyro = t => t >= 1 && t < 3 ? 1.0 : 0.0 };
        var konce = Prehraj(s, 6).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That((WheelSlipKind)konce[0].Kind, Is.EqualTo(WheelSlipKind.Opposite));
    }

    [Test]
    public void VibraceAKratkyRaz_NicNehlasi()
    {
        // Za jizdy se gyro meni o 7-17 deg/s za 10 ms (vibrace tela, ktere kola nevidi) - prumer pres
        // okno je ale nulovy. A kratky raz (0,05 s, ~3 deg) prumer okna nad prah nedostane.
        var s = new Scena
        {
            V = _ => 0.8,
            Gyro = t => 0.8 * Math.Sin(2 * Math.PI * 10 * t) + (t >= 4 && t < 4.05 ? 1.0 : 0.0),
        };
        Assert.That(Prehraj(s, 8).Msgs, Is.Empty);
    }

    [Test]
    public void KratkyAleVelkyRozdil_SeOhlasi()
    {
        // 0,15 s pri 86 deg/s = 13 deg, ktere kola nevidi - to uz do kurzu fuze zalezi.
        var s = new Scena { Gyro = t => t >= 2 && t < 2.15 ? 1.5 : 0.0 };
        var konce = Prehraj(s, 5).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That(konce[0].GyroAngle, Is.EqualTo(0.225).Within(0.02));
        Assert.That(konce[0].EpisodeSec, Is.InRange(0.1, 0.25), "kratka epizoda nesmi mit delku nula ani delku okna");
    }

    [Test]
    public void MezeraVGyru_NeniProkluz()
    {
        // Zasek procesu 1. 10. 2026 (1,19 s bez dat): robot zataci, gyro chvili chybi. Drzena
        // posledni hodnota gyra by proti kolum vypadala jako prokluz - v mezere se nerozhoduje.
        // 2 rad/s: drzena hodnota gyra z doby pred zatackou (0) by proti kolum (2 rad/s) dala
        // falesny prokluz i po MinOnSec (pri 0,8 rad/s by ho schovalo uz MinOnSec - test by pak
        // kontrolu mezery nehlidal, nalez kontroly 11. 10. 2026).
        var s = new Scena
        {
            V = _ => 0.5,
            Gyro = t => t < 3 ? 0.0 : 2.0,
            Kola = t => t < 3 ? 0.0 : 2.0,
            GyroChybi = t => t >= 2.9 && t < 4.1,
        };
        Assert.That(Prehraj(s, 8).Msgs, Is.Empty);
    }

    [Test]
    public void KratkyProkluzPriVibracich_DelkaNeniNafouknuta()
    {
        // Nalez kontroly 11. 10. 2026: |rozdil| po useccich scital vibrace gyra, takze z 0,15s
        // prokluzu vyslo 1,5 s. Se znamenkem epizody se vibrace vyrusi.
        var s = new Scena
        {
            V = _ => 0.8,
            Gyro = t => 0.8 * Math.Sin(2 * Math.PI * 10 * t) + (t >= 2 && t < 2.15 ? 1.5 : 0.0),
        };
        var konce = Prehraj(s, 6).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That(konce[0].EpisodeSec, Is.LessThan(0.4));
        Assert.That(konce[0].GyroAngle - konce[0].OdoAngle, Is.EqualTo(0.225).Within(0.03));
    }

    [Test]
    public void MezeraVGyruBehemOtaceni_UhelSeNeztrati()
    {
        // Nalez kontroly: po 0,2s mezere se zahodila i pulsekunda, nez se okno znovu naplnilo -
        // epizoda hlasila 131 deg misto ~172. Ztratit se smi jen sama mezera (0,2 rad).
        var s = new Scena { Gyro = t => t >= 2 && t < 5 ? 1.0 : 0.0, GyroChybi = t => t >= 3.0 && t < 3.2 };
        var konce = Prehraj(s, 9).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That(konce[0].GyroAngle, Is.EqualTo(2.8).Within(0.1));
    }

    [Test]
    public void DelsiMezeraBehemOtaceni_EpizodaSeNerozdeli()
    {
        // Nalez kontroly: casovy limit se meril od posledniho PLATNEHO okna, takze naplneni okna po
        // mezere se pocitalo jako „bez dat" a 0,55s mezera rozdelila jedno otoceni na dve epizody.
        var s = new Scena { Gyro = t => t >= 2 && t < 5 ? 1.0 : 0.0, GyroChybi = t => t >= 3.0 && t < 3.55 };
        var konce = Prehraj(s, 9).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That(konce[0].GyroAngle, Is.EqualTo(3.0 - 0.55).Within(0.1));
    }

    [Test]
    public void KolaPrestanouChodit_EpizodaSkonciAZustaneStopa()
    {
        // Nalez kontroly: kdyz jeden zdroj prestane chodit uplne, konec okna stoji, Evaluate se
        // nevola a epizoda nekonci nikdy - po prokluzu nezustal ani radek v Trace.
        var s = new Scena { Gyro = t => t >= 2 ? 1.0 : 0.0, KolaChybi = t => t >= 3.0 };
        var v = Prehraj(s, 10);
        var konce = v.Konce.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(konce, Has.Count.EqualTo(1));
            Assert.That(konce[0].Text, Does.Contain("chybí data"));
            Assert.That(v.Lines.Select(l => l.Line), Has.Some.Contains("chybi data"));
            Assert.That(v.Monitor.Read(T0.AddSeconds(10)).Text, Does.StartWith("prokluz kol před"));
        });
    }

    [Test]
    public void GyroRidne_EpizodaSkonciNaChybejiciData()
    {
        // Gyro chodi jen 5x za sekundu (mezery nad MaxGapSec), konec okna se ale posouva -
        // epizoda musi skoncit pres neplatne okno, ne viset.
        var s = new Scena { Gyro = t => t >= 2 ? 1.0 : 0.0, GyroChybi = t => t >= 3.0 && (t * 100) % 20 >= 1 };
        var konce = Prehraj(s, 7).Konce.ToList();
        Assert.That(konce, Has.Count.EqualTo(1));
        Assert.That(konce[0].Text, Does.Contain("chybí data"));
    }

    [Test]
    public void Zastaveni_ProbihajiciProkluzSeOhlasi()
    {
        var s = new Scena { Gyro = t => t >= 2 ? 1.0 : 0.0 };
        var v = Prehraj(s, 4);
        Assert.That(v.Lines, Is.Empty, "epizoda trva, radek prijde az pri konci");
        var radky = new List<string>();
        var det = new WheelSlipDetector();
        // Tataz scena primo nad detektorem, pak Flush jako pri zastaveni runtime.
        for (int k = 0; k <= 400; k++)
        {
            var t = T0.AddSeconds(k * 0.01);
            det.Feed(Imu(t, k >= 200 ? 1.0 : 0.0), radky);
            det.Feed(Motor(t.AddMilliseconds(3), 0, 0), radky);
        }
        Assert.That(det.Level, Is.EqualTo(WheelSlipLevel.Slip));
        var m = det.Flush(radky, "runtime se zastavuje");
        Assert.Multiple(() =>
        {
            Assert.That(m, Is.Not.Null);
            Assert.That(m.Level, Is.EqualTo((byte)WheelSlipLevel.None));
            Assert.That(radky, Has.Some.Contains("runtime se zastavuje"));
            Assert.That(det.Level, Is.EqualTo(WheelSlipLevel.None));
        });
    }

    [Test]
    public void MezeraBehemEpizody_StrankaNeukazujeNaN()
    {
        var s = new Scena { Gyro = t => t >= 2 ? 1.0 : 0.0, GyroChybi = t => t >= 3.0 && t < 3.15 };
        var v = Prehraj(s, 3.3);
        var r = v.Monitor.Read(T0.AddSeconds(3.3));
        Assert.That(r.Level, Is.EqualTo(WheelSlipLevel.Slip));
        Assert.That(r.Text, Does.Not.Contain("NaN"));
    }

    [Test]
    public void MnohoEpizod_TraceSeSkrti_ZpravyNe()
    {
        // Kalibrace kompasu (12. 9. 2026): 64 otoceni rukou za sebou. Do Trace jde prvni cela,
        // dalsi do 5 s jen pocitem; do zaznamu jde kazda.
        // Otaceni do 21 s, pak klid dele nez TracePeriodSec - posledni potlacene musi prijit souhrnem.
        var s = new Scena { Gyro = t => (t % 3.0) < 1.5 && t > 1 && t < 21 ? 1.0 : 0.0 };
        var v = Prehraj(s, 30);
        int epizod = v.Konce.Count();
        Assert.Multiple(() =>
        {
            Assert.That(epizod, Is.GreaterThanOrEqualTo(6));
            Assert.That(v.Lines.Count, Is.LessThanOrEqualTo(30 / 5 + 2), "nejvys jeden radek za TracePeriodSec");
            Assert.That(v.Lines.Any(l => l.Line.Contains("dalsich")), Is.True, "potlacene se ohlasi souhrnem");
            // Posledni epizody davky se ohlasi odlozenym souhrnem i bez dalsi epizody (Tick).
            Assert.That(v.Lines.Last().Line, Does.StartWith("PROKLUZ kol: dalsich"));
        });
    }

    [Test]
    public void Stranka_ProbihajiciPakNedavnyPakNic()
    {
        var s = new Scena { Gyro = t => t >= 2 && t < 5 ? 1.0 : 0.0 };
        var v = Prehraj(s, 4);   // uprostred epizody
        var ted = v.Monitor.Read(T0.AddSeconds(4));
        Assert.Multiple(() =>
        {
            Assert.That(ted.Level, Is.EqualTo(WheelSlipLevel.Slip));
            Assert.That(ted.Recent, Is.False);
            Assert.That(ted.Text, Does.StartWith("PROKLUZ kol"));
            Assert.That(ted.Text, Does.Contain("kola stojí, robot se otáčí"));
        });

        var po = Prehraj(s, 7);
        var r1 = po.Monitor.Read(T0.AddSeconds(10));
        var r2 = po.Monitor.Read(T0.AddSeconds(40));
        Assert.Multiple(() =>
        {
            Assert.That(r1.Level, Is.EqualTo(WheelSlipLevel.None));
            Assert.That(r1.Recent, Is.True);
            Assert.That(r1.Text, Does.StartWith("prokluz kol před"));
            Assert.That(r2.Text, Is.Empty, "po RecentSec se uz neukazuje");
        });
    }

    [Test]
    public void Stranka_BezCerstvychDat_ProbihajiciNeukazuje()
    {
        var s = new Scena { Gyro = t => t >= 2 ? 1.0 : 0.0 };
        var v = Prehraj(s, 4);
        Assert.That(v.Monitor.Read(T0.AddSeconds(4)).Level, Is.EqualTo(WheelSlipLevel.Slip));
        Assert.That(v.Monitor.Read(T0.AddSeconds(4 + WheelSlipMonitor.StaleSec + 1)).Text, Is.Empty,
                    "hlidac bez dat nesmi tvrdit, ze prokluz trva");
    }

    [Test]
    public void VzorekSCasemPozadu_SeZahodi()
    {
        var det = new WheelSlipDetector();
        det.Feed(Imu(T0.AddSeconds(1), 0), null);
        det.Feed(Imu(T0.AddSeconds(0.5), 0), null);
        det.Feed(Motor(T0.AddSeconds(1), 0, 0), null);
        det.Feed(Motor(T0.AddSeconds(0.9), 0, 0), null);
        Assert.That(det.OutOfOrder, Is.EqualTo(2));
    }

    [Test]
    public void ImuBezAbsolutnihoKurzu_SeNepouzije()
    {
        // T265 (relativni yaw) do fuze nenese IMU/gyro - hlidac ji take nebere.
        var det = new WheelSlipDetector();
        for (int k = 0; k < 300; k++)
        {
            var t = T0.AddSeconds(k * 0.01);
            det.Feed(new IMUState { TimeStamp = t, HasAbsoluteHeading = false, AngularVelocity = new Vector3(0, 0, 1) }, null);
            det.Feed(Motor(t.AddMilliseconds(3), 0, 0), null);
        }
        Assert.That(det.Level, Is.EqualTo(WheelSlipLevel.None));
        Assert.That(det.LastEval, Is.EqualTo(default(DateTime)), "bez gyra se nic nevyhodnocuje");
    }

    [TestCase(-49, 6, WheelSlipKind.WheelsMore)]     // 1. 10. 15:27:18 - kola hrabou
    [TestCase(0, 27, WheelSlipKind.WheelsStill)]     // otoceni rukou
    [TestCase(-10, 68, WheelSlipKind.WheelsStill)]   // 1. 10. 15:27:34 - kola skoro stoji
    [TestCase(-30, 40, WheelSlipKind.Opposite)]
    [TestCase(50, 40, WheelSlipKind.WheelsMore)]
    [TestCase(40, 50, WheelSlipKind.BodyMore)]
    public void Druh_ZIntegralu(double kolaDeg, double gyroDeg, WheelSlipKind ocekavany)
        => Assert.That(WheelSlipDetector.KindOfIntegrals(kolaDeg * Deg, gyroDeg * Deg), Is.EqualTo(ocekavany));

    [Test]
    public void Konfigurace_PrebiraRozchodAZnamenkoZFuze()
    {
        var c = WheelSlipConfig.From(new ARBot.Common.Fusion.FusionConfig { WheelBase = 0.5, OdoOmegaSign = -1 });
        Assert.That((c.WheelBase, c.OdoOmegaSign), Is.EqualTo((0.5, -1.0)));
    }

    [Test]
    public void Zprava_TamAZpet()
    {
        var m = new WheelSlipMsg
        {
            Level = 1, Kind = (byte)WheelSlipKind.WheelsStill, OdoRate = 0.01, GyroRate = 1.2, Speed = 0.02,
            Threshold = 0.45, EpisodeSec = 1.75, EpisodeStart = T0.AddSeconds(-2), OdoAngle = -0.1, GyroAngle = 1.3,
            Text = "PROKLUZ kol 1,8 s: kola 1 °/s, robot 69 °/s — kola stojí", TimeStamp = T0,
        };
        var buf = new System.IO.MemoryStream();
        using (var bw = new System.IO.BinaryWriter(buf, System.Text.Encoding.UTF8, leaveOpen: true)) m.ToData(bw);
        buf.Position = 0;
        var r = new WheelSlipMsg();
        using (var br = new System.IO.BinaryReader(buf, System.Text.Encoding.UTF8, leaveOpen: true)) r.FromData(br);

        Assert.Multiple(() =>
        {
            Assert.That(r.Level, Is.EqualTo(1));
            Assert.That(r.Kind, Is.EqualTo((byte)WheelSlipKind.WheelsStill));
            Assert.That(r.OdoRate, Is.EqualTo(0.01));
            Assert.That(r.GyroRate, Is.EqualTo(1.2));
            Assert.That(r.Speed, Is.EqualTo(0.02));
            Assert.That(r.Threshold, Is.EqualTo(0.45));
            Assert.That(r.EpisodeSec, Is.EqualTo(1.75));
            Assert.That(r.EpisodeStart, Is.EqualTo(T0.AddSeconds(-2)));
            Assert.That(r.OdoAngle, Is.EqualTo(-0.1));
            Assert.That(r.GyroAngle, Is.EqualTo(1.3));
            Assert.That(r.Text, Is.EqualTo(m.Text));
            Assert.That(r.TimeStamp, Is.EqualTo(T0));
            Assert.That(buf.Position, Is.EqualTo(buf.Length));
        });
    }

    [Test]
    public void Katalog_ZnaZpravu()
    {
        Assert.That(ARBot.Common.Communication.MessageCatalog.RecordDefaults().Contains(new WheelSlipMsg().MsgName), Is.True);
    }
}
