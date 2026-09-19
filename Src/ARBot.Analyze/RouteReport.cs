using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Maps.OsmNav.Osm;
using ARBot.Common.Maps.OsmNav.Routing;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Vede po síti mapy trasa z A do B — a když ne, PROČ?</b> Totéž, co dělá
    /// <c>GlobalNavigator.Probe</c> při přijímání cíle z QR kódu (přichycení, pole cost-to-goal
    /// nad přichyceným cílem, obě orientace startovní hrany), jen offline nad souborem `.osm`
    /// a s vysvětlením.
    ///
    /// <para><b>Nač to je:</b> 19. 9. 2026 na soutěži mise zamítla kód s cílem
    /// <c>50.1038082,14.4240751</c> hláškou „na cíl nevede po síti žádná trasa (je mimo mapu?)" —
    /// a ten bod je přitom <b>přesně uzel mapy</b> na živé cestě <c>footway</c>. Hláška tedy
    /// nerozliší „mimo mapu" od „síť je v tom místě rozpojená" od „profil tu cestu nepouští"
    /// (schody), a stránka náhledu ji ani neukázala. Tady se to rozebere: komponenty souvislosti
    /// sítě, do které komponenty padá start a cíl, a cena v obou orientacích.</para>
    ///
    /// <para>Použití: <c>ARBot.Analyze route --map=OSM/x.osm --from=lat,lon --to=lat,lon
    /// [--roadwidth=3]</c>. Souřadnice ve <b>stupních</b> (je to okraj systému, jako u `.track`).</para>
    /// </summary>
    public static class RouteReport
    {
        public static void Run(string mapPath, string from, string to, double roadWidth, bool prune = true)
        {
            if (string.IsNullOrWhiteSpace(mapPath) || !File.Exists(mapPath))
            {
                Console.Error.WriteLine("route: --map=<cesta.osm> je povinne (a soubor musi existovat).");
                return;
            }
            if (!TryLla(from, out var a) || !TryLla(to, out var b))
            {
                Console.Error.WriteLine("route: --from=lat,lon a --to=lat,lon ve stupnich jsou povinne.");
                return;
            }

            RoadNetwork net;
            NetworkIslands.Report ostrovy = null;
            using (var fs = File.OpenRead(mapPath))
            {
                var data = OsmXmlReader.Read(fs);
                // Totez, co runtime s mapprune=true: ostrovy pryc. --noprune ukaze puvodni sit.
                if (prune) data = NetworkIslands.Prune(data, TravelProfile.Robot(), out ostrovy);
                net = GraphBuilder.BuildNetwork(data, TravelProfile.Robot(), roadWidth);
            }
            if (ostrovy != null) Console.WriteLine($"  mapprune: {NetworkIslands.Describe(ostrovy)}");
            else Console.WriteLine("  mapprune: VYPNUTO (--noprune), sit jako v souboru");

            var nodes = new HashSet<long>();
            foreach (var e in net.Edges) { nodes.Add(e.From.Id); nodes.Add(e.To.Id); }
            Console.WriteLine($"Mapa: {mapPath}");
            Console.WriteLine($"  hran (orientovanych) {net.Edges.Count}, uzlu {nodes.Count}, profil Robot "
                              + "(schody se NEPOUSTEJI), objekty s action=delete preskocene");

            // ---- Komponenty souvislosti (neorientovane) ----
            var comp = Components(net);
            var velikosti = comp.Values.GroupBy(c => c).Select(g => (id: g.Key, n: g.Count()))
                                .OrderByDescending(x => x.n).ToList();
            Console.WriteLine($"  komponent souvislosti: {velikosti.Count}"
                              + (velikosti.Count > 1 ? "  <- SIT JE ROZPOJENA" : ""));
            foreach (var v in velikosti.Take(8))
            {
                Console.WriteLine($"    komponenta #{v.id}: {v.n} uzlu, delka hran {DelkaKomponenty(net, comp, v.id):F0} m");
                // U ostruvku vypsat cesty, ze kterych se sklada - podle nich se v JOSM najde, co je
                // odrizlo (smazana spojka, schody, chybejici uzel).
                if (v.id != velikosti[0].id)
                    Console.WriteLine("      cesty (way id): " + string.Join(", ",
                        net.Edges.Where(e => comp[e.From.Id] == v.id).Select(e => e.WayId).Distinct().OrderBy(w => w)));
            }

            // ---- Start a cil ----
            Console.WriteLine();
            var ea = net.NearestEdge(a, out _, out var pa, out double da);
            var eb = net.NearestEdge(b, out _, out var pb, out double db);
            Popis("START", a, ea, pa, da, comp);
            Popis("CIL  ", b, eb, pb, db, comp);
            if (ea == null || eb == null) { Console.WriteLine("  Sit nema hranu, na kterou by se dalo prichytit."); return; }

            int ca = comp[ea.From.Id], cb = comp[eb.From.Id];
            Console.WriteLine();
            if (ca != cb)
            {
                Console.WriteLine($"  !! Start je v komponente #{ca}, cil v komponente #{cb} -> NoRoute je SPRAVNE, "
                                  + "ale pricina je ROZPOJENA SIT, ne cil mimo mapu.");
                Console.WriteLine("     Hledej mezi nimi cestu, ktera v mape chybi nebo je smazana (action=delete),");
                Console.WriteLine("     nebo je to schodiste (profil Robot 'steps' nepousti).");
                NejblizsiMezera(net, comp, ca, cb);
            }

            // ---- Totez, co Probe: pole nad prichycenym cilem, obe orientace startu ----
            var field = new GoalField(net, pb);
            var node = field.NearestNode(a, out _, out _, out _);
            if (node == null) { Console.WriteLine("  Pole nenaslo startovni hranu."); return; }
            field.EnsureSettled(node);
            double cost = field.CostToGoal(node);
            double costRev = double.PositiveInfinity;
            var rev = field.FindReverse(node);
            if (rev != null) { field.EnsureSettled(rev); costRev = field.CostToGoal(rev); }
            Console.WriteLine($"  cena z hrany {node.From.Id}->{node.To.Id}: {F(cost)}; opacna orientace: {F(costRev)}");
            double best = Math.Min(cost, costRev);
            if (double.IsInfinity(best) || double.IsNaN(best))
            {
                Console.WriteLine("  VERDIKT: NEDOSAZITELNE (Probe by vratil Reachable=false).");
                return;
            }
            var route = new Router(field).Plan(a);
            double len = 0; foreach (var e in route) len += e.LengthMeters;
            Console.WriteLine($"  VERDIKT: DOSAZITELNE, trasa {route.Count} hran, {len:F0} m.");
        }

        private static void Popis(string co, LLA p, Edge e, LLA proj, double dist, Dictionary<long, int> comp)
        {
            Console.WriteLine($"  {co} {Deg(p.Latitude):F7},{Deg(p.Longitude):F7}"
                              + (e == null ? "  -> zadna hrana"
                                           : $"  -> hrana {e.From.Id}->{e.To.Id} (way {e.WayId}), {dist:F1} m od site, komponenta #{comp[e.From.Id]}"));
        }

        /// <summary>Nejblizsi dvojice uzlu ze dvou komponent — kde by mela byt spojka.</summary>
        private static void NejblizsiMezera(RoadNetwork net, Dictionary<long, int> comp, int ca, int cb)
        {
            var na = net.Edges.Where(e => comp[e.From.Id] == ca).Select(e => e.From).Distinct().ToList();
            var nb = net.Edges.Where(e => comp[e.From.Id] == cb).Select(e => e.From).Distinct().ToList();
            double best = double.PositiveInfinity; Node ba = null, bb = null;
            foreach (var x in na)
                foreach (var y in nb)
                {
                    double d = Dist(x.Location, y.Location);
                    if (d < best) { best = d; ba = x; bb = y; }
                }
            if (ba != null)
                Console.WriteLine($"     Nejblizsi uzly obou komponent: {ba.Id} a {bb.Id}, {best:F1} m od sebe "
                                  + $"({Deg(ba.Location.Latitude):F6},{Deg(ba.Location.Longitude):F6} / "
                                  + $"{Deg(bb.Location.Latitude):F6},{Deg(bb.Location.Longitude):F6}).");
            // Vsechna mista, kde se komponenty skoro dotykaji - kandidati na spojku v JOSM.
            var blizko = new List<(double d, Node x, Node y)>();
            foreach (var x in na)
                foreach (var y in nb)
                {
                    double d = Dist(x.Location, y.Location);
                    if (d < 3.0) blizko.Add((d, x, y));
                }
            if (blizko.Count > 1)
            {
                Console.WriteLine($"     Dvojice uzlu blize nez 3 m (kde by spojka mohla byt): {blizko.Count}");
                foreach (var p in blizko.OrderBy(p => p.d).Take(12))
                    Console.WriteLine($"       {p.x.Id} - {p.y.Id}: {p.d:F1} m  ({Deg(p.x.Location.Latitude):F6},{Deg(p.x.Location.Longitude):F6})");
            }
        }

        private static Dictionary<long, int> Components(RoadNetwork net)
        {
            var adj = new Dictionary<long, List<long>>();
            void Add(long u, long v) { if (!adj.TryGetValue(u, out var l)) adj[u] = l = new List<long>(); l.Add(v); }
            foreach (var e in net.Edges) { Add(e.From.Id, e.To.Id); Add(e.To.Id, e.From.Id); }
            var comp = new Dictionary<long, int>();
            int id = 0;
            foreach (var start in adj.Keys)
            {
                if (comp.ContainsKey(start)) continue;
                id++;
                var stack = new Stack<long>(); stack.Push(start); comp[start] = id;
                while (stack.Count > 0)
                {
                    var u = stack.Pop();
                    foreach (var v in adj[u]) if (!comp.ContainsKey(v)) { comp[v] = id; stack.Push(v); }
                }
            }
            return comp;
        }

        private static double DelkaKomponenty(RoadNetwork net, Dictionary<long, int> comp, int id)
            => net.Edges.Where(e => comp[e.From.Id] == id).Sum(e => e.LengthMeters) / 2;   // obe orientace

        private static double Dist(LLA a, LLA b)
        {
            double lat = (a.Latitude + b.Latitude) / 2;
            double dy = (a.Latitude - b.Latitude) * 6371000;
            double dx = (a.Longitude - b.Longitude) * 6371000 * Math.Cos(lat);
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool TryLla(string s, out LLA lla)
        {
            lla = null;
            if (string.IsNullOrWhiteSpace(s)) return false;
            var p = s.Split(',');
            if (p.Length != 2
                || !double.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
                || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
                return false;
            lla = new LLA(Conversions.Deg2Rad(lat), Conversions.Deg2Rad(lon));
            return true;
        }

        private static double Deg(double rad) => Conversions.Rad2Deg(rad);
        private static string F(double c) => double.IsInfinity(c) || double.IsNaN(c) ? "nekonecno" : c.ToString("F1", CultureInfo.InvariantCulture);
    }
}
