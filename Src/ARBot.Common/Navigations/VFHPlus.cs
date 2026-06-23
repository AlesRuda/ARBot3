using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using ARBot.Common.Logs;

namespace ARBot.Common.Navigations
{
    public class VFHPlus
    {
        /// <summary>
        /// Vypocet vahy vzorku podle puvodni dokumentace
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public double CalcMetrika(VFHPlusItem item)
        {
            double d = item.Distance / Perimeter;
            double m = item.Coeficient * a * (1 - d * d);
            return m;
        }
        /// <summary>
        /// Upraveny vypocet vahy vzorku. 
        /// Nebere v uvahu vzdalenost prekazky.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public double CalcMetrikaSimple(VFHPlusItem item)
        {
            return item.Coeficient;
        }

        public Func<VFHPlusItem, double> CalcFunc;
        public class VFHSegment
        {
            public double HP; //primarni histogram
            public bool HB; //binarni histogram
            private double cnt;
            private double sum;
            public double? min;
            public double? AvgDistance
            {
                get
                {
                    if (cnt == 0)
                        return null;
                    return sum / cnt;
                }
            }

            public void Reset()
            {
                HP = 0;
                sum = 0;
                cnt = 0;
                min = null;
            }
            public void Aggregate(double distance, double m)
            {
                HP += m;
                if (min == null || min > distance)
                    min = distance;
                sum += distance;
                cnt++;
            }
        }

        int Segments; //Pocet segmentu
        VFHSegment[] segs;
        double Perimeter;	 // prumer oblasti ve ktere se hledaji prekazky
        double Angle;	 // velikost sledovaneho pole v radianech
        public double SegmentSize { get; private set; }	 // velikost segmentu v radianech
        double a, b;  // koeficienty pro vypocet metriky
        public double SafeRadius { get; private set; }	 // polomer pro bezpecny prujezd robota
        public double tl;	 // spodni hranice pro binarni histogram, nastaveni na 0
        public double th;	 // horni hranice pro binarni histogram, nastaveni na 1
        double LeftRadius;	 // polomer otaceni na levou stranu
        double RightRadius;	 // polomer otaceni na pravou stranu
        public double mcil;	 // vaha smeru k cili
        public double mcenter;	 // vaha smeru stredem volneho prostoru
        public double mold;	 // vaha puvodniho smeru 
        public double msize;	 // vaha sirky mezery - mela by byt zaporna aby dochazelo k preferenci sirokych mezer
        public double mroad;	 // vaha pro smer urceni LR ci jenak 
        public int Segment { get; private set; }	 // vypocteny smer v segmentech
        /// <summary>
        /// vypocteny volny smer vzhledem k orientaci robotu, v radianaech
        /// </summary>
        public double Direction { get; private set; }	 
        public double? Distance { get; private set; }	 // volny prostor ve smeru Direction
        bool Closed;	// sledovane pole zaujima 360 stupnu
        double Segs2;   // (Segments/2)^2


        protected virtual double DifSeg( double a, double b)
        {
            double i = Math.Abs(a - b);
            return i * i / Segs2;
//           return Math.Min(Math.Abs(a - b), Math.Min(Math.Abs(a - b - Segment) , Math.Abs(a - b + Segment)));
        }

        /// <summary>
        /// Nastavi hranice pro binarni histogram
        /// </summary>
        /// <param name="minDist">vzdalenost pri ktere musi dojit k aktivaci binarniho histogramu pro jeden jediny bod prekazky </param>
        public void InitTreshods(double minDist)
        {
            double gama = Math.Atan(this.SafeRadius / minDist);
            var v = (int)(2*gama / SegmentSize);

            double d = minDist / Perimeter;
            th = v*a * (1 - d * d);
            tl = th * 0.8;
        }

        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="angle">Uhel zaberu</param>
        /// <param name="segments"></param>
        /// <param name="perimeter"></param>
        public VFHPlus(double angle, int segments, double perimeter, double safeRadius = 0.4)
        {
            CalcFunc = CalcMetrika;
            this.Segments = segments;
            this.Segs2 = (Segments / 2) * (Segments / 2);
            this.Angle = Math.Min(angle, Math.PI * 2);
            this.SegmentSize = angle / Segments; // velikost segmentu
            this.Perimeter = perimeter;		 // polomer oblasti ve ktere se hleda prekazka
            this.a = 1.0 / Segments;
            this.b = (perimeter * perimeter) / Segments;

            this.SafeRadius = safeRadius;	 // polomer pro bezpecny prujezd robota
            tl = 0.15;	 // spodni hranice pro binarni histogram, nastaveni na 0
            th = 0.20;	 // horni hranice pro binarni histogram, nastaveni na 1
            mcil = 25.0;	 // vaha smeru k cili
            mcenter = 20;	 // vaha smeru stredem volneho prostoru
            mold = 1.0;	 // vaha puvodniho smeru 
            msize = 1.0;	 // vaha sire prujezdu
            mroad = 10.0;	 // vaha rizeni na stred cesty

            segs = new VFHSegment[Segments];
            for (int i = 0; i < Segments; i++)
                segs[i]=new VFHSegment();

            Closed = this.Angle == Math.PI * 2;
        }

