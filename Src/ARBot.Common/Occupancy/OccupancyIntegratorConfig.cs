using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Konfigurace zapisu z <see cref="ARBot.Common.Devices.CameraFrame"/> do occupancy gridu
    /// (<see cref="OccupancyIntegrator"/>). Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public sealed class OccupancyIntegratorConfig
    {
        /// <summary>Kolik sloupcu bylo oriznuto z kazde strany hloubkoveho obrazu
        /// (musi odpovidat <c>PolarGridConfig.EdgeColumnTrim</c>, jinak se azimut posune).</summary>
        public int EdgeColumnTrim = 0;

        /// <summary>
        /// Prah probability (0..255), pod kterym je pixel povazovan za NEsjizdny.
        /// Konvence stejna jako v <c>PathEdgeFinder</c>: vyssi hodnota = sjizdnejsi, 128 = neutralni.
        /// </summary>
        public byte RoadNeutral = 128;

        /// <summary>Do teto vzdalenosti [m] ma barevny vzorek plnou duveru.</summary>
        public double RoadFullRangeM = 3.0;

        /// <summary>Za touto vzdalenosti [m] se barva uz nepouziva (duvera 0). Barva dohledne dal nez
        /// pouzitelna hloubka, ale roste chyba rovinneho predpokladu i velikost pudorysu pixelu.</summary>
        public double RoadMaxRangeM = 8.0;

        /// <summary>
        /// Zapisovat semanticky kanal i tam, kde hloubka nic nevi (za dosahem polarniho gridu)?
        /// Ano je zamer: barva dohledne dal a je to jediny zdroj informace o cestě pred robotem.
        /// Okluze se pritom porad respektuje (za prvni prekazkou v danem azimutu se nevzorkuje).
        /// </summary>
        public bool RoadBeyondDepthRange = true;

        /// <summary>
        /// Maximalni vzdalenost [m], do ktere se vubec prochazi okoli robotu. 0 = odvodit z gridu
        /// a z dosahu polarniho gridu (default).
        /// </summary>
        public double MaxRangeM = 0;

        /// <summary>Duvera barevneho vzorku podle vzdalenosti (linearni pokles za
        /// <see cref="RoadFullRangeM"/> na 0 v <see cref="RoadMaxRangeM"/>).</summary>
        public float RoadConfidence(double range)
        {
            if (range <= RoadFullRangeM) return 1f;
            if (range >= RoadMaxRangeM) return 0f;
            return (float)((RoadMaxRangeM - range) / (RoadMaxRangeM - RoadFullRangeM));
        }

        /// <summary>Prevede hodnotu probability (0..255) na pravdepodobnost sjizdnosti 0..1
        /// (<see cref="RoadNeutral"/> -&gt; 0,5).</summary>
        public float ProbabilityToTraversable(byte value)
        {
            if (value >= RoadNeutral)
            {
                int span = 255 - RoadNeutral;
                return span <= 0 ? 1f : 0.5f + 0.5f * (value - RoadNeutral) / span;
            }
            return RoadNeutral <= 0 ? 0f : 0.5f * value / RoadNeutral;
        }

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (EdgeColumnTrim < 0) throw new ArgumentException("OccupancyIntegratorConfig.EdgeColumnTrim musi byt >= 0.");
            if (RoadMaxRangeM <= RoadFullRangeM)
                throw new ArgumentException(
                    $"OccupancyIntegratorConfig: RoadMaxRangeM ({RoadMaxRangeM}) musi byt > RoadFullRangeM ({RoadFullRangeM}).");
        }
    }
}
