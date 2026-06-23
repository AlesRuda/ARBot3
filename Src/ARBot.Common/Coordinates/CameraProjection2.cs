using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using System.Windows.Media.Media3D;

namespace ARBot.Common.Coordinates
{
    public class CameraProjection2: ICameraProjection
    {
        public static CameraProjection2 Cam21
        {
            get
            {
                return new CameraProjection2(new Matrix(new double[,] { { 363, 0, 0 }, { 0.4, 363, 0 }, { 366.4, 251, 1 } }), new Matrix(-0.3133, 0.0991, -0.0141), new Matrix(6.5e-4, 3.8e-5));
            }
        }
        Matrix3D p;
        Matrix3D k;
        double k1, k2, k3;
        double p1, p2;
        double cx, cy;
        double fx, fy;
        double s;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="k">instrinsic matrix 3x3</param>
        /// <param name="pixelWidth">Velikost pixelu v m</param>
        /// <param name="nativeResolutin">Fyzicke rozliseni kamery</param>
        /// <param name="currentResolution">Rozliseni snimku</param>
        public CameraProjection2(Matrix k, Matrix radialDistortion, Matrix tangentialDistortion)
        {
            this.k = new Matrix3D(
                    k[0, 0], k[0, 1], k[0, 2], 0,
                    k[1, 0], k[1, 1], k[1, 2], 0,
                    k[2, 0], k[2, 1], k[2, 2], 0,
                    0, 0, 0, 1);

            fx = k[0, 0];
            fy = k[1, 1];
            s = k[1, 0];
            cx = k[2, 0];
            cy = k[2, 1];

            k1 = radialDistortion[0, 0];
            k2 = radialDistortion[1, 0];
            k3 = radialDistortion[2, 0];

            p1 = tangentialDistortion[0, 0];
            p2 = tangentialDistortion[1, 0];
            SetOrientation(Conversions.CameraToWordTransform(0, 0, 0, new Vector3D(0, 0, 0)));
        }


        void ToDistortCentered(double ux, double uy, out double dx, out double dy)
        {
            double x = ux;
            double y = uy;

            double r2 = x * x + y * y;
            double k = 1 + r2 * (k1 + r2 * (k2 + k3 * r2));
            x = x * k + (2 * p1 * x * y + p2 * (r2 + 2 * x * x));
            y = y * k + (p1 * (r2 + 2 * y * y) + 2 * p2 * x * y);
            dx = x;
            dy = y;
        }


        void ToDistort(double ux, double uy, out double dx, out double dy)
        {
            ToDistortCentered((ux - cx)/fx, (uy - cy)/fy, out dx, out dy);
            dx =dx*fx+ cx;
            dy =dy*fy+ cy;
        }

        public Image<T> UnDistort<T>(Image<T> d, int width, int height) where T : IPixel, new()
        {
            Image<T> u = new Image<T>(width, height);

            T pu = new T();
            pu.Data = u.Data;

            T pd = new T();
            pd.Data = d.Data;

            double w2 = width/2;
            double h2 = height/2;

            double w = d.Width;
            double h = d.Height;

            double dx;
            double dy;

            double ux2;
            double uy2;

            for (double ux = 0; ux < width; ux++)
            {
                for (double uy = 0; uy < height; uy++)
                {
                    ux2 = (ux - w2) / fx;
                    uy2 = (uy - h2) / fy;

                    ToDistortCentered(ux2, uy2, out dx, out dy);
                    dx = dx * fx + cx;
                    dy = dy * fy + cy;

                    int idx = (int)dx;
                    int idy = (int)dy;
                    if (idx >= 0 && idx < w && idy >= 0 && idy < h)
                    {

                        pu.Index = u.Index((int)ux, (int)uy);
                        pd.Index = d.Index(idx, idy);

                        pu.Values = pd.Values;
                    }
                }
            }

            return u;
        }

