using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
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

            // MVP waypoint: (0,0) = bezpecny default (nikam nejede), dokud nebude plano­vac.
            var loop = new ControlLoop(engine, regulator, motor, clock, scheduler,
                                       targetX: 0.0, targetY: 0.0,
                                       period: TimeSpan.FromMilliseconds(Profile.Ts));
            stages.Add(loop);

            var fusion = new FusionProcessor(engine, mapper);
            stages.Add(fusion);

            // Vize: BackProject nad CameraFrame -> pravdepodobnostni ImageMsg.
            // includeSourceRgb=false: RGB je uz v zaznamu uvnitr CameraFrame (measurement se zapisuje
            // vzdy), samostatny "rgb" ImageMsg by byl duplicitni a jen by pridaval Jpeg encode.
            var vision = new BackProjectProcessor(new BackProject(BackProject.RoadProbability),
                                                  includeSourceRgb: false);
            stages.Add(vision);

            // Vstup zpracovani (fan-out primarnich zprav do stupnu).
            var processing = new RelaySource();
            connections.Add(processing.Connect(vision));
            connections.Add(processing.Connect(fusion));
            connections.Add(processing.Connect(loop));

            // Odvozene vystupy stupnu -> Stream.
            connections.Add(vision.Output.Connect(stream));
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

        /// <summary>Sestavi zdroje pro dostupne senzory a pripoji je na router.</summary>
        private void BuildSensorSources(ARBotHW hw, RoleRouter router)
        {
            // Kamery (CameraFrame).
            foreach (var s in hw.Sensors)
            {
                if (s is ICamera cam)
                {
                    var c = cam;
                    var src = new SensorMessageSource<CameraFrame>(
                        h => c.MeasurementArived += h, h => c.MeasurementArived -= h);
                    connections.Add(src.Connect(router));
                    sources.Add(src);
                }
            }

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

        private void CloseFiles()
        {
            try { fileData?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex); }
            try { fileIndex?.Dispose(); } catch (Exception ex) { Debug.WriteLine(ex); }
            fileData = null;
            fileIndex = null;
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
