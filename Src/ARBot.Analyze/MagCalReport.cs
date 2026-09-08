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
                return;
            }

            bool ok = MagCalFit.TryFit(cov.Mag, bRefG, out var r, out double cond, cov.Acc);

            Console.WriteLine();
            Console.WriteLine("2) PROLOZENI");
            Console.WriteLine($"  podminenost:         {cond:G4}"
                              + $"  (prah {MagCalThresholds.MaxCondition:G4})");
            if (!ok)
            {
                Console.WriteLine("  Soustava NENI URCENA - neprokladam.");
                Console.WriteLine("  Neni to chyba: bezna jizda azimuty ani naklony nepokryje."
                                  + " Presne proto je potreba rotacni test.");
                return;
            }

            Console.WriteLine($"  sd(|B|) po korekci:  {r.SdMagnitudeG:F5} G"
                              + $"  (prah {MagCalThresholds.MaxSdMagnitudeG:F3})");
            Console.WriteLine($"  sd(sklonu):          {r.SdInclinationDeg:F3}°"
                              + $"  (prah {MagCalThresholds.MaxSdInclinationDeg:F1})");

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
                    Console.WriteLine("  ⚠️ rozpuleni SAMO NESTACI - dve stejne degenerovana data"
                                      + " se shodnou taky. Musi platit i podminenost.");
                }
                else
                {
                    Console.WriteLine("  rozpuleni dat:       nelze - jedna z polovin neni urcena.");
                }
            }

            Console.WriteLine();
            Console.WriteLine("3) VYSLEDEK — ke zkopirovani za VNWRG,23,");
            Console.WriteLine("  " + r.ToVnwrg23());

            bool pouzitelne = cov.Complete
                              && r.SdMagnitudeG <= MagCalThresholds.MaxSdMagnitudeG
                              && !(r.SdInclinationDeg > MagCalThresholds.MaxSdInclinationDeg);
            Console.WriteLine("  verdikt: " + (pouzitelne
                ? "POUZITELNE"
                : "NEPOUZITELNE - viz pokryti a cisla vyse"));

            if (reg47 != null)
            {
                Console.WriteLine();
                Console.WriteLine("4) POROVNANI S REGISTREM 47 (co spocital sam senzor)");
                Console.WriteLine("  senzor: " + reg47);
                Console.WriteLine("  my:     " + r.ToVnwrg23());
                Console.WriteLine("  ⚠️ Dve nezavisle metody, ktere se shodnou, jsou dukaz."
                                  + " Kdyz se rozejdou, NEZAPISUJ nic a premysli.");
            }

            VerdiktZPole(rec, bRefG, r);
        }

        /// <summary>
        /// <b>Co spocital robot v poli</b> (<see cref="MagCalMsg"/> ze zaznamu) vedle vlastniho
        /// prepoctu. Je to ta kontrola, kvuli ktere zprava vubec tece do zaznamu: verdikt v poli
        /// a verdikt u stolu musi byt <b>totez cislo</b>.
        /// </summary>
        private static void VerdiktZPole(RecordFile rec, double bRefG, MagCalResult nas)
        {
            MagCalMsg posledni = null;
            foreach (var m in rec.ReadAll<MagCalMsg>(nameof(MagCalMsg))) posledni = m;
            if (posledni == null) return;

            Console.WriteLine();
            Console.WriteLine("5) CO SPOCITAL ROBOT V POLI (MagCalMsg ze zaznamu)");
            Console.WriteLine($"  podminenost {posledni.Condition:G4}, verdikt: {posledni.Verdict}");
            Console.WriteLine($"  pokryti: azimuty {posledni.FilledAzimuthBins}/24,"
                              + $" naklony {posledni.TiltGroups} (odklonene {posledni.TiltedGroups},"
                              + $" na obe strany {(posledni.HasOppositeTilts ? "ano" : "NE")}),"
                              + $" vzorku {posledni.Samples}");
            Console.WriteLine($"  registr 23 pred merenim: "
                              + (posledni.Reg23Before.Length == 0 ? "(neprecteno)" : posledni.Reg23Before));
            Console.WriteLine("  " + (posledni.Vnwrg23.Length == 0 ? "(neprolozeno)" : posledni.Vnwrg23));

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
