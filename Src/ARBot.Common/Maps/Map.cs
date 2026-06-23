using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.LocalMaps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml;

namespace ARBot.Common.Maps
{
    public class Map
    {
        private long lastID = -1;

        Transformation t;
        static System.Globalization.CultureInfo ci = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        public Map()
        {
            Points = new MapPointCollection();
            Ways = new MapWayCollection();
        }

        private long GetNextID()
        {
            return lastID--;
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="minDistance">Vzdalenost od bodu ve ktere se povazuje bod za dosazeny, vyhleda se nejblizsi point k cili do ktereho vede z tohoto cesta 
        /// </param>
        /// <param name="all"></param>
        public Map(string fileName, double minDistance, double width, bool all)
            : this()
        {
            XmlDocument d = new XmlDocument();
            d.Load(fileName);
            Load(d, minDistance, width, all);
//            Debug.WriteLine(string.Format("Points={0}, Ways={1}", Points.Count, Ways.Count));
        }
        public Map(XmlNode node, double minDistance, double width, bool all)
            : this()
        {
            Load(node, minDistance, width, all);
        }
        /// <summary>
        /// Nacita data z OSM - OpenStreetMap
        /// </summary>
        /// <param name="node"></param>
        /// <param name="minDistance">Vzdalenost od bodu ve ktere se povazuje bod za dosazeny, vyhleda se nejblizsi point k cili do ktereho vede z tohoto cesta 
        /// </param>
        /// <param name="all"></param>
        public void Load(XmlNode node, double minDistance, double width, bool all)
        {
            foreach (XmlNode n in node.SelectNodes("osm/node"))
            {
                if (n.Attributes["action"]?.ToString() != "delete")
                {
                    var v = n.SelectNodes("tag[@k='barrier']");
                    MapPoint p = new MapPoint();
                    p.MinDistance = minDistance;
                    p.Width = width;
                    if (n.Attributes["width"] != null && !string.IsNullOrEmpty(n.Attributes["width"].Value))
                        p.Width = Convert.ToDouble(n.Attributes["width"].Value);
                    p.ID = Convert.ToInt64(n.Attributes["id"].Value);
                    p.LLA = new LLA(Conversions.Deg2Rad(Convert.ToDouble(n.Attributes["lat"].Value, CultureInfo.InvariantCulture)), Conversions.Deg2Rad(Convert.ToDouble(n.Attributes["lon"].Value, CultureInfo.InvariantCulture)));
                    p.NonDrivable = v.Count != 0;
                    Points.Add(p);
                }
            }

            XmlNodeList nl;
            if (all)
                nl = node.SelectNodes("osm/way[tag/@k='highway']");
            else
                nl = node.SelectNodes("osm/way[tag/@k='highway' and tag/@v='footway']");

            foreach (XmlNode n in nl)
            {
                var surface = n.SelectSingleNode("tag[@k='surface']/@v")?.Value?.ToString();
                if (n.Attributes["action"]?.ToString() != "delete" && surface != "stepping_stones")
                {
                    long? oldID = null;
                    double smoothness = 1.3;

                    long wid = Convert.ToInt64(n.Attributes["id"].Value, CultureInfo.InvariantCulture);

                    if (n.Attributes["smoothness"] != null && !string.IsNullOrEmpty(n.Attributes["smoothness"].Value))
                        switch (n.Attributes["smoothness"].Value)
                        {
                            case "excellent":
                                smoothness = 1;
                                break;
                            case "good":
                                smoothness = 1.3;
                                break;
                            case "intermediate":
                                smoothness = 2;
                                break;
                            case "bad":
                                smoothness = 10;
                                break;
                            case "very_bad":
                                smoothness = 10000000;
                                break;
                            case "horrible":
                                smoothness = 10000000;
                                break;
                            case "very_horrible":
                                smoothness = 10000000;
                                break;
                            case "impassable ":
                                smoothness = 10000000;
                                break;
                            default:
                                smoothness = 10000000;
                                break;
                        }

                    foreach (XmlNode nn in n.SelectNodes("nd"))
                    {
                        long id = Convert.ToInt64(nn.Attributes["ref"].Value, CultureInfo.InvariantCulture);
                        if (oldID != null)
                        {
                            MapWay w = new MapWay();
                            w.ID = wid;
                            w.Bidirectional = true;
                            w.Start = Points.FindByID(oldID.Value);
                            w.End = Points.FindByID(id);
                            w.Weigth = smoothness;
                            if (w.Start != null && w.End != null)
                            {
                                w.Start.Ways.Add(w);
                                w.End.Ways.Add(w);

                                Ways.Add(w);
                            }
                        }
                        oldID = id;
                    }
                }
            }
            foreach (MapPoint p in new List<MapPoint>(Points))
            {
                if (p.Ways.Count == 0)
                    Points.Remove(p);
            }
        }


        /// <summary>
        /// RoboOrienteering
        /// </summary>
        /// <param name="kmlFileName"></param>
        /// <param name="minDistance"></param>
        /// <param name="maxPercetDistance"></param>
        /// <returns></returns>
        public static Map FromDAT(string datFileName)
        {
            string[] lines = File.ReadAllLines(datFileName, Encoding.ASCII);
            int id = 0;
            Map map = new Map();
            MapPoint old = null;

            foreach (string s in lines)
            {
                if (!string.IsNullOrEmpty(s.Trim()))
                {
                    string[] coords = s.Split(new string[] { " ", "\t" }, StringSplitOptions.RemoveEmptyEntries);

                    if (coords.Length != 3)
                        throw new Exception(string.Format("Chybny pocet prvku na radku '{0}'.", s));

                    MapPoint p = new MapPoint();
                    p.Width = 0;
                    p.ID = id++;
                    p.LLA = new LLA(Conversions.Deg2Rad(double.Parse(coords[1], ci)), Conversions.Deg2Rad(double.Parse(coords[2], ci)));
                    map.Points.Add(p);
                    if (old != null)
                    {
                        MapWay w = new MapWay();
                        w.Start = old;
                        w.End = p;
                        w.Weigth = 1;

                        w.Start.Ways.Add(w);
                        w.End.Ways.Add(w);

                        map.Ways.Add(w);
                    }
                    old = p;
                }
            }
            return map;
        }

        /// <summary>
        /// ARBot Tack
        /// </summary>
        /// <param name="fn"></param>
        /// <returns></returns>
        public static Map FromTrack(string fn)
        {
            string[] lines = File.ReadAllLines(fn, Encoding.ASCII);
            int id = 0;
            Map map = new Map();
            MapPoint old = null;

            foreach (string s in lines)
            {
                if (!string.IsNullOrEmpty(s.Trim()))
                {
                    if (s.ToLower() == "repeat")
                    {
                        if (old != null)
                        {
                            MapWay w = new MapWay();
                            w.Start = old;
                            w.End = map.Points[0];
                            w.Weigth = 1;

                            w.Start.Ways.Add(w);
                            w.End.Ways.Add(w);

                            map.Ways.Add(w);
                        }
                        break;
                    }
                    else
                    {
                        string[] coords = s.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);

                        if (coords.Length != 2)
                            throw new Exception(string.Format("Chybny pocet prvku na radku '{0}'.", s));

                        MapPoint p = new MapPoint();
                        p.Width = 0;
                        p.ID = id++;
                        p.LLA = new LLA(Conversions.Deg2Rad(double.Parse(coords[0], ci)), Conversions.Deg2Rad(double.Parse(coords[1], ci)));
                        map.Points.Add(p);
                        if (old != null)
                        {
                            MapWay w = new MapWay();
                            w.Start = old;
                            w.End = p;
                            w.Weigth = 1;

                            w.Start.Ways.Add(w);
                            w.End.Ways.Add(w);

                            map.Ways.Add(w);
                        }
                        old = p;
                    }
                }
            }

            map.Init(new Transformation());

            return map;
        }

