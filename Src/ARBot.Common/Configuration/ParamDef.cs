using System;
using System.Globalization;

namespace ARBot.Common.Configuration
{
    /// <summary>Typ hodnoty parametru. Urcuje validaci a to, jak se hodnota cte.</summary>
    public enum ParamType
    {
        /// <summary>true / false (case-insensitive).</summary>
        Bool,
        /// <summary>Desetinne cislo v InvariantCulture (tecka, ne carka).</summary>
        Double,
        /// <summary>Libovolny retezec - vcetne slozenych tvaru jako "vpred,vlevo,stupne".</summary>
        String,
        /// <summary>Cesta k souboru nebo slozce; relativni se resi proti koreni repa.</summary>
        Path,
    }

    /// <summary>
    /// Popis jednoho konfiguracniho parametru. Do 31. 8. 2026 zadny takovy popis neexistoval -
    /// klic byl jen string literal na miste cteni, takze neslo vypsat, co lze nastavit, a preklep
    /// tise propadl na vychozi hodnotu. Viz doc/configuration.md.
    /// </summary>
    public sealed class ParamDef
    {
        /// <summary>Jmeno klice, jak se pise na prikazovou radku i do profilu. Porovnava se
        /// case-insensitive (stejne jako dosud v <c>Program.GetParam</c>).</summary>
        public string Name;

        /// <summary>Typ hodnoty - urcuje validaci.</summary>
        public ParamType Type;

        /// <summary>
        /// Vychozi hodnota v TEXTOVE podobe, presne jak by stala v profilu. Textove proto, aby
        /// zapis profilu i vypis v panelu sly jednou cestou a nemohly se rozejit o formatovani
        /// cisla. <c>null</c> znamena "nenastaveno".
        /// </summary>
        public string Default;

        // POZN.: bývalo tu DefaultFromCode + DefaultDescription ("default určuje až kód za běhu,
        // registr ho nezná"). Zrušeno 4. 9. 2026: žádný takový parametr ve skutečnosti nebyl -
        // maxspeed/safedist mají pravdu v Profile a porty UART jsou konstanty podle platformy
        // (teď Profile.PortAHRS…), takže si registr default z kódu PŘEČTE (ParamRegistry.Fmt) a
        // Default je vyplněný u každého parametru, kde nějaký existuje. Viz doc/configuration.md.

        /// <summary>Veta do panelu i do komentare v profilu. Povinna.</summary>
        public string Description;

        /// <summary>Kategorie pro razeni a nadpisy ("Fuze a lokalizace", "Mise", ...). Povinna.</summary>
        public string Category;

        /// <summary>
        /// Uplny vycet povolenych hodnot (napr. <c>mission</c>: none | freerun | robotour), nebo
        /// <c>null</c>, kdyz vycet neexistuje. Porovnava se bez ohledu na velikost pismen.
        ///
        /// <para>Je to silnejsi nez validace vzorem a hlavne to <b>nese informaci pro UI</b> -
        /// panel z toho muze udelat rozbalovaci seznam misto psani.</para>
        ///
        /// <para>⚠️ <b>Vycet musi odpovidat tomu, co kod skutecne zna</b> (u <c>mission</c> je to
        /// <c>switch</c> v <c>ARBotRuntime</c>). Automaticky to nikdo nehlida - kdyz pribude mise,
        /// musi se pridat i sem, jinak ji panel odmitne.</para>
        /// </summary>
        public string[] AllowedValues;

        /// <summary>
        /// Rozbor slozene hodnoty (dvojice cisel, poloha) - vraci i DUVOD odmitnuti, at hlaska
        /// rekne, co se cekalo. <c>null</c> = zadny rozbor nad ramec <see cref="Type"/>.
        ///
        /// <para><b>Musi volat tentyz kod, jaky pouzije runtime pri skutecnem cteni</b> (viz
        /// <see cref="ParamParsers"/>), jinak by panel prijal hodnotu, kterou runtime zahodi.</para>
        ///
        /// <para><b>Dusledek, se kterym se pocita:</b> hodnota, kterou runtime dosud jen zahodil
        /// s hlaskou (<c>wheelslip=-1,0</c>), ted zastavi start. Je to zamer - tise ignorovana
        /// hodnota je tataz past jako preklep v klici.</para>
        /// </summary>
        public Func<string, ParamParseResult> Parse;

        /// <summary>Projde hodnota validaci? Prazdny retezec projde vzdy (znamena "nezadano").</summary>
        public bool IsValidValue(string raw) => Validate(raw).Ok;

        /// <summary>
        /// Prevede hodnotu na tvar zapsany ve <see cref="AllowedValues"/> (jinak ji vrati beze
        /// zmeny).
        ///
        /// <para><b>Proc to je potreba:</b> validace vyctu je case-insensitive, takze
        /// <c>mission=NONE</c> z profilu projde - ale rozbalovaci seznam v panelu porovnava
        /// hodnoty PRESNE, takze by zadnou nevybral, ukazal prazdno a pri ulozeni by se hodnota
        /// ztratila. Kanonizace tu past zaviera.</para>
        /// </summary>
        public string Canonical(string raw)
        {
            if (AllowedValues == null || string.IsNullOrWhiteSpace(raw)) return raw;
            string text = raw.Trim();
            foreach (var v in AllowedValues)
                if (string.Equals(v, text, StringComparison.OrdinalIgnoreCase))
                    return v;
            return raw;
        }

        /// <summary>Jako <see cref="IsValidValue"/>, ale s duvodem odmitnuti.</summary>
        public ParamParseResult Validate(string raw)
        {
            if (raw == null)
                return ParamParseResult.Invalid("hodnota chybi");

            string text = raw.Trim();

            switch (Type)
            {
                case ParamType.Bool:
                    return bool.TryParse(text, out _)
                           ? ParamParseResult.Valid()
                           : ParamParseResult.Invalid("cekam true nebo false");
                case ParamType.Double:
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                        return ParamParseResult.Invalid("cekam cislo (desetinna TECKA, ne carka)");
                    break;
            }

            // Prazdna hodnota je u retezcovych parametru legitimni (qrcamera= znamena VSECHNY
            // kamery), takze se vycet ani rozbor nespousti.
            if (text.Length == 0)
                return ParamParseResult.Valid();

            if (AllowedValues != null)
            {
                foreach (var v in AllowedValues)
                    if (string.Equals(v, text, StringComparison.OrdinalIgnoreCase))
                        return ParamParseResult.Valid();
                return ParamParseResult.Invalid(
                    "cekam jednu z hodnot: " + string.Join(" | ", AllowedValues));
            }

            return Parse != null ? Parse(text) : ParamParseResult.Valid();
        }
    }
}
