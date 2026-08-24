using System;
using System.Linq;
using System.Diagnostics;
using ARBot.Common.Configuration;
using ARBot.HAL;
using ARBot.HAL.Devices.AHRS;
using ARBot.HAL.Devices.GPSs.uBlox;
using ARBot.HAL.Devices.Camera;
using ARBot.HAL.Devices.Uart;
using ARBot.HAL.Devices.NeoPixel;
using ARBot.HAL.Devices.MotorDrivers;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Simulation;
using ARBot.Common.Vision.Synthetic;
using ARBot.HAL.Devices.GPSs;
using System.Threading.Tasks;

namespace ARBot.Robot
{
    /// <summary>Ktery hardware je prave zalozeny (viz doc/virtual-hw.md).</summary>
    public enum HwMode
    {
        /// <summary>Zadny - zadne senzory. Stav po startu aplikace.</summary>
        None = 0,
        /// <summary>Skutecne senzory (kamery, IMU, GPS, motor).</summary>
        Real = 1,
        /// <summary>Simulovane senzory renderovane z mapy (vyzaduje mapu a fuzi).</summary>
        Virtual = 2,
    }

    public class ARBotHW
    {
        const string T265Serial = "925122110155";
        const string D435LeftSerial = "740112071040";
        const string D435RightSerial = "740112071021";

        private List<ISensor> sensors= new List<ISensor>();

        private static ARBotHW current;
        public static ARBotHW Current
        {
            get
            {
                if (current == null)
                {
                    current = new ARBotHW();
                    // Init bezi asynchronne (dotazovani portu/kamer muze trvat). Task
                    // ukladame, aby na nej mohl ARBotRuntime.Start(Run) pockat pred
                    // dratovanim grafu (viz InitTask / WaitReady / IsReady).
                    current.initTask = Task.Run(() => current.Init());
                }
                return current;
            }
        }

        private Task initTask;

        /// <summary>Task probihajiciho asynchronniho <see cref="Init"/> (null jen do prvniho pristupu).</summary>
        public Task InitTask => initTask ?? Task.CompletedTask;

        /// <summary>Je asynchronni init hotov (senzory nadratovane)?</summary>
        public bool IsReady => initTask != null && initTask.IsCompleted;

        /// <summary>Pocka na dokonceni asynchronniho <see cref="Init"/> (idempotentni).</summary>
        public void WaitReady()
        {
            try { initTask?.Wait(); }
            catch (Exception ex) { Debug.WriteLine(ex.ToString()); }
        }

        public IJoystick Joystick { get; set; }
        public ICamera LeftCamera { get; set; }
        public ICamera RightCamera { get; set; }
        public IIMU TrackingCamera { get; set; }
        public IMotorControl Motor { get; set; }
        public IGPS GPS { get; set; }
        public IIMU IMU { get; set; }
        //        public SndGenerator SndGenerator { get; set; }
        public NeoPixelProcessor NeoPixel { get; set; }
        protected IUart UartMotor { get; set; }
        protected IUart UartGPS { get; set; }
        protected IUart UartAHRS { get; set; }

        /// <summary>
        /// Zdroj <b>odhadu pozy</b> (fuze) pro metadata snimku — kamery z nej plni
        /// <see cref="ARBot.Common.Devices.CameraFrame.PoseAtCaptureX"/>. Plati pro
        /// <b>realny i virtualni</b> HW, aby se obe vetve chovaly stejne.
        ///
        /// <para><b>Proc property a ne parametr <c>SetRealHW</c>.</b> Realny HW se zaklada
        /// <b>driv, nez existuje fuzni engine</b> (<c>ARBotRuntime.WireRun</c> vola
        /// <c>SetRealHW()</c> pred jeho vytvorenim). Setter proto lambdu <b>rozda i uz zalozenym
        /// kamerám</b> a pamatuje si ji pro ty pristi — poradi zakladani tim prestane hrat roli.</para>
        ///
        /// <para><b>Nezamenovat s <c>VirtualHWOptions.PoseAt</c></b>, coz je RENDEROVACI poza
        /// virtualnich kamer (parametr <c>camerapose=</c>, ve vychozim stavu ground truth).
        /// Tady musi byt vzdy odhad z fuze. Viz doc/virtual-hw.md.</para>
        /// </summary>
        public Func<DateTime, ARBot.Common.Fusion.RobotState> EstimatedPoseAt
        {
            get => estimatedPoseAt;
            set
            {
                estimatedPoseAt = value;
                ApplyEstimatedPose();
            }
        }
        private Func<DateTime, ARBot.Common.Fusion.RobotState> estimatedPoseAt;

