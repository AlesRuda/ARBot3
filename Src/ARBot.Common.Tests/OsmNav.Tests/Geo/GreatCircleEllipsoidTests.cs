using System;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Tests.OsmNav.Geo;

/// <summary>
/// <see cref="GreatCircle"/> po prechodu z pevne koule na zvoleny <see cref="Ellipsoid"/>.
/// Tezistem je <b>sjednoceni s <see cref="GeoReference"/></b> - obojí musi merit tentyz svet,
/// jinak se delky hran v grafu rozchazeji s metry, ve kterych robot jede.
/// </summary>
public class GreatCircleEllipsoidTests
{
    /// <summary>Pracovni sirka projektu (Praha) - tam nas rozdil modelu zajima.</summary>
    private const double Lat = 50.029;
    private const double Lon = 14.520;

    [Test]
    public void VychoziModelJeWgs84()
    {
        Assert.That(new GreatCircle().Ellipsoid, Is.SameAs(Ellipsoid.Wgs84));
    }

    [Test]
    public void ShodneBody_JsouNula()
    {
        var p = LLA.FromDegrees(Lat, Lon);
        Assert.That(GreatCircle.Wgs84.Distance(p, p), Is.EqualTo(0.0).Within(1e-9));
    }

    [Test]
    public void Vzdalenost_JeSymetricka()
    {
        var p = LLA.FromDegrees(Lat, Lon);
        var q = LLA.FromDegrees(Lat + 0.01, Lon + 0.02);
        Assert.That(GreatCircle.Wgs84.Distance(p, q),
                    Is.EqualTo(GreatCircle.Wgs84.Distance(q, p)).Within(1e-9));
    }

    /// <summary>
    /// Jadro zmeny: 10 m na VYCHOD podle <see cref="GeoReference"/> musi <see cref="GreatCircle"/>
    /// zmerit taky jako 10 m. Se starou koulí R = 6 371 000 m tu vychazelo 9,969 m (-0,31 %),
    /// protoze ve smeru vychod-zapad rozhoduje polomer krivosti v prvnim vertikalu, ne stredni
    /// polomer koule. Vychod-zapad je nejcitlivejsi smer, proto se meri prave ten.
    /// </summary>
    [Test]
    public void SedisSGeoReference_VeSmeruVychodZapad()
    {
        var geo = GeoReference.FromDegrees(Lat, Lon);
        var from = geo.ToLLA(0, 0);
        var to = geo.ToLLA(10.0, 0);   // 10 m na vychod

        double gc = GreatCircle.Wgs84.Distance(from, to);
        Assert.That(gc, Is.EqualTo(10.0).Within(0.001), "geodetika na WGS84 musi sedet na ENU");
    }

    [Test]
    public void SedisSGeoReference_VeSmeruSeverJih()
    {
        var geo = GeoReference.FromDegrees(Lat, Lon);
        var from = geo.ToLLA(0, 0);
        var to = geo.ToLLA(0, 10.0);   // 10 m na sever

        Assert.That(GreatCircle.Wgs84.Distance(from, to), Is.EqualTo(10.0).Within(0.001));
    }

    [Test]
    public void SedisSGeoReference_IPresDelsiUsek()
    {
        var geo = GeoReference.FromDegrees(Lat, Lon);
        var from = geo.ToLLA(0, 0);
        var to = geo.ToLLA(300.0, 400.0);   // 500 m sikmo

        Assert.That(GreatCircle.Wgs84.Distance(from, to), Is.EqualTo(500.0).Within(0.01));
    }

    /// <summary>
    /// Stara koule se od WGS84 lisila znatelne - test to drzi zaznamenane, aby bylo videt,
    /// ze rozdil neni sum, ale systematicka chyba (v desetinach procenta).
    /// </summary>
    [Test]
    public void StaraKoule_PodstrelovalaVychodZapad()
    {
        var geo = GeoReference.FromDegrees(Lat, Lon);
        var from = geo.ToLLA(0, 0);
        var to = geo.ToLLA(10.0, 0);

        var stara = new GreatCircle(new Ellipsoid(6_371_000, 6_371_000));
        double d = stara.Distance(from, to);

        Assert.That(d, Is.LessThan(9.99), "stara koule mela merit vyrazne min nez 10 m");
        Assert.That(d, Is.GreaterThan(9.95));
    }

    /// <summary>Pro a == b se Vincenty degeneruje na obycejny great-circle vzorec (b·σ).</summary>
    [Test]
    public void Koule_SeShodujeSHaversinem()
    {
        const double r = 6_371_000;
        var p = LLA.FromDegrees(Lat, Lon);
        var q = LLA.FromDegrees(Lat + 0.5, Lon + 0.7);

        double vincenty = new GreatCircle(new Ellipsoid(r, r)).Distance(p, q);

        double df = q.Latitude - p.Latitude, dl = q.Longitude - p.Longitude;
        double h = Math.Sin(df / 2) * Math.Sin(df / 2)
                   + Math.Cos(p.Latitude) * Math.Cos(q.Latitude) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
        double haversine = r * 2 * Math.Atan2(Math.Sqrt(h), Math.Sqrt(1 - h));

        Assert.That(vincenty, Is.EqualTo(haversine).Within(1e-6));
    }

    /// <summary>Kontrola proti znamemu udaji: Praha - Brno je po geodetice ~185 km.</summary>
    [Test]
    public void ZnamaVzdalenost_PrahaBrno()
    {
        var praha = LLA.FromDegrees(50.0833, 14.4353);
        var brno = LLA.FromDegrees(49.1907, 16.6127);
        Assert.That(GreatCircle.Wgs84.Distance(praha, brno), Is.InRange(183_000, 187_000));
    }

    [Test]
    public void Azimut_NaSeverJeNula_NaVychodDevadesat()
    {
        var geo = GeoReference.FromDegrees(Lat, Lon);
        var from = geo.ToLLA(0, 0);

        double north = GreatCircle.Wgs84.Bearing(from, geo.ToLLA(0, 100));
        double east = GreatCircle.Wgs84.Bearing(from, geo.ToLLA(100, 0));

        Assert.That(Conversions.Rad2Deg(north), Is.EqualTo(0.0).Within(0.01));
        Assert.That(Conversions.Rad2Deg(east), Is.EqualTo(90.0).Within(0.01));
    }
}
