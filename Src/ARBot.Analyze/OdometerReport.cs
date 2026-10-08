using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;
using ARBot.Common.Missions;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Kolik robot ujel</b> (prikaz <c>odometer</c>): prehraje <see cref="Odometer"/> - tutez
    /// tridu, ktera od 6. 10. 2026 pocita „ujeto v misi" na strance nahledu - nad
    /// <see cref="RobotStateMsg"/> ze zaznamu a srovna ji s drahou z fuzovane polohy a z GPS.
    ///
    /// <para><b>Zdroj drahy.</b> Od <see cref="RobotStateMsg"/> verze 2 (4. 10. 2026) se bere
    /// odometricka poza (<c>OdomX/OdomY</c>), tedy totez co na robotu. Starsi zaznamy ji nenesou -
    /// pak se integruje fuzovane <c>v</c>, <c>omega</c> z teze zpravy (10 Hz), coz je tataz velicina
    /// v hrubsim kroku. Fuzovana poloha je pro srovnani: kazda korekce (GPS, koridor) se do ni
    /// pricte jako ujeta draha - rozdil proti odometrii je tedy soucet korekci, ne ujeta draha navic.</para>
    ///
    /// <para><b>GPS draha</b> se pocita kotvou: prirustek se pricte, az se fix vzdali od posledni
    /// kotvy o 1 m (resp. 3 m). Sum fixu by jinak pri 10 Hz pricital decimetry kazdou desetinu
    /// sekundy; kotva ho potlaci za cenu zkraceni zatacek. Dve kotvy vedle sebe ukazuji, jak moc
    /// cislo na volbe zavisi.</para>
    ///
    /// <para>Useky mise z <see cref="TrackMsg"/> (faze, misto, kolo): u kazdeho draha podle vsech
    /// tri zdroju a planovana delka trasy na zacatku useku. Cte jen zpravy stavu, zadne snimky.</para>
    /// </summary>
    public static class OdometerReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <summary>Kumulativni draha v case (pro dotaz „kolik mezi a a b").</summary>
        private sealed class Kum
        {
            private readonly List<long> t = new List<long>();
            private readonly List<double> d = new List<double>();
            public void Add(DateTime cas, double draha) { t.Add(cas.Ticks); d.Add(draha); }
            public double At(DateTime cas)
            {
                if (t.Count == 0) return double.NaN;
                int i = t.BinarySearch(cas.Ticks);
                if (i < 0) i = ~i - 1;
                return i < 0 ? 0 : d[i];
            }
            public double Mezi(DateTime a, DateTime b) => At(b) - At(a);
        }

        public static void Run(RecordFile rec)
        {
            var stavy = rec.ReadAll<RobotStateMsg>("RobotStateMsg").OrderBy(s => s.TimeStamp).ToList();
            var gps = rec.ReadAll<GPSState>("GPSState").OrderBy(g => g.TimeStamp).ToList();
            var track = rec.ReadAll<TrackMsg>("TrackMsg").OrderBy(m => m.TimeStamp).ToList();
            if (stavy.Count == 0) { Console.WriteLine("Zaznam nema RobotStateMsg."); return; }
            bool odom = stavy.All(s => s.HasOdom);
            Console.WriteLine(string.Format(Ci, "RobotStateMsg {0} (verze {1}), GPSState {2}, TrackMsg {3}; {4:HH:mm:ss} - {5:HH:mm:ss}",
                stavy.Count, string.Join("/", stavy.Select(s => s.Verze).Distinct()), gps.Count, track.Count,
                stavy[0].TimeStamp, stavy[^1].TimeStamp));
            Console.WriteLine(odom
                ? "zdroj drahy: ODOMETRICKA poza ze zpravy (OdomX/OdomY) - totez co na robotu"
                : "zdroj drahy: zprava odometrickou pozu nenese (verze < 2) -> integral fuzovaneho v, omega po 10 Hz");
            Console.WriteLine();

            // --- Odometer nad odometrii (nebo jeji nahradou) a nad fuzovanou polohou ---
            var odoOdom = new Odometer();
            var odoFuze = new Odometer();
            var kOdom = new Kum();
            var kFuze = new Kum();
            double ix = 0, iy = 0, ith = stavy[0].Theta;
            DateTime? posl = null;
            foreach (var s in stavy)
            {
                if (!odom && posl != null)
                {
                    double dt = (s.TimeStamp - posl.Value).TotalSeconds;
                    // Mezera nad 1 s (restart fuze, vypadek zaznamu) se neintegruje - stejne pravidlo,
                    // jakym Odometer nepocita cas v pohybu pres dlouhou mezeru.
                    if (dt > 0 && dt <= Odometer.MaxGapSec)
                    {
                        ith += s.Omega * dt;
                        ix += s.V * Math.Cos(ith) * dt;
                        iy += s.V * Math.Sin(ith) * dt;
                    }
                }
                posl = s.TimeStamp;
                if (odom) odoOdom.Add(s.TimeStamp, s.OdomX, s.OdomY, s.V);
                else odoOdom.Add(s.TimeStamp, ix, iy, s.V);
                odoFuze.Add(s.TimeStamp, s.X, s.Y, s.V);
                kOdom.Add(s.TimeStamp, odoOdom.TotalDistanceM);
                kFuze.Add(s.TimeStamp, odoFuze.TotalDistanceM);
            }

            // --- GPS: draha kotvou 1 m a 3 m ---
            var kGps1 = GpsDraha(gps, 1.0, out double gps1, out int fixu);
            var kGps3 = GpsDraha(gps, 3.0, out double gps3, out _);

            Console.WriteLine("CELY ZAZNAM");
            Console.WriteLine(string.Format(Ci, "  odometrie (Odometer z HEAD):   {0,8:F1} m, v pohybu {1,6:F0} s, prumer v pohybu {2:F2} m/s",
                odoOdom.TotalDistanceM, odoOdom.TotalMovingSec, odoOdom.TotalDistanceM / Math.Max(1, odoOdom.TotalMovingSec)));
            Console.WriteLine(string.Format(Ci, "  fuzovana poloha (s korekcemi): {0,8:F1} m  ({1:+0.0;-0.0} % proti odometrii = kolik korekce polohy pridaly delce drahy)",
                odoFuze.TotalDistanceM, Pct(odoFuze.TotalDistanceM, odoOdom.TotalDistanceM)));
            Console.WriteLine(string.Format(Ci, "  GPS kotva 1 m:                 {0,8:F1} m  ({1:+0.0;-0.0} %), kotva 3 m: {2:F1} m ({3:+0.0;-0.0} %); fixu {4} z {5}",
                gps1, Pct(gps1, odoOdom.TotalDistanceM), gps3, Pct(gps3, odoOdom.TotalDistanceM), fixu, gps.Count));
            Console.WriteLine();

            if (track.Count == 0) { Console.WriteLine("TrackMsg v zaznamu nejsou - useky mise se nevypisuji."); return; }

            Console.WriteLine("USEKY MISE TRACK (zmena faze, mista nebo kola; useky kratsi nez 2 s vynechany)");
            Console.WriteLine("  od        do        trvani  faze                  misto  kolo  trasa na zac.[m]  odometrie[m] v pohybu[s]  fuze[m]  GPS 1 m / 3 m [m]");
            var useky = new List<(DateTime od, DateTime d0, TrackMsg m)>();
            foreach (var m in track)
            {
                if (useky.Count > 0)
                {
                    var u = useky[^1];
                    if (u.m.Phase == m.Phase && u.m.PointIndex == m.PointIndex && u.m.Lap == m.Lap)
                    {
                        useky[^1] = (u.od, m.TimeStamp, u.m);
                        continue;
                    }
                    useky[^1] = (u.od, m.TimeStamp, u.m);   // usek konci az prichodem dalsiho stavu
                }
                useky.Add((m.TimeStamp, m.TimeStamp, m));
            }
            double celkemJizda = 0;
            foreach (var (od, d0, m) in useky)
            {
                if ((d0 - od).TotalSeconds < 2) continue;
                double dOdom = kOdom.Mezi(od, d0);
                double vPohybu = odoOdom.Since(od).MovingSec - odoOdom.Since(d0).MovingSec;
                if ((TrackPhase)m.Phase == TrackPhase.Driving) celkemJizda += dOdom;
                Console.WriteLine(string.Format(Ci, "  {0:HH:mm:ss}  {1:HH:mm:ss}  {2,6:F0}  {3,-21} {4,2}/{5,-2}  {6,4}  {7,16:F0}  {8,12:F1} {9,10:F0}  {10,7:F1}  {11,7:F1} / {12,6:F1}",
                    od, d0, (d0 - od).TotalSeconds, (TrackPhase)m.Phase, m.PointIndex + 1, m.PointCount, m.Lap,
                    m.RouteLengthM, dOdom, vPohybu, kFuze.Mezi(od, d0), kGps1.Mezi(od, d0), kGps3.Mezi(od, d0)));
            }
            Console.WriteLine(string.Format(Ci, "  odometrie ve fazi Driving celkem {0:F1} m", celkemJizda));
            Console.WriteLine("  (trasa na zac. = delka naplanovane trasy k mistu v prvni zprave useku; Track mezi misty nezastavuje,");
            Console.WriteLine("   takze ujeta draha useku je draha od prepnuti cile po dalsi prepnuti)");
            Console.WriteLine();
        }

        private static double Pct(double a, double b) => b > 0 ? 100.0 * (a - b) / b : double.NaN;

        /// <summary>
        /// Draha z GPS fixu s kotvou: prirustek az po vzdaleni o <paramref name="kotva"/> metru od posledni
        /// zapoctene polohy. Zemepisne souradnice jsou v radianech (GPSState od 26. 8. 2026).
        /// </summary>
        private static Kum GpsDraha(List<GPSState> gps, double kotva, out double celkem, out int fixu)
        {
            const double R = 6371000.0;
            var k = new Kum();
            celkem = 0;
            fixu = 0;
            double? aLat = null, aLon = null;
            foreach (var g in gps)
            {
                if (!g.IsFixed) continue;
                fixu++;
                if (aLat == null) { aLat = g.Latitude; aLon = g.Longitude; }
                double dn = (g.Latitude - aLat.Value) * R;
                double de = (g.Longitude - aLon.Value) * R * Math.Cos(g.Latitude);
                double d = Math.Sqrt(dn * dn + de * de);
                if (d >= kotva) { celkem += d; aLat = g.Latitude; aLon = g.Longitude; }
                k.Add(g.TimeStamp, celkem);
            }
            return k;
        }
    }
}
