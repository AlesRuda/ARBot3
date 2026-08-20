using ARBot.Common.Fusion;
using ARBot.Common.Simulation;

namespace ARBot.Common.Tests.Simulation;

/// <summary>
/// Testy umele chyby pozy pro virtualni kameru (viz doc/virtual-hw.md a
/// doc/map-correlation-localization.md).
///
/// <para>Smysl te chyby: kamera renderuje z pozy POSUNUTE proti te, kterou je ukotveny occupancy
/// grid, takze obsah gridu nesedi s mapou o znamou hodnotu. Korelator pak MUSI ohlasit prave ji -
/// bez toho je jeho Dx/Dy ve virtualnim HW strukturalne nulove (obraz i ukotveni gridu vychazeji
/// z teze pozy) a nic nedokazuje.</para>
///
/// <para>Konvence: svet ENU + matematicky uhel (0 = vychod, +CCW), telo FLU (X vpred, Y vlevo).
/// Korelator hlasi "skutecna poloha = odhad + D", takze plati <b>D = vnucena chyba</b>.</para>
/// </summary>
public class VirtualPoseErrorTests
{
    private static readonly DateTime T0 = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static RobotState Pose(double x, double y, double thetaDeg) => new RobotState
    {
        X = x,
        Y = y,
        Theta = thetaDeg * Math.PI / 180.0,
        V = 1.5,
        Omega = 0.25,
        TimeStamp = T0,
    };

    /// <summary>Robot miri na vychod: "dopredu" musi jit na vychod (+X), pricna slozka nula.</summary>
    [Test]
    public void Dopredu_PriKurzuNaVychod_PosouvaNaVychod()
    {
        var e = new VirtualPoseError { ForwardM = 0.5 };

        var moved = e.Apply(Pose(10, 20, 0));

        Assert.Multiple(() =>
        {
            Assert.That(moved.X, Is.EqualTo(10.5).Within(1e-9));
            Assert.That(moved.Y, Is.EqualTo(20.0).Within(1e-9));
        });
    }

    /// <summary>Robot miri na vychod: "doleva" (FLU +Y) je na sever (+Y ve svete).</summary>
    [Test]
    public void Doleva_PriKurzuNaVychod_PosouvaNaSever()
    {
        var e = new VirtualPoseError { LeftM = 0.5 };

        var moved = e.Apply(Pose(10, 20, 0));

        Assert.Multiple(() =>
        {
            Assert.That(moved.X, Is.EqualTo(10.0).Within(1e-9));
            Assert.That(moved.Y, Is.EqualTo(20.5).Within(1e-9));
        });
    }

    /// <summary>
    /// Pri kurzu 90 deg (na sever) se ramec otoci: "dopredu" jde na sever, "doleva" na zapad.
    /// Tohle je test, ktery chytne zamenu sinu a kosinu i obracene znamenko pricne slozky.
    /// </summary>
    [Test]
    public void PriKurzu90_SeRamecOtoci()
    {
        var e = new VirtualPoseError { ForwardM = 2.0, LeftM = 3.0 };

        var moved = e.Apply(Pose(0, 0, 90));

        Assert.Multiple(() =>
        {
            Assert.That(moved.X, Is.EqualTo(-3.0).Within(1e-9), "doleva pri kurzu na sever = na zapad");
            Assert.That(moved.Y, Is.EqualTo(2.0).Within(1e-9), "dopredu pri kurzu na sever = na sever");
        });
    }

    /// <summary>Chyba kurzu se pricte k orientaci.</summary>
    [Test]
    public void ChybaKurzu_SePricteKOrientaci()
    {
        var e = new VirtualPoseError { HeadingRad = 5.0 * Math.PI / 180.0 };

        var moved = e.Apply(Pose(0, 0, 10));

        Assert.That(moved.Theta * 180.0 / Math.PI, Is.EqualTo(15.0).Within(1e-9));
    }

    /// <summary>
    /// KLICOVE: Apply nesmi zmutovat vstupni stav. Ten stav prichazi z fuze
    /// (<c>engine.GetStateAt</c>) a mutace by injektaz protlacila do filtru - experiment by
    /// se sezral sam a "znama odpoved" by zmizela.
    /// </summary>
    [Test]
    public void Apply_NemutujeVstupniStav()
    {
        var original = Pose(10, 20, 30);
        var e = new VirtualPoseError { ForwardM = 1.0, LeftM = 1.0, HeadingRad = 0.1 };

        e.Apply(original);

        Assert.Multiple(() =>
        {
            Assert.That(original.X, Is.EqualTo(10.0).Within(1e-12));
            Assert.That(original.Y, Is.EqualTo(20.0).Within(1e-12));
            Assert.That(original.Theta, Is.EqualTo(30.0 * Math.PI / 180.0).Within(1e-12));
        });
    }

