using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Hlídá profily uložené v repu (<c>config/*.cfg</c>).
    ///
    /// <para><b>Nač to je:</b> celý konfigurační systém stojí na tom, že neznámý klíč nebo
    /// neplatná hodnota v profilu je <b>chyba při startu</b>, ne tiché propadnutí na default
    /// (viz doc/configuration.md). Profily v repu ale nikdo nekontroloval — překlep v nich se
    /// projevil až tím, že aplikace na zařízení <b>vůbec nenastartuje</b>. Přesně tam, kde je
    /// oprava nejdražší: přes SSH, v terénu.</para>
    ///
    /// <para>Test proto profily čte a validuje týmž registrem, kterým je validuje
    /// <see cref="ParamStore.Build"/> za běhu. Záměrně <b>nevolá</b> <c>Build</c> — ten
    /// přepisuje statické <c>ParamStore.Current</c>, takže by testy ovlivňovaly jeden druhý.</para>
    /// </summary>
    public class ProfilyVRepuTests
    {
        private static string ConfigDir() => Path.Combine(RepoPaths.RootOrBase(), "config");

        private static IEnumerable<string> Profily()
            => Directory.Exists(ConfigDir())
                ? Directory.EnumerateFiles(ConfigDir(), "*.cfg", SearchOption.TopDirectoryOnly)
                : Enumerable.Empty<string>();

        [Test]
        public void SlozkaConfigExistujeANeniPrazdna()
        {
            if (!Directory.Exists(ConfigDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            Assert.That(Profily(), Is.Not.Empty, "V config/ nejsou zadne profily.");
        }

        /// <summary>
        /// Každý profil musí projít registrem: žádný neznámý klíč, žádná neplatná hodnota.
        /// </summary>
        [Test]
        public void KazdyProfilProjdeRegistrem()
        {
            if (!Directory.Exists(ConfigDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var vady = new List<string>();
            foreach (string profil in Profily())
            {
                List<KeyValuePair<string, string>> dvojice;
                try
                {
                    dvojice = ParamFile.Read(profil);
                }
                catch (ParamFileException ex)
                {
                    vady.Add($"{Path.GetFileName(profil)}: nejde precist - {ex.Message}");
                    continue;
                }

                foreach (string vada in ParamRegistry.Validate(dvojice))
                    vady.Add($"{Path.GetFileName(profil)}: {vada}");
            }

            Assert.That(vady, Is.Empty, string.Join("; ", vady));
        }

        /// <summary>
        /// Hodnoty typu <see cref="ParamType.Path"/> musí ukazovat na existující soubor.
        ///
        /// <para>Registr kontroluje jen TVAR cesty, ne že tam něco je — takže profil
        /// s <c>map=OSM/PreklepVeJmenu.osm</c> validaci projde a aplikace spadne až za běhu.</para>
        /// </summary>
        [Test]
        public void CestyVProfilechUkazujiNaExistujiciSoubory()
        {
            if (!Directory.Exists(ConfigDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var chybi = new List<string>();
            foreach (string profil in Profily())
                foreach (var dvojice in ParamFile.Read(profil))
                {
                    if (!ParamRegistry.TryGet(dvojice.Key, out var def) || def.Type != ParamType.Path)
                        continue;
                    if (string.IsNullOrWhiteSpace(dvojice.Value))
                        continue;

                    string cesta = RepoPaths.Resolve(dvojice.Value);
                    if (!File.Exists(cesta) && !Directory.Exists(cesta))
                        chybi.Add($"{Path.GetFileName(profil)}: {dvojice.Key}={dvojice.Value} "
                                  + $"-> '{cesta}' neexistuje");
                }

            Assert.That(chybi, Is.Empty, string.Join("; ", chybi));
        }

        /// <summary>
        /// Relativní cesty v profilech musí být psané tak, jak je uvidí <b>Linux</b>: lomítko
        /// dopředu a velikost písmen přesně jako na disku.
        ///
        /// <para><b>Nač to je:</b> profily z <c>config/</c> se nasazují na Orange Pi
        /// (<c>deploy/nasad.ps1</c>), ale píšou se na Windows — a tam obojí projde. Zpětné lomítko
        /// je na Linuxu <b>obyčejný znak ve jménu souboru</b>, takže <c>map=osm\haje.osm</c>
        /// se hledá jako soubor „osm\haje.osm" v kořeni a nenajde; totéž udělá malé <c>osm</c>
        /// proti adresáři <c>OSM</c>. Test výš (existence souboru) to na Windows nechytí, protože
        /// tam obojí existuje. Nalezeno 12. 9. 2026, když panel Konfigurace uložil do
        /// <c>pi-provoz.cfg</c> cestu ve windowsovém tvaru.</para>
        /// </summary>
        [Test]
        public void RelativniCestyVProfilechJsouPsaneProLinux()
        {
            if (!Directory.Exists(ConfigDir()))
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var vady = new List<string>();
            foreach (string profil in Profily())
                foreach (var dvojice in ParamFile.Read(profil))
                {
                    if (!ParamRegistry.TryGet(dvojice.Key, out var def) || def.Type != ParamType.Path)
                        continue;
                    string hodnota = (dvojice.Value ?? string.Empty).Trim();
                    if (hodnota.Length == 0 || Path.IsPathRooted(hodnota))
                        continue;

                    string jmenoProfilu = Path.GetFileName(profil);
                    if (hodnota.Contains('\\'))
                    {
                        vady.Add($"{jmenoProfilu}: {dvojice.Key}={hodnota} -> zpetne lomitko; "
                                 + "na Linuxu je to znak ve jmene souboru, piš '/'");
                        continue;
                    }

                    string naDisku = SkutecnyTvar(hodnota);
                    if (naDisku != null && naDisku != hodnota)
                        vady.Add($"{jmenoProfilu}: {dvojice.Key}={hodnota} -> na disku je "
                                 + $"'{naDisku}'; na Linuxu zalezi na velikosti pismen");
                }

            Assert.That(vady, Is.Empty, string.Join("; ", vady));
        }

        /// <summary>
        /// Přepíše relativní cestu velikostí písmen, jakou má skutečně na disku; <c>null</c>,
        /// když některý článek neexistuje (to hlásí jiný test).
        /// </summary>
        private static string SkutecnyTvar(string relativni)
        {
            var dir = new DirectoryInfo(RepoPaths.RootOrBase());
            var clanky = relativni.Split('/');
            var vysledek = new List<string>();

            for (int i = 0; i < clanky.Length; i++)
            {
                if (dir == null || !dir.Exists) return null;
                var nalezeno = dir.EnumerateFileSystemInfos()
                                  .FirstOrDefault(x => string.Equals(
                                      x.Name, clanky[i], System.StringComparison.OrdinalIgnoreCase));
                if (nalezeno == null) return null;
                vysledek.Add(nalezeno.Name);
                dir = nalezeno as DirectoryInfo;
            }

            return string.Join("/", vysledek);
        }
    }
}
