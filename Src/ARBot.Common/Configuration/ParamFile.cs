using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ARBot.Common.Configuration
{
    /// <summary>Vada v profilu nebo v zadane konfiguraci - hlasi se tak, aby sla opravit
    /// (u souboru vcetne cisla radku).</summary>
    public sealed class ParamFileException : Exception
    {
        public ParamFileException(string message) : base(message) { }
    }

    /// <summary>
    /// Cteni a zapis profilu ve tvaru <c>klic=hodnota</c>, radek na klic, <c>#</c> uvozuje
    /// komentar.
    ///
    /// <para>Je to zamerne PRESNE to, co by se jinak napsalo na prikazovou radku, jen po radcich -
    /// jedna semantika, zadne mapovani, edituje se v nano pres SSH a diff v gitu je citelny.
    /// Viz doc/configuration.md.</para>
    /// </summary>
    public static class ParamFile
    {
        /// <summary>
        /// Rozebere radky profilu. Poradi zachovava (kvuli hlaskam a kvuli tomu, ze pozdejsi klic
        /// by jinak nesel dohledat). Duplicitni klic je CHYBA, ne tiche prepsani - v souboru,
        /// ktery clovek edituje rucne, je to skoro jiste omyl.
        /// </summary>
        public static List<KeyValuePair<string, string>> Parse(IEnumerable<string> lines)
        {
            var result = new List<KeyValuePair<string, string>>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int lineNo = 0;

            foreach (var rawLine in lines)
            {
                lineNo++;
                string line = (rawLine ?? string.Empty).TrimEnd('\r').Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int eq = line.IndexOf('=');
                if (eq < 0)
                    throw new ParamFileException(
                        $"Radek {lineNo}: '{line}' neni ve tvaru klic=hodnota.");

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                if (key.Length == 0)
                    throw new ParamFileException($"Radek {lineNo}: prazdny klic.");
                if (!seen.Add(key))
                    throw new ParamFileException(
                        $"Radek {lineNo}: klic '{key}' je v profilu uz podruhe.");

                result.Add(new KeyValuePair<string, string>(key, value));
            }

            return result;
        }

        /// <summary>Precte profil ze souboru. Chybejici soubor je chyba - viz doc/configuration.md.</summary>
        public static List<KeyValuePair<string, string>> Read(string path)
        {
            if (!File.Exists(path))
                throw new ParamFileException($"Konfiguracni soubor '{path}' neexistuje.");
            return Parse(File.ReadAllLines(path));
        }

        /// <summary>
        /// Slozi obsah profilu: poradi a nadpisy kategorii bere z <see cref="ParamRegistry"/> a ke
        /// kazdemu klici pise popis jako komentar. Profil je tim sam o sobe dokumentaci parametru -
        /// pulka objevitelnosti tak funguje i bez panelu.
        ///
        /// <para>Klice, ktere registr nezna, se pripoji na konec bez komentare. Nemelo by nastat
        /// (do profilu se zapisuji jen registrovane), ale zapis kvuli tomu nesmi spadnout.</para>
        /// </summary>
        public static string Format(IReadOnlyDictionary<string, string> values)
        {
            var sb = new StringBuilder();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string category = null;

            foreach (var def in ParamRegistry.All)
            {
                if (!values.TryGetValue(def.Name, out string value))
                    continue;

                if (!string.Equals(category, def.Category, StringComparison.Ordinal))
                {
                    category = def.Category;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append("# --- ").Append(category).Append(" ---\n");
                }

                if (!string.IsNullOrWhiteSpace(def.Description))
                    sb.Append("# ").Append(def.Description).Append('\n');
                sb.Append(def.Name).Append('=').Append(value).Append('\n');
                written.Add(def.Name);
            }

            foreach (var pair in values)
            {
                if (written.Contains(pair.Key)) continue;
                sb.Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
            }

            return sb.ToString();
        }
    }
}
