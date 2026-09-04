using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.HAL;
using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Nativni varianta ovladace T265 (pres AkceleratorDll). Dedi ze SensorBase;
    /// zdroj dat se startuje v konstruktoru (T265Start) a cte synchronne v GetMeasurement (T265Grab).
    /// </summary>
    public sealed class T265TrackingCameraNative : SensorBase<IMUState>, IIMU
    {
#if IsX64
        [DllImport("AkceleratorDll.dll", EntryPoint = "T265Start", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern IntPtr T265Start(string sn);
        [DllImport("AkceleratorDll.dll", EntryPoint = "T265Stop", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void T265Stop(IntPtr info);
        [DllImport("AkceleratorDll.dll", EntryPoint = "T265Grab", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern int T265Grab(IntPtr info, ref PoseFrameData data);
#else
        [DllImport("AkceleratorDll.dll", EntryPoint = "T265Start", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr T265Start(string sn);
        [DllImport("AkceleratorDll.dll", EntryPoint = "T265Stop", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern void T265Stop(IntPtr info);
        [DllImport("AkceleratorDll.dll", EntryPoint = "T265Grab", SetLastError = true, CallingConvention = CallingConvention.Cdecl)]
        private static extern int T265Grab(IntPtr info, ref PoseFrameData data);
#endif

        [StructLayout(LayoutKind.Sequential)]
        public struct PoseFrameData
        {
            /// <summary>Poradi vrozku</summary>
            public uint FrameNum;
            /// <summary>Akcelerace v m/s^2</summary>
            public double AccelerationX, AccelerationY, AccelerationZ;
            /// <summary>Uhlova akcelerace rad/s^2</summary>
            public double AngularAccelerationX, AngularAccelerationY, AngularAccelerationZ;
            /// <summary>Uhlova rychlost rad/s</summary>
            public double AngularVelocityX, AngularVelocityY, AngularVelocityZ;
            /// <summary>Posunuti od pocatecni pozice v metrech</summary>
            public double TranslationX, TranslationY, TranslationZ;
            /// <summary>Rychlost v m/s</summary>
            public double VelocityX, VelocityY, VelocityZ;
            /// <summary>Orientace senzoru</summary>
            public double RotationX, RotationY, RotationZ, RotationW;
            /// <summary>Kvalita mereni</summary>
            public uint MapperConfidence;
            /// <summary>Kvalita mereni</summary>
            public uint TrackerConfidence;
            /// <summary>okamzik vzorku v ms</summary>
            public double TimeStamp;
            /// <summary>Doba od prichodu predchoziho frejmu v ms</summary>
            public double FrameReceivePeriod;
            /// <summary>Doba od vyzvednuti predchoziho frejmu v ms</summary>
            public double FramePickupPeriod;
        };

        IntPtr info;

        /// <summary>Prvni dostupna kamera.</summary>
        public T265TrackingCameraNative() : this(null)
        {
        }

        /// <summary>
        /// Kamera dle serioveho cisla. Spusti nativni zdroj a pozadi task.
        /// </summary>
        /// <param name="sn">Seriove cislo zarizeni; null = prvni dostupne.</param>
        public T265TrackingCameraNative(string sn)
        {
            info = T265Start(sn);
            Start();
        }

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public override string Name => $"T265";


        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro rotacni vektory.
        /// </summary>
        private Vector3 Angular2Vector3D(double x, double y, double z)
        {
            return new Vector3((float)-x, (float)z, (float)y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro pozicni vektory.
        /// </summary>
        private Vector3 Translation2Vector3D(double x, double y, double z)
        {
            return new Vector3((float)x, (float)-z, (float)y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro rotaci.
        /// </summary>
        private Quaternion ToQuaternion(double x, double y, double z, double w)
        {
            return new Quaternion((float)x, (float)-z, (float)y, (float)w);
        }

        /// <summary>
        /// Prevede tracker_confidence (0-3) na normalizovanou duveru 0-1.
        /// </summary>
        private double ToConfidence(uint v)
        {
            if (v == 1)
                return 0.33;
            if (v == 2)
                return 0.66;
            if (v == 3)
                return 1;

            return 0;
        }

        /// <summary>
        /// Blokujici cteni z nativniho zdroje - ceka na dalsi snimek (nebo do zastaveni).
        /// Volano ze SensorBase.Process.
        /// </summary>
        protected override IMUState GetMeasurement()
        {
            var f = new PoseFrameData();
            while (!stopRequired)
            {
                if (T265Grab(info, ref f) != 0)
                {
                    return new IMUState()
                    {
                        Name = Name,   // puvodce mereni - v robotovi muze byt IMU vic (viz IMUState.Name)
                        Rotation = ToQuaternion(f.RotationX, f.RotationY, f.RotationZ, f.RotationW),
                        AngularVelocity = Angular2Vector3D(f.AngularVelocityX, f.AngularVelocityY, f.AngularVelocityZ),
                        AngularAcceleration = Angular2Vector3D(f.AngularAccelerationX, f.AngularAccelerationY, f.AngularAccelerationZ),
                        Translation = Translation2Vector3D(f.TranslationX, f.TranslationY, f.TranslationZ),
                        Velocity = Translation2Vector3D(f.VelocityX, f.VelocityY, f.VelocityZ),
                        Acceleration = Translation2Vector3D(f.AccelerationX, f.AccelerationY, f.AccelerationZ),
                        TimeStamp = new DateTime(1970, 1, 1, 0, 0, 0, 0).ToLocalTime().AddMilliseconds(f.TimeStamp),
                        Confidence = ToConfidence(f.TrackerConfidence)
                    };
                }
                Thread.Sleep(1);
            }
            return null;
        }

        /// <summary>
        /// Zastavi pozadi task (base) a uvolni nativni zdroj (T265Stop).
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && info != IntPtr.Zero)
            {
                T265Stop(info);
                info = IntPtr.Zero;
            }
        }
    }
}
