using System;
using System.Collections.Generic;
using ARBot.Common.Calibration;
using ARBot.Common.Missions;
using ARBot.Common.Regulators;
using NUnit.Framework;

using static ARBot.Common.Tests.Calibration.MagCalSamples;

namespace ARBot.Common.Tests.Missions
{
    /// <summary>
    /// Mise <c>magcal</c>: robot STOJI, sbira pole pri otaceni rukou, hlasi pokryti a na pokyn
    /// zapise kalibraci. Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalMissionTests
    {
        /// <summary>Regulator, ktery nic neridi — testum staci, ze je (nebo NENI) nastaveny.</summary>
        private sealed class StubRegulator : IRegulator
        {
            public RegulatorResult Control(ARBot.Common.Models.IModelState state)
                => new RegulatorResult();
            public bool IsFinished => false;
        }

        private sealed class Drzitel : IRegulatorHolder
        {
            public IRegulator Regulator { get; set; } = new StubRegulator();
        }

        private sealed class Senzor : IMagCalControl
        {
            /// <summary>Jednotkova kompenzace — tu mise zapisuje na dobu mereni.</summary>
            public const string Jednotka = "1,0,0,0,1,0,0,0,1,0,0,0";

            /// <summary>
            /// Kompenzace „v senzoru" pri startu mise — <b>zamerne NEjednotkova</b>: je to ta
            /// skutecna, zapsana 11. 9. 2026. Kdyby tu byla jednotka, testy na vymazani a vraceni
            /// registru 23 by prosly i s rozbitou implementaci.
            /// </summary>
            public double[] Reg23 =
            {
                1.121575, 0.007452, -0.007101,
                0.007452, 1.103300, -0.021040,
                -0.007101, -0.021040, 1.022071,
                -0.110929, 0.014435, 0.049725,
            };

            /// <summary>Vsechny zapisy do registru 23 v poradi — na poradi tady zalezi.</summary>
            public readonly List<string> Zapisy = new();

            public bool Flash, Hsi;
            public bool ZapisSelze, FlashSelze, Reg21Mlci, Reg23Mlci, VymazaniSelze;

            /// <summary>Posledni zapis do registru 23, nebo <c>null</c>.</summary>
            public string Zapsano => Zapisy.Count == 0 ? null : Zapisy[Zapisy.Count - 1];

            /// <summary>Zapisy KALIBRACE, tedy vsechno krome vymazani a vraceni.</summary>
            public List<string> ZapisyKalibrace
                => Zapisy.FindAll(z => z != Jednotka && z != Reg23Text);

            /// <summary>Puvodni obsah registru 23 v tom tvaru, v jakem ho mise zapisuje zpet.</summary>
            public string Reg23Text => string.Join(",", Array.ConvertAll(Reg23,
                v => v.ToString("F6", System.Globalization.CultureInfo.InvariantCulture)));

            public double[] ReadRegister(int reg)
            {
                if (reg == IMagCalControl.RegReference)
                    // (0,234; 0; 0,4212) pole a (0; 0; -9,79375) gravitace -> |B| = 0,4818 G.
                    return Reg21Mlci ? null : new double[] { 0.234, 0, 0.4212, 0, 0, -9.79375 };
                if (reg == IMagCalControl.RegCompensation)
                    return Reg23Mlci ? null : (double[])Reg23.Clone();
                if (reg == IMagCalControl.RegCalControl) return new double[] { 0, 1, 5 };
                return null;
            }

            public bool WriteMagCompensation(string s)
            {
                // Rozlisuje se, CO se zapisuje: vymazani (jednotka) a zapis kalibrace se daji
                // shodit nezavisle, jinak by nesly otestovat obe vetve zvlast.
                if (VymazaniSelze && s == Jednotka) return false;
                if (ZapisSelze && s != Jednotka && s != Reg23Text) return false;
                Zapisy.Add(s);
                return true;
            }

            /// <summary>Posloupnost zasahu do registru 44 — na PORADI tady zalezi.</summary>
            public readonly List<string> HsiPrikazy = new();

            public bool SetOnboardHsi(bool run)
            {
                Hsi = run;
                HsiPrikazy.Add(run ? "run" : "off");
                return true;
            }

            public bool ResetOnboardHsi()
            {
                HsiPrikazy.Add("reset");
                return true;
            }

            public bool SaveToFlash()
            {
                if (FlashSelze) return false;
                Flash = true;
                return true;
            }
        }

        private static MagCalMission Mise(Senzor s, Drzitel d) => new MagCalMission(s, d);

        /// <summary>Mise s velkou frontou — testy, ktere ji krmi tisici vzorku, nesmi nic ztratit.</summary>
        private static MagCalMission MiseSFrontou(Senzor s, Drzitel d)
            => new MagCalMission(s, d, queueCapacity: 100000);

        [Test]
        public void PriStartu_JeRegulatorZahozeny()
        {
            var d = new Drzitel();
            using var m = Mise(new Senzor(), d);

            m.StartMission();

            Assert.That(d.Regulator, Is.Null,
                "mise magcal nesmi NIKDY nechat robota rozjet - je to bezpecnostni invariant");
        }

        [Test]
        public void PriStartu_ZahodiRegulatorIKdyzSenzorMLCI()
        {
            // Bezpecnost se nesmi opirat o to, ze cteni registru projde.
            var d = new Drzitel();
            using var m = Mise(new Senzor { Reg21Mlci = true }, d);

            m.StartMission();

            Assert.That(d.Regulator, Is.Null);
            Assert.That(m.Phase, Is.EqualTo(MagCalPhase.NoReference));
            Assert.That(m.PhaseText, Does.Contain("NEZACALA"));
        }

        [Test]
        public void PriStartu_PrecteRegistr21_AZapneHsi()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            m.StartMission();

            Assert.Multiple(() =>
            {
                Assert.That(m.BRefG, Is.EqualTo(0.4818).Within(0.001),
                            "referencni |B| se CTE z registru 21, nepise natvrdo");
                Assert.That(s.Hsi, Is.True, "registr 44 na Run/NoOnboard - nezavisla kontrola");
                Assert.That(m.Reg23Before, Is.Not.Empty, "stav registru 23 patri do zaznamu");
            });
        }

