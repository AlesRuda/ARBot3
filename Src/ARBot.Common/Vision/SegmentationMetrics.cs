using System;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Vysledek porovnani jedne segmentace s pravdou — zmatkova matice, ze ktere se pocitaji
    /// vsechny metriky. <b>Kladna trida je SJIZDNO</b> (cesta), protoze prave ta se hleda.
    ///
    /// <para><b>Proc matice a ne jen procento:</b> samotna presnost per-pixel je na tehle uloze
    /// zradna. Kdyz je na snimku 80 % plochy sjizdne, odpoved "vsechno je cesta" ma presnost
    /// 80 % a je bezcenna — a presne takove cislo se u teto ulohy uz jednou vydavalo za dobre.
    /// <see cref="IoU"/> takovou odpoved potopi (0,80 vs. 1,00 se lisi malo, ale u IoU se to
    /// projevi), a <see cref="Precision"/>/<see cref="Recall"/> rozliseji, jestli metoda
    /// <i>pridava</i> cestu, kde neni (nebezpecne), nebo ji <i>ubira</i> (jen opatrne).</para>
    /// </summary>
    public struct SegmentationConfusion
    {
        /// <summary>Pravda sjizdno a metoda sjizdno.</summary>
        public long TruePositive;
        /// <summary>Pravda NEsjizdno, ale metoda tvrdi sjizdno — <b>nebezpecna</b> chyba.</summary>
        public long FalsePositive;
        /// <summary>Pravda sjizdno, ale metoda tvrdi nesjizdno — opatrna chyba.</summary>
        public long FalseNegative;
        /// <summary>Pravda nesjizdno a metoda nesjizdno.</summary>
        public long TrueNegative;

        /// <summary>Pocet porovnanych pixelu.</summary>
        public long Total => TruePositive + FalsePositive + FalseNegative + TrueNegative;

        /// <summary>Podil spravne rozhodnutych pixelu (metrika notebooku: sparse_categorical_accuracy).</summary>
        public double Accuracy => Total == 0 ? double.NaN : (double)(TruePositive + TrueNegative) / Total;

        /// <summary>
        /// Prusecik/sjednoceni pro tridu SJIZDNO. <see cref="double.NaN"/>, kdyz cestu netvrdi
        /// ani pravda, ani metoda — pak neni co merit a nula by lhala.
        /// </summary>
        public double IoU
        {
            get
            {
                long u = TruePositive + FalsePositive + FalseNegative;
                return u == 0 ? double.NaN : (double)TruePositive / u;
            }
        }

        /// <summary>Kolik z toho, co metoda oznacila za cestu, cesta opravdu je.</summary>
        public double Precision
        {
            get
            {
                long p = TruePositive + FalsePositive;
                return p == 0 ? double.NaN : (double)TruePositive / p;
            }
        }

        /// <summary>Kolik ze skutecne cesty metoda nasla.</summary>
        public double Recall
        {
            get
            {
                long p = TruePositive + FalseNegative;
                return p == 0 ? double.NaN : (double)TruePositive / p;
            }
        }

        /// <summary>Podil plochy, kterou za sjizdnou oznacila METODA.</summary>
        public double PredictedFraction => Total == 0 ? double.NaN : (double)(TruePositive + FalsePositive) / Total;

        /// <summary>Podil plochy, ktera je sjizdna podle PRAVDY.</summary>
        public double TruthFraction => Total == 0 ? double.NaN : (double)(TruePositive + FalseNegative) / Total;

        /// <summary>Souctem se skladaji matice z jednotlivych snimku do celkove.</summary>
        public static SegmentationConfusion operator +(SegmentationConfusion a, SegmentationConfusion b)
            => new SegmentationConfusion
            {
                TruePositive = a.TruePositive + b.TruePositive,
                FalsePositive = a.FalsePositive + b.FalsePositive,
                FalseNegative = a.FalseNegative + b.FalseNegative,
                TrueNegative = a.TrueNegative + b.TrueNegative,
            };
    }

    /// <summary>
    /// Porovnani pravdepodobnostniho obrazu sjizdnosti s <b>pravdou</b> (rucne oznacenou maskou).
    ///
    /// <para>Oddelene od nastroje, ktery to tiskne, prave proto, aby to slo testovat proti znamym
    /// odpovedim: chyba v metrice se jinak projevi jako <i>vysledek, ktery vypada rozumne</i>, a
    /// takovou vadu nikdo nenajde. Tuhle past uz projekt jednou zaplatil u korelace s mapou, kde
    /// meridlo pripisovalo korelatoru chybu fuze (viz doc/map-correlation-localization.md).</para>
    /// </summary>
    public static class SegmentationMetrics
    {
        /// <summary>
        /// Prah rozhodnuti sjizdno/nesjizdno nad bajtovou pravdepodobnosti. Tatataz hodnota, jakou
        /// pouziva <c>OccupancyIntegratorConfig.RoadNeutral</c> i shoda v <c>BackProjectReport</c>
        /// — jinak by se cisla z ruznych meridel nedala srovnavat. Rozhoduje se <c>&gt;=</c>.
        /// </summary>
        public const byte Threshold = 128;

        /// <summary>
        /// Porovna rozhodnuti metody s pravdou pixel po pixelu.
        /// </summary>
        /// <param name="prediction">Pravdepodobnost sjizdnosti z metody (0..255).</param>
        /// <param name="truth">Pravda (maska); ocekava se 0 = nesjizdno, 255 = sjizdno, ale
        /// prahuje se stejne jako predikce, takze projde i maska 0/1 se snizenym
        /// <paramref name="truthThreshold"/>.</param>
        /// <param name="count">Kolik pixelu porovnat (obe pole musi mit alespon tolik prvku).</param>
        /// <param name="threshold">Prah predikce; vychozi <see cref="Threshold"/>.</param>
        /// <param name="truthThreshold">Prah pravdy; vychozi <see cref="Threshold"/>.</param>
        public static SegmentationConfusion Compare(byte[] prediction, byte[] truth, int count,
                                                    byte threshold = Threshold,
                                                    byte truthThreshold = Threshold)
        {
            if (prediction == null) throw new ArgumentNullException(nameof(prediction));
            if (truth == null) throw new ArgumentNullException(nameof(truth));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count), "Pocet pixelu je negativni.");
            if (prediction.Length < count)
                throw new ArgumentException($"Predikce ma {prediction.Length} pixelu, ceka se {count}.", nameof(prediction));
            if (truth.Length < count)
                throw new ArgumentException($"Pravda ma {truth.Length} pixelu, ceka se {count}.", nameof(truth));

            var c = new SegmentationConfusion();
            for (int i = 0; i < count; i++)
            {
                bool p = prediction[i] >= threshold;
                bool t = truth[i] >= truthThreshold;
                if (t) { if (p) c.TruePositive++; else c.FalseNegative++; }
                else { if (p) c.FalsePositive++; else c.TrueNegative++; }
            }
            return c;
        }
    }
}
