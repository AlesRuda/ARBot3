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
        public static void Run(RecordFile rec, bool ignoreGroundTruth = false, string csvPath = null)
        {
            var truth = new List<(double T, double Th, double V)>();
            var imu = new List<(double T, double Yaw)>();
            // Syrove pole a zrychleni: umoznuji spocitat kurz z magnetometru ZNOVU, nezavisle
            // na atitudovem filtru VN — a hlavne zmerit VELIKOST a SKLON pole, tedy jestli je
            // vada v magnetickem prostredi, nebo az v tom, co s nim senzor dela. Viz Magnetometer().
            var mag = new List<(double T, System.Numerics.Vector3 M, System.Numerics.Vector3 A)>();
            var gps = new List<(double T, double Course, double Speed)>();
            var est = new List<(double T, double Th)>();

            DateTime t0 = DateTime.MinValue;
            int gpsTotal = 0;
            // Jmeno zdroje -> ma absolutni kurz? Tiskne se, aby bylo videt, co se pouzilo.
            var zdroje = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            int relativnich = 0;
            var gpsSample = new List<string>();
            // Sledovat GPS stopu: kurz z POLOHY je treti, na Doppleru nezavisla reference —
            // viz TrackCourseCheck. Drzi se cely zaznam, protoze okno se sklada az potom.
            var track = new List<(double T, double Lat, double Lon)>();
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
                ReportWithoutTruth(imu, gps, track, mag, est, csvPath);
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
                                               List<(double T, double Course, double Speed)> gps,
                                               List<(double T, double Lat, double Lon)> track,
                                               List<(double T, System.Numerics.Vector3 M, System.Numerics.Vector3 A)> mag,
                                               List<(double T, double Th)> est,
                                               string csvPath = null)
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
            Console.WriteLine("  ⚠️ Rozpor sam NERIKA, KTERA reference se myli - na to jsou bloky nize:");
            Console.WriteLine("  zavislost na kurzu (konstantni posun / znamenko / zelezo), kontrola GPS");
            Console.WriteLine("  kurzu smerem posunu polohy a rozbor syroveho magnetickeho pole.");
            Console.WriteLine();
            Console.WriteLine("  K cemu to je: potvrdit, jestli ma smysl davat bias kompasu do stavu EKF.");
            Console.WriteLine("  V simulaci to gatuje vnuceny imubias=, ktery si zada clovek - na zarizeni");
            Console.WriteLine("  se teprve ukaze, jestli tam vubec nejaky bias je. Viz doc/ekf-fusion.md.");
            Console.WriteLine();

            HeadingDependence(pair);
            WhoDoesFusionFollow(est, imu, gps);
            TrackCourseCheck(track, gps, imu);
            Magnetometer(mag, imu, gps);
            if (csvPath != null) WriteCsv(csvPath, pair);
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
