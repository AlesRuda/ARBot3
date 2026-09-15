using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Pravidla pro skripty v <c>deploy/</c>, které sahají na <b>železo</b> nebo na <b>nasazení</b>.
    ///
    /// <para><b>Nač to je (nález auditu 15. 9. 2026).</b> <c>vnrestore.sh</c> zapisuje konfiguraci
    /// do <b>flash</b> magnetometrického senzoru — a dělal to <b>bez potvrzení</b> a <b>bez
    /// <c>set -e</c></b>. Po selhání dřívějšího příkazu (nepovedené <c>stty</c>, zápis do
    /// nedostupného portu) skript pokračoval dál a uložil do flash <b>polovičatý stav</b>.
    /// U senzoru, jehož špatná konfigurace stála projekt několik výjezdů
    /// (viz doc/imu-and-frames.md), je to nejdražší možná tichá chyba.</para>
    ///
    /// <para><c>pipefail</c> je tu podstatný zvlášť: bez něj má roura úspěch podle
    /// <b>posledního</b> článku, takže selhání uprostřed (<c>cat | grep | sort</c>) projde.</para>
    /// </summary>
    public class DeploySkriptyTests
    {
        private static List<string> Skripty()
        {
            string dir = Path.Combine(RepoPaths.RootOrBase(), "deploy");
            return Directory.Exists(dir)
                 ? Directory.EnumerateFiles(dir, "*.sh").OrderBy(x => x).ToList()
                 : new List<string>();
        }

        [Test]
        public void SkriptyKonciPriPrvniChybe()
        {
            var skripty = Skripty();
            if (skripty.Count == 0) Assert.Ignore("Bezi bez repa - neni co skenovat.");

            var vady = new List<string>();
            foreach (string s in skripty)
            {
                var radky = File.ReadAllLines(s);
                bool ma = radky.Any(r =>
                {
                    string t = r.Trim();
                    return t.StartsWith("set -") && t.Contains("e") && t.Contains("u")
                        && t.Contains("pipefail");
                });
                if (!ma) vady.Add(Path.GetFileName(s));
            }

            Assert.That(vady, Is.Empty,
                "Skript v deploy/ sahá na zelezo nebo na nasazeni a nekonci pri prvni chybe. "
                + "Chce to 'set -euo pipefail' (bez pipefail projde selhani uprostred roury). "
                + "Chybi v: " + string.Join(", ", vady));
        }

        /// <summary>
        /// ⚠️ <b>Zápis do flash senzoru se musí potvrdit.</b> Je to nevratná změna železa, kterou
        /// si má člověk odsouhlasit — ne vedlejší účinek spuštění skriptu.
        /// </summary>
        [Test]
        public void ZapisDoFlashSePotvrzuje()
        {
            var skripty = Skripty();
            if (skripty.Count == 0) Assert.Ignore("Bezi bez repa - neni co skenovat.");

            var vady = new List<string>();
            foreach (string s in skripty)
            {
                var radky = File.ReadAllLines(s);

                // Hleda se SKUTECNE ODESLANI prikazu, ne zminka. `vnprobe.sh` je cistě
                // diagnosticky a VNWNV jen POJMENOVAVA v komentari („zadny zapis do flash") —
                // kdyby test koukal na vyskyt retezce, oznacil by prave ten skript, ktery to
                // pravidlo dodrzuje nejpriklednejs.
                bool zapisuje = radky.Any(r =>
                {
                    string t = r.Trim();
                    return !t.StartsWith("#") && t.Contains("VNWNV");
                });
                if (!zapisuje) continue;

                if (!File.ReadAllText(s).Contains("read -r")) vady.Add(Path.GetFileName(s));
            }

            Assert.That(vady, Is.Empty,
                "Skript zapisuje do FLASH senzoru (VNWNV), ale na nic se nepta. "
                + "Nalezeno v: " + string.Join(", ", vady));
        }
    }
}