        [Test]
        public void PoStartu_CekaNaObsluhu()
        {
            using var m = Mise(new Senzor(), new Drzitel());
            m.StartMission();

            Assert.Multiple(() =>
            {
                Assert.That(m.MissionName, Is.EqualTo("magcal"));
                Assert.That(m.Phase, Is.EqualTo(MagCalPhase.Collecting));
                Assert.That(m.WaitingFor, Is.EqualTo(MissionWait.MagCoverage));
                Assert.That(m.PhaseText, Does.StartWith("POKRACUJ"));
                Assert.That(m.Elapsed, Is.EqualTo(TimeSpan.Zero));
            });
        }

        [Test]
        public void ZapisBezHotovehoPokryti_SeNEPROVEDE()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());
            m.StartMission();

            Assert.Multiple(() =>
            {
                Assert.That(m.WriteToSensor(), Is.False);
                Assert.That(s.ZapisyKalibrace, Is.Empty,
                            "nehotova kalibrace se do senzoru zapsat NESMI");
                Assert.That(s.Zapsano, Is.EqualTo(Senzor.Jednotka),
                            "v registru zustava jen vymazani, ktere mise dela na zacatku");
                Assert.That(s.Flash, Is.False);
                Assert.That(m.Phase, Is.EqualTo(MagCalPhase.Collecting));
            });
        }

        [Test]
        public void ZapisBezStartuMise_SeNEPROVEDE()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            Assert.That(m.WriteToSensor(), Is.False);
            Assert.That(s.Zapisy, Is.Empty, "mise nezacala - do senzoru se nesmi sahnout vubec");
        }

        [Test]
        public void MiseKteraNezacala_NesbiraANehlasiPokryti()
        {
            using var m = Mise(new Senzor { Reg21Mlci = true }, new Drzitel());
            m.StartMission();

            Assert.That(m.Coverage, Is.Null);
            Assert.That(m.Usable, Is.False);
            Assert.That(m.WaitingFor, Is.EqualTo(MissionWait.None),
                        "mise, ktera nezacala, na obsluhu neceka - ceka na opravu senzoru");
        }

        [Test]
        public void MagCoverage_JeNaKONCI_Vyctu()
        {
            // Cislo je soucasti formatu zpravy - precislovani by rozbilo starsi zaznamy.
            Assert.That((int)MissionWait.MagCoverage, Is.EqualTo(7));
        }

        [Test]
        public void ZapisTvrdehoZeleza_BezUrceneKoule_SeNEPROVEDE()
        {
            // Slabsi brana nez u plne kalibrace porad JE brana - na rovine se koule neurci
            // (a rovinna elipsa dava nesmyslny stred), takze nesmi jit zapsat nic.
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());
            m.StartMission();

            Assert.Multiple(() =>
            {
                Assert.That(m.WriteHardIronOnly(), Is.False);
                Assert.That(s.ZapisyKalibrace, Is.Empty);
                Assert.That(s.Flash, Is.False);
            });
        }

        [Test]
        public void ZapisTvrdehoZeleza_BezStartuMise_SeNEPROVEDE()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            Assert.That(m.WriteHardIronOnly(), Is.False);
            Assert.That(s.Zapisy, Is.Empty);
        }

        [Test]
        public void PriStartu_SeNejdrivRESETUJEPalubniHsi_TeprvePakRUN()
        {
            // TN002 (VectorNav), kap. 4.1, krok 1: "Clear any previous real-time calibration
            // solutions by setting Mode to Reset". ICD registru 44 rika proc: prechod Run->Off
            // reseni NEMAZE a pri dalsim Run se pokracuje ze stareho. Bez resetu by tedy
            // registr 47 nesl reseni z MINULE mise - a prave ten pouzivame jako NEZAVISLOU
            // kontrolu naseho prolozeni.
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            m.StartMission();

            Assert.That(s.HsiPrikazy, Is.EqualTo(new[] { "reset", "run" }),
                "poradi je podstatne: reset MUSI predchazet run");
        }

        [Test]
        public void PriUkonceniMise_SeVypnePalubniHsi_IKdyzSeNICNEZAPSALO()
        {
            // TN002, kap. 5.2: "if a calibration procedure is not being run, then the Mode field
            // in Register 44 must be turned off to ensure proper function of the sensor" - a Run
            // je tam primo vyjmenovany jako pricina UJIZDEJICIHO KURZU.
            //
            // ⚠️ Presne tohle se stalo 10. 9. 2026: obsluha misi nedokoncila, takze se cesta
            // s vypnutim (ta po uspesnem zapisu) nikdy neprovedla a senzor zustal v Run.
            var s = new Senzor();
            var m = Mise(s, new Drzitel());
            m.StartMission();

            m.Dispose();

            Assert.That(s.HsiPrikazy, Does.Contain("off"),
                "nedokoncena mise nesmi nechat senzor v rezimu Run");
            Assert.That(s.HsiPrikazy[s.HsiPrikazy.Count - 1], Is.EqualTo("off"));
        }

        // ------------------------------------------------------------------
        // Registr 23 (kompenzace) na dobu mereni
        //
        // ⚠️ Duvod, proc tahle skupina existuje: UncompMag v binarnim vystupu VN100 je
        // KOMPENZOVANY (zmereno 12. 9. 2026 - je bit po bitu shodny s registrem 20). Kdyby
        // mise merila s nenulovym registrem 23, prokladala by uz zkompenzovane pole a vysledek
        // by nebyl kalibrace, ale REZIDUUM - a jeho zapis zpatky do registru 23 by tam
        // stavajici dobrou kalibraci PREPSAL matici blizkou jednotkove, tedy smazal.
        // Viz doc/imu-and-frames.md.
        // ------------------------------------------------------------------

        [Test]
        public void PriStartu_VymazeRegistr23_ALEJENDoRAM()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            m.StartMission();

            Assert.Multiple(() =>
            {
                Assert.That(s.Zapisy, Is.EqualTo(new[] { Senzor.Jednotka }),
                    "bez vymazani by se merilo ze zkompenzovaneho pole a vysledek by byl zbytek");
                Assert.That(s.Flash, Is.False,
                    "vymazani se do flash ukladat NESMI - vypadek napajeni ma sam vratit"
                    + " puvodni kalibraci");
                Assert.That(m.Reg23Before, Is.EqualTo(s.Reg23Text),
                    "stav pred misi musi zustat znamy, jinak neni co vratit");
                Assert.That(m.Phase, Is.EqualTo(MagCalPhase.Collecting));
            });
        }

        [Test]
        public void KdyzRegistr23NejdePrecist_MiseNEZACNE()
        {
            // Mazat bez precteni nejde: nebylo by co vratit. A merit bez vymazani taky ne.
            var s = new Senzor { Reg23Mlci = true };
            var d = new Drzitel();
            using var m = Mise(s, d);

            m.StartMission();

            Assert.Multiple(() =>
            {
                Assert.That(m.Phase, Is.EqualTo(MagCalPhase.NotCleared));
                Assert.That(m.PhaseText, Does.Contain("NEZACALA"));
                Assert.That(m.Coverage, Is.Null, "mise, ktera nezacala, nesmi sbirat");
                Assert.That(s.Zapisy, Is.Empty);
                Assert.That(d.Regulator, Is.Null, "bezpecnostni invariant plati i tady");
            });
        }

        [Test]
        public void KdyzRegistr23NejdeVymazat_MiseNEZACNE_APalubniHsiZUSTANEVYPNUTE()
        {
            // Poradi v StartMission je podstatne: registr 23 se resi PRED zapnutim HSI, takze
            // kdyz vymazani selze, neni po cem uklizet.
            var s = new Senzor { VymazaniSelze = true };
            using var m = Mise(s, new Drzitel());

            m.StartMission();

            Assert.Multiple(() =>
            {
                Assert.That(m.Phase, Is.EqualTo(MagCalPhase.NotCleared));
                Assert.That(m.Coverage, Is.Null);
                Assert.That(s.HsiPrikazy, Is.Empty,
                    "kdyz mise nezacala, nesmi zustat zapnuta palubni HSI");
            });
        }

        [Test]
        public void NedokoncenaMise_VRATIRegistr23()
        {
            // ⚠️ Presne tenhle scenar se v poli 10. 9. 2026 stal DVAKRAT: obsluha misi
            // nedokoncila. Bez vraceni by robot jezdil BEZ kalibrace az do restartu senzoru -
            // tedy ve stavu, ktery 6. 9. 2026 delal chybu kurzu +-25 stupnu.
            var s = new Senzor();
            var m = Mise(s, new Drzitel());
            m.StartMission();

            m.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(s.Zapsano, Is.EqualTo(s.Reg23Text),
                    "nedokoncena mise musi vratit registr 23 do stavu pred misi");
                Assert.That(s.Flash, Is.False, "vraceni je taky jen do RAM");
                Assert.That(s.ZapisyKalibrace, Is.Empty);
            });
        }

        [Test]
        public void VraceniRegistru23_JeIdempotentni()
        {
            // Stop() chodi i vickrat (explicitne + z Dispose) a druhy zapis do senzoru je
            // zbytecny provoz na lince.
            var s = new Senzor();
            var m = Mise(s, new Drzitel());
            m.StartMission();

            m.Stop();
            m.Dispose();

            Assert.That(s.Zapisy, Is.EqualTo(new[] { Senzor.Jednotka, s.Reg23Text }));
        }

        [Test]
        public void MiseKteraNezacala_Registr23NESAHA()
        {
            var s = new Senzor { Reg21Mlci = true };
            var m = Mise(s, new Drzitel());
            m.StartMission();

            m.Dispose();

            Assert.That(s.Zapisy, Is.Empty,
                "co jsme nevymazali, to nesmime ani vracet - prepsalo by to cizi nastaveni");
        }

        [Test]
        public void PoUSPESNEMZapisu_SeRegistr23UzNEVRACI()
        {
            // V registru je to, co si obsluha vyzadala - vratit pres to starou kalibraci by
            // znamenalo zahodit vysledek cele mise.
            var s = new Senzor();
            var m = MiseSFrontou(s, new Drzitel());
            m.StartMission();
            NakrmOtackami(m);

            Assert.That(m.WriteToSensor(), Is.True, m.PhaseText);
            string zapsanaKalibrace = s.Zapsano;
            m.Dispose();

            Assert.Multiple(() =>
            {
                Assert.That(zapsanaKalibrace, Is.Not.EqualTo(Senzor.Jednotka));
                Assert.That(s.Flash, Is.True, "kalibrace se uklada i do flash");
                Assert.That(s.Zapsano, Is.EqualTo(zapsanaKalibrace),
                    "po uspesnem zapisu uz se registr 23 vracet NESMI");
            });
        }

        [Test]
        public void KdyzZapisKalibraceSELZE_RegistrSeStejneVRATI()
        {
            var s = new Senzor { ZapisSelze = true };
            var m = MiseSFrontou(s, new Drzitel());
            m.StartMission();
            NakrmOtackami(m);

            Assert.That(m.WriteToSensor(), Is.False, "zapis mel selhat");
            m.Dispose();

            Assert.That(s.Zapsano, Is.EqualTo(s.Reg23Text),
                "kdyz se nova kalibrace nezapsala, musi se vratit ta puvodni");
        }

        /// <summary>
        /// Nakrmi misi trema otackami (rovina + dva protilehle naklony) a pocka, az prolozeni
        /// dobehne. Mise ma sberac uvnitr a zpravy zpracovava na vlastnim vlakne, takze se
        /// na vysledek musi pockat — cekani je na CPU, takze v praxi desetiny sekundy.
        /// </summary>
        private static void NakrmOtackami(MagCalMission m)
        {
            m.Start();
            double t = 0;
            Obrat(m.Post, 0.0, 0, ref t);
            Obrat(m.Post, 0.40, 0, ref t);
            Obrat(m.Post, 0.40, Math.PI, ref t);

            var konec = DateTime.UtcNow.AddSeconds(30);
            while (!m.Usable && DateTime.UtcNow < konec) System.Threading.Thread.Sleep(10);
            Assert.That(m.Usable, Is.True, "syntetickych otacek melo byt dost: " + m.PhaseText);
        }

        [Test]
        public void MiseKteraNezacala_PalubniHsiNESAHA()
        {
            // Kdyz mise nezacala (napr. nesla precist reference), nesmi se pri uklidu vypinat
            // neco, co jsme nezapnuli - prepsalo by to nastaveni, ktere si nekdo udelal jinak.
            var s = new Senzor();
            var m = Mise(s, new Drzitel());

            m.Dispose();

            Assert.That(s.HsiPrikazy, Is.Empty);
        }
    }
}
