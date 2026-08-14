using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Occupancy;
using ARBot.Common.Vision;
using ARBot.Common.Vision.Synthetic;
using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests;

/// <summary>
/// Reprodukce cele vizualni cesty virtualniho HW bez GUI: <see cref="VirtualCamera"/> ->
/// <see cref="CameraFrameProcessor"/> (probability + polarni grid) -> <see cref="OccupancyIntegrator"/>
/// -> <see cref="OccupancyGrid"/>. Zapojeni je stejne jako v <c>ARBotRuntime</c> vcetne montaznich
/// transformaci z <see cref="Profile"/> - test tedy chyti i chybu, ktera je jen ve SLOZENI
/// (spravna projekce ke spravnemu streamu), ne v jednotlivych dilech.
///
/// <para>Vzniklo pri ladeni „occupancy grid prichazi prazdny" (viz doc/devlog.md) - dosavadni testy
/// <c>OccupancyIntegratorTest</c> pouzivaly umelou projekci a robota v pocatku, takze runtime
/// kombinaci (nativni transform, robot desitky metru od pocatku roviny) nepokryvaly.</para>
/// </summary>
public class VirtualHwOccupancyTest
{
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>
    /// Rovna vozovka sirky 4 m, ktera prochazi bodem <paramref name="x"/>,<paramref name="y"/>
    /// ve smeru <paramref name="theta"/> - robot na ni tedy stoji a kamera na ni vidi.
    /// </summary>
    private static RoadScene Scene(GeoReference origin, double x, double y, double theta)
    {
        double cx = Math.Cos(theta), sy = Math.Sin(theta);
        var a = new Node(1, origin.ToLLA(x - 50 * cx, y - 50 * sy), 4.0);
        var b = new Node(2, origin.ToLLA(x + 100 * cx, y + 100 * sy), 4.0);

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 150.0, wayId: 1, traversalCost: 150.0);
        return new RoadScene(builder.Build(), origin);
    }

    /// <summary>Pocka na prvni snimek z pozadi smycky kamery.</summary>
    private static CameraFrame? WaitForFrame(VirtualCamera cam, TimeSpan timeout)
    {
        CameraFrame? result = null;
        using var arrived = new ManualResetEventSlim(false);

        void Handler(object? sender, CameraFrame frame)
        {
            result = frame;
            arrived.Set();
        }

        cam.MeasurementArived += Handler;
        try { arrived.Wait(timeout); }
        finally { cam.MeasurementArived -= Handler; }
        return result;
    }

    /// <summary>
    /// Projede celou cestu pro danou pozu robota a vrati stav gridu + rozpad zapisu.
    /// </summary>
    private static (int NonZeroOcc, int NonZeroRoad, int Touched, OccupancyIntegrator.IntegrateStats Stats,
                    OccupancyGrid Grid)
        Run(double x, double y, double thetaDeg, bool nativeTransform)
    {
        var origin = Origin();
        double theta = Conversions.Deg2Rad(thetaDeg);
        var pose = new RobotState { X = x, Y = y, Theta = theta };

        using var cam = new VirtualCamera("Left", Scene(origin, x, y, theta), new SyntheticSceneOptions(),
                                          Profile.LeftCameraTransform, _ => pose);

        // Stejne zapojeni jako ARBotRuntime: procesor snimku dostane projekci HLOUBKOVEHO streamu
        // s robot-centrickou orientaci.
        var depthProjection = cam.CreateDepthProjector();
        depthProjection.SetOrientation(Profile.LeftCameraTransform);
        cam.FrameProcessor = new CameraFrameProcessor(
            _ => depthProjection,
            new PolarGridConfig { UseNativeTransform = nativeTransform },
            backProject: new BackProject(BackProject.RoadProbability));

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));
        Assert.That(frame, Is.Not.Null, "virtualni kamera nedodala snimek");
        Assert.Multiple(() =>
        {
            Assert.That(frame!.Grid, Is.Not.Null, "polarni grid se nespocital");
            Assert.That(frame.ImageProbability, Is.Not.Null, "probability se nespocitala");
        });

        // Barevna projekce - stejne jako BuildColorProjectionResolver v ARBotRuntime.
        var colorProjection = cam.CreateProjector();
        colorProjection.SetOrientation(Profile.LeftCameraTransform);

        // Presne jako ARBotRuntime: hloubkova projekce se do integratoru predava pretypovana
        // na ICameraProjection (IDepthCameraProjection z ni nededi).
        var depthAsCamera = depthProjection as ICameraProjection;
        Assert.That(depthAsCamera, Is.Not.Null,
                    "hloubkova projekce neni ICameraProjection - geometricky kanal by se nezapisoval");

        var grid = new OccupancyGrid();
        var integrator = new OccupancyIntegrator(grid);

        int touched = integrator.Integrate(frame!, depthAsCamera, colorProjection,
                                           pose.X, pose.Y, pose.Theta);

        int nonZeroOcc = 0, nonZeroRoad = 0;
        for (int i = 0; i < grid.Occ.Length; i++)
        {
            if (grid.Occ[i] != 0) nonZeroOcc++;
            if (grid.Road[i] != 0) nonZeroRoad++;
        }
        return (nonZeroOcc, nonZeroRoad, touched, integrator.LastStats, grid);
    }

    /// <summary>
    /// Snimek z virtualni kamery zapsany do occupancy gridu musi neco zapsat - jinak robot jede
    /// naslepo, i kdyz kamera i polarni grid vypadaji v poradku.
    ///
    /// <para>Pozy pokryvaji i realny beh: robot desitky metru od pocatku ENU roviny a se zapornym
    /// kurzem (kartezsky grid je kotveny ve svete pres absolutni indexy bunek - zaporne indexy a
    /// maskovani kruhoveho bufferu musi fungovat stejne jako v pocatku).</para>
    /// </summary>
    [TestCase(0.0, 0.0, 0.0, false, TestName = "Occupancy_Pocatek_Managed")]
    [TestCase(0.0, 0.0, 0.0, true, TestName = "Occupancy_Pocatek_Nativni")]
    [TestCase(-24.91, -54.45, -168.6, true, TestName = "Occupancy_DalekoOdPocatku_Nativni")]
    [TestCase(-24.91, -54.45, -168.6, false, TestName = "Occupancy_DalekoOdPocatku_Managed")]
    public void SnimekVirtualniKamery_ZapiseDoOccupancyGridu(double x, double y, double thetaDeg,
                                                             bool nativeTransform)
    {
        var r = Run(x, y, thetaDeg, nativeTransform);
        TestContext.Out.WriteLine($"occ={r.NonZeroOcc} road={r.NonZeroRoad} {r.Stats}");

        Assert.Multiple(() =>
        {
            Assert.That(r.Touched, Is.GreaterThan(0), $"zadna bunka gridu nedostala zapis: {r.Stats}");
            Assert.That(r.NonZeroOcc, Is.GreaterThan(0), $"kanal geometrie zustal nulovy: {r.Stats}");
            Assert.That(r.NonZeroRoad, Is.GreaterThan(0), $"kanal semantiky zustal nulovy: {r.Stats}");
        });
    }

    /// <summary>
    /// Semanticky kanal musi plochu MIMO cestu oznacit za neprujezdnou. Barva je jediny zdroj,
    /// ktery to o rovne trave vi - hloubka ji vidi jako rovinu, tedy sjizdnou. Kdyz tohle
    /// prestane platit, robot povazuje travu vedle cesty za volnou plochu.
    /// </summary>
    [Test]
    public void MimoCestu_JeZeSemantikyNesjizdne()
    {
        var origin = Origin();
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };
        var scene = Scene(origin, 0, 0, 0);
        var r = Run(0, 0, 0, nativeTransform: true);
        var grid = r.Grid;

        int mimoSpravne = 0, mimoSpatne = 0, naCesteSpravne = 0, naCesteSpatne = 0;
        for (int j = 0; j < grid.Size; j++)
        {
            for (int i = 0; i < grid.Size; i++)
            {
                int cx = grid.OriginX + i, cy = grid.OriginY + j;
                double wx = grid.CenterX(cx), wy = grid.CenterY(cy);
                float road = grid.LogOddsRoad(cx, cy);
                if (road == 0f) continue;                      // bez barevneho vzorku - neresime

                if (scene.IsRoad(wx, wy)) { if (road < 0) naCesteSpravne++; else naCesteSpatne++; }
                else { if (road > 0) mimoSpravne++; else mimoSpatne++; }
            }
        }

        TestContext.Out.WriteLine($"na ceste: spravne={naCesteSpravne} spatne={naCesteSpatne}; "
                                  + $"mimo cestu: spravne={mimoSpravne} spatne={mimoSpatne}");

        Assert.Multiple(() =>
        {
            Assert.That(mimoSpravne, Is.GreaterThan(100),
                        "plocha mimo cestu nedostala ze semantiky zadnou neprujezdnost");
            Assert.That(mimoSpravne, Is.GreaterThan(mimoSpatne * 5),
                        "vetsina vzorku mimo cestu ji hlasi jako sjizdnou - barva se vzorkuje ze spatnych pixelu");
            Assert.That(naCesteSpravne, Is.GreaterThan(naCesteSpatne * 5),
                        "vetsina vzorku na ceste ji hlasi jako nesjizdnou");
        });
    }

    /// <summary>
    /// MERENI (ne assert): co dela semanticky kanal pricne pres cestu. Vypise pravdepodobnost
    /// sjizdnosti pro barvy syntetické sceny a pricny profil gridu, aby bylo videt, kde je hranice
    /// cesty a jestli ji barva vubec pozna.
    /// </summary>
    [Test]
    [Explicit("Diagnostika - spoustet rucne pri ladeni semantickeho kanalu.")]
    public void Diagnostika_PricnyProfilSemantiky()
    {
        var bp = new BackProject(BackProject.RoadProbability);
        var opt = new SyntheticSceneOptions();
        TestContext.Out.WriteLine(
            $"probability vozovky (RGB {opt.RoadR},{opt.RoadG},{opt.RoadB}) = {bp.Project(opt.RoadR, opt.RoadG, opt.RoadB)}");
        TestContext.Out.WriteLine(
            $"probability travy   (RGB {opt.GrassR},{opt.GrassG},{opt.GrassB}) = {bp.Project(opt.GrassR, opt.GrassG, opt.GrassB)}");

        var r = Run(0, 0, 0, nativeTransform: true);
        var grid = r.Grid;
        var cfg = grid.Config;
        TestContext.Out.WriteLine(r.Stats.ToString());

        // Cesta vede po ose X (vychod), sirka 4 m -> mimo cestu je |y| > 2 m.
        // Agregat pres CELY grid: dostavaji bunky mimo cestu vubec barevny vzorek?
        int onRoadColor = 0, onRoadColorPlus = 0, offRoadColor = 0, offRoadColorPlus = 0, offRoadNoColor = 0;
        for (int j = 0; j < grid.Size; j++)
        {
            for (int i = 0; i < grid.Size; i++)
            {
                int cx = grid.OriginX + i, cy = grid.OriginY + j;
                double wx = grid.CenterX(cx), wy = grid.CenterY(cy);
                if (wx <= 0.5 || wx > 7.0) continue;      // jen pas pred robotem
                float road = grid.LogOddsRoad(cx, cy);
                bool off = Math.Abs(wy) > 2.0;
                if (road == 0f) { if (off) offRoadNoColor++; continue; }
                if (off) { offRoadColor++; if (road > 0) offRoadColorPlus++; }
                else { onRoadColor++; if (road > 0) onRoadColorPlus++; }
            }
        }
        TestContext.Out.WriteLine($"na ceste:  s barvou={onRoadColor}, z toho LRoad>0 (spatne)={onRoadColorPlus}");
        TestContext.Out.WriteLine($"mimo cestu: s barvou={offRoadColor}, z toho LRoad>0 (spravne)={offRoadColorPlus}, "
                                  + $"BEZ barvy={offRoadNoColor}");

        // Pricny profil dal pred robotem, kde je okraj cesty jeste v zornem poli.
        TestContext.Out.WriteLine("y[m]    LOcc    LRoad   stav        (x = 5 m pred robotem)");
        for (double y = -4.0; y <= 4.0; y += 0.5)
        {
            int cx = grid.CellX(5.0), cy = grid.CellY(y);
            TestContext.Out.WriteLine(
                $"{y,5:F1} {grid.LogOddsOcc(cx, cy),7:F2} {grid.LogOddsRoad(cx, cy),7:F2}   {grid.State(cx, cy)}");
        }
        TestContext.Out.WriteLine($"prahy: blocked>={cfg.BlockedThreshold} free<={cfg.FreeThreshold}");
    }

    /// <summary>
    /// MERENI (ne assert): sedi mapovani BUNKA -> PIXEL pro barevny kanal? Pro body na zemi se
    /// porovna, co o nich rika scena (<see cref="RoadScene.IsRoad"/>, ground truth) s tim, co
    /// v jejich promitnutem pixelu skutecne stoji v probability. Neshoda = barva se vzorkuje
    /// ze spatneho mista; shoda = mapovani je v poradku a chyba je jinde (stin / zorne pole).
    /// </summary>
    [Test]
    [Explicit("Diagnostika - spoustet rucne pri ladeni semantickeho kanalu.")]
    public void Diagnostika_MapovaniBunkaNaPixel()
    {
        var origin = Origin();
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };
        var scene = Scene(origin, 0, 0, 0);

        // Bez sumu - deterministicky rezim sceny, aby merene prahy nekmitaly po jednotlivych pixelech.
        var sceneOpts = new SyntheticSceneOptions
        {
            ColorNoise = 0,
            DepthNoiseM = 0,
            GrassRoughnessM = 0,
        };

        using var cam = new VirtualCamera("Left", scene, sceneOpts,
                                          Profile.LeftCameraTransform, _ => pose);
        var depthProjection = cam.CreateDepthProjector();
        cam.FrameProcessor = new CameraFrameProcessor(
            _ => depthProjection, new PolarGridConfig(),
            backProject: new BackProject(BackProject.RoadProbability));

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));
        Assert.That(frame?.ImageProbability, Is.Not.Null);
        var prob = frame!.ImageProbability!;
        var colorProjection = cam.CreateProjector();

        // (A) Kde se barva v obraze prekloni z cesty na travu, kdyz jdu pricne pres cestu?
        //     Ocekavani: prah presne na |y| = 2 m (polosirka cesty). Cokoliv jineho je posun
        //     mapovani bunka -> pixel.
        foreach (double xs in new[] { 2.0, 4.0, 6.0 })
        {
            double? flip = null;
            var profil = new System.Text.StringBuilder();
            for (double y = -6.0; y <= 6.0; y += 0.25)
            {
                float c = 0, rw = 0;
                if (!colorProjection.Transform((float)xs, (float)y, ref c, ref rw)) { profil.Append(" ---"); continue; }
                int ppx = (int)c, ppy = (int)rw;
                if (ppx < 0 || ppy < 0 || ppx >= prob.Width || ppy >= prob.Height) { profil.Append(" ---"); continue; }
                byte v = prob[ppx, ppy].Value;
                profil.Append(v >= 128 ? " C" : " t");   // C = cesta, t = trava
                if (v < 128 && !flip.HasValue) flip = y;
            }
            TestContext.Out.WriteLine(
                $"x={xs,3:F1} m  (scena: cesta pro |y|<=2,00)  y=-6..+6 po 0,25:{profil}");
        }

        int shoda = 0, neshoda = 0, mimoObraz = 0;
        TestContext.Out.WriteLine("  x     y  IsRoad  pixel        prob   verdikt");
        for (double x = 1.0; x <= 6.0; x += 1.0)
        {
            for (double y = -3.0; y <= 3.0; y += 0.5)
            {
                float col = 0, row = 0;
                if (!colorProjection.Transform((float)x, (float)y, ref col, ref row)) { mimoObraz++; continue; }
                int px = (int)col, py = (int)row;
                if (px < 0 || py < 0 || px >= prob.Width || py >= prob.Height) { mimoObraz++; continue; }

                bool isRoad = scene.IsRoad(x, y);
                byte p = prob[px, py].Value;
                bool pixelRoad = p >= 128;
                bool ok = isRoad == pixelRoad;
                if (ok) shoda++; else neshoda++;

                // Zpetna transformace TYMZ objektem projekce: kdyz Transform a TransformBack
                // nedavaji tentyz bod, je rozbite mapovani (ne renderer).
                float bx = 0, by = 0;
                bool backOk = colorProjection.TransformBack(col, row, ref bx, ref by);

                if (!ok || Math.Abs(y) is > 1.4 and < 2.6)   // okraje a vsechny neshody
                    TestContext.Out.WriteLine(
                        $"{x,4:F1} {y,5:F1}  {isRoad,6}  ({px,3},{py,3})  {p,4}   {(ok ? "ok" : "NESEDI")}"
                        + $"   zpet=({(backOk ? bx : float.NaN),5:F2},{(backOk ? by : float.NaN),5:F2})");
            }
        }
        TestContext.Out.WriteLine($"shoda={shoda} neshoda={neshoda} mimo obraz={mimoObraz}");
    }

    /// <summary>
    /// MERENI (ne assert): proc plan predepisuje tak nizkou rychlost? Vypise rozpad rychlostni
    /// obalky po uzlech - co v <c>min(VClear, VBrake)</c> vyhrava - a zastoupeni stavu bunek
    /// v koridoru pred robotem (jen <see cref="CellState.Free"/> posouva hranici brzdne obalky).
    /// </summary>
    [Test]
    [Explicit("Diagnostika - spoustet rucne pri ladeni rychlosti.")]
    public void Diagnostika_RychlostniObalka()
    {
        var origin = Origin();
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };
        var scene = Scene(origin, 0, 0, 0);

        using var cam = new VirtualCamera("Left", scene, new SyntheticSceneOptions(),
                                          Profile.LeftCameraTransform, _ => pose);
        var depthProjection = cam.CreateDepthProjector();
        cam.FrameProcessor = new CameraFrameProcessor(
            _ => depthProjection, new PolarGridConfig { UseNativeTransform = true },
            backProject: new BackProject(BackProject.RoadProbability));

        var frame = WaitForFrame(cam, TimeSpan.FromSeconds(5));
        Assert.That(frame, Is.Not.Null);

        var grid = new OccupancyGrid();
        var integrator = new OccupancyIntegrator(grid);
        var depthAsCamera = (ICameraProjection)depthProjection;
        var colorProjection = cam.CreateProjector();

        // Robot stoji a diva se na tutez scenu - nekolik snimku, at se log-odds nascitaji
        // (jedno pozorovani barvy da nejvys -0,60, na prah Free je potreba <= -1,00).
        for (int i = 0; i < 10; i++)
            integrator.Integrate(frame!, depthAsCamera, colorProjection, pose.X, pose.Y, pose.Theta);

        // Zastoupeni stavu v koridoru pred robotem (na ceste, do 6 m).
        int free = 0, unknown = 0, blocked = 0, unknownBezBarvy = 0;
        for (int j = 0; j < grid.Size; j++)
            for (int i = 0; i < grid.Size; i++)
            {
                int cx = grid.OriginX + i, cy = grid.OriginY + j;
                double wx = grid.CenterX(cx), wy = grid.CenterY(cy);
                if (wx <= 0.3 || wx > 6.0 || Math.Abs(wy) > 1.0) continue;
                switch (grid.State(cx, cy))
                {
                    case CellState.Free: free++; break;
                    case CellState.Blocked: blocked++; break;
                    default:
                        unknown++;
                        if (grid.LogOddsRoad(cx, cy) == 0f) unknownBezBarvy++;
                        break;
                }
            }
        TestContext.Out.WriteLine(
            $"koridor pred robotem: Free={free} Unknown={unknown} (z toho BEZ barevneho vzorku="
            + $"{unknownBezBarvy}) Blocked={blocked}");

        var field = new ClearanceField(grid);
        field.Build(grid);
        var planner = new LocalPathPlanner(grid.Size);
        var cfg = planner.Config;
        TestContext.Out.WriteLine($"MaxSpeed={cfg.MaxSpeed} SafeDist={cfg.SafeDist} PrefDist={cfg.PrefDist} "
                                  + $"MaxDecel={cfg.MaxDeceleration} MinCostSpeed={cfg.MinCostSpeed}");

        var plan = planner.Plan(grid, field, pose.X, pose.Y, pose.Theta, 5.0, 0.0);
        TestContext.Out.WriteLine($"plan: {plan.Status}, uzlu={plan.WayPoints?.Length ?? 0}, "
                                  + $"minClearance={plan.MinClearanceM:F2} m");

        if (plan.WayPoints == null) return;
        // freeAhead = vzdalenost od uzlu dopredu po drahe k prvni bunce, ktera NENI Free
        // (presne to, co planovac pouziva v brzdne obalce).
        double FreeAhead(int from)
        {
            double s = 0;
            for (int k = from; k < plan.WayPoints.Length - 1; k++)
            {
                var a = plan.WayPoints[k];
                var b = plan.WayPoints[k + 1];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                for (double t = 0; t < len; t += 0.025)
                {
                    double x = a.X + dx * (t / len), y = a.Y + dy * (t / len);
                    if (grid.State(grid.CellX(x), grid.CellY(y)) != CellState.Free) return s;
                    s += 0.025;
                }
            }
            return s;
        }

        TestContext.Out.WriteLine("  #      X      Y   odstup  VClear  freeAh  VBrake   Speed");
        for (int k = 0; k < plan.WayPoints.Length; k++)
        {
            var w = plan.WayPoints[k];
            double clr = field.Distance(grid.CellX(w.X), grid.CellY(w.Y));
            double fa = FreeAhead(k);
            TestContext.Out.WriteLine(
                $"{k,3} {w.X,6:F2} {w.Y,6:F2} {clr,7:F2} {cfg.VClear(clr),7:F2} "
                + $"{fa,7:F2} {cfg.VBrake(fa),7:F2} {w.Speed,7:F3}");
        }

        // Kde jsou prekazky, ktere odstup srazeji? (Blocked bunky blizko drahy.)
        TestContext.Out.WriteLine("Blocked bunky v koridoru (x, y):");
        int vypsano = 0;
        for (int j = 0; j < grid.Size && vypsano < 20; j++)
            for (int i = 0; i < grid.Size && vypsano < 20; i++)
            {
                int cx = grid.OriginX + i, cy = grid.OriginY + j;
                double wx = grid.CenterX(cx), wy = grid.CenterY(cy);
                if (wx <= 0.3 || wx > 6.0 || Math.Abs(wy) > 1.0) continue;
                if (grid.State(cx, cy) != CellState.Blocked) continue;
                TestContext.Out.WriteLine($"   ({wx,5:F2},{wy,6:F2})  LOcc={grid.LogOddsOcc(cx, cy),5:F2} "
                                          + $"LRoad={grid.LogOddsRoad(cx, cy),5:F2}");
                vypsano++;
            }
    }

    /// <summary>
    /// MERENI (ne assert): souhlasi dve mapovani, ktera musi byt navzajem inverzni?
    /// <list type="bullet">
    /// <item>PIXEL -&gt; ZEM: <c>Camera2DToCamera3D</c> + <c>Transformation</c> (paprsek protnuty
    /// s rovinou z=0) - tak vznika bod v <c>SyntheticFrameRenderer</c> i v <c>BuildGrid</c>.</item>
    /// <item>ZEM -&gt; PIXEL: <c>ICameraProjection.Transform</c> - tim <c>OccupancyIntegrator</c>
    /// dohledava, co senzor o bunce rika.</item>
    /// </list>
    /// Kdyz round-trip nevyjde, vzorkuje se occupancy grid ze spatnych pixelu - a to plati
    /// i na realnem HW, nejen v simulaci.
    /// </summary>
    [Test]
    public void ProjekceTamZpet_JeInverzniKRenderu()
    {
        var pose = new RobotState { X = 0, Y = 0, Theta = 0 };
        using var cam = new VirtualCamera("Left", Scene(Origin(), 0, 0, 0), new SyntheticSceneOptions(),
                                          Profile.LeftCameraTransform, _ => pose);

        var proj = cam.CreateDepthProjector();
        var asCamera = (ICameraProjection)proj;
        var table = proj.Camera2DToCamera3D;
        var m = proj.Transformation;
        var eye = m.Translation;
        int tblH = table.GetLength(0), tblW = table.GetLength(1);

        int overeno = 0;
        double maxChyba = 0;
        for (int py = tblH / 2; py < tblH; py += 10)
        {
            for (int px = 10; px < tblW; px += 30)
            {
                var dir = Vector3.TransformNormal(new Vector3(table[py, px].X, table[py, px].Y, 1f), m);
                if (Math.Abs(dir.Z) < 1e-9f) continue;
                double s = (0.0 - eye.Z) / dir.Z;
                if (s <= 0) continue;                     // paprsek miri nad horizont

                double hx = eye.X + s * dir.X, hy = eye.Y + s * dir.Y;

                float c = 0, r = 0;
                Assert.That(asCamera.Transform((float)hx, (float)hy, ref c, ref r), Is.True,
                            $"bod zeme ({hx:F2},{hy:F2}) z pixelu ({px},{py}) se nepromitl zpet do obrazu");

                double chyba = Math.Sqrt((c - px) * (c - px) + (r - py) * (r - py));
                if (chyba > maxChyba) maxChyba = chyba;
                overeno++;
            }
        }

        TestContext.Out.WriteLine($"overeno {overeno} pixelu, nejvetsi chyba round-tripu {maxChyba:F3} px");
        Assert.Multiple(() =>
        {
            Assert.That(overeno, Is.GreaterThan(50), "test nic neoveril - zmenilo se rozliseni nebo geometrie?");
            Assert.That(maxChyba, Is.LessThan(0.5),
                        "Transform neni inverzni k mapovani pixel -> zem, ze ktereho se rendruje "
                        + "a stavi polarni grid; occupancy by se vzorkoval ze spatnych pixelu");
        });
    }

    /// <summary>
    /// Snapshot do zpravy musi prenest presne to, co je v gridu. Pri ladeni „grid prichazi prazdny"
    /// je to druha polovina otazky: kdyz je grid plny a zprava prazdna, chyba je v prevodu.
    ///
    /// <para>Test zaroven dokumentuje, PROC zprava v debuggeru vypada prazdna: zapsane bunky lezi
    /// v kuzelu PRED robotem, tedy uprostred pole, zatimco prvni stovky prvku jsou jihozapadni roh
    /// gridu, kam kamera nevidi - ten je nulovy pravem.</para>
    /// </summary>
    [Test]
    public void ToLogMessage_PreneseObsahGridu()
    {
        var r = Run(-24.91, -54.45, -168.6, nativeTransform: true);
        var msg = r.Grid.ToLogMessage(DateTime.UtcNow);

        int msgOcc = 0, msgRoad = 0, firstNonZero = -1;
        for (int i = 0; i < msg.Occ.Length; i++)
        {
            if (msg.Occ[i] != 0) { msgOcc++; if (firstNonZero < 0) firstNonZero = i; }
            if (msg.Road[i] != 0) msgRoad++;
        }

        TestContext.Out.WriteLine($"grid occ={r.NonZeroOcc} road={r.NonZeroRoad} -> "
                                  + $"msg occ={msgOcc} road={msgRoad}, prvni nenulovy index={firstNonZero} "
                                  + $"z {msg.Occ.Length}");

        Assert.Multiple(() =>
        {
            Assert.That(msgOcc, Is.EqualTo(r.NonZeroOcc), "zprava ztratila cast kanalu geometrie");
            Assert.That(msgRoad, Is.EqualTo(r.NonZeroRoad), "zprava ztratila cast kanalu semantiky");
            Assert.That(msg.OriginX, Is.EqualTo(r.Grid.OriginX));
            Assert.That(msg.OriginY, Is.EqualTo(r.Grid.OriginY));
        });
    }
}
