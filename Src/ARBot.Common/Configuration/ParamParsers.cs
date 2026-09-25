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
        /// Podlaha sigmy kurzu z kompasu ve <b>STUPNICH</b>: bud presne 0 (vypnuto), nebo
        /// 0,1 az 45.
        ///
        /// <para><b>Ta dira mezi 0 a 0,1 je pojistka proti zadani v RADIANECH.</b> Konfigurace
        /// fuze je uvnitr cela v radianech a tohle je jedno z mala mist, kde se na okraji prevadi;
        /// rozumna podlaha je v radianech 0,02-0,8, tedy <c>imuheadingstd=0.087</c> (mysleno
        /// radiany) by tise nastavilo 0,087 <b>stupne</b> — a to je jeste min nez <c>YprU</c>
        /// samotne (0,06), takze by se podlaha fakticky vypla a nikdo by si toho nevsiml.
        /// Radeji chyba pri startu, stejne jako u portu nahledu.</para>
        ///
        /// <para>Horni mez 45 stupnu je zdravy rozum: takova sigma uz znamena, ze kompas nenese
        /// informaci, a chtit ji je spis preklep.</para>
        /// </summary>
        public static ParamParseResult ImuHeadingStd(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return ParamParseResult.Invalid("cekam cislo");
            if (v == 0) return ParamParseResult.Valid();
            if (v > 0 && v < 0.1)
                return ParamParseResult.Invalid(
                    "sigma kurzu se zadava ve STUPNICH a " + text + " je min nez samotne YprU "
                    + "senzoru (0,06 deg) - nezadavas to omylem v radianech? Pouzij 0 pro vypnuti.");
            return v >= 0.1 && v <= 45
                ? ParamParseResult.Valid()
                : ParamParseResult.Invalid("cekam sigmu kurzu ve STUPNICH: 0 (vypnuto), nebo 0,1 az 45");
        }

        /// <summary>
        /// Kadence absolutniho kurzu z kompasu [Hz]: 0 (neomezeno) az 200.
        ///
        /// <para>Horni mez je kadence samotneho VN100 (100 Hz) s rezervou — zadat vic nedava smysl,
        /// vic vzorku nez senzor posila stejne nevznikne. Zaporna hodnota ani NaN neprojdou.</para>
        /// </summary>
        public static ParamParseResult ImuHeadingHz(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 200 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam kadenci v Hz: 0 (neomezeno) az 200");

        /// <summary>
        /// Prirazek k sigme pricne polohy koridoru [m]: 0 (vypnuto) az 10.
        /// <para>Strop 10 m je nad sirkou jakekoli cesty, kterou koridor umi zmerit
        /// (<c>CorridorConfig.MaxWidthM</c> = 8 m) - vetsi cislo uz neni odtlumeni, je to preklep.</para>
        /// </summary>
        public static ParamParseResult CorridorStd(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 10 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam prirazek k sigme v METRECH: 0 (vypnuto) az 10");

        /// <summary>
        /// Prirazek k sigme kurzu z koridoru ve STUPNICH: 0 (vypnuto) az 180.
        /// <para>Stejna past jako u <see cref="ImuHeadingStd"/>: velmi mala kladna hodnota je
        /// skoro jiste radian zadany omylem.</para>
        /// </summary>
        public static ParamParseResult CorridorHeadingStd(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return ParamParseResult.Invalid("cekam cislo");
            if (v == 0) return ParamParseResult.Valid();
            if (v > 0 && v < 0.1)
                return ParamParseResult.Invalid(
                    "sigma kurzu se zadava ve STUPNICH a " + text + " je min nez podlaha sigmy "
                    + "koridoru (0,5 deg) - nezadavas to omylem v radianech? Pouzij 0 pro vypnuti.");
            return v >= 0.1 && v <= 180
                ? ParamParseResult.Valid()
                : ParamParseResult.Invalid("cekam sigmu kurzu ve STUPNICH: 0 (vypnuto), nebo 0,1 az 180");
        }

        /// <summary>Kadence merenii z koridoru do fuze [Hz]: 0 (neomezeno) az 60.</summary>
        public static ParamParseResult CorridorHz(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 60 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam kadenci v Hz: 0 (neomezeno) az 60");

        /// <summary>
        /// Rychlostni limit pricne korekce z koridoru [m/s]: 0 (vypnuto) az 10.
        /// <para>Strop 10 m/s je desetinasobek rychlosti robotu - vetsi cislo uz neni limit,
        /// je to preklep.</para>
        /// </summary>
        public static ParamParseResult CorridorSlew(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 10 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam rychlostni limit v m/s: 0 (vypnuto) az 10");

        /// <summary>
        /// Rychlostni limit korekce kurzu z koridoru ve STUPNICH za sekundu: 0 (vypnuto) az 360.
        /// <para>Stejna past jako u <see cref="CorridorHeadingStd"/>: velmi mala kladna hodnota je
        /// skoro jiste radian zadany omylem.</para>
        /// </summary>
        public static ParamParseResult CorridorHeadingSlew(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return ParamParseResult.Invalid("cekam cislo");
            if (v == 0) return ParamParseResult.Valid();
            if (v > 0 && v < 0.1)
                return ParamParseResult.Invalid(
                    "limit kurzu se zadava ve STUPNICH za sekundu a " + text + " je pod 0,1 - "
                    + "nezadavas to omylem v radianech? Pouzij 0 pro vypnuti.");
            return v >= 0.1 && v <= 360
                ? ParamParseResult.Valid()
                : ParamParseResult.Invalid("cekam limit kurzu ve STUPNICH za sekundu: 0 (vypnuto), nebo 0,1 az 360");
        }

        /// <summary>Pocet kandidatnich hran pri prirazeni koridoru: 1 az 16.</summary>
        public static ParamParseResult AssocK(string text)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
               && v >= 1 && v <= 16
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam pocet kandidatnich hran: 1 az 16");

        /// <summary>
        /// Veto na azimut pri prirazeni hrany ve STUPNICH: 1 az 90.
        /// <para>Nad 90 to nema smysl — primka nema orientaci, takze vetsi rozdil dvou smeru
        /// neexistuje.</para>
        /// </summary>
        public static ParamParseResult AssocVeto(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 1 && v <= 90
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam veto na azimut ve STUPNICH: 1 az 90 "
                                          + "(nad 90 nema smysl, primka nema orientaci)");

        /// <summary>Podlaha sigmy pricne polohy pri prirazeni [m]: 0 az 50.</summary>
        public static ParamParseResult AssocFloorLat(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 50 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam podlahu sigmy v METRECH: 0 (bez podlahy) az 50");

        /// <summary>
        /// Podlaha sigmy kurzu pri prirazeni ve STUPNICH: 0 az 90.
        /// <para>Tataz past jako u <see cref="ImuHeadingStd"/>: velmi mala kladna hodnota je
        /// skoro jiste radian zadany omylem.</para>
        /// </summary>
        public static ParamParseResult AssocFloorHdg(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                return ParamParseResult.Invalid("cekam cislo");
            if (v == 0) return ParamParseResult.Valid();
            if (v > 0 && v < 0.1)
                return ParamParseResult.Invalid(
                    "podlaha sigmy kurzu se zadava ve STUPNICH a " + text
                    + " je podezrele male - nezadavas to omylem v radianech? Pouzij 0 pro vypnuti.");
            return v >= 0.1 && v <= 90
                ? ParamParseResult.Valid()
                : ParamParseResult.Invalid("cekam podlahu sigmy kurzu ve STUPNICH: 0 (bez podlahy), nebo 0,1 az 90");
        }

        /// <summary>Strop / odstup chi-kvadratu pri prirazeni hrany: 0 az 1000.</summary>
        public static ParamParseResult AssocChi2(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 1000 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam hodnotu chi-kvadratu: 0 az 1000 "
                                          + "(pro 2 stupne volnosti je 5,99 = 95 %, 9,21 = 99 %)");

        /// <summary>
        /// Nejmensi pocet inlieru RANSACu, aby hranice platila: 3 az 500.
        ///
        /// <para>Spodni mez je 3 zamerne: dvema body jde primku prolozit vzdy, takze
        /// <c>MinInliers = 2</c> by branu fakticky vypnulo a do statistiky by se dostaly prave ty
        /// primky kolme na cestu, proti kterym prah vznikl.</para>
        /// </summary>
        public static ParamParseResult CorridorMinInliers(string text)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
               && v >= 3 && v <= 500
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam pocet inlieru: cele cislo 3 az 500 "
                                          + "(vychozi 25; dve primku prolozi vzdy, proto ne min nez 3)");

        /// <summary>
        /// Nejistota mapove sirky pro pricnou polohu z jedne hrany [m]: 0 az 5. Nula je povolena
        /// (verit mapove sirce presne), ale je to vedome rozhodnuti - u cest bez tagu width je
        /// mapova sirka jen roadwidth=.
        /// </summary>
        public static ParamParseResult CorridorSingleWidthStd(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 5 && !double.IsNaN(v)
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam nejistotu sirky v metrech: 0 az 5 (vychozi 1)");

        /// <summary>Snimkove frekvence, ktere D435 zna (jina pipeline vubec nenastartuje).</summary>
        public static readonly int[] CameraFpsHodnoty = { 6, 15, 30, 60 };

        /// <summary>
        /// Snimkova frekvence kamer: jen hodnota, kterou D435 umi.
        ///
        /// <para>Volny rozsah by tu byl past: RealSense pipeline na neznamou frekvenci
        /// <b>nenastartuje</b> a projevi se to jako "kamera se nepripojila" - tedy zdanliva
        /// porucha hardwaru misto preklepu v profilu. Radeji chyba pri startu, stejne jako
        /// u portu nahledu.</para>
        /// </summary>
        public static ParamParseResult CameraFps(string text)
            => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)
               && Array.IndexOf(CameraFpsHodnoty, v) >= 0
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam snimkovou frekvenci, kterou D435 zna: "
                                          + string.Join(", ", CameraFpsHodnoty));

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
            "sensors", "images", "robot", "profile", "world", "telemetry", "debug", "virtual", "robotour", "config", "perf",
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