        /// <summary>Parametry sceny, se kterymi bezi PRAVE ZALOZENY virtualni HW.</summary>
        private SyntheticSceneOptions activeSceneOptions;

        /// <summary>Rozda <see cref="EstimatedPoseAt"/> aktualne zalozenym kameram.</summary>
        private void ApplyEstimatedPose()
        {
            if (LeftCamera != null) LeftCamera.EstimatedPoseAt = estimatedPoseAt;
            if (RightCamera != null) RightCamera.EstimatedPoseAt = estimatedPoseAt;
        }

        /// <summary>
        /// Ktery hardware je prave zalozeny. Po startu aplikace <see cref="HwMode.None"/> -
        /// co se zalozi, urcuje parametr <c>hw=</c> nebo volba v menu; skutecne se to stane
        /// az v <c>ARBotRuntime.Start</c> (virtualni HW potrebuje fuzi a mapu).
        /// </summary>
        public HwMode Mode { get; private set; } = HwMode.None;

        // Porty UART senzoru zjistene v Init; senzory z nich vznikaji az v SetRealHW.
        private string portAHRS, portMotor, portGPS;

        protected ARBotHW()
        {
        }

        /// <summary>
        /// Seznam sensoru
        /// </summary>
        public IEnumerable<ISensor> Sensors => sensors;

        protected virtual void Init()
        {
            string? PortAHRS = null;
            string? PortMotor = null;
            string? PortGPS = null;

#if IsX64
            PortAHRS = "COM5";
            PortMotor = "COM9";
            PortGPS = "COM8";
#endif
#if IsARM64
            // OrangePI/Armbian: VN100 IMU pres sdilenou tridu Uart (System.IO.Ports) - stejny kod
            // jako na x64, jen s linuxovym zarizenim /dev/tty... System.IO.Ports (v10) podporuje
            // i Linux/ARM64. Zarizeni lze zadat argumentem "UartAHRS=/dev/ttyS0"; vychozi hodnotu
            // nutno overit dle zapojeni. Obaleno try/catch, aby chyba senzoru neshodila cely Init.
            PortAHRS = "/dev/ttyS0";
#endif

            /*
            var f = new FTD2XX_NET.FTDI();
            var spiList = FTD2xxNeoPixelDriver.GetDeviceList(f);
            var n = spiList.FirstOrDefault(i => i.Type == FTDI.FT_DEVICE.FT_DEVICE_4232H && i.Description.EndsWith(" A"));
            if (n != null)
                NeoPixel = new NeoPixelProcessor(new FTD2xxNeoPixelDriver(f, n));
            */

            // Diagnostický přepínač: no_uart=true přeskočí UART senzory (IMU/GPS/motor). Odpojené UART
            // drivery házejí výjimky v těsné smyčce (GC churn + CPU) - toto umožní je při měření výkonu
            // vizuální cesty vypnout bez fyzického odpojení (viz devlog 2026-08-01, self-test no_uart).
            bool noUart = Program.GetParamBool("no_uart", false);
            if (noUart)
            {
                PortAHRS = null; PortMotor = null; PortGPS = null;
                Debug.WriteLine("ARBotHW: no_uart=true -> UART senzory (IMU/GPS/motor) přeskočeny.");
            }

            // Porty se jen zapamatuji - senzory z nich zaklada az SetRealHW. Po startu aplikace
            // zadny HW nebezi (HwMode.None); co se zalozi, urcuje parametr hw= nebo menu.
            portAHRS = PortAHRS;
            portMotor = PortMotor;
            portGPS = PortGPS;

#if IsX64
            Joystick = new HAL.Devices.Joystick.Joystick();
#endif
        }


        /// <summary>
        /// Ground truth simulovaneho robota - nenulovy jen pri virtualnim HW. Slouzi k porovnani
        /// skutecnosti s odhadem fuze (viz doc/virtual-hw.md).
        /// </summary>
        public SimulatedRobot SimulatedRobot { get; private set; }

