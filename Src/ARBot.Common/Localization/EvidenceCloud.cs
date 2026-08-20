using System;
using System.Collections.Generic;
using ARBot.Common.Logs;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Dukazni bunky pro korelaci: jen ty, kde ma semanticky kanal LRoad dost silne mineni.
    /// Souradnice jsou STREDY bunek ve svete [m], vaha je log-odds VCETNE ZNAMENKA
    /// (kladne = "mimo cestu", zaporne = "cesta"). Viz doc/map-correlation-localization.md.
    ///
    /// <para>Kanal Occ se NEUCASTNI: jsou v nem parkujici auta, chodci a stromy, ktere v mape
    /// nejsou, a systematicky by odhad tlacily stranou.</para>
    ///
    /// <para>Struktura je "pole miste objektu" (SoA) schvalne - skenovani jde pres oblak stovky krat
    /// za cyklus a chce sekvencni pristup do pameti.</para>
    /// </summary>
    public sealed class EvidenceCloud
    {
        /// <summary>Pocet dukaznich bunek.</summary>
        public int Count { get; }

        /// <summary>Svetove X stredu bunky [m].</summary>
        public double[] X { get; }

        /// <summary>Svetove Y stredu bunky [m].</summary>
        public double[] Y { get; }

        /// <summary>LRoad [log-odds] vcetne znamenka.</summary>
        public float[] W { get; }

        private EvidenceCloud(double[] x, double[] y, float[] w, int count)
        {
            X = x; Y = y; W = w; Count = count;
        }

        /// <summary>
        /// Vytahne ze snapshotu gridu bunky s <c>|LRoad| &gt;= threshold</c>.
        /// </summary>
        /// <param name="msg">Snapshot gridu (kanaly v lokalnim poradi <c>i + j * Size</c>).</param>
        /// <param name="threshold">Prah absolutni hodnoty LRoad [log-odds].</param>
        public static EvidenceCloud FromGrid(OccupancyGridMsg msg, float threshold)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            var xs = new List<double>();
            var ys = new List<double>();
            var ws = new List<float>();

            if (msg.Road != null)
            {
                for (int j = 0; j < msg.Size; j++)
                {
                    double y = msg.CenterY(j);
                    int rowBase = j * msg.Size;
                    for (int i = 0; i < msg.Size; i++)
                    {
                        float w = msg.Road[rowBase + i] * msg.Scale;
                        if (w > -threshold && w < threshold) continue;
                        xs.Add(msg.CenterX(i));
                        ys.Add(y);
                        ws.Add(w);
                    }
                }
            }

            return new EvidenceCloud(xs.ToArray(), ys.ToArray(), ws.ToArray(), ws.Count);
        }
    }
}
