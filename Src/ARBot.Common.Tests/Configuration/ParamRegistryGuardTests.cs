using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Hlida, ze registr parametru a zdrojovy kod se nerozejdou.
    ///
    /// <para>Do 4. 9. 2026 to znamenalo skenovat zdrojaky regexem na literaly
    /// <c>Program.GetParam("klic", default)</c> a porovnavat je s registrem obousmerne. Od typovanych
    /// odkazu (<c>ParamRegistry.NoUart.Value</c>) spatny klic neprojde prekladacem, takze zbyvaji tri
    /// veci, ktere prekladac neumi: (1) jmeno pole odpovida klici (PascalCase bez podtrzitek) a kazdy
    /// popis v <c>All</c> ma sve pole, (2) kazdy parametr se v aplikaci nekde cte (mrtva deklarace),
    /// (3) nikdo nevratil <c>Program.GetParam</c>. Viz doc/configuration.md.</para>
    /// </summary>
    public class ParamRegistryGuardTests
    {
        private static string AppDir()
            => Path.Combine(RepoPaths.RootOrBase(), "Src", "ARBot");

        private static IEnumerable<string> Zdrojaky()
            => Directory.EnumerateFiles(AppDir(), "*.cs", SearchOption.AllDirectories)
                        .Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                                 && !p.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar));

        /// <summary>Verejna staticka pole registru typu <see cref="Param"/> (odkazy na parametry).</summary>
        private static List<(string FieldName, Param Param)> Odkazy()
            => typeof(ParamRegistry)
               .GetFields(BindingFlags.Public | BindingFlags.Static)
               .Where(f => typeof(Param).IsAssignableFrom(f.FieldType))
               .Select(f => (f.Name, (Param)f.GetValue(null)))
               .ToList();

        /// <summary>Klic bez podtrzitek, male pismo - proti nemu se porovnava jmeno pole.</summary>
        private static string Normalizuj(string klic) => klic.Replace("_", string.Empty).ToLowerInvariant();

        [Test]
        public void JmenoPoleOdpovidaKlici_PascalCaseBezPodtrzitek()
        {
            var spatne = Odkazy()
                .Where(o => Normalizuj(o.Param.Name) != o.FieldName.ToLowerInvariant())
                .Select(o => $"{o.FieldName} <-> '{o.Param.Name}'")
                .ToList();

            Assert.That(spatne, Is.Empty,
                        "Jmeno pole ma byt klic v PascalCase (no_uart -> NoUart): " + string.Join(", ", spatne));
        }

        [Test]
        public void KazdyPopisVAllMaSvePoleANaopak()
        {
            var odkazy = Odkazy();
            var vAll = ParamRegistry.All.Select(d => d.Name).OrderBy(n => n).ToList();
            var vPolich = odkazy.Select(o => o.Param.Name).OrderBy(n => n).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(vPolich, Is.EqualTo(vAll), "All a staticka pole musi byt tataz mnozina");
                Assert.That(vAll, Is.Unique, "duplicitni klic v registru");
                foreach (var o in odkazy)
                    Assert.That(ParamRegistry.TryGet(o.Param.Name, out var def) && ReferenceEquals(def, o.Param.Def),
                                Is.True, $"{o.FieldName}: TryGet ma vratit tenhle popis");
            });
        }

        [Test]
        public void KazdyParametrSeVAplikaciNekdeCte()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            // Jmena poli pouzita v aplikaci: ParamRegistry.NoUart, ParamRegistry.StSeconds, ...
            var pouzita = new HashSet<string>(StringComparer.Ordinal);
            var vzor = new Regex(@"ParamRegistry\.([A-Z]\w*)\b", RegexOptions.Compiled);
            foreach (var file in Zdrojaky())
                foreach (Match m in vzor.Matches(File.ReadAllText(file)))
                    pouzita.Add(m.Groups[1].Value);

            var mrtve = Odkazy().Select(o => o.FieldName)
                                .Where(n => !pouzita.Contains(n))
                                .OrderBy(n => n).ToList();

            Assert.That(mrtve, Is.Empty,
                        "Tyhle parametry jsou v ParamRegistry, ale aplikace je nikde necte: "
                        + string.Join(", ", mrtve));
        }

        [Test]
        public void ProgramGetParamUzNeexistuje()
        {
            if (!Directory.Exists(AppDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            // Cteni retezcovym klicem s defaultem u volani bylo zruseno 4. 9. 2026 - kdyby se
            // vratilo, vratil by se i dvoji zapis defaultu a preklep v klici bez prekladu.
            var vzor = new Regex(@"Program\.GetParam(?:Bool|Double|Path)?\s*\(", RegexOptions.Compiled);
            var nalezy = Zdrojaky().Where(f => vzor.IsMatch(File.ReadAllText(f)))
                                   .Select(Path.GetFileName).ToList();

            Assert.That(nalezy, Is.Empty, "Program.GetParam* se vratilo do: " + string.Join(", ", nalezy));
        }
    }
}
