using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public class DrawEngine
    {
        public Action<int, int> PixelSetter;
        public int XMin;
        public int XMax;
        public int YMin;
        public int YMax;
        public bool Clipping = true;

        struct PolyEdge
        {
            public int idx, di;
            public int ye;
            public double dx, x;
        }

        public static int Limit(int v, int min, int max)
        {
            return (v < min) ? min : ((v > max) ? max : v);
        }

        public bool Inside(Point pt)
        {
            return (!Clipping) || (pt.X >= XMin && pt.X <= XMax && pt.Y >= YMin && pt.Y <= YMax);
        }

        public void DrawPixel(int x, int y)
        {
            if (!Clipping)
                PixelSetter(x, y);
            else
                if (x >= XMin && x <= XMax && y >= YMin && y <= YMax)
                    PixelSetter(x, y);
        }

        public bool ClipLine(ref Point pt1, ref Point pt2)
        {
            if (!Clipping)
                return true;

            int x1, y1, x2, y2;
            int c1, c2;
            int right = XMax, bottom = YMax;

            if (right < 0 || bottom < 0)
                return false;

            x1 = pt1.X;
            y1 = pt1.Y;
            x2 = pt2.X;
            y2 = pt2.Y;

            c1 = (x1 < XMin ? 1 : 0) + (x1 > right ? 2 : 0) + (y1 < YMin ? 4 : 0) + (y1 > bottom ? 8 : 0);
            c2 = (x2 < XMin ? 1 : 0) + (x2 > right ? 2 : 0) + (y2 < YMin ? 4 : 0) + (y2 > bottom ? 8 : 0);

            if ((c1 & c2) == 0 && (c1 | c2) != 0)
            {
                int a;
                if ((c1 & 12) != 0)
                {
                    a = c1 < 8 ? YMin : bottom;
                    x1 += (int)((double)(a - y1) * (double)(x2 - x1) / (double)(y2 - y1));
                    y1 = a;
                    c1 = (x1 < XMin ? 1 : 0) + (x1 > right ? 2 : 0);
                }
                if ((c2 & 12) != 0)
                {
                    a = c2 < 8 ? YMin : bottom;
                    x2 += (int)((double)(a - y2) * (double)(x2 - x1) / (double)(y2 - y1));
                    y2 = a;
                    c2 = (x2 < XMin ? 1 : 0) + (x2 > right ? 2 : 0);
                }
                if ((c1 & c2) == 0 && (c1 | c2) != 0)
                {
                    if (c1 != 0)
                    {
                        a = c1 == 1 ? XMin : right;
                        y1 += (int)((double)(a - x1) * (double)(y2 - y1) / (double)(x2 - x1));
                        x1 = a;
                        c1 = 0;
                    }
                    if (c2 != 0)
                    {
                        a = c2 == 1 ? XMin : right;
                        y2 += (int)((double)(a - x2) * (double)(y2 - y1) / (double)(x2 - x1));
                        x2 = a;
                        c2 = 0;
                    }
                }

                pt1.X = x1;
                pt1.Y = y1;
                pt2.X = x2;
                pt2.Y = y2;
            }

            return (c1 | c2) == 0;
        }

        // Kresli horizontalni linku
        // xl - pocatek linky v px
        // xr - konec linky v px
        public void HLine(int x, int y, int l)
        {
            if (!Clipping)
                for (int i = 0; i <= l; i++, x++)
                    PixelSetter(x, y);
            else
            {
                if (y >= YMin && y <= YMax)
                {
                    int x1 = Math.Max(x, XMin);
                    int x2 = Math.Min(x + l, XMax);
                    for (; x1 <= x2; x1++)
                        PixelSetter(x1, y);
                }
            }
        }

        public void Line(Point p1, Point p2)
        {
            int a = 65536;
            int dx, dy;
            int count;
            int ax, ay;
            int sx, sy;
            int x_step, y_step;
            Point pt1 = p1;
            Point pt2 = p2;

            if (!ClipLine(ref pt1, ref pt2))
                return;

            dx = pt2.X - pt1.X;
            dy = pt2.Y - pt1.Y;

            sx = dx < 0 ? -1 : 1;
            sy = dy < 0 ? -1 : 1;

            ax = dx * sx;
            ay = dy * sy;

            if (ax > ay)
            {
                count = ax;
                x_step = a;
                y_step = (ay << 16) / ax;
            }
            else
            {
                count = ay;
                x_step = ay==0?0:(ax << 16) / ay;
                y_step = a;
            }

            dx = pt1.X;
            dy = pt1.Y;

            ax = ay = 0;

            while (count >= 0)
            {
                PixelSetter(dx, dy);

                ax += x_step;
                ay += y_step;

                if (ax >= a)
                {
                    dx+=sx;
                    ax -= a;
                }

                if (ay >= a)
                {
                    dy+=sy;
                    ay -= a;
                }

                count--;
            }
        }
        // Kresli neuzavrenou spojitou lomenou caru
        public void PolyLine(Point[] points)
        {
            int i;

            if (points.Length > 1)
            {
                for (i = 1; i < points.Length; i++)
                    Line(points[i - 1], points[i]);
            }
        }


        // Kresli uzavrenou konvexni vyplnenou oblas
        // img - struktura popisujici obrazek
        // v - pole bodu
        // npts - pocet bodu v poli v, bude nakresleno npts car, kterymi je oblast omezena
        // color  - hodnota pixelu
        public void FillConvexPoly(Point[] points)
        {
            int minKX = 3;
            int maxKX = -1;
            int minKY = 3;
            int maxKY = -1;

            if (Clipping)
            {
                foreach (Point p in points)
                {
                    int kx = 1, ky = 1;

                    if (p.X < XMin)
                        kx = 0;
                    else if (p.X > XMax)
                        kx = 2;

                    if (p.Y < YMin)
                        ky = 0;
                    else if (p.Y > YMax)
                        ky = 2;

                    if (minKX > kx)
                        minKX = kx;
                    if (maxKX < kx)
                        maxKX = kx;

                    if (minKY > ky)
                        minKY = ky;
                    if (maxKY < ky)
                        maxKY = ky;
                }
            }


            if (!Clipping || ((maxKX==1 || minKX==1 || (maxKX==2 && minKX==0)) && (maxKY == 1 || minKY == 1 || (maxKY == 2 && minKY == 0))))
            {
                PolyEdge[] edge = new PolyEdge[2];

                int i, y, imin = 0, left = 0, right = 1, x1, x2;
                int edges = points.Length;
                int npts = points.Length;
                int xmin, xmax, ymin, ymax;
                Point p0;

                p0 = points[edges - 1];
                xmin = xmax = points[0].X;
                ymin = ymax = points[0].Y;

                for (i = 0; i < edges; i++)
                {
                    Point p = points[i];
                    if (p.Y < ymin)
                    {
                        ymin = p.Y;
                        imin = i;
                    }

                    ymax = Math.Max(ymax, p.Y);
                    xmax = Math.Max(xmax, p.X);
                    xmin = Math.Min(xmin, p.X);

                    p0 = p;
                }

                if (edges < 3)
                    return;

                edge[0].idx = edge[1].idx = imin;

                edge[0].ye = edge[1].ye = y = ymin;
                edge[0].di = 1;
                edge[1].di = edges - 1;

                int dy = y;

                do
                {
                    if (y < ymax || y == ymin)
                    {
                        for (i = 0; i < 2; i++)
                        {
                            if (y >= edge[i].ye)
                            {
                                int idx = edge[i].idx, di = edge[i].di;
                                int ye, ty = 0;
                                double xs = 0, xe;

                                for (;;)
                                {
                                    ty = points[idx].Y;
                                    if (ty > y || edges == 0)
                                        break;
                                    xs = points[idx].X;
                                    idx += di;
                                    if (idx >= npts)
                                        idx -= npts;
                                    edges--;
                                }

                                ye = ty;
                                xe = points[idx].X;

                                /* no more edges */
                                if (y >= ye)
                                    return;

                                edge[i].ye = ye;
                                edge[i].dx = ((xe - xs) / (ye - y));
                                edge[i].x = xs;
                                edge[i].idx = idx;
                            }
                        }
                    }

                    if (edge[left].x > edge[right].x)
                    {
                        left ^= 1;
                        right ^= 1;
                    }

                    x1 = (int)edge[left].x;
                    x2 = (int)edge[right].x;

                    HLine(x1, dy, x2 - x1);

                    edge[left].x += edge[left].dx;
                    edge[right].x += edge[right].dx;
                    dy++;
                }
                while (++y <= ymax);
            }
        }


        // kresli kruh
        // img - struktura popisujici obrazek
        // center - stred kruhu
        // radius - polomer kruhu
        // color  - hodnota pixelu
        public void Circle(Point center, int radius)
        {
            int x0 = center.X;
            int y0 = center.Y;

            if (!Clipping || !((x0 + radius < XMin) || (x0 - radius > XMax) || (y0 + radius < YMin) || (y0 - radius > YMax)))
            {
                int x = radius;
                int y = 0;
                int decisionOver2 = 1 - x;   // Decision criterion divided by 2 evaluated at x=r, y=0

                while (x >= y)
                {
                    DrawPixel(x + x0, y + y0);
                    DrawPixel(y + x0, x + y0);
                    DrawPixel(-x + x0, y + y0);
                    DrawPixel(-y + x0, x + y0);
                    DrawPixel(-x + x0, -y + y0);
                    DrawPixel(-y + x0, -x + y0);
                    DrawPixel(x + x0, -y + y0);
                    DrawPixel(y + x0, -x + y0);
                    y++;
                    if (decisionOver2 <= 0)
                    {
                        decisionOver2 += 2 * y + 1;   // Change in decision criterion for y -> y+1
                    }
                    else
                    {
                        x--;
                        decisionOver2 += 2 * (y - x) + 1;   // Change for y -> y+1, x -> x-1
                    }
                }
            }
        }

        // kresli vyplneny kruh
        // img - struktura popisujici obrazek
        // center - stred kruhu
        // radius - polomer kruhu
        // color  - hodnota pixelu
        public void FillCircle(Point center, int radius)
        {
            int x0 = center.X;
            int y0 = center.Y;
            if (!Clipping || !((x0 + radius < XMin) || (x0 - radius > XMax) || (y0 + radius < YMin) || (y0 - radius > YMax)))
            {
                int x = radius;
                int y = 0;
                int xChange = 1 - (radius << 1);
                int yChange = 0;
                int radiusError = 0;

                while (x >= y)
                {
                    HLine(x0 - x, y0 + y, 2 * x);
                    HLine(x0 - x, y0 - y, 2 * x);

                    HLine(x0 - y, y0 + x, 2 * y);
                    HLine(x0 - y, y0 - x, 2 * y);

                    y++;
                    radiusError += yChange;
                    yChange += 2;
                    if (((radiusError << 1) + xChange) > 0)
                    {
                        x--;
                        radiusError += xChange;
                        xChange += 2;
                    }
                }
            }
        }

        public void ThickLine(Point p0, Point p1, int thickness)
        {
            if (thickness <= 1)
            {
                Line(p0, p1);
            }
            else
            {
                Point[] pt = new Point[4];
                double dx = (p0.X - p1.X), dy = (p1.Y - p0.Y);
                double r = dx * dx + dy * dy;
                int oddThickness = thickness & 1;

                r = ((double)thickness + oddThickness * 0.5) / Math.Sqrt(r);
                dx = dy * r;
                dy = dx * r;

                pt[0].X = (int)(p0.X + dx);
                pt[0].Y = (int)(p0.Y + dy);
                pt[1].X = (int)(p0.X - dx);
                pt[1].Y = (int)(p0.Y - dy);
                pt[2].X = (int)(p1.X - dx);
                pt[2].Y = (int)(p1.Y - dy);
                pt[3].X = (int)(p1.X + dx);
                pt[3].Y = (int)(p1.Y + dy);

                FillConvexPoly(pt);
            }
        }
    }
}


