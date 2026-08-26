using System;
using System.IO;
using System.Text;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;

namespace ARBot.Common.Tests.Devices;

/// <summary>
/// Testy <b>jednotek</b> <see cref="GPSState.Latitude"/> / <see cref="GPSState.Longitude"/>.
///
/// <para><b>Od 26. 8. 2026 jsou to RADIANY</b> — tedy tatáž jednotka, jakou drzi
/// <see cref="LLA"/>, <c>GeoReference</c> i cely zbytek systemu. Do te doby to byly STUPNE, coz
/// bylo jedine misto s jinou konvenci — a prave proto, ze <c>new LLA(gps.Latitude, ...)</c> je ta
/// nejprirozenejsi vec, kterou clovek napise, byla to <b>tichá a fatalni</b> past: mapper na ni
/// musel mit varovny komentar a mise Robotour do ni stejne spadla (uvizla v <c>ArmingAtDepot</c>,
/// protoze rozptyl fixu vysel astronomicky).</para>
///
/// <para>Rozhodnuti autora: zmenit jednotku tak, aby nejprirozenejsi zapis byl <b>spravny</b>.</para>
/// </summary>
public class GpsStateUnitsTests
{
    private const double PragueLatDeg = 50.08758;
    private const double PragueLonDeg = 14.42076;

    private static byte[] Serialize(GPSState s)
    {
        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
            s.ToData(bw);
        return buffer.ToArray();
    }

    private static GPSState Deserialize(byte[] data, int verze)
    {
        var loaded = new GPSState { Verze = verze };
        using (var br = new BinaryReader(new MemoryStream(data), Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);
        return loaded;
    }

    /// <summary>
    /// <c>new LLA(gps.Latitude, gps.Longitude)</c> musi dat spravne misto — to je celý smysl te
    /// zmeny. Kdyby byl <c>GPSState</c> ve stupnich, vysel by bod desitky radianu odsud.
    /// </summary>
    [Test]
    public void FixJdeRovnouDoLLA_BezPrevodu()
    {
        var gps = new GPSState
        {
            Latitude = Conversions.Deg2Rad(PragueLatDeg),
            Longitude = Conversions.Deg2Rad(PragueLonDeg),
        };

        var lla = new LLA(gps.Latitude, gps.Longitude);

        Assert.Multiple(() =>
        {
            Assert.That(Conversions.Rad2Deg(lla.Latitude), Is.EqualTo(PragueLatDeg).Within(1e-9));
            Assert.That(Conversions.Rad2Deg(lla.Longitude), Is.EqualTo(PragueLonDeg).Within(1e-9));
        });
    }

    [Test]
    public void Serializace_JeObousmerna()
    {
        var original = new GPSState
        {
            Latitude = Conversions.Deg2Rad(PragueLatDeg),
            Longitude = Conversions.Deg2Rad(PragueLonDeg),
            Quality = GPSState.FixQuality.GpsFix,
            NumberOfSatellites = 11,
            Hdop = 0.8,
        };

        var loaded = Deserialize(Serialize(original), GPSState.FormatVersion);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Latitude, Is.EqualTo(original.Latitude).Within(1e-12));
            Assert.That(loaded.Longitude, Is.EqualTo(original.Longitude).Within(1e-12));
            Assert.That(loaded.NumberOfSatellites, Is.EqualTo(11));
        });
    }

    /// <summary>
    /// <b>Stary zaznam (verze 1) drzi STUPNE a musi se pri cteni prevest.</b>
    ///
    /// <para>Bez toho by se z kazdeho archivniho zaznamu stala nesmyslna data — a co je horsi,
    /// nesmyslna TICHE: 50 „radianu" je platne cislo, takze by se to projevilo az divnym chovanim
    /// fuze o desitky tisic kilometru dal.</para>
    /// </summary>
    [Test]
    public void StaryZaznamVeStupnich_SePrevedeNaRadiany()
    {
        // Zaznam verze 1: na dratu jsou STUPNE.
        var asWritten = new GPSState { Latitude = PragueLatDeg, Longitude = PragueLonDeg };
        var bytes = Serialize(asWritten);

        var loaded = Deserialize(bytes, verze: 1);

        Assert.Multiple(() =>
        {
            Assert.That(Conversions.Rad2Deg(loaded.Latitude), Is.EqualTo(PragueLatDeg).Within(1e-9),
                        "stupne ze stareho zaznamu se prevedly na radiany");
            Assert.That(Conversions.Rad2Deg(loaded.Longitude), Is.EqualTo(PragueLonDeg).Within(1e-9));
        });
    }

    [Test]
    public void VerzeFormatu_JeAspon2()
    {
        // Bez zvyseni verze by stary zaznam nesel od noveho odlisit.
        Assert.That(GPSState.FormatVersion, Is.GreaterThanOrEqualTo(2));
    }
}
