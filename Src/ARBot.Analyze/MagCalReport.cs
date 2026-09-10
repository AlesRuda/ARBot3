using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Calibration;
using ARBot.Common.Logs;
using ARBot.Common.Models;

using Vector3 = System.Numerics.Vector3;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Kalibrace magnetometru ze zaznamu</b> — tentyz <see cref="MagCalFit"/>, ktery bezi na
    /// robotu. To je zamer: verdikt v poli a verdikt u stolu musi byt <b>totez cislo</b>, ne dve
    /// implementace, ktere se pak rozchazeji.
    ///
    /// <para>Tiskne 12 cisel ve tvaru ke zkopirovani za <c>VNWRG,23,</c>, verdikt, pokryti,
    /// kontrolu rozpulenim a — kdyz zaznam nese <c>MagCalMsg</c> — i to, co spocital robot
    /// v poli, aby se dalo overit, ze se neresi jina data.</para>
    ///
    /// <para>Pusteno na <b>bezny jizdni</b> zaznam odpovi na otazku, kvuli ktere to vzniklo
    /// jako prvni: jak moc je rotacni test vubec potreba. Bezna jizda azimuty nepokryje
    /// a podminenost bude o rady nad prahem — a to je uzitecna predpoved, ne chyba.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public static class MagCalReport
    {
        /// <summary>Mezera v datech, pres kterou se uz uhlova rychlost neintegruje [s].</summary>
        private const double MaxGapSec = 0.5;

        public static void Run(RecordFile rec, double bRefG, string reg47)
        {
            var cov = new MagCalCoverage();
            double yaw = 0;
            double? tPred = null;
            DateTime t0 = DateTime.MinValue;
            int bezPole = 0, bezGyra = 0, bezAcc = 0, relativnich = 0;
            bool surove = false;
            var zdroje = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var omega = new List<double>();   // |uhlova rychlost| ke kazdemu vzorku [rad/s]
            var rot = new List<System.Numerics.Quaternion?>();  // atituda senzoru ke kazdemu vzorku
            var yawy = new List<double>();    // integrovany yaw ke kazdemu vzorku [rad]

            foreach (var i in rec.ReadAll<IMUState>(nameof(IMUState)))
            {
                // ⚠️ Stejny duvod jako ve Vn100Report: v robotu je IMU vic a T265 posila
                // RELATIVNI yaw. Michat dve ruzne nuly by dalo nesmysl.
                string jmeno = i.Name ?? "(bez jmena)";
                if (!i.HasAbsoluteHeading) { relativnich++; continue; }
                zdroje.TryGetValue(jmeno, out int n);
                zdroje[jmeno] = n + 1;

                // Surove pole ma prednost; kompenzovane je zaloha pro zaznamy formatu < 4.
                var pole = i.MagnetometerRaw ?? i.Magnetometer;
                if (i.MagnetometerRaw.HasValue) surove = true;
                if (pole == null) { bezPole++; continue; }
                if (i.AngularVelocity == null) { bezGyra++; continue; }
                if (i.Acceleration == null) { bezAcc++; continue; }

                double t = Sec(i.TimeStamp, ref t0);
                if (tPred.HasValue)
                {
                    double dt = t - tPred.Value;
                    // Mezera v datech: integrace by pres ni nasbirala otoceni, ktere se nestalo.
                    if (dt > 0 && dt < MaxGapSec) yaw += i.AngularVelocity.Value.Z * dt;
                }
                tPred = t;

                cov.Add(yaw, pole.Value, i.Acceleration.Value);
                omega.Add(i.AngularVelocity.Value.Length());
                rot.Add(i.Rotation);
                yawy.Add(yaw);
            }

            Console.WriteLine("=== KALIBRACE MAGNETOMETRU ZE ZAZNAMU ===");
            Console.WriteLine($"  vzorku: {cov.Mag.Count}, referencni |B| = {bRefG:F4} G");
            if (surove)
            {
                Console.WriteLine("  pole: SUROVE (MagnetometerRaw) - vysledek je ABSOLUTNI"
                                  + " kalibrace, nezavisle na tom, co bylo v registru 23.");
            }
            else
            {
                Console.WriteLine("  ⚠️ pole: KOMPENZOVANE (Magnetometer) - vysledek plati JEN kdyz"
                                  + " byl registr 23 pri nahravani JEDNOTKOVY.");
                Console.WriteLine("     Ze zaznamu se to NEPOZNA: elipsoida uz kompenzovanych dat"
                                  + " je priblizne vycentrovana, coz je od dobreho zeleza"
                                  + " nerozeznatelne. Zaznamy formatu < 4 surove pole nenesou.");
            }
            foreach (var z in zdroje) Console.WriteLine($"  zdroj: {z.Key} ({z.Value} vzorku)");
            if (relativnich > 0)
                Console.WriteLine($"  vynechano {relativnich} vzorku z IMU bez absolutniho kurzu (T265).");
            if (bezPole > 0 || bezGyra > 0 || bezAcc > 0)
                Console.WriteLine($"  vynechano: bez pole {bezPole}, bez gyra {bezGyra},"
                                  + $" bez akcelerometru {bezAcc}.");

            Console.WriteLine();
            Console.WriteLine("1) POKRYTI");
            Console.WriteLine($"  azimutove kose:      {cov.FilledAzimuthBins} z {MagCalThresholds.AzimuthBins}");
            Console.WriteLine($"  naklonove skupiny:   {cov.TiltGroups}"
                              + $" (z toho odklonenych {cov.TiltedGroups})");
            Console.WriteLine($"  naklony na obe strany: {(cov.HasOppositeTilts ? "ano" : "NE")}");
            Console.WriteLine($"  otoceni gyrem celkem: {yaw * 180.0 / Math.PI:F0} stupnu");
            string chybi = cov.MissingText();
            Console.WriteLine(chybi.Length == 0 ? "  pokryti je uplne." : "  " + chybi);

            if (cov.Mag.Count < MagCalFit.MinSamples)
            {
                Console.WriteLine();
                Console.WriteLine($"  Malo vzorku ({cov.Mag.Count} < {MagCalFit.MinSamples})"
                                  + " - neprokladam.");
                VerdiktZPole(rec, bRefG, null);
                return;
            }

            bool ok = MagCalFit.TryFit(cov.Mag, bRefG, out var r, out double cond, out string duvod, cov.Acc);

            KouleBlok(cov, bRefG);

            Console.WriteLine();
            Console.WriteLine("3) PROLOZENI ELIPSOIDY");
            if (cov.Mag.Count > MagCalFit.MaxFitSamples)
                Console.WriteLine($"  do soustavy vstoupil kazdy"
                                  + $" {(cov.Mag.Count + MagCalFit.MaxFitSamples - 1) / MagCalFit.MaxFitSamples}."
                                  + $" vzorek (strop {MagCalFit.MaxFitSamples});"
                                  + " podminenost je na poctu vzorku invariantni, zbytky se pocitaji ze VSECH.");
            Console.WriteLine($"  podminenost:         {cond:G4}"
                              + $"  (prah {MagCalThresholds.MaxCondition:G4})");
            if (!ok)
            {
                Console.WriteLine("  Soustava NENI URCENA - neprokladam. Duvod: " + duvod);
                Console.WriteLine("  Neni to chyba: bezna jizda azimuty ani naklony nepokryje."
                                  + " Presne proto je potreba rotacni test.");
                VerdiktZPole(rec, bRefG, null);
                CasovaOsa(rec);
                return;
            }

            Console.WriteLine($"  sd(|B|) po korekci:  {r.SdMagnitudeG:F5} G"
                              + $"  (prah {MagCalThresholds.MaxSdMagnitudeG:F3})");
            Console.WriteLine($"  sd(sklonu):          {r.SdInclinationDeg:F3}°"
                              + $"  (orientacne {MagCalThresholds.MaxSdInclinationDeg:F1}; od 10. 9. 2026 NENI brana"
                              + " - meri hlavne akcelerometr, viz blok 7)");

            // Rozpuleni: dve nezavisle poloviny se musi shodnout v OPRAVE KURZU.
            int p = cov.Mag.Count / 2;
            if (p >= MagCalFit.MinSamples)
            {
                var m1 = cov.Mag.Take(p).ToList();
                var a1 = cov.Acc.Take(p).ToList();
                var m2 = cov.Mag.Skip(p).ToList();
                var a2 = cov.Acc.Skip(p).ToList();
                if (MagCalFit.TryFit(m1, bRefG, out var f1, out _, a1)
                    && MagCalFit.TryFit(m2, bRefG, out var f2, out _, a2))
                {
                    double d = MagCalFit.HeadingDiffDeg(f1, f2);
                    Console.WriteLine($"  rozpuleni dat:       {d:F2}° rozdilu v oprave kurzu"
                                      + $"  (prah {MagCalThresholds.MaxHalfSplitDeg:F0})");
                    // Kurz testuje jen vodorovne slozky; slozka z (bias i meritko) je ta slabe
                    // urcena, tak se ukaze zvlast — kdyz se poloviny v z rozejdou, je z nejiste,
                    // i kdyz kurz sedi.
                    Console.WriteLine($"    1. pulka: diag C [{f1.C[0, 0]:F4}, {f1.C[1, 1]:F4}, {f1.C[2, 2]:F4}],"
                                      + $" b [{f1.B[0]:F4}, {f1.B[1]:F4}, {f1.B[2]:F4}] G, podminenost {f1.Condition:G4}");
                    Console.WriteLine($"    2. pulka: diag C [{f2.C[0, 0]:F4}, {f2.C[1, 1]:F4}, {f2.C[2, 2]:F4}],"
                                      + $" b [{f2.B[0]:F4}, {f2.B[1]:F4}, {f2.B[2]:F4}] G, podminenost {f2.Condition:G4}");
                    Console.WriteLine("  ⚠️ rozpuleni SAMO NESTACI - dve stejne degenerovana data"
                                      + " se shodnou taky. Musi platit i podminenost.");
                }
                else
                {
                    Console.WriteLine("  rozpuleni dat:       nelze - jedna z polovin neni urcena.");
                }
            }

            Console.WriteLine();
            Console.WriteLine("4) VYSLEDEK — ke zkopirovani za VNWRG,23,");
            Console.WriteLine("  " + r.ToVnwrg23());

            // Tataz pravidla jako MagCalCollector.Usable (bez sklonu - od 10. 9. 2026 jen diagnostika).
            bool pouzitelne = cov.Complete
                              && r.SdMagnitudeG <= MagCalThresholds.MaxSdMagnitudeG;
            Console.WriteLine("  verdikt: " + (pouzitelne
                ? "POUZITELNE"
                : "NEPOUZITELNE - viz pokryti a cisla vyse"));

            if (reg47 != null)
            {
                Console.WriteLine();
                Console.WriteLine("5) POROVNANI S REGISTREM 47 (co spocital sam senzor)");
                Console.WriteLine("  senzor: " + reg47);
                Console.WriteLine("  my:     " + r.ToVnwrg23());
                Console.WriteLine("  ⚠️ Dve nezavisle metody, ktere se shodnou, jsou dukaz."
                                  + " Kdyz se rozejdou, NEZAPISUJ nic a premysli.");
            }

            VerdiktZPole(rec, bRefG, r);
            RozborSklonu(cov, omega, rot, yawy, r, bRefG);
            CasovaOsa(rec);
        }

        /// <summary>
        /// <b>Z ceho se sklada rozptyl sklonu</b> — kriterium, na kterem 10. 9. 2026 padl verdikt
        /// (2,09° proti prahu 0,5°), zatimco <c>sd(|B|)</c> i rozpuleni prosly.
        ///
        /// <para>Sklon se sklapi <b>surovym akcelerometrem</b>, a ten pri otaceni robotem rukou
        /// nemeri jen gravitaci: trhnuti, dostredive zrychleni a chveni ruky jdou primo do
        /// „sklonu", ackoli s magnetometrem nemaji nic spolecneho. Tenhle blok proto pocita
        /// tentyz rozptyl nad <b>klidnymi</b> vzorky (|acc| ≈ g, mala uhlova rychlost), po radcich
        /// naklonu (systematicky posun mezi radky = skutecna chyba kalibrace slozky z, rozptyl
        /// uvnitr radku = sum) a v zavislosti na odchylce |acc| od g.</para>
        /// </summary>
        private static void RozborSklonu(MagCalCoverage cov, List<double> omega,
                                         List<System.Numerics.Quaternion?> rot, List<double> yawy,
                                         MagCalResult r, double bRefG)
        {
            Console.WriteLine();
            Console.WriteLine("7) ROZBOR SKLONU — je to kalibrace, nebo akcelerometr?");
            int n = cov.Mag.Count;

            // ⚠️ |acc| tohohle senzoru NENI 9,81 (znamy nevysvetleny nalez +7,4 %, viz
            // imu-and-frames.md), takze se klid meri proti klidove hodnote ze zaznamu samotneho:
            // median |acc| pres vzorky, kde se robot netocil.
            var velAcc = cov.Acc.Select(a => (double)a.Length()).ToList();
            var klidIdx = Enumerable.Range(0, n).Where(i => omega[i] < 2 * Math.PI / 180).ToList();
            double gKlid = klidIdx.Count > 10 ? P(klidIdx.Select(i => velAcc[i]).ToList(), 0.5) : P(velAcc, 0.5);

            var dAcc = new double[n];      // | |acc| - gKlid | relativne
            var radek = new int[n];
            var odklon = new double[n];
            for (int i = 0; i < n; i++)
            {
                dAcc[i] = Math.Abs(velAcc[i] - gKlid) / gKlid;
                radek[i] = MagCalCoverage.RowOf(cov.Acc[i], out odklon[i]);
            }
            Console.WriteLine($"  |acc| v klidu (omega < 2°/s, {klidIdx.Count} vzorku): p50 {gKlid:F3} m/s2"
                              + $"  (9,81 by bylo {100 * (gKlid / 9.80665 - 1):+F1} % jinde)");
            Console.WriteLine($"  |acc| celkem: prumer {velAcc.Average():F3} m/s2, sd {Sd(velAcc):F3};"
                              + $" mimo ±1 % klidu: {100.0 * dAcc.Count(d => d > 0.01) / n:F1} % vzorku,"
                              + $" mimo ±3 %: {100.0 * dAcc.Count(d => d > 0.03) / n:F1} %");
            Console.WriteLine($"  |omega|: p50 {P(omega, 0.5) * 180 / Math.PI:F1} °/s,"
                              + $" p90 {P(omega, 0.9) * 180 / Math.PI:F1} °/s");

            // Vyhlazeny akcelerometr: klouzavy prumer ±50 vzorku (~0,5 s pri 100 Hz) — chveni ruky
            // se vyprumeruje, trvale dostredive zrychleni ne.
            var accHl = new Vector3[n];
            {
                int w = 50;
                var cx = new double[n + 1]; var cy = new double[n + 1]; var cz = new double[n + 1];
                for (int i = 0; i < n; i++) { cx[i + 1] = cx[i] + cov.Acc[i].X; cy[i + 1] = cy[i] + cov.Acc[i].Y; cz[i + 1] = cz[i] + cov.Acc[i].Z; }
                for (int i = 0; i < n; i++)
                {
                    int a = Math.Max(0, i - w), b = Math.Min(n, i + w + 1);
                    accHl[i] = new Vector3((float)((cx[b] - cx[a]) / (b - a)), (float)((cy[b] - cy[a]) / (b - a)), (float)((cz[b] - cz[a]) / (b - a)));
                }
            }

            // Gravitace z atitudy senzoru (VPE filtruje zrychleni gyrem). Konvence Rotation
            // je body->world; dolu v telese = inverzni rotace (0,0,-1). Kdyby byla konvence
            // obracena, prozradi se to na tom, ze prumer sklonu po radcich naklonu nesedi.
            var dolu = new Vector3?[n];
            var doluInv = new Vector3?[n];
            for (int i = 0; i < n; i++)
            {
                if (rot[i] is System.Numerics.Quaternion q && !float.IsNaN(q.W))
                {
                    var qi = System.Numerics.Quaternion.Inverse(q);
                    dolu[i] = Vector3.Transform(new Vector3(0, 0, -1), qi);
                    doluInv[i] = Vector3.Transform(new Vector3(0, 0, -1), q);
                }
            }
            Console.WriteLine($"  atituda (Rotation) k dispozici u {dolu.Count(d => d.HasValue)} z {n} vzorku");
            if (dolu.Any(d => d.HasValue))
            {
                // Shoda smeru gravitace z atitudy proti akcelerometru, kdyz robot stoji:
                double Uhel(Vector3?[] dd) => klidIdx.Where(i => dd[i].HasValue).Select(i =>
                {
                    var a = cov.Acc[i]; var d = dd[i].Value;
                    double c = -(a.X * d.X + a.Y * d.Y + a.Z * d.Z) / (a.Length() * d.Length());
                    return Math.Acos(Math.Clamp(c, -1, 1)) * 180 / Math.PI;
                }).DefaultIfEmpty(double.NaN).Average();
                Console.WriteLine($"  uhel mezi -acc a 'dolu' z atitudy v klidu: inverzni {Uhel(dolu):F2}°, prima {Uhel(doluInv):F2}°"
                                  + " (spravna konvence je ta blizko nule)");
            }

            Func<Func<Vector3, Vector3>, double[]> sklony = f =>
            {
                var o = new double[n];
                for (int i = 0; i < n; i++) o[i] = Sklon(f(cov.Mag[i]), cov.Acc[i]);
                return o;
            };
            var sklonRaw = sklony(m => m);
            var sklonEl = sklony(r.Apply);
            MagCalResult koule = null;
            if (MagCalFit.TryFitSphere(cov.Mag, bRefG, out var k, out _, cov.Acc)) koule = k;
            var sklonKoule = koule == null ? null : sklony(koule.Apply);

            // Tentyz sklon, jen s jinym smerem 'dolu': vyhlazeny akcelerometr a atituda.
            Func<Func<Vector3, Vector3>, Func<int, Vector3?>, double[]> sklonyS = (f, gDir) =>
            {
                var o = new double[n];
                for (int i = 0; i < n; i++)
                {
                    var d = gDir(i);
                    // Sklon() ocekava acc (= -dolu), takze se smer dolu obraci.
                    o[i] = d.HasValue ? Sklon(f(cov.Mag[i]), -d.Value) : double.NaN;
                }
                return o;
            };
            var sklonElHl = sklonyS(r.Apply, i => -accHl[i]);
            var sklonElAt = sklonyS(r.Apply, i => dolu[i]);
            var sklonElAtInv = sklonyS(r.Apply, i => doluInv[i]);

            Console.WriteLine();
            Console.WriteLine("  sd(sklonu) [deg]            vse   |acc|±1%   +omega<20°/s   (pocet klidnych)");
            var klid1 = Enumerable.Range(0, n).Where(i => dAcc[i] <= 0.01).ToList();
            var klid2 = klid1.Where(i => omega[i] < 20 * Math.PI / 180).ToList();
            void Radka(string jm, double[] v)
            {
                if (v == null) return;
                Console.WriteLine($"  {jm,-26} {Sd(v.ToList()),8:F3} {Sd(klid1.Select(i => v[i]).ToList()),10:F3}"
                                  + $" {Sd(klid2.Select(i => v[i]).ToList()),14:F3}   ({klid1.Count} / {klid2.Count})");
            }
            Radka("bez korekce (surove)", sklonRaw);
            Radka("koule (tvrde zelezo)", sklonKoule);
            Radka("elipsoida", sklonEl);
            // Prumerny sklon NA ROVINE (tam smer 'dolu' nekazi bias akcelerometru) — porovnat
            // s modelem pole pro misto (WMM pro CR ~66°). Odchylka = zbytek v slozce z, nebo
            // mistni anomalie; ze zaznamu se to nerozlisi.
            var rovina = Enumerable.Range(0, n).Where(i => radek[i] == 0 && omega[i] < 20 * Math.PI / 180).ToList();
            Console.WriteLine($"  prumerny sklon NA ROVINE v klidu: surove {rovina.Average(i => sklonRaw[i]):F2}°,"
                              + (sklonKoule == null ? "" : $" koule {rovina.Average(i => sklonKoule[i]):F2}°,")
                              + $" elipsoida {rovina.Average(i => sklonEl[i]):F2}°  (WMM pro CR ~66°)");
            Radka("elipsoida, acc hlazeny 1 s", sklonElHl);
            Radka("elipsoida, dolu z atitudy", sklonElAt);
            Radka("  (obracena konvence)", sklonElAtInv);
            Console.WriteLine($"  (orientacne {MagCalThresholds.MaxSdInclinationDeg:F1}°; brana to neni)");

            Console.WriteLine();
            Console.WriteLine("  prumer sklonu po radcich, ruzne smery 'dolu' (odchylka od celku):");
            Console.WriteLine("                     acc surovy   acc hlazeny   atituda   atituda obr.");
            double[][] varianty = { sklonEl, sklonElHl, sklonElAt, sklonElAtInv };
            double[] celky = varianty.Select(v => v.Where(x => !double.IsNaN(x)).DefaultIfEmpty(double.NaN).Average()).ToArray();
            for (int rr = 0; rr < MagCalCoverage.TiltRows; rr++)
            {
                var idx = Enumerable.Range(0, n).Where(i => radek[i] == rr).ToList();
                if (idx.Count == 0) continue;
                string line = $"  {MagCalCoverage.RowLabels[rr],-16}";
                for (int v = 0; v < varianty.Length; v++)
                {
                    var vals = idx.Select(i => varianty[v][i]).Where(x => !double.IsNaN(x)).ToList();
                    line += vals.Count == 0 ? "           -  " : $" {vals.Average() - celky[v],+8:F2}° ";
                    line += "   ";
                }
                Console.WriteLine(line);
            }

            Console.WriteLine();
            Console.WriteLine("  po radcich mrizky (elipsoida): radek | n | prumer sklonu (odchylka od celku) | sd | sd klidnych | odklon p50");
            double celk = Enumerable.Range(0, n).Average(i => sklonEl[i]);
            for (int rr = 0; rr < MagCalCoverage.TiltRows; rr++)
            {
                var idx = Enumerable.Range(0, n).Where(i => radek[i] == rr).ToList();
                if (idx.Count == 0) { Console.WriteLine($"  {MagCalCoverage.RowLabels[rr],-16} 0"); continue; }
                var v = idx.Select(i => sklonEl[i]).ToList();
                var vk = idx.Where(i => dAcc[i] <= 0.01).Select(i => sklonEl[i]).ToList();
                Console.WriteLine($"  {MagCalCoverage.RowLabels[rr],-16} {idx.Count,6}  {v.Average(),7:F2}°"
                                  + $" ({v.Average() - celk,+6:F2})  {Sd(v),6:F3}  {Sd(vk),6:F3}"
                                  + $"  {P(idx.Select(i => odklon[i]).ToList(), 0.5),5:F1}°");
            }
            Console.WriteLine("  (systematicky posun mezi radky = chyba kalibrace slozky z; rozptyl uvnitr radku = sum)");

            Console.WriteLine();
            Console.WriteLine("  |acc| p50 po radcich (jen omega < 20°/s) — je +7 % izotropni (stejne ve vsech polohach)?");
            for (int rr = 0; rr < MagCalCoverage.TiltRows; rr++)
            {
                var idx = Enumerable.Range(0, n).Where(i => radek[i] == rr && omega[i] < 20 * Math.PI / 180).ToList();
                if (idx.Count < 10) continue;
                var ax = idx.Select(i => (double)cov.Acc[i].X).ToList();
                var ay = idx.Select(i => (double)cov.Acc[i].Y).ToList();
                var az = idx.Select(i => (double)cov.Acc[i].Z).ToList();
                Console.WriteLine($"  {MagCalCoverage.RowLabels[rr],-16} n {idx.Count,6}  |acc| {P(idx.Select(i => velAcc[i]).ToList(), 0.5):F3}"
                                  + $"  acc p50 [{P(ax, 0.5),6:F2}, {P(ay, 0.5),6:F2}, {P(az, 0.5),6:F2}]");
            }

            // AKCELEROMETR jako by to byl magnetometr: ma |acc| pri otaceni lezet na kouli
            // o polomeru g. Prolozeni ukaze bias a zisky os — tedy jestli je +7 % izotropni
            // (jen meritko, smer 'dolu' nezkresli) nebo v jedne ose (smer zkresli az o stupne).
            {
                var idx = Enumerable.Range(0, n).Where(i => omega[i] < 20 * Math.PI / 180 && dAcc[i] <= 0.02).ToList();
                var aM = idx.Select(i => cov.Acc[i]).ToList();
                Console.WriteLine();
                Console.WriteLine($"  AKCELEROMETR prolozeny jako koule/elipsoida ({aM.Count} klidnych vzorku, reference g = 9,80665):");
                if (aM.Count >= MagCalFit.MinSamples)
                {
                    if (MagCalFit.TryFitSphere(aM, 9.80665, out var ak, out double ac))
                        Console.WriteLine($"   koule: podminenost {ac:G4}, stred [{ak.B[0]:F3}, {ak.B[1]:F3}, {ak.B[2]:F3}] m/s2,"
                                          + $" meritko {ak.C[0, 0]:F4} (1/meritko = polomer/g = {1 / ak.C[0, 0]:F4}), sd(|a|) po korekci {ak.SdMagnitudeG:F4} m/s2");
                    else Console.WriteLine($"   koule: neurcena (podminenost {ac:G4})");
                    if (MagCalFit.TryFit(aM, 9.80665, out var ae, out double ace, null, 1e6))
                        Console.WriteLine($"   elipsoida: podminenost {ace:G4}, bias [{ae.B[0]:F3}, {ae.B[1]:F3}, {ae.B[2]:F3}] m/s2,"
                                          + $" diag C [{ae.C[0, 0]:F4}, {ae.C[1, 1]:F4}, {ae.C[2, 2]:F4}], mimo diag [{ae.C[0, 1]:F4}, {ae.C[0, 2]:F4}, {ae.C[1, 2]:F4}],"
                                          + $" sd(|a|) po korekci {ae.SdMagnitudeG:F4} m/s2");
                    else Console.WriteLine($"   elipsoida: neurcena (podminenost {ace:G4})");
                    Console.WriteLine("   (C = zisk, kterym se ma osa NASOBIT, aby |a| = g: 1/1,07 = 0,935 znamena osu ctouci o 7 % vic)");
                }
            }

            // Sklon po AZIMUTECH na rovine, klidne vzorky: hladka vlna = systematika (nesouosost
            // magnetometru a akcelerometru, tj. antisymetricka cast mekkeho zeleza, kterou
            // symetricka odmocnina nevidi); bily sum = sum.
            Console.WriteLine();
            Console.WriteLine("  sklon po azimutech, NA ROVINE, klidne (|acc| ±1 %, omega < 20°/s), elipsoida — odchylka od prumeru [deg]:");
            int bins = MagCalThresholds.AzimuthBins;
            var sum = new double[bins]; var cnt = new int[bins]; var sq = new double[bins];
            double c0 = 0; int n0 = 0;
            double s1 = 0, c1 = 0, s2 = 0, c2 = 0;
            foreach (int i in klid2)
            {
                if (radek[i] != 0 || double.IsNaN(sklonEl[i])) continue;
                double f = yawy[i] % (2 * Math.PI); if (f < 0) f += 2 * Math.PI;
                int b = Math.Min(bins - 1, (int)(f / (2 * Math.PI) * bins));
                sum[b] += sklonEl[i]; sq[b] += sklonEl[i] * sklonEl[i]; cnt[b]++;
                c0 += sklonEl[i]; n0++;
            }
            if (n0 > 0)
            {
                double mean0 = c0 / n0;
                string l1 = "   ", l2 = "   ";
                for (int b = 0; b < bins; b++)
                {
                    l1 += cnt[b] > 0 ? $"{sum[b] / cnt[b] - mean0,6:F2}" : "     -";
                    l2 += $"{cnt[b],6}";
                }
                string l3 = "   ";
                for (int b = 0; b < bins; b++)
                    l3 += cnt[b] > 1 ? $"{Math.Sqrt(Math.Max(0, (sq[b] - sum[b] * sum[b] / cnt[b]) / (cnt[b] - 1))),6:F2}" : "     -";
                Console.WriteLine("   kos 0..23 (po 15° integrovaneho yaw):");
                Console.WriteLine(l1);
                Console.WriteLine("   sd:" + l3.Substring(3));
                Console.WriteLine("   n:" + l2.Substring(3));
                // harmonicka analyza nad vzorky
                foreach (int i in klid2)
                {
                    if (radek[i] != 0 || double.IsNaN(sklonEl[i])) continue;
                    double d = sklonEl[i] - mean0;
                    s1 += d * Math.Sin(yawy[i]); c1 += d * Math.Cos(yawy[i]);
                    s2 += d * Math.Sin(2 * yawy[i]); c2 += d * Math.Cos(2 * yawy[i]);
                }
                double a1 = 2 * Math.Sqrt(s1 * s1 + c1 * c1) / n0, a2 = 2 * Math.Sqrt(s2 * s2 + c2 * c2) / n0;
                // rozptyl prumeru kosu vs rozptyl uvnitr kosu
                var prumery = Enumerable.Range(0, bins).Where(b => cnt[b] > 1).Select(b => sum[b] / cnt[b]).ToList();
                double sdUvnitr = Math.Sqrt(Enumerable.Range(0, bins).Where(b => cnt[b] > 1)
                    .Sum(b => sq[b] - sum[b] * sum[b] / cnt[b]) / Math.Max(1, n0 - prumery.Count));
                Console.WriteLine($"   1. harmonicka {a1:F2}°, 2. harmonicka {a2:F2}° (amplitudy);"
                                  + $" sd prumeru kosu {Sd(prumery):F3}°, sd uvnitr kosu {sdUvnitr:F3}°, celkem {Sd(klid2.Where(i => radek[i] == 0).Select(i => sklonEl[i]).ToList()):F3}°");
                Console.WriteLine("   (velka 1./2. harmonicka a sd prumeru kosu >> 0 = systematika, kterou by opravila rotace/antisymetrie C;"
                                  + " sd uvnitr kosu = sum senzoru + dynamika)");
            }

            Console.WriteLine();
            Console.WriteLine("  sd(sklonu) podle odchylky |acc| od g (elipsoida):");
            double[] hran = { 0, 0.005, 0.01, 0.02, 0.05, 0.1, double.PositiveInfinity };
            for (int b = 0; b + 1 < hran.Length; b++)
            {
                var idx = Enumerable.Range(0, n).Where(i => dAcc[i] >= hran[b] && dAcc[i] < hran[b + 1]).ToList();
                if (idx.Count < 2) continue;
                string horni = double.IsInfinity(hran[b + 1]) ? "   " : (hran[b + 1] * 100).ToString("F1");
                Console.WriteLine($"    {hran[b] * 100,4:F1}–{horni,5} % g:"
                                  + $" n {idx.Count,6}  sd {Sd(idx.Select(i => sklonEl[i]).ToList()),6:F3}°"
                                  + $"  prumer {idx.Average(i => sklonEl[i]),7:F2}°");
            }
        }

        /// <summary>Sklon pole sklopeny gravitaci [deg] — stejny vzorec jako v MagCalFit.SeZbytky.</summary>
        private static double Sklon(Vector3 m, Vector3 acc)
        {
            double an = acc.Length(), mn = m.Length();
            if (!(an > 0) || !(mn > 0)) return double.NaN;
            double dot = -(m.X * acc.X + m.Y * acc.Y + m.Z * acc.Z) / (an * mn);
            return Math.Asin(Math.Clamp(dot, -1.0, 1.0)) * 180.0 / Math.PI;
        }

        /// <summary>
        /// <b>Prubeh zprav z pole</b> — kdy se zmenil verdikt a jak sla podminenost a zbytky.
        /// Obsluha si z pole pamatuje jedno cislo („170"); tady je videt, ke kteremu okamziku patri.
        /// </summary>
        private static void CasovaOsa(RecordFile rec)
        {
            var zpravy = rec.ReadAll<MagCalMsg>(nameof(MagCalMsg)).ToList();
            if (zpravy.Count == 0) return;
            Console.WriteLine();
            Console.WriteLine("8) PRUBEH V POLI (MagCalMsg) — radek pri kazde zmene verdiktu");
            Console.WriteLine("     t[s]  vzorku  azim  nakl  podmin.   sd|B|   sd skl  koule sd  verdikt");
            DateTime t0 = zpravy[0].TimeStamp;
            string posledni = null;
            double condMin = double.PositiveInfinity, condMax = 0;
            foreach (var m in zpravy)
            {
                if (!double.IsInfinity(m.Condition)) { condMin = Math.Min(condMin, m.Condition); condMax = Math.Max(condMax, m.Condition); }
                string v = m.Verdict ?? "";
                string klic = v.Length > 40 ? v.Substring(0, 40) : v;
                if (klic == posledni) continue;
                posledni = klic;
                Console.WriteLine($"  {(m.TimeStamp - t0).TotalSeconds,7:F0} {m.Samples,7} {m.FilledAzimuthBins,5} {m.TiltGroups,5}"
                                  + $" {m.Condition,8:G4} {m.SdMagnitudeG,7:F4} {m.SdInclinationDeg,8:F2} {m.SphereSdMagnitudeG,9:F4}  {v}");
            }
            var last = zpravy[zpravy.Count - 1];
            Console.WriteLine($"  posledni: t {(last.TimeStamp - t0).TotalSeconds:F0} s, vzorku {last.Samples}, podminenost {last.Condition:G4},"
                              + $" sd skl {last.SdInclinationDeg:F2}, verdikt: {last.Verdict}");
            Console.WriteLine($"  podminenost: min {condMin:G4}, max {condMax:G4}");
            int urc = zpravy.Count(z => !string.IsNullOrEmpty(z.Vnwrg23));
            Console.WriteLine($"  zprav {zpravy.Count}, z toho s urcenou elipsoidou {urc}, s kouli {zpravy.Count(z => z.CanWriteHardIron)}");
        }

        private static double Sd(List<double> x)
        {
            x = x.Where(v => !double.IsNaN(v)).ToList();
            if (x.Count < 2) return double.NaN;
            double m = x.Average();
            return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / (x.Count - 1));
        }

        private static double P(List<double> x, double q)
        {
            var s = x.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToList();
            if (s.Count == 0) return double.NaN;
            return s[Math.Min(s.Count - 1, (int)(q * s.Count))];
        }

        /// <summary>
        /// <b>Co spocital robot v poli</b> (<see cref="MagCalMsg"/> ze zaznamu) vedle vlastniho
        /// prepoctu. Je to ta kontrola, kvuli ktere zprava vubec tece do zaznamu: verdikt v poli
        /// a verdikt u stolu musi byt <b>totez cislo</b>.
        /// </summary>
        /// <summary>
        /// <b>Prolozeni samotne koule</b> — jen tvrde zelezo. Tiskne se PRED elipsoidou, protoze
        /// odpovida na otazku, ktera je polozena driv: <i>lezi ta data vubec na nejake kouli?</i>
        ///
        /// <para>Kdyz koule nesedi, menilo se behem mereni pole a <b>elipsoida uz nema co
        /// zachranit</b> — a hlavne to znamena, ze otaceni nepomuze. Presne tohle chybelo
        /// pri vyjezdu 10. 9. 2026, kdy stranka porad radila „otacej dal".</para>
        /// </summary>
        private static void KouleBlok(MagCalCoverage cov, double bRefG)
        {
            Console.WriteLine();
            Console.WriteLine("2) PROLOZENI KOULE (jen tvrde zelezo)");
            bool ok = MagCalFit.TryFitSphere(cov.Mag, bRefG, out var k, out double cond, cov.Acc);
            Console.WriteLine($"  podminenost:         {cond:G4}"
                              + $"  (prah {MagCalThresholds.MaxCondition:G4})");
            if (!ok)
            {
                Console.WriteLine("  Koule NENI URCENA - data lezi v rovine nebo je jich malo.");
                Console.WriteLine("  ⚠️ Pozor: samotna podminenost tenhle stav NECHYTI"
                                  + " (rovinna rotace s mekkym zelezem da ~538) - rozhodla brana"
                                  + " na meritko, viz MagCalThresholds.MaxSphereScale.");
                return;
            }

            Console.WriteLine($"  sd(|B|) po korekci:  {k.SdMagnitudeG:F5} G"
                              + $"  (prah {MagCalThresholds.MaxSphereSdMagnitudeG:F3})");
            Console.WriteLine($"  tvrde zelezo:        [{k.B[0]:F4}, {k.B[1]:F4}, {k.B[2]:F4}] G"
                              + $"  (|b| = {k.B.L2Norm():F4})");
            if (k.SdMagnitudeG > MagCalThresholds.MaxSphereSdMagnitudeG)
            {
                Console.WriteLine("  ⚠️ POLE SE BEHEM MERENI MENILO - data nelezi na zadne kouli.");
                Console.WriteLine("     Otaceni to nespravi; robot musi stat na JEDNOM miste"
                                  + " dal od kovu. Vysledek elipsoidy niz je proto neduveryhodny.");
            }
            else
            {
                Console.WriteLine("  pole je konzistentni - kdyz elipsoida niz nevyjde,"
                                  + " chybi POUZE naklon.");
            }
            Console.WriteLine("  ⚠️ Sam o sobe je tenhle vysledek jen CASTECNA kalibrace:"
                              + " odstrani 100 % tvrdeho zeleza bez mekkeho, ~80 % pri tom"
                              + " z referencniho exportu, ale jen ~38 % pri patologickem.");
        }

        private static void VerdiktZPole(RecordFile rec, double bRefG, MagCalResult nas)   // nas = null pri neurcenem prolozeni
        {
            MagCalMsg posledni = null;
            foreach (var m in rec.ReadAll<MagCalMsg>(nameof(MagCalMsg))) posledni = m;
            if (posledni == null) return;

            Console.WriteLine();
            Console.WriteLine("6) CO SPOCITAL ROBOT V POLI (MagCalMsg ze zaznamu)");
            Console.WriteLine($"  podminenost {posledni.Condition:G4}, verdikt: {posledni.Verdict}");
            Console.WriteLine($"  pokryti: azimuty {posledni.FilledAzimuthBins}/24,"
                              + $" naklony {posledni.TiltGroups} (odklonene {posledni.TiltedGroups},"
                              + $" na obe strany {(posledni.HasOppositeTilts ? "ano" : "NE")}),"
                              + $" vzorku {posledni.Samples}");
            Console.WriteLine($"  registr 23 pred merenim: "
                              + (posledni.Reg23Before.Length == 0 ? "(neprecteno)" : posledni.Reg23Before));
            Console.WriteLine("  " + (posledni.Vnwrg23.Length == 0 ? "(neprolozeno)" : posledni.Vnwrg23));

            if (nas == null)
            {
                // Bez vlastniho prolozeni neni s cim porovnavat - ale co robot v poli tvrdil, je
                // uzitecne videt PRAVE TADY, kdyz se u stolu prolozit nedalo.
                Console.WriteLine("  (vlastni prolozeni se nepodarilo, takze neni s cim porovnat)");
                return;
            }

            // ⚠️ Robot normoval na |B| PRECTENE ZE SENZORU, report na --bref. Kdyz se ta dve
            // cisla lisi, lisi se i vysledky OPRAVNENE - proto se to tiskne, ne zamlcuje.
            if (Math.Abs(posledni.BRefG - bRefG) > 1e-4)
                Console.WriteLine($"  ⚠️ robot normoval na |B| = {posledni.BRefG:F4} G, report na"
                                  + $" {bRefG:F4} G -> rozdil je OPRAVNENY. Pusti se s"
                                  + $" --bref={posledni.BRefG:F4}, kdyz chces srovnatelna cisla.");
            else
                Console.WriteLine("  ⚠️ Rozdil proti bodu 3 pri stejnem |B| znamena chybu v KODU,"
                                  + " ne v senzoru.");
        }

        private static double Sec(DateTime t, ref DateTime t0)
        {
            if (t0 == DateTime.MinValue) t0 = t;
            return (t - t0).TotalSeconds;
        }
    }
}
