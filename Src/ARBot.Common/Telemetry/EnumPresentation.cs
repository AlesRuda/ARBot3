using System;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Prevod vyctove hodnoty sloupce telemetrie na text. Viz doc/telemetry-view.md.
    ///
    /// <para>Ve sloupci je hodnota vzdy jako <c>double</c> (tabulka drzi cisla), takze se pro
    /// zobrazeni musi vratit zpatky na vycet. Zdanlive trivialni prevod ma jednu past - viz
    /// <see cref="Text{TEnum}"/>.</para>
    /// </summary>
    public static class EnumPresentation
    {
        /// <summary>
        /// Jmeno prvku vyctu pro danou hodnotu; kdyz hodnota do vyctu nepatri, vrati cislo.
        ///
        /// <para><b>Proc pres <c>GetUnderlyingType</c> a ne primo
        /// <c>Enum.IsDefined(typeof(TEnum), raw)</c>:</b> <see cref="Enum.IsDefined(Type, object)"/>
        /// vyzaduje, aby typ predane hodnoty odpovidal PODKLADOVEMU typu vyctu. Predavat vzdy
        /// <c>int</c> proto funguje jen u vyctu se standardnim <c>int</c> podkladem a u <c>: byte</c>
        /// vyctu to spadne na <see cref="ArgumentException"/>. Stejne tak <c>(TEnum)(object)raw</c>
        /// by bylo neplatne rozbaleni. Hodnota se tedy nejdriv prevede na skutecny podkladovy typ -
        /// helper pak snese <c>byte</c>, <c>short</c>, <c>int</c> i <c>long</c>.</para>
        ///
        /// <para>Narazilo se na to 19. 8. 2026: sloupec "korel duvod" nese
        /// <c>MapCorrelationReason</c>, ktery je <c>: byte</c> (aby se do zpravy serializoval na
        /// jeden bajt), a zobrazeni telemetrie na nem padalo. Vsechny starsi vyctove sloupce
        /// (<c>GlobalNavStatus</c>, <c>LocalPlanStatus</c>, <c>GPSState.FixQuality</c>) maji
        /// standardni <c>int</c> podklad, takze tu past dlouho nikdo neodhalil.</para>
        /// </summary>
        /// <param name="raw">Hodnota ze sloupce, uz zaokrouhlena na cele cislo.</param>
        public static string Text<TEnum>(int raw) where TEnum : struct, Enum
        {
            try
            {
                object underlying = Convert.ChangeType(raw, Enum.GetUnderlyingType(typeof(TEnum)));
                if (Enum.IsDefined(typeof(TEnum), underlying))
                    return Enum.ToObject(typeof(TEnum), underlying).ToString();
            }
            catch (OverflowException)
            {
                // Hodnota se do podkladoveho typu nevejde, takze prvkem vyctu byt nemuze.
            }
            return raw.ToString();
        }
    }
}
