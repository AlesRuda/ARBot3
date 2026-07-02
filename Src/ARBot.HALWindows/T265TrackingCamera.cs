using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.HAL;
using Intel.RealSense;
using System;
using System.Numerics;

namespace HALWindows
{
    /// <summary>
    /// Ovladac sledovaci kamery Intel RealSense T265 (6DOF pose / IMU).
    /// Dedi ze SensorBase: po Init bezi na pozadi task ctouci pose snimky z pipeline;
    /// posledni stav je dostupny pres GetLastMeasurement a udalost MeasurementArived.
    /// </summary>
    public sealed class T265TrackingCamera : SensorBase<IMUState>, IIMU
    {
        /// <summary>Seriove cislo zarizeni; null = prvni dostupna kamera.</summary>
        string sn;

        private Pipeline pipeline;
        private PipelineProfile pipelineProfile;

        /// <summary>Prvni dostupna kamera.</summary>
        public T265TrackingCamera() : this(null)
        {
        }

        /// <summary>
        /// Kamera dle serioveho cisla. Hned nakonfiguruje a spusti snimani.
        /// </summary>
        /// <param name="sn">Seriove cislo zarizeni; null = prvni dostupne.</param>
        public T265TrackingCamera(string sn)
        {
            this.sn = sn;
            Init();
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro rotacni vektory.
        /// </summary>
        private Vector3 Angular2Vector3D(Intel.RealSense.Math.Vector v)
        {
            return new Vector3(-v.x, v.z, v.y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro pozicni vektory.
        /// </summary>
        private Vector3 Translation2Vector3D(Intel.RealSense.Math.Vector v)
        {
            return new Vector3(v.x, -v.z, v.y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro rotaci.
        /// </summary>
        private Quaternion ToQuaternion(Intel.RealSense.Math.Quaternion v)
        {
            return new Quaternion(v.x, -v.z, v.y, v.w);
        }

        /// <summary>
        /// Prevede tracker_confidence (0-3) z T265 na normalizovanou duveru 0-1.
        /// </summary>
        protected double ToConfidence(uint v)
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
        /// (Re)konfiguruje kameru (pose stream) a (znovu)spusti pipeline + pozadi task.
        /// </summary>
        private void Init()
        {
            if (IsRunning)
                Stop();

            var cfg = new Config();
            if (sn != null)
                cfg.EnableDevice(sn);
            cfg.EnableStream(Stream.Pose, Format.SixDOF);

            if (pipeline == null)
                pipeline = new Pipeline();
            else
                pipeline.Stop();

            pipelineProfile = pipeline.Start(cfg);

            Start();
        }

        /// <summary>
        /// Pocka na dalsi pose snimek z pipeline a vrati ho jako IMUState.
        /// Bookkeeping (FrameNum/periody) doplni SensorBase.
        /// </summary>
        protected override IMUState GetMeasurement()
        {
            using (var frames = pipeline.WaitForFrames())
            using (var pf = frames.PoseFrame)
            {
                var f = pf.PoseData;
                DateTime ts = D435Camera.CalcTimeStamp(pf.Timestamp);
                return new IMUState()
                {
                    Translation = Translation2Vector3D(f.translation),
                    Velocity = Translation2Vector3D(f.velocity),
                    Acceleration = Translation2Vector3D(f.acceleration),
                    Rotation = ToQuaternion(f.rotation),
                    AngularVelocity = Angular2Vector3D(f.angular_velocity),
                    AngularAcceleration = Angular2Vector3D(f.angular_acceleration),
                    TimeStamp = ts,
                    Confidence = ToConfidence(f.tracker_confidence)
                };
            }
        }

        /// <summary>
        /// Zastavi pozadi task (base) a uvolni pipeline (nativni prostredky kamery).
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
                pipeline?.Dispose();
        }
    }
}
