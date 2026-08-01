using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.Common.Runtime;
using ARBot.Common.Vision;
using ARBot.HAL;

namespace ARBot.Robot
{
    /// <summary>Rezim behu aplikace (viz doc/record-replay.md).</summary>
    public enum Mode
    {
        /// <summary>Realne rizeni + zaznam.</summary>
        Run,
        /// <summary>Zobrazeni (prehravani) zaznamu.</summary>
        View
        /* , Simulate (odlozeno) */
    }

    /// <summary>
    /// Behove jadro aplikace (obdoba <see cref="ARBotHW"/> jako singleton <see cref="Current"/>).
    /// Drzi kořenovy zdroj zprav, graf zpracovani a scheduler; navenek vystavuje jediny
    /// <see cref="Stream"/> (verejny fan-out, raw &cup; derived), na ktery se pripojuji
    /// odberatele (UI dokumenty, telemetrie, zaznam).
    ///
    /// <para><b>Run:</b> koreny = <see cref="SensorMessageSource{TState}"/> nad senzory z
    /// <see cref="ARBotHW"/> -&gt; <see cref="RoleRouter"/> -&gt; (a) <see cref="Stream"/>,
    /// (b) zpracovani: vize (<see cref="BackProjectProcessor"/>), fuze
    /// (<see cref="FusionProcessor"/>) a periodicka ridici smycka (<see cref="ControlLoop"/>)
    /// pumovana <see cref="System.Threading.Timer"/>em pres <see cref="IScheduler"/>. Fuze i
    /// rizeni sdileji tentyz <see cref="AsyncFusionEngine"/>. Je-li pri Startu Run zadan
    /// <c>file</c>, pripoji se best-effort <see cref="RecordingTarget"/> na <see cref="Stream"/>.</para>
    ///
    /// <para><b>View:</b> koren = <see cref="FileMessageSource"/> -&gt; <see cref="Stream"/>
    /// (bez zpracovani). Navigaci (Play/Pause/Seek) resi krok 9.</para>
    ///
    /// <para>Životni cyklus: <see cref="Start"/> / <see cref="Stop"/> bez ziveho prepinani.
    /// Cile (odberatele) se startuji pred zdroji; pri Stop opacne (drain).</para>
    /// </summary>
    public sealed class ARBotRuntime
    {
        private static readonly Encoding Enc = Encoding.UTF8;

        private static ARBotRuntime current;
        /// <summary>Singleton instance.</summary>
        public static ARBotRuntime Current => current ??= new ARBotRuntime();

        private readonly object gate = new object();
        private readonly RelaySource stream = new RelaySource();

        // Zdroje (koreny) - startuji se posledni, zastavuji prvni.
        private readonly List<MessageSource> sources = new List<MessageSource>();
        // Odberatele grafu / disposables k uklidu pri Stop (v poradi pripojeni).
        private readonly List<IDisposable> connections = new List<IDisposable>();
        // Stupne zpracovani (MessageTargety) - startuji pred zdroji, zastavuji po zdrojich.
        private readonly List<MessageTarget> stages = new List<MessageTarget>();

        private Timer schedTimer;
        private RecordingTarget recording;
        private FileMessageSource fileSource;
        private Stream fileData;
        private Stream fileIndex;
        private bool running;

        private ARBotRuntime() { }

        /// <summary>Aktualni rezim (platny mezi <see cref="Start"/> a <see cref="Stop"/>).</summary>
        public Mode Mode { get; private set; }

        /// <summary>Je runtime spusteny?</summary>
        public bool IsRunning => running;

        /// <summary>Verejny fan-out proud (raw &cup; derived). Odberatele: <c>Stream.Connect(sink)</c>.</summary>
        public MessageSource Stream => stream;

        /// <summary>Kořenovy zdroj replay ve View (jinak null) - pro navigacni nastroj (krok 9).</summary>
        public FileMessageSource FileSource => fileSource;

        /// <summary>
        /// Spusti runtime v danem rezimu. <paramref name="file"/>: ve View cesta k zaznamu
        /// (povinne), v Run volitelna cesta k vystupnimu zaznamu (null = bez zaznamu).
        /// </summary>
        public void Start(Mode mode, string file = null)
        {
            lock (gate)
            {
                if (running) Stop();
                Mode = mode;
                if (mode == Mode.Run) WireRun(file);
                else WireView(file);
                running = true;
            }
        }

