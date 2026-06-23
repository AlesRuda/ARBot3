using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class EKFModel2Measurement : Matrix
    {
        public EKFModel2Measurement()
            : base(13, 1)
        {
        }


        public double CompasOrientation { get { return Conversions.NormalizeOrientation(this[0, 0]); } set { this[0, 0] = Conversions.NormalizeOrientation(value); } }
        public double GPSOriantation { get { return Conversions.NormalizeOrientation(this[1, 0]); } set { this[1, 0] = Conversions.NormalizeOrientation(value); } }
        public double CameraOrientation { get { return Conversions.NormalizeOrientation(this[2, 0]); } set { this[2, 0] = Conversions.NormalizeOrientation(value); } }


        public double CompasRotationSpeed { get { return this[3, 0]; } set { this[3, 0] = value; } }
        public double OdometryRotationSpeed { get { return this[4, 0]; } set { this[4, 0] = value; } }
        public double TrackinkCameraRotationSpeed { get { return this[5, 0]; } set { this[5, 0] = value; } }



        public double OdometrySpeed { get { return this[6, 0]; } set { this[6, 0] = value; } }
        public double GPSSpeed { get { return this[7, 0]; } set { this[7, 0] = value; } }
        public double TrackinkCameraSpeed { get { return this[8, 0]; } set { this[8, 0] = value; } }


        public double LocalMap_X { get { return this[9, 0]; } set { this[9, 0] = value; } }
        public double LocalMap_Y { get { return this[10, 0]; } set { this[10, 0] = value; } }
        public double GPS_X { get { return this[11, 0]; } set { this[11, 0] = value; } }
        public double GPS_Y { get { return this[12, 0]; } set { this[12, 0] = value; } }


        /// <summary>
        /// Rozdil dvou mereni.
        /// Orientacni udaje se normalizuji v settru orientaci
        /// </summary>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static Matrix operator -(EKFModel2Measurement a, EKFModel2Measurement b)
        {
            EKFModel2Measurement x=new EKFModel2Measurement();
            x.CompasOrientation = Conversions.NormalizeOrientation(a.CompasOrientation - b.CompasOrientation);
            x.GPSOriantation = Conversions.NormalizeOrientation(a.GPSOriantation - b.GPSOriantation);
            x.CameraOrientation = Conversions.NormalizeOrientation(a.CameraOrientation - b.CameraOrientation);

            x.CompasRotationSpeed = a.CompasRotationSpeed - b.CompasRotationSpeed;
            x.OdometryRotationSpeed = a.OdometryRotationSpeed - b.OdometryRotationSpeed;
            x.TrackinkCameraRotationSpeed = a.TrackinkCameraRotationSpeed - b.TrackinkCameraRotationSpeed;

            x.OdometrySpeed = a.OdometrySpeed - b.OdometrySpeed;
            x.GPSSpeed = a.GPSSpeed - b.GPSSpeed;
            x.TrackinkCameraSpeed = a.TrackinkCameraSpeed - b.TrackinkCameraSpeed;

            x.LocalMap_X = a.LocalMap_X - b.LocalMap_X;
            x.LocalMap_Y = a.LocalMap_Y - b.LocalMap_Y;
            x.GPS_X = a.GPS_X - b.GPS_X;
            x.GPS_Y = a.GPS_Y - b.GPS_Y;

            return x;
        }
    }
}
