using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.SLAM
{
    /// <summary>
    /// Stav bodu ICP.
    /// Je pouzit EKF pro aktualizaci pozice bodu.
    /// Nese souradnice bodu, rozptyl, generaci, ...
    /// </summary>
    public class ICPStatePoint
    {
        /// <summary>
        /// Typ stavu - slouzi pro rozliseni ruznych puvodcu mereni, ktere jsou vzajemne disjunktni
        /// </summary>
        public int Type;
        /// <summary>
        /// Blizsi cleneni v ramci Type
        /// </summary>
        public int SubType;
        /// <summary>
        /// Pozice bodu
        /// </summary>
        public Point2D Point;
        /// <summary>
        /// Generace - kolikrat byl dohledan
        /// </summary>
        public int Generace;
        /// <summary>
        /// Kolik iteraci ICP stav existuje
        /// </summary>
        public int Iterace;
        /// <summary>
        /// Pred kolika iteracemi byla nalezena posledni shoda v pozorovani
        /// </summary>
        public int LastMatch;
        /// <summary>
        /// Kovariancni matice 
        /// </summary>
        public Matrix P;
        /// <summary>
        /// Odkaz prirazeni. Pouziva se behem vypoctu ICP, aby byl jeden state pouzit jen k jednomu mereni.
        /// </summary>
        internal ICPMatchPoint Match;

        public double Rozptyl => Math.Abs(P[0, 0]) + Math.Abs(P[1, 1]);

//        public bool IsMain { get { return Generace > 1 && Generace>Iterace/3 && (Rozptyl < (LastMatch > 10 ? 0.01 : 0.1)); } }
// Type = 10 jsou hranice cesty, snazim se nepouzivat nejnovejsi mereni
        public bool IsMain { get { return Type!=10; } }

        public double? Orientation;
    }
}
