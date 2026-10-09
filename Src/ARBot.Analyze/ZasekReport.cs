using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Missions;
using ARBot.Common.Models;
using ARBot.Common.Occupancy;
using SkiaSharp;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Proc robot stoji v blokovane oblasti</b> (prikaz <c>zasek</c>). Vzniklo 7. 10. 2026 nad
    /// Trackem z 1. 10. 2026 (<c>20261001-144638.rec</c>), kde robot na konci stal minuty
    /// v <c>RobotBlocked</c> a autor cekal unik nebo „vrteni na miste".
    ///
    /// <para><b>Co to dela.</b> Nad kazdym snapshotem gridu (<see cref="OccupancyGridMsg"/>, 2 Hz)
    /// v okne <c>--from..--to</c> rozebere bunku pod robotem (stav, kanal, log-odds, odstup z EDT)
    /// a <b>zopakuje hledani uniku</b> stejnymi pravidly jako <c>LocalPathPlanner.PlanEscape</c>:
    /// pres geometricky blokovane bunky se nesmi, vychod je bunka neblokovana s odstupem
    /// <c>&gt;= SafeDist</c>. Hleda se ale <b>bez horizontu</b>, takze report rekne, JAK DALEKO
    /// nejblizsi vychod je a jestli ho zatarasila geometrie (protifakt: totez hledani bez zakazu
    /// geometrie). Pro kontrolu se pusti i skutecny planovac (<see cref="LocalPathPlanner"/>) nad
    /// tymz gridem a jeho stav se srovna se zaznamenanym planem.</para>
    ///
    /// <para><b>Stari bunek.</b> Grid nema casovy rozpad - hodnotu prepise jen nove pozorovani.
    /// Ze snapshotu se proto pocita, jak dlouho se hodnota bunky NEZMENILA, a s <c>--frames</c>
    /// se snimky kamer znovu zapisi kodem robota (<see cref="OccupancyIntegrator"/>) do prazdneho
    /// „sondovaciho" gridu, takze je videt, KDY kterou bunku kamera naposledy opravdu pozorovala
    /// (stari hodnoty samo nestaci: bunka na clampu se pozorovanim nezmeni). Sonda pokryva cely
    /// dosah integratoru, takze se pocita i pozorovani z dalky (bunka videna 3-4 m pred robotem,
    /// nez k ni dojel). Tentyz pruchod
    /// prehraje i cely grid (s <see cref="WedgeFiller"/> a detektorem skoku pozy ve verzi
    /// z robotu) a srovna ho se zaznamenanymi snapshoty - tim se overuje, ze rekonstrukce
    /// projekci sedi.</para>
    ///
    /// <para><b>Obrazky</b> (<c>--png=</c>, pro kazdy cas z <c>--at=</c>): pudorys v ramci
    /// robotu (vpred nahoru) podle kanalu, kdy kamera bunku naposledy videla, a kdy se hodnota
    /// naposledy zmenila; vedle toho snimky obou kamer (barva, pravdepodobnost site, hloubka).</para>
    ///
    /// <para>Casy <c>--from/--to/--at</c> jsou v sekundach od prvniho <c>LocalPlanMsg</c>, tedy
    /// stejne jako v reportu <c>localplan</c>.</para>
    /// </summary>
    public static class ZasekReport
    {
        /// <summary>Prepinace prikazu.</summary>
        public sealed class Volby
        {
            public double From = double.NaN, To = double.NaN;
            public List<double> At = new List<double>();
            public string Png;
            public double Radius = 1.5;
            public double Lookback = 120;
            public bool Frames;
            public int FrameStep = 1;
            public double SafeDist = 0.4;
            public double EscapeMax = 1.5;
            public double EscapeCost = 4.0;
            public double MaxSpeed = 1.7;
            public int Every = 1;
            /// <summary>Okno [s] pro radkovy detail pozy, IMU, prikazu a mereni koridoru; null = nevypisovat.</summary>
            public double[] Detail;
            /// <summary>Jak daleko [m] se hleda vychod bez horizontu.</summary>
            public double SearchMax = 8.0;
        }

        // Osmiokoli v poradi planovace (na vysledku hledani nejkratsi delky nezalezi, jen na shodnosti).
        private static readonly int[] NeighDx = { 1, 0, -1, 0, 1, 1, -1, -1 };
        private static readonly int[] NeighDy = { 0, 1, 0, -1, 1, -1, 1, -1 };

        private struct Poza { public DateTime T; public double X, Y, Th, V, Om; }

        /// <summary>Snapshot gridu pripraveny pro hledani (lokalni poradi i + j*N, jako planovac).</summary>
        private sealed class Ctx
        {
            public OccupancyGridMsg Msg;
            public OccupancyGrid Grid;
            public ClearanceField Field;
            public int N;
            public double Res;
            public byte[] Stav;
            public byte[] Duvod;
            public float[] Odstup;
            public double SafeDist;
        }

        /// <summary>Rozbor jednoho snapshotu.</summary>
        private sealed class Rozbor
        {
            public DateTime T;
            public double X, Y, Th;
            public CellState Stav;
            public CellBlockReason Duvod;
            public float LOcc, LRoad;
            public double Odstup;
            public bool Unik;                       // splnena podminka pro PlanEscape
            public double ExitLen = double.NaN;      // delka nejkratsi unikove cesty (pravidla uniku)
            public double ExitDirBody = double.NaN;  // smer k vychodu v ramci robotu [st.], 0 = vpred, + = vlevo
            public double ExitDist = double.NaN;     // primo vzdusnou carou k tomu vychodu
            public int ExitIdx = -1;
            public int ExitPathSem, ExitPathUnk;     // bunek na unikove ceste blokovanych semantikou / Unknown
            public double ExitLenBezZakazu = double.NaN;   // totez bez zakazu geometrie (protifakt)
            public double ExitDirBezZakazu = double.NaN;
            public double EuklExit = double.NaN;     // vzdusnou carou k nejblizsi legalni bunce
            public double EuklExitDir = double.NaN;
            public int VHorizontu;                   // bunek dosazitelnych unikem do EscapeMax
            public int Geom, Sem, Obe, Free, Unk, UnkBezDat;   // v polomeru --radius
            public int Known;
            public double NejblizsiGeom = double.NaN;  // vzdalenost k nejblizsi geometricky blokovane bunce
            public LocalPlanStatus? Replay;
            public LocalPlanMsg Plan;                // zaznamenany plan k tomuto snapshotu (tyz cas)
            public List<int> ExitPath;
            public double[] DistHorizon;             // pro kresleni oblasti dosazitelne unikem
        }

        /// <summary>Historie hodnoty bunky ze snapshotu (absolutni bunka).</summary>
        private sealed class Hist
        {
            public sbyte Occ, Road;
            public DateTime OccZmena, RoadZmena;
            public bool OccOdZacatku = true, RoadOdZacatku = true;
        }

        /// <summary>Pozorovani bunky kamerou (z prehrani snimku do sondovaciho gridu).</summary>
        private sealed class Vid
        {
            public DateTime OccPosl = DateTime.MinValue, RoadPosl = DateTime.MinValue;
            public int OccN, RoadN, OccObs, RoadNon;
        }

        /// <summary>
        /// Overeni prehrani gridu proti snapshotum (blok 8) a mazani gridu replikou detektoru skoku
        /// (blok 4). Shoda se pocita ZVLAST pro stav a pro hodnoty obou kanalu: stav rozhoduje
        /// planovac, ale hodnoty rikaji, jestli prehrani zapisuje TOTEZ - u stavu se rozdil v hodnote
        /// schova, dokud nepreleze prah.
        /// </summary>
        private sealed class Overeni
        {
            public readonly Stats Stav = new Stats("shoda STAVU bunek (Free/Blocked/Unknown) [%]");
            public readonly Stats Occ = new Stats("shoda HODNOT occ (geometrie) [%]          ");
            public readonly Stats Road = new Stats("shoda HODNOT road (semantika) [%]         ");
            public readonly Stats Obe = new Stats("shoda HODNOT occ i road soucasne [%]      ");
            public readonly List<(DateTime t, double stav, double occ, double road, double obe)> PoCase
                = new List<(DateTime, double, double, double, double)>();
            /// <summary>Kdy replika detektoru skoku smazala grid (cas snimku) a radek pro vypis.</summary>
            public readonly List<(DateTime t, string popis)> Mazani = new List<(DateTime, string)>();
        }

        private static long Klic(int cx, int cy) => ((long)cx << 32) | (uint)cy;

        public static void Run(RecordFile rec, Volby v)
        {
            var ci = CultureInfo.InvariantCulture;

            // ---------------- cas 0 a okno ----------------
            DateTime t0 = DateTime.MinValue;
            foreach (var e in rec.Index)
                if (e.MsgName == "LocalPlanMsg" && rec.Read(e) is LocalPlanMsg p0) { t0 = p0.TimeStamp; break; }
            if (t0 == DateTime.MinValue) { Console.WriteLine("Zaznam nema LocalPlanMsg."); return; }

            DateTime tKonec = rec.Index.Where(e => e.MsgName == "LocalPlanMsg").Select(e => e.CaptureTime).DefaultIfEmpty(t0).Max();
            double from = double.IsNaN(v.From) ? Math.Max(0, (tKonec - t0).TotalSeconds - 60) : v.From;
            double to = double.IsNaN(v.To) ? (tKonec - t0).TotalSeconds + 1 : v.To;
            DateTime wFrom = t0.AddSeconds(from), wTo = t0.AddSeconds(to);
            DateTime hFrom = wFrom.AddSeconds(-v.Lookback);

            double T(DateTime t) => (t - t0).TotalSeconds;

            Console.WriteLine(string.Format(ci, "t = 0 je prvni LocalPlanMsg {0:HH:mm:ss.fff}", t0));
            Console.WriteLine(string.Format(ci, "okno {0:F1}-{1:F1} s ({2:HH:mm:ss}-{3:HH:mm:ss}), historie gridu od {4:F1} s, "
                + "SafeDist {5:F2} m, EscapeMax {6:F2} m, polomer okoli {7:F2} m",
                from, to, wFrom, wTo, from - v.Lookback, v.SafeDist, v.EscapeMax, v.Radius));
            Console.WriteLine();

            // ---------------- nacteni zprav ----------------
            var pozy = new List<Poza>();
            var plany = new List<LocalPlanMsg>();
            var gridy = new List<OccupancyGridMsg>();
            var gn = new List<GlobalNavMsg>();
            var tracky = new List<TrackMsg>();
            var info = new List<Info>();
            var diag = new List<MeasurementDiagMsg>();
            var drive = new List<DriveCommandMsg>();
            var imu = new List<(DateTime t, double yaw, double gz)>();
            var motory = new List<MotorStateBase>();
            DateTime detOd = v.Detail != null && v.Detail.Length == 2 ? t0.AddSeconds(v.Detail[0]) : DateTime.MaxValue;
            DateTime detDo = v.Detail != null && v.Detail.Length == 2 ? t0.AddSeconds(v.Detail[1]) : DateTime.MinValue;
            var snimky = new List<IndexEntry>();

            DateTime okrajOd = hFrom.AddSeconds(-5), okrajDo = wTo.AddSeconds(5);
            foreach (var e in rec.Index)
            {
                if (e.MsgName == "Info")
                {
                    if (rec.Read(e) is Info inf && inf.TimeStamp >= wFrom.AddSeconds(-30) && inf.TimeStamp <= okrajDo) info.Add(inf);
                    continue;
                }
                var ct = e.CaptureTime;
                if (ct < okrajOd || ct > okrajDo) continue;
                switch (e.MsgName)
                {
                    case "RobotStateMsg":
                        if (rec.Read(e) is RobotStateMsg rs)
                            pozy.Add(new Poza { T = rs.TimeStamp, X = rs.X, Y = rs.Y, Th = rs.Theta, V = rs.V, Om = rs.Omega });
                        break;
                    case "LocalPlanMsg":
                        if (ct >= wFrom.AddSeconds(-5) && rec.Read(e) is LocalPlanMsg lp) plany.Add(lp);
                        break;
                    case "OccupancyGridMsg":
                        if (rec.Read(e) is OccupancyGridMsg og && og.TimeStamp >= hFrom && og.TimeStamp <= wTo) gridy.Add(og);
                        break;
                    case "GlobalNavMsg":
                        if (ct >= wFrom.AddSeconds(-30) && rec.Read(e) is GlobalNavMsg g) gn.Add(g);
                        break;
                    case "TrackMsg":
                        if (ct >= wFrom.AddSeconds(-30) && rec.Read(e) is TrackMsg tm) tracky.Add(tm);
                        break;
                    case "MeasurementDiagMsg":
                        if (ct >= wFrom && rec.Read(e) is MeasurementDiagMsg md) diag.Add(md);
                        break;
                    case "DriveCommandMsg":
                        if (ct >= wFrom && rec.Read(e) is DriveCommandMsg dc) drive.Add(dc);
                        break;
                    case "MotorStateBase":
                        if (ct >= detOd && ct <= detDo && rec.Read(e) is MotorStateBase ms) motory.Add(ms);
                        break;
                    case "IMUState":
                        if (ct >= wFrom && rec.Read(e) is IMUState im)
                        {
                            var ypr = im.YPR();
                            imu.Add((im.TimeStamp, ypr != null ? ypr.Yaw : double.NaN,
                                     im.AngularVelocity.HasValue ? im.AngularVelocity.Value.Z : double.NaN));
                        }
                        break;
                    case "CameraFrame":
                        if (ct >= wFrom.AddSeconds(-1)) snimky.Add(e);
                        break;
                }
            }
            pozy.Sort((a, b) => a.T.CompareTo(b.T));
            plany.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            gridy.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            Console.WriteLine($"nacteno: RobotStateMsg {pozy.Count}, LocalPlanMsg {plany.Count}, OccupancyGridMsg {gridy.Count} "
                              + $"(s historii), GlobalNavMsg {gn.Count}, TrackMsg {tracky.Count}, Info {info.Count}, "
                              + $"MeasurementDiagMsg {diag.Count}, CameraFrame v okne {snimky.Count}");
            Console.WriteLine();
            if (gridy.Count == 0 || pozy.Count == 0) { Console.WriteLine("Chybi gridy nebo pozy."); return; }

            var planPodleCasu = new Dictionary<long, LocalPlanMsg>();
            foreach (var p in plany) planPodleCasu[p.TimeStamp.Ticks] = p;

            // Casy --at se prevedou na snapshot, nad kterym se rozbor dela (posledni snapshot <= at).
            // Historie hodnot i pozorovani se pro ne ZMRAZI v tom case - jinak by stari bunky
            // pocitalo i se zmenami, ktere prisly az pozdeji (zaporne stari).
            var cileAt = new SortedSet<DateTime>();
            foreach (double at in v.At)
            {
                DateTime tAt = t0.AddSeconds(at);
                var mAt = gridy.LastOrDefault(g => g.TimeStamp <= tAt && g.TimeStamp >= wFrom) ?? gridy.FirstOrDefault(g => g.TimeStamp >= wFrom);
                if (mAt != null) cileAt.Add(mAt.TimeStamp);
            }
            var histV = new Dictionary<DateTime, Dictionary<long, Hist>>();

            // ---------------- historie hodnot bunek ze snapshotu ----------------
            var hist = new Dictionary<long, Hist>();
            var gridyOkno = new List<OccupancyGridMsg>();
            var znamych = new List<(DateTime t, int known)>();
            foreach (var m in gridy)
            {
                int n = m.Size, known = 0;
                for (int j = 0; j < n; j++)
                    for (int i = 0; i < n; i++)
                    {
                        int idx = i + j * n;
                        sbyte o = m.Occ[idx], r = m.Road != null ? m.Road[idx] : (sbyte)0;
                        if (m.State(i, j) != CellState.Unknown) known++;
                        long k = Klic(m.OriginX + i, m.OriginY + j);
                        if (!hist.TryGetValue(k, out var h))
                        {
                            hist[k] = new Hist { Occ = o, Road = r, OccZmena = m.TimeStamp, RoadZmena = m.TimeStamp };
                            continue;
                        }
                        if (h.Occ != o) { h.Occ = o; h.OccZmena = m.TimeStamp; h.OccOdZacatku = false; }
                        if (h.Road != r) { h.Road = r; h.RoadZmena = m.TimeStamp; h.RoadOdZacatku = false; }
                    }
                znamych.Add((m.TimeStamp, known));
                if (m.TimeStamp >= wFrom) gridyOkno.Add(m);
                if (cileAt.Contains(m.TimeStamp))
                    histV[m.TimeStamp] = hist.ToDictionary(kv => kv.Key, kv => new Hist
                    {
                        Occ = kv.Value.Occ, Road = kv.Value.Road, OccZmena = kv.Value.OccZmena, RoadZmena = kv.Value.RoadZmena,
                        OccOdZacatku = kv.Value.OccOdZacatku, RoadOdZacatku = kv.Value.RoadOdZacatku,
                    });
            }
            // Bunky, ktere z gridu vypadly a vratily se, maji zmenu z vynulovani - to je spravne
            // (posun okna je taky „prepsani"), jen se to v okoli stojiciho robotu nedeje.

            // ---------------- prehrani snimku (volitelne) ----------------
            Dictionary<DateTime, Dictionary<long, Vid>> vidV = null;
            var ov = new Overeni();
            var projekce = new Dictionary<string, Kamera>();
            if (v.Frames && snimky.Count > 0)
                vidV = PrehrajSnimky(rec, snimky, v, gridy, pozy, projekce, ov, cileAt.ToList(), T);

            // ---------------- rozbor snapshotu v okne ----------------
            var cfg = new LocalPlannerConfig
            {
                SafeDist = v.SafeDist,
                MaxSpeed = v.MaxSpeed,
                EscapeMaxLength = v.EscapeMax,
                EscapeBlockedCostFactor = v.EscapeCost,
            };
            LocalPathPlanner planovac = null;

            var rozbory = new List<Rozbor>();
            foreach (var m in gridyOkno)
            {
                var poza = PozaV(pozy, m.TimeStamp);
                var ctx = Priprav(m, v.SafeDist);
                planPodleCasu.TryGetValue(m.TimeStamp.Ticks, out var plan);
                planovac ??= new LocalPathPlanner(m.Size, cfg);
                var r = Rozeber(ctx, poza, v, plan, planovac, keepDraw: false);
                rozbory.Add(r);
            }

            // ---------------- 1. casova osa ----------------
            Console.WriteLine("=== 1. CASOVA OSA (snapshoty gridu, 2 Hz) ===");
            Console.WriteLine("  stav = zaznamenany plan se STEJNYM casem snimku jako snapshot (jinak nejblizsi drivejsi, ~),");
            Console.WriteLine("  replay = tyz grid prohnany dnesnim LocalPathPlanner (unik je od 29. 9. beze zmeny),");
            Console.WriteLine("  bunka = stav/kanal bunky pod robotem (G geometrie, S semantika), Locc/Lroad log-odds, odst = EDT [m],");
            Console.WriteLine("  vychod = nejkratsi cesta pravidly uniku k bunce neblokovane s odstupem >= SafeDist [m] a smer");
            Console.WriteLine("  (0 = vpred, + vlevo), bezG = totez bez zakazu geometrie, vzduch = vzdusnou carou k nejblizsi legalni,");
            Console.WriteLine($"  okoli r<={v.Radius:F1} m: pocty blokovanych G/S/GS, Free, Unknown; nG = nejblizsi geometricky blokovana [m]");
            Console.WriteLine("    t[s]  cas          stav            replay          x       y     th[st]  bunka  Locc  Lroad  odst | vychod  smer  | bezG   smer  | vzduch smer | G    S    GS   Free Unk  | nG    known");
            LocalPlanMsg poslPlan = null;
            int pi = 0;
            for (int k = 0; k < rozbory.Count; k++)
            {
                var r = rozbory[k];
                while (pi < plany.Count && plany[pi].TimeStamp <= r.T) poslPlan = plany[pi++];
                if (k % Math.Max(1, v.Every) != 0) continue;
                string stav = r.Plan != null ? ((LocalPlanStatus)r.Plan.Status).ToString()
                            : poslPlan != null ? "~" + ((LocalPlanStatus)poslPlan.Status) : "-";
                Console.WriteLine(string.Format(ci,
                    "  {0,7:F1} {1:HH:mm:ss.f}  {2,-15} {3,-15} {4,7:F2} {5,7:F2} {6,7:F1}  {7,-5} {8,5:F2} {9,5:F2} {10,5:F2} | {11,6} {12,5} | {13,6} {14,5} | {15,5} {16,5} | {17,4} {18,4} {19,4} {20,4} {21,4} | {22,5} {23,6}",
                    T(r.T), r.T, stav, r.Replay?.ToString() ?? "-", r.X, r.Y, r.Th * 180 / Math.PI,
                    StavKratce(r.Stav, r.Duvod), r.LOcc, r.LRoad, r.Odstup,
                    F(r.ExitLen), r.ExitLen > 0 ? F0(r.ExitDirBody) : "-",
                    F(r.ExitLenBezZakazu), r.ExitLenBezZakazu > 0 ? F0(r.ExitDirBezZakazu) : "-",
                    F(r.EuklExit), r.ExitLen > 0 || double.IsNaN(r.ExitLen) ? F0(r.EuklExitDir) : "-",
                    r.Geom, r.Sem, r.Obe, r.Free, r.Unk, F(r.NejblizsiGeom), r.Known));
            }
            Console.WriteLine();

            // Shoda replaye se zaznamem.
            int sRepl = 0, sShoda = 0;
            var neshody = new Dictionary<string, int>();
            foreach (var r in rozbory)
            {
                if (r.Plan == null || r.Replay == null) continue;
                sRepl++;
                var zaz = (LocalPlanStatus)r.Plan.Status;
                if (zaz == r.Replay.Value) sShoda++;
                else
                {
                    string kl = zaz + " -> " + r.Replay.Value;
                    neshody[kl] = neshody.TryGetValue(kl, out int c) ? c + 1 : 1;
                }
            }
            Console.WriteLine($"Replay planovace nad snapshotem: {sShoda} z {sRepl} se stavem shodnym se zaznamem"
                              + (neshody.Count > 0 ? "; neshody: " + string.Join(", ", neshody.Select(kv => $"{kv.Key} {kv.Value}x")) : ""));
            Console.WriteLine();

            // ---------------- 2. epizody stani ----------------
            Epizody(plany, rozbory, v, T);

            // ---------------- 3. okoli robotu v casech --at ----------------
            foreach (double at in v.At)
            {
                DateTime tAt = t0.AddSeconds(at);
                var m = gridyOkno.LastOrDefault(g => g.TimeStamp <= tAt) ?? gridyOkno.FirstOrDefault();
                if (m == null) continue;
                var poza = PozaV(pozy, m.TimeStamp);
                var ctx = Priprav(m, v.SafeDist);
                planPodleCasu.TryGetValue(m.TimeStamp.Ticks, out var plan);
                if (plan == null) plan = plany.LastOrDefault(p => p.TimeStamp <= m.TimeStamp);
                var r = Rozeber(ctx, poza, v, plan, planovac, keepDraw: true);
                var histAt = histV.TryGetValue(m.TimeStamp, out var hh) ? hh : hist;
                Dictionary<long, Vid> vid = null;
                vidV?.TryGetValue(m.TimeStamp, out vid);
                Okoli(ctx, r, histAt, vid, v, T);

                if (!string.IsNullOrWhiteSpace(v.Png))
                {
                    string zaklad = $"{v.Png}-{m.TimeStamp:HHmmss}";
                    var gnAt = gn.LastOrDefault(g => g.TimeStamp <= m.TimeStamp);
                    KresliPudorys(zaklad + "-grid.png", ctx, r, histAt, vid, poza, gnAt, projekce, v, T);
                    KresliKamery(zaklad + "-kamery.png", rec, snimky, m.TimeStamp, T);
                }
            }

            // ---------------- 4. skoky pozy a mazani gridu ----------------
            Console.WriteLine("=== 4. SKOKY POZY A MAZANI GRIDU ===");
            Console.WriteLine("  propady znamych bunek mezi snapshoty (pod polovinu = grid se mazal):");
            var propady = new List<DateTime>();
            for (int k = 1; k < znamych.Count; k++)
            {
                if (znamych[k].t < wFrom) continue;
                if (znamych[k].known < znamych[k - 1].known / 2)
                {
                    propady.Add(znamych[k].t);
                    Console.WriteLine(string.Format(ci, "    {0,7:F1} s {1:HH:mm:ss.fff}  {2} -> {3}",
                        T(znamych[k].t), znamych[k].t, znamych[k - 1].known, znamych[k].known));
                }
            }
            // Parovani mazani repliky s propady snapshotu - viz Sparuj. Pocita se i tehdy, kdyz
            // replika nesmazala nic (pak jsou vsechny propady „robot smazal, replika ne").
            var udalosti = v.Frames && v.FrameStep <= 1 ? Sparuj(ov.Mazani.Select(x => x.t).ToList(), propady) : null;
            Console.WriteLine("  RobotStateMsg: kroky pozy nad |v|*dt + 0,10 m nebo kurz nad |omega|*dt + 2 st. (prahy detektoru jsou 0,5 m / 5 st.):");
            for (int k = 1; k < pozy.Count; k++)
            {
                var a = pozy[k - 1]; var b = pozy[k];
                if (b.T < wFrom || b.T > wTo) continue;
                double dt = (b.T - a.T).TotalSeconds;
                if (dt <= 0) continue;
                double moved = Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
                double turned = Math.Abs(Conversions.NormalizeOrientation(b.Th - a.Th));
                double nadPos = moved - Math.Abs(b.V) * dt, nadRot = turned - Math.Abs(b.Om) * dt;
                if (nadPos > 0.10 || nadRot > 2 * Math.PI / 180)
                    Console.WriteLine(string.Format(ci, "    {0,7:F1} s {1:HH:mm:ss.fff}  posun {2:F2} m (nad rychlost {3:F2}), kurz {4:F1} st. (nad omega {5:F1}), dt {6:F2} s",
                        T(b.T), b.T, moved, nadPos, turned * 180 / Math.PI, nadRot * 180 / Math.PI, dt));
            }
            if (ov.Mazani.Count > 0)
            {
                Console.WriteLine("  prehrani detektoru skoku (verze z robotu; poloha a kurz ze snimku, v a omega z RobotStateMsg 10 Hz)");
                Console.WriteLine("  - kdy by se grid MAZAL, a jestli to potvrdil snapshot (propad znamych bunek do 1 s):");
                foreach (var s in ov.Mazani)
                {
                    var u = udalosti?.FirstOrDefault(x => x.Replika && x.T == s.t);
                    string znacka = u == null ? ""
                                  : u.Sparovano ? "   [snapshot POTVRDIL - robot mazal taky]"
                                                : "   [snapshot NEPOTVRDIL - robot NEMAZAL]";
                    Console.WriteLine("    " + s.popis + znacka);
                }
            }
            else if (v.Frames) Console.WriteLine("  prehrani detektoru skoku: zadny skok.");
            if (udalosti != null)
                foreach (var u in udalosti.Where(x => !x.Replika && !x.Sparovano))
                    Console.WriteLine(string.Format(ci, "    {0,7:F1} s {1:HH:mm:ss.fff} robot grid SMAZAL (propad snapshotu), replika NE", T(u.T), u.T));
            Console.WriteLine();

            // ---------------- 5. globalni navigace a mise ----------------
            GlobalniNavigace(gn, tracky, pozy, wFrom, wTo, T);

            // ---------------- 6. mereni koridoru a prikazy ----------------
            Koridor(diag, drive, wFrom, wTo, T);
            Pohyb(drive, pozy, imu, plany, wFrom, wTo, T);
            if (v.Detail != null && v.Detail.Length == 2)
                Detail(t0.AddSeconds(v.Detail[0]), t0.AddSeconds(v.Detail[1]), drive, pozy, imu, plany, diag, motory, T);

            // ---------------- 7. log ----------------
            Console.WriteLine("=== 7. LOG (Info) v okne ===");
            foreach (var inf in info.Where(x => x.TimeStamp >= wFrom.AddSeconds(-30) && x.TimeStamp <= wTo))
            {
                string txt = (inf.Message ?? string.Empty).Replace("\n", " ");
                if (txt.Length > 200) txt = txt.Substring(0, 200) + "...";
                Console.WriteLine(string.Format(ci, "  {0,7:F1} s {1:HH:mm:ss.fff}  {2}", T(inf.TimeStamp), inf.TimeStamp, txt));
            }
            Console.WriteLine();

            if (v.Frames)
            {
                Console.WriteLine("=== 8. OVERENI PREHRANI GRIDU PROTI SNAPSHOTUM ===");
                Console.WriteLine("  (grid prehrany ze snimku kodem robota - zapis, klin, detektor skoku - proti zaznamenanemu");
                Console.WriteLine("   snapshotu se stejnym casem; vysoka shoda = projekce i pozy jsou rekonstruovane spravne)");
                if (v.FrameStep > 1)
                    Console.WriteLine("  s --framestep > 1 se cely grid neprehrava - shoda neni k dispozici.");
                Console.WriteLine("  " + Fmt(ov.Stav));
                Console.WriteLine("  " + Fmt(ov.Occ));
                Console.WriteLine("  " + Fmt(ov.Road));
                Console.WriteLine("  " + Fmt(ov.Obe));
                Console.WriteLine("  POZOR: STAV a HODNOTY nejsou totez: planovac rozhoduje podle stavu (prahy log-odds), takze shoda stavu");
                Console.WriteLine("    je ta, ktera rika, jestli rozbor uniku nad prehranim plati. Hodnoty se lisi i tam, kde stav");
                Console.WriteLine("    sedi - prehrani bere pozu ZE SNIMKU (PoseAtCapture, dotaz na fuzi na vlakne kamery hned po");
                Console.WriteLine("    sestaveni snimku, casto extrapolace), robot v LocalNavigatoru ze stavu fuze v case snimku");
                Console.WriteLine("    (GetStateAt) az pri zpracovani, tedy po dalsich merenich; rozdil par cm posune zapis o bunku");
                Console.WriteLine("    a na okrajich prekazek a cesty se pak hodnota lisi.");
                Console.WriteLine("  POZOR: replika detektoru skoku (PoseJumpDetector ve verzi z robotu) bere v a omega z RobotStateMsg");
                Console.WriteLine("    (10 Hz, linearne interpolovane), robot ze stavu fuze v case snimku. Pri rychlem otaceni (obsluha");
                Console.WriteLine("    toci robotem rukou) proto replika muze grid SMAZAT JINDY nez robot - a od takoveho mista do");
                Console.WriteLine("    pristiho spolecneho mazani se prehrani se snapshoty rozchazi o desitky procent. To NENI chyba");
                Console.WriteLine("    projekce; useky 'rozjete mazanim' jsou v bloku 4 oznacene a tady se pocitaji zvlast.");

                if (udalosti != null)
                {
                    var mimo = new Overeni();
                    int vyrazeno = 0;
                    foreach (var x in ov.PoCase)
                    {
                        if (Rozjeto(udalosti, x.t)) { vyrazeno++; continue; }
                        mimo.Stav.Add(x.stav); mimo.Occ.Add(x.occ); mimo.Road.Add(x.road); mimo.Obe.Add(x.obe);
                    }
                    Console.WriteLine($"  bez useku rozjetych mazanim (vyrazeno {vyrazeno} z {ov.PoCase.Count} snapshotu):");
                    Console.WriteLine("    " + Fmt(mimo.Stav));
                    Console.WriteLine("    " + Fmt(mimo.Occ));
                    Console.WriteLine("    " + Fmt(mimo.Road));
                    Console.WriteLine("    " + Fmt(mimo.Obe));
                }
                Console.WriteLine("  po 10 s (p50 shody [%]: stav / hodnoty occ / road / obe; * = v binu je usek rozjety mazanim):");
                for (DateTime a = wFrom; a < wTo; a = a.AddSeconds(10))
                {
                    var bin = ov.PoCase.Where(x => x.t >= a && x.t < a.AddSeconds(10) && !double.IsNaN(x.stav)).ToList();
                    if (bin.Count == 0) continue;
                    bool rozj = udalosti != null && bin.Any(x => Rozjeto(udalosti, x.t));
                    Console.WriteLine(string.Format(ci, "    {0,7:F1} s {1:HH:mm:ss}  {2,6:F1} / {3,6:F1} / {4,6:F1} / {5,6:F1}  (n={6}){7}", T(a), a,
                        Median(bin.Select(x => x.stav).ToList()), Median(bin.Select(x => x.occ).ToList()),
                        Median(bin.Select(x => x.road).ToList()), Median(bin.Select(x => x.obe).ToList()), bin.Count,
                        rozj ? " *" : ""));
                }
                Console.WriteLine();
            }
        }

        /// <summary>Mazani gridu: replikou detektoru skoku, nebo robotem (propad snapshotu).</summary>
        private sealed class Udalost
        {
            public DateTime T;
            public bool Replika;
            public bool Sparovano;
        }

        /// <summary>
        /// Sparuje mazani gridu replikou s propady snapshotu. Robot smaze grid pri zpracovani snimku
        /// a snapshot, ktery to ukaze, prijde nejpozdeji o periodu snapshotu (0,5 s) pozdeji; snimky
        /// dvou kamer se navic zpracovavaji v poradi prichodu, ne porizeni. Proto okno
        /// <c>[t - 0,1 s; t + 1,0 s]</c> od mazani repliky.
        /// </summary>
        private static List<Udalost> Sparuj(List<DateTime> replika, List<DateTime> robot)
        {
            var u = new List<Udalost>();
            foreach (var t in replika)
                u.Add(new Udalost
                {
                    T = t, Replika = true,
                    Sparovano = robot.Any(d => d >= t.AddSeconds(-0.1) && d <= t.AddSeconds(1.0)),
                });
            foreach (var d in robot)
                u.Add(new Udalost
                {
                    T = d, Replika = false,
                    Sparovano = replika.Any(t => d >= t.AddSeconds(-0.1) && d <= t.AddSeconds(1.0)),
                });
            u.Sort((a, b) => a.T.CompareTo(b.T));
            return u;
        }

        /// <summary>
        /// Je prehrani v case <paramref name="t"/> rozjete mazanim? Ano, kdyz posledni NESPAROVANE
        /// mazani (replika bez robotu nebo naopak) je mladsi nez posledni SPOLECNE mazani (propad
        /// snapshotu, ktery replika potvrdila) - teprve po spolecnem mazani jsou oba gridy zase
        /// prazdne ve stejnou chvili.
        /// </summary>
        private static bool Rozjeto(List<Udalost> u, DateTime t)
        {
            DateTime nesp = DateTime.MinValue, spol = DateTime.MinValue;
            foreach (var x in u)
            {
                if (x.T > t) break;
                if (!x.Sparovano) nesp = x.T;
                else if (!x.Replika) spol = x.T;
            }
            return nesp > spol;
        }

        // ==================================================================
        // Rozbor snapshotu
        // ==================================================================

        private static Ctx Priprav(OccupancyGridMsg m, double safeDist)
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

            var c = new Ctx
            {
                Msg = m, Grid = g, Field = f, N = n, Res = m.Resolution, SafeDist = safeDist,
                Stav = new byte[n * n], Duvod = new byte[n * n], Odstup = new float[n * n],
            };
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int local = g.LocalIndex(i, j);
                    c.Stav[i + j * n] = (byte)g.StateAt(local);
                    c.Duvod[i + j * n] = (byte)g.BlockReasonAt(local);
                    c.Odstup[i + j * n] = f.DistanceLocal(i, j);
                }
            return c;
        }

        private static Rozbor Rozeber(Ctx c, Poza poza, Volby v, LocalPlanMsg plan, LocalPathPlanner planovac, bool keepDraw)
        {
            var m = c.Msg;
            int n = c.N;
            var r = new Rozbor { T = m.TimeStamp, X = poza.X, Y = poza.Y, Th = poza.Th, Plan = plan };

            int i0 = c.Grid.CellX(poza.X) - m.OriginX, j0 = c.Grid.CellY(poza.Y) - m.OriginY;
            if ((uint)i0 >= (uint)n || (uint)j0 >= (uint)n) return r;
            int s0 = i0 + j0 * n;
            r.Stav = (CellState)c.Stav[s0];
            r.Duvod = (CellBlockReason)c.Duvod[s0];
            r.LOcc = m.Occ[s0] * m.Scale;
            r.LRoad = (m.Road != null ? m.Road[s0] : 0) * m.Scale;
            r.Odstup = c.Odstup[s0];
            r.Unik = r.Stav == CellState.Blocked || r.Odstup < v.SafeDist - c.Res / 2;

            // Vychod pravidly uniku, bez horizontu (jen strop SearchMax).
            var dist = new double[n * n];
            var parent = new int[n * n];
            int exit = Dijkstra(c, s0, zakazGeom: true, v.SearchMax, stopAtExit: true, dist, parent, out double len);
            if (exit >= 0)
            {
                r.ExitIdx = exit;
                r.ExitLen = len;
                Smer(c, exit, poza, out r.ExitDirBody, out r.ExitDist);
                r.ExitPath = new List<int>();
                for (int k = exit; k >= 0; k = parent[k]) r.ExitPath.Add(k);
                r.ExitPath.Reverse();
                foreach (int k in r.ExitPath)
                {
                    if (k == s0) continue;
                    if (c.Stav[k] == (byte)CellState.Blocked) r.ExitPathSem++;
                    else if (c.Stav[k] == (byte)CellState.Unknown) r.ExitPathUnk++;
                }
            }
            var dist2 = new double[n * n];
            var parent2 = new int[n * n];
            int exit2 = Dijkstra(c, s0, zakazGeom: false, v.SearchMax, stopAtExit: true, dist2, parent2, out double len2);
            if (exit2 >= 0)
            {
                r.ExitLenBezZakazu = len2;
                Smer(c, exit2, poza, out r.ExitDirBezZakazu, out _);
            }

            // Oblast dosazitelna unikem do horizontu (pro pocty a kresleni).
            var distH = new double[n * n];
            Dijkstra(c, s0, zakazGeom: true, v.EscapeMax, stopAtExit: false, distH, new int[n * n], out _);
            int vh = 0;
            for (int k = 0; k < n * n; k++) if (distH[k] <= v.EscapeMax + c.Res * 1.5) vh++;
            r.VHorizontu = vh;
            if (keepDraw) r.DistHorizon = distH;

            // Vzdusnou carou + okoli.
            double best = double.PositiveInfinity, bestDir = double.NaN;
            double bestG = double.PositiveInfinity;
            int rad = (int)Math.Ceiling(v.SearchMax / c.Res);
            int radOk = (int)Math.Ceiling(v.Radius / c.Res);
            for (int dj = -rad; dj <= rad; dj++)
            {
                int j = j0 + dj;
                if ((uint)j >= (uint)n) continue;
                for (int di = -rad; di <= rad; di++)
                {
                    int i = i0 + di;
                    if ((uint)i >= (uint)n) continue;
                    int idx = i + j * n;
                    double wx = m.CenterX(i), wy = m.CenterY(j);
                    double d = Math.Sqrt((wx - poza.X) * (wx - poza.X) + (wy - poza.Y) * (wy - poza.Y));
                    bool exitOk = c.Stav[idx] != (byte)CellState.Blocked && c.Odstup[idx] >= v.SafeDist;
                    if (exitOk && d < best) { best = d; bestDir = SmerBody(wx, wy, poza); }
                    var duv = (CellBlockReason)c.Duvod[idx];
                    if ((duv & CellBlockReason.Geometry) != 0 && d < bestG) bestG = d;
                    if (d > v.Radius || Math.Abs(di) > radOk || Math.Abs(dj) > radOk) continue;
                    var st = (CellState)c.Stav[idx];
                    if (st == CellState.Blocked)
                    {
                        if (duv == (CellBlockReason.Geometry | CellBlockReason.Semantics)) r.Obe++;
                        else if (duv == CellBlockReason.Geometry) r.Geom++;
                        else r.Sem++;
                    }
                    else if (st == CellState.Free) r.Free++;
                    else
                    {
                        r.Unk++;
                        if (m.Occ[idx] == 0 && (m.Road == null || m.Road[idx] == 0)) r.UnkBezDat++;
                    }
                }
            }
            if (!double.IsInfinity(best)) { r.EuklExit = best; r.EuklExitDir = bestDir; }
            if (!double.IsInfinity(bestG)) r.NejblizsiGeom = bestG;

            int known = 0;
            for (int k = 0; k < n * n; k++) if (c.Stav[k] != (byte)CellState.Unknown) known++;
            r.Known = known;

            // Skutecny planovac nad tymz gridem (cil = pozadovany cil ze zaznamenaneho planu).
            if (plan != null && planovac != null)
            {
                try
                {
                    var res = planovac.Plan(c.Grid, c.Field, poza.X, poza.Y, poza.Th, plan.RequestedGoalX, plan.RequestedGoalY);
                    r.Replay = res.Status;
                }
                catch (Exception ex) { Console.Error.WriteLine("replay planu selhal: " + ex.Message); }
            }
            return r;
        }

        /// <summary>
        /// Dijkstra podle DELKY s pravidly uniku: start je vzdy prujezdny, pres geometricky
        /// blokovane bunky se nesmi (<paramref name="zakazGeom"/>), diagonala jen kdyz jsou oba
        /// ortogonalni sousede prujezdni (bez rezani rohu, jako planovac). Vychod = bunka
        /// neblokovana s odstupem &gt;= SafeDist (<c>IsEscapeExit</c>). <paramref name="maxLen"/>
        /// je strop delky (bunka za nim se uz nerozviji - tytez semantika jako horizont planovace).
        /// </summary>
        private static int Dijkstra(Ctx c, int start, bool zakazGeom, double maxLen, bool stopAtExit,
                                    double[] dist, int[] parent, out double len)
        {
            int n = c.N;
            Array.Fill(dist, double.PositiveInfinity);
            var closed = new bool[n * n];
            var pq = new System.Collections.Generic.PriorityQueue<int, double>();
            dist[start] = 0;
            parent[start] = -1;
            pq.Enqueue(start, 0);
            double diagLen = Math.Sqrt(2) * c.Res;
            len = double.NaN;

            bool Pruchozi(int idx) => idx == start || !zakazGeom || (c.Duvod[idx] & (byte)CellBlockReason.Geometry) == 0;

            while (pq.TryDequeue(out int cur, out double d))
            {
                if (closed[cur]) continue;
                closed[cur] = true;
                if (stopAtExit && c.Stav[cur] != (byte)CellState.Blocked && c.Odstup[cur] >= c.SafeDist)
                {
                    len = d;
                    return cur;
                }
                if (d >= maxLen) continue;
                int ci = cur % n, cj = cur / n;
                for (int k = 0; k < 8; k++)
                {
                    int ni = ci + NeighDx[k], nj = cj + NeighDy[k];
                    if ((uint)ni >= (uint)n || (uint)nj >= (uint)n) continue;
                    int nidx = ni + nj * n;
                    if (closed[nidx] || !Pruchozi(nidx)) continue;
                    bool diagonal = k >= 4;
                    if (diagonal && (!Pruchozi(ni + cj * n) || !Pruchozi(ci + nj * n))) continue;
                    double nd = d + (diagonal ? diagLen : c.Res);
                    if (nd < dist[nidx])
                    {
                        dist[nidx] = nd;
                        parent[nidx] = cur;
                        pq.Enqueue(nidx, nd);
                    }
                }
            }
            return -1;
        }

        private static void Smer(Ctx c, int idx, Poza poza, out double dirBody, out double dist)
        {
            int i = idx % c.N, j = idx / c.N;
            double wx = c.Msg.CenterX(i), wy = c.Msg.CenterY(j);
            dirBody = SmerBody(wx, wy, poza);
            dist = Math.Sqrt((wx - poza.X) * (wx - poza.X) + (wy - poza.Y) * (wy - poza.Y));
        }

        /// <summary>Smer bodu v ramci robotu [st.]: 0 = vpred, +90 = vlevo, +-180 = vzad.</summary>
        private static double SmerBody(double wx, double wy, Poza p)
            => Conversions.NormalizeOrientation(Math.Atan2(wy - p.Y, wx - p.X) - p.Th) * 180 / Math.PI;

        // ==================================================================
        // Epizody
        // ==================================================================

        private static void Epizody(List<LocalPlanMsg> plany, List<Rozbor> rozbory, Volby v, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine("=== 2. EPIZODY STANI (EscapingBlocked / RobotBlocked / LocalMinimum, zaznamenane plany, mezery do 1 s se preklenou) ===");
            var ep = new List<(DateTime od, DateTime d0, LocalPlanStatus st, int n)>();
            foreach (var p in plany)
            {
                var st = (LocalPlanStatus)p.Status;
                // LocalMinimum (od 9. 10. 2026) = slepy konec; starsi zaznamy ho maji pod AlreadyAtGoal.
                bool stani = st == LocalPlanStatus.RobotBlocked || st == LocalPlanStatus.EscapingBlocked
                          || st == LocalPlanStatus.LocalMinimum;
                if (!stani) continue;
                if (ep.Count > 0 && ep[^1].st == st && (p.TimeStamp - ep[^1].d0).TotalSeconds <= 1.0)
                    ep[^1] = (ep[^1].od, p.TimeStamp, st, ep[^1].n + 1);
                else ep.Add((p.TimeStamp, p.TimeStamp, st, 1));
            }
            foreach (var e in ep)
            {
                double dur = (e.d0 - e.od).TotalSeconds;
                if (dur < 1.0) continue;
                var rr = rozbory.Where(r => r.T >= e.od && r.T <= e.d0).ToList();
                Console.WriteLine(string.Format(ci, "  {0,7:F1}-{1,7:F1} s ({2:HH:mm:ss}-{3:HH:mm:ss}) {4,-15} {5,6:F1} s, {6} planu, {7} snapshotu",
                    T(e.od), T(e.d0), e.od, e.d0, e.st, dur, e.n, rr.Count));
                if (rr.Count == 0) continue;
                int blok = rr.Count(r => r.Stav == CellState.Blocked);
                int blokG = rr.Count(r => (r.Duvod & CellBlockReason.Geometry) != 0);
                int blokS = rr.Count(r => (r.Duvod & CellBlockReason.Semantics) != 0);
                int tesna = rr.Count(r => r.Stav != CellState.Blocked && r.Odstup < v.SafeDist - 0.025);
                int bezVychodu = rr.Count(r => double.IsNaN(r.ExitLen));
                int dal = rr.Count(r => !double.IsNaN(r.ExitLen) && r.ExitLen > v.EscapeMax);
                int vHoriz = rr.Count(r => !double.IsNaN(r.ExitLen) && r.ExitLen <= v.EscapeMax);
                int geomZatarasila = rr.Count(r => (double.IsNaN(r.ExitLen) || r.ExitLen > v.EscapeMax)
                                                 && !double.IsNaN(r.ExitLenBezZakazu) && r.ExitLenBezZakazu <= v.EscapeMax);
                var lens = new Stats("delka unikove cesty k nejblizsimu vychodu [m]");
                foreach (var r in rr) lens.Add(r.ExitLen);
                var vz = new Stats("vzdusnou carou k nejblizsi legalni bunce [m]");
                foreach (var r in rr) vz.Add(r.EuklExit);
                var lo = new Stats("Locc pod robotem");
                var lr = new Stats("Lroad pod robotem");
                var od = new Stats("odstup (EDT) pod robotem [m]");
                foreach (var r in rr) { lo.Add(r.LOcc); lr.Add(r.LRoad); od.Add(r.Odstup); }
                Console.WriteLine($"      bunka pod robotem: Blocked {blok}x (z toho geometrii {blokG}, semantikou {blokS}), "
                                  + $"neblokovana ale tesna (odstup < SafeDist - pul bunky) {tesna}x");
                Console.WriteLine("      " + Fmt(lo));
                Console.WriteLine("      " + Fmt(lr));
                Console.WriteLine("      " + Fmt(od));
                Console.WriteLine($"      vychod pravidly uniku: do EscapeMax {vHoriz}x, DAL nez EscapeMax {dal}x, zadny do {v.SearchMax:F0} m {bezVychodu}x; "
                                  + $"z neuspesnych by bez zakazu geometrie vychod do EscapeMax byl {geomZatarasila}x");
                Console.WriteLine("      " + Fmt(lens));
                Console.WriteLine("      " + Fmt(vz));
                var smery = rr.Where(r => !double.IsNaN(r.ExitDirBody)).Select(r => r.ExitDirBody).ToList();
                if (smery.Count > 0)
                    Console.WriteLine(string.Format(ci, "      smer k vychodu (ramec robotu, 0 = vpred, + vlevo): p50 {0:F0} st., rozsah {1:F0}..{2:F0}",
                        Median(smery), smery.Min(), smery.Max()));
                var repl = rr.Where(r => r.Replay != null).GroupBy(r => r.Replay.Value)
                             .Select(g => $"{g.Key} {g.Count()}x");
                Console.WriteLine("      replay planovace: " + string.Join(", ", repl));
            }
            Console.WriteLine();
        }

        // ==================================================================
        // Okoli robotu v case --at
        // ==================================================================

        private static void Okoli(Ctx c, Rozbor r, Dictionary<long, Hist> hist, Dictionary<long, Vid> vid,
                                  Volby v, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            var m = c.Msg;
            int n = c.N;
            Console.WriteLine(string.Format(ci, "=== 3. OKOLI ROBOTU v {0:F1} s ({1:HH:mm:ss.fff}) - snapshot gridu ===", T(r.T), r.T));
            Console.WriteLine(string.Format(ci, "  poza x {0:F2} y {1:F2} kurz {2:F1} st.; zaznamenany plan: {3}",
                r.X, r.Y, r.Th * 180 / Math.PI, r.Plan != null ? ((LocalPlanStatus)r.Plan.Status).ToString() : "-"));
            Console.WriteLine(string.Format(ci, "  bunka pod robotem: {0} ({1}), Locc {2:F2}, Lroad {3:F2}, odstup {4:F2} m (SafeDist {5:F2}) -> unik {6}",
                r.Stav, r.Duvod, r.LOcc, r.LRoad, r.Odstup, v.SafeDist, r.Unik ? "ANO" : "ne"));
            Console.WriteLine(string.Format(ci, "  vychod pravidly uniku: {0}; bez zakazu geometrie: {1}; vzdusnou carou: {2}",
                double.IsNaN(r.ExitLen) ? $"zadny do {v.SearchMax:F0} m" : string.Format(ci, "{0:F2} m (vzdusnou {1:F2} m) smerem {2:F0} st., na ceste {3} bunek S-blok. a {4} Unknown", r.ExitLen, r.ExitDist, r.ExitDirBody, r.ExitPathSem, r.ExitPathUnk),
                double.IsNaN(r.ExitLenBezZakazu) ? "zadny" : string.Format(ci, "{0:F2} m smerem {1:F0} st.", r.ExitLenBezZakazu, r.ExitDirBezZakazu),
                double.IsNaN(r.EuklExit) ? "zadna" : string.Format(ci, "{0:F2} m smerem {1:F0} st.", r.EuklExit, r.EuklExitDir)));
            Console.WriteLine($"  bunek dosazitelnych unikem do {v.EscapeMax:F1} m: {r.VHorizontu}");

            // Po pasmech vzdalenosti a sektorech: kolik blokovano kterym kanalem, jak stare hodnoty,
            // a jestli je kamera videla.
            Console.WriteLine("  pasma (vzdalenost od stredu robotu) x sektor (vpred |az| < 60, bok 60-120, vzad > 120 st.):");
            Console.WriteLine("    pasmo      sektor  bunek  G     S     GS    Free  Unk  | stari hodnoty blok. [s] p50/max | videno kamerou (blok. bunky): occ za 10 s  road za 10 s  nikdy  posl. occ p50 [s]");
            var pasma = new[] { (0.0, 0.30, "0-0,3 m"), (0.30, 0.70, "0,3-0,7 m"), (0.70, v.Radius, $"0,7-{v.Radius:F1} m") };
            var sektory = new[] { ("vpred", 0.0, 60.0), ("bok", 60.0, 120.0), ("vzad", 120.0, 180.01) };
            var poza = new Poza { X = r.X, Y = r.Y, Th = r.Th };
            int i0 = c.Grid.CellX(r.X) - m.OriginX, j0 = c.Grid.CellY(r.Y) - m.OriginY;
            int rad = (int)Math.Ceiling(v.Radius / c.Res) + 1;
            foreach (var (lo, hi, nazev) in pasma)
                foreach (var (sn, alo, ahi) in sektory)
                {
                    int cnt = 0, g = 0, s = 0, gs = 0, fr = 0, un = 0, occ10 = 0, road10 = 0, nikdy = 0;
                    var stari = new Stats("stari");
                    var poslOcc = new Stats("posl");
                    for (int dj = -rad; dj <= rad; dj++)
                        for (int di = -rad; di <= rad; di++)
                        {
                            int i = i0 + di, j = j0 + dj;
                            if ((uint)i >= (uint)n || (uint)j >= (uint)n) continue;
                            double wx = m.CenterX(i), wy = m.CenterY(j);
                            double d = Math.Sqrt((wx - r.X) * (wx - r.X) + (wy - r.Y) * (wy - r.Y));
                            if (d < lo || d >= hi) continue;
                            double az = Math.Abs(SmerBody(wx, wy, poza));
                            if (az < alo || az >= ahi) continue;
                            cnt++;
                            int idx = i + j * n;
                            var st = (CellState)c.Stav[idx];
                            var duv = (CellBlockReason)c.Duvod[idx];
                            if (st == CellState.Free) { fr++; continue; }
                            if (st != CellState.Blocked) { un++; continue; }
                            if (duv == (CellBlockReason.Geometry | CellBlockReason.Semantics)) gs++;
                            else if (duv == CellBlockReason.Geometry) g++;
                            else s++;

                            long k = Klic(m.OriginX + i, m.OriginY + j);
                            if (hist.TryGetValue(k, out var h))
                            {
                                // Stari hodnoty kanalu, ktery bunku blokuje (u obou ten mladsi).
                                DateTime zm = DateTime.MinValue;
                                if ((duv & CellBlockReason.Geometry) != 0) zm = h.OccZmena;
                                if ((duv & CellBlockReason.Semantics) != 0 && h.RoadZmena > zm) zm = h.RoadZmena;
                                stari.Add((r.T - zm).TotalSeconds);
                            }
                            if (vid != null)
                            {
                                vid.TryGetValue(k, out var w);
                                bool o10 = w != null && (r.T - w.OccPosl).TotalSeconds <= 10;
                                bool r10 = w != null && (r.T - w.RoadPosl).TotalSeconds <= 10;
                                if (o10) occ10++;
                                if (r10) road10++;
                                if (w == null || (w.OccN == 0 && w.RoadN == 0)) nikdy++;
                                if (w != null && w.OccN > 0) poslOcc.Add((r.T - w.OccPosl).TotalSeconds);
                            }
                        }
                    if (cnt == 0) continue;
                    int blok = g + s + gs;
                    Console.WriteLine(string.Format(ci, "    {0,-10} {1,-6} {2,6} {3,5} {4,5} {5,5} {6,5} {7,4} | {8,6} / {9,6}             | {10,22} {11,13} {12,6} {13,10}",
                        nazev, sn, cnt, g, s, gs, fr, un,
                        stari.Count > 0 ? stari.Median.ToString("F1", ci) : "-", stari.Count > 0 ? stari.Max.ToString("F1", ci) : "-",
                        vid == null ? "-" : $"{occ10}/{blok}", vid == null ? "-" : $"{road10}/{blok}", vid == null ? "-" : nikdy.ToString(ci),
                        poslOcc.Count > 0 ? poslOcc.Median.ToString("F1", ci) : "-"));
                }
            Console.WriteLine("  (stari hodnoty = jak dlouho se v snapshotech NEZMENILA hodnota blokujiciho kanalu; strop je zacatek historie --lookback)");

            // Bunky pod pudorysem robotu (polomer 0,3 m) jednotlive - tady se rozhoduje o uniku.
            Console.WriteLine("  bunky pod pudorysem (r <= 0,30 m), jen blokovane: [dx dy v ramci robotu] kanal Locc Lroad stari occ/road [s] posl. pozorovani occ/road [s] pocet occ/road (z toho prekazka/mimo cestu)");
            int vypsano = 0;
            for (int dj = -7; dj <= 7; dj++)
                for (int di = -7; di <= 7; di++)
                {
                    int i = i0 + di, j = j0 + dj;
                    if ((uint)i >= (uint)n || (uint)j >= (uint)n) continue;
                    double wx = m.CenterX(i), wy = m.CenterY(j);
                    double dx = wx - r.X, dy = wy - r.Y;
                    if (dx * dx + dy * dy > 0.30 * 0.30) continue;
                    int idx = i + j * n;
                    if (c.Stav[idx] != (byte)CellState.Blocked) continue;
                    if (vypsano >= 40) { vypsano++; continue; }
                    double fx = dx * Math.Cos(r.Th) + dy * Math.Sin(r.Th), fy = -dx * Math.Sin(r.Th) + dy * Math.Cos(r.Th);
                    long k = Klic(m.OriginX + i, m.OriginY + j);
                    hist.TryGetValue(k, out var h);
                    Vid w = null;
                    vid?.TryGetValue(k, out w);
                    Console.WriteLine(string.Format(ci, "    [{0,5:F2} {1,5:F2}] {2,-3} {3,5:F2} {4,5:F2}  {5,6} / {6,6}  {7,6} / {8,6}  {9}/{10} ({11}/{12})",
                        fx, fy, StavKratce(CellState.Blocked, (CellBlockReason)c.Duvod[idx]),
                        m.Occ[idx] * m.Scale, (m.Road != null ? m.Road[idx] : 0) * m.Scale,
                        h == null ? "-" : ((r.T - h.OccZmena).TotalSeconds.ToString("F1", ci) + (h.OccOdZacatku ? "+" : "")),
                        h == null ? "-" : ((r.T - h.RoadZmena).TotalSeconds.ToString("F1", ci) + (h.RoadOdZacatku ? "+" : "")),
                        w == null || w.OccN == 0 ? "nikdy" : (r.T - w.OccPosl).TotalSeconds.ToString("F1", ci),
                        w == null || w.RoadN == 0 ? "nikdy" : (r.T - w.RoadPosl).TotalSeconds.ToString("F1", ci),
                        w?.OccN ?? 0, w?.RoadN ?? 0, w?.OccObs ?? 0, w?.RoadNon ?? 0));
                    vypsano++;
                }
            if (vypsano > 40) Console.WriteLine($"    ... a dalsich {vypsano - 40}");
            if (vypsano == 0) Console.WriteLine("    (pod pudorysem neni zadna blokovana bunka)");
            Console.WriteLine("    (+ = hodnota beze zmeny od zacatku historie; nikdy = v prehranych snimcich ji kamera nepozorovala)");
            Console.WriteLine();
        }

        // ==================================================================
        // Prehrani snimku: sondovaci grid (co kamera videla) + cely grid (overeni)
        // ==================================================================

        private static Dictionary<DateTime, Dictionary<long, Vid>> PrehrajSnimky(RecordFile rec, List<IndexEntry> snimky, Volby v,
            List<OccupancyGridMsg> gridy, List<Poza> pozy,
            Dictionary<string, Kamera> projekce, Overeni ov,
            List<DateTime> cile, Func<DateTime, double> T)
        {
            var zmrazeno = new Dictionary<DateTime, Dictionary<long, Vid>>();
            int dalsiCil = 0;
            Dictionary<long, Vid> Kopie(Dictionary<long, Vid> d) => d.ToDictionary(kv => kv.Key, kv => new Vid
            {
                OccPosl = kv.Value.OccPosl, RoadPosl = kv.Value.RoadPosl, OccN = kv.Value.OccN, RoadN = kv.Value.RoadN,
                OccObs = kv.Value.OccObs, RoadNon = kv.Value.RoadNon,
            });
            var ci = CultureInfo.InvariantCulture;
            var vid = new Dictionary<long, Vid>();
            var m0 = gridy.LastOrDefault(g => g.TimeStamp <= snimky[0].CaptureTime) ?? gridy[0];
            var gcfg = new OccupancyGridConfig
            {
                Size = m0.Size, Resolution = m0.Resolution, Scale = m0.Scale,
                BlockedThreshold = m0.BlockedThreshold, FreeThreshold = m0.FreeThreshold,
            };

            // Cely grid: start ze snapshotu, pak snimek po snimku jako LocalNavigator.
            var replay = new OccupancyGrid(gcfg);
            replay.MoveOrigin(m0.OriginX, m0.OriginY);
            for (int j = 0; j < m0.Size; j++)
                for (int i = 0; i < m0.Size; i++)
                {
                    int dst = replay.LocalIndex(i, j);
                    replay.Occ[dst] = m0.Occ[i + j * m0.Size];
                    replay.Road[dst] = m0.Road != null ? m0.Road[i + j * m0.Size] : (sbyte)0;
                }
            var icfg = new OccupancyIntegratorConfig();
            var integ = new OccupancyIntegrator(replay, icfg);
            var wedge = new WedgeFiller(replay, icfg.WedgeFillDeg * Math.PI / 180.0 / 2.0,
                                        icfg.WedgeFillRangeM, icfg.WedgeFillMaxGapM, icfg.WedgeFillConfidence);
            var skok = new PoseJumpRobot();

            // Sonda: prazdny grid, do ktereho se zapise JEN tento snimek - co je nenulove, to kamera
            // v tomhle snimku opravdu pozorovala (po kvantizaci, tedy presne to, co by se zapsalo).
            var sonda = new OccupancyGrid(gcfg);
            var sondaInteg = new OccupancyIntegrator(sonda, icfg);

            var snapshotPodleCasu = new Dictionary<long, OccupancyGridMsg>();
            foreach (var g in gridy) snapshotPodleCasu[g.TimeStamp.Ticks] = g;

            bool cely = v.FrameStep <= 1;
            int zpracovano = 0, bezPozy = 0, bezProjekce = 0;
            DateTime start = DateTime.Now;
            for (int fi = 0; fi < snimky.Count; fi++)
            {
                if (!cely && fi % v.FrameStep != 0) continue;
                if (!(rec.Read(snimky[fi]) is CameraFrame f)) continue;
                if (f.TimeStamp < m0.TimeStamp) continue;
                while (dalsiCil < cile.Count && f.TimeStamp > cile[dalsiCil])
                    zmrazeno[cile[dalsiCil++]] = Kopie(vid);

                double x, y, th;
                var pp = PozaV(pozy, f.TimeStamp);
                if (f.HasPose) { x = f.PoseAtCaptureX; y = f.PoseAtCaptureY; th = f.PoseAtCaptureTheta; }
                else { x = pp.X; y = pp.Y; th = pp.Th; bezPozy++; }

                var kam = Projekce(f, projekce);
                var dp = kam?.D;
                var cp = kam?.C;
                if (dp == null && cp == null) { bezProjekce++; continue; }

                if (cely)
                {
                    if (skok.Check(x, y, th, pp.V, pp.Om, f.TimeStamp, out double moved, out double expl, out double turned, out double explT))
                    {
                        replay.Clear();
                        ov.Mazani.Add((f.TimeStamp, string.Format(ci, "{0,7:F1} s {1:HH:mm:ss.fff} {2,-22} posun {3:F2} m (vysvetli rychlost {4:F2}), kurz {5:F1} st. (vysvetli omega {6:F1}) -> grid.Clear()",
                            T(f.TimeStamp), f.TimeStamp, f.Name, moved, expl, turned * 180 / Math.PI, explT * 180 / Math.PI)));
                    }
                    integ.Integrate(f, dp, cp, x, y, th);
                    wedge.Fill(x, y, th);
                    if (snapshotPodleCasu.TryGetValue(f.TimeStamp.Ticks, out var snap))
                    {
                        Porovnej(replay, snap, ov);
                    }
                }

                // Sonda se prochazi CELA, ne jen okoli robotu: integrator zapisuje az do sveho dosahu
                // (RoadMaxRangeM / dosah polarniho gridu, nejvys pul gridu - po Recenter je to cely
                // grid kolem robotu). Do 7. 10. 2026 se cetlo jen --radius + 1 m od robotu, takze
                // bunky, ktere kamera videla z dalky (3-4 m pred robotem, nez k nim dojel), sonda
                // nevidela a pocty pozorovani i „kdy naposledy videno" u nich lhaly (nasel skeptik
                // nad Trackem 1. 10.). Cely grid stoji stejne jako Clear(), takze levnejsi
                // replika dosahu integratoru (je private a mohla by se rozejit) nema smysl.
                sonda.Clear();
                sondaInteg.Integrate(f, dp, cp, x, y, th);
                int ox = sonda.OriginX, oy = sonda.OriginY, sz = sonda.Size;
                for (int cy = oy; cy < oy + sz; cy++)
                    for (int cx = ox; cx < ox + sz; cx++)
                    {
                        int idx = sonda.Index(cx, cy);
                        sbyte o = sonda.Occ[idx], r = sonda.Road[idx];
                        if (o == 0 && r == 0) continue;
                        long k = Klic(cx, cy);
                        if (!vid.TryGetValue(k, out var w)) vid[k] = w = new Vid();
                        if (o != 0) { w.OccPosl = f.TimeStamp; w.OccN++; if (o > 0) w.OccObs++; }
                        if (r != 0) { w.RoadPosl = f.TimeStamp; w.RoadN++; if (r > 0) w.RoadNon++; }
                    }
                zpracovano++;
            }
            while (dalsiCil < cile.Count) zmrazeno[cile[dalsiCil++]] = Kopie(vid);
            Console.WriteLine(string.Format(ci, "Prehrano {0} snimku za {1:F0} s (bez pozy ve snimku {2}, bez projekce {3}){4}",
                zpracovano, (DateTime.Now - start).TotalSeconds, bezPozy, bezProjekce,
                cely ? "" : $", kazdy {v.FrameStep}. - cely grid se proto NEPREHRAVA"));
            Console.WriteLine();
            return zmrazeno;
        }

        /// <summary>
        /// Shoda prehraneho gridu se zaznamenanym snapshotem (jen znama oblast aspon jednoho z nich):
        /// stav bunky a zvlast hodnota occ, hodnota road a obe zaroven. Kanaly zvlast proto, aby bylo
        /// videt, KTERY zapis se rozchazi - geometrie z hloubky, nebo semantika z barvy (ta ma navic
        /// klin, ktery doplnuje interpolaci z okoli, takze je citlivejsi na poradi zapisu).
        /// </summary>
        private static void Porovnej(OccupancyGrid g, OccupancyGridMsg m, Overeni ov)
        {
            int n = m.Size, stejnyStav = 0, stejneOcc = 0, stejneRoad = 0, stejneObe = 0, celkem = 0;
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int cx = m.OriginX + i, cy = m.OriginY + j;
                    int idx = i + j * n;
                    var stZ = m.State(i, j);
                    var stR = g.State(cx, cy);
                    if (stZ == CellState.Unknown && stR == CellState.Unknown) continue;
                    celkem++;
                    if (stZ == stR) stejnyStav++;
                    if (g.Contains(cx, cy))
                    {
                        int gi = g.Index(cx, cy);
                        bool o = g.Occ[gi] == m.Occ[idx];
                        bool r = g.Road[gi] == (m.Road != null ? m.Road[idx] : 0);
                        if (o) stejneOcc++;
                        if (r) stejneRoad++;
                        if (o && r) stejneObe++;
                    }
                }
            if (celkem == 0) return;
            double Pct(int k) => 100.0 * k / celkem;
            ov.Stav.Add(Pct(stejnyStav));
            ov.Occ.Add(Pct(stejneOcc));
            ov.Road.Add(Pct(stejneRoad));
            ov.Obe.Add(Pct(stejneObe));
            ov.PoCase.Add((m.TimeStamp, Pct(stejnyStav), Pct(stejneOcc), Pct(stejneRoad), Pct(stejneObe)));
        }

        /// <summary>
        /// Projekce hloubky a barvy pro snimek - TAK, JAK JE STAVI DRIVER na robotu.
        /// Hloubka: <see cref="CameraProjectionInfo.CreateProjection"/> (intrinsika v popisu uz
        /// zohlednuji otoceni obrazu). Barva: <c>D435Camera.CreateProjector</c> stavi
        /// <c>CameraProjection(i1, i1.Inverse(), colorToDepth, depthToColor)</c> a u leve kamery
        /// (<c>Swap = true</c>, ARBotHW) prevrati hlavni bod v <b>obou</b> intrinsikach - kdyz je
        /// model bez zkresleni, vraci <c>Inverse()</c> tentyz objekt, takze se prevraceni zrusi.
        /// Replikuje se to doslova, ne „opravene"; spravnost overuje srovnani gridu se snapshoty.
        /// </summary>
        private sealed class Kamera
        {
            public ICameraProjection D, C;
            public int DW, DH, CW, CH;
        }

        private static Kamera Projekce(CameraFrame f, Dictionary<string, Kamera> cache)
        {
            string jmeno = f.Name ?? string.Empty;
            if (cache.TryGetValue(jmeno, out var p)) return p;
            var info = f.Projection;
            if (info == null || info.Intrinsics == null) return null;

            ICameraProjection d = info.CreateProjection();
            ICameraProjection c = null;
            if (info.ColorIntrinsics != null)
            {
                var src = info.ColorIntrinsics;
                var i1 = new Intrinsics
                {
                    Width = src.Width, Height = src.Height, PPx = src.PPx, PPy = src.PPy,
                    Fx = src.Fx, Fy = src.Fy, Model = src.Model,
                    Coeffs = src.Coeffs != null ? (float[])src.Coeffs.Clone() : new float[5],
                };
                var ii = i1.Inverse();
                if (jmeno.StartsWith("Left", StringComparison.OrdinalIgnoreCase))
                {
                    i1.PPx = i1.Width - i1.PPx;
                    i1.PPy = i1.Height - i1.PPy;
                    ii.PPx = ii.Width - ii.PPx;
                    ii.PPy = ii.Height - ii.PPy;
                }
                var cp = new CameraProjection(i1, ii, info.ColorToDepth, info.DepthToColor);
                cp.SetOrientation(info.Transformation);
                c = cp;
            }
            var k = new Kamera
            {
                D = d, C = c, DW = info.Intrinsics.Width, DH = info.Intrinsics.Height,
                CW = info.ColorIntrinsics?.Width ?? 0, CH = info.ColorIntrinsics?.Height ?? 0,
            };
            cache[jmeno] = k;
            return k;
        }

        /// <summary>
        /// <see cref="PoseJumpDetector"/> ve verzi, ktera bezela na robotu 1. 10. 2026 (f848fdf):
        /// cas pozadu (prehozene snimky dvou kamer) skok NEKONTROLUJE, jen prepise stav. Od
        /// a4397ab se kontroluje i ten - replika tu je, aby prehrani odpovidalo robotu.
        /// </summary>
        private sealed class PoseJumpRobot
        {
            private bool has;
            private double px, py, pth;
            private DateTime pt;
            public double ToleranceM = 0.5;
            public double ToleranceRad = 5.0 * Math.PI / 180.0;

            public bool Check(double x, double y, double th, double v, double om, DateTime t,
                              out double moved, out double explained, out double turned, out double explainedTurn)
            {
                moved = explained = turned = explainedTurn = 0;
                if (!has) { Rem(x, y, th, t); return false; }
                double dt = (t - pt).TotalSeconds;
                if (dt <= 0) { Rem(x, y, th, t); return false; }
                moved = Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py));
                explained = Math.Abs(v) * dt;
                turned = Math.Abs(Conversions.NormalizeOrientation(th - pth));
                explainedTurn = Math.Abs(om) * dt;
                Rem(x, y, th, t);
                return moved > explained + ToleranceM || turned > explainedTurn + ToleranceRad;
            }

            private void Rem(double x, double y, double th, DateTime t) { px = x; py = y; pth = th; pt = t; has = true; }
        }

        // ==================================================================
        // Globalni navigace, mise, koridor
        // ==================================================================

        private static void GlobalniNavigace(List<GlobalNavMsg> gn, List<TrackMsg> tracky, List<Poza> pozy,
                                             DateTime wFrom, DateTime wTo, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine("=== 5. GLOBALNI NAVIGACE (GlobalNavMsg) A MISE (TrackMsg) ===");
            Console.WriteLine("    t[s]  cas        stav       mrkev x/y          robot->mrkev  offRoute  trasa[m]  hran  phi[s]  uzavreni");
            GlobalNavMsg posl = null;
            DateTime poslTisk = DateTime.MinValue;
            foreach (var g in gn)
            {
                if (g.TimeStamp < wFrom.AddSeconds(-10) || g.TimeStamp > wTo) continue;
                bool zmena = posl == null || posl.Status != g.Status || posl.ClosureCount != g.ClosureCount
                             || Math.Abs(posl.RouteLengthM - g.RouteLengthM) > 20;
                if (zmena || (g.TimeStamp - poslTisk).TotalSeconds >= 10)
                {
                    var p = PozaV(pozy, g.TimeStamp);
                    double dm = g.HasCarrot ? Math.Sqrt((g.CarrotX - p.X) * (g.CarrotX - p.X) + (g.CarrotY - p.Y) * (g.CarrotY - p.Y)) : double.NaN;
                    Console.WriteLine(string.Format(ci, "  {0,7:F1} {1:HH:mm:ss.f} {2,-10} {3,8:F2} {4,8:F2}  {5,8:F2}     {6,7:F2}  {7,8:F1}  {8,4}  {9,6:F1}  {10}",
                        T(g.TimeStamp), g.TimeStamp, (GlobalNavStatus)g.Status, g.CarrotX, g.CarrotY, dm, g.OffRouteDist,
                        g.RouteLengthM, g.RouteEdgeCount, g.Phi, g.ClosureCount));
                    poslTisk = g.TimeStamp;
                }
                posl = g;
            }
            TrackMsg pt = null;
            foreach (var t in tracky)
            {
                if (t.TimeStamp > wTo) break;
                if (pt == null || pt.Phase != t.Phase || pt.PointIndex != t.PointIndex || pt.Lap != t.Lap || pt.Reached != t.Reached)
                    Console.WriteLine(string.Format(ci, "  {0,7:F1} {1:HH:mm:ss.f} Track: faze {2}, misto {3}/{4}, kolo {5}, objeto {6}, trasa {7:F0} m{8}",
                        T(t.TimeStamp), t.TimeStamp, (TrackPhase)t.Phase, t.PointIndex + 1, t.PointCount, t.Lap, t.Reached,
                        t.RouteLengthM, string.IsNullOrEmpty(t.AbortReason) ? "" : ", duvod: " + t.AbortReason));
                pt = t;
            }
            Console.WriteLine();
        }

        private static void Koridor(List<MeasurementDiagMsg> diag, List<DriveCommandMsg> drive,
                                    DateTime wFrom, DateTime wTo, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine("=== 6. MERENI KORIDORU (MeasurementDiagMsg) A RIDICI PRIKAZY po 10 s ===");
            Console.WriteLine("    t[s]   koridor mereni  prijato  krok-omezen  NIS p50   | prikaz v p50  |v|max  omega max[st/s]  estop%  held%");
            for (DateTime a = wFrom; a < wTo; a = a.AddSeconds(10))
            {
                DateTime b = a.AddSeconds(10);
                var dd = diag.Where(x => x.TimeStamp >= a && x.TimeStamp < b).ToList();
                var cc = drive.Where(x => x.TimeStamp >= a && x.TimeStamp < b).ToList();
                var nis = new Stats("nis");
                foreach (var x in dd) nis.Add(x.Nis);
                var vv = new Stats("v");
                foreach (var x in cc) vv.Add(x.Speed);
                Console.WriteLine(string.Format(ci, "  {0,7:F1}   {1,6}         {2,6}   {3,6}       {4,7}   | {5,8}  {6,6:F2}  {7,8:F1}       {8,5:F0}  {9,5:F0}",
                    T(a), dd.Count, dd.Count(x => x.Accepted), dd.Count(x => x.StepLimited),
                    nis.Count > 0 ? nis.Median.ToString("F2", ci) : "-",
                    vv.Count > 0 ? vv.Median.ToString("F2", ci) : "-",
                    cc.Count > 0 ? cc.Max(x => Math.Abs(x.Speed)) : 0,
                    cc.Count > 0 ? cc.Max(x => Math.Abs(x.RotationSpeed)) * 180 / Math.PI : 0,
                    cc.Count > 0 ? 100.0 * cc.Count(x => x.EmergencyStop) / cc.Count : 0,
                    cc.Count > 0 ? 100.0 * cc.Count(x => x.Held) / cc.Count : 0));
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Pohyb robotu po 2 s: co poslala ridici smycka (v, omega) a co namerila fuze / gyro.
        /// Rozlisi "stoji" od "toci se na miste za zastaralou drahou" (lp-zastaraly-regulator-
        /// toci-na-miste: na robotu 1. 10. jeste nebyla oprava z 5. 10.).
        /// </summary>
        private static void Pohyb(List<DriveCommandMsg> drive, List<Poza> pozy, List<(DateTime t, double yaw, double gz)> imu,
                                  List<LocalPlanMsg> plany, DateTime wFrom, DateTime wTo, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine("=== 6b. POHYB po 2 s: prikaz smycky vs. namereno (fuze, gyro VN100) ===");
            Console.WriteLine("    t[s]  cas      plan(posl.)      vCmd max  wCmd min/max [st/s] | vFuze max  wFuze min/max | gyro z min/max [st/s] | kurz fuze od..do [st]  IMU yaw od..do   | korekce [m] smer [st]");
            double D(double r) => r * 180 / Math.PI;
            for (DateTime a = wFrom; a < wTo; a = a.AddSeconds(2))
            {
                DateTime b = a.AddSeconds(2);
                var cc = drive.Where(x => x.TimeStamp >= a && x.TimeStamp < b).ToList();
                var pp = pozy.Where(x => x.T >= a && x.T < b).ToList();
                var gz = imu.Where(x => x.t >= a && x.t < b && !double.IsNaN(x.gz)).Select(x => x.gz).ToList();
                var yw = imu.Where(x => x.t >= a && x.t < b && !double.IsNaN(x.yaw)).Select(x => x.yaw).ToList();
                var pl = plany.LastOrDefault(x => x.TimeStamp < b);
                if (cc.Count == 0 && pp.Count == 0) continue;
                Console.WriteLine(string.Format(ci, "  {0,7:F1} {1:HH:mm:ss} {2,-16} {3,6:F2}  {4,6:F1}/{5,6:F1}       | {6,6:F2}  {7,6:F1}/{8,6:F1}   | {9,6:F1}/{10,6:F1}         | {11,7:F1}..{12,7:F1}   {13,7:F1}..{14,7:F1}",
                    T(a), a, pl != null ? ((LocalPlanStatus)pl.Status).ToString() : "-",
                    cc.Count > 0 ? cc.Max(x => Math.Abs(x.Speed)) : double.NaN,
                    cc.Count > 0 ? D(cc.Min(x => x.RotationSpeed)) : double.NaN, cc.Count > 0 ? D(cc.Max(x => x.RotationSpeed)) : double.NaN,
                    pp.Count > 0 ? pp.Max(x => Math.Abs(x.V)) : double.NaN,
                    pp.Count > 0 ? D(pp.Min(x => x.Om)) : double.NaN, pp.Count > 0 ? D(pp.Max(x => x.Om)) : double.NaN,
                    gz.Count > 0 ? D(gz.Min()) : double.NaN, gz.Count > 0 ? D(gz.Max()) : double.NaN,
                    pp.Count > 0 ? D(pp[0].Th) : double.NaN, pp.Count > 0 ? D(pp[^1].Th) : double.NaN,
                    yw.Count > 0 ? D(yw[0]) : double.NaN, yw.Count > 0 ? D(yw[^1]) : double.NaN)
                    + string.Format(ci, "   | {0,5:F2}  {1,5:F0}", Korekce(pozy, a, b, out double sm), sm));
            }
            Console.WriteLine("  (kurz fuze je matematicky, 0 = vychod; IMU yaw je tak, jak ho vraci IMUState.YPR() - sleduj zmenu, ne hodnotu)");
            Console.WriteLine("  (korekce = posun pozy, ktery NEVYSVETLI vlastni rychlost a kurz fuze: delta pozy minus integral v*(cos th, sin th) dt;");
            Console.WriteLine("   smer v ramci robotu na konci okna, 0 = vpred, +90 = vlevo. Grid je kotveny ve svete, takze o tolik se robot posune");
            Console.WriteLine("   vuci obsahu gridu, aniz by se fyzicky pohnul.)");
            Console.WriteLine();
        }

        /// <summary>
        /// Posun pozy v okne, ktery nevysvetli vlastni rychlost fuze: <c>delta pozy - suma v*(cos th, sin th)*dt</c>
        /// (lichobeznikove, mezi sousednimi RobotStateMsg). To je soucet korekci z mereni polohy
        /// (koridor, GPS) - o tolik se robot posune proti world-kotvenemu gridu.
        /// </summary>
        private static double Korekce(List<Poza> pozy, DateTime a, DateTime b, out double smerBody)
        {
            smerBody = double.NaN;
            var pp = pozy.Where(x => x.T >= a && x.T <= b).ToList();
            if (pp.Count < 2) return double.NaN;
            double drx = 0, dry = 0;
            for (int k = 1; k < pp.Count; k++)
            {
                double dt = (pp[k].T - pp[k - 1].T).TotalSeconds;
                double vv = 0.5 * (pp[k].V + pp[k - 1].V);
                double th = pp[k - 1].Th + 0.5 * Conversions.NormalizeOrientation(pp[k].Th - pp[k - 1].Th);
                drx += vv * dt * Math.Cos(th);
                dry += vv * dt * Math.Sin(th);
            }
            double cx = (pp[^1].X - pp[0].X) - drx, cy = (pp[^1].Y - pp[0].Y) - dry;
            double m = Math.Sqrt(cx * cx + cy * cy);
            if (m > 1e-6) smerBody = Conversions.NormalizeOrientation(Math.Atan2(cy, cx) - pp[^1].Th) * 180 / Math.PI;
            return m;
        }

        /// <summary>Radkovy detail kolem udalosti (napr. skoku kurzu): poza, IMU, prikaz, mereni koridoru.</summary>
        private static void Detail(DateTime a, DateTime b, List<DriveCommandMsg> drive, List<Poza> pozy,
                                   List<(DateTime t, double yaw, double gz)> imu, List<LocalPlanMsg> plany,
                                   List<MeasurementDiagMsg> diag, List<MotorStateBase> motory, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            Console.WriteLine(string.Format(ci, "=== 9. DETAIL {0:F1}-{1:F1} s ===", T(a), T(b)));
            var radky = new List<(DateTime t, string s)>();
            foreach (var p in pozy.Where(x => x.T >= a && x.T <= b))
                radky.Add((p.T, string.Format(ci, "poza    x {0,8:F3} y {1,8:F3} kurz {2,7:F2} st. v {3,5:F2} omega {4,6:F1} st/s",
                    p.X, p.Y, p.Th * 180 / Math.PI, p.V, p.Om * 180 / Math.PI)));
            DateTime posl = DateTime.MinValue;
            foreach (var i in imu.Where(x => x.t >= a && x.t <= b))
            {
                if ((i.t - posl).TotalSeconds < 0.05) continue;
                posl = i.t;
                radky.Add((i.t, string.Format(ci, "imu     yaw {0,7:F2} st. gyro z {1,6:F1} st/s", i.yaw * 180 / Math.PI, i.gz * 180 / Math.PI)));
            }
            foreach (var d in drive.Where(x => x.TimeStamp >= a && x.TimeStamp <= b))
                radky.Add((d.TimeStamp, string.Format(ci, "prikaz  v {0,5:F2} omega {1,6:F1} st/s{2}{3}", d.Speed, d.RotationSpeed * 180 / Math.PI,
                    d.EmergencyStop ? " ESTOP" : "", d.Held ? " HELD" : "")));
            foreach (var m in diag.Where(x => x.TimeStamp >= a && x.TimeStamp <= b))
                radky.Add((m.TimeStamp, string.Format(ci, "mereni  {0} z=[{1}] diagR=[{2}] NIS {3:F2} prijato {4} R x{5:F1}{6}",
                    m.Source, m.Z == null ? "" : string.Join(" ", m.Z.Select(z => z.ToString("F3", ci))),
                    m.DiagR == null ? "" : string.Join(" ", m.DiagR.Select(z => z.ToString("G3", ci))),
                    m.Nis, m.Accepted, m.RInflation, m.StepLimited ? " OMEZEN KROK" : "")));
            foreach (var p in plany.Where(x => x.TimeStamp >= a && x.TimeStamp <= b))
                radky.Add((p.TimeStamp, "plan    " + (LocalPlanStatus)p.Status));
            DateTime poslM = DateTime.MinValue;
            foreach (var mo in motory.Where(x => x.TimeStamp >= a && x.TimeStamp <= b))
            {
                if ((mo.TimeStamp - poslM).TotalSeconds < 0.05) continue;
                poslM = mo.TimeStamp;
                radky.Add((mo.TimeStamp, string.Format(ci, "motory  kolo L {0,6:F3} P {1,6:F3} m/s  proud L {2,5:F1} P {3,5:F1} A{4}",
                    mo.LeftWheelSpeed, mo.RightWheelSpeed, mo.LeftMotorCurrent, mo.RightMotorCurrent,
                    mo.HasMeasurement ? "" : " (bez mereni)")));
            }
            foreach (var r in radky.OrderBy(x => x.t))
                Console.WriteLine(string.Format(ci, "  {0,8:F2} {1:HH:mm:ss.fff}  {2}", T(r.t), r.t, r.s));
            Console.WriteLine();
        }

        // ==================================================================
        // Kresleni
        // ==================================================================

        // Vyrez pudorysu v ramci robotu: vpred nahoru, vlevo vlevo.
        private const double VpredMax = 5.0, VzadMax = 3.0, BokMax = 4.0;
        private const double PxNaM = 60;

        private static void KresliPudorys(string cesta, Ctx c, Rozbor r, Dictionary<long, Hist> hist,
            Dictionary<long, Vid> vid, Poza poza, GlobalNavMsg gnAt,
            Dictionary<string, Kamera> projekce, Volby v, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            var m = c.Msg;
            int n = c.N;
            int w = (int)(2 * BokMax * PxNaM), h = (int)((VpredMax + VzadMax) * PxNaM);
            const int hlavicka = 64, mezera = 8, legenda = 150;
            int panelu = 3;
            int sirka = panelu * w + (panelu - 1) * mezera, vyska = hlavicka + h + legenda;

            using var bmp = new SKBitmap(new SKImageInfo(sirka, vyska, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(new SKColor(16, 18, 22));
            using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 13);
            using var fontMaly = new SKFont(SKTypeface.Default, 11);

            double cosT = Math.Cos(poza.Th), sinT = Math.Sin(poza.Th);
            // pixel panelu -> svet
            (double wx, double wy) Svet(int px, int py)
            {
                double xb = VpredMax - (py + 0.5) / PxNaM;
                double yb = BokMax - (px + 0.5) / PxNaM;
                return (poza.X + xb * cosT - yb * sinT, poza.Y + xb * sinT + yb * cosT);
            }
            // svet -> pixel panelu
            SKPoint Px(double wx, double wy)
            {
                double dx = wx - poza.X, dy = wy - poza.Y;
                double xb = dx * cosT + dy * sinT, yb = -dx * sinT + dy * cosT;
                return new SKPoint((float)((BokMax - yb) * PxNaM), (float)((VpredMax - xb) * PxNaM));
            }
            SKPoint Body(double xb, double yb) => new SKPoint((float)((BokMax - yb) * PxNaM), (float)((VpredMax - xb) * PxNaM));

            int Bunka(double wx, double wy)
            {
                int i = (int)Math.Floor(wx / m.Resolution) - m.OriginX, j = (int)Math.Floor(wy / m.Resolution) - m.OriginY;
                if ((uint)i >= (uint)n || (uint)j >= (uint)n) return -1;
                return i + j * n;
            }

            // --- panel 1: kanaly ---
            SKColor Kanal(int idx)
            {
                var st = (CellState)c.Stav[idx];
                var duv = (CellBlockReason)c.Duvod[idx];
                if (st == CellState.Blocked)
                {
                    if (duv == (CellBlockReason.Geometry | CellBlockReason.Semantics)) return new SKColor(200, 60, 210);
                    if (duv == CellBlockReason.Geometry) return new SKColor(230, 40, 40);
                    return new SKColor(235, 150, 30);
                }
                if (st == CellState.Free) return new SKColor(40, 150, 70);
                bool bezDat = m.Occ[idx] == 0 && (m.Road == null || m.Road[idx] == 0);
                return bezDat ? new SKColor(34, 36, 40) : new SKColor(95, 98, 105);
            }

            // --- panel 2: kdy kamera bunku naposledy pozorovala (libovolny kanal) ---
            SKColor Videno(int idx, int cx, int cy)
            {
                var zakl = Kanal(idx);
                if (vid == null) return Ztmav(zakl, 0.3);
                vid.TryGetValue(Klic(cx, cy), out var vv);
                DateTime posl = vv == null ? DateTime.MinValue : (vv.OccPosl > vv.RoadPosl ? vv.OccPosl : vv.RoadPosl);
                if (posl == DateTime.MinValue || posl > r.T) return new SKColor(60, 0, 0);
                double s = (r.T - posl).TotalSeconds;
                return StupniceStari(s);
            }

            // --- panel 3: jak dlouho se hodnota bunky nezmenila (blokujici kanal; u ostatnich max) ---
            SKColor Stari(int idx, int cx, int cy)
            {
                var st = (CellState)c.Stav[idx];
                if (!hist.TryGetValue(Klic(cx, cy), out var hh)) return new SKColor(34, 36, 40);
                if (st == CellState.Unknown && m.Occ[idx] == 0 && (m.Road == null || m.Road[idx] == 0)) return new SKColor(34, 36, 40);
                DateTime zm;
                var duv = (CellBlockReason)c.Duvod[idx];
                if (st == CellState.Blocked)
                {
                    zm = DateTime.MinValue;
                    if ((duv & CellBlockReason.Geometry) != 0) zm = hh.OccZmena;
                    if ((duv & CellBlockReason.Semantics) != 0 && hh.RoadZmena > zm) zm = hh.RoadZmena;
                }
                else zm = hh.OccZmena > hh.RoadZmena ? hh.OccZmena : hh.RoadZmena;
                var col = StupniceStari((r.T - zm).TotalSeconds);
                return st == CellState.Blocked ? col : Ztmav(col, 0.35);
            }

            for (int panel = 0; panel < panelu; panel++)
            {
                float ox = panel * (w + mezera), oy = hlavicka;
                using var pb = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
                for (int py = 0; py < h; py++)
                    for (int px = 0; px < w; px++)
                    {
                        var (wx, wy) = Svet(px, py);
                        int idx = Bunka(wx, wy);
                        SKColor col;
                        if (idx < 0) col = new SKColor(10, 10, 10);
                        else
                        {
                            int cx = (int)Math.Floor(wx / m.Resolution), cy = (int)Math.Floor(wy / m.Resolution);
                            col = panel == 0 ? Kanal(idx) : panel == 1 ? Videno(idx, cx, cy) : Stari(idx, cx, cy);
                        }
                        pb.SetPixel(px, py, col);
                    }
                using (var img = SKImage.FromBitmap(pb)) canvas.DrawImage(img, ox, oy);

                canvas.Save();
                canvas.Translate(ox, oy);
                canvas.ClipRect(new SKRect(0, 0, w, h));

                // Mrizka 1 m.
                using (var mriz = new SKPaint { Color = new SKColor(255, 255, 255, 30), StrokeWidth = 1, IsAntialias = false })
                {
                    for (double xb = -Math.Floor(VzadMax); xb <= VpredMax; xb += 1) canvas.DrawLine(Body(xb, BokMax), Body(xb, -BokMax), mriz);
                    for (double yb = -Math.Floor(BokMax); yb <= BokMax; yb += 1) canvas.DrawLine(Body(-VzadMax, yb), Body(VpredMax, yb), mriz);
                }

                // Oblast dosazitelna unikem do horizontu (jen panel 1) - modre tecky.
                if (panel == 0 && r.DistHorizon != null)
                {
                    using var mod = new SKPaint { Color = new SKColor(80, 200, 255, 150), IsAntialias = false };
                    int i0 = c.Grid.CellX(poza.X) - m.OriginX, j0 = c.Grid.CellY(poza.Y) - m.OriginY;
                    int rad = (int)Math.Ceiling((v.EscapeMax + 0.2) / c.Res);
                    for (int dj = -rad; dj <= rad; dj++)
                        for (int di = -rad; di <= rad; di++)
                        {
                            int i = i0 + di, j = j0 + dj;
                            if ((uint)i >= (uint)n || (uint)j >= (uint)n) continue;
                            if (!(r.DistHorizon[i + j * n] <= v.EscapeMax + c.Res * 1.5)) continue;
                            var p = Px(m.CenterX(i), m.CenterY(j));
                            canvas.DrawRect(p.X - 1, p.Y - 1, 2, 2, mod);
                        }
                }

                // Kruh horizontu uniku a SafeDist.
                using (var kruh = new SKPaint { Color = new SKColor(80, 200, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true,
                                                PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0) })
                    canvas.DrawCircle(Body(0, 0), (float)(v.EscapeMax * PxNaM), kruh);

                // Zorna pole se schvalne NEKRESLI: okraj obrazu promitnuty pres TransformBack (i pres
                // TargetPoly) neodpovidal oblasti, kterou integrator skutecne zapisoval (krizil se ~1,5 m
                // pred robotem, kdezto zapisy zacinaji 0,2-0,5 m pred nim). Kam kamera opravdu videla,
                // ukazuje panel 2 z prehrani snimku kodem robota - to je mereni, ne model.

                // Zaznamenany plan (zluta) a cesta k nejblizsimu vychodu (azurova).
                if (r.Plan?.WayPoints != null && r.Plan.WayPoints.Length >= 2)
                {
                    using var pl = new SKPaint { Color = new SKColor(255, 230, 0), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true };
                    using var path = new SKPath();
                    path.MoveTo(Px(r.Plan.WayPoints[0].X, r.Plan.WayPoints[0].Y));
                    for (int k = 1; k < r.Plan.WayPoints.Length; k++) path.LineTo(Px(r.Plan.WayPoints[k].X, r.Plan.WayPoints[k].Y));
                    canvas.DrawPath(path, pl);
                }
                if (r.ExitPath != null && r.ExitPath.Count >= 1)
                {
                    using var ex = new SKPaint { Color = new SKColor(0, 255, 255), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
                    using var path = new SKPath();
                    for (int k = 0; k < r.ExitPath.Count; k++)
                    {
                        int idx = r.ExitPath[k];
                        var p = Px(m.CenterX(idx % n), m.CenterY(idx / n));
                        if (k == 0) path.MoveTo(p); else path.LineTo(p);
                    }
                    canvas.DrawPath(path, ex);
                    int e = r.ExitPath[^1];
                    using var exf = new SKPaint { Color = new SKColor(0, 255, 255), IsAntialias = true };
                    canvas.DrawCircle(Px(m.CenterX(e % n), m.CenterY(e / n)), 5, exf);
                }

                // Smer k mrkvi (cil lokalniho planu) a k mrkvi globalni navigace.
                if (r.Plan != null)
                    Sipka(canvas, Body(0, 0), Px(r.Plan.RequestedGoalX, r.Plan.RequestedGoalY), new SKColor(255, 230, 0), 2.6 * PxNaM);

                // Robot: pudorys 0,3 m a kurz.
                using (var rob = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true })
                {
                    canvas.DrawCircle(Body(0, 0), (float)(0.3 * PxNaM), rob);
                    canvas.DrawLine(Body(0, 0), Body(0.45, 0), rob);
                }
                // Sever: svetovy vektor (0, 1) je v ramci robotu (sin th, cos th).
                var sever = Body(Math.Sin(poza.Th), Math.Cos(poza.Th));
                var stred = Body(0, 0);
                var sv = new SKPoint(w - 34 + (sever.X - stred.X) * 0.4f, 40 + (sever.Y - stred.Y) * 0.4f);
                using (var sp = new SKPaint { Color = SKColors.White, StrokeWidth = 2, IsAntialias = true })
                {
                    canvas.DrawLine(new SKPoint(w - 34, 40), sv, sp);
                    canvas.DrawText("S", sv.X + 3, sv.Y, SKTextAlign.Left, fontMaly, sp);
                }
                canvas.Restore();

                string nadpis = panel == 0 ? "kanaly: G cervena, S oranzova, GS fialova, Free zelena, Unknown seda (tmava = bez dat)"
                              : panel == 1 ? "kdy kamera bunku naposledy POZOROVALA (prehrani snimku)"
                              : "jak dlouho se hodnota bunky NEZMENILA (snapshoty)";
                canvas.DrawText(nadpis, ox + 4, hlavicka - 8, SKTextAlign.Left, fontMaly, text);
            }

            // Hlavicka.
            string stav = r.Plan != null ? ((LocalPlanStatus)r.Plan.Status).ToString() : "-";
            canvas.DrawText(string.Format(ci, "{0:HH:mm:ss.f}  t = {1:F1} s   plan: {2}   replay: {3}   pudorys v ramci robotu (vpred nahoru), mrizka 1 m, kruh = EscapeMax {4:F1} m",
                r.T, T(r.T), stav, r.Replay?.ToString() ?? "-", v.EscapeMax), 6, 18, SKTextAlign.Left, font, text);
            canvas.DrawText(string.Format(ci, "pod robotem: {0} ({1}) Locc {2:F2} Lroad {3:F2} odstup {4:F2} m | vychod unikem: {5} | bez zakazu geometrie: {6} | vzdusnou: {7}",
                r.Stav, r.Duvod, r.LOcc, r.LRoad, r.Odstup,
                double.IsNaN(r.ExitLen) ? "zadny" : string.Format(ci, "{0:F2} m @ {1:F0} st.", r.ExitLen, r.ExitDirBody),
                double.IsNaN(r.ExitLenBezZakazu) ? "zadny" : string.Format(ci, "{0:F2} m @ {1:F0} st.", r.ExitLenBezZakazu, r.ExitDirBezZakazu),
                double.IsNaN(r.EuklExit) ? "-" : string.Format(ci, "{0:F2} m", r.EuklExit)), 6, 38, SKTextAlign.Left, font, text);

            // Legenda stupnice stari.
            float ly = hlavicka + h + 18;
            canvas.DrawText("stupnice stari (panely 2 a 3): ", 6, ly, SKTextAlign.Left, fontMaly, text);
            double[] ukazky = { 0.2, 1, 3, 10, 30, 60, 120, 300 };
            for (int k = 0; k < ukazky.Length; k++)
            {
                using var bp = new SKPaint { Color = StupniceStari(ukazky[k]) };
                float x0 = 200 + k * 80;
                canvas.DrawRect(x0, ly - 11, 18, 12, bp);
                canvas.DrawText(ukazky[k].ToString("0.#", ci) + " s", x0 + 22, ly, SKTextAlign.Left, fontMaly, text);
            }
            using (var bp = new SKPaint { Color = new SKColor(60, 0, 0) })
            {
                canvas.DrawRect(200 + 8 * 80, ly - 11, 18, 12, bp);
                canvas.DrawText("nepozorovano", 200 + 8 * 80 + 22, ly, SKTextAlign.Left, fontMaly, text);
            }
            canvas.DrawText("modre tecky = bunky dosazitelne unikem do EscapeMax (pres S ano, pres G ne), azurova cara + bod = nejkratsi cesta k nejblizsi legalni bunce (bez horizontu)",
                6, ly + 22, SKTextAlign.Left, fontMaly, text);
            canvas.DrawText("zluta cara = zaznamenany plan, zluta sipka = smer k cili lokalniho planu (mrkvi), bily kruh = pudorys 0,3 m",
                6, ly + 40, SKTextAlign.Left, fontMaly, text);
            if (gnAt != null)
                canvas.DrawText(string.Format(ci, "globalni navigace: {0}, offRoute {1:F2} m, trasa {2:F0} m, phi {3:F0} s", (GlobalNavStatus)gnAt.Status, gnAt.OffRouteDist, gnAt.RouteLengthM, gnAt.Phi),
                    6, ly + 58, SKTextAlign.Left, fontMaly, text);

            Uloz(bmp, cesta);
        }

        /// <summary>Stupnice stari: cerstve zlute, starsi do cervena az tmave.</summary>
        private static SKColor StupniceStari(double s)
        {
            if (s <= 1) return new SKColor(255, 255, 120);
            if (s <= 3) return new SKColor(255, 220, 40);
            if (s <= 10) return new SKColor(255, 160, 0);
            if (s <= 30) return new SKColor(240, 90, 0);
            if (s <= 60) return new SKColor(200, 40, 40);
            if (s <= 120) return new SKColor(150, 20, 90);
            return new SKColor(90, 20, 140);
        }

        private static SKColor Ztmav(SKColor c, double k)
            => new SKColor((byte)(c.Red * k), (byte)(c.Green * k), (byte)(c.Blue * k));

        private static void Sipka(SKCanvas canvas, SKPoint od, SKPoint k, SKColor barva, double maxDelka)
        {
            if (barva.Alpha == 0) return;
            float dx = k.X - od.X, dy = k.Y - od.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            float s = (float)(Math.Min(len, maxDelka) / len);
            var cil = new SKPoint(od.X + dx * s, od.Y + dy * s);
            using var p = new SKPaint { Color = barva, StrokeWidth = 2, IsAntialias = true, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(od, cil, p);
            double a = Math.Atan2(dy, dx);
            for (int z = -1; z <= 1; z += 2)
            {
                double b = a + Math.PI + z * 0.45;
                canvas.DrawLine(cil, new SKPoint(cil.X + (float)(12 * Math.Cos(b)), cil.Y + (float)(12 * Math.Sin(b))), p);
            }
        }

        /// <summary>
        /// Snimky obou kamer nejblize casu <paramref name="t"/>: barva, pravdepodobnost site
        /// (svetle = sjizdne) a hloubka (barevna stupnice 0,2-6 m, cerna = bez mereni).
        /// </summary>
        private static void KresliKamery(string cesta, RecordFile rec, List<IndexEntry> snimky, DateTime t, Func<DateTime, double> T)
        {
            var ci = CultureInfo.InvariantCulture;
            var vybrane = new List<CameraFrame>();
            foreach (var g in snimky.GroupBy(e => e.Name ?? string.Empty).OrderBy(g => g.Key))
            {
                var e = g.OrderBy(x => Math.Abs((x.CaptureTime - t).Ticks)).FirstOrDefault();
                if (e.MsgName == null) continue;
                if (rec.Read(e) is CameraFrame f) vybrane.Add(f);
            }
            if (vybrane.Count == 0) return;

            const int pw = 320, ph = 240, hl = 22, mez = 6;
            int sirka = vybrane.Count * pw + (vybrane.Count - 1) * mez, vyska = 3 * (ph + hl) + 30;
            using var bmp = new SKBitmap(new SKImageInfo(sirka, vyska, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(new SKColor(16, 18, 22));
            using var text = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 12);
            canvas.DrawText(string.Format(ci, "kamery nejblize {0:HH:mm:ss.f} (t = {1:F1} s); radky: barva | pravdepodobnost site (svetle = sjizdne) | hloubka 0,2-6 m (cerna = nic)", t, T(t)),
                4, 16, SKTextAlign.Left, font, text);

            for (int k = 0; k < vybrane.Count; k++)
            {
                var f = vybrane[k];
                float x = k * (pw + mez);
                float y = 30;
                canvas.DrawText(string.Format(ci, "{0}  {1:HH:mm:ss.fff}", f.Name, f.TimeStamp), x + 2, y + 14, SKTextAlign.Left, font, text);
                if (f.ImageRGB != null) Vloz(canvas, f.ImageRGB, x, y + hl, pw, ph, null);
                y += ph + hl;
                canvas.DrawText("pravdepodobnost", x + 2, y + 14, SKTextAlign.Left, font, text);
                if (f.ImageProbability != null) Vloz(canvas, f.ImageProbability, x, y + hl, pw, ph, null);
                y += ph + hl;
                canvas.DrawText("hloubka", x + 2, y + 14, SKTextAlign.Left, font, text);
                if (f.ImageDepth != null) Vloz(canvas, f.ImageDepth, x, y + hl, pw, ph, HloubkaBarva);
            }
            Uloz(bmp, cesta);
        }

        private static SKColor HloubkaBarva(int mm)
        {
            if (mm <= 0) return SKColors.Black;
            double m = mm / 1000.0;
            double u = Math.Max(0, Math.Min(1, (m - 0.2) / (6.0 - 0.2)));
            // modra (blizko) -> zelena -> zluta -> cervena (daleko)
            double r = Math.Min(1, Math.Max(0, 2 * u - 0.5)), g = u < 0.5 ? 2 * u : 2 - 2 * u * 1.0, b = Math.Max(0, 1 - 2 * u);
            g = Math.Max(0, Math.Min(1, g + 0.2));
            return new SKColor((byte)(255 * r), (byte)(255 * g), (byte)(255 * b));
        }

        /// <summary>Vlozi obraz do platna (nejblizsi soused). Gray16 pres <paramref name="mapa16"/>.</summary>
        private static void Vloz(SKCanvas canvas, Image zdroj, float x, float y, int w, int h, Func<int, SKColor> mapa16)
        {
            using var bmp = new SKBitmap(new SKImageInfo(zdroj.Width, zdroj.Height, SKColorType.Bgra8888, SKAlphaType.Opaque));
            var src = zdroj.Data;
            int step = zdroj.Step;
            for (int y0 = 0; y0 < zdroj.Height; y0++)
                for (int x0 = 0; x0 < zdroj.Width; x0++)
                {
                    int si = (x0 + y0 * zdroj.Width) * step;
                    SKColor col;
                    if (step == 2) col = (mapa16 ?? HloubkaBarva)(src[si] | (src[si + 1] << 8));
                    else if (step >= 3) col = new SKColor(src[si + 2], src[si + 1], src[si]);
                    else col = new SKColor(src[si], src[si], src[si]);
                    bmp.SetPixel(x0, y0, col);
                }
            using var image = SKImage.FromBitmap(bmp);
            canvas.DrawImage(image, new SKRect(x, y, x + w, y + h), new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }

        private static void Uloz(SKBitmap bmp, string cesta)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(cesta));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 90);
            using var fs = File.Create(cesta);
            data.SaveTo(fs);
            Console.WriteLine($"ulozeno {cesta}");
        }

        // ==================================================================
        // Pomocne
        // ==================================================================

        private static Poza PozaV(List<Poza> p, DateTime t)
        {
            if (p.Count == 0) return default;
            int lo = 0, hi = p.Count - 1;
            if (t <= p[0].T) return p[0];
            if (t >= p[hi].T) return p[hi];
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (p[mid].T <= t) lo = mid; else hi = mid;
            }
            var a = p[lo]; var b = p[hi];
            double span = (b.T - a.T).TotalSeconds;
            double u = span > 0 ? (t - a.T).TotalSeconds / span : 0;
            return new Poza
            {
                T = t,
                X = a.X + (b.X - a.X) * u,
                Y = a.Y + (b.Y - a.Y) * u,
                Th = a.Th + Conversions.NormalizeOrientation(b.Th - a.Th) * u,
                V = a.V + (b.V - a.V) * u,
                Om = a.Om + (b.Om - a.Om) * u,
            };
        }

        private static string StavKratce(CellState s, CellBlockReason d)
        {
            if (s != CellState.Blocked) return s == CellState.Free ? "Free" : "Unk";
            return d == (CellBlockReason.Geometry | CellBlockReason.Semantics) ? "B:GS"
                 : d == CellBlockReason.Geometry ? "B:G" : "B:S";
        }

        private static string F(double x) => double.IsNaN(x) ? "-" : x.ToString("F2", CultureInfo.InvariantCulture);
        private static string F0(double x) => double.IsNaN(x) ? "-" : x.ToString("F0", CultureInfo.InvariantCulture);

        private static double Median(List<double> x)
        {
            var s = x.OrderBy(a => a).ToList();
            return s.Count == 0 ? double.NaN : s[s.Count / 2];
        }

        private static string Fmt(Stats s)
            => s.Count == 0 ? $"{s.Name}: -"
             : string.Format(CultureInfo.InvariantCulture, "{0}: n={1} p50={2:F2} p10={3:F2} p90={4:F2} min={5:F2} max={6:F2}",
                             s.Name, s.Count, s.Median, s.Percentile(10), s.Percentile(90), s.Min, s.Max);
    }
}
