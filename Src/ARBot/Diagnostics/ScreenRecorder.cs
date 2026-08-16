using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace ARBot.Diagnostics
{
    /// <summary>Výsledek dokončeného záznamu (pro hlášku v toolbaru).</summary>
    public sealed class RecordingResult
    {
        /// <summary>Podařilo se video zapsat?</summary>
        public bool Ok;
        /// <summary>Cesta k výslednému souboru.</summary>
        public string Path;
        /// <summary>Hláška pro uživatele (úspěch i důvod selhání).</summary>
        public string Message;
    }

    /// <summary>
    /// Interaktivní záznam obrazu hlavního okna do mp4 / GIF (tlačítka v toolbaru, viz
    /// doc/screen-capture.md). Snímkuje časovačem na UI vlákně (jinak nelze - Avalonia vizuál se
    /// smí renderovat jen odtud) a hned je posílá dál:
    ///
    /// - je-li k dispozici ffmpeg, teče surové BGRA přes <see cref="FfmpegPipe"/> rovnou do kodéru
    ///   (žádné mezisoubory, konstantní paměť, buffery se recyklují);
    /// - bez ffmpegu zbývá vestavěný <see cref="GifWriter"/>, který si ale musí držet všechny snímky
    ///   v paměti - proto jen zmenšené a s tvrdým stropem počtu snímků.
    ///
    /// Rozměr snímku se zafixuje při startu (a zarovná na sudý kvůli yuv420p) - kodér neumí měnit
    /// rozměr za běhu. Když se okno během záznamu zvětší, video zůstane na původním výřezu.
    /// </summary>
    public sealed class ScreenRecorder
    {
        // Výchozí parametry záznamu. mp4 je levné (H.264), GIF drahý na paměť i velikost -> nižší fps,
        // menší šířka a kratší strop.
        private const int Mp4Fps = 15, Mp4MaxWidth = 1280;
        private const double Mp4MaxSeconds = 600;
        private const int GifFps = 8, GifMaxWidth = 1280;
        private const double GifMaxSeconds = 60;
        /// <summary>Strop pro fallback bez ffmpegu - snímky se drží v paměti (300 @ 8 fps ≈ 37 s).</summary>
        private const int MemMaxFrames = 300;
        /// <summary>Kolik bufferů snímků držet v poolu k recyklaci.</summary>
        private const int PoolLimit = 8;

        private DispatcherTimer _timer;
        private RenderTargetBitmap _rtb;
        private FfmpegPipe _pipe;
        private Visual _visual;
        private ConcurrentBag<byte[]> _pool;
        private List<byte[]> _memFrames;          // fallback: RGB snímky v paměti
        private int _memW, _memH, _memScale;
        private Stopwatch _clock;
        private int _w, _h;                       // zafixovaný rozměr snímku (px, sudý)
        private int _fps, _delayMs;
        private double _maxSeconds;
        private string _path;
        private bool _autoStopFired;

        /// <summary>Běží záznam?</summary>
        public bool IsRecording { get; private set; }
        /// <summary>Formát běžícího (nebo posledního) záznamu: <c>mp4</c> / <c>gif</c>.</summary>
        public string Format { get; private set; }
        /// <summary>Kóduje se přes ffmpeg (jinak vestavěný GIF).</summary>
        public bool UsesFfmpeg { get; private set; }
        /// <summary>Počet zachycených snímků.</summary>
        public int FrameCount { get; private set; }
        /// <summary>Snímky zahozené kvůli nestíhajícímu kodéru.</summary>
        public int DroppedFrames => _pipe?.DroppedFrames ?? 0;
        /// <summary>Délka běžícího záznamu.</summary>
        public TimeSpan Elapsed => _clock?.Elapsed ?? TimeSpan.Zero;
        /// <summary>Zbývající čas do automatického zastavení.</summary>
        public TimeSpan Remaining
            => IsRecording ? TimeSpan.FromSeconds(Math.Max(0, _maxSeconds - Elapsed.TotalSeconds)) : TimeSpan.Zero;

        /// <summary>
        /// Záznam dosáhl limitu (nebo se rozpadla roura) a je nutné ho zastavit. Vyvolá se na UI vlákně;
        /// odběratel má zavolat <see cref="StopAsync"/> (sám se recorder nezastaví, aby ViewModel mohl
        /// zároveň dorovnat stav tlačítek).
        /// </summary>
        public event Action AutoStopRequested;

        /// <summary>
        /// Spustí záznam vizuálu <paramref name="visual"/> (typicky hlavní okno) do souboru
        /// <paramref name="path"/>. <paramref name="format"/> = <c>mp4</c> / <c>gif</c>.
        /// Vrací false a vyplní <paramref name="error"/>, když start selhal.
        /// MUSÍ se volat z UI vlákna.
        /// </summary>
        public bool Start(Visual visual, string format, string path, out string error)
        {
            error = null;
            if (IsRecording) { error = "Záznam už běží."; return false; }
            if (visual == null) { error = "Není co snímat (okno není k dispozici)."; return false; }

            bool gif = string.Equals(format, "gif", StringComparison.OrdinalIgnoreCase);
            Format = gif ? "gif" : "mp4";

            // Sudý rozměr - vyžaduje ho yuv420p a nechceme ho řešit až v kodéru.
            _w = (int)visual.Bounds.Width & ~1;
            _h = (int)visual.Bounds.Height & ~1;
            if (_w <= 0 || _h <= 0) { error = "Okno má nulový rozměr."; return false; }

            _fps = gif ? GifFps : Mp4Fps;
            _delayMs = Math.Max(1, 1000 / _fps);
            _maxSeconds = gif ? GifMaxSeconds : Mp4MaxSeconds;

            string exe = Ffmpeg.Find();
            if (exe == null && !gif)
            {
                error = "MP4 vyžaduje ffmpeg (nenalezen v PATH ani v ARBOT_FFMPEG). Použij GIF, nebo doinstaluj ffmpeg.";
                return false;
            }

            try
            {
                if (exe != null)
                {
                    int outW = Math.Min(_w, gif ? GifMaxWidth : Mp4MaxWidth) & ~1;
                    _pool = new ConcurrentBag<byte[]>();
                    _pipe = FfmpegPipe.Start(exe, _w, _h, _fps, path, Format, outW, Recycle, out error);
                    if (_pipe == null) { _pool = null; return false; }
                    _rtb = new RenderTargetBitmap(new PixelSize(_w, _h), new Vector(96, 96));
                    UsesFfmpeg = true;
                }
                else
                {
                    // Bez ffmpegu: vestavěný GIF ze zmenšených snímků držených v paměti.
                    _memScale = Math.Max(1, (int)Math.Ceiling(_w / (double)GifMaxWidth));
                    _memFrames = new List<byte[]>();
                    _memW = _memH = 0;
                    _maxSeconds = Math.Min(_maxSeconds, MemMaxFrames / (double)_fps);
                    UsesFfmpeg = false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Cleanup();
                return false;
            }

            _visual = visual;
            _path = path;
            FrameCount = 0;
            _autoStopFired = false;
            _clock = Stopwatch.StartNew();
            IsRecording = true;

            // Background priorita: snímkování nesmí předbíhat vlastní vykreslování a vstup.
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(_delayMs), DispatcherPriority.Background, OnTick);
            _timer.Start();
            return true;
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!IsRecording) return;

            try
            {
                if (UsesFfmpeg)
                {
                    if (_pipe.Failed) { RequestAutoStop(); return; }

                    var buf = Rent();
                    _rtb.Render(_visual);
                    var handle = GCHandle.Alloc(buf, GCHandleType.Pinned);
                    try { _rtb.CopyPixels(new PixelRect(0, 0, _w, _h), handle.AddrOfPinnedObject(), buf.Length, _w * 4); }
                    finally { handle.Free(); }

                    if (_pipe.WriteFrame(buf)) FrameCount++;
                }
                else
                {
                    var rgb = ScreenCapture.CaptureRgb(_visual, _memScale, out int fw, out int fh);
                    // Změna rozměru okna by rozbila GIF (snímky musí být stejné) - takový snímek zahodíme.
                    if (rgb != null && (_memFrames.Count == 0 || (fw == _memW && fh == _memH)))
                    {
                        _memFrames.Add(rgb);
                        _memW = fw; _memH = fh;
                        FrameCount++;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ScreenRecorder.OnTick: " + ex.Message);
            }

            if (Elapsed.TotalSeconds >= _maxSeconds)
                RequestAutoStop();
        }

        private void RequestAutoStop()
        {
            if (_autoStopFired) return;
            _autoStopFired = true;
            AutoStopRequested?.Invoke();
        }

        /// <summary>
        /// Zastaví snímkování a dokončí kódování (na pracovním vlákně). Vrací výsledek pro uživatele.
        /// Volat z UI vlákna; awaitovat.
        /// </summary>
        public async Task<RecordingResult> StopAsync()
        {
            if (!IsRecording)
                return new RecordingResult { Ok = false, Message = "Záznam neběží." };

            IsRecording = false;
            _timer?.Stop();
            _timer = null;
            _clock?.Stop();

            var res = new RecordingResult { Path = _path };
            double secs = Elapsed.TotalSeconds;
            int frames = FrameCount;

            try
            {
                if (UsesFfmpeg)
                {
                    var pipe = _pipe;
                    _pipe = null;
                    bool ok = await Task.Run(() => pipe.Finish());
                    res.Ok = ok && File.Exists(_path);
                    res.Message = res.Ok
                        ? Describe(frames, secs, pipe.DroppedFrames)
                        : "Kódování selhalo: " + pipe.ErrorTail;
                    pipe.Dispose();
                }
                else
                {
                    var frameList = _memFrames;
                    _memFrames = null;
                    int w = _memW, h = _memH, delay = _delayMs;
                    string path = _path;
                    bool ok = await Task.Run(() => GifWriter.Save(frameList, w, h, delay, path));
                    res.Ok = ok;
                    res.Message = ok
                        ? Describe(frames, secs, 0) + " (vestavěný GIF - bez ffmpegu)"
                        : "Zápis GIF selhal.";
                }
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Message = "Chyba při ukládání: " + ex.Message;
            }
            finally
            {
                Cleanup();
            }

            return res;
        }

        /// <summary>
        /// Souhrn záznamu pro hlášku. Cestu k souboru záměrně NEobsahuje - ta se v toolbaru zobrazuje
        /// zvlášť jako odkaz (<see cref="RecordingResult.Path"/>).
        /// </summary>
        private string Describe(int frames, double seconds, int dropped)
        {
            string size = "";
            try { size = $" · {new FileInfo(_path).Length / 1024.0 / 1024.0:0.0} MB"; } catch { }
            string drop = dropped > 0 ? $" · {dropped} zahozeno" : "";
            return $"Záznam {Format} · {frames} snímků / {seconds:0.0} s{drop}{size}";
        }

        private byte[] Rent()
        {
            int need = _w * _h * 4;
            if (_pool != null && _pool.TryTake(out var b) && b.Length == need) return b;
            return new byte[need];
        }

        private void Recycle(byte[] buf)
        {
            // Volá se z vlákna roury; _pool může být mezitím zahozen (Stop) - proto lokální kopie.
            var pool = _pool;
            if (pool != null && pool.Count < PoolLimit) pool.Add(buf);
        }

        private void Cleanup()
        {
            try { _rtb?.Dispose(); } catch { }
            _rtb = null;
            _pipe = null;
            _pool = null;
            _memFrames = null;
            _visual = null;
        }
    }
}
