using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ARBot.Diagnostics
{
    /// <summary>
    /// Volitelné použití ffmpeg pro self-test video (komprimovaný GIF / mp4). ffmpeg NENÍ závislost -
    /// hledá se za běhu (PATH, známá místa jako Shotcut, winget, nebo override); když není, self-test
    /// spadne zpět na vestavěný <see cref="GifWriter"/>. Viz doc/selftest.md.
    /// </summary>
    public static class Ffmpeg
    {
        /// <summary>Najde ffmpeg.exe (override → env ARBOT_FFMPEG → PATH → známá místa). null = není.</summary>
        public static string Find(string overridePath = null)
        {
            var cands = new List<string>();
            if (!string.IsNullOrWhiteSpace(overridePath)) cands.Add(overridePath);
            var env = Environment.GetEnvironmentVariable("ARBOT_FFMPEG");
            if (!string.IsNullOrWhiteSpace(env)) cands.Add(env);

            // PATH
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator))
                if (!string.IsNullOrWhiteSpace(dir)) cands.Add(Path.Combine(dir.Trim(), "ffmpeg.exe"));

            // Známá místa (Shotcut přibaluje ffmpeg; winget Links; typické instalace).
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            cands.Add(Path.Combine(pf, "Shotcut", "ffmpeg.exe"));
            cands.Add(Path.Combine(pf86, "Shotcut", "ffmpeg.exe"));
            cands.Add(Path.Combine(local, "Microsoft", "WinGet", "Links", "ffmpeg.exe"));
            cands.Add(@"C:\ffmpeg\bin\ffmpeg.exe");

            foreach (var c in cands)
                try { if (File.Exists(c)) return c; } catch { }
            return null;
        }

        /// <summary>Spustí ffmpeg s danými argumenty; vrací true při exit code 0. stderr jde do Debug.</summary>
        public static bool Run(string ffmpegExe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegExe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                string err = p.StandardError.ReadToEnd();
                p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0)
                    System.Diagnostics.Debug.WriteLine($"ffmpeg exit {p.ExitCode}: {Tail(err)}");
                return p.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Ffmpeg.Run: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Zakóduje sérii PNG snímků (<paramref name="pattern"/> = např. <c>f_%05d.png</c>) na komprimovaný
        /// GIF (dvouprůchodově přes palettegen) o šířce <paramref name="width"/> px. Vrací true při úspěchu.
        /// </summary>
        public static bool EncodeGif(string exe, string framesDir, string pattern, int fps, int width, string outPath)
        {
            string input = Path.Combine(framesDir, pattern);
            string palette = Path.Combine(framesDir, "palette.png");
            string scale = $"scale={width}:-2:flags=lanczos";

            bool p1 = Run(exe, $"-y -framerate {fps} -i \"{input}\" -vf \"{scale},palettegen=stats_mode=diff\" \"{palette}\"");
            if (!p1) return false;
            return Run(exe, $"-y -framerate {fps} -i \"{input}\" -i \"{palette}\" " +
                            $"-lavfi \"{scale}[x];[x][1:v]paletteuse=dither=bayer:bayer_scale=3\" \"{outPath}\"");
        }

        /// <summary>Zakóduje sérii PNG snímků na H.264 mp4 (yuv420p, sudé rozměry) o šířce <paramref name="width"/>.</summary>
        public static bool EncodeMp4(string exe, string framesDir, string pattern, int fps, int width, string outPath)
        {
            string input = Path.Combine(framesDir, pattern);
            return Run(exe, $"-y -framerate {fps} -i \"{input}\" -c:v libx264 -pix_fmt yuv420p -crf 23 " +
                            $"-vf \"scale={width}:-2:flags=lanczos\" -movflags +faststart \"{outPath}\"");
        }

        private static string Tail(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 300 ? s.Substring(s.Length - 300) : s;
        }
    }
}
