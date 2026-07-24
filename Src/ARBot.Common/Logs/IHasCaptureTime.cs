using System;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Zprava nesouci cas porizeni (capture). Vyuziva se pro index (seek podle casu)
    /// a pro RealTime pacing pri replay. Zpravy, ktere ho neimplementuji, se prehravaji
    /// jen podle poradi v souboru.
    /// </summary>
    public interface IHasCaptureTime
    {
        /// <summary>Cas porizeni.</summary>
        DateTime CaptureTime { get; }
    }
}
