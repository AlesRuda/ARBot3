using System;
using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests;

/// <summary>
/// <see cref="StreamFreezeWatch"/> — hlídka zamrzlého streamu kamery.
///
/// <para><b>Proč to vzniklo:</b> 6. 9. 2026 se na zařízení ukázalo, že pravá D435 posílá framesety
/// dál 10 Hz a hloubka se mění, ale <b>barva je pořád tatáž</b>. Čítač timeoutů v driveru to
/// nechytí (žádný timeout není), takže kamera hlásila <c>OK</c> a robot jel podle nehybné fotky.</para>
///
/// <para>Testy jedou na <b>podvrženém čase</b>, aby nemusely čekat pět sekund — a hlavně aby prahy
/// šly ověřit přesně, ne „přibližně po chvíli".</para>
/// </summary>
public class StreamFreezeWatchTests
{
    private DateTime ted = new DateTime(2026, 9, 6, 8, 0, 0, DateTimeKind.Utc);

    private StreamFreezeWatch Hlidka(double limit = 5.0)
        => new StreamFreezeWatch(limit, () => ted);

    private void Pockej(double sekund) => ted = ted.AddSeconds(sekund);

    [Test]
    public void BezicíStreamy_Nehlasi()
    {
        var h = Hlidka();
        double barva = 100, hloubka = 200;

        for (int i = 0; i < 100; i++)
        {
            Pockej(0.1);
            Assert.That(h.Check(barva += 33, hloubka += 33), Is.Null);
        }
        Assert.That(h.Detections, Is.Zero);
    }

    [Test]
    public void ZamrzlaBarva_SeOhlasi_AzPoPrahu()
    {
        var h = Hlidka(limit: 5.0);
        double hloubka = 200;

        // Barva stoji, hloubka bezi. Do prahu se mlci - kratke opakovani je legitimni (pomala
        // expozice v seru srazi snimkovou frekvenci pod periodu ctení).
        h.Check(100, hloubka);
        Pockej(4.9);
        Assert.That(h.Check(100, hloubka += 33), Is.Null, "pod prahem se nehlasi");

        Pockej(0.2);
        string duvod = h.Check(100, hloubka + 33);

        Assert.Multiple(() =>
        {
            Assert.That(duvod, Does.Contain("BARVA"));
            Assert.That(h.Detections, Is.EqualTo(1));
        });
    }

    [Test]
    public void ZamrzlaHloubka_SeOhlasi()
    {
        var h = Hlidka();
        double barva = 100;

        h.Check(barva, 200);
        Pockej(6);

        Assert.That(h.Check(barva + 33, 200), Does.Contain("HLOUBKA"));
    }

    [Test]
    public void ZmenaRazitka_CasovacVynuluje()
    {
        var h = Hlidka(limit: 5.0);

        h.Check(100, 200);
        Pockej(4.5);
        h.Check(100, 200);      // porad stejne, ale pod prahem
        Pockej(1.0);
        Assert.That(h.Check(133, 233), Is.Null, "razitka se zmenila -> zacina se znovu");

        Pockej(4.9);
        Assert.That(h.Check(133, 233), Is.Null, "od zmeny jeste neuplynul prah");
    }

    [Test]
    public void PoResetu_SeZacinaZnovu()
    {
        var h = Hlidka();
        h.Check(100, 200);
        Pockej(10);

        h.Reset();
        // Prvni snimek po prestaveni pipeline jen ukotvi stav; kdyby se prah pocital dal, sepnul
        // by hned a restarty by se zacyklily.
        Assert.That(h.Check(100, 200), Is.Null);

        Pockej(4.9);
        Assert.That(h.Check(100, 200), Is.Null);
    }

    [Test]
    public void ChybejiciStream_SeNehlida()
    {
        // Kdyz kamera nema barevny stream zapnuty, driver posle null - nesmi se to hlasit jako zásek.
        var h = Hlidka();

        h.Check(null, 200);
        Pockej(60);

        Assert.That(h.Check(null, 233), Is.Null);
    }

    [Test]
    public void ObaZamrzle_HlasiSePrednostneBarva()
    {
        // Zamrzla barva je zakernejsi: vede z ni „cesta z RGB" do occupancy gridu, takze robot
        // jede podle nehybne fotky. Hlaska ale nese OBE doby, aby slo poznat, ze stoji obojí.
        var h = Hlidka();

        h.Check(100, 200);
        Pockej(6);
        string duvod = h.Check(100, 200);

        Assert.Multiple(() =>
        {
            Assert.That(duvod, Does.Contain("BARVA"));
            Assert.That(duvod, Does.Contain("hloubka"));
        });
    }
}
