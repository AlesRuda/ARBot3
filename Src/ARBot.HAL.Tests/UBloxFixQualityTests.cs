using ARBot.Common.Devices;
using ARBot.HAL.Devices.GPSs.uBlox;

namespace ARBot.HAL.Tests;

/// <summary>
/// Převod u-bloxího <c>fixType</c> na <see cref="GPSState.FixQuality"/>.
///
/// <para><b>Proč to má test:</b> do 6. 9. 2026 se tu jen <b>přetypovávalo</b>, ačkoli ty dva výčty
/// spolu nesouvisejí — <c>fixType</c> (UBX-NAV-PVT) říká <i>způsob řešení</i>, <c>FixQuality</c>
/// pochází z NMEA GGA a říká <i>druh korekce</i>. Dopadalo to tak, že <b>samotný mrtvý odhad</b>
/// (bez družic) prošel do fúze jako platný fix, zatímco <b>GNSS + mrtvý odhad</b> — tedy dobré
/// řešení — se zahazovalo.</para>
/// </summary>
public class UBloxFixQualityTests
{
    [TestCase((byte)0, GPSState.FixQuality.Invalid, false, TestName = "BezFixu_NeniPlatny")]
    [TestCase((byte)1, GPSState.FixQuality.Estimated, false, TestName = "JenMrtvyOdhad_NeniPlatnaPoloha")]
    [TestCase((byte)2, GPSState.FixQuality.GpsFix, true, TestName = "Fix2D_JePlatny")]
    [TestCase((byte)3, GPSState.FixQuality.DgpsFix, true, TestName = "Fix3D_JePlatny")]
    [TestCase((byte)4, GPSState.FixQuality.DgpsFix, true, TestName = "Fix3DsMrtvymOdhadem_JePlatny")]
    [TestCase((byte)5, GPSState.FixQuality.Invalid, false, TestName = "JenCas_NeniPlatnaPoloha")]
    public void FixTypeSePrevadiNaKvalitu(byte fixType, GPSState.FixQuality ocekavana, bool platny)
    {
        var kvalita = uBloxGps.FixQualityFrom(fixType);

        Assert.Multiple(() =>
        {
            Assert.That(kvalita, Is.EqualTo(ocekavana));
            // IsFixed je to, podle čeho se fúze rozhoduje - proto se hlídá i ono, ne jen výčet.
            Assert.That(new GPSState { Quality = kvalita }.IsFixed, Is.EqualTo(platny));
        });
    }

    [Test]
    public void NeznamyFixType_NeniPlatny()
    {
        // Novější firmware může přidat hodnotu; nesmí propadnout jako platná poloha.
        Assert.That(uBloxGps.FixQualityFrom(9), Is.EqualTo(GPSState.FixQuality.Invalid));
    }
}
