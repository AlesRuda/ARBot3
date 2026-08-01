using System.Collections.Generic;
using ARBot.Common.Devices;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Zdroj nejnovejsich snimku kamer pro <see cref="ControlLoop"/> (pull model). Ridici smycka
    /// si na kazdem tiku vyzada nejnovejsi snimky vsech kamer, vezme z nich grid pro rizeni a cely
    /// <see cref="CameraFrame"/> forwardne na Stream (zaznam/UI) - viz doc/plan-camera-vision-refactor.md
    /// (krok 3). Kamery uz tedy nejsou v pipeline pres <c>SensorMessageSource</c>.
    ///
    /// <para><b>Smer zavislosti:</b> <see cref="ControlLoop"/> zije v <c>ARBot.Common</c>, ale kamery
    /// (<c>ICamera</c>) a jejich singleton (<c>ARBotHW</c>) v app/HAL vrstve. Toto rozhrani je proto
    /// injektovana abstrakce, kterou naplnuje app vrstva (<c>ARBotRuntime</c>) ctenim <c>ARBotHW.Current</c>
    /// za behu - Common tak nezavisi na HAL/app (smer <c>Common ← HAL ← app</c> zustava).</para>
    /// </summary>
    public interface ICameraPullSource
    {
        /// <summary>
        /// Vrati nejnovejsi (dosud nevyzvednute) snimky vsech kamer. Kamera, ktera od posledniho
        /// volani nemela novy snimek, se vynecha (semantika <c>GetLastMeasurement</c> - vraci null,
        /// kdyz neni novy snimek). Prazdny seznam = zadna kamera zatim nedodala novy snimek.
        /// </summary>
        IReadOnlyList<CameraFrame> PullLatest();
    }
}
