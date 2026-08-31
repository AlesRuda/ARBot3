using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Hlida, ze registr parametru a zdrojovy kod se nerozejdou. Centralni deklarace ma jednu
    /// vadu - da se na ni zapomenout - a tenhle test je ta vada zalatana.
    /// Viz doc/configuration.md.
    /// </summary>
    public class ParamRegistryGuardTests
    {
        /// <summary>
        /// Sest vzoru, ne jen GetParam*: ARBotRuntime ma dva vlastni pomocniky (ReadDouble,
        /// TryReadMeters), ktere GetParam volaji s PROMENNOU - literal je az na miste volani
        /// toho pomocnika.
        /// </summary>
        private static readonly Regex Volani = new Regex(
            @"(?:GetParamBool|GetParamDouble|GetParamPath|GetParam|ReadDouble|TryReadMeters)\s*\(\s*""([^""]+)""",
            RegexOptions.Compiled);

        /// <summary>Volani Program.GetParam* s necim jinym nez retezcovym literalem.</summary>
        private static readonly Regex NeprimeVolani = new Regex(
            @"Program\.GetParam(?:Bool|Double|Path)?\s*\(\s*(?!"")",
            RegexOptions.Compiled);

        private static string AppDir()
            => Path.Combine(RepoPaths.RootOrBase(), "Src", "ARBot");

        private static IEnumerable<string> Zdrojaky()
            => Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
                        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj"
                                                + Path.DirectorySeparatorChar)
                                 && !p.Contains(Path.DirectorySeparatorChar + "bin"
                                                + Path.DirectorySeparatorChar));

        private static HashSet<string> KliceVeZdroji()
        {
            var klice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Zdrojaky())
                foreach (Match m in Volani.Matches(File.ReadAllText(file)))
                    klice.Add(m.Groups[1].Value);
            // "config" je volba, ODKUD se nastaveni bere - do registru zamerne nepatri.
            klice.Remove("config");
            return klice;
        }

        [Test]
        public void KazdyKlicZeZdrojeJeVRegistru()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var chybi = KliceVeZdroji()
                .Where(k => !ParamRegistry.TryGet(k, out _))
                .OrderBy(k => k).ToList();

            Assert.That(chybi, Is.Empty,
                        "Tyhle klice se v kodu ctou, ale nejsou v ParamRegistry: "
                        + string.Join(", ", chybi));
        }

        [Test]
        public void KazdyKlicZRegistruSeNekdeCte()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var veZdroji = KliceVeZdroji();
            var mrtve = ParamRegistry.All.Select(d => d.Name)
                                         .Where(n => !veZdroji.Contains(n))
                                         .OrderBy(n => n).ToList();

            Assert.That(mrtve, Is.Empty,
                        "Tyhle klice jsou v ParamRegistry, ale nikdo je necte: "
                        + string.Join(", ", mrtve));
        }

        [Test]
        public void NeprimeVolaniGetParamJenVeDvouZnamychPomocnicich()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            // Povolena jsou jen tela ReadDouble a TryReadMeters v ARBotRuntime.cs. Kdyby vznikl
            // dalsi pomocnik, test padne a resenim je pridat ho do vzoru Volani - ne vypnout test.
            var nalezy = new List<string>();
            foreach (var file in Zdrojaky())
            {
                if (Path.GetFileName(file) == "ARBotRuntime.cs") continue;
                if (NeprimeVolani.IsMatch(File.ReadAllText(file)))
                    nalezy.Add(Path.GetFileName(file));
            }

            Assert.That(nalezy, Is.Empty,
                        "Volani Program.GetParam* s ne-literalnim klicem mimo zname pomocniky: "
                        + string.Join(", ", nalezy));
        }
    }
}
