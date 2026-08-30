using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;
// Pozn.: z Occupancy se bere JEN enum LocalPlanStatus - tedy smlouva zpravy, kterou tato vrstva
// odebira (zavisi na nem i Logs.LocalPlanMsg). Na grid ani planovac vrstva nezavisi, jak vyzaduje
// doc/global-navigation-runtime.md.
using ARBot.Common.Occupancy;
using ARBot.Common.Runtime;

namespace ARBot.Common.Maps.OsmNav.Navigation
{
    /// <summary>
    /// Globalni navigace: drzi trasu po silnicni siti k cili v LLA a krmi lokalni vrstvu
    /// "mrkvi" na trase (viz doc/global-navigation-runtime.md).
    /// <para>
    /// Bezi jako <see cref="MessageProcessor"/> na vlastnim vlakne, takze ridici tik zustava
    /// deterministicky. Odebira <see cref="RobotStateMsg"/>; vlastni praci dela jen jednou za
    /// <see cref="GlobalNavigatorConfig.ReplanPeriod"/>.
    /// </para>
    /// <para>
    /// Vrstva nezna occupancy grid ani regulatory - jedine pouto dolu je
    /// <see cref="ILocalGoalSink"/>. Diky tomu je cela testovatelna bez gridu i bez HW.
    /// </para>
    /// </summary>
    public sealed class GlobalNavigator : MessageProcessor, IGlobalGoalSink, Missions.IRouteProbe
    {
        private readonly RoadNetwork network;
        private readonly GeoReference origin;
        private readonly ILocalGoalSink localGoal;
        private readonly GlobalNavigatorConfig cfg;
        private readonly NavigatorOptions navOptions;

        private readonly object gate = new object();

        // Pole i sledovac vznikaji az s prvnim cilem (bez cile neni co pocitat).
        private GoalField field;
        private Navigator navigator;
        private Router router;
        private LLA goal;

        private DateTime lastCycle = DateTime.MinValue;

        // --- Metadata o postupu a detektory (viz doc/global-navigation-runtime.md) ---

        private SignApplier signs;

        /// <summary>Klouzave okno (ujeta draha, φ) pro detektor B.</summary>
        private readonly ProgressWindow progress;

        /// <summary>Kumulativni ujeta draha [m] - integruje se z po sobe jdoucich poz.</summary>
        private double travelledM;
        private double lastX, lastY;
        private bool hasLastPose;

        // Posledni VIDENA poza - vedena zvlast od lastX/lastY detektoru, protoze ta se plni jen
        // kdyz je aktivni cil (jinak by se do travelledM zapocital skok pres dobu bez cile).
        // Zkouska dosazitelnosti (Probe) ale potrebuje vedet, kde robot je, i kdyz cil jeste neni.
        private double probeX, probeY;
        private bool hasProbePose;

        /// <summary>Pocet po sobe jdoucich neuspechu lokalniho planovani (detektor C).</summary>
        private int planFailureStreak;

        /// <summary>Posledni znamy stav nouzoveho zastaveni (z <c>DriveCommandMsg</c>).</summary>
        private volatile bool emergencyStop;

        /// <summary>Hlasi lokalni vrstva platny plan? Bez nej detektor A nema co resit.</summary>
        private bool localPlanValid;

        // Detektor A: draha a cas na zacatku okna klidu.
        private DateTime noMotionSince = DateTime.MinValue;
        private double noMotionStartOdo;

        /// <summary>Kolikrat uz se na teto hrane zkousela naprava (detektor A).</summary>
        private readonly Dictionary<EdgeKey, int> recoveryCount = new();

        /// <summary>Autoritativni seznam uzavrenych / penalizovanych hran.</summary>
        private readonly Dictionary<EdgeKey, EdgeClosure> closures = new();

        /// <summary>Hrany site podle trvaleho klice - pro znovuaplikovani uzavreni.</summary>
        private readonly Dictionary<EdgeKey, Edge> edgeByKey = new();

        /// <summary>Uzavrene a penalizovane hrany (pro UI a diagnostiku).</summary>
        public IReadOnlyCollection<EdgeClosure> Closures => closures.Values;

        /// <summary>Potencial postupu φ [s] z posledniho cyklu (klesa pri priblizovani k cili).</summary>
        public double Phi { get; private set; }

