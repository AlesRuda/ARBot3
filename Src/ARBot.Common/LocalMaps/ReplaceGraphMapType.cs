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
    /// Nahradi vsechny body 
    /// </summary>
    public class ReplaceGraphMapType : GraphMapType
    {
        /// <summary>
        /// Nahradi vsechny body urceneho typu
        /// </summary>
        /// <param name="gm"></param>
        /// <param name="type">Typ stavu</param>
        /// <param name="points">Pozice prekazek s pocatkem v [0.0]</param>
        public override void Update(GraphMap gm, int type, IEnumerable<ICPObservationPoint> points)
        {
            int idx = 0;
            while (idx < gm.States.Count)
            {
                var s = gm.States[idx];
                if (s.Type == type)
                {
                    gm.States.RemoveAt(idx);
                }
                else
                    idx++;
            }

            // pridani novych stavu
            foreach (var p in points)
            {
                gm.States.Add(new ICPStatePoint() { Point = p.Point, Type=type, SubType=p.SubType });
            }
        }
    }
}
