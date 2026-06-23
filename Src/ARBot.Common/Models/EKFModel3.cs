using ARBot.Common.Common;
using ARBot.Common.Logs;
using ARBot.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Models
{
    public class EKFModel3 : EKF<EKFModel3State, EKFModel3Measurement, EKFModel3Input>, IModel
    {
        double maxRotAcceleration = 0;
        double maxAcceleration = 0;

        IModelState IModel.CurrentState => Step.CurrentState;

        IModelState IModel.PredictedState => Step.PredictedState;

        public EKFModel3(double maxRotAcc, double maxAcc)
        {
            Ar = 0.99;
            Set(Step.R, .10);
            Set(Step.R_Internal, .10);
            Set(Step.PredictedP, .1);
            Set(Step.CorrectedP, .1);


            maxRotAcceleration = maxRotAcc;
            maxAcceleration = maxAcc;
            MeasurementDescriptions = new string[]
            {
                "CompasO",
                "GPSO",
                "CameraO",
                "CompasRS",
                "OdometryRS",
                "TracCameraRS",
                "OdometrySpd",
                "GPSSpd",
                "TracCameraSpd",
                "LocalMap_X",
                "LocalMap_Y",
                "GPS_X",
                "GPS_Y"
            };

            StateDescriptions = new string[]
            {
                "Orient",
                "OrientSpeed",
                "Speed",
                "X",
                "Y",
            };

            InputDescriptions = new string[]
            {
                "Ts",
                "ReqOrientSpeed",
                "ReqSpeed",
                "Pitch"
            };


            // matice C je konstantni a tak ji staci nastavit zde
            //            n.CompasOrientation = x.Orientation;
            Step.C[0, 0] = 1;

            //            n.GPSOriantation = x.Orientation;
            Step.C[1, 0] = 1;

            //            n.CameraOrientation = x.Orientation;
            Step.C[2, 0] = 1;

            //            n.CompasRotationSpeed = x.RotationSpeed;
            Step.C[3, 1] = 1;

            //            n.OdometryRotationSpeed = x.RotationSpeed;
            Step.C[4, 1] = 1;

            //            n.TrackinkCameraRotationSpeed = x.RotationSpeed;
            Step.C[5, 1] = 1;

            //            n.OdometrySpeed = x.Speed;
            Step.C[6, 2] = 1;

            //            n.GPSSpeed = x.Speed;
            Step.C[7, 2] = 1;

            //            n.TrackinkCameraSpeed = x.Speed;
            Step.C[8, 2] = 1;

            //            n.LocalMap_X = x.X;
            Step.C[9, 3] = 1;

            // n.LocalMap_Y = x.Y;
            Step.C[10, 4] = 1;

            // n.GPS_X = x.X;
            Step.C[11, 3] = 1;

            // n.GPS_Y = x.Y;
            Step.C[12, 4] = 1;

        }

        protected override Matrix LinearizeM(EKFModel3State x, EKFModel3Input u)
        {
            double ts = u.Ts;
            double b = x.Orientation + ts * x.OrientationVelocity / 2;
            /*
            EKFModel2State n = CreateState();
            n.Orientation = x.Orientation + ts * x.RotationSpeed;
            n.RotationSpeed = x.RotationSpeed;
            n.Speed = x.Speed;
            n.TimeStamp = u.TimeStamp.AddSeconds(u.Ts);
            b = x.Orientation + ts * x.RotationSpeed / 2;
            n.X = x.X + ts * x.Speed * Math.Cos(b);
            n.Y = x.Y + ts * x.Speed * Math.Sin(b);
            */

            var m = new Matrix(x.NoRows, x.NoRows);
            Step.M = m;

            //          Orientation[k] = Orientation[k] +Ts[k]*RotationSpeed[k]
            m[0, 0] = 1; //dOrientation[k]/dOrientation[k]
            m[0, 1] = ts; //dOrientation[k]/dRotationSpeed[k]

            // n.OrientationVelocity = x.OrientationVelocity + ts * Limit((u.ReqRotationSpeed - x.OrientationVelocity) / ts, -maxRotAcceleration, maxRotAcceleration);
            m[1, 1] = Math.Abs(u.ReqRotationSpeed - x.OrientationVelocity) /u.Ts<maxRotAcceleration?0:1; //dRotationSpeed[k]/dRotationSpeed[k]

            //            n.Speed = x.Speed + u.Ts * Limit((u.ReqSpeed - x.Speed) / u.Ts, -maxAcceleration, maxAcceleration);
            m[2, 2] = 1; //dSpeed[k]/dSpeed[k]
                         // vypocet rychlost z pozadovane rychlosti ve vstupu
                         //            m[3, 3] = Math.Abs(u.ReqSpeed - x.Speed) / u.Ts < maxAcceleration ? 0 : 1; //dSpeed[k]/dSpeed[k]

            double s = ts * x.Velocity * Math.Cos(u.Pitch);
            // n.X = x.X + s * Math.Cos(b);
            m[3, 0] = -s * Math.Sin(b); //dx[k]/dOrientation[k]
            m[3, 1] = -ts * s * Math.Sin(b) / 2; //dx[k]/dRotationSpeed[k]
            m[3, 2] = ts * Math.Cos(u.Pitch) * Math.Cos(b); //dx[k]/dSpeed[k]
            m[3, 3] = 1; //dx[k]/dx[k]
            m[3, 4] = 0; //dx[k]/dy[k]

            // n.Y = x.Y + s * Math.Sin(b);
            m[4, 0] = s * Math.Cos(b); //dy[k]/dOrientation[k]
            m[4, 1] = ts * s * Math.Cos(b) / 2; //dy[k]/dRotationSpeed[k]
            m[4, 2] = ts * Math.Cos(u.Pitch) * Math.Sin(b); //dy[k]/dSpeed[k]
            m[4, 3] = 0; //dy[k]/dx[k]
            m[4, 4] = 1; //dy[k]/dy[k]

            return m;
        }

        protected override Matrix LinearizeC(EKFModel3State x, EKFModel3Input u)
        {
            return Step.C;
        }

        protected override Matrix Diff(EKFModel3Measurement x1, EKFModel3Measurement x2)
        {
            var r = new Random();

            // mapa dava jen opravy
            x2.LocalMap_X = 0;
            x2.LocalMap_Y = 0;
            var d = x1 - x2;
            //vyraazena rychlost z trekovaci kamery, byla moc mimo
            d[8, 0] = 0;

//            d[1, 0] = 0;
//            d[1, 0] += r.NextDouble();

//                      d[9, 0] = 0;
  //                    d[10, 0] = 0;
//                        d[11, 0] = 0;
  //                    d[12, 0] = 0;
            return d;
        }

        protected override void EstimateQ(EKFModel3Measurement y, EKFModel3Input u)
        {
            Step.Q[0, 0] = 0.01;
            Step.Q[1, 1] = 0.01;
            Step.Q[2, 2] = 0.010;
            Step.Q[3, 3] = 0.1;
            Step.Q[4, 4] = 0.1;
        }


        protected override void EstimateR(EKFModel3Measurement y, EKFModel3Input u)
        {
            base.EstimateR(y, u);
            //EstimateRAgg(y, u, 50);

            //            Step.R[4, 4] += 10000;
            //          Step.R[6, 6] += 10000;

// robotour

            Step.R[9, 9] += 1;
            Step.R[10, 10] += 1;
            Step.R[11, 11] += 1;
            Step.R[12, 12] += 1;


//roboorienteering
/*
            Step.R[9, 9] += 1;
            Step.R[10, 10] += 1;
            Step.R[11, 11] = 0.001;
            Step.R[12, 12] = 0.001;
*/
        }

        double Limit(double val, double min, double max)
        {
            return Math.Max(min, Math.Min(max, val));
        }

        protected override EKFModel3State PredictState(EKFModel3State x, EKFModel3Input u)
        {
            EKFModel3State n = CreateState();
            double ts = u.Ts;
            double b = ts * x.OrientationVelocity;
            n.Orientation = Conversions.NormalizeOrientation(x.Orientation+b);
            n.OrientationVelocity = x.OrientationVelocity + ts * Limit((u.ReqRotationSpeed - x.OrientationVelocity) / ts, -maxRotAcceleration, maxRotAcceleration);
            n.Velocity = x.Velocity;
            // vypocet rychlost z pozadovane rychlosti ve vstupu
            //            n.Speed = x.Speed + ts * Limit((u.ReqSpeed - x.Speed) / ts, -maxAcceleration, maxAcceleration);
            double s = ts * x.Velocity * Math.Cos(u.Pitch);
            b = x.Orientation + b / 2;
            n.X = x.X + s * Math.Cos(b);
            n.Y = x.Y + s * Math.Sin(b);

            n.Pitch = u.Pitch;
            n.Roll = u.Roll;

            return n;
        }

        protected override EKFModel3Measurement CalcOutput(EKFModel3State x, EKFModel3Input u)
        {
            EKFModel3Measurement n = CreateMeasurement();

            n.CompasOrientation = x.Orientation;
//            n.CompasOrientation = Conversions.NormalizeOrientation(x.Orientation+x.CompasOff);
            n.GPSOriantation = x.Orientation;
            n.CameraOrientation = x.Orientation;

            n.CompasRotationSpeed = x.OrientationVelocity;
            n.OdometryRotationSpeed = x.OrientationVelocity;
            n.TrackinkCameraRotationSpeed = x.OrientationVelocity;

            n.OdometrySpeed = x.Velocity;
            n.GPSSpeed = x.Velocity;
            n.TrackinkCameraSpeed = x.Velocity;

            n.LocalMap_X = x.X;
            n.LocalMap_Y = x.Y;

            n.GPS_X = x.X;
            n.GPS_Y = x.Y;


            return n;
        }

        /// <summary>
        /// Aktualizace stavu
        /// </summary>
        public void Update(ARBotState s)
        {
            EKFModel3Input u = new EKFModel3Input()
            {
                Ts = s.Ts,
                ReqRotationSpeed = s.ReqRotationSpeed,
                ReqSpeed = s.ReqSpeed,
                Pitch = Conversions.Deg2Rad(s.Pitch),
                Roll = Conversions.Deg2Rad(s.Roll)
        };
            /*            EKFModel2Measurement yy = new EKFModel2Measurement();
                        yy.in_Mat = CalcOutput(Step.PredictedState, u).in_Mat;
            */

            double speed = (s.RightWheelSpeed + s.LeftWheelSpeed) / 2;
//robotour, docasne vymeneno
            EKFModel3Measurement y = new EKFModel3Measurement()
            {
                CompasOrientation = Conversions.Azimut2Orientation(Conversions.Deg2Rad(-s.Yaw)),
                GPSOriantation = speed > 0.3 ? s.GPSOriantation ?? double.NaN : double.NaN,
                CameraOrientation = s.CameraOrientation ?? double.NaN,
                CompasRotationSpeed = s.CompasRotationSpeed ?? double.NaN,
                OdometryRotationSpeed = s.OdometryRotationSpeed ?? double.NaN,
                TrackinkCameraRotationSpeed = s.TrackinkCameraRotationSpeed ?? double.NaN,
                OdometrySpeed = (s.RightWheelSpeed + s.LeftWheelSpeed) / 2,
                // rychlost z GPS je promitnuta na povrh, melo by se delit cos(pitch)
                GPSSpeed = speed > 0.3 ? s.GPSSpeed ?? double.NaN : double.NaN,
                TrackinkCameraSpeed = s.TrackingCameraState?.Velocity?.Length ?? double.NaN,
                LocalMap_X = s.LocalMapCorrelX!=0? s.LocalMapCorrelX:double.NaN,
                LocalMap_Y = s.LocalMapCorrelY != 0 ? s.LocalMapCorrelY : double.NaN,
                GPS_X = s.GpsXY?.X ?? double.NaN,
                GPS_Y = s.GpsXY?.Y ?? double.NaN

            };

// robo orientiering
/*            EKFModel3Measurement y = new EKFModel3Measurement()
            {
                CompasOrientation = Conversions.Azimut2Orientation(Conversions.Deg2Rad(-s.Yaw)),
                GPSOriantation = speed > 0.3 ? s.GPSOriantation ?? double.NaN : double.NaN,
                CameraOrientation = s.CameraOrientation ?? double.NaN,
                CompasRotationSpeed = s.CompasRotationSpeed ?? double.NaN,
                OdometryRotationSpeed = double.NaN,
                TrackinkCameraRotationSpeed = s.TrackinkCameraRotationSpeed ?? double.NaN,
                OdometrySpeed = double.NaN,
                // rychlost z GPS je promitnuta na povrh, melo by se delit cos(pitch)
                GPSSpeed = double.NaN,
                TrackinkCameraSpeed = double.NaN,
                LocalMap_X = double.NaN,
                LocalMap_Y = double.NaN,
                GPS_X = s.GpsXY?.X ?? double.NaN,
                GPS_Y = s.GpsXY?.Y ?? double.NaN

            };
*/

            Update(y, u);
            Step.CurrentState.TimeStamp = TimeBase.Now;
            Step.PredictedState.TimeStamp = Step.CurrentState.TimeStamp + new TimeSpan((int)(s.Ts * 10000000));

        }

        /// <summary>
        /// Aktualizace stavu
        /// </summary>
        public void Update(StateBase s)
        {
            EKFModel3Input u = new EKFModel3Input()
            {
                Ts = s.Ts,
                ReqRotationSpeed = s.ReqRotationSpeed,
                ReqSpeed = s.ReqSpeed,
                Pitch = s.YPR?.Pitch ?? 0,
                Roll = s.YPR?.Roll ?? 0
            };

            var speed = ((s.Motor?.RightWheelSpeed ?? 0) + (s.Motor?.LeftWheelSpeed ?? 0)) / 2;

            EKFModel3Measurement y = new EKFModel3Measurement()
            {
                CompasOrientation = (s.YPR?.Yaw + Math.PI / 2) ?? double.NaN,
                GPSOriantation = speed > 0.3 ? s.GPSOriantation ?? double.NaN : double.NaN,
                CameraOrientation = s.CameraOrientation ?? double.NaN,
                CompasRotationSpeed = s.CompasRotationSpeed ?? double.NaN,
                OdometryRotationSpeed = s.OdometryRotationSpeed ?? double.NaN,
                TrackinkCameraRotationSpeed = s.TrackingCameraState?.AngularVelocity?.Z ?? double.NaN,
                OdometrySpeed = speed,
                GPSSpeed = speed > 0.3?s.GPSSpeed ?? double.NaN:double.NaN,
                TrackinkCameraSpeed = s.TrackingCameraState?.Velocity?.Length ?? double.NaN,
                LocalMap_X = s.LocalMapCorrelX ?? double.NaN,
                LocalMap_Y = s.LocalMapCorrelY ?? double.NaN,
                GPS_X = s.GPS_Location?.X ?? double.NaN,
                GPS_Y = s.GPS_Location?.Y ?? double.NaN

            };

            Update(y, u);
            Step.CurrentState.TimeStamp = TimeBase.Now;
            Step.PredictedState.TimeStamp = Step.CurrentState.TimeStamp + new TimeSpan((int)(s.Ts * 10000000));
        }

        /// <summary>
        /// Vytvari stav modelu
        /// </summary>
        /// <returns></returns>
        public override EKFModel3State CreateState()
        {
            return new EKFModel3State();
        }

        public override EKFModel3Measurement CreateMeasurement()
        {
            return new EKFModel3Measurement();
        }

        public override EKFModel3Input CreateInput()
        {
            return new EKFModel3Input();
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
            Step.CurrentState.Orientation = orientation;
            Step.CurrentState.X = x;
            Step.CurrentState.Y = y;
            Step.CurrentState.Velocity = 0;
            Step.CurrentState.OrientationVelocity = 0;

            Step.PredictedState.Orientation = orientation;
            Step.PredictedState.X = x;
            Step.PredictedState.Y = y;
            Step.PredictedState.Velocity = 0;
            Step.PredictedState.OrientationVelocity = 0;

            Step.PrevState.Orientation = orientation;
            Step.PrevState.X = x;
            Step.PrevState.Y = y;
            Step.PrevState.Velocity = 0;
            Step.PrevState.OrientationVelocity = 0;
        }

        public EKFStepMsg ToLogMessage()
        {
            return Step.ToLogMessage();
        }

    }
}
