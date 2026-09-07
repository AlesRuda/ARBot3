using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using ARBot.Common.Common;
using ARBot.Common.Logs;
using ARBot.Common.Vision;
using ARBot.Common.Vision.Nn;
using SkiaSharp;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Kdo ma pravdu?</b> Druhy rezim prikazu <c>backproject</c>: misto shody dvou metod meri
    /// obe <b>proti rucne oznacene pravde</b> (ground truth).
    ///
    /// <para><b>Nac to je:</b> shoda site s histogramem (rezim nad zaznamem) rika jen, jak moc se
    /// obe metody rozchazeji — <b>ne kdo se myli</b>. Kdyz na venkovnim zaznamu vyjde shoda 89,6 %,
    /// je to stejne dobre vysvetlitelne tim, ze se myli sit, jako tim, ze se myli histogram.
    /// Rozhodnout to umi jen sada s pravdou.</para>
    ///
    /// <para><b>Data:</b> <c>models/testset/</c> — tatataz pevna 50snimkova sada, na ktere vznikla
    /// cisla v trenovacim notebooku (viz doc/semantic-segmentation.md). Vytahne ji
    /// <c>Src/Colab/ExportTestSet.ipynb</c>; do repa nepatri o nic mene nez model sam, protoze
    /// bez ni je kazda zmena modelu hadani.</para>
    ///
    /// <para><b>Ve kterem rozliseni se meri:</b> hlavni cislo je na rozmeru VYSTUPU SITE (128x128)
    /// s pravdou zmensenou nejblizsim sousedem — presne jak to delal notebook, jinak by nasla
    /// cisla nebyla srovnatelna s temi jeho. Histogram se meri i v <b>plnem rozliseni</b>, protoze
    /// to je jeho skutecny provozni bod; ty dva sloupce se proto nesmi zamenit.</para>
    /// </summary>
    public static partial class BackProjectReport
    {
        /// <param name="dir">Adresar sady: <c>img/*.jpg|png</c> a <c>gt/*.png</c> se stejnymi jmeny.</param>
        /// <param name="modelPath">Cesta k <c>.onnx</c> modelu.</param>
        /// <param name="bgr">Pořadi kanalu BGR misto vychoziho RGB (A/B).</param>
        /// <param name="png">Prefix pro ulozeni srovnani NEJHORSIHO snimku; <c>null</c> = neukladat.</param>
        /// <param name="truthThreshold">
        /// Prah, od ktereho je pixel masky SJIZDNY. Vychozi 128 odpovida maskam 0/255, jak je
        /// uklada <c>Src/Colab/ExportTestSet.ipynb</c>. <b>Puvodni <c>ds_train</c> z notebooku ma
        /// ale masky 0/1</b> (index tridy), a ty by pri prahu 128 vysly cele nesjizdne - tedy
        /// pravda "nikde neni cesta" a nesmyslna cisla. Proto je to prepinac
        /// (<c>--truththreshold=1</c>), ne konstanta.
        /// </param>
        public static void Truth(string dir, string modelPath, bool bgr, string png,
                                 byte truthThreshold = SegmentationMetrics.Threshold,
                                 bool resizeCenter = false)
        {
            // Relativni cesta se resi proti KORENI REPA, ne proti pracovnimu adresari - nastroj
            // se spousti ze svého bin/, takze --truth=models/testset by se jinak hledalo
            // v bin\x64\Debug\net10.0\models\testset. Stejne to uz dela vychozi model
            // (ParamRegistry.NnModel pres RepoPaths), takze bez toho byl jeden prepinac
            // relativni k jinemu miste nez druhy.
            string zadano = dir;
            dir = ARBot.Common.Configuration.RepoPaths.Resolve(dir);
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Adresar testovaci sady neexistuje: {dir}");
                if (!string.Equals(zadano, dir, StringComparison.Ordinal))
                    Console.Error.WriteLine($"  (zadano '{zadano}', reseno proti koreni repa)");
                Console.Error.WriteLine("Sadu vytahne Src/Colab/ExportTestSet.ipynb (viz doc/semantic-segmentation.md).");
                return;
            }
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                Console.Error.WriteLine($"Model neexistuje: {modelPath}");
                return;
            }

            var pary = Pary(dir).ToList();
            if (pary.Count == 0)
            {
                Console.Error.WriteLine($"V {dir} nejsou zadne pary img/gt se stejnym jmenem.");
                return;
            }

            var opts = new OnnxBackProjectOptions { ChannelOrder = bgr ? NnChannelOrder.Bgr : NnChannelOrder.Rgb };
            using var nn = new OnnxBackProject(modelPath, opts);
            var hist = new BackProject(BackProject.RoadProbability);

            Console.WriteLine($"sada: {dir} ({pary.Count} snimku)");
            Console.WriteLine($"model: {Path.GetFileName(modelPath)}, vystup {nn.OutputWidth}x{nn.OutputHeight}, "
                              + $"kanaly {opts.ChannelOrder}");
            Console.WriteLine($"histogram: BackProject.RoadProbability (tatataz tabulka, kterou pouziva runtime)");
            Console.WriteLine($"prah pravdy: {truthThreshold} (maska je sjizdna od teto hodnoty)");
            Console.WriteLine();

            // Souctove matice = presnost vazena pixely, tedy metrika notebooku. Vedle toho se
            // drzi cisla PER SNIMEK, protoze prumer pres snimky schova, ze par snimku je uplne
            // mimo — a prave ty jsou zajimave.
            SegmentationConfusion sitCelkem = default, histCelkem = default, histPlneCelkem = default;
            var sitIoU = new List<double>();
            var histIoU = new List<double>();
            var sitPresnost = new List<double>();
            var histPresnost = new List<double>();
            var podilPravda = new List<double>();
            var podilSit = new List<double>();
            var podilHist = new List<double>();

            string nejhorsi = null;
            double nejhorsiIoU = double.PositiveInfinity;
            var rozliseni = new Dictionary<string, int>(StringComparer.Ordinal);

            Image<Gray> probNn = null, probHist = null, probHistMaly = null, pravdaMala = null;
            Image<BGR32> zmenseny = null;

            foreach (var (jmeno, cestaImg, cestaGt) in pary)
            {
                var rgb = NactiBgr(cestaImg);
                var pravda = NactiGray(cestaGt);
                if (pravda.Width != rgb.Width || pravda.Height != rgb.Height)
                {
                    Console.Error.WriteLine($"  {jmeno}: maska {pravda.Width}x{pravda.Height} "
                                            + $"nesouhlasi se snimkem {rgb.Width}x{rgb.Height} — preskoceno.");
                    continue;
                }
                string klic = $"{rgb.Width}x{rgb.Height}";
                rozliseni[klic] = rozliseni.TryGetValue(klic, out int n) ? n + 1 : 1;

                // --- sit: stejna cesta, jakou jde snimek v aplikaci (CameraFrameProcessor)
                var sizeNn = nn.Size(rgb.Width, rgb.Height);
                zmenseny = Ensure(zmenseny, sizeNn.Width, sizeNn.Height);
                Zmensi(rgb, zmenseny, resizeCenter);
                probNn = Ensure(probNn, sizeNn.Width, sizeNn.Height);
                nn.Process(zmenseny, probNn);

                // --- histogram v plnem rozliseni (jeho skutecny provozni bod)
                probHist = Ensure(probHist, rgb.Width, rgb.Height);
                hist.Process(rgb, probHist);

                // --- na rozmer site: prahuje se stejne, pravda se zmensuje nejblizsim sousedem
                probHistMaly = Ensure(probHistMaly, sizeNn.Width, sizeNn.Height);
                Zmensi(probHist, probHistMaly, resizeCenter);
                pravdaMala = Ensure(pravdaMala, sizeNn.Width, sizeNn.Height);
                Zmensi(pravda, pravdaMala, resizeCenter);

                int maly = probNn.DataLength;
                var cSit = SegmentationMetrics.Compare(probNn.Data, pravdaMala.Data, maly, truthThreshold: truthThreshold);
                var cHist = SegmentationMetrics.Compare(probHistMaly.Data, pravdaMala.Data, maly, truthThreshold: truthThreshold);
                var cHistPlne = SegmentationMetrics.Compare(probHist.Data, pravda.Data, probHist.DataLength, truthThreshold: truthThreshold);

                sitCelkem += cSit;
                histCelkem += cHist;
                histPlneCelkem += cHistPlne;
                if (!double.IsNaN(cSit.IoU)) sitIoU.Add(cSit.IoU);
                if (!double.IsNaN(cHist.IoU)) histIoU.Add(cHist.IoU);
                sitPresnost.Add(cSit.Accuracy);
                histPresnost.Add(cHist.Accuracy);
                podilPravda.Add(cSit.TruthFraction);
                podilSit.Add(cSit.PredictedFraction);
                podilHist.Add(cHist.PredictedFraction);

                if (!double.IsNaN(cSit.IoU) && cSit.IoU < nejhorsiIoU) { nejhorsiIoU = cSit.IoU; nejhorsi = jmeno; }

                Console.WriteLine($"  {jmeno,-28} pravda {100 * cSit.TruthFraction,5:F1} %  |  "
                                  + $"sit acc {100 * cSit.Accuracy,5:F1} % IoU {cSit.IoU,5:F3}  |  "
                                  + $"hist acc {100 * cHist.Accuracy,5:F1} % IoU {cHist.IoU,5:F3}");
            }

            if (sitPresnost.Count == 0) { Console.Error.WriteLine("Nezpracoval se ani jeden snimek."); return; }

            Console.WriteLine();
            Console.WriteLine($"rozliseni snimku: {string.Join(", ", rozliseni.Select(kv => $"{kv.Key} ({kv.Value}x)"))}");
            Console.WriteLine();
            Console.WriteLine($"=== NA ROZMERU SITE {nn.OutputWidth}x{nn.OutputHeight} "
                              + "(pravda zmensena nejblizsim sousedem — srovnatelne s notebookem)");
            VypisMatici("  sit      ", sitCelkem);
            VypisMatici("  histogram", histCelkem);
            Console.WriteLine();
            Console.WriteLine("  rozptyl mezi snimky (souctova cisla vyse schovavaji, ze par snimku je mimo):");
            Vypis("    sit  IoU      ", sitIoU);
            Vypis("    hist IoU      ", histIoU);
            Vypis("    sit  presnost ", sitPresnost);
            Vypis("    hist presnost ", histPresnost);
            Console.WriteLine();
            Console.WriteLine("  podil sjizdne plochy (kdyz se lisi od pravdy, lisi se i to, co uvidi occupancy grid):");
            Vypis("    pravda        ", podilPravda);
            Vypis("    sit           ", podilSit);
            Vypis("    histogram     ", podilHist);

            Console.WriteLine();
            Console.WriteLine("=== HISTOGRAM V PLNEM ROZLISENI (jeho skutecny provozni bod, NEsrovnatelne s cisly vyse)");
            VypisMatici("  histogram", histPlneCelkem);

            Console.WriteLine();
            Console.WriteLine($"nejhorsi snimek podle IoU site: {nejhorsi} (IoU {nejhorsiIoU:F3})");
            if (!string.IsNullOrWhiteSpace(png) && nejhorsi != null)
                UlozNejhorsi(png, dir, nejhorsi, nn, hist);
        }

        /// <summary>Matice na jeden radek plus metriky, ktere z ni plynou.</summary>
        private static void VypisMatici(string popis, SegmentationConfusion c)
        {
            Console.WriteLine($"{popis} presnost {100 * c.Accuracy,5:F2} %   IoU {c.IoU,5:F3}   "
                              + $"precision {c.Precision,5:F3}   recall {c.Recall,5:F3}");
            Console.WriteLine($"{new string(' ', popis.Length)}   cesta pridana kde neni (FP) "
                              + $"{100.0 * c.FalsePositive / c.Total,5:F2} %   "
                              + $"cesta zamlcena (FN) {100.0 * c.FalseNegative / c.Total,5:F2} %");
        }

        /// <summary>
        /// Ulozi vstup | pravdu | histogram | sit vedle sebe pro NEJHORSI snimek. Prumerne
        /// cislo neukaze, <i>jak</i> se metoda myli — a jak se myli je to, co se opravuje.
        /// </summary>
        private static void UlozNejhorsi(string prefix, string dir, string jmeno,
                                         OnnxBackProject nn, BackProject hist)
        {
            try
            {
                var par = Pary(dir).First(p => p.jmeno == jmeno);
                var rgb = NactiBgr(par.img);
                var pravda = NactiGray(par.gt);

                var size = nn.Size(rgb.Width, rgb.Height);
                var zmenseny = new Image<BGR32>(size.Width, size.Height);
                zmenseny.Resize(rgb);
                var probNn = new Image<Gray>(size.Width, size.Height);
                nn.Process(zmenseny, probNn);

                var probHist = new Image<Gray>(rgb.Width, rgb.Height);
                hist.Process(rgb, probHist);
                var probHistMaly = new Image<Gray>(size.Width, size.Height);
                probHistMaly.Resize(probHist);
                var pravdaMala = new Image<Gray>(size.Width, size.Height);
                pravdaMala.Resize(pravda);

                const int Mezera = 4;
                int w = size.Width, h = size.Height;
                var spolu = new Image<BGR32>(4 * w + 3 * Mezera, h);
                Vloz(spolu, zmenseny, 0);
                Vloz(spolu, pravdaMala, w + Mezera);
                Vloz(spolu, probHistMaly, 2 * (w + Mezera));
                Vloz(spolu, probNn, 3 * (w + Mezera));

                string cesta = $"{prefix}-{jmeno}.png";
                File.WriteAllBytes(cesta, ImageMsg.EncodePng(spolu));
                Console.WriteLine($"ulozeno {cesta}  (vstup | pravda | histogram | sit)");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"PNG se neulozilo: {ex.Message}");
            }
        }

        /// <summary>
        /// Zmenseni nejblizsim sousedem s volitelnym pulpixelovym posunem vzorku.
        ///
        /// <para><b>Nac to je:</b> <see cref="Image{T}.Resize"/> bere <c>floor(i·scale)</c>, tedy
        /// <b>roh</b> zdrojoveho bloku. <c>tf.image.resize(..., 'nearest')</c>, kterym se model
        /// TRENOVAL, ma ale ve vychozim stavu half-pixel centers, tedy <c>floor((i+0,5)·scale)</c>
        /// — <b>stred</b> bloku. Pri zmenseni 640→128 je to rozdil zdrojoveho pixelu 5i+2 proti 5i,
        /// tedy systematicky posun o pul bloku. Jestli to na vysledku zalezi, se neda odhadnout,
        /// jen zmerit — proto je to prepinac (<c>--resizecenter</c>), ne zmena chovani.</para>
        /// </summary>
        private static void Zmensi(Image src, Image dst, bool center)
        {
            int step = src.Step;
            double sx = (double)src.Width / dst.Width, sy = (double)src.Height / dst.Height;
            double off = center ? 0.5 : 0.0;
            var s = src.Data;
            var d = dst.Data;
            for (int y = 0; y < dst.Height; y++)
            {
                int syi = Math.Min((int)((y + off) * sy), src.Height - 1);
                for (int x = 0; x < dst.Width; x++)
                {
                    int sxi = Math.Min((int)((x + off) * sx), src.Width - 1);
                    int si = step * (sxi + syi * src.Width);
                    int di = step * (x + y * dst.Width);
                    for (int k = 0; k < step; k++) d[di + k] = s[si + k];
                }
            }
        }

        /// <summary>Pary snimek+maska podle shodneho jmena bez pripony.</summary>
        private static IEnumerable<(string jmeno, string img, string gt)> Pary(string dir)
        {
            string imgDir = Path.Combine(dir, "img");
            string gtDir = Path.Combine(dir, "gt");
            if (!Directory.Exists(imgDir) || !Directory.Exists(gtDir)) yield break;

            foreach (var f in Directory.EnumerateFiles(imgDir).OrderBy(x => x, StringComparer.Ordinal))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext != ".jpg" && ext != ".jpeg" && ext != ".png") continue;
                string jmeno = Path.GetFileNameWithoutExtension(f);
                string gt = Path.Combine(gtDir, jmeno + ".png");
                if (File.Exists(gt)) yield return (jmeno, f, gt);
            }
        }

        /// <summary>Nacte JPEG/PNG jako <see cref="BGR32"/> (SkiaSharp — tentyz dekoder jako <c>ImageMsg</c>).</summary>
        private static Image<BGR32> NactiBgr(string path)
        {
            using var dec = SKBitmap.Decode(path) ?? throw new Exception($"Nelze dekodovat {path}");
            using var conv = dec.Copy(SKColorType.Bgra8888) ?? throw new Exception($"Konverze pixelu selhala: {path}");
            var img = new Image<BGR32>(conv.Width, conv.Height);
            Kopiruj(conv, img.Data, 4);
            return img;
        }

        /// <summary>Nacte masku jako <see cref="Gray"/>; barevna maska se prevede na sedou.</summary>
        private static Image<Gray> NactiGray(string path)
        {
            using var dec = SKBitmap.Decode(path) ?? throw new Exception($"Nelze dekodovat {path}");
            using var conv = dec.Copy(SKColorType.Gray8) ?? throw new Exception($"Konverze pixelu selhala: {path}");
            var img = new Image<Gray>(conv.Width, conv.Height);
            Kopiruj(conv, img.Data, 1);
            return img;
        }

        /// <summary>Zkopiruje pixely z SKBitmap do pole a osetri padding radku.</summary>
        private static void Kopiruj(SKBitmap bmp, byte[] dst, int step)
        {
            int rowBytes = bmp.RowBytes;
            int tight = bmp.Width * step;
            IntPtr ptr = bmp.GetPixels();
            if (rowBytes == tight)
                Marshal.Copy(ptr, dst, 0, Math.Min(dst.Length, tight * bmp.Height));
            else
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(IntPtr.Add(ptr, y * rowBytes), dst, y * tight, tight);
        }
    }
}
