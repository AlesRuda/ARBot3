using System;
using System.Collections.Generic;
using System.Numerics;
using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Vision
{
    /// <summary>Jeden bod hloubky v profilu (ramec robotu, viz <see cref="SceneProfile"/>).</summary>
    public struct ProfilePoint
    {
        /// <summary>Vodorovna vzdalenost od pocatku robotu <c>√(x²+y²)</c> [m] - tataz velicina,
        /// podle ktere <see cref="CameraFrameProcessor.BuildGrid"/> radi bod do prstence.</summary>
        public float Range;
        /// <summary>Vyska bodu [m] (vuci referencnimu bodu robotu).</summary>
        public float Z;
        /// <summary>Sloupec hloubkoveho obrazu, ze ktereho bod je.</summary>
        public short Column;
        /// <summary>Radek hloubkoveho obrazu.</summary>
        public short Row;
        /// <summary>Radialni prstenec gridu; -1 = mimo rozsah gridu (bod se do gridu nedostal).</summary>
        public short Radial;
    }

    /// <summary>
    /// Proc ma bunka svou tridu: hodnoty, ktere <see cref="CameraFrameProcessor"/> porovnava s prahy,
    /// spoctene TYMZ kodem (fit roviny, odchylka, stoupani vuci sousedum). Bunka pod podlahou poctu
    /// bodu ma <see cref="Evaluated"/> = false.
    /// </summary>
    public struct ProfileVerdict
    {
        public bool Evaluated;
        /// <summary>Vodorovna vzdalenost teziste bunky [m].</summary>
        public float Range;
        /// <summary>Vyska referencni roviny pod tezistem bunky [m].</summary>
        public float PlaneZ;
        /// <summary>Znamenkova odchylka teziste od roviny [m] a jeji tolerance.</summary>
        public float Deviation, DeviationLimit;
        /// <summary>Drsnost (<see cref="PolarCell.StdZ"/>) a jeji prah.</summary>
        public float Rough, RoughLimit;
        /// <summary>Nejvetsi stoupani vuci sousedum (Δz/Δs) a jeho prah.</summary>
        public float Slope, SlopeLimit;

        public bool TooHigh => Evaluated && Math.Abs(Deviation) > DeviationLimit;
        public bool TooRough => Evaluated && Rough > RoughLimit;
        public bool TooSteep => Evaluated && Slope > SlopeLimit;

        /// <summary>Trida, ktera z techto hodnot plyne (musi sedet s <see cref="PolarCell.Class"/>).</summary>
        public TraversabilityClass ImpliedClass => !Evaluated ? TraversabilityClass.Unknown
            : (TooHigh || TooRough || TooSteep ? TraversabilityClass.Obstacle : TraversabilityClass.Free);
    }

    /// <summary>
    /// Profil sceny pred robotem v jednom azimutu polarniho gridu: SUROVE body z hloubky (sloupce
    /// obrazu, ktere tvori azimutovou bunku) a pres ne bunky gridu s vysvetlenim klasifikace.
    /// Nastroj na ladeni detekce terenu - ukazuje, z ceho bunka vznikla a proc ma svou tridu.
    ///
    /// <para><b>Body se pocitaji stejne jako v <see cref="CameraFrameProcessor.BuildGrid"/></b>
    /// (managed cesta: paprsek z <c>Camera2DToCamera3D</c> × hloubka, pak <c>Transformation</c>),
    /// takze pri zobrazeni nejde o jinou geometrii nez tu, kterou videl grid. Nativni SIMD cesta
    /// dava tytez body (ekvivalencni test gridu).</para>
    ///
    /// <para>Azimut = skupina <see cref="PolarTraversabilityGrid.ColumnsPerCell"/> sloupcu, ne uhel
    /// (u sklonene kamery sloupec neni konstantni azimut, viz <see cref="PolarTraversabilityGrid"/>).
    /// Oriznuti okraju se dopocita z rozmeru: <c>trim = (W − A·N) / 2</c>.</para>
    /// </summary>
    public sealed class SceneProfile
    {
        /// <summary>Azimutova bunka, ke ktere profil patri.</summary>
        public int Azimuth { get; private set; }
        /// <summary>Prvni a posledni+1 sloupec obrazu azimutove bunky.</summary>
        public int ColumnFrom { get; private set; }
        public int ColumnTo { get; private set; }
        /// <summary>Surove body (platne pixely sloupcu bunky, v libovolne vzdalenosti).</summary>
        public ProfilePoint[] Points { get; private set; } = Array.Empty<ProfilePoint>();
        /// <summary>Hrany prstencu gridu (null = profil bez gridu).</summary>
        public RadialEdge[] Edges { get; private set; }
        /// <summary>Bunky gridu v tomto azimutu (index = prstenec); null = bez gridu.</summary>
        public PolarCell[] Cells { get; private set; }
        /// <summary>Vysvetleni klasifikace per prstenec; null = bez gridu.</summary>
        public ProfileVerdict[] Verdicts { get; private set; }
        /// <summary>Pocet bunek, jejichz trida NESEDI s prepoctem (jina konfigurace pri zaznamu,
        /// nebo chyba) - nastroj to musi ukazat, jinak by vysvetleni lhalo.</summary>
        public int Mismatches { get; private set; }
        /// <summary>Referencni rovina celeho snimku (spolecna pro vsechny azimuty).</summary>
        public PlaneParams Plane { get; private set; }

        /// <summary>
        /// Sestavi profil azimutu <paramref name="azimuth"/>. <paramref name="grid"/> muze byt null
        /// (pak jen surove body a rozdeleni sloupcu podle <paramref name="columnsPerCell"/>).
        /// </summary>
        /// <param name="cfg">Konfigurace, se kterou grid vznikl (prahy klasifikace). Runtime pouziva
        /// vychozi (<c>ARBotRuntime</c>), proto je default <c>new PolarGridConfig()</c>.</param>
        public static SceneProfile Extract(Image<Gray16> depth, IDepthCameraProjection proj,
                                           PolarTraversabilityGrid grid, int azimuth,
                                           PolarGridConfig cfg = null, int columnsPerCell = 16)
        {
            if (depth == null) throw new ArgumentNullException(nameof(depth));
            if (proj == null) throw new ArgumentNullException(nameof(proj));
            cfg ??= new PolarGridConfig();

            int W = depth.Width, H = depth.Height;
            int n = grid?.ColumnsPerCell > 0 ? grid.ColumnsPerCell : columnsPerCell;
            int A = grid?.AzimuthCount > 0 ? grid.AzimuthCount : W / n;
            int trim = Math.Max(0, (W - A * n) / 2);
            azimuth = Math.Clamp(azimuth, 0, Math.Max(0, A - 1));

            var p = new SceneProfile
            {
                Azimuth = azimuth,
                ColumnFrom = trim + azimuth * n,
                ColumnTo = Math.Min(W, trim + (azimuth + 1) * n),
            };

            var table = proj.Camera2DToCamera3D;
            var m = proj.Transformation;
            int tblH = table.GetLength(0), tblW = table.GetLength(1);
            var data = depth.Data;
            var edges = grid?.RadialEdges;
            bool withGrid = grid != null && grid.RadialCount > 0;

            var pts = new List<ProfilePoint>((p.ColumnTo - p.ColumnFrom) * H);
            for (int y = 0; y < H && y < tblH; y++)
            {
                int rowByte = y * W * 2;
                for (int x = p.ColumnFrom; x < p.ColumnTo && x < tblW; x++)
                {
                    int o = rowByte + x * 2;
                    int d = data[o] | (data[o + 1] << 8);
                    if (d <= 0 || d >= 65535) continue;          // nezmereny pixel (jako BuildGrid)
                    float dm = d * 0.001f;
                    var ray = table[y, x];
                    var w3 = Vector3.Transform(new Vector3(ray.X * dm, ray.Y * dm, dm), m);
                    float r = MathF.Sqrt(w3.X * w3.X + w3.Y * w3.Y);

                    // Do gridu jde bod jen v [MinRange, MaxRange] a uvnitr hran - stejne podminky.
                    int rb = -1;
                    if (withGrid && r >= cfg.MinRangeM && r <= cfg.MaxRangeM)
                        rb = grid.RadialBin(r);

                    pts.Add(new ProfilePoint
                    {
                        Range = r, Z = w3.Z, Column = (short)x, Row = (short)y, Radial = (short)rb,
                    });
                }
            }
            p.Points = pts.ToArray();

            if (withGrid)
                p.FillGrid(grid, azimuth, cfg);
            return p;
        }

        private void FillGrid(PolarTraversabilityGrid grid, int a, PolarGridConfig cfg)
        {
            int A = grid.AzimuthCount, R = grid.RadialCount;
            var cells = grid.Cells;
            Edges = grid.RadialEdges;

            var plane = CameraFrameProcessor.FitReferencePlane(cells, cfg, new List<Point4D>());
            Plane = plane;

            // Odchylky celeho gridu - stoupani se meri i vuci azimutovym sousedum.
            var dev = new float[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                dev[i] = cells[i].Count >= cfg.MinPointsPerCell
                    ? CameraFrameProcessor.Deviation(cells[i], plane) : 0f;

            Cells = new PolarCell[R];
            Verdicts = new ProfileVerdict[R];
            int mismatches = 0;
            for (int r = 0; r < R; r++)
            {
                int idx = a * R + r;
                var c = cells[idx];
                Cells[r] = c;

                var v = new ProfileVerdict();
                if (c.Count >= cfg.MinPointsPerCell)
                {
                    float rng = MathF.Sqrt(c.MeanX * c.MeanX + c.MeanY * c.MeanY);
                    v.Evaluated = true;
                    v.Range = rng;
                    // Rovina z = -(v.X·x + v.Y·y + v.A), protoze v.Z = 1 (viz PlaneParams).
                    v.PlaneZ = -(plane.v.X * c.MeanX + plane.v.Y * c.MeanY + plane.v.A);
                    v.Deviation = dev[idx];
                    v.DeviationLimit = cfg.MaxHeightDev(rng);
                    v.Rough = c.StdZ;
                    v.RoughLimit = cfg.RoughObstacleFactor * cfg.RoughRef(rng);
                    v.Slope = CameraFrameProcessor.MaxNeighborSlope(cfg, cells, dev, a, r, A, R, idx);
                    v.SlopeLimit = cfg.MaxSlope;
                }
                Verdicts[r] = v;
                if (v.ImpliedClass != c.Class) mismatches++;
            }
            Mismatches = mismatches;
        }

        /// <summary>Vyska referencni roviny v bode <c>(x, y)</c> ramce robotu [m].</summary>
        public float PlaneZAt(float x, float y)
            => -(Plane.v.X * x + Plane.v.Y * y + Plane.v.A);
    }
}
