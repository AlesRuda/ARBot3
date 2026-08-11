using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Konfigurace kartezskeho occupancy gridu (<see cref="OccupancyGrid"/>).
    /// Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para><b>Konvence znamenka log-odds:</b> OBA kanaly (<c>LOcc</c> i <c>LRoad</c>) drzi log-odds
    /// <b>neprujezdnosti</b> - kladne = neprujezdne, negativni = prujezdne, 0 = nevim. Diky tomu maji
    /// oba kanaly stejne prahy i stejnou aritmetiku a jsou skutecne rovnocenne.</para>
    /// </summary>
    public sealed class OccupancyGridConfig
    {
        /// <summary>Velikost bunky [m].</summary>
        public double Resolution = 0.05;

        /// <summary>Pocet bunek na stranu. MUSI byt mocnina dvou (kruhovy buffer maskuje indexy).
        /// 256 pri 5 cm = 12,8 x 12,8 m; robot je ve stredu.</summary>
        public int Size = 256;

        // --- Inverzni senzorovy model (prirustky log-odds pri duvere 1) ---

        /// <summary>Prirustek pri pozorovani prekazky (kladny = k neprujezdnosti).</summary>
        public float OccupiedUpdate = 0.85f;

        /// <summary>Prirustek pri pozorovani volne plochy (zaporny = k prujezdnosti).</summary>
        public float FreeUpdate = -0.40f;

        /// <summary>Maximalni prirustek kanalu <c>LRoad</c> pri jistem pozorovani mimo cestu.
        /// Skutecny prirustek se odvozuje z pravdepodobnosti sjizdnosti - viz
        /// <see cref="RoadUpdateFromProbability"/>.</summary>
        public float RoadUpdate = 0.60f;

        /// <summary>Omezeni |log-odds| v bunce. Dava konecnou dobu prepsani (kratka pamet, aby
        /// stara/mispozicovana data nezustala navzdy) - z plne obsazene na volnou ~25 pozorovani
        /// pri <see cref="FreeUpdate"/> = -0,4, tj. 2,5 s pri 10 Hz.</summary>
        public float Clamp = 5.0f;

        /// <summary>Krok fixed-point ulozeni do <c>sbyte</c>. 0,05 -&gt; rozsah +-6,35, tedy clamp +-5
        /// se vejde s rezervou a zaroven je krok dost maly, aby se neztracela slaba pozorovani
        /// (prirustek se zaokrouhluje - viz <see cref="OccupancyGrid.AddOcc"/>).</summary>
        public float Scale = 0.05f;

        // --- Prahy pro odvozeni stavu bunky ---

        /// <summary>Od tohoto log-odds vyse je kanal "jiste neprujezdny" (-&gt; <see cref="CellState.Blocked"/>).</summary>
        public float BlockedThreshold = 1.0f;

        /// <summary>Do tohoto log-odds nize je kanal "jiste prujezdny". Bunka je
        /// <see cref="CellState.Free"/>, jen kdyz to plati pro OBA kanaly.
        ///
        /// <para>Klicovy detail: "nemam o ceste data" (<c>LRoad</c> ~ 0) NENI "neni to cesta".
        /// Nulovy kanal tedy nebrani jizde jako <see cref="CellState.Blocked"/>, jen drzi bunku
        /// v <see cref="CellState.Unknown"/> - symetricky k <c>Unknown != Free</c> u polarniho gridu.</para></summary>
        public float FreeThreshold = -1.0f;

        /// <summary>Kvantovany prah neprujezdnosti (odvozeny z <see cref="BlockedThreshold"/>).</summary>
        public sbyte BlockedQuantized => Quantize(BlockedThreshold);

        /// <summary>Kvantovany prah prujezdnosti (odvozeny z <see cref="FreeThreshold"/>).</summary>
        public sbyte FreeQuantized => Quantize(FreeThreshold);

        /// <summary>
        /// Prevede pravdepodobnost SJIZDNOSTI (0 = jiste nesjizdne, 1 = jiste sjizdne, 0,5 = nevim)
        /// na prirustek log-odds NEPRUJEZDNOSTI kanalu <c>LRoad</c>. Linearni a symetricke:
        /// p=1 -&gt; -<see cref="RoadUpdate"/>, p=0 -&gt; +<see cref="RoadUpdate"/>, p=0,5 -&gt; 0.
        /// </summary>
        public float RoadUpdateFromProbability(float pTraversable)
            => RoadUpdate * (1f - 2f * pTraversable);

        /// <summary>Kvantuje log-odds do <c>sbyte</c> (s clampem na <see cref="Clamp"/>).</summary>
        public sbyte Quantize(float logOdds)
        {
            float limit = Clamp / Scale;
            float q = logOdds / Scale;
            if (q > limit) q = limit;
            if (q < -limit) q = -limit;
            int i = (int)MathF.Round(q);
            if (i > sbyte.MaxValue) i = sbyte.MaxValue;
            if (i < sbyte.MinValue) i = sbyte.MinValue;
            return (sbyte)i;
        }

        /// <summary>Zkontroluje konzistenci konfigurace; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (Size <= 0 || (Size & (Size - 1)) != 0)
                throw new ArgumentException($"OccupancyGridConfig.Size musi byt mocnina dvou, je {Size}.");
            if (Resolution <= 0)
                throw new ArgumentException($"OccupancyGridConfig.Resolution musi byt > 0, je {Resolution}.");
            if (Scale <= 0)
                throw new ArgumentException($"OccupancyGridConfig.Scale musi byt > 0, je {Scale}.");
            if (Clamp <= 0)
                throw new ArgumentException($"OccupancyGridConfig.Clamp musi byt > 0, je {Clamp}.");
            if (Clamp / Scale > sbyte.MaxValue)
                throw new ArgumentException(
                    $"OccupancyGridConfig: Clamp/Scale = {Clamp / Scale} se nevejde do sbyte (max {sbyte.MaxValue}).");
            if (FreeThreshold >= BlockedThreshold)
                throw new ArgumentException(
                    $"OccupancyGridConfig: FreeThreshold ({FreeThreshold}) musi byt < BlockedThreshold ({BlockedThreshold}).");
            if (OccupiedUpdate <= 0)
                throw new ArgumentException("OccupancyGridConfig.OccupiedUpdate musi byt kladny (log-odds neprujezdnosti).");
            if (FreeUpdate >= 0)
                throw new ArgumentException("OccupancyGridConfig.FreeUpdate musi byt zaporny (log-odds neprujezdnosti).");
        }
    }
}
