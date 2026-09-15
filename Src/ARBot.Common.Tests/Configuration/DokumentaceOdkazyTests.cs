using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Hlídá, že <b>odkazy v živé dokumentaci vedou na existující soubory</b>.
    ///
    /// <para><b>Nač to je.</b> Dokumentace projektu je rozsáhlá (~40 k řádků) a odkazuje si do
    /// kódu. Když se soubor přesune, odkazy zůstanou — a protože je nic nekontroluje, tiše
    /// shnijí. Přesně to se stalo 4. 9. 2026, kdy se runtime přestěhoval do vlastního projektu
    /// (<c>Src/ARBot</c> → <c>Src/ARBot.Runtime</c>): mrtvé odkazy na <c>ARBotRuntime.cs</c>
    /// zůstaly ve <b>čtyřech doménových dokumentech</b> a našel je až externí audit
    /// 15. 9. 2026, tedy o jedenáct dní později.</para>
    ///
    /// <para><b>Co se nehlídá a proč.</b> <c>devlog.md</c> a <c>plan-*.md</c> jsou <b>záznam
    /// historie</b>, ne živá reference — cesta zapsaná pod starým datem je pro to datum správná
    /// a přepisovat ji by znamenalo přepisovat záznam. Je to tatáž zásada, kterou má devlog
    /// v hlavičce u obrázků. Externí odkazy (<c>http</c>) se nekontrolují: test nesmí chodit
    /// na síť.</para>
    /// </summary>
    public class DokumentaceOdkazyTests
    {
        /// <summary>Odkaz ve tvaru <c>[text](cíl)</c>; kotvy a externí adresy řeší volající.</summary>
        private static readonly Regex Odkaz = new Regex(@"\]\(([^)\s]+)\)", RegexOptions.Compiled);

        private static bool JeHistorie(string jmeno)
            => jmeno.Equals("devlog.md", StringComparison.OrdinalIgnoreCase)
            || jmeno.StartsWith("plan-", StringComparison.OrdinalIgnoreCase);

        private static IEnumerable<string> ZiveDokumenty(string koren)
        {
            foreach (string kandidat in new[] { "CLAUDE.md", "README.md", "THIRD-PARTY-NOTICES.md" })
            {
                string p = Path.Combine(koren, kandidat);
                if (File.Exists(p)) yield return p;
            }

            foreach (string dir in new[] { "doc", "deploy" })
            {
                string d = Path.Combine(koren, dir);
                if (!Directory.Exists(d)) continue;
                foreach (string f in Directory.EnumerateFiles(d, "*.md"))
                    if (!JeHistorie(Path.GetFileName(f)))
                        yield return f;
            }
        }

        [Test]
        public void OdkazyVZiveDokumentaci_VedouNaExistujiciSoubory()
        {
            string koren = RepoPaths.RootOrBase();
            var dokumenty = ZiveDokumenty(koren).ToList();
            if (dokumenty.Count == 0)
                Assert.Ignore("Bezi bez repa (nasazeni na zarizeni) - neni co skenovat.");

            var vady = new List<string>();
            foreach (string f in dokumenty)
            {
                string dir = Path.GetDirectoryName(f);
                foreach (Match m in Odkaz.Matches(File.ReadAllText(f)))
                {
                    string cil = m.Groups[1].Value;

                    if (cil.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                     || cil.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                     || cil.StartsWith("#"))
                        continue;

                    // Kotva (#sekce) a odkaz na radek (:123) nejsou soucast cesty.
                    string cesta = cil.Split('#')[0];
                    cesta = Regex.Replace(cesta, @":\d+$", string.Empty);
                    if (cesta.Length == 0) continue;

                    string plna = Path.GetFullPath(Path.Combine(dir, cesta));
                    if (!File.Exists(plna) && !Directory.Exists(plna))
                        vady.Add($"{Path.GetFileName(f)} -> {cil}");
                }
            }

            Assert.That(vady, Is.Empty,
                "Odkaz v zive dokumentaci vede na neexistujici soubor (typicky po presunu v repu). "
                + "Nalezeno: " + string.Join(", ", vady));
        }
    }
}