        /// <summary>Zastavi runtime a uvolni graf (zdroje prvni, pak stupne - drain).</summary>
        public void Stop()
        {
            lock (gate)
            {
                if (!running) return;

                // 1) Zastav zdroje (prestanou prichazet nove zpravy).
                foreach (var s in sources)
                    try { s.Stop(); } catch (Exception ex) { Debug.WriteLine(ex); }
                sources.Clear();

                // 2) Zastav casovac scheduleru.
                schedTimer?.Dispose();
                schedTimer = null;

                // 3) Odpoj propojeni grafu.
                for (int i = connections.Count - 1; i >= 0; i--)
                    try { connections[i].Dispose(); } catch (Exception ex) { Debug.WriteLine(ex); }
                connections.Clear();

                // 4) Zastav stupne zpracovani (dojedou frontu - drain).
                for (int i = stages.Count - 1; i >= 0; i--)
                    try { stages[i].Stop(); } catch (Exception ex) { Debug.WriteLine(ex); }
                stages.Clear();

                // 5) Zastav zaznam (flush).
                recording?.Stop();
                recording = null;

                // 6) Zavri soubory replay/zaznamu.
                fileSource = null;
                CloseFiles();

                running = false;
            }
        }

        // ---------------- Run ----------------

        private void WireRun(string recordFile)
        {
            // Pockej na dokonceni asynchronniho initu ARBotHW pred dratovanim grafu.
            var hw = ARBotHW.Current;
            hw.WaitReady();

            // Sdileny fuzni engine (fuze i rizeni jej sdili - thread-safe).
            var engine = new AsyncFusionEngine(new EKFModel());
            var mapper = new DefaultMeasurementMapper();
            var clock = new SystemClock();
            var scheduler = new Scheduler();

            // Motory: realny driver z ARBotHW, jinak DummyMotors (dev bez HW).
            IMotorControl motor = hw.Motor ?? (IMotorControl)new DummyMotors();

            var regulator = new Regulator(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed,
                                          Profile.MaxAcceleration, Profile.Rozchod);

            // Vizualni cesta (krok 3): kamery uz nejsou v pipeline pres SensorMessageSource; ridici
            // smycka si je na tiku PULLNE pres injektovanou abstrakci (Common nesmi referencovat
            // HAL/app) a cely CameraFrame forwardne na Stream. Zdroj cte ARBotHW.Current za behu.
            var cameraPull = new HwCameraPullSource(hw);

            // MVP waypoint: (0,0) = bezpecny default (nikam nejede), dokud nebude plano­vac.
            var loop = new ControlLoop(engine, regulator, motor, clock, scheduler,
                                       targetX: 0.0, targetY: 0.0,
                                       period: TimeSpan.FromMilliseconds(Profile.Ts),
                                       cameras: cameraPull);
            stages.Add(loop);

            var fusion = new FusionProcessor(engine, mapper);
            stages.Add(fusion);

            // Vize: probability (barva -> pravdepodobnost) + polarni grid sjizdnosti z hloubky se
            // pocitaji SYNCHRONNE na vlakne kamery pres CameraFrameProcessor a zapisuji se PRIMO do
            // CameraFrame (frame.ImageProbability, frame.Grid). Nahrazuje drivejsi asynchronni stupne
            // BackProjectProcessor + DepthTraversabilityProcessor (viz doc/plan-camera-vision-refactor.md).
            // Projekce se sestavuje LINE z pripojene kamery (Profile.Left/RightCameraTransform) pres
            // stavajici lazy resolver; dokud kamera neni pripojena, grid se preskoci. Grid je nyni
            // soucasti CameraFrame -> tece na Stream a do zaznamu spolu s ramcem; ve View se prehraje.
            var gridCfg = new PolarGridConfig { UseNativeTransform = true };   // nativni SIMD transform (viz ekvivalencni test)
            var projectionResolver = BuildDepthProjectionResolver(hw);
            // Diagnostika (traversability-timing CSV + GC merani na vlakne kamery) je volitelna: pro
            // soutezni jizdu ji lze vypnout parametrem diag=false (vypne co neni potreba). Default on.
            bool diag = Program.GetParamBool("diag", true);
            foreach (var s in hw.Sensors)
            {
                if (s is ICamera cam)
                {
                    var fp = new CameraFrameProcessor(
                        projectionResolver, gridCfg,
                        backProject: new BackProject(BackProject.RoadProbability),
                        diagnosticsCsvPath: diag ? DiagCsvPath($"traversability-timing-{FileToken(cam.Name)}.csv") : null);
                    cam.FrameProcessor = fp;
                    // Pri Stop: odpoj procesor od kamery (prestane pocitat) a zavri jeho diagnostiku.
                    var c = cam;
                    connections.Add(new ActionDisposable(() => { c.FrameProcessor = null; fp.Dispose(); }));
                }
            }

            // Vstup zpracovani (fan-out primarnich zprav do stupnu). Vize uz neni v grafu -> jen fuze + rizeni.
            var processing = new RelaySource();
            connections.Add(processing.Connect(fusion));
            connections.Add(processing.Connect(loop));

            // Odvozene vystupy stupnu -> Stream.
            connections.Add(loop.Output.Connect(stream));

            // Router: primarni -> Stream i processing; odvozene -> jen Stream.
            var router = new RoleRouter(stream, processing);

            // Volitelny zaznam (best-effort) jako odberatel Stream. Bloby nizky limit.
            if (!string.IsNullOrEmpty(recordFile))
            {
                fileData = new FileStream(recordFile, FileMode.Create, FileAccess.Write, FileShare.Read);
                fileIndex = new FileStream(recordFile + ".idx", FileMode.Create, FileAccess.Write, FileShare.Read);
                // ImageMsg (odvozene RGB JPEG + backproject) best-effort (limit 2). CameraFrame je
                // MERENI (primarni) - zaznamenava se VZDY (mimo mapu = neomezeny, bezztratova fronta);
                // obrazy uvnitr jsou uz komprimovane (RGB Jpeg, Prob Png, Depth Deflate - viz CameraFrame.ToData).
                var limits = new Dictionary<string, int> { ["ImageMsg"] = 2 };
                recording = new RecordingTarget(fileData, fileIndex, Enc,
                                                OverflowPolicy.DropNewest, limits);
                connections.Add(stream.Connect(recording));
            }

            // Kořenove zdroje ze senzoru ARBotHW (robustni: chybejici senzor se preskoci).
            BuildSensorSources(hw, router);

            // --- Start: cile pred zdroji ---
            foreach (var st in stages) st.Start();
            recording?.Start();

            // Casovac scheduleru: pravidelne pumpuje ridici smycku (~Profile.Ts).
            // Reentrancni pojistka: System.Threading.Timer callbacky se pri pomalem Pump()
            // (napr. blokujici zapis na nedostupny UART) jinak PREKRYVAJI a zahlti threadpool.
            // Guard zajisti, ze v jednom okamziku bezi nejvyse jeden Pump; zameskane takty
            // dozene Scheduler pri pristim tiku.
            int periodMs = Math.Max(1, Profile.Ts);
            int pumping = 0;
            schedTimer = new Timer(_ =>
            {
                if (Interlocked.Exchange(ref pumping, 1) == 1)
                    return; // predchozi tik jeste bezi
                try { loop.Pump(); }
                catch (Exception ex) { Debug.WriteLine(ex); }
                finally { Volatile.Write(ref pumping, 0); }
            }, null, periodMs, periodMs);

            foreach (var s in sources) s.Start();
        }

