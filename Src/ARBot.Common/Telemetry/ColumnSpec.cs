using System;
using ARBot.Common.Logs;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Popis jednoho sloupce telemetricke tabulky: ze ktere zpravy se hodnota bere a jak se
    /// zobrazi. Samotny <b>registr</b> sloupcu zije v UI vrstve (jednotky, format a "co ma smysl
    /// kreslit" jsou prezentacni vec, ktera nepatri do domeny) - viz doc/telemetry-view.md.
    /// </summary>
    public sealed class ColumnSpec
    {
        /// <summary>Typ zpravy, ze ktere se hodnota bere (<see cref="Message.MsgName"/>).
        /// Zaroven urcuje, ktere zpravy vubec stoji za precteni ze zaznamu.</summary>
        public string MsgName;

        /// <summary>Volitelne i konkretni instance (<see cref="INamedMessage.Name"/>) - napr. leva
        /// vs. prava kamera. null/prazdne = kterakoli instance daneho typu.</summary>
        public string Name;

        /// <summary>Zahlavi sloupce vcetne jednotky, napr. <c>"v [m/s]"</c>.</summary>
        public string Header;

        /// <summary>Textove vysvetleni udaje (co to je, odkud se bere, jak to cist). Zobrazuje se
        /// jako tooltip na zahlavi sloupce v tabulce a na radku v detailu - zahlavi je zkratka
        /// (musi se vejit do sloupce), takze vyznam patri sem.</summary>
        public string Description;

        /// <summary>Format cisla pro zobrazeni; uplatni se, kdyz neni zadany <see cref="Text"/>.</summary>
        public string Format = "F2";

        /// <summary>Smi tento sloupec do grafu? (Faze 2 - viz doc/telemetry-view.md.)</summary>
        public bool Graphable = true;

        /// <summary>
        /// Druh uhlove veliciny. Ulozena hodnota je vzdy MATEMATICKA (kurz 0 = vychod a +CCW,
        /// kladna rychlost = doleva); podle tohoto priznaku ji zobrazeni umi prepocitat do svetove
        /// konvence. Viz <see cref="AnglePresentation"/> a doc/telemetry-view.md.
        /// </summary>
        public AngleKind Angle = AngleKind.None;

        /// <summary>Hodnota ze zpravy; <c>null</c> = tato zprava tento sloupec neplni.</summary>
        public Func<Message, double?> Value;

        /// <summary>Volitelny prevod cisla na text (vycet → jmeno, logicka hodnota → "STOP").
        /// Ma prednost pred <see cref="Format"/>. Uvnitr je hodnota vzdy cislo, takze i stav
        /// jde vykreslit do grafu jako schod.</summary>
        public Func<double, string> Text;
    }
}
