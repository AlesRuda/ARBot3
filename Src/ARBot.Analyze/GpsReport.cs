using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Occupancy;
using ARBot.Common.Runtime;

// Jeden fix GPS pro rozbor. Platny = GPSState.IsFixed: neplatny fix nema smysluplnou polohu, takze
// do statistik polohy nepatri (do brany A4 ano - "zadny platny fix" je taky odmitnuti).
// VE/VN = rychlost z PRIJIMACE (Doppler: Speed a DynamicOrientation) [m/s], NaN = neni.
using GpsFix = (double T, double Lat, double Lon, double Dop, int Sat, string Reject, double Std, bool Platny,
                double VE, double VN);

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

        /// <param name="gpsPosStd">Prepis <c>gpsposstd</c> [m]; &lt;= 0 = ze zaznamu.</param>
        /// <param name="kurz">Hlavni referencni draha v A2b (<c>auto</c>, <c>gyro</c>, <c>gyroraw</c>,
        /// <c>fuze</c>, <c>kola</c>, <c>doppler</c>); null = <c>auto</c>.</param>
        /// <param name="meritko">Zarovnani v A2b odhaduje i meritko odometrie.</param>
        /// <param name="jenBrana">A2b jen z fixu, ktere brana fuze pustila (vychozi), jinak ze vsech platnych.
        /// Od 13. 10. 2026: 27. 9. 2026 17:25 brana odmitla 22 fixu se skokem ~214 m a se vsemi
        /// fixy mel zbytek RMS 20 m misto 1,5 m - meri se chyba, kterou filtr opravdu dostava.</param>
        public static void Run(RecordFile rec, double minStandS, double gpsMaxDop, int gpsMinSat,
                               double standTolM, double minJizdaS, double minDrahaM,
                               double gpsPosStd = 0, string kurz = null, bool meritko = true,
                               bool jenBrana = true)
        {
            if (minStandS <= 0) minStandS = DefaultMinStandS;
            if (standTolM <= 0) standTolM = DefaultStandTolM;

            // Brana a sigma GPS ZE ZAZNAMU (od 13. 10. 2026). Do te doby se pocitalo s defaulty
            // z kodu, tedy s gpsposstd 1,5 m, ackoli provozni profil ma od 6. 9. 2026 30 m:
            // "sigma GPS pouzita fuzi" i predpovezene P/(P+R) v A1 tak patrily jinemu robotu,
            // nez ktery jel. Prepinace meridla maji prednost (A/B), default z kodu je jen zaloha
            // pro zaznamy bez vypisu konfigurace (pred 5. 9. 2026).
            var konf = LogConfig.Read(rec);
            var cfg = new FusionConfig();
            // Prepis se resi tady, ne v LogConfig.Resolve: ten by do puvodu napsal "--gpsposstd=",
            // tedy prepinac, ktery meridlo nema (ma --posstd=, --maxdop=, --minsat=).
            cfg.GpsPosStd = Hodnota(konf, "gpsposstd", gpsPosStd, "--posstd=", cfg.GpsPosStd, out string puvodPosStd);
            cfg.GpsMaxDop = Hodnota(konf, "gpsmaxdop", gpsMaxDop, "--maxdop=", cfg.GpsMaxDop, out string puvodMaxDop);
            cfg.GpsMinSatellites = (int)Math.Round(Hodnota(konf, "gpsminsat", gpsMinSat, "--minsat=",
                                                           cfg.GpsMinSatellites, out string puvodMinSat));
            string puvodDopSigma;
            if (bool.TryParse(konf.Text("gpsdopsigma"), out bool dopSigma))
            {
                cfg.GpsScaleStdByDop = dopSigma;
                puvodDopSigma = "ze zaznamu (" + (konf.Puvod("gpsdopsigma") ?? "?") + ")";
            }
            else puvodDopSigma = konf.Any ? "v zaznamu neni, default z kodu" : "zaznam nema vypis konfigurace, default z kodu";

            var st = new List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)>();
            var gps = new List<GpsFix>();
            var enc = new List<(double T, double S)>();
            // Leve a prave kolo zvlast: draha mrtveho odhadu v A2b je z prumeru kol, kurz z gyra
            // (rozdil kol je jen jeden ze srovnavacich zdroju - viz A2b). Kurz z fuze se pouzit
            // NESMI - obsahuje GPS kurz, koridor i kompas, tedy i to, co se meri.
            var encLR = new List<(double T, double L, double R)>();
            // Gyro VN100 (Z = staceni; kompenzovane odhadem biasu z VPE a od IMUState verze 5 i
            // syrove). Jen zdroj s absolutnim kurzem - T265 (relativni) gyro neposila.
            var imu = new List<(double T, double Gz, double GzRaw)>();
            var plan = new List<(double T, int Status, double Clearance)>();
            var grid = new List<(double T, int Blocked, double Mass)>();
            // A0: hodiny robota proti GPS. UTC cas fixu je JEDINY absolutni cas v zaznamu, ktery
            // nepochazi z hodin Pi. Viz blok A0 nize.
            var hodiny = new List<(DateTime Stamp, TimeSpan Fix, int Verze, TimeSpan Utc, int Sat)>();

            DateTime t0 = DateTime.MinValue;
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "RobotStateMsg" && e.MsgName != "GPSState" && e.MsgName != "MotorStateBase"
                    && e.MsgName != "LocalPlanMsg" && e.MsgName != "OccupancyGridMsg"
                    && e.MsgName != "IMUState") continue;
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
                                 DefaultMeasurementMapper.PositionStd(p, cfg), p.IsFixed,
                                 RychlostVE(p), RychlostVN(p)));
                        if (p.UtcTimeOfDay() is TimeSpan utcFix)
                            hodiny.Add((p.TimeStamp, p.FixTime, p.Verze, utcFix, p.NumberOfSatellites));
                        break;
                    case MotorStateBase m:
                        double tm = Sec(m.TimeStamp, ref t0);
                        enc.Add((tm, 0.5 * (m.LeftEncoder + m.RightEncoder)));
                        encLR.Add((tm, m.LeftEncoder, m.RightEncoder));
                        break;
                    case LocalPlanMsg lp:
                        plan.Add((Sec(lp.TimeStamp, ref t0), lp.Status, lp.MinClearanceM));
                        break;
                    case IMUState i when i.HasAbsoluteHeading && i.AngularVelocity.HasValue:
                        imu.Add((Sec(i.TimeStamp, ref t0), i.AngularVelocity.Value.Z,
                                 i.AngularVelocityRaw.HasValue ? i.AngularVelocityRaw.Value.Z : double.NaN));
                        break;
                    case OccupancyGridMsg og:
                        grid.Add((Sec(og.TimeStamp, ref t0), Blocked(og), Mass(og)));
                        break;
                }
            }

            Hodiny(hodiny);

            Console.WriteLine($"RobotStateMsg {st.Count}, GPSState {gps.Count}, MotorStateBase {enc.Count}, "
                              + $"IMUState (gyro) {imu.Count}, LocalPlanMsg {plan.Count}, OccupancyGridMsg {grid.Count}");
            if (konf.Version != null) Console.WriteLine(konf.Version);
            Console.WriteLine("brana a sigma GPS pouzite pri rozboru (brana A1/A4 i sigma v A1):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  gpsposstd={0:G4} m   {1}", cfg.GpsPosStd, puvodPosStd));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  gpsdopsigma={0}   {1}",
                cfg.GpsScaleStdByDop ? "true" : "false", puvodDopSigma));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  gpsmaxdop={0:G4}   {1}", cfg.GpsMaxDop, puvodMaxDop));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  gpsminsat={0}   {1}", cfg.GpsMinSatellites, puvodMinSat));
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
                A1(st, gps, stani, cfg);
                A2(gps, stani, jenBrana);
            }

            A2b(gps, encLR, imu, st, minJizdaS, minDrahaM, stani, kurz, meritko, jenBrana);
            A5(st);
            if (stani.Count > 0) A4(st, gps, stani, plan, grid);
        }

        /// <summary>Hodnota parametru pro rozbor: prepis meridla (&gt; 0), jinak ze zaznamu, jinak default.</summary>
        private static double Hodnota(LogConfig konf, string klic, double prepis, string prepinac,
                                      double vychozi, out string puvod)
        {
            if (prepis > 0) { puvod = prepinac + " (prepis meridla)"; return prepis; }
            return konf.Resolve(klic, double.NaN, vychozi, out puvod);
        }

        /// <summary>
        /// Lokalni ENU [m] kolem (lat0, lon0) na elipsoidu WGS84 (polomery krivosti N a M).
        /// Do 13. 10. 2026 se pocitalo na kouli s rovnikovym polomerem pro obe osy: na 50° s. s. je
        /// pak vychod o 0,197 % kratky a sever o 0,081 % dlouhy, a tu anizotropii zarovnani s jednim
        /// meritkem nepohlti (±0,3-0,7 m na stovkach metru - srovnatelne se zbytkem v A2b).
        /// </summary>
        private static (double E, double N) Enu(double lat0, double lon0, double lat, double lon)
        {
            const double A = 6378137.0, E2 = 6.69437999014e-3;
            double s = Math.Sin(lat0);
            double w = Math.Sqrt(1 - E2 * s * s);
            double polN = A / w, polM = A * (1 - E2) / (w * w * w);
            return (polN * Math.Cos(lat0) * (lon - lon0), polM * (lat - lat0));
        }

        // ---------------------------------------------------------------------------------
        // A1: tahne GPS stojiciho robota, a jak silne?
        // ---------------------------------------------------------------------------------
        private static void A1(List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)> st,
                               List<GpsFix> gps,
                               List<(double A, double B)> stani, FusionConfig cfg)
        {
            Console.WriteLine("A1) TAHNE GPS STOJICIHO ROBOTA?");

            var drift = new Stats("ujeta draha ODHADU za 60 s stani");
            var sigX = new Stats("hlasena sigma polohy z fuze (sqrt P_xx)");
            var sigGps = new Stats(string.Format(CultureInfo.InvariantCulture, "sigma GPS pouzita fuzi ({0:G4} m{1})",
                                                 cfg.GpsPosStd, cfg.GpsScaleStdByDop ? " x DOP" : ""));
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
                    var (fe, fn) = Enu(lat0, lon0, g.Lat, g.Lon);

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
        private static double Kadence(List<GpsFix> gps,
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
        private static void A2(List<GpsFix> gps,
                               List<(double A, double B)> stani, bool jenBrana)
        {
            Console.WriteLine("A2) JE CHYBA GPS CASOVE KORELOVANA? (stojici robot = pravda je konstanta)");

            // Odchylky od prumeru segmentu, v metrech. Od 13. 10. 2026 jen platne fixy a s --brana=1
            // (vychozi) jen pustene branou, jako v A2b (driv vse vcetne odmitnutych - brana je
            // jina otazka a jeji vliv resi A4.
            //
            // POZOR: DRZI SE PO SEGMENTECH, ne v jednom seznamu. Prvni verze je slila dohromady,
            // takze bloky i lagy prekracovaly MEZERU mezi segmenty (tady 32 s, kdy robot jel) -
            // a pocitaly korelaci mezi vzorky, ktere spolu casove nesousedi.
            var dev = new List<double>();
            var segE = new List<List<double>>();
            var segN = new List<List<double>>();
            var segT = new List<double[]>();
            double dt = 0.2;
            double dobaCelkem = 0;

            // Tytez fixy jako A2b (s branou jen pustene) a segment se deli na mezerach GPS nad 1 s -
            // autokorelace se pocita po vzorcich, takze mezera by posunula lag proti casu.
            var pouzitelne = gps.Where(g => g.Platny && (!jenBrana || g.Reject == null)).ToList();
            foreach (var f in Behy(pouzitelne, stani, double.MaxValue))
            {
                if (f.Count < 30) continue;
                double a = f[0].T, b = f[f.Count - 1].T;
                double lat0 = f.Average(x => x.Lat), lon0 = f.Average(x => x.Lon);
                var e1 = new List<double>(); var n1 = new List<double>();
                foreach (var g in f)
                {
                    var (e, n) = Enu(lat0, lon0, g.Lat, g.Lon);
                    e1.Add(e); n1.Add(n); dev.Add(Math.Sqrt(e * e + n * n));
                }
                segE.Add(e1); segN.Add(n1);
                segT.Add(f.Select(x => x.T).ToArray());
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
            // Sweep musi dosahnout DAL nez ocekavany T_d, jinak report skonci na "delsi nez merene
            // okno" a nic nerekne. Prvni verze koncila na 250 vzorcich (25 s), kde je rho 0,38 -
            // tesne NAD 1/e, takze odpoved lezela hned za koncem tabulky. Strop drzi az podminka
            // lag < delka segmentu / 3. Tabulka je ridka, T_d se odecita jemne (Prechod).
            double tDecorr = Dekorelace(segE, segN, dt, "    ");
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
                    + " ({1:F0} s / 2x {2:F0} s).", dobaCelkem / (2 * tDecorr), dobaCelkem, tDecorr));

            // Kalibrace i pro stani (od 13. 10. 2026): totez Monte Carlo jako v A2b, jen "zarovnani"
            // je odecet prumeru segmentu. Bez ni se stani a jizda srovnavaly kazde jinak srazene.
            var ms = new MereniA2b { Dt = dt };
            for (int i = 0; i < segE.Count; i++)
            {
                ms.SegE.Add(segE[i]);
                ms.SegN.Add(segN[i]);
                ms.Geometrie.Add((segT[i], Enumerable.Repeat((0.0, 0.0), segT[i].Length)
                                                     .Select(x => (X: x.Item1, Y: x.Item2)).ToList()));
            }
            Console.WriteLine();
            Kalibrace(ms, false, tDecorr, jenPrumer: true);

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
        /// <para><b>Postup.</b> Z dráhy kol (<c>Δs</c> z průměru kol) a z průběhu kurzu se sestaví
        /// mrtvý odhad, <b>tuze se zarovná na dráhu z GPS</b> (2D Procrustes: jeden posun, jedna
        /// rotace a jedno měřítko na celý úsek) a zbytek je <c>chyba GPS − drift odometrie</c>.
        /// Jeho autokorelace dá dekorelační čas.</para>
        ///
        /// <para><b>Kurz z GYRA VN100</b> (od 13. 10. 2026; do té doby z rozdílu kol). Kurz fúze
        /// nese GPS kurz, koridor i kompas, tedy i to, co se měří — bylo by to kruhové. Rozdíl
        /// kol zase nese prokluz při otáčení (<c>WheelSlipMonitor</c>, <c>lok-fuze-rucni-otoceni</c>)
        /// a chyba kurzu se v mrtvém odhadu integruje do polohy, tedy právě do nízkých frekvencí,
        /// které dekorelační čas <b>prodlužují</b>. VN100 GPS nemá. Pro srovnání se T_d spočítá
        /// i z ostatních zdrojů (<c>--kurz=</c> vybere hlavní): když se mezi nimi liší málo, na
        /// kurzu výsledek nestojí. <b>Měřítko</b> (od téhož dne) pohltí chybu průměru kol: 1 %
        /// na 774 m je ±4 m pomalého zbytku, tedy stejný řád jako měřená chyba GPS.</para>
        ///
        /// <para><b>Úsek se dělí na přerušení dat</b> (od 13. 10. 2026): mezera v enkodérech, ve
        /// zdroji kurzu nebo v GPS a nemožný skok enkodéru, plus 1 s ochrany za mezerou (dávka
        /// rámců z bufferu má razítka příchodu, ne měření). Po záseku procesu 1. 10. 2026 15:15:41
        /// se ztratilo ~0,29 m enkodéru; mrtvý odhad přes to je posunutý o kus dráhy a zarovnání
        /// to rozprostře po celém úseku.</para>
        ///
        /// <para>⚠️ <b>Zarovnání odečte nejnižší frekvence</b>, takže krátký úsek zkrátí i skutečný
        /// T_d. Od 13. 10. 2026 to <b>vyčísluje kalibrace na téže geometrii</b> (AR(1) chyba se
        /// známým T_d, tytéž úseky, totéž zarovnání a týž odečet) — dřív to report jen označil
        /// jako „spodní odhad". Zbytek přitom pořád <b>míchá chybu GPS s driftem odometrie</b>.</para>
        /// </summary>
        private static void A2b(List<GpsFix> gps, List<(double T, double L, double R)> enc,
                                List<(double T, double Gz, double GzRaw)> imu,
                                List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)> st,
                                double minJizdaS, double minDrahaM, List<(double A, double B)> stani,
                                string kurz, bool meritko, bool jenBrana)
        {
            double MinJizdaS = minJizdaS > 0 ? minJizdaS : 60.0;
            double MinDrahaM = minDrahaM > 0 ? minDrahaM : 20.0;

            Console.WriteLine("A2b) DEKORELACNI CAS CHYBY GPS ZA JIZDY (proti referencnim draham: odometrie a rychlost GPS)");
            enc = enc.OrderBy(x => x.T).ToList();
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

            var zdroje = ZdrojeKurzu(enc, imu, st, gps);
            Console.WriteLine("  referencni drahy (mrtvy odhad; --kurz= vybere hlavni, vychozi auto):");
            foreach (var z in zdroje)
                Console.WriteLine($"    {z.Nazev,-8} {(z.Dostupny ? z.Popis : "v zaznamu neni - " + z.Popis)}");
            Console.WriteLine(meritko
                ? "  zarovnani: posun + rotace + MERITKO (--meritko=0 = jen posun a rotace jako do 13. 10. 2026)"
                : "  zarovnani: posun + rotace (bez meritka)");
            Console.WriteLine(jenBrana
                ? "  fixy: jen ty, ktere BRANA FUZE pustila - chyba, kterou filtr opravdu dostava (--brana=0 = vsechny platne)"
                : "  fixy: vsechny platne vcetne odmitnutych branou (--brana=0)");

            // Zmeri se se vsemi dostupnymi zdroji: hlavni vybere "auto" a srovnani ukaze, kolik
            // vysledku stoji na kurzu mrtveho odhadu.
            var vysledky = new Dictionary<ZdrojKurzu, MereniA2b>();
            foreach (var z in zdroje.Where(z => z.Dostupny))
                vysledky[z] = Mer(z, gps, enc, jizdy, MinJizdaS, MinDrahaM, meritko, jenBrana);

            // AUTO = odometricky zdroj s NEJMENSIM zbytkem. Chyba GPS je pro vsechny zdroje tataz,
            // takze nejmensi zbytek ma ten s nejmensim driftem mrtveho odhadu. Doppler do vyberu
            // nejde: je z tehoz prijimace, takze jeho mensi zbytek muze byt spolecna chyba, ne
            // presnost (prijimac si polohu s rychlosti svazuje vlastnim filtrem).
            string chci = string.IsNullOrWhiteSpace(kurz) ? "auto" : kurz.Trim().ToLowerInvariant();
            var hlavni = chci == "auto" ? null : zdroje.FirstOrDefault(z => z.Nazev == chci && z.Dostupny);
            if (hlavni == null)
            {
                if (chci != "auto")
                    Console.WriteLine($"  ⚠️ zdroj '{chci}' v zaznamu neni (nebo neznamy) - vybira se auto.");
                // Jen mezi drahami se srovnatelnym pokrytim: RMS nad kratsimi useky je mensi uz tim,
                // ze se kratsi usek zarovna lip (27. 9. 17:30 by jinak vyhrala fuze s 94 s nad gyrem
                // se 158 s jen proto, ze stav fuze skoncil o 0,15 s driv a usek vypadl).
                var odo = vysledky.Where(kv => !kv.Key.Doppler && kv.Value.Pouzito > 0).ToList();
                double maxDoba = odo.Count > 0 ? odo.Max(kv => kv.Value.Doba) : 0;
                // ...a se stejnym poctem useku jako nejdelsi: vlastni mezera zdroje uprostred jizdy
                // stoji jen ~1 s doby, ale rozdeli usek na dva, ktere se zarovnaji lip.
                int useku = odo.Count > 0 ? odo.OrderByDescending(kv => kv.Value.Doba).First().Value.Pouzito : 0;
                hlavni = odo.Where(kv => kv.Value.Doba >= 0.95 * maxDoba && kv.Value.Pouzito == useku)
                            .OrderBy(kv => RmsVse(kv.Value.SegE, kv.Value.SegN))
                            .Select(kv => kv.Key).FirstOrDefault()
                         ?? zdroje.First(z => z.Dostupny && !z.Doppler);   // kola jsou vzdy
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  hlavni zdroj (auto = odometricky s nejmensim zbytkem mezi drahami s pokrytim aspon 95 % z {0:F0} s): '{1}'",
                    maxDoba, hlavni.Nazev));
                var mimo = odo.Where(kv => kv.Value.Doba < 0.95 * maxDoba || kv.Value.Pouzito != useku)
                              .Select(kv => kv.Key.Nazev).ToList();
                if (mimo.Count > 0)
                    Console.WriteLine("    (do vyberu nesly pro mensi pokryti: " + string.Join(", ", mimo) + ")");
            }
            else Console.WriteLine($"  hlavni zdroj (--kurz=): '{hlavni.Nazev}'");

            var m = vysledky[hlavni];

            Console.WriteLine($"  preruseni dat uvnitr jizd (usek se na nich deli): {m.Preruseni.Count}");
            foreach (var p in m.Preruseni.Take(20)) Console.WriteLine(p);
            if (m.Preruseni.Count > 20) Console.WriteLine($"    ... a dalsich {m.Preruseni.Count - 20}");

            // Tabulka useku i s duvodem zamitnuti: bez ni je "nelze merit" k nerozeznani od vady
            // nastroje, a prave tady je podstatne vedet, CO v datech chybi.
            Console.WriteLine("  usek [s]        doba    draha   fixu   natoceni  meritko   verdikt");
            foreach (var r in m.Radky) Console.WriteLine(r);

            if (m.Pouzito == 0)
            {
                Console.WriteLine();
                Console.WriteLine("  => NA TOHLE MERENI NEJSOU V ZAZNAMU DATA. Neni to vada nastroje ani");
                Console.WriteLine("     vysledek 'chyba nekoreluje' - proste tu neni dost souvisle jizdy.");
                Console.WriteLine("     Aby to slo zmerit, je potreba usek, ktery je DELSI NEZ ocekavany T_d");
                Console.WriteLine("     (pri stani 2. 9. 2026 T_d >= ~30-46 s), tedy radove 5-10 minut jizdy bez zastaveni.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  useku {0}, celkem {1:F0} s jizdy a {2:F0} m drahy, perioda fixu {3:F2} s{4}",
                m.Pouzito, m.Doba, m.Draha, m.Dt,
                m.Neplatnych > 0 ? $", vynechano {m.Neplatnych} neplatnych fixu" : ""));
            if (jenBrana)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  brana fuze z pouzitych useku vyradila {0} platnych fixu (--brana=0 je vrati); zbytek max {1:F1} m",
                    m.VyrazenoBranou, m.ZbytekPustenych.Max));
            else if (m.Odmitnutych > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  z pouzitych fixu brana fuze ODMITLA {0} - jejich zbytek max {1:F1} m (p50 {2:F2} m),"
                    + " u pustenych max {3:F1} m (p50 {4:F2} m)",
                    m.Odmitnutych, m.ZbytekOdmitnutych.Max, m.ZbytekOdmitnutych.Median,
                    m.ZbytekPustenych.Max, m.ZbytekPustenych.Median));
            else if (m.ZbytekPustenych.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  vsechny pouzite fixy brana fuze pustila (zbytek max {0:F1} m)", m.ZbytekPustenych.Max));
            Console.WriteLine();

            // Rozbor proti hlavni (odometricke) draze a zvlast proti Doppleru. Kazda ma jinou vadu:
            // odometrie drift (prodluzuje T_d), Doppler spolecnou chybu s polohou (tu nevidi).
            Console.WriteLine($"  --- PROTI DRAZE '{hlavni.Nazev}' ---");
            var (tOdo, kOdo) = Rozbor(m, meritko, hlavni.Doppler
                ? "chyba GPS minus chyba drahy z rychlosti GPS"
                : "chyba GPS + drift odometrie");

            var dop = zdroje.FirstOrDefault(z => z.Doppler && z != hlavni && vysledky.ContainsKey(z));
            double tDop = double.NaN; string kDop = null;
            if (dop != null && vysledky[dop].Pouzito > 0)
            {
                Console.WriteLine("  --- PROTI DRAZE Z RYCHLOSTI GPS (doppler) ---");
                (tDop, kDop) = Rozbor(vysledky[dop], meritko, "chyba GPS minus chyba drahy z rychlosti GPS");
            }
            if ((dop != null && vysledky[dop].Pouzito > 0) || hlavni.Doppler)
            {
                Console.WriteLine("  ⚠️ Doppler NENI cista reference ani dolni mez: chybu, kterou prijimac vnese do polohy");
                Console.WriteLine("     i rychlosti zaroven, odecte a neuvidi (T_d zkrati), a ma i VLASTNI chybu (sum rychlosti,");
                Console.WriteLine("     kurz pri male rychlosti), ktera T_d prodlouzi. Co prevazuje, ukaze tabulka PRIRUSTKU");
                Console.WriteLine("     nize proti nezavisle odometrii tam, kde je zdrava.");
                Console.WriteLine();
            }

            SrovnaniZdroju(zdroje, hlavni, vysledky, tOdo);
            Prirustky(zdroje, vysledky);

            Console.WriteLine("  SHRNUTI A2b:");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "    proti '{0}': namereno {1}, {2}", hlavni.Nazev, F1(tOdo), kOdo));
            if (kDop != null)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    proti 'doppler': namereno {0}, {1}", F1(tDop), kDop));
            Console.WriteLine();

            // Kontrola oknem tymz filtrem fixu jako A2b - jinak by nebylo "totez meridlo".
            KontrolaOknem(jenBrana ? gps.Where(g => g.Reject == null).ToList() : gps,
                          stani, m.Doba / Math.Max(1, m.Pouzito), m.Dt, tOdo);
        }

        /// <summary>
        /// Autokorelace zbytku, namereny T_d, rychla slozka a kalibrace pro jednu referencni drahu.
        /// Vraci namereny T_d a slovni vysledek kalibrace (pro shrnuti).
        /// </summary>
        private static (double T, string Kalibrovano) Rozbor(MereniA2b m, bool meritko, string coZbytek)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  zbytek po zarovnani ({0}):", coZbytek));
            Console.WriteLine("    " + m.Zbytek.Line("m"));
            Console.WriteLine("  autokorelace zbytku (prumer obou os):");
            double tDecorr = Dekorelace(m.SegE, m.SegN, m.Dt, "    ");
            int maxLag = m.SegE.Max(v => v.Count) / 3;
            if (double.IsNaN(tDecorr))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => rho neklesla pod 1/e ani na {0:F0} s: T_d je DELSI nez to, co jde z techto dat rict.",
                    maxLag * m.Dt));
            else
            {
                // Nezavislych vzorku je doba / (2 T_d): integral autokorelace AR(1) pres obe strany je 2T.
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => NAMERENY T_d ~ {0:F1} s   (nezavislych vzorku ~{1:F0})", tDecorr, m.Doba / (2 * tDecorr)));
                if (m.Doba / (2 * tDecorr) < 10)
                    Console.WriteLine("  ⚠️ Pod deseti nezavislymi vzorky je to orientacni cislo, ne mereni.");
            }
            RychlaSlozka(m, "  ");
            Console.WriteLine();
            string k = Kalibrace(m, meritko, tDecorr);
            return (tDecorr, k);
        }

        /// <summary>
        /// Referencni draha pro A2b: zdroj kurzu (draha je z kol), nebo <see cref="Doppler"/> =
        /// draha z rychlosti GPS bez kol a gyra. <see cref="Prubeh"/> je integrovany kurz,
        /// <see cref="Mezery"/> mista, kde zdroj mlcel.
        /// </summary>
        private sealed class ZdrojKurzu
        {
            public string Nazev;
            public string Popis;
            public bool Doppler;
            public bool Dostupny;
            public readonly List<(double T, double V)> Prubeh = new List<(double T, double V)>();
            public readonly List<(double A, double B)> Mezery = new List<(double A, double B)>();
        }

        /// <summary>
        /// Rychlost z prijimace (Doppler) po osach ENU [m/s]; NaN, kdyz chybi rychlost nebo kurz
        /// pohybu. Bere se jen <see cref="GPSState.Speed"/> (od prijimace), NE
        /// <see cref="GPSState.DynamicSpeed"/> - ta je dopoctena z poloh, tedy z toho, co se meri.
        /// </summary>
        private static double RychlostVE(GPSState p)
            => p.Speed.HasValue && p.DynamicOrientation.HasValue ? p.Speed.Value * Math.Cos(p.DynamicOrientation.Value) : double.NaN;

        private static double RychlostVN(GPSState p)
            => p.Speed.HasValue && p.DynamicOrientation.HasValue ? p.Speed.Value * Math.Sin(p.DynamicOrientation.Value) : double.NaN;

        /// <summary>
        /// Referencni drahy v poradi gyro, gyroraw, fuze, kola, doppler. Klid na odhad biasu
        /// syroveho gyra se bere z ENKODERU (5 s, kdy se ani jedno kolo nepohne) - kurz ani poloha
        /// fuze do toho nevstupuji. Pozor: klid se pozna z drahy KOL v absolutni hodnote, ne
        /// z prumeru kol - otaceni na miste ma dL = −dR a prumer by ho prohlasil za stani.
        ///
        /// <para>⚠️ <b>Kompenzovane gyro neni automaticky lepsi nez syrove.</b> FreeRun 1. 10. 2026:
        /// odhad biasu z VPE (<c>Gyro − UncompGyro</c>) sice sledoval zmenu biasu (2 547 → 2 060 deg/h,
        /// <c>vn100</c> blok 6), ale byl o ~100–180 deg/h vedle, a mrtvy odhad s nim mel zbytek RMS 10,6 m,
        /// kdezto syrove gyro minus bias z oken klidu (mezi okny linearne) 2,4 m. Proto "auto".</para>
        /// </summary>
        private static List<ZdrojKurzu> ZdrojeKurzu(List<(double T, double L, double R)> enc,
                                                    List<(double T, double Gz, double GzRaw)> imu,
                                                    List<(double T, double X, double Y, double Th, double V, double W, double Pxx, double Pyy)> st,
                                                    List<GpsFix> gps)
        {
            const double MaxMezeraImu = 0.25, MaxMezeraFuze = 0.5, KlidS = 5.0;
            const double D = 180.0 / Math.PI * 3600.0;   // rad/s -> deg/h
            var zdroje = new List<ZdrojKurzu>();
            var im = imu.OrderBy(x => x.T).ToList();

            var drahaKol = new List<(double T, double S)>();
            double acc = 0;
            for (int i = 0; i < enc.Count; i++)
            {
                if (i > 0) acc += 0.5 * (Math.Abs(enc[i].L - enc[i - 1].L) + Math.Abs(enc[i].R - enc[i - 1].R));
                drahaKol.Add((enc[i].T, acc));
            }
            var klid = Stani(drahaKol, KlidS, DefaultStandTolM);
            bool VKlidu(double t)
            {
                foreach (var (a, b) in klid)
                    if (t >= a + 1.0 && t <= b - 1.0) return true;
                return false;
            }

            double klidS = klid.Sum(k => Math.Max(0, k.B - k.A - 2.0));
            string kdeKlid = klid.Count == 0 ? "zadny"
                : string.Join(", ", klid.Take(6).Select(k => string.Format(CultureInfo.InvariantCulture, "{0:F0}-{1:F0} s", k.A, k.B)))
                  + (klid.Count > 6 ? $" a dalsich {klid.Count - 6}" : "");
            // Klid z enkoderu nestaci: 29. 9. 2026 15:16 se v "klidu" otacelo rukou o 55-151 deg a prumer
            // syroveho gyra vysel +5 057 deg/h misto ~-3 800. Vyradi se proto vzorky, kde kompenzovane
            // gyro hlasi otaceni proti svemu vlastnimu medianu v klidu (VPE muze mit offset, otaceni
            // je proti nemu skok), a bias se bere MEDIANEM.
            const double MaxOtaceni = 0.5 * Math.PI / 180.0;   // rad/s
            // Median zvlast pro kazde okno klidu: globalni median ma bod zlomu 50 % (Kolo 3 navrat:
            // 51 % vzorku "klidu" se otacelo) a offset VPE se mezi okny meni.
            // Okno, jehoz median kompenzovaneho gyra je nad 1 deg/s, se pro bias syroveho gyra nebere vubec:
            // bud se otacelo cele (median je pak sama rychlost otaceni), nebo je vadna VPE (17. 9. -3,9 deg/s).
            const double MaxStredOkna = 1.0 * Math.PI / 180.0;   // rad/s
            var klidVse = new List<(double T, double Gz, double GzRaw)>();
            var klidCisty = new List<(double T, double Gz, double GzRaw)>();
            int oknaVyrazena = 0;
            foreach (var (ka, kb) in klid)
            {
                var okno = im.Where(x => x.T >= ka + 1.0 && x.T <= kb - 1.0).ToList();
                if (okno.Count == 0) continue;
                double medOkna = Median(okno.Select(x => x.Gz));
                klidVse.AddRange(okno);
                if (Math.Abs(medOkna) > MaxStredOkna) { oknaVyrazena++; continue; }
                klidCisty.AddRange(okno.Where(x => Math.Abs(x.Gz - medOkna) < MaxOtaceni));
            }
            int otaceni = klidVse.Count - klidCisty.Count;
            var gzKlid = klidCisty.Select(x => x.Gz).ToList();
            var gyro = Integruj("gyro", im.Select(x => (x.T, x.Gz)).ToList(), MaxMezeraImu);
            gyro.Dostupny = gyro.Prubeh.Count >= 2;
            gyro.Popis = gzKlid.Count >= 100
                ? string.Format(CultureInfo.InvariantCulture,
                    "gyro VN100 (Z, kompenzovane odhadem biasu z VPE); v klidu median {0:F1} deg/h ({1} vzorku,"
                    + " {2} vyrazeno pro otaceni)",
                    Median(gzKlid) * D, gzKlid.Count, otaceni)
                : "gyro VN100 (Z, kompenzovane odhadem biasu z VPE); klidu malo, bias neovereny";
            zdroje.Add(gyro);

            var raw = im.Where(x => !double.IsNaN(x.GzRaw)).ToList();
            // Bias syroveho gyra: median kazdeho okna klidu zvlast a mezi okny LINEARNE (mimo ne
            // konstantne). Syrovy bias jde s teplotou (FreeRun 1. 10.: okno na zacatku a na konci se
            // lisi o stovky deg/h), takze jedna hodnota pro celou jizdu je spatne - a median pres obe
            // okna by vybral to, ktere ma vic vzorku.
            var oknaBias = new List<(double T, double B)>();
            foreach (var (a, b) in klid)
            {
                var vz = klidCisty.Where(x => x.T >= a + 1.0 && x.T <= b - 1.0 && !double.IsNaN(x.GzRaw))
                                  .Select(x => x.GzRaw).ToList();
                if (vz.Count >= 100) oknaBias.Add((0.5 * (a + b), Median(vz)));
            }
            double BiasRaw(double t)
            {
                if (oknaBias.Count == 0) return 0.0;
                if (t <= oknaBias[0].T) return oknaBias[0].B;
                if (t >= oknaBias[oknaBias.Count - 1].T) return oknaBias[oknaBias.Count - 1].B;
                int i = 1;
                while (oknaBias[i].T < t) i++;
                double u = (t - oknaBias[i - 1].T) / (oknaBias[i].T - oknaBias[i - 1].T);
                return oknaBias[i - 1].B + u * (oknaBias[i].B - oknaBias[i - 1].B);
            }
            var gyroRaw = Integruj("gyroraw", raw.Select(x => (x.T, x.GzRaw - BiasRaw(x.T))).ToList(), MaxMezeraImu);
            gyroRaw.Popis = raw.Count < 2
                ? "syrove gyro VN100 (UncompGyro) nese az IMUState verze 5 (od 24. 9. 2026)"
                : oknaBias.Count > 0
                    ? string.Format(CultureInfo.InvariantCulture,
                        "syrove gyro VN100 (UncompGyro) minus bias z oken klidu (median okna, mezi okny linearne):"
                        + " {0} oken, bias po oknech [{1}] deg/h{5} (klid {3:F0} s: {4})",
                        oknaBias.Count,
                        string.Join(", ", oknaBias.Take(8).Select(x => (x.B * D).ToString("F0", CultureInfo.InvariantCulture)))
                            + (oknaBias.Count > 8 ? ", ..." : ""),
                        0, klidS, kdeKlid,
                        oknaVyrazena > 0 ? $", {oknaVyrazena} oken vyrazeno (median komp. gyra nad 1 deg/s)" : "")
                    : "syrove gyro VN100 (UncompGyro) BEZ odectu biasu - v zaznamu neni klid";
            gyroRaw.Dostupny = gyroRaw.Prubeh.Count >= 2;
            zdroje.Add(gyroRaw);

            var fuze = Integruj("fuze", st.OrderBy(x => x.T).Select(x => (x.T, x.W)).ToList(), MaxMezeraFuze);
            fuze.Popis = "integral uhlove rychlosti z fuze (ne kurz fuze - ten nese GPS kurz, koridor a kompas;"
                         + " do omega se pres kovarianci dostanou taky, slabe)";
            fuze.Dostupny = fuze.Prubeh.Count >= 2;
            zdroje.Add(fuze);

            var kola = new ZdrojKurzu
            {
                Nazev = "kola",
                Popis = "rozdil kol / rozchod (do 13. 10. 2026 jediny zdroj; nese prokluz pri otaceni)",
            };
            double rozchod = ARBot.Common.Configuration.Profile.Rozchod;
            double th = 0;
            if (enc.Count > 0) kola.Prubeh.Add((enc[0].T, 0));
            for (int i = 1; i < enc.Count; i++)
            {
                th += ((enc[i].R - enc[i - 1].R) - (enc[i].L - enc[i - 1].L)) / rozchod;
                kola.Prubeh.Add((enc[i].T, th));
            }
            kola.Dostupny = kola.Prubeh.Count >= 2;
            zdroje.Add(kola);

            int sRychlosti = gps.Count(g => g.Platny && !double.IsNaN(g.VE));
            zdroje.Add(new ZdrojKurzu
            {
                Nazev = "doppler",
                Doppler = true,
                Dostupny = sRychlosti >= 2,
                Popis = "draha z RYCHLOSTI GPS (Doppler: Speed a kurz pohybu) bez kol a gyra - nezavisla na"
                        + " odometrii, ale ne na prijimaci (jeho filtr polohu s rychlosti svazuje); jen kontrola,"
                        + " auto ji nevybira",
            });
            return zdroje;
        }

        /// <summary>
        /// Integral uhlove rychlosti (lichobeznik). Mezera delsi nez <paramref name="maxMezera"/>
        /// se neintegruje a zapise se do <see cref="ZdrojKurzu.Mezery"/> - usek se na ni deli.
        /// </summary>
        private static ZdrojKurzu Integruj(string nazev, List<(double T, double W)> w, double maxMezera)
        {
            var z = new ZdrojKurzu { Nazev = nazev };
            if (w.Count < 2) return z;
            double th = 0;
            z.Prubeh.Add((w[0].T, 0));
            for (int i = 1; i < w.Count; i++)
            {
                double dt = w[i].T - w[i - 1].T;
                if (dt > maxMezera) z.Mezery.Add((w[i - 1].T, w[i].T));
                else if (dt > 0) th += 0.5 * (w[i].W + w[i - 1].W) * dt;
                z.Prubeh.Add((w[i].T, th));
            }
            return z;
        }

        /// <summary>
        /// Mista, kde se souvisly usek jizdy musi rozdelit: (A, B) je vynechany interval.
        /// <list type="bullet">
        /// <item>mezera v enkoderech nad 0,5 s a za ni 1 s ochrany - po zaseku prijde davka rámců
        /// z bufferu s razitky prichodu &lt; 1 ms od sebe (<c>hw-motor-davka-po-zaseku</c>), takze
        /// casy v ni neplati;</item>
        /// <item>nemozny skok enkoderu: kolo rychleji nez 3 m/s, pocitano aspon pres 50 ms
        /// (jitter razitek by jinak delal z 12 mm za 1 ms „12 m/s");</item>
        /// <item>mezera ve zdroji kurzu (gyro 0,25 s, fuze 0,5 s), taky s ochranou;</item>
        /// <item>mezera mezi pouzitelnymi fixy GPS (platnymi; s branou jen pustenymi) nad 1 s, taky
        /// s ochranou - autokorelace se pocita po vzorcich, takze mezera by posunula lag proti casu,
        /// a fixy z bufferu po zaseku nesou razitka prichodu stejne jako enkodery.</item>
        /// </list>
        /// </summary>
        private static List<(double A, double B, string Duvod)> Preruseni(List<(double T, double L, double R)> enc,
                                                                          ZdrojKurzu kurz, List<GpsFix> gps)
        {
            const double MaxMezeraEnc = 0.5, MaxRychlostKola = 3.0, MinDtRychlosti = 0.05;
            const double MaxMezeraGps = 1.0, Ochrana = 1.0;
            var p = new List<(double A, double B, string Duvod)>();
            for (int i = 1; i < enc.Count; i++)
            {
                double dt = enc[i].T - enc[i - 1].T;
                double skok = Math.Max(Math.Abs(enc[i].L - enc[i - 1].L), Math.Abs(enc[i].R - enc[i - 1].R));
                if (dt > MaxMezeraEnc)
                    p.Add((enc[i - 1].T, enc[i].T + Ochrana, string.Format(CultureInfo.InvariantCulture,
                        "mezera enkoderu {0:F2} s (kolo za ni {1:F2} m)", dt, skok)));
                else if (skok / Math.Max(dt, MinDtRychlosti) > MaxRychlostKola)
                    p.Add((enc[i - 1].T, enc[i].T + Ochrana, string.Format(CultureInfo.InvariantCulture,
                        "skok enkoderu {0:F2} m za {1:F0} ms", skok, dt * 1000)));
            }
            foreach (var (a, b) in kurz.Mezery)
                p.Add((a, b + Ochrana, string.Format(CultureInfo.InvariantCulture,
                    "mezera ve zdroji kurzu '{0}' {1:F2} s", kurz.Nazev, b - a)));

            double tPred = double.NaN;
            foreach (var g in gps.Where(g => g.Platny).OrderBy(g => g.T))
            {
                if (!double.IsNaN(tPred) && g.T - tPred > MaxMezeraGps)
                    p.Add((tPred, g.T + Ochrana, string.Format(CultureInfo.InvariantCulture,
                        "mezera mezi pouzitelnymi fixy GPS {0:F1} s", g.T - tPred)));
                tPred = g.T;
            }
            return p.OrderBy(x => x.A).ToList();
        }

        /// <summary>Vysledek A2b pro jeden zdroj kurzu.</summary>
        private sealed class MereniA2b
        {
            public int Pouzito, Neplatnych, Odmitnutych, VyrazenoBranou;
            /// <summary>Zbytek u fixu, ktere brana fuze pustila / odmitla (A2b je jinak bere vsechny).</summary>
            public readonly Stats ZbytekPustenych = new Stats("zbytek - fix pusteny branou");
            public readonly Stats ZbytekOdmitnutych = new Stats("zbytek - fix odmitnuty branou");
            public double Doba, Draha, Dt = 0.1;
            public readonly List<List<double>> SegE = new List<List<double>>();
            public readonly List<List<double>> SegN = new List<List<double>>();
            /// <summary>Casy fixu a mrtvy odhad v nich, po pouzitych usecich - pro kalibraci.</summary>
            public readonly List<(double[] T, List<(double X, double Y)> A)> Geometrie
                = new List<(double[] T, List<(double X, double Y)> A)>();
            public readonly Stats Zbytek = new Stats("zbytek");
            public readonly Stats Meritko = new Stats("meritko");
            public readonly List<string> Radky = new List<string>();
            public readonly List<string> Preruseni = new List<string>();
        }

        /// <summary>A2b pro jeden zdroj kurzu: rozdeleni jizd, mrtvy odhad, zarovnani, zbytky.</summary>
        private static MereniA2b Mer(ZdrojKurzu kurz, List<GpsFix> gps, List<(double T, double L, double R)> enc,
                                     List<(double A, double B)> jizdy, double minJizdaS, double minDrahaM,
                                     bool meritko, bool jenBrana)
        {
            var m = new MereniA2b();
            // Fixy, ze kterych se meri: platne, a s --brana=1 jen ty, ktere brana fuze pustila.
            var pouzitelne = gps.Where(g => g.Platny && (!jenBrana || g.Reject == null)).ToList();
            var prer = Preruseni(enc, kurz, pouzitelne);
            var useky = new List<(double A, double B, bool Kratky)>();
            // Usek se orizne na pokryti zdroje kurzu: kdyz stav fuze skonci o 0,15 s driv nez
            // enkodery (27. 9. 17:30), nesmi kvuli tomu vypadnout cely usek.
            double pokrytiOd = double.NegativeInfinity, pokrytiDo = double.PositiveInfinity;
            if (!kurz.Doppler && kurz.Prubeh.Count >= 2)
            {
                pokrytiOd = kurz.Prubeh[0].T;
                pokrytiDo = kurz.Prubeh[kurz.Prubeh.Count - 1].T;
            }
            void Pridej(double x, double y)
            {
                double x0 = x, y0 = y;
                x = Math.Max(x, pokrytiOd);
                y = Math.Min(y, pokrytiDo);
                if (y > x) useky.Add((x, y, y - x < minJizdaS));
                else m.Radky.Add(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0}-{1,6:F0}  {2,6:F1} s                                              mimo pokryti zdroje kurzu",
                    x0, y0, y0 - x0));
            }
            foreach (var (a, b) in jizdy)
            {
                double zac = a;
                foreach (var p in prer)
                {
                    if (p.B <= a || p.A >= b) continue;
                    m.Preruseni.Add(string.Format(CultureInfo.InvariantCulture, "    {0,7:F1} s  {1}", p.A, p.Duvod));
                    if (p.A > zac) Pridej(zac, p.A);
                    zac = Math.Max(zac, p.B);
                }
                if (b > zac) Pridej(zac, b);
            }

            int nFix = 0; double spanFix = 0;
            foreach (var (a, b, kratky) in useky)
            {
                var e = enc.Where(x => x.T >= a && x.T <= b).ToList();
                var f = pouzitelne.Where(g => g.T >= a && g.T <= b).OrderBy(g => g.T).ToList();
                int neplatnych = gps.Count(g => g.T >= a && g.T <= b && !g.Platny);
                int vyrazeno = jenBrana ? gps.Count(g => g.T >= a && g.T <= b && g.Platny && g.Reject != null) : 0;

                double draha = 0;
                for (int i = 1; i < e.Count; i++)
                    draha += Math.Abs(0.5 * ((e[i].L - e[i - 1].L) + (e[i].R - e[i - 1].R)));

                string verdikt = kratky ? $"kratky po deleni ({b - a:F0} < {minJizdaS:F0} s)"
                               : e.Count < 20 ? "malo vzorku enkoderu"
                               : f.Count < 60 ? $"malo fixu ({f.Count} < 60)"
                               : draha < minDrahaM ? $"kratka draha ({draha:F1} < {minDrahaM:F0} m)"
                               : "POUZITO";
                double fi = double.NaN, s = double.NaN;

                if (verdikt == "POUZITO")
                {
                    var dr = new List<(double T, double X, double Y)>();
                    double x = 0, y = 0;
                    bool ok;
                    if (kurz.Doppler)
                    {
                        // Draha z rychlosti prijimace (lichobeznik mezi fixy), bez kol a gyra.
                        var fv = f.Where(g => !double.IsNaN(g.VE)).ToList();
                        ok = fv.Count >= 60;
                        if (ok)
                        {
                            dr.Add((fv[0].T, 0, 0));
                            for (int i = 1; i < fv.Count; i++)
                            {
                                double dtv = fv[i].T - fv[i - 1].T;
                                x += 0.5 * (fv[i].VE + fv[i - 1].VE) * dtv;
                                y += 0.5 * (fv[i].VN + fv[i - 1].VN) * dtv;
                                dr.Add((fv[i].T, x, y));
                            }
                        }
                    }
                    else
                    {
                        // Mrtvy odhad: pocatek (0,0), draha z prumeru kol, kurz ze zdroje uprostred kroku.
                        ok = Interpoluj1(kurz.Prubeh, e[0].T, out double thPred);
                        dr.Add((e[0].T, 0, 0));
                        for (int i = 1; i < e.Count && ok; i++)
                        {
                            if (!Interpoluj1(kurz.Prubeh, e[i].T, out double th)) { ok = false; break; }
                            double ds = 0.5 * ((e[i].L - e[i - 1].L) + (e[i].R - e[i - 1].R));
                            double thm = 0.5 * (thPred + th);
                            x += ds * Math.Cos(thm);
                            y += ds * Math.Sin(thm);
                            thPred = th;
                            dr.Add((e[i].T, x, y));
                        }
                    }
                    if (!ok) verdikt = kurz.Doppler ? "malo fixu s rychlosti" : "zdroj kurzu usek nepokryva";
                    else
                    {
                        // GPS v lokalni ENU kotvene na zacatku useku + mrtvy odhad v tychz casech.
                        double lat0 = f[0].Lat, lon0 = f[0].Lon;
                        var A = new List<(double X, double Y)>();
                        var B = new List<(double X, double Y)>();
                        var ts = new List<double>();
                        var odm = new List<bool>();
                        foreach (var g in f)
                        {
                            if (!Interpoluj(dr, g.T, out double dx, out double dy)) continue;
                            A.Add((dx, dy));
                            B.Add(Enu(lat0, lon0, g.Lat, g.Lon));
                            ts.Add(g.T);
                            odm.Add(g.Reject != null);
                        }
                        if (A.Count < 60) verdikt = $"malo fixu v dosahu odometrie ({A.Count} < 60)";
                        else
                        {
                            var z = Zarovnej(A, B, meritko);
                            fi = z.Fi; s = z.S;
                            for (int i = 0; i < z.E.Count; i++)
                            {
                                double r = Math.Sqrt(z.E[i] * z.E[i] + z.N[i] * z.N[i]);
                                m.Zbytek.Add(r);
                                (odm[i] ? m.ZbytekOdmitnutych : m.ZbytekPustenych).Add(r);
                                if (odm[i]) m.Odmitnutych++;
                            }
                            m.SegE.Add(z.E); m.SegN.Add(z.N);
                            m.Geometrie.Add((ts.ToArray(), A));
                            m.Meritko.Add(s);
                            m.Doba += b - a;
                            m.Draha += draha;
                            m.Neplatnych += neplatnych;
                            m.VyrazenoBranou += vyrazeno;
                            m.Pouzito++;
                            nFix += ts.Count - 1;
                            spanFix += ts[ts.Count - 1] - ts[0];
                        }
                    }
                }

                m.Radky.Add(string.Format(CultureInfo.InvariantCulture,
                    "  {0,6:F0}-{1,6:F0}  {2,6:F1} s  {3,6:F1} m  {4,5}   {5,8}  {6,7}   {7}",
                    a, b, b - a, draha, f.Count,
                    double.IsNaN(fi) ? "-" : (fi * 180 / Math.PI).ToString("F1", CultureInfo.InvariantCulture),
                    double.IsNaN(s) ? "-" : s.ToString("F4", CultureInfo.InvariantCulture),
                    verdikt));
            }
            if (nFix > 0 && spanFix > 0) m.Dt = spanFix / nFix;
            return m;
        }

        /// <summary>
        /// Tuhe zarovnani mrtveho odhadu <paramref name="A"/> na GPS <paramref name="B"/> (2D
        /// Procrustes: posun, rotace a volitelne meritko) a zbytek <c>B − T(A)</c> po osach.
        /// Tataz funkce bezi nad daty i v kalibraci - jinak by kalibrace merila jine meridlo.
        /// </summary>
        private static (List<double> E, List<double> N, double Fi, double S) Zarovnej(
            List<(double X, double Y)> A, List<(double X, double Y)> B, bool meritko)
        {
            int n = A.Count;
            double ax = 0, ay = 0, bx = 0, by = 0;
            for (int i = 0; i < n; i++) { ax += A[i].X; ay += A[i].Y; bx += B[i].X; by += B[i].Y; }
            ax /= n; ay /= n; bx /= n; by /= n;

            double sxx = 0, sxy = 0, saa = 0;
            for (int i = 0; i < n; i++)
            {
                double px = A[i].X - ax, py = A[i].Y - ay;
                double qx = B[i].X - bx, qy = B[i].Y - by;
                sxx += px * qx + py * qy;
                sxy += px * qy - py * qx;
                saa += px * px + py * py;
            }
            double fi = Math.Atan2(sxy, sxx);
            // Meritko pri dane rotaci: (cos fi * sxx + sin fi * sxy) / saa = |(sxx, sxy)| / saa.
            double s = meritko && saa > 1e-9 ? Math.Sqrt(sxx * sxx + sxy * sxy) / saa : 1.0;
            double c = s * Math.Cos(fi), sn = s * Math.Sin(fi);

            var re = new List<double>(n); var rn = new List<double>(n);
            for (int i = 0; i < n; i++)
            {
                double px = A[i].X - ax, py = A[i].Y - ay;
                re.Add((B[i].X - bx) - (c * px - sn * py));
                rn.Add((B[i].Y - by) - (sn * px + c * py));
            }
            return (re, rn, fi, s);
        }

        /// <summary>
        /// <b>Kolik z naměřeného T_d sežere samo měřidlo?</b> Monte Carlo na TÉŽE geometrii: do
        /// každého použitého úseku se místo GPS vloží mrtvý odhad plus chyba AR(1) se ZNÁMÝM T_d
        /// (exponenciální autokorelace, obě osy nezávisle), projde tímtéž zarovnáním (u stání
        /// odečtem průměru) a týmž odečtem přechodu 1/e. Výsledek je <b>pásmo</b> skutečných T_d,
        /// u kterých naměřená hodnota leží mezi p10 a p90.
        ///
        /// <para><b>Od 13. 10. 2026 (po ověření) cenzurovaně:</b> první verze měla tabulku jen do 300 s
        /// a 24 pokusů, „strop" brala jako nejvyšší medián a nad ním hlásila „není AR(1)". Jenže
        /// medián roste i nad 300 s, jedna realizace se má srovnat s rozdělením, ne s mediánem,
        /// a bodový převod v ploché části křivky určoval šum (posun mediánu o 1 s posunul výsledek
        /// o desítky sekund). Teď tabulka sahá po náhodnou procházku (T → ∞), pokusů je 48, bod se
        /// tiskne jen tam, kde medián prokazatelně roste, a pásmo sahající k náhodné procházce je
        /// „T_d ≥ dolní hrana" (shora neomezeno). „Mimo model" je až nad p90 všech řádků.</para>
        ///
        /// <para><b>Druhé kolo ověření (13. 10. 2026):</b> hrany pásma se brala jako množina řádků
        /// (šum 48 pokusů ji trhal: „180-600 s" s vypadlými řádky, „90-90 s"), plató dávalo falešné
        /// horní meze a pásmo s řádkem 10 s se tisklo jako „≥ 10 s", ač pod 10 s se netestuje.
        /// Teď: kvantily monotonizované přes T, hrany interpolované mezi řádky, horní hrana se nehlásí
        /// v plató (medián nerozlišitelný od náhodné procházky), pásmo od prvního řádku je „zdola
        /// neomezeno", 96 pokusů.</para>
        ///
        /// <para>⚠️ Platí pro model AR(1) a pro referenci bez vlastní chyby: chyba reference (drift
        /// odometrie, šum rychlosti z Doppleru) se v datech přičte a T_d prodlouží. Pseudonáhoda je se
        /// semínkem — tentýž záznam dá tutéž tabulku.</para>
        /// </summary>
        private static string Kalibrace(MereniA2b m, bool meritko, double tNamereno, bool jenPrumer = false)
        {
            double[] skutecne = { 10, 20, 30, 45, 60, 90, 120, 180, 300, 600, 1000, double.PositiveInfinity };
            const int Pokusu = 96;
            var rnd = new Random(20261013);
            double sigma = Math.Sqrt(0.5) * Math.Max(0.1, RmsVse(m.SegE, m.SegN));

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  KALIBRACE MERIDLA na teze geometrii ({0} pokusu na radek, chyba AR(1), {1}):",
                Pokusu, jenPrumer ? "odecet prumeru segmentu" : (meritko ? "zarovnani s meritkem" : "zarovnani bez meritka")));
            Console.WriteLine("    skutecny T_d   namereno p50   (p10 - p90)   bez prechodu");
            var tab = new List<(double Skut, double P10, double P50, double P90)>();
            foreach (double tSkut in skutecne)
            {
                var vys = new List<double>();
                int bez = 0;
                for (int k = 0; k < Pokusu; k++)
                {
                    var e = new List<List<double>>();
                    var n = new List<List<double>>();
                    foreach (var (ts, a) in m.Geometrie)
                    {
                        var b = new List<(double X, double Y)>(a.Count);
                        double ex = sigma * Gauss(rnd), ey = sigma * Gauss(rnd);
                        b.Add((a[0].X + ex, a[0].Y + ey));
                        for (int i = 1; i < a.Count; i++)
                        {
                            double dtv = ts[i] - ts[i - 1];
                            if (double.IsInfinity(tSkut))
                            {
                                // Nahodna prochazka: meritko nehraje roli (autokorelace je normovana).
                                double q = sigma * Math.Sqrt(Math.Max(0, dtv) / 60.0);
                                ex += q * Gauss(rnd);
                                ey += q * Gauss(rnd);
                            }
                            else
                            {
                                double fi = Math.Exp(-dtv / tSkut);
                                double q = sigma * Math.Sqrt(Math.Max(0, 1 - fi * fi));
                                ex = fi * ex + q * Gauss(rnd);
                                ey = fi * ey + q * Gauss(rnd);
                            }
                            b.Add((a[i].X + ex, a[i].Y + ey));
                        }
                        if (jenPrumer)
                        {
                            double mx = b.Average(v => v.X), my = b.Average(v => v.Y);
                            e.Add(b.Select(v => v.X - mx).ToList());
                            n.Add(b.Select(v => v.Y - my).ToList());
                        }
                        else
                        {
                            var z = Zarovnej(a, b, meritko);
                            e.Add(z.E);
                            n.Add(z.N);
                        }
                    }
                    double td = Prechod(e, n, m.Dt);
                    if (double.IsNaN(td)) { bez++; vys.Add(double.PositiveInfinity); }
                    else vys.Add(td);
                }
                vys.Sort();
                double p10 = Kvantil(vys, 0.1), p50 = Kvantil(vys, 0.5), p90 = Kvantil(vys, 0.9);
                tab.Add((tSkut, p10, p50, p90));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,10}     {1,8} s    ({2} - {3})    {4,3}/{5}",
                    double.IsInfinity(tSkut) ? "nahodna pr." : tSkut.ToString("F0", CultureInfo.InvariantCulture) + " s",
                    F1(p50), F1(p10), F1(p90), bez, Pokusu));
            }

            if (double.IsNaN(tNamereno))
            {
                Console.WriteLine();
                return "rho neklesla pod 1/e - T_d delsi, nez data unesou";
            }

            // Obalky p10 / p50 / p90 monotonizovane (kumulativni maximum pres rostouci T): delsi T nesmi
            // dat nizsi kvantil. Bez toho sum Monte Carla delal z pasma nesouvisle kusy (13. 10. 2026,
            // druhe kolo overeni: „180-600 s" s vypadlymi radky, „90-90 s").
            int nr = tab.Count;
            var m10 = new double[nr];
            var m50 = new double[nr];
            var m90 = new double[nr];
            var sum50 = new double[nr];
            double k10 = double.NegativeInfinity, k50 = double.NegativeInfinity, k90 = double.NegativeInfinity;
            for (int i = 0; i < nr; i++)
            {
                k10 = Math.Max(k10, tab[i].P10); m10[i] = k10;
                k50 = Math.Max(k50, tab[i].P50); m50[i] = k50;
                k90 = Math.Max(k90, tab[i].P90); m90[i] = k90;
                // sd medianu ~ 1,25 sd / sqrt(n), sd z p10-p90 jako u normalniho rozdeleni
                sum50[i] = double.IsInfinity(tab[i].P90) ? double.PositiveInfinity
                         : 1.25 * (tab[i].P90 - tab[i].P10) / 2.563 / Math.Sqrt(Pokusu);
            }

            // Hrana mezi radky: linearne v T; k radku nahodne prochazky se neinterpoluje.
            double Hrana(int i, double[] obalka)
            {
                if (i <= 0) return tab[0].Skut;
                if (double.IsInfinity(tab[i].Skut)) return double.PositiveInfinity;
                double d = obalka[i] - obalka[i - 1];
                double u = d > 1e-9 ? (tNamereno - obalka[i - 1]) / d : 1.0;
                return tab[i - 1].Skut + Math.Max(0, Math.Min(1, u)) * (tab[i].Skut - tab[i - 1].Skut);
            }

            // Plato: od radku, kde se monotonni median lisi od SYROVEHO medianu nahodne prochazky mene nez
            // o dva sumy - tam meridlo na teto geometrii T_d nerozlisi a dolni hrana uvnitr plato je jen
            // sum p90 mezi radky (treti kolo overeni: „>= ~367 s, od ~180 s nerozlisi").
            int plato = nr - 1;
            for (int i = 0; i < nr - 1; i++)
                if (Math.Abs(tab[nr - 1].P50 - m50[i]) < 2 * Math.Max(sum50[i], sum50[nr - 1])) { plato = i; break; }

            // Obalka p10 pro HORNI hranu shora (minimum pres vsechny delsi T): radek je zamitnuty, jen kdyz
            // jsou svym p10 zamitnute i vsechny delsi. Kumulativni maximum zdola by jeden zasumeny vysoky
            // p10 nechal zamitnout i delsi radky, ktere zamitnute nejsou - horni hrana by vysla moc nizko.
            var r10 = new double[nr];
            double kr = double.PositiveInfinity;
            for (int i = nr - 1; i >= 0; i--) { kr = Math.Min(kr, tab[i].P10); r10[i] = kr; }

            if (tNamereno > m90[nr - 1])
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => namerenych {0:F1} s je NAD p90 vsech radku vcetne nahodne prochazky ({1}) - jednostranny test",
                    tNamereno, F1(m90[nr - 1])));
                Console.WriteLine("     na hladine ~10 %: jedna jizda nad hranou dukaz neni, opakovane a vyrazne ano.");
                Console.WriteLine(jenPrumer
                    ? "     U stani (bez reference) to znamena, ze chyba GPS je hladsi, nez AR(1) i nahodna prochazka pripousti"
                      + " (vyhlazuje ji filtr prijimace), nebo se antena hybala."
                    : "     Pak ma zbytek slozku, kterou AR(1) ani nahodna prochazka nepopise - nejspis drift reference.");
                Console.WriteLine();
                return string.Format(CultureInfo.InvariantCulture, "nad p90 vsech radku ({0}) - podezreni na slozku mimo model",
                    F1(m90[nr - 1]));
            }
            if (tNamereno < m10[0])
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => namerenych {0:F1} s je pod p10 radku 10 s: skutecny T_d pod 10 s", tNamereno));
                Console.WriteLine();
                return "skutecny T_d pod 10 s";
            }

            // Dolni hrana: kde monotonni p90 dosahne namerene hodnoty; padne-li na prvni radek, pod 10 s se
            // netestovalo - zdola neomezeno; padne-li do plato, hlasi se zacatek plato (hrana dal je sum).
            // Horni: kde obalka p10 shora namerenou hodnotu prekroci; radek nahodne prochazky v pasmu =
            // shora neomezeno.
            int iDol = Array.FindIndex(m90, x => x >= tNamereno);
            bool dolVPlatu = iDol > plato;
            double dol = iDol <= 0 ? double.NaN : dolVPlatu ? tab[plato].Skut : Hrana(iDol, m90);
            int iHor = Array.FindIndex(r10, x => x > tNamereno);
            double hor = iHor < 0 || iHor >= nr - 1 ? double.PositiveInfinity : Hrana(iHor, r10);
            if (!double.IsNaN(dol) && hor < dol) hor = dol;

            // Bod: median, jen kde mezi sousednimi radky prokazatelne roste.
            string bod = null;
            int iBod = Array.FindIndex(m50, x => x >= tNamereno);
            if (iBod > 0 && !double.IsInfinity(tab[iBod].Skut)
                && m50[iBod] - m50[iBod - 1] > 2 * Math.Max(sum50[iBod], sum50[iBod - 1]))
                bod = string.Format(CultureInfo.InvariantCulture, "~{0:F0} s", Hrana(iBod, m50));

            string F0(double x) => x.ToString("F0", CultureInfo.InvariantCulture);
            string vysledek;
            if (double.IsNaN(dol) && double.IsInfinity(hor))
                vysledek = "meridlo na teto geometrii T_d nerozlisi (pasmo pres celou tabulku)";
            else if (double.IsNaN(dol))
                vysledek = string.Format(CultureInfo.InvariantCulture,
                    "skutecny T_d <= ~{0} s (zdola neomezeno - pod 10 s se netestuje)", F0(hor));
            else if (double.IsInfinity(dol))
                vysledek = "skutecny T_d nad 1000 s (na urovni nahodne prochazky)";
            else if (dolVPlatu)
                vysledek = string.Format(CultureInfo.InvariantCulture,
                    "skutecny T_d >= ~{0} s (namerena hodnota lezi u p90 plato - od ~{0} s meridlo nerozlisi, hranicne"
                    + " nad p90 vsech radku)", F0(dol));
            else if (double.IsInfinity(hor))
                vysledek = string.Format(CultureInfo.InvariantCulture,
                    "skutecny T_d >= ~{0} s (shora neomezeno - od ~{1} meridlo na teto geometrii nerozlisi)",
                    F0(dol), double.IsInfinity(tab[plato].Skut) ? "1000 s" : F0(tab[plato].Skut) + " s");
            else
                vysledek = string.Format(CultureInfo.InvariantCulture, "skutecny T_d ~{0}-{1} s{2}",
                    F0(dol), F0(hor), bod != null ? ", median dava " + bod : "");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  => namerenych {0:F1} s: {1}  (pasmo p10-p90, hrany linearne mezi radky)", tNamereno, vysledek));
            Console.WriteLine();
            return vysledek;
        }

        /// <summary>
        /// <b>Kolik zbytku se dekoreluje RYCHLE?</b> Horni mez podilu rozptylu, ktery nese slozka
        /// s T_d do 5 s, resp. do 10 s: <c>1 − ρ(τ) ≥ f·(1 − e^(−τ/T))</c>, takze pro T ≤ τ je
        /// <c>f ≤ (1 − ρ(τ)) / (1 − 1/e)</c>. Mez plati bez ohledu na tvar pomale slozky (ta
        /// <c>1 − ρ</c> jen zvetsi) - je tedy konzervativni.
        ///
        /// <para>To je cislo, na kterem stoji decimace: jen tahle cast chyby se mezi fixy
        /// vzdalenymi pod 10 s prumeruje. Zbytek je pro filtr jedno mereni na desitky sekund.</para>
        /// </summary>
        private static void RychlaSlozka(MereniA2b m, string odsazeni)
        {
            if (m.SegE.Count == 0) return;
            double rms = RmsVse(m.SegE, m.SegN);
            double k = 1.0 - 1.0 / Math.E;
            double r5 = RhoV(m, 5.0), r10 = RhoV(m, 10.0);
            double f5 = Math.Min(1, Math.Max(0, (1 - r5) / k)), f10 = Math.Min(1, Math.Max(0, (1 - r10) / k));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "{0}rychla slozka: s T_d do 5 s nejvys {1:F0} % rozptylu zbytku (RMS do {2:F2} m), s T_d do 10 s"
                + " nejvys {3:F0} % ({4:F2} m); zbytek RMS {5:F2} m",
                odsazeni, 100 * f5, Math.Sqrt(f5) * rms, 100 * f10, Math.Sqrt(f10) * rms, rms));
        }

        /// <summary>Autokorelace zbytku (prumer os) na lagu nejblizsim <paramref name="tauS"/>.</summary>
        private static double RhoV(MereniA2b m, double tauS)
        {
            int lag = Math.Max(1, (int)Math.Round(tauS / m.Dt));
            return 0.5 * (Autokorelace(m.SegE, lag) + Autokorelace(m.SegN, lag));
        }

        /// <summary>
        /// Tytez jizdy s ostatnimi referencnimi drahami: kdyz se T_d mezi nimi lisi malo, na kurzu
        /// mrtveho odhadu nestoji. Zbytek je pro vsechny tataz chyba GPS plus drift dane drahy,
        /// takze mensi zbytek = lepsi mrtvy odhad (krome Doppleru, viz <see cref="ZdrojeKurzu"/>).
        /// </summary>
        private static void SrovnaniZdroju(List<ZdrojKurzu> zdroje, ZdrojKurzu hlavni,
                                           Dictionary<ZdrojKurzu, MereniA2b> vysledky, double tHlavni)
        {
            Console.WriteLine("  SROVNANI REFERENCNICH DRAH (tytez jizdy, deleni podle preruseni dane drahy):");
            Console.WriteLine("    draha     useku  doba [s]  zbytek p50 / p90 / RMS [m]  meritko p50  rho(5s) rho(10s)  T_d [s]");
            foreach (var z in zdroje)
            {
                if (!vysledky.TryGetValue(z, out var m))
                {
                    Console.WriteLine($"    {z.Nazev,-8}  v zaznamu neni");
                    continue;
                }
                bool ma = m.Pouzito > 0;
                double td = z == hlavni ? tHlavni : (ma ? Prechod(m.SegE, m.SegN, m.Dt) : double.NaN);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,-8}{1} {2,4}   {3,7:F0}    {4,6:F2} / {5,6:F2} / {6,6:F2}    {7,8}    {8,6}  {9,6}   {10,7}",
                    z.Nazev, z == hlavni ? "*" : " ", m.Pouzito, m.Doba,
                    ma ? m.Zbytek.Median : double.NaN,
                    ma ? m.Zbytek.Percentile(90) : double.NaN,
                    ma ? RmsVse(m.SegE, m.SegN) : double.NaN,
                    m.Meritko.Count > 0 ? m.Meritko.Median.ToString("F4", CultureInfo.InvariantCulture) : "-",
                    ma ? RhoV(m, 5).ToString("F3", CultureInfo.InvariantCulture) : "-",
                    ma ? RhoV(m, 10).ToString("F3", CultureInfo.InvariantCulture) : "-",
                    ma ? F1(td) : "-"));
            }
            Console.WriteLine("    (* = hlavni; T_d namereny, nekalibrovany; doppler je z tehoz prijimace jako poloha)");
            Console.WriteLine();
        }

        /// <summary>
        /// <b>Jak dobre zna GPS POSUN?</b> RMS prirustku zbytku za tau, <c>|r(t+tau) − r(t)|</c>,
        /// po referencnich drahach. Na kratkem tau odometrie jeste nedriftuje (gyro za 10 s
        /// desetiny stupne, kola ~1 % drahy), takze je to chyba POSUNU z GPS - a kdyz ji odometrie
        /// a Doppler vidi stejne, Doppler kratkodobou chybu GPS neschovava. Kde odometrie s tau
        /// roste a Doppler ne, je to drift odometrie.
        /// </summary>
        private static void Prirustky(List<ZdrojKurzu> zdroje, Dictionary<ZdrojKurzu, MereniA2b> vysledky)
        {
            double[] tau = { 1, 2, 5, 10, 20, 30, 60, 120 };
            Console.WriteLine("  PRIRUSTEK ZBYTKU ZA tau - RMS |r(t+tau) - r(t)| [m] (jak dobre GPS zna POSUN proti draze):");
            Console.WriteLine("    draha    " + string.Concat(tau.Select(t => string.Format(CultureInfo.InvariantCulture, "{0,8}", t + " s"))));
            foreach (var z in zdroje)
            {
                if (!vysledky.TryGetValue(z, out var m) || m.Pouzito == 0) continue;
                Console.WriteLine("    " + z.Nazev.PadRight(9) + string.Concat(tau.Select(t =>
                    string.Format(CultureInfo.InvariantCulture, "{0,8}", F2(Prirustek(m, t))))));
            }
            Console.WriteLine();
        }

        /// <summary>RMS prirustku zbytku (obe osy dohromady) za <paramref name="tauS"/>; NaN, kdyz je usek kratsi.</summary>
        private static double Prirustek(MereniA2b m, double tauS)
        {
            int lag = Math.Max(1, (int)Math.Round(tauS / m.Dt));
            double ss = 0;
            long n = 0;
            for (int k = 0; k < m.SegE.Count; k++)
            {
                var e = m.SegE[k];
                var no = m.SegN[k];
                for (int i = 0; i + lag < e.Count; i++)
                {
                    double de = e[i + lag] - e[i], dn = no[i + lag] - no[i];
                    ss += de * de + dn * dn;
                    n++;
                }
            }
            return n > 0 ? Math.Sqrt(ss / n) : double.NaN;
        }

        private static string F2(double v) =>
            double.IsNaN(v) ? "-" : v.ToString("F2", CultureInfo.InvariantCulture);

        private static double RmsVse(List<List<double>> e, List<List<double>> n)
        {
            double ss = 0; int k = 0;
            foreach (var v in e) foreach (var x in v) { ss += x * x; k++; }
            foreach (var v in n) foreach (var x in v) { ss += x * x; k++; }
            return k > 0 ? Math.Sqrt(2.0 * ss / k) : 0;
        }

        private static double Median(IEnumerable<double> v)
        {
            var l = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (l.Count == 0) return double.NaN;
            return l.Count % 2 == 1 ? l[l.Count / 2] : 0.5 * (l[l.Count / 2 - 1] + l[l.Count / 2]);
        }

        private static double Kvantil(List<double> serazene, double q)
        {
            if (serazene.Count == 0) return double.NaN;
            int i = (int)Math.Round(q * (serazene.Count - 1));
            return serazene[Math.Max(0, Math.Min(serazene.Count - 1, i))];
        }

        private static string F1(double v) =>
            double.IsNaN(v) ? "-" : double.IsInfinity(v) ? "> okno" : v.ToString("F1", CultureInfo.InvariantCulture);

        /// <summary>Normalni rozdeleni N(0,1) (Box-Muller).</summary>
        private static double Gauss(Random r)
        {
            double u1 = 1.0 - r.NextDouble(), u2 = r.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// <summary>Linearni interpolace prubehu v case; false mimo rozsah.</summary>
        private static bool Interpoluj1(List<(double T, double V)> l, double t, out double v)
        {
            v = 0;
            if (l.Count < 2 || t < l[0].T || t > l[l.Count - 1].T) return false;
            int lo = 0, hi = l.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (l[mid].T <= t) lo = mid; else hi = mid;
            }
            double dt = l[hi].T - l[lo].T;
            double u = dt > 1e-9 ? (t - l[lo].T) / dt : 0;
            v = l[lo].V + u * (l[hi].V - l[lo].V);
            return true;
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
        private static void KontrolaOknem(List<GpsFix> gps,
                                          List<(double A, double B)> stani, double oknoS, double dt,
                                          double tJizda)
        {
            if (stani.Count == 0 || oknoS <= 0) return;

            Console.WriteLine("  KONTROLA: totez meridlo na datech ze STANI, nakrajenych na stejne dlouha okna");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  (okno {0:F0} s - kdyby zkracene stani vydalo totez co jizda, meri se DELKA OKNA, ne pole)",
                oknoS));

            // Stani kratsi nez okno jizdy se "nakrajet" neda - vysly by tytez cele segmenty a verdikt
            // "okno neomezuje" by byl jen shoda dat se sebou samymi (do 13. 10. 2026 se to tak
            // tisklo). Na tuhle otazku od 13. 10. odpovida kalibrace meridla vyse.
            double nejdelsi = stani.Max(x => x.B - x.A);
            if (nejdelsi < oknoS)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  Nejde: nejdelsi stani ({0:F0} s) je kratsi nez okno jizdy - stani se neda zkratit na delku,",
                    nejdelsi));
                Console.WriteLine("  kterou nema. Vliv delky okna rika KALIBRACE MERIDLA vyse.");
                Console.WriteLine();
                return;
            }

            // Tytez segmenty stani dvakrat: CELE (tam je prava odpoved) a NAKRAJENE na okna
            // delky jizdnich useku. Rozdil mezi nimi je primo velikost artefaktu.
            // Jen segmenty aspon tak dlouhe jako okno: kratsi by v "nakrajene" sade byly cele
            // a srovnani by naredily shodnymi daty.
            var dlouha = stani.Where(x => x.B - x.A >= oknoS).ToList();
            var celeE = Odchylky(gps, dlouha, double.MaxValue, out var celeN);
            var kratE = Odchylky(gps, dlouha, oknoS, out var kratN);

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
        private static List<List<double>> Odchylky(List<GpsFix> gps,
                                                   List<(double A, double B)> stani, double oknoS,
                                                   out List<List<double>> sever)
        {
            var vychod = new List<List<double>>();
            sever = new List<List<double>>();
            foreach (var f in Behy(gps.Where(g => g.Platny).ToList(), stani, oknoS))
            {
                {
                    if (f.Count < 60) continue;
                    double lat0 = f.Average(x => x.Lat), lon0 = f.Average(x => x.Lon);
                    var e1 = new List<double>(); var n1 = new List<double>();
                    foreach (var g in f)
                    {
                        var (ee, nn) = Enu(lat0, lon0, g.Lat, g.Lon);
                        e1.Add(ee);
                        n1.Add(nn);
                    }
                    vychod.Add(e1); sever.Add(n1);
                }
            }
            return vychod;
        }

        /// <summary>
        /// Souvisle behy fixu v segmentech stani: segment se rozdeli na okna delky
        /// <paramref name="oknoS"/> (MaxValue = cely segment) a okno jeste na mezerach mezi fixy nad 1 s.
        /// </summary>
        private static IEnumerable<List<GpsFix>> Behy(List<GpsFix> gps, List<(double A, double B)> stani, double oknoS)
        {
            const double MaxMezera = 1.0;
            foreach (var (a, b) in stani)
            {
                double krok = oknoS > b - a ? b - a : oknoS;
                if (krok <= 0) continue;
                for (double t = a; t + krok <= b + 1e-9; t += krok)
                {
                    bool posledni = t + 2 * krok > b + 1e-9;
                    var f = gps.Where(g => g.T >= t && (posledni ? g.T <= b : g.T < t + krok)).OrderBy(g => g.T).ToList();
                    var beh = new List<GpsFix>();
                    foreach (var g in f)
                    {
                        if (beh.Count > 0 && g.T - beh[beh.Count - 1].T > MaxMezera) { yield return beh; beh = new List<GpsFix>(); }
                        beh.Add(g);
                    }
                    if (beh.Count > 0) yield return beh;
                }
            }
        }

        /// <summary>
        /// Dekorelacni cas z autokorelace (jemne, viz <see cref="Prechod"/>);
        /// <paramref name="odsazeni"/> != null = vypisuje ridkou tabulku autokorelace.
        /// </summary>
        private static double Dekorelace(List<List<double>> e, List<List<double>> n, double dt, string odsazeni)
        {
            if (e.Count == 0) return double.NaN;
            int maxLag = e.Max(v => v.Count) / 3;
            if (odsazeni != null)
                foreach (int lag in new[] { 1, 5, 10, 25, 50, 100, 150, 250, 400, 600, 800, 1200, 1800 })
                {
                    if (lag >= maxLag) break;
                    double r = 0.5 * (Autokorelace(e, lag) + Autokorelace(n, lag));
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "{0}tau = {1,6:F1} s   rho = {2,6:F2}", odsazeni, lag * dt, r));
                }
            return Prechod(e, n, dt);
        }

        /// <summary>
        /// <b>Dekorelacni cas</b>: kde autokorelace (prumer obou os) poprve klesne pod 1/e,
        /// <b>linearne interpolovano</b> mezi lagy po ~1 s. NaN = neklesla do tretiny nejdelsiho
        /// segmentu.
        ///
        /// <para>Do 13. 10. 2026 se bral prvni lag z ridke rady 1, 5, 10, 25, 50, 100, 150, 250,
        /// 400, 600, 800 vzorku, ktery byl pod 1/e — pri 10 Hz tedy vysledek zaokrouhleny NAHORU
        /// na 10 / 15 / 25 / 40 / 60 / 80 s. „T_d ~60 s" z FreeRun 1. 10. znamenalo jen „nekde
        /// mezi 40 a 60 s".</para>
        /// </summary>
        private static double Prechod(List<List<double>> e, List<List<double>> n, double dt)
        {
            if (e.Count == 0 || dt <= 0) return double.NaN;
            int maxLag = e.Max(v => v.Count) / 3;
            int krok = Math.Max(1, (int)Math.Round(1.0 / dt));
            double prah = 1.0 / Math.E;
            double lagPred = 0, rPred = 1.0;   // lag 0: rho = 1 z definice
            // Centrovane kopie a jmenovatele po segmentech JEDNOU (Autokorelace je pocita pri kazdem
            // lagu znovu; kalibrace to vola tisickrat). Vysledek je tentyz jako 0,5*(Autokorelace(e)+(n)).
            var ce = Centruj(e, out var de);
            var cn = Centruj(n, out var dn);
            for (int lag = 1; lag < maxLag; lag = lag < krok ? krok : lag + krok)
            {
                double r = 0.5 * (RhoCentr(ce, de, lag) + RhoCentr(cn, dn, lag));
                if (double.IsNaN(r)) return double.NaN;
                if (r < prah)
                {
                    double u = (rPred - prah) / Math.Max(1e-12, rPred - r);
                    return (lagPred + u * (lag - lagPred)) * dt;
                }
                lagPred = lag;
                rPred = r;
            }
            return double.NaN;
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
                               List<GpsFix> gps,
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
                        // MinClearanceM = double.MaxValue znamena "na draze zadna prekazka" - do statistiky
                        // nepatri (18. 9. 2026 se tiskl jako p50 1,8e308 a avg Infinity).
                        if (p.Clearance > 0 && p.Clearance < 1e6) (prosel ? clrAcc : clrRej).Add(p.Clearance);
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

        /// <summary>Segmenty minus jejich prumer a soucet ctvercu po segmentech (pro <see cref="RhoCentr"/>).</summary>
        private static List<double[]> Centruj(List<List<double>> segmenty, out double[] jmen)
        {
            var c = new List<double[]>(segmenty.Count);
            jmen = new double[segmenty.Count];
            for (int k = 0; k < segmenty.Count; k++)
            {
                var v = segmenty[k];
                double m = v.Count > 0 ? v.Average() : 0;
                var a = new double[v.Count];
                double ss = 0;
                for (int i = 0; i < v.Count; i++) { a[i] = v[i] - m; ss += a[i] * a[i]; }
                c.Add(a);
                jmen[k] = ss;
            }
            return c;
        }

        /// <summary>Totez co <see cref="Autokorelace"/>, nad predpocitanymi centrovanymi segmenty.</summary>
        private static double RhoCentr(List<double[]> c, double[] jmen, int lag)
        {
            double num = 0, den = 0;
            for (int k = 0; k < c.Count; k++)
            {
                var v = c[k];
                if (v.Length <= lag) continue;
                for (int i = 0; i < v.Length - lag; i++) num += v[i] * v[i + lag];
                den += jmen[k];
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

        /// <summary>
        /// <b>A0) Jdou hodinám robota správně?</b> Jediný absolutní čas v záznamu, který
        /// NEpochází z hodin Pi, je UTC čas fixu (<see cref="GPSState.UtcTimeOfDay"/>). Rozdíl
        /// proti razítku zprávy (které je z hodin Pi) je posun systémového času, a ten rozhoduje,
        /// jestli jdou časy v záznamu porovnat s čímkoli zvenčí (snímek obrazovky, journal,
        /// poznámka obsluhy).
        ///
        /// <para>⚠️ Tiskne se i <b>syrová</b> hodnota <see cref="GPSState.FixTime"/> s verzí zprávy:
        /// do verze 2 do ní ovladač u-bloxu skládal ITOW a skládal ho špatně (i „9 dní"), převod
        /// na UTC ho invertuje. Bez syrové hodnoty by se chybný převod tvářil jako posun hodin.</para>
        /// </summary>
        private static void Hodiny(List<(DateTime Stamp, TimeSpan Fix, int Verze, TimeSpan Utc, int Sat)> h)
        {
            Console.WriteLine();
            Console.WriteLine("A0) JDOU HODINAM ROBOTA SPRAVNE? (GPS cas proti razitku zpravy)");
            if (h.Count == 0) { Console.WriteLine("  zadny fix s casem - nelze rict."); Console.WriteLine(); return; }

            Console.WriteLine($"  fixu s casem: {h.Count}");
            foreach (var i in new[] { 0, h.Count / 2, h.Count - 1 }.Distinct())
            {
                var (stamp, fix, verze, utcI, sat) = h[i];
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  razitko {0:yyyy-MM-dd HH:mm:ss.fff}  FixTime {1} (verze {2}) -> UTC {3}, druzic {4}",
                    stamp, fix, verze, utcI, sat));
            }

            TimeSpan utc = h[0].Utc;
            TimeSpan mistni = h[0].Stamp.TimeOfDay;
            TimeSpan rozdil = mistni - utc;
            double hodZona = Math.Round(rozdil.TotalHours);
            TimeSpan posun = rozdil - TimeSpan.FromHours(hodZona);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  razitko {0} -> pri zone UTC{1:+0;-0} je POSUN HODIN {2:F1} s",
                mistni, hodZona, posun.TotalSeconds));
            Console.WriteLine("  (posun radove sekundy = hodiny jdou; minuty a vic = casy v zaznamu");
            Console.WriteLine("   NEJDOU porovnavat s hodinami telefonu ani s journalem jineho stroje)");
            Console.WriteLine();
        }

    }
}
