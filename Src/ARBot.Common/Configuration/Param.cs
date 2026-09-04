using System;
using System.Globalization;

namespace ARBot.Common.Configuration
{
    /// <summary>
    /// Typovany odkaz na jeden konfiguracni parametr - to, co drzi staticka pole
    /// <see cref="ParamRegistry"/> (<c>ParamRegistry.NoUart.Value</c>). Hodnotu bere z
    /// <see cref="ParamStore.Current"/>, takze precedence default -&gt; profil -&gt; prikazova radka
    /// zustava tam, kde je.
    ///
    /// <para><b>Proc odkazy a ne <c>Program.GetParam("no_uart", false)</c></b> (do 4. 9. 2026):
    /// spatny klic se nepreloži, misto cteni je jedno <i>Find references</i> a ne grep na retezec,
    /// a default je definovany PRESNE JEDNOU - v <see cref="ParamDef"/>, nikdy u volani. Straz nad
    /// shodou literalu se registrem (regex nad zdrojaky, behova kontrola dvojiho defaultu) tim
    /// prestala byt potreba. Viz doc/configuration.md a doc/decisions.md 4. 9. 2026.</para>
    ///
    /// <para><b>Konvence:</b> parametry se ctou jen v mistech skladani (<c>Program</c>,
    /// <c>ARBotRuntime</c>, <c>ARBotHW</c>, view modely). Domenove tridy dostavaji hodnoty pres
    /// konfiguracni objekty - odkazy jsou v <c>Common</c>, aby je videl registr i testy, ne aby si
    /// je domena cetla sama.</para>
    /// </summary>
    public abstract class Param
    {
        /// <summary>Popis parametru v registru (klic, typ, default, popis, kategorie, validace).</summary>
        public ParamDef Def { get; }

        protected Param(ParamDef def)
        {
            Def = def ?? throw new ArgumentNullException(nameof(def));
        }

        /// <summary>Klic, jak se pise do profilu i na prikazovou radku.</summary>
        public string Name => Def.Name;

        /// <summary>
        /// Surova ucinna hodnota (text): z prikazove radky, profilu, nebo default z registru.
        /// <c>null</c> jen u parametru, ktere default nemaji a nikdo je nezadal.
        /// </summary>
        public string Raw => ParamStore.Current.Get(Def.Name);

        /// <summary>Odkud ucinna hodnota pochazi.</summary>
        public ParamOrigin Origin => ParamStore.Current.OriginOf(Def.Name);

        /// <summary>Zadano v profilu nebo na prikazove radce (ne default z registru).</summary>
        public bool IsSet => Origin != ParamOrigin.Default;

        public override string ToString() => $"{Def.Name}={Raw ?? "(nenastaveno)"} ({Origin})";
    }

    /// <summary>Parametr true/false. Default je v registru vzdy, takze <see cref="Value"/> existuje vzdy.</summary>
    public sealed class BoolParam : Param
    {
        public BoolParam(ParamDef def) : base(def) { }

        public bool Value => bool.TryParse((Raw ?? string.Empty).Trim(), out bool v) && v;
    }

    /// <summary>Ciselny parametr (InvariantCulture). Default je v registru vzdy.</summary>
    public sealed class DoubleParam : Param
    {
        public DoubleParam(ParamDef def) : base(def) { }

        /// <summary>Ucinna hodnota. Registr default i zadanou hodnotu validuje, takze rozbor nemuze selhat -
        /// kdyby preci (default null), je to chyba deklarace a ma se ozvat, ne tise vratit nulu.</summary>
        public double Value
        {
            get
            {
                string raw = Raw;
                if (double.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Float,
                                    CultureInfo.InvariantCulture, out double v))
                    return v;
                throw new InvalidOperationException(
                    $"Parametr '{Def.Name}' nema ciselnou hodnotu ('{raw}') - chybi default v registru?");
            }
        }
    }

    /// <summary>
    /// Textovy parametr - vcetne vyctu (<c>mission</c>) a slozenych hodnot (<c>wheelslip=1,1</c>),
    /// ktere si rozebira ctenar tymz kodem, jakym je validoval registr (<see cref="ParamParsers"/>).
    /// </summary>
    public sealed class StringParam : Param
    {
        public StringParam(ParamDef def) : base(def) { }

        /// <summary>Ucinna hodnota; <c>null</c>, kdyz neni default ani zadani.</summary>
        public string Value => Raw;

        /// <summary>Prazdne nebo nezadane.</summary>
        public bool IsEmpty => string.IsNullOrWhiteSpace(Raw);

        /// <summary>Porovnani s polozkou vyctu bez ohledu na velikost pismen a okolni mezery.</summary>
        public bool Is(string value)
            => string.Equals((Raw ?? string.Empty).Trim(), value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cesta k souboru nebo slozce: relativni se resi proti koreni repa (<see cref="RepoPaths.Resolve"/>),
    /// absolutni se necha. <c>null</c>, kdyz neni zadana.
    /// </summary>
    public sealed class PathParam : Param
    {
        public PathParam(ParamDef def) : base(def) { }

        public string Value => RepoPaths.Resolve(Raw);
    }
}
