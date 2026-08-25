using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Jela mise FreeRun v pravé polovině koridoru?</b> Viz doc/mission-freerun.md.
    ///
    /// <para>Dva ukazatele, ktere nejsou totez:</para>
    /// <list type="number">
    ///   <item><b>Regulacni odchylka</b> — <c>Lateral</c> proti pozadovanemu <c>−Width/4</c>. Rika,
    ///   jak dobre mise drzi svuj vlastni cil, ale je to merene TIM SAMYM koridorem, ktery mrkev
    ///   pokladal, takze je to trochu kruhove.</item>
    ///   <item><b>Proti PRAVDE</b> — <c>--axisy</c> a <c>--truewidth</c> zadaji skutecnou osu a sirku
    ///   cesty, takze se pricna poloha porovna s ground truth. Nekruhove, ale jen v simulaci.
    ///   Pro <c>OSM/SyntetickyRovny.osm</c>: <c>--axisy=0 --truewidth=2.0</c>.</item>
    /// </list>
    ///
    /// <para><b>A podil cyklu, kde koridor vubec byl</b> — bez nej cisla vyse nic nevazi: mise mohla
    /// „drzet se vpravo" perfektne na dvou procentech cyklu a jinak jen jet rovne.</para>
    /// </summary>
    public static class FreeRunReport
    {
        /// <param name="axisY">Skutecna osa cesty (primka <c>y = axisY</c>); NaN = neporovnavat.</param>
        /// <param name="trueWidth">Skutecna sirka cesty [m]; 0 = vzit sirku hlasenou misi.</param>
        public static void Run(RecordFile rec, double axisY, double trueWidth)
        {
            var msgs = new List<FreeRunMsg>();
            var truth = new List<GroundTruthMsg>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName == "FreeRunMsg" && rec.Read(e) is FreeRunMsg m) msgs.Add(m);
                else if (e.MsgName == "GroundTruthMsg" && rec.Read(e) is GroundTruthMsg g) truth.Add(g);
            }

            Console.WriteLine($"FreeRunMsg: {msgs.Count} zprav, GroundTruthMsg {truth.Count}");
            if (msgs.Count == 0)
            {
                Console.WriteLine("Zaznam misi nenese — pusti se parametrem mission=freerun.");
                return;
            }
            msgs.Sort((a, b) => a.TimeStamp.CompareTo(b.TimeStamp));
            double dur = (msgs[msgs.Count - 1].TimeStamp - msgs[0].TimeStamp).TotalSeconds;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "delka useku: {0:F1} s  ({1:F2} Hz)", dur, msgs.Count / Math.Max(0.001, dur)));
            Console.WriteLine();

            // (1) Mela mise vubec co sledovat?
            int fromCorridor = msgs.Count(m => m.FromCorridor);
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "MELA MISE CO SLEDOVAT: mrkev z koridoru u {0} z {1} cyklu ({2:F0} %), "
                + "zbytek jel rovne", fromCorridor, msgs.Count, 100.0 * fromCorridor / msgs.Count));
            Console.WriteLine("  duvody (CorridorFixReason):");
            foreach (var g in msgs.GroupBy(m => m.Reason).OrderByDescending(g => g.Count()))
                Console.WriteLine($"    {g.Key,-4} {g.Count(),6}");
            Console.WriteLine();

            var ok = msgs.Where(m => m.FromCorridor).ToList();
            if (ok.Count == 0)
            {
                Console.WriteLine("Zadny cyklus s koridorem — dal neni co merit.");
                return;
            }

            // (2) Regulacni odchylka proti VLASTNIMU cili.
            var width = new Stats("sirka koridoru [m]");
            var err = new Stats("odchylka od pozadovane cary [m]");
            var lateral = new Stats("hlasena pricna poloha [m]");
            foreach (var m in ok)
            {
                width.Add(m.Width);
                lateral.Add(m.Lateral);
                // Pozadovano: Lateral = -Width/4. Kladna odchylka = robot je VIC VLEVO, nez ma.
                err.Add(m.Lateral + m.Width / 4.0);
            }

            Console.WriteLine("DRZI MISE SVUJ VLASTNI CIL (pozadovano Lateral = -Width/4):");
            Console.WriteLine("  " + width.Line("m"));
            Console.WriteLine("  " + lateral.Line("m"));
            Console.WriteLine("  " + err.Line("m"));
            Console.WriteLine("  Kladna odchylka = robot je VIC VLEVO, nez ma. Pozor: meri se TIM");
            Console.WriteLine("  SAMYM koridorem, ktery mrkev pokladal, takze je to trochu kruhove.");
            Console.WriteLine();

            // (3) Proti PRAVDE — nekruhove.
            if (double.IsNaN(axisY))
            {
                Console.WriteLine("(Bez --axisy se pricna poloha proti PRAVDE neporovnava. Pro");
                Console.WriteLine(" OSM/SyntetickyRovny.osm: --axisy=0 --truewidth=2.0)");
                return;
            }
            if (truth.Count == 0)
            {
                Console.WriteLine("Zaznam nenese GroundTruthMsg — proti pravde merit nejde (jen simulace).");
                return;
            }

            double want = -(trueWidth > 0 ? trueWidth : width.Median) / 4.0;
            var truthY = new Stats("skutecna pricna poloha [m]");
            var truthErr = new Stats("skutecna odchylka od pozadovane [m]");
            foreach (var g in truth)
            {
                double y = g.Y - axisY;
                truthY.Add(y);
                truthErr.Add(y - want);
            }

            Console.WriteLine("PROTI PRAVDE (ground truth, nekruhove):");
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  pozadovana pricna poloha: {0:F3} m (= -sirka/4 pri sirce {1:F2} m)",
                want, trueWidth > 0 ? trueWidth : width.Median));
            Console.WriteLine("  " + truthY.Line("m"));
            Console.WriteLine("  " + truthErr.Line("m"));
            Console.WriteLine();
            Console.WriteLine("  ⚠️ Rozjezd je v tom zahrnuty: robot startuje na ose, takze prvni");
            Console.WriteLine("  sekundy se teprve srovnava. Zajima p50 a konec, ne prumer.");

            if (truth.Count > 4)
            {
                var last = truth.Skip(truth.Count * 3 / 4).ToList();
                var tail = new Stats("posledni ctvrtina behu [m]");
                foreach (var g in last) tail.Add(g.Y - axisY);
                Console.WriteLine("  " + tail.Line("m"));
            }
        }
    }
}
