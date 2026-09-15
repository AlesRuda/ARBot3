using System;
using ARBot.Common.Common;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Převádí razítko <b>hodin kamery</b> (ms od zapnutí zařízení) na
    /// <see cref="TimeBase"/> — ukotvením počátku prvním snímkem.
    ///
    /// <para><b>Proč to je.</b> Pravidlo projektu zní <b>všechen čas vychází z <c>TimeBase</c></b>
    /// (viz CLAUDE.md): je to čas startu aplikace plus monotónní <c>Stopwatch</c>, takže razítka
    /// jdou monotónně za sebou a neskáčou s NTP. Do 15. 9. 2026 si ale ovladače D435 počítaly
    /// razítka streamů jako <c>epocha 1970 + offset časové zóny + ms z kamery</c> — druhá časová
    /// základna, navíc závislá na časové zóně stroje. Audit to našel jako součást nálezu
    /// o razítkách kamer; u <c>T265TrackingCamera</c> tatáž chyba tekla přímo do fúze.</para>
    ///
    /// <para><b>Proč se nedosadí prostě <c>TimeBase.Now</c>.</b> Pole
    /// <c>CameraFrame.RGBTimeStamp</c>/<c>DepthTimeStamp</c> mají v záznamu jediný úkol: poznat
    /// <b>zamrzlý stream</b>, tedy „razítko se nehýbe, přestože framesety chodí" (rozliší to
    /// „nedodává librealsense/senzor" od „chybuje naše kopírování pixelů"). Čas stroje se hýbe
    /// vždycky, takže by tu diagnostiku zabil. Proto se ukotví jen <b>počátek</b> a přírůstky
    /// zůstávají z kamery: zamrzlé razítko zůstane po převodu zamrzlé.</para>
    ///
    /// <para><b>Kotva není měření latence</b> a netváří se tak. Je to převod základny; o kolik
    /// je snímek starší než okamžik vyzvednutí, tenhle typ neříká (a jediný čtenář těch polí se
    /// na to neptá — ptá se, jestli se hýbou). Skutečný čas snímku pro řízení nese
    /// <c>CameraFrame.TimeStamp</c>, který je z <c>TimeBase</c> už dnes.</para>
    ///
    /// <para><b>Není thread-safe</b> — volá se z jediné snímací smyčky ovladače.</para>
    /// </summary>
    public sealed class DeviceClockAnchor
    {
        private bool anchored;
        private DateTime baseTime;
        private double baseDeviceMs;

        /// <summary>Je už kotva založená (přišel aspoň jeden rozumný snímek)?</summary>
        public bool IsAnchored => anchored;

        /// <summary>
        /// Zapomene kotvu. <b>Nutné při zboření a znovupostavení pipeline</b>: hodiny kamery pak
        /// začínají odjinud (klidně od nuly) a bez nové kotvy by razítka skočila o roky zpět.
        /// </summary>
        public void Reset() => anchored = false;

        /// <summary>
        /// Razítko hodin kamery <paramref name="deviceMs"/> v základně <see cref="TimeBase"/>.
        /// </summary>
        /// <param name="deviceMs">Razítko streamu z librealsense [ms hodin zařízení].</param>
        /// <param name="now">Aktuální <see cref="TimeBase.Now"/> (parametr kvůli testům).</param>
        public DateTime ToTimeBase(double deviceMs, DateTime now)
        {
            // Nesmyslne razitko kotvu nezaklada - jinak by se o nej oprelo vsechno dalsi.
            if (!double.IsFinite(deviceMs))
                return now;

            if (!anchored)
            {
                anchored = true;
                baseTime = now;
                baseDeviceMs = deviceMs;
            }

            return baseTime.AddMilliseconds(deviceMs - baseDeviceMs);
        }

        /// <summary>Razítko v základně <see cref="TimeBase"/> s časem z <see cref="TimeBase.Now"/>.</summary>
        public DateTime ToTimeBase(double deviceMs) => ToTimeBase(deviceMs, TimeBase.Now);
    }
}
