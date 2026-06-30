using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.LocalMaps;
using ARBot.Common.Logs;
using ARBot.Common.Maps;
using ARBot.Common.Navigations;

namespace ARBot.Common.Models
{
    public class StateBase
    {
        public double Rozchod { get; private set; }

        /// <summary>
        /// Orientace robotu spoctena ze smeru cesty a videne cesty v kamere.
        /// V radianech a matematickem smyslu.
        /// </summary>
        public double? CameraOrientation;
        /// <summary>
        /// Smer cesty k cili ve svetove orientaci v radianech
        /// </summary>
        public double? WayOrientation;
        /// <summary>
        /// Oprava pozice robotu v normalovem smeru k smeru cesty.
        /// </summary>
        public double? RobotWayOffset;
        /// <summary>
        /// Orientace robotu spoctena z kamery relativne k ceste v radianech a matematickem smyslu.
        /// </summary>
        public double? CameraToWayOrientation;

        /// <summary>
        /// Orientace robotu urcena z GPS v radianech a matematickem smyslu.
        /// </summary>
        public virtual double? GPSOriantation => null;

        /// <summary>
        /// Rychlost robotu urcena z GPS v m/s.
        /// </summary>
        public virtual double? GPSSpeed => null;

        /// <summary>
        /// Oprava pozice robotu urcena z mereni okraju cesty v kamere a mapy
        /// </summary>
        public Vector2D LocalMapRobotOff;

        public double? LocalMapCorrelX, LocalMapCorrelY;


        public IMotorState Motor { get; set; }
        public YawPitchRoll YPR { get; set; }
        public YawPitchRoll PreviousYPR { get; set; }
        public IMUState IMU { get; set; }
        public Point2D? GPS_Location { get; set; }
        public IMUState TrackingCameraState { get; set; }
        /// <summary>
        /// Vzorkovaci perioda
        /// </summary>
        public double Ts { get; set;}

        public double ReqLeftMotorSpeed { get { return ReqSpeed - ReqRotationSpeed * Rozchod / 2.0; } }
        public double ReqRightMotorSpeed { get { return ReqSpeed + ReqRotationSpeed * Rozchod / 2.0; } }

        /// <summary>
        /// Pozadovana rychlost robotu v m/s
        /// </summary>
        public double ReqSpeed { get; set; }
        /// <summary>
        /// Pozadovana rychlost otaceni robotu v radianech/s, kladne v protismeru hodinovych rucicek
        /// </summary>
        public double ReqRotationSpeed { get; set; }
        /// <summary>
        /// Zrychleni pozadovane uhlove rychlosti v rad/s^2
        /// </summary>
        public double ReqRotationAcceleration { get; set; }

        public double PreviosReqRotationSpeed { get; set; }

        /// <summary>
        /// Rychlost rotace podle kompasu v radianech a matematickem smyslu
        /// </summary>
        public double? CompasRotationSpeed => YPR != null && PreviousYPR != null ? Conversions.NormalizeOrientation(YPR.Yaw - PreviousYPR.Yaw) / Ts : (double?)null;


        public double? OdometryRotationSpeed => Motor != null ? (Motor.RightWheelSpeed - Motor.LeftWheelSpeed) / Rozchod : (double?)null;

        public StateBase(double rozchod)
        {
            Rozchod = rozchod;
        }

        public virtual void Read(int msPeriod)
        {
            Ts = msPeriod / 1000.0;
        }
    }
}
