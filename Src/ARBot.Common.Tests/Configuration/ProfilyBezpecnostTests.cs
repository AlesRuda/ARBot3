using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Bezpečnostní pravidla pro konfigurační profily v <c>config/</c>.
    ///
    /// <para><b>Nač to je (nález auditu 15. 9. 2026).</b> <c>config/pi-freerun.cfg</c> měl
    /// <c>autorun=true</c> spolu s <c>mission=freerun</c>, tedy <b>robot se po startu aplikace
    /// rozjel sám</b>. To je přesně to, co má projekt zakázané — a ten zákaz není starý, drží ho
    /// dvoufázový běh headless runtime: bez zadané mise runtime nastartuje, rozjede senzory
    /// a <b>stojí</b>, dokud mu člověk misi nevybere, a i pak jen při drženém nouzovém
    /// zastavení (viz CLAUDE.md, doc/headless.md). Profil tu bránu obcházel.</para>
    ///
    /// <para>Druhá polovina téhož nálezu: profil <b>v komentáři sliboval „defenzivních 0,1 m/s"</b>
    /// a nastavoval <c>maxspeed=1</c>, tedy desetkrát víc. Takový rozpor se nepozná jinak než
    /// tím, že robot jede rychleji, než kdokoli čekal.</para>
    /// </summary>
    public class ProfilyBezpecnostTests
    {
        /// <summary>Mise, které robota rozjedou samy (na rozdíl od <c>none</c> a <c>magcal</c>).</summary>
        private static readonly string[] JedouciMise = { "freerun", "robotour", "track" };

        private static List<string> Profily()
        {
            string dir = Path.Combine(RepoPaths.RootOrBase(), "config");
            return Directory.Exists(dir)
                 ? Directory.EnumerateFiles(dir, "*.cfg").OrderBy(x => x).ToList()
                 : new List<string>();
        }

        /// <summary>Hodnota klíče z profilu (řádky <c>klic=hodnota</c>, komentáře od <c>#</c>).</summary>
        private static string Hodnota(IEnumerable<string> radky, string klic)
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

        /// <summary>
        /// ⚠️ <b>Žádný profil nesmí robota rozjet sám.</b> Rozjezd je vyhrazený člověku —
        /// výběrem mise a uvolněním drženého nouzového zastavení.
        /// </summary>
        [Test]
        public void ZadnyProfil_NespojujeAutorunSJedouciMisi()
        {
            var profily = Profily();
            if (profily.Count == 0) Assert.Ignore("Bezi bez repa - neni co skenovat.");

            var vady = new List<string>();
            foreach (string p in profily)
            {
                var radky = File.ReadAllLines(p);
                string autorun = Hodnota(radky, "autorun");
                string mise = Hodnota(radky, "mission");

                if (string.Equals(autorun, "true", StringComparison.OrdinalIgnoreCase)
                 && mise != null
                 && JedouciMise.Contains(mise, StringComparer.OrdinalIgnoreCase))
                    vady.Add($"{Path.GetFileName(p)} (autorun=true + mission={mise})");
            }

            Assert.That(vady, Is.Empty,
                "Profil rozjede robota bez lidskeho pokynu. Rozjezd patri cloveku: vyber mise "
                + "a uvolneni DRZENEHO nouzoveho zastaveni (viz CLAUDE.md, doc/headless.md). "
                + "Nalezeno: " + string.Join(", ", vady));
        }

        /// <summary>
        /// ⚠️ <b>Popis stropu rychlosti musí sedět s hodnotou.</b>
        ///
        /// <para><b>Co se skutečně stalo</b> (upřesněno autorem 15. 9. 2026): strop se v průběhu
        /// času <b>zvedl na 1 m/s</b>, ale komentář dál popisoval počáteční desetkrát nižší
        /// hodnotu. Autoritativní je tedy <b>hodnota</b>, ne popis — rozešly se proto, že se
        /// při změně přepsal jen jeden z nich.</para>
        ///
        /// <para>A přesně to je důvod, proč tenhle test existuje: u stropu rychlosti se rozpor
        /// mezi popisem a hodnotou pozná až tím, že robot jede jinak, než kdokoli čekal — a to
        /// v obou směrech. Při další změně stropu se musí přepsat obojí.</para>
        ///
        /// <para>⚠️ Kouká se <b>jen na souvislý blok komentářů těsně nad klíčem</b>, ne na celý
        /// soubor: tam žije tvrzení o tomhle nastavení. Jinde v profilu se čísla v m/s objevují
        /// legitimně (výchozí hodnota v kódu, historie) a test by je hlásil jako rozpor.</para>
        /// </summary>
        [Test]
        public void PopisStropuRychlosti_SediSHodnotou()
        {
            var profily = Profily();
            if (profily.Count == 0) Assert.Ignore("Bezi bez repa - neni co skenovat.");

            var vady = new List<string>();
            foreach (string p in profily)
            {
                var radky = File.ReadAllLines(p);
                string hodnota = Hodnota(radky, "maxspeed");
                if (hodnota == null) continue;
                if (!double.TryParse(hodnota, System.Globalization.NumberStyles.Float,
                                     System.Globalization.CultureInfo.InvariantCulture, out double v)
                    || v <= 0)
                {
                    vady.Add($"{Path.GetFileName(p)}: maxspeed='{hodnota}' neni kladne cislo");
                    continue;
                }

                foreach (double slib in SlibyNadKlicem(radky, "maxspeed"))
                {
                    // Radovy nesoulad, ne presna shoda: komentar smi mluvit i o vychozi hodnote
                    // v kodu (1.2) nebo o blizkem cisle, aniz by to byl rozpor.
                    if (v / slib > 5 || slib / v > 5)
                        vady.Add($"{Path.GetFileName(p)}: komentar nad klicem mluvi o {slib} m/s, "
                                 + $"maxspeed={v}");
                }
            }

            Assert.That(vady, Is.Empty,
                "Popis profilu se rozchazi s hodnotou stropu rychlosti. " + string.Join("; ", vady));
        }

        /// <summary>Čísla v m/s ze souvislého bloku komentářů těsně nad daným klíčem.</summary>
        private static IEnumerable<double> SlibyNadKlicem(string[] radky, string klic)
        {
            int i = Array.FindIndex(radky, r =>
            {
                string t = r.Trim();
                int j = t.IndexOf('=');
                return j > 0 && !t.StartsWith("#")
                    && t.Substring(0, j).Trim().Equals(klic, StringComparison.OrdinalIgnoreCase);
            });
            if (i < 0) yield break;

            for (int k = i - 1; k >= 0; k--)
            {
                string t = radky[k].Trim();
                if (!t.StartsWith("#")) yield break;          // konec souvisleho bloku
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(t, @"(\d+(?:[.,]\d+)?)\s*m/s"))
                {
                    if (double.TryParse(m.Groups[1].Value.Replace(',', '.'),
                                        System.Globalization.NumberStyles.Float,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out double d) && d > 0)
                        yield return d;
                }
            }
        }
    }
}
