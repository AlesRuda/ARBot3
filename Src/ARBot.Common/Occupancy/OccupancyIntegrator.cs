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
    /// se okluze: zem, kterou zakryva prekazka, se nevzorkuje (jinak by se barva prekazky pripsala
    /// zemi za ni). Ktera zem to je, rika <see cref="OccupancyIntegratorConfig.ColorShadow"/> -
    /// od 10. 10. 2026 pas za prekazkou podle jeji vysky, puvodne vse za prvni prekazkou azimutu.
    /// Naopak ZA dosahem hloubky se vzorkovat smi - barva dohledne dal a je to jediny zdroj
    /// informace o ceste pred robotem.</para>
    ///
    /// <para><b>Vlaknova bezpecnost:</b> zadna (znovupouzity buffer stinu). Jedna instance = jedno vlakno.</para>
    /// </summary>
    public sealed class OccupancyIntegrator
    {
        private readonly OccupancyGrid grid;
        private readonly OccupancyIntegratorConfig cfg;

        // Pro kazdy azimut nejblizsi radialni prstenec s prekazkou (int.MaxValue = zadna) - stin
        // pravidla FirstObstacle.
        private int[] shadowFrom = new int[0];

        // Stin podle vysky (ColorShadowMode.Height): pro bunku [azimut, prstenec] nejvzdalenejsi konec
        // stinu [m] pres prekazky v prstencich 0..prstenec vcetne (prefixove maximum podel azimutu);
        // -inf = zadna prekazka. Bod v prstenci rb je ve stinu, kdyz konec pres prstence PRED nim
        // presahuje jeho vzdalenost - dotaz na bunku je tedy jedno cteni z pole jako u FirstObstacle.
        private double[] shadowEnd = new double[0];
        // Plati pro posledni BuildShadow stin podle vysky? (false = FirstObstacle, i jako zaloha.)
        private bool shadowByHeight;

        /// <summary>
        /// Vrchol prekazky pro stin podle vysky je <c>MeanZ + TopSigmaFactor·StdZ</c>: u svisle plochy
        /// s rovnomerne rozlozenymi body (stred h/2, smerodatna odchylka h/√12) presne jeji vrchol,
        /// a proti <c>MaxZ</c> ho jeden uletly bod neposune o celou vysku. Viz
        /// <see cref="ColorShadowMode.Height"/>.
        /// </summary>
        public const float TopSigmaFactor = 1.7320508f;   // √3

        /// <summary>Kamera nize nad rovinou z = 0 ramce robotu [m] je nesmysl (spatna transformace) -
        /// stin podle vysky se pak nepocita a pouzije se <see cref="ColorShadowMode.FirstObstacle"/>.</summary>
        public const double MinCameraHeightM = 0.1;

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
            /// <summary>Pocital se stin podle vysky (<see cref="ColorShadowMode.Height"/>)? false = pravidlo
            /// prvni prekazky - nastavene, nebo jako zaloha, kdyz hloubkova projekce nenese polohu kamery.</summary>
            public bool ShadowByHeight;

            /// <inheritdoc/>
            public override string ToString()
                => $"depth[grid={(HasPolarGrid ? 1 : 0)} proj={(HasDepthProjection ? 1 : 0)}] "
                 + $"color[prob={(HasProbability ? 1 : 0)} proj={(HasColorProjection ? 1 : 0)}] "
                 + $"cells={CellsInRange} dproj={DepthProjected} az={AzimuthOk} rad={RadialOk} occ={WroteOcc} "
                 + $"shadow={ColorShadowed}{(ShadowByHeight ? "(h)" : "")} noconf={ColorNoConfidence} "
                 + $"cproj={ColorProjected} road={WroteRoad} touched={Touched}";
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

            if (useDepth) BuildShadow(depthProjection, polar);
            stats.ShadowByHeight = useDepth && shadowByHeight;

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
                    int azimuth = -1, rb = -1;
                    bool beyondDepthRange = true;   // dokud hloubka bunku nezaradi, je "za dosahem"

                    if (useDepth)
                    {
                        float col = 0, row = 0;
                        if (depthProjection.Transform(rx, ry, ref col, ref row))
                        {
                            stats.DepthProjected++;
                            azimuth = polar.AzimuthBinFromColumn((int)Math.Round(col), cfg.EdgeColumnTrim);
                            if (azimuth >= 0) stats.AzimuthOk++;
                            rb = azimuth >= 0 ? polar.RadialBin((float)range) : -1;
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
                    bool inShadow = InShadow(azimuth, rb, polar, range, useDepth);
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

        /// <summary>
        /// Pripravi stin pro dotazy <see cref="InShadow"/>: podle vysky (<see cref="ColorShadowMode.Height"/>),
        /// kdyz je nastaveny a zna se vyska kamery, jinak pro kazdy azimut nejblizsi prstenec s prekazkou.
        /// </summary>
        private void BuildShadow(ICameraProjection depthProjection, PolarTraversabilityGrid polar)
        {
            shadowByHeight = cfg.ColorShadow == ColorShadowMode.Height && BuildHeightShadow(depthProjection, polar);
            if (!shadowByHeight) BuildFirstObstacleShadow(polar);
        }

        /// <summary>
        /// Stin podle vysky. Barva bunky se bere z pixelu, kam se promita bod <c>(x, y, 0)</c> ramce
        /// robotu, takze ten pixel ukazuje prekazku presne tehdy, kdyz paprsek z kamery k bodu projde
        /// pod jejim vrcholem: pro vrchol ve vzdalenosti <c>e</c> a vysce <c>zT</c> a kameru ve vysce
        /// <c>Cz</c> plati <c>range &lt; e·Cz/(Cz − zT)</c>. Vysky jsou proto ABSOLUTNI v ramci
        /// robotu (tamtez jsou body polarniho gridu), ne nad prolozenou rovinou zeme - ta by konec
        /// stinu zmenila pomerem <c>(Cz − c)/Cz</c> (c = vyska roviny pod kamerou; nalezeno kontrolou
        /// 10. 10. 2026), a navic nad snimky, kde se rovina prolozi spatne, by stin rozbila uplne.
        ///
        /// <para>Poloha kamery je z TEZE hloubkove projekce, ze ktere vznikl polarni grid. Vzdalenost
        /// vrhace je VNEJSI hrana jeho prstence: vrchol muze lezet kdekoli v prstenci a nabezna hrana
        /// by stin zkratila az o sirku prstence × <c>Cz/(Cz − zT)</c> - do zakryte zeme by se zapsala
        /// barva prekazky. Vzdalenosti se meri od pocatku robotu, ne od paty kamery (~0,1 m bokem):
        /// priblizeni, ktere konec stinu posune nejvys o (<c>Cz/(Cz − zT)</c> − 1) × ten posun.</para>
        ///
        /// <para>Vraci false, kdyz projekce polohu kamery nenese nebo je nesmyslna (pak plati pravidlo
        /// prvni prekazky).</para>
        /// </summary>
        private bool BuildHeightShadow(ICameraProjection depthProjection, PolarTraversabilityGrid polar)
        {
            if (!(depthProjection is IDepthCameraProjection dp)) return false;
            double camZ = dp.Transformation.Translation.Z;   // vyska kamery v ramci robotu
            if (!(camZ >= MinCameraHeightM)) return false;   // i NaN

            int A = polar.AzimuthCount, R = polar.RadialCount;
            if (shadowEnd.Length < A * R) shadowEnd = new double[A * R];
            var cells = polar.Cells;
            var edges = polar.RadialEdges;
            for (int a = 0; a < A; a++)
            {
                double end = double.NegativeInfinity;
                for (int r = 0; r < R; r++)
                {
                    int idx = a * R + r;
                    ref readonly var c = ref cells[idx];
                    float top = c.MeanZ + TopSigmaFactor * c.StdZ;
                    // top <= 0: prekazka pod urovni z = 0 (prohlubne) paprsek k zemi nezakryva.
                    if (c.Class == TraversabilityClass.Obstacle && top > 0)
                    {
                        double d = top >= camZ ? double.PositiveInfinity
                                               : edges[r + 1].Range * camZ / (camZ - top);
                        if (d > end) end = d;
                    }
                    shadowEnd[idx] = end;
                }
            }
            return true;
        }

        /// <summary>Pro kazdy azimut najde nejblizsi prstenec s prekazkou - za nim je zem ve stinu.</summary>
        private void BuildFirstObstacleShadow(PolarTraversabilityGrid polar)
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
        /// Je bod v dane vzdalenosti zakryty prekazkou daneho azimutu? (Tedy zem, kterou kamera
        /// nemuze videt - barva by tam patrila prekazce, ne zemi.) Bez hloubky se stin neurcuje.
        /// </summary>
        /// <param name="rb">Radialni prstenec bodu (-1 = mimo dosah hloubky).</param>
        private bool InShadow(int azimuth, int rb, PolarTraversabilityGrid polar, double range, bool useDepth)
        {
            if (!useDepth || azimuth < 0) return false;

            if (shadowByHeight)
            {
                // Bez sebestineni: bod v prstenci rb zakryvaji jen prstence PRED nim. Mimo dosah
                // hloubky (rb = -1) je bod bud pred prvnim prstencem (nic ho nezakryva), nebo za
                // poslednim (zakryvaji vsechny).
                if (range < polar.RadialEdges[0].Range) return false;
                int R = polar.RadialCount;
                int k = rb >= 0 ? rb - 1 : R - 1;
                return k >= 0 && shadowEnd[azimuth * R + k] > range;
            }

            int first = shadowFrom[azimuth];
            if (first == int.MaxValue) return false;

            // Vse od NABEZNE HRANY prvni prekazky dal je ve stinu (vcetne te prekazky same).
            float edge = polar.RadialEdges[first].Range;
            return range >= edge;
        }
    }
}
