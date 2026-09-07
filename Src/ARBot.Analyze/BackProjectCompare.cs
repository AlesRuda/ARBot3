using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Vision.Nn;
using SkiaSharp;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Jak to vlastně vypadá?</b> Srovnávací mřížka: řádek = snímek ze záznamu, sloupce =
    /// vstup, histogram a libovolný počet modelů vedle sebe.
    ///
    /// <para><b>Nač to je:</b> čísla (přesnost, IoU) řeknou, <i>o kolik</i> se metody liší, ale ne
    /// <b>čím</b> — že histogram zrní na asfaltu a rozpadá se mu hranice trávy, je vidět až na
    /// obrázku. Vzniklo pro devlog, ale hodí se pokaždé, když má někdo posoudit nový model.</para>
    ///
    /// <para><b>Snímky se berou rozprostřené po celém záznamu</b>, ne první čtyři za sebou — ty
    /// jsou při 30 sn/s prakticky totožné a ukázka by neukázala nic.</para>
    ///
    /// <para>Panely jsou v rozlišení SÍTĚ (128×128, tedy i s deformací poměru stran), protože
    /// přesně tohle model vidí; zvětšují se nejbližším sousedem, aby nevznikl dojem hladkosti,
    /// který v datech není.</para>
    /// </summary>
    public static class BackProjectCompare
    {
        private const int Zvetseni = 2;      // 128 -> 256 px na panel
        private const int Mezera = 6;
        private const int Popisek = 22;      // pruh na nadpis sloupce

        /// <param name="rec">Otevřený záznam.</param>
        /// <param name="modely">Cesty k modelům (`.onnx` / `.rknn`), oddělené čárkou.</param>
        /// <param name="pocet">Kolik snímků do mřížky.</param>
        /// <param name="kamera">Jen snímky této kamery; <c>null</c> = první, na kterou se narazí.</param>
        /// <param name="png">Cesta k výslednému PNG.</param>
        /// <param name="bgr">Pořadí kanálů BGR místo výchozího RGB.</param>
        public static void Run(RecordFile rec, string modely, int pocet, string kamera, string png, bool bgr)
        {
            if (string.IsNullOrWhiteSpace(png))
            {
                Console.Error.WriteLine("Chybi --png=<cesta k vystupnimu obrazku>.");
                return;
            }
            var cesty = (modely ?? string.Empty).Split(',')
                                                .Select(x => x.Trim())
                                                .Where(x => x.Length > 0)
                                                .ToList();
            if (cesty.Count == 0)
            {
                Console.Error.WriteLine("Chybi --compare=<model1.onnx,model2.rknn>.");
                return;
            }
            foreach (var c in cesty.Where(c => !File.Exists(c)))
            {
                Console.Error.WriteLine($"Model neexistuje: {c}");
                return;
            }

            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            if (entries.Count == 0) { Console.Error.WriteLine("V zaznamu nejsou zadne snimky."); return; }

            var channels = bgr ? NnChannelOrder.Bgr : NnChannelOrder.Rgb;
            var site = new List<INnBackProject>();
            try
            {
                foreach (var c in cesty) site.Add(NnBackProject.Open(c, channels));
                var hist = new BackProject(BackProject.RoadProbability);

                var snimky = Vyber(rec, entries, pocet, ref kamera);
                if (snimky.Count == 0) { Console.Error.WriteLine("Zadny pouzitelny snimek s RGB."); return; }

                Console.WriteLine($"kamera: {kamera}, snimku: {snimky.Count}, "
                                  + $"sloupce: vstup, histogram, {string.Join(", ", cesty.Select(Path.GetFileNameWithoutExtension))}");

                var nadpisy = new List<string> { "vstup (co vidi sit)", "histogram barev" };
                nadpisy.AddRange(cesty.Select(Path.GetFileNameWithoutExtension));
                Kresli(snimky, hist, site, nadpisy, png);
                Console.WriteLine($"ulozeno {png}");
            }
            finally
            {
                foreach (var s in site) s.Dispose();
            }
        }

        /// <summary>Snímky rovnoměrně po celém záznamu, všechny z jedné kamery.</summary>
        private static List<Image<BGR32>> Vyber(RecordFile rec, List<ARBot.Common.Communication.IndexEntry> entries,
                                                int pocet, ref string kamera)
        {
            // Filtrovat az po precteni snimku by rozhodilo rozestup (pulka zaznamu je z druhe
            // kamery), takze se kamera vybere UZ V INDEXU - jmeno tam je.
            if (kamera == null) kamera = entries.Select(e => e.Name).FirstOrDefault(n => !string.IsNullOrEmpty(n));
            string jmeno = kamera;      // ref parametr nejde pouzit v lambde
            var moje = entries.Where(e => string.Equals(e.Name, jmeno, StringComparison.Ordinal)).ToList();
            if (moje.Count == 0) moje = entries;

            var vybrane = new List<Image<BGR32>>();
            int krok = Math.Max(1, moje.Count / (pocet + 1));
            for (int i = krok; i < moje.Count && vybrane.Count < pocet; i += krok)
            {
                if (!(rec.Read(moje[i]) is CameraFrame f) || f.ImageRGB == null) continue;
                vybrane.Add(f.ImageRGB);
            }
            return vybrane;
        }

        private static void Kresli(List<Image<BGR32>> snimky, BackProject hist,
                                   List<INnBackProject> site, List<string> nadpisy, string png)
        {
            // Rozmer panelu urcuje prvni sit; histogram i vstup se do nej zmensi, aby slo
            // srovnavat "co ze stejneho mista rekla ktera metoda".
            var size = site[0].Size(0, 0);
            int w = size.Width * Zvetseni, h = size.Height * Zvetseni;
            int sloupcu = 2 + site.Count;
            int sirka = sloupcu * w + (sloupcu - 1) * Mezera;
            int vyska = Popisek + snimky.Count * h + (snimky.Count - 1) * Mezera;

            using var bmp = new SKBitmap(new SKImageInfo(sirka, vyska, SKColorType.Bgra8888, SKAlphaType.Opaque));
            using var canvas = new SKCanvas(bmp);
            canvas.Clear(new SKColor(24, 24, 24));

            using var textPaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var font = new SKFont(SKTypeface.Default, 13);

            for (int c = 0; c < sloupcu; c++)
            {
                float x = c * (w + Mezera);
                canvas.DrawText(nadpisy[c], x + 2, Popisek - 7, SKTextAlign.Left, font, textPaint);
            }

            var zmenseny = new Image<BGR32>(size.Width, size.Height);
            var prob = new Image<Gray>(size.Width, size.Height);
            var probHistMaly = new Image<Gray>(size.Width, size.Height);

            for (int r = 0; r < snimky.Count; r++)
            {
                var rgb = snimky[r];
                float y = Popisek + r * (h + Mezera);
                zmenseny.Resize(rgb);

                Vloz(canvas, zmenseny, 0, y, w, h);

                var probHist = new Image<Gray>(rgb.Width, rgb.Height);
                hist.Process(rgb, probHist);
                probHistMaly.Resize(probHist);
                Vloz(canvas, probHistMaly, w + Mezera, y, w, h);

                for (int m = 0; m < site.Count; m++)
                {
                    site[m].Process(zmenseny, prob);
                    Vloz(canvas, prob, (2 + m) * (w + Mezera), y, w, h);
                }
            }

            using var img = SKImage.FromBitmap(bmp);
            using var data = img.Encode(SKEncodedImageFormat.Png, 90);
            var dir = Path.GetDirectoryName(Path.GetFullPath(png));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = File.Create(png);
            data.SaveTo(fs);
        }

        /// <summary>Vykresli obraz do platna; zvetsuje se NEJBLIZSIM SOUSEDEM, aby nevznikl
        /// dojem hladkosti, ktery v datech neni.</summary>
        private static void Vloz(SKCanvas canvas, Image zdroj, float x, float y, int w, int h)
        {
            using var bmp = new SKBitmap(new SKImageInfo(zdroj.Width, zdroj.Height,
                                                         SKColorType.Bgra8888, SKAlphaType.Opaque));
            var src = zdroj.Data;
            int step = zdroj.Step;
            // Pixel po pixelu: jde o par obrazku do dokumentace, ne o horkou smycku.
            for (int y0 = 0; y0 < zdroj.Height; y0++)
                for (int x0 = 0; x0 < zdroj.Width; x0++)
                {
                    int si = (x0 + y0 * zdroj.Width) * step;
                    byte b = src[si];
                    byte g = step >= 3 ? src[si + 1] : b;
                    byte rr = step >= 3 ? src[si + 2] : b;
                    bmp.SetPixel(x0, y0, new SKColor(rr, g, b));
                }
            using var image = SKImage.FromBitmap(bmp);
            canvas.DrawImage(image, new SKRect(x, y, x + w, y + h),
                             new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None));
        }
    }
}
