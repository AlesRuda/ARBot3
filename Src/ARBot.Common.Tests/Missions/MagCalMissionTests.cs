using System;
using System.Collections.Generic;
using ARBot.Common.Calibration;
using ARBot.Common.Missions;
using ARBot.Common.Regulators;
using NUnit.Framework;

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
            public string Zapsano;
            public bool Flash, Hsi;
            public bool ZapisSelze, FlashSelze, Reg21Mlci;

            public double[] ReadRegister(int reg)
            {
                if (reg == IMagCalControl.RegReference)
                    // (0,234; 0; 0,4212) pole a (0; 0; -9,79375) gravitace -> |B| = 0,4818 G.
                    return Reg21Mlci ? null : new double[] { 0.234, 0, 0.4212, 0, 0, -9.79375 };
                if (reg == IMagCalControl.RegCompensation)
                    return new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
                if (reg == IMagCalControl.RegCalControl) return new double[] { 0, 1, 5 };
                return null;
            }

            public bool WriteMagCompensation(string s)
            {
                if (ZapisSelze) return false;
                Zapsano = s;
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
                Assert.That(s.Zapsano, Is.Null, "nehotova kalibrace se do senzoru zapsat NESMI");
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
            Assert.That(s.Zapsano, Is.Null);
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
                Assert.That(s.Zapsano, Is.Null);
                Assert.That(s.Flash, Is.False);
            });
        }

        [Test]
        public void ZapisTvrdehoZeleza_BezStartuMise_SeNEPROVEDE()
        {
            var s = new Senzor();
            using var m = Mise(s, new Drzitel());

            Assert.That(m.WriteHardIronOnly(), Is.False);
            Assert.That(s.Zapsano, Is.Null);
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
