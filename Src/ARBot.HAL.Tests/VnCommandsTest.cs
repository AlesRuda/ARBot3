using System;
using System.Globalization;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.HAL.Devices.AHRS;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Sestavení příkazů pro VN-100 (<see cref="VnCommands"/>) — <b>bez UARTu a bez hardwaru</b>,
    /// porovnáním výsledného řetězce znak po znaku.
    ///
    /// <para><b>Nač to je.</b> <c>SetModelParams</c> (registr 83, model pole → deklinace)
    /// existoval rok v ASCII driveru a <b>nikdy nemohl fungovat</b>: posílal čtecí <c>VNRRG</c>
    /// místo <c>VNWRG</c>, formátoval čísla přes <c>{0:N3}</c> (oddělovač tisíců = čárka
    /// doprostřed čárkami odděleného příkazu) a posílal souřadnice v radiánech, ačkoli VN je
    /// čeká ve stupních. Nikdo si toho nevšiml, protože ho nikdo nevolal — a kdyby zavolal,
    /// selhalo by to tiše. Každá z těch tří pastí má tady svůj test.</para>
    /// </summary>
    [TestFixture]
    public class VnCommandsTest
    {
        /// <summary>Praha zhruba; LLA je v RADIÁNECH (pravidlo projektu).</summary>
        private static LLA Praha() => new LLA(Conversions.Deg2Rad(50.087451),
                                              Conversions.Deg2Rad(14.420671),
                                              235.0);

        private static readonly DateTime Datum = new DateTime(2026, 9, 6);

        [Test]
        public void ReferenceVectorConfig_JeZAPIS_NeCteni()
        {
            // Past c. 1: puvodni kod posilal VNRRG, coz je CTECI prikaz - nic nenastavil.
            string body = VnCommands.ReferenceVectorConfig(Praha(), Datum);
            Assert.That(body, Does.StartWith("VNWRG,83,"));
            Assert.That(body, Does.Not.Contain("VNRRG"));
        }

        [Test]
        public void ReferenceVectorConfig_PrevadiRadianyNaStupne()
        {
            // Past c. 2: LLA je v radianech, VN ceka stupne. Puvodni kod posilal radiany,
            // takze by senzor dostal polohu nekde u rovniku (50 stupnu -> 0,874).
            string body = VnCommands.ReferenceVectorConfig(Praha(), Datum);

            var pole = body.Split(',');
            // VNWRG,83,mag,grav,resv1,resv2,thr,rok,lat,lon,alt
            double lat = double.Parse(pole[8], CultureInfo.InvariantCulture);
            double lon = double.Parse(pole[9], CultureInfo.InvariantCulture);

            Assert.That(lat, Is.EqualTo(50.087451).Within(1e-6), "sirka ma byt ve stupnich");
            Assert.That(lon, Is.EqualTo(14.420671).Within(1e-6), "delka ma byt ve stupnich");
        }

        [Test]
        public void ReferenceVectorConfig_NevkladaOddelovacTisicu()
        {
            // Past c. 3: {0:N3} by z vysky 1234,5 m udelalo "1,234.500" - carka doprostred
            // carkami oddeleneho prikazu, tedy rozbity ramec. Na male vysce to videt neni,
            // proto se testuje prave nad tisicem.
            var vysoko = new LLA(Conversions.Deg2Rad(50.0), Conversions.Deg2Rad(14.0), 1234.5);
            string body = VnCommands.ReferenceVectorConfig(vysoko, Datum);

            Assert.That(body, Does.Contain("1234.500"));
            Assert.That(body.Split(',').Length, Is.EqualTo(11),
                        "prikaz ma mit presne 11 poli - vic znamena carku navic v cisle");
        }

        [Test]
        public void ReferenceVectorConfig_MaOcekavanePoradiPoli()
        {
            // Poradi overeno proti odpovedi skutecneho senzoru:
            //   $VNRRG,83,0,0,0,0,1000,0.000,+00.00000000,+000.00000000,+00000.000
            string body = VnCommands.ReferenceVectorConfig(Praha(), Datum,
                                                           useMagModel: true,
                                                           useGravityModel: false,
                                                           recalcThresholdM: 500);
            var pole = body.Split(',');
            Assert.That(pole[0], Is.EqualTo("VNWRG"));
            Assert.That(pole[1], Is.EqualTo("83"));
            Assert.That(pole[2], Is.EqualTo("1"), "UseMagModel");
            Assert.That(pole[3], Is.EqualTo("0"), "UseGravityModel");
            Assert.That(pole[4], Is.EqualTo("0"), "Resv1");
            Assert.That(pole[5], Is.EqualTo("0"), "Resv2");
            Assert.That(pole[6], Is.EqualTo("500"), "RecalcThreshold");
        }

        [Test]
        public void ReferenceVectorConfig_DesetinnyRokSediNaDatum()
        {
            // Model pole se s casem meni, takze na roku zalezi. 6. 9. 2026 je den 249.
            string body = VnCommands.ReferenceVectorConfig(Praha(), Datum);
            double rok = double.Parse(body.Split(',')[7], CultureInfo.InvariantCulture);
            Assert.That(rok, Is.EqualTo(2026 + 248 / 365.25).Within(1e-3));
        }

        [Test]
        public void ReferenceVectorConfig_NezavisiNaKultureVlakna()
        {
            // Ceska kultura ma desetinnou CARKU - kdyby se formatovalo bez InvariantCulture,
            // rozpadl by se prikaz na jinem stroji jinak. Presne ta trida chyby, kterou
            // {0:N3} predvedlo.
            var puvodni = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("cs-CZ");
                string body = VnCommands.ReferenceVectorConfig(Praha(), Datum);
                Assert.That(body.Split(',').Length, Is.EqualTo(11));
                Assert.That(body, Does.Contain("50.08745"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = puvodni;
            }
        }

        [Test]
        public void Frame_PridaDolarHvezdickuAKontrolniSoucet()
        {
            // Znama dvojice z dokumentace VN: $VNRRG,01*72 (XOR znaku mezi $ a *).
            Assert.That(VnCommands.Frame("VNRRG,01"), Is.EqualTo("$VNRRG,01*72"));
            Assert.That(VnCommands.Frame("VNWNV"), Is.EqualTo("$VNWNV*57"));
        }

        [Test]
        public void Checksum_JeXorMeziDolaremAHvezdickou()
        {
            // Stejny vysledek s dolarem i bez nej, a znaky za '*' se uz nepocitaji.
            Assert.That(VnCommands.Checksum("$VNWNV"), Is.EqualTo(VnCommands.Checksum("VNWNV")));
            Assert.That(VnCommands.Checksum("$VNWNV*FF"), Is.EqualTo(VnCommands.Checksum("VNWNV")));
        }

        [Test]
        public void ReferenceVectorConfig_BezPolohyHodiVyjimku()
        {
            Assert.Throws<ArgumentNullException>(() => VnCommands.ReferenceVectorConfig(null, Datum));
        }
    }
}
