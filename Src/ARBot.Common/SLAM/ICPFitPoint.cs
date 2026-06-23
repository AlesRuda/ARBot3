using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.SLAM
{
    /// <summary>
    /// bod zpracovavany metodou ICP
    /// </summary>
    public class ICPFitPoint
    {
        /// <summary>
        /// Pozice bodu
        /// </summary>
        public Point2D Point;
        /// <summary>
        /// Generace - kolikrat byl dohledan
        /// </summary>
        public int Generace;
        /// <summary>
        /// Index predchazejiciho bodu, tj. bodu ke kteremu byl prirazen a kteremu odpovida (Match)
        /// </summary>
        public int Index;
        /// <summary>
        /// Vzdalenost k odpovidajicimu bodu
        /// </summary>
        public double Distance;
        /// <summary>
        /// 
        /// </summary>
        public object Tag;
        /// <summary>
        /// Do vypoctu vstupuji body a chci najit jejich odpovidajici body a posunuti. Toto je odkaz na vstupni bod.
        /// </summary>
        public ICPFitPoint Original;
        /// <summary>
        /// Odpovidajici bod
        /// </summary>
        public ICPFitPoint Match;
    }
}
