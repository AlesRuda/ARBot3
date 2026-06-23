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
    /// Doplnujici informace k lidarovym bodum v GraphMap
    /// </summary>
    public class LidarGraphMapType:GraphMapType
    {
        public class DirInfo
        {
            public double Direction;
            public double? Distance;
            public int SubType;
        }

        /// <summary>
        /// Vzdalenost pokud ji neuvadi mereni lidaru
        /// </summary>
        public double DefaultDistance;
        /// <summary>
        /// Uhlove rozliseni lidaru
        /// </summary>
        public double AngleResolution;
        /// <summary>
        /// Orientace lidaru v radianech a matematickem smyslu.
        /// </summary>
        public double LidarOrientation;
        /// <summary>
        /// Slepe uhly lidaru vzhledem k nulovemu uhlu lidaru
        /// </summary>
        public IEnumerable<BlindRegion> BlindRegions;

        /// <summary>
        /// Slucuje stavy s novymi merenimi.
        /// Ve slepich regionech necha puvodni stavy.
        /// Mimo slepe regiony pouzije nejblizsi mereni a nebo defaultDistance, pokud zadne mereni neni.
        /// V kazdem neslepem diskretnim smeru (urceno resolution) vymaze stavy az k urcene vzdalenosti a tam vytvori novy stav pokud ve smeru byla nejaka mereni.
        /// </summary>
        /// <param name="gm"></param>
        /// <param name="type">Typ stavu</param>
        /// <param name="points">Pozice prekazek s pocatkem v [0.0]</param>
        public override void Update(GraphMap gm, int type, IEnumerable<ICPObservationPoint> points)
        {
            int idx = (int)(2 * Math.PI / AngleResolution);
            List<DirInfo> dir = Enumerable.Range(0, idx).Select(i =>
            {
                double d = i * AngleResolution;
                double d1 = Conversions.NormalizeOrientation(d - LidarOrientation);
                return BlindRegions.Any(r => r.InSide(d1)) ? null : new DirInfo() { Direction = d };
            }).ToList();
            // vzdalenosti prekazek ve smerech
            foreach (var pp in points)
            {
                Point2D p = pp.Point;
                double d = Math.Atan2(p.Y, p.X);
                if (d < 0)
                    d += 2 * Math.PI;
                idx = (int)(d / AngleResolution);
                var di = dir[idx];
                if (di != null)
                {
                    double r = Math.Sqrt(Math.Pow(p.X, 2) + Math.Pow(p.Y, 2));
                    if (di.Distance == null || di.Distance > r)
                    {
                        di.Distance = r;
                        di.SubType = pp.SubType;
                    }
                }
            }

            // odstraneni stavu ve zmerenych oblastech
            idx = 0;
            while (idx < gm.States.Count)
            {
                var s = gm.States[idx];
                double d = Math.Atan2(s.Point.Y, s.Point.X);
                if (d < 0)
                    d += 2 * Math.PI;
                int i = (int)(d / AngleResolution);
                var di = dir[i];
                if (di != null)
                {
                    double r = Math.Sqrt(Math.Pow(s.Point.X, 2) + Math.Pow(s.Point.Y, 2));
                    if (r < di.Distance.GetValueOrDefault(DefaultDistance))
                    {
                        gm.States.RemoveAt(idx);
                        continue;
                    }
                }
                idx++;
            }

            // pridani novych stavu
            foreach (var di in dir)
            {
                if (di != null && di.Distance != null)
                {
                    double d = di.Distance.Value;

                    gm.States.Add(new ICPStatePoint() { Point = new Point2D(d * Math.Cos(di.Direction), d * Math.Sin(di.Direction)), Type=type, SubType=di.SubType });
                }
            }
        }
    }
}
