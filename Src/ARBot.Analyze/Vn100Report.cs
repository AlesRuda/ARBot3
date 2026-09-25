using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ARBot.Common.Devices;
using ARBot.Common.Models;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Prověření samotného VN100 ze záznamu</b> — bez sáhnutí na senzor.
    ///
    /// <para><b>Nacpak.</b> <c>heading</c> zjistí, ŽE je kurz vedle, a porovnáním s kurzem
    /// přepočteným z pole ukáže, že se atitudové řešení VN odtáhlo od vlastního magnetometru
    /// (viz doc/imu-and-frames.md, nález z 6. 9. 2026). Otázka „proč" se ale dá zúžit i dál,
    /// a pořád jen ze záznamu — protože záznam nese <b>všechno, co senzor poslal</b>: yaw,
    /// jeho vlastní odhad nejistoty (YprU), gyroskop i pole.</para>
    ///
    /// <para>Tři otázky, na které to odpovídá:</para>
    /// <list type="number">
    /// <item><b>Co senzor tvrdí o sobě?</b> <c>YprU</c> je 1σ jeho vlastního kurzu — a je to
    ///   přesně to číslo, které si <c>DefaultMeasurementMapper</c> bere jako σ měření
    ///   <c>IMU/heading</c>. Když senzor hlásí desetinu stupně a přitom je o 59° vedle, není
    ///   to jen vada senzoru, ale i vysvětlení, proč mu fúze bezvýhradně věří.</item>
    /// <item><b>Reaguje yaw vůbec na rozpor s vlastním magnetometrem?</b> Kdyby VN kurz
    ///   z magnetometru používal, byl by ve výstupu vidět <b>zpětnovazební člen</b>: přírůstek
    ///   yaw by byl o kousek větší, než co říká gyroskop, a ten rozdíl by mířil k poli.
    ///   Změří se to regresí <c>(Δyaw/Δt − ω_z)</c> na <c>(kurz z pole − yaw)</c>; směrnice je
    ///   zesílení zpětné vazby [1/s] a její převrácená hodnota časová konstanta.</item>
    /// <item><b>Je yaw čistá integrace gyra?</b> Když je zesílení v mezích šumu nuly, chová se
    ///   yaw jako volně běžící integrace — tedy jako <i>relativní</i> kurz. Doplňkově se měří
    ///   drift yaw proti poli za celou dobu záznamu (kolik stupňů za hodinu).</item>
    /// </list>
    ///
    /// <para><b>Co to NEDOKÁŽE:</b> přečíst konfigurační registry senzoru (35 VPE heading mode,
    /// 44 HSI mode, 23, 26). Ty v záznamu nejsou a je na ně potřeba připojený senzor
    /// (read-only <c>VNRRG</c>). Tenhle report ale umí říct, jestli se senzor <b>chová</b> tak,
    /// jako by magnetometr na kurz nepoužíval — a to je ta otázka, kvůli které se do registrů
    /// leze.</para>
    /// </summary>
    public static class Vn100Report
    {
        /// <summary>Délka okna, na kterém se porovnává přírůstek yaw s integrálem gyra [s].</summary>
        private const double WindowS = 1.0;

        public static void Run(RecordFile rec, double bRefG, double refInclinationDeg,
                               double camWinSec = 3.0, double camDeadSec = 0.5)
        {
            var s = new List<Sample>();
            var speed = new List<(double T, double V)>();
            var motor = new List<(double T, double Amp)>();
            DateTime t0 = DateTime.MinValue;
            var zdroje = new SortedDictionary<string, bool>(StringComparer.Ordinal);
            int relativnich = 0;

            foreach (var e in rec.Index)
            {
                if (e.MsgName != "IMUState" && e.MsgName != "GPSState"
                    && e.MsgName != "MotorStateBase") continue;
                var msg = rec.Read(e);
                switch (msg)
                {
                    case IMUState i when i.Rotation.HasValue:
                        // ⚠️ Stejny duvod jako v HeadingReferencesReport: v robotu je IMU vic
                        // a T265 posila RELATIVNI yaw. Tenhle report zkouma VN100, takze
                        // zdroje bez absolutniho kurzu se vynechavaji - jinak by se do rady
                        // yaw michaly dve ruzne nuly a vysel by nesmysl.
                        zdroje[i.Name ?? "(bez jmena)"] = i.HasAbsoluteHeading;
                        if (!i.HasAbsoluteHeading) { relativnich++; break; }

                        var ypr = i.YPR();
                        if (ypr == null) break;
                        s.Add(new Sample
                        {
                            T = Sec(i.TimeStamp, ref t0),
                            Yaw = ypr.Yaw,
                            GyroZ = i.AngularVelocity?.Z,
                            GyroRaw = i.AngularVelocityRaw,
                            Gyro = i.AngularVelocity,
                            Temp = i.Temperature,
                            Unc = i.OrientationUncertainty,
                            Mag = i.Magnetometer,
                            Acc = i.Acceleration,
                        });
                        break;
                    case GPSState p:
                        speed.Add((Sec(p.TimeStamp, ref t0), p.Speed ?? p.DynamicSpeed ?? double.NaN));
                        break;
                    case MotorStateBase mo:
                        motor.Add((Sec(mo.TimeStamp, ref t0),
                                   Math.Abs(mo.LeftMotorCurrent) + Math.Abs(mo.RightMotorCurrent)));
                        break;
                }
            }

            Console.WriteLine("IMUState podle zdroje:");
            foreach (var z in zdroje)
                Console.WriteLine($"  {z.Key,-28} {(z.Value ? "absolutni kurz - POUZITO" : "RELATIVNI yaw - VYNECHANO")}");
            if (relativnich > 0)
                Console.WriteLine($"  (vynechano {relativnich} vzorku z relativnich zdroju)");
            Console.WriteLine();
            Console.WriteLine($"IMUState se zapsanou atitudou: {s.Count}");
            if (s.Count < 100) { Console.WriteLine("Prilis malo vzorku."); return; }
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "delka zaznamu: {0:F1} s, kadence {1:F1} Hz", s[s.Count - 1].T - s[0].T,
                (s.Count - 1) / Math.Max(1e-9, s[s.Count - 1].T - s[0].T)));
            Console.WriteLine();

            SelfReportedUncertainty(s);
            // Vyhlazene zrychleni potrebuji DVA bloky (kurz z pole i sklon pole), takze se
            // pocita jednou tady - dve kopie by se mohly rozejit v sirce okna.
            var acc = MovingAverageAcc(s, 0.5);
            var field = FieldHeading(s, acc);
            var okna = MagFeedback(s, field, acc);
            MagFeedbackByDeviation(okna, bRefG, refInclinationDeg);
            StandingDrift(s, field, speed);
            MotorInterference(s, motor);
            CameraInterference(rec, s, t0, camWinSec, camDeadSec);
            FilterGyroBias(s, motor);
        }

        /// <summary>
        /// Blok 6 — <b>odhad biasu gyra z filtru VN</b> (<c>Gyro − UncompGyro</c>) a teplota, po
        /// minutach. Od <c>IMUState</c> verze 5 (24. 9. 2026).
        ///
        /// <para><b>Nacpak:</b> 23. 9. 2026 ujel kurz VN100 o ~1 °/s spolu s nahravanym gyrem
        /// (<c>Gyro</c> je kompenzovane odhadem biasu z VPE) a nebylo poznat, jestli je to gyro,
        /// nebo filtr. Tady je to videt primo: kdyz roste rozdil, dela to filtr; kdyz roste surove
        /// gyro ve stani, dela to senzor (a pak je zajimava teplota).</para>
        /// </summary>
        private static void FilterGyroBias(List<Sample> s, List<(double T, double Amp)> motor)
        {
            Console.WriteLine();
            Console.WriteLine("6) ODHAD BIASU GYRA Z FILTRU VN (Gyro - UncompGyro) A TEPLOTA:");
            var raw = s.Where(x => x.GyroRaw.HasValue && x.Gyro.HasValue).ToList();
            if (raw.Count < 100)
            {
                Console.WriteLine("  Zaznam UncompGyro nenese (IMUState verze < 5, pred 24. 9. 2026) - nelze.");
                return;
            }
            const double D = 180.0 / Math.PI * 3600.0;   // rad/s -> deg/h
            bool shodne = raw.All(x => x.GyroRaw.Value == x.Gyro.Value);
            if (shodne)
                Console.WriteLine("  ⚠️ Gyro a UncompGyro jsou BIT PO BITU SHODNE - vystup UncompGyro "
                                  + "tedy NENI bez biasu (jako UncompMag 12. 9. 2026).");

            // Stani = proud motoru pod 0,5 A (stejne jako blok 4).
            bool Stoji(double t)
            {
                if (motor.Count == 0) return false;
                int lo = 0, hi = motor.Count - 1;
                while (lo < hi) { int m = (lo + hi) / 2; if (motor[m].T < t) lo = m + 1; else hi = m; }
                return Math.Abs(motor[lo].T - t) < 0.5 && motor[lo].Amp < 0.5;
            }

            Console.WriteLine("    minuta      n   bias filtru Z [deg/h]   syrove gyro Z ve stani [deg/h]  (n)   teplota [C]");
            double t0 = raw[0].T;
            foreach (var g in raw.GroupBy(x => (int)((x.T - t0) / 60)).OrderBy(g => g.Key))
            {
                var bias = g.Select(x => (double)(x.Gyro.Value.Z - x.GyroRaw.Value.Z) * D).ToList();
                var stani = g.Where(x => Stoji(x.T)).Select(x => (double)x.GyroRaw.Value.Z * D).ToList();
                var tep = g.Where(x => x.Temp.HasValue).Select(x => x.Temp.Value).ToList();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,4}-{1,4} s {2,6}   {3,12:F1}           {4,12}              {5,5}   {6,8}",
                    g.Key * 60, g.Key * 60 + 60, bias.Count, bias.Average(),
                    stani.Count > 0 ? stani.Average().ToString("F1", CultureInfo.InvariantCulture) : "-",
                    stani.Count,
                    tep.Count > 0 ? tep.Average().ToString("F2", CultureInfo.InvariantCulture) : "-"));
            }
            Console.WriteLine("  Bias filtru = co VPE od gyra odecita (znamenko: Gyro = UncompGyro + bias).");
            Console.WriteLine("  Kdyz se meni bias filtru a syrove gyro ve stani stoji, ujizdi FILTR, ne senzor.");
        }

        /// <summary>Blok 1 — co senzor tvrdi o vlastni presnosti (YprU).</summary>
        private static void SelfReportedUncertainty(List<Sample> s)
        {
            Console.WriteLine("1) CO SENZOR TVRDI O SOBE (YprU = jeho vlastni 1 sigma):");
            var yawU = new Stats("yaw 1 sigma");
            var pitchU = new Stats("pitch 1 sigma");
            var rollU = new Stats("roll 1 sigma");
            foreach (var x in s)
                if (x.Unc is Vector3 u)
                {
                    yawU.Add(Deg(u.X)); pitchU.Add(Deg(u.Y)); rollU.Add(Deg(u.Z));
                }
            if (yawU.Count == 0)
            {
                Console.WriteLine("  Zaznam YprU nenese (ASCII driver ho neposila).");
                Console.WriteLine();
                return;
            }
            Console.WriteLine("  " + yawU.Line("deg"));
            Console.WriteLine("  " + pitchU.Line("deg"));
            Console.WriteLine("  " + rollU.Line("deg"));
            Console.WriteLine("  Tohle cislo si bere DefaultMeasurementMapper jako sigma mereni IMU/heading,");
            Console.WriteLine("  takze rika, jak moc fuze kompasu veri. Porovnej se skutecnou chybou z 'heading'.");
            Console.WriteLine();
        }

        /// <summary>
        /// Blok 4 — <b>je pole rusene vlastnim robotem?</b> Rozhoduje to o tom, jestli ma vubec
        /// smysl magnetometr kalibrovat: kalibrace (hard/soft iron) umi odecist jen pole, ktere je
        /// v telesovem ramci <b>konstantni</b>. Pole od motoru se meni s proudem, takze otocenim
        /// robotu se nezmeri a odecist nejde — a kdo ho kalibraci "odecte", zafixuje stav pri jednom
        /// proudu a pri jinem si pohorsi.
        ///
        /// <para>Meri se dve veci: (a) zavislost <c>|B|</c> na celkovem proudu motoru (regrese,
        /// G/A), (b) rozdil <c>|B|</c> mezi STANIM a JIZDOU. Zemske pole je konstantni, takze
        /// jakakoli zavislost je rusení. Rozdil mezi stanim a jizdou nemusi byt jen od motoru
        /// (robot se pri jizde taky presouva jinam), ale nulovy rozdil to vylucuje.</para>
        /// </summary>
        private static void MotorInterference(List<Sample> s, List<(double T, double Amp)> motor)
        {
            Console.WriteLine();
            Console.WriteLine("4) JE POLE RUSENE VLASTNIM ROBOTEM? (rozhoduje, jestli lze kalibrovat)");
            if (motor.Count < 50)
            {
                Console.WriteLine("  Zaznam nenese stav motoru - nelze rict.");
                Console.WriteLine();
                return;
            }

            motor.Sort((a, b) => a.T.CompareTo(b.T));
            var amp = new List<double>();
            var bmag = new List<double>();
            var stand = new Stats("|B| pri STANI (proud pod 0,5 A)");
            var drive = new Stats("|B| pri JIZDE (proud nad 2 A)");
            var ampStat = new Stats("celkovy proud motoru");
            int mi = 0;
            foreach (var x in s)
            {
                if (!(x.Mag is Vector3 m)) continue;
                while (mi + 1 < motor.Count && motor[mi + 1].T <= x.T) mi++;
                if (Math.Abs(motor[mi].T - x.T) > 0.5) continue;      // bez soucasneho stavu motoru
                double b = Math.Sqrt(m.X * m.X + m.Y * m.Y + m.Z * m.Z);
                double a = motor[mi].Amp;
                amp.Add(a); bmag.Add(b); ampStat.Add(a);
                if (a < 0.5) stand.Add(b);
                else if (a > 2.0) drive.Add(b);
            }

            if (amp.Count < 100)
            {
                Console.WriteLine("  Prilis malo sparovanych vzorku.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine("  " + ampStat.Line("A"));
            Console.WriteLine("  " + stand.Line("G"));
            Console.WriteLine("  " + drive.Line("G"));

            double ma = amp.Average(), mb = bmag.Average(), sxx = 0, sxy = 0;
            for (int k = 0; k < amp.Count; k++)
            { sxx += (amp[k] - ma) * (amp[k] - ma); sxy += (amp[k] - ma) * (bmag[k] - mb); }
            if (sxx < 1e-9)
            {
                Console.WriteLine("  Proud se v zaznamu skoro nemeni - zavislost nejde odhadnout.");
                Console.WriteLine();
                return;
            }
            double slope = sxy / sxx, c = mb - slope * ma, ss = 0;
            for (int k = 0; k < amp.Count; k++) { double d = bmag[k] - (slope * amp[k] + c); ss += d * d; }
            double sd = Math.Sqrt(ss / Math.Max(1, amp.Count - 2));
            double se = sd / Math.Sqrt(sxx);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  |B| na proudu: {0:F5} +- {1:F5} G/A   ({2:F1} sigma od nuly), rozsah proudu {3:F1} A",
                slope, se, se > 0 ? Math.Abs(slope) / se : 0, ampStat.Max - ampStat.Min));
            if (stand.Count > 20 && drive.Count > 20)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  |B| jizda - stani: {0:+0.000;-0.000} G (mediany {1:F3} vs {2:F3})",
                    drive.Median - stand.Median, drive.Median, stand.Median));
            Console.WriteLine("  Zemske pole je KONSTANTNI, takze kazda zavislost je RUSENI od robotu.");
            Console.WriteLine("  Ruseni zavisle na proudu kalibrace neodstrani (neni v telesovem ramci");
            Console.WriteLine("  konstantni) - to se resi az stinenim nebo presunem senzoru.");
            Console.WriteLine();
        }

        /// <summary>
        /// Kurz prepocteny ze syroveho pole (svislice z akcelerometru) pro kazdy vzorek;
        /// <c>NaN</c>, kde to nejde. Tataz metoda jako v <see cref="HeadingReferencesReport"/> —
        /// tady se ale nepotrebuje statistika, nybrz casova rada.
        /// </summary>
        private static double[] FieldHeading(List<Sample> s, Vector3?[] acc)
        {
            var outv = new double[s.Count];
            for (int i = 0; i < s.Count; i++) outv[i] = double.NaN;

            // Gravitace je nizkofrekvencni cast zrychleni; jednotlivy vzorek je na trave
            // nepouzitelny. Prah kolem MEDIANU |a|, ne kolem tabulkoveho g - viz komentar
            // v HeadingReferencesReport.Magnetometer.
            var norm = new Stats("|a|");
            for (int i = 0; i < s.Count; i++)
                if (acc[i] is Vector3 a) norm.Add(Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z));
            if (norm.Count == 0) return outv;
            double aRef = norm.Percentile(50);

            for (int i = 0; i < s.Count; i++)
            {
                if (!(acc[i] is Vector3 a) || !(s[i].Mag is Vector3 m)) continue;
                double an = Math.Sqrt(a.X * a.X + a.Y * a.Y + a.Z * a.Z);
                if (an < 0.98 * aRef || an > 1.02 * aRef) continue;
                double mn = Math.Sqrt(m.X * m.X + m.Y * m.Y + m.Z * m.Z);
                if (mn < 1e-6) continue;

                double ux = a.X / an, uy = a.Y / an, uz = a.Z / an;
                double mu = m.X * ux + m.Y * uy + m.Z * uz;
                double hx = m.X - mu * ux, hy = m.Y - mu * uy, hz = m.Z - mu * uz;
                double fx = 1 - ux * ux, fy = -ux * uy, fz = -ux * uz;
                double fn = Math.Sqrt(fx * fx + fy * fy + fz * fz);
                if (fn < 1e-6) continue;
                fx /= fn; fy /= fn; fz /= fn;
                double lx = uy * fz - uz * fy, ly = uz * fx - ux * fz, lz = ux * fy - uy * fx;
                double azimut = Math.Atan2(hx * lx + hy * ly + hz * lz, hx * fx + hy * fy + hz * fz);
                outv[i] = Math.PI / 2 - azimut;
            }
            return outv;
        }

        /// <summary>
        /// Blok 2+3 — reaguje yaw na rozpor s vlastnim polem? Na oknech po
        /// <see cref="WindowS"/> se porovna PRIRUSTEK yaw s integralem gyra; co zbyde, je
        /// oprava, kterou filtr senzoru pridal. Ta se regresuje na chybu proti poli.
        /// </summary>
        private static List<Window> MagFeedback(List<Sample> s, double[] field, Vector3?[] acc)
        {
            Console.WriteLine("2) REAGUJE YAW NA ROZPOR S VLASTNIM MAGNETOMETREM?");
            var okna = new List<Window>();
            if (s.All(x => x.GyroZ == null))
            {
                Console.WriteLine("  Zaznam nenese uhlovou rychlost - nelze oddelit integraci gyra od opravy.");
                Console.WriteLine();
                return okna;
            }

            var e = new List<double>();      // chyba proti poli [rad]
            var r = new List<double>();      // oprava nad ramec gyra [rad/s]
            var rStat = new Stats("oprava nad ramec gyra");
            int i0 = 0;
            while (i0 < s.Count - 1)
            {
                int i1 = i0 + 1;
                while (i1 < s.Count && s[i1].T - s[i0].T < WindowS) i1++;
                if (i1 >= s.Count) break;
                double T = s[i1].T - s[i0].T;
                if (T > 2 * WindowS) { i0 = i1; continue; }   // mezera v datech

                // Integral gyra pres okno (lichobezniky) a chyba proti poli (kruhovy prumer).
                double integ = 0; bool ok = true;
                double sx = 0, sy = 0; int n = 0;
                // |B| a sklon na okne - vstup pro rozpad zesileni po kosich odchylky (blok 2b).
                double sumB = 0, sumSkl = 0; int nB = 0, nSkl = 0;
                for (int k = i0; k < i1; k++)
                {
                    if (s[k].GyroZ == null || s[k + 1].GyroZ == null) { ok = false; break; }
                    integ += 0.5 * (s[k].GyroZ.Value + s[k + 1].GyroZ.Value) * (s[k + 1].T - s[k].T);
                    if (!double.IsNaN(field[k]))
                    {
                        double d = Wrap(field[k] - s[k].Yaw);
                        sx += Math.Cos(d); sy += Math.Sin(d); n++;
                    }
                    if (!(s[k].Mag is Vector3 m)) continue;
                    double mn = m.Length();
                    if (mn < 1e-6) continue;
                    sumB += mn; nB++;
                    if (!(acc[k] is Vector3 g)) continue;
                    double gn = g.Length();
                    if (gn < 1e-6) continue;

                    // ⚠️ Sklon je velicina SVETOVA, takze se sklapi gravitaci:
                    // sin(sklon) = m̂ · ĝ_dolu, kde ĝ_dolu = −acc/|acc| (akcelerometr v klidu
                    // meri −g). TATAZ konvence jako v MagCalFit; z telesoveho pole by pri
                    // naklonech vysel rozptyl v desitkach stupnu i u perfektniho senzoru.
                    double dot = -(m.X * g.X + m.Y * g.Y + m.Z * g.Z) / (mn * gn);
                    sumSkl += Math.Asin(Math.Clamp(dot, -1.0, 1.0)); nSkl++;
                }
                if (ok)
                {
                    double corr = (Wrap(s[i1].Yaw - s[i0].Yaw) - integ) / T;
                    rStat.Add(Deg(corr));
                    if (n > 0)
                    {
                        double chyba = Math.Atan2(sy / n, sx / n);
                        e.Add(chyba); r.Add(corr);
                        okna.Add(new Window
                        {
                            E = chyba,
                            R = corr,
                            Yaw = s[i0].Yaw,
                            MagG = nB > 0 ? sumB / nB : double.NaN,
                            InclDeg = nSkl > 0 ? Deg(sumSkl / nSkl) : double.NaN,
                        });
                    }
                }
                i0 = i1;
            }

            Console.WriteLine($"  oken po {WindowS:F1} s: {rStat.Count} (z toho s polem: {e.Count})");
            if (rStat.Count > 0) Console.WriteLine("  " + rStat.Line("deg/s"));
            if (e.Count < 30)
            {
                Console.WriteLine("  Prilis malo oken s polem - regrese by nic nerekla.");
                Console.WriteLine();
                return okna;
            }

            // r = K*e + c; K je zesileni zpetne vazby [1/s], 1/K casova konstanta.
            double me = e.Average(), mr = r.Average();
            double sxx = 0, sxy = 0;
            for (int k = 0; k < e.Count; k++) { sxx += (e[k] - me) * (e[k] - me); sxy += (e[k] - me) * (r[k] - mr); }
            if (sxx < 1e-12)
            {
                Console.WriteLine("  Chyba proti poli se v zaznamu skoro nemeni - zesileni nejde odhadnout.");
                Console.WriteLine();
                return okna;
            }
            double K = sxy / sxx, c = mr - K * me;
            double ss = 0;
            for (int k = 0; k < e.Count; k++) { double d = r[k] - (K * e[k] + c); ss += d * d; }
            double sd = Math.Sqrt(ss / Math.Max(1, e.Count - 2));
            double seK = sd / Math.Sqrt(sxx);

            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  rozsah chyby proti poli: {0:F1} .. {1:F1} deg (sd {2:F1})",
                Deg(e.Min()), Deg(e.Max()), Deg(Math.Sqrt(sxx / e.Count))));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  zesileni zpetne vazby K = {0:F5} +- {1:F5} 1/s   ({2:F1} sigma od nuly)",
                K, seK, seK > 0 ? Math.Abs(K) / seK : 0));
            if (K > 3 * seK)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  => magnetometr SE NA KURZ POUZIVA, casova konstanta {0:F1} s", 1.0 / K));
            else
                Console.WriteLine("  => magnetometr se na kurz PROKAZATELNE NEPOUZIVA (zesileni v mezich sumu nuly)");
            Console.WriteLine("  Pozn.: kladne K znamena, ze filtr yaw tahne K POLI. Zaporne nebo nulove");
            Console.WriteLine("  znamena, ze yaw je volne bezici integrace gyra - tedy RELATIVNI kurz.");
            Console.WriteLine("  ⚠️ Sum v CHYBE (kurz z pole je sam zasumeny) tlaci K SYSTEMATICKY K NULE");
            Console.WriteLine("  (regresni utlum), takze male K je horni odhad, ne presna hodnota. Model-free");
            Console.WriteLine("  kontrola: kdyby filtr pole pouzival, musel by rozpor proti GPS kurzu v case");
            Console.WriteLine("  KLESAT - to ukaze 'heading' po minutach.");
            Console.WriteLine();
            return okna;
        }

        /// <summary>
        /// <b>Blok 2b — dusi VPE magnetometr, kdyz se pole rozejde s referencnim vektorem?</b>
        ///
        /// <para><b>Nacpak.</b> Registr 36 zapina adaptivni ladeni VPE, ktere ma magnetometr
        /// <i>zamerne</i> utlumit, kdyz se merene <c>|B|</c> a sklon rozejdou s referenci
        /// z registru 21. Na tom stoji rozhodnuti zapnout model pole (<c>magmodel=</c>): registr
        /// 21 dnes znamena sklon 60,9°, ackoli pro CR je ~65,7°, takze i perfektni kalibrace
        /// muze zustat castecne udusena. Z dokumentace VN se to vycist neda; ze zaznamu se to
        /// da <b>podporit nebo vyvratit</b>. Hypoteza predpovida <b>monotonni pokles K</b>
        /// s rostouci odchylkou. Viz doc/plan-vn100-kalibrace.md, „Levne overeni hypotezy".</para>
        ///
        /// <para>⚠️ <b>Je to NUTNA PODMINKA, NE DUKAZ</b> — a to ze dvou nezavislych duvodu:</para>
        /// <list type="number">
        /// <item><b>Odchylka <c>|B|</c> je sama funkci kurzu</b> (dela ji tvrde zelezo), takze
        ///   „K klesa s odchylkou" muze byt zastrene „K zavisi na kurzu". Proto se tiskne i rozpad
        ///   po kosich KURZU a podil rozptylu odchylky, ktery kurz vysvetli (η²): kdyz je η²
        ///   blizko 1, ty dva rozpady se od sebe <b>nedaji odlisit</b>.</item>
        /// <item><b>Regresni utlum se v kosich lisi.</b> K se tlaci k nule tim vic, cim mensi
        ///   je rozptyl chyby proti poli v tom kosi — takze kos s uzsim rozsahem chyby ukaze
        ///   mensi K, i kdyby VPE dusilo stejne. Proto je u kazdeho kosi vypsana i sd chyby.</item>
        /// </list>
        ///
        /// <para><b>Kose jsou kvantily, ne pevne hranice</b> — pri pevnych by kos zustal skoro
        /// prazdny a jeho K by byl sum.</para>
        /// </summary>
        private static void MagFeedbackByDeviation(List<Window> okna, double bRefG,
                                                   double refInclinationDeg)
        {
            Console.WriteLine("2b) DUSI VPE MAGNETOMETR PRI NESOUHLASU S REGISTREM 21?");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  reference (registr 21): |B| = {0:F4} G, sklon = {1:F1} deg"
                + "   (--bref=/--incl= je prepisou)", bRefG, refInclinationDeg));

            var sB = okna.Where(w => !double.IsNaN(w.MagG)).ToList();
            var sI = okna.Where(w => !double.IsNaN(w.InclDeg)).ToList();
            if (sB.Count < 4 * MinBinWindows)
            {
                Console.WriteLine($"  Oken s polem je malo ({sB.Count}) - rozpad by nic nerekl.");
                Console.WriteLine();
                return;
            }

            Rozpad("odchylka |B| [G]", sB, w => Math.Abs(w.MagG - bRefG), "F4");
            if (sI.Count >= 4 * MinBinWindows)
                Rozpad("odchylka sklonu [deg]", sI, w => Math.Abs(w.InclDeg - refInclinationDeg), "F1");
            else
                Console.WriteLine("  Sklon: malo oken se zrychlenim - rozpad podle sklonu nelze.");

            // KONTROLA KONFUNDANTU: tentyz rozpad podle kurzu. Kdyz K kolisa i tady, neni
            // z ceho rozhodnout, ktera z tech dvou promennych ho hybe.
            Rozpad("kurz [deg]  (KONTROLA)", sB, w => Deg(w.Yaw), "F0");

            // Jak moc je odchylka |B| funkci kurzu: podil rozptylu vysvetleny azimutovymi kosi
            // (η²). Blizko 1 znamena, ze se oba rozpady od sebe nedaji odlisit.
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  podil rozptylu odchylky |B|, ktery vysvetli KURZ (η², 24 kosu po 15 deg):"
                + " {0:F3}", Eta2ByHeading(sB, bRefG)));
            Console.WriteLine("  η² blizko 1 = odchylka |B| je skoro funkci kurzu, takze"
                              + " „K klesa s odchylkou\" a „K zavisi na kurzu\" JSOU TOTEZ MERENI.");
            Console.WriteLine("  ⚠️ Souhlasny vysledek hypotezu NEDOKAZUJE - viz dokumentace metody.");
            Console.WriteLine();
        }

        /// <summary>Kolik oken musi byt v kosi, aby mela regrese smysl.</summary>
        private const int MinBinWindows = 25;

        /// <summary>
        /// Vytiskne zesileni <c>K</c> ve ctyrech kvantilovych kosich podle
        /// <paramref name="podle"/>. Krome <c>K</c> i sd chyby proti poli — bez ni se rozdil
        /// mezi kosi nemuze vylozit (regresni utlum, viz
        /// <see cref="MagFeedbackByDeviation"/>).
        /// </summary>
        private static void Rozpad(string nazev, List<Window> okna, Func<Window, double> podle,
                                   string format)
        {
            var setrid = okna.OrderBy(podle).ToList();
            int kosu = 4, n = setrid.Count;
            Console.WriteLine($"  po kosich: {nazev}");
            Console.WriteLine("    kos  rozsah                 n     K [1/s]        sd(chyby) [deg]");
            for (int q = 0; q < kosu; q++)
            {
                int od = q * n / kosu, doIdx = (q + 1) * n / kosu;
                var kos = setrid.GetRange(od, doIdx - od);
                if (kos.Count < MinBinWindows)
                {
                    Console.WriteLine($"    {q + 1}    (jen {kos.Count} oken - vynechano)");
                    continue;
                }
                var (K, seK, sdE) = Regrese(kos);
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "    {0}    {1,8:" + format + "} .. {2,8:" + format + "}  {3,5}   "
                    + "{4,8:F5} +- {5:F5}   {6,6:F1}",
                    q + 1, podle(kos[0]), podle(kos[kos.Count - 1]), kos.Count, K, seK, sdE));
            }
        }

        /// <summary>Regrese <c>r = K·e + c</c> na jedne skupine oken; vraci i sd chyby.</summary>
        private static (double K, double SeK, double SdEdeg) Regrese(List<Window> okna)
        {
            double me = okna.Average(w => w.E), mr = okna.Average(w => w.R);
            double sxx = 0, sxy = 0;
            foreach (var w in okna) { sxx += (w.E - me) * (w.E - me); sxy += (w.E - me) * (w.R - mr); }
            if (sxx < 1e-12) return (double.NaN, double.NaN, 0);
            double K = sxy / sxx, c = mr - K * me, ss = 0;
            foreach (var w in okna) { double d = w.R - (K * w.E + c); ss += d * d; }
            double sd = Math.Sqrt(ss / Math.Max(1, okna.Count - 2));
            return (K, sd / Math.Sqrt(sxx), Deg(Math.Sqrt(sxx / okna.Count)));
        }

        /// <summary>
        /// Podil rozptylu odchylky <c>|B|</c>, ktery vysvetli kurz (η² pres azimutove kose).
        /// Cim blize 1, tim mene se rozpad podle odchylky da odlisit od rozpadu podle kurzu.
        /// </summary>
        private static double Eta2ByHeading(List<Window> okna, double bRefG)
        {
            const int Kosu = 24;
            var kose = new List<double>[Kosu];
            for (int i = 0; i < Kosu; i++) kose[i] = new List<double>();
            foreach (var w in okna)
            {
                double a = w.Yaw % (2 * Math.PI);
                if (a < 0) a += 2 * Math.PI;
                int i = Math.Min(Kosu - 1, (int)(a / (2 * Math.PI / Kosu)));
                kose[i].Add(Math.Abs(w.MagG - bRefG));
            }
            double vse = okna.Average(w => Math.Abs(w.MagG - bRefG));
            double ssTot = okna.Sum(w => Math.Pow(Math.Abs(w.MagG - bRefG) - vse, 2));
            if (!(ssTot > 0)) return double.NaN;
            double ssMezi = kose.Where(k => k.Count > 0)
                                .Sum(k => k.Count * Math.Pow(k.Average() - vse, 2));
            return ssMezi / ssTot;
        }

        /// <summary>
        /// Blok 3b — jak rychle yaw utika proti poli, a kolik z toho je bias gyra videt ve stani.
        /// Ve stani je uhlova rychlost nula, takze cokoli, co yaw dela, je drift.
        /// </summary>
        private static void StandingDrift(List<Sample> s, double[] field, List<(double T, double V)> speed)
        {
            Console.WriteLine("3) DRIFT YAW PROTI POLI A KLIDOVY BIAS GYRA:");

            // Drift chyby proti poli se meri JEN NA USECICH S TEMEZ KURZEM.
            //
            // ⚠️ Past, do ktere tenhle report nejdriv spadl: prolozit primku pres cely zaznam
            // dalo 812 deg/h, coz je nesmysl - kurz proti GPS zustal celou dobu na -59 deg.
            // Duvod: kurz PREPOCTENY Z POLE ma vlastni chybu zavislou na kurzu (zbytkove
            // zelezo), takze po otocce o 180 deg skoci - a proklad casem to precte jako drift.
            // Porovnavat se smi jen v ramci useku, kde se kurz temer nemeni.
            const double SegHeadingTolRad = 0.26;   // ~15 deg
            const double SegMinS = 10.0;
            Console.WriteLine("  drift chyby proti poli po usecich s temez kurzem:");
            int printed = 0;
            var driftStat = new Stats("drift po usecich");
            int a0 = 0;
            while (a0 < s.Count)
            {
                if (double.IsNaN(field[a0])) { a0++; continue; }
                int a1 = a0;
                while (a1 + 1 < s.Count && Math.Abs(Wrap(s[a1 + 1].Yaw - s[a0].Yaw)) < SegHeadingTolRad) a1++;
                double dur = s[a1].T - s[a0].T;
                if (dur >= SegMinS)
                {
                    var t = new List<double>(); var y = new List<double>();
                    for (int i = a0; i <= a1; i++)
                    {
                        if (double.IsNaN(field[i])) continue;
                        t.Add(s[i].T); y.Add(Wrap(field[i] - s[i].Yaw));
                    }
                    if (t.Count > 50)
                    {
                        double mt = t.Average(), my = y.Average(), sxx = 0, sxy = 0;
                        for (int k = 0; k < t.Count; k++)
                        { sxx += (t[k] - mt) * (t[k] - mt); sxy += (t[k] - mt) * (y[k] - my); }
                        double slope = sxx > 0 ? sxy / sxx : 0;
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "    {0,6:F0}-{1,6:F0} s ({2,5:F0} s, kurz {3,7:F1} deg): chyba {4,7:F1} deg, drift {5,9:F1} deg/h",
                            s[a0].T, s[a1].T, dur, Deg(s[a0].Yaw), Deg(my), Deg(slope) * 3600));
                        printed++;
                        driftStat.Add(Deg(slope) * 3600);
                    }
                }
                a0 = a1 + 1;
            }
            if (printed == 0)
                Console.WriteLine("    zadny usek delsi nez " + SegMinS.ToString("F0", CultureInfo.InvariantCulture)
                                  + " s s temez konstantnim kurzem");
            else
            {
                Console.WriteLine("  " + driftStat.Line("deg/h"));
                Console.WriteLine("  Kdyby filtr pole pouzival, tahla by chyba na useku k nule. Zustava-li");
                Console.WriteLine("  velka a plocha, yaw pole ignoruje. POZOR: na kratkem useku je smernice");
                Console.WriteLine("  hlavne sum kurzu z pole - vazi az median pres useky, a i ten slabe.");
            }

            // Klidovy usek: rychlost z GPS pod 5 cm/s a uhlova rychlost pod 0,01 rad/s.
            var stand = new Stats("uhlova rychlost ve stani");
            int used = 0;
            foreach (var x in s)
            {
                if (x.GyroZ == null) continue;
                if (!TryNearest(speed, x.T, 0.5, out double v) || double.IsNaN(v) || Math.Abs(v) > 0.05) continue;
                if (Math.Abs(x.GyroZ.Value) > 0.01) continue;
                stand.Add(Deg(x.GyroZ.Value)); used++;
            }
            if (used > 100)
            {
                Console.WriteLine("  " + stand.Line("deg/s"));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  klidovy bias gyra: {0:F1} deg/h (VN100 ma in-run stabilitu radu jednotek deg/h)",
                    stand.Mean * 3600));
            }
            else
            {
                Console.WriteLine($"  Klidovych vzorku je malo ({used}) - bias gyra z tohohle zaznamu nejde.");
            }
            Console.WriteLine();
            Console.WriteLine("  ⚠️ Konfiguracni registry senzoru (35 VPE heading mode, 44 HSI, 23, 26)");
            Console.WriteLine("  v zaznamu NEJSOU - na ne je potreba pripojeny senzor a read-only VNRRG.");
        }

        // --- pomucky ---

        /// <summary>
        /// Jedno okno bloku 2: chyba proti poli a oprava nad ramec gyra, a k tomu <c>|B|</c>,
        /// sklon a kurz — vstup pro rozpad zesileni po kosich (blok 2b).
        /// </summary>
        private struct Window
        {
            /// <summary>Chyba proti poli (kurz z pole − yaw) na okne [rad].</summary>
            public double E;

            /// <summary>Oprava nad ramec integrace gyra [rad/s].</summary>
            public double R;

            /// <summary>Yaw na zacatku okna [rad] — kvuli kontrole konfundantu kurzu.</summary>
            public double Yaw;

            /// <summary>Velikost pole na okne [G]; <c>NaN</c>, kdyz pole chybi.</summary>
            public double MagG;

            /// <summary>Sklon pole na okne, SKLOPENY gravitaci [deg]; <c>NaN</c> bez zrychleni.</summary>
            public double InclDeg;
        }

        /// <summary>
        /// <b>Blok 5 — je pole vázané na kamery, resp. na jejich kabely?</b>
        ///
        /// <para><b>Nač to je.</b> Když se mezi dvěma dny změní kurz o desítky stupňů a jediné, co se
        /// na robotu hnulo, jsou <b>kabely ke kamerám</b>, je to hypotéza, kterou záznam umí rozhodnout:
        /// kabel vede <b>proud</b>, a ten teče teprve, když kamera běží. Nahrávání začíná dřív, než
        /// se D435 připojí (řádově jednotky sekund), takže v každém záznamu je kousek pole <b>bez
        /// proudu v kabelech</b> — a při výpadku kamery a jejím zotavení další.</para>
        ///
        /// <para><b>Měří se vektor v tělese, ne <c>|B|</c>.</b> Magnetometr hlásí pole v rámci robotu,
        /// takže při nehybném robotu je zemská složka konstantní a rozdíl středních vektorů před a po
        /// přechodu je <b>přímo příspěvek kabelu</b>. <c>|B|</c> by na to bylo slepé tam, kde je ten
        /// příspěvek kolmý na pole, a hlavně se plete s kurzem (blok 2b: η² = 0,93).</para>
        ///
        /// <para>⚠️ <b>Záměna s otáčením je tady ta hlavní past</b>, proto se ke každému přechodu tiskne
        /// i změna atitudy. Když se robot mezi okny otočil, číslo neplatí — a řádek je proto označený.
        /// Nejdůvěryhodnější je <b>první přechod po startu</b>: robot tehdy stojí a čeká na uvolnění
        /// nouzového zastavení.</para>
        ///
        /// <para><b>Čte se jen index</b> (čas a jméno kamery), ne obrazy — jinak by to na
        /// dvacetigigabajtovém záznamu trvalo desítky minut. Viz doc/imu-and-frames.md.</para>
        /// </summary>
        private static void CameraInterference(RecordFile rec, List<Sample> s, DateTime t0,
                                               double winSec, double deadSec)
        {
            const double GapSec = 1.5;      // delsi mezera = kamera nestreamuje
            if (winSec <= 0) winSec = 3.0;  // okno prumerovani pole na kazde strane prechodu
            // Mrtve pasmo kolem prechodu. ⚠️ Hrana je ROZMAZANA: USB zarizeni se napaji a
            // enumeruje driv, nez dorazi prvni snimek (v logu "pipeline pripojena" ~0,5-1 s pred
            // nim), takze okno tesne pred prvnim snimkem uz muze mit kameru pod proudem. Proto
            // je sirka okna i mrtve pasmo prepinatelne (--camwin=, --camdead=) - cislo, ktere se
            // pri jejich zmene nehne, je dukaz; cislo, ktere se hne, byla kontaminace.
            if (deadSec < 0) deadSec = 0.5;

            Console.WriteLine();
            Console.WriteLine("5) JE POLE VAZANE NA KAMERY (JEJICH KABELY)?");
            if (t0 == DateTime.MinValue) { Console.WriteLine("  Zaznam nenese casovou zakladnu."); return; }

            // Casy snimku po kamerach - jen z indexu, obrazy se nectou.
            var podleKamer = new SortedDictionary<string, List<double>>(StringComparer.Ordinal);
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "CameraFrame" || e.CaptureTicks == 0) continue;
                string jm = string.IsNullOrEmpty(e.Name) ? "(bez jmena)" : e.Name;
                if (!podleKamer.TryGetValue(jm, out var l)) podleKamer[jm] = l = new List<double>();
                l.Add((e.CaptureTime - t0).TotalSeconds);
            }
            if (podleKamer.Count == 0) { Console.WriteLine("  Zaznam nenese CameraFrame."); return; }

            // Prechody: kazda kamera se "rozsviti" prvnim snimkem a "zhasne" mezerou > GapSec.
            var prechody = new List<(double T, string Kdo, bool Zap)>();
            foreach (var kv in podleKamer)
            {
                var l = kv.Value; l.Sort();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-16} {1,6} snimku, prvni v {2:F1} s, posledni v {3:F1} s",
                    kv.Key, l.Count, l[0], l[l.Count - 1]));
                prechody.Add((l[0], kv.Key, true));
                for (int k = 1; k < l.Count; k++)
                    if (l[k] - l[k - 1] > GapSec)
                    {
                        prechody.Add((l[k - 1], kv.Key, false));
                        prechody.Add((l[k], kv.Key, true));
                    }
            }
            prechody.Sort((a, b) => a.T.CompareTo(b.T));

            Console.WriteLine();
            Console.WriteLine("  prechod (kamera zap/vyp) a zmena STREDNIHO VEKTORU POLE v telese:");
            Console.WriteLine("    cas [s]  kamera           z/v  n_pred  n_po   dMag [mG]                |dMag|   zmena atitudy");
            int tisknuto = 0, vynechano = 0;
            foreach (var (t, kdo, zap) in prechody)
            {
                if (!Prumer(s, t - deadSec - winSec, t - deadSec, out var mPred, out var yPred,
                            out var aPred, out int nPred)) { vynechano++; continue; }
                if (!Prumer(s, t + deadSec, t + deadSec + winSec, out var mPo, out var yPo,
                            out var aPo, out int nPo)) { vynechano++; continue; }

                var d = mPo - mPred;
                double dYaw = Math.Abs(Deg(Wrap(yPo - yPred)));
                // Zmena smeru tize = naklon; oboji dohromady rekne, jestli robot stal.
                double dTilt = Deg(Math.Acos(Math.Clamp(
                    Vector3.Dot(Vector3.Normalize(aPo), Vector3.Normalize(aPred)), -1f, 1f)));
                bool klid = dYaw < 2.0 && dTilt < 2.0;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,8:F1}  {1,-16} {2,-3} {3,6} {4,5}   [{5,7:F1},{6,7:F1},{7,7:F1}]  {8,7:F1}   yaw {9,5:F1} dg, naklon {10,4:F1} dg{11}",
                    t, kdo, zap ? "ZAP" : "VYP", nPred, nPo,
                    d.X * 1000, d.Y * 1000, d.Z * 1000, d.Length() * 1000, dYaw, dTilt,
                    klid ? string.Empty : "   << ROBOT SE HYBAL, NEPLATI"));
                if (++tisknuto >= 40) { Console.WriteLine("  (dalsi prechody vynechany)"); break; }
            }
            if (tisknuto == 0)
                Console.WriteLine("  Zadny prechod nema pole na obou stranach - neda se rozhodnout.");
            if (vynechano > 0)
                Console.WriteLine($"  (prechodu bez pole na obou stranach: {vynechano})");

            Console.WriteLine();
            Console.WriteLine("  Jak to cist: kdyz se pole pri ROZSVICENI kamery skokem posune vzdy stejnym");
            Console.WriteLine("  smerem a pri zhasnuti zpet, je zdrojem PROUD V KABELU. Kdyz se nehne, je");
            Console.WriteLine("  zelezo STATICKE (samotny kabel, konektor, srouby) - to kalibrace odstrani,");
            Console.WriteLine("  ale jen dokud se kabel zase nepohne.");
            Console.WriteLine("  !! Rozlisovat ma smysl jen u radku BEZ oznaceni - pri otoceni robotu se meni");
            Console.WriteLine("  i zemska slozka a ta je o rad vetsi nez cokoli, co hleda tenhle blok.");
        }

        /// <summary>Stredni vektor pole, yaw a smer tize v casovem okne; false = malo vzorku.</summary>
        private static bool Prumer(List<Sample> s, double od, double doT, out Vector3 mag,
                                   out double yaw, out Vector3 acc, out int n)
        {
            mag = default; acc = default; yaw = 0; n = 0;
            double sy = 0, cy = 0;
            foreach (var x in s)
            {
                if (x.T < od || x.T > doT) continue;
                if (x.Mag == null || x.Acc == null) continue;
                mag += x.Mag.Value; acc += x.Acc.Value;
                sy += Math.Sin(x.Yaw); cy += Math.Cos(x.Yaw);
                n++;
            }
            if (n < 20) return false;
            mag /= n; acc /= n; yaw = Math.Atan2(sy, cy);
            return true;
        }

        private struct Sample
        {
            public double T;
            public double Yaw;
            public float? GyroZ;
            public Vector3? Gyro, GyroRaw;
            public double? Temp;
            public Vector3? Unc;
            public Vector3? Mag;
            public Vector3? Acc;
        }

        private static Vector3?[] MovingAverageAcc(List<Sample> s, double halfWindowS)
        {
            var outv = new Vector3?[s.Count];
            int lo = 0, hi = 0;
            double sx = 0, sy = 0, sz = 0; int cnt = 0;
            for (int k = 0; k < s.Count; k++)
            {
                while (hi < s.Count && s[hi].T <= s[k].T + halfWindowS)
                {
                    if (s[hi].Acc is Vector3 a) { sx += a.X; sy += a.Y; sz += a.Z; cnt++; }
                    hi++;
                }
                while (lo < hi && s[lo].T < s[k].T - halfWindowS)
                {
                    if (s[lo].Acc is Vector3 a) { sx -= a.X; sy -= a.Y; sz -= a.Z; cnt--; }
                    lo++;
                }
                outv[k] = cnt > 0 ? new Vector3((float)(sx / cnt), (float)(sy / cnt), (float)(sz / cnt))
                                  : (Vector3?)null;
            }
            return outv;
        }

        private static bool TryNearest(List<(double T, double V)> list, double t, double tol, out double value)
        {
            value = 0;
            if (list.Count == 0) return false;
            int lo = 0, hi = list.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (list[mid].T <= t) lo = mid; else hi = mid;
            }
            var best = Math.Abs(list[lo].T - t) <= Math.Abs(list[hi].T - t) ? list[lo] : list[hi];
            if (Math.Abs(best.T - t) > tol) return false;
            value = best.V;
            return true;
        }

        private static double Sec(DateTime t, ref DateTime t0)
        {
            if (t0 == DateTime.MinValue) t0 = t;
            return (t - t0).TotalSeconds;
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private static double Deg(double rad) => rad * 180.0 / Math.PI;
    }
}
