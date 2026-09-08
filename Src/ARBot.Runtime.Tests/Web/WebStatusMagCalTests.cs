using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.Robot.Web;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// <b>Gate zapisu kalibrace do senzoru</b>: jen pri HOTOVEM pokryti a DRZENEM nouzovem
    /// zastaveni.
    ///
    /// <para>Nejsou to kosmeticke testy. Tenhle gate je to, co drzi vedome nakreslenou caru
    /// projektu „zadny zapis do senzoru bez rozhodnuti cloveka" (viz doc/decisions.md
    /// a doc/plan-vn100-kalibrace.md, rozhodnuti 2) — mechanismus se presunul ze ssh do
    /// tlacitka, ale pravidlo plati dal.</para>
    /// </summary>
    [NonParallelizable]
    public class WebStatusMagCalTests
    {
        /// <summary>Stav motoru s nouzovym zastavenim - to je ta fyzicka pojistka.</summary>
        private static void Stop(WebStatus s, bool drzi)
            => s.Post(new ARBot.Common.Devices.MotorStateBase(drzi, 0, 0, 24, 0, 0, 0, 0));

        private static MagCalMsg Zprava(MagCalPhase faze, string verdikt) => new MagCalMsg
        {
            Phase = (int)faze,
            Verdict = verdikt,
            MissingText = faze == MagCalPhase.Ready ? string.Empty : "chybi naklon",
            FilledAzimuthBins = 24, TiltGroups = 3, TiltedGroups = 2, HasOppositeTilts = true,
            Samples = 3770, Condition = 412.5, SdMagnitudeG = 0.0012, SdInclinationDeg = 0.21,
            BRefG = 0.4818,
            Vnwrg23 = "1.2,0.0,0.0,0.0,1.1,0.0,0.0,0.0,1.0,-0.274,-0.058,0.076",
        };

        private static WebStatus Stav(bool stopDrzeny, bool pouzitelne)
        {
            var s = new WebStatus();
            Stop(s, stopDrzeny);
            s.Post(pouzitelne
                ? Zprava(MagCalPhase.Ready, "HOTOVO")
                : Zprava(MagCalPhase.Collecting, "POKRACUJ: chybi naklon"));
            return s;
        }

        [Test]
        public void BezMise_JeZapisZablokovany()
        {
            var s = new WebStatus();
            Stop(s, drzi: true);

            Assert.That(s.MagCalWriteBlockedReason(), Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void BezDrzenehoStopu_JeZapisZablokovany()
        {
            var duvod = Stav(stopDrzeny: false, pouzitelne: true).MagCalWriteBlockedReason();

            Assert.That(duvod, Is.Not.Null, "do senzoru se nezapisuje, kdyz robot muze jet");
            Assert.That(duvod, Does.Contain("nouzove zastaveni"),
                        "duvod musi rict, CO udelat - sede tlacitko bez vysvetleni obsluhu zastavi");
        }

        [Test]
        public void BezHotovehoPokryti_JeZapisZablokovany()
        {
            var duvod = Stav(stopDrzeny: true, pouzitelne: false).MagCalWriteBlockedReason();

            Assert.That(duvod, Is.Not.Null, "nehotova kalibrace se zapsat NESMI");
            Assert.That(duvod, Does.Contain("naklon"), "a rekne se, co chybi");
        }

        [Test]
        public void BezStavuMotoru_JeZapisZablokovany()
        {
            // Kdyz motory mlci, o stavu stopu nevime nic - a "nevim" nesmi znamenat "smi se".
            var s = new WebStatus();
            s.Post(Zprava(MagCalPhase.Ready, "HOTOVO"));

            Assert.That(s.MagCalWriteBlockedReason(), Does.Contain("motory"));
        }

        [Test]
        public void SDrzenymStopemAHotovymPokrytim_Projde()
        {
            Assert.That(Stav(stopDrzeny: true, pouzitelne: true).MagCalWriteBlockedReason(),
                        Is.Null);
        }

        [Test]
        public void Json_NeseVerdiktCislaIDuvodZablokovani()
        {
            string json = Stav(stopDrzeny: false, pouzitelne: false).ToJson(running: true);

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"magcal\""));
                Assert.That(json, Does.Contain("\"verdict\":\"POKRACUJ: chybi naklon\""));
                Assert.That(json, Does.Contain("\"azimuths\":24"));
                Assert.That(json, Does.Contain("\"opposite\":true"));
                Assert.That(json, Does.Contain("\"canWrite\":false"));
                Assert.That(json, Does.Contain("\"writeBlocked\""),
                            "duvod musi byt v JSONu, aby ho stranka mohla ukazat u tlacitka");
            });
        }

        [Test]
        public void Json_BezMise_BlokVubecNeni()
        {
            // Prazdny blok by stranka musela umet odlisit od "mise bezi, ale nic nenamerila".
            Assert.That(new WebStatus().ToJson(running: true), Does.Not.Contain("\"magcal\""));
        }
    }
}
