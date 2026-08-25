using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Devices;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Nesedí absolutní referenceи kurzu — a jde to poznat i bez mapy?</b>
    ///
    /// <para><b>Nacpak.</b> Fuze ma dnes JEDINOU absolutni referenci kurzu (<c>IMU/heading</c>
    /// z magnetometru), takze bias kompasu nema proti cemu zmerit: namereno 25. 8. 2026, ze pri
    /// <c>imubias=3</c> zustane chyba kurzu na 3,0 stupne bez ohledu na to, co dela korelace
    /// s mapou. GPS ale <b>kurz nad zemi taky zna</b> (<c>DynamicOrientation</c>, u NMEA z VTG,
    /// u uBloxu jako <c>atan2</c> z vektoru rychlosti) — jen se dosud nikam nepouzival a virtualni
    /// GPS ho vubec nehlasila.</para>
    ///
    /// <para>Tenhle report da tri absolutni kurzy vedle sebe proti <b>pravde</b>: IMU, GPS a odhad
    /// fuze. Kdyz IMU sedi na biasu a GPS na nule, je bias kompasu <b>observabilni bez mapy</b> —
    /// a tim padá hlavni namitka proti tomu, dat ho do stavu EKF.</para>
    ///
    /// <para><b>Kurz z GPS je pouzitelny jen za jizdy.</b> Je to <c>atan2</c> z vektoru rychlosti,
    /// takze jeho nejistota je <c>~sigma_v / v</c> — pri stani je to rovnomerne rozdeleny uhel.
    /// Report proto deli vzorky podle rychlosti a pomala zahazuje.</para>
    /// </summary>
    public static class HeadingReferencesReport
    {
        /// <summary>Pod touto rychlosti je kurz z GPS sum, ne merenie [m/s].</summary>
        private const double MinSpeedMps = 0.3;

        /// <param name="ignoreGroundTruth">
        /// Tvarit se, ze zaznam pravdu nenese — tedy jet <b>touz cestou jako na realnem HW</b>.
        ///
        /// <para><b>Nacpak:</b> cesta bez ground truth je ta, ktera na zarizeni skutecne pobezi,
        /// a ze zaznamu ze zarizeni ji nejde overit (neni proti cemu). Timhle prepinacem se pusti
        /// nad SIMULACNIM zaznamem, kde znama odpoved existuje — takze se da rict, jestli hlasi
        /// totez. Tentyz vzor, jakym se tady overuje vsechno ostatni: nejdriv proti znamé odpovedi.</para>
        /// </param>
        public static void Run(RecordFile rec, bool ignoreGroundTruth = false)
        {
            var truth = new List<(double T, double Th, double V)>();
            var imu = new List<(double T, double Yaw)>();
            var gps = new List<(double T, double Course, double Speed)>();
            var est = new List<(double T, double Th)>();

            DateTime t0 = DateTime.MinValue;
            int gpsTotal = 0;
            var gpsSample = new List<string>();
            foreach (var e in rec.Index)
            {
                var msg = rec.Read(e);
                if (msg == null) continue;
                if (t0 == DateTime.MinValue && msg is GroundTruthMsg g0) t0 = g0.TimeStamp;

                switch (msg)
                {
                    case GroundTruthMsg g:
                        truth.Add((Sec(g.TimeStamp, ref t0), g.Theta, g.V));
                        break;
                    case RobotStateMsg s:
                        est.Add((Sec(s.TimeStamp, ref t0), s.Theta));
                        break;
                    case IMUState i when i.Rotation.HasValue:
                        var ypr = i.YPR();
                        if (ypr != null) imu.Add((Sec(i.TimeStamp, ref t0), ypr.Yaw));
                        break;
                    case GPSState p:
                        gpsTotal++;
                        if (gpsSample.Count < 5)
                            gpsSample.Add(string.Format(CultureInfo.InvariantCulture,
                                "    Speed={0}  DynamicSpeed={1}  DynamicOrientation={2}  Orientation={3}",
                                Fmt(p.Speed), Fmt(p.DynamicSpeed), Fmt(p.DynamicOrientation),
                                Fmt(p.Orientation)));
                        if (p.DynamicOrientation.HasValue)
                            gps.Add((Sec(p.TimeStamp, ref t0), p.DynamicOrientation.Value,
                                     p.Speed ?? p.DynamicSpeed ?? 0.0));
                        break;
                }
            }

            Console.WriteLine($"GroundTruthMsg {truth.Count}, IMUState {imu.Count} (s atitudou), "
                              + $"GPSState {gpsTotal} (z toho s kurzem {gps.Count}), "
                              + $"RobotStateMsg {est.Count}");
            if (gpsSample.Count > 0)
            {
                Console.WriteLine("  co GPS hlasi (prvni vzorky):");
                foreach (var s in gpsSample) Console.WriteLine(s);
            }
            Console.WriteLine();

            // BEZ GROUND TRUTH (tedy na REALNEM HW) se da porovnat porad to podstatne: rozpor
            // IMU vs. GPS kurz. Pravdu k tomu nikdo nepotrebuje - staci, ze jsou to DVE nezavisle
            // absolutni reference. Prave tohle je otazka, kterou je treba na zarizeni potvrdit:
            // ma skutecny magnetometr bias, nebo je ta cela vada jen artefakt vnuceneho imubias=?
            if (truth.Count == 0 || ignoreGroundTruth)
            {
                if (ignoreGroundTruth && truth.Count > 0)
                    Console.WriteLine($"--nogt: {truth.Count} vzorku pravdy se ZAHAZUJE — jede se "
                                      + "cestou pro realne HW.");
                ReportWithoutTruth(imu, gps);
                return;
            }
            if (gps.Count == 0)
            {
                Console.WriteLine("⚠️ Zaznam nenese ZADNY kurz z GPS (GPSState.DynamicOrientation).");
                Console.WriteLine("   Virtualni GPS ho hlasi az od 25. 8. 2026; starsi zaznamy ho nemaji,");
                Console.WriteLine("   takze druhou absolutni referenci kurzu z nich vytahnout nelze.");
                Console.WriteLine();
            }

            truth.Sort((a, b) => a.T.CompareTo(b.T));

            // Rychlost robotu je PREDPOKLAD celeho mereni: kurz z GPS je atan2 z vektoru rychlosti,
            // takze pri stani neexistuje. Bez tohoto radku by "GPS kurz n=0" slo splest za vadu
            // senzoru, i kdyz robot jen stal.
            var vTruth = new Stats("skutecna rychlost robotu [m/s]");
            foreach (var (_, _, v) in truth) vTruth.Add(v);
            Console.WriteLine("  " + vTruth.Line("m/s"));
            int moving = truth.Count(a => Math.Abs(a.V) >= MinSpeedMps);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  nad prahem {0:F1} m/s: {1} z {2} vzorku ({3:F0} %)",
                MinSpeedMps, moving, truth.Count, 100.0 * moving / Math.Max(1, truth.Count)));
            Console.WriteLine();

            var imuErr = new Stats("IMU yaw - pravda [deg]");
            var gpsErr = new Stats("GPS kurz - pravda [deg]");
            var estErr = new Stats("odhad fuze - pravda [deg]");
            var imuVsGps = new Stats("IMU yaw - GPS kurz [deg]");

            foreach (var (t, yaw) in imu)
                if (TryTruth(truth, t, out double th, out _)) imuErr.Add(Deg(Wrap(yaw - th)));
            foreach (var (t, th2) in est)
                if (TryTruth(truth, t, out double th, out _)) estErr.Add(Deg(Wrap(th2 - th)));

            int slow = 0;
            foreach (var (t, course, speed) in gps)
            {
                // Rychlost se bere z GROUND TRUTH, ne z hlaseneho fixu - hlasena rychlost je taky
                // zasumena a u prahu by rozhodovala nahoda.
                if (!TryTruth(truth, t, out double th, out double v)) continue;
                if (Math.Abs(v) < MinSpeedMps) { slow++; continue; }

                gpsErr.Add(Deg(Wrap(course - th)));
                if (TryNearest(imu, t, 0.2, out double yaw)) imuVsGps.Add(Deg(Wrap(yaw - course)));
            }

            Console.WriteLine($"ABSOLUTNI REFERENCE KURZU proti pravde (vzorky pod {MinSpeedMps:F1} m/s "
                              + $"zahozeny: {slow}):");
            Console.WriteLine("  " + imuErr.Line("deg"));
            Console.WriteLine("  " + gpsErr.Line("deg"));
            Console.WriteLine("  " + estErr.Line("deg"));
            Console.WriteLine("  " + imuVsGps.Line("deg"));
            Console.WriteLine();

            if (imuErr.Count > 0 && gpsErr.Count > 0)
            {
                double bias = imuErr.Mean;
                double gpsBias = gpsErr.Mean;
                double gpsSd = Sd(gpsErr);
                // Kolik vzorku je potreba, aby se bias kompasu odlisil od sumu GPS kurzu na 3 sigma.
                double need = gpsSd > 0 && Math.Abs(bias - gpsBias) > 1e-9
                    ? Math.Pow(3.0 * gpsSd / Math.Abs(bias - gpsBias), 2)
                    : double.NaN;

                Console.WriteLine("JE BIAS KOMPASU OBSERVABILNI Z GPS KURZU?");
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  vychyleni IMU:  {0,7:F2} deg   (to je ten bias, ktery ma stav pojmout)", bias));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  vychyleni GPS:  {0,7:F2} deg   (ma byt ~0 - GPS kurz bias nema)", gpsBias));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  sum GPS kurzu:  {0,7:F2} deg   (sd jednoho vzorku)", gpsSd));
                if (!double.IsNaN(need))
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "  => na rozliseni 3 sigma staci {0:F0} vzorku; pri 5 Hz je to {1:F1} s jizdy",
                        Math.Ceiling(need), Math.Ceiling(need) / 5.0));
                Console.WriteLine();
                Console.WriteLine("  Kdyz vychyleni IMU sedi na vnucenem biasu a GPS na nule, je bias kompasu");
                Console.WriteLine("  observabilni BEZ mapy - a padá hlavni namitka proti stavu v EKF (ze by");
                Console.WriteLine("  pojedl chybu korelatoru misto chyby kompasu).");
                Console.WriteLine();
            }

            if (estErr.Count > 0 && imuErr.Count > 0)
            {
                Console.WriteLine("KOHO ODHAD NASLEDUJE:");
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  odhad je od pravdy o {0:F2} deg, IMU o {1:F2} deg -> odhad sedi na IMU na {2:F0} %",
                    estErr.Mean, imuErr.Mean,
                    Math.Abs(imuErr.Mean) > 1e-9 ? 100.0 * estErr.Mean / imuErr.Mean : 0.0));
                Console.WriteLine("  Blizko 100 % znamena, ze kompas kurz DEFINUJE - ne ze ho jen vazi.");
            }
        }

        /// <summary>
        /// Rozbor <b>bez ground truth</b> — tedy to, co jde udelat na REALNEM ZARIZENI.
        ///
        /// <para>Pravda tam neexistuje, ale otazka „nesedi absolutni reference kurzu?" ji
        /// nepotrebuje: staci rozdil dvou nezavislych referenci. Kdyz je jeho <b>stredni hodnota</b>
        /// vyrazne mimo nulu, ma jedna z nich bias — a protoze kurz nad zemi z Dopplera bias mit
        /// nema (namereno v simulaci +0,20 stupne), je to nejspis magnetometr.</para>
        ///
        /// <para><b>Rychlost se tu bere z FIXU</b>, ne z pravdy: na zarizeni nic jineho neni. Je
        /// zasumena, takze u prahu rozhoduje nahoda — proto se prah bere s rezervou.</para>
        /// </summary>
        private static void ReportWithoutTruth(List<(double T, double Yaw)> imu,
                                               List<(double T, double Course, double Speed)> gps)
        {
            Console.WriteLine("Zaznam nenese GroundTruthMsg — jde tedy o REALNE ZARIZENI (nebo beh");
            Console.WriteLine("bez simulace). Pravda neexistuje, ale to podstatne se zmerit da:");
            Console.WriteLine();

            if (gps.Count == 0 || imu.Count == 0)
            {
                Console.WriteLine("  Chybi jedna z referenci (IMU atituda nebo GPS kurz) - neni co porovnat.");
                Console.WriteLine("  GPS kurz hlasi jen jedouci prijimac; u NMEA je to VTG, u uBloxu");
                Console.WriteLine("  atan2 z vektoru rychlosti.");
                return;
            }

            var diff = new Stats("IMU yaw - GPS kurz [deg]");
            var used = new Stats("rychlost pri pouzitych vzorcich [m/s]");
            int slow = 0;
            foreach (var (t, course, speed) in gps)
            {
                if (speed < MinSpeedMps) { slow++; continue; }
                if (!TryNearest(imu, t, 0.2, out double yaw)) continue;
                diff.Add(Deg(Wrap(yaw - course)));
                used.Add(speed);
            }

            Console.WriteLine($"ROZPOR DVOU ABSOLUTNICH REFERENCI (vzorku pod prahem zahozeno: {slow}):");
            Console.WriteLine("  " + diff.Line("deg"));
            Console.WriteLine("  " + used.Line("m/s"));
            Console.WriteLine();

            if (diff.Count < 10)
            {
                Console.WriteLine("  Prilis malo vzorku - potreba delsi jizda nad prahem rychlosti.");
                return;
            }

            double mean = diff.Mean;
            double sd = Sd(diff);
            // Kolik vzorku je potreba, aby se stredni hodnota odlisila od nuly na 3 sigma.
            double need = sd > 0 && Math.Abs(mean) > 1e-9 ? Math.Pow(3.0 * sd / Math.Abs(mean), 2) : double.NaN;

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  stredni rozpor: {0,7:F2} deg   (kdyz je vyrazne mimo nulu, ma jedna reference bias)", mean));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  sum rozporu:    {0,7:F2} deg   (sd jednoho vzorku)", sd));
            if (!double.IsNaN(need))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => na 3 sigma je potreba {0:F0} vzorku; pri 5 Hz je to {1:F1} s jizdy",
                    Math.Ceiling(need), Math.Ceiling(need) / 5.0));
            Console.WriteLine();
            Console.WriteLine("  ⚠️ Rozpor sam NERIKA, KTERA reference se myli. Kurz nad zemi z Dopplera");
            Console.WriteLine("  ale bias mit nema (v simulaci +0,20 deg), takze podezreny je magnetometr.");
            Console.WriteLine("  Rozlisit to jde jinak: bias magnetometru je vazany na TELO robota, takze");
            Console.WriteLine("  se s kurzem OTACI. Projet smycku a sledovat, jestli rozpor na kurzu zavisi.");
            Console.WriteLine();
            Console.WriteLine("  K cemu to je: potvrdit, jestli ma smysl davat bias kompasu do stavu EKF.");
            Console.WriteLine("  V simulaci to gatuje vnuceny imubias=, ktery si zada clovek - na zarizeni");
            Console.WriteLine("  se teprve ukaze, jestli tam vubec nejaky bias je. Viz doc/ekf-fusion.md.");
        }

        private static double Sec(DateTime t, ref DateTime t0)
        {
            if (t0 == DateTime.MinValue) t0 = t;
            return (t - t0).TotalSeconds;
        }

        /// <summary>Pravda v nejblizsim case; <c>false</c>, kdyz je nejblizsi vzorek dal nez 0,2 s.</summary>
        private static bool TryTruth(List<(double T, double Th, double V)> truth, double t,
                                     out double theta, out double v)
        {
            theta = v = 0;
            if (truth.Count == 0) return false;

            int lo = 0, hi = truth.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (truth[mid].T <= t) lo = mid; else hi = mid;
            }
            var best = Math.Abs(truth[lo].T - t) <= Math.Abs(truth[hi].T - t) ? truth[lo] : truth[hi];
            if (Math.Abs(best.T - t) > 0.2) return false;
            theta = best.Th; v = best.V;
            return true;
        }

        private static bool TryNearest(List<(double T, double Yaw)> list, double t, double tol,
                                       out double value)
        {
            value = 0;
            double bestDt = double.MaxValue;
            foreach (var (tt, y) in list)
            {
                double dt = Math.Abs(tt - t);
                if (dt < bestDt) { bestDt = dt; value = y; }
            }
            return bestDt <= tol;
        }

        private static double Sd(Stats s)
        {
            // Stats drzi percentily; sd se z nich nespocita, tak se pouzije robustni prevod
            // z mezikvartiloveho rozpeti (p90-p10 ~ 2,563 sigma u normalniho rozdeleni).
            double spread = s.Percentile(90) - s.Percentile(10);
            return spread > 0 ? spread / 2.563 : 0.0;
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private static double Deg(double rad) => rad * 180.0 / Math.PI;

        private static string Fmt(double? v)
            => v.HasValue ? v.Value.ToString("F3", CultureInfo.InvariantCulture) : "null";
    }
}
