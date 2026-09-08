using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Regulators;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Proč plán předepsal zrovna takovou rychlost</b> — rozpad rychlostní obálky lokálního
    /// plánovače na jednotlivé členy, <b>uzel po uzlu</b>. Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para><b>Nač to je.</b> <see cref="LocalPlanReport"/> ukáže, <i>že</i> se robot plazí
    /// (příkazovaná rychlost na podlaze <c>MinCostSpeed</c> = 0,05 m/s), ale nikoli <b>čím</b>.
    /// Rychlost uzlu je <c>max(MinCostSpeed, min(VAlong, VClosing, VBrake))</c> a každý ten člen
    /// znamená jinou příčinu a jinou léčbu:</para>
    /// <list type="bullet">
    /// <item><description><b>VAlong</b> (podélný strop u okraje) — dráha vede blízko
    /// <b>neprůjezdné</b> buňky. Nula znamená odstup <b>právě</b> <c>SafeDist</c>: A* blíž nesmí,
    /// takže v úzkém místě je nejlepší legální dráha zároveň tou, které obálka dá nulu.</description></item>
    /// <item><description><b>VClosing</b> (kolmý strop) — dráha k překážce <b>míří</b>, takže se
    /// brzdí na brzdnou dráhu k hranici <c>SafeDist</c>.</description></item>
    /// <item><description><b>VBrake</b> (hranice potvrzeného) — před robotem není nic
    /// <c>Free</c>. To NENÍ překážka, to je <b>nevidím</b>; léčba je vidět dál, ne uhýbat.</description></item>
    /// </list>
    ///
    /// <para><b>Proč po uzlech.</b> Minimum přes plán splácne dohromady dvě úplně jiné situace:
    /// „leze už od sebe" (váže první uzel) a „za dva metry se cesta zužuje" (váže uzel na konci
    /// dráhy, a robot přitom může jet plnou rychlostí). Rozbor proto vypisuje i <b>vzdálenost
    /// vázajícího uzlu od robota</b> a rychlost <b>prvního</b> uzlu, tedy to, co robot dostane
    /// u sebe.</para>
    ///
    /// <para><b>Odkud se rozpad bere.</b> Od <c>LocalPlanMsg</c> verze 2 ho nese <b>zpráva</b>
    /// (<c>EnvClearanceM</c>, <c>EnvClosing</c>, <c>EnvFreeAheadM</c>, <c>EnvVClearance</c>,
    /// <c>EnvVBrake</c> — jedna hodnota na waypoint), tedy přesně to, co spočítal běžící plánovač.
    /// U <b>starších záznamů</b> v nich není a rozbor se <b>rekonstruuje</b> z
    /// <see cref="OccupancyGridMsg"/> a waypointů: grid se ze zprávy postaví zpátky, přepočítá se
    /// pole odstupů a dráha se navzorkuje týmž způsobem jako v
    /// <c>LocalPathPlanner.BuildWayPoints</c>. Rekonstrukce <b>není domněnka</b> — kontroluje se
    /// proti <c>MinClearanceM</c>, které zpráva nese od začátku; když ta kontrola nesedí, zbytek
    /// čísel nemá smysl číst a rozbor to napíše.</para>
    ///
    /// <para><b>Cena rekonstrukce:</b> grid je v záznamu ~2×/s, plány ~18×/s, takže se plán páruje
    /// s <b>nejbližším</b> gridem a párovací zpoždění se vypisuje. Grid se mezitím posouvá s robotem,
    /// proto se páry nad <c>--maxlag</c> zahazují.</para>
    /// </summary>
    public static class EnvelopeReport
    {
        /// <summary>Který člen obálky vázal rychlost.</summary>
        private enum Binder { None = 0, Along = 1, Closing = 2, Brake = 3 }

        /// <param name="binSeconds">Šířka okna časové osy [s].</param>
        /// <param name="maxLagSec">Nejvýš takové zpoždění mezi plánem a použitým gridem [s].</param>
        /// <param name="safeDist">Přepis <c>SafeDist</c> [m]; NaN = vzít z konfigurace v záznamu.</param>
        /// <param name="maxSpeed">Přepis <c>MaxSpeed</c> [m/s]; NaN = vzít z konfigurace v záznamu.</param>
        public static void Run(RecordFile rec, double binSeconds, double maxLagSec,
                               double safeDist, double maxSpeed)
        {
            var plans = rec.ReadAll<LocalPlanMsg>("LocalPlanMsg").OrderBy(p => p.TimeStamp).ToList();
            var drives = rec.ReadAll<DriveCommandMsg>("DriveCommandMsg").OrderBy(d => d.TimeStamp).ToList();
            Console.WriteLine($"LocalPlanMsg {plans.Count} (verze {(plans.Count > 0 ? plans[0].Verze : 0)}), "
                              + $"DriveCommandMsg {drives.Count}");
            if (plans.Count == 0) { Console.WriteLine("Zaznam lokalni plany nenese."); return; }

            var cfg = BuildConfig(rec, safeDist, maxSpeed);
            var t0 = plans[0].TimeStamp;

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "OBALKA, SE KTEROU SE POCITA: {0}, SafeDist={1:F2} m, MaxSpeed={2:F2} m/s, "
                + "EdgeMargin={3:F2} m, a={4:F2} m/s2, podlaha={5:F2} m/s",
                cfg.Envelope, cfg.SafeDist, cfg.MaxSpeed, cfg.EdgeMarginM, cfg.MaxDeceleration,
                cfg.MinCostSpeed));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  plnou rychlost PODEL prekazky dostane az odstup >= {0:F2} m; pri odstupu {1:F2} m "
                + "je podelny strop NULA", cfg.SafeDist + cfg.EdgeMarginM, cfg.SafeDist));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  brzdna obalka: na podlahu {0:F2} m/s klesne az pri volnu pod {1:F3} m "
                + "(v = sqrt(2*a*volno))", cfg.MinCostSpeed,
                cfg.MinCostSpeed * cfg.MinCostSpeed / (2 * cfg.MaxDeceleration)));
            Console.WriteLine();

            var rows = plans[0].HasEnvelope
                ? FromMessage(plans, drives, cfg, t0)
                : Reconstruct(rec, plans, drives, cfg, t0, maxLagSec);
            if (rows == null || rows.Count == 0) return;

            Summary(rows, cfg);
            TimeLine(rows, cfg, binSeconds);
        }

        // ------------------------------------------------------------------
        // Rozpad PRIMO ZE ZPRAVY (LocalPlanMsg verze >= 2)
        // ------------------------------------------------------------------

        /// <summary>
        /// Rozpad tak, jak ho spocital bezici planovac. Parovat s gridem neni potreba — cislo je
        /// presne, ne rekonstruovane.
        /// </summary>
        private static List<Row> FromMessage(List<LocalPlanMsg> plans, List<DriveCommandMsg> drives,
                                             LocalPlannerConfig cfg, DateTime t0)
        {
            Console.WriteLine("ZDROJ ROZPADU: zprava (LocalPlanMsg verze >= 2) — hodnoty jsou z BEZICIHO");
            Console.WriteLine("planovace, uzel po uzlu, nic se nerekonstruuje.");
            Console.WriteLine();

            var rows = new List<Row>(plans.Count);
            int di = 0;
            foreach (var p in plans)
            {
                if (p.WayPoints == null || p.WayPoints.Length < 2 || !p.HasEnvelope) continue;
                while (di + 1 < drives.Count && drives[di + 1].TimeStamp <= p.TimeStamp) di++;

                var r = Analyze(cfg, p.WayPoints, p.EnvClearanceM, p.EnvClosing,
                                p.EnvFreeAheadM, p.EnvVClearance, p.EnvVBrake);
                r.T = (p.TimeStamp - t0).TotalSeconds;
                r.Status = p.PlanStatus;
                if (drives.Count > 0) { r.VCmd = Math.Abs(drives[di].Speed); r.Estop = drives[di].EmergencyStop; }
                rows.Add(r);
            }
            Console.WriteLine($"vyhodnoceno planu: {rows.Count}");
            Console.WriteLine();
            return rows;
        }

        // ------------------------------------------------------------------
        // REKONSTRUKCE (starsi zaznamy)
        // ------------------------------------------------------------------

        private static List<Row> Reconstruct(RecordFile rec, List<LocalPlanMsg> plans,
                                             List<DriveCommandMsg> drives, LocalPlannerConfig cfg,
                                             DateTime t0, double maxLagSec)
        {
            Console.WriteLine("ZDROJ ROZPADU: REKONSTRUKCE (LocalPlanMsg verze 1 rozpad nenese).");
            Console.WriteLine("Grid se stavi ze zpravy, pole odstupu se prepocita a draha navzorkuje");
            Console.WriteLine("stejne jako v planovaci. Kontrola je nize - bez ni necti zbytek.");
            Console.WriteLine();

            var grids = rec.ReadAll<OccupancyGridMsg>("OccupancyGridMsg").OrderBy(g => g.TimeStamp).ToList();
            var runs = rec.ReadAll<FreeRunMsg>("FreeRunMsg").OrderBy(f => f.TimeStamp).ToList();
            Console.WriteLine($"OccupancyGridMsg {grids.Count}");
            if (grids.Count == 0)
            {
                Console.WriteLine("Zaznam grid nenese - rozpad nejde rekonstruovat. Poridit novy "
                                  + "zaznam (LocalPlanMsg verze 2 uz rozpad nese sama).");
                return null;
            }

            var rows = new List<Row>(plans.Count);
            var lag = new Stats("parovaci zpozdeni plan<->grid [s]");
            var check = new Stats("rekonstruovany - hlaseny MinClearance [m]");
            int noPath = 0, tooOld = 0, agree = 0;

            OccupancyGrid grid = null;
            ClearanceField field = null;
            OccupancyGridMsg builtFrom = null;
            var sampler = new PathSampler();
            int gi = 0, di = 0, fi = 0;

            foreach (var p in plans)
            {
                if (p.WayPoints == null || p.WayPoints.Length < 2) { noPath++; continue; }
                while (gi + 1 < grids.Count && grids[gi + 1].TimeStamp <= p.TimeStamp) gi++;
                while (di + 1 < drives.Count && drives[di + 1].TimeStamp <= p.TimeStamp) di++;
                while (fi + 1 < runs.Count && runs[fi + 1].TimeStamp <= p.TimeStamp) fi++;

                // Nejblizsi grid v case (predchazejici nebo nasledujici - plan mohl vzniknout
                // tesne pred snapshotem).
                var g = grids[gi];
                if (gi + 1 < grids.Count
                    && Math.Abs((grids[gi + 1].TimeStamp - p.TimeStamp).TotalSeconds)
                       < Math.Abs((g.TimeStamp - p.TimeStamp).TotalSeconds)) g = grids[gi + 1];

                double dt = Math.Abs((g.TimeStamp - p.TimeStamp).TotalSeconds);
                lag.Add(dt);
                if (dt > maxLagSec) { tooOld++; continue; }

                if (!ReferenceEquals(g, builtFrom))
                {
                    if (grid == null || grid.Size != g.Size)
                    {
                        grid = NewGrid(g);
                        field = new ClearanceField(grid);
                    }
                    Fill(grid, g);
                    field.Build(grid);
                    builtFrom = g;
                }

                sampler.Build(grid, field, cfg, p.WayPoints);
                var r = Analyze(cfg, p.WayPoints, sampler.Clearance, sampler.Closing,
                                sampler.FreeAhead, sampler.VClearance, sampler.VBrake);
                r.T = (p.TimeStamp - t0).TotalSeconds;
                r.Status = p.PlanStatus;
                if (drives.Count > 0) { r.VCmd = Math.Abs(drives[di].Speed); r.Estop = drives[di].EmergencyStop; }
                r.Frontier = sampler.FrontierState;
                r.NearestReason = sampler.NearestReason;
                r.NearestNeigh8 = sampler.NearestNeigh8;
                r.NearestBlob = sampler.NearestBlob;
                r.ChannelM = sampler.ChannelM;
                // Sirku koridoru nese jen cyklus, ktery ho nasel; jinak neni s cim srovnavat.
                if (runs.Count > 0 && runs[fi].FromCorridor
                    && Math.Abs((runs[fi].TimeStamp - p.TimeStamp).TotalSeconds) < 0.5)
                    r.CamWidthM = runs[fi].Width;
                rows.Add(r);

                // Kontrola: MinClearanceM zprava nese od verze 1, takze je to nezavisla odpoved.
                // Plany bez drahy maji v zazname double.MaxValue (planovac ho nechal na inicialni
                // hodnote) - takove do kontroly nepatri.
                if (p.MinClearanceM < 1e6)
                {
                    double d = r.Clearance - p.MinClearanceM;
                    check.Add(d);
                    if (Math.Abs(d) <= grid.Resolution) agree++;
                }
            }

            Console.WriteLine($"planu bez drahy: {noPath}, zahozeno kvuli zpozdeni > {maxLagSec:F2} s: {tooOld}, "
                              + $"vyhodnoceno: {rows.Count}");
            Console.WriteLine("  " + lag.Line("s"));
            Console.WriteLine();

            Console.WriteLine("KONTROLA REKONSTRUKCE (proti MinClearanceM ze zpravy):");
            Console.WriteLine("  " + check.Line("m"));
            if (check.Count > 0 && grid != null)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  souhlas do jedne bunky ({0:F2} m): {1:F1} %  ({2} z {3})",
                    grid.Resolution, 100.0 * agree / check.Count, agree, check.Count));
            Console.WriteLine("  Rozdil je hlavne z toho, ze grid je v zazname ~2x/s, kdezto planuje se ~18x/s:");
            Console.WriteLine("  planovac videl jinou (mladsi) verzi mapy. Kdyz souhlas klesne pod ~2/3,");
            Console.WriteLine("  je rekonstrukce k nicemu a je potreba novy zaznam s LocalPlanMsg verze 2.");
            Console.WriteLine();
            return rows;
        }

        // ------------------------------------------------------------------
        // Vyhodnoceni jednoho planu z per-uzlovych poli (spolecne pro oba zdroje)
        // ------------------------------------------------------------------

        /// <summary>
        /// Z rozpadu po uzlech udela jeden radek: co vazalo, jak silne a KDE na draze. Minimum se
        /// bere pres MEZILEHLE uzly - posledni uzel je z definice konec drahy (freeAhead 0), takze
        /// by minimum vzdycky vyslo tam.
        /// </summary>
        private static Row Analyze(LocalPlannerConfig cfg, RegulatorWayPoint[] wps,
                                   float[] clearance, float[] closing, float[] freeAhead,
                                   float[] vClearance, float[] vBrake)
        {
            int n = Math.Min(wps.Length, vClearance.Length);
            var r = new Row
            {
                Clearance = double.MaxValue,
                FreeAhead = double.MaxValue,
                VClear = double.MaxValue,
                VBrake = double.MaxValue,
                Speed = double.MaxValue,
                Node0Speed = wps[0].Speed,
                Nodes = n,
            };

            // Arc-length uzlu (v zazname jsou jen souradnice) - pro "kde na draze to vaze".
            var s = new double[n];
            for (int k = 1; k < n; k++)
            {
                double dx = wps[k].X - wps[k - 1].X, dy = wps[k].Y - wps[k - 1].Y;
                s[k] = s[k - 1] + Math.Sqrt(dx * dx + dy * dy);
            }

            int bindNode = 0;
            for (int k = 0; k < n - 1; k++)
            {
                double vc = vClearance[k], vb = vBrake[k];
                if (double.IsNaN(vc) || double.IsNaN(vb)) continue;
                double speed = Math.Max(cfg.MinCostSpeed, Math.Min(vc, vb));

                if (clearance[k] < r.Clearance) r.Clearance = clearance[k];
                if (freeAhead[k] < r.FreeAhead) r.FreeAhead = freeAhead[k];
                if (vc < r.VClear) r.VClear = vc;
                if (vb < r.VBrake) r.VBrake = vb;
                if (speed < r.Speed)
                {
                    r.Speed = speed;
                    bindNode = k;
                    // Podelny nebo kolmy? Obojí jde dopocitat z odstupu a priblizovani, takze
                    // zprava ani rekonstrukce nemusi ukladat oba stropy zvlast.
                    double along = cfg.VAlong(clearance[k]);
                    double clos = cfg.VClosing(clearance[k], closing[k]);
                    r.Bind = vb < Math.Min(along, clos) ? Binder.Brake
                           : along <= clos ? Binder.Along : Binder.Closing;
                }
            }
            r.BindAtM = s[Math.Min(bindNode, n - 1)];
            r.PathLenM = s[n - 1];
            if (r.Speed > cfg.MinCostSpeed + 1e-9 && r.VClear >= cfg.MaxSpeed && r.VBrake >= cfg.MaxSpeed)
                r.Bind = Binder.None;
            return r;
        }

        // ------------------------------------------------------------------
        // Souhrn a casova osa
        // ------------------------------------------------------------------

        private static void Summary(List<Row> rows, LocalPlannerConfig cfg)
        {
            Console.WriteLine("KTERY CLEN OBALKY VAZAL RYCHLOST (nejnizsi mezilehly uzel planu):");
            foreach (var g in rows.GroupBy(r => r.Bind).OrderByDescending(g => g.Count()))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-34} {1,6}  ({2,5:F1} %)   srazil na {3:F2} m/s, {4:F2} m od robotu",
                    Label(g.Key), g.Count(), 100.0 * g.Count() / rows.Count,
                    Median(g.Select(r => r.Speed).ToArray()),
                    Median(g.Select(r => r.BindAtM).ToArray())));
            Console.WriteLine();

            // Uzel u ROBOTU je to, co dostane regulator hned; vazani na konci drahy robota
            // nezdrzuje (dojede tam za pul sekundy a plan je uz jiny).
            int floor = rows.Count(r => r.Speed <= cfg.MinCostSpeed + 1e-6);
            int floor0 = rows.Count(r => r.Node0Speed <= cfg.MinCostSpeed + 1e-6);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "PLANU NA PODLAZE ({0:F2} m/s): nekde na draze {1} z {2} ({3:F1} %), "
                + "UZ V PRVNIM UZLU {4} ({5:F1} %)",
                cfg.MinCostSpeed, floor, rows.Count, 100.0 * floor / rows.Count,
                floor0, 100.0 * floor0 / rows.Count));
            var atFloor = rows.Where(r => r.Speed <= cfg.MinCostSpeed + 1e-6).ToList();
            foreach (var g in atFloor.GroupBy(r => r.Bind).OrderByDescending(g => g.Count()))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  z toho {0,-34} {1,6}  ({2,5:F1} %)",
                    Label(g.Key), g.Count(), 100.0 * g.Count() / Math.Max(1, atFloor.Count)));
            Console.WriteLine();

            var clr = new Stats("nejmensi odstup na draze [m]");
            var free = new Stats("nejmensi volno pred sebou [m]");
            var vAl = new Stats("strop z odstupu VClear [m/s]");
            var vBr = new Stats("strop z potvrzeneho VBrake [m/s]");
            var sp = new Stats("nejnizsi rychlost uzlu [m/s]");
            var sp0 = new Stats("rychlost PRVNIHO uzlu [m/s]");
            var at = new Stats("vazajici uzel od robotu [m]");
            var len = new Stats("delka drahy [m]");
            var cmd = new Stats("prikazana rychlost [m/s]");
            foreach (var r in rows)
            {
                if (r.Clearance < 1e6) clr.Add(r.Clearance);
                free.Add(r.FreeAhead); vAl.Add(r.VClear); vBr.Add(r.VBrake);
                sp.Add(r.Speed); sp0.Add(r.Node0Speed); at.Add(r.BindAtM);
                len.Add(r.PathLenM); cmd.Add(r.VCmd);
            }
            Console.WriteLine("ROZDELENI:");
            foreach (var st in new[] { clr, free, vAl, vBr, sp, sp0, at, len, cmd })
                Console.WriteLine("  " + st.Line());
            Console.WriteLine();

            // Odstup PRAVE na SafeDist je zvlastni pripad, ktery je potreba videt jmenovite: A* bliz
            // nesmi, takze v uzkem miste je nejlepsi legalni draha zaroven ta, ktere obalka da nulu.
            int atSafe = rows.Count(r => r.Clearance < 1e6
                                         && Math.Abs(r.Clearance - cfg.SafeDist) <= 1e-6);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "ODSTUP PRAVE NA SafeDist ({0:F2} m, tedy podelny strop = 0): {1} z {2} ({3:F1} %)",
                cfg.SafeDist, atSafe, rows.Count, 100.0 * atSafe / rows.Count));
            Console.WriteLine("  A* pod SafeDist nejde, takze v uzkem miste je nejlepsi LEGALNI draha");
            Console.WriteLine("  zaroven ta, ktere obalka da nulu - a robot leze podlahou. Neni to vada");
            Console.WriteLine("  planovace ani obalky; je to dusledek toho, ze SafeDist je zaroven tvrda");
            Console.WriteLine("  hranice A* i nulovy bod rampy.");
            Console.WriteLine();

            if (!rows.Any(r => r.ChannelM > 0)) return;    // dalsi bloky umi jen rekonstrukce

            var byFrontier = rows.Where(r => r.Frontier != CellState.Free).GroupBy(r => r.Frontier).ToList();
            Console.WriteLine("CO JE HRANICE POTVRZENEHO (prvni ne-Free bunka na draze):");
            foreach (var g in byFrontier.OrderByDescending(g => g.Count()))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} {1,6}  ({2,5:F1} %)", g.Key, g.Count(), 100.0 * g.Count() / rows.Count));
            Console.WriteLine("  Unknown = NEVIDIM (kamera tam nedosviti / neni potvrzeno obema kanaly),");
            Console.WriteLine("  Blocked = skutecna prekazka. Lecba je u kazdeho jina.");
            Console.WriteLine();

            Console.WriteLine("CIM JE BLOKOVANA NEJBLIZSI NEPRUJEZDNA BUNKA (ta, co dava odstup):");
            foreach (var g in rows.Where(r => r.NearestReason != CellBlockReason.None)
                                  .GroupBy(r => r.NearestReason).OrderByDescending(g => g.Count()))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-22} {1,6}  ({2,5:F1} %)", g.Key, g.Count(), 100.0 * g.Count() / rows.Count));
            Console.WriteLine("  Geometry = hloubka (fyzicka prekazka), Semantics = barva (\"tohle neni cesta\").");
            Console.WriteLine();

            // HRANA nebo SKVRNA? Lecba je uplne jina: hranu je potreba objet, skvrnu odfiltrovat.
            var blob = new Stats("velikost skvrny [bunek, strop 64]");
            var neigh = new Stats("Blocked sousedu z 8");
            foreach (var r in rows)
            {
                if (r.NearestBlob >= 0) blob.Add(r.NearestBlob);
                if (r.NearestNeigh8 >= 0) neigh.Add(r.NearestNeigh8);
            }
            Console.WriteLine("JE TA PREKAZKA HRANA, NEBO SKVRNA? (bunka, ktera dava odstup)");
            foreach (var st in new[] { blob, neigh }) Console.WriteLine("  " + st.Line());
            int speck = rows.Count(r => r.NearestBlob >= 0 && r.NearestBlob <= 4);
            int solid = rows.Count(r => r.NearestBlob >= 20);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  skvrna do 4 bunek (tedy do {0:F2} m2): {1} z {2} ({3:F1} %)",
                4 * 0.05 * 0.05, speck, rows.Count, 100.0 * speck / rows.Count));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  souvisla plocha >= 20 bunek (spis skutecna hrana): {0} z {1} ({2:F1} %)",
                solid, rows.Count, 100.0 * solid / rows.Count));
            Console.WriteLine("  Souvisla hrana travy ma 3-5 Blocked sousedu z 8 a skvrnu o desitkach");
            Console.WriteLine("  bunek. Izolovana bunka (0-1 soused) je SUM V MAPE, ne prekazka - a");
            Console.WriteLine("  presto srazi rychlost na podlahu uplne stejne.");
            Console.WriteLine();

            // Uzky odstup muze znamenat uzkou cestu NEBO prekazku nakreslenou do siroke cesty.
            // Rozhodne to nezavisla sirka koridoru z kamer.
            var ch = new Stats("volny kanal v gridu napric drahou [m]");
            var cam = new Stats("sirka koridoru z KAMER (FreeRunMsg) [m]");
            var diff = new Stats("kamera - grid [m]");
            foreach (var r in rows)
            {
                ch.Add(r.ChannelM);
                if (!double.IsNaN(r.CamWidthM)) { cam.Add(r.CamWidthM); diff.Add(r.CamWidthM - r.ChannelM); }
            }
            Console.WriteLine("JAK SIROKO MA ROBOT V GRIDU (a co k tomu rikaji kamery):");
            foreach (var st in new[] { ch, cam, diff }) Console.WriteLine("  " + st.Line("m"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  na plnou rychlost podel obou okraju je potreba kanal >= {0:F2} m "
                + "(2 x (SafeDist + EdgeMargin))", 2 * (cfg.SafeDist + cfg.EdgeMarginM)));
            int narrow = rows.Count(r => !double.IsNaN(r.ChannelM)
                                         && r.ChannelM < 2 * (cfg.SafeDist + cfg.EdgeMarginM));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  kanal UZSI nez to: {0} z {1} ({2:F1} %)", narrow, rows.Count,
                100.0 * narrow / rows.Count));
            Console.WriteLine("  Kanal je merenej napric drahou U ROBOTU, takze siroky kanal + nizky");
            Console.WriteLine("  odstup znamena, ze prekazka je PODEL drahy dal, ne u robotu.");
            Console.WriteLine();
        }

        private static void TimeLine(List<Row> rows, LocalPlannerConfig cfg, double binSeconds)
        {
            Console.WriteLine($"CASOVA OSA (okno {binSeconds:F0} s; mediany v okne):");
            Console.WriteLine("    t[s]   n  Along Clos Brake Free  podl%  u0%  odstup  volno  vClear  vBrake  vUzel   v0  kde  vCmd");
            double tMax = rows[rows.Count - 1].T;
            for (double t = 0; t < tMax; t += binSeconds)
            {
                double to = t + binSeconds;
                var bin = rows.Where(r => r.T >= t && r.T < to).ToList();
                if (bin.Count == 0) continue;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0} {1,3} {2,6} {3,4} {4,5} {5,4} {6,6:F0} {7,4:F0}  {8,6:F2} {9,6:F2}  {10,6:F2}  {11,6:F2} {12,6:F2} {13,4:F2} {14,4:F1} {15,5:F2}",
                    t, bin.Count,
                    bin.Count(r => r.Bind == Binder.Along), bin.Count(r => r.Bind == Binder.Closing),
                    bin.Count(r => r.Bind == Binder.Brake), bin.Count(r => r.Bind == Binder.None),
                    100.0 * bin.Count(r => r.Speed <= cfg.MinCostSpeed + 1e-6) / bin.Count,
                    100.0 * bin.Count(r => r.Node0Speed <= cfg.MinCostSpeed + 1e-6) / bin.Count,
                    Median(bin.Where(r => r.Clearance < 1e6).Select(r => r.Clearance).ToArray()),
                    Median(bin.Select(r => r.FreeAhead).ToArray()),
                    Median(bin.Select(r => r.VClear).ToArray()),
                    Median(bin.Select(r => r.VBrake).ToArray()),
                    Median(bin.Select(r => r.Speed).ToArray()),
                    Median(bin.Select(r => r.Node0Speed).ToArray()),
                    Median(bin.Select(r => r.BindAtM).ToArray()),
                    Median(bin.Select(r => r.VCmd).ToArray())));
            }
            Console.WriteLine("  Along/Clos/Brake/Free = kolik planu v okne vazal ktery clen (Free = zadny,");
            Console.WriteLine("  tedy plna rychlost), podl% = podil planu s nejnizsim uzlem na podlaze,");
            Console.WriteLine("  u0% = podil, kde je na podlaze uz PRVNI uzel (to robota skutecne zdrzi),");
            Console.WriteLine("  v0 = rychlost prvniho uzlu, kde = vzdalenost vazajiciho uzlu od robotu.");
            Console.WriteLine();
        }

        private static string Label(Binder b) => b switch
        {
            Binder.Along => "VAlong (odstup od prekazky)",
            Binder.Closing => "VClosing (priblizovani k prekazce)",
            Binder.Brake => "VBrake (hranice potvrzeneho)",
            _ => "nic (plna rychlost)",
        };

        private static double Median(double[] v)
        {
            var s = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToArray();
            return s.Length == 0 ? double.NaN : s[s.Length / 2];
        }

        // ------------------------------------------------------------------
        // Konfigurace ze zaznamu
        // ------------------------------------------------------------------

        /// <summary>
        /// Obalka se musi pocitat s TOUZ konfiguraci, s jakou bezel zaznam - jinak rozpad odpovida
        /// jinemu robotu. Ucinne hodnoty jsou v zaznamu jako <see cref="Info"/> (vypis konfigurace
        /// po pripojeni TraceInfoBridge, viz doc/record-replay.md), takze se ctou odtud a jen kdyz
        /// tam nejsou, padne se na default z kodu.
        /// </summary>
        private static LocalPlannerConfig BuildConfig(RecordFile rec, double safeDist, double maxSpeed)
        {
            var cfg = new LocalPlannerConfig();
            string envelope = null;
            double? fileSafe = null, fileMax = null;

            foreach (var e in rec.Index)
            {
                if (e.MsgName != "Info") continue;
                if (!(rec.Read(e) is Info info)) continue;
                string s = (info.Message ?? string.Empty).Trim();
                if (s.StartsWith("safedist=")) fileSafe = ParseValue(s);
                else if (s.StartsWith("maxspeed=")) fileMax = ParseValue(s);
                else if (s.StartsWith("envelope=")) envelope = Word(s);
            }

            if (!double.IsNaN(safeDist)) cfg.SafeDist = safeDist;
            else if (fileSafe.HasValue) cfg.SafeDist = fileSafe.Value;

            if (!double.IsNaN(maxSpeed)) cfg.MaxSpeed = maxSpeed;
            else if (fileMax.HasValue) cfg.MaxSpeed = fileMax.Value;

            if (string.Equals(envelope, "radial", StringComparison.OrdinalIgnoreCase))
                cfg.Envelope = SpeedEnvelopeMode.Radial;

            cfg.Validate();
            return cfg;
        }

        /// <summary>Ciselna hodnota z radku <c>klic=hodnota  (puvod)</c>; <c>null</c> pri nezdaru.</summary>
        private static double? ParseValue(string line)
            => double.TryParse(Word(line), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               ? v : (double?)null;

        /// <summary>Hodnota z radku <c>klic=hodnota  (puvod)</c> bez poznamky o puvodu.</summary>
        private static string Word(string line)
        {
            int eq = line.IndexOf('=');
            if (eq < 0) return string.Empty;
            string rest = line.Substring(eq + 1).Trim();
            int sp = rest.IndexOf(' ');
            return (sp < 0 ? rest : rest.Substring(0, sp)).Trim();
        }

        // ------------------------------------------------------------------
        // Grid ze zpravy
        // ------------------------------------------------------------------

        /// <summary>Prazdny grid s TOUZ geometrii a prahy, jake ma snapshot ve zprave.</summary>
        private static OccupancyGrid NewGrid(OccupancyGridMsg m)
            => new OccupancyGrid(new OccupancyGridConfig
            {
                Size = m.Size,
                Resolution = m.Resolution,
                Scale = m.Scale,
                BlockedThreshold = m.BlockedThreshold,
                FreeThreshold = m.FreeThreshold,
            });

        /// <summary>
        /// Nasype snapshot ze zpravy do gridu. Zprava je v LOKALNIM poradi (<c>i + j*Size</c>),
        /// grid v kruhovem bufferu - prevod dela <see cref="OccupancyGrid.LocalIndex"/>.
        /// </summary>
        private static void Fill(OccupancyGrid grid, OccupancyGridMsg m)
        {
            grid.MoveOrigin(m.OriginX, m.OriginY);
            for (int j = 0; j < m.Size; j++)
            {
                int src = j * m.Size;
                for (int i = 0; i < m.Size; i++)
                {
                    int dst = grid.LocalIndex(i, j);
                    grid.Occ[dst] = m.Occ[src + i];
                    grid.Road[dst] = m.Road != null ? m.Road[src + i] : (sbyte)0;
                }
            }
        }

        // ------------------------------------------------------------------
        // Vzorkovani drahy - replika LocalPathPlanner.SamplePath/BuildWayPoints
        // ------------------------------------------------------------------

        /// <summary>
        /// Prepocet rozpadu obalky nad ZAZNAMENANOU drahou. Zamerne kopiruje postup
        /// <c>LocalPathPlanner.SamplePath</c> + <c>BuildWayPoints</c> (krok 1/2 bunky, okno uzlu =
        /// usek pred + za, frontier od konce drahy, pudorys robotu jako sjizdny) - kdyby se lisil,
        /// nemeri se obalka planovace, ale jina obalka. Souhlas hlida kontrola proti
        /// <c>MinClearanceM</c>.
        /// </summary>
        private sealed class PathSampler
        {
            private readonly List<double> s = new List<double>();
            private readonly List<double> clear = new List<double>();
            private readonly List<double> closing = new List<double>();
            private readonly List<bool> isFree = new List<bool>();
            private readonly List<CellState> state = new List<CellState>();
            private readonly List<CellBlockReason> reason = new List<CellBlockReason>();
            // Lokalni linearni index (i + j*Size) NEJBLIZSI Blocked bunky u vzorku; -1 = zadna.
            private readonly List<int> nearestCell = new List<int>();

            /// <summary>Odstup u uzlu [m] (stejne pole, jake od verze 2 nese zprava).</summary>
            public float[] Clearance { get; private set; } = new float[0];

            /// <summary>Priblizovani k prekazce u uzlu 0..1.</summary>
            public float[] Closing { get; private set; } = new float[0];

            /// <summary>Volno pred uzlem [m].</summary>
            public float[] FreeAhead { get; private set; } = new float[0];

            /// <summary>Strop z odstupu u uzlu [m/s].</summary>
            public float[] VClearance { get; private set; } = new float[0];

            /// <summary>Strop z brzdne obalky u uzlu [m/s].</summary>
            public float[] VBrake { get; private set; } = new float[0];

            /// <summary>Stav bunky na hranici potvrzeneho (Unknown = nevidim, Blocked = prekazka).</summary>
            public CellState FrontierState { get; private set; }

            /// <summary>Cim je blokovana nejblizsi neprujezdna bunka u nejtesnejsiho uzlu.</summary>
            public CellBlockReason NearestReason { get; private set; }

            /// <summary>Kolik z 8 sousedu te bunky je take <c>Blocked</c> (0..8). Skutecna hrana
            /// travy ma 3-5, izolovana skvrna 0-1 - tim se odlisi mapa od sumu v mape.</summary>
            public int NearestNeigh8 { get; private set; }

            /// <summary>Velikost SOUVISLE skvrny (4-okoli), do ktere ta bunka patri [bunky];
            /// pocita se do stropu, protoze u velke hrany je presne cislo k nicemu.</summary>
            public int NearestBlob { get; private set; }

            /// <summary>Sirka volneho kanalu napric drahou u robotu [m].</summary>
            public double ChannelM { get; private set; }

            public void Build(OccupancyGrid grid, ClearanceField field, LocalPlannerConfig cfg,
                              RegulatorWayPoint[] wps)
            {
                int n = wps.Length;
                var nodeS = new double[n];
                var nodeSample = new int[n];
                Sample(grid, field, cfg, wps, n, nodeS, nodeSample);

                if (Clearance.Length < n)
                {
                    Clearance = new float[n]; Closing = new float[n]; FreeAhead = new float[n];
                    VClearance = new float[n]; VBrake = new float[n];
                }

                // Frontier = arc-length prvni NE-sjizdne bunky od vzorku dopredu; inicializuje se na
                // konec drahy (za nim uz nic overeneho neni).
                int m = s.Count;
                var frontier = new double[m];
                var frontierState = new CellState[m];
                double f = nodeS[n - 1];
                var fs = CellState.Unknown;
                for (int i = m - 1; i >= 0; i--)
                {
                    if (!isFree[i]) { f = s[i]; fs = state[i]; }
                    frontier[i] = f; frontierState[i] = fs;
                }

                double tightest = double.MaxValue, leastFree = double.MaxValue;
                NearestReason = CellBlockReason.None;
                NearestNeigh8 = -1;
                NearestBlob = -1;
                FrontierState = CellState.Free;
                int tightSample = -1;

                for (int k = 0; k < n; k++)
                {
                    double sFrom = k > 0 ? nodeS[k - 1] : nodeS[0];
                    double sTo = k < n - 1 ? nodeS[k + 1] : nodeS[n - 1];

                    double clr = double.MaxValue, vEnv = double.MaxValue, clos = 0;
                    var nearest = CellBlockReason.None;
                    for (int i = 0; i < m; i++)
                    {
                        if (s[i] < sFrom || s[i] > sTo) continue;
                        if (clear[i] < clr) { clr = clear[i]; nearest = reason[i]; }
                        double ve = cfg.VEnvelope(clear[i], closing[i]);
                        if (ve < vEnv) { vEnv = ve; clos = closing[i]; }
                    }
                    if (clr == double.MaxValue)
                    {
                        int i = nodeSample[k];
                        clr = clear[i]; nearest = reason[i]; clos = closing[i];
                        vEnv = cfg.VEnvelope(clr, clos);
                    }

                    double freeAhead = Math.Max(0, frontier[nodeSample[k]] - nodeS[k]);

                    Clearance[k] = (float)clr;
                    Closing[k] = (float)clos;
                    FreeAhead[k] = (float)freeAhead;
                    VClearance[k] = (float)vEnv;
                    VBrake[k] = (float)cfg.VBrake(freeAhead);

                    if (k < n - 1)
                    {
                        if (clr < tightest) { tightest = clr; NearestReason = nearest; tightSample = nodeSample[k]; }
                        if (freeAhead < leastFree)
                        {
                            leastFree = freeAhead;
                            FrontierState = frontierState[nodeSample[k]];
                        }
                    }
                }

                // Je ta prekazka HRANA, nebo SKVRNA? Rozdil urcuje lecbu: hranu je potreba objet,
                // skvrnu odfiltrovat. Meri se na bunce, ktera odstup dava.
                if (tightSample >= 0 && nearestCell[tightSample] >= 0)
                {
                    int idx = nearestCell[tightSample];
                    NearestNeigh8 = Neigh8(grid, idx % grid.Size, idx / grid.Size);
                    NearestBlob = BlobSize(grid, idx % grid.Size, idx / grid.Size, 64);
                }

                ChannelM = Channel(grid, wps[0].X, wps[0].Y, wps[1].X - wps[0].X, wps[1].Y - wps[0].Y);
            }

            private void Sample(OccupancyGrid grid, ClearanceField field, LocalPlannerConfig cfg,
                                RegulatorWayPoint[] wps, int n, double[] nodeS, int[] nodeSample)
            {
                s.Clear(); clear.Clear(); closing.Clear(); isFree.Clear(); state.Clear();
                reason.Clear(); nearestCell.Clear();

                double step = grid.Resolution * 0.5;
                double acc = 0, ux = 1, uy = 0;
                for (int k = 0; k < n; k++)
                {
                    double dx = 0, dy = 0, len = 0;
                    if (k < n - 1)
                    {
                        dx = wps[k + 1].X - wps[k].X;
                        dy = wps[k + 1].Y - wps[k].Y;
                        len = Math.Sqrt(dx * dx + dy * dy);
                        if (len > 0) { ux = dx / len; uy = dy / len; }
                    }

                    nodeS[k] = acc;
                    nodeSample[k] = s.Count;
                    Add(grid, field, cfg, wps[k].X, wps[k].Y, acc, ux, uy);
                    if (k == n - 1) break;

                    int steps = Math.Max(1, (int)Math.Ceiling(len / step));
                    for (int q = 1; q < steps; q++)
                    {
                        double t = (double)q / steps;
                        Add(grid, field, cfg, wps[k].X + dx * t, wps[k].Y + dy * t, acc + len * t, ux, uy);
                    }
                    acc += len;
                }
            }

            private void Add(OccupancyGrid grid, ClearanceField field, LocalPlannerConfig cfg,
                             double x, double y, double arc, double dirX, double dirY)
            {
                int i = grid.CellX(x) - grid.OriginX;
                int j = grid.CellY(y) - grid.OriginY;
                bool inside = (uint)i < (uint)grid.Size && (uint)j < (uint)grid.Size;

                double d = inside ? field.DistanceLocal(i, j) : 0.0;
                var st = inside ? grid.StateAt(grid.LocalIndex(i, j)) : CellState.Unknown;

                s.Add(arc);
                clear.Add(d);
                closing.Add(inside ? ClosingAt(field, i, j, dirX, dirY) : 1.0);
                state.Add(st);

                // Duvod se bere z bunky, ve ktere pole odstupu ma nulu, tedy z nejblizsi Blocked
                // v okoli - ta pod vzorkem blokovana byt nemusi.
                int near = !inside ? -1
                         : st == CellState.Blocked ? i + j * grid.Size
                         : NearestBlockedCell(grid, field, i, j);
                nearestCell.Add(near);
                reason.Add(near < 0 ? CellBlockReason.None
                         : grid.BlockReasonAt(grid.LocalIndex(near % grid.Size, near / grid.Size)));

                double footprint = Math.Min(cfg.FootprintRadiusM, cfg.SafeDist);
                isFree.Add(inside && (st == CellState.Free
                                      || (arc < footprint && st != CellState.Blocked)));
            }

            /// <summary>
            /// NEJBLIZSI neprujezdna bunka (lokalni linearni index <c>i + j*Size</c>), nebo -1.
            /// Pole odstupu vzdalenost zna, ale ne SMER - hleda se proto v kruhu o te vzdalenosti
            /// (± bunka) prvni <c>Blocked</c>.
            /// </summary>
            private static int NearestBlockedCell(OccupancyGrid grid, ClearanceField field, int i, int j)
            {
                int rad = (int)Math.Round(field.DistanceLocal(i, j) / grid.Resolution);
                if (rad > 40) return -1;          // daleko = odstup nikoho nevaze
                for (int dj = -rad - 1; dj <= rad + 1; dj++)
                    for (int di = -rad - 1; di <= rad + 1; di++)
                    {
                        int ii = i + di, jj = j + dj;
                        if ((uint)ii >= (uint)grid.Size || (uint)jj >= (uint)grid.Size) continue;
                        if (di * di + dj * dj > (rad + 1) * (rad + 1)) continue;
                        if (grid.StateAt(grid.LocalIndex(ii, jj)) != CellState.Blocked) continue;
                        return ii + jj * grid.Size;
                    }
                return -1;
            }

            /// <summary>Kolik z 8 sousedu je take <c>Blocked</c> (0..8).</summary>
            private static int Neigh8(OccupancyGrid grid, int i, int j)
            {
                int n = 0;
                for (int dj = -1; dj <= 1; dj++)
                    for (int di = -1; di <= 1; di++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int ii = i + di, jj = j + dj;
                        if ((uint)ii >= (uint)grid.Size || (uint)jj >= (uint)grid.Size) continue;
                        if (grid.StateAt(grid.LocalIndex(ii, jj)) == CellState.Blocked) n++;
                    }
                return n;
            }

            /// <summary>
            /// Velikost SOUVISLE skvrny <c>Blocked</c> (4-okoli), do ktere bunka patri - do stropu
            /// <paramref name="cap"/>. Vic uz stejne znamena "hrana", takze presne cislo je k nicemu
            /// a strop drzi cenu vypoctu.
            /// </summary>
            private static int BlobSize(OccupancyGrid grid, int i, int j, int cap)
            {
                var seen = new HashSet<int>();
                var q = new Queue<int>();
                q.Enqueue(i + j * grid.Size);
                seen.Add(i + j * grid.Size);
                int n = 0;
                while (q.Count > 0 && n < cap)
                {
                    int c = q.Dequeue();
                    n++;
                    int ci = c % grid.Size, cj = c / grid.Size;
                    for (int k = 0; k < 4; k++)
                    {
                        int ii = ci + (k == 0 ? 1 : k == 1 ? -1 : 0);
                        int jj = cj + (k == 2 ? 1 : k == 3 ? -1 : 0);
                        if ((uint)ii >= (uint)grid.Size || (uint)jj >= (uint)grid.Size) continue;
                        int idx = ii + jj * grid.Size;
                        if (seen.Contains(idx)) continue;
                        if (grid.StateAt(grid.LocalIndex(ii, jj)) != CellState.Blocked) continue;
                        seen.Add(idx);
                        q.Enqueue(idx);
                    }
                }
                return n;
            }

            /// <summary>Priblizovani k prekazce: zaporny prumet smeru drahy do gradientu pole odstupu
            /// (replika <c>LocalPathPlanner.Closing</c>).</summary>
            private static double ClosingAt(ClearanceField field, int i, int j, double dirX, double dirY)
            {
                int size = field.Size;
                int ip = i + 1 < size ? i + 1 : i, im = i > 0 ? i - 1 : i;
                int jp = j + 1 < size ? j + 1 : j, jm = j > 0 ? j - 1 : j;
                double res = field.Resolution;
                double gx = ip == im ? 0
                          : (field.DistanceLocal(ip, j) - field.DistanceLocal(im, j)) / ((ip - im) * res);
                double gy = jp == jm ? 0
                          : (field.DistanceLocal(i, jp) - field.DistanceLocal(i, jm)) / ((jp - jm) * res);
                double c = -(dirX * gx + dirY * gy);
                if (!(c > 0)) return 0.0;
                return c > 1.0 ? 1.0 : c;
            }

            /// <summary>
            /// Sirka VOLNEHO KANALU napric drahou u robotu [m]: soucet vzdalenosti k prvni
            /// <c>Blocked</c> bunce vlevo a vpravo po normale ke smeru jizdy (strop 6 m).
            ///
            /// <para><b>Nacpak.</b> Odstup sam nerozlisi "cesta je uzka" od "prekazka je podel
            /// drahy o dva metry dal". Kanal merenej U ROBOTU to oddeli a da se srovnat se sirkou
            /// koridoru z kamer (<c>FreeRunMsg.Width</c>), tedy s nezavislym merenim teze cesty.</para>
            /// </summary>
            private static double Channel(OccupancyGrid grid, double x, double y, double dx, double dy)
            {
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (!(len > 0)) return double.NaN;
                double nx = -dy / len, ny = dx / len;      // normala vlevo
                return Side(grid, x, y, nx, ny) + Side(grid, x, y, -nx, -ny);
            }

            private static double Side(OccupancyGrid grid, double x, double y, double nx, double ny)
            {
                double step = grid.Resolution * 0.5;
                for (double d = 0; d <= 6.0; d += step)
                {
                    int i = grid.CellX(x + nx * d) - grid.OriginX;
                    int j = grid.CellY(y + ny * d) - grid.OriginY;
                    if ((uint)i >= (uint)grid.Size || (uint)j >= (uint)grid.Size) return d;
                    if (grid.StateAt(grid.LocalIndex(i, j)) == CellState.Blocked) return d;
                }
                return 6.0;
            }
        }

        private sealed class Row
        {
            public double T;
            public LocalPlanStatus Status;
            public int Nodes;
            public double Clearance;
            public double FreeAhead;
            public double VClear;
            public double VBrake;
            public double Speed;          // nejnizsi mezilehly uzel
            public double Node0Speed;     // co robot dostane HNED u sebe
            public double BindAtM;        // vzdalenost vazajiciho uzlu od robotu
            public double PathLenM;
            public double VCmd = double.NaN;
            public bool Estop;
            public Binder Bind;
            public CellState Frontier;
            public CellBlockReason NearestReason;
            public double ChannelM = double.NaN;
            public double CamWidthM = double.NaN;
            public int NearestBlob = -1;      // kolik Blocked sousedu ma bunka, ktera dava odstup
            public int NearestNeigh8 = -1;
        }
    }
}
