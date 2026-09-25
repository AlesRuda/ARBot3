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
        public static void Run(RecordFile rec, bool ignoreGroundTruth = false, string csvPath = null,
                               double binSec = 60)
        {
            var truth = new List<(double T, double Th, double V)>();
            var imu = new List<(double T, double Yaw)>();
            // Syrove pole a zrychleni: umoznuji spocitat kurz z magnetometru ZNOVU, nezavisle
            // na atitudovem filtru VN — a hlavne zmerit VELIKOST a SKLON pole, tedy jestli je
            // vada v magnetickem prostredi, nebo az v tom, co s nim senzor dela. Viz Magnetometer().
            var mag = new List<(double T, System.Numerics.Vector3 M, System.Numerics.Vector3 A)>();
            var gps = new List<(double T, double Course, double Speed)>();
            var est = new List<(double T, double Th)>();
            var estXy = new List<(double T, double X, double Y)>();

            DateTime t0 = DateTime.MinValue;
            int gpsTotal = 0;
            // Jmeno zdroje -> ma absolutni kurz? Tiskne se, aby bylo videt, co se pouzilo.
            var zdroje = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            int relativnich = 0;
            var gpsSample = new List<string>();
            // Sledovat GPS stopu: kurz z POLOHY je treti, na Doppleru nezavisla reference —
            // viz TrackCourseCheck. Drzi se cely zaznam, protoze okno se sklada az potom.
            var track = new List<(double T, double Lat, double Lon)>();
            var gyro = new List<(double T, double W)>();
            foreach (var e in rec.Index)
            {
                // Snimky kamer tvori 99,9 % objemu zaznamu (12 GB) a tenhle report je nepotrebuje.
                // Bez tehle radky trva jeden pruchod minuty misto sekund.
                if (e.MsgName != "IMUState" && e.MsgName != "GPSState"
                    && e.MsgName != "RobotStateMsg" && e.MsgName != "GroundTruthMsg") continue;
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
                        estXy.Add((Sec(s.TimeStamp, ref t0), s.X, s.Y));
                        break;
                    case IMUState i when i.Rotation.HasValue:
                        // ⚠️ V robotu je IMU VIC (VN100 + T265, napojena 6. 9. 2026) a obe posilaji
                        // IMUState. Do ABSOLUTNICH referenci kurzu smi jen zdroj, ktery absolutni
                        // kurz opravdu ma: T265 nema magnetometr, takze jeji yaw je posunuty
                        // o neznamou konstantu. Michat je znamena vyrobit nesmysl - naslapnuto
                        // 6. 9. 2026, kdy prvni beh nad takovym zaznamem vydal sd rozporu 89 stupnu
                        // a "odhad - IMU yaw" -148 stupnu, coz vypadalo jako porucha fuze.
                        // Rozlisuje se podle HasAbsoluteHeading (verze zpravy 3), ne podle jmena.
                        zdroje[i.Name ?? "(bez jmena)"] = i.HasAbsoluteHeading;
                        if (!i.HasAbsoluteHeading) { relativnich++; break; }

                        var ypr = i.YPR();
                        if (ypr != null) imu.Add((Sec(i.TimeStamp, ref t0), ypr.Yaw));
                        // Gyro se bere ZVLAST od yaw: pro mereni sumu GPS kurzu je potreba
                        // reference zmeny kurzu, ktera na GPS ani na magnetometru NEZAVISI.
                        if (i.AngularVelocity.HasValue)
                            gyro.Add((Sec(i.TimeStamp, ref t0), i.AngularVelocity.Value.Z));
                        if (i.Magnetometer.HasValue && i.Acceleration.HasValue)
                            mag.Add((Sec(i.TimeStamp, ref t0), i.Magnetometer.Value, i.Acceleration.Value));
                        break;
                    case GPSState p:
                        gpsTotal++;
                        track.Add((Sec(p.TimeStamp, ref t0), p.Latitude, p.Longitude));
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

            Console.WriteLine("IMUState podle zdroje:");
            foreach (var z in zdroje)
                Console.WriteLine($"  {z.Key,-28} {(z.Value ? "absolutni kurz - POUZITO" : "RELATIVNI yaw - VYNECHANO")}");
            if (relativnich > 0)
                Console.WriteLine($"  (vynechano {relativnich} vzorku z relativnich zdroju - jejich yaw "
                                  + "je posunuty o neznamou konstantu)");
            Console.WriteLine();

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
                ReportWithoutTruth(imu, gps, track, mag, est, gyro, csvPath, binSec);
                MotionDirection(track, estXy, est, binSec);
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

            // Frekvence fixu se MERI ze zaznamu, nepredpoklada - viz FixRateHz.
            double hz = FixRateHz(gps);
            if (double.IsNaN(hz)) hz = 10.0;

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
                        "  => na rozliseni 3 sigma staci {0:F0} vzorku; pri {1:F1} Hz je to {2:F1} s jizdy",
                        Math.Ceiling(need), hz, Math.Ceiling(need) / hz));
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
                                               List<(double T, double Course, double Speed)> gps,
                                               List<(double T, double Lat, double Lon)> track,
                                               List<(double T, System.Numerics.Vector3 M, System.Numerics.Vector3 A)> mag,
                                               List<(double T, double Th)> est,
                                               List<(double T, double W)> gyro,
                                               string csvPath = null,
                                               double binSec = 60)
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
            // Parovane vzorky si drzime cele: stredni rozpor sam o sobe NEROZLISI konstantni
            // posun (deklinace / ramce) od otoceneho znamenka nebo tvrdeho zeleza - k tomu je
            // potreba videt, jak rozpor ZAVISI NA KURZU. Viz HeadingDependence nize.
            var pair = new List<(double T, double Yaw, double Course, double Speed)>();
            int slow = 0;
            foreach (var (t, course, speed) in gps)
            {
                if (speed < MinSpeedMps) { slow++; continue; }
                if (!TryNearest(imu, t, 0.2, out double yaw)) continue;
                diff.Add(Deg(Wrap(yaw - course)));
                used.Add(speed);
                pair.Add((t, yaw, course, speed));
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

            double hzFix = FixRateHz(gps);
            if (double.IsNaN(hzFix)) hzFix = 10.0;
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
                    "  => na 3 sigma je potreba {0:F0} vzorku; pri {1:F1} Hz je to {2:F1} s jizdy",
                    Math.Ceiling(need), hzFix, Math.Ceiling(need) / hzFix));
            Console.WriteLine();
            Console.WriteLine("  ⚠️ Rozpor sam NERIKA, KTERA reference se myli - na to jsou bloky nize:");
            Console.WriteLine("  zavislost na kurzu (konstantni posun / znamenko / zelezo), kontrola GPS");
            Console.WriteLine("  kurzu smerem posunu polohy a rozbor syroveho magnetickeho pole.");
            Console.WriteLine();
            Console.WriteLine("  K cemu to je: potvrdit, jestli ma smysl davat bias kompasu do stavu EKF.");
            Console.WriteLine("  V simulaci to gatuje vnuceny imubias=, ktery si zada clovek - na zarizeni");
            Console.WriteLine("  se teprve ukaze, jestli tam vubec nejaky bias je. Viz doc/ekf-fusion.md.");
            Console.WriteLine();

            HeadingDependence(pair);
            TimeEvolution(pair, est, imu, gps, binSec);
            DriftAgainstGyro(imu, gps, est, gyro, binSec);
            WhoDoesFusionFollow(est, imu, gps);
            TrackCourseCheck(track, gps, imu);
            GpsCourseNoise(gps, gyro);
            Magnetometer(mag, imu, gps);
            if (csvPath != null) WriteCsv(csvPath, pair);
        }

        /// <summary>
        /// <b>Jak zasumeny je kurz z GPS — a jak dlouho je jeho chyba KORELOVANA?</b>
        ///
        /// <para><b>Nacpak.</b> Fuze si sigmu kurzu z GPS <b>pocita</b> jako
        /// <c>atan2(GpsCrossTrackStd, v)</c> s <c>GpsCrossTrackStd = 0,3 m/s</c> — jenze to cislo
        /// je <b>predpoklad</b> ("stejne jako GpsSpeedStd"), overeny jen v simulaci. Pri 0,7 m/s
        /// z nej vychazi <b>23,5 stupne</b>, a na tom stoji cela vaha GPS kurzu proti kompasu.</para>
        ///
        /// <para><b>Metoda.</b> Za okno delky <c>lag</c> se porovna zmena kurzu z GPS se zmenou
        /// kurzu z <b>GYRA</b> — nezavislym zdrojem, ktery na GPS ani na magnetometru nezavisi.
        /// Rozdil <c>(Δ GPS − Δ gyro)</c> je chyba, ktera za tu dobu pribyla:</para>
        /// <list type="bullet">
        /// <item><b>bily sum</b> → sd na lagu <b>nezavisi</b>;</item>
        /// <item><b>korelovana chyba</b> → sd s lagem <b>roste</b> a pak se ustali; misto ustaleni
        ///   je celkova sigma a cas, za ktery ho dosahne, je <b>dekorelacni cas</b>.</item>
        /// </list>
        ///
        /// <para>⚠️ <b>Proc to sample-to-sample mereni nestaci:</b> rozdil dvou sousednich fixu
        /// odectenim vyrusi vsechno, co se meni pomalu — tedy prave tu korelovanou cast. Merit jen
        /// ji znamena sigmu <b>drasticky podstrelit</b>. Tatáž past a tytéz merilo jako
        /// u <c>gpsposstd</c> (dekorelacni cas polohy ~40 s), viz doc/ekf-fusion.md.</para>
        ///
        /// <para><b>Jak se z toho dela sigma pro filtr:</b> filtr bere fixy jako nezavisle, takze
        /// pri dekorelacnim case <c>tau</c> a frekvenci <c>f</c> si nadsazuje informaci
        /// <c>tau·f</c>-krat. Poctiva sigma je proto <c>sigma_celkova · sqrt(tau·f)</c> — tentyz
        /// vzorec, jakym se odvodilo <c>gpsposstd</c>.</para>
        /// </summary>
        private static void GpsCourseNoise(List<(double T, double Course, double V)> gps,
                                           List<(double T, double W)> gyro)
        {
            Console.WriteLine();
            Console.WriteLine("SUM KURZU Z GPS A JEHO KORELACE (proti gyru):");
            var g = gps.Where(x => x.V >= MinSpeedMps).OrderBy(x => x.T).ToList();
            if (g.Count < 200 || gyro.Count < 200)
            {
                Console.WriteLine($"  Malo dat (GPS {g.Count}, gyro {gyro.Count}) - rozpad by nic nerekl.");
                return;
            }

            var kum = new List<(double T, double Yaw)>(gyro.Count);
            double yaw = 0;
            var gs = gyro.OrderBy(x => x.T).ToList();
            for (int i = 0; i < gs.Count; i++)
            {
                if (i > 0)
                {
                    double dt = gs[i].T - gs[i - 1].T;
                    if (dt > 0 && dt < 0.5) yaw += gs[i].W * dt;
                }
                kum.Add((gs[i].T, yaw));
            }

            Console.WriteLine($"  vzorku nad prahem rychlosti {MinSpeedMps:F1} m/s: {g.Count}");
            Console.WriteLine("    lag [s]      n   sd(dGPS - dGyro) [deg]   sigma jednoho odectu [deg]");
            var krivka = new List<(double Lag, double Sigma)>();
            foreach (double lag in new[] { 0.2, 0.5, 1.0, 2.0, 5.0, 10.0, 20.0, 40.0 })
            {
                var chyby = new List<double>();
                int j = 0;
                for (int k = 0; k < g.Count; k++)
                {
                    double cil = g[k].T + lag;
                    while (j < g.Count && g[j].T < cil - 0.11) j++;
                    if (j >= g.Count || Math.Abs(g[j].T - cil) > 0.11) continue;
                    // Mezera ve fixech by do rozdilu pustila otoceni, ktere gyro zna a GPS ne.
                    bool spojite = true;
                    for (int i = k + 1; i <= j && spojite; i++)
                        if (g[i].T - g[i - 1].T > 0.35) spojite = false;
                    if (!spojite) continue;

                    double dGps = Wrap(g[j].Course - g[k].Course);
                    if (!YawZGyra(kum, g[k].T, out double y0)) continue;
                    if (!YawZGyra(kum, g[j].T, out double y1)) continue;
                    chyby.Add(Wrap(dGps - (y1 - y0)));
                }
                if (chyby.Count < 40) continue;
                double sd = RobustSd(chyby);
                double sigma = sd / Math.Sqrt(2);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0,6:F1} {1,7}            {2,8:F2}                 {3,8:F2}",
                    lag, chyby.Count, Deg(sd), Deg(sigma)));
                krivka.Add((lag, sigma));
            }

            if (krivka.Count < 3) return;

            // ⚠️ Dekorelacni cas NEBRAT jako "lag s nejvetsi sigmou" - konec krivky ma nejmin
            // dvojic a tim nejvic sumu, takze maximum tam padne skoro vzdy a tau vyjde nadsazene
            // (na obou zaznamech 12. 9. 2026 to davalo 40 s misto 5-10). Usazeni se pozna tak, ze
            // krivka poprve dosahne 90 % sveho maxima; celkova sigma je pak prumer toho ocasu.
            double max = krivka.Max(x => x.Sigma);
            double lagUstaleni = krivka.First(x => x.Sigma >= 0.9 * max).Lag;
            double sigmaUstalena = krivka.Where(x => x.Lag >= lagUstaleni).Average(x => x.Sigma);
            if (sigmaUstalena <= 0) return;
            double vStred = g.Select(x => x.V).OrderBy(x => x).ElementAt(g.Count / 2);
            double modelRad = Math.Max(0.1, Math.Atan2(0.3, vStred));
            double hz = FixRateHz(g);
            if (double.IsNaN(hz)) hz = 10.0;
            double poctivaRad = sigmaUstalena * Math.Sqrt(lagUstaleni * hz);
            Console.WriteLine();
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  celkova sigma kurzu: {0:F2} deg (prumer ocasu), krivka se usadi na lagu {1:F1} s"
                + " (= dekorelacni cas)", Deg(sigmaUstalena), lagUstaleni));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  poctiva sigma pro filtr ({0:F1} Hz bere za nezavisle): {1:F2} * sqrt({2:F0}*{0:F1})"
                + " = {3:F1} deg", hz, Deg(sigmaUstalena), lagUstaleni, Deg(poctivaRad)));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  model fuze pri v = {0:F2} m/s: atan2(0,3; v) = {1:F1} deg",
                vStred, Deg(modelRad)));
            Console.WriteLine("  ⚠️ Kdyz model sedi na POCTIVOU sigmu a ne na celkovou, je to spravne");
            Console.WriteLine("  cislo ze spatneho duvodu: jeho dokumentace mluvi o pricnem sumu rychlosti,");
            Console.WriteLine("  ktery je ve skutecnosti o rad mensi. Drzi to nahodou, ne konstrukci.");
        }

        /// <summary>
        /// <b>Kdo z kurzu ujizdi proti gyru?</b> Rozpor dvou absolutnich referenci nerekne, ktera
        /// se hybe; gyro je treti cesta, ktera na magnetometru ani na GPS nezavisi a na minutach
        /// ujede jen o svuj bias (klidovy −4,6 °/h, viz doc/imu-and-frames.md). Tiskne se stredni
        /// <c>kurz − integral gyra</c> po kosech, vztazeny k prvnimu kosu: roste-li jen sloupec
        /// VN yaw, driftuje <b>atitudove reseni senzoru</b>; roste-li i GPS, je to bias gyra.
        ///
        /// <para>Pridano 24. 9. 2026 nad zaznamy FreeRun z 23. 9., kde VN yaw ujel o ~180° za
        /// 5 minut, zatimco kurz z magnetickeho pole sedel na GPS.</para>
        /// </summary>
        private static void DriftAgainstGyro(List<(double T, double Yaw)> imu,
                                             List<(double T, double Course, double Speed)> gps,
                                             List<(double T, double Th)> est,
                                             List<(double T, double W)> gyro,
                                             double binSec)
        {
            Console.WriteLine();
            Console.WriteLine("DRIFT PROTI GYRU (kurz - integral gyra, vztazeno k prvnimu kosu):");
            if (gyro.Count < 200) { Console.WriteLine("  Malo gyra."); return; }
            if (binSec < 1) binSec = 60;

            var kum = new List<(double T, double Yaw)>(gyro.Count);
            double yaw = 0;
            var gs = gyro.OrderBy(x => x.T).ToList();
            for (int i = 0; i < gs.Count; i++)
            {
                if (i > 0)
                {
                    double dt = gs[i].T - gs[i - 1].T;
                    if (dt > 0 && dt < 0.5) yaw += gs[i].W * dt;
                }
                kum.Add((gs[i].T, yaw));
            }

            List<(double T, double D)> Proti(IEnumerable<(double T, double V)> src)
            {
                var r = new List<(double T, double D)>();
                foreach (var (t, v) in src)
                    if (YawZGyra(kum, t, out double g)) r.Add((t, Wrap(v - g)));
                return r;
            }

            var dImu = Proti(imu);
            var dGps = Proti(gps.Where(x => x.Speed >= MinSpeedMps).Select(x => (x.T, x.Course)));
            var dEst = Proti(est);
            double t0 = kum[0].T, last = kum[kum.Count - 1].T;

            double? r0Imu = null, r0Gps = null, r0Est = null;
            string Col(List<(double T, double D)> d, double b, ref double? r0)
            {
                var v = d.Where(x => x.T - t0 >= b && x.T - t0 < b + binSec).Select(x => x.D).ToList();
                if (v.Count < 5) return string.Format("{0,5}  {1,8}", v.Count, "-");
                double m = CircMean(v);
                if (r0 == null) r0 = m;
                return string.Format(CultureInfo.InvariantCulture, "{0,5}  {1,8:F2}", v.Count, Deg(Wrap(m - r0.Value)));
            }

            Console.WriteLine("    cas [s]        n    VN yaw        n  GPS kurz        n  odhad fuze");
            for (double b = 0; b < last - t0; b += binSec)
            {
                string a = Col(dImu, b, ref r0Imu), c = Col(dGps, b, ref r0Gps), e = Col(dEst, b, ref r0Est);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F0}-{1,4:F0}  {2}   {3}   {4}", b, b + binSec, a, c, e));
            }
            Console.WriteLine("  Roste-li jen VN yaw, ujizdi atitudove reseni senzoru (VPE), ne gyro;");
            Console.WriteLine("  roste-li i GPS kurz stejnym tempem, je to bias gyra (a VN yaw je v pravu).");
        }

        /// <summary>
        /// <b>Kam se to hybe na mape?</b> Azimut POSUNU za kos: z GPS polohy (skutecny smer jizdy)
        /// a z polohy odhadu fuze (co ukazuje mapa), vedle azimutu kurzu odhadu. Rozpor kurzu
        /// rika, kam ukazuje sipka; tenhle blok rika, kam utika stopa — a to nemusi byt totez,
        /// protoze polohu tahne i GPS a koridor. Azimut je od severu po smeru hodin (jak na mape).
        /// Pridano 24. 9. 2026 (FreeRun 23. 9., „odhad se staci na zapad").
        /// </summary>
        private static void MotionDirection(List<(double T, double Lat, double Lon)> track,
                                            List<(double T, double X, double Y)> estXy,
                                            List<(double T, double Th)> est,
                                            double binSec)
        {
            Console.WriteLine();
            Console.WriteLine("SMER POSUNU PO KOSECH (azimut od severu po smeru hodin, jak na mape):");
            if (track.Count < 10 || estXy.Count < 10) { Console.WriteLine("  Malo dat."); return; }
            if (binSec < 1) binSec = 60;
            double lat0 = track[0].Lat;
            const double R = 6378137.0;
            // GPSState nese radiany (od 26. 8. 2026), viz CLAUDE.md.
            double E(double lon) => (lon - track[0].Lon) * R * Math.Cos(lat0);
            double N(double lat) => (lat - lat0) * R;
            double Az(double de, double dn) => (Deg(Math.Atan2(de, dn)) + 360) % 360;
            double t0 = Math.Min(track[0].T, estXy[0].T);
            double last = Math.Max(track[track.Count - 1].T, estXy[estXy.Count - 1].T);
            Console.WriteLine("    cas [s]    GPS draha  az GPS posunu   odhad draha  az posunu odhadu   az kurzu odhadu");
            for (double b = 0; b < last - t0; b += binSec)
            {
                var g = track.Where(x => x.T - t0 >= b && x.T - t0 < b + binSec).ToList();
                var e = estXy.Where(x => x.T - t0 >= b && x.T - t0 < b + binSec).ToList();
                var th = est.Where(x => x.T - t0 >= b && x.T - t0 < b + binSec).Select(x => x.Th).ToList();
                if (g.Count < 2 || e.Count < 2) continue;
                double gde = E(g[g.Count - 1].Lon) - E(g[0].Lon), gdn = N(g[g.Count - 1].Lat) - N(g[0].Lat);
                double ede = e[e.Count - 1].X - e[0].X, edn = e[e.Count - 1].Y - e[0].Y;
                double gd = Math.Sqrt(gde * gde + gdn * gdn), ed = Math.Sqrt(ede * ede + edn * edn);
                string azTh = th.Count > 0 ? string.Format(CultureInfo.InvariantCulture, "{0,6:F0}",
                                  (90 - Deg(CircMean(th)) + 720) % 360) : "     -";
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F0}-{1,4:F0}  {2,8:F1} m  {3,10}   {4,8:F1} m  {5,14}   {6,15}",
                    b, b + binSec, gd, gd > 3 ? Az(gde, gdn).ToString("F0", CultureInfo.InvariantCulture) : "-",
                    ed, ed > 3 ? Az(ede, edn).ToString("F0", CultureInfo.InvariantCulture) : "-", azTh));
            }
            Console.WriteLine("  Posun odhadu vedle GPS = stopa na mape utika; kurz odhadu vedle posunu = sipka ukazuje jinam.");
        }

        /// <summary>Integrovany yaw z gyra v case <paramref name="t"/>; false = neni vzorek dost blizko.</summary>
        private static bool YawZGyra(List<(double T, double Yaw)> kum, double t, out double yawOut)
        {
            yawOut = 0;
            int i = kum.BinarySearch((t, 0.0), Comparer<(double T, double Yaw)>.Create(
                (x, y) => x.T.CompareTo(y.T)));
            if (i < 0) i = ~i;
            if (i <= 0 || i >= kum.Count) return false;
            if (Math.Abs(kum[i].T - t) > 0.05) return false;
            yawOut = kum[i].Yaw;
            return true;
        }

        /// <summary>
        /// <b>Frekvence fixu GPS [Hz] ZE ZAZNAMU.</b>
        ///
        /// <para>⚠️ Do 12. 9. 2026 si tenhle report psal <b>5 Hz natvrdo</b> na tri mistech
        /// („pri 5 Hz je to N s jizdy", a hlavne v prepoctu poctive sigmy). Prijimac na robotu
        /// jede <b>10 Hz</b> — takze vsechny ty prepocty byly <b>dvakrat vedle</b> a nikdo si toho
        /// nevsiml, protoze vysledek porad vypadal rozumne. Frekvence se proto MERI: median
        /// rozestupu mezi fixy, mezery nad 0,35 s se vynechavaji (vypadek fixu neni perioda).</para>
        /// </summary>
        private static double FixRateHz(List<(double T, double Course, double Speed)> gps)
        {
            var dt = new List<double>();
            for (int i = 1; i < gps.Count; i++)
            {
                double d = gps[i].T - gps[i - 1].T;
                if (d > 0.01 && d < 0.35) dt.Add(d);
            }
            if (dt.Count < 10) return double.NaN;
            dt.Sort();
            return 1.0 / dt[dt.Count / 2];
        }

        /// <summary>Robustni sd z mezikvartiloveho rozpeti - chrani pred vyskoky pri vypadku fixu.</summary>
        private static double RobustSd(List<double> a)
        {
            var x = a.OrderBy(v => v).ToList();
            double q1 = x[(int)(0.25 * (x.Count - 1))], q3 = x[(int)(0.75 * (x.Count - 1))];
            return (q3 - q1) / 1.349;
        }

        /// <summary>
        /// <b>Ustaluje se kurz v case?</b> Blok 2 ve <c>vn100</c> merí zesílení zpetné vazby
        /// <c>K</c> regresí, a ta je pri zasumené chybe systematicky utlumená k nule — report si
        /// proto sám ríká o <b>model-free kontrolu</b>: kdyby filtr pole (resp. fúze kompas)
        /// pouzíval, musel by rozpor proti GPS kurzu v case <b>klesat</b>. Tenhle blok to tiskne.
        ///
        /// <para>Tiskne se <b>obojí</b> — <c>IMU yaw - GPS kurz</c> (usazuje se VPE uvnitr senzoru)
        /// i <c>odhad - GPS kurz</c> (usazuje se nase fúze). Rozlisit je podstatné: prvni je
        /// casová konstanta cizího filtru, na kterou nastavením <c>imuheadingstd=</c> /
        /// <c>imuheadinghz=</c> nesaháme, kdezto druhé je presne to, co ta nastavení ridí.</para>
        ///
        /// <para>⚠️ Minuta bez jízdy nad prahem rychlosti nemá GPS kurz a v tabulce chybí —
        /// mezera v case tedy <b>není</b> výpadek mereni, ale stojící robot.</para>
        /// </summary>
        private static void TimeEvolution(List<(double T, double Yaw, double Course, double Speed)> pair,
                                          List<(double T, double Th)> est,
                                          List<(double T, double Yaw)> imu,
                                          List<(double T, double Course, double Speed)> gps,
                                          double binSec)
        {
            Console.WriteLine();
            Console.WriteLine("VYVOJ ROZPORU V CASE (usaduje se kurz?):");
            if (pair.Count < 10) { Console.WriteLine("  Prilis malo vzorku."); return; }
            if (binSec < 1) binSec = 60;

            // Zacatek se bere od prvni zpravy, ne od prvni JIZDY - usazovani po startu je
            // prave to, co je videt, kdyz robot jeste stoji a ceka na uvolneni stopu.
            double t0 = est.Count > 0 ? Math.Min(est[0].T, pair[0].T) : pair[0].T;
            var fast = gps.Where(g => g.Speed >= MinSpeedMps).Select(g => (g.T, g.Course)).ToList();

            // Odhad fuze proti GPS kurzu - stejne parovani jako ve WhoDoesFusionFollow,
            // jen rozdelene do minutovych kosu.
            var estVsGps = new List<(double T, double D)>();
            foreach (var (t, th) in est)
                if (TryNearest(fast, t, 0.2, out double course)) estVsGps.Add((t, Wrap(th - course)));

            // Odhad proti IMU yaw - na rozdil od GPS kurzu to jde merit i ve STANI, takze
            // je videt i usazovani po startu, kdy robot jeste ceka na uvolneni nouzoveho stopu.
            var estVsImu = new List<(double T, double D)>();
            foreach (var (t, th) in est)
                if (TryNearest(imu, t, 0.1, out double yaw)) estVsImu.Add((t, Wrap(th - yaw)));

            Console.WriteLine("    cas [s]        n   IMU yaw - GPS kurz        n   odhad - GPS kurz        n   odhad - IMU yaw");
            double last = pair[pair.Count - 1].T;
            for (double b = 0; b < last - t0; b += binSec)
            {
                var im = pair.Where(x => x.T - t0 >= b && x.T - t0 < b + binSec)
                             .Select(x => Wrap(x.Yaw - x.Course)).ToList();
                var es = estVsGps.Where(x => x.T - t0 >= b && x.T - t0 < b + binSec)
                                 .Select(x => x.D).ToList();
                var ei = estVsImu.Where(x => x.T - t0 >= b && x.T - t0 < b + binSec)
                                 .Select(x => x.D).ToList();
                if (im.Count == 0 && es.Count == 0 && ei.Count == 0) continue;
                string a = im.Count > 0
                    ? string.Format(CultureInfo.InvariantCulture, "{0,5}  {1,7:F2} +- {2,6:F2}",
                                    im.Count, Deg(CircMean(im)), Deg(CircSd(im)))
                    : string.Format("{0,5}  {1,17}", 0, "-");
                string c = es.Count > 0
                    ? string.Format(CultureInfo.InvariantCulture, "{0,5}  {1,7:F2} +- {2,6:F2}",
                                    es.Count, Deg(CircMean(es)), Deg(CircSd(es)))
                    : string.Format("{0,5}  {1,17}", 0, "-");
                string d = ei.Count > 0
                    ? string.Format(CultureInfo.InvariantCulture, "{0,5}  {1,7:F2} +- {2,6:F2}",
                                    ei.Count, Deg(CircMean(ei)), Deg(CircSd(ei)))
                    : string.Format("{0,5}  {1,17}", 0, "-");
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4:F0}-{1,4:F0}  {2}   {3}   {4}", b, b + binSec, a, c, d));
            }
            Console.WriteLine("  Klesajici |stredni rozpor| = kurz se usazuje; plochy = neusazuje se.");
            Console.WriteLine("  ⚠️ Kdyz se usazuje sloupec IMU, dela to VPE uvnitr senzoru a nase");
            Console.WriteLine("  nastaveni nejistot (imuheadingstd=, imuheadinghz=) na to nesahaji.");
        }

        /// <summary>
        /// <b>Co z toho vyleze na mape?</b> Kurz robotu, ktery clovek vidi v pohledu a podle
        /// ktereho se rezou mrkve, je <see cref="RobotStateMsg.Theta"/> z fuze. Kdyz nesedi
        /// absolutni reference, je podstatne, KTEROU z nich odhad nasleduje — a tady se to
        /// pozna i bez pravdy, protoze reference se rozchazeji: odhad muze sedet jen na jedne.
        /// </summary>
        private static void WhoDoesFusionFollow(List<(double T, double Th)> est,
                                                List<(double T, double Yaw)> imu,
                                                List<(double T, double Course, double Speed)> gps)
        {
            Console.WriteLine();
            Console.WriteLine("KOHO ODHAD FUZE NASLEDUJE (to je kurz, ktery je videt na mape):");
            if (est.Count == 0) { Console.WriteLine("  Zaznam nenese RobotStateMsg."); return; }

            var vsImu = new List<double>();
            var vsGps = new List<double>();
            var fast = gps.Where(g => g.Speed >= MinSpeedMps).Select(g => (g.T, g.Course)).ToList();
            foreach (var (t, th) in est)
            {
                if (TryNearest(imu, t, 0.1, out double yaw)) vsImu.Add(Wrap(th - yaw));
                if (TryNearest(fast, t, 0.2, out double course)) vsGps.Add(Wrap(th - course));
            }
            if (vsImu.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  odhad - IMU yaw:  {0,7:F2} deg +- {1:F2}   (n={2})",
                    Deg(CircMean(vsImu)), Deg(CircSd(vsImu)), vsImu.Count));
            if (vsGps.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  odhad - GPS kurz: {0,7:F2} deg +- {1:F2}   (n={2})",
                    Deg(CircMean(vsGps)), Deg(CircSd(vsGps)), vsGps.Count));
            Console.WriteLine("  Blizko nuly u IMU a daleko u GPS znamena, ze kompas kurz DEFINUJE.");
        }

        /// <summary>
        /// <b>Neni spatne rovnou ten kurz z GPS?</b> Rozpor dvou referenci sam o sobe neukazuje
        /// na vinika. Kurz nad zemi z Dopplera (<c>atan2</c> z vektoru rychlosti) je ale mozne
        /// overit <b>treti</b>, na nem nezavislou cestou: smerem, kterym se posunula <b>poloha</b>.
        ///
        /// <para>Poloha a rychlost jsou v prijimaci jina vetev reseni, takze kdyz oba kurzy sedi,
        /// je GPS jako reference potvrzena a zbyva IMU. Okno se sklada na <b>vzdalenost</b>
        /// (ne na cas): pri stani je smer posunu sum.</para>
        /// </summary>
        private static void TrackCourseCheck(List<(double T, double Lat, double Lon)> track,
                                             List<(double T, double Course, double Speed)> gps,
                                             List<(double T, double Yaw)> imu)
        {
            const double MinStepM = 1.5;    // kratsi posun je pod sumem polohy
            const double MaxGapS = 5.0;     // delsi okno uz muze obsahovat zatacku
            const double EarthR = 6378137.0;

            Console.WriteLine();
            Console.WriteLine("KONTROLA GPS KURZU TRETI CESTOU (smer posunu POLOHY):");
            if (track.Count < 3)
            {
                Console.WriteLine("  Malo vzorku polohy.");
                return;
            }

            var vsDoppler = new List<double>();
            var vsImu = new List<double>();
            int i = 0;
            while (i < track.Count - 1)
            {
                int j = i + 1;
                double dE = 0, dN = 0;
                while (j < track.Count)
                {
                    double lat0 = track[i].Lat, lat1 = track[j].Lat;
                    dE = EarthR * Math.Cos(0.5 * (lat0 + lat1)) * (track[j].Lon - track[i].Lon);
                    dN = EarthR * (lat1 - lat0);
                    if (Math.Sqrt(dE * dE + dN * dN) >= MinStepM) break;
                    j++;
                }
                if (j >= track.Count) break;
                if (track[j].T - track[i].T > MaxGapS) { i = j; continue; }

                double course = Math.Atan2(dN, dE);          // ENU math: 0 = vychod, +CCW
                double tm = 0.5 * (track[i].T + track[j].T);
                if (TryNearest(gps.Select(g => (g.T, g.Course)).ToList(), tm, 0.3, out double dop))
                    vsDoppler.Add(Wrap(dop - course));
                if (TryNearest(imu, tm, 0.3, out double yaw))
                    vsImu.Add(Wrap(yaw - course));
                i = j;
            }

            if (vsDoppler.Count < 5 && vsImu.Count < 5)
            {
                Console.WriteLine($"  Prilis kratka ujeta draha (oken: {Math.Max(vsDoppler.Count, vsImu.Count)}).");
                return;
            }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  oken po {0:F1} m: {1}", MinStepM, Math.Max(vsDoppler.Count, vsImu.Count)));
            if (vsDoppler.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  Doppler - smer posunu: {0,7:F2} deg  +- {1:F2}   (ma byt ~0)",
                    Deg(CircMean(vsDoppler)), Deg(CircSd(vsDoppler))));
            if (vsImu.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  IMU yaw - smer posunu: {0,7:F2} deg  +- {1:F2}",
                    Deg(CircMean(vsImu)), Deg(CircSd(vsImu))));
            Console.WriteLine("  Kdyz Doppler sedi na nulu a IMU ne, je vadna reference IMU.");
        }

        /// <summary>
        /// <b>Ktera vada to je?</b> Stredni rozpor IMU vs. GPS rekne jen ZE nesedi. Rozlisit
        /// mezi kandidaty jde podle toho, jak rozpor <b>zavisi na kurzu</b>:
        /// <list type="bullet">
        /// <item><b>konstantni posun</b> (magneticka deklinace, spatny reference frame na VN,
        ///   pootocena montaz) - rozpor je na kurzu <b>nezavisly</b>;</item>
        /// <item><b>otocene znamenko / zamenena konvence</b> (azimut vs. matematicka orientace) -
        ///   rozpor jde s kurzem <b>dvojnasobnou rychlosti</b>, takze model <c>yaw = -kurz + a</c>
        ///   sedi lip nez <c>yaw = +kurz + a</c>;</item>
        /// <item><b>tvrde zelezo</b> (magnet/kov na robotu) - rozpor je <b>sinusova</b> funkce
        ///   kurzu s jednou periodou na otacku; <b>mekke zelezo</b> ma dve.</item>
        /// </list>
        /// <para><b>Predpoklad mereni:</b> kurz se v zaznamu musi dost menit. Kdyz robot jel
        /// rovne, jsou vsechny tri modely nerozlisitelne a report to rekne misto toho, aby
        /// vydal cislo, ktere nic neznamena.</para>
        /// </summary>
        private static void HeadingDependence(List<(double T, double Yaw, double Course, double Speed)> pair)
        {
            if (pair.Count < 20)
            {
                Console.WriteLine("ZAVISLOST NA KURZU: malo vzorku.");
                return;
            }

            // Jak moc se kurz vubec menil. Kruhova sd 0 = porad tentyz smer.
            double spread = Deg(CircSd(pair.Select(x => x.Course)));
            Console.WriteLine("ZAVISLOST ROZPORU NA KURZU (co je to za vadu?):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  rozptyl kurzu v zaznamu: {0:F1} deg (kruhova sd) - pod ~20 deg nejdou modely rozlisit",
                spread));

            // Tabulka po oktantech: syrovy pohled, ktery neni schovany za zadnym modelem.
            Console.WriteLine();
            Console.WriteLine("  kurz z GPS [deg]     n   rozpor IMU-GPS [deg]");
            for (int k = 0; k < 8; k++)
            {
                double lo = -180 + k * 45, hi = lo + 45;
                var inBin = pair.Where(x => { double c = Deg(Wrap(x.Course)); return c >= lo && c < hi; }).ToList();
                if (inBin.Count == 0) continue;
                double m = Deg(CircMean(inBin.Select(x => Wrap(x.Yaw - x.Course))));
                double sdb = Deg(CircSd(inBin.Select(x => Wrap(x.Yaw - x.Course))));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  <{0,4:F0},{1,4:F0})  {2,6}   {3,8:F2}  +- {4:F2}", lo, hi, inBin.Count, m, sdb));
            }
            Console.WriteLine();

            // Model A: yaw = +kurz + a   (konstantni posun)
            // Model B: yaw = -kurz + a   (otocene znamenko / zamenena konvence)
            // Model C: yaw = a          (IMU stoji - zamrzly kompas)
            double sdA = Deg(CircSd(pair.Select(x => Wrap(x.Yaw - x.Course))));
            double sdB = Deg(CircSd(pair.Select(x => Wrap(x.Yaw + x.Course))));
            double sdC = Deg(CircSd(pair.Select(x => x.Yaw)));
            double aA = Deg(CircMean(pair.Select(x => Wrap(x.Yaw - x.Course))));
            double aB = Deg(CircMean(pair.Select(x => Wrap(x.Yaw + x.Course))));
            Console.WriteLine("  KTERY MODEL SEDI (mensi zbytkovy rozptyl = lepsi):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "    A) yaw = +kurz + {0,7:F2} deg   zbytek sd = {1,6:F2} deg   (konstantni posun)", aA, sdA));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "    B) yaw = -kurz + {0,7:F2} deg   zbytek sd = {1,6:F2} deg   (OTOCENE ZNAMENKO)", aB, sdB));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "    C) yaw = konst              zbytek sd = {0,6:F2} deg   (zamrzly kompas)", sdC));

            // Tvrde/mekke zelezo: harmonicky rozklad zbytku modelu A podle kurzu.
            // rozpor(psi) = a0 + a1*cos(psi) + b1*sin(psi) + a2*cos(2psi) + b2*sin(2psi)
            int occupied = Enumerable.Range(0, 8).Count(k =>
            {
                double lo = -180 + k * 45, hi = lo + 45;
                return pair.Any(x => { double c = Deg(Wrap(x.Course)); return c >= lo && c < hi; });
            });
            if (occupied < 5)
            {
                Console.WriteLine();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  HARMONICKY ROZKLAD (tvrde/mekke zelezo) se NEPOCITA: obsazeno jen {0} z 8 oktantu.",
                    occupied));
                Console.WriteLine("  Petiparametricky proklad by na dvou shlucich kurzu vydal cislo,");
                Console.WriteLine("  ktere neni merenim - k odliseni zeleza je potreba projeta SMYCKA.");
                return;
            }
            var psi = pair.Select(x => Wrap(x.Course)).ToArray();
            double c0 = CircMean(pair.Select(x => Wrap(x.Yaw - x.Course)));
            var r = pair.Select(x => Wrap(Wrap(x.Yaw - x.Course) - c0)).ToArray();
            double[] coef = FitHarmonic(psi, r);
            if (coef != null)
            {
                double amp1 = Deg(Math.Sqrt(coef[1] * coef[1] + coef[2] * coef[2]));
                double amp2 = Deg(Math.Sqrt(coef[3] * coef[3] + coef[4] * coef[4]));
                Console.WriteLine();
                Console.WriteLine("  HARMONICKY ROZKLAD zbytku podle kurzu:");
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    1. harmonicka (tvrde zelezo): amplituda {0,6:F2} deg", amp1));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    2. harmonicka (mekke zelezo): amplituda {0,6:F2} deg", amp2));
            }
        }

        /// <summary>
        /// <b>Je vada v poli, nebo az v tom, co s nim senzor dela?</b> Kurz z kompasu ma dva
        /// clanky: (1) zmerene magneticke pole a (2) atitudovy filtr, ktery z nej dela yaw.
        /// Rozlisit je jde tim, ze se pole posoudi samo o sobe — <b>velikost</b> a <b>sklon</b>
        /// (inklinace) jsou v danem miste konstanty, ktere se s otocenim robotu NEMENI:
        /// v CR je pole ~0,49 G a sklon ~66 stupnu dolu.
        ///
        /// <para>Kdyz velikost nebo sklon kolisaji s kurzem, je porusene POLE (tvrde/mekke
        /// zelezo na robotu nebo spatna kalibrace HSI v senzoru) a yaw nema z ceho vyjit spravne.
        /// Kdyz jsou v poradku a presto nesedi kurz, je vada az za magnetometrem.</para>
        ///
        /// <para><b>Svislice se bere z akcelerometru</b>, ne z atitudy senzoru — jinak by se
        /// do „nezavisleho" prepoctu vratil prave ten yaw, ktery se ma overit. Pri jizde 0,7 m/s
        /// je vlastni zrychleni proti g zanedbatelne; vzorky, kde |a| utece od g, se zahazuji.</para>
        ///
        /// <para><b>Pozor na jednotky a na to, co Mag je:</b> binarni vystup VN posila pole
        /// v <b>Gaussech</b> a v konfiguraci driveru je to pole <c>Mag</c>, tedy hodnota
        /// <b>PO</b> palubni kompenzaci HSI (ne <c>UncompMag</c>). Vada palubni kalibrace se tedy
        /// v techto cislech projevi.</para>
        /// </summary>
        private static void Magnetometer(List<(double T, System.Numerics.Vector3 M, System.Numerics.Vector3 A)> mag,
                                         List<(double T, double Yaw)> imu,
                                         List<(double T, double Course, double Speed)> gps)
        {
            Console.WriteLine();
            Console.WriteLine("SYROVE MAGNETICKE POLE (je vada v poli, nebo az za nim?):");
            if (mag.Count == 0)
            {
                Console.WriteLine("  Zaznam nenese magnetometr ani zrychleni.");
                return;
            }

            var normStat = new Stats("velikost pole |B|");
            var accStat = new Stats("velikost zrychleni |a|");
            var dipStat = new Stats("sklon pole (inklinace)");
            var headStat = new List<double>();   // kurz z pole - yaw ze senzoru
            var courseStat = new List<double>(); // kurz z pole - kurz z GPS
            var headByTime = new List<(double T, double D)>();
            int tilted = 0;

            // Svislice z JEDNOHO vzorku zrychleni je pri jizde po trave nepouzitelna - otresy
            // maji stejny rad jako g a chyba svislice jde primo do kurzu. Gravitace je ta
            // NIZKOFREKVENCNI cast, takze se zrychleni klouzave prumeruje pres +-0,5 s.
            var aAvg = MovingAverage(mag, 0.5);

            // Prah se bere kolem MEDIANU |a|, ne kolem tabulkoveho g: kdyby mel akcelerometr
            // meritko nebo bias, utnul by pevny prah cely zaznam a report by mlcel misto toho,
            // aby to rekl. Median se proto i vypisuje.
            foreach (var v in aAvg) accStat.Add(Math.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z));
            double aRef = accStat.Percentile(50);

            for (int k = 0; k < mag.Count; k++)
            {
                var (t, m, _) = mag[k];
                var a = aAvg[k];
                double an = Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
                // Vzorek s vlastnim zrychlenim nebo narazem svislici pokazi.
                if (an < 0.98 * aRef || an > 1.02 * aRef) { tilted++; continue; }
                double mn = Math.Sqrt(m.X * m.X + m.Y * m.Y + m.Z * m.Z);
                if (mn < 1e-6) continue;

                // Svislice (nahoru) z akcelerometru; body FLU, takze v klidu miri a nahoru.
                double ux = a.X / an, uy = a.Y / an, uz = a.Z / an;
                normStat.Add(mn);
                double mu = m.X * ux + m.Y * uy + m.Z * uz;
                dipStat.Add(Deg(Math.Asin(Math.Max(-1, Math.Min(1, -mu / mn)))));

                // Vodorovna slozka pole a vodorovny prumet osy "vpred" (X) a "vlevo".
                double hx = m.X - mu * ux, hy = m.Y - mu * uy, hz = m.Z - mu * uz;
                double fx = 1 - ux * ux, fy = -ux * uy, fz = -ux * uz;
                double fn = Math.Sqrt(fx * fx + fy * fy + fz * fz);
                if (fn < 1e-6) continue;
                fx /= fn; fy /= fn; fz /= fn;
                double lx = uy * fz - uz * fy, ly = uz * fx - ux * fz, lz = ux * fy - uy * fx;

                // Uhel od osy "vpred" k magnetickemu severu, proti smeru hod. rucicek = azimut.
                double azimut = Math.Atan2(hx * lx + hy * ly + hz * lz, hx * fx + hy * fy + hz * fz);
                double orientation = Math.PI / 2 - azimut;   // ENU math (bez deklinace, ta je ~5 deg)

                if (TryNearest(imu, t, 0.05, out double yaw))
                {
                    headStat.Add(Wrap(orientation - yaw));
                    headByTime.Add((t, Wrap(orientation - yaw)));
                }
                if (TryNearest(gps.Where(g => g.Speed >= MinSpeedMps).Select(g => (g.T, g.Course)).ToList(),
                               t, 0.2, out double course)) courseStat.Add(Wrap(orientation - course));
            }

            if (normStat.Count == 0)
            {
                Console.WriteLine($"  Zadny pouzitelny vzorek (zahozeno kvuli zrychleni: {tilted}).");
                return;
            }
            Console.WriteLine($"  vzorku: {normStat.Count} (zahozeno kvuli vlastnimu zrychleni: {tilted})");
            Console.WriteLine("  " + accStat.Line("m/s2") + "   (median je referenci svislice; g = 9,807)");
            Console.WriteLine("  " + normStat.Line("G") + "   (v CR ma byt ~0,49 G a hlavne KONSTANTNI)");
            Console.WriteLine("  " + dipStat.Line("deg") + "   (v CR ma byt ~66 deg dolu a taky konstantni)");
            if (headStat.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  kurz z pole - yaw ze senzoru: {0,7:F2} deg +- {1:F2}   (~0 = filtr VN pole jen prepocitava)",
                    Deg(CircMean(headStat)), Deg(CircSd(headStat))));
            if (courseStat.Count > 0)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  kurz z pole - kurz z GPS:     {0,7:F2} deg +- {1:F2}   (ma byt ~ +5 deg deklinace)",
                    Deg(CircMean(courseStat)), Deg(CircSd(courseStat))));

            // Vyvoj v case: konstantni rozdil = jiny referencni smer filtru, rostouci = drift gyra.
            if (headByTime.Count > 50)
            {
                Console.WriteLine("  vyvoj rozdilu 'kurz z pole - yaw' po minutach:");
                double tEnd = headByTime[headByTime.Count - 1].T;
                for (double b = 0; b < tEnd; b += 60)
                {
                    double lo = b, hi = b + 60;
                    var inBin = headByTime.Where(x => x.T >= lo && x.T < hi).Select(x => x.D).ToList();
                    if (inBin.Count < 20) continue;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                        "    {0,5:F0}-{1,5:F0} s  n={2,6}  {3,7:F2} deg +- {4:F2}",
                        lo, hi, inBin.Count, Deg(CircMean(inBin)), Deg(CircSd(inBin))));
                }
            }
        }

        /// <summary>Klouzavy prumer zrychleni v okne +-<paramref name="halfWindowS"/> sekund.</summary>
        private static System.Numerics.Vector3[] MovingAverage(
            List<(double T, System.Numerics.Vector3 M, System.Numerics.Vector3 A)> mag, double halfWindowS)
        {
            var outv = new System.Numerics.Vector3[mag.Count];
            int lo = 0, hi = 0;
            double sx = 0, sy = 0, sz = 0;
            for (int k = 0; k < mag.Count; k++)
            {
                while (hi < mag.Count && mag[hi].T <= mag[k].T + halfWindowS)
                { sx += mag[hi].A.X; sy += mag[hi].A.Y; sz += mag[hi].A.Z; hi++; }
                while (lo < hi && mag[lo].T < mag[k].T - halfWindowS)
                { sx -= mag[lo].A.X; sy -= mag[lo].A.Y; sz -= mag[lo].A.Z; lo++; }
                int n = Math.Max(1, hi - lo);
                outv[k] = new System.Numerics.Vector3((float)(sx / n), (float)(sy / n), (float)(sz / n));
            }
            return outv;
        }

        /// <summary>Parovane vzorky do CSV - aby slo cislo overit i mimo tenhle nastroj.</summary>
        private static void WriteCsv(string path, List<(double T, double Yaw, double Course, double Speed)> pair)
        {
            using (var w = new System.IO.StreamWriter(path, false))
            {
                w.WriteLine("t_s;imu_yaw_deg;gps_course_deg;speed_mps;diff_deg");
                foreach (var x in pair)
                    w.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0:F3};{1:F3};{2:F3};{3:F3};{4:F3}",
                        x.T, Deg(Wrap(x.Yaw)), Deg(Wrap(x.Course)), x.Speed, Deg(Wrap(x.Yaw - x.Course))));
            }
            Console.WriteLine($"  CSV zapsano: {path} ({pair.Count} vzorku)");
        }

        /// <summary>Kruhovy prumer uhlu [rad].</summary>
        private static double CircMean(IEnumerable<double> a)
        {
            double sx = 0, sy = 0; int n = 0;
            foreach (var v in a) { sx += Math.Cos(v); sy += Math.Sin(v); n++; }
            return n == 0 ? 0 : Math.Atan2(sy / n, sx / n);
        }

        /// <summary>Kruhova smerodatna odchylka [rad] (sqrt(-2 ln R)).</summary>
        private static double CircSd(IEnumerable<double> a)
        {
            double sx = 0, sy = 0; int n = 0;
            foreach (var v in a) { sx += Math.Cos(v); sy += Math.Sin(v); n++; }
            if (n == 0) return 0;
            double R = Math.Sqrt(sx * sx + sy * sy) / n;
            return R <= 0 ? Math.PI : Math.Sqrt(-2.0 * Math.Log(Math.Min(1.0, R)));
        }

        /// <summary>Nejmensi ctverce pro [1, cos, sin, cos2, sin2]; null pri singularite.</summary>
        private static double[] FitHarmonic(double[] psi, double[] y)
        {
            const int m = 5;
            var A = new double[m, m + 1];
            for (int i = 0; i < psi.Length; i++)
            {
                double[] b = { 1, Math.Cos(psi[i]), Math.Sin(psi[i]), Math.Cos(2 * psi[i]), Math.Sin(2 * psi[i]) };
                for (int r0 = 0; r0 < m; r0++)
                {
                    for (int c = 0; c < m; c++) A[r0, c] += b[r0] * b[c];
                    A[r0, m] += b[r0] * y[i];
                }
            }
            for (int col = 0; col < m; col++)
            {
                int piv = col;
                for (int r0 = col + 1; r0 < m; r0++) if (Math.Abs(A[r0, col]) > Math.Abs(A[piv, col])) piv = r0;
                if (Math.Abs(A[piv, col]) < 1e-12) return null;
                if (piv != col) for (int c = col; c <= m; c++) { var t = A[col, c]; A[col, c] = A[piv, c]; A[piv, c] = t; }
                for (int r0 = 0; r0 < m; r0++)
                {
                    if (r0 == col) continue;
                    double f = A[r0, col] / A[col, col];
                    for (int c = col; c <= m; c++) A[r0, c] -= f * A[col, c];
                }
            }
            var x = new double[m];
            for (int i = 0; i < m; i++) x[i] = A[i, m] / A[i, i];
            return x;
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
