using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests;

/// <summary>
/// <see cref="VycetZarizeni"/> — hledání zařízení, které přežije chybu jednoho zařízení ve výčtu
/// (13. 10. 2026, <c>hw-vetev-usb-2-1-3</c>). Do té doby dotaz skončil první výjimkou a kamera ve výčtu
/// ZA streamující kamerou (ta hází „failed to set power state") se po restartu nenašla nikdy.
/// </summary>
public class VycetZarizeniTests
{
    private sealed class Zarizeni
    {
        public string Serial = "";
        public bool HodiPriCteni;
        public bool Uvolneno;
    }

    private static Zarizeni Z(string sn, bool hodi = false) => new Zarizeni { Serial = sn, HodiPriCteni = hodi };

    private static bool Shoda(Zarizeni d, string sn)
    {
        if (d.HodiPriCteni) throw new InvalidOperationException("failed to set power state");
        return d.Serial == sn;
    }

    /// <summary>Přesně případ tvrdého záseku: levá streamuje (hází) a je ve výčtu PŘED pravou, kterou hledáme.</summary>
    [Test]
    public void StreamujiciPredHledanou_HledanaSeNajde()
    {
        var zar = new[] { Z("LEVA", hodi: true), Z("PRAVA") };

        var v = VycetZarizeni.Najdi(zar.Length, i => zar[i], d => Shoda(d, "PRAVA"), d => d.Uvolneno = true);

        Assert.That(v.Nalezeno, Is.True, "pravá se má najít, i když levá hodila");
        Assert.That(v.IndexNalezeneho, Is.EqualTo(1));
        Assert.That(v.Chyby, Has.Count.EqualTo(1));
        Assert.That(v.Chyby[0].Index, Is.EqualTo(0));
        Assert.That(v.Chyby[0].Krok, Is.EqualTo(VycetZarizeni.KrokShoda));
        Assert.That(v.PopisChyb(), Does.Contain("zarizeni 1/2 (cteni udaju): failed to set power state"));
        Assert.That(zar.All(z => z.Uvolneno), Is.True, "každé vytvořené zařízení se uvolní");
    }

    [Test]
    public void ChybaPriVytvoreni_PokracujeSeDalsim()
    {
        var zar = new[] { Z("LEVA"), Z("PRAVA") };

        var v = VycetZarizeni.Najdi<Zarizeni>(2,
            i => i == 0 ? throw new InvalidOperationException("failed to set power state") : zar[i],
            d => Shoda(d, "PRAVA"), d => d.Uvolneno = true);

        Assert.That(v.Nalezeno, Is.True);
        Assert.That(v.Chyby.Single().Krok, Is.EqualTo(VycetZarizeni.KrokVytvoreni));
        Assert.That(zar[0].Uvolneno, Is.False, "nevytvořené se neuvolňuje");
    }

    [Test]
    public void NenalezenoSChybou_JeNeznamo()
    {
        var zar = new[] { Z("LEVA", hodi: true), Z("JINA") };
        var v = VycetZarizeni.Najdi(zar.Length, i => zar[i], d => Shoda(d, "PRAVA"), _ => { });
        Assert.That(v.Nalezeno, Is.Null, "hledané mohlo být právě to, které hodilo");
        Assert.That(v.IndexNalezeneho, Is.EqualTo(-1));
    }

    [Test]
    public void NenalezenoBezChyby_OpravduChybi()
    {
        var zar = new[] { Z("LEVA"), Z("JINA") };
        var v = VycetZarizeni.Najdi(zar.Length, i => zar[i], d => Shoda(d, "PRAVA"), _ => { });
        Assert.That(v.Nalezeno, Is.False);
        Assert.That(v.Chyby, Is.Empty);
    }

    /// <summary>Nalezené jako první: dál se nesahá (pozdější zařízení by hodilo) a chyba se nezapíše.</summary>
    [Test]
    public void NalezenoDriv_DalSeNesaha()
    {
        int vytvoreno = 0;
        var zar = new[] { Z("PRAVA"), Z("LEVA", hodi: true) };
        var v = VycetZarizeni.Najdi(zar.Length, i => { vytvoreno++; return zar[i]; }, d => Shoda(d, "PRAVA"), _ => { });
        Assert.That(v.Nalezeno, Is.True);
        Assert.That(v.Chyby, Is.Empty);
        Assert.That(vytvoreno, Is.EqualTo(1));
    }

    [Test]
    public void PrazdnyVycet_Chybi()
    {
        var v = VycetZarizeni.Najdi<Zarizeni>(0, _ => throw new InvalidOperationException(), _ => true, _ => { });
        Assert.That(v.Nalezeno, Is.False);
    }
}