        /// <summary>Posledni spocteny stav (pro UI).</summary>
        public GlobalNavStatus Status { get; private set; } = GlobalNavStatus.NoGoal;

        /// <summary>Posledni trasa jako hrany site (pro zobrazeni a diagnostiku).</summary>
        public IReadOnlyList<Edge> Route { get; private set; } = Array.Empty<Edge>();

        /// <summary>Posledni predana mrkev [m, world ENU], nebo null.</summary>
        public Point2D? Carrot { get; private set; }

        // Geometrie trasy je z techto zprav nejvetsi, proto se posila jen pri ZMENE trasy
        // nebo jednou za RouteMessagePeriod.
        private string lastRouteKey = string.Empty;
        private DateTime lastRouteMsgAt = DateTime.MinValue;

        /// <param name="network">Silnicni sit (nemenna po sestaveni).</param>
        /// <param name="origin">Pocatek lokalni ENU roviny - tentyz, se kterym pocita fuze.</param>
        /// <param name="localGoal">Prijemce lokalniho cile (v aplikaci <c>LocalNavigator</c>).</param>
        /// <param name="config">Parametry; null = vychozi.</param>
        /// <param name="navigatorOptions">Nastaveni sledovace gradientu (vc. radiusu dojezdu).</param>
        public GlobalNavigator(RoadNetwork network, GeoReference origin, ILocalGoalSink localGoal,
                               GlobalNavigatorConfig config = null,
                               NavigatorOptions navigatorOptions = null)
            : base(OverflowPolicy.DropOldest, capacity: 16)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));
            this.localGoal = localGoal ?? throw new ArgumentNullException(nameof(localGoal));
            cfg = config ?? new GlobalNavigatorConfig();
            navOptions = navigatorOptions;

            progress = new ProgressWindow(cfg.ProgressWindowM);

            foreach (var e in this.network.Edges)
                edgeByKey[EdgeKey.Of(e)] = e;
        }

        /// <summary>
        /// Nastavi cil. Pole se <b>neruší</b>, jen prepne cil (<c>ClearGoal</c> + <c>InsertGoal</c>) -
        /// overlay znacek (a tim i pripadne uzavrene hrany) to prezije. Proto je i navrat do depa
        /// normalni cil, ne "zruseni cile".
        /// </summary>
        public void SetGoal(LLA target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            lock (gate)
            {
                goal = target;

                if (field == null)
                {
                    field = new GoalField(network, target);
                    navigator = new Navigator(field, navOptions);
                    router = new Router(field);
                    signs = new SignApplier(field);
                }
                else
                {
                    field.ClearGoal();
                    field.InsertGoal(target);
                }

                // Potencial je vazany na cil - po jeho zmene je stara historie bezcenna.
                progress.Reset();

                lastCycle = DateTime.MinValue;   // spocitat hned pri nejblizsi poze
            }
        }

        /// <summary>
        /// Prestat jezdit. Vyhrazeno pro nouzove zastaveni / preruseni mise - <b>ne</b> pro zmenu
        /// cile (na to je <see cref="SetGoal"/>).
        /// </summary>
        public void Cancel()
        {
            lock (gate)
            {
                goal = null;
                Route = Array.Empty<Edge>();
                Status = GlobalNavStatus.NoGoal;
            }
            localGoal.ClearGoal();
        }

        /// <summary>
        /// Zkouska, jestli na cil vede po siti trasa — <b>bez</b> zmeny aktivniho cile navigace.
        /// Pouziva to mise pri prijeti cile z QR kodu; bez teto kontroly by se <c>NoRoute</c>
        /// zjistilo az za jizdy. Viz doc/robotour-mission.md.
        ///
        /// <para>Pocita se nad <b>vlastnim</b>, zahoditelnym <see cref="GoalField"/>, takze zkouska
        /// nesahne na pole ani na overlay znacek. Cena je stejna jako u <see cref="SetGoal"/> (jedno
        /// postaveni pole), coz je pri jednom kodu za stanoviste zanedbatelne.</para>
        ///
        /// <para><b>Uzavrene hrany se do zkousky zamerne nepromitaji.</b> Jsou docasne (casti maji
        /// expiraci), takze zamitnout cil kvuli prave uzavrene hrane by znamenalo zamitnout
        /// stanoviste, na ktere se za minutu bezne dojede. Zkouska se pta „ma mapa vubec cestu",
        /// ne „je pruchodna prave nyni".</para>
        ///
        /// <para><b>Cil se prichycuje na sit</b> (<see cref="Missions.RouteProbeResult.SnappedTarget"/>)
        /// a zkouska rekne, jak daleko od ni lezel
        /// (<see cref="Missions.RouteProbeResult.OffRoadM"/>). Prichyceny cil je ten, na ktery se
        /// ma jezdit: <c>GoalField.GoalPoint</c> je surovy cil a <c>Navigator</c> proti nemu meri
        /// dojezd, takze cil odsazeny vic nez o <c>ArrivalRadiusMeters</c> by <b>nikdy</b>
        /// neohlasil <c>Arrived</c>.</para>
        ///
        /// <para><b>Vzdalenost od site zkouska neposuzuje</b> — jen ji zmeri. Limit patri mise
        /// (<c>RobotourConfig.MaxTargetOffRoadM</c>), protoze „co je jeste prijatelne" je pravidlo
        /// ulohy, ne vlastnost grafu. Odpoved <c>Reachable</c> tedy porad zni „vede v grafu cesta
        /// z hrany u robota do hrany u cile", ne „cil lezi na ceste".</para>
        ///
        /// <para><b>Zkousi se OBE orientace mapmatchnute hrany</b>, ne jen ta, kterou vratil
        /// <c>NearestNode</c> — jinak je zkouska pesimistictejsi nez jizda, kterou ma predpovedet.
        /// Na obousmerne ceste jsou oba smery stejne daleko, takze mapmatch vybira podle poradi
        /// hran, ne podle kurzu robota; kdyz padne na smer OD cile, je cena nekonecna (otocka na
        /// teze ceste neni v grafu prechod). <c>Navigator.Update</c> i <c>Router</c> pritom obe
        /// orientace zkousi a beru levnejsi, takze <b>jet se tam da</b>. Do 27. 8. 2026 to zkouska
        /// nedelala a zamitala dobre cile hlaskou „nevede trasa".</para>
        ///
        /// <para><b>Delka trasy</b> je soucet delek hran: u cile presna, na zacatku nadhodnocena az
        /// o delku jedne hrany (viz <see cref="Logs.GlobalNavMsg.RouteLengthM"/>).</para>
        /// </summary>
        public Missions.RouteProbeResult Probe(LLA target)
        {
            if (target == null) return new Missions.RouteProbeResult(false, 0);

            double x, y;
            bool has;
            lock (gate) { x = probeX; y = probeY; has = hasProbePose; }

            // Bez pozy neni odkud pocitat. Zamitnuti je spravnejsi nez "asi ano": cil, ktery se
            // neda overit, nema projit strojovou kontrolou.
            if (!has) return new Missions.RouteProbeResult(false, 0);

            var here = origin.ToLLA(x, y);
            try
            {
                // Prichyceni na sit se dela HNED, jeste pred stavbou pole: prichyceny cil se posila
                // dal (jezdi se na nej) a vzdalenost od site je udaj, podle ktereho mise cil
                // zamita. Kdyz sit zadnou hranu nema, neni co prichytit ani kam jet.
                var edge = network.NearestEdge(target, out _, out LLA snapped, out double offRoad);
                if (edge == null) return new Missions.RouteProbeResult(false, 0);

                // Pole se stavi nad PRICHYCENYM cilem, aby GoalPoint (a tim i mereni dojezdu)
                // souhlasil s cilem, ktery se vraci volajicimu. Hrana vyjde tataz - InsertGoal
                // prichycuje tymz NearestEdge.
                var probeField = new GoalField(network, snapped);
                var node = probeField.NearestNode(here, out _, out _, out _);
                if (node == null) return new Missions.RouteProbeResult(false, 0, snapped, offRoad);

                // OBE orientace, ne jen tu, kterou vratil mapmatch. Na obousmerne ceste jsou oba
                // smery stejne daleko, takze NearestNode vybira podle poradi hran, ne podle toho,
                // kam robot miri - a kdyz padne na smer OD cile, je cena nekonecna (otocka na teze
                // ceste neni v grafu prechod, GraphBuilder U-turn vynechava). Jet se tam ale da:
                // Navigator.Update i Router zkousi obe orientace a berou levnejsi. Bez tohoto
                // kroku je zkouska pesimistictejsi nez jizda, kterou ma predpovedet, a zamitne
                // dobry cil hlaskou "nevede trasa" (namereno 27. 8. 2026 na cili 50 m za robotem).
                probeField.EnsureSettled(node);
                double cost = probeField.CostToGoal(node);

                var reverse = probeField.FindReverse(node);
                if (reverse != null)
                {
                    probeField.EnsureSettled(reverse);
                    double costRev = probeField.CostToGoal(reverse);
                    if (!(costRev > cost)) cost = costRev;   // NaN-safe minimum
                }

                if (double.IsInfinity(cost) || double.IsNaN(cost))
                    return new Missions.RouteProbeResult(false, 0, snapped, offRoad);

                var route = new Router(probeField).Plan(here);
                double length = 0;
                for (int i = 0; i < route.Count; i++) length += route[i].LengthMeters;

                return new Missions.RouteProbeResult(true, length, snapped, offRoad);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GlobalNavigator.Probe: {ex.Message}");
                return new Missions.RouteProbeResult(false, 0);
            }
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Nouzove zastaveni: pod nim robot legitimne stoji, i kdyz ma cil i platny plan -
            // bez teto podminky by kazde zmacknuti stopu za jizdy po 10 s vyrobilo falesny zasek
            // a robot by zacal zavirat hrany kvuli tomu, ze u nej nekdo stal.
            if (msg is DriveCommandMsg drive)
            {
                OnDriveCommand(drive.EmergencyStop);
                return;
            }

            // Zpetna vazba od lokalni vrstvy: jak se ji dari planovat (detektory A a C).
            if (msg is LocalPlanMsg plan)
            {
                OnLocalPlan(plan.PlanStatus);
                return;
            }

            if (msg is not RobotStateMsg state) return;

            // Vlastni praci jen jednou za ReplanPeriod; mezi tim se poza jen zahodi.
            if (lastCycle != DateTime.MinValue && state.TimeStamp - lastCycle < cfg.ReplanPeriod)
                return;
            lastCycle = state.TimeStamp;

            var result = Step(state.X, state.Y, state.TimeStamp);
            if (result != null)
                EmitDerived(result);

            var route = BuildRouteMessageIfDue(state.X, state.Y, state.TimeStamp);
            if (route != null)
                EmitDerived(route);
        }

        /// <summary>
        /// Stav nouzoveho zastaveni z ridici smycky. Pod stopem robot legitimne stoji, i kdyz ma
        /// cil i platny plan - detektor A se proto musi vypnout.
        /// </summary>
        public void OnDriveCommand(bool emergencyStopActive) => emergencyStop = emergencyStopActive;

        /// <summary>
        /// Zpetna vazba od lokalni vrstvy. Volatelne primo z testu.
        /// </summary>
        public void OnLocalPlan(LocalPlanStatus status)
        {
            // POZOR na EscapingBlocked: robot uvazl v blokovane bunce a prave se z ni vyhrabava.
            // NENI to selhani (jede) ani platny plan k cili (nemiri k mrkvi), takze zamerne nepadne
            // do zadne z obou vetvi: serie selhani se vynuluje (uvaznuti nesmi nakonec zavrit hranu,
            // ktera je v poradku) a detektor zaseku zustane odzbrojeny, dokud unik trva.
            // Viz doc/occupancy-and-local-planning.md.
            bool failed = status == LocalPlanStatus.NoRoute || status == LocalPlanStatus.RobotBlocked;
            localPlanValid = status == LocalPlanStatus.Ok || status == LocalPlanStatus.Partial;

            planFailureStreak = failed ? planFailureStreak + 1 : 0;
        }

        /// <summary>
        /// Vrati geometrii trasy k zobrazeni, kdyz se trasa zmenila nebo uplynula
        /// <see cref="GlobalNavigatorConfig.RouteMessagePeriod"/>; jinak null.
        /// </summary>
        public GraphNavigationMsg BuildRouteMessageIfDue(double robotX, double robotY, DateTime now)
        {
            var route = Route;
            string key = RouteKey(route);

            bool changed = key != lastRouteKey;
            bool due = lastRouteMsgAt == DateTime.MinValue || now - lastRouteMsgAt >= cfg.RouteMessagePeriod;
            if (!changed && !due) return null;

            lastRouteKey = key;
            lastRouteMsgAt = now;

            return BuildRouteMessage(robotX, robotY);
        }

        /// <summary>
        /// Slozi <see cref="GraphNavigationMsg"/> z aktualni trasy: vrcholy v lokalnim ENU, hrany
        /// mezi nimi jako zvyraznena cesta, znacky start (robot) / cil / vysledek (mrkev).
        /// </summary>
        public GraphNavigationMsg BuildRouteMessage(double robotX, double robotY)
        {
            LLA target;
            lock (gate) target = goal;

            double targetX = 0, targetY = 0;
            if (target != null)
            {
                var g = origin.ToLocal(target);
                targetX = g.X;
                targetY = g.Y;
            }

            var carrot = Carrot;
            var vertexes = new List<GraphNavigationMsg.Vertex>();
            var edges = new List<GraphNavigationMsg.Edge>();

            // Bezparametrovy ctor zpravy nechava seznamy null - pouziva se ten s listy.
            var msg = new GraphNavigationMsg(robotX, robotY, targetX, targetY,
                                             carrot?.X, carrot?.Y, vertexes, edges);

            var route = Route;
            if (route.Count > 0)
            {
                vertexes.Add(ToVertex(route[0].From));
                foreach (var e in route)
                {
                    vertexes.Add(ToVertex(e.To));
                    edges.Add(new GraphNavigationMsg.Edge(msg)
                    {
                        ID = e.WayId,
                        From = vertexes.Count - 2,
                        To = vertexes.Count - 1,
                        Length = e.LengthMeters,
                        HightLight = true,   // trasa, po ktere se prave jede
                        Path = true,
                    });
                }
            }

            // Uzavrene a penalizovane hrany - at je v mape videt, cemu se robot vyhyba a proc.
            // Na trase uz nejsou (prave proto se objizdi), takze se pridavaji zvlast.
            foreach (var c in closures.Values)
            {
                if (!edgeByKey.TryGetValue(c.Key, out var e)) continue;

                vertexes.Add(ToVertex(e.From));
                vertexes.Add(ToVertex(e.To));
                edges.Add(new GraphNavigationMsg.Edge(msg)
                {
                    ID = e.WayId,
                    From = vertexes.Count - 2,
                    To = vertexes.Count - 1,
                    Length = e.LengthMeters,
                    Collision = true,        // ve world pohledu odlisena barva
                    Graph = true,
                });
            }

            return msg;
        }

        /// <summary>Prevede uzel site na vrchol zpravy (v lokalnim ENU).</summary>
        private GraphNavigationMsg.Vertex ToVertex(Node node)
        {
            var p = origin.ToLocal(node.Location);
            return new GraphNavigationMsg.Vertex
            {
                ID = node.Id,
                X = p.X,
                Y = p.Y,
                Width = node.Width,
            };
        }

        /// <summary>Identita trasy pro detekci zmeny (poradi hran).</summary>
        private static string RouteKey(IReadOnlyList<Edge> route)
        {
            if (route.Count == 0) return string.Empty;

            var sb = new System.Text.StringBuilder(route.Count * 8);
            for (int i = 0; i < route.Count; i++)
            {
                sb.Append(route[i].Index);
                sb.Append(',');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Jeden cyklus globalni navigace: poza -&gt; LLA -&gt; sledovac gradientu -&gt; mrkev -&gt;
        /// lokalni cil. Volatelne primo z testu (bez vlaken a bez zprav).
        /// </summary>
        /// <param name="x">Poloha robota na vychod [m].</param>
        /// <param name="y">Poloha robota na sever [m].</param>
        /// <param name="now">Cas pozy.</param>
        /// <returns>Zprava o stavu, nebo null kdyz neni co hlasit.</returns>
        public GlobalNavMsg Step(double x, double y, DateTime now)
        {
            LLA target;
            Navigator nav;
            Router rt;

            lock (gate)
            {
                target = goal;
                nav = navigator;
                rt = router;
                // Poza se pamatuje VZDY, i bez cile - jinak by zkouska dosazitelnosti (Probe)
                // nemela odkud vyjit prave ve chvili, kdy je potreba: mise se rozhoduje o prvnim
                // cili z QR kodu jeste PREDTIM, nez nejaky cil vubec existuje.
                probeX = x; probeY = y; hasProbePose = true;
            }

            var here = origin.ToLLA(x, y);

            if (target == null || nav == null)
            {
                Status = GlobalNavStatus.NoGoal;
                return BuildMessage(here, null, null, 0, 0, now);
            }

            var fix = nav.Update(here);

            if (fix.NoRoute)
            {
                Status = GlobalNavStatus.NoRoute;
                localGoal.ClearGoal();
                return BuildMessage(here, target, null, fix.OffRouteDist, 0, now);
            }

            if (fix.Arrived)
            {
                Status = GlobalNavStatus.Arrived;
                // Zastaveni si zaridi odberatel (mise zrusi cil) - Arrived je hlaseni, ne manevr.
                return BuildMessage(here, target, null, fix.OffRouteDist, 0, now);
            }

            var route = rt.Plan(here);
            Route = route;

            var polyline = ToPolyline(route, target);
            var robot = new Point2D(x, y);
            var carrot = RouteCarrot.Find(polyline, robot, cfg.CarrotHalfExtentM);

            if (carrot == null)
            {
                Status = GlobalNavStatus.NoRoute;
                localGoal.ClearGoal();
                return BuildMessage(here, target, null, fix.OffRouteDist, route.Count, now);
            }

            // Mimo trasu: mrkev na hrane mapy prestava mit smysl (mezi robotem a siti muze byt
            // cokoli). RouteCarrot v tom pripade vraci nejblizsi bod trasy, coz je presne ono.
            Status = fix.OffRouteDist > cfg.OffRouteMaxM
                ? GlobalNavStatus.OffRoute
                : (IsGoalInMap(target, robot) ? GlobalNavStatus.GoalInMap : GlobalNavStatus.Driving);

            Carrot = carrot;
            localGoal.SetGoal(carrot.Value.X, carrot.Value.Y);

            TrackProgressAndDetect(here, fix, x, y, now);

            return BuildMessage(here, target, carrot, fix.OffRouteDist, route.Count, now);
        }

        /// <summary>
        /// Vede metadata o postupu a spousti detektory zaseku. Viz doc/global-navigation-runtime.md.
        /// </summary>
        private void TrackProgressAndDetect(LLA here, NavigationFix fix, double x, double y, DateTime now)
        {
            // Ujeta draha z po sobe jdoucich poz (odometr pro okno i pro detektor A).
            if (hasLastPose)
            {
                double dx = x - lastX, dy = y - lastY;
                travelledM += Math.Sqrt(dx * dx + dy * dy);
            }
            lastX = x; lastY = y; hasLastPose = true;

            ExpireClosures(now);

            var edge = fix.CurrentEdge;
            if (edge == null) return;

            Phi = ComputePhi(here, edge);
            progress.Add(travelledM, Phi);

            DetectNoProgress(edge, now);     // B
            DetectRoadBlocked(edge, now);    // C
            DetectNoMotion(edge, now);       // A
        }

        /// <summary>
        /// Potencial postupu φ = (1 − t) · cena zbytku hrany + cost-to-goal [s].
        /// <para>Skalar, ktery pri postupu k cili monotonne klesa i pres krizovatky - a klesa
        /// i kdyz robot prekazku OBJIZDI jinou cestou, protoze pole je goal-rooted. Proti prosté
        /// vzdusne vzdalenosti (ktera pri objizdeni roste) je to poctiva mira postupu.</para>
        /// </summary>
        private double ComputePhi(LLA here, Edge edge)
        {
            var f = field;
            if (f == null) return 0;

            f.NearestNode(here, out double t, out _, out _);
            double cost = f.CostToGoal(edge);
            if (double.IsInfinity(cost) || double.IsNaN(cost)) return double.PositiveInfinity;

            return (1.0 - t) * f.BaseTraversalCost(edge) + cost;
        }

        /// <summary>
        /// Detektor B - bloudim. Za posledních <c>ProgressWindowM</c> ujete drahy neklesl potencial
        /// dost. Reakce je <b>soft penalizace</b>: hrana se nezakaze, jen zdrazi, takze robot zkusi
        /// jinudy a sem se vrati, jen kdyz nic jineho neni. Falesny poplach tak nezniči trasu.
        /// </summary>
        private void DetectNoProgress(Edge edge, DateTime now)
        {
            if (!progress.TryGetDrop(out double drop)) return;
            if (drop >= cfg.RequiredPhiDrop) return;

            var key = EdgeKey.Of(edge);
            if (closures.TryGetValue(key, out var existing) && existing.Reason == ClosureReason.NoProgress)
                CloseEdge(edge, ClosureReason.NoProgress, now, hard: true);   // opakuje se -> jako C
            else
                PenalizeEdge(edge, ClosureReason.NoProgress, now);

            progress.Reset();   // at se totez nevyhodnoti hned znovu
        }

        /// <summary>
        /// Detektor C - cesta je prehrazena (mapa lze). Lokalni planovani opakovane hlasi, ze
        /// se neda projet. Reakce: zakazat hranu <b>i jeji reverzni</b> - fyzicka zabrana blokuje
        /// oba smery.
        /// </summary>
        private void DetectRoadBlocked(Edge edge, DateTime now)
        {
            if (planFailureStreak < cfg.BlockedPlanCount) return;

            CloseEdge(edge, ClosureReason.RoadBlocked, now, hard: true);
            planFailureStreak = 0;
        }

        /// <summary>
        /// Detektor A - nehybu se. Vypnuty, kdyz robot legitimne stoji: bez aktivniho cile,
        /// bez platneho planu, nebo pod nouzovym zastavenim.
        /// <para><b>Zotaveni (couvnuti/otocka) dnes neexistuje</b> - detektor proto umi jen pockat
        /// a po vycerpani pokusu s hranou zachazet jako u prehrazeni. Viz otevrene ukoly.</para>
        /// </summary>
        private void DetectNoMotion(Edge edge, DateTime now)
        {
            if (emergencyStop || !localPlanValid)
            {
                noMotionSince = DateTime.MinValue;
                return;
            }

            if (noMotionSince == DateTime.MinValue)
            {
                noMotionSince = now;
                noMotionStartOdo = travelledM;
                return;
            }

            // Pohnul se dost -> okno klidu zacina znovu.
            if (travelledM - noMotionStartOdo >= cfg.MinMotionM)
            {
                noMotionSince = now;
                noMotionStartOdo = travelledM;
                return;
            }

            if (now - noMotionSince < cfg.NoMotionSec + cfg.EscalateSec) return;

            var key = EdgeKey.Of(edge);
            recoveryCount.TryGetValue(key, out int tries);
            recoveryCount[key] = tries + 1;

            // Zotaveni neexistuje, takze po vycerpani pokusu rovnou uzavreni.
            if (tries + 1 > cfg.MaxRecoveries)
                CloseEdge(edge, ClosureReason.NoMotion, now, hard: true);

            noMotionSince = now;
            noMotionStartOdo = travelledM;
        }

        /// <summary>Zdrazi hranu (soft penalizace) a zapise to do seznamu.</summary>
        private void PenalizeEdge(Edge edge, ClosureReason reason, DateTime now)
        {
            var f = field;
            if (f == null) return;

            var key = EdgeKey.Of(edge);
            double baseCost = f.BaseTraversalCost(edge);

            f.SetTraversalCost(edge, baseCost * cfg.PenaltyFactor);

            closures[key] = new EdgeClosure
            {
                Key = key,
                Reason = reason,
                At = now,
                Count = closures.TryGetValue(key, out var old) ? old.Count : 0,
                Hard = false,
                BaseCost = baseCost,
            };
        }

        /// <summary>
        /// Zakaze hranu i jeji reverzni (fyzicka zabrana blokuje oba smery) a zapise to do seznamu.
        /// </summary>
        private void CloseEdge(Edge edge, ClosureReason reason, DateTime now, bool hard)
        {
            var f = field;
            if (f == null || signs == null) return;

            var key = EdgeKey.Of(edge);
            double baseCost = f.BaseTraversalCost(edge);
            int count = closures.TryGetValue(key, out var old) ? old.Count + 1 : 1;

            signs.CloseRoad(edge);
            var reverse = f.FindReverse(edge);
            if (reverse != null) signs.CloseRoad(reverse);

            closures[key] = new EdgeClosure
            {
                Key = key,
                Reason = reason,
                At = now,
                Count = count,
                Hard = true,
                BaseCost = double.IsInfinity(baseCost) ? (old?.BaseCost ?? 0) : baseCost,
            };
        }

        /// <summary>
        /// Zapominani uzavreni: po <c>ClosureTtl</c> se hrana nevraci do plne ceny, ale na
        /// <b>soft penalizaci</b> - kdo vi, jestli tam ta prekazka porad je, ale preferovat ji
        /// nebudeme. Po <c>MaxClosures</c> potvrzenich je uzavreni trvale.
        /// </summary>
        private void ExpireClosures(DateTime now)
        {
            var f = field;
            if (f == null) return;

            foreach (var c in closures.Values)
            {
                if (!c.Hard || c.Count > cfg.MaxClosures) continue;
                if (now - c.At < cfg.ClosureTtl) continue;

                if (!edgeByKey.TryGetValue(c.Key, out var edge)) continue;

                f.SetTraversalCost(edge, c.BaseCost * cfg.PenaltyFactor);
                var reverse = f.FindReverse(edge);
                if (reverse != null) f.SetTraversalCost(reverse, c.BaseCost * cfg.PenaltyFactor);

                c.Hard = false;
                c.At = now;
            }
        }

        /// <summary>Lezi cil uz uvnitr lokalni mapy? Pak je mrkev primo cil a zadny zvlastni dojezd netreba.</summary>
        private bool IsGoalInMap(LLA target, Point2D robot)
        {
            var g = origin.ToLocal(target);
            double half = cfg.CarrotHalfExtentM;
            return Math.Abs(g.X - robot.X) <= half && Math.Abs(g.Y - robot.Y) <= half;
        }

        /// <summary>
        /// Prevede trasu (hrany site) na lomenou caru v lokalni ENU rovine. Sousedni hrany sdileji
        /// uzel, takze staci pocatek prvni hrany a pak konce vsech.
        /// </summary>
        private List<Point2D> ToPolyline(IReadOnlyList<Edge> route, LLA target)
        {
            var pts = new List<Point2D>(route.Count + 2);
            if (route.Count == 0)
            {
                pts.Add(origin.ToLocal(target));
                return pts;
            }

            pts.Add(origin.ToLocal(route[0].From.Location));
            foreach (var e in route)
                pts.Add(origin.ToLocal(e.To.Location));

            return pts;
        }

        /// <summary>Slozi zpravu o stavu cyklu.</summary>
        private GlobalNavMsg BuildMessage(LLA here, LLA target, Point2D? carrot,
                                          double offRoute, int routeEdges, DateTime now)
        {
            double routeLength = 0;
            var route = Route;
            for (int i = 0; i < route.Count; i++)
                routeLength += route[i].LengthMeters;

            return new GlobalNavMsg
            {
                Status = (int)Status,
                HasGoal = target != null,
                GoalLatDeg = target != null ? Conversions.Rad2Deg(target.Latitude) : 0,
                GoalLonDeg = target != null ? Conversions.Rad2Deg(target.Longitude) : 0,
                LatDeg = Conversions.Rad2Deg(here.Latitude),
                LonDeg = Conversions.Rad2Deg(here.Longitude),
                HasCarrot = carrot.HasValue,
                CarrotX = carrot?.X ?? 0,
                CarrotY = carrot?.Y ?? 0,
                OffRouteDist = offRoute,
                RouteEdgeCount = routeEdges,
                RouteLengthM = routeLength,
                Phi = Phi,
                ClosureCount = closures.Count,
                TimeStamp = now,
            };
        }
    }

    /// <summary>
    /// Prijemce globalniho cile - pouto mezi misi a globalni navigaci
    /// (viz doc/global-navigation-runtime.md).
    /// </summary>
    public interface IGlobalGoalSink
    {
        /// <summary>Nastavi cil mise.</summary>
        void SetGoal(LLA target);

        /// <summary>Prestat jezdit (nouzove zastaveni, nakladka, preruseni mise).</summary>
        void Cancel();
    }
}
