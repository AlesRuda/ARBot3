using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Platformne nezavisly synchronni procesor snimku kamery: z jednoho <see cref="CameraFrame"/>
    /// dopocte primo do ramce (a) pravdepodobnost sjizdnosti (<see cref="CameraFrame.ImageProbability"/>)
    /// pres volitelny <see cref="IBackProject"/> a (b) robot-centricky polarni grid sjizdnosti
    /// (<see cref="CameraFrame.Grid"/>) z hloubkoveho obrazu. Vola se SYNCHRONNE na vlakne kamery,
    /// takze <see cref="cloud"/> buffer i <see cref="edgeCache"/> jsou bez zamku (jedna instance = jedno
    /// vlakno kamery).
    ///
    /// <para>Nahrazuje drivejsi asynchronni stupne <c>BackProjectProcessor</c> + <c>DepthTraversabilityProcessor</c>
    /// (viz doc/plan-camera-vision-refactor.md). Jadro vypoctu gridu (<see cref="BuildGrid"/>) je prenesene
    /// 1:1 z <c>DepthTraversabilityProcessor.BuildGrid</c> (vcetne nativni SIMD cesty a fitu roviny).</para>
    ///
    /// Postup <see cref="BuildGrid"/> (per snimek):
    /// 1. Depth -> point cloud v robot-rel. ramci pres projekci kamery
    ///    (<see cref="IDepthCameraProjection.Camera2DToCamera3D"/> + <see cref="IDepthCameraProjection.Transformation"/>).
    ///    Projekce MUSI byt robot-centricka (SetOrientation dostal transformaci kamery vuci telu robotu,
    ///    NE svetovou pozu) - detekce nezavisi na kvalite lokalizace.
    /// 2. Azimut = skupina N sloupcu obrazu; radialne dle predpoctenych hran.
    /// 3. Fit referencni roviny z blizkych nizkych bunek; klasifikace Free/Obstacle/Unknown
    ///    (odchylka od roviny, drsnost, stoupani vuci sousedum) + duvera.
    /// </summary>
    public sealed class CameraFrameProcessor : ICameraFrameProcessor, IDisposable
    {
        private readonly Func<string, IDepthCameraProjection> resolveProjection;
        private readonly PolarGridConfig cfg;
        private readonly IBackProject backProject;

        // Cache radialnich hran per projekce (geometrie kamery je stala).
        private readonly Dictionary<IDepthCameraProjection, RadialEdge[]> edgeCache
            = new Dictionary<IDepthCameraProjection, RadialEdge[]>();

        // Volitelny CSV log casu (diagnostika latence). Zapisuje se z vlakna kamery (jedna instance).
        private TextWriter diag;
        private int diagSeq;
        private bool disposed;

        // Znovupouzity buffer point cloudu pro nativni transform (jen vlakno kamery -> bez zamku).
        private Point4D[] cloud;

        // Znovupouzity docasny buffer pro resize RGB pred BackProject (jen vlakno kamery -> bez zamku).
        // Je transientni (nikdy neopousti procesor), takze staci jedna instance sdilena pres snimky.
        private Image<BGR32> resizeTemp;

        // Znovupouzite transientni buffery vypoctu gridu (jen vlakno kamery). Puvodne se alokovaly per
        // snimek (acc ~79 KB, dev, List bodu roviny) -> zbytecny GC churn na vlakne kamery. Nejsou nikdy
        // predavany ven (grid.Cells se stale alokuji cerstve, protoze je drzi async odberatele).
        private Accum[] accBuf;
        private float[] devBuf;
        private readonly List<Point4D> planePts = new List<Point4D>();

        private void EnsureCloud(int len)
        {
            if (cloud == null || cloud.Length < len) cloud = new Point4D[len];
        }

        /// <param name="projectionResolver">Vrati projekci pro kameru dle <see cref="CameraFrame.Name"/>,
        /// nebo null, neni-li (jeste) k dispozici (napr. kamera se pripojuje line - grid se preskoci).</param>
        /// <param name="config">Konfigurace gridu; null = vychozi.</param>
        /// <param name="backProject">Volitelny prevod barvy na pravdepodobnost sjizdnosti; null = nepocitat
        /// (<see cref="CameraFrame.ImageProbability"/> zustane, jak prislo z kamery).</param>
        /// <param name="diagnosticsCsvPath">Volitelna cesta k CSV logu casu (wait/compute per snimek) pro
        /// diagnostiku latence; null = nelogovat.</param>
        public CameraFrameProcessor(
            Func<string, IDepthCameraProjection> projectionResolver,
            PolarGridConfig config = null,
            IBackProject backProject = null,
            string diagnosticsCsvPath = null)
        {
            this.resolveProjection = projectionResolver ?? throw new ArgumentNullException(nameof(projectionResolver));
            this.cfg = config ?? new PolarGridConfig();
            this.backProject = backProject;
            this.diag = OpenDiag(diagnosticsCsvPath);
        }

        /// <param name="projections">Projekce per kamera (klic = <see cref="CameraFrame.Name"/>).</param>
        /// <param name="config">Konfigurace gridu; null = vychozi.</param>
        /// <param name="backProject">Volitelny prevod barvy na pravdepodobnost; null = nepocitat.</param>
        /// <param name="diagnosticsCsvPath">Volitelna cesta k CSV logu casu; null = nelogovat.</param>
        public CameraFrameProcessor(
            IReadOnlyDictionary<string, IDepthCameraProjection> projections,
            PolarGridConfig config = null,
            IBackProject backProject = null,
            string diagnosticsCsvPath = null)
            : this(name => (projections ?? throw new ArgumentNullException(nameof(projections)))
                              .TryGetValue(name, out var p) ? p : null,
                   config, backProject, diagnosticsCsvPath)
        {
        }

        /// <inheritdoc/>
        public void Process(CameraFrame frame)
        {
            if (frame == null) return;

            // Diagnostika je-li zapnuta (diag != null). Pri vypnute diagnostice (soutez) NEmerime nic
            // navic - zadne GC dotazy ani DateTime.Now na vlakne kamery.
            bool diagOn = diag != null;

            // wait = od porizeni snimku (T_in) po start dopoctu = kamera grab + kopie do bufferu.
            double waitMs = diagOn ? (DateTime.Now - frame.TimeStamp).TotalMilliseconds : 0;

            // DIAGNOSTIKA GC: alokace vlakna kamery behem Process (izoluje procesor) a gen2 pocitadlo
            // pred/po (potvrdi, zda spicka v compute = blokujici gen2 pauza).
            long camAlloc0 = diagOn ? GC.GetAllocatedBytesForCurrentThread() : 0;
            int gen2Before = diagOn ? GC.CollectionCount(2) : 0;

            var sw = diagOn ? System.Diagnostics.Stopwatch.StartNew() : null;

            // (1) Pravdepodobnost sjizdnosti (barva -> Gray). BackProject je vstupem pro RIZENI robota
            // (viz decisions.md 2026-08-01), proto se pocita vzdy, kdyz je RGB k dispozici. Vysledek se
            // zapisuje do znovupouziteho bufferu frame.ImageProbability (soucast poolovaneho capture slotu,
            // krok 4) - kdyz ma spravny rozmer, prepise se bez alokace.
            if (backProject != null && frame.ImageRGB != null)
                frame.ImageProbability = ComputeProbability(frame.ImageRGB, frame.ImageProbability);

            // (2) Polarni grid z hloubky (jen kdyz je depth i projekce k dispozici).
            int cells = 0;
            if (frame.ImageDepth != null)
            {
                var proj = resolveProjection(frame.Name ?? string.Empty);
                if (proj != null)
                {
                    var grid = BuildGrid(frame.ImageDepth, proj);
                    frame.Grid = grid;
                    cells = grid?.Cells?.Length ?? 0;
                }
            }

            if (!diagOn) return;   // soutez: bez diagnostiky nemerime ani nezapisujeme

            sw.Stop();
            double computeMs = sw.Elapsed.TotalMilliseconds;
            if (frame.Grid != null)
                frame.Grid.ComputeMs = computeMs;

            long camAllocKb = (GC.GetAllocatedBytesForCurrentThread() - camAlloc0) / 1024;
            int gen2 = GC.CollectionCount(2) - gen2Before;   // >0 = behem Process probehla gen2 GC
            WriteDiag(frame.Name, frame.TimeStamp, waitMs, computeMs, cells, camAllocKb, gen2);
        }

        /// <summary>
        /// Barva -> pravdepodobnost sjizdnosti (jako drivejsi BackProjectProcessor, jen bez fan-outu).
        /// Znovupouzije <paramref name="reuseProb"/> (poolovany buffer capture slotu), kdyz ma spravny
        /// rozmer - bez alokace per snimek (krok 4). Docasny resize buffer je take znovupouzity.
        /// </summary>
        private Image<Gray> ComputeProbability(Image<BGR32> rgb, Image<Gray> reuseProb)
        {
            var size = backProject.Size(rgb.Width, rgb.Height);
            Image<BGR32> src = rgb;
            if (size.Width != rgb.Width || size.Height != rgb.Height)
            {
                resizeTemp = CameraFramePool.Ensure(resizeTemp, size.Width, size.Height);
                resizeTemp.Resize(rgb);
                src = resizeTemp;
            }
            var prob = CameraFramePool.Ensure(reuseProb, size.Width, size.Height);
            backProject.Process(src, prob);
            return prob;
        }

        private static TextWriter OpenDiag(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var w = new StreamWriter(path, append: false) { AutoFlush = true };
                // cam_alloc_kb = alokace vlakna kamery behem Process; proc_alloc_kb = alokace VSECH vlaken
                // od predchoziho snimku (odhali churn na UI/recorder vlaknech); gen2 = probehla-li gen2 GC.
                w.WriteLine("seq;capture;camera;wait_ms;compute_ms;cells;cam_alloc_kb;proc_alloc_kb;gen2");
                return w;
            }
            catch { return null; }
        }

        private long prevTotalAlloc;

        private void WriteDiag(string cam, DateTime capture, double waitMs, double computeMs, int cells,
                               long camAllocKb, int gen2)
        {
            var w = diag;
            if (w == null) return;
            try
            {
                // Alokace vsech vlaken od predchoziho snimku (proces-wide delta).
                long tot = GC.GetTotalAllocatedBytes(false);
                long procKb = prevTotalAlloc == 0 ? 0 : (tot - prevTotalAlloc) / 1024;
                prevTotalAlloc = tot;

                w.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0};{1:HH:mm:ss.fff};{2};{3:F1};{4:F1};{5};{6};{7};{8}",
                    diagSeq++, capture, cam, waitMs, computeMs, cells, camAllocKb, procKb, gen2));
            }
            catch { /* diagnostika nesmi shodit zpracovani */ }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            var w = diag;
            diag = null;
            try { w?.Flush(); w?.Dispose(); } catch { }
        }

        // Interni akumulator bunky (sumy pro teziste, vysku a drsnost).
        private struct Accum
        {
            public int Count;
            public double SumX, SumY, SumZ, SumZ2;
            public float MaxZ;
            public float MinR;
        }

        /// <summary>
        /// Spocte polarni grid sjizdnosti z hloubkoveho obrazu. Verejne kvuli testovatelnosti
        /// (lze volat s libovolnou <see cref="IDepthCameraProjection"/>). Vraci null, pokud
        /// geometrie nedovoli sestavit ani jeden prstenec.
        /// </summary>
        public PolarTraversabilityGrid BuildGrid(Image<Gray16> depth, IDepthCameraProjection proj)
        {
            if (depth == null) throw new ArgumentNullException(nameof(depth));
            if (proj == null) throw new ArgumentNullException(nameof(proj));

            int W = depth.Width, H = depth.Height;
            int trim = cfg.EdgeColumnTrim;
            int usableW = W - 2 * trim;
            if (usableW <= 0)
                throw new ArgumentException($"EdgeColumnTrim {trim} je prilis velky pro sirku {W}.");
            if (usableW % cfg.ColumnsPerCell != 0)
                throw new ArgumentException(
                    $"Pouzitelna sirka {usableW} neni delitelna ColumnsPerCell {cfg.ColumnsPerCell}.");

            int A = usableW / cfg.ColumnsPerCell;

            RadialEdge[] edges = GetRadialEdges(proj, W, H);
            int R = edges.Length - 1;
            if (R <= 0) return null;

            // Znovupouzity acc buffer (vynulovany) misto alokace per snimek.
            if (accBuf == null || accBuf.Length < A * R) accBuf = new Accum[A * R];
            var acc = accBuf;
            Array.Clear(acc, 0, A * R);
            var table = proj.Camera2DToCamera3D;
            var m = proj.Transformation;
            int tblH = table.GetLength(0), tblW = table.GetLength(1);
            var data = depth.Data;
            int len = W * H;

            // Depth -> point cloud: bud nativne (SIMD, DepthTransform2Impl - mm->m interne, vystup v
            // OPACNEM poradi: cloud[len-1-p] = bod pixelu p), nebo managed (Vector3.Transform per pixel).
            // Nativni cesta pouziva znovupouzity buffer (zadna alokace per snimek). Managed zustava
            // jako referencni/fallback (ekvivalence overena testem).
            bool native = cfg.UseNativeTransform;
            if (native)
            {
                EnsureCloud(len);
                var rotate = NativeComputeUnit.Transformation(m);
                NativeComputeUnit.DepthTransform2Impl(cloud, table, rotate, data, len);
            }

            for (int y = 0; y < H; y++)
            {
                int rowByte = y * W * 2;
                for (int x = trim; x < trim + usableW; x++)
                {
                    float rx, ry, rz;
                    if (native)
                    {
                        var wp = cloud[len - 1 - (x + y * W)];   // opacne poradi
                        if (wp.A == 0f) continue;                // nezmereny pixel -> [0,0,0,0]
                        rx = wp.X; ry = wp.Y; rz = wp.Z;
                    }
                    else
                    {
                        if (y >= tblH || x >= tblW) continue;
                        int o = rowByte + x * 2;
                        int d = data[o] | (data[o + 1] << 8);
                        if (d <= 0 || d >= 65535) continue;      // nezmereny pixel
                        float dm = d * 0.001f;
                        var ray = table[y, x];
                        var w3 = System.Numerics.Vector3.Transform(
                            new System.Numerics.Vector3(ray.X * dm, ray.Y * dm, dm), m);
                        rx = w3.X; ry = w3.Y; rz = w3.Z;
                    }

                    float r = MathF.Sqrt(rx * rx + ry * ry);
                    if (r < cfg.MinRangeM || r > cfg.MaxRangeM) continue;
                    int rb = RadialBin(edges, r);
                    if (rb < 0) continue;

                    int ab = (x - trim) / cfg.ColumnsPerCell;
                    int idx = ab * R + rb;

                    int c = acc[idx].Count;
                    acc[idx].Count = c + 1;
                    acc[idx].SumX += rx;
                    acc[idx].SumY += ry;
                    acc[idx].SumZ += rz;
                    acc[idx].SumZ2 += (double)rz * rz;
                    if (c == 0) { acc[idx].MaxZ = rz; acc[idx].MinR = r; }
                    else
                    {
                        if (rz > acc[idx].MaxZ) acc[idx].MaxZ = rz;
                        if (r < acc[idx].MinR) acc[idx].MinR = r;
                    }
                }
            }

            var grid = new PolarTraversabilityGrid
            {
                AzimuthCount = A,
                ColumnsPerCell = cfg.ColumnsPerCell,
                RadialEdges = edges,
                Cells = new PolarCell[A * R],
            };

            // 1. pruchod: statistiky bunek. Iterujeme jen platnych A*R (acc muze byt znovupouzity vetsi).
            for (int i = 0; i < grid.Cells.Length; i++)
            {
                var a = acc[i];
                var cell = new PolarCell();
                if (a.Count > 0)
                {
                    float mx = (float)(a.SumX / a.Count);
                    float my = (float)(a.SumY / a.Count);
                    float mz = (float)(a.SumZ / a.Count);
                    double var = a.SumZ2 / a.Count - (double)mz * mz;
                    cell.Count = a.Count;
                    cell.MeanX = mx;
                    cell.MeanY = my;
                    cell.MeanZ = mz;
                    cell.StdZ = (float)Math.Sqrt(Math.Max(0, var));
                    cell.MaxZ = a.MaxZ;
                    cell.EdgeRange = a.MinR;
                }
                else
                {
                    cell.EdgeRange = float.NaN;
                }
                cell.Class = TraversabilityClass.Unknown;
                grid.Cells[i] = cell;
            }

            // 2. Referencni rovina z blizkych nizkych bunek.
            var plane = FitReferencePlane(grid.Cells);

            // 3. Klasifikace + duvera.
            Classify(grid, R, plane);

            return grid;
        }

        /// <summary>Vrati (a nacachuje) radialni hrany pro danou projekci.</summary>
        public RadialEdge[] GetRadialEdges(IDepthCameraProjection proj, int width, int height)
        {
            if (!edgeCache.TryGetValue(proj, out var edges))
            {
                edges = cfg.BuildRadialEdges(proj, width, height);
                edgeCache[proj] = edges;
            }
            return edges;
        }

        // Radialni bin (edges rostouci dle Range): index r, kde edges[r].Range <= range < edges[r+1].Range; jinak -1.
        // Puleni intervalu - vola se jednou na kazdy platny pixel, proto ne linearne.
        private static int RadialBin(RadialEdge[] edges, float range)
        {
            int n = edges.Length;
            if (range < edges[0].Range || range >= edges[n - 1].Range) return -1;

            // Invariant: edges[lo].Range <= range < edges[hi].Range; zuzujeme, az hi-lo == 1.
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) >> 1;
                if (range < edges[mid].Range) hi = mid;
                else lo = mid;
            }
            return lo;
        }

        // Fit referencni roviny z bunek s dost body, blizko a s malou vyskou (odfiltruje prekazky).
        private PlaneParams FitReferencePlane(PolarCell[] cells)
        {
            var pts = planePts;   // znovupouzity seznam (misto alokace per snimek)
            pts.Clear();
            float maxR2 = cfg.PlaneFitMaxRangeM * cfg.PlaneFitMaxRangeM;
            foreach (var c in cells)
            {
                if (c.Count < cfg.MinPointsPerCell) continue;
                float r2 = c.MeanX * c.MeanX + c.MeanY * c.MeanY;
                if (r2 > maxR2) continue;
                if (Math.Abs(c.MeanZ) > cfg.PlaneFitMaxAbsHeightM) continue;
                pts.Add(new Point4D { X = c.MeanX, Y = c.MeanY, Z = c.MeanZ, A = 1 });
            }

            if (pts.Count >= 3)
                return new PlaneParams(pts);

            // Fallback: rovina zeme z=0 -> odchylka bodu p je primo p.Z (v = (0,0,1,0)).
            return new PlaneParams { v = new Point4D { X = 0, Y = 0, Z = 1, A = 0 } };
        }

        // Znamenkova odchylka teziste bunky od roviny (v-vektor viz PlaneParams).
        private static float Deviation(in PolarCell c, in PlaneParams plane)
        {
            var p = new Point4D { X = c.MeanX, Y = c.MeanY, Z = c.MeanZ, A = 1 };
            return p * plane.v;
        }

        private void Classify(PolarTraversabilityGrid grid, int R, PlaneParams plane)
        {
            int A = grid.AzimuthCount;
            var cells = grid.Cells;

            // Predpocitane odchylky (pro stoupani vuci sousedum) - znovupouzity buffer.
            if (devBuf == null || devBuf.Length < cells.Length) devBuf = new float[cells.Length];
            var dev = devBuf;
            for (int i = 0; i < cells.Length; i++)
                dev[i] = cells[i].Count >= cfg.MinPointsPerCell ? Deviation(cells[i], plane) : 0f;

            for (int a = 0; a < A; a++)
            {
                for (int r = 0; r < R; r++)
                {
                    int idx = a * R + r;
                    var cell = cells[idx];
                    if (cell.Count < cfg.MinPointsPerCell)
                    {
                        cell.Class = TraversabilityClass.Unknown;
                        cell.Confidence = 0f;
                        cells[idx] = cell;
                        continue;
                    }

                    float rng = MathF.Sqrt(cell.MeanX * cell.MeanX + cell.MeanY * cell.MeanY);
                    float adev = Math.Abs(dev[idx]);
                    float rough = cell.StdZ;
                    float slope = MaxNeighborSlope(cells, dev, a, r, A, R, idx);

                    float roughRef = cfg.RoughRef(rng);
                    bool obstacle =
                        adev > cfg.MaxHeightDev(rng) ||
                        rough > cfg.RoughObstacleFactor * roughRef ||
                        slope > cfg.MaxSlope;

                    cell.Class = obstacle ? TraversabilityClass.Obstacle : TraversabilityClass.Free;

                    // Duvera = pocet vzorku x dosah x drsnost (vse 0..1). Klasifikovana bunka (>= podlaha)
                    // ma vzdy kladny fCount (podlaha -> male kladne, cil a vic -> 1); confidence == 0 znaci Unknown.
                    float fCount = Clamp01((cell.Count - cfg.MinPointsPerCell + 1) /
                                           (float)Math.Max(1, cfg.TargetPointsPerCell - cfg.MinPointsPerCell + 1));
                    float fRange = Clamp01(1f - (rng / cfg.MaxRangeM) * (rng / cfg.MaxRangeM));
                    float roughLimit = cfg.RoughObstacleFactor * roughRef;
                    float fRough = roughLimit > 0 ? Clamp01(1f - rough / roughLimit) : 1f;
                    cell.Confidence = fCount * fRange * fRough;

                    cells[idx] = cell;
                }
            }
        }

        // Max stoupani (plane-rel.) vuci radialnim a azimutovym sousedum.
        private float MaxNeighborSlope(PolarCell[] cells, float[] dev, int a, int r, int A, int R, int idx)
        {
            float max = 0f;
            var self = cells[idx];
            void Consider(int na, int nr)
            {
                if (na < 0 || na >= A || nr < 0 || nr >= R) return;
                int nidx = na * R + nr;
                var n = cells[nidx];
                if (n.Count < cfg.MinPointsPerCell) return;
                float dx = self.MeanX - n.MeanX;
                float dy = self.MeanY - n.MeanY;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist < 1e-4f) return;
                float s = Math.Abs(dev[idx] - dev[nidx]) / dist;
                if (s > max) max = s;
            }
            Consider(a, r - 1);
            Consider(a, r + 1);
            Consider(a - 1, r);
            Consider(a + 1, r);
            return max;
        }

        private static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
    }
}
