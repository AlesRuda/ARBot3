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
    }
}
