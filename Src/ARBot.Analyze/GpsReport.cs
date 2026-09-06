using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Runtime;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Proč se stojícímu robotu hýbe poloha — a co s tím.</b> Měření, které má rozhodnout
    /// mezi několika léčbami, ne je ilustrovat.
    ///
    /// <para><b>Nač to je.</b> Autor pozoroval, že při poklesu DOP se u <b>stojícího</b> robota
    /// začne aktualizovat poloha, tím se posouvá world-kotvený occupancy grid a v lokální mapě
    /// vznikají artefakty. Kandidátů na příčinu je víc a každý chce jinou opravu; tenhle report
    /// ke každému vydá číslo a k němu prahovou hodnotu, která rozhoduje.</para>
    ///
    /// <para><b>Klíčový trik: stojící robot dává pravdu zadarmo.</b> Skutečná poloha je po dobu
    /// stání <b>konstanta</b> (neznámá, ale konstantní), takže odchylka fixu od průměru segmentu
    /// je čistá chyba GPS — bez jakékoli ground truth. Totéž platí pro odhad fúze: cokoli, co se
    /// v něm za tu dobu pohne, je chyba.</para>
    ///
    /// <para><b>Stání se pozná z ENKODÉRŮ</b> (kumulativní ujetá dráha kol v <c>MotorStateBase</c>),
    /// ne z <c>V</c> ve stavu fúze — to je odhad, tedy právě ta veličina, kterou měříme. Bez toho
    /// by bylo celé měření kruhové.</para>
    ///
    /// <para>Bloky odpovídají plánu měření z doc/occupancy-and-local-planning.md.</para>
    /// </summary>
    public static class GpsReport
    {
        /// <summary>Kolik smí ujet kolo, aby to ještě bylo stání [m] — výchozí, jde přebít.</summary>
        private const double DefaultStandTolM = 0.005;

        /// <summary>Nejkratší použitelný segment stání [s].</summary>
        private const double DefaultMinStandS = 20.0;

        private const double EarthR = 6378137.0;

        public static void Run(RecordFile rec, double minStandS, double gpsMaxDop, int gpsMinSat,
                               double standTolM, double minJizdaS, double minDrahaM)
        {
            if (minStandS <= 0) minStandS = DefaultMinStandS;
            if (standTolM <= 0) standTolM = DefaultStandTolM;

            var cfg = new FusionConfig();
            if (gpsMaxDop > 0) cfg.GpsMaxDop = gpsMaxDop;
            if (gpsMinSat > 0) cfg.GpsMinSatellites = gpsMinSat;

            var st = new List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)>();
            var gps = new List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)>();
            var enc = new List<(double T, double S)>();
            // Leve a prave kolo zvlast: z jejich rozdilu se dela kurz mrtveho odhadu. Kurz z fuze
            // se pouzit NESMI - obsahuje kompas, ktery byl 6. 9. o 59 stupnu vedle, a hlavne
            // obsahuje GPS, tedy prave to, co se meri.
            var encLR = new List<(double T, double L, double R)>();
            var plan = new List<(double T, int Status, double Clearance)>();
            var grid = new List<(double T, int Blocked, double Mass)>();

            DateTime t0 = DateTime.MinValue;
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "RobotStateMsg" && e.MsgName != "GPSState" && e.MsgName != "MotorStateBase"
                    && e.MsgName != "LocalPlanMsg" && e.MsgName != "OccupancyGridMsg") continue;
                var msg = rec.Read(e);
                switch (msg)
                {
                    case RobotStateMsg r:
                        st.Add((Sec(r.TimeStamp, ref t0), r.X, r.Y, r.Theta, r.V, r.Omega,
                                r.Covariance != null ? r.Covariance[0, 0] : double.NaN,
                                r.Covariance != null ? r.Covariance[1, 1] : double.NaN));
                        break;
                    case GPSState p:
                        // Branu pocitame TOUZ funkci, jakou pouziva fuze i stranka - jinak by
                        // report rikal neco jineho, nez co robot delal.
                        gps.Add((Sec(p.TimeStamp, ref t0), p.Latitude, p.Longitude, p.Hdop,
                                 p.NumberOfSatellites,
                                 DefaultMeasurementMapper.PositionRejectReason(p, cfg),
                                 DefaultMeasurementMapper.PositionStd(p, cfg)));
                        break;
                    case MotorStateBase m:
                        double tm = Sec(m.TimeStamp, ref t0);
                        enc.Add((tm, 0.5 * (m.LeftEncoder + m.RightEncoder)));
                        encLR.Add((tm, m.LeftEncoder, m.RightEncoder));
                        break;
                    case LocalPlanMsg lp:
                        plan.Add((Sec(lp.TimeStamp, ref t0), lp.Status, lp.MinClearanceM));
                        break;
                    case OccupancyGridMsg og:
                        grid.Add((Sec(og.TimeStamp, ref t0), Blocked(og), Mass(og)));
                        break;
                }
            }

            Console.WriteLine($"RobotStateMsg {st.Count}, GPSState {gps.Count}, MotorStateBase {enc.Count}, "
                              + $"LocalPlanMsg {plan.Count}, OccupancyGridMsg {grid.Count}");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "brana pouzita pri rozboru: gpsminsat={0}, gpsmaxdop={1:F1}, GpsPosStd={2:F2} m, skalovani DOP={3}",
                cfg.GpsMinSatellites, cfg.GpsMaxDop, cfg.GpsPosStd, cfg.GpsScaleStdByDop));
            Console.WriteLine("  ⚠️ Beh mohl mit jine parametry - v zaznamu jsou v ucinne konfiguraci (prikaz 'log').");
            Console.WriteLine();

            if (st.Count < 10 || enc.Count < 10)
            {
                Console.WriteLine("Zaznam nenese stav robota nebo enkodery - nelze merit.");
                return;
            }

            var stani = Stani(enc, minStandS, standTolM);
            Console.WriteLine($"SEGMENTY STANI (z enkoderu, tolerance {standTolM * 1000:F0} mm, "
                              + $"nejkratsi {minStandS:F0} s): {stani.Count}");
            foreach (var (a, b) in stani)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,7:F1} - {1,7:F1} s  ({2,6:F1} s)", a, b, b - a));
            Console.WriteLine();
            if (stani.Count == 0)
            {
                // Neni-li co merit, report musi rict PROC - jinak je "0 segmentu" k nerozeznani
                // od vady nastroje. Vypise se, jak daleko se kola v oknech dane delky posunula:
                // par milimetru = sum enkoderu (staci povolit --standtol), metry = robot jel.
                PrecStani(enc, minStandS);
                Console.WriteLine("Bloky A1, A2 a A4 se preskakuji (potrebuji stani); A2b a A5 bezi dal.");
                Console.WriteLine();
            }
            else
            {
                A1(st, gps, stani);
                A2(gps, stani);
            }

            A2b(gps, encLR, minJizdaS, minDrahaM, stani);
            A5(st);
            if (stani.Count > 0) A4(st, gps, stani, plan, grid);
        }

        // ---------------------------------------------------------------------------------
        // A1: tahne GPS stojiciho robota, a jak silne?
        // ---------------------------------------------------------------------------------
        private static void A1(List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)> st,
                               List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)> gps,
                               List<(double A, double B)> stani)
        {
            Console.WriteLine("A1) TAHNE GPS STOJICIHO ROBOTA?");

            var drift = new Stats("ujeta draha ODHADU za 60 s stani");
            var sigX = new Stats("hlasena sigma polohy z fuze (sqrt P_xx)");
            var sigGps = new Stats("sigma GPS pouzita fuzi (1,5 x DOP)");
            // Regrese kroku odhadu na innovaci: dX = K*(fix - X) + K*c. Smernice je EFEKTIVNI
            // Kalmanovo zesileni, usek pohlti neznamy posun mezi nasi a runtimovou ENU.
            var ux = new List<double>(); var dxs = new List<double>();
            var uy = new List<double>(); var dys = new List<double>();

            foreach (var (a, b) in stani)
            {
                var stSeg = st.Where(s => s.T >= a && s.T <= b).OrderBy(s => s.T).ToList();
                if (stSeg.Count < 5) continue;

                double draha = 0;
                for (int i = 1; i < stSeg.Count; i++)
                    draha += Math.Sqrt(Sq(stSeg[i].X - stSeg[i - 1].X) + Sq(stSeg[i].Y - stSeg[i - 1].Y));
                drift.Add(draha * 60.0 / Math.Max(1e-9, b - a));

                foreach (var s in stSeg)
                {
                    if (!double.IsNaN(s.Pxx) && s.Pxx > 0) sigX.Add(Math.Sqrt(s.Pxx));
                    if (!double.IsNaN(s.Pyy) && s.Pyy > 0) sigX.Add(Math.Sqrt(s.Pyy));
                }

                // Lokalni ENU segmentu: kotva v prvnim fixu segmentu (posun je konstanta,
                // takze regresi nevadi - pohlti ho usek).
                var fixy = gps.Where(g => g.T >= a && g.T <= b && g.Reject == null).OrderBy(g => g.T).ToList();
                if (fixy.Count < 5) continue;
                double lat0 = fixy[0].Lat, lon0 = fixy[0].Lon;
                foreach (var g in fixy) sigGps.Add(g.Std);

                for (int i = 1; i < stSeg.Count; i++)
                {
                    if (!TryNearest(fixy.Select(g => (g.T, 0.0)).ToList(), stSeg[i - 1].T, 0.3, out _)) continue;
                    var g = fixy.OrderBy(x => Math.Abs(x.T - stSeg[i - 1].T)).First();
                    double fe = EarthR * Math.Cos(lat0) * (g.Lon - lon0);
                    double fn = EarthR * (g.Lat - lat0);

                    ux.Add(fe - stSeg[i - 1].X); dxs.Add(stSeg[i].X - stSeg[i - 1].X);
                    uy.Add(fn - stSeg[i - 1].Y); dys.Add(stSeg[i].Y - stSeg[i - 1].Y);
                }
            }

            if (drift.Count > 0) Console.WriteLine("  " + drift.Line("m/min"));
            if (sigX.Count > 0) Console.WriteLine("  " + sigX.Line("m"));
            if (sigGps.Count > 0) Console.WriteLine("  " + sigGps.Line("m"));

            if (ux.Count >= 20)
            {
                var (kx, r2x) = Regrese(ux, dxs);
                var (ky, r2y) = Regrese(uy, dys);
                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  EFEKTIVNI KALMANOVO ZESILENI (regrese kroku odhadu na innovaci, n={0}):", ux.Count));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    K_vychod = {0:F4}  (R2 {1:F2})", kx, r2x));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    K_sever  = {0:F4}  (R2 {1:F2})", ky, r2y));

                // Predpovezene zesileni z hlasene P a pouzite sigmy - kdyz sedi na namerene,
                // je model pochopeny a da se s nim pocitat.
                if (sigX.Count > 0 && sigGps.Count > 0)
                {
                    double p = Sq(sigX.Median), r = Sq(sigGps.Median);
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    pro srovnani P/(P+R) z hlasenych cisel = {0:F4}", p / (p + r)));
                }

                double k = 0.5 * (Math.Abs(kx) + Math.Abs(ky));

                // ⚠️ NA SUROVE K SE PRAH DAT NEDA - je to zesileni NA JEDNU OPRAVU, takze jeho
                // vyznam zavisi na kadenci. Puvodni kriterium "K >= 0,05" bylo takhle spatne
                // skalovane: namereno K ~ 0,002, coz podle nej znamena "GPS netahne", ale pri
                // 10 oprav za sekundu je to casova konstanta 43 s - a odhad se za tu dobu
                // rozjede o metry. Rozhoduje TAU, ne K.
                double f = Kadence(gps, stani);
                double tau = (k > 1e-9 && f > 0) ? 1.0 / (k * f) : double.NaN;
                double nejdelsiStani = stani.Max(x => x.B - x.A);

                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  kadence oprav {0:F1} Hz  =>  CASOVA KONSTANTA nasledovani GPS tau = {1:F0} s",
                    f, tau));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  nejdelsi segment stani: {0:F0} s", nejdelsiStani));
                if (!double.IsNaN(tau))
                    Console.WriteLine(tau < nejdelsiStani
                        ? "  => tau je KRATSI nez doba stani: odhad se za tu dobu na GPS stihne dotahnout,"
                          + " tedy GPS ma nad stojicim robotem autoritu."
                        : "  => tau je delsi nez doba stani: GPS odhad za tu dobu vyrazne nepretahne.");

                if (sigX.Count > 0 && drift.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  ⚠️ POCTIVOST P: filtr hlasi sigmu polohy {0:F3} m, a pritom se odhad"
                        + " stojiciho robota pohybuje o {1:F1} m/min.",
                        sigX.Median, drift.Median));
                    Console.WriteLine("     To nesedi ani radove - hlasena nejistota neni poctiva.");
                }
            }
            else
            {
                Console.WriteLine("  Malo parovanych vzorku na regresi zesileni.");
            }
            Console.WriteLine();
        }

        /// <summary>Prumerna kadence fixu v segmentech stani [Hz] — bez ni nejde K prevest na tau.</summary>
        private static double Kadence(List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)> gps,
                                      List<(double A, double B)> stani)
        {
            int n = 0; double doba = 0;
            foreach (var (a, b) in stani)
            {
                int c = gps.Count(g => g.T >= a && g.T <= b);
                if (c < 2) continue;
                n += c; doba += b - a;
            }
            return doba > 0 ? n / doba : 0;
        }

        // ---------------------------------------------------------------------------------
        // A2: je chyba GPS casove korelovana?
        // ---------------------------------------------------------------------------------
        private static void A2(List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)> gps,
                               List<(double A, double B)> stani)
        {
            Console.WriteLine("A2) JE CHYBA GPS CASOVE KORELOVANA? (stojici robot = pravda je konstanta)");

            // Odchylky od prumeru segmentu, v metrech. Bere se VSE, i odmitnute - brana je
            // jina otazka a jeji vliv resi A4.
            //
            // POZOR: DRZI SE PO SEGMENTECH, ne v jednom seznamu. Prvni verze je slila dohromady,
            // takze bloky i lagy prekracovaly MEZERU mezi segmenty (tady 32 s, kdy robot jel) -
            // a pocitaly korelaci mezi vzorky, ktere spolu casove nesousedi.
            var dev = new List<double>();
            var segE = new List<List<double>>();
            var segN = new List<List<double>>();
            double dt = 0.2;
            double dobaCelkem = 0;

            foreach (var (a, b) in stani)
            {
                var f = gps.Where(g => g.T >= a && g.T <= b).OrderBy(g => g.T).ToList();
                if (f.Count < 30) continue;
                double lat0 = f.Average(x => x.Lat), lon0 = f.Average(x => x.Lon);
                var e1 = new List<double>(); var n1 = new List<double>();
                foreach (var g in f)
                {
                    double e = EarthR * Math.Cos(lat0) * (g.Lon - lon0);
                    double n = EarthR * (g.Lat - lat0);
                    e1.Add(e); n1.Add(n); dev.Add(Math.Sqrt(e * e + n * n));
                }
                segE.Add(e1); segN.Add(n1);
                dobaCelkem += b - a;
                if (f.Count > 2) dt = (f[f.Count - 1].T - f[0].T) / (f.Count - 1);
            }

            int celkem = segE.Sum(x => x.Count);
            if (celkem < 30)
            {
                Console.WriteLine("  Malo fixu v segmentech stani.");
                Console.WriteLine();
                return;
            }

            var rozptyl = new Stats("odchylka fixu od prumeru segmentu");
            foreach (var d in dev) rozptyl.Add(d);
            Console.WriteLine("  " + rozptyl.Line("m"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  vzorku {0} ve {1} segmentech, perioda {2:F2} s, celkem {3:F0} s stani",
                celkem, segE.Count, dt, dobaCelkem));

            Console.WriteLine("  autokorelace odchylky (prumer obou os):");
            double tDecorr = double.NaN;
            // Sweep musi dosahnout DAL nez ocekavany T_d, jinak report skonci na "delsi nez merene
            // okno" a nic nerekne. Prvni verze koncila na 250 vzorcich (25 s), kde je rho 0,38 -
            // tesne NAD 1/e, takze odpoved lezela hned za koncem tabulky. Strop drzi az podminka
            // lag < delka segmentu / 3.
            foreach (int lag in new[] { 1, 5, 10, 25, 50, 100, 150, 250, 400, 600, 800 })
            {
                if (lag >= segE.Max(x => x.Count) / 3) break;
                double r = 0.5 * (Autokorelace(segE, lag) + Autokorelace(segN, lag));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    tau = {0,6:F1} s   rho = {1,6:F2}", lag * dt, r));
                if (double.IsNaN(tDecorr) && r < 1.0 / Math.E) tDecorr = lag * dt;
            }
            Console.WriteLine(double.IsNaN(tDecorr)
                ? "  => rho neklesla pod 1/e ani na nejdelsim lagu: dekorelacni cas je DELSI nez merene okno."
                : string.Format(CultureInfo.InvariantCulture,
                    "  => dekorelacni cas T_d ~ {0:F1} s", tDecorr));

            // Dve meze presnosti, ktere se musi rict, aby se to cislo necetlo prisneji, nez unese:
            // 1) Odchylky se berou od PRUMERU SEGMENTU, cimz se odecte stejnosmerna slozka a
            //    autokorelace na dlouhych lagach se umele srazi - T_d je proto SPODNI odhad.
            // 2) Nezavislych vzorku je jen doba stani / T_d, tedy jich muze byt par.
            if (!double.IsNaN(tDecorr))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "     POZOR: je to SPODNI odhad (odectenim prumeru segmentu se odecte i"
                    + " stejnosmerna slozka) a stoji jen na ~{0:F0} nezavislych vzorcich"
                    + " ({1:F0} s / {2:F0} s).", dobaCelkem / tDecorr, dobaCelkem, tDecorr));

            Console.WriteLine();
            Console.WriteLine("  PRUMEROVACI KRIVKA (klesa-li jako 1/sqrt(N), jsou vzorky nezavisle):");
            // N = 1 je NORMALIZACNI BOD, ne mereni: blok o jednom vzorku je ten vzorek sam,
            // takze sd "prumeru" je sd jednotlivych fixu a cinitel vyjde 1,00 z definice.
            // Informativni jsou az radky N >= 2.
            double sd1 = 0.5 * (Sd(segE) + Sd(segN));
            foreach (int n in new[] { 1, 2, 5, 10, 25, 50, 100 })
            {
                if (n > segE.Max(x => x.Count) / 4) break;
                double sdN = 0.5 * (SdBlokovehoPrumeru(segE, n) + SdBlokovehoPrumeru(segN, n));
                string popis = n == 1 ? "  <- jednotlive fixy (normalizace, ne mereni)" : "";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    N = {0,4} ({1,6:F1} s)   sd prumeru = {2,6:F3} m   kdyby byly nezavisle: {3,6:F3} m   "
                    + "cinitel nadsazeni {4,5:F2}x{5}",
                    n, n * dt, sdN, sd1 / Math.Sqrt(n), sdN / Math.Max(1e-9, sd1 / Math.Sqrt(n)), popis));
            }
            Console.WriteLine();
            Console.WriteLine("  => Cinitel vyrazne nad 1 znamena, ze filtr povazuje za nezavisla mereni,");
            Console.WriteLine("     ktera nezavisla nejsou - tedy stejna past jako MinPeriod u MapCorrelatoru.");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------------------------
        // A2b: dekorelacni cas chyby GPS ZA JIZDY
        // ---------------------------------------------------------------------------------

        /// <summary>
        /// <b>Platí ten dekorelační čas i za jízdy?</b> A2 ho měří při stání, kde je pravda
        /// konstanta. Za jízdy konstanta není — ale je tu <b>odometrie</b>, takže se dá udělat
        /// totéž proti ní.
        ///
        /// <para><b>Proč na tom záleží.</b> Multipath závisí na tom, co je kolem antény; když
        /// robot jede, prostředí se mění a chyba se dekoreluje <b>rychleji</b>. Decimovat za jízdy
        /// na periodu naměřenou při stání by pak zahazovalo skutečnou informaci.</para>
        ///
        /// <para><b>Postup.</b> Z enkodérů se sestaví mrtvý odhad (<c>Δs</c> z průměru kol,
        /// <c>Δθ</c> z jejich rozdílu), <b>tuze se zarovná na dráhu z GPS</b> (2D Procrustes:
        /// jedna rotace a posun na celý úsek) a zbytek je <c>chyba GPS − drift odometrie</c>.
        /// Jeho autokorelace dá dekorelační čas.</para>
        ///
        /// <para><b>Kurz se bere z KOL, ne z fúze.</b> Fúzní kurz obsahuje kompas (6. 9. o 59°
        /// vedle) i GPS, tedy právě to, co se měří — bylo by to kruhové. Absolutní orientaci
        /// mrtvého odhadu proto neznáme a doplní ji až to zarovnání.</para>
        ///
        /// <para>⚠️ <b>Dvě meze, které z toho dělají SPODNÍ odhad.</b> Tuhé zarovnání odečte
        /// střední hodnotu i celkové natočení, tedy nejnižší frekvence — táž past jako odečtení
        /// průměru segmentu v A2, jen silnější. A zbytek <b>míchá chybu GPS s driftem odometrie</b>,
        /// takže jeho velikost je horní mez obojího, ne měření jednoho z nich.</para>
        /// </summary>
        private static void A2b(List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)> gps,
                                List<(double T, double L, double R)> enc, double minJizdaS, double minDrahaM,
                                List<(double A, double B)> stani)
        {
            double MinJizdaS = minJizdaS > 0 ? minJizdaS : 60.0;
            double MinDrahaM = minDrahaM > 0 ? minDrahaM : 20.0;
            double rozchod = ARBot.Common.Configuration.Profile.Rozchod;

            Console.WriteLine("A2b) DEKORELACNI CAS CHYBY GPS ZA JIZDY (proti mrtvemu odhadu z kol)");
            var jizdy = Jizdy(enc, MinJizdaS);
            if (jizdy.Count == 0)
            {
                Console.WriteLine($"  Zadny souvisly usek jizdy delsi nez {MinJizdaS:F0} s - nelze merit.");
                var vsechny = Jizdy(enc, 0);
                if (vsechny.Count > 0)
                {
                    var d = new Stats("delky nalezenych useku jizdy");
                    foreach (var (a2, b2) in vsechny) d.Add(b2 - a2);
                    Console.WriteLine("  " + d.Line("s"));
                }
                Console.WriteLine();
                return;
            }
            Console.WriteLine($"  useku jizdy nad {MinJizdaS:F0} s: {jizdy.Count}");

            var segE = new List<List<double>>();
            var segN = new List<List<double>>();
            var zbytek = new Stats("zbytek po zarovnani (chyba GPS + drift odometrie)");
            double dt = 0.1, dobaCelkem = 0, drahaCelkem = 0;
            int pouzito = 0;

            // Tabulka useku i s duvodem zamitnuti: bez ni je "nelze merit" k nerozeznani od vady
            // nastroje, a prave tady je podstatne vedet, CO v datech chybi.
            Console.WriteLine("  usek [s]        doba    draha   fixu   verdikt");
            foreach (var (a, b) in jizdy)
            {
                var e = enc.Where(x => x.T >= a && x.T <= b).OrderBy(x => x.T).ToList();
                var f = gps.Where(g => g.T >= a && g.T <= b).OrderBy(g => g.T).ToList();

                double draha = 0;
                for (int i = 1; i < e.Count; i++)
                    draha += Math.Abs(0.5 * ((e[i].L - e[i - 1].L) + (e[i].R - e[i - 1].R)));

                string verdikt = e.Count < 20 ? "malo vzorku enkoderu"
                               : f.Count < 60 ? $"malo fixu ({f.Count} < 60)"
                               : draha < MinDrahaM ? $"kratka draha ({draha:F1} < {MinDrahaM:F0} m)"
                               : "POUZITO";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0}-{1,6:F0}  {2,6:F1} s  {3,6:F1} m  {4,5}   {5}",
                    a, b, b - a, draha, f.Count, verdikt));
                if (verdikt != "POUZITO") continue;

                // Mrtvy odhad z kol: pocatek (0,0,0), kurz z rozdilu kol.
                var dr = new List<(double T, double X, double Y)>();
                double x = 0, y = 0, th = 0;
                dr.Add((e[0].T, 0, 0));
                for (int i = 1; i < e.Count; i++)
                {
                    double dl = e[i].L - e[i - 1].L, drr = e[i].R - e[i - 1].R;
                    double ds = 0.5 * (dl + drr), dth = (drr - dl) / rozchod;
                    x += ds * Math.Cos(th + dth / 2);
                    y += ds * Math.Sin(th + dth / 2);
                    th += dth;
                    dr.Add((e[i].T, x, y));
                }

                // GPS v lokalni ENU kotvene na zacatku useku + vzorkovany mrtvy odhad v tychz casech.
                double lat0 = f[0].Lat, lon0 = f[0].Lon;
                var A = new List<(double X, double Y)>();   // mrtvy odhad
                var B = new List<(double X, double Y)>();   // GPS
                foreach (var g in f)
                {
                    if (!Interpoluj(dr, g.T, out double dx, out double dy)) continue;
                    A.Add((dx, dy));
                    B.Add((EarthR * Math.Cos(lat0) * (g.Lon - lon0), EarthR * (g.Lat - lat0)));
                }
                if (A.Count < 60) continue;

                // 2D Procrustes: jedna rotace + posun, ktere nejlip prilozi mrtvy odhad na GPS.
                double ax = A.Average(v => v.X), ay = A.Average(v => v.Y);
                double bx = B.Average(v => v.X), by = B.Average(v => v.Y);
                double sxx = 0, sxy = 0;
                for (int i = 0; i < A.Count; i++)
                {
                    double px = A[i].X - ax, py = A[i].Y - ay;
                    double qx = B[i].X - bx, qy = B[i].Y - by;
                    sxx += px * qx + py * qy;
                    sxy += px * qy - py * qx;
                }
                double fi = Math.Atan2(sxy, sxx);
                double c = Math.Cos(fi), sn = Math.Sin(fi);

                var re = new List<double>(); var rn = new List<double>();
                for (int i = 0; i < A.Count; i++)
                {
                    double px = A[i].X - ax, py = A[i].Y - ay;
                    double rx = (B[i].X - bx) - (c * px - sn * py);
                    double ry = (B[i].Y - by) - (sn * px + c * py);
                    re.Add(rx); rn.Add(ry);
                    zbytek.Add(Math.Sqrt(rx * rx + ry * ry));
                }
                segE.Add(re); segN.Add(rn);
                dobaCelkem += b - a;
                drahaCelkem += draha;
                pouzito++;
                if (f.Count > 2) dt = (f[f.Count - 1].T - f[0].T) / (f.Count - 1);
            }

            if (pouzito == 0)
            {
                Console.WriteLine();
                Console.WriteLine("  => NA TOHLE MERENI NEJSOU V ZAZNAMU DATA. Neni to vada nastroje ani");
                Console.WriteLine("     vysledek 'chyba nekoreluje' - proste tu neni dost souvisle jizdy.");
                Console.WriteLine("     Aby to slo zmerit, je potreba usek, ktery je DELSI NEZ ocekavany T_d");
                Console.WriteLine("     (pri stani vyslo ~40 s), tedy radove 5-10 minut jizdy bez zastaveni.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  useku {0}, celkem {1:F0} s jizdy a {2:F0} m drahy, perioda fixu {3:F2} s",
                pouzito, dobaCelkem, drahaCelkem, dt));
            Console.WriteLine("  " + zbytek.Line("m"));

            Console.WriteLine("  autokorelace zbytku (prumer obou os):");
            double tDecorr = double.NaN;
            int maxLag = segE.Max(v => v.Count) / 3;
            foreach (int lag in new[] { 1, 5, 10, 25, 50, 100, 150, 250, 400, 600, 800 })
            {
                if (lag >= maxLag) break;
                double r = 0.5 * (Autokorelace(segE, lag) + Autokorelace(segN, lag));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    tau = {0,6:F1} s   rho = {1,6:F2}", lag * dt, r));
                if (double.IsNaN(tDecorr) && r < 1.0 / Math.E) tDecorr = lag * dt;
            }

            Console.WriteLine();
            if (double.IsNaN(tDecorr))
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => rho neklesla pod 1/e ani na {0:F0} s: T_d za jizdy je DELSI nez to, co jde"
                    + " z techto dat rict.", maxLag * dt));
            }
            else
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => T_d za jizdy ~ {0:F1} s   (nezavislych vzorku ~{1:F0})",
                    tDecorr, dobaCelkem / tDecorr));
                if (dobaCelkem / tDecorr < 10)
                    Console.WriteLine("  ⚠️ Pod deseti nezavislymi vzorky je to orientacni cislo, ne mereni.");
            }
            Console.WriteLine("  ⚠️ SPODNI odhad: tuhe zarovnani odecte stredni hodnotu i celkove natoceni,");
            Console.WriteLine("     tedy nejnizsi frekvence. A zbytek micha chybu GPS s driftem odometrie,");
            Console.WriteLine("     takze jeho VELIKOST je horni mez obojiho, ne mereni jednoho z nich.");
            Console.WriteLine();

            KontrolaOknem(gps, stani, dobaCelkem / Math.Max(1, pouzito), dt, tDecorr);
        }

        /// <summary>
        /// <b>Není to jen artefakt délky okna?</b> Kritická kontrola k A2b: krátký úsek jízdy
        /// odečtením střední hodnoty (a u A2b i celkového natočení) <b>smaže všechno pomalejší
        /// než to okno</b>, takže i skutečných 40 s musí vyjít jako zlomek jeho délky.
        ///
        /// <para>Rozhodnout to jde tak, že se <b>totéž měřidlo pustí na data ze STÁNÍ nakrájená
        /// na stejně dlouhá okna</b>. Tam je pravá odpověď známá z A2 (dlouhé segmenty). Když
        /// zkrácené stání vydá zhruba totéž co jízda, měří se délka okna, ne pole; když stání
        /// i po zkrácení drží déle, je rozdíl skutečný.</para>
        /// </summary>
        private static void KontrolaOknem(List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)> gps,
                                          List<(double A, double B)> stani, double oknoS, double dt,
                                          double tJizda)
        {
            if (stani.Count == 0 || oknoS <= 0) return;

            Console.WriteLine("  KONTROLA: totez meridlo na datech ze STANI, nakrajenych na stejne dlouha okna");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  (okno {0:F0} s - kdyby zkracene stani vydalo totez co jizda, meri se DELKA OKNA, ne pole)",
                oknoS));

            // Tytez segmenty stani dvakrat: CELE (tam je prava odpoved) a NAKRAJENE na okna
            // delky jizdnich useku. Rozdil mezi nimi je primo velikost artefaktu.
            var celeE = Odchylky(gps, stani, double.MaxValue, out var celeN);
            var kratE = Odchylky(gps, stani, oknoS, out var kratN);

            if (kratE.Count == 0)
            {
                Console.WriteLine("  Na kontrolu nevzniklo dost oken ze stani.");
                Console.WriteLine();
                return;
            }

            double tCele = Dekorelace(celeE, celeN, dt, null);
            double tKrat = Dekorelace(kratE, kratN, dt, "    ");

            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  => tataz data ze stani: cele segmenty T_d ~ {0}, nakrajene na {1:F0}s okna T_d ~ {2}",
                Fmt(tCele), oknoS, Fmt(tKrat)));

            // Verdikt: rozhoduje, jestli okno vubec DOVOLI videt to, co je na celych segmentech.
            bool oknoOmezuje = !double.IsNaN(tCele) && !double.IsNaN(tKrat) && tKrat < 0.6 * tCele;
            if (oknoOmezuje)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  ⚠️ OKNO JE OMEZUJICI: totez pole vypada na {0:F0}s oknech {1:F0}x kratceji.",
                    oknoS, tCele / Math.Max(1e-9, tKrat)));
                Console.WriteLine("     Cislo z A2b tedy NENI dekorelacni cas za jizdy - je to delka okna");
                Console.WriteLine("     deleno par. Z techto dat se T_d za jizdy zmerit NEDA.");
                if (!double.IsNaN(tJizda))
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "     (Jizda vydala {0:F1} s, zkracene stani {1:F1} s - k nerozeznani.)", tJizda, tKrat));
            }
            else
            {
                Console.WriteLine("  => Okno neomezuje: cislo z A2b jde brat jako dekorelacni cas za jizdy.");
            }
            Console.WriteLine();
        }

        private static string Fmt(double v) =>
            double.IsNaN(v) ? "> okno" : v.ToString("F1", CultureInfo.InvariantCulture);

        /// <summary>Odchylky fixu od prumeru okna; <paramref name="oknoS"/> = MaxValue vezme cely segment.</summary>
        private static List<List<double>> Odchylky(List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)> gps,
                                                   List<(double A, double B)> stani, double oknoS,
                                                   out List<List<double>> sever)
        {
            var vychod = new List<List<double>>();
            sever = new List<List<double>>();
            foreach (var (a, b) in stani)
            {
                double krok = double.IsInfinity(oknoS) || oknoS > b - a ? b - a : oknoS;
                for (double t = a; t + krok <= b + 1e-9; t += krok)
                {
                    var f = gps.Where(g => g.T >= t && g.T < t + krok).OrderBy(g => g.T).ToList();
                    if (f.Count < 60) continue;
                    double lat0 = f.Average(x => x.Lat), lon0 = f.Average(x => x.Lon);
                    var e1 = new List<double>(); var n1 = new List<double>();
                    foreach (var g in f)
                    {
                        e1.Add(EarthR * Math.Cos(lat0) * (g.Lon - lon0));
                        n1.Add(EarthR * (g.Lat - lat0));
                    }
                    vychod.Add(e1); sever.Add(n1);
                }
            }
            return vychod;
        }

        /// <summary>Dekorelacni cas z autokorelace; <paramref name="odsazeni"/> != null = vypisuje tabulku.</summary>
        private static double Dekorelace(List<List<double>> e, List<List<double>> n, double dt, string odsazeni)
        {
            if (e.Count == 0) return double.NaN;
            double td = double.NaN;
            int maxLag = e.Max(v => v.Count) / 3;
            foreach (int lag in new[] { 1, 5, 10, 25, 50, 100, 150, 250, 400, 600, 800 })
            {
                if (lag >= maxLag) break;
                double r = 0.5 * (Autokorelace(e, lag) + Autokorelace(n, lag));
                if (odsazeni != null)
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}tau = {1,6:F1} s   rho = {2,6:F2}", odsazeni, lag * dt, r));
                if (double.IsNaN(td) && r < 1.0 / Math.E) td = lag * dt;
            }
            return td;
        }

        /// <summary>
        /// Souvisle useky, kde se kola tocila, delsi nez zadana doba.
        ///
        /// <para>⚠️ Kriterium je <b>drazka KOL</b> <c>(|dL| + |dR|) / 2</c>, ne prumer
        /// <c>(L+R)/2</c>. Prvni verze pouzivala prumer a nenasla ani jeden usek v 11minutovem
        /// zaznamu z jizdy — protoze <b>otoceni na miste ma dL = −dR</b>, takze se prumer nehne
        /// a manevrujici robot vypadal jako stojici. FreeRun manevruje porad, takze se kazdy
        /// usek rozpadl na kousky pod prahem.</para>
        /// </summary>
        private static List<(double A, double B)> Jizdy(List<(double T, double L, double R)> enc, double minS)
        {
            var outv = new List<(double, double)>();
            if (enc.Count < 3) return outv;
            var e = enc.OrderBy(x => x.T).ToList();

            // Kumulativni draha KOL (zapocte i otaceni na miste).
            var s = new List<(double T, double S)> { (e[0].T, 0.0) };
            double acc = 0;
            for (int i = 1; i < e.Count; i++)
            {
                acc += 0.5 * (Math.Abs(e[i].L - e[i - 1].L) + Math.Abs(e[i].R - e[i - 1].R));
                s.Add((e[i].T, acc));
            }

            int a = 0;
            while (a < s.Count)
            {
                if (!Jede(s, a)) { a++; continue; }
                int j = a;
                while (j + 1 < s.Count && Jede(s, j)) j++;
                if (s[j].T - s[a].T >= minS) outv.Add((s[a].T, s[j].T));
                a = j + 1;
            }
            return outv;
        }

        /// <summary>Posunula se kola za nasledujici sekundu aspon o 5 cm?</summary>
        private static bool Jede(List<(double T, double S)> s, int i)
        {
            double t = s[i].T;
            int k = i;
            while (k + 1 < s.Count && s[k].T - t < 1.0) k++;
            return Math.Abs(s[k].S - s[i].S) > 0.05;
        }

        /// <summary>Linearni interpolace mrtveho odhadu v case; false mimo rozsah.</summary>
        private static bool Interpoluj(List<(double T, double X, double Y)> dr, double t,
                                       out double x, out double y)
        {
            x = y = 0;
            if (dr.Count < 2 || t < dr[0].T || t > dr[dr.Count - 1].T) return false;
            int lo = 0, hi = dr.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (dr[mid].T <= t) lo = mid; else hi = mid;
            }
            double dt = dr[hi].T - dr[lo].T;
            double u = dt > 1e-9 ? (t - dr[lo].T) / dt : 0;
            x = dr[lo].X + u * (dr[hi].X - dr[lo].X);
            y = dr[lo].Y + u * (dr[hi].Y - dr[lo].Y);
            return true;
        }

        // ---------------------------------------------------------------------------------
        // A5: skace poza, nebo se plizi?
        // ---------------------------------------------------------------------------------
        private static void A5(List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)> st)
        {
            Console.WriteLine("A5) SKACE POZA, NEBO SE PLIZI? (sweep prahu PoseJumpDetectoru)");
            Console.WriteLine("  prah [m]   skoku   (rotacni prah drzen na 5 deg)");
            var t0 = new DateTime(2000, 1, 1);
            foreach (double prah in new[] { 0.05, 0.1, 0.2, 0.3, 0.5, 1.0 })
            {
                var det = new PoseJumpDetector { ToleranceM = prah };
                int n = 0;
                foreach (var s in st)
                    if (det.Check(s.X, s.Y, s.Th, s.V, s.W, t0.AddSeconds(s.T))) n++;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0,8:F2}   {1,5}", prah, n));
            }
            var det2 = new PoseJumpDetector { ToleranceM = 0.05 };
            int nejmensi = st.Count(s => det2.Check(s.X, s.Y, s.Th, s.V, s.W, t0.AddSeconds(s.T)));
            Console.WriteLine(nejmensi == 0
                ? "  => Ani pri nejmensim prahu 0,05 m ZADNY skok: je to CISTE PLIZIVY drift."
                  + " Grid se tedy nikdy nezahazuje, jen se rozmazava - a posun originu misto"
                  + " Clear() na tom nezmeni nic."
                : $"  => Pri prahu 0,05 m je {nejmensi} skoku: cast driftu ma skokovou povahu.");
            Console.WriteLine();
        }

        // ---------------------------------------------------------------------------------
        // A4: prirozene A/B - useky, kde fix prosel, proti tem, kde ho brana odmitla
        // ---------------------------------------------------------------------------------
        private static void A4(List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)> st,
                               List<(double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std)> gps,
                               List<(double A, double B)> stani,
                               List<(double T, int Status, double Clearance)> plan,
                               List<(double T, int Blocked, double Mass)> grid)
        {
            Console.WriteLine("A4) PRIROZENE A/B: useky stani, kde fix PROSEL, proti tem, kde ho brana ODMITLA");
            Console.WriteLine("    (stejne misto, stejne kamery - lisi se jen to, jestli GPS hyba pozou)");

            var driftAcc = new Stats("drift odhadu - fix PROSEL");
            var driftRej = new Stats("drift odhadu - fix ODMITNUT");
            var blkAcc = new Stats("blokovanych bunek - fix PROSEL");
            var blkRej = new Stats("blokovanych bunek - fix ODMITNUT");
            var clrAcc = new Stats("MinClearance - fix PROSEL");
            var clrRej = new Stats("MinClearance - fix ODMITNUT");
            var statAcc = new Dictionary<int, int>();
            var statRej = new Dictionary<int, int>();
            double okno = 2.0;

            foreach (var (a, b) in stani)
            {
                for (double t = a; t + okno <= b; t += okno)
                {
                    var f = gps.Where(g => g.T >= t && g.T < t + okno).ToList();
                    if (f.Count == 0) continue;
                    bool prosel = f.Any(g => g.Reject == null);

                    var s = st.Where(x => x.T >= t && x.T < t + okno).OrderBy(x => x.T).ToList();
                    if (s.Count >= 2)
                    {
                        double d = 0;
                        for (int i = 1; i < s.Count; i++)
                            d += Math.Sqrt(Sq(s[i].X - s[i - 1].X) + Sq(s[i].Y - s[i - 1].Y));
                        (prosel ? driftAcc : driftRej).Add(d * 60.0 / okno);
                    }

                    foreach (var g2 in grid.Where(x => x.T >= t && x.T < t + okno))
                        (prosel ? blkAcc : blkRej).Add(g2.Blocked);

                    foreach (var p in plan.Where(x => x.T >= t && x.T < t + okno))
                    {
                        if (p.Clearance > 0) (prosel ? clrAcc : clrRej).Add(p.Clearance);
                        var d = prosel ? statAcc : statRej;
                        d[p.Status] = d.TryGetValue(p.Status, out int c) ? c + 1 : 1;
                    }
                }
            }

            Console.WriteLine();
            if (driftAcc.Count == 0 || driftRej.Count == 0)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  ⚠️ A/B NELZE UDELAT: oken s prijatym fixem {0}, s odmitnutym {1}.",
                    driftAcc.Count, driftRej.Count));
                Console.WriteLine("     Aby A/B vzniklo, musi brana behem STANI nekdy fix pustit a jindy odmitnout");
                Console.WriteLine("     - tedy zaznam z mista, kde DOP kolisa kolem prahu (napr. u budovy).");
                Console.WriteLine("     Tohle je duvod pro cileny zaznam, ne vada nastroje.");
            }
            if (driftAcc.Count > 0) Console.WriteLine("  " + driftAcc.Line("m/min"));
            if (driftRej.Count > 0) Console.WriteLine("  " + driftRej.Line("m/min"));
            if (driftAcc.Count > 0 && driftRej.Count > 0)
            {
                double pomer = driftAcc.Median / Math.Max(1e-9, driftRej.Median);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => drift je s prijatym fixem {0:F1}x vetsi nez bez nej", pomer));
                if (pomer < 2)
                    Console.WriteLine("  ⚠️ Pod 2x: hypoteza 'za artefakty muze GPS' timhle NENI potvrzena.");
            }

            if (blkAcc.Count > 0 || blkRej.Count > 0)
            {
                Console.WriteLine();
                if (blkAcc.Count > 0) Console.WriteLine("  " + blkAcc.Line("bunek"));
                if (blkRej.Count > 0) Console.WriteLine("  " + blkRej.Line("bunek"));
                Console.WriteLine("  (stojici robot nic noveho nevidi - rostouci pocet blokovanych bunek = rozmazavani)");
            }

            if (clrAcc.Count > 0 || clrRej.Count > 0)
            {
                Console.WriteLine();
                if (clrAcc.Count > 0) Console.WriteLine("  " + clrAcc.Line("m"));
                if (clrRej.Count > 0) Console.WriteLine("  " + clrRej.Line("m"));
            }
            if (statAcc.Count > 0 || statRej.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine("  stavy planu (prosel / odmitnut):");
                foreach (var k in statAcc.Keys.Union(statRej.Keys).OrderBy(x => x))
                    Console.WriteLine($"    {(LocalPlanStatus)k,-22} {Get(statAcc, k),5} / {Get(statRej, k),5}");
            }
            Console.WriteLine();
        }

        // --- pomucky ---------------------------------------------------------------------

        private static int Get(Dictionary<int, int> d, int k) => d.TryGetValue(k, out int v) ? v : 0;

        /// <summary>Segmenty, kde se kola nepohnula o vic nez <see cref="StandTolM"/>.</summary>
        private static List<(double A, double B)> Stani(List<(double T, double S)> enc, double minS, double tol)
        {
            var outv = new List<(double, double)>();
            enc.Sort((x, y) => x.T.CompareTo(y.T));
            int i = 0;
            while (i < enc.Count)
            {
                int j = i;
                double min = enc[i].S, max = enc[i].S;
                while (j + 1 < enc.Count)
                {
                    double s = enc[j + 1].S;
                    double nmin = Math.Min(min, s), nmax = Math.Max(max, s);
                    if (nmax - nmin > tol) break;
                    min = nmin; max = nmax; j++;
                }
                if (enc[j].T - enc[i].T >= minS) outv.Add((enc[i].T, enc[j].T));
                i = j + 1;
            }
            return outv;
        }

        /// <summary>
        /// Diagnostika, kdyz zadny segment stani nevznikne: rozdeleni posunu kol v oknech dane
        /// delky. Par milimetru znamena "sum enkoderu, povol --standtol", metry "robot jel".
        /// </summary>
        private static void PrecStani(List<(double T, double S)> enc, double oknoS)
        {
            var posun = new Stats($"posun kol v oknech po {oknoS:F0} s");
            int i = 0;
            while (i < enc.Count)
            {
                int j = i;
                double min = enc[i].S, max = enc[i].S;
                while (j + 1 < enc.Count && enc[j + 1].T - enc[i].T < oknoS)
                {
                    j++;
                    min = Math.Min(min, enc[j].S);
                    max = Math.Max(max, enc[j].S);
                }
                if (enc[j].T - enc[i].T >= oknoS * 0.9) posun.Add(max - min);
                i = j + 1;
            }
            if (posun.Count > 0)
            {
                Console.WriteLine("  " + posun.Line("m"));
                Console.WriteLine(posun.Median < 0.05
                    ? "  => Nejtissi okna jsou v radu centimetru: to je spis SUM enkoderu nez jizda."
                      + " Zkus --standtol=0.05."
                    : "  => Robot se v kazdem okne posunul o desitky cm a vic: opravdu jel.");
            }
            Console.WriteLine();
        }

        private static int Blocked(OccupancyGridMsg g)
        {
            int n = 0;
            for (int i = 0; i < g.Occ.Length; i++)
                if (g.Occ[i] * g.Scale >= g.BlockedThreshold) n++;
            return n;
        }

        private static double Mass(OccupancyGridMsg g)
        {
            double m = 0;
            for (int i = 0; i < g.Occ.Length; i++) m += Math.Abs(g.Occ[i] * (double)g.Scale);
            return m;
        }

        /// <summary>Nejmensi ctverce y = k*x + q; vraci smernici a R2.</summary>
        private static (double K, double R2) Regrese(List<double> x, List<double> y)
        {
            double mx = x.Average(), my = y.Average();
            double sxx = 0, sxy = 0, syy = 0;
            for (int i = 0; i < x.Count; i++)
            {
                sxx += (x[i] - mx) * (x[i] - mx);
                sxy += (x[i] - mx) * (y[i] - my);
                syy += (y[i] - my) * (y[i] - my);
            }
            if (sxx < 1e-12) return (double.NaN, double.NaN);
            double k = sxy / sxx;
            double r2 = syy > 0 ? (sxy * sxy) / (sxx * syy) : double.NaN;
            return (k, r2);
        }

        /// <summary>
        /// Autokorelace na danem lagu, pocitana UVNITR segmentu - dvojice nikdy neprekroci
        /// mezeru mezi nimi (v ni robot jel, takze by to spojovalo vzorky, ktere spolu
        /// casove nesousedi).
        /// </summary>
        private static double Autokorelace(List<List<double>> segmenty, int lag)
        {
            double num = 0, den = 0;
            foreach (var v in segmenty)
            {
                if (v.Count <= lag) continue;
                double m = v.Average();
                for (int i = 0; i < v.Count - lag; i++) num += (v[i] - m) * (v[i + lag] - m);
                for (int i = 0; i < v.Count; i++) den += (v[i] - m) * (v[i] - m);
            }
            return den > 0 ? num / den : double.NaN;
        }

        private static double Sd(List<double> v)
        {
            if (v.Count < 2) return 0;
            double m = v.Average();
            return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
        }

        /// <summary>Smerodatna odchylka vzorku pres vsechny segmenty (kazdy kolem sveho prumeru).</summary>
        private static double Sd(List<List<double>> segmenty)
        {
            double ss = 0; int n = 0;
            foreach (var v in segmenty)
            {
                if (v.Count < 2) continue;
                double m = v.Average();
                ss += v.Sum(x => (x - m) * (x - m));
                n += v.Count - 1;
            }
            return n > 0 ? Math.Sqrt(ss / n) : 0;
        }

        /// <summary>
        /// Smerodatna odchylka prumeru z N po sobe jdoucich vzorku; bloky se skladaji
        /// UVNITR segmentu, takze zadny neprekroci mezeru mezi nimi.
        /// </summary>
        private static double SdBlokovehoPrumeru(List<List<double>> segmenty, int n)
        {
            var bloky = new List<double>();
            foreach (var v in segmenty)
                for (int i = 0; i + n <= v.Count; i += n)
                {
                    double s = 0;
                    for (int k = 0; k < n; k++) s += v[i + k];
                    bloky.Add(s / n);
                }
            return bloky.Count >= 2 ? Sd(bloky) : double.NaN;
        }

        private static bool TryNearest(List<(double T, double V)> list, double t, double tol, out double value)
        {
            value = 0;
            if (list.Count == 0) return false;
            double best = double.MaxValue;
            foreach (var (tt, v) in list)
            {
                double d = Math.Abs(tt - t);
                if (d < best) { best = d; value = v; }
            }
            return best <= tol;
        }

        private static double Sq(double x) => x * x;

        private static double Sec(DateTime t, ref DateTime t0)
        {
            if (t0 == DateTime.MinValue) t0 = t;
            return (t - t0).TotalSeconds;
        }
    }
}
