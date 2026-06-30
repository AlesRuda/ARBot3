using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Media3D;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Rozsirena informace o mereni lidaru
    /// </summary>
    /// <remarks>
    /// Obsahuje kompenzaci na pohyb a orientaci robotu.
    /// Spocitana X, Y mista prekazek.
    /// </remarks>
    public class RayEx
    {
        /// <summary>
        /// Mereny
        /// </summary>
        public Ray Ray;
        /// <summary>
        /// Uhel vzorku v radianech v matematickem smeru a svetove orientaci (0 na vychod)
        /// Pocitano proti aktualni pozici robota
        /// </summary>
        public double Angle;

        /// <summary>
        /// Uhel vzorku v radianech v matematickem smeru a orientaci vhledem k robotu (0 pred robotem)
        /// Pocitano proti aktualni pozici robota
        /// </summary>
        public double LocalAngle;

        /// <summary>
        /// Vzdalenost proti aktualni pozici robota
        /// </summary>
        public double? Distance;

        /// <summary>
        /// Souradnice lidaru v okamziku Ray.TimeStamp
        /// </summary>
        public Vector3D OriginalLidar;

        /// <summary>
        /// Souradnice vzorku v globalnich souradnicich
        /// </summary>
        public Vector3D? Target;

        /// <summary>
        /// Souradnice vzorku vzhledem k robotu
        /// </summary>
        public Vector3D? LocalTarget;

        /// <summary>
        /// Casovy okamzik vzorku.
        /// </summary>
        public DateTime TimeStamp;
    }
}
