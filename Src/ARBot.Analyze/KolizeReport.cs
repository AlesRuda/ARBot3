using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Nouzova zastaveni „kolize na aktualni draze"</b> (prikaz <c>kolize</c>, tema
    /// <c>lp-kolize-pri-stani</c>). Pro kazdou zpravu <c>LocalPlanMsg</c> se stavem
    /// <c>AbortedCollision</c> zjisti, co se tehdy delo, a kontrolu kolize ZOPAKUJE
    /// (<see cref="PathCollision.Collides"/>, tentyz kod jako runtime) nad gridem ze snimku
    /// <c>OccupancyGridMsg</c>:
    /// <list type="bullet">
    /// <item><b>Aktivni draha</b> = posledni draha predana PRED udalosti v PORADI ZPRACOVANI (poradi
    /// indexu), ne podle casu snimku - snimky dvou kamer chodi mimo poradi (v indexu ma obraceny cas
    /// 20-25 % planu) a razeni podle casu vybiralo drahu, kterou runtime predal az po udalosti
    /// (nalez kontroly 11. 10. 2026).</item>
    /// <item><b>Bunka robotu</b> = startovni bunka planovace v tomtez cyklu: zprava AbortedCollision je
    /// vysledek planovace s prepsanym stavem, a kdyz planovac nedal drahu (<c>CostSeconds = 0</c>,
    /// <c>LengthM = 0</c>), je <c>ReachedGoalX/Y</c> stred jeho startovni bunky. Poza z
    /// <c>RobotStateMsg</c> lezi u stojiciho robotu desetiny mm od hranice bunek, takze by
    /// zarazeni bylo hod minci (nalez kontroly) - poza se do bunky planovace jen posune.</item>
    /// <item><b>Soustava gridu</b> se zachova (<c>localframe=odom</c>: odometricka poza), grid se
    /// neprevzorkovava.</item>
    /// </list>
    /// Protifakty: <b>postaru</b> = prah kolize SafeDist (do 11. 10. 2026; od te doby je prah kolize
    /// <c>LocalPlannerConfig.TightClearance</c> = <c>SafeDist − bunka/2</c>, oprava (b)), (e) unikova
    /// draha vyjima i bunku prvniho bodu drahy, <b>(d)</b> drahu, kterou ridici smycka uz nejede,
    /// navigator nekontroluje (od 11. 10. 2026, <c>ControlLoop.IsDriving</c>). (d) se ze zaznamu
    /// <b>rekonstruuje</b> pravidlem tiku smycky, PRIBLIZNE: dojeta je draha, pro kterou mezi
    /// „predana + <c>Profile.PathControlTimeOut</c>" a udalosti existuje <c>DriveCommandMsg</c> s doprednou
    /// slozkou 0, pri kterem podle posledniho <c>MotorStateBase</c> kola stala (presne 0; bez mereni
    /// = stoji, jako <c>DriveHoldRegistry.NoteMotorState</c>; stari vzorku se neomezuje, smycka bere
    /// posledni stav taky bez ohledu na stari). Prvni takovy takt dojeti ZAMKNE, jako tik - pozdejsi
    /// pohyb kol uz na tom nic nemeni. Odchylky od tiku: stari se meri od casu SNIMKU predaneho planu
    /// (tik od taktu, ktery predani prevzal, tedy o latenci zpracovani a az takt pozdeji) a dopredna
    /// slozka je ze zpravy, tedy az PO nouzovem zastaveni (tik zamyka podle hodnoty pred nim - lisi
    /// se o takt). Rotace z prikazu se NEposuzuje: binarky pred 5. 10. 2026 u zastarale drahy jeste
    /// tocily na miste (lp-zastaraly-regulator-toci-na-miste), dnesni ji v tomtez stavu nuluje.
    /// Regulator vymeneny nekym jinym (mise) se ze zaznamu poznat neda, takze se pocita jako jety
    /// (konzervativne).
    /// </summary>
    public static class KolizeReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        private sealed class Udalost
        {
            public DateTime T;
            public double Hit;
            public double V = double.NaN, VCmd = double.NaN;
            public bool Estop, Held;
            public LocalPlanMsg Predchozi;      // posledni plan zpracovany pred udalosti
            public LocalPlanMsg Draha;          // posledni predana draha pred udalosti (poradi zpracovani)
            public double StariDrahy = double.NaN;
            public bool NavCil, NavZprava, FreeRun;
            public string NavStav = "-";
            public double GridStari = double.NaN;
            public bool BunkaZPlanovace;        // bunka robotu ze startu planovace (jinak z RobotStateMsg)
            public double Odstup = double.NaN;  // odstup bunky robotu [m]
            public CellState Stav;
            public bool? Zopakovano;
            public string ZasahBunka = "-";
            public bool? Postaru;               // kolidovala by se starym prahem SafeDist (do 11. 10. 2026)?
            public bool? PoOpraveE;             // unik: koliduje i s vyjimkou startovni bunky drahy?
            public bool? SmyckaNejede;          // (d): smycka tehdy drahu uz nejela (rekonstrukce, viz popis tridy)
            public double PovelRot = double.NaN;
            public double DojetaPo = double.NaN; // (d): stari drahy [s], kdy ji smycka podle rekonstrukce zamkla jako dojetou
            public double DalsiDrahaZa = double.NaN;
            public double Ujeto5s = double.NaN;
            public bool Unik => Draha?.PlanStatus == LocalPlanStatus.EscapingBlocked;
        }

        public static void Run(RecordFile rec, int vypis)
        {
            // Soustava gridu a planu tak, jak v ni pracoval runtime (zadne prevzorkovani do sveta).
            rec.LocalLayerInWorld = false;
            var lc = LogConfig.Read(rec);
            double safe = lc.Num("safedist") ?? Profile.SafeDist;
            var cfg = new LocalPlannerConfig { SafeDist = safe };

            var plans = new List<LocalPlanMsg>();      // v PORADI INDEXU (= poradi zpracovani)
            var states = new List<RobotStateMsg>();
            var drives = new List<DriveCommandMsg>();
            var motory = new List<MotorStateBase>();
            var navs = new List<GlobalNavMsg>();
            var free = new List<DateTime>();
            var gridy = new List<IndexEntry>();
            foreach (var e in rec.Index)
            {
                switch (e.MsgName)
                {
                    case "LocalPlanMsg": if (rec.Read(e) is LocalPlanMsg p) plans.Add(p); break;
                    case "RobotStateMsg": if (rec.Read(e) is RobotStateMsg s) states.Add(s); break;
                    case "DriveCommandMsg": if (rec.Read(e) is DriveCommandMsg d) drives.Add(d); break;
                    case "MotorStateBase": if (rec.Read(e) is MotorStateBase ms) motory.Add(ms); break;
                    case "GlobalNavMsg": if (rec.Read(e) is GlobalNavMsg g) navs.Add(g); break;
                    case "FreeRunMsg": if (rec.Read(e) is FreeRunMsg f) free.Add(f.TimeStamp); break;
                    case "OccupancyGridMsg": gridy.Add(e); break;
                }
            }
            states.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            drives.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            motory.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            navs.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            free.Sort();
            gridy.Sort((a, b) => a.CaptureTime.CompareTo(b.CaptureTime));
            int mimoPoradi = 0;
            for (int k = 1; k < plans.Count; k++) if (plans[k].TimeStamp < plans[k - 1].TimeStamp) mimoPoradi++;

            Console.WriteLine(string.Format(Ci,
                "planu {0} (s casem pozadu v poradi zpracovani {1}), RobotStateMsg {2}, GlobalNavMsg {3}, FreeRunMsg {4}, snimku gridu {5}; SafeDist {6:F3} m ({7})",
                plans.Count, mimoPoradi, states.Count, navs.Count, free.Count, gridy.Count, safe,
                lc.Num("safedist").HasValue ? "ze zaznamu" : "vychozi"));

            var udalosti = new List<Udalost>();
            for (int i = 0; i < plans.Count; i++)
            {
                var p = plans[i];
                if (p.PlanStatus != LocalPlanStatus.AbortedCollision) continue;
                var u = new Udalost { T = p.TimeStamp, Hit = p.MinClearanceM };
                for (int k = i - 1; k >= 0; k--)
                {
                    if (u.Predchozi == null && plans[k].PlanStatus != LocalPlanStatus.AbortedCollision) u.Predchozi = plans[k];
                    if (MaDrahu(plans[k]))
                    {
                        u.Draha = plans[k];
                        u.StariDrahy = (p.TimeStamp - plans[k].TimeStamp).TotalSeconds;
                        break;
                    }
                }
                for (int k = i + 1; k < plans.Count; k++)
                    if (MaDrahu(plans[k])) { u.DalsiDrahaZa = (plans[k].TimeStamp - p.TimeStamp).TotalSeconds; break; }

                var st = Nejblizsi(states, x => x.TimeStamp, p.TimeStamp, 0.2);
                if (st != null) u.V = st.V;
                var dr = Posledni(drives, x => x.TimeStamp, p.TimeStamp, 1.0);
                if (dr != null) { u.VCmd = dr.Speed; u.PovelRot = dr.RotationSpeed; u.Estop = dr.EmergencyStop; u.Held = dr.Held; }
                // (d): smycka drahu uz nejede = nekdy po zastarani (PathControlTimeOut) byl takt s doprednym
                // prikazem 0 a stojicimi koly - tik ji tim zamkl jako dojetou (ControlLoop.IsDriving).
                if (u.Draha != null && dr != null)
                {
                    u.SmyckaNejede = false;
                    var od = u.Draha.TimeStamp.AddMilliseconds(Profile.PathControlTimeOut);
                    int di = drives.FindIndex(x => x.TimeStamp > od);
                    for (; di >= 0 && di < drives.Count && drives[di].TimeStamp <= p.TimeStamp; di++)
                    {
                        if (drives[di].Forvard != 0) continue;
                        var mot = Posledni(motory, x => x.TimeStamp, drives[di].TimeStamp, double.MaxValue);
                        bool stoji = mot == null || !mot.HasMeasurement
                                     || (mot.LeftWheelSpeed == 0 && mot.RightWheelSpeed == 0);
                        if (!stoji) continue;
                        u.SmyckaNejede = true;
                        u.DojetaPo = (drives[di].TimeStamp - u.Draha.TimeStamp).TotalSeconds;
                        break;
                    }
                }
                var nv = Posledni(navs, x => x.TimeStamp, p.TimeStamp, 2.0);
                if (nv != null)
                {
                    u.NavZprava = true;
                    u.NavCil = nv.HasGoal;
                    u.NavStav = ((GlobalNavStatus)nv.Status).ToString();
                }
                int fi = free.BinarySearch(p.TimeStamp);
                if (fi < 0) fi = ~fi;
                u.FreeRun = (fi > 0 && (p.TimeStamp - free[fi - 1]).TotalSeconds <= 2) || (fi < free.Count && (free[fi] - p.TimeStamp).TotalSeconds <= 0.2);

                var st5 = Nejblizsi(states, x => x.TimeStamp, p.TimeStamp.AddSeconds(5), 0.3);
                if (st != null && st5 != null)
                    u.Ujeto5s = Math.Sqrt((st5.X - st.X) * (st5.X - st.X) + (st5.Y - st.Y) * (st5.Y - st.Y));

                int gi = PosledniIndex(gridy, p.TimeStamp, 1.0);
                if (gi >= 0 && st != null && rec.Read(gridy[gi]) is OccupancyGridMsg gm)
                {
                    u.GridStari = (p.TimeStamp - gm.TimeStamp).TotalSeconds;
                    var (grid, field) = Rekonstrukce(gm);
                    bool odom = gm.Frame == LocalFrame.Odom && st.HasOdom;
                    double px = odom ? st.OdomX : st.X, py = odom ? st.OdomY : st.Y;

                    // Bunka robotu podle planovace tehoz cyklu (viz popis tridy); poza se do ni posune.
                    int cx = grid.CellX(px), cy = grid.CellY(py);
                    if (p.CostSeconds == 0 && p.LengthM == 0 && double.IsFinite(p.ReachedGoalX) && double.IsFinite(p.ReachedGoalY))
                    {
                        int rx = grid.CellX(p.ReachedGoalX), ry = grid.CellY(p.ReachedGoalY);
                        if (Math.Abs(rx - cx) <= 2 && Math.Abs(ry - cy) <= 2)
                        {
                            u.BunkaZPlanovace = true;
                            (px, py) = DoBunky(grid, rx, ry, px, py);
                            cx = rx; cy = ry;
                        }
                    }
                    if (grid.Contains(cx, cy))
                    {
                        u.Odstup = field.Distance(cx, cy);
                        u.Stav = grid.State(cx, cy);
                    }
                    if (u.Draha != null)
                    {
                        var w = u.Draha.WayPoints;
                        u.Zopakovano = PathCollision.Collides(grid, field, cfg, w, px, py, st.V, u.Unik, out double h);
                        if (u.Zopakovano == true && BodNaDraze(w, px, py, h, out double bx, out double by))
                        {
                            int hx = grid.CellX(bx), hy = grid.CellY(by);
                            if (grid.Contains(hx, hy))
                                u.ZasahBunka = string.Format(Ci, "{0}/{1}/{2:F3}{3}", grid.State(hx, hy),
                                    grid.BlockReason(hx, hy).ToString().Replace(", ", "+"), field.Distance(hx, hy),
                                    hx == cx && hy == cy ? "/pod-robotem"
                                    : hx == grid.CellX(w[0].X) && hy == grid.CellY(w[0].Y) ? "/start-drahy" : "/vedle");
                        }
                        // Protifakt „postaru": prah kolize SafeDist (TightClearance = SafeDist − bunka/2,
                        // takze konfigurace o pul bunky vys).
                        var cfgStary = new LocalPlannerConfig { SafeDist = safe + grid.Resolution / 2 };
                        u.Postaru = PathCollision.Collides(grid, field, cfgStary, w, px, py, st.V, u.Unik, out _);
                        // Protifakt (e): unik vyjima i bunku prvniho bodu drahy.
                        if (u.Unik) u.PoOpraveE = UnikSVyjimkouStartu(grid, cfg, w, px, py, st.V);
                    }
                }
                udalosti.Add(u);
            }

            Console.WriteLine();
            Console.WriteLine($"UDALOSTI AbortedCollision: {udalosti.Count}");
            if (udalosti.Count == 0) return;
            double pasmoOd = safe - 0.025;   // SafeDist − bunka/2 (bunka 5 cm)
            Console.WriteLine("   cas           kolize  v[m/s]  povel  draha (stav, stari s)   predchozi plan     cil nav (stav)   FreeRun  bunka robotu (zdroj) [m]   pasmo  zopak.  trefena bunka (stav/duvod/odstup)        postaru (d)     po (e)");
            foreach (var u in udalosti.Take(vypis))
            {
                string pasmo = double.IsNaN(u.Odstup) ? "?" : u.Stav == CellState.Blocked ? "blok"
                             : u.Odstup < pasmoOd ? "unik" : u.Odstup < safe ? "ANO" : "ne";
                Console.WriteLine(string.Format(Ci,
                    "   {0:HH:mm:ss.f}  {1,6:F2}  {2,6:F2}  {3,5:F2}  {4,-16} {5,5:F1}   {6,-17}  {7,-5} {8,-10}  {9,-7}  {10,6:F3} {11,-7} ({12,-5})  {13,-5}  {14,-6}  {15,-40}  {16,-6}  {18,-7} {17}",
                    u.T, u.Hit, u.V, u.VCmd, u.Draha?.PlanStatus.ToString() ?? "-", u.StariDrahy,
                    u.Predchozi?.PlanStatus.ToString() ?? "-",
                    u.NavZprava ? (u.NavCil ? "ano" : "NE") : "-", u.NavStav, u.FreeRun ? "ano" : "-",
                    u.Odstup, u.Stav, u.BunkaZPlanovace ? "plan" : "RSM", pasmo,
                    u.Zopakovano == null ? "?" : u.Zopakovano.Value ? "ano" : "ne", u.ZasahBunka,
                    u.Postaru == null ? "?" : u.Postaru.Value ? "kol." : "-",
                    u.PoOpraveE == null ? "" : u.PoOpraveE.Value ? "kol." : "-",
                    u.SmyckaNejede == null ? "?" : u.SmyckaNejede.Value
                        ? string.Format(Ci, "nejede od {0:F1} s", u.DojetaPo) : "jede"));
            }

            var bezne = udalosti.Where(u => !u.Unik).ToList();
            var unik = udalosti.Where(u => u.Unik).ToList();
            Console.WriteLine();
            Console.WriteLine(string.Format(Ci,
                "SOUHRN bezna draha: {0}; robot stal (|v| < 0,05) {1}, jel (|v| >= 0,1) {2}; zopakovano {3}; bunka robotu v pasmu {4:F3}-{5:F3} m {6}," +
                " >= SafeDist {7}; trefa pod robotem {8}, start drahy {9}, vedle {10}; bez cile {11}; draha starsi nez 0,5 s {12};" +
                " postaru (prah SafeDist) by kolidovalo {13}; smycka drahu uz nejela (d) {14}; postaru a smycka jela (zbylo by jen s (d)) {15};" +
                " dnes (b + d) zustane {16}",
                bezne.Count, bezne.Count(u => Math.Abs(u.V) < 0.05), bezne.Count(u => Math.Abs(u.V) >= 0.1),
                bezne.Count(u => u.Zopakovano == true), pasmoOd, safe,
                bezne.Count(u => !double.IsNaN(u.Odstup) && u.Stav != CellState.Blocked && u.Odstup >= pasmoOd && u.Odstup < safe),
                bezne.Count(u => u.Odstup >= safe),
                bezne.Count(u => u.ZasahBunka.EndsWith("pod-robotem")), bezne.Count(u => u.ZasahBunka.EndsWith("start-drahy")),
                bezne.Count(u => u.ZasahBunka.EndsWith("vedle")),
                bezne.Count(u => u.NavZprava && !u.NavCil && !u.FreeRun), bezne.Count(u => u.StariDrahy > 0.5),
                bezne.Count(u => u.Postaru == true), bezne.Count(u => u.SmyckaNejede == true),
                bezne.Count(u => u.Postaru == true && u.SmyckaNejede != true),
                bezne.Count(u => u.Zopakovano == true && u.SmyckaNejede != true)));
            Console.WriteLine(string.Format(Ci,
                "SOUHRN unikova draha: {0}; zopakovano {1}; trefa start drahy {2}, pod robotem {3}, vedle {4}; zustane po (e) {5};" +
                " smycka drahu uz nejela (d) {6}; dnes (d) zustane {7}",
                unik.Count, unik.Count(u => u.Zopakovano == true), unik.Count(u => u.ZasahBunka.EndsWith("start-drahy")),
                unik.Count(u => u.ZasahBunka.EndsWith("pod-robotem")), unik.Count(u => u.ZasahBunka.EndsWith("vedle")),
                unik.Count(u => u.PoOpraveE == true), unik.Count(u => u.SmyckaNejede == true),
                unik.Count(u => u.Zopakovano == true && u.SmyckaNejede != true)));
            Console.WriteLine("  zopak. = dnesni kod (prah SafeDist − bunka/2 = prah uniku, od 11. 10. 2026), postaru = prah SafeDist,");
            Console.WriteLine("  (e) = unik vyjima i bunku 1. bodu drahy, (d) = smycka drahu uz nejela (draha starsi nez");
            Console.WriteLine("  PathControlTimeOut byl takt s doprednym prikazem 0 a stojicimi koly; rekonstrukce je priblizna,");
            Console.WriteLine("  viz popis tridy) - od 11. 10. 2026 se takova draha nekontroluje.");
            Console.WriteLine("  CSV: soubor;bezne;zopak.;postaru;unik;unik zopak.;unik po (e);bezne starsi 0,5 s;bezne nejede (d);bezne dnes;unik dnes");
            Console.WriteLine(string.Format(Ci, "SOUHRN;{0};{1};{2};{3};{4};{5};{6};{7};{8};{9};{10}",
                System.IO.Path.GetFileName(rec.Path), bezne.Count, bezne.Count(u => u.Zopakovano == true),
                bezne.Count(u => u.Postaru == true), unik.Count, unik.Count(u => u.Zopakovano == true),
                unik.Count(u => u.PoOpraveE == true), bezne.Count(u => u.StariDrahy > 0.5),
                bezne.Count(u => u.SmyckaNejede == true),
                bezne.Count(u => u.Zopakovano == true && u.SmyckaNejede != true),
                unik.Count(u => u.Zopakovano == true && u.SmyckaNejede != true)));
        }

        private static bool MaDrahu(LocalPlanMsg p)
            => p.PlanStatus != LocalPlanStatus.AbortedCollision && p.WayPoints != null && p.WayPoints.Length >= 2;

        /// <summary>
        /// Posune (px, py) do bunky (cx, cy) nejkratsi cestou, jen 0,01 mm za hranici. Robot u bezne
        /// drahy stoji desetiny mm od hranice bunek a prumet na drahu je na takovy posun citlivy -
        /// vetsi posun (prvni verze: 1 mm) zmenil vysledek u 4 udalosti Kola 4.
        /// </summary>
        private static (double X, double Y) DoBunky(OccupancyGrid g, int cx, int cy, double px, double py)
        {
            double h = g.Resolution / 2 - 1e-5;
            double sx = g.CenterX(cx), sy = g.CenterY(cy);
            return (Math.Max(sx - h, Math.Min(sx + h, px)), Math.Max(sy - h, Math.Min(sy + h, py)));
        }

        /// <summary>
        /// Protifakt (e): kontrola unikove drahy jako <see cref="PathCollision.Collides"/>, ale s vyjimkou
        /// i pro bunku PRVNIHO BODU drahy (start planu), nejen pro bunku pod robotem. Kopie pravidla
        /// unikove vetve (jen geometrie), ne cela kontrola.
        /// </summary>
        private static bool UnikSVyjimkouStartu(OccupancyGrid grid, LocalPlannerConfig cfg, RegulatorWayPoint[] path,
                                                double rx, double ry, double v)
        {
            if (path == null || path.Length < 2) return false;
            v = Math.Abs(v);
            double check = v * v / (2.0 * cfg.MaxAcceleration) + v * (Profile.Ts / 1000.0) + grid.Resolution;
            int robCx = grid.CellX(rx), robCy = grid.CellY(ry);
            int stCx = grid.CellX(path[0].X), stCy = grid.CellY(path[0].Y);
            int seg = 0; double t = 0, best = double.MaxValue;
            for (int i = 0; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y, dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double l2 = dx * dx + dy * dy;
                if (l2 < 1e-18) continue;
                double uu = Math.Max(0, Math.Min(1, ((rx - x0) * dx + (ry - y0) * dy) / l2));
                double qx = x0 + dx * uu - rx, qy = y0 + dy * uu - ry;
                if (qx * qx + qy * qy < best) { best = qx * qx + qy * qy; seg = i; t = uu; }
            }
            double step = grid.Resolution * 0.5, traveled = 0;
            for (int i = seg; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y, dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double from = i == seg ? t * len : 0;
                for (double s = from; s <= len; s += step)
                {
                    if (traveled + (s - from) > check) return false;
                    int cx = grid.CellX(x0 + dx * (s / len)), cy = grid.CellY(y0 + dy * (s / len));
                    if (!grid.Contains(cx, cy)) continue;
                    if ((cx == robCx && cy == robCy) || (cx == stCx && cy == stCy)) continue;
                    if (grid.BlockReason(cx, cy).HasFlag(CellBlockReason.Geometry)) return true;
                }
                traveled += len - from;
            }
            return false;
        }

        /// <summary>Bod na draze ve vzdalenosti <paramref name="d"/> od prumetu (x, y) - jako PathCollision.</summary>
        private static bool BodNaDraze(RegulatorWayPoint[] path, double x, double y, double d,
                                       out double bx, out double by)
        {
            bx = by = double.NaN;
            if (path == null || path.Length < 2 || double.IsNaN(d)) return false;
            int seg = 0; double t = 0, best = double.MaxValue;
            for (int i = 0; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y, dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double l2 = dx * dx + dy * dy;
                if (l2 < 1e-18) continue;
                double uu = Math.Max(0, Math.Min(1, ((x - x0) * dx + (y - y0) * dy) / l2));
                double px = x0 + dx * uu - x, py = y0 + dy * uu - y;
                if (px * px + py * py < best) { best = px * px + py * py; seg = i; t = uu; }
            }
            double zbyva = d;
            for (int i = seg; i < path.Length - 1; i++)
            {
                double x0 = path[i].X, y0 = path[i].Y, dx = path[i + 1].X - x0, dy = path[i + 1].Y - y0;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double from = i == seg ? t * len : 0;
                if (zbyva <= len - from)
                {
                    double s0 = from + zbyva;
                    bx = x0 + dx * s0 / len; by = y0 + dy * s0 / len;
                    return true;
                }
                zbyva -= len - from;
            }
            return false;
        }

        private static (OccupancyGrid Grid, ClearanceField Field) Rekonstrukce(OccupancyGridMsg m)
        {
            var g = new OccupancyGrid(new OccupancyGridConfig
            {
                Size = m.Size,
                Resolution = m.Resolution,
                Scale = m.Scale,
                BlockedThreshold = m.BlockedThreshold,
                FreeThreshold = m.FreeThreshold,
            });
            g.MoveOrigin(m.OriginX, m.OriginY);
            int n = m.Size;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int dst = g.LocalIndex(i, j);
                    g.Occ[dst] = m.Occ[i + j * n];
                    g.Road[dst] = m.Road != null ? m.Road[i + j * n] : (sbyte)0;
                }
            var f = new ClearanceField(g);
            f.Build(g);
            return (g, f);
        }

        private static T Nejblizsi<T>(List<T> l, Func<T, DateTime> cas, DateTime t, double tolSec) where T : class
        {
            int lo = 0, hi = l.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (cas(l[m]) < t) lo = m + 1; else hi = m; }
            T best = null; double bd = tolSec;
            for (int k = Math.Max(0, lo - 1); k <= Math.Min(l.Count - 1, lo); k++)
            {
                double d = Math.Abs((cas(l[k]) - t).TotalSeconds);
                if (d <= bd) { bd = d; best = l[k]; }
            }
            return best;
        }

        private static int PosledniIndex(List<IndexEntry> l, DateTime t, double maxStariSec)
        {
            int lo = 0, hi = l.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (l[m].CaptureTime <= t) lo = m + 1; else hi = m; }
            if (lo == 0) return -1;
            return (t - l[lo - 1].CaptureTime).TotalSeconds <= maxStariSec ? lo - 1 : -1;
        }

        private static T Posledni<T>(List<T> l, Func<T, DateTime> cas, DateTime t, double maxStariSec) where T : class
        {
            int lo = 0, hi = l.Count;
            while (lo < hi) { int m = (lo + hi) / 2; if (cas(l[m]) <= t) lo = m + 1; else hi = m; }
            if (lo == 0) return null;
            var x = l[lo - 1];
            return (t - cas(x)).TotalSeconds <= maxStariSec ? x : null;
        }
    }
}
