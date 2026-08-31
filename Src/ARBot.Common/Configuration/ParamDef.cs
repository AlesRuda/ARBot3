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

        /// <summary>
        /// Vychozi hodnotu urcuje az kod za behu, takze ji registr nezna. Priklad:
        /// <c>UartAHRS</c> ma default z detekce portu. U takovych parametru se v panelu misto
        /// hodnoty ukaze <see cref="DefaultDescription"/>, do profilu se nezapisuji, dokud je
        /// nekdo vyslovne nenastavi, a kontrola shody defaultu se preskoci.
        /// </summary>
        public bool DefaultFromCode;

        /// <summary>Cim je default urcen, kdyz <see cref="DefaultFromCode"/> - napr.
        /// "podle detekce portu". Jen pro zobrazeni.</summary>
        public string DefaultDescription;

        /// <summary>Veta do panelu i do komentare v profilu. Povinna.</summary>
        public string Description;

        /// <summary>Kategorie pro razeni a nadpisy ("Fuze a lokalizace", "Mise", ...). Povinna.</summary>
        public string Category;

        /// <summary>Projde hodnota validaci pro <see cref="Type"/>?</summary>
        public bool IsValidValue(string raw)
        {
            if (raw == null) return false;
            switch (Type)
            {
                case ParamType.Bool:
                    return bool.TryParse(raw.Trim(), out _);
                case ParamType.Double:
                    return double.TryParse(raw.Trim(), NumberStyles.Float,
                                           CultureInfo.InvariantCulture, out _);
                default:
                    return true;    // String i Path prijmou cokoliv vcetne prazdneho
            }
        }
    }
}