        /// <summary>
        /// Umela chyba pozy vnucena do renderovaci cesty OBOU virtualnich kamer - ladici pomucka
        /// pro overeni korelace s mapou (viz doc/virtual-hw.md). Nenulova jen kdyz ji nekdo nastavi
        /// nastrojem nad virtualni kamerou; ve vychozim stavu se nedeje nic.
        ///
        /// <para>Zije po celou dobu behu aplikace (ne jen pri virtualnim HW), aby si ji nastroj
        /// mohl drzet i pres prepnuti rezimu HW a nemusel resit null.</para>
        /// </summary>
        public VirtualPoseError VirtualPoseError { get; } = new VirtualPoseError();

        /// <summary>
        /// Sum, biasy a prokluz kol simulovanych senzoru - <b>jedna sdilena instance</b> pro cely
        /// virtualni HW (viz doc/virtual-hw.md). Zije po celou dobu behu aplikace, aby si na ni
        /// nastroj mohl drzet odkaz i pres prepnuti rezimu HW (stejne jako
        /// <see cref="VirtualPoseError"/>).
        ///
        /// <para>Sum a biasy ctou senzory pri kazdem vzorku, takze zmena plati hned. Prokluz kol
        /// drzi <see cref="SimulatedRobot"/>, ktery o teto tride nevi (je v HAL, on v Common) -
        /// po zmene je proto potreba zavolat <see cref="ApplyVirtualSensorOptions"/>.</para>
        /// </summary>
        public ARBot.HAL.Devices.VirtualSensorOptions VirtualSensors { get; }
            = new ARBot.HAL.Devices.VirtualSensorOptions();

        /// <summary>Nastaveni, se kterym bezi PRAVE ZALOZENY virtualni HW - obvykle
        /// <see cref="VirtualSensors"/>, ale test si smi predat vlastni instanci.</summary>
        private ARBot.HAL.Devices.VirtualSensorOptions activeSensorOptions;

        /// <summary>
        /// Vzhled a sum simulovane SCENY (sum hloubky, drsnost a vyska travy, sum barvy). Zije po
        /// celou dobu behu aplikace, aby na ni nastroj mohl drzet odkaz i pres prepnuti rezimu HW.
        ///
        /// <para><b>Zmena plati hned</b> — renderer drzi tuto instanci a cte ji pri kazdem pixelu,
        /// takze neni potreba kamery zakladat znovu.</para>
        ///
        /// <para><b>Nacpak se to da vypinat</b> (23. 8. 2026): hranicni body se do metru prepocitavaji
        /// zpetnou projekci pres MERENOU hloubku, zatimco semanticky kanal occupancy gridu se promita
        /// dopredu na ROVINU zeme. S <c>DepthNoiseM = 0</c>, <c>GrassRoughnessM = 0</c> a
        /// <c>GrassHeightM = 0</c> je scena
        /// dokonala rovina, oba smery se stanou touz geometrii a hranice se ma s hranici v lokalni
        /// mape krýt — zbytek rozdilu je uz jen casovani pozy. Viz doc/virtual-hw.md.</para>
        /// </summary>
        public SyntheticSceneOptions VirtualScene { get; } = new SyntheticSceneOptions();

        /// <summary>
        /// Prenese prokluz kol z nastaveni do beziciho <see cref="SimulatedRobot"/>.
        /// Bez virtualniho HW nedela nic.
        /// <para>Vola se pri zalozeni virtualniho HW a po kazde zmene z UI nebo prikazove radky.
        /// Sum a biasy se prenaset nemusi - senzory ctou tutez instanci pri kazdem vzorku;
        /// prokluz ano, protoze <see cref="SimulatedRobot"/> je v <c>Common</c> a o nastaveni
        /// v <c>HAL</c> nevi.</para>
        /// </summary>
        public void ApplyVirtualSensorOptions()
        {
            var sim = SimulatedRobot;
            var opt = activeSensorOptions;
            if (sim == null || opt == null) return;

            sim.LeftWheelSlip = opt.LeftWheelSlip;
            sim.RightWheelSlip = opt.RightWheelSlip;
        }

        /// <summary>
        /// Uvolni motory, GPS a IMU (obdoba <see cref="CameraStop"/> pro zbytek senzoru) -
        /// pouziva se pri prepnuti na virtualni HW.
        /// </summary>
        private void MotionSensorsStop()
        {
            foreach (var s in new object[] { Motor, GPS, IMU })
            {
                if (s == null) continue;
                if (s is ISensor sensor) sensors.Remove(sensor);
                (s as IDisposable)?.Dispose();
            }

            Motor = null;
            GPS = null;
            IMU = null;

            // I porty - bez toho by je nasledny SetRealHW nemohl znovu otevrit (obsazene).
            foreach (var u in new object[] { UartMotor, UartGPS, UartAHRS })
                (u as IDisposable)?.Dispose();

            UartMotor = null;
            UartGPS = null;
            UartAHRS = null;
        }

