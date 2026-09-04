using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARBot.Common.Configuration
{
    /// <summary>Odkud pochazi ucinna hodnota parametru.</summary>
    public enum ParamOrigin
    {
        /// <summary>Vychozi hodnota z registru.</summary>
        Default,
        /// <summary>Z profilu zadaneho pres <c>config=</c>.</summary>
        File,
        /// <summary>Z prikazove radky - prebiji vse.</summary>
        CommandLine,
    }

    /// <summary>
    /// Ucinne hodnoty parametru a jejich puvod. Sklada se jednou pri startu podle poradi
    /// <c>default z registru</c> -&gt; <c>soubor (config=)</c> -&gt; <c>prikazova radka</c>.
    ///
    /// <para><b>Proc prikazova radka prebiji soubor.</b> Jinak by prestalo platit skriptovane A/B
    /// mereni (behy se lisi jednim prepinacem) a vznikla past "proc mi mapcorr=true nic nedela"
    /// by byla ticha. Viz doc/configuration.md.</para>
    /// </summary>
    public sealed class ParamStore
    {
        private readonly Dictionary<string, string> values =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ParamOrigin> origins =
            new Dictionary<string, ParamOrigin>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> warnings = new List<string>();

        /// <summary>Hlasky, ktere nezastavily start (neznamy klic na prikazove radce, nesoulad
        /// defaultu). Volajici je vypise do Trace, at skonci i v zaznamu.</summary>
        public IReadOnlyList<string> Warnings => warnings;

        /// <summary>Cesta k profilu z <c>config=</c>, nebo <c>null</c>.</summary>
        public string ConfigPath { get; private set; }

        /// <summary>
        /// Store platny pro tenhle beh. Do zavolani <see cref="Build"/> je prazdny, takze cteni
        /// pred inicializaci vraci defaulty misto padu.
        /// </summary>
        public static ParamStore Current { get; private set; } = new ParamStore();

        /// <summary>
        /// Slozi store z argumentu prikazove radky. Vyhodi <see cref="ParamFileException"/>, kdyz
        /// je konfigurace vadna - tedy jeste driv, nez se cokoliv zalozi.
        /// </summary>
        public static ParamStore Build(IEnumerable<string> commandLineArgs)
        {
            var store = new ParamStore();
            var cmdline = new List<KeyValuePair<string, string>>();

            // 1) Rozebrat prikazovou radku. Ciziho argumentu (cesta k exe, prepinac Avalonie)
            //    si nevsimame - poznamena se az u neznameho klice ve tvaru klic=hodnota.
            foreach (var arg in commandLineArgs ?? new string[0])
            {
                if (arg == null) continue;
                int eq = arg.IndexOf('=');
                if (eq <= 0) continue;
                cmdline.Add(new KeyValuePair<string, string>(
                    arg.Substring(0, eq).Trim(), arg.Substring(eq + 1).Trim()));
            }

            // 2) config= se cte z prikazove radky driv nez cokoliv jineho.
            foreach (var pair in cmdline)
            {
                if (!string.Equals(pair.Key, "config", StringComparison.OrdinalIgnoreCase))
                    continue;
                store.ConfigPath = RepoPaths.Resolve(pair.Value);
                break;
            }

            // 3) Profil. Neznamy klic i neplatna hodnota jsou CHYBA - v souboru, ktery clovek
            //    edituje rucne, zadne cizi klice byt nemaji a tise propadly preklep je presne to,
            //    cemu registr ma zabranit.
            if (store.ConfigPath != null)
            {
                var dvojice = ParamFile.Read(store.ConfigPath);

                // Vsechny vady naraz, at je clovek nemusi opravovat po jedne a startovat mezi tim.
                var vady = ParamRegistry.Validate(dvojice);
                if (vady.Count > 0)
                    throw new ParamFileException(
                        $"Profil '{store.ConfigPath}': " + string.Join("; ", vady) + ".");

                foreach (var pair in dvojice)
                {
                    ParamRegistry.TryGet(pair.Key, out var def);
                    store.Set(def.Name, pair.Value, ParamOrigin.File);
                }
            }

            // 4) Prikazova radka. Neznamy klic je jen varovani (cizi argumenty), ale NEPLATNA
            //    hodnota u znameho klice je chyba - tise propadnout na default je tataz past.
            foreach (var pair in cmdline)
            {
                if (string.Equals(pair.Key, "config", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!ParamRegistry.TryGet(pair.Key, out var def))
                {
                    store.warnings.Add(
                        $"Prikazova radka: '{pair.Key}' neni znamy parametr -> ignoruje se.");
                    continue;
                }
                // Stejne jako u profilu s DUVODEM z parseru (u vyctu a slozenych hodnot je to jedina
                // cesta, jak se clovek dozvi, ktera jmena/tvar registr zna) - do 4. 9. 2026 se
                // vypisoval jen typ ("neni platna hodnota typu String"), coz u open=world,mapa nic nerekne.
                var overeni = def.Validate(pair.Value);
                if (!overeni.Ok)
                    throw new ParamFileException(
                        $"Prikazova radka: '{pair.Key}={pair.Value}' neni platna hodnota "
                        + $"({overeni.Error}).");
                store.Set(def.Name, pair.Value, ParamOrigin.CommandLine);
            }

            Current = store;
            return store;
        }

        private void Set(string name, string value, ParamOrigin origin)
        {
            values[name] = value;
            origins[name] = origin;
        }

        /// <summary>Surova hodnota, nebo default z registru; <c>null</c>, kdyz neni ani ten.</summary>
        public string Get(string name)
        {
            if (values.TryGetValue(name ?? string.Empty, out string v))
                return v;
            if (ParamRegistry.TryGet(name, out var def))
                return def.Default;
            return null;
        }

        /// <summary>
        /// Ucinna konfigurace radek po radku - hlavicka a pak <c>klic=hodnota  (puvod)</c> pro KAZDY
        /// parametr registru v poradi deklarace. Vypisuje se jednou pri startu do Debug (a tedy do
        /// zaznamu pres Info zpravu). Od 4. 9. 2026 nahrazuje vypis pri kazdem cteni: zaznam tak nese
        /// celou konfiguraci vcetne defaultu, ne jen to, co se nahodou precetlo.
        /// </summary>
        public IEnumerable<string> DescribeAll()
        {
            yield return "Konfigurace (ucinne hodnoty; poradi default -> profil -> prikazova radka):";
            foreach (var def in ParamRegistry.All)
            {
                string value = Get(def.Name) ?? "(nenastaveno)";
                string origin = OriginOf(def.Name) switch
                {
                    ParamOrigin.CommandLine => "prikazova radka",
                    ParamOrigin.File => "profil",
                    _ => "default",
                };
                yield return $"  {def.Name}={value}  ({origin})";
            }
        }

        /// <summary>Odkud pochazi ucinna hodnota.</summary>
        public ParamOrigin OriginOf(string name)
        {
            return origins.TryGetValue(name ?? string.Empty, out var o) ? o : ParamOrigin.Default;
        }

        // Typovane cteni podle klice s fallbackem. Aplikace uz je necte (od 4. 9. 2026 jdou typovane
        // odkazy ParamRegistry.X.Value pres Get), zustavaji pro testy a pro cteni mimo registr.
        // Kontrola shody fallbacku s registrem (CheckDefault) odesla s dvojim zapisem defaultu.

        public bool GetBool(string name, bool fallback)
        {
            string raw = Get(name);
            return bool.TryParse((raw ?? string.Empty).Trim(), out bool v) ? v : fallback;
        }

        public double GetDouble(string name, double fallback)
        {
            string raw = Get(name);
            return double.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        public string GetString(string name, string fallback)
        {
            return Get(name) ?? fallback;
        }

        public string GetPath(string name, string fallback)
        {
            return RepoPaths.Resolve(GetString(name, fallback));
        }

    }
}
