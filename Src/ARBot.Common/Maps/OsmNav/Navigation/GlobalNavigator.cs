using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Routing;
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
    public sealed class GlobalNavigator : MessageProcessor, IGlobalGoalSink
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
                }
                else
                {
                    field.ClearGoal();
                    field.InsertGoal(target);
                }

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

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
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

            return BuildMessage(here, target, carrot, fix.OffRouteDist, route.Count, now);
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
