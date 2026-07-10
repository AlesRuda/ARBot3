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
using ARBot.HAL.Devices.Joystick;
using FTD2XX_NET;
using System.Collections.Generic;
using ARBot.Common.Devices;

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
                    current.Init();
                }
                return current;
            }
        }

        public IJoystick Joystick { get; set; }
        public D435Camera LeftCamera { get; set; }
        public D435Camera RightCamera { get; set; }
        public T265TrackingCamera TrackingCamera { get; set; }
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
#if IsX64
            /*
            var f = new FTDI();
            var spiList = FTD2xxNeoPixelDriver.GetDeviceList(f);
            var n = spiList.FirstOrDefault(i => i.Type == FTDI.FT_DEVICE.FT_DEVICE_4232H && i.Description.EndsWith(" A"));
            if (n != null)
                NeoPixel = new NeoPixelProcessor(new FTD2xxNeoPixelDriver(f, n));
            */

            //                UartGimbal = new Uart("UartGimbal", "COM11", 9600);

            UartAHRS = new Uart("UartAHRS", Program.GetParam("UartAHRS", "COM5"), 115200);

            UartGPS = new Uart("UartGPS", Program.GetParam("UartGPS", "COM6"), 921600);

            UartMotor = new Uart("UartMotor", Program.GetParam("UartMotor", "COM9"), 115200, "\r");

            if (UartMotor != null)
            {
                Debug.WriteLine($"MaxTheoreticalSpeed={Profile.MaxTheoreticalSpeed}");
                Debug.WriteLine($"MaxAllowedSpeed={Profile.MaxAllowedSpeed}");
                Debug.WriteLine($"WheelPerimeter={Profile.WheelPerimeter}");
                Debug.WriteLine($"EncoderCounts={Profile.EncoderCounts}");
                Debug.WriteLine($"MotorGearBoxReduction={Profile.MotorGearBoxReduction}");
                Motor = new SDC2160Ex(UartMotor, Profile.MaxTheoreticalSpeed, Profile.MaxAllowedSpeed, Profile.WheelPerimeter, Profile.EncoderCounts * Profile.MotorGearBoxReduction);
                Motor.SetAcceleration(Profile.MaxAcceleration);
            }

            if (UartGPS != null)
                //                GPS = new NmeaGps(UartGPS);
                GPS = new uBloxGps(UartGPS);

            if (UartAHRS != null)
            {
                //                AHRS = new VN100(UartAHRS);
                IMU = new VN100IMUBinary(UartAHRS);
            }

            Joystick = new Joystick();
#endif
            if(Motor != null)
                sensors.Add(Motor);
            if (GPS != null)
                sensors.Add(GPS);
            if (IMU != null)
                sensors.Add(IMU);
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
                sensors.Add(LeftCamera = new D435Camera(D435LeftSerial) { Swap = true });
            if (RightCamera == null)
                sensors.Add(RightCamera = new D435Camera(D435RightSerial) { Swap = false });
            if (CameraStateChanged != null)
                CameraStateChanged();
        }
    }
}