        /// <summary>
        /// Vrati resolver projekci hloubkovych kamer pro <see cref="CameraFrameProcessor"/>:
        /// mapuje <see cref="CameraFrame.Name"/> na <see cref="IDepthCameraProjection"/> s robot-centrickou
        /// orientaci (<see cref="Profile.LeftCameraTransform"/> / <see cref="Profile.RightCameraTransform"/>).
        /// Projekce se sestavuje LINE (kamera se pripojuje az v pozadi smycce a <c>CreateDepthProjector</c>
        /// vyzaduje pripojenou pipeline) a cachuje; dokud kamera neni pripojena, vraci null.
        /// </summary>
        private static Func<string, IDepthCameraProjection> BuildDepthProjectionResolver(ARBotHW hw)
        {
            var xforms = new List<(ICamera cam, Matrix4x4 transform)>();
            if (hw.LeftCamera != null) xforms.Add((hw.LeftCamera, Profile.LeftCameraTransform));
            if (hw.RightCamera != null) xforms.Add((hw.RightCamera, Profile.RightCameraTransform));

            var cache = new Dictionary<string, IDepthCameraProjection>();
            return name =>
            {
                if (cache.TryGetValue(name, out var p)) return p;
                foreach (var (cam, tf) in xforms)
                {
                    if (cam.Name != name) continue;
                    try
                    {
                        var proj = cam.CreateDepthProjector();   // vyhodi, dokud kamera neni pripojena
                        proj.SetOrientation(tf);
                        cache[name] = proj;
                        return proj;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"DepthProjector '{name}' zatim nedostupny: {ex.Message}");
                        return null;   // zkusi se znovu pri pristim snimku
                    }
                }
                return null;
            };
        }

