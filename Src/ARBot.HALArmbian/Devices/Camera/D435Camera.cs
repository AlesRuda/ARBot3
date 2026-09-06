using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.LocalMaps;
using ARBot.Common.Vision;
using ARBot.HAL;
using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Ovladac hloubkove kamery Intel RealSense D435 pro Armbian/ARM64.
    /// Funkcne shodny s HALWindows variantou, ale linkuje managed wrapper Intel.RealSense 2.53
    /// (odpovida native librealsense2.so 2.53 na Orange Pi). Nacteni nativni knihovny resi
    /// <see cref="RealSenseNativeResolver"/>.
    /// Dedi ze SensorBase: po Start bezi na pozadi task ctouci snimky z pipeline,
    /// posledni snimek je dostupny pres GetLastMeasurement a udalost MeasurementArived.
    /// <para>
    /// Ovladac je odolny proti odpojeni a pripojeni kamery za behu: pipeline se
    /// nezaklada v konstruktoru, ale az v pozadi smycce, jakmile je zarizeni k dispozici.
    /// Vypadek za behu (odpojeni) se detekuje pri cteni snimku, pipeline se zbouri a smycka
    /// se v intervalu <see cref="ReconnectPeriodMs"/> pokousi o opetovne pripojeni. Dokud
    /// kamera neni pripojena, <see cref="IsError"/> vraci true a smycka nezahlcuje CPU.
    /// </para>
    /// <para><b>Diagnostika jde do <see cref="System.Diagnostics.Trace"/>, ne do <c>Debug</c>.</b>
    /// <c>Debug.WriteLine</c> je <c>[Conditional("DEBUG")]</c>, takze v Release buildu - a prave
    /// ten bezi na zarizeni - nezustane po poruche ZADNA stopa. Stalo se to 2. 9. 2026: kamery
    /// se neohlasily a v panelu Debug output nebyl o nich ANI RADEK, takze se pricina hledala
    /// hodinu merenim zvenci, misto aby ji driver rovnou napsal (byla to nesplnitelna kombinace
    /// hloubky a barvy na USB 2.0). Tataz past uz jednou byla opravena u hlasky o zahozenem
    /// merenii ve fuzi - viz <c>AsyncFusionEngine.Enqueue</c>. Hlaseni jsou jednorazova
    /// (pripojeni, odpojeni) nebo throtlovana, takze proud nezaplavi.
    /// Hlida to <c>DiagnostikaSenzoruTests</c>.</para>
    /// </summary>
    public sealed class D435Camera : SensorBase<CameraFrame>, ICamera
    {
        /// <summary>Seriove cislo zarizeni; null = prvni dostupna kamera.</summary>
        string sn;
        /// <summary>Nazev kamery (napr. "Left"/"Right") - soucast <see cref="Name"/>.</summary>
        string nazev = "D435";
        /// <inheritdoc/>
        public ICameraFrameProcessor FrameProcessor { get; set; }

        /// <inheritdoc/>
        public System.Func<System.DateTime, ARBot.Common.Fusion.RobotState> EstimatedPoseAt { get; set; }

        /// <summary>Nastaveni barevneho (RGB) streamu.</summary>
        CameraSettings settingsRGB;
        /// <summary>Nastaveni hloubkoveho streamu.</summary>
        CameraSettings settingsDepth;

        /// <summary>Triple-buffer capture pool (krok 4): recyklovane buffery misto alokace per grab.
        /// Pouziva jen vlakno kamery (GetMeasurement) -> bez zamku.</summary>
        private readonly CaptureFramePool capturePool = new CaptureFramePool(3);

        /// <summary>Interval opakovanych pokusu o pripojeni, kdyz kamera chybi [ms].</summary>
        private const int ReconnectPeriodMs = 1000;
        /// <summary>Timeout cekani na snimek [ms]. Kratky, aby smycka rychle reagovala
        /// na Stop() i na odpojeni kamery.</summary>
        private const uint FrameTimeoutMs = 1000;

        /// <summary>
        /// Po kolika po sobe jdoucich timeoutech se pipeline povazuje za ZASEKNUTOU a zbourá se
        /// (priste ji <see cref="EnsureConnected"/> nastartuje znovu).
        ///
        /// <para><b>Proc to vubec je:</b> stream se umi zaseknout, aniz by se kamera odpojila od
        /// USB — librealsense prestane dodavat snimky, ale zarizeni je dal vyctene, takze
        /// <see cref="DevicePresent"/> vraci true. Bez tohoto citace by se pipeline nezbourala,
        /// <c>connected</c> by zustalo true, <see cref="IsError"/> by hlasil <b>OK</b> a kamera
        /// by mlcela navzdy. Presne to se stalo na OrangePi 31. 8. 2026: prava D435 prestala
        /// dodavat snimky, v dmesg zadne odpojeni (jen opakovane <c>USBDEVFS_CLEAR_HALT</c>),
        /// panel Sensors ji pritom ukazoval jako OK.</para>
        ///
        /// <para><b>Proc zrovna 3:</b> pri <see cref="FrameTimeoutMs"/> = 1 s to znamena ~3 s bez
        /// snimku, zatimco pozadovanych je 30 za sekundu — takze to nemuze byt jen zpomaleni.
        /// Nizsi prah by skodil: cerstve nastartovana pipeline dodá prvni snimek az za ~1–2 s
        /// (zmereno na zarizeni), takze prilis agresivni restart by se zacyklil a snimky by
        /// nedorazily nikdy.</para>
        /// </summary>
        private const int StallTimeoutsBeforeRestart = 3;

        /// <summary>
        /// Hlidka zamrzlych streamu: snimky chodi, ale razitko jednoho streamu stoji. Logika je
        /// spolecna pro obe platformy a otestovana - viz <see cref="StreamFreezeWatch"/>.
        /// </summary>
        private readonly ARBot.HAL.Devices.Camera.StreamFreezeWatch freezeWatch =
            new ARBot.HAL.Devices.Camera.StreamFreezeWatch();

        /// <summary>
        /// Kolikrat se pipeline restartovala kvuli ZAMRZLEMU streamu (razitko stalo, snimky chodily).
        /// Vede se zvlast od <see cref="StallRestarts"/>: „snimky nechodi" a „snimky chodi, ale jsou
        /// porad stejne" jsou jine poruchy a pri diagnostice je potreba je rozlisit.
        /// </summary>
        public int FrozenStreamRestarts { get; private set; }

        /// <summary>Pocet po sobe jdoucich timeoutu; 0 = posledni cteni snimek doslo.</summary>
        private int consecutiveTimeouts;

        /// <summary>
        /// Kolikrat uz se pipeline restartovala kvuli zaseknutemu streamu. Diagnostika —
        /// rostouci cislo znamena, ze se problem opakuje a resi ho az tahle zachrana.
        /// </summary>
        public int StallRestarts { get; private set; }

        // Kontext je SDILENY pro vsechny RealSense drivery (RealSenseShared): vlastni kontext
        // per driver znamenal tri device watchery a tri konkurencni bootery T265 - viz tam.

        private Pipeline pipeline;
        private PipelineProfile pipelineProfile;

        /// <summary>true = pipeline bezi a streamuje z pripojene kamery.</summary>
        private volatile bool connected = false;

        /// <summary>
        /// Otoceni kamery vzuhu nohama, tj. rotace podel z o 180 stupnu.
        /// </summary>
        public bool Swap;

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public override string Name => $"{nazev} {sn}";

        /// <summary>
        /// Chybovy stav: krome chyby zpracovani (base) je za chybu povazovano i odpojeni
        /// kamery (dokud neni pipeline pripojena).
        /// </summary>
        public override bool IsError => !connected || base.IsError;

        /// <summary>
        /// Prevede timestamp snimku (ms od epochy) na lokalni DateTime.
        /// </summary>
        /// <param name="miliseconds">Cas v milisekundach od 1.1.1970.</param>
        /// <returns>Lokalni cas snimku.</returns>
        public static DateTime CalcTimeStamp(double miliseconds)
        {
            return new DateTime(1970, 1, 1).Add(DateTimeOffset.Now.Offset).AddMilliseconds(miliseconds);
        }

        /// <summary>Prvni dostupna kamera, RGB 640x480.</summary>
        public D435Camera() : this(null, new CameraSettings(640, 480))
        {
        }

        /// <summary>Kamera dle serioveho cisla, RGB 640x480.</summary>
        /// <param name="sn">Seriove cislo zarizeni.</param>
        public D435Camera(string sn) : this(sn, new CameraSettings(640, 480))
        {
        }

        /// <summary>Kamera dle serioveho cisla s nazvem (napr. "Left"/"Right"), RGB 640x480.</summary>
        /// <param name="sn">Seriove cislo zarizeni.</param>
        /// <param name="nazev">Nazev kamery (soucast <see cref="Name"/>).</param>
        public D435Camera(string sn, string nazev) : this(sn, new CameraSettings(640, 480))
        {
            this.nazev = nazev;
        }

        /// <summary>
        /// Hlavni konstruktor. Nakonfiguruje a spusti kameru pres Init (depth je fixne 480x270).
        /// </summary>
        /// <param name="sn">Seriove cislo zarizeni; null = prvni dostupne.</param>
        /// <param name="rgb">Nastaveni barevneho streamu.</param>
        public D435Camera(string sn, CameraSettings rgb)
        {
            this.sn = sn;

            Init(rgb, new CameraSettings(480, 270));
        }

        /// <summary>Aktualni nastaveni hloubkoveho streamu.</summary>
        public CameraSettings DepthSettings => settingsDepth;

        /// <summary>Aktualni nastaveni barevneho (RGB) streamu.</summary>
        public CameraSettings RGBSettings => settingsRGB;

        /// <summary>
        /// Zkopiruje data hloubkoveho snimku (16 bit) do ciloveho bufferu, pripadne v obracenem poradi (Swap).
        /// </summary>
        /// <param name="f">Zdrojovy hloubkovy frame (bude uvolnen).</param>
        /// <param name="d">Cilovy buffer.</param>
        private void GetDataGray(VideoFrame f, byte[] d)
        {
            if (f == null)
                return;

            using (f)
            {
                if (Swap)
                    NativeComputeUnit.ReverseInt16IntPtr(d, f.Data, f.Width * f.Height);
                else
                    NativeComputeUnit.CopyIntPtr(d, f.Data, f.Width * f.Height * 2);
            }
        }

        /// <summary>
        /// Zkopiruje barevny snimek (RGB24) do ciloveho bufferu jako BGR32, pripadne v obracenem poradi (Swap).
        /// </summary>
        /// <param name="f">Zdrojovy barevny frame (bude uvolnen).</param>
        /// <param name="d">Cilovy buffer (BGR32).</param>
        private void GetDataRGB(VideoFrame f, byte[] d)
        {
            if (f == null)
                return;

            using (f)
            {
                if (Swap)
                    NativeComputeUnit.ReverseRGB24ToBGR32IntPtr(d, f.Data, f.Width * f.Height);
                else
                    NativeComputeUnit.CopyRGB24ToBGR32IntPtr(d, f.Data, f.Width * f.Height);
            }
        }

        /// <summary>
        /// Pocka na dalsi snimek z pipeline, zpracuje ho (RGB, hloubka; odvozene entity dopocte
        /// <see cref="FrameProcessor"/>) a vrati jako CameraFrame z capture poolu. Volano ze SensorBase.Process.
        /// </summary>
        protected override CameraFrame GetMeasurement()
        {
            // (Re)pripojeni: kdyz kamera chybi, nezahlcovat CPU a vratit null (bez udalosti).
            if (!EnsureConnected())
            {
                Thread.Sleep(ReconnectPeriodMs);
                return null;
            }

            try
            {
                if (pipeline.TryWaitForFrames(out var frames, FrameTimeoutMs))
                using (frames)
                {
                    consecutiveTimeouts = 0;

                    // Poolovany capture slot (recyklovane buffery, krok 4) misto alokace per grab.
                    var frame = capturePool.Next(
                        settingsRGB != null, settingsRGB?.Width ?? 0, settingsRGB?.Height ?? 0,
                        settingsDepth != null, settingsDepth?.Width ?? 0, settingsDepth?.Height ?? 0);
                    var imageRGB = frame.ImageRGB;
                    var imageDepth = frame.ImageDepth;

                    var ts = TimeBase.Now;
                    var colorFrame = frames.ColorFrame;
                    var depthFrame = frames.DepthFrame;

                    var RGBTimeStamp = CalcTimeStamp(colorFrame.Timestamp);
                    var DepthTimeStamp = CalcTimeStamp(depthFrame.Timestamp);
                    if (imageDepth != null)
                        GetDataGray(depthFrame, imageDepth.Data);
                    if (imageRGB != null)
                        GetDataRGB(colorFrame, imageRGB.Data);

                    frame.Name = Name;
                    frame.TimeStamp = ts;
                    frame.RGBTimeStamp = RGBTimeStamp;
                    frame.DepthTimeStamp = DepthTimeStamp;

                    // Zamrzly stream: razitko stoji, prestoze framesety chodi. Kdyz se to potvrdi,
                    // zbourat pipeline - stejnou cestou jako u timeoutu, tedy vcetne toho, ze
                    // kamera do prvniho uspesneho pripojeni poctive hlasi CHYBU.
                    string zamrzlo = freezeWatch.Check(settingsRGB != null ? colorFrame.Timestamp : (double?)null,
                                                       settingsDepth != null ? depthFrame.Timestamp : (double?)null);
                    if (zamrzlo != null)
                    {
                        FrozenStreamRestarts++;
                        Trace.WriteLine($"{Name}: {zamrzlo} -> restart pipeline (celkem {FrozenStreamRestarts}x). "
                                        + "Snimky chodily dal, jen porad tytez - bez tohohle by kamera hlasila OK.");
                        Teardown();
                        return null;
                    }

                    // Odhad pozy jako METADATUM snimku (vyhradne pro vizualizaci - viz
                    // CameraFrame.PoseAtCaptureX). Chybejici poza snimek NEZAHAZUJE.
                    ARBot.HAL.Devices.Camera.CameraPoseStamp.Apply(frame, EstimatedPoseAt);

                    // Synchronni dopocet odvozenych vlastnosti (probability, polarni grid) na vlakne
                    // kamery - misto asynchronniho fan-outu do pipeline (viz doc/plan-camera-vision-refactor.md).
                    FrameProcessor?.Process(frame);

                    return frame;
                }

                // Timeout bez snimku: odpojeni se nemusi projevit vyjimkou, jen prestanou chodit
                // snimky. Overit fyzickou pritomnost - kdyz kamera zmizela, zbourat pipeline (jinak
                // by connected zustalo true, IsError by hlasil OK a reconnect by se nespustil).
                // Jeden timeout jeste nic neznamena - na sbernici se NEPTAME. Duvod je merený:
                // DevicePresent() vola ctx.QueryDevices(), a to nad bezicimi streamy neni zdarma
                // ani neomylne - pri castem volani sam selze na "failed to set power state"
                // (zmereno na OrangePi 1. 9. 2026). Puvodne se ptal pri KAZDEM timeoutu.
                if (++consecutiveTimeouts < StallTimeoutsBeforeRestart)
                    return null;

                // Prah prekrocen: pipeline se bourá tak ci tak (Teardown shodi connected, takze
                // se stav konecne projevi i v IsError - dokud se kamera nechytne, hlasi CHYBA
                // misto klamneho OK). Na sbernici se ptame jen kvuli SPRAVNEMU HLASENI, at se
                // nehleda kabel, kdyz je kamera na miste a zasekl se jen stream.
                bool? present = DevicePresent();
                Teardown();
                consecutiveTimeouts = 0;

                if (present == false)
                {
                    Trace.WriteLine($"{Name}: kamera odpojena (bez snimku a zarizeni na sbernici chybi).");
                }
                else
                {
                    // Zarizeni je na sbernici (nebo se to nepodarilo zjistit), ale snimky nechodi.
                    // Sam se stream nerozjede - priste ho nastartuje EnsureConnected znovu.
                    StallRestarts++;
                    Trace.WriteLine($"{Name}: stream zaseknuty ({StallTimeoutsBeforeRestart} timeoutu po sobe" +
                                    $"{(present == null ? ", pritomnost zarizeni nezjistena" : ", zarizeni je pritomne")})" +
                                    $" -> restart pipeline (celkem {StallRestarts}x).");
                }
                return null;
            }
            catch (Exception ex)
            {
                // Tvrdy vypadek za behu (odpojeni kamery) -> zbourat pipeline, priste se zkusi reconnect.
                Trace.WriteLine($"{Name}: cteni snimku selhalo (odpojeno?): {ex.Message}");
                Teardown();
                return null;
            }
        }

        /// <summary>
        /// Zjisti, zda je pozadovana kamera fyzicky pripojena. Pri sn==null hleda libovolnou
        /// hloubkovou kameru radu D4xx (podle nazvu), jinak podle serioveho cisla.
        /// </summary>
        private bool? DevicePresent()
        {
            // Dotaz jde pres sdileny kontext a pod jednim zamkem (RealSenseShared.Query) - kazdy
            // QueryDevices v RSUSB backendu spousti i boot T265, a ten nesmi bezet dvakrat naraz.
            // null = dotaz sam selhal, a to NENI dukaz, ze kamera chybi: QueryDevices nad bezicimi
            // streamy umi spadnout na "failed to set power state" (zmereno na OrangePi 1. 9. 2026),
            // a kdyby se to vydavalo za odpojeni, hlasil by driver "kamera odpojena" u kamery,
            // ktera je na miste - a slo by se hledat kabel.
            return RealSenseShared.Query(Name, RealSenseShared.BySerialOrName(sn, "D4"));
        }

        /// <summary>
        /// Zajisti pripojenou a bezici pipeline s aktualnim nastavenim streamu. Pokud kamera
        /// chybi nebo se start nezdari, vrati false (a pipeline necha zbouranou pro dalsi pokus).
        /// </summary>
        private bool EnsureConnected()
        {
            if (connected)
                return true;

            // != true: kdyz se pritomnost nepodarilo zjistit (null), radeji pockame na dalsi
            // pokus, nez abychom slepe startovali pipeline.
            if (DevicePresent() != true)
                return false;

            try
            {
                var cfg = new Config();
                if (sn != null)
                    cfg.EnableDevice(sn);
                if (settingsDepth != null)
                    cfg.EnableStream(Stream.Depth, settingsDepth.Width, settingsDepth.Height, Format.Z16, 30);
                if (settingsRGB != null)
                    cfg.EnableStream(Stream.Color, settingsRGB.Width, settingsRGB.Height, Format.Rgb8, 30);

                // Po odpojeni je pipeline zbourana (Teardown) - vzdy tvorime cerstvou instanci
                // ze sdileneho kontextu; znovupouzita pipeline se na nove zarizeni nenavaze.
                if (pipeline == null)
                    pipeline = new Pipeline(RealSenseShared.Context);

                pipelineProfile = pipeline.Start(cfg);
                connected = true;
                Trace.WriteLine($"{Name}: pipeline pripojena.");
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{Name}: pripojeni pipeline selhalo: {ex.Message}");
                Teardown();
                return false;
            }
        }

        /// <summary>
        /// Zastavi a UVOLNI pipeline a oznaci odpojeno. Pipeline se zahazuje (ne jen Stop),
        /// protoze po odpojeni USB se stara instance na znovupripojene zarizeni nenavaze -
        /// pri reconnectu se v EnsureConnected vytvori cerstva.
        /// </summary>


        private void Teardown()
        {
            connected = false;
            pipelineProfile = null;

            // Sledovani zamrzlych streamu zacina znovu: nova pipeline ma nova razitka a bez
            // vynulovani by prah sepnul hned pri prvnim snimku po pripojeni.
            freezeWatch.Reset();
            if (pipeline != null)
            {
                try
                {
                    pipeline.Stop();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"{Name}: Stop pipeline: {ex.Message}");
                }
                try
                {
                    pipeline.Dispose();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"{Name}: Dispose pipeline: {ex.Message}");
                }
                pipeline = null;
            }
        }

        /// <summary>
        /// (Re)konfiguruje kameru dle zadanych rozliseni a (znovu)spusti pozadi task.
        /// Lze volat opakovane za behu - bezici zpracovani se pred rekonfiguraci zastavi.
        /// Pipeline se sama pripoji az v pozadi smycce (kamera nemusi byt prave pripojena).
        /// </summary>
        public bool Init(CameraSettings rgbSettings, CameraSettings depthSettings)
        {
            if (IsRunning)
                Stop();   // pockej na dobehnuti smycky - pote lze bezpecne menit nastaveni

            settingsRGB = rgbSettings;
            settingsDepth = depthSettings;

            Teardown();   // vynutit reconnect s novym nastavenim
            Start();      // znovu spustit smycku (pripoji se lazily, jakmile je kamera k dispozici)

            return true;
        }

        /* STARA IMPLEMENTACE Init - ponechana do overeni na zarizeni (viz CLAUDE.md:
           "pri prepisech nemazat starou implementaci, dokud novou nepotvrdi testy").
           Puvodni Init konfiguroval a spoustel pipeline synchronne a nezvladal
           odpojeni/pripojeni za behu (pri odpojeni WaitForFrames vyhazoval vyjimku
           v tesne smycce a pipeline se uz neobnovila).

        public bool Init(CameraSettings rgbSettings, CameraSettings depthSettings)
        {
            if (IsRunning)
                Stop();

            settingsRGB = rgbSettings;
            settingsDepth = depthSettings;

            var cfg = new Config();
            if (sn != null)
                cfg.EnableDevice(sn);
            if (settingsDepth != null)
                cfg.EnableStream(Stream.Depth, settingsDepth.Width, settingsDepth.Height, Format.Z16, 30);
            if (settingsRGB != null)
                cfg.EnableStream(Stream.Color, settingsRGB.Width, settingsRGB.Height, Format.Rgb8, 30);

            if (pipeline == null)
                pipeline = new Pipeline();
            else
                pipeline.Stop();

            pipelineProfile = pipeline.Start(cfg);

            Start();

            return true;
        }
        */

        /// <summary>
        /// Zastavi pozadi task (base) a uvolni pipeline + kontext (nativni prostredky kamery).
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);   // zastavi a pocka na dokonceni pozadi smycky
            if (disposing)
            {
                Teardown();   // zastavi a uvolni pipeline (sdileny kontext se nedisposuje)
            }
        }

        /// <summary>
        /// Prevede RealSense vnitrni parametry kamery (Intrinsics) na vlastni ARBot Intrinsics
        /// vcetne mapovani modelu zkresleni.
        /// </summary>
        private Common.Coordinates.Intrinsics Simplify(Intel.RealSense.Intrinsics i)
        {
            var ii = new Common.Coordinates.Intrinsics();
            ii.Coeffs = i.coeffs;
            ii.Fx = i.fx;
            ii.Fy = i.fy;
            ii.Height = i.height;
            ii.Width = i.width;
            ii.PPx = i.ppx;
            ii.PPy = i.ppy;
            if (i.coeffs.All(f => f == 0))
                ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.None;
            else
            {
                switch (i.model)
                {
                    case Distortion.BrownConrady:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.BrownConrady;
                        break;
                    case Distortion.Ftheta:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.Ftheta;
                        break;
                    case Distortion.InverseBrownConrady:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.InverseBrownConrady;
                        break;
                    case Distortion.ModifiedBrownConrady:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.ModifiedBrownConrady;
                        break;
                    default:
                        ii.Model = ARBot.Common.Coordinates.Intrinsics.Distortion.None;
                        break;
                }
            }
            return ii;
        }

        /// <summary>
        /// Sestavi projekci kamery z RealSense intrinsics/extrinsics. Pokud je zadana hloubkova
        /// intrinsika, vrati D435CameraProjection (3D), jinak zakladni CameraProjection.
        /// Zohlednuje prevraceni obrazu (Swap).
        /// </summary>
        /// <param name="name">Popis pro ladici vypis.</param>
        /// <param name="colorIntrin">Vnitrni parametry barevne kamery.</param>
        /// <param name="depthIntrin">Vnitrni parametry hloubkove kamery (null = jen barevna projekce).</param>
        /// <param name="color2Depth">Transformace z barevne do hloubkove kamery.</param>
        /// <param name="depth2Color">Transformace z hloubkove do barevne kamery.</param>
        private CameraProjection CreateProjector(string name,
            Intel.RealSense.Intrinsics? colorIntrin,
            Intel.RealSense.Intrinsics? depthIntrin,
            Intel.RealSense.Extrinsics? color2Depth,
            Intel.RealSense.Extrinsics? depth2Color
            )
        {
            Intel.RealSense.Intrinsics? i = depthIntrin ?? colorIntrin;

            var i1 = Simplify(i.Value);
            Debug.WriteLine(name + ": " + i1.ToString());
            var ii = i1.Inverse();
            if (Swap)
            {
                i1.PPx = i1.Width - i1.PPx;
                i1.PPy = i1.Height - i1.PPy;

                ii.PPx = ii.Width - ii.PPx;
                ii.PPy = ii.Height - ii.PPy;
            }
            if (depthIntrin == null)
                return new CameraProjection(i1, ii, Extrinsic2Transform(color2Depth.Value), Extrinsic2Transform(depth2Color.Value));

            // Hloubkova projekce je od 21. 8. 2026 obycejna CameraProjection: podtrida
            // D435CameraProjection na ARM jen vyhazovala NotSupportedException (nativni
            // ColorPixel23D v libNativeLib.so neni - a jak se ukazalo, neni ani ve verzi pro
            // Windows). Ten prepocet umi ted baze managed, pro vsechny platformy stejne.
            var proj = new CameraProjection(i1, ii, System.Numerics.Matrix4x4.Identity,
                                            System.Numerics.Matrix4x4.Identity);

            // Barevna intrinsika a extrinsiky color<->depth do POPISU projekce. Na ARM je puvodni
            // konstruktor zahazoval uplne (prazdne telo), takze sem nedotekly vubec. Managed
            // prepocet hranic cesty do metru je potrebuje.
            // Viz ARBot.Common/Vision/ColorPixelTo3D.cs.
            proj.SetColorAlignment(Simplify(colorIntrin.Value),
                                   Extrinsic2Transform(color2Depth.Value),
                                   Extrinsic2Transform(depth2Color.Value));
            return proj;
        }

        /// <summary>
        /// Prevede RealSense extrinsics (rotace 3x3 + translace) na transformacni matici 4x4.
        /// </summary>
        Matrix4x4 Extrinsic2Transform(Intel.RealSense.Extrinsics e)
        {
            return new Matrix4x4(e.rotation[0], e.rotation[1], e.rotation[2], 0, e.rotation[3], e.rotation[4], e.rotation[5], 0, e.rotation[6], e.rotation[7], e.rotation[8], 0, e.translation[0], e.translation[1], e.translation[2], 1);
        }

        /// <summary>
        /// Vytvori projekci barevne kamery do roviny po ktere jede robot (bez hloubky).
        /// </summary>
        public ICameraProjection CreateProjector()
        {
            if (pipelineProfile == null)
                throw new InvalidOperationException($"{Name}: kamera neni pripojena.");
            var c = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Color);
            var d = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Depth);
            return CreateProjector("Color Intrinsics", c.GetIntrinsics(), null, c.GetExtrinsicsTo(d), d.GetExtrinsicsTo(c));
        }

        /// <summary>
        /// Vytvori hloubkovou projekci (3D rekonstrukce bodu z hloubkove mapy).
        /// </summary>
        public IDepthCameraProjection CreateDepthProjector()
        {
            if (pipelineProfile == null)
                throw new InvalidOperationException($"{Name}: kamera neni pripojena.");
            var c = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Color);
            var d = pipelineProfile.GetStream<VideoStreamProfile>(Stream.Depth);
            return CreateProjector("Depth Intrinsics", c.GetIntrinsics(), d.GetIntrinsics(), c.GetExtrinsicsTo(d), d.GetExtrinsicsTo(c));
        }
    }
}
