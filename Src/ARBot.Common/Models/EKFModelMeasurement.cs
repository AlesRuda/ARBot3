using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class EKFModelMeasurement:Matrix
    {
        double rozchod;
        public EKFModelMeasurement(double rozchod)
            : base(9, 1)
        {
            this.rozchod = rozchod;
        }

        /// <summary>
        /// Rychlost leveho kola podle encoderu
        /// </summary>
        public double LeftWheelVelocity { get { return this[0, 0]; } set { this[0, 0] = value; } }
        /// <summary>
        /// Rychlost praveho kola podle encoderu
        /// </summary>
        public double RightWheelVelocity { get { return this[1, 0]; } set { this[1, 0] = value; } }
        /// <summary>
        /// X absolutni souradnice odvozena z GPS
        /// </summary>
        public double X { get { return this[2, 0]; } set { this[2, 0] = value; } }
        /// <summary>
        /// Y absolutni souradnice odvozena z GPS
        /// </summary>
        public double Y { get { return this[3, 0]; } set { this[3, 0] = value; } }
        /// <summary>
        /// Svetova orientace robotu v radianech. Tj. 0 na sever, 90 na vychod
        /// </summary>
        public double Azimut { get { return this[4, 0]; } set { this[4, 0] = value; } }

        /// <summary>
        /// Rychlost z 1. optical flow senzoru
        /// </summary>
        public double OpticalFlow1X { get { return this[5, 0]; } set { this[5, 0] = value; } }
        public double OpticalFlow1Y { get { return this[6, 0]; } set { this[6, 0] = value; } }

        /// <summary>
        /// Rychlost z 2. optical flow senzoru
        /// </summary>
        public double OpticalFlow2X { get { return this[7, 0]; } set { this[7, 0] = value; } }
        public double OpticalFlow2Y { get { return this[8, 0]; } set { this[8, 0] = value; } }


        /// <summary>
        /// Matematicka orientace robotu v radianech. 0 na vychod, 90 na sever
        /// </summary>
        public double Orientation { get { return Conversions.Azimut2Orientation(Azimut); } set { Azimut = Conversions.Orientation2Azimut(value); } }

        /// <summary>
        /// Rychlost otaceni robotu v matematickem smyslu a radianech
        /// </summary>
        public double OrientationVelocity { get { return (RightWheelVelocity - LeftWheelVelocity) / rozchod; } }
    }
}
