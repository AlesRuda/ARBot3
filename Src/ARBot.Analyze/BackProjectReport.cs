using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Vision.Nn;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Vyplatí se neuronová síť místo histogramu?</b> Pustí přes RGB snímky ze záznamu obě
    /// implementace <see cref="IBackProject"/> — dosavadní <see cref="BackProject"/> (zpětná
    /// projekce z histogramu barev) i <see cref="OnnxBackProject"/> (sémantická segmentace) —
    /// a řekne, <b>jak dlouho každá trvá</b> a <b>jak moc se jejich verdikt liší</b>.
    ///
    /// <para><b>Nač to je:</b> obojí se dá zapnout parametrem (<c>backproject=</c>), ale rozhodnout
    /// se dá jen podle čísel z reálných snímků — a ty jsou právě v záznamu ze zařízení. Čas měřený
    /// tady je z toho stroje, kde nástroj běží (tedy z vývojového PC); pro čas <b>na robotu</b> se
    /// musí pustit aplikace s <c>backproject=nn</c> a číst <c>traversability-timing-*.csv</c>
    /// nebo panel Výkon (viz doc/perf-monitoring.md).</para>
    ///
    /// <para><b>Porovnává se ve stejném rozlišení.</b> Histogram vrací plný snímek (640×480), síť
    /// svých 128×128 — histogram se proto zmenší nejbližším sousedem na rozměr sítě, stejně jako
    /// to dělá <c>CameraFrameProcessor</c> před vlastním voláním. Shoda se počítá jako <b>shoda
    /// rozhodnutí</b> při prahu 128 (sjízdno/nesjízdno), ne jako rozdíl hodnot: obě metody dávají
    /// spojitou pravděpodobnost, ale každá ve své škále, takže rozdíl hodnot by neříkal nic.</para>
    ///
    /// <para>Čte celé snímky, takže na velkém záznamu to trvá — proto <c>--limit</c> (výchozí 100).</para>
    /// </summary>
    public static partial class BackProjectReport
    {
        /// <param name="rec">Otevřený záznam.</param>
        /// <param name="modelPath">Cesta k <c>.onnx</c> modelu.</param>
        /// <param name="limit">Kolik snímků zpracovat (0 = vše).</param>
        /// <param name="skip">Kolik snímků na začátku přeskočit.</param>
        /// <param name="bgr">Pořadí kanálů BGR místo výchozího RGB (A/B, viz <c>nnchannels=</c>).</param>
        /// <param name="png">Prefix cesty pro uložení srovnání prvního snímku; <c>null</c> = neukládat.</param>
        public static void Run(RecordFile rec, string modelPath, int limit, int skip, bool bgr, string png)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
            {
                Console.Error.WriteLine("Chybi --model=<cesta k .onnx>. Prevod z TFLite: models/tflite2onnx.py.");
                return;
            }
            if (!File.Exists(modelPath))
            {
                Console.Error.WriteLine($"Model neexistuje: {modelPath}");
                return;
            }

            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            int celkem = entries.Count;
            if (skip > 0) entries = entries.Skip(skip).ToList();
            if (limit > 0) entries = entries.Take(limit).ToList();
            Console.WriteLine($"CameraFrame v indexu: {celkem}"
                              + (entries.Count < celkem ? $" (cte se {entries.Count} od poradi {skip})" : ""));
            if (entries.Count == 0) return;

            var channels = bgr ? NnChannelOrder.Bgr : NnChannelOrder.Rgb;
            // Podle pripony: .rknn jde na NPU, jinak ONNX Runtime na CPU.
            using var nn = NnBackProject.Open(modelPath, channels);
            var hist = new BackProject(BackProject.RoadProbability);

            Console.WriteLine($"model: {Path.GetFileName(modelPath)}, vstup {nn.InputWidth}x{nn.InputHeight}, "
                              + $"vystup {nn.OutputWidth}x{nn.OutputHeight}x{nn.OutputChannels}, "
                              + $"kanaly {channels}");
            Console.WriteLine();

            // Statistika ZVLAST za kazdou kameru. Michat je dohromady je past: 6. 9. 2026 mela
            // prava D435 zamrzly barevny stream (viz hardware.md), takze polovina snimku byla
            // tentyz obraz - a prumer pres obe kamery pak vypadal podezrele stabilne.
            var kamery = new Dictionary<string, Kamera>(StringComparer.Ordinal);
            var ulozeneKamery = new HashSet<string>(StringComparer.Ordinal);
            int zpracovano = 0;
            var sw = new Stopwatch();

            Image<Gray> probNn = null, probHist = null, probHistMaly = null;
            Image<BGR32> zmenseny = null;

            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f) || f.ImageRGB == null) continue;
                var rgb = f.ImageRGB;
                string jmeno = f.Name ?? "(bez jmena)";
                if (!kamery.TryGetValue(jmeno, out var k)) kamery[jmeno] = k = new Kamera(jmeno);

                // --- sit: snimek se zmensi na rozmer, ktery zada Size() (jako CameraFrameProcessor)
                var sizeNn = nn.Size(rgb.Width, rgb.Height);
                zmenseny = Ensure(zmenseny, sizeNn.Width, sizeNn.Height);
                zmenseny.Resize(rgb);
                probNn = Ensure(probNn, sizeNn.Width, sizeNn.Height);
                sw.Restart();
                nn.Process(zmenseny, probNn);
                k.CasNn.Add(sw.Elapsed.TotalMilliseconds);

                // --- histogram: pracuje v plnem rozliseni snimku
                probHist = Ensure(probHist, rgb.Width, rgb.Height);
                sw.Restart();
                hist.Process(rgb, probHist);
                k.CasHist.Add(sw.Elapsed.TotalMilliseconds);

                // --- porovnani ve stejnem rozliseni
                probHistMaly = Ensure(probHistMaly, sizeNn.Width, sizeNn.Height);
                probHistMaly.Resize(probHist);

                int stejne = 0, sjizdnoNn = 0, sjizdnoHist = 0;
                var a = probNn.Data;
                var b = probHistMaly.Data;
                for (int i = 0; i < a.Length; i++)
                {
                    bool an = a[i] >= 128, bh = b[i] >= 128;
                    if (an == bh) stejne++;
                    if (an) sjizdnoNn++;
                    if (bh) sjizdnoHist++;
                }
                k.Shoda.Add(100.0 * stejne / a.Length);
                k.PodilNn.Add(100.0 * sjizdnoNn / a.Length);
                k.PodilHist.Add(100.0 * sjizdnoHist / a.Length);
                k.Otisky.Add(Otisk(rgb.Data));
                zpracovano++;

                if (!string.IsNullOrWhiteSpace(png) && ulozeneKamery.Add(jmeno))
                    UlozSrovnani(png, jmeno, zmenseny, probNn, probHistMaly);
            }

            if (zpracovano == 0)
            {
                Console.WriteLine("Zadny snimek s RGB obrazem — porovnavat neni co.");
                return;
            }

            Console.WriteLine($"zpracovano {zpracovano} snimku ze {kamery.Count} kamer");

            foreach (var k in kamery.Values)
            {
                Console.WriteLine();
                Console.WriteLine($"--- {k.Jmeno} ({k.Shoda.Count} snimku, {k.Otisky.Count} ruznych obrazu)");
                if (k.Otisky.Count == 1 && k.Shoda.Count > 1)
                    Console.WriteLine("    ⚠️ POZOR: vsechny snimky jsou TYZ obraz (zamrzly stream) — "
                                      + "cisla nize nic nevypovidaji o rozmanitosti sceny.");
                Console.WriteLine("  cas jednoho snimku [ms] (tento stroj, NE robot):");
                Vypis("    sit      ", k.CasNn);
                Vypis("    histogram", k.CasHist);
                Vypis("  shoda rozhodnuti (prah 128) [%]", k.Shoda);
                Console.WriteLine("  podil sjizdne plochy [%] — kdyz se lisi radove, lisi se i to, co uvidi occupancy grid:");
                Vypis("    sit      ", k.PodilNn);
                Vypis("    histogram", k.PodilHist);
            }

            // Cenu za snimek plati robot za KAZDOU kameru, takze soucet mediánu pres kamery.
            double zaSekundu = kamery.Values.Sum(k => 30 * Median(k.CasNn)) / 1000.0;
            Console.WriteLine();
            Console.WriteLine($"Pri {kamery.Count} kamerach po 30 snimcich/s stoji sit {zaSekundu * 100:F1} % "
                              + "jednoho jadra tohoto stroje (medianem, bez predzpracovani kamery).");
        }

        /// <summary>
        /// Ulozi JEDEN obrazek: vstup | sit | histogram vedle sebe. Tri soubory zvlast by se
        /// musely skladat rucne pokazde, kdyz se ma vysledek nekomu ukazat nebo dat do dokumentace.
        /// </summary>
        private static void UlozSrovnani(string prefix, string kamera, Image<BGR32> vstup,
                                         Image<Gray> sit, Image<Gray> histogram)
        {
            try
            {
                const int Mezera = 4;
                int w = vstup.Width, h = vstup.Height;
                var spolu = new Image<BGR32>(3 * w + 2 * Mezera, h);
                Vloz(spolu, vstup, 0);
                Vloz(spolu, histogram, w + Mezera);
                Vloz(spolu, sit, 2 * (w + Mezera));

                string token = string.Join("_", (kamera ?? "kamera").Split(Path.GetInvalidFileNameChars()));
                string cesta = $"{prefix}-{token}.png";
                File.WriteAllBytes(cesta, ImageMsg.EncodePng(spolu));
                Console.WriteLine($"ulozeno {cesta}  (vlevo vstup, uprostred histogram, vpravo sit)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"PNG se neulozilo: {ex.Message}");
            }
        }

        /// <summary>Vykresli obraz do slozeneho snimku na vodorovny posun <paramref name="x0"/>.</summary>
        private static void Vloz(Image<BGR32> cil, Image zdroj, int x0)
        {
            var dst = cil.Data;
            var src = zdroj.Data;
            int step = zdroj.Step;
            for (int y = 0; y < zdroj.Height; y++)
                for (int x = 0; x < zdroj.Width; x++)
                {
                    int si = step * (x + y * zdroj.Width);
                    int di = 4 * ((x0 + x) + y * cil.Width);
                    // Gray (1 bajt) se roztahne do vsech tri kanalu, BGR32 se zkopiruje.
                    dst[di] = src[si];
                    dst[di + 1] = step >= 3 ? src[si + 1] : src[si];
                    dst[di + 2] = step >= 3 ? src[si + 2] : src[si];
                }
        }

        private static Image<T> Ensure<T>(Image<T> img, int w, int h) where T : IPixel, new()
            => img != null && img.Width == w && img.Height == h ? img : new Image<T>(w, h);

        /// <summary>FNV-1a otisk obrazu — stejny trik jako <see cref="CameraFramesReport"/>: hleda
        /// se shoda, ne podobnost, a drzet predchozi snimky by znamenalo drzet megabajty.</summary>
        private static ulong Otisk(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            foreach (var b in data) { h ^= b; h *= 1099511628211UL; }
            return h;
        }

        /// <summary>Statistika jedne kamery.</summary>
        private sealed class Kamera
        {
            public Kamera(string jmeno) { Jmeno = jmeno; }
            public string Jmeno { get; }
            public List<double> CasNn { get; } = new List<double>();
            public List<double> CasHist { get; } = new List<double>();
            public List<double> Shoda { get; } = new List<double>();
            public List<double> PodilNn { get; } = new List<double>();
            public List<double> PodilHist { get; } = new List<double>();
            public HashSet<ulong> Otisky { get; } = new HashSet<ulong>();
        }

        private static void Vypis(string popis, List<double> hodnoty)
        {
            var s = hodnoty.OrderBy(v => v).ToList();
            Console.WriteLine($"{popis} p50 {Median(hodnoty),8:F3}   p90 {s[(int)(0.9 * (s.Count - 1))],8:F3}"
                              + $"   min {s[0],8:F3}   max {s[s.Count - 1],8:F3}");
        }

        private static double Median(List<double> v)
        {
            var s = v.OrderBy(x => x).ToList();
            return s.Count == 0 ? 0 : s[s.Count / 2];
        }
    }
}
