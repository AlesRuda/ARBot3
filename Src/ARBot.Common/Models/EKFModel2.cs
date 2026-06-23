using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Models
{
    public class EKFModel2 : EKF<EKFModel2State, EKFModel2Measurement, EKFModel2Input>
    {
        double rozchod;
        StateBase s;
        public EKFModel2(StateBase s, double rozchod)
        {

            MeasurementDescriptions= new string[]
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
                "X",
                "Y",
                "Speed",
                "Orient",
                "RS",
                "Time"
            };

            InputDescriptions = new string[]
            {
                "Ts",
                "Time",
                "Pitch"
            };


            this.s = s;
            this.rozchod = rozchod;
            // matice C je konstantni a tak ji stavi nastavit zde
            //            n.CompasOrientation = x.Orientation;
            Step.C[0, 3] = 1;

            //            n.GPSOriantation = x.Orientation;
            Step.C[1, 3] = 1;

            //            n.CameraOrientation = x.Orientation;
            Step.C[2, 3] = 1;

            //            n.CompasRotationSpeed = x.RotationSpeed;
            Step.C[3, 4] = 1;

            //            n.OdometryRotationSpeed = x.RotationSpeed;
            Step.C[4, 4] = 1;

            //            n.TrackinkCameraRotationSpeed = x.RotationSpeed;
            Step.C[5, 4] = 1;

            //            n.OdometrySpeed = x.Speed;
            Step.C[6, 2] = 1;

            //            n.GPSSpeed = x.Speed;
            Step.C[7, 2] = 1;

            //            n.TrackinkCameraSpeed = x.Speed;
            Step.C[8, 2] = 1;

            //            n.LocalMap_X = x.X;
            Step.C[9, 0] = 1;

            //            n.LocalMap_Y = x.Y;
            Step.C[10, 1] = 1;

            //            n.GPS_X = x.X;
            Step.C[11, 0] = 1;

            //            n.GPS_Y = x.Y;
            Step.C[12, 1] = 1;

        }

        protected override Matrix LinearizeM(EKFModel2State x, EKFModel2Input u)
        {
            double ts = u.Ts;
            double b = x.Orientation+ts * x.RotationSpeed/2;
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
            //          x[k] = x[k]+ ts * Speed[k] * Math.Cos(Orientation[k]+ts * RotationSpeed[k]/2);
            m[0, 0] = 1; //dx[k]/dx[k]
            m[0, 1] = 0; //dx[k]/dy[k]
            m[0, 2] = ts * Math.Cos(b); //dx[k]/dSpeed[k]
            m[0, 3] = -ts * x.Speed * Math.Sin(b); //dx[k]/dOrientation[k]
            m[0, 4] = -ts*ts * x.Speed * Math.Sin(b)/2; //dx[k]/dRotationSpeed[k]
            m[0, 5] = 0; //dx[k]/dTimeStampSecs[k]

            //          y[k] = y[k]+ ts * Speed[k] * Math.Sin(Orientation[k]+ts * RotationSpeed[k]/2);
            m[1, 0] = 0; //dy[k]/dx[k]
            m[1, 1] = 1; //dy[k]/dy[k]
            m[1, 2] = ts * Math.Sin(b); //dy[k]/dSpeed[k]
            m[1, 3] = ts * x.Speed * Math.Cos(b); //dy[k]/dOrientation[k]
            m[1, 4] = ts*ts * x.Speed * Math.Cos(b) / 2; //dy[k]/dRotationSpeed[k]
            m[4, 5] = 0; //dy[k]/dTimeStampSecs[k]

            //          Speed[k] = Speed[k];
            m[2, 0] = 0; //dSpeed[k]/dx[k]
            m[2, 1] = 0; //dSpeed[k]/dy[k]
            m[2, 2] = 1; //dSpeed[k]/dSpeed[k]
            m[2, 3] = 0; //dSpeed[k]/dOrientation[k]
            m[2, 4] = 0; //dSpeed[k]/dRotationSpeed[k]
            m[4, 5] = 0; //dSpeed[k]/dTimeStampSecs[k]

            //          Orientation[k] = Orientation[k] + ts * RotationSpeed[k];
            m[3, 0] = 0; //dOrientation[k]/dx[k]
            m[3, 1] = 0; //dOrientation[k]/dy[k]
            m[3, 2] = 0; //dOrientation[k]/dSpeed[k]
            m[3, 3] = 1; //dOrientation[k]/dOrientation[k]
            m[3, 4] = ts; //dOrientation[k]/dRotationSpeed[k]
            m[4, 5] = 0; //dOrientation[k]/dTimeStampSecs[k]

            //          RotationSpeed[k] = RotationSpeed[k];
            m[4, 0] = 0; //dRotationSpeed[k]/dx[k]
            m[4, 1] = 0; //dRotationSpeed[k]/dy[k]
            m[4, 2] = 0; //dRotationSpeed[k]/dSpeed[k]
            m[4, 3] = 0; //dRotationSpeed[k]/dOrientation[k]
            m[4, 4] = 1; //dRotationSpeed[k]/dRotationSpeed[k]
            m[4, 5] = 0; //dRotationSpeed[k]/dTimeStampSecs[k]

            //          TimeStampSecs[k] = TimeStampSecs[k];
            m[5, 0] = 0; //dTimeStampSecs[k]/dx[k]
            m[5, 1] = 0; //dTimeStampSecs[k]/dy[k]
            m[5, 2] = 0; //dTimeStampSecs[k]/dSpeed[k]
            m[5, 3] = 0; //dTimeStampSecs[k]/dOrientation[k]
            m[5, 4] = 0; //dTimeStampSecs[k]/dRotationSpeed[k]
            m[5, 5] = 1; //dTimeStampSecs[k]/dTimeStampSecs[k]

            return m;
        }

        protected override Matrix LinearizeC(EKFModel2State x, EKFModel2Input u)
        {
            return Step.C;
        }

        protected override Matrix Diff(EKFModel2Measurement x1, EKFModel2Measurement x2)
        {
            var d = x1 - x2;
            return d;
        }

        protected override EKFModel2State PredictState(EKFModel2State x, EKFModel2Input u)
        {
            EKFModel2State n = CreateState();
            double ts = u.Ts;
            double b = ts * x.RotationSpeed;
            n.Orientation = x.Orientation + b;
            n.RotationSpeed = x.RotationSpeed;
            n.Speed = x.Speed;
            n.TimeStamp = u.TimeStamp.AddSeconds(u.Ts);
            b = x.Orientation + b / 2;
            n.X = x.X + ts * x.Speed * Math.Cos(b);
            n.Y = x.Y + ts * x.Speed * Math.Sin(b);

            return n;
        }

        protected override EKFModel2Measurement CalcOutput(EKFModel2State x, EKFModel2Input u)
        {
            EKFModel2Measurement n = CreateMeasurement();

            n.CompasOrientation = x.Orientation;
            n.GPSOriantation = x.Orientation;
            n.CameraOrientation = x.Orientation;

            n.CompasRotationSpeed = x.RotationSpeed;
            n.OdometryRotationSpeed = x.RotationSpeed;
            n.TrackinkCameraRotationSpeed = x.RotationSpeed;

            n.OdometrySpeed = x.Speed;
            n.GPSSpeed = x.Speed;
            n.TrackinkCameraSpeed = x.Speed;

            n.GPS_X = x.X;
            n.GPS_Y = x.Y;

            n.LocalMap_X = x.X;
            n.LocalMap_Y = x.Y;

            return n;
        }

        /// <summary>
        /// Aktualizace stavu
        /// </summary>
        public void Update()
        {
            EKFModel2Input u = new EKFModel2Input() 
            { 
                Pitch = s.YPR != null ? s.YPR.Pitch : 0,
                Ts = s.Ts,
                TimeStamp=TimeBase.Now
            };
/*            EKFModel2Measurement yy = new EKFModel2Measurement();
            yy.in_Mat = CalcOutput(Step.PredictedState, u).in_Mat;
*/
            EKFModel2Measurement y = new EKFModel2Measurement()
            {
                CompasOrientation = s.CameraOrientation ?? double.NaN,
                GPSOriantation = s.GPSOriantation ?? double.NaN,
                CameraOrientation = s.CameraOrientation ?? double.NaN,

                CompasRotationSpeed = ((s.YPR?.Yaw - s.PreviousYPR?.Yaw) / s.Ts) ?? double.NaN,
                OdometryRotationSpeed = s.Motor != null ? (s.Motor.RightWheelSpeed - s.Motor.LeftWheelSpeed) / rozchod : double.NaN,
                TrackinkCameraRotationSpeed = s.TrackingCameraState?.AngularVelocity?.Z ?? double.NaN,

                OdometrySpeed = s.Motor != null ? (s.Motor.RightWheelSpeed + s.Motor.LeftWheelSpeed) / 2 : double.NaN,
                GPSSpeed = s.GPSSpeed ?? double.NaN,
                TrackinkCameraSpeed = s.TrackingCameraState?.Velocity?.Length ?? double.NaN,


                LocalMap_X = s.LocalMapRobotOff != null ? (Step.PredictedState.X + s.LocalMapRobotOff.X) : double.NaN,
                LocalMap_Y = s.LocalMapRobotOff != null ? (Step.PredictedState.Y + s.LocalMapRobotOff.Y) : double.NaN,
                GPS_X = s.GPS_Location?.X ?? double.NaN,
                GPS_Y = s.GPS_Location?.Y ?? double.NaN
            };
/*            y.Orientation = s.YPR != null ? Conversions.NormalizeOrientation(s.YPR.Yaw+Math.PI/2+deklinace) : PredictedState.Orientation;
            y.LeftWheelVelocity = s.Motor?.LeftWheelSpeed??0;
            y.RightWheelVelocity = s.Motor?.RightWheelSpeed??0;
            y.OpticalFlow1X = yy.OpticalFlow1X;
            y.OpticalFlow1Y = yy.OpticalFlow1Y;
            y.OpticalFlow2X = yy.OpticalFlow2X;
            y.OpticalFlow2Y = yy.OpticalFlow2Y;
            y.X = s.GPS_Location?.X??PredictedState.X;
            y.Y = s.GPS_Location?.Y??PredictedState.Y;
*/
            Update(y, u);
        }

        /// <summary>
        /// Vytvari stav modelu
        /// </summary>
        /// <returns></returns>
        public override EKFModel2State CreateState()
        {
            return new EKFModel2State();
        }

        public override EKFModel2Measurement CreateMeasurement()
        {
            return new EKFModel2Measurement();
        }

        public override EKFModel2Input CreateInput()
        {
            return new EKFModel2Input();
        }
    }
}
