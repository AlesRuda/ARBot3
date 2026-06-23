using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class EKFModel3Input : Matrix
    {
        public EKFModel3Input()
            : base(4, 1)
        {
        }

        /// <summary>
        /// Perioda vzorkovani v sekundach
        /// </summary>
        public double Ts { get { return this[0, 0]; } set { this[0, 0] = value; } }

        /// <summary>
        /// Pozadovana rychlost otaceni v rad/s
        /// </summary>
        public double ReqRotationSpeed { get { return this[1, 0]; } set { this[1, 0] = value; } }
        /// <summary>
        /// Pozadovana rychlost v m/s
        /// </summary>
        public double ReqSpeed { get { return this[2, 0]; } set { this[2, 0] = value; } }
        /// <summary>
        /// Pitch - predozadni naklon v radianech, 0 je vodorovne
        /// </summary>
        public double Pitch { get { return this[3, 0]; } set { this[3, 0] = value; } }

        /// <summary>
        /// Pravolevy naklon
        /// </summary>
        public double Roll { get; set; }

    }
}
