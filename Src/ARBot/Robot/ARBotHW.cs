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
using ARBot.Common.Devices;
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
            CameraStart();
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
        public void CameraStart()
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
    }
}