        /// <summary>Sestavi zdroje pro dostupne senzory a pripoji je na router.</summary>
        private void BuildSensorSources(ARBotHW hw, RoleRouter router)
        {
            // Kamery (CameraFrame) uz NEJSOU zdrojem v pipeline: od kroku 3 je ridici smycka pulluje
            // (viz HwCameraPullSource) a cely ramec forwardne na Stream. Ostatni senzory (IMU/GPS/motor)
            // jdou dal pres router/Stream.

            // IMU (IMUState).
            if (hw.IMU != null)
            {
                var imu = hw.IMU;
                var src = new SensorMessageSource<IMUState>(
                    h => imu.MeasurementArived += h, h => imu.MeasurementArived -= h);
                connections.Add(src.Connect(router));
                sources.Add(src);
            }

            // GPS (GPSState).
            if (hw.GPS != null)
            {
                var gps = hw.GPS;
                var src = new SensorMessageSource<GPSState>(
                    h => gps.MeasurementArived += h, h => gps.MeasurementArived -= h);
                connections.Add(src.Connect(router));
                sources.Add(src);
            }

            // Motory (IMotorState je rozhrani, ne SensorStateBase -> vlastni zdroj).
            if (hw.Motor != null)
            {
                var src = new MotorSource(hw.Motor);
                connections.Add(src.Connect(router));
                sources.Add(src);
            }
        }

        // ---------------- View ----------------

        private void WireView(string file)
        {
            if (string.IsNullOrEmpty(file))
                throw new ArgumentException("View vyzaduje cestu k zaznamu.", nameof(file));

            var catalog = BuildCatalog();
            fileData = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);

            // Volitelny sidecar index (*.idx) - pro navigaci/seek (krok 9).
            List<IndexEntry> index = null;
            string idxPath = file + ".idx";
            if (File.Exists(idxPath))
            {
                fileIndex = new FileStream(idxPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                index = MessageIndex.Read(fileIndex, Enc);
            }

            fileSource = new FileMessageSource(fileData, Enc, catalog,
                                               FileMessageSource.ReplayPacing.RealTime, index: index);
            sources.Add(fileSource);

            // Koren -> Stream (bez zpracovani).
            connections.Add(fileSource.Connect(stream));

            // Ve View se prehrava rovnou; navigacni nastroj muze prepnout na Paused + Seek.
            fileSource.Start();
        }

        /// <summary>Katalog prototypu zprav pro replay (Common + zarizeni).</summary>
        private static MessageCatalog BuildCatalog()
            => MessageCatalog.CommonDefaults()
                .Register(new GPSState())
                .Register(new MotorStateBase())
                .Register(new CameraFrame());

        // ---------------- pomocne ----------------

