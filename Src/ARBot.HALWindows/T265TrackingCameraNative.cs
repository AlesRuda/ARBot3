using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.LocalMaps;
using ARBot.Common.Models;
using ARBot.HAL;
using Intel.RealSense;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Numerics;

namespace HALWindows
{
    public class T265TrackingCameraNative: T265TrackingCamera, IDisposable
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
            /// <summary>
            /// Poradi vrozku
            /// </summary>
            public uint FrameNum;
            /// <summary>
            /// Akcelerace v m/s^2
            /// </summary>
            public double AccelerationX, AccelerationY, AccelerationZ;
            /// <summary>
            /// Uhlova akcelerace rad/s^2
            /// </summary>
            public double AngularAccelerationX, AngularAccelerationY, AngularAccelerationZ;
            /// <summary>
            /// Uhlova rychlost rad/s
            /// </summary>
            public double AngularVelocityX, AngularVelocityY, AngularVelocityZ;
            /// <summary>
            /// Posunuti od pocatecni pozice v metrech
            /// </summary>
            public double TranslationX, TranslationY, TranslationZ;
            /// <summary>
            /// Rychlost v m/s
            /// </summary>
            public double VelocityX, VelocityY, VelocityZ;
            /// <summary>
            /// Orientace senzoru
            /// </summary>
            public double RotationX, RotationY, RotationZ, RotationW;

            /// <summary>
            /// Kvalita mereni 
            /// </summary>
            public uint MapperConfidence;
            /// <summary>
            /// Kvalita mereni 
            /// </summary>
            public uint TrackerConfidence;

            /// <summary>
            /// okamzik vzorku v ms
            /// </summary>
            public double TimeStamp;
            /// <summary>
            /// Doba od prichodu predchoziho frejmu v ms
            /// </summary>
            public double FrameReceivePeriod;
            /// <summary>
            /// Doba od vyzvednuti predchoziho frejmu v ms
            /// </summary>
            public double FramePickupPeriod;
        };

        IntPtr info;
        bool disposed = false;

        public T265TrackingCameraNative():this(null)
        {
        }

        public T265TrackingCameraNative(string sn):base(sn)
        {
            info=T265Start(sn);
        }

        /// <summary>
        /// Nativni varianta nepouziva managed RealSense pipeline - zdroj dat se spousti
        /// v konstruktoru pres T265Start a cte se synchronne v GetLastMeasurement.
        /// </summary>
        protected override void Init()
        {
        }


        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahodu) pro rotacni vektory
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        private Vector3 Angular2Vector3D(double x, double y, double z)
        {
            return new Vector3((float)-x, (float)z, (float)y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahodu) pro pozicni vektory
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <returns></returns>
        private Vector3 Translation2Vector3D(double x, double y, double z)
        {
            return new Vector3((float)x, (float)-z, (float)y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahodu) pro rotaci
        /// </summary>
        /// <param name="x"></param>
        /// <param name="y"></param>
        /// <param name="z"></param>
        /// <param name="w"></param>
        /// <returns></returns>
        private Quaternion ToQuaternion(double x, double y, double z, double w)
        {
            return new Quaternion((float)x, (float)-z, (float)y, (float)w);
        }

        public override IMUState GetLastMeasurement()
        {
            PoseFrameData f=new PoseFrameData();
            f.TranslationX = 1;
            var ret=T265Grab(info, ref f);
            if (ret == 0)
                return null;

            return new IMUState()
            {
                Rotation = ToQuaternion(f.RotationX, f.RotationY, f.RotationZ, f.RotationW),
                AngularVelocity = Angular2Vector3D(f.AngularVelocityX, f.AngularVelocityY, f.AngularVelocityZ),
                AngularAcceleration = Angular2Vector3D(f.AngularAccelerationX, f.AngularAccelerationY, f.AngularAccelerationZ),

                Translation = Translation2Vector3D(f.TranslationX, f.TranslationY, f.TranslationZ),
                Velocity = Translation2Vector3D(f.VelocityX, f.VelocityY, f.VelocityZ),
                Acceleration = Translation2Vector3D(f.AccelerationX, f.AccelerationY, f.AccelerationZ),

                FrameNum = f.FrameNum,
                TimeStamp = new DateTime(1970, 1, 1, 0, 0, 0, 0).ToLocalTime().AddMilliseconds(f.TimeStamp),
                //                MapperConfidence = ToConfidence(f.MapperConfidence),
                Confidence = ToConfidence(f.TrackerConfidence),
                FramePickupPeriod = TimeSpan.FromSeconds(f.FramePickupPeriod),
                FrameReceivePeriod= TimeSpan.FromSeconds(f.FrameReceivePeriod)
            };
        }

        // Protected implementation of Dispose pattern.
        protected override void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                // Free any other managed objects here.
                //
            }

            T265Stop(info);

            disposed = true;
            base.Dispose(disposing);
        }

        ~T265TrackingCameraNative()
        {
            Dispose(false);
        }
    }
}