        /// <summary>
        /// Uvolni VSECHEN hardware (kamery, IMU, GPS, motor, simulaci) a prepne do
        /// <see cref="HwMode.None"/>. Volaji ho <see cref="SetRealHW"/> i <see cref="SetVirtualHW"/>
        /// na zacatku, takze prepnuti mezi rezimy je ciste v obou smerech - drive slo prejit
        /// jen z realneho na virtualni a zpatky uz ne (<c>SetRealHW</c> zaklada kamery jen kdyz
        /// jsou pole null, a ta po virtualnim HW null nejsou).
        /// </summary>
        public void SetNoHW()
        {
            CameraStop();
            MotionSensorsStop();
            SimulatedRobot = null;
            Mode = HwMode.None;

            if (CameraStateChanged != null)
                CameraStateChanged();
        }

        public Action CameraStateChanged;
        public void CameraStop()
        {
            if (TrackingCamera is IDisposable)
                ((IDisposable)TrackingCamera).Dispose();
            if(TrackingCamera != null)
                sensors.Remove(TrackingCamera);
            TrackingCamera = null;

            if (LeftCamera is IDisposable)
                ((IDisposable)LeftCamera).Dispose();
            if(LeftCamera != null)
                sensors.Remove(LeftCamera);
            LeftCamera = null;

            if (RightCamera is IDisposable)
                ((IDisposable)RightCamera).Dispose();
            if(RightCamera != null)
                sensors.Remove(RightCamera);
            RightCamera = null;

            if (CameraStateChanged != null)
                CameraStateChanged();
        }
        /// <summary>
        /// Zalozi SKUTECNE senzory: kamery i UART (IMU, GPS, motor). Porty zjistil <see cref="Init"/>;
        /// prazdny port (nebo <c>no_uart=true</c>) prislusny senzor preskoci.
        /// <para>Nevola se automaticky - po startu aplikace bezi <see cref="HwMode.None"/> a rezim
        /// urcuje parametr <c>hw=real</c> nebo volba v menu. Viz doc/virtual-hw.md.</para>
        /// </summary>
        public void SetRealHW()
        {
            SetNoHW();   // ciste vychozi stav (i kdyz predtim bezel virtualni HW)

            if (!string.IsNullOrEmpty(portAHRS))
            {
                UartAHRS = new Uart("UartAHRS", Program.GetParam("UartAHRS", portAHRS), 115200);
                //                AHRS = new VN100(UartAHRS);
                IMU = new VN100IMUBinary(UartAHRS);
                sensors.Add(IMU);
            }

            if (!string.IsNullOrEmpty(portMotor))
            {
                UartMotor = new Uart("UartMotor", Program.GetParam("UartMotor", portMotor), 115200, "\r");
                Debug.WriteLine($"MaxTheoreticalSpeed={Profile.MaxTheoreticalSpeed}");
                Debug.WriteLine($"MaxAllowedSpeed={Profile.MaxAllowedSpeed}");
                Debug.WriteLine($"WheelPerimeter={Profile.WheelPerimeter}");
                Debug.WriteLine($"EncoderCounts={Profile.EncoderCounts}");
                Debug.WriteLine($"MotorGearBoxReduction={Profile.MotorGearBoxReduction}");
                Motor = new SDC2160Ex(UartMotor, Profile.MaxTheoreticalSpeed, Profile.MaxAllowedSpeed, Profile.WheelPerimeter, Profile.EncoderCounts * Profile.MotorGearBoxReduction);
                Motor.SetAcceleration(Profile.MaxAcceleration);
                sensors.Add(Motor);
            }

            if (!string.IsNullOrEmpty(portGPS))
            {
                UartGPS = new Uart("UartGPS", Program.GetParam("UartGPS", portGPS), 921600);
                GPS = new uBloxGps(UartGPS);
                sensors.Add(GPS);
            }

            sensors.Add(TrackingCamera = new T265TrackingCamera(T265Serial));
//            TrackingCamera = new T265TrackingCameraNative(T265Serial);
            sensors.Add(LeftCamera = new D435Camera(D435LeftSerial, "Left") { Swap = true });
            sensors.Add(RightCamera = new D435Camera(D435RightSerial, "Right") { Swap = false });
            ApplyEstimatedPose();   // muze byt jeste null - runtime ji doplni, az bude fuze

            Mode = HwMode.Real;
            Debug.WriteLine("ARBotHW: realny HW aktivni.");

            if (CameraStateChanged != null)
                CameraStateChanged();
        }

