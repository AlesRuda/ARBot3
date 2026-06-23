using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.SLAM
{
    /// <summary>
    /// Nalezena shoda zmereneho bodu a bodu stavu.
    /// </summary>
    public class ICPMatchPoint
    {
        /// <summary>
        /// Pozice bodu
        /// </summary>
        public Point2D Point;
        /// <summary>
        /// Vzdalenost k odpovidajicimu bodu
        /// </summary>
        public double Distance;
        /// <summary>
        /// Odkaz na stavovy bod, ktery odpovida mereni
        /// </summary>
        public ICPStatePoint State;
        /// <summary>
        /// Odkaz na nejblizsi stavovy bod
        /// </summary>
        public ICPStatePoint NearestState;
        /// <summary>
        /// Odkaz na bod mereni
        /// </summary>
        public ICPObservationPoint Observation;

        /// <summary>
        /// Pridat do stavu
        /// </summary>
        public bool Add;
    }
}
