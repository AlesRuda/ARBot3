using System;
using System.Collections.Generic;
using System.Diagnostics;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Models;
using ARBot.Common.Logs;
using ARBot.Common.Regulators;
using ARBot.Common.Runtime;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// „Vyssi ridici smycka": odebira <see cref="CameraFrame"/>, akumuluje z nich occupancy grid,
    /// planuje lokalni cestu k cili a vysledek atomicky preda nizsi smycce
    /// (<see cref="ControlLoop.Regulator"/>). Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para><b>Vlakno:</b> je to <see cref="MessageProcessor"/>, takze vsechna prace (integrace, EDT,
    /// A*) bezi na jeho vlastnim vlakne fronty. Tik <c>ControlLoop</c> tim zustava deterministicky -
    /// planovac smi obcas trvat i 15 ms. Vstupni fronta je <see cref="OverflowPolicy.DropOldest"/>
    /// s malou kapacitou: kdyz planovac nestiha, je spravne zpracovat NEJNOVEJSI snimek a stare
    /// zahodit (stara mapa je horsi nez zadna).</para>
    ///
    /// <para><b>Poza:</b> pro KAZDY snimek zvlast se vyzada
    /// <see cref="AsyncFusionEngine.GetStateAt"/> v case <see cref="Message.TimeStamp"/> toho snimku -
    /// jen tak se snimky obou kamer (jine casy grabu) zarovnaji do gridu na spravne misto. Kdyz fuze
    /// vrati <c>null</c> (snimek je starsi nez okno historie), snimek se ZAHODI: zapsat ho se spatnou
    /// pozou otravi mapu hur, nez kdyz jeden chybi.</para>
    ///
    /// <para><b>Zadny stary plan se nedrzi.</b> Kazdy cyklus se pocita cely znovu z aktualniho stavu
    /// gridu; drzet plan spocteny nad starsi mapou = jet proti dukazum, ktere robot uz ma. Stabilitu
    /// resi cena otoceni v <see cref="LocalPathPlanner"/>, ne lepivost v case.</para>
    /// </summary>
    public sealed class LocalNavigator : MessageProcessor, ARBot.Common.Runtime.ILocalGoalSink
    {
        private readonly AsyncFusionEngine engine;
        private readonly Func<string, ICameraProjection> depthProjectionResolver;
        private readonly Func<string, ICameraProjection> colorProjectionResolver;
        private readonly IPathPlanner pathPlanner;
        private readonly OccupancyGrid grid;
        private readonly OccupancyIntegrator integrator;
        private readonly ClearanceField field;
        private readonly LocalPathPlanner planner;
        private readonly TimeSpan gridMsgPeriod;

        private readonly Stopwatch sw = new Stopwatch();
        private DateTime lastGridMsg;
        private DateTime lastFrameTime;

        // Draha, po ktere robot prave jede (posledni predana). Kazdy cyklus se overuje proti
        // AKTUALNI mape - viz KontrolaKolize v Process.
        private RegulatorWayPoint[] activePath;

        // Cil: nastavuje ho UI / globalni navigace z jineho vlakna -> pod zamkem (dve doubly nejde
        // menit atomicky).
        private readonly object goalLock = new object();
        private bool hasGoal;
        private double goalX, goalY;

        /// <summary>Sirka koridoru cesty v miste cile [m]; zatim jen ulozena (faze 4b).</summary>
        private double goalCorridorWidth;

        /// <summary>Occupancy grid, ktery smycka akumuluje (jen ke cteni zvenci - vlastni ho toto vlakno).</summary>
        public OccupancyGrid Grid => grid;

        /// <summary>Pole vzdalenosti k neprujezdnemu z posledniho cyklu.</summary>
        public ClearanceField Field => field;

        /// <summary>Konfigurace planovace.</summary>
        public LocalPlannerConfig PlannerConfig => planner.Config;

        /// <summary>
        /// Nizsi ridici smycka, ktere se predava naplanovany <see cref="IRegulator"/>.
        /// null = plan se jen emituje jako zprava (napr. v testech nebo pri ladeni bez jizdy).
        /// </summary>
        public ControlLoop ControlLoop { get; set; }

        /// <summary>Posledni vysledek planovani (diagnostika pro UI).</summary>
        public LocalPlanResult LastPlan { get; private set; }

        /// <summary>DIAGNOSTIKA: pocet zpracovanych snimku (zapsanych do mapy).</summary>
        public long ProcessedFrames { get; private set; }

        /// <summary>DIAGNOSTIKA: pocet snimku zahozenych proto, ze fuze neumela dat pozu v jejich
        /// case (starsi nez okno historie). Rostouci cislo = zpracovani nestiha nebo vypadla fuze.</summary>
        public long DroppedFrames { get; private set; }

        /// <param name="engine">Fuze - dotazuje se na pozu v case snimku.</param>
        /// <param name="depthProjections">Projekce HLOUBKOVEHO streamu per kamera
        /// (klic = <see cref="CameraFrame.Name"/>), s robot-centrickou orientaci. Muze vracet null,
        /// dokud kamera neni pripojena.</param>
        /// <param name="colorProjections">Projekce BAREVNEHO streamu per kamera; null = semanticky
        /// kanal se nezapisuje.</param>
        /// <param name="pathPlanner">Planovac drahy pro prevod waypointu na <see cref="IRegulator"/>;
        /// null = regulator se nesestavuje (jen zpravy).</param>
        /// <param name="gridConfig">Konfigurace occupancy gridu; null = vychozi.</param>
        /// <param name="plannerConfig">Konfigurace lokalniho planovace; null = vychozi.</param>
        /// <param name="integratorConfig">Konfigurace zapisu snimku; null = vychozi.</param>
        /// <param name="gridMessagePeriod">Jak casto emitovat <see cref="OccupancyGridMsg"/>
        /// (snapshot 128 KB); default 500 ms. <see cref="TimeSpan.Zero"/> = kazdy cyklus.</param>
        /// <param name="queueCapacity">Kapacita vstupni fronty snimku (DropOldest); default 4.</param>
        public LocalNavigator(AsyncFusionEngine engine,
                              Func<string, ICameraProjection> depthProjections,
                              Func<string, ICameraProjection> colorProjections = null,
                              IPathPlanner pathPlanner = null,
                              OccupancyGridConfig gridConfig = null,
                              LocalPlannerConfig plannerConfig = null,
                              OccupancyIntegratorConfig integratorConfig = null,
                              TimeSpan? gridMessagePeriod = null,
                              int queueCapacity = 4)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            depthProjectionResolver = depthProjections ?? throw new ArgumentNullException(nameof(depthProjections));
            colorProjectionResolver = colorProjections;
            this.pathPlanner = pathPlanner;

            grid = new OccupancyGrid(gridConfig);
            integrator = new OccupancyIntegrator(grid, integratorConfig);
            field = new ClearanceField(grid);
            planner = new LocalPathPlanner(grid.Size, plannerConfig);
            gridMsgPeriod = gridMessagePeriod ?? TimeSpan.FromMilliseconds(500);
        }

        /// <summary>
        /// Nastavi cil lokalniho planovani [m, world ENU]. Volatelne z jineho vlakna (UI).
        /// Cil dal nez grid se orizne na jeho hranici - jede se jeho smerem.
        /// </summary>
        /// <param name="corridorWidthM">
        /// Sirka koridoru cesty v miste cile [m]; zatim se jen prijima (test prurezu koridorem
        /// je faze 4b v doc/global-navigation-runtime.md). 0 = neresit.
        /// </param>
        public void SetGoal(double worldX, double worldY, double corridorWidthM = 0)
        {
            lock (goalLock)
            {
                goalX = worldX;
                goalY = worldY;
                goalCorridorWidth = corridorWidthM;
                hasGoal = true;
            }
        }

        /// <summary>
        /// Zrusi cil. Robot prestane dostavat novy plan a nizsi smycka po
        /// <c>Profile.PathControlTimeOut</c> nouzove dobrzdi po posledni trase (RIZENE dobrzdeni,
        /// ne tvrde zastaveni). Posledni draha se pritom porad hlida proti mape - kdyby na ni behem
        /// dobrzdovani prekazka prece jen byla, rizeni se zahodi okamzite.
        /// </summary>
        public void ClearGoal()
        {
            lock (goalLock)
                hasGoal = false;
        }

        /// <summary>Aktualni cil, nebo null.</summary>
        public (double X, double Y)? Goal
        {
            get
            {
                lock (goalLock)
                    return hasGoal ? ((double, double)?)(goalX, goalY) : null;
            }
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (!(msg is CameraFrame frame)) return;

            try { Process(frame); }
            catch (Exception ex) { Debug.WriteLine($"LocalNavigator: {ex}"); }
        }

        private void Process(CameraFrame frame)
        {
            sw.Restart();

            // (1) Poza v case PORIZENI TOHOTO snimku (per kamera zvlast). null = mimo okno historie
            //     -> snimek zahodit, spatna poza by mapu otravila.
            var pose = engine.GetStateAt(frame.TimeStamp);
            if (pose == null)
            {
                DroppedFrames++;
                return;
            }

            // Pocitadlo az PO dokonceni cele prace - jinak by pozorovatel (UI, test) videl
            // "zpracovano" driv, nez je grid a plan hotovy.
            try { ProcessCore(frame, pose); }
            finally { ProcessedFrames++; }
        }

        private void ProcessCore(CameraFrame frame, RobotState pose)
        {
            // (2) Zapis do gridu (grid se pritom vycentruje na robota).
            var depthProj = depthProjectionResolver(frame.Name ?? string.Empty);
            var colorProj = colorProjectionResolver?.Invoke(frame.Name ?? string.Empty);
            integrator.Integrate(frame, depthProj, colorProj, pose.X, pose.Y, pose.Theta);
            lastFrameTime = frame.TimeStamp;

            // (3) Bez cile a bez rozjete drahy neni co resit - mapa se ale akumuluje dal.
            double gx, gy;
            bool goal;
            lock (goalLock) { goal = hasGoal; gx = goalX; gy = goalY; }
            if (!goal && activePath == null)
            {
                EmitGridIfDue(frame.TimeStamp);
                return;
            }

            // (4) Pole vzdalenosti - vzdy cele znovu z AKTUALNIHO stavu gridu.
            field.Build(grid);

            LocalPlanResult plan = null;
            bool handedOver = false;

            if (goal)
            {
                plan = planner.Plan(grid, field, pose.X, pose.Y, pose.Theta, gx, gy);
                plan.TimeStamp = frame.TimeStamp;

                // (5) Predani nizsi smycce.
                if (plan.HasPath && pathPlanner != null)
                {
                    try
                    {
                        var regulator = pathPlanner.Plan(plan.WayPoints);
                        var loop = ControlLoop;
                        if (loop != null) loop.Regulator = regulator;
                        activePath = plan.WayPoints;
                        handedOver = true;
                    }
                    catch (Exception ex)
                    {
                        // Degenerovana draha (nulovy usek apod.) - radeji nic nez spatny regulator.
                        Debug.WriteLine($"LocalNavigator: PathPlanner.Plan selhal: {ex.Message}");
                    }
                }
                else if (plan.HasPath)
                {
                    activePath = plan.WayPoints;   // bez IPathPlanneru jen evidujeme (testy/ladeni)
                    handedOver = true;
                }
            }

            // (6) Novy plan nevznikl -> robot jede dal po POSLEDNI drazе. Sama o sobe uz nemusi byt
            //     platna: mapa se mezitim zmenila a muze na ni byt prekazka. Watchdog nizsi smycky
            //     dobrzdi az po Profile.PathControlTimeOut (500 ms) a z 0,8 m/s je brzdna draha
            //     dalsi ~1 m - to je pozde. Proto se draha KAZDY CYKLUS overuje proti aktualni mape
            //     a pri kolizi v dosahu brzdne drahy se rizeni zahazuje OKAMZITE (robot stoji).
            if (!handedOver && activePath != null && PathCollides(activePath, pose, out double hitDist))
            {
                var loop = ControlLoop;
                if (loop != null) loop.Regulator = null;   // ControlLoop: null = stat (bezpecny stav)
                activePath = null;

                Debug.WriteLine($"LocalNavigator: NOUZOVE ZASTAVENI - kolize {hitDist:F2} m na aktualni draze");
                plan ??= new LocalPlanResult { RequestedGoalX = gx, RequestedGoalY = gy };
                plan.Status = LocalPlanStatus.AbortedCollision;
                plan.TimeStamp = frame.TimeStamp;
                plan.MinClearanceM = hitDist;
            }

            sw.Stop();
            if (plan != null)
            {
                plan.ComputeMs = sw.Elapsed.TotalMilliseconds;
                LastPlan = plan;
                EmitDerived(plan.ToLogMessage());
            }
            EmitGridIfDue(frame.TimeStamp);
        }

        /// <summary>
        /// Koliduje draha, po ktere robot prave jede, s AKTUALNI mapou? Kontroluje se jen usek, na
        /// ktery uz je robot fakticky zavazany - od jeho aktualni polohy dopredu o
        /// <b>brzdnou drahu + jeden takt reakce + rezerva</b>. Dal do budoucna to nema smysl: tam
        /// prekazku vyresi priste uspesne preplanovani objezdem.
        ///
        /// <para>Kolize = odstup pod <see cref="LocalPlannerConfig.SafeDist"/> nebo bunka
        /// <see cref="CellState.Blocked"/>. <see cref="CellState.Unknown"/> kolize NENI - to resi
        /// rychlostni obalka (nejed rychleji, nez z ceho zastavis na hranici potvrzeneho).</para>
        /// </summary>
        /// <param name="hitDistance">Vzdalenost k nalezene kolizi podel drahy [m]; jinak NaN.</param>
        private bool PathCollides(RegulatorWayPoint[] path, RobotState pose, out double hitDistance)
        {
            hitDistance = double.NaN;
            if (path == null || path.Length < 2) return false;

            var cfg = planner.Config;
            double v = Math.Abs(pose.V);
            double check = v * v / (2.0 * cfg.MaxDeceleration)      // brzdna draha z aktualni rychlosti
                           + v * (Profile.Ts / 1000.0)              // jeden takt nez zasah dojede
                           + grid.Resolution;                       // rezerva na diskretizaci
            if (check <= 0) return false;                           // robot stoji - neni co zastavovat

            // Zacatek kontroly = prumet robotu na drahu (nejblizsi bod), aby se uz projeta cast
            // drahy nekontrolovala.
            FindClosest(path, pose.X, pose.Y, out int seg, out double t);

            double step = grid.Resolution * 0.5;
            double traveled = 0;
            for (int i = seg; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y;
                double dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;

                double from = (i == seg) ? t * len : 0;
                for (double s = from; s <= len; s += step)
                {
                    double d = traveled + (s - from);
                    if (d > check) return false;                    // za dosahem zavazku - konec

                    double x = x0 + dx * (s / len), y = y0 + dy * (s / len);
                    int cx = grid.CellX(x), cy = grid.CellY(y);
                    if (!grid.Contains(cx, cy)) continue;           // mimo mapu = nevim, ne kolize

                    if (grid.State(cx, cy) == CellState.Blocked || field.Distance(cx, cy) < cfg.SafeDist)
                    {
                        hitDistance = d;
                        return true;
                    }
                }
                traveled += len - from;
            }
            return false;
        }

        /// <summary>Najde nejblizsi bod na lomene care k <c>(x,y)</c>: index useku + parametr t v nem.</summary>
        private static void FindClosest(RegulatorWayPoint[] path, double x, double y,
                                        out int segment, out double t)
        {
            segment = 0;
            t = 0;
            double best = double.MaxValue;

            for (int i = 0; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y;
                double dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double len2 = dx * dx + dy * dy;
                if (len2 < 1e-18) continue;

                double u = ((x - x0) * dx + (y - y0) * dy) / len2;
                if (u < 0) u = 0; else if (u > 1) u = 1;

                double px = x0 + dx * u - x, py = y0 + dy * u - y;
                double d2 = px * px + py * py;
                if (d2 < best) { best = d2; segment = i; t = u; }
            }
        }

        /// <summary>Emituje snapshot gridu, kdyz od posledniho uplynula <see cref="gridMsgPeriod"/>.</summary>
        private void EmitGridIfDue(DateTime now)
        {
            if (now - lastGridMsg < gridMsgPeriod) return;
            lastGridMsg = now;
            EmitDerived(grid.ToLogMessage(lastFrameTime));
        }
    }
}
