using ARBot.Common.Common;
using ARBot.Common.SLAM;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.LocalMaps
{
    /// <summary>
    /// Doplnujici informace k typu v GraphMap
    /// </summary>
    public abstract class GraphMapType
    {
        /// <summary>
        /// Slucuje stavy s novymi merenimi.
        /// </summary>
        /// <param name="gm"></param>
        /// <param name="type">Typ stavu</param>
        /// <param name="points">Pozice prekazek s pocatkem v [0.0]</param>
        public abstract void Update(GraphMap gm, int type, IEnumerable<ICPObservationPoint> points);
    }
}
