using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Configuration;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// Pravidla pro systemd jednotku <c>deploy/arbot.service</c>.
    ///
    /// <para><b>Nač to je (nález auditu 15. 9. 2026).</b> Jednotka měla
    /// <c>StartLimitBurst=5</c> / <c>StartLimitIntervalSec=300</c>, takže <b>šestý start během
    /// pěti minut skončil <c>start-limit-hit</c></b>, jednotka zůstala ve stavu <c>failed</c>
    /// a <c>Restart=always</c> ji <b>už nikdy nevrátila</b>. Robot byl mrtvý do ručního
    /// <c>systemctl reset-failed</c> — v terénu, kde je u robota jen mobil, tedy <b>trvale</b>.</para>
    ///
    /// <para><b>Proč je odstranění limitu bezpečné.</b> Limit tam byl proti zaplavení journalu.
    /// Jenže robot se po restartu <b>nerozjede</b> — runtime nastartuje, rozjede senzory a stojí,
    /// dokud mu člověk nevybere misi, a i pak jen při drženém nouzovém zastavení. Opakovaný
    /// start tedy nic neřídí, jen to zkouší znovu; a dvě deterministické příčiny, u kterých
    /// opakování nemá smysl (vadná konfigurace = 2, druhá instance = 3), už odfiltruje
    /// <c>RestartPreventExitStatus</c>. Zbývají přechodné pády — a u těch je opakování
    /// přesně to, co chceme.</para>
    /// </summary>
    public class ArbotServiceUnitTests
    {
        private static string[] Jednotka()
        {
            string p = Path.Combine(RepoPaths.RootOrBase(), "deploy", "arbot.service");
            return File.Exists(p) ? File.ReadAllLines(p) : null;
        }

        /// <summary>Hodnota direktivy, nebo <c>null</c>; komentáře (<c>#</c>) se ignorují.</summary>
        private static string Direktiva(string[] radky, string klic)
        {
            foreach (string r in radky)
            {
                string t = r.Trim();
                if (t.Length == 0 || t.StartsWith("#")) continue;
                int i = t.IndexOf('=');
                if (i <= 0) continue;
                if (t.Substring(0, i).Trim().Equals(klic, StringComparison.OrdinalIgnoreCase))
                    return t.Substring(i + 1).Trim();
            }
            return null;
        }

        [Test]
        public void SluzbaSeNevzdavaPoNekolikaRestartech()
        {
            var radky = Jednotka();
            if (radky == null) Assert.Ignore("Bezi bez repa - neni co skenovat.");

            string interval = Direktiva(radky, "StartLimitIntervalSec");
            string burst = Direktiva(radky, "StartLimitBurst");

            // Limit je vypnuty, kdyz je interval 0 (systemd: „bez omezeni") nebo burst 0.
            bool vypnuty = interval == "0" || burst == "0";

            Assert.That(vypnuty, Is.True,
                "Jednotka omezuje pocet startu (StartLimitIntervalSec=" + (interval ?? "-")
                + ", StartLimitBurst=" + (burst ?? "-") + "). Po vycerpani limitu zustane ve stavu "
                + "'failed' a Restart=always ji uz nevrati - robot je v terenu mrtvy do rucniho "
                + "'systemctl reset-failed'. Robot se pritom po restartu sam nerozjede, takze "
                + "opakovany start nic neridi.");
        }

        /// <summary>
        /// Druhá strana téže mince: restart musí zůstat zapnutý a deterministické příčiny se
        /// nemají opakovat donekonečna.
        /// </summary>
        [Test]
        public void RestartJeZapnutyAVadnaKonfiguraceSeNeopakuje()
        {
            var radky = Jednotka();
            if (radky == null) Assert.Ignore("Bezi bez repa - neni co skenovat.");

            Assert.Multiple(() =>
            {
                Assert.That(Direktiva(radky, "Restart"), Is.EqualTo("always"));

                string prevent = Direktiva(radky, "RestartPreventExitStatus") ?? string.Empty;
                var kody = prevent.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                Assert.That(kody, Does.Contain("2"), "vadna konfigurace se restartem nespravi");
                Assert.That(kody, Does.Contain("3"), "druha instance se restartem nespravi");
            });
        }
    }
}