    /// <summary>Ostatni slozky stavu musi projit beze zmeny - posouva se jen poza.</summary>
    [Test]
    public void Apply_ZachovaOstatniSlozkyStavu()
    {
        var e = new VirtualPoseError { ForwardM = 1.0 };

        var moved = e.Apply(Pose(0, 0, 0));

        Assert.Multiple(() =>
        {
            Assert.That(moved.V, Is.EqualTo(1.5).Within(1e-12));
            Assert.That(moved.Omega, Is.EqualTo(0.25).Within(1e-12));
            Assert.That(moved.TimeStamp, Is.EqualTo(T0));
        });
    }

    /// <summary>
    /// Nulova chyba je bezny provozni stav (nastroj zavreny, nikdo nic nenastavil) - musi vratit
    /// TENTYZ objekt, aby se v renderovaci ceste zbytecne nealokovalo pri 30 Hz na dve kamery.
    /// </summary>
    [Test]
    public void NuloveChyba_VraciTentyzObjekt()
    {
        var e = new VirtualPoseError();
        var original = Pose(1, 2, 3);

        Assert.That(e.Apply(original), Is.SameAs(original));
    }

    /// <summary>Null na vstupu (fuze jeste nema stav) nesmi shodit renderovaci cestu.</summary>
    [Test]
    public void Apply_SNullem_VraciNull()
    {
        var e = new VirtualPoseError { ForwardM = 1.0 };

        Assert.That(e.Apply(null), Is.Null);
    }

    /// <summary>
    /// Ocekavany vysledek korelace: prevod vnucene chyby do svetovych slozek, ktere ma korelator
    /// ohlasit. Nastroj je zobrazuje vedle namerenych, aby slo srovnat predpoved s merenim.
    /// </summary>
    [Test]
    public void OcekavanyPosun_OdpovidaSvetovymSlozkam()
    {
        var e = new VirtualPoseError { ForwardM = 2.0, LeftM = 3.0 };

        var (dx, dy) = e.ExpectedWorldOffset(90.0 * Math.PI / 180.0);

        Assert.Multiple(() =>
        {
            Assert.That(dx, Is.EqualTo(-3.0).Within(1e-9));
            Assert.That(dy, Is.EqualTo(2.0).Within(1e-9));
        });
    }

    /// <summary>
    /// Parametr prikazove radky <c>poseerror=vpred,vlevo,stupne</c> - kvuli reprodukovatelnemu
    /// bezobsluznemu mereni (klik v UI se do skriptu zapsat neda).
    /// </summary>
    [Test]
    public void Parse_TriSlozky()
    {
        Assert.That(VirtualPoseError.TryParse("0.5,-1.25,3", out var e), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(e.ForwardM, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(e.LeftM, Is.EqualTo(-1.25).Within(1e-12));
            Assert.That(e.HeadingRad * 180.0 / Math.PI, Is.EqualTo(3.0).Within(1e-9));
        });
    }

    /// <summary>Kurz je nepovinny - casty pripad je cisty posun bez natoceni.</summary>
    [Test]
    public void Parse_BezKurzu_NechaKurzNulovy()
    {
        Assert.That(VirtualPoseError.TryParse("0,0.5", out var e), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(e.LeftM, Is.EqualTo(0.5).Within(1e-12));
            Assert.That(e.HeadingRad, Is.EqualTo(0.0).Within(1e-12));
        });
    }

    /// <summary>
    /// Desetinna tecka musi platit bez ohledu na narodni prostredi - jinak by tentyz prikazovy
    /// radek delal na ceskem a anglickem stroji neco jineho.
    /// </summary>
    [Test]
    public void Parse_JeNezavislyNaNarodnimProstredi()
    {
        var puvodni = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture =
                new System.Globalization.CultureInfo("cs-CZ");

            Assert.That(VirtualPoseError.TryParse("0.5,0", out var e), Is.True);
            Assert.That(e.ForwardM, Is.EqualTo(0.5).Within(1e-12));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = puvodni; }
    }

    /// <summary>Nesmysl na vstupu nesmi shodit start aplikace - jen se nepouzije.</summary>
    [Test]
    public void Parse_Nesmysl_VratiFalse()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VirtualPoseError.TryParse(null, out _), Is.False);
            Assert.That(VirtualPoseError.TryParse("", out _), Is.False);
            Assert.That(VirtualPoseError.TryParse("0.5", out _), Is.False, "jedna slozka nestaci");
            Assert.That(VirtualPoseError.TryParse("a,b", out _), Is.False);
        });
    }

    /// <summary>Je vubec nejaka chyba nastavena? Nastroj podle toho hlasi "aktivni".</summary>
    [Test]
    public void JeAktivni_JenKdyzNeniVseNula()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new VirtualPoseError().IsActive, Is.False);
            Assert.That(new VirtualPoseError { ForwardM = 0.1 }.IsActive, Is.True);
            Assert.That(new VirtualPoseError { LeftM = -0.1 }.IsActive, Is.True);
            Assert.That(new VirtualPoseError { HeadingRad = 0.01 }.IsActive, Is.True);
        });
    }
}
