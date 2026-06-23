using ARBot.Common.Common;
using ARBot.Common.Devices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Models
{
    /// <summary>
    /// Mereni inercialni jednotky
    /// </summary>
    public class IMUState: SensorStateBase, ICloneable, IHistoryItem<IMUState>
    {
        /// <summary>
        /// Uzaje z magnetometru
        /// </summary>
        public Vector3? Magnetometer;
        /// <summary>
        /// Akcelerace v m/s^2
        /// </summary>
        public Vector3? Acceleration ;
        /// <summary>
        /// Uhlova akcelerace rad/s^2
        /// </summary>
        public Vector3? AngularAcceleration;
        /// <summary>
        /// Uhlova rychlost rad/s
        /// </summary>
        public Vector3? AngularVelocity;
        /// <summary>
        /// Posunuti od pocatecni pozice v metrech
        /// </summary>
        public Vector3? Translation;
        /// <summary>
        /// Rychlost v m/s
        /// </summary>
        public Vector3? Velocity;
        /// <summary>
        /// Orientace senzoru
        /// </summary>
        public Quaternion? Rotation;

        DateTime IHistoryItem<IMUState>.TimeStamp { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        /// <summary>
        /// Kvalita mereni 0- fail, 1-high  
        /// </summary>
        public double Confidence { get; set; }

        /// <summary>
        /// Konstruktor
        /// </summary>
        public IMUState()
        {
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        public IMUState(Quaternion rotation)
        {
            Rotation = rotation;
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        public IMUState(Quaternion rotation, Vector3 angularVelocity)
        {
            Rotation = rotation;
            AngularVelocity = angularVelocity;
        }

        public IMUState Clone()
        {
            IMUState v = new IMUState();

            v.Acceleration = Acceleration;
            v.AngularAcceleration = AngularAcceleration;
            v.AngularVelocity = AngularVelocity;
            v.Confidence = Confidence;
            v.FrameNum = FrameNum;
            v.FramePickupPeriod = FramePickupPeriod;
            v.FrameReceivePeriod = FrameReceivePeriod;
            v.Rotation = Rotation;
            v.TimeStamp = TimeStamp;
            v.Translation = Translation;
            v.Velocity = Velocity;
            v.Magnetometer = Magnetometer;

            return v;
        }

        object ICloneable.Clone()
        {
            return Clone();
        }

        /// <summary>
        /// Nevim co to presne je za operaci, ale je to zkopirovano z rs-pose-predict.cpp v samplech realsense
        /// </summary>
        /// <param name="v"></param>
        /// <returns></returns>
        Quaternion Exp(Vector3 v)
        {
            float x = v.X / 2;
            float y = v.Y / 2;
            float z = v.Z / 2;
            float th2 = x * x + y * y + z * z;
            float th = MathF.Sqrt(th2);
            float c = MathF.Cos(th);
            float s = th2 < 1e-9 ? 1 - th2 / 6 : MathF.Sin(th) / th;
            return new Quaternion(s * x, s * y, s * z, c);
        }
        /// <summary>
        /// Predikce budouciho stavu.
        /// </summary>
        /// <param name="ts">Pocet sekund do budoucna, kdy chci spocitat predikci</param>
        /// <returns></returns>
        public IMUState Predict(TimeSpan ts)
        {
            var s = Clone();
            float t = (float)ts.TotalSeconds;
            s.Translation = t * (t / 2 * s.Acceleration + s.Velocity) + s.Translation;
            s.Velocity = s.Velocity + t * s.Acceleration;
            Vector3 v = new Vector3();
            s.Rotation = Exp(t * (t / 2 * s.AngularAcceleration??v + s.AngularVelocity??v)) * s.Rotation;
            s.AngularVelocity = s.AngularVelocity + t * s.AngularVelocity;
            return s;
        }

        public override string ToString()
        {
            return string.Format("yaw={0}", YPR()?.Yaw);
        }

        private static Vector3? Interpolate(Vector3? prev, Vector3? next, float d)
        {
            if (next == null || prev == null)
                return null;
            return prev.Value + d * (next.Value - prev.Value);
        }

        /// <summary>
        /// K okamziku d provede predikci stavu a znich vezme stredni hodnotu.
        /// </summary>
        /// <param name="prev"></param>
        /// <param name="next"></param>
        /// <param name="d"></param>
        /// <returns></returns>
        public static IMUState InterpolateDynamic(IMUState prev, IMUState next, double d)
        {
            var s = next.Clone();
            var ts = (next.TimeStamp - prev.TimeStamp);

            var pp = prev.Predict(new TimeSpan((long)(ts.Ticks * d)));
            var pn = next.Predict(new TimeSpan((long)(ts.Ticks * (d - 1))));

            return InterpolateLinear(pp, pn, 0.5f);
        }

        /// <summary>
        /// Linearni imterpolace mezi dvama stavy
        /// </summary>
        /// <param name="prev"></param>
        /// <param name="next"></param>
        /// <param name="d"></param>
        /// <returns></returns>
        public static IMUState InterpolateLinear(IMUState prev, IMUState next, float d)
        {
            var s = next.Clone();
            s.Translation = Interpolate(prev.Translation, next.Translation, d);
            s.Velocity = Interpolate(prev.Velocity, next.Velocity, d);
            s.Acceleration = Interpolate(prev.Acceleration, next.Acceleration, d);
            s.Magnetometer = Interpolate(prev.Magnetometer, next.Magnetometer, d);

            if (prev.Rotation == null || next.Rotation == null)
                s.Rotation = null;
            else
                s.Rotation = Quaternion.Slerp(prev.Rotation.Value, next.Rotation.Value, d);
            s.AngularVelocity = Interpolate(prev.AngularVelocity, next.AngularVelocity, d);
            s.AngularAcceleration = Interpolate(prev.AngularAcceleration, next.AngularAcceleration, d);

            return s;
        }

        public IMUState Interpolate(IMUState prev, IMUState next, float d)
        {
            return InterpolateLinear(prev, next, d);
        }


        /// <summary>
        /// Prevod na YPR
        /// </summary>
        /// <returns></returns>
        public YawPitchRoll YPR()
        {
            if (Rotation == null)
                return null;
            var ypr = new YawPitchRoll(Rotation.Value, YawPitchRoll.Euler.zxy);
            ypr.TimeStamp = TimeStamp;
            return ypr;
        }

        public static Quaternion QuaternionFromRotationMatrix(Matrix4x4 matrix)
        {
            var num8 = (matrix.M11 + matrix.M22) + matrix.M33;
            Quaternion quaternion = new Quaternion();
            if (num8 > 0f)
            {
                var num = MathF.Sqrt((num8 + 1f));
                quaternion.W = num * 0.5f;
                num = 0.5f / num;
                quaternion.X = (matrix.M23 - matrix.M32) * num;
                quaternion.Y = (matrix.M31 - matrix.M13) * num;
                quaternion.Z = (matrix.M12 - matrix.M21) * num;
                return quaternion;
            }
            if ((matrix.M11 >= matrix.M22) && (matrix.M11 >= matrix.M33))
            {
                var num7 = MathF.Sqrt((((1f + matrix.M11) - matrix.M22) - matrix.M33));
                var num4 = 0.5f / num7;
                quaternion.X = 0.5f * num7;
                quaternion.Y = (matrix.M12 + matrix.M21) * num4;
                quaternion.Z = (matrix.M13 + matrix.M31) * num4;
                quaternion.W = (matrix.M23 - matrix.M32) * num4;
                return quaternion;
            }
            if (matrix.M22 > matrix.M33)
            {
                var num6 = MathF.Sqrt((((1f + matrix.M22) - matrix.M11) - matrix.M33));
                var num3 = 0.5f / num6;
                quaternion.X = (matrix.M21 + matrix.M12) * num3;
                quaternion.Y = 0.5f * num6;
                quaternion.Z = (matrix.M32 + matrix.M23) * num3;
                quaternion.W = (matrix.M31 - matrix.M13) * num3;
                return quaternion;
            }
            var num5 = MathF.Sqrt((((1f + matrix.M33) - matrix.M11) - matrix.M22));
            var num2 = 0.5f / num5;
            quaternion.X = (matrix.M31 + matrix.M13) * num2;
            quaternion.Y = (matrix.M32 + matrix.M23) * num2;
            quaternion.Z = 0.5f * num5;
            quaternion.W = (matrix.M12 - matrix.M21) * num2;

            return quaternion;

        }
    }
}
