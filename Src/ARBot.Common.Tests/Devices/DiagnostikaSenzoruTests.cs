using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>
    /// Hlídá, že diagnostika <b>poruch senzorů</b> jde do <c>Trace</c>, ne do <c>Debug</c>.
    ///
    /// <para><b>Nač to je:</b> <c>Debug.WriteLine</c> je <c>[Conditional("DEBUG")]</c>, takže
    /// v Release buildu — a právě ten běží na zařízení — po poruše nezůstane <b>žádná stopa</b>.
    /// Ta past už kousla <b>dvakrát</b>: nejdřív u hlášky o zahozeném měření ve fúzi
    /// (opraveno 20. 8. 2026, viz <c>AsyncFusionEngine.Enqueue</c>), pak 2. 9. 2026 u kamer —
    /// neohlásily se a v panelu <i>Debug output</i> o nich nebyl ani řádek, takže se příčina
    /// hledala hodinu měřením zvenčí, místo aby ji driver rovnou napsal. Potřetí už ne.</para>
    ///
    /// <para>Test hledá vzorek <c>Debug.WriteLine($"{Name}: …</c>, tedy hlášení <b>o stavu
    /// senzoru</b> (<c>Name</c> je jméno senzoru ze <c>SensorBase</c>). Vývojářské dumpy, které
    /// se senzoru netýkají, schválně nechává na pokoji — třeba výpis intrinsik kamery
    /// (<c>Debug.WriteLine(name + …)</c>) je ladicí pomůcka, ne diagnostika poruchy.</para>
    /// </summary>
    public class DiagnostikaSenzoruTests
    {
        /// <summary>Projekty, ve kterých žijí ovladače senzorů.</summary>
        private static readonly string[] Projekty =
        {
            Path.Combine("Src", "ARBot.Common", "Devices"),
            Path.Combine("Src", "ARBot.HAL", "Devices"),
            Path.Combine("Src", "ARBot.HALWindows", "Devices"),
            Path.Combine("Src", "ARBot.HALArmbian", "Devices"),
        };

        /// <summary>Hlášení o stavu senzoru poslané do Debug (tedy v Release zahozené).</summary>
        private static readonly Regex DebugSeStavemSenzoru =
            new Regex(@"Debug\.WriteLine\s*\(\s*\$""\{Name\}", RegexOptions.Compiled);

        private static IEnumerable<string> Zdrojaky()
        {
            string koren = RepoPaths.RootOrBase();
            foreach (string rel in Projekty)
            {
                string dir = Path.Combine(koren, rel);
                if (!Directory.Exists(dir))
                    continue;
                foreach (string f in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                    if (!f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                     && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
                        yield return f;
            }
        }

        [Test]
        public void PoruchySenzoruJdouDoTrace_NeDoDebug()
        {
            var zdrojaky = Zdrojaky().ToList();
            if (zdrojaky.Count == 0)
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var vady = new List<string>();
            foreach (string f in zdrojaky)
            {
                string[] radky = File.ReadAllLines(f);
                for (int i = 0; i < radky.Length; i++)
                    if (DebugSeStavemSenzoru.IsMatch(radky[i]))
                        vady.Add($"{Path.GetFileName(f)}:{i + 1}");
            }

            Assert.That(vady, Is.Empty,
                "Diagnostika stavu senzoru musí jít do Trace, ne do Debug - v Release (na zařízení) "
                + "by po poruše nezůstala žádná stopa. Nalezeno v: " + string.Join(", ", vady));
        }

        /// <summary>
        /// Druhá strana téže mince: ověřuje, že ty hlášky v ovladačích <b>vůbec jsou</b>.
        /// Kdyby je někdo smazal, první test by prošel triviálně a slepota by byla zpátky.
        /// </summary>
        [Test]
        public void OvladaceKamerHlasiStavDoTrace()
        {
            // Presna jmena, ne StartsWith: T265TrackingCameraNative.cs je zakomentovana varianta
            // (ARBotHW ji nevytvari) a nema diagnostiku vubec - hlidat u ni hlasky nema smysl.
            var hlidane = new[] { "D435Camera.cs", "T265TrackingCamera.cs" };
            var zdrojaky = Zdrojaky().Where(f => hlidane.Contains(Path.GetFileName(f))).ToList();
            if (zdrojaky.Count == 0)
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var traceVzor = new Regex(@"Trace\.WriteLine\s*\(\s*\$""\{Name\}", RegexOptions.Compiled);
            foreach (string f in zdrojaky)
            {
                int pocet = traceVzor.Matches(File.ReadAllText(f)).Count;
                Assert.That(pocet, Is.GreaterThan(0),
                            $"{Path.GetFileName(f)} nehlási stav senzoru do Trace vůbec - "
                            + "na zařízení by po poruše nebylo poznat proč.");
            }
        }
    }
}