        /// <summary>
        /// Spocte VFH+ z udaju v lokalni mape a orientace je matematicka vzhledem ke smeru robotu
        /// </summary>
        /// <param name="lm"></param>
        /// <param name="smerKCili">Smer k cili vzhledem k orientaci robotu v matematickem smyslu.</param>
        /// <param name="smerRoad">Smer cesty vzhledm k orientaci robotu v matematickem smyslu.</param>
        /// <param name="speed"></param>
        /// <param name="maxRotUhel"></param>
        /// <param name="robotOrientation">Matematicka orientace robotu v radianech.</param>
        public void Calc(ILocalMap lm, double smerKCili, double smerRoad, double speed, double maxRotUhel, double robotOrientation)
        {
            List<VFHPlusItem> l = new List<VFHPlusItem>();

            int i = Math.Min(Math.Min((int)(Perimeter / lm.Resolution), lm.Width), lm.Height);
            for (int x = -i; x <= i; x++)
            {
                for (int y = -i; y <= i; y++)
                {
                    double v = lm[x, y].Value;
                    if (v < 0.5)
                    {
                        double xd = x * lm.Resolution;
                        double yd = y * lm.Resolution;
                        double d = Math.Sqrt(xd * xd + yd * yd);
                        double b = Math.Atan2(yd, xd);
                        l.Add(new VFHPlusItem() { Beta = Conversions.NormalizeOrientation(b - robotOrientation), Distance = d, Coeficient = 1 - v });
                    }
//                    lm[x, y].Update(0.51);
                }
            }
            Calc(l, smerKCili, smerRoad, speed, maxRotUhel);
        }
        
