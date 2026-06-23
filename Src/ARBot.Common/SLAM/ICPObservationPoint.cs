using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.SLAM
{
    /// <summary>
    /// Zmereny bod, ktery bude dohledavan ve stavu ICP
    /// </summary>
    public class ICPObservationPoint
    {
        /// <summary>
        /// Typ - slouzi pro rozliseni ruznych puvodcu mereni, ktere jsou vzajemne disjunktni
        /// </summary>
        public int Type;
        /// <summary>
        /// Blizsi cleneni v ramci Type
        /// </summary>
        public int SubType;
        /// <summary>
        /// Pravdepodobnost prekazky
        /// </summary>
        public double Probability = 1;
        /// <summary>
        /// 
        /// </summary>
        public object Tag;
        /// <summary>
        /// Pozice bodu
        /// </summary>
        public Point2D Point;
        /// <summary>
        /// Reprezentuje sum mereni 
        /// </summary>
        public Matrix R;

        /// <summary>
        /// Orientace pri zaznamu prekazky, v radianech a svetovych souradnicich
        /// </summary>
        public double? Orientation;
    }
}
