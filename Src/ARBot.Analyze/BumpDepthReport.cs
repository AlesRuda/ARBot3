using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Vision;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Byla nerovnost vidět v hloubkové kameře dřív, než na ni robot najel?</b>
    ///
    /// <para><b>Nač to je.</b> Celý záměr <c>lp-drsnost-povrchu-rychlostni-strop</c> stojí na tom,
    /// že se hrbol dá <i>předpovědět</i> — jinak robot nestihne zpomalit. Otázka se dlouho vedla
    /// jako „nezodpověditelná ze záznamu, protože polární grid svou drsnost neposílá".
    /// ⚠️ <b>To bylo omylem:</b> grid se do záznamu ukládá <b>uvnitř <see cref="CameraFrame"/></b>
    /// (<c>CameraFrame.Grid</c>, FormatVersion 2, od 1. 8. 2026) i s <c>StdZ</c>, <c>MeanZ</c>,
    /// <c>MaxZ</c> a třídou buňky. Nic se tedy nemusí přehrávat — stačí se podívat.</para>
    ///
    /// <para><b>Jak se páruje místo.</b> Hrbol je v okamžiku zakopnutí <b>pod robotem</b>. O pár
    /// sekund dřív tedy ležel <c>Δs</c> metrů <b>před</b> ním, kde <c>Δs</c> je ujetá dráha mezi
    /// oběma okamžiky. Grid je robot-centrický, takže se hledá buňka kolem bodu
    /// <c>(x = Δs, y = 0)</c> v tělesovém rámci — <b>bez pózy</b>, a to je podstatné: póza
    /// v Kole 3b skákala korekcemi až o 4 m, kdežto ujetá dráha za pár sekund je spolehlivá.
    /// Buňka se hledá přes <c>MeanX</c>/<c>MeanY</c>, které nese každá buňka; azimutové koše
    /// jsou totiž definované <b>sloupcem obrazu</b>, ne úhlem, takže „buňka pro úhel 0°" by se
    /// musela hledat zpětnou projekcí.</para>
    ///
    /// <para>⚠️ <b>Bez kontrolní skupiny by to nic neznamenalo.</b> „Na místě hrbolu hlásila
    /// kamera StdZ 2 cm" je bezcenné, dokud se neví, co hlásí na <b>obyčejném</b> místě. Proto se
    /// týmž postupem měří i náhodná místa, kde robot nezakopl, a tiskne se to vedle sebe. Táž
    /// lekce jako u poissonovské nuly v <see cref="BumpReport"/>.</para>
    /// </summary>
    public static class BumpDepthReport
    {
        /// <summary>Jedno nahlédnutí kamery na místo, kde robot později zakopl (nebo na kontrolní).</summary>
        private sealed class Pohled
        {
            public double Dist;        // jak daleko to jeste bylo [m]
            public double StdZ;        // drsnost bunky [m]
            public double DevZ;        // odchylka vysky od mistni reference [m]
            public double MaxDevZ;     // totez pro nejvyssi bod v bunce [m]
            public bool Obstacle;      // oznacil to grid za prekazku?
            public int Count;          // kolik bodu bunku tvori
        }

        /// <param name="udalosti">Casy zakopnuti [s od zacatku zaznamu] a jejich ujeta draha.</param>
        /// <param name="drahaVCase">Prevod cas -> ujeta draha.</param>
        /// <param name="maxAhead">Jak daleko dopredu se jeste ptat [m].</param>
        /// <param name="lateral">Bocni tolerance pri hledani bunky [m].</param>
        public static void Run(RecordFile rec, List<(double T, double S)> udalosti,
                               Func<double, double> drahaVCase, double odS, double doS,
                               double maxAhead, double lateral, int maxFrames)
        {
            Console.WriteLine("=== 9. BYLA NEROVNOST VIDET V HLOUBKOVE KAMERE? ===");
            if (udalosti.Count == 0) { Console.WriteLine("  zadne udalosti."); return; }

            // Snimky se ctou az ted a jen z okna - jsou to megabajty na kus. Zacatek okna se
            // posouva o maxAhead/0,2 m/s zpet, aby byly k dispozici i snimky, na kterych je misto
            // jeste daleko.
            double odFrames = odS - 12.0;
            var frames = new List<(double T, string Name, PolarTraversabilityGrid G)>();
            DateTime t0 = DateTime.MinValue;
            int precteno = 0;
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "CameraFrame") continue;
                if (frames.Count >= maxFrames) break;
                var m = rec.Read(e);
                if (!(m is CameraFrame cf)) continue;
                precteno++;
                if (t0 == DateTime.MinValue) t0 = cf.TimeStamp;
                double t = (cf.TimeStamp - t0).TotalSeconds;
                if (t < odFrames || t > doS) continue;
                if (cf.Grid == null || cf.Grid.Cells == null) continue;
                frames.Add((t, cf.Name ?? "?", cf.Grid));
            }
            if (frames.Count == 0)
            {
                Console.WriteLine($"  v okne neni zadny snimek s gridem (precteno {precteno} snimku).");
                Console.WriteLine("  ⚠️ Zaznamy formatu 1 grid neobsahuji - viz doc/traversability-grid.md.");
                return;
            }
            var podleKamery = frames.GroupBy(f => f.Name)
                                    .ToDictionary(g => g.Key, g => g.Count());
            Console.WriteLine($"  snimku s gridem v okne: {frames.Count}  ["
                              + string.Join(", ", podleKamery.Select(k => $"{k.Key}: {k.Value}")) + "]");
            var g0 = frames[0].G;
            Console.WriteLine(F("  grid: {0} azimutu x {1} prstencu, dosah {2:F2}-{3:F2} m",
                                g0.AzimuthCount, g0.RadialCount,
                                g0.RadialEdges[0].Range, g0.RadialEdges[g0.RadialEdges.Length - 1].Range));
            Console.WriteLine();

            // Kontrolni casy: rovnomerne po okne, ale ne bliz nez 1,5 s k nektere udalosti.
            var kontrolni = new List<(double T, double S)>();
            for (double t = odS; t <= doS; t += 0.5)
                if (udalosti.All(u => Math.Abs(u.T - t) > 1.5))
                    kontrolni.Add((t, drahaVCase(t)));

            var hrbol = Sber(frames, udalosti, maxAhead, lateral, drahaVCase);
            var kontrola = Sber(frames, kontrolni, maxAhead, lateral, drahaVCase);
            Console.WriteLine(F("  nahledu na misto zakopnuti: {0} (z {1} udalosti);"
                                + " kontrolnich: {2} (z {3} mist)",
                                hrbol.Count, udalosti.Count, kontrola.Count, kontrolni.Count));
            if (hrbol.Count < 10)
            {
                Console.WriteLine("  prilis malo nahledu - bez zaveru.");
                return;
            }
            Console.WriteLine();
            Console.WriteLine("  co kamera hlasila NA MISTE, kde robot pozdeji zakopl, podle toho,");
            Console.WriteLine("  jak daleko to jeste bylo - a totez na NAHODNYCH mistech (kontrola):");
            Console.WriteLine();
            Console.WriteLine("   vzdalenost      n      StdZ [cm]        odchylka vysky [cm]   max bod [cm]   Obstacle [%]");
            Console.WriteLine("                hrb/kon   hrbol  kontrola    hrbol  kontrola    hrbol  kontr   hrbol kontr");
            for (double d = 0.5; d < maxAhead; d += 0.5)
            {
                var h = hrbol.Where(x => x.Dist >= d && x.Dist < d + 0.5).ToList();
                var k = kontrola.Where(x => x.Dist >= d && x.Dist < d + 0.5).ToList();
                if (h.Count == 0 && k.Count == 0) continue;
                Console.WriteLine(F("  {0,4:F1}-{1,4:F1} m  {2,4}/{3,-4} {4,7} {5,9} {6,9} {7,9} {8,8} {9,7} {10,7} {11,6}",
                    d, d + 0.5, h.Count, k.Count,
                    P50(h, x => x.StdZ * 100), P50(k, x => x.StdZ * 100),
                    P50(h, x => x.DevZ * 100), P50(k, x => x.DevZ * 100),
                    P50(h, x => x.MaxDevZ * 100), P50(k, x => x.MaxDevZ * 100),
                    Pct(h, x => x.Obstacle), Pct(k, x => x.Obstacle)));
            }
            Console.WriteLine();

            // Jedno cislo, ktere rozhoduje: je na miste hrbolu neco vic nez jinde, a do jake
            // vzdalenosti? Porovnava se p90 (hrbol je vzacny jev, median ho neuvidi).
            Console.WriteLine("  ROZHODUJICI SROVNANI (p90, tedy ten horsi konec - hrbol je vzacny):");
            Console.WriteLine("   vzdalenost   StdZ p90 [cm]      max bod p90 [cm]     pomer hrbol/kontrola");
            for (double d = 0.5; d < maxAhead; d += 0.5)
            {
                var h = hrbol.Where(x => x.Dist >= d && x.Dist < d + 0.5).ToList();
                var k = kontrola.Where(x => x.Dist >= d && x.Dist < d + 0.5).ToList();
                if (h.Count < 5 || k.Count < 5) continue;
                double hs = Q(h, x => x.StdZ, 0.9) * 100, ks = Q(k, x => x.StdZ, 0.9) * 100;
                double hm = Q(h, x => x.MaxDevZ, 0.9) * 100, km = Q(k, x => x.MaxDevZ, 0.9) * 100;
                Console.WriteLine(F("  {0,4:F1}-{1,4:F1} m  {2,6:F2} / {3,-6:F2}   {4,6:F2} / {5,-6:F2}"
                                    + "      StdZ {6,5:F2}x   max bod {7,5:F2}x",
                                    d, d + 0.5, hs, ks, hm, km,
                                    ks > 0 ? hs / ks : double.NaN, km > 0 ? hm / km : double.NaN));
            }
            Console.WriteLine();
            Console.WriteLine("  ⚠️ Pomer kolem 1,0 znamena, ze se misto hrbolu od obycejneho mista");
            Console.WriteLine("     NELISI - tedy ze hloubka tu nerovnost na te vzdalenosti nevidi.");
            Console.WriteLine("     Sum hloubky roste s r^2, takze rozdil ma mizet s rostouci vzdalenosti;");
            Console.WriteLine("     zajimave je, DO JAKE vzdalenosti jeste vydrzi.");
        }

        /// <summary>Posbira, co kamery hlasily o zadanych mistech, dokud byla jeste pred robotem.</summary>
        private static List<Pohled> Sber(List<(double T, string Name, PolarTraversabilityGrid G)> frames,
                                         List<(double T, double S)> mista, double maxAhead, double lateral,
                                         Func<double, double> drahaVCase)
        {
            var outp = new List<Pohled>();
            foreach (var u in mista)
                foreach (var f in frames)
                {
                    if (f.T >= u.T) continue;                 // snimek musi byt PRED udalosti
                    double d = u.S - drahaVCase(f.T);
                    if (double.IsNaN(d) || d < 0.3 || d > maxAhead) continue;
                    var p = Bunka(f.G, d, lateral);
                    if (p != null) { p.Dist = d; outp.Add(p); }
                }
            return outp;
        }

        /// <summary>
        /// Najde v gridu bunku kolem bodu <c>(x = dist, y = 0)</c> a spocte jeji odchylku vysky
        /// proti <b>mistni referenci</b> — medianu vysek bunek v podobne vzdalenosti.
        ///
        /// <para>⚠️ Reference se musi brat z TEHOZ snimku a TEHOZ prstence. Absolutni <c>MeanZ</c>
        /// nic nerika: kamera je sklonena a zem pod robotem neni v nule, takze by „odchylka vysky"
        /// merila hlavne sklon terenu a montaz kamery.</para>
        /// </summary>
        private static Pohled Bunka(PolarTraversabilityGrid g, double dist, double lateral)
        {
            int R = g.RadialCount, A = g.AzimuthCount;
            if (R <= 0 || A <= 0) return null;
            var vRozsahu = new List<PolarCell>();
            var vPasu = new List<PolarCell>();
            for (int a = 0; a < A; a++)
                for (int r = 0; r < R; r++)
                {
                    var c = g[a, r];
                    if (c.Count <= 0) continue;
                    double rng = Math.Sqrt(c.MeanX * c.MeanX + c.MeanY * c.MeanY);
                    if (Math.Abs(rng - dist) > 0.5) continue;
                    vRozsahu.Add(c);
                    // ⚠️ Pas, ne osa. Kola jsou na +-0,205 m od osy, takze bunka presne pred
                    // robotem nemusi byt ta, o kterou kolo zakoplo; a z pasu se bere ta NEJHORSI,
                    // protoze kolo najede na to nejvyssi, co mu lezi v ceste - ne na prumer.
                    if (Math.Abs(c.MeanY) <= lateral && Math.Abs(c.MeanX - dist) <= 0.15)
                        vPasu.Add(c);
                }
            if (vPasu.Count == 0 || vRozsahu.Count < 5) return null;
            double refZ0 = vRozsahu.Select(c => (double)c.MeanZ).OrderBy(z => z)
                                   .ElementAt(vRozsahu.Count / 2);
            PolarCell? nejlepsi = null;
            double nejhorsi = -1;
            foreach (var c in vPasu)
            {
                double d2 = Math.Abs(c.MaxZ - refZ0);
                if (d2 > nejhorsi) { nejhorsi = d2; nejlepsi = c; }
            }
            var cell = nejlepsi.Value;
            double refZ = refZ0;
            return new Pohled
            {
                StdZ = cell.StdZ,
                DevZ = Math.Abs(cell.MeanZ - refZ),
                MaxDevZ = Math.Abs(cell.MaxZ - refZ),
                Obstacle = cell.Class == TraversabilityClass.Obstacle,
                Count = cell.Count,
            };
        }

        private static string P50(List<Pohled> l, Func<Pohled, double> f)
            => l.Count == 0 ? "   -" : F("{0,6:F2}", Q(l, f, 0.5));

        private static string Pct(List<Pohled> l, Func<Pohled, bool> f)
            => l.Count == 0 ? "   -" : F("{0,5:F1}", 100.0 * l.Count(f) / l.Count);

        private static double Q(List<Pohled> l, Func<Pohled, double> f, double q)
        {
            if (l.Count == 0) return double.NaN;
            var v = l.Select(f).Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (v.Count == 0) return double.NaN;
            return v[Math.Min(v.Count - 1, (int)(q * (v.Count - 1)))];
        }

        private static string F(string fmt, params object[] a)
            => string.Format(CultureInfo.InvariantCulture, fmt, a);
    }
}
