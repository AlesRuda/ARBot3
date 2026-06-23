using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Algorithms.Statistic;

namespace ARBot.Common.Maps
{
    public class MapWay
    {
        public MapWay()
        {
            WeigthIndex = 1;
            MaxDistance = 5;
        }
        public bool HighLight = false;
        public bool Bidirectional;
        public bool TemporaryDisable;
        public MapPoint End;
        public MapPoint Start;
        public long ID { get; set; }
        public double Weigth { get; set; }
        public double WeigthIndex { get; set; }
        /// <summary>
        /// Rovnice primky A*x+B*y+C=0
        /// </summary>
        public double A { get; private set; }
        public double B { get; private set; }
        public double C { get; private set; }

        /// <summary>
        /// Pri prekroceni teto vzdalenosti od cesty dojde k vyhledani nejblizsiho bodu k robotu.
        /// </summary>
        public double MaxDistance { get; private set; }

        public double Distance
        {
            get;
            private set;
        }
        public double WeigthDistance
        {
            get;
            private set;
        }

        public void CalcDistance()
        {
            ECEF d=End.Position- Start.Position;
            Distance = d.Radius;
            double n=Math.Sqrt(d.Y*d.Y+d.Z*d.Z);
            A = d.Z / n;
            B = d.Y / n;
            C = (-d.Z - End.Position.Z*d.Y) / n;
            WeigthDistance=Distance *Weigth * WeigthIndex;
        }
        /// <summary>
        /// Uhel primky Start-> End v matematickem smyslu v radianech
        /// </summary>
        public double Angle
        {
            get
            {
                return Math.Atan2(A, B);
            }
        }

        /// <summary>
        /// Uhel primky vzhledem k smeru prujezdu k cili
        /// </summary>
        public double DriveAngle
        {
            get
            {
                var a = Angle;
                return Start.Distance > End.Distance ? a : Conversions.NormalizeOrientation(a + Math.PI);
            }
        }

        /// <summary>
        /// Spocte prusecik primky prochazejici body p0 a p1 a kolmice prochazejici bodem p.
        /// Parametr pos je relativni pozice mezi p0 (pos=0) a p1 (pos=1)
        /// </summary>
        /// <param name="p0">Prvni bod na primce</param>
        /// <param name="p1">Druhy bod na primce</param>
        /// <param name="p">Bod na kolmici</param>
        /// <param name="pos">Relativni pozice pruseciku usecky s kolmici z bodu p. 0=na bodu p0, 1=na bodu p1</param>
        /// <returns></returns>
        public static ECEF Intersect(
            ECEF p0,
            ECEF p1,
            ECEF p,
            out double pos)
        {
            ECEF d21 = p1 - p0;
            ECEF d = p - p0;

            double r2 = Matrix.DotProduct(d21, d21);
            if (r2 == 0)
            {
                pos = -1;
                return null;
            }
            pos = Matrix.DotProduct(d21, d) / r2;
            return new ECEF() { X = p0.X + pos * d21.X, Y = p0.Y + pos * d21.Y, Z=p0.Z+pos*d21.Z };
        }

        public ECEF Intersect(ECEF p, out double pos)
        {
            return Intersect(Start.Position, End.Position, p, out pos);
        }

        /// <summary>
        /// Vsdalenost bodu od cesty
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public double GetDistance(ECEF p)
        {
            double pos;
            ECEF i = Intersect(p, out pos);
            //prusecik je pred zacatkem - vzdalenost od zacatku
            if (pos < 0)
                return (i - Start.Position).Radius;
            //prusecik je za koncem - vzdalenost od konce
            else if (pos > 1)
                return (i - End.Position).Radius;
            //prusecik mezi zacatkem a koncem - vzdalenost od pruseciku 
            return (i - p).Radius;
        }

        public IEnumerable<MapWay> GetNearestWays(double radius)
        {
            Dictionary<MapWay, bool> ways = new Dictionary<MapWay, bool>();
            ways.Add(this, true);
            Start.GetWays(ways, radius);
            End.GetWays(ways, radius);
            return ways.Keys.ToList();
        }

        public Line2D ToLine2D(MapPoint target)
        {
            Line2D wayDir = null;
            var sp = new Point2D(Start.Position.Y, Start.Position.Z);
            var ep = new Point2D(End.Position.Y, End.Position.Z);
            if (End==target)
            {
                wayDir = new Line2D(sp, ep);
            }
            if (Start == target)
            {
                wayDir = new Line2D(ep, sp);
            }
            return wayDir;
        }
        /// <summary>
        /// Line2D ve smeru k cili.
        /// </summary>
        /// <returns></returns>
        public Line2D ToLine2D()
        {
            Line2D wayDir = null;
            var sp = new Point2D(Start.Position.Y, Start.Position.Z);
            var ep = new Point2D(End.Position.Y, End.Position.Z);
            if (Start.WeigthDistance>End.WeigthDistance)
            {
                wayDir = new Line2D(sp, ep);
            }
            else
            {
                wayDir = new Line2D(ep, sp);
            }
            return wayDir;
        }
        /// <summary>
        /// Sirka cesty v bode pos.
        /// </summary>
        /// <returns></returns>
        public double Width(Point2D pos)
        {
            var sp = new Point2D(Start.Position.Y, Start.Position.Z);
            var ep = new Point2D(End.Position.Y, End.Position.Z);
            var l = (sp - ep).Length;
            var p = (sp - pos).Length;
            if (l == 0)
                return (Start.Width + End.Width) / 2;
            return Start.Width + (End.Width - Start.Width) * p / l;
        }

        public override string ToString()
        {
            return $"ID={ID}, Start={Start.ID}, End={End.ID}";
        }
    }
}
