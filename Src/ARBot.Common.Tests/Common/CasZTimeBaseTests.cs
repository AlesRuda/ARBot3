using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Common
{
    /// <summary>
    /// Hlídá pravidlo z CLAUDE.md: <b>čas se měří přes <c>TimeBase.Now</c>, ne přes systémové
    /// hodiny</b>.
    ///
    /// <para><b>Proč to je pravidlo.</b> <see cref="ARBot.Common.Common.TimeBase"/> je čas startu
    /// aplikace plus monotónní <c>Stopwatch</c> a <b>záměrně nesleduje skoky systémových hodin</b>
    /// (NTP). Když se obě základny míchají, rozdíly se po synchronizaci hodin skokově rozjedou
    /// a proti <c>UtcNow</c> jsou navíc posunuté o offset zóny (u nás 1–2 h). Sjednoceno
    /// 4. 9. 2026 — tehdy se našly čtyři případy míchání, všechny v diagnostice, takže posun
    /// o dvě hodiny nevypadal jako chyba, jen jako nesmyslné číslo.</para>
    ///
    /// <para>⚠️ <b>Audit 15. 9. 2026 našel pátý — a ten už v diagnostice není.</b>
    /// <c>T265TrackingCamera</c> razítkovala <c>IMUState</c> přes <c>D435Camera.CalcTimeStamp</c>,
    /// tedy <b>hodinami zařízení</b> (epocha 1970 + offset zóny + ms z kamery). Ten
    /// <c>IMUState</c> teče od 6. 9. 2026 do fúze jako <c>VIO/yawrate</c>, takže se buď
    /// zahazoval jako <c>TooOld</c>, nebo — kdyby byl napřed — posunul <c>tBase</c> a zahodil
    /// tím <b>VN100, GPS i odometrii</b>.</para>
    ///
    /// <para><b>Co je povolené:</b> kalendářní datum pro člověka (jména souborů
    /// <c>records/yyyyMMdd-HHmmss.rec</c>, <c>logs/crash-*.log</c>, hlavička crash logu, hodiny
    /// na stránce náhledu) a seed generátoru. Poznají se podle formátovacího řetězce, resp.
    /// <c>.Ticks</c>.</para>
    /// </summary>
    public class CasZTimeBaseTests
    {
        /// <summary>Projekty, které běží na zařízení (UI se hlídá volněji).</summary>
        private static readonly string[] Projekty =
        {
            Path.Combine("Src", "ARBot.Common"),
            Path.Combine("Src", "ARBot.HAL"),
            Path.Combine("Src", "ARBot.HALArmbian"),
            Path.Combine("Src", "ARBot.HALWindows"),
            Path.Combine("Src", "ARBot.Runtime"),
        };

        private static readonly Regex SystemoveHodiny =
            new Regex(@"\bDateTime(Offset)?\.(Utc)?Now\b", RegexOptions.Compiled);

        /// <summary>Kalendářní datum pro člověka nebo seed — to se smí.</summary>
        private static readonly Regex PovolenePouziti =
            new Regex(@"(:\s*""?yyyy|ToString\s*\(\s*""|\.Ticks\b)", RegexOptions.Compiled);

        private static bool JeKomentar(string radek)
        {
            string t = radek.TrimStart();
            return t.StartsWith("//") || t.StartsWith("*");
        }

        private static IEnumerable<string> Zdrojaky(string koren)
        {
            foreach (string rel in Projekty)
            {
                string dir = Path.Combine(koren, rel);
                if (!Directory.Exists(dir)) continue;
                foreach (string f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     || f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                        continue;
                    // TimeBase sam systemove hodiny pouzit MUSI - je to jeho kotva.
                    if (Path.GetFileName(f) == "TimeBase.cs") continue;
                    yield return f;
                }
            }
        }

        [Test]
        public void MereniCasuJdeZTimeBase_NeZeSystemovychHodin()
        {
            string koren = RepoPaths.RootOrBase();
            var zdrojaky = Zdrojaky(koren).ToList();
            if (zdrojaky.Count == 0)
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var vady = new List<string>();
            foreach (string f in zdrojaky)
            {
                string[] radky = File.ReadAllLines(f);
                for (int i = 0; i < radky.Length; i++)
                {
                    string r = radky[i];
                    if (JeKomentar(r) || !SystemoveHodiny.IsMatch(r) || PovolenePouziti.IsMatch(r))
                        continue;
                    vady.Add($"{Path.GetFileName(f)}:{i + 1}");
                }
            }

            Assert.That(vady, Is.Empty,
                "Cas se meri pres TimeBase.Now (monotonni, neskace s NTP), ne pres systemove hodiny. "
                + "Systemovy cas patri jen tam, kde je potreba kalendarni datum pro cloveka. "
                + "Nalezeno v: " + string.Join(", ", vady));
        }
    }
}
