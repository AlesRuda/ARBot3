using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class EKFModelInput:Matrix
    {
        public EKFModelInput()
            : base(5, 1)
        {
        }

        /// <summary>
        /// Perioda vzorkovani
        /// </summary>
        public double Ts { get { return this[0, 0]; } set { this[0, 0] = value; } }
        /// <summary>
        /// Pozadovana leveho praveho kola v ms
        /// </summary>
        public double ReqLeftWheelVelocity { get { return this[1, 0]; } set { this[1, 0] = value; } }
        /// <summary>
        /// Pozadovana rychlost praveho kola v ms
        /// </summary>
        public double ReqRightWheelVelocity { get { return this[2, 0]; } set { this[2, 0] = value; } }
        /// <summary>
        /// Pitch - predozadni naklon v radianech
        /// </summary>
        public double Pitch { get { return this[3, 0]; } set { this[3, 0] = value; } }
        /// <summary>
        /// Akcelerace nastavena do ridici jednotky motoru m/(s^2).
        /// </summary>
        public double Acceleration { get { return this[4, 0]; } set { this[4, 0] = value; } }
    }
}
