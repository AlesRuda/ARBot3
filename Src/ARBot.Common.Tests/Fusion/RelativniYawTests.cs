using System.Linq;
using System.Numerics;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Fusion;

/// <summary>
/// Zpracování <b>relativního</b> kurzu (T265 / VIO) ve fúzi — 6. 9. 2026.
///
/// <para><b>Proč to má vlastní cestu:</b> T265 nemá magnetometr, takže její yaw je o <b>neznámou
/// konstantu</b> vedle severu. Poslat ho do fúze jako kurz by znamenalo vnutit filtru libovolně
/// otočený svět — a ze zprávy to do téhle změny nešlo poznat, takže to musel vědět každý čtenář
/// předem. Testy hlídají hlavně to, co se stát <b>nesmí</b>: že z relativního zdroje nikdy
/// nevznikne <see cref="HeadingMeasurement"/>.</para>
///
/// <para>Použitelná je jeho <b>změna</b>: v rozdílu dvou odečtů yaw se neznámá konstanta odečte.</para>
/// </summary>
public class RelativniYawTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);

    /// <summary>IMU s daným yaw [rad]; <paramref name="absolutni"/> = má magnetometr (VN100).</summary>
    private static IMUState Imu(double yaw, double sekundOdT0, bool absolutni, double confidence = 1)
        => new IMUState
        {
            Name = absolutni ? "VN100" : "T265 925122110155",
            HasAbsoluteHeading = absolutni,
            // Kvaternion se sklada TOUTEZ cestou jako u virtualniho IMU (YawPitchRoll.zxy),
            // aby test nezavisel na jine konvenci nez zbytek projektu.
            Rotation = new YawPitchRoll((float)yaw, 0, 0).ToQuaternion(YawPitchRoll.Euler.zxy),
            AngularVelocity = new Vector3(0, 0, 0.123f),
            Confidence = confidence,
            TimeStamp = T0.AddSeconds(sekundOdT0),
        };

    private static DefaultMeasurementMapper Mapper(FusionConfig cfg = null)
        => new DefaultMeasurementMapper(cfg ?? new FusionConfig());

    // ---------------- Co se stát NESMÍ ----------------

    [Test]
    public void RelativniZdroj_NikdyNedaKurz()
    {
        var m = Mapper();

        var mereni = m.ToMeasurements(Imu(0.5, 0, absolutni: false))
                      .Concat(m.ToMeasurements(Imu(0.7, 1.0, absolutni: false)))
                      .ToList();

        Assert.That(mereni.OfType<HeadingMeasurement>(), Is.Empty,
                    "relativní yaw se nesmí dostat do fúze jako absolutní kurz");
    }

    [Test]
    public void RelativniZdroj_NepridavaSvujSurovyGyroskop()
    {
        // Byl by to tyz fyzikalni pohyb podruhe - filtr by si informaci spocital dvakrat.
        var m = Mapper();
        m.ToMeasurements(Imu(0.0, 0, absolutni: false)).ToList();

        var mereni = m.ToMeasurements(Imu(0.1, 1.0, absolutni: false)).ToList();

        Assert.That(mereni.Select(x => x.Source), Has.None.EqualTo("IMU/gyro"));
    }

    [Test]
    public void AbsolutniZdroj_SeChovaJakoDriv()
    {
        // Regrese: VN100 musi dal davat kurz i gyro, jinak by ta zmena rozbila stavajici fuzi.
        var mereni = Mapper().ToMeasurements(Imu(0.5, 0, absolutni: true)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(mereni.OfType<HeadingMeasurement>().Count(), Is.EqualTo(1));
            Assert.That(mereni.Select(x => x.Source), Has.Some.EqualTo("IMU/gyro"));
        });
    }

    // ---------------- Úhlová rychlost z rozdílu yaw ----------------

    [Test]
    public void PrvniVzorek_JenUkotviOkno()
    {
        Assert.That(Mapper().ToMeasurements(Imu(0.5, 0, absolutni: false)), Is.Empty);
    }

    [Test]
    public void PredUplynutimOkna_NicNevznikne()
    {
        var cfg = new FusionConfig { RelYawWindowSec = 0.5 };
        var m = Mapper(cfg);
        m.ToMeasurements(Imu(0.0, 0, absolutni: false)).ToList();

        Assert.That(m.ToMeasurements(Imu(0.4, 0.4, absolutni: false)), Is.Empty);
    }

    [Test]
    public void PoUplynutiOkna_VznikneUhlovaRychlostZRozdilu()
    {
        var cfg = new FusionConfig { RelYawWindowSec = 0.5, RelYawStd = 0.002 };
        var m = Mapper(cfg);
        m.ToMeasurements(Imu(1.0, 0, absolutni: false)).ToList();

        var mereni = m.ToMeasurements(Imu(1.3, 0.6, absolutni: false)).ToList();

        Assert.That(mereni, Has.Count.EqualTo(1));
        var rate = mereni[0];
        Assert.Multiple(() =>
        {
            Assert.That(rate.Source, Is.EqualTo("VIO/yawrate"));
            // Tolerance 1e-5: Quaternion je float, takze yaw se cestou zaokrouhli (~1e-7 rad),
            // a deleni oknem tu chybu jeste vydeli. Neni to volnost navic, je to presnost typu.
            Assert.That(rate.Value[0], Is.EqualTo(0.3 / 0.6).Within(1e-5));
            // sigma = √2·sigma_yaw/dt; v mereni je kovariance, tedy na druhou.
            double ocekavana = Math.Sqrt(2) * 0.002 / 0.6;
            Assert.That(rate.NoiseCovariance[0, 0], Is.EqualTo(ocekavana * ocekavana).Within(1e-15));
            Assert.That(rate.TimeStamp, Is.EqualTo(T0.AddSeconds(0.6)), "razitko je konec okna");
        });
    }

    [Test]
    public void NeznamaKonstanta_SeVRozdiluOdecte()
    {
        // Jadro cele veci: tatáž otocka posunuta o libovolny offset da tutez uhlovou rychlost.
        double Rate(double offset)
        {
            var m = Mapper(new FusionConfig { RelYawWindowSec = 0.5 });
            m.ToMeasurements(Imu(offset + 0.1, 0, absolutni: false)).ToList();
            return m.ToMeasurements(Imu(offset + 0.4, 1.0, absolutni: false)).Single().Value[0];
        }

        Assert.That(Rate(2.5), Is.EqualTo(Rate(0.0)).Within(1e-5));
    }

    [Test]
    public void PrechodPresPi_SePocitaKratsiCestou()
    {
        // Bez normalizace by z otocky o -0,2 rad pres +-pi vysla rychlost skoro 2pi/dt.
        var m = Mapper(new FusionConfig { RelYawWindowSec = 0.5 });
        m.ToMeasurements(Imu(3.0, 0, absolutni: false)).ToList();

        double rate = m.ToMeasurements(Imu(-3.0, 1.0, absolutni: false)).Single().Value[0];

        Assert.That(Math.Abs(rate), Is.LessThan(0.5), "musí jít kratší cestou přes ±pi");
    }

    [Test]
    public void OknaSeNeprekryvaji()
    {
        // Prekryvajici se okna by dala korelovana merenia a filtr by si nadsadil informaci.
        var cfg = new FusionConfig { RelYawWindowSec = 0.5 };
        var m = Mapper(cfg);
        m.ToMeasurements(Imu(0.0, 0.0, absolutni: false)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(m.ToMeasurements(Imu(0.5, 0.6, absolutni: false)).Count(), Is.EqualTo(1));
            // Hned dalsi vzorek uz je v NOVEM okne, ktere jeste neuplynulo.
            Assert.That(m.ToMeasurements(Imu(0.6, 0.7, absolutni: false)), Is.Empty);
            Assert.That(m.ToMeasurements(Imu(1.0, 1.2, absolutni: false)).Count(), Is.EqualTo(1));
        });
    }

    [Test]
    public void ZtracenaKvalita_ZahodiOknoANepocitaPresSkok()
    {
        // Confidence 0 = VIO ztraceno; po znovunalezeni muze yaw skocit, takze rozdil pres tu
        // dobu je nesmysl - okno se musi zacit znovu.
        var m = Mapper(new FusionConfig { RelYawWindowSec = 0.5 });
        m.ToMeasurements(Imu(0.0, 0.0, absolutni: false)).ToList();
        m.ToMeasurements(Imu(0.0, 0.3, absolutni: false, confidence: 0)).ToList();

        Assert.That(m.ToMeasurements(Imu(2.0, 0.6, absolutni: false)), Is.Empty,
                    "po ztrátě sledování se okno ukotví znovu, ne dopočítá přes skok");
    }

    [Test]
    public void DvaRelativniZdroje_SeNemichaji()
    {
        var m = Mapper(new FusionConfig { RelYawWindowSec = 0.5 });
        var a = Imu(0.0, 0.0, absolutni: false); a.Name = "T265 A";
        var b = Imu(2.0, 0.1, absolutni: false); b.Name = "T265 B";
        m.ToMeasurements(a).ToList();
        m.ToMeasurements(b).ToList();

        var a2 = Imu(0.3, 0.6, absolutni: false); a2.Name = "T265 A";

        Assert.That(m.ToMeasurements(a2).Single().Value[0], Is.EqualTo(0.5).Within(1e-5),
                    "kotva se musí držet per zdroj, jinak by se rozdíly počítaly mezi kamerami");
    }
}
