using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Occupancy;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Casova osa kolem drzeneho zastaveni</b> (prikaz <c>zotaveni</c>, tema
    /// <c>lp-zastaraly-regulator-toci-na-miste</c>, krok „proc po zotaveni kamer 9–13 s nevznika
    /// plan"). Pro kazdou epizodu <c>DriveCommandMsg.Held</c> vypise od uvolneni (t = 0) do prvniho
    /// planu s drahou a kousek za nim, co se delo v retezu snimek → grid → plan:
    /// <list type="bullet">
    /// <item><b>snimky po kamerach</b> z indexu (jmeno, cas porizeni a prichodu do streamu - bez
    /// cteni obrazu): posledni pred holdem, prvni po uvolneni, pocty po sekundach a posun
    /// razitka porizeni proti prichodu (po vymene kontextu RealSense muze razitko ujet a fuze pak
    /// snimek zahodi jako mimo okno historie);</item>
    /// <item><b>stupne zpracovani</b> z <c>PerfMsg</c> (zpracovano / zahozeno / fronta za sekundu)
    /// - jestli <c>LocalNavigator</c> v mezere snimky vubec dostava;</item>
    /// <item><b>grid a plany</b> (<c>OccupancyGridMsg</c>, VSECHNY <c>LocalPlanMsg</c> i bez drahy,
    /// se stavem), <b>cil navigace</b> (<c>GlobalNavMsg</c>) a <b>mise</b> (<c>MissionMsg</c>);</item>
    /// <item><b>hlasky z Trace</b> (<c>Info</c>) v okne a prikaz smycky (stop, rotace na miste).</item>
    /// </list>
    /// Casy jsou v case PRICHODU do streamu (index), ten je spolecny pro vsechny zpravy; u snimku
    /// a planu se vedle tiskne i cas porizeni.
    /// </summary>
    public static class ZotaveniReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        private struct Ev
        {
            public DateTime Arr;     // prichod do streamu
            public DateTime Cap;     // porizeni
            public string Kind;      // typ zpravy
            public string Name;      // jmeno instance (kamera)
            public IndexEntry E;
        }

        public static void Run(RecordFile rec, double poPlanuSec = 3.0, double maxSec = 40.0, double predSec = 3.0)
        {
            var drives = new List<DriveCommandMsg>();
            var infos = new List<(DateTime T, DateTime Arr, string Text)>();
            var evs = new List<Ev>();
            foreach (var e in rec.Index)
            {
                switch (e.MsgName)
                {
                    case "DriveCommandMsg":
                        if (rec.Read(e) is DriveCommandMsg d) drives.Add(d);
                        break;
                    case "Info":
                        if (rec.Read(e) is Info i) infos.Add((i.TimeStamp, e.ArrivalTime, (i.Message ?? "").Trim()));
                        break;
                    case "CameraFrame":
                    case "LocalPlanMsg":
                    case "OccupancyGridMsg":
                    case "GlobalNavMsg":
                    case "MissionMsg":
                    case "PerfMsg":
                    case "FreeRunMsg":
                        evs.Add(new Ev { Arr = e.ArrivalTime, Cap = e.CaptureTime, Kind = e.MsgName, Name = e.Name ?? "", E = e });
                        break;
                }
            }
            drives.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            evs.Sort((a, b) => a.Arr.CompareTo(b.Arr));
            infos.Sort((a, b) => a.T.CompareTo(b.T));
            bool arrivalZnamy = evs.Count > 0 && evs[0].E.ArrivalTicks != 0;

            Console.WriteLine(string.Format(Ci, "DriveCommandMsg {0}, Info {1}, sledovanych zprav {2}; cas prichodu v indexu {3}",
                drives.Count, infos.Count, evs.Count, arrivalZnamy ? "ano" : "NE (casy jsou porizeni)"));
            if (!arrivalZnamy)
                for (int k = 0; k < evs.Count; k++) { var x = evs[k]; x.Arr = x.Cap; evs[k] = x; }
            evs.Sort((a, b) => a.Arr.CompareTo(b.Arr));

            // Epizody drzeni: prechody Held.
            var epizody = new List<(DateTime Od, DateTime Do)>();
            for (int k = 0; k < drives.Count; k++)
            {
                if (!drives[k].Held || (k > 0 && drives[k - 1].Held)) continue;
                int j = k;
                while (j + 1 < drives.Count && drives[j + 1].Held) j++;
                if (j + 1 < drives.Count) epizody.Add((drives[k].TimeStamp, drives[j + 1].TimeStamp));
                k = j;
            }
            Console.WriteLine($"epizod drzeneho zastaveni (Held): {epizody.Count}");

            // Kamery (jmeno = prvni slovo instance) - kvuli souhrnu po kamerach.
            var kamery = evs.Where(x => x.Kind == "CameraFrame").Select(x => Kamera(x.Name)).Distinct().OrderBy(x => x).ToList();
            var souhrn = new List<string>();

            int cislo = 0;
            foreach (var (od, uvolneno) in epizody)
            {
                cislo++;
                var duvody = infos.Where(x => x.T >= od.AddSeconds(-1) && x.T <= uvolneno.AddSeconds(1) && x.Text.Contains("StopHold:"))
                                  .Select(x => x.Text).ToList();
                Console.WriteLine();
                Console.WriteLine(string.Format(Ci, "=== EPIZODA {0}: drzeni {1:HH:mm:ss.fff} - {2:HH:mm:ss.fff} ({3:F1} s); t = 0 je UVOLNENI",
                    cislo, od, uvolneno, (uvolneno - od).TotalSeconds));
                foreach (var d in duvody) Console.WriteLine("  " + d);

                // Prvni plan s drahou po uvolneni (cas prichodu).
                DateTime? prvniDraha = null;
                foreach (var x in evs)
                {
                    if (x.Kind != "LocalPlanMsg" || x.Arr <= uvolneno) continue;
                    if ((x.Arr - uvolneno).TotalSeconds > maxSec) break;
                    if (rec.Read(x.E) is LocalPlanMsg p && p.WayPoints != null && p.WayPoints.Length >= 2) { prvniDraha = x.Arr; break; }
                }
                var konec = prvniDraha.HasValue ? prvniDraha.Value.AddSeconds(poPlanuSec) : uvolneno.AddSeconds(maxSec);
                var zacatek = od.AddSeconds(-predSec);
                Console.WriteLine(prvniDraha.HasValue
                    ? string.Format(Ci, "  prvni plan S DRAHOU po uvolneni za {0:F2} s (cas prichodu)", (prvniDraha.Value - uvolneno).TotalSeconds)
                    : string.Format(Ci, "  plan s drahou do {0:F0} s po uvolneni NEPRISEL", maxSec));

                double Rel(DateTime t) => (t - uvolneno).TotalSeconds;

                // --- snimky po kamerach ---
                Console.WriteLine("  SNIMKY po kamerach (cas prichodu; posun = porizeni - prichod):");
                var okno = evs.Where(x => x.Arr >= zacatek && x.Arr <= konec).ToList();
                var prvniSnimek = new Dictionary<string, double>();
                foreach (var kam in kamery)
                {
                    var vse = evs.Where(x => x.Kind == "CameraFrame" && Kamera(x.Name) == kam).ToList();
                    var pred = vse.LastOrDefault(x => x.Arr < od);
                    var pod = vse.Where(x => x.Arr >= od && x.Arr <= uvolneno).ToList();
                    var po = vse.Where(x => x.Arr > uvolneno && x.Arr <= konec).ToList();
                    string s1 = pred.Kind != null ? string.Format(Ci, "posledni pred holdem {0:F2} s", Rel(pred.Arr)) : "pred holdem zadny";
                    string s2 = po.Count > 0 ? string.Format(Ci, "PRVNI po uvolneni {0:F2} s (posun {1:F3} s)", Rel(po[0].Arr), (po[0].Cap - po[0].Arr).TotalSeconds) : "po uvolneni ZADNY";
                    if (po.Count > 0) prvniSnimek[kam] = Rel(po[0].Arr);
                    Console.WriteLine(string.Format(Ci, "    {0,-6} {1}; pod holdem {2}; {3}; po uvolneni v okne {4}",
                        kam, s1, pod.Count, s2, po.Count));
                }

                // --- po sekundach od uvolneni: snimky, navigator (PerfMsg), plany, grid ---
                Console.WriteLine("  PO SEKUNDACH (od -predSec do konce okna):");
                Console.WriteLine("    t[s]   " + string.Join(" ", kamery.Select(k => k.PadLeft(6))) + "  posun[s]      grid  plany (stav x pocet)                         LocalNavigator zprac/zahoz/fronta");
                int odS = (int)Math.Floor(Rel(zacatek)), doS = (int)Math.Ceiling(Rel(konec));
                for (int s = odS; s < doS; s++)
                {
                    var t0 = uvolneno.AddSeconds(s); var t1 = t0.AddSeconds(1);
                    var v = okno.Where(x => x.Arr >= t0 && x.Arr < t1).ToList();
                    var snimky = v.Where(x => x.Kind == "CameraFrame").ToList();
                    string pocty = string.Join(" ", kamery.Select(k => snimky.Count(x => Kamera(x.Name) == k).ToString().PadLeft(6)));
                    string posun = snimky.Count > 0
                        ? string.Format(Ci, "{0,6:F3}..{1,6:F3}", snimky.Min(x => (x.Cap - x.Arr).TotalSeconds), snimky.Max(x => (x.Cap - x.Arr).TotalSeconds))
                        : "      -       ";
                    int gridy = v.Count(x => x.Kind == "OccupancyGridMsg");
                    var plany = v.Where(x => x.Kind == "LocalPlanMsg").Select(x => rec.Read(x.E) as LocalPlanMsg).Where(p => p != null).ToList();
                    string ps = string.Join(", ", plany.GroupBy(p => p.PlanStatus.ToString() + (p.WayPoints != null && p.WayPoints.Length >= 2 ? "" : "/bez drahy"))
                                                        .Select(g => g.Key + " x" + g.Count()));
                    string nav = "-";
                    foreach (var pm in v.Where(x => x.Kind == "PerfMsg").Select(x => rec.Read(x.E) as PerfMsg).Where(p => p != null))
                    {
                        var st = pm.Stages?.Where(z => (z.Name ?? "").Contains("LocalNavigator")).ToList();
                        if (st != null && st.Count > 0)
                            nav = string.Format(Ci, "{0}/{1}/{2} (do {3:HH:mm:ss})", st[0].Processed, st[0].Dropped, st[0].QueueLength, pm.To);
                    }
                    Console.WriteLine(string.Format(Ci, "    {0,4}   {1}  {2}  {3,4}  {4,-44} {5}",
                        s, pocty, posun, gridy, ps.Length == 0 ? "-" : ps, nav));
                }

                // --- cil navigace, mise ---
                Console.WriteLine("  NAVIGACE a MISE (zmeny v okne):");
                string predNav = null;
                foreach (var x in okno.Where(x => x.Kind == "GlobalNavMsg"))
                {
                    if (!(rec.Read(x.E) is GlobalNavMsg g)) continue;
                    string stav = string.Format(Ci, "GlobalNav {0}, cil {1}, mrkev {2}", (GlobalNavStatus)g.Status, g.HasGoal ? "ano" : "NE", g.HasCarrot ? "ano" : "NE");
                    if (stav == predNav) continue;
                    predNav = stav;
                    Console.WriteLine(string.Format(Ci, "    {0,7:F2}  {1}", Rel(x.Arr), stav));
                }
                int predFaze = int.MinValue;
                foreach (var x in okno.Where(x => x.Kind == "MissionMsg"))
                {
                    if (!(rec.Read(x.E) is MissionMsg m) || m.Phase == predFaze) continue;
                    predFaze = m.Phase;
                    Console.WriteLine(string.Format(Ci, "    {0,7:F2}  MissionMsg faze {1}", Rel(x.Arr), m.Phase));
                }
                int freeRun = okno.Count(x => x.Kind == "FreeRunMsg");
                if (freeRun > 0) Console.WriteLine($"    FreeRunMsg v okne: {freeRun}");

                // --- prikaz smycky ve SLEPE dobe (uvolneni -> prvni snimek kterekoli kamery) ---
                var prvniSnimekPo = okno.FirstOrDefault(x => x.Kind == "CameraFrame" && x.Arr > uvolneno);
                var slepaDo = prvniSnimekPo.Kind != null ? prvniSnimekPo.Arr : konec;
                // >= : uvolneno je razitko PRVNIHO taktu bez drzeni, ten do slepe doby patri.
                var dd = drives.Where(x => x.TimeStamp >= uvolneno && x.TimeStamp <= slepaDo).ToList();
                var rot = dd.Where(x => x.Speed == 0 && x.RotationSpeed != 0 && !x.EmergencyStop).ToList();
                var estop = dd.FirstOrDefault(x => x.EmergencyStop);
                int estopTaktu = dd.Count(x => x.EmergencyStop);
                Console.WriteLine(string.Format(Ci, "  PRIKAZ ve slepe dobe (uvolneni -> prvni snimek, {0:F2} s): taktu {1}, rotace na miste (v = 0, w != 0, bez stopu) {2}{3}{4}{5}",
                    Rel(slepaDo), dd.Count, rot.Count,
                    rot.Count > 0 ? string.Format(Ci, " od {0:F2} do {1:F2} s, max |w| {2:F2} rad/s", Rel(rot[0].TimeStamp), Rel(rot[rot.Count - 1].TimeStamp), rot.Max(x => Math.Abs(x.RotationSpeed))) : "",
                    estop != null ? string.Format(Ci, "; NOUZOVE ZASTAVENI od {0:F2} s ({1} taktu)", Rel(estop.TimeStamp), estopTaktu) : "",
                    rot.Count > 0 && estop != null ? string.Format(Ci, "; rotace pred stopem {0}, po nem {1}", rot.Count(x => x.TimeStamp < estop.TimeStamp), rot.Count(x => x.TimeStamp > estop.TimeStamp)) : ""));

                // --- hlasky ---
                Console.WriteLine("  HLASKY (Info) v okne (cas zpravy):");
                foreach (var x in infos.Where(x => x.T >= zacatek && x.T <= konec))
                    Console.WriteLine(string.Format(Ci, "    {0,7:F2}  {1}", Rel(x.T), x.Text.Length > 220 ? x.Text.Substring(0, 220) + "…" : x.Text));

                // --- souhrn ---
                var prvniGrid = okno.FirstOrDefault(x => x.Kind == "OccupancyGridMsg" && x.Arr > uvolneno);
                var prvniPlan = okno.FirstOrDefault(x => x.Kind == "LocalPlanMsg" && x.Arr > uvolneno);
                souhrn.Add(string.Format(Ci, "  ep. {0} {1:HH:mm:ss}: drzeni {2:F1} s; po uvolneni prvni snimek {3}; prvni grid {4}; prvni plan {5}; prvni plan s drahou {6}",
                    cislo, uvolneno, (uvolneno - od).TotalSeconds,
                    string.Join(", ", kamery.Select(k => k + " " + (prvniSnimek.TryGetValue(k, out var f) ? f.ToString("F2", Ci) + " s" : "-"))),
                    prvniGrid.Kind != null ? Rel(prvniGrid.Arr).ToString("F2", Ci) + " s" : "-",
                    prvniPlan.Kind != null ? Rel(prvniPlan.Arr).ToString("F2", Ci) + " s" : "-",
                    prvniDraha.HasValue ? Rel(prvniDraha.Value).ToString("F2", Ci) + " s" : "-"));
            }

            Console.WriteLine();
            Console.WriteLine("SOUHRN (casy od uvolneni drzeni, cas prichodu do streamu):");
            foreach (var s in souhrn) Console.WriteLine(s);
        }

        /// <summary>
        /// Rychly rezim <c>--kontext</c>: jen z hlasek (<c>Info</c>) - pro kazdou recyklaci sdileneho
        /// kontextu RealSense („novy kontext zalozen") za kolik sekund se ktera kamera znovu pripojila
        /// („pipeline pripojena") a co mezitim hlasily (selhany dotaz, selhany start, T265). Hlasku
        /// „dotaz prosel, zarizeni tu neni" driver nepise, takze ticho mezi recyklaci a pripojenim
        /// znamena jen to, ze se zadne volani nezaseklo (hlidac nativnich volani hlasi az od 20 s).
        /// Posledni radky jsou CSV: <c>ZOTAVENI;soubor;cas;kamera;za [s];selhanych dotazu;selhanych startu</c>.
        /// </summary>
        public static void RunKontext(RecordFile rec, double maxSec = 120.0)
        {
            var infos = new List<(DateTime T, string Text)>();
            foreach (var e in rec.Index)
                if (e.MsgName == "Info" && rec.Read(e) is Info i)
                    infos.Add((i.TimeStamp, (i.Message ?? "").Trim()));
            infos.Sort((a, b) => a.T.CompareTo(b.T));
            string soubor = System.IO.Path.GetFileName(rec.Path);

            var kontexty = infos.Where(x => x.Text.Contains("novy kontext zalozen")).ToList();
            Console.WriteLine($"{soubor}: hlasek {infos.Count}, recyklaci kontextu {kontexty.Count}");
            for (int k = 0; k < kontexty.Count; k++)
            {
                var t0 = kontexty[k].T;
                var dalsi = k + 1 < kontexty.Count ? kontexty[k + 1].T : t0.AddSeconds(maxSec);
                var doKdy = dalsi < t0.AddSeconds(maxSec) ? dalsi : t0.AddSeconds(maxSec);
                var okno = infos.Where(x => x.T > t0 && x.T <= doKdy).ToList();
                var drzeni = infos.LastOrDefault(x => x.T <= t0 && x.T >= t0.AddSeconds(-15) && x.Text.Contains("StopHold: robot se zastavuje"));
                var uvolnene = infos.Where(x => x.T >= t0.AddSeconds(-15) && x.T <= t0 && x.Text.Contains("handle uvolneny"))
                                    .Select(x => Kamera(x.Text)).ToList();
                Console.WriteLine(string.Format(Ci, "  {0:HH:mm:ss.fff}  {1}; uvolneno {2}; {3}",
                    t0, kontexty[k].Text, string.Join(", ", uvolnene),
                    drzeni.Text != null ? "pod drzenim (" + drzeni.Text + ")" : "BEZ drzeni (ridici smycka nebezela)"));
                foreach (var kam in uvolnene.Distinct())
                {
                    var pripojena = okno.FirstOrDefault(x => Kamera(x.Text) == kam && x.Text.Contains("pipeline pripojena"));
                    var doPripojeni = pripojena.Text != null ? pripojena.T : doKdy;
                    int qfail = okno.Count(x => x.T <= doPripojeni && Kamera(x.Text) == kam && x.Text.Contains("QueryDevices selhalo"));
                    int sfail = okno.Count(x => x.T <= doPripojeni && Kamera(x.Text) == kam && x.Text.Contains("pripojeni pipeline selhalo"));
                    string za = pripojena.Text != null ? ((pripojena.T - t0).TotalSeconds).ToString("F2", Ci) : "NE";
                    Console.WriteLine(string.Format(Ci, "    {0,-6} pripojena za {1,6} s; selhanych dotazu {2}, startu {3}", kam, za, qfail, sfail));
                    Console.WriteLine(string.Format(Ci, "ZOTAVENI;{0};{1:HH:mm:ss};{2};{3};{4};{5}", soubor, t0, kam, za, qfail, sfail));
                }
                foreach (var x in okno.Where(x => !x.Text.Contains("pipeline pripojena")).Take(12))
                    Console.WriteLine(string.Format(Ci, "      {0,7:F2}  {1}", (x.T - t0).TotalSeconds,
                        x.Text.Length > 160 ? x.Text.Substring(0, 160) + "…" : x.Text));
            }
        }

        /// <summary>Jmeno kamery = prvni slovo instance („Right 740112071021" → „Right").</summary>
        private static string Kamera(string jmeno)
        {
            if (string.IsNullOrEmpty(jmeno)) return "?";
            int i = jmeno.IndexOf(' ');
            return i > 0 ? jmeno.Substring(0, i) : jmeno;
        }
    }
}
