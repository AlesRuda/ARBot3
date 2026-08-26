using System;

namespace ARBot.Common.Vision.Qr
{
    /// <summary>Konfigurace <see cref="QrScanner"/>. Viz doc/robotour-mission.md, sekce Parametry.</summary>
    public sealed class QrScannerConfig
    {
        /// <summary>
        /// Kamera, ze ktere se cte (<c>CameraFrame.Name</c>). Vychozi je <b>pravá</b> — kod tedy musi
        /// byt napravo od robota.
        ///
        /// <para><b>Prazdne = skenovat VSECHNY kamery.</b> Je to levne zmirneni: pod nouzovym
        /// zastavenim je vypocetni cas zdarma a odpada tim cela otazka, na kterou stranu robot
        /// dojel. (Predchozi generace robotu pouzivala levou kameru — je to konfigurace, ne zakon.)</para>
        /// </summary>
        public string CameraName = "Right";

        /// <summary>
        /// Podvzorkovani pred dekodovanim. Kod velikosti A5 z 2 m ma v 640x480 dost pixelu i po
        /// zmenseni na polovinu.
        /// </summary>
        public int Downscale = 2;

        /// <summary>
        /// Kolik shodnych dekodovani se pozaduje, nez se kod ohlasi jako precteny. <b>Skutecnou
        /// pojistkou je az potvrzeni obsluhou</b> — vic nez jedno cteni je levne, ale nic to
        /// nezaruci. Vychozi 1, jako v puvodnim kodu.
        /// </summary>
        public int Confirmations = 1;

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (Downscale < 1)
                throw new ArgumentException(
                    $"QrScannerConfig.Downscale ({Downscale}) musi byt >= 1; 1 = bez zmenseni.");
            if (Confirmations < 1)
                throw new ArgumentException(
                    $"QrScannerConfig.Confirmations ({Confirmations}) musi byt >= 1; nula by "
                    + "znamenala 'prijmi cil, ktery se nikdy neprecetl'.");
        }
    }
}
