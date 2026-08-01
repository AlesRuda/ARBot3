using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Konfigurace polarniho gridu sjizdnosti (<see cref="CameraFrameProcessor"/>).
    ///
    /// Navrh (viz diskuse v doc/traversability-grid.md):
    /// - Robot-centricky, per-kamera.
    /// - Azimut = skupiny <see cref="ColumnsPerCell"/> sloupcu obrazu (konstantni pocet sloupcu,
    ///   aby pouzitelna sirka byla beze zbytku delitelna). Uhlova sirka bunky se bere z realnych
    ///   paprsku (<see cref="IDepthCameraProjection.Camera2DToCamera3D"/>), ne z predpokladu.
    /// - Radialne: Δr zacina na <see cref="MinRadialStepM"/> (5 cm) a roste tak, aby bunka drzela
    ///   <see cref="TargetPointsPerCell"/> bodu. Tvrda podlaha <see cref="MinPointsPerCell"/> - pod
    ///   ni je bunka Unknown.
    /// </summary>
    public sealed class PolarGridConfig
    {
        /// <summary>Pocet sloupcu obrazu na jednu azimutovou bunku (N). Pouzitelna sirka musi
        /// byt beze zbytku delitelna timto cislem.</summary>
        public int ColumnsPerCell = 16;

        /// <summary>Cilovy pocet bodu na bunku pri navrhu radialnich hran (min + rezerva).</summary>
        public int TargetPointsPerCell = 15;

        /// <summary>Tvrda podlaha poctu bodu na bunku. Pod ni je bunka <see cref="TraversabilityClass.Unknown"/>.</summary>
        public int MinPointsPerCell = 8;

        /// <summary>Kolik sloupcu oriznout z kazde strany obrazu (distorze na okrajich). 0 = neoriznout.</summary>
        public int EdgeColumnTrim = 0;

        /// <summary>Pouzit nativni SIMD depth-&gt;pointcloud (<c>DepthTransform2Impl</c>) se znovupouzitym
        /// bufferem misto managed per-pixel transformu. Rychlejsi; vyzaduje NativeLib. Ekvivalence
        /// s managed cestou je overena testem. Default false (bezpecny managed fallback).</summary>
        public bool UseNativeTransform = false;

        /// <summary>Minimalni pouzitelna vzdalenost [m].</summary>
        public float MinRangeM = 0.3f;
        /// <summary>Maximalni pouzitelna vzdalenost [m].</summary>
        public float MaxRangeM = 5.5f;
        /// <summary>Minimalni (pocatecni) radialni krok [m] - blizka podlaha (~5 cm dle kartezskeho gridu).</summary>
        public float MinRadialStepM = 0.05f;
        /// <summary>Predpokladany podil platnych depth pixelu - navysuje cil pri navrhu hran, aby
        /// bunka drzela cil i po vypadku casti pixelu. 1 = nekracet.</summary>
        public float AssumedValidFraction = 0.6f;

        // --- Referencni rovina (fit z blizkych nizkych bunek) ---
        /// <summary>Do teto vzdalenosti se berou bunky pro fit referencni roviny [m].</summary>
        public float PlaneFitMaxRangeM = 2.0f;
        /// <summary>Maximalni |vyska| bunky pouzita pro fit roviny [m] (odfiltruje prekazky).</summary>
        public float PlaneFitMaxAbsHeightM = 0.15f;

        // --- Prahy klasifikace (skalovane vzdalenosti - sum depth roste s r) ---
        /// <summary>Zakladni tolerance odchylky od roviny [m].</summary>
        public float MaxHeightDevBaseM = 0.03f;
        /// <summary>Prirustek tolerance odchylky na metr vzdalenosti [m/m].</summary>
        public float MaxHeightDevPerM = 0.02f;
        /// <summary>Maximalni stoupani vuci sousedum (Δz/Δs, plane-relativni).</summary>
        public float MaxSlope = 0.35f;
        /// <summary>Zakladni ocekavany sum vysky senzoru [m].</summary>
        public float RoughRefBaseM = 0.01f;
        /// <summary>Prirustek ocekavaneho sumu vysky s r^2 [m/m^2].</summary>
        public float RoughRefPerM2 = 0.004f;
        /// <summary>Nasobek ocekavaneho sumu, nad kterym je bunka klasifikovana jako prekazka (drsnost).</summary>
        public float RoughObstacleFactor = 2.5f;

        /// <summary>Tolerance odchylky od roviny v dane vzdalenosti [m].</summary>
        public float MaxHeightDev(float r) => MaxHeightDevBaseM + MaxHeightDevPerM * r;

        /// <summary>Ocekavany sum vysky senzoru v dane vzdalenosti [m].</summary>
        public float RoughRef(float r) => RoughRefBaseM + RoughRefPerM2 * r * r;

        /// <summary>
        /// Vypocte radialni hrany z geometrie kamery (<see cref="IDepthCameraProjection.Camera2DToCamera3D"/>
        /// + <see cref="IDepthCameraProjection.Transformation"/>). Model: prusecik paprsku s rovinou zeme z=0.
        /// Vzorkuje sloupce STREDNI azimutove bunky, spocte pozemni vzdalenost kazdeho paprsku a hrany
        /// klade tak, aby kazdy prstenec spanoval alespon <see cref="MinRadialStepM"/> a zaroven mel
        /// dost bodu (cil / <see cref="AssumedValidFraction"/>). Vraci rostouci pole hran [m]
        /// (delka = pocet prstencu + 1); prazdne pole pokud kamera nevidi zem v rozsahu.
        /// </summary>
        public RadialEdge[] BuildRadialEdges(IDepthCameraProjection proj, int width, int height)
        {
            if (proj == null) throw new ArgumentNullException(nameof(proj));
            var table = proj.Camera2DToCamera3D;
            var m = proj.Transformation;

            // Pozice kamery v robot-rel. ramci = transformace pocatku.
            var origin = new Point4D { A = 1 }.Transform(m);
            int tblH = table.GetLength(0), tblW = table.GetLength(1);

            int trim = EdgeColumnTrim;
            int usableW = width - 2 * trim;
            if (usableW <= 0) return Array.Empty<RadialEdge>();

            // Pozemni vzdalenost paprsku (pixel x,y) pri modelu rovne zeme z=0; NaN pokud nemiri k zemi.
            float GroundRange(int x, int y)
            {
                if (y < 0 || x < 0 || y >= tblH || x >= tblW) return float.NaN;
                var ray = table[y, x];
                var p1 = new Point4D { X = ray.X, Y = ray.Y, Z = 1, A = 1 }.Transform(m);
                float dirZ = p1.Z - origin.Z;
                if (dirZ >= 0) return float.NaN;             // nemiri k zemi
                float t = -origin.Z / dirZ;
                if (t <= 0) return float.NaN;
                float gx = origin.X + t * (p1.X - origin.X);
                float gy = origin.Y + t * (p1.Y - origin.Y);
                return MathF.Sqrt(gx * gx + gy * gy);
            }

            // Sloupce stredni azimutove bunky (pro rozdeleni na prstence dle poctu bodu).
            int centerCol = trim + usableW / 2;
            int c0 = centerCol - ColumnsPerCell / 2;
            int c1 = c0 + ColumnsPerCell;
            if (c0 < 0) c0 = 0;
            if (c1 > width) c1 = width;

            var ranges = new List<float>();
            for (int x = c0; x < c1; x++)
                for (int y = 0; y < height; y++)
                {
                    float r = GroundRange(x, y);
                    if (!float.IsNaN(r) && r >= MinRangeM && r <= MaxRangeM)
                        ranges.Add(r);
                }

            if (ranges.Count == 0) return Array.Empty<RadialEdge>();
            ranges.Sort();

            // Cil poctu geometrickych bodu na prstenec (navyseny o predpokladany vypadek pixelu).
            float valid = AssumedValidFraction > 0 ? AssumedValidFraction : 1f;
            int needed = Math.Max(MinPointsPerCell, (int)Math.Ceiling(TargetPointsPerCell / valid));

            var edgeRanges = new List<float>();
            float lastEdge = Math.Max(MinRangeM, ranges[0]);
            edgeRanges.Add(lastEdge);
            int cnt = 0;
            for (int i = 0; i < ranges.Count; i++)
            {
                float rr = ranges[i];
                if (rr < lastEdge) continue;
                cnt++;
                if (rr - lastEdge >= MinRadialStepM && cnt >= needed)
                {
                    edgeRanges.Add(rr);
                    lastEdge = rr;
                    cnt = 0;
                }
            }
            if (cnt >= MinPointsPerCell && ranges[ranges.Count - 1] > lastEdge)
                edgeRanges.Add(Math.Min(MaxRangeM, ranges[ranges.Count - 1]));

            if (edgeRanges.Count < 2) return Array.Empty<RadialEdge>();

            // Radek "kde se hranice lame": ve STREDNIM sloupci najdi radek s nejblizsi pozemni
            // vzdalenosti (u dopredne skloneni kamery je range(radek) monotonni -> presne).
            int centerX = Math.Min(width - 1, Math.Max(0, centerCol));
            var profile = new List<(float range, int row)>();
            for (int y = 0; y < height; y++)
            {
                float r = GroundRange(centerX, y);
                if (!float.IsNaN(r)) profile.Add((r, y));
            }
            int RowForRange(float r)
            {
                int best = -1; float bestD = float.MaxValue;
                foreach (var (pr, py) in profile)
                {
                    float d = Math.Abs(pr - r);
                    if (d < bestD) { bestD = d; best = py; }
                }
                return best;
            }

            var edges = new RadialEdge[edgeRanges.Count];
            for (int i = 0; i < edgeRanges.Count; i++)
                edges[i] = new RadialEdge(edgeRanges[i], RowForRange(edgeRanges[i]));
            return edges;
        }
    }
}
