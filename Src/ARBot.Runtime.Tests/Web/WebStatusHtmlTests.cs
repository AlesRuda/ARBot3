using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Robot.Web;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// <b>Kontrola stránky náhledu jako celku</b> — že v ní ovládání skutečně je a že skript
    /// nesahá na prvky, které na stránce nejsou.
    ///
    /// <para><b>Nač to je (6. 9. 2026).</b> Autor hlásil, že v náhledu není tlačítko
    /// <i>Power off</i>, ačkoli bylo nasazené a <c>/status.json</c> hlásil <c>poweroff:true</c>.
    /// Příčina byla jinde (viz <c>StrankaNeseVerzi…</c>), ale hledání ukázalo, že celý ten řetěz —
    /// HTML tlačítko, jeho <c>id</c>, obsluha ve skriptu, příznak v JSONu — <b>nedržel pohromadě
    /// nic než pozornost</b>. Přitom jedno chybějící <c>id</c> shodí <c>getElementById(...).style</c>
    /// výjimkou, ta ukončí celou obsluhu odpovědi a stránka pak <b>tiše ukazuje stará čísla</b>.
    /// Táž třída poruchy, jakou kód už jednou zaznamenal u ztraceného escapu (SyntaxError, po němž
    /// stránka zůstala na „spojuji se…“).</para>
    /// </summary>
    public class WebStatusHtmlTests
    {
        private static string Html() => new WebStatus().ToHtml();

        [Test]
        public void Stranka_MaTlacitkoPowerOff()
        {
            string h = Html();
            Assert.That(h, Does.Contain("id=\"vypnout\""), "tlacitko Power off zmizelo ze stranky");
            Assert.That(h, Does.Contain("Power off"));
            Assert.That(h, Does.Contain("/poweroff"), "obsluha tlacitka nikam neposila");
        }

        [Test]
        public void Stranka_MaTlacitkoTerminateAVirtualniStop()
        {
            string h = Html();
            Assert.That(h, Does.Contain("Terminate"));
            Assert.That(h, Does.Contain("id=\"vstop\""));
        }

        [Test]
        public void Stranka_NeseVerziBinarky_AbyPoznalaZeJeStara()
        {
            // Nahled je jednostrankova aplikace: nacte se jednou a dal uz jen dotazuje stav.
            // Po nasazeni tedy v otevrene zalozce bezi stara stranka, zatimco hlavicka ukazuje
            // novou verzi (ta se cte ze stavu) - a chybejici tlacitka vypadaji jako nenasazena
            // funkce. Presne to se 6. 9. 2026 stalo s Power off.
            string h = Html();
            Assert.That(h, Does.Not.Contain("@VERZE_STRANKY@"),
                        "zastupny znak zustal nenahrazeny - stranka by verzi neznala");
            Assert.That(h, Does.Contain("VERZE_STRANKY="), "stranka si verzi nepamatuje");
            Assert.That(h, Does.Contain("location.reload()"), "stranka se sama neprenacte");
            Assert.That(h, Does.Contain("sessionStorage"),
                        "prenacteni neni pojistene proti zacykleni");
            Assert.That(h, Does.Contain("id=\"stara\""), "chybi viditelne varovani o stare strance");
        }

        /// <summary>
        /// <b>Každé <c>getElementById('x')</c> musí mít na stránce <c>id="x"</c>.</b> Jedno
        /// chybějící <c>id</c> shodí obsluhu odpovědi výjimkou a stránka pak tiše zamrzne
        /// na starých hodnotách — bez jakékoli stopy pro člověka u robota.
        /// </summary>
        [Test]
        public void Skript_SahaJenNaPrvky_KtereNaStranceJsou()
        {
            string h = Html();

            var idVHtml = new HashSet<string>(
                Regex.Matches(h, "id=\"([^\"]+)\"").Cast<Match>().Select(m => m.Groups[1].Value));

            var chybi = Regex.Matches(h, @"getElementById\('([^']+)'\)")
                             .Cast<Match>()
                             .Select(m => m.Groups[1].Value)
                             .Distinct()
                             .Where(id => !idVHtml.Contains(id))
                             .ToList();

            Assert.That(chybi, Is.Empty,
                        "skript saha na prvky, ktere na strance nejsou: " + string.Join(", ", chybi));
        }

        /// <summary>
        /// <b>Každé <c>onclick="f()"</c> musí mít ve skriptu <c>function f(</c>.</b> Táž třída
        /// poruchy z druhé strany: tlačítko je vidět, ale po stisku nedělá nic a v konzoli,
        /// do které se u robota nikdo nedívá, je <c>ReferenceError</c>.
        /// </summary>
        [Test]
        public void Tlacitka_VolajiJenFunkce_KtereExistuji()
        {
            string h = Html();

            var chybi = Regex.Matches(h, @"onclick=""([A-Za-z_][A-Za-z0-9_]*)\(")
                             .Cast<Match>()
                             .Select(m => m.Groups[1].Value)
                             .Distinct()
                             .Where(f => !Regex.IsMatch(h, @"function\s+" + Regex.Escape(f) + @"\s*\("))
                             .ToList();

            Assert.That(chybi, Is.Empty,
                        "tlacitka volaji nedefinovane funkce: " + string.Join(", ", chybi));
        }
    }
}
