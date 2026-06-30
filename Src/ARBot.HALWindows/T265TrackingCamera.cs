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
    /// <summary>
    /// Ovladac sledovaci kamery Intel RealSense T265 (6DOF pose / IMU).
    /// Po vytvoreni bezi na pozadi task, ktery cte pose snimky z pipeline a posledni
    /// stav zpristupnuje pres GetLastMeasurement.
    /// </summary>
    public class T265TrackingCamera : IDisposable, IIMU
    {
        /// <summary>Pocitadlo zpracovanych snimku.</summary>
        int cnt = 0;
        /// <summary>Bezi prave zpracovavaci task?</summary>
        private bool processingIsRunning = false;
        private Task processingTask;
        CancellationTokenSource ctSource;
        /// <summary>Seriove cislo zarizeni; null = prvni dostupna kamera.</summary>
        string sn;
        bool disposed = false;
        /// <summary>Cas posledniho odebraneho snimku (pro vypocet periody odberu).</summary>
        DateTime? LastPickupTimeStamp = null;
        /// <summary>Cislo posledniho odebraneho snimku (pro detekci vypadku).</summary>
        uint? LastFrameNum = null;

        private Pipeline pipeline;
        private PipelineProfile pipelineProfile;

        /// <summary>Posledni zachyceny stav (sdileny mezi pozadim taskem a GetLastMeasurement).</summary>
        IMUState frame;
        /// <summary>Priznak, ze je k dispozici novy (jeste neodebrany) snimek.</summary>
        bool imageGrabed;

        /// <summary>
        /// Vyvolano po prichodu noveho mereni (v ramci zpracovani na pozadi).
        /// </summary>
        public event EventHandler<IMUState> MeasurementArived;

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
            imageGrabed = false;
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
        /// Lze volat opakovane - bezici zpracovani se pred rekonfiguraci zastavi a po ni obnovi.
        /// Potomci (napr. nativni varianta) mohou prepsat a pouzit vlastni zdroj dat.
        /// </summary>
        protected virtual void Init()
        {
            if (processingIsRunning)
                StopProcessing();

            var cfg = new Config();
            if (sn != null)
                cfg.EnableDevice(sn);
            cfg.EnableStream(Stream.Pose, Format.SixDOF);
            //            cfg.EnableStream(Stream.Fisheye, Format.Y8, 1);
            //          cfg.EnableStream(Stream.Fisheye, Format.Y8, 2);

            if (pipeline == null)
                pipeline = new Pipeline();
            else
                pipeline.Stop();

            pipelineProfile = pipeline.Start(cfg);

            Start();
        }

        /// <summary>
        /// Spusti zpracovani snimku na pozadi (volano z Init).
        /// </summary>
        private void Start()
        {
            if (!processingIsRunning)
            {
                ctSource = new CancellationTokenSource();
                processingTask = new Task(Process, ctSource.Token);
                processingIsRunning = true;
                processingTask.Start();
            }
        }

        /// <summary>
        /// Zastavi zpracovani na pozadi a pocka na dobehnuti tasku.
        /// </summary>
        private void StopProcessing()
        {
            if (processingIsRunning)
            {
                ctSource?.Cancel();
                try { processingTask?.Wait(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
                processingIsRunning = false;
            }
        }

        /// <summary>
        /// Pozadi smycka: cte pose snimky z pipeline a kazdy zapamatuje jako posledni stav.
        /// Bezi az do zruseni tokenu (StopProcessing/Dispose).
        /// </summary>
        private void Process()
        {
            try
            {
                DateTime? lastTS = null;
                while (!ctSource.IsCancellationRequested)
                {
                    using (var frames = pipeline.WaitForFrames())
                    {
                        IMUState current;
                        lock (this)
                        {
                            using (var pf = frames.PoseFrame)
                            {
                                var f = pf.PoseData;
                                cnt++;
                                DateTime ts = D435Camera.CalcTimeStamp(pf.Timestamp);
                                frame = new IMUState()
                                {
                                    Translation = Translation2Vector3D(f.translation),
                                    Velocity = Translation2Vector3D(f.velocity),
                                    Acceleration = Translation2Vector3D(f.acceleration),
                                    Rotation = ToQuaternion(f.rotation),
                                    AngularVelocity = Angular2Vector3D(f.angular_velocity),
                                    AngularAcceleration = Angular2Vector3D(f.angular_acceleration),
                                    //                                        MapperConfidence = ToConfidence(f.mapper_confidence),
                                    TimeStamp = ts,
                                    FrameNum = (uint)pf.Number,
                                    Confidence = ToConfidence(f.tracker_confidence),
                                    FrameReceivePeriod = ts - (lastTS ?? ts),
                                    FramePickupPeriod = ts - (LastPickupTimeStamp ?? ts),
                                    DropedOutNum = (uint)pf.Number - (LastFrameNum ?? (uint)pf.Number) - 1
                                };

                                lastTS = ts;
                                imageGrabed = true;
                                current = frame;
                            }
                        }

                        MeasurementArived?.Invoke(this, current);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex.ToString());
            }
            finally
            {
                processingIsRunning = false;
            }
        }

        /// <summary>
        /// Vraci posledni zachyceny stav. Opakovane volani bez prichodu noveho snimku vraci null.
        /// </summary>
        public virtual IMUState GetLastMeasurement()
        {
            lock (this)
            {
                if (imageGrabed)
                {
                    imageGrabed = false;
                    LastPickupTimeStamp = frame.TimeStamp;
                    LastFrameNum = frame.FrameNum;
                    return frame;
                }
                return null;
            }
        }

        /// <summary>
        /// Zastavi zpracovani a uvolni pipeline (nativni prostredky kamery). Idempotentni.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                StopProcessing();       // zrusi token a pocka na dobehnuti tasku
                ctSource?.Dispose();
                pipeline?.Dispose();    // uvolni nativni prostredky kamery
            }

            disposed = true;
        }

        ~T265TrackingCamera()
        {
            Dispose(false);
        }
    }
}
