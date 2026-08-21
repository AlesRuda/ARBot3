using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ARBot.Diagnostics
{
    /// <summary>
    /// Konfigurace automatického self-testu (bezobslužný běh pro měření výkonu/latence). Umožňuje
    /// spustit aplikaci s parametrem <c>selftest=true</c>, která sama otevře požadovaná okna, pustí Run,
    /// po zadaný čas nechá běžet, zastaví, spočte souhrn z diagnostického CSV a ukončí se. Slouží
    /// k reprodukovatelnému A/B měření variant (záznam on/off, otevřená okna, ...) bez ruční obsluhy.
    ///
    /// <para>Parametry (přes <see cref="Program.GetParam"/>, tvar <c>klíč=hodnota</c>):
    /// <list type="bullet">
    /// <item><c>selftest=true</c> — zapne self-test (jinak se nespustí).</item>
    /// <item><c>st_seconds=30</c> — jak dlouho nechat Run běžet.</item>
    /// <item><c>st_record=false</c> — Run se záznamem (<c>true</c>) nebo bez (<c>false</c>).</item>
    /// <item><c>st_images=false</c> — otevřít okno Images (obrazové vrstvy).</item>
    /// <item><c>st_robot=true</c> — otevřít okno Robot-centric.</item>
    /// <item><c>st_world=false</c> — otevřít okno World (mapové vrstvy) a nechat ho aktivní.</item>
    /// <item><c>st_name=baseline</c> — štítek varianty (jen do souhrnu/názvu výstupu).</item>
    /// <item><c>st_out=</c> — cesta k souboru souhrnu (default <c>logs/selftest-result.txt</c>).</item>
    /// <item><c>no_uart=true</c> — přeskočit UART senzory (IMU/GPS/motor) - čte <see cref="Robot.ARBotHW"/>.</item>
    /// </list></para>
    /// </summary>
    public sealed class SelfTestConfig
    {
        public bool Enabled;
        public string Name = "baseline";
        public int Seconds = 30;
        public bool Record;
        public bool OpenImages;
        public bool ImagesActive;   // st_images_active: aktivovat (zviditelnit) tab Images (jinak je na pozadi)
        public bool OpenRobotCentric = true;

        /// <summary>
        /// <c>st_world=true</c> — otevřít okno World (a udělat z něj aktivní tab, aby ho zachytil
        /// <c>st_shot</c>). Slouží k bezobslužnému ověření mapových vrstev — mj. vrstvy „Mapa (vize)"
        /// z parametru <c>visionmap=</c>. Viz doc/virtual-hw.md.
        /// </summary>
        public bool OpenWorld;

        public string OutPath;

        public bool Shot;           // st_shot: pořídit screenshot hlavního okna na konci běhu
        public bool Video;          // st_video: nahrát krátké video (animovaný GIF) během běhu
        public double VideoSeconds = 5;  // st_video_seconds
        public double VideoFps = 8;      // st_video_fps
        public int VideoScale = 3;       // st_video_scale: zmenšení (nekomprimovaný GIF je velký -> menší = menší soubor)
        public string VideoFormat;       // st_video_format: "gif" | "mp4" | null(auto: ffmpeg gif, jinak vestavěný gif)
        public string FfmpegPath;        // ffmpeg=<cesta>: override umístění ffmpeg

        /// <summary>Sestaví konfiguraci z parametrů příkazové řádky.</summary>
        public static SelfTestConfig FromArgs()
        {
            var enabled = Program.GetParamBool("selftest", false);
            var cfg = new SelfTestConfig
            {
                Enabled = enabled,
                Name = Program.GetParam("st_name", "baseline"),
                Seconds = (int)Program.GetParamDouble("st_seconds", 30),
                Record = Program.GetParamBool("st_record", false),
                OpenImages = Program.GetParamBool("st_images", false),
                ImagesActive = Program.GetParamBool("st_images_active", false),
                OpenRobotCentric = Program.GetParamBool("st_robot", true),
                OpenWorld = Program.GetParamBool("st_world", false),
                OutPath = Program.GetParam("st_out", null),
                Shot = Program.GetParamBool("st_shot", false),
                Video = Program.GetParamBool("st_video", false),
                VideoSeconds = Program.GetParamDouble("st_video_seconds", 5),
                VideoFps = Program.GetParamDouble("st_video_fps", 8),
                VideoScale = (int)Program.GetParamDouble("st_video_scale", 3),
                VideoFormat = Program.GetParam("st_video_format", null),
                FfmpegPath = Program.GetParam("ffmpeg", null),
            };
            if (cfg.Seconds < 1) cfg.Seconds = 1;
            return cfg;
        }
    }

    /// <summary>Pomocné funkce self-testu: souhrn z diagnostického CSV (<c>logs/traversability-timing-*.csv</c>).</summary>
    public static class SelfTest
    {
        /// <summary>Složka <c>logs/</c> v kořenu repa (fallback build output) - shodně s runtime diagnostikou.</summary>
        public static string LogsDir()
        {
            try
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    string git = Path.Combine(dir.FullName, ".git");
                    if (Directory.Exists(git) || File.Exists(git)) break;
                    dir = dir.Parent;
                }
                string root = dir?.FullName ?? AppContext.BaseDirectory;
                return Path.Combine(root, "logs");
            }
            catch { return Path.Combine(AppContext.BaseDirectory, "logs"); }
        }

        /// <summary>Složka <c>doc/media/</c> v kořenu repa - pro screenshoty/videa do deníčku.</summary>
        public static string MediaDir()
        {
            var logs = LogsDir();                       // .../logs
            var root = Path.GetDirectoryName(logs);     // kořen repa
            return Path.Combine(root ?? AppContext.BaseDirectory, "doc", "media");
        }

        /// <summary>
        /// Spočte souhrn ze všech <c>traversability-timing-*.csv</c> a zapíše ho do souboru souhrnu
        /// (a vrátí jako text). Warmup (seq 0) se přeskakuje. Odolné vůči starému headeru bez GC sloupců.
        /// </summary>
        public static string WriteSummary(SelfTestConfig cfg)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"# self-test '{cfg.Name}'  seconds={cfg.Seconds}  record={cfg.Record}  " +
                          $"images={cfg.OpenImages}  robot={cfg.OpenRobotCentric}");

            string logs = LogsDir();
            string[] files;
            try { files = Directory.GetFiles(logs, "traversability-timing-*.csv"); }
            catch { files = Array.Empty<string>(); }

            if (files.Length == 0)
                sb.AppendLine("(zadny traversability-timing CSV nenalezen - kamera nedodala snimky?)");

            foreach (var f in files.OrderBy(x => x))
            {
                try { sb.AppendLine(SummarizeFile(f)); }
                catch (Exception ex) { sb.AppendLine($"{Path.GetFileName(f)}: chyba souhrnu: {ex.Message}"); }
            }

            // UI aktivita (ověří, zda okna reálně churnují - ne jen analýza).
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "UI: robotCentricRenders={0}  imageFramesIngested={1}  writeableBitmapsCreated={2}",
                ARBot.Views.Controls.RobotCentricControl.DiagRenders,
                ViewModels.ImageDocument.DiagFramesIngested,
                ViewModels.ImageDocument.DiagBitmapsCreated));

            string outPath = string.IsNullOrEmpty(cfg.OutPath)
                ? Path.Combine(logs, "selftest-result.txt")
                : cfg.OutPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllText(outPath, sb.ToString());
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SelfTest: zapis souhrnu selhal: {ex.Message}"); }

            System.Diagnostics.Debug.WriteLine(sb.ToString());
            return sb.ToString();
        }

        private static string SummarizeFile(string path)
        {
            var compute = new List<double>();
            double waitSum = 0; long camAllocSum = 0; int camAllocN = 0; int gen2Sum = 0; int rows = 0;

            using (var r = new StreamReader(path))
            {
                string line = r.ReadLine();   // header
                var header = (line ?? string.Empty).Split(';');
                int iWait = Array.IndexOf(header, "wait_ms");
                int iComp = Array.IndexOf(header, "compute_ms");
                int iCam = Array.IndexOf(header, "cam_alloc_kb");
                int iGen2 = Array.IndexOf(header, "gen2");
                if (iWait < 0) iWait = 3;
                if (iComp < 0) iComp = 4;

                while ((line = r.ReadLine()) != null)
                {
                    var c = line.Split(';');
                    if (c.Length <= iComp) continue;
                    if (int.TryParse(c[0], out int seq) && seq == 0) continue;   // warmup

                    if (TryD(c, iComp, out double comp)) compute.Add(comp);
                    if (TryD(c, iWait, out double w)) waitSum += w;
                    if (iCam >= 0 && TryD(c, iCam, out double cam)) { camAllocSum += (long)cam; camAllocN++; }
                    if (iGen2 >= 0 && c.Length > iGen2 && int.TryParse(c[iGen2], out int g2)) gen2Sum += g2;
                    rows++;
                }
            }

            if (compute.Count == 0)
                return $"{Path.GetFileName(path)}: 0 snimku (mimo warmup)";

            compute.Sort();
            int n = compute.Count;
            double avg = compute.Average();
            double max = compute[n - 1];
            double p50 = Percentile(compute, 0.50);
            double p95 = Percentile(compute, 0.95);
            int over100 = compute.Count(x => x > 100);
            string camStr = camAllocN > 0 ? $"{camAllocSum / camAllocN} KB" : "n/a";

            return string.Format(CultureInfo.InvariantCulture,
                "{0}: frames={1} compute avg={2:F1} p50={3:F1} p95={4:F1} max={5:F1} ms | " +
                ">100ms={6} ({7:F2}%) | gen2={8} | wait_avg={9:F1} ms | cam_alloc_avg={10}",
                Path.GetFileName(path), n, avg, p50, p95, max, over100, 100.0 * over100 / n, gen2Sum,
                waitSum / n, camStr);
        }

        private static bool TryD(string[] c, int i, out double v)
        {
            v = 0;
            return i >= 0 && i < c.Length && double.TryParse(c[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }

        private static double Percentile(List<double> sorted, double p)
        {
            if (sorted.Count == 0) return 0;
            int idx = (int)Math.Ceiling(p * sorted.Count) - 1;
            if (idx < 0) idx = 0;
            if (idx >= sorted.Count) idx = sorted.Count - 1;
            return sorted[idx];
        }
    }
}
