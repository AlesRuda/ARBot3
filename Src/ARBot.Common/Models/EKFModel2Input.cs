using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.Models
{
    public class EKFModel2Input : Matrix
    {
        public EKFModel2Input()
            : base(5, 1)
        {
        }

        /// <summary>
        /// Perioda vzorkovani v sekundach
        /// </summary>
        public double Ts { get { return this[0, 0]; } set { this[0, 0] = value; } }
        /// <summary>
        /// Cas v sekundech, jen pro vypocty. Lepe je pouzivat TimeStamp
        /// </summary>
        public double TimeStampSecs { get { return this[1, 0]; } set { this[1, 0] = value; } }
        /// <summary>
        /// Pitch - predozadni naklon v radianech, 0 je vodorovne
        /// </summary>
        public double Pitch { get { return this[2, 0]; } set { this[2, 0] = value; } }
        /// <summary>
        /// Cas vzorku
        /// </summary>
        public DateTime TimeStamp { get => DateTime.FromOADate(TimeStampSecs); set { TimeStampSecs = value.ToOADate(); } }
    }
}
