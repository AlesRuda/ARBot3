using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;
using ARBot.Common.Algorithms.Statistic;
using ARBot.Common.Common;
using ARBot.Common.Logs;
using ARBot.Driver;

namespace ARBot.Common.Models
{
    /// <summary>
    /// Jednoduchy model robotu.
    /// Z akcnich zasahu a mereni pocita stav robotu.
    /// </summary>
    public class SimpleModel : IModel
    {
        double a;
        double rozchod;
        double deklinace;
        IDataFusor fusor;

        double? lastTrackingCameraOrintation;
        //        Vector3D? lastTrackingCameraPosition;
        double? lastCompassOrintation;

        public SimpleModel(double acceleration, double rozchod, double deklinace)
        {
            this.a = acceleration;
            this.rozchod = rozchod;
            this.deklinace = deklinace;
            PredictedState = CurrentState = CreateState();
            fusor = new MedianDataFusor();
        }


        /// <summary>
        /// Aktualizace stavu
        /// </summary>
        public void Update(StateBase s)
        {
            double? trackingOrintation = null;
            if (s.TrackingCameraState != null && s.TrackingCameraState.Confidence == 1)
            {
                trackingOrintation = s.TrackingCameraState?.YPR()?.Yaw;
            }

            double ts = s.Ts;
            double pitch = s.YPR != null ? s.YPR.Pitch : 0;

            double? yaw = s.YPR != null ? Conversions.NormalizeOrientation(s.YPR.Yaw + Math.PI / 2 - deklinace) : (double?)null;
            double leftWheelVelocity = s.Motor?.LeftWheelSpeed ?? 0;
            double rightWheelVelocity = s.Motor?.RightWheelSpeed ?? 0;
            double orientationVelocity = (rightWheelVelocity - leftWheelVelocity) / rozchod;

            double speed = (leftWheelVelocity + rightWheelVelocity) / 2;
            double d = speed * ts * Math.Cos(pitch); // vzdalenost na mape
            double old = CurrentState.Orientation;

            var tf = new List<double>();
            // zmena smeru urcena z odometrie
            if (s.Motor != null)
            {
                tf.Add(Conversions.NormalizeOrientation(ts * orientationVelocity));
                //   Debug.WriteLine(string.Format("Motor {0}", tf[tf.Count - 1]));
            }
            // zmena smeru urcena z tracking kamery
            if (trackingOrintation != null && lastTrackingCameraOrintation != null)
            {
                tf.Add(Conversions.NormalizeOrientation(trackingOrintation.Value - lastTrackingCameraOrintation.Value));
                // Debug.WriteLine(string.Format("Tracking camera {0}", tf[tf.Count-1]));
            }
            // zmena smeru z kompasu
            if (lastCompassOrintation != null && yaw != null)
            {
                tf.Add(Conversions.NormalizeOrientation(yaw.Value - lastCompassOrintation.Value));
                //Debug.WriteLine(string.Format("Comapss {0}", tf[tf.Count - 1]));
            }
            // jeste by tu mohl byt odhad modelu

            double rotDif = (tf.Count > 0 ? fusor.Fusion(tf.ToArray()) : 0);
            rotDif = (yaw != null ? Conversions.NormalizeOrientation(yaw.Value - PredictedState.Orientation - rotDif) / 100 : 0) // tohle zpusobi pomalou konvergenci k udaji z kompasu
                + rotDif;
            //Debug.WriteLine(string.Format("rotDif {0}", (tf.Count > 0 ? fusor.Fusion(tf.ToArray()) : 0)));

            double o = old + rotDif;
            double z = old + Conversions.NormalizeOrientation(rotDif) / 2; // to deleni dvema je dano aproximaci pohybu robotu viz. odvozeni ve wordu

            CurrentState.LeftWheelVelocity = leftWheelVelocity;
            CurrentState.RightWheelVelocity = rightWheelVelocity;
            CurrentState.Orientation = o;
            CurrentState.Roll = s.YPR != null ? s.YPR.Roll : 0;
            CurrentState.Pitch = s.YPR != null ? s.YPR.Pitch : 0;

            Vector2D v = new Vector2D(d * Math.Cos(z), d * Math.Sin(z));
            if (s.GPS_Location != null)
            {
                // oprava pozice pomoci udaju z GPS
                // pouziju jen 1/1000 rozdilu vzdalenosti GPS a robotu
                v += new Vector2D(
                    Math.Abs(Math.Cos(CurrentState.Orientation)) * (s.GPS_Location.Value.X - CurrentState.X),
                    Math.Abs(Math.Sin(CurrentState.Orientation)) * (s.GPS_Location.Value.Y - CurrentState.Y)) / 1000;
            }

            CurrentState.X += v.X;
            CurrentState.Y += v.Y;

            CurrentState = CurrentState.Clone();
            PredictedState = CurrentState.Clone();

            z = old + 2 * Conversions.NormalizeOrientation(rotDif) / 2; // to deleni dvema je dano aproximaci pohybu robotu viz. odvozeni ve wordu

            PredictedState.X += d * Math.Cos(z);
            PredictedState.Y += d * Math.Sin(z);
            PredictedState.Orientation = z;

            CurrentState.TimeStamp = TimeBase.Now;
            PredictedState.TimeStamp = CurrentState.TimeStamp + new TimeSpan((int)(ts * 10000000));

            lastTrackingCameraOrintation = trackingOrintation;
            lastCompassOrintation = yaw;
        }


