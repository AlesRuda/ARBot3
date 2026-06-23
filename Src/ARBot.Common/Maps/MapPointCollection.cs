using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Maps
{
    public class MapPointCollection:List<MapPoint>
    {
        public MapPoint FindByID(long id)
        {
            return this.FirstOrDefault<MapPoint>((p) => p.ID == id);
        }
        public void UpdatePosition(Transformation t)
        {
            foreach (MapPoint p in this)
                p.UpdatePosition(t);
        }

        public LLA Min
        {
            get
            {
                return new LLA(this.Min((p)=>p.LLA.Latitude), this.Min((p)=>p.LLA.Longitude));
            }
        }
        public LLA Max
        {
            get
            {
                return new LLA(this.Max((p) => p.LLA.Latitude), this.Max((p) => p.LLA.Longitude));
            }
        }
    }
}