        public static Map FromKML(string kmlFileName)
        {
            Map map = new Map();
            XmlDocument d = new XmlDocument();
            d.Load(kmlFileName);

            XmlNamespaceManager ns = new XmlNamespaceManager(new NameTable());
            ns.AddNamespace("kml", "http://www.opengis.net/kml/2.2");
            ns.AddNamespace("gx", "http://www.google.com/kml/ext/2.2");
            ns.AddNamespace("atom", "http://www.w3.org/2005/Atom");
            ns.AddNamespace("", "http://www.opengis.net/kml/2.2");
            XmlNode n = d.SelectSingleNode(@"/kml:kml/kml:Document/kml:Placemark/kml:LineString/kml:coordinates", ns);
            //            XmlNode n = d.SelectSingleNode(@"/kml/Document/Placemark/LineString/coordinates", ns);
            if (n != null)
            {
                int id = 0;
                MapPoint old = null;

                string v = n.InnerText;
                string[] c = v.Split(new string[] { " ", "\t", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string item in c)
                {
                    string[] coords = item.Split(new string[] { "," }, StringSplitOptions.RemoveEmptyEntries);
                    if (coords.Length != 3)
                        throw new Exception(string.Format("Chybny pocet souradnic '{0}'.", item));

                    MapPoint p = new MapPoint();
                    p.Width = 4;
                    p.ID = id++;
                    p.LLA = new LLA(Conversions.Deg2Rad(double.Parse(coords[1], ci)), Conversions.Deg2Rad(double.Parse(coords[0], ci)), double.Parse(coords[2], ci));
                    map.Points.Add(p);
                    if (old != null)
                    {
                        MapWay w = new MapWay();
                        w.Start = old;
                        w.End = p;
                        w.Weigth = 1;

                        w.Start.Ways.Add(w);
                        w.End.Ways.Add(w);

                        map.Ways.Add(w);
                    }
                    old = p;
                }
            }
            return map;
        }

        public MapPointCollection Points { get; set; }
        public MapWayCollection Ways { get; set; }

        /*        public BitmapSource DrawMap(double width, double height, double centerX, double centerY, double rozliseni, Color color, ARBot.Common.Coordinates.Transform m)
                {
                    DrawingVisual visual = new DrawingVisual();
                    using (DrawingContext context = visual.RenderOpen())
                    {
        //                context.DrawRectangle(new SolidColorBrush(Colors.White), null, new Rect(0, 0, width, height));
                        SolidColorBrush brush = new SolidColorBrush(color);
                        Pen pen = new Pen(brush, 0);

                        double xc = width / 2;
                        double yc = height / 2;

                        foreach (Point p in Points)
                        {
                            if (!p.Temporary)
                            {
                                ECEF e = m.Mul(new ECEF(Ellipsoid.Wgs84, p.LLA));
                                context.DrawEllipse(brush, pen, new System.Windows.Point((e.Y - centerX) / rozliseni + xc, (centerY - e.Z) / rozliseni + yc), 0.5 * p.Width / rozliseni, 0.5 * p.Width / rozliseni);
                            }
                        }
         */
        /*
                        foreach (Way w in Ways)
                        {
                            if (!w.Start.Temporary && !w.End.Temporary)
                            {
                                ECEF e = w.Start.LLA.ECEF.Mul(m);
                                double x1 = (e.Y - centerX) / rozliseni + xc;
                                double y1 = (centerY - e.Z) / rozliseni + yc;
                                double w1 = w.Start.Width / rozliseni;
                                e = w.End.LLA.ECEF.Mul(m);
                                double x2 = (e.Y - centerX) / rozliseni + xc;
                                double y2 = (centerY - e.Z) / rozliseni + yc;
                                double w2 = w.End.Width / rozliseni;

                                context.DrawLine(pen, new System.Windows.Point(x1, y1), new System.Windows.Point(x2, y2));
                            }
                        }
                        */
        /*
                foreach (Way w in Ways)
                {
                    if (!w.Start.Temporary && !w.End.Temporary)
                    {
                        PathGeometry pg = new PathGeometry();
                        PathFigure pf = new PathFigure();
                        System.Windows.Point[] pt = new System.Windows.Point[4];

                        ECEF e = m.Mul(new ECEF(Ellipsoid.Wgs84, w.Start.LLA));
                        double x1 = (e.Y - centerX) / rozliseni + xc;
                        double y1 = (centerY-e.Z) / rozliseni + yc;
                        double w1 = w.Start.Width / rozliseni;
                        e = m.Mul(new ECEF(Ellipsoid.Wgs84, w.End.LLA));
                        double x2 = (e.Y - centerX) / rozliseni + xc;
                        double y2 = (centerY-e.Z) / rozliseni + yc;
                        double w2 = w.End.Width / rozliseni;
                        double dx = (x1 - x2), dy = (y1 - y2), dx1, dy1;
                        double r = 2 * Math.Sqrt(dx * dx + dy * dy);
                        w1 = w1 / r;
                        w2 = w2 / r;

                        dx1 = dy * w1;
                        dy1 = dx * w1;


                        pt[0].X = x1 + dx1;
                        pt[0].Y = y1 - dy1;
                        pt[1].X = x1 - dx1;
                        pt[1].Y = y1 + dy1;

                        dx1 = dy * w2;
                        dy1 = dx * w2;

                        pt[2].X = x2 - dx1;
                        pt[2].Y = y2 + dy1;
                        pt[3].X = x2 + dx1;
                        pt[3].Y = y2 - dy1;

                        pf.StartPoint = pt[0];

                        pf.Segments.Add(new LineSegment(pt[1], true));
                        pf.Segments.Add(new LineSegment(pt[2], true));
                        pf.Segments.Add(new LineSegment(pt[3], true));
                        pf.IsClosed = true;
                        pf.IsFilled = true;

                        pg.Figures.Add(pf);

                        context.DrawGeometry(brush, pen, pg);
                    }
                }
                context.Close();
            }
            RenderTargetBitmap rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            return rtb;
        }*/
        // 
        /// <summary>
        /// Vrati nejblizsi cestu k bodu p. 
        /// Kolmice bodem p na cestu mosi byt mezi zacatkem a koncem.
        /// </summary>
        /// <param name="p">Bod ke kteremu hledame cestu</param>
        /// <param name="dist">Vzdalenost bodu od cesty</param>
        /// <param name="all">Vsechny cesty v mape, jinak nedisablovane</param>
        /// <returns></returns>
        public MapWay GetNearestWay(ECEF p, out double dist, bool all)
        {
            MapWay way = null;
            double d = double.MaxValue, d1;
            double ipos;
            dist = 0;
            foreach (MapWay w in Ways)
            {
                if (all || (!w.TemporaryDisable && w.Start.Final && w.End.Final))
                {
                    ECEF i = w.Intersect(p, out ipos);
                    if (ipos >= 0 && ipos <= 1)
                    {
                        ECEF dp = i - p;
                        d1 = dp.Radius;
                        if (d > d1 || way == null)
                        {
                            d = d1;
                            way = w;
                            dist = d1;
                        }
                    }
                }
            }
            return way;
        }

        public MapPoint GetNearestPoint(ECEF p, out double dist, bool all)
        {
            MapPoint point = null;
            double d = double.MaxValue, d1;
            dist = 0;
            foreach (MapPoint cp in Points)
            {
                if (all || cp.Final)
                {
                    ECEF dp = cp.Position - p;
                    d1 = dp.Radius;
                    if (d > d1 || point == null)
                    {
                        d = d1;
                        point = cp;
                        dist = d;
                    }
                }
            }
            return point;
        }
        /// <summary>
        /// Najde nejblizsi bod na mape
        /// </summary>
        /// <param name="p"></param>
        /// <param name="all"></param>
        /// <returns></returns>
        public ECEF GetNearestPoint(ECEF p, bool all)
        {
            double pd, wd;
            MapPoint np = GetNearestPoint(p, out pd, all);
            MapWay nw = GetNearestWay(p, out wd, all);
            if (nw != null && pd >= wd)
                return nw.Intersect(p, out wd);
            if (np != null)
                return np.Position;
            return null;
        }

        /// <summary>
        /// Vrati index bodu kam ma jet robot na zaklade jeho pozice
        /// way smeruje k cili a vraceny bod je na jeho zacatku ci konci.
        /// Muze se stat, ze way je null.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public MapPoint GetPoint(ECEF p, ref MapWay way)
        {
            double pd, wd;
            MapPoint np = GetNearestPoint(p, out pd, false);
            MapWay nw = GetNearestWay(p, out wd, false);
            way = nw;
            if (nw != null && pd >= wd)
            {
                //                dprintf("MapGetPoint.1 %d, %e, %e, %e, %e", i, points[ways[i].StartPoint].Distance, d1, points[ways[i].EndPoint].Distance, d2);
                if (nw.Start.WeigthDistance < nw.End.WeigthDistance)
                    return nw.Start;
                else
                    return nw.End;
            }
            if(nw==null || pd < wd)
            {
                double min = double.MaxValue;
                nw = null;
                foreach(var w in np.Ways)
                {
                    if (w.End.Final && w.Start.Final)
                    {
                        double d = w.Start == np ? w.End.WeigthDistance : w.Start.WeigthDistance;
                        if (nw == null || min > d)
                        {
                            nw = w;
                            min = d;
                        }
                    }
                }
                way = nw;
            }
            //            dprintf("MapGetPoint.End %d", i);
            return np;
        }

        /// <summary>
        /// Inicializuje priznaky pro vypocet nejkratsi cesty.
        /// </summary>
        /// <returns></returns>
        public void Init()
        {
            foreach (MapPoint mp in new List<MapPoint>(Points))
            {
                if (mp.Temporary && mp.Ways.Count <= 2)
                    RemovePoint(mp);
                mp.Distance = 0;
                mp.WeigthDistance = 0;
                mp.Final = false;
                mp.Target = false;
                mp.DistanceCalculated = false;
            }
        }

        /// <summary>
        /// Inicializuje priznaky pro vypocet nejkratsi cesty - volani Init()
        /// Inizializuje mapu, pocita x,y souradnice bodu mapy, spocita delky cest a ulozi je do Way.Distance a do Way.WeightDistance
        /// </summary>
        /// <returns></returns>
        public void Init(Transformation t)
        {
            this.t = t;
            Init();
            Points.UpdatePosition(t);
            foreach (MapWay mw in Ways)
            {
                mw.TemporaryDisable = false;
                mw.CalcDistance();
            }
        }

        public void HighLightWay(MapPoint mp)
        {
            foreach (MapWay mw in Ways)
                mw.HighLight = false;

            while(mp!=null && mp.WeigthDistance>0)
            {
                var d = mp.WeigthDistance;
                var mw = mp.Ways.OrderBy(w => Math.Min(w.Start.WeigthDistance, w.End.WeigthDistance)).FirstOrDefault();
                if (mw != null)
                {
                    mw.HighLight = true;
                    mp = mw.Start == mp ? mw.End : mw.Start;
                }
            }
        }

        /// <summary>
        /// Rozdeli cestu way v bode kolmice z bodu point na ni
        /// </summary>
        /// <param name="w"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        MapPoint SplitWay(MapWay w, ECEF point)
        {
            double ipos;
            ECEF inter = w.Intersect(point, out ipos);

            double d = w.Distance;

            MapPoint target = new MapPoint();
            target.ID = GetNextID();
            Points.Add(target);
            target.Ways.Add(w);
            target.Width = w.Start.Width + ipos * (w.End.Width - w.Start.Width);
            target.MinDistance = w.Start.MinDistance + ipos * (w.End.MinDistance - w.Start.MinDistance);
            target.Position = inter;

            MapWay nw = new MapWay();
            nw.ID = GetNextID();
            nw.Weigth = w.Weigth;
            nw.WeigthIndex = w.WeigthIndex;
            nw.Bidirectional = w.Bidirectional;
            nw.TemporaryDisable = w.TemporaryDisable;

            Ways.Add(nw);
            nw.Start = target;
            nw.End = w.End;

            target.Ways.Add(nw);
            w.End.Ways.Add(nw);

            w.End.Ways.Remove(w);
            w.End = target;

            w.CalcDistance();
            nw.CalcDistance();

            return target;
        }


        /// <summary>
        /// Rozdeli cestu way v bode kolmice z bodu point na ni
        /// </summary>
        /// <param name="w"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        void RemovePoint(MapPoint p)
        {
            if (p.Ways.Count > 2)
                throw new Exception("Bod ma moc cest.");
            if (p.Ways.Count == 2)
            {
                MapWay w = p.Ways[0];
                MapPoint p2 = p.Ways[1].Start == p ? p.Ways[1].End : p.Ways[1].Start;
                if (w.Start == p)
                {
                    w.Start = p2;
                    p2.Ways.Add(w);
                }
                else
                {
                    w.End = p2;
                    p2.Ways.Add(w);
                }
                w.CalcDistance();

                MapWay w1 = p.Ways[1];
                w1.Start.Ways.Remove(w1);
                w1.End.Ways.Remove(w1);
                Ways.Remove(w1);
            }
            else
            {
                MapWay w = p.Ways[0];
                w.Start.Ways.Remove(w);
                w.End.Ways.Remove(w);
                Ways.Remove(w);
            }

            Points.Remove(p);
        }


        /// <summary>
        /// Vyhleda nejblizsi bod mapy k body End.
        /// Spocte nejblizsi bod na ceste mapy k dodu End. Pokud je bliz nez nejblizsi bod mapy rozdeli cestu v tomto bode.
        /// </summary>
        /// <param name="end"></param>
        /// <returns></returns>
        public MapPoint NearestPoint(ECEF end)
        {
            double pd, wd;
            MapPoint np = GetNearestPoint(end, out pd, true);
            MapWay nw = GetNearestWay(end, out wd, true);
            if (nw != null && wd < pd)
            {
                MapPoint t = SplitWay(nw, end);
                t.Temporary = true;
                return t;
            }
            return np;
        }

        /// <summary>
        /// Vyhleda nejblizsi bod na ceste k bodu End.
        /// Spocte vzdalensti od tohoto bodu v kazdem bode mapy.
        /// Pred volanim je nutne volat Init()
        /// </summary>
        /// <param name="end"></param>
        public int CalculateDistances(ECEF end)
        {
            MapPoint np = NearestPoint(end);
            return CalculateDistances(np);
        }
        /// <summary>
        /// Spocte vzdalensti od bodu p v kazdem bode mapy.
        /// Pred volanim je nutne volat Init()
        /// </summary>
        /// <param name="p">target point</param>
        public int CalculateDistances(MapPoint p)
        {
            if (p != null)
            {
                p.Target = true;
                p.DistanceCalculated = true;
                p.Final = false;
                p.Distance = 0;
                p.WeigthDistance = 0;

                return CalculateDistances();
            }
            return 0;
        }
        /// <summary>
        /// Spocte vzdalensti v mape.
        /// Jako meritko vzdalenosti je pouzita way.WeigthDistance.
        /// Pred volanim je nutne volat Init() 
        /// Pred volanim je nutne nastavit pocatecni body (priznak Final=false, DistanceCalulated=true a Distance=pocatecni vzdalenost, typicky 0) 
        /// </summary>
        /// <returns>Pocet dosazitelnych bodu.</returns>
        public int CalculateDistances()
        {
            int cnt = 0;
            MapPoint w;
            do
            {
                w = null;
                double d = 0;
                foreach (MapPoint p in Points)
                {
                    if (!p.Final && p.DistanceCalculated)
                    {
                        if (w == null || p.WeigthDistance < d)
                        {
                            w = p;
                            d = p.WeigthDistance;
                        }
                    }
                }
                if (w != null)
                {
                    w.Final = true;
                    cnt++;
                    if (!w.NonDrivable)
                    {
                        foreach (MapWay mw in w.Ways)
                        {
                            if (!mw.TemporaryDisable)
                            {
                                if (mw.Bidirectional || w == mw.Start)
                                {
                                    MapPoint mp2 = (w == mw.Start) ? mw.End : mw.Start;
                                    if (!mp2.DistanceCalculated || (!mp2.Final && w.WeigthDistance + mw.WeigthDistance < mp2.WeigthDistance))
                                    {
                                        mp2.WeigthDistance = w.WeigthDistance + mw.WeigthDistance;
                                        mp2.Distance = w.Distance + mw.Distance;
                                        mp2.DistanceCalculated = true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            while (w != null);
            return cnt;
        }

        /// <summary>
        /// Kontroluje zda je mapa souvisla. Predem je nutne volat CalculateDistances.
        /// </summary>
        public bool Check()
        {
            foreach (MapPoint p in Points)
            {
                if (!p.Final && !p.DistanceCalculated)
                {
                    Debug.WriteLine(string.Format("Mapa neni souvisla. Bod ID={0}", p.ID));
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Vrati seznam bodu ktere nalezi kazdy do oddeleneho segmentu (jeden bod pro kazdy segment) grafu.
        /// </summary>
        /// <returns></returns>
        public List<Tuple<MapPoint, int>> GetSegments()
        {
            var segs = new List<Tuple<MapPoint, int>>();
            MapPoint point;
            
            while ((point = Points.FirstOrDefault(p => !p.Final))!=null)
            {
                segs.Add(new Tuple<MapPoint, int>(point, CalculateDistances(point)));
            }

            return segs.OrderByDescending(i=>i.Item2).ToList();
        }


        /// <summary>
        /// Vrati bod kam ma jet robot na zaklade jeho pozice a predesleho bodu.
        /// Vyhledava nasledujici bod po dosazeni aktualniho na zaklade blizsi vzdalenosti k cili.
        /// </summary>
        /// <param name="position"></param>
        /// <returns>Null indikuje cil</returns>
        public MapPoint GetMapNextPoint(ECEF position, ref MapPoint mapPoint, ref MapWay mapWay, ref double? navigationPointDistance, ref double? navigationWayDistance)
        {
            if (mapPoint == null)
            {
                mapPoint = GetPoint(position, ref mapWay);
                HighLightWay(mapPoint);
                Debug.WriteLine(string.Format("{0}->{1}", position, mapPoint));
            }

            if (mapWay != null)
            {
                navigationWayDistance = mapWay.GetDistance(position);

                Debug.WriteLine(string.Format("navigationWayDistance={0}", navigationWayDistance));

                if (navigationWayDistance > mapWay.MaxDistance)
                {
                    mapPoint = GetPoint(position, ref mapWay);
                    HighLightWay(mapPoint);

                    navigationWayDistance = mapWay?.GetDistance(position);
                    Debug.WriteLine(string.Format("{0}->{1}", position, mapPoint));
                }
            }
            else
                navigationWayDistance = null;

            navigationPointDistance = (mapPoint.Position - position).Radius;
            Debug.WriteLine(string.Format("navigationPointDistance={0}", navigationPointDistance));

            //            TargetDistance = NavigationPointDistance + MapPoint.Distance;

            if (navigationPointDistance < mapPoint.MinDistance)
            {
                if (!mapPoint.Target)
                {
                    MapPoint mp = mapPoint;
                    mapPoint = null;
                    double dmin = 0;
                    foreach (MapWay mw in mp.Ways)
                    {
                        if (!mw.TemporaryDisable && (mw.Bidirectional || mapPoint == mw.Start))
                        {
                            MapPoint j = (mp == mw.Start) ? mw.End : mw.Start;
                            if (mapPoint == null || dmin > j.WeigthDistance+mw.WeigthDistance)
                            {
                                dmin = j.WeigthDistance+mw.WeigthDistance;
                                mapPoint = j;
                                mapWay = mw;
                            }
                        }
                    }
                    HighLightWay(mapPoint);
                    return mapPoint;
                }
                else
                    return null;
            }
            return mapPoint;
        }

        /// <summary>
        /// Vykresli mapu
        /// </summary>
        /// <param name="de"></param>
        /// <param name="x">Pozice na mape v metrech, ktera se promitne do bodu 0,0 vykreslovaneho obrazku.</param>
        /// <param name="y">Pozice na mape v metrech, ktera se promitne do bodu 0,0 vykreslovaneho obrazku.</param>
        /// <param name="resolution">Velikost pixelu v metrech.</param>
        /// <param name="m"></param>
        public void Draw(DrawEngine de, float x, float y, float resolution)
        {
            MoveScale2DTransformation tr = new MoveScale2DTransformation();
            tr.Move(-x / resolution, -y / resolution);
            tr.Scale = 1 / resolution;
            Draw(de, tr);
        }
/*        [Obsolete]
        public void Draw(DrawEngine de, ILocalMap lm)
        {
            Draw(de, new Point(lm.Center.X - lm.Width / 2, lm.Center.Y + lm.Height / 2), lm.Resolution);
        }*/
        public void Draw(DrawEngine de, ARBot.Common.Coordinates.MoveScale2DTransformation t)
        {
            Point[] pt = new Point[4];
            Point p;
            ECEF e;
            Vector3 v=new Vector3();
            double scale = t.Scale;

            foreach (MapPoint mp in Points)
            {
                if (mp.LLA != null)
                {
                    e = mp.Position;
                    v=(Vector3)e;
                    v = t.Transform(v);
                    mp.X = v.X;
                    mp.Y = v.Y;
                    p.X = (int)v.X;
                    p.Y = (int)v.Y;
                    de.FillCircle(p, (int)(0.5 * mp.Width * scale));
                }
            }

            MapPoint mp1;

            foreach (MapWay mw in Ways)
            {
                if (mw.Start.LLA != null && mw.End.LLA != null)
                {
                    mp1=mw.Start;
                    double x1 = mp1.X;
                    double y1 = mp1.Y;
                    double w1 = mw.Start.Width * scale;
                    mp1 = mw.End;
                    double x2 = mp1.X;
                    double y2 = mp1.Y;
                    double w2 = mw.End.Width * scale;
                    double dx = (x1 - x2);
                    double dy = (y1 - y2);
                    double r = 2 * Math.Sqrt(dx * dx + dy * dy);
                    w1 = w1 / r;
                    w2 = w2 / r;

                    double dx1 = dy * w1;
                    double dy1 = dx * w1;


                    pt[0].X = (int)Math.Round(x1 + dx1);
                    pt[0].Y = (int)Math.Round(y1 - dy1);
                    pt[1].X = (int)Math.Round(x1 - dx1);
                    pt[1].Y = (int)Math.Round(y1 + dy1);

                    dx1 = dy * w2;
                    dy1 = dx * w2;

                    pt[2].X = (int)Math.Round(x2 - dx1);
                    pt[2].Y = (int)Math.Round(y2 + dy1);
                    pt[3].X = (int)Math.Round(x2 + dx1);
                    pt[3].Y = (int)Math.Round(y2 - dy1);
                    
                    de.FillConvexPoly(pt);
                }
            }
        }
    }
}
