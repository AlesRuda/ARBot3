using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Co korekce z lokalizace proti mapě SKUTEČNĚ dělají, když se pustí naostro?</b>
    ///
    /// <para><b>Nacpak.</b> Podminky 2 a 3 (rychlostni limit na aplikovanou korekci, strop na
    /// nesouhlas s GPS) jsou posledni dve veci, ktere gatuji pustit korekce naostro — a obe jsou
    /// zatim jen NAVRH. Volit velikost limitu odhadem je presne ta chyba, ktera se tady uz
    /// nekolikrat vymstila; tenhle report ma dat cislo, ze ktereho se da vyjit.</para>
    ///
    /// <para><b>Tri otazky, na ktere odpovida:</b></para>
    /// <list type="number">
    ///   <item><b>Jak velky KROK pozy korekce zpusobi.</b> <c>MaxOffsetM</c> omezuje NAMERENY
    ///   posun, ne aplikovany — pri male sigma proti velkemu <c>P</c> muze filtr aplikovat skoro
    ///   cele dva metry v jednom updatu. Meri se stejnou logikou, jakou pouziva provoz
    ///   (<see cref="PoseJumpDetector"/>), takze cislo odpovida tomu, kdy se SKUTECNE zahodi grid.</item>
    ///   <item><b>Je gating konzistentni.</b> Rozdeleni NIS pro dany zdroj: pri gatingu na 95 %
    ///   chi2 ma byt zamitnutych kolem 5 %. Vyrazne vic znamena prilis malou sigma — a je to
    ///   nezavisly test poctivosti, ktery nepotrebuje znamou odpoved.</item>
    ///   <item><b>Pomohlo to vubec.</b> Chyba pozy proti ground truth; srovnava se mezi behy
    ///   s korekcemi a bez nich.</item>
    /// </list>
    /// </summary>
    public static class CorrectionsReport
    {
        /// <summary>Zdroje merenii, ktere sem patri (zbytek se jen secte jako „ostatni").</summary>
        private static readonly string[] Interesting = { "MapCorr", "Corridor" };

        public static void Run(RecordFile rec)
        {
            var states = new List<RobotStateMsg>();
            var truth = new List<GroundTruthMsg>();
            var diag = new List<MeasurementDiagMsg>();
            var corr = new List<MapCorrelationMsg>();

            foreach (var e in rec.Index)
            {
                switch (e.MsgName)
                {
                    case "RobotStateMsg":
                        if (rec.Read(e) is RobotStateMsg s) states.Add(s);
                        break;
                    case "GroundTruthMsg":
                        if (rec.Read(e) is GroundTruthMsg g) truth.Add(g);
                        break;
                    case "MeasurementDiagMsg":
                        if (rec.Read(e) is MeasurementDiagMsg d) diag.Add(d);
                        break;
                    case "MapCorrelationMsg":
                        if (rec.Read(e) is MapCorrelationMsg m) corr.Add(m);
                        break;
                }
            }
            states.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            truth.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            diag.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));

            Console.WriteLine($"RobotStateMsg {states.Count}, GroundTruthMsg {truth.Count}, "
                              + $"MeasurementDiagMsg {diag.Count}, MapCorrelationMsg {corr.Count}");
            if (states.Count < 2)
            {
                Console.WriteLine("Bez RobotStateMsg nejde merit nic.");
                return;
            }
            Console.WriteLine();

            ReportEmitted(corr);
            ReportGating(diag);
            ReportPoseJumps(states);
            ReportPoseError(states, truth);
        }

        /// <summary>Kolik korekci korelace vubec do fuze poslala a kolik z nich fuze zahodila.</summary>
        private static void ReportEmitted(List<MapCorrelationMsg> corr)
        {
            if (corr.Count == 0)
            {
                Console.WriteLine("KOREKCE Z KORELACE: zaznam MapCorrelationMsg nenese "
                                  + "(pusti se mapcorr=true).");
                Console.WriteLine();
                return;
            }

            int emitted = corr.Count(m => m.Emitted);
            int tight = corr.Count(m => m.EmitTightAxis);
            int loose = corr.Count(m => m.EmitLooseAxis);
            int heading = corr.Count(m => m.EmitHeading);
            long dropped = corr.Max(m => m.DroppedByFusion);

            Console.WriteLine("KOREKCE Z KORELACE — co se vubec poslalo:");
            Console.WriteLine($"  cyklu {corr.Count}, z toho poslalo aspon neco {emitted}");
            Console.WriteLine($"  tesna osa {tight}, volna osa {loose}, kurz {heading}");
            Console.WriteLine($"  fuze zahodila jako PRILIS STARE (kumulativne): {dropped}");
            if (emitted == 0)
                Console.WriteLine("  ⚠️ Nic se neposlalo — bez mapcorrsend=true nema tenhle report co merit.");
            Console.WriteLine();
        }

        /// <summary>
        /// Rozdeleni NIS podle zdroje. <b>Nezavisly test poctivosti sigma:</b> pri gatingu na 95 %
        /// chi2 ma byt zamitnutych kolem 5 % — vyrazne vic znamena prilis malou sigma, vyrazne
        /// méně prilis velkou. Nepotrebuje k tomu znamou odpoved ani posunutou mapu.
        /// </summary>
        private static void ReportGating(List<MeasurementDiagMsg> diag)
        {
            if (diag.Count == 0)
            {
                Console.WriteLine("GATING A NIS: zaznam MeasurementDiagMsg nenese "
                                  + "(pusti se measdiag=true).");
                Console.WriteLine();
                return;
            }

            Console.WriteLine("GATING A NIS podle zdroje merenia:");
            Console.WriteLine("  zdroj                    n   prijato  zamitnuto  pozde   NIS p50   NIS p90   NIS max"
                              + "   sigma p50");
            foreach (var grp in diag.GroupBy(d => d.Source ?? "?").OrderByDescending(g => g.Count()))
            {
                var list = grp.ToList();
                int acc = list.Count(d => d.Verdict == 0);
                int gated = list.Count(d => d.Verdict == 1);
                int old = list.Count(d => d.Verdict == 2);
                var nis = new Stats("");
                // Sigma z prvni slozky DiagR. Bez ni nejde rict, PROC je NIS nizky - jestli je
                // merenie presne, nebo jen prizna velkou nejistotu. A prave tohle rozhoduje
                // u otazky "muze GPS slouzit jako nezavisla kontrola?".
                var sig = new Stats("");
                foreach (var d in list)
                {
                    nis.Add(d.Nis);
                    if (d.DiagR != null && d.DiagR.Length > 0 && d.DiagR[0] > 0)
                        sig.Add(Math.Sqrt(d.DiagR[0]));
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-20} {1,5}  {2,6}  {3,9}  {4,5}  {5,8:F2}  {6,8:F2}  {7,8:F2}  {8,8:F3}",
                    Trim(grp.Key, 20), list.Count, acc, gated, old,
                    nis.Median, nis.Percentile(90), nis.Max, sig.Median));
            }
            Console.WriteLine();

            foreach (var name in Interesting)
            {
                var list = diag.Where(d => (d.Source ?? "").StartsWith(name, StringComparison.Ordinal)).ToList();
                if (list.Count == 0) continue;

                int gated = list.Count(d => d.Verdict == 1);
                double pct = 100.0 * gated / list.Count;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0}: zamitnuto gatingem {1:F1} % ({2} z {3}) — u konzistentniho filtru ~5 %",
                    name, pct, gated, list.Count));
                Console.WriteLine(pct > 12.0
                    ? "    => VYRAZNE VIC nez 5 %: sigma je prilis mala, nebo je merenie vychylene."
                    : pct < 1.0
                        ? "    => VYRAZNE MENE nez 5 %: sigma je prilis velka (gating nic nedeli)."
                        : "    => v pasmu, ktere se u konzistentniho filtru ceka.");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// Jak velky krok pozy se skutecne aplikoval. <b>Meri se PROVOZNI logikou</b>
        /// (<see cref="PoseJumpDetector"/>), aby cislo odpovidalo tomu, kdy se opravdu zahodi grid —
        /// ne znovu-implementaci, ktera by se od ni mohla lisit.
        /// </summary>
        private static void ReportPoseJumps(List<RobotStateMsg> states)
        {
            var det = new PoseJumpDetector();
            int jumps = 0;
            var over = new Stats("pretok posunu nad rychlost [m]");
            var overTheta = new Stats("pretok kurzu nad omegu [deg]");

            for (int i = 0; i < states.Count; i++)
            {
                var s = states[i];
                if (det.Check(s.X, s.Y, s.Theta, s.V, s.Omega, s.TimeStamp)) jumps++;

                if (i == 0) continue;
                var p = states[i - 1];
                double dt = (s.TimeStamp - p.TimeStamp).TotalSeconds;
                if (dt <= 0) continue;

                double moved = Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Y - p.Y) * (s.Y - p.Y));
                over.Add(Math.Max(0, moved - Math.Abs(p.V) * dt));
                double dth = Math.Abs(Wrap(s.Theta - p.Theta));
                overTheta.Add(Math.Max(0, dth - Math.Abs(p.Omega) * dt) * 180.0 / Math.PI);
            }

            Console.WriteLine("APLIKOVANY KROK POZY — o kolik poza pretekla to, co vysvetli rychlost:");
            Console.WriteLine("  " + over.Line("m"));
            Console.WriteLine("  " + overTheta.Line("deg"));
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  SKOKU podle PoseJumpDetector (tolerance {0:F2} m / {1:F1} deg): {2} z {3} vzorku"
                + "  -> tolikrat by se zahodil grid",
                new PoseJumpDetector().ToleranceM,
                new PoseJumpDetector().ToleranceRad * 180.0 / Math.PI, jumps, states.Count));
            Console.WriteLine();
            Console.WriteLine("  Tohle je to cislo, ze ktereho ma vyjit RYCHLOSTNI LIMIT (podminka 2):");
            Console.WriteLine("  MaxOffsetM omezuje NAMERENY posun, ne aplikovany krok. Pozor - pretok");
            Console.WriteLine("  neni jen z korekci: patri do nej i sum filtru a zpetne precisteni uzlu");
            Console.WriteLine("  fixed-lag smootherem, takze bez srovnani s behem BEZ korekci to samo");
            Console.WriteLine("  nic nedokazuje.");
            Console.WriteLine();
        }

        /// <summary>Chyba pozy proti ground truth — jediny ukazatel, ktery rika, jestli to pomohlo.</summary>
        private static void ReportPoseError(List<RobotStateMsg> states, List<GroundTruthMsg> truth)
        {
            if (truth.Count == 0)
            {
                Console.WriteLine("CHYBA POZY: zaznam GroundTruthMsg nenese (jen simulace ho ma).");
                return;
            }

            var err = new Stats("chyba pozy |pravda - odhad| [m]");
            var errLat = new Stats("z toho PRICNE (napric kurzem) [m]");
            var errHead = new Stats("chyba kurzu [deg]");

            int ti = 0;
            foreach (var s in states)
            {
                // Nejblizsi ground truth v case; vzorkuje se stejne casto (~10 Hz), takze hledat
                // nejblizsi je presnejsi i levnejsi nez interpolovat.
                while (ti + 1 < truth.Count
                       && Math.Abs((truth[ti + 1].TimeStamp - s.TimeStamp).TotalSeconds)
                          <= Math.Abs((truth[ti].TimeStamp - s.TimeStamp).TotalSeconds))
                    ti++;
                var g = truth[ti];
                if (Math.Abs((g.TimeStamp - s.TimeStamp).TotalSeconds) > 0.2) continue;

                double dx = g.X - s.X, dy = g.Y - s.Y;
                err.Add(Math.Sqrt(dx * dx + dy * dy));
                // Pricna slozka je ta, kterou korelace s mapou vubec umi opravit.
                errLat.Add(Math.Abs(-Math.Sin(s.Theta) * dx + Math.Cos(s.Theta) * dy));
                errHead.Add(Math.Abs(Wrap(g.Theta - s.Theta)) * 180.0 / Math.PI);
            }

            Console.WriteLine("CHYBA POZY PROTI GROUND TRUTH — pomohlo to vubec?");
            Console.WriteLine("  " + err.Line("m"));
            Console.WriteLine("  " + errLat.Line("m"));
            Console.WriteLine("  " + errHead.Line("deg"));
            Console.WriteLine();
            Console.WriteLine("  Srovnavej PROTI BEHU BEZ KOREKCI (mapcorrsend=false / corridorsend=false),");
            Console.WriteLine("  jinak to nic nerika. A mer kazdou variantu VICKRAT - rozptyl mezi behy");
            Console.WriteLine("  teze konfigurace je tady vetsi, nez se ceka.");
        }

        private static double Wrap(double a)
        {
            while (a > Math.PI) a -= 2 * Math.PI;
            while (a < -Math.PI) a += 2 * Math.PI;
            return a;
        }

        private static string Trim(string s, int n) => s.Length <= n ? s : s.Substring(0, n);
    }
}
