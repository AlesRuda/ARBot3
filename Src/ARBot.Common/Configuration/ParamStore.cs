using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARBot.Common.Configuration
{
    /// <summary>Odkud pochazi ucinna hodnota parametru.</summary>
    public enum ParamOrigin
    {
        /// <summary>Vychozi hodnota z registru (nebo z kodu u DefaultFromCode).</summary>
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
                if (!def.IsValidValue(pair.Value))
                    throw new ParamFileException(
                        $"Prikazova radka: '{pair.Key}={pair.Value}' neni platna hodnota "
                        + $"typu {def.Type}.");
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
            if (ParamRegistry.TryGet(name, out var def) && !def.DefaultFromCode)
                return def.Default;
            return null;
        }

        /// <summary>Odkud pochazi ucinna hodnota.</summary>
        public ParamOrigin OriginOf(string name)
        {
            return origins.TryGetValue(name ?? string.Empty, out var o) ? o : ParamOrigin.Default;
        }

        public bool GetBool(string name, bool fallback)
        {
            CheckDefault(name, fallback ? "true" : "false");
            string raw = Get(name);
            return bool.TryParse((raw ?? string.Empty).Trim(), out bool v) ? v : fallback;
        }

        public double GetDouble(string name, double fallback)
        {
            CheckDefault(name, fallback.ToString(CultureInfo.InvariantCulture));
            string raw = Get(name);
            return double.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Float,
                                   CultureInfo.InvariantCulture, out double v) ? v : fallback;
        }

        public string GetString(string name, string fallback)
        {
            CheckDefault(name, fallback);
            return Get(name) ?? fallback;
        }

        public string GetPath(string name, string fallback)
        {
            return RepoPaths.Resolve(GetString(name, fallback));
        }

        /// <summary>
        /// Default je zapsany dvakrat - v registru a dal i ve volani (GetParamBool("mapcorr",
        /// false)). Neshodu je potreba slyset, jinak by panel ukazoval jinou vychozi hodnotu, nez
        /// jaka realne plati.
        ///
        /// <para><b>Dve vyjimky, bez kterych by to bylo k nepouziti.</b> (1) Volani, ktere default
        /// nepredava vubec - <c>Program.GetParam("mission")</c> posle null a registrovanou hodnotu
        /// "none" nema s cim porovnavat; hlasit to by v Debug buildu znemoznilo start.
        /// (2) <see cref="ParamDef.DefaultFromCode"/> - tam registr default zamerne nezna.</para>
        /// </summary>
        private void CheckDefault(string name, string callerDefault)
        {
            if (callerDefault == null) return;
            if (!ParamRegistry.TryGet(name, out var def)) return;
            if (def.DefaultFromCode) return;
            if (string.Equals(def.Default ?? string.Empty, callerDefault,
                              StringComparison.OrdinalIgnoreCase))
                return;

            string hlaska = $"Parametr '{name}': volani predava vychozi hodnotu "
                            + $"'{callerDefault}', ale registr ma '{def.Default}'.";
            if (!warnings.Contains(hlaska))
                warnings.Add(hlaska);
#if DEBUG
            throw new ParamFileException(hlaska);
#endif
        }
    }
}
