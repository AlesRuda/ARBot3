using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Nastaveni <see cref="RoadWidthEstimator"/>. Viz doc/map-correlation-localization.md.
    ///
    /// <para>⚠️ <b>Vsechny tri hodnoty jsou PROZATIMNI</b> — odhadnute, ne zmerene. Nastavit se maji
    /// z dat: estimator je <b>ciste funkce posloupnosti</b> <c>Width</c> z <c>RoadCorridorMsg</c>,
    /// a ta je v zaznamu i u cyklu, ktere neprosly branami. Prahy tedy jdou proladit offline nad
    /// jednim vyjezdem, misto aby se hadaly — stejne jako se z dat nastavil <c>MinPeriod</c>
    /// u korelace, <c>imuheadinghz</c> u kompasu i <c>corridortol</c>.</para>
    /// </summary>
    public sealed class RoadWidthEstimatorConfig
    {
        /// <summary>
        /// Kolik poslednich merení na hranu se drzi.
        ///
        /// <para><b>Okno, ne cela historie — schvalne.</b> Cesta se muze SKUTECNE rozsirovat
        /// (nálevka; drzi to <c>RoadWidthFilterTests.NaRozsirujiciSeCeste_filtrTrvaleZaostava</c>).
        /// Pres celou historii by na takove ceste rozptyl rostl a kvalita by nebyla dobra nikdy;
        /// v okne je i rozsirujici se cesta lokalne konzistentni. Vychozich 20 je pri 10 Hz
        /// zhruba 2 s.</para>
        /// </summary>
        public int WindowSize = 20;

        /// <summary>
        /// Kolik merení v okne musi byt, nez ma smysl se ptat na rozptyl. Pod tim je rozptyl sam
        /// prilis nejisty na to, aby o necem rozhodoval.
        /// </summary>
        public int MinSamples = 10;

        /// <summary>
        /// Strop na rozptyl merení v okne [m] — <b>MAD</b> (median absolutnich odchylek od
        /// medianu), ne smerodatna odchylka. Nad nim je odhad oznaceny za nedůvěryhodny.
        ///
        /// <para><b>Nacpak rozptyl.</b> Spatne prolozeni (chytly se jine dve hranice) dava sirky,
        /// ktere se navzajem <b>rozchazeji</b>; spravne prolozeni dava sirky, ktere si sednou.
        /// Rozptyl merení je tedy prime meritko kvality odhadu a <b>nepotrebuje zadnou vnejsi
        /// referenci</b> — ani mapu, ani pozu.</para>
        ///
        /// <para>⚠️ <b>Sirka na poze NEZAVISI</b> (<c>CorridorFinder</c>: <c>Width = cL − cR</c>,
        /// oboji offsety primek v ramci robotu). Chyba pozy se do ni dostat nemuze — proto se
        /// kvalita neposuzuje shodou s mapou, ale shodou merení mezi sebou.</para>
        ///
        /// <para>Vychozich 0,10 m je z reziduí prolozeni na realistickych datech (0,03–0,09 m;
        /// sirka je rozdil dvou hranic, tedy ~√2×) prepoctenych na MAD ≈ 0,674 σ.</para>
        /// </summary>
        public double MaxDispersionM = 0.10;

        /// <summary>Vyhodi vyjimku, kdyz nastaveni nedava smysl.</summary>
        public void Validate()
        {
            if (WindowSize < 1) throw new ArgumentOutOfRangeException(nameof(WindowSize));
            if (MinSamples < 1) throw new ArgumentOutOfRangeException(nameof(MinSamples));
            if (MinSamples > WindowSize)
                throw new ArgumentOutOfRangeException(nameof(MinSamples),
                    "MinSamples nesmi byt vetsi nez WindowSize - kvalita by nebyla dobra nikdy.");
            if (!(MaxDispersionM > 0)) throw new ArgumentOutOfRangeException(nameof(MaxDispersionM));
        }
    }
}
