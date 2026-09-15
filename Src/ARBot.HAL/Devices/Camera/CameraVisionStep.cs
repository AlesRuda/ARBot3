using System;
using System.Collections.Generic;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using ARBot.Common.Vision;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Dopočet odvozených vlastností snímku (pravděpodobnost cesty, polární grid)
    /// <b>izolovaný od snímací smyčky</b>.
    ///
    /// <para>⚠️ <b>Nač to je (nález auditu 15. 9. 2026).</b> <c>FrameProcessor.Process</c> se volá
    /// uvnitř <c>try</c> snímací smyčky ovladače — a její <c>catch</c> hlásí
    /// „cteni snimku selhalo (odpojeno?)", <b>zboří pipeline</b> a jde do reconnectu. Softwarová
    /// chyba v ONNX/RKNN nebo v gridu se tedy tvářila jako porucha USB a <b>přičítala se
    /// k reálným výpadkům D435</b>, které se zrovna vyšetřují (viz doc/hardware.md). Horší
    /// záměnu si vymyslet nejde: hledá se kabel, zatímco vada je v kódu.</para>
    ///
    /// <para>Správná odpověď je opačná — <b>surový snímek je v pořádku</b>, jen k němu nejsou
    /// odvozená data. Snímek se vydá dál (occupancy grid o něj přijde, řídicí smyčka ale dostane
    /// obraz) a porucha se ohlásí do <c>Trace</c>, <b>škrceně</b>: snímků chodí 30/s, takže
    /// trvalá vada by jinak zaplavila záznam, ve kterém se ta vada hledá.</para>
    /// </summary>
    public static class CameraVisionStep
    {
        // Jeden skrtic na kameru (klic = jmeno). Kamer jsou jednotky, takze slovnik staci;
        // zamek kvuli tomu, ze kazda kamera bezi na vlastnim vlakne.
        private static readonly Dictionary<string, PoruchaHlasic> hlasici =
            new Dictionary<string, PoruchaHlasic>();

        private static PoruchaHlasic Hlasic(string kamera)
        {
            lock (hlasici)
            {
                if (!hlasici.TryGetValue(kamera ?? string.Empty, out var h))
                    hlasici[kamera ?? string.Empty] = h = new PoruchaHlasic();
                return h;
            }
        }

        /// <summary>
        /// Spustí dopočet nad snímkem. Vrací <c>true</c>, když vize doběhla; <c>false</c>, když
        /// spadla (snímek je pak bez odvozených dat, ale <b>platný</b>).
        /// </summary>
        /// <param name="processor">Dopočet; <c>null</c> = není co počítat.</param>
        /// <param name="frame">Snímek k doplnění.</param>
        /// <param name="kamera">Jméno kamery do hlášení.</param>
        public static bool Run(ICameraFrameProcessor processor, CameraFrame frame, string kamera)
        {
            if (processor == null || frame == null)
                return true;

            try
            {
                processor.Process(frame);
                return true;
            }
            catch (Exception ex)
            {
                // Trace, ne Debug: v Release (na zarizeni) by po poruche vize nezustala zadna
                // stopa a mlcici occupancy grid by vypadal jako vadna kamera.
                Hlasic(kamera).Hlas($"{kamera}: vypocet vize selhal (snimek jde dal bez neho)", ex);
                return false;
            }
        }
    }
}
