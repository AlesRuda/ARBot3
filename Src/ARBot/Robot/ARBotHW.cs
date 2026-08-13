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
using ARBot.Common.Simulation;
using ARBot.Common.Vision.Synthetic;
using ARBot.HAL.Devices.GPSs;
using System.Threading.Tasks;

namespace ARBot.Robot
{
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

            if (!string.IsNullOrEmpty(PortAHRS))
            {
                UartAHRS = new Uart("UartAHRS", Program.GetParam("UartAHRS", PortAHRS), 115200);
                //                AHRS = new VN100(UartAHRS);
                IMU = new VN100IMUBinary(UartAHRS);
                sensors.Add(IMU);
            }

            if (!string.IsNullOrEmpty(PortMotor))
            {
                UartMotor = new Uart("UartMotor", Program.GetParam("UartMotor", PortMotor), 115200, "\r");
                Debug.WriteLine($"MaxTheoreticalSpeed={Profile.MaxTheoreticalSpeed}");
                Debug.WriteLine($"MaxAllowedSpeed={Profile.MaxAllowedSpeed}");
                Debug.WriteLine($"WheelPerimeter={Profile.WheelPerimeter}");
                Debug.WriteLine($"EncoderCounts={Profile.EncoderCounts}");
                Debug.WriteLine($"MotorGearBoxReduction={Profile.MotorGearBoxReduction}");
                Motor = new SDC2160Ex(UartMotor, Profile.MaxTheoreticalSpeed, Profile.MaxAllowedSpeed, Profile.WheelPerimeter, Profile.EncoderCounts * Profile.MotorGearBoxReduction);
                Motor.SetAcceleration(Profile.MaxAcceleration);
                sensors.Add(Motor);
            }

            if (!string.IsNullOrEmpty(PortGPS))
            { 
                UartGPS = new Uart("UartGPS", Program.GetParam("UartGPS", PortGPS), 921600);
                GPS = new uBloxGps(UartGPS);
                sensors.Add(GPS);
            }

#if IsX64
            Joystick = new HAL.Devices.Joystick.Joystick();
#endif
            SetRealHW();
        }


        /// <summary>
        /// Ground truth simulovaneho robota - nenulovy jen pri virtualnim HW. Slouzi k porovnani
        /// skutecnosti s odhadem fuze (viz doc/virtual-hw.md).
        /// </summary>
        public SimulatedRobot SimulatedRobot { get; private set; }

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
        /// Zalozi REALNE senzory zavisle na hardwaru (kamery). Vola se automaticky z <see cref="Init"/>,
        /// takze bez dalsiho zasahu bezi aplikace nad skutecnym HW.
        /// </summary>
        public void SetRealHW()
        {
            if (TrackingCamera == null)
                sensors.Add(TrackingCamera = new T265TrackingCamera(T265Serial));
//            TrackingCamera = new T265TrackingCameraNative(T265Serial);
            if (LeftCamera == null)
                sensors.Add(LeftCamera = new D435Camera(D435LeftSerial, "Left") { Swap = true });
            if (RightCamera == null)
                sensors.Add(RightCamera = new D435Camera(D435RightSerial, "Right") { Swap = false });
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

            CameraStop();          // uvolni pripadne realne kamery
            MotionSensorsStop();   // uvolni pripadne realne motory/GPS/IMU

            var scene = new RoadScene(options.Network, options.Origin);

            sensors.Add(LeftCamera = new VirtualCamera(
                "Left", scene, options.Scene, options.LeftCameraTransform, options.PoseAt, options.Camera));
            sensors.Add(RightCamera = new VirtualCamera(
                "Right", scene, options.Scene, options.RightCameraTransform, options.PoseAt, options.Camera));

            // Ground truth: motory ho posouvaji, GPS a IMU ho zasumene meri.
            SimulatedRobot = new SimulatedRobot(options.WheelBase, TimeBase.Now)
            {
                X = options.StartX,
                Y = options.StartY,
                Theta = options.StartTheta,
            };
            SimulatedRobot.SetAcceleration(options.Acceleration);

            sensors.Add((ISensor)(Motor = new VirtualMotors(SimulatedRobot)));
            Motor.SetAcceleration(options.Acceleration);

            sensors.Add(GPS = new VirtualGps(SimulatedRobot, options.Origin, options.Sensors));
            sensors.Add(IMU = new VirtualImu(SimulatedRobot, options.Sensors));

            Debug.WriteLine("ARBotHW: virtualni HW aktivni (kamery, motory, GPS a IMU ze simulace).");

            if (CameraStateChanged != null)
                CameraStateChanged();
        }
    }
}
