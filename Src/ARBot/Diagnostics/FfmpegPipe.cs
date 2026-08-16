using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace ARBot.Diagnostics
{
    /// <summary>
    /// Běžící ffmpeg proces, do jehož stdin se streamují surové snímky (BGRA) - pro interaktivní
    /// videozáznam obrazovky z toolbaru (viz doc/screen-capture.md).
    ///
    /// Proti dávkovému <see cref="Ffmpeg.EncodeMp4"/> / <see cref="Ffmpeg.EncodeGif"/> (self-test) tu
    /// odpadá ukládání každého snímku jako PNG do dočasné složky: záznam může běžet libovolně dlouho
    /// a na UI vlákně zbyde jen kopie pixelů. Zápis do roury dělá vlastní vlákno přes frontu s pevnou
    /// kapacitou - když ffmpeg nestíhá, snímek se zahodí (počítá se do <see cref="DroppedFrames"/>),
    /// aby se nikdy nezablokovalo UI. Vyprázdněné buffery se vracejí přes <c>recycle</c> zpět volajícímu
    /// k dalšímu použití (bez recyklace by šlo o megabajty alokací na snímek).
    ///
    /// ffmpeg NENÍ závislost projektu - hledá se za běhu (<see cref="Ffmpeg.Find"/>).
    /// </summary>
    public sealed class FfmpegPipe : IDisposable
    {
        /// <summary>Kolik snímků smí čekat ve frontě na zápis do roury, než se začnou zahazovat.</summary>
        private const int QueueCapacity = 8;

        private readonly Process _proc;
        private readonly Stream _stdin;
        private readonly BlockingCollection<byte[]> _queue;
        private readonly Thread _writer;
        private readonly Action<byte[]> _recycle;
        private readonly StringBuilder _err = new();
        private int _dropped;
        private int _written;
        private volatile bool _failed;

        /// <summary>Počet snímků zahozených proto, že ffmpeg nestíhal (fronta plná).</summary>
        public int DroppedFrames => Volatile.Read(ref _dropped);
        /// <summary>Počet snímků skutečně zapsaných do roury.</summary>
        public int WrittenFrames => Volatile.Read(ref _written);
        /// <summary>Roura se rozpadla (ffmpeg spadl / zavřel stdin) - další snímky nemá smysl posílat.</summary>
        public bool Failed => _failed;
        /// <summary>Konec chybového výstupu ffmpegu (pro hlášku uživateli).</summary>
        public string ErrorTail
        {
            get { lock (_err) { var s = _err.ToString(); return s.Length > 300 ? s.Substring(s.Length - 300) : s; } }
        }

        private FfmpegPipe(Process proc, Action<byte[]> recycle)
        {
            _proc = proc;
            _stdin = proc.StandardInput.BaseStream;
            _recycle = recycle;
            _queue = new BlockingCollection<byte[]>(QueueCapacity);
            _writer = new Thread(WriteLoop) { IsBackground = true, Name = "ffmpeg-pipe" };
            _writer.Start();
        }

        /// <summary>
        /// Spustí ffmpeg čtoucí surové BGRA snímky <paramref name="width"/>x<paramref name="height"/>
        /// ze stdin a kódující je do <paramref name="outPath"/>. <paramref name="format"/> = <c>mp4</c>
        /// (H.264) nebo <c>gif</c> (paletizovaný). <paramref name="outWidth"/> = cílová šířka videa
        /// (zmenšení dělá ffmpeg lanczosem; 0 nebo shodná se zdrojem = bez škálování).
        /// Vrací null a vyplní <paramref name="error"/>, když se proces nepodařilo spustit.
        /// </summary>
        public static FfmpegPipe Start(string exe, int width, int height, int fps, string outPath,
                                       string format, int outWidth, Action<byte[]> recycle, out string error)
        {
            error = null;
            try
            {
                var dir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = BuildArgs(width, height, fps, outPath, format, outWidth),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                };
                var proc = Process.Start(psi);
                if (proc == null) { error = "ffmpeg se nepodařilo spustit."; return null; }

                var pipe = new FfmpegPipe(proc, recycle);
                proc.ErrorDataReceived += (_, e) => pipe.AppendErr(e.Data);
                proc.OutputDataReceived += (_, __) => { };
                proc.BeginErrorReadLine();
                proc.BeginOutputReadLine();
                return pipe;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }

        /// <summary>Sestaví argumenty ffmpegu pro čtení surového videa ze stdin.</summary>
        private static string BuildArgs(int width, int height, int fps, string outPath, string format, int outWidth)
        {
            string input = $"-hide_banner -loglevel error -y -f rawvideo -pixel_format bgra " +
                           $"-video_size {width}x{height} -framerate {fps} -i - -an";

            // Zmenšení má smysl jen když je cíl jiný než zdroj; -2 dopočítá sudou výšku (yuv420p ji vyžaduje).
            bool scaling = outWidth > 0 && outWidth != width;
            string scale = scaling ? $"scale={outWidth}:-2:flags=lanczos" : null;

            if (string.Equals(format, "gif", StringComparison.OrdinalIgnoreCase))
            {
                // Paletizace ve dvou větvích jednoho průchodu: palettegen spočte paletu, paletteuse ji
                // aplikuje. Pozor: palettegen potřebuje celý stream, takže si ffmpeg snímky drží v paměti
                // - proto je délka GIF záznamu shora omezena (viz ScreenRecorder).
                string vf = (scale != null ? scale + "," : "") +
                            "split[a][b];[a]palettegen=stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=3";
                return $"{input} -vf \"{vf}\" -loop 0 \"{outPath}\"";
            }

            string mp4Vf = scale != null ? $"-vf \"{scale}\" " : "";
            return $"{input} -c:v libx264 -preset veryfast -pix_fmt yuv420p -crf 23 " +
                   $"{mp4Vf}-movflags +faststart \"{outPath}\"";
        }

        /// <summary>
        /// Zařadí snímek (BGRA, width*height*4) k zápisu. Nikdy neblokuje: když je fronta plná nebo je
        /// roura po smrti, snímek zahodí (a buffer rovnou vrátí k recyklaci) a vrátí false.
        /// </summary>
        public bool WriteFrame(byte[] bgra)
        {
            if (bgra == null) return false;
            if (!_failed)
            {
                try
                {
                    if (_queue.TryAdd(bgra)) return true;
                }
                catch (InvalidOperationException) { /* už se dokončuje */ }
            }
            Interlocked.Increment(ref _dropped);
            _recycle?.Invoke(bgra);
            return false;
        }

        private void WriteLoop()
        {
            foreach (var buf in _queue.GetConsumingEnumerable())
            {
                if (!_failed)
                {
                    try { _stdin.Write(buf, 0, buf.Length); Interlocked.Increment(ref _written); }
                    catch (Exception ex)
                    {
                        _failed = true;
                        AppendErr("zápis do roury selhal: " + ex.Message);
                    }
                }
                _recycle?.Invoke(buf);   // recyklovat i zahozené - jinak by se buffery ztrácely
            }
        }

        /// <summary>
        /// Dopíše frontu, zavře stdin a počká na dokončení kódování. Vrací true při čistém konci
        /// ffmpegu (exit 0). Blokuje - volat z pracovního vlákna, ne z UI.
        /// </summary>
        public bool Finish(int timeoutMs = 60000)
        {
            try { _queue.CompleteAdding(); } catch { }
            try { _writer.Join(timeoutMs); } catch { }
            try { _stdin.Flush(); _stdin.Close(); } catch { }

            try
            {
                if (!_proc.WaitForExit(timeoutMs))
                {
                    AppendErr("ffmpeg nedoběhl v limitu - ukončuji.");
                    try { _proc.Kill(); } catch { }
                    return false;
                }
                _proc.WaitForExit();   // dobere asynchronní čtení stderr
                if (_proc.ExitCode != 0) AppendErr($"exit {_proc.ExitCode}");
                return _proc.ExitCode == 0 && !_failed;
            }
            catch (Exception ex)
            {
                AppendErr(ex.Message);
                return false;
            }
        }

        private void AppendErr(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            lock (_err)
            {
                if (_err.Length > 4000) _err.Clear();
                _err.Append(line).Append(' ');
            }
        }

        public void Dispose()
        {
            try { _queue.Dispose(); } catch { }
            try { _proc.Dispose(); } catch { }
        }
    }
}
