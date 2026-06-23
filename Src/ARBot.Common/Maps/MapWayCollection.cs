using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ARBot.Common.Common;

namespace ARBot.Common.Maps
{
    public class MapWayCollection:List<MapWay>
    {
        public MapWay FindByID(long id)
        {
            return this.FirstOrDefault<MapWay>((p) => p.ID == id);
        }
        public double MaxDistance
        {
            get
            {
                double d = 0;
                foreach (MapWay w in this)
                    d = Math.Max(d, w.Distance);
                return d;
            }
        }
    }
}