        /// <summary>
        /// Aktualizace stavu
        /// </summary>
        public void Update(ARBotState s)
        {
            double? trackingOrintation = null;
            if (s.TrackingCameraState != null && s.TrackingCameraState.Confidence == 1)
            {
                trackingOrintation = s.TrackingCameraState?.YPR()?.Yaw;
            }

            double ts = s.Ts;
            double pitch = Conversions.Deg2Rad(s.Pitch);

            double? yaw = Conversions.Azimut2Orientation(Conversions.Deg2Rad(-s.Yaw));
            double leftWheelVelocity = s.LeftWheelSpeed;
            double rightWheelVelocity = s.RightWheelSpeed;
            double orientationVelocity = (rightWheelVelocity - leftWheelVelocity) / rozchod;

            double speed = (leftWheelVelocity + rightWheelVelocity) / 2;
            double d = speed * ts * Math.Cos(pitch); // vzdalenost na mape
            double old = CurrentState.Orientation;

            var tf = new List<double>();
            // zmena smeru urcena z odometrie
            tf.Add(Conversions.NormalizeOrientation(ts * orientationVelocity));
                //   Debug.WriteLine(string.Format("Motor {0}", tf[tf.Count - 1]));
            // zmena smeru urcena z tracking kamery
            if (trackingOrintation != null && lastTrackingCameraOrintation != null)
            {
                tf.Add(Conversions.NormalizeOrientation(trackingOrintation.Value - lastTrackingCameraOrintation.Value));
                // Debug.WriteLine(string.Format("Tracking camera {0}", tf[tf.Count-1]));
            }
            // zmena smeru z kompasu
            if (lastCompassOrintation != null && yaw != null)
            {
                tf.Add(Conversions.NormalizeOrientation(yaw.Value - lastCompassOrintation.Value));
                //Debug.WriteLine(string.Format("Comapss {0}", tf[tf.Count - 1]));
            }
            // jeste by tu mohl byt odhad modelu

            double rotDif = (tf.Count > 0 ? fusor.Fusion(tf.ToArray()) : 0);
            rotDif = (yaw != null ? Conversions.NormalizeOrientation(yaw.Value - PredictedState.Orientation - rotDif) / 100 : 0) // tohle zpusobi pomalou konvergenci k udaji z kompasu
                + rotDif;
            //Debug.WriteLine(string.Format("rotDif {0}", (tf.Count > 0 ? fusor.Fusion(tf.ToArray()) : 0)));

            double o = old + rotDif;
            double z = old + Conversions.NormalizeOrientation(rotDif) / 2; // to deleni dvema je dano aproximaci pohybu robotu viz. odvozeni ve wordu

            CurrentState.LeftWheelVelocity = leftWheelVelocity;
            CurrentState.RightWheelVelocity = rightWheelVelocity;
            CurrentState.Orientation = o;
            CurrentState.Roll = Conversions.Deg2Rad(s.Roll);
            CurrentState.Pitch = Conversions.Deg2Rad(s.Pitch);

            Vector2D v = new Vector2D(d * Math.Cos(z), d * Math.Sin(z));
            if (s.GpsX != null && s.GpsY != null)
            {
                // oprava pozice pomoci udaju z GPS
                // pouziju jen 1/1000 rozdilu vzdalenosti GPS a robotu
                v += new Vector2D(
                    Math.Abs(Math.Cos(CurrentState.Orientation)) * (s.GpsX.Value - CurrentState.X),
                    Math.Abs(Math.Sin(CurrentState.Orientation)) * (s.GpsY.Value - CurrentState.Y)) / 1000;
            }

            CurrentState.X += v.X;
            CurrentState.Y += v.Y;

            CurrentState = CurrentState.Clone();
            PredictedState = CurrentState.Clone();

            z = old + 2 * Conversions.NormalizeOrientation(rotDif) / 2; // to deleni dvema je dano aproximaci pohybu robotu viz. odvozeni ve wordu

            PredictedState.X += d * Math.Cos(z);
            PredictedState.Y += d * Math.Sin(z);
            PredictedState.Orientation = z;

            CurrentState.TimeStamp = TimeBase.Now;
            PredictedState.TimeStamp = CurrentState.TimeStamp + new TimeSpan((int)(ts * 10000000));

            lastTrackingCameraOrintation = trackingOrintation;
            lastCompassOrintation = yaw;
        }


        public ModelState CurrentState
        {
            get;
            set;
        }

        public ModelState PredictedState
        {
            get;
            private set;
        }
        IModelState IModel.CurrentState => CurrentState;

        IModelState IModel.PredictedState => PredictedState;

        /// <summary>
        /// Vytvari stav modelu
        /// </summary>
        /// <returns></returns>
        public ModelState CreateState()
        {
            return new ModelState(rozchod);
        }

        IModelState IModel.CreateState()
        {
            return CreateState();
        }

        /// <summary>
        /// Nastavuje pozici robata
        /// </summary>
        /// <param name="orientation"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        public void SetOrietantionPosition(double orientation, double x, double y)
        {
            CurrentState.Orientation = orientation;
            CurrentState.X = x;
            CurrentState.Y = y;
        }
        public EKFStepMsg ToLogMessage()
        {
            return new EKFStepMsg()
            {
                CurrentState = CurrentState,
                PredictedState = PredictedState
            };
        }
    }
}