        /// <summary>
        /// Vymeni kamery za SIMULOVANE, ktere rendruji scenu z OsmNav mapy a pozy robota
        /// (viz doc/virtual-hw.md). Jmena kamer ("Left"/"Right") i montazni transformace zustavaji
        /// stejne jako u realnych, takze zbytek aplikace (vize, occupancy, UI) rozdil nepozna.
        /// <para>
        /// Virtualni kamera nema <c>Swap</c>: prevraceni leve D435 je artefakt fyzicke montaze,
        /// simulace rovnou rendruje podle montazni transformace.
        /// </para>
        /// <para>
        /// T265 (<see cref="TrackingCamera"/>) zustava nezalozena - virtualni IMU/GPS jsou zatim
        /// otevreny ukol.
        /// </para>
        /// </summary>
        public void SetVirtualHW(VirtualHWOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            options.Validate();

            SetNoHW();   // uvolni pripadny predchozi HW (kamery i UART senzory)

            // Sdilena instance nastaveni senzoru: bez vyslovneho zadani se bere ta, kterou drzi
            // ARBotHW - jen tak se dá sum a chyby menit za behu z UI (viz VirtualSensors).
            var sensorOptions = options.Sensors ?? VirtualSensors;
            activeSensorOptions = sensorOptions;

            // Sdilena instance parametru sceny (stejny duvod jako u sensorOptions vyse): jen tak
            // jde sum hloubky a drsnost travy menit za behu z UI a z prikazove radky.
            activeSceneOptions = options.Scene ?? VirtualScene;

            // Vypsat, s CIM se opravdu renderuje. Bez teto hlasky byla scena z prikazove radky
            // i z panelu pul dne mrtva a nikdo si toho nevsiml: VirtualHWOptions.Scene mela
            // vychozi new SyntheticSceneOptions(), takze se '??' nikdy neuplatnil a kamery jely
            // s vychozimi hodnotami, zatimco parametry se zapisovaly do VirtualScene, ze ktereho
            // nikdo nerenderoval. Viz VirtualHWOptions.Scene (24. 8. 2026).
            Trace.WriteLine(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "virtualhw scena ({0}): grassheight={1} m, grassrough={2} m, depthnoise={3} m",
                ReferenceEquals(activeSceneOptions, VirtualScene) ? "sdilena, lze menit z UI" : "vlastni instance",
                activeSceneOptions.GrassHeightM, activeSceneOptions.GrassRoughnessM,
                activeSceneOptions.DepthNoiseM));

            var scene = new RoadScene(options.Network, options.Origin);

            sensors.Add(LeftCamera = new VirtualCamera(
                "Left", scene, activeSceneOptions, options.LeftCameraTransform, options.PoseAt, options.Camera));
            sensors.Add(RightCamera = new VirtualCamera(
                "Right", scene, activeSceneOptions, options.RightCameraTransform, options.PoseAt, options.Camera));
            ApplyEstimatedPose();   // POZOR: options.PoseAt je render (camerapose=), tohle je odhad z fuze

            // Ground truth: motory ho posouvaji, GPS a IMU ho zasumene meri.
            SimulatedRobot = new SimulatedRobot(options.WheelBase, TimeBase.Now, options.MaxWheelSpeed)
            {
                X = options.StartX,
                Y = options.StartY,
                Theta = options.StartTheta,
            };
            SimulatedRobot.SetAcceleration(options.Acceleration);
            ApplyVirtualSensorOptions();   // prokluz kol ze sdilene instance (options.Sensors)

            sensors.Add((ISensor)(Motor = new VirtualMotors(SimulatedRobot)));
            Motor.SetAcceleration(options.Acceleration);

            sensors.Add(GPS = new VirtualGps(SimulatedRobot, options.Origin, sensorOptions));
            sensors.Add(IMU = new VirtualImu(SimulatedRobot, sensorOptions));

            Mode = HwMode.Virtual;
            Debug.WriteLine("ARBotHW: virtualni HW aktivni (kamery, motory, GPS a IMU ze simulace).");

            if (CameraStateChanged != null)
                CameraStateChanged();
        }
    }
}