        /// <summary>
        /// Cesta k diagnostickemu CSV logu ve slozce <c>logs/</c> v korenu repa (fallback build output).
        /// Prepisuje se pri kazdem Startu (append:false v processoru). null pri selhani.
        /// </summary>
        private static string DiagCsvPath(string fileName)
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
                string logs = Path.Combine(root, "logs");
                return Path.Combine(logs, fileName);
            }
            catch { return null; }
        }

        /// <summary>Bezpecny token do nazvu souboru z nazvu kamery (mezery/neplatne znaky -> '_').</summary>
        private static string FileToken(string name)
        {
            if (string.IsNullOrEmpty(name)) return "cam";
            var chars = name.ToCharArray();
            foreach (var bad in Path.GetInvalidFileNameChars())
                for (int i = 0; i < chars.Length; i++)
                    if (chars[i] == bad) chars[i] = '_';
            for (int i = 0; i < chars.Length; i++)
                if (char.IsWhiteSpace(chars[i])) chars[i] = '_';
            return new string(chars);
        }

        /// <summary>IDisposable, ktery pri <see cref="Dispose"/> zavola predanou akci (uklid pri Stop).</summary>
        private sealed class ActionDisposable : IDisposable
        {
            private Action action;
            public ActionDisposable(Action action) => this.action = action;
            public void Dispose()
            {
                var a = action;
                action = null;
                try { a?.Invoke(); } catch (Exception ex) { Debug.WriteLine(ex); }
            }
        }

        private void CloseFiles()
        {
            try { fileData?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex); }
            try { fileIndex?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex); }
            fileData = null;
            fileIndex = null;
        }

        /// <summary>
        /// Pull kamer pro ridici smycku (krok 3): implementace <see cref="ICameraPullSource"/> nad
        /// <see cref="ARBotHW"/>. Cte <c>hw.Sensors</c> ZA BEHU (podchyti prip. (od|při)pojeni kamer
        /// pres CameraStart/CameraStop) a z kazde kamery vyzvedne nejnovejsi nevyzvednuty snimek
        /// (<see cref="ICamera.GetLastMeasurement"/> - vraci null, kdyz neni novy snimek).
        ///
        /// <para>Tim se zachova smer zavislosti <c>Common ← HAL ← app</c>: <see cref="ControlLoop"/>
        /// v Common zna jen rozhrani <see cref="ICameraPullSource"/>, konkretni cteni HW je zde v app.</para>
        /// </summary>
        private sealed class HwCameraPullSource : ICameraPullSource
        {
            private readonly ARBotHW hw;
            public HwCameraPullSource(ARBotHW hw) => this.hw = hw ?? throw new ArgumentNullException(nameof(hw));

            public IReadOnlyList<CameraFrame> PullLatest()
            {
                List<CameraFrame> frames = null;
                // ToArray: hw.Sensors se muze za behu menit (CameraStart/Stop na jinem vlakne);
                // snapshot zabrani "collection modified" pri iteraci.
                foreach (var s in System.Linq.Enumerable.ToArray(hw.Sensors))
                {
                    if (s is ICamera cam)
                    {
                        CameraFrame f = null;
                        try { f = cam.GetLastMeasurement(); }
                        catch (Exception ex) { Debug.WriteLine(ex); }
                        if (f != null) (frames ??= new List<CameraFrame>(2)).Add(f);
                    }
                }
                return (IReadOnlyList<CameraFrame>)frames ?? Array.Empty<CameraFrame>();
            }
        }

        /// <summary>
        /// Zdroj zprav nad motorem: udalost <c>MeasurementArived</c> nese <see cref="IMotorState"/>
        /// (rozhrani, ne <see cref="SensorStateBase"/>), proto nelze pouzit
        /// <see cref="SensorMessageSource{TState}"/>. Emituje jen stavy, ktere jsou
        /// <see cref="Message"/> (realny driver posila <see cref="MotorStateBase"/>).
        /// </summary>
        private sealed class MotorSource : MessageSource
        {
            private readonly IMotorControl motor;
            private EventHandler<IMotorState> handler;

            public MotorSource(IMotorControl motor) => this.motor = motor;

            public override void Start()
            {
                if (handler != null) return;
                handler = (s, state) => { if (state is Message m) Emit(m); };
                motor.MeasurementArived += handler;
            }

            public override void Stop()
            {
                if (handler == null) return;
                motor.MeasurementArived -= handler;
                handler = null;
            }
        }
    }
}
