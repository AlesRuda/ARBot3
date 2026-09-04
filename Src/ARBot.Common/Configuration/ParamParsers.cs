using System;
using System.Globalization;

namespace ARBot.Common.Configuration
{
    /// <summary>Vysledek rozboru hodnoty parametru: bud v poradku, nebo s duvodem, proc ne.</summary>
    public readonly struct ParamParseResult
    {
        public bool Ok { get; }

        /// <summary>Duvod odmitnuti - vypise ho panel i hlaska pri startu. Prazdny, kdyz <see cref="Ok"/>.</summary>
        public string Error { get; }

        private ParamParseResult(bool ok, string error) { Ok = ok; Error = error; }

        public static ParamParseResult Valid() => new ParamParseResult(true, null);
        public static ParamParseResult Invalid(string error) => new ParamParseResult(false, error);
    }

    /// <summary>
    /// Rozbor slozenych hodnot parametru (dvojice cisel, zemepisna poloha).
    ///
    /// <para><b>Proc to bydli tady, a ne u volajiciho.</b> Tentyz kod pouziva registr pri validaci
    /// (panel i start aplikace) I runtime pri skutecnem cteni hodnoty. Kdyby to byla dve mista,
    /// mohl by panel prijmout hodnotu, kterou runtime zahodi - presne ta past, kvuli ktere je
    /// <see cref="ParamRegistry.Validate"/> jedine misto pravidel. Viz doc/configuration.md.</para>
    /// </summary>
    public static class ParamParsers
    {
        /// <summary>
        /// Dvojice cisel oddelenych carkou (<c>"1.5,2"</c>). Dalsi casti se ignoruji - tak se to
        /// chovalo od zacatku a nektere parametry toho vyuzivaji.
        /// </summary>
        public static bool TryPair(string text, out double a, out double b)
        {
            a = 0; b = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(',');
            return parts.Length >= 2
                   && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out a)
                   && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out b);
        }

        /// <summary>
        /// Zemepisna poloha <c>lat,lon</c> ve stupnich, volitelne s kurzem <c>,kurzDeg</c>.
        /// </summary>
        public static bool TryLatLonHeading(string text, out double latDeg, out double lonDeg,
                                            out double? headingDeg)
        {
            latDeg = 0; lonDeg = 0; headingDeg = null;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split(',');
            if (parts.Length < 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latDeg)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lonDeg))
                return false;

            if (parts.Length >= 3
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double hdg))
                headingDeg = hdg;
            return true;
        }

        // --- Hotove validatory pro ParamDef.Parse -------------------------------------------

        /// <summary>Dvojice cisel s volitelnymi mezemi; <paramref name="tvar"/> je do hlasky.</summary>
        public static Func<string, ParamParseResult> Pair(string tvar,
                                                          double minA = double.NegativeInfinity,
                                                          double minB = double.NegativeInfinity,
                                                          bool aStrict = false, bool bStrict = false)
        {
            return text =>
            {
                if (!TryPair(text, out double a, out double b))
                    return ParamParseResult.Invalid($"cekam dve cisla oddelena carkou ({tvar})");

                if (aStrict ? !(a > minA) : a < minA)
                    return ParamParseResult.Invalid(
                        $"prvni cislo musi byt {(aStrict ? "vetsi nez" : "aspon")} "
                        + minA.ToString(CultureInfo.InvariantCulture) + $" ({tvar})");
                if (bStrict ? !(b > minB) : b < minB)
                    return ParamParseResult.Invalid(
                        $"druhe cislo musi byt {(bStrict ? "vetsi nez" : "aspon")} "
                        + minB.ToString(CultureInfo.InvariantCulture) + $" ({tvar})");

                return ParamParseResult.Valid();
            };
        }

        /// <summary>Poloha <c>lat,lon[,kurzDeg]</c>.</summary>
        /// <summary>Nezaporne cislo (nula projde, zaporna hodnota ne).</summary>
        public static ParamParseResult Nezaporne(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam nezaporne cislo");

        /// <summary>Kladne cislo (nula ani zaporna hodnota neprojde).</summary>
        public static ParamParseResult Kladne(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v > 0
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam cislo vetsi nez 0");

        /// <summary>
        /// Port weboveho nahledu: 0 (vypnuto) nebo cele cislo 1024-65535.
        ///
        /// <para>Privilegovane porty (pod 1024) odmitame zamerne: proces bezi jako bezny uzivatel,
        /// takze by bind selhal az za behu - a to je presne ten druh chyby, ktery se pak hleda na
        /// souteze. Radeji chyba pri startu. Viz doc/headless.md.</para>
        /// </summary>
        public static ParamParseResult WebPort(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return ParamParseResult.Invalid("cekam cislo");
            if (double.IsNaN(v) || v != Math.Floor(v))
                return ParamParseResult.Invalid("cekam cele cislo");
            if (v == 0)
                return ParamParseResult.Valid();
            if (v < 1024 || v > 65535)
                return ParamParseResult.Invalid("cekam 0 (vypnuto) nebo port 1024-65535");
            return ParamParseResult.Valid();
        }

        public static ParamParseResult LatLon(string text)
            => TryLatLonHeading(text, out _, out _, out _)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam 'lat,lon' ve stupnich, volitelne ',kurzDeg'");

        /// <summary>Poloha jako <see cref="LatLon"/>, nebo slovo <c>gps</c> (pocka na prvni fix).</summary>
        public static ParamParseResult LatLonOrGps(string text)
            => string.Equals(text?.Trim(), "gps", StringComparison.OrdinalIgnoreCase)
               ? ParamParseResult.Valid()
               : TryLatLonHeading(text, out _, out _, out _)
                 ? ParamParseResult.Valid()
                 : ParamParseResult.Invalid("cekam 'lat,lon[,kurzDeg]' ve stupnich, nebo 'gps'");

        /// <summary>Umela chyba pozy <c>vpred,vlevo[,stupne]</c>.</summary>
        public static ParamParseResult PoseError(string text)
            => ARBot.Common.Simulation.VirtualPoseError.TryParse(text, out _)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam 'vpred,vlevo[,stupne]' v metrech a stupnich");

        // --- Pohledy UI (parametr open=) ---------------------------------------------------

        /// <summary>
        /// Jmena pohledu, ktere umi parametr <c>open=</c> otevrit po startu (v poradi menu Tools).
        /// Mapovani jmen na dokumenty/nastroje je v aplikaci (<c>MainWindowViewModel.OpenViews</c>);
        /// seznam bydli tady, aby registr umel hodnotu odmitnout uz pri startu a panel Konfigurace
        /// ji umel napovedet. Pridani pohledu = pridat jmeno sem A vetev do OpenView.
        /// </summary>
        public static readonly string[] ViewNames =
        {
            "sensors", "images", "robot", "world", "telemetry", "debug", "virtual", "robotour", "config", "perf",
        };

        /// <summary>
        /// Seznam pohledu oddelenych carkou (nebo strednikem), napr. <c>"world,telemetry"</c>:
        /// mezery a velikost pismen se ignoruji, duplicity se slouci (zustane prvni vyskyt), prazdny
        /// text je prazdny seznam. Vraci false, kdyz je nektere jmeno nezname;
        /// <paramref name="unknown"/> pak nese to prvni.
        /// </summary>
        public static bool TryViews(string text, out string[] views, out string unknown)
        {
            unknown = null;
            var list = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrWhiteSpace(text))
            {
                foreach (var part in text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string name = part.Trim().ToLowerInvariant();
                    if (name.Length == 0) continue;
                    if (Array.IndexOf(ViewNames, name) < 0)
                    {
                        unknown = part.Trim();
                        views = Array.Empty<string>();
                        return false;
                    }
                    if (!list.Contains(name)) list.Add(name);
                }
            }
            views = list.ToArray();
            return true;
        }

        /// <summary>Validator pro <c>open=</c>: seznam znamych pohledu.</summary>
        public static ParamParseResult Views(string text)
            => TryViews(text, out _, out string unknown)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid($"neznamy pohled '{unknown}'; znam: {string.Join(", ", ViewNames)}");
    }
}
