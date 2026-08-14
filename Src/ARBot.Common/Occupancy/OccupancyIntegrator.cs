using System;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Vision;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Zapis jednoho <see cref="CameraFrame"/> do kartezskeho <see cref="OccupancyGrid"/> - oba kanaly
    /// v jednom pruchodu. Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para><b>Gather, ne scatter:</b> prochazi se KARTEZSKE bunky v okoli robotu a pro kazdou se
    /// dohleda, co o nich senzor rika. Blizko robotu je polarni bunka mensi nez 5 cm (vic polarnich
    /// bunek na jednu kartezskou), daleko je vetsi (jedna polarni pres mnoho kartezskych) - scatter by
    /// daleko delal diry, gather je korektni v obou smerech a bez aliasingu.</para>
    ///
    /// <para><b>Jak se hleda polarni bunka:</b> stred kartezske bunky se PROMITNE DO OBRAZU
    /// (<see cref="ICameraProjection.Transform"/>, rovina zeme z = 0) a azimutova bunka se vezme z jeho
    /// SLOUPCE. Tim se presne invertuje mapovani, ktere pouzil <see cref="CameraFrameProcessor.BuildGrid"/>
    /// (azimut = skupina sloupcu). Uhlem to nejde: u sklonene kamery neni sloupec obrazu konstantnim
    /// azimutem - azimut pozemniho bodu na jednom sloupci se meni s radkem az o sirku cele bunky
    /// (dolozeno testem <c>PolarGridLookupTest.SloupecObrazuNeniKonstantniAzimut</c>).
    /// Radialni prstenec se bere ze vzdalenosti, protoze presne tak ho pocital i BuildGrid.</para>
    ///
    /// <para><b>Semanticky kanal</b> (<see cref="CameraFrame.ImageProbability"/>) se vzorkuje stejnym
    /// gatherem, jen barevnou projekci - u bunky ZEME je rovinny predpoklad presne platny. Respektuje
    /// se okluze: za prvni prekazkou v danem azimutu se uz nevzorkuje (jinak by se barva prekazky
    /// pripsala zemi za ni). Naopak ZA dosahem hloubky se vzorkovat smi - barva dohledne dal a je to
    /// jediny zdroj informace o ceste pred robotem.</para>
    ///
    /// <para><b>Vlaknova bezpecnost:</b> zadna (znovupouzity buffer stinu). Jedna instance = jedno vlakno.</para>
    /// </summary>
    public sealed class OccupancyIntegrator
    {
        private readonly OccupancyGrid grid;
        private readonly OccupancyIntegratorConfig cfg;

        // Pro kazdy azimut nejblizsi radialni prstenec s prekazkou (int.MaxValue = zadna) - stin.
        private int[] shadowFrom = new int[0];

        /// <summary>Konfigurace zapisu.</summary>
        public OccupancyIntegratorConfig Config => cfg;

        /// <summary>
        /// DIAGNOSTIKA posledniho <see cref="Integrate"/>: kde presne se zapisy ztraceji. Kdyz je
        /// occupancy grid prazdny, prvni nenulove pole zprava rekne, ktery clanek retezu selhal
        /// (chybejici projekce -&gt; bunky mimo zorne pole -&gt; azimut/prstenec mimo grid -&gt;
        /// same Unknown -&gt; stin). Pocitadla jsou jen inkrementy intu v uz existujici smycce.
        /// </summary>
        public IntegrateStats LastStats { get; private set; }

        /// <summary>Vysledek jednoho <see cref="Integrate"/> - viz <see cref="LastStats"/>.</summary>
        public struct IntegrateStats
        {
            /// <summary>Prisel ve snimku pouzitelny polarni grid?</summary>
            public bool HasPolarGrid;
            /// <summary>Byla k dispozici projekce hloubkoveho streamu?</summary>
            public bool HasDepthProjection;
            /// <summary>Prisla ve snimku probability (barva -&gt; sjizdnost)?</summary>
            public bool HasProbability;
            /// <summary>Byla k dispozici projekce barevneho streamu?</summary>
            public bool HasColorProjection;

            /// <summary>Bunek gridu v dosahu, ktere se vubec zkoumaly.</summary>
            public int CellsInRange;
            /// <summary>Z toho se jich promitlo do hloubkoveho obrazu.</summary>
            public int DepthProjected;
            /// <summary>Z toho padlo do platneho azimutu polarniho gridu.</summary>
            public int AzimuthOk;
            /// <summary>Z toho padlo i do platneho radialniho prstence.</summary>
            public int RadialOk;
            /// <summary>Zapisu do kanalu geometrie (Free + Obstacle).</summary>
            public int WroteOcc;
            /// <summary>Bunek, kterym barvu zakazal stin za prekazkou.</summary>
            public int ColorShadowed;
            /// <summary>Bunek, kterym barvu zakazala nulova duvera (prilis daleko).</summary>
            public int ColorNoConfidence;
            /// <summary>Z toho se jich promitlo do barevneho obrazu.</summary>
            public int ColorProjected;
            /// <summary>Zapisu do kanalu semantiky.</summary>
            public int WroteRoad;
            /// <summary>Bunek, do kterych se zapsal aspon jeden kanal (navratova hodnota Integrate).</summary>
            public int Touched;

            /// <inheritdoc/>
            public override string ToString()
                => $"depth[grid={(HasPolarGrid ? 1 : 0)} proj={(HasDepthProjection ? 1 : 0)}] "
                 + $"color[prob={(HasProbability ? 1 : 0)} proj={(HasColorProjection ? 1 : 0)}] "
                 + $"cells={CellsInRange} dproj={DepthProjected} az={AzimuthOk} rad={RadialOk} occ={WroteOcc} "
                 + $"shadow={ColorShadowed} noconf={ColorNoConfidence} cproj={ColorProjected} road={WroteRoad} "
                 + $"touched={Touched}";
        }

        /// <param name="grid">Cilovy occupancy grid.</param>
        /// <param name="config">Konfigurace; null = vychozi.</param>
        public OccupancyIntegrator(OccupancyGrid grid, OccupancyIntegratorConfig config = null)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            cfg = config ?? new OccupancyIntegratorConfig();
            cfg.Validate();
        }

        /// <summary>
        /// Zapise snimek do gridu. Grid se PREDEM vycentruje na polohu robotu
        /// (<see cref="OccupancyGrid.Recenter"/>).
        /// </summary>
        /// <param name="frame">Snimek s polarnim gridem a/nebo probability.</param>
        /// <param name="depthProjection">Projekce HLOUBKOVEHO streamu s robot-centrickou orientaci
        /// (stejna, jakou dostal <see cref="CameraFrameProcessor"/>). null = geometricky kanal se
        /// nezapisuje.</param>
        /// <param name="colorProjection">Projekce BAREVNEHO streamu s robot-centrickou orientaci.
        /// null = semanticky kanal se nezapisuje.</param>
        /// <param name="robotX">Poloha robotu [m, world ENU].</param>
        /// <param name="robotY">Poloha robotu [m, world ENU].</param>
        /// <param name="heading">Kurz robotu [rad] (0 = vychod, +CCW).</param>
        /// <returns>Pocet bunek, do kterych se neco zapsalo (diagnostika).</returns>
        public int Integrate(CameraFrame frame,
                             ICameraProjection depthProjection, ICameraProjection colorProjection,
                             double robotX, double robotY, double heading)
        {
            LastStats = default;
            if (frame == null) return 0;

            var polar = frame.Grid;
            bool useDepth = polar != null && polar.RadialCount > 0 && polar.AzimuthCount > 0
                            && depthProjection != null;
            bool useColor = frame.ImageProbability != null && colorProjection != null;

            var stats = new IntegrateStats
            {
                HasPolarGrid = polar != null && polar.RadialCount > 0 && polar.AzimuthCount > 0,
                HasDepthProjection = depthProjection != null,
                HasProbability = frame.ImageProbability != null,
                HasColorProjection = colorProjection != null,
            };
            LastStats = stats;

            if (!useDepth && !useColor) return 0;

            grid.Recenter(robotX, robotY);

            double maxRange = ResolveMaxRange(polar, useDepth);
            if (maxRange <= 0) return 0;

            if (useDepth) BuildShadow(polar);

            // Prevod svetove bunky do robot-rel. ramce = rotace o -heading.
            double cosH = Math.Cos(heading), sinH = Math.Sin(heading);

            int span = (int)Math.Ceiling(maxRange / grid.Resolution) + 1;
            int cx0 = grid.CellX(robotX), cy0 = grid.CellY(robotY);
            double maxRange2 = maxRange * maxRange;

            // Probability muze mit jine rozliseni nez barevny obraz, do jehoz pixelu projekce miri
            // (BackProject si velikost voli sam) - stejna konvence jako v PathEdgeFinderItem.Scale*.
            var prob = frame.ImageProbability;
            double probScaleX = 1, probScaleY = 1;
            if (useColor && frame.ImageRGB != null && prob.Width > 0 && prob.Height > 0)
            {
                probScaleX = (double)frame.ImageRGB.Width / prob.Width;
                probScaleY = (double)frame.ImageRGB.Height / prob.Height;
            }

            int touched = 0;
            for (int cy = cy0 - span; cy <= cy0 + span; cy++)
            {
                for (int cx = cx0 - span; cx <= cx0 + span; cx++)
                {
                    if (!grid.Contains(cx, cy)) continue;

                    double dx = grid.CenterX(cx) - robotX;
                    double dy = grid.CenterY(cy) - robotY;
                    double r2 = dx * dx + dy * dy;
                    if (r2 > maxRange2) continue;
                    stats.CellsInRange++;

                    // Do robot-rel. ramce (X vpred, Y vlevo).
                    float rx = (float)(dx * cosH + dy * sinH);
                    float ry = (float)(-dx * sinH + dy * cosH);
                    double range = Math.Sqrt(r2);

                    bool wrote = false;
                    int azimuth = -1;
                    bool beyondDepthRange = true;   // dokud hloubka bunku nezaradi, je "za dosahem"

                    if (useDepth)
                    {
                        float col = 0, row = 0;
                        if (depthProjection.Transform(rx, ry, ref col, ref row))
                        {
                            stats.DepthProjected++;
                            azimuth = polar.AzimuthBinFromColumn((int)Math.Round(col), cfg.EdgeColumnTrim);
                            if (azimuth >= 0) stats.AzimuthOk++;
                            int rb = azimuth >= 0 ? polar.RadialBin((float)range) : -1;
                            if (rb >= 0)
                            {
                                stats.RadialOk++;
                                beyondDepthRange = false;
                                var pc = polar[azimuth, rb];
                                // Unknown se NEzapisuje (Unknown != Free).
                                if (pc.Class == TraversabilityClass.Obstacle)
                                {
                                    grid.ObserveOccupied(cx, cy, pc.Confidence);
                                    stats.WroteOcc++;
                                    wrote = true;
                                }
                                else if (pc.Class == TraversabilityClass.Free)
                                {
                                    grid.ObserveFree(cx, cy, pc.Confidence);
                                    stats.WroteOcc++;
                                    wrote = true;
                                }
                            }
                        }
                    }

                    // Barvu za dosahem hloubky jen tehdy, kdyz je to povolene.
                    bool inShadow = InShadow(azimuth, polar, range, useDepth);
                    if (useColor && inShadow) stats.ColorShadowed++;
                    bool colorAllowed = useColor
                                        && (cfg.RoadBeyondDepthRange || !beyondDepthRange)
                                        && !inShadow;
                    if (colorAllowed)
                    {
                        float conf = cfg.RoadConfidence(range);
                        if (conf <= 0) stats.ColorNoConfidence++;
                        if (conf > 0)
                        {
                            float col = 0, row = 0;
                            if (colorProjection.Transform(rx, ry, ref col, ref row))
                            {
                                stats.ColorProjected++;
                                int px = (int)(col / probScaleX);
                                int py = (int)(row / probScaleY);
                                if (px >= 0 && py >= 0 && px < prob.Width && py < prob.Height)
                                {
                                    float p = cfg.ProbabilityToTraversable(prob[px, py].Value);
                                    grid.ObserveRoad(cx, cy, p, conf);
                                    stats.WroteRoad++;
                                    wrote = true;
                                }
                            }
                        }
                    }

                    if (wrote) touched++;
                }
            }

            stats.Touched = touched;
            LastStats = stats;
            return touched;
        }

        /// <summary>Dosah prochazeni okoli: z konfigurace, jinak max z dosahu polarniho gridu a
        /// dosahu barvy.</summary>
        private double ResolveMaxRange(PolarTraversabilityGrid polar, bool useDepth)
        {
            if (cfg.MaxRangeM > 0) return cfg.MaxRangeM;

            double r = cfg.RoadMaxRangeM;
            if (useDepth)
            {
                var e = polar.RadialEdges;
                r = Math.Max(r, e[e.Length - 1].Range);
            }
            // Dal nez polovina gridu nema smysl chodit (stejne by to bylo mimo okno).
            return Math.Min(r, grid.Size * grid.Resolution * 0.5);
        }

        /// <summary>Pro kazdy azimut najde nejblizsi prstenec s prekazkou - za nim je zem ve stinu.</summary>
        private void BuildShadow(PolarTraversabilityGrid polar)
        {
            int a = polar.AzimuthCount, r = polar.RadialCount;
            if (shadowFrom.Length < a) shadowFrom = new int[a];

            for (int i = 0; i < a; i++)
            {
                shadowFrom[i] = int.MaxValue;
                for (int k = 0; k < r; k++)
                {
                    if (polar[i, k].Class == TraversabilityClass.Obstacle)
                    {
                        shadowFrom[i] = k;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Je bod v dane vzdalenosti za prvni prekazkou daneho azimutu? (Tedy zem, kterou kamera
        /// nemuze videt - barva by tam patrila prekazce, ne zemi.) Bez hloubky se stin neurcuje.
        /// </summary>
        private bool InShadow(int azimuth, PolarTraversabilityGrid polar, double range, bool useDepth)
        {
            if (!useDepth || azimuth < 0) return false;

            int first = shadowFrom[azimuth];
            if (first == int.MaxValue) return false;

            // Vse od NABEZNE HRANY prvni prekazky dal je ve stinu (vcetne te prekazky same).
            float edge = polar.RadialEdges[first].Range;
            return range >= edge;
        }
    }
}