        public Image<T> UnDistort<T>(Image<T> d) where T: IPixel, new()
        {
            Image<T> u = new Image<T>(d.Width, d.Height);

            T pu = new T();
            pu.Data = u.Data;

            double w = d.Width;
            double h = d.Height;

            double dx;
            double dy;

            for(double ux=0; ux<w;ux++)
            {
                for (double uy = 0; uy < h; uy++)
                {
                    ToDistort(ux, uy, out dx, out dy);

                    pu.Index = u.Index((int)ux, (int)uy);

                    // na d se pouziva indexer, aby doslo k omezeni na max rozmery
                    pu.Values = d[(int)dx, (int)dy].Values;
                }
            }

            return u;
        }


        /// <summary>
        /// Inicializuje orientaci kamery pred vypoctem transformace
        /// </summary>
        /// <param name="yaw">Uhel natoceni kamery v radianech. 0 je na vychod, a kladny smer je v protismeru hodinek.</param>
        /// <param name="pitch">Uhel skloneni kamery v radianech. 0 je vodorovne a roste smerem dolu.</param>
        /// <param name="roll">Pravoleve otoceni oproti vodorovne rovine v radianech </param>
        /// <param name="offset">Posunuti kamery vzhledem k rovine po ktere jede robot. (pouziva jen Z)</param>
        public void SetOrientation(double yaw, double pitch, double roll, Vector3D offset)
        {
            var m = Conversions.WordToWordTransform(yaw, roll, pitch, new Vector3D());
            var i1 = Matrix3D.Identity;

            var o = m.Transform(-offset);
//            Debug.WriteLine(string.Format("{0} - {1}", offset, o));
            m.Translate(o);
            p = m* k;
        }
        /// <summary>
        /// Transformuje svetove souradnice do souradnic kamery (pocatek vlevo dole).
        /// </summary>
        /// <param name="x">Roste smerem na jih v metrech.</param>
        /// <param name="y">Roste smerem nahoru v metrech.</param>
        /// <param name="z">Roste smerem na zapad v metrech.</param>
        /// <param name="unDistort">Odstrnit zkresleni objektivu.</param>
        /// <param name="xc">X v rovine kamery. Roste smerem doprava v pixlech.</param>
        /// <param name="yc">Y v rovine kamery. Roste smerem dolu v pixlech.</param>
        public bool Transform(double x, double y, double z, bool unDistort, ref double xc, ref double yc)
        {
            var p = this.p.Transform(new Point3D(x, y, z));
            z = p.Z;
            if (z < -0)
                return false;
            xc = p.X/z;
            yc = p.Y/z;
            if(unDistort)
                ToDistort(xc, yc, out xc, out yc);
            return true;
        }
        /// <summary>
        /// Transformuje souradnice v rovine po niz jede robot (pocatek v miste robotu) do roviny kamery (pocatek uprostred obrazku).
        /// </summary>
        /// <param name="x">Roste smerem na vychod v metrech.</param>
        /// <param name="y">Roste smerem na sever v metrech.</param>
        /// <param name="xc">X v rovine kamery. Roste smerem nahoru v pixlech.</param>
        /// <param name="yc">Y v rovine kamery. Roste smerem doprava v pixlech.</param>
        public bool Transform(double x, double y, ref double xc, ref double yc)
        {
            return false;
        }

        /// <summary>
        /// Transformuje souradnice v rovine kamery (pocatek uprostred obrazku) do roviny po niz jede robot (pocatek v miste robotu).
        /// </summary>
        /// <param name="xc">X v rovine kamery. Roste smerem nahoru v pixlech.</param>
        /// <param name="yc">Y v rovine kamery. Roste smerem doprava v pixlech.</param>
        /// <param name="x">Roste smerem na vychod v metrech.</param>
        /// <param name="y">Roste smerem na sever v metrech.</param>
        public bool TransformBack(double xc, double yc, ref double x, ref double y)
        {
            return false;
        }

        public void SetOrientation(Matrix3D transform)
        {
            throw new NotImplementedException();
        }

        public List<Point2D> TargetPoly
        {
            get
            {
                return null;
            }
        }
    }
}
