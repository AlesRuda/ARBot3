using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.ComponentModel;
using ARBot.Common.Coordinates;
using System.IO;
using ARBot.Common.Common;
using ARBot.Common.Models;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class State:Message
    {
        public class SonarState
        {
            /// <summary>
            /// Id sonaru
            /// </summary>
            public int ID { get; set; }
            /// <summary>
            /// Vzdalenost v metrech
            /// </summary>
            public double Distance { get; set; }
            /// <summary>
            /// Orientace vzhledem k robotu, pouziva matematicky smer
            /// </summary>
            public double Orientation { get; set; }
            /// <summary>
            /// Orientace vzhledem k robotu, pouziva matematicky smer
            /// </summary>
            public double OrientationDeg { get { return -Conversions.Rad2Deg(Orientation); } }
            /// <summary>
            /// Sirka zaberu sonaru
            /// </summary>
            public double BeamAngle { get; set; }
        }

        public State() : base("State", 7)
        {
        }

        /// <summary>
        /// Zasova znacka porizeni zaznamu
        /// </summary>
        public DateTime TimeStamp { get; set; }
        public double ReqSpeed { get; set; }
        /// <summary>
        /// Pozadovana uhlova rychlost v rad/s
        /// </summary>
        public double ReqRotationSpeed { get; set; }
        /// <summary>
        /// Zrychleni pozadovane uhlove rychlosti v rad/s^2
        /// </summary>
        public double ReqRotationAcceleration { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>
        /// Matematicka orientace robotu v radianech. 0 na vychod, PI/2 na sever
        /// </summary>
        public double Orientation { get; set; }
        public double Speed { get; set; }
        public double UjetaVzdalenost { get; set; }
        public int Time { get; set; }
        public double Ts { get; set; }
        /// <summary>
        /// Orintace robotu vzhledem k severu v radianech, roste v matematickem smeru.
        /// </summary>
        public double Yaw { get; set; }
        /// <summary>
        /// predozadni naklon v radianech, roste smerem nahoru
        /// </summary>
        public double Pitch { get; set; }
        /// <summary>
        /// Pravolevy naklon v radianech, roste doprava
        /// </summary>
        public double Roll { get; set; }

        /// <summary>
        /// Orientace robotu spoctena ze smeru cesty v obraze a smeru cesty podle mapy.
        /// </summary>
        public double? CameraOrientation { get; set; }
        /// <summary>
        /// Smer cesty k cili ve svetove orientaci v radianech
        /// </summary>
        public double? WayOrientation;
        /// <summary>
        /// Oprava pozice robotu ve normalovem smeru k smeru cesty.
        /// </summary>
        public double? RobotWayOffset;
        /// <summary>
        /// Orientace kamery relativne k ceste v radianech a matematickem smyslu.
        /// </summary>
        public double? CameraToWayOrientation;

        /// <summary>
        /// Rotacni rychlost spoctena z odometrie v rad/s
        /// </summary>
        public double? OdometryRotationSpeed { get; set; }

        /// <summary>
        /// Rotacni rychlost spoctena z kompasu v rad/s
        /// </summary>
        public double? CompasRotationSpeed { get; set; }

        /// <summary>
        /// Rotacni rychlost spoctena z trekovaci kamery v rad/s
        /// </summary>
        public double? TrackinkCameraRotationSpeed { get; set; }

        /// <summary>
        /// Rychlost urcena z trekovaci kamery v m/s
        /// </summary>
        public double? TrackinkCameraSpeed { get; set; }

        /// <summary>
        /// [x, y] souradnice robotu urcena z GPS
        /// </summary>
        public Point2D? GPSLocation { get; set; }

        /// <summary>
        /// Smer urceny z GPS, udaj z VTG zpravy.
        /// Orientace v radianech a matematickem smyslu.
        /// </summary>
        public double? GPSOriantation { get; set; }
        /// <summary>
        /// Rychlost urceny z GPS v m/s, udaj z VTG zpravy
        /// </summary>
        public double? GPSSpeed { get; set; }

        public int GPSFix { get; set; }
        public int GPSFlags { get; set; }
        public double GPSLatitude { get; set; }
        public double GPSLongitude { get; set; }
        public double GPSAltitude { get; set; }
        public double GPSpDOP { get; set; }
        public int GPSnumSV { get; set; }
        public DateTime? GPSUTC { get; set; }
        public double LeftWheelSpeed { get; set; }
        public double RightWheelSpeed { get; set; }
        public double MotorsVoltage { get; set; }
        public double LeftMotorsCurrent { get; set; }
        public double RightMotorsCurrent { get; set; }
        public double RefLatitude { get; set; }
        public double RefLongitude { get; set; }
        public double ReqRotation { get; set; }
        public long PointIndex { get; set; }
        public long WayIndex { get; set; }
        public double TargetDistance { get; set; }
        public double PointDistance { get; set; }
        public double WayDistance { get; set; }
        public double LocalMapCorrelX { get; set; }
        public double LocalMapCorrelY { get; set; }
        public double CPUUtilization { get; set; }
        public IMUState TrackingCametaState { get; set; }
        public IMUState IMUState { get; set; }

        public SonarState[] Sonars { get; set; }

        public override string ToString()
        {
            return string.Format("T={3}, X={0}, Y={1}, Orientation={2}", X, Y, Orientation / Math.PI * 180, Time);
        }

        public override void ToData(BinaryWriter bw)
        {
            if (Verze >= 7)
                Write(bw, TimeStamp);
            if (Verze == 2 || Verze >= 5)
                Write(bw, IMUState);

            if (Verze == 3 || Verze >= 5)
            {
                Write(bw, CameraOrientation);
                Write(bw, WayOrientation);
                Write(bw, RobotWayOffset);
                Write(bw, CameraToWayOrientation);
                Write(bw, OdometryRotationSpeed);
                Write(bw, CompasRotationSpeed);
                Write(bw, TrackinkCameraRotationSpeed);
                Write(bw, TrackinkCameraSpeed);
                Write(bw, GPSOriantation);
                Write(bw, GPSSpeed);
            }

            if(Verze >= 4)
            {
                bw.Write(ReqRotationAcceleration);
            }

            if (Verze >= 6)
            {
                Write(bw, GPSLocation);
            }

            Write(bw, TrackingCametaState);
            bw.Write(ReqSpeed);
            bw.Write(ReqRotationSpeed);
            bw.Write(X);
            bw.Write(Y);
            bw.Write(Orientation);
            bw.Write(Speed);
            bw.Write(UjetaVzdalenost);
            bw.Write(Time);
            bw.Write(Ts);
            bw.Write(Yaw);
            bw.Write(Pitch);
            bw.Write(Roll);
            bw.Write(GPSFix);
            bw.Write(GPSFlags);
            bw.Write(GPSLatitude);
            bw.Write(GPSLongitude);
            bw.Write(GPSAltitude);
            bw.Write(GPSpDOP);
            bw.Write(GPSnumSV);
            bw.Write(GPSUTC.HasValue ? GPSUTC.Value.Ticks : 0);
            bw.Write(LeftWheelSpeed);
            bw.Write(RightWheelSpeed);
            bw.Write(MotorsVoltage);
            bw.Write(LeftMotorsCurrent);
            bw.Write(RightMotorsCurrent);
            bw.Write(RefLatitude);
            bw.Write(RefLongitude);
            if (Verze == 1)
            {
                bw.Write((double)0.0);
                bw.Write((double)0.0);
            }
            bw.Write(ReqRotation);
            bw.Write(PointIndex);
            bw.Write(WayIndex);
            bw.Write(TargetDistance);
            bw.Write(PointDistance);
            bw.Write(WayDistance);
            bw.Write(LocalMapCorrelX);
            bw.Write(LocalMapCorrelY);
            bw.Write(CPUUtilization);

            bw.Write(Sonars != null ? Sonars.Length : 0);
            if (Sonars != null)
            {
                foreach (SonarState ss in Sonars)
                {
                    bw.Write(ss.ID);
                    bw.Write(ss.Distance);
                    bw.Write(ss.Orientation);
                    bw.Write(ss.BeamAngle);
                }
            }
        }

        public override void FromData(BinaryReader br)
        {
            if (Verze >= 7)
                TimeStamp = ReadDateTime(br);
            if (Verze == 2 || Verze >= 5)
                IMUState= ReadIMUState(br);
            if (Verze == 3 || Verze >= 5)
            {
                CameraOrientation = ReadDouble(br);

                WayOrientation = ReadDouble(br);
                RobotWayOffset = ReadDouble(br);
                CameraToWayOrientation = ReadDouble(br);

                OdometryRotationSpeed = ReadDouble(br);
                CompasRotationSpeed = ReadDouble(br);
                TrackinkCameraRotationSpeed = ReadDouble(br);
                TrackinkCameraSpeed = ReadDouble(br);
                GPSOriantation = ReadDouble(br);
                GPSSpeed=ReadDouble(br);
            }

            if(Verze >= 4)
            {
                ReqRotationAcceleration= br.ReadDouble(); 
            }

            if (Verze >= 6)
            {
                GPSLocation = ReadNullablePoint2D(br);
            }

            TrackingCametaState = ReadIMUState(br);

            ReqSpeed = br.ReadDouble();
            ReqRotationSpeed = br.ReadDouble();
            X = br.ReadDouble();
            Y = br.ReadDouble();
            Orientation = br.ReadDouble();
            Speed = br.ReadDouble();
            UjetaVzdalenost = br.ReadDouble();
            Time = br.ReadInt32();
            Ts = br.ReadDouble();
            Yaw = br.ReadDouble();
            Pitch = br.ReadDouble();
            Roll = br.ReadDouble();
            GPSFix = br.ReadInt32();
            GPSFlags = br.ReadInt32();
            GPSLatitude = br.ReadDouble();
            GPSLongitude = br.ReadDouble();
            GPSAltitude = br.ReadDouble();
            GPSpDOP = br.ReadDouble();
            GPSnumSV = br.ReadInt32();
            long l = br.ReadInt64();
            GPSUTC = l == 0 ? null : (DateTime?)new DateTime(l);
            LeftWheelSpeed = br.ReadDouble();
            RightWheelSpeed = br.ReadDouble();
            MotorsVoltage = br.ReadDouble();
            LeftMotorsCurrent = br.ReadDouble();
            RightMotorsCurrent = br.ReadDouble();
            RefLatitude = br.ReadDouble();
            RefLongitude = br.ReadDouble();
            if (Verze == 1)
            {
                br.ReadDouble();
                br.ReadDouble();
            }
            ReqRotation = br.ReadDouble();
            PointIndex = br.ReadInt64();
            WayIndex = br.ReadInt64();
            TargetDistance = br.ReadDouble();
            PointDistance = br.ReadDouble();
            WayDistance = br.ReadDouble();
            LocalMapCorrelX = br.ReadDouble();
            LocalMapCorrelY = br.ReadDouble();
            CPUUtilization = br.ReadDouble();

            int cnt = br.ReadInt32();
            Sonars = new SonarState[cnt];
            for (int i = 0; i < cnt; i++)
            {
                SonarState ss = new SonarState();
                ss.ID = br.ReadInt32();
                ss.Distance = br.ReadDouble();
                ss.Orientation = br.ReadDouble();
                ss.BeamAngle = br.ReadDouble();
                Sonars[i] = ss;
            }
        }

        public override Message Build()
        {
            var s= new State();
            s.MsgName = MsgName;
            return s;
        }
    }
}
