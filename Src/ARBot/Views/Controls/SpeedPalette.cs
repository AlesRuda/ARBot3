using System;

namespace ARBot.Views.Controls
{
    /// <summary>
    /// Jedna barevna skala rychlosti pro mapu i graf: <b>0 = cervena</b> (stoji / plazi se),
    /// <b>polovina = oranzova</b>, <b>1 = modra planu</b> (plna rychlost). Vstupem je rychlost
    /// normalizovana na strop rizeni (<c>PlanSpeedProfile.Normalized</c>).
    ///
    /// <para>Proc konci modrou a ne zelenou: lokalni plan byl v mape odjakziva modry (0x42A5F5),
    /// zelena je zvyraznena globalni trasa, ktera lezi hned pod nim. Plan v plne rychlosti tedy
    /// vypada jako driv a "teple" barvy znaci jen mista, kde ho neco brzdi - odstup od prekazek
    /// nebo hranice potvrzene sjizdneho.</para>
    ///
    /// <para>Vraci bajty RGB, protoze mapa (Mapsui) a graf (Avalonia) maji kazdy vlastni typ barvy.</para>
    /// </summary>
    public static class SpeedPalette
    {
        private static readonly (byte R, byte G, byte B) Slow = (0xE5, 0x39, 0x35);   // cervena
        private static readonly (byte R, byte G, byte B) Mid = (0xFF, 0xB3, 0x00);    // oranzova
        private static readonly (byte R, byte G, byte B) Fast = (0x42, 0xA5, 0xF5);   // modra planu

        /// <summary>Barva pro normalizovanou rychlost <paramref name="t"/> v 0..1 (mimo rozsah se orizne).</summary>
        public static (byte R, byte G, byte B) Rgb(double t)
        {
            if (double.IsNaN(t)) t = 0;
            t = Math.Clamp(t, 0, 1);
            return t < 0.5 ? Lerp(Slow, Mid, t * 2) : Lerp(Mid, Fast, (t - 0.5) * 2);
        }

        private static (byte, byte, byte) Lerp((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t)
            => ((byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
    }
}