        /// <summary>
        /// Spocte VFH+ 
        /// </summary>
        /// <param name="items"></param>
        /// <param name="smerKCili"></param>
        /// <param name="smerRoad"></param>
        /// <param name="speed"></param>
        /// <param name="maxRotSpeed"></param>
        public void Calc(IEnumerable<VFHPlusItem> items, double smerKCili, double smerRoad, double speed, double maxRotSpeed)
        {
            Distance = null;

            LeftRadius = speed / maxRotSpeed;	 // polomer otaceni na levou stranu
            RightRadius = speed / maxRotSpeed;	 // polomer otaceni na pravou stranu

            double off = (Angle) / 2;
            double v, fr = Math.PI, fl = -Math.PI;
            double rl1 = LeftRadius + this.SafeRadius;
            double rr1 = RightRadius + this.SafeRadius;
            double cmin = -1;
            int s = -1;
            int l = -1, r = -1, cil = (int)((Conversions.NormalizeOrientation(smerKCili) + off) / SegmentSize), road = (int)((Conversions.NormalizeOrientation(smerRoad) + off) / SegmentSize);

//            Debug.Write(string.Format("VFH: cil={0} , {1}", smerKCili, cil));

            rl1 *= rl1;
            rr1 *= rr1;

            for (int i = 0; i < Segments; i++)
            {
                segs[i].Reset();
            }

            // krok 1		
            foreach (VFHPlusItem item in items)
            {
                int j;

                item.Segment = (int)((Conversions.NormalizeOrientation(item.Beta) + off) / SegmentSize);

                if (item.Segment>=0 && item.Segment<Segments && item.Distance <= Perimeter)
                {
                    var m = CalcFunc(item);

                    double gama = Math.Atan(this.SafeRadius/item.Distance);

                    if (this.Closed)
                    {
                        v = item.Beta - gama + off;
                        l = (int)(v / SegmentSize);

                        v = item.Beta + gama + off;
                        r = (int)(v / SegmentSize);

                        for (j = l; j <= r; j++)
                        {
                            int k = j < 0 ? j + Segments : (j >= Segments ? j - Segments : j);
                            segs[k].Aggregate(item.Distance, m); 
                        }
                    }
                    else
                    {
                        v = item.Beta - gama + off;
                        if (v < 0)
                            l = 0;
                        else if (v >= Angle)
                            l = Segments - 1;
                        else
                            l = (int)(v / SegmentSize);

                        v = item.Beta + gama + off;
                        if (v < 0)
                            r = 0;
                        else if (v >= Angle)
                            r = Segments - 1;
                        else
                            r = (int)(v / SegmentSize);

                        for (j = l; j <= r; j++)
                            segs[j].Aggregate(item.Distance, m);
                    }
                }
            }

            // krok 2
            for (int i = 0; i < Segments; i++)
            {
                var h = segs[i].HP;
//                var h = segs[i].min ?? 0;
                if (h > th)
                    segs[i].HB = true;
                if (h < tl)
                    segs[i].HB = false;
            }


            // krok 3
            if (!Closed)
            {
                foreach (VFHPlusItem item in items)
                {
                    if (item.Segment >= 0 && item.Segment < Segments && segs[item.Segment].HB)
                    {
                        double x, y, dr, dl;
                        x = item.Distance * Math.Sin(item.Beta);
                        y = item.Distance * Math.Cos(item.Beta);

                        v = x + LeftRadius;
                        dl = v * v;
                        v = x - RightRadius;
                        dr = v * v;
                        v = y * y;
                        dl += v;
                        dr += v;

                        //		if(item.c>0)
                        {
                            if (item.Beta > 0 && item.Beta < fr && dr < rr1)
                                fr = item.Beta;
                            if (item.Beta < 0 && item.Beta > fl && dl < rl1)
                                fl = item.Beta;
                        }
                    }
                }

                double c = -SegmentSize * Segments / 2;
                for (int i = 0; i < Segments; i++)
                {
                    if (c < fl || c > fr)
                        segs[i].HB = true;
                    c += SegmentSize;
                }
            }

            // krok 4

            r = -1;
            l = -1;
            double b = -Angle / 2;
            for (int i = 0; i < Segments; i++)
            {
                if (!segs[i].HB)
                {
                    if (l == -1)
                    {
                        if (Closed)
                        {
                            l = i;
                            if (i == 0)
                            {
                                for (l = -1; -l <= Segments && !segs[l + Segments].HB; l--) ;
                            }
                            for (r = i; r < 2*Segments && !segs[r >= Segments ? r - Segments : r].HB; r++) ;
                        }
                        else
                        {
                            l = i;
                            for (r = l; r < Segments && !segs[r].HB; r++) ;
                        }
                    }
//kvuli uzavrenemu VFH jsou opraveny faktory pro stred volneho prostoru a delku colneho prostoru
                    double c = mcil * DifSeg(i, cil) + mroad * DifSeg(i, road) + mcenter * DifSeg(i, r-l>=Segments?i:(l + r) / 2) + ((Segment == -1) ? 0 : mold * DifSeg(i, Segment)) - msize * Math.Min(r - l, Segments)/(Segments/2);
                    if (c < cmin || cmin == -1)
                    {
                        cmin = c;
                        s = i;
                    }
                }
                else
                {
                    r = -1;
                    l = -1;
                }
                b += SegmentSize;
            }
            Segment = s;
            if (s >= 0)
            {
                Direction = Conversions.NormalizeOrientation( s * SegmentSize - off+SegmentSize/2);

                double? dis = null;
                for (int i = 0; i < Segments; i++)
                {
                    if((segs[i].HB && s<=i && i<=Segments/2)||(Segments/2<=i && i<=s))
                    {
                        double? d = segs[i].min;
                        if (d.HasValue && (dis==null || dis > d.Value))
                            dis= d.Value;
                    }
                }
                Distance = dis;
            }
            else
                Direction = 0;
        }

        public VFH ToLogMessage()
        {
            VFH vfh = new VFH();
            vfh.SelSegment = Segment;
            vfh.Segments = Segments;
            vfh.SegmentSize = SegmentSize;
            vfh.Direction = Direction;
            vfh.Distance = Distance;
            vfh.TauHi = th;
            vfh.TauLo = tl;
            if (segs != null)
            {
                vfh.HP = segs.Select(i => i.HP).ToArray();
                vfh.HB = segs.Select(i => i.HB).ToArray();
            }
            else
            {
                vfh.HP = new double[Segments];
                vfh.HB = new bool[Segments];
            }

            return vfh;
        }
    }
}
