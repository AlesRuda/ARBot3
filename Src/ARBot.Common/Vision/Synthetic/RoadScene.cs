using System;
using System.Collections.Generic;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Vision.Synthetic
{
    /// <summary>
    /// Geometrie sceny pro virtualni kameru: vozovka prevedena z OsmNav site do lokalni ENU roviny.
    /// Vozovka je sjednoceni kapsli kolem os hran (polosirka se interpoluje mezi uzly).
    /// Viz doc/virtual-hw.md.
    /// <para>
    /// Dotaz <see cref="IsRoad"/> se vola radove statisickrat na snimek (jednou na pixel), proto jsou
    /// useky zaindexovane v uniformni mrizce a testuje se jen obsah jedne bunky.
    /// </para>
    /// </summary>
    public sealed class RoadScene
    {
        /// <summary>Usek vozovky v lokalni ENU rovine (osa + polosirka na obou koncich).</summary>
        private readonly struct Segment
        {
            public readonly float Ax, Ay, Bx, By;
            /// <summary>Polosirka v bode A [m].</summary>
            public readonly float HalfWidthA;
            /// <summary>Polosirka v bode B [m].</summary>
            public readonly float HalfWidthB;

            public Segment(float ax, float ay, float bx, float by, float halfWidthA, float halfWidthB)
            {
                Ax = ax; Ay = ay; Bx = bx; By = by;
                HalfWidthA = halfWidthA; HalfWidthB = halfWidthB;
            }

            /// <summary>Nejvetsi polosirka useku - o tolik kapsle presahuje usecku.</summary>
            public float MaxHalfWidth => HalfWidthA > HalfWidthB ? HalfWidthA : HalfWidthB;
        }

        private readonly Segment[] segments;

        // --- Uniformni mrizka nad useky (prazdna sit => cols == rows == 0) ---
        private readonly float minX, minY;
        private readonly float cellSize;
        private readonly int cols, rows;
        /// <summary>Indexy useku po bunkach; <c>cellStart[c]..cellStart[c+1]</c> je obsah bunky c (CSR).</summary>
        private readonly int[] cellStart;
        private readonly int[] cellItems;

        /// <summary>Cilova velikost bunky [m]; u velkych map se zvetsi, aby mrizka nerostla nade vse.</summary>
        private const float TargetCellSize = 10f;
        /// <summary>Max pocet bunek v jedne ose.</summary>
        private const int MaxCellsPerAxis = 512;

        /// <summary>
        /// Postavi scenu ze site a pocatku lokalni roviny.
        /// </summary>
        /// <param name="network">Silnicni sit (uzly v LLA).</param>
        /// <param name="origin">Pocatek lokalni ENU roviny.</param>
        public RoadScene(RoadNetwork network, GeoReference origin)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (origin == null) throw new ArgumentNullException(nameof(origin));

            segments = BuildSegments(network, origin);

            if (segments.Length == 0)
            {
                cellStart = Array.Empty<int>();
                cellItems = Array.Empty<int>();
                return;
            }

            // --- Rozsah mrizky (AABB useku nafouknuty o polosirku) ---
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;
            minX = float.PositiveInfinity; minY = float.PositiveInfinity;
            foreach (var s in segments)
            {
                float h = s.MaxHalfWidth;
                minX = Math.Min(minX, Math.Min(s.Ax, s.Bx) - h);
                minY = Math.Min(minY, Math.Min(s.Ay, s.By) - h);
                maxX = Math.Max(maxX, Math.Max(s.Ax, s.Bx) + h);
                maxY = Math.Max(maxY, Math.Max(s.Ay, s.By) + h);
            }

            float extent = Math.Max(maxX - minX, maxY - minY);
            cellSize = Math.Max(TargetCellSize, extent / MaxCellsPerAxis);

            cols = Math.Max(1, (int)((maxX - minX) / cellSize) + 1);
            rows = Math.Max(1, (int)((maxY - minY) / cellSize) + 1);

            BuildIndex(out cellStart, out cellItems);
        }

        /// <summary>
        /// Prevede hrany site na useky v lokalni rovine. Obousmerne hrany daji tentyz pas, proto se
        /// kazda dvojice zpracuje jen jednou (stejny klic jako <c>RoadNetwork.ToLogMessage</c>).
        /// </summary>
        private static Segment[] BuildSegments(RoadNetwork network, GeoReference origin)
        {
            var list = new List<Segment>(network.Count);
            var seen = new HashSet<(long, long, long)>();

            foreach (var e in network.Edges)
            {
                long a = e.From.Id, b = e.To.Id;
                var key = a < b ? (a, b, e.WayId) : (b, a, e.WayId);
                if (!seen.Add(key)) continue;

                var pa = origin.ToLocal(e.From.Location);
                var pb = origin.ToLocal(e.To.Location);

                list.Add(new Segment(pa.X, pa.Y, pb.X, pb.Y,
                                     (float)(e.From.Width * 0.5), (float)(e.To.Width * 0.5)));
            }

            return list.ToArray();
        }

        /// <summary>
        /// Zatridi useky do bunek mrizky (kazdy do vsech bunek, ktere protne jeho nafouknuty AABB)
        /// a slozi je do CSR poli. Dvoupruchodove, bez seznamu na bunku.
        /// </summary>
        private void BuildIndex(out int[] start, out int[] items)
        {
            int cellCount = cols * rows;
            var counts = new int[cellCount + 1];

            for (int i = 0; i < segments.Length; i++)
            {
                CellRange(segments[i], out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                        counts[r * cols + c + 1]++;
            }

            for (int i = 1; i <= cellCount; i++)
                counts[i] += counts[i - 1];

            start = counts;
            items = new int[counts[cellCount]];

            var cursor = new int[cellCount];
            for (int i = 0; i < segments.Length; i++)
            {
                CellRange(segments[i], out int c0, out int c1, out int r0, out int r1);
                for (int r = r0; r <= r1; r++)
                    for (int c = c0; c <= c1; c++)
                    {
                        int cell = r * cols + c;
                        items[start[cell] + cursor[cell]++] = i;
                    }
            }
        }

        /// <summary>Rozsah bunek, ktere protne nafouknuty AABB useku (orezany na mrizku).</summary>
        private void CellRange(in Segment s, out int c0, out int c1, out int r0, out int r1)
        {
            float h = s.MaxHalfWidth;
            c0 = ClampCol((Math.Min(s.Ax, s.Bx) - h - minX) / cellSize);
            c1 = ClampCol((Math.Max(s.Ax, s.Bx) + h - minX) / cellSize);
            r0 = ClampRow((Math.Min(s.Ay, s.By) - h - minY) / cellSize);
            r1 = ClampRow((Math.Max(s.Ay, s.By) + h - minY) / cellSize);
        }

        private int ClampCol(float v) => v < 0 ? 0 : (v >= cols ? cols - 1 : (int)v);
        private int ClampRow(float v) => v < 0 ? 0 : (v >= rows ? rows - 1 : (int)v);

        /// <summary>
        /// Lezi bod lokalni roviny na vozovce?
        /// </summary>
        /// <param name="x">Souradnice na vychod [m].</param>
        /// <param name="y">Souradnice na sever [m].</param>
        public bool IsRoad(double x, double y)
        {
            if (segments.Length == 0) return false;

            // Mimo rozsah mrizky nemuze zadny usek zasahovat (AABB uz jsou nafouknute o polosirku).
            double fc = (x - minX) / cellSize;
            double fr = (y - minY) / cellSize;
            if (fc < 0 || fr < 0 || fc >= cols || fr >= rows) return false;

            int cell = (int)fr * cols + (int)fc;
            int from = cellStart[cell], to = cellStart[cell + 1];

            for (int k = from; k < to; k++)
            {
                ref readonly var s = ref segments[cellItems[k]];

                double abx = s.Bx - s.Ax;
                double aby = s.By - s.Ay;
                double apx = x - s.Ax;
                double apy = y - s.Ay;

                double len2 = abx * abx + aby * aby;
                double t = len2 > 0 ? (apx * abx + apy * aby) / len2 : 0.0;
                if (t < 0) t = 0;
                else if (t > 1) t = 1;

                double dx = apx - t * abx;
                double dy = apy - t * aby;

                // Sirka se meni podel useku - polosirka v bode nejblizsiho promitnuti.
                double half = s.HalfWidthA + t * (s.HalfWidthB - s.HalfWidthA);
                if (dx * dx + dy * dy <= half * half)
                    return true;
            }
            return false;
        }
    }
}
