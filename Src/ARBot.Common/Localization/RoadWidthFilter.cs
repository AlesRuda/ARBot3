using System;
using System.Collections.Generic;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Odhad skutecne sirky cesty <b>per hrana</b> (klic je OSM way) z merene sirky koridoru.
    /// Exponencialni klouzavy prumer, aby to bylo hladke.
    ///
    /// <para><b>Nacpak to je</b> (podnet autora): mapova hranice cesty je syntetická — osa odsazena
    /// o polosirku z OSM, a kde <c>width</c> chybi, vezme se default 3 m. Kdyz hranice v obraze
    /// merime, muzeme sirku <b>aktualizovat</b> a pak srovnavat hranice, ne jen osu. Vedlejsi
    /// produkt: „tady je cesta o 0,8 m uzsi, nez rika mapa".</para>
    ///
    /// <para><b>Filtr je zamerne pomaly a podminovany.</b> Sirka se aktualizuje jen z cyklu, kde
    /// jsou videt OBE hranice a poza sedi (o tom rozhoduje volajici) — jinak by se chyba pozy
    /// zapsala do sirky a ta by ji pak zpetne utvrzovala. Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    public sealed class RoadWidthFilter
    {
        private readonly Dictionary<long, double> width = new Dictionary<long, double>();
        private readonly Dictionary<long, int> samples = new Dictionary<long, int>();
        private readonly double alpha;

        /// <param name="alpha">Vaha noveho merenia (0..1); mensi = hladsi a pomalejsi.</param>
        public RoadWidthFilter(double alpha = 0.05)
        {
            if (alpha <= 0 || alpha > 1) throw new ArgumentOutOfRangeException(nameof(alpha));
            this.alpha = alpha;
        }

        /// <summary>Kolik hran uz ma odhad.</summary>
        public int Count => width.Count;

        /// <summary>
        /// Zapracuje merenou sirku hrany. Prvni merenie odhad <b>zaklada</b> (jinak by se filtr
        /// rozjizdel z mapove hodnoty pres desitky snimku).
        /// </summary>
        public double Update(long wayId, double measuredWidthM)
        {
            if (!(measuredWidthM > 0)) return Estimate(wayId, measuredWidthM);

            if (width.TryGetValue(wayId, out double w))
                width[wayId] = w + alpha * (measuredWidthM - w);
            else
                width[wayId] = measuredWidthM;

            samples.TryGetValue(wayId, out int n);
            samples[wayId] = n + 1;
            return width[wayId];
        }

        /// <summary>Odhad sirky hrany; kdyz jeste zadny neni, vraci <paramref name="fallbackM"/>.</summary>
        public double Estimate(long wayId, double fallbackM)
            => width.TryGetValue(wayId, out double w) ? w : fallbackM;

        /// <summary>Kolik merenii uz do odhadu hrany vstoupilo (0 = zadny odhad).</summary>
        public int Samples(long wayId) => samples.TryGetValue(wayId, out int n) ? n : 0;

        /// <summary>Zahodi vsechny odhady (novy beh, seek v zaznamu).</summary>
        public void Reset()
        {
            width.Clear();
            samples.Clear();
        }
    }
}
