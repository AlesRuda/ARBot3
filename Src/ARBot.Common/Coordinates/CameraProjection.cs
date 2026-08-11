using ARBot.Common.Common;
using ARBot.Common.LocalMaps;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Coordinates
{
    /// <summary>
    /// Projekce kamery
    /// </summary>
    public class CameraProjection : ICameraProjection, IDepthCameraProjection
    {
        Intrinsics intrinsics;
        Intrinsics inverseIntrinsics;

        Matrix4x4 from;
        Matrix4x4 to;

        Point2D[,] toDistortCache;
        protected Point2D[,] camera2DToCamera3DCache;
        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="intrinsics"></param>
        /// <param name="inverseIntrinsics"></param>
        public CameraProjection(Intrinsics intrinsics, Intrinsics inverseIntrinsics, Matrix4x4 from, Matrix4x4 to)
        {
            this.from = from;
            this.to = to;

            this.intrinsics = intrinsics;
            this.inverseIntrinsics = inverseIntrinsics;
            toDistortCache = new Point2D[inverseIntrinsics.Width, inverseIntrinsics.Height];
            camera2DToCamera3DCache = new Point2D[inverseIntrinsics.Height, inverseIntrinsics.Width];
            for (int x = 0; x < inverseIntrinsics.Width; x++)
            {
                for (int y = 0; y < inverseIntrinsics.Height; y++)
                {
                    float dx;
                    float dy;
                    // POZOR: pretypovani na float je nutne - s int argumenty by se vybralo
                    // pretizeni ToDistort(int,int), ktere cte prave plnenou (jeste prazdnou)
                    // toDistortCache, a cache by se naplnila samymi nulami.
                    ToDistort((float)x, (float)y, out dx, out dy);
                    toDistortCache[x, y] = new Point2D(dx, dy);
                    Camera2DToCamera3DCalc(x, y, out dx, out dy);
                    camera2DToCamera3DCache[y, x] = new Point2D((float)dx, (float)dy);
                }
            }
        }
        /// <summary>
        /// Transformuje 3D bod v prostoru kamery na pixel kamery.
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        public Point2D? Camera3DToCamera2D(Vector3 point)
        {
            Point2D pixel = new Point2D();

            double x = point.X / point.Z;
            double y = point.Y / point.Z;

            if (intrinsics.Model == Intrinsics.Distortion.ModifiedBrownConrady)
            {
                double r2 = x * x + y * y;
                double f = 1.0 + intrinsics.Coeffs[0] * r2 + intrinsics.Coeffs[1] * r2 * r2 + intrinsics.Coeffs[4] * r2 * r2 * r2;

                x *= f;
                y *= f;

                double dx = x + 2.0 * intrinsics.Coeffs[2] * x * y + intrinsics.Coeffs[3] * (r2 + 2 * x * x);
                double dy = y + 2.0 * intrinsics.Coeffs[3] * x * y + intrinsics.Coeffs[2] * (r2 + 2 * y * y);

                x = dx;
                y = dy;

            }

            if (intrinsics.Model == Intrinsics.Distortion.Ftheta)
            {
                double r = Math.Sqrt(x * x + y * y);
                double rd = (1.0 / intrinsics.Coeffs[0] * Math.Atan(2.0 * r * Math.Tan(intrinsics.Coeffs[0] / 2.0)));

                x *= rd / r;
                y *= rd / r;
            }

            pixel.X = (float)x * intrinsics.Fx + intrinsics.PPx;
            pixel.Y = (float)y * intrinsics.Fy + intrinsics.PPy;

            if (pixel.X < 0 || pixel.X >= intrinsics.Width || pixel.Y < 0 || pixel.Y >= intrinsics.Height)
                return null;
            return pixel;
        }

        void ToDistortCentered(float ux, float uy, out float dx, out float dy)
        {
            if (intrinsics.Model == Intrinsics.Distortion.InverseBrownConrady)
            {
                float r2 = ux * ux + uy * uy;
                float f = 1 + inverseIntrinsics.Coeffs[0] * r2 + inverseIntrinsics.Coeffs[1] * r2 * r2 + inverseIntrinsics.Coeffs[4] * r2 * r2 * r2;
                dx = ux * f + 2 * inverseIntrinsics.Coeffs[2] * ux * uy + inverseIntrinsics.Coeffs[3] * (r2 + 2 * ux * ux);
                dy = uy * f + 2 * inverseIntrinsics.Coeffs[3] * ux * uy + inverseIntrinsics.Coeffs[2] * (r2 + 2 * uy * uy);
            }
            else
            {
                dx = ux;
                dy = uy;
            }
        }

        void ToDistort(float ux, float uy, out float dx, out float dy)
        {
            ToDistortCentered((ux - inverseIntrinsics.PPx) / inverseIntrinsics.Fx, (uy - inverseIntrinsics.PPy) / inverseIntrinsics.Fy, out dx, out dy);
            dx = dx * inverseIntrinsics.Fx + inverseIntrinsics.PPx;
            dy = dy * inverseIntrinsics.Fy + inverseIntrinsics.PPy;
        }

        /// <summary>
        /// Zkresleni pro celopixelovou souradnici - z cache, mimo rozsah dopocet.
        /// Vraci pixelove souradnice (stejne jako <see cref="ToDistort(float,float,out float,out float)"/>).
        /// </summary>
        void ToDistort(int ux, int uy, out float dx, out float dy)
        {
            if (ux < 0 || uy < 0 || ux >= inverseIntrinsics.Width || uy >= inverseIntrinsics.Height)
            {
                // Pretypovani na float je nutne: s int argumenty by tato vetev volala SAMA SEBE
                // (nekonecna rekurze). Float pretizeni uz prevod do pixelu (Fx/PPx) dela samo,
                // takze se tu uz znovu neskaluje.
                ToDistort((float)ux, (float)uy, out dx, out dy);
            }
            else
            {
                dx = toDistortCache[ux, uy].X;
                dy = toDistortCache[ux, uy].Y;
            }
        }
        void Camera2DToCamera3DCalc(float px, float py, out float rx, out float ry)
        {
            float x = (px - inverseIntrinsics.PPx) / inverseIntrinsics.Fx;
            float y = (py - inverseIntrinsics.PPy) / inverseIntrinsics.Fy;

            if (intrinsics.Model == Intrinsics.Distortion.InverseBrownConrady)
            {
                float r2 = x * x + y * y;
                float f = 1 + inverseIntrinsics.Coeffs[0] * r2 + inverseIntrinsics.Coeffs[1] * r2 * r2 + inverseIntrinsics.Coeffs[4] * r2 * r2 * r2;
                float ux = x * f + 2 * inverseIntrinsics.Coeffs[2] * x * y + inverseIntrinsics.Coeffs[3] * (r2 + 2 * x * x);
                float uy = y * f + 2 * inverseIntrinsics.Coeffs[3] * x * y + inverseIntrinsics.Coeffs[2] * (r2 + 2 * y * y);

                rx = ux;
                ry = uy;
            }
            else
            {
                rx = x;
                ry = y;
            }
        }

        /// <summary>
        /// Ze souradnic bodu x,y (pocatek vlevo nahore) v obrazku a hloubky spocte xyz pozici v prostoru kamery.
        /// </summary>
        /// <param name="x">Roste smerem doprava. Pocatek vlevo,</param>
        /// <param name="y">Roste smerem dolu. Pocatek nahore.</param>
        /// <param name="depth">Hloubka v miste bodu x,y</param>
        /// <returns>XYZ souradnice bodu v prostoru kamery. Pocatek je cca uprostred obrazu. X roste doprava. Y rostem smerem dolu z smerem od kamery. </returns>
        public Vector3 Camera2DToCamera3D(int x, int y, float depth)
        {
            float rx;
            float ry;

            if (x < 0 || y < 0 || x >= inverseIntrinsics.Width || y >= inverseIntrinsics.Height)
                Camera2DToCamera3DCalc(x, y, out rx, out ry);
            else
            {
                var p = camera2DToCamera3DCache[y, x];
                rx = p.X;
                ry = p.Y;
            }

            Vector3 point = new Vector3();

            point.X = depth * rx;
            point.Y = depth * ry;
            point.Z = depth;

            return point;
        }

        /// <summary>
        /// Odstranuje zkresleni kamery.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="d"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
        public Image<T> UnDistort<T>(Image<T> d, int width, int height) where T : IPixel, new()
        {
            Image<T> u = new Image<T>(width, height);

            T pu = new T();
            pu.Data = u.Data;

            T pd = new T();
            pd.Data = d.Data;

            float w2 = width / 2;
            float h2 = height / 2;

            float w = d.Width;
            float h = d.Height;

            float dx;
            float dy;

            float ux2;
            float uy2;

            for (float ux = 0; ux < width; ux++)
            {
                for (float uy = 0; uy < height; uy++)
                {
                    ux2 = (ux - w2) / inverseIntrinsics.Fx;
                    uy2 = (uy - h2) / inverseIntrinsics.Fy;

                    ToDistortCentered(ux2, uy2, out dx, out dy);
                    dx = dx * inverseIntrinsics.Fx + inverseIntrinsics.PPx;
                    dy = dy * inverseIntrinsics.Fy + inverseIntrinsics.PPy;

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

        /// <summary>
        /// Odstranuje zkresleni kamery.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="d"></param>
        /// <returns></returns>
        public Image<T> UnDistort<T>(Image<T> d) where T : IPixel, new()
        {
            Image<T> u = new Image<T>(d.Width, d.Height);

            T pu = new T();
            pu.Data = u.Data;

            float w = d.Width;
            float h = d.Height;

            float dx;
            float dy;

            for (int ux = 0; ux < w; ux++)
            {
                for (int uy = 0; uy < h; uy++)
                {
                    ToDistort(ux, uy, out dx, out dy);

                    pu.Index = u.Index(ux, uy);

                    // na d se pouziva indexer, aby doslo k omezeni na max rozmery
                    pu.Values = d[(int)dx, (int)dy].Values;
                }
            }

            return u;
        }

        /// <summary>
        /// rotace a posunuti souradnic kamery do sveta s nulou yaw na vychod
        /// </summary>
        Matrix4x4 transformation;
        /// <summary>
        /// Rotace souradnic kamery do svetovych
        /// </summary>
        public Matrix4x4 rotation;
        /// <summary>
        /// Rotace svetovych souradnic do souradnic kamery
        /// </summary>
        Matrix4x4 rotationWorld2Cam;
        /// <summary>
        /// Posunuti kamery nad terenem 
        /// </summary>
        public Vector3 offset;

        public Matrix4x4 Transformation => transformation;

        Point2D[,] IDepthCameraProjection.Camera2DToCamera3D => camera2DToCamera3DCache;

        // Popis projekce pro serializaci; staví se lazy a zneplatnuje ho SetOrientation
        // (jedina vec, ktera se za zivota projekce meni).
        private CameraProjectionInfo info;

        /// <inheritdoc/>
        public CameraProjectionInfo Info
            => info ??= CameraProjectionInfo.Capture(intrinsics, inverseIntrinsics, from, to, transformation);

        /// <summary>
        /// Polygon oznacujici kam se na vozovce promitne obraz kamery
        /// </summary>
        public List<Point2D> TargetPoly
        {
            get
            {
                var poly = new List<Point2D>();
                float x = 0, y = 0;
                // levy dolni
                if (TransformBack(0, inverseIntrinsics.Height - 1, ref x, ref y))
                {
                    poly.Add(new Point2D(x, y));
                }
                // pravy dolni
                if (TransformBack(inverseIntrinsics.Width - 1, inverseIntrinsics.Height - 1, ref x, ref y))
                {
                    poly.Add(new Point2D(x, y));
                }
                int i = 0;
                // pravy horni
                while (i < inverseIntrinsics.Height)
                {
                    if (TransformBack(inverseIntrinsics.Width - 1, i++, ref x, ref y))
                    {
                        poly.Add(new Point2D(x, y));
                        break;
                    }
                }
                // levy horni
                while (i < inverseIntrinsics.Height)
                {
                    if (TransformBack(0, i++, ref x, ref y))
                    {
                        poly.Add(new Point2D(x, y));
                        break;
                    }
                }

                return poly;
            }
        }

        /// <summary>
        /// Nastavi orientaci kamery a pozici kamery
        /// </summary>
        /// <param name="transform">Natoceni a pozice kamery</param>
        /// <remarks>
        /// Pri postupnem aplikovani transormaci se nasobi zprava H12*H01.
        /// Hnm - transformace z nodu n na m
        /// </remarks>
        public void SetOrientation(Matrix4x4 transform)
        {
            // 1. Uložení původní kompletní transformace
            transformation = transform;
            info = null;   // popis pro serializaci se prepocita az bude potreba

            // 2. Vytvoření čisté rotace (zkopírujeme matici a vynulujeme její posun)
            rotation = transform;
            rotation.Translation = Vector3.Zero; // Tímto se M41, M42, M43 nastaví na 0 (včetně M14, M24, M34 z definice)

            // 3. V původním kódu se na tomto řádku do 'rotationWorld2Cam' i do 'rotation' 
            // paradoxně vrátil původní offset (protože OffsetX/Y/Z se četly z té osekané rotace, kde byly nuly,
            // ale původní M14, M24, M34 tam zůstaly).
            // Pro zachování identického chování 1:1 s WPF to přepíšeme takto:
            rotationWorld2Cam = rotation;
            rotationWorld2Cam.M41 = transform.M41;
            rotationWorld2Cam.M42 = transform.M42;
            rotationWorld2Cam.M43 = transform.M43;
            rotation = rotationWorld2Cam; // Obě proměnné teď mají opět matici i s posunem

            // 4. Inverze matice (WPF .Invert() měnilo matici na místě, System.Numerics vrací novou)
            if (Matrix4x4.Invert(rotationWorld2Cam, out Matrix4x4 inverted))
            {
                rotationWorld2Cam = inverted;
            }
            else
            {
                // Pojistka pro případ, že by matice nešla invertovat (byla singulární)
                rotationWorld2Cam = Matrix4x4.Identity;
            }

            // 5. Append(to) ve WPF znamenal: vynásob to maticí 'to' ZPRAVA (rotationWorld2Cam * to)
            rotationWorld2Cam = rotationWorld2Cam * to;

            // 6. Uložení čistého offsetu (pozice) z původní transformační matice
            this.offset = transform.Translation;
        }  
        
        /// <summary>
                 /// Mrak bodu v prostoru kamery tj. nebere v uvahu SetOrientation
                 /// </summary>
                 /// <param name="depth"></param>
                 /// <returns></returns>
        public List<ARBot.Common.Common.Point4D> GetPointCloud(Image<Gray16> depth)
        {
            var points = new List<ARBot.Common.Common.Point4D>();
            depth.ForEach((x, y, p) =>
            {
                var d = p.Value;
                if (d > 0 && d < 65535)
                {
                    var point = Camera2DToCamera3D(x, y, d * 0.001f);
                    var p1 = point;
                    //                    var p1 = transformation.Transform(point);
                    points.Add(new ARBot.Common.Common.Point4D() { X = (float)p1.X, Y = (float)p1.Y, Z = (float)p1.Z, A = 1 });
                }
            });
            return points;
        }

        /// <summary>
        /// Transformuje souradnice v rovine po niz jede robot (pocatek v miste robotu) do roviny kamery (pocatek vlevo nahore).
        /// </summary>
        /// <param name="x">Roste smerem na vychod v metrech.</param>
        /// <param name="y">Roste smerem na sever v metrech.</param>
        /// <param name="xc">X v rovine kamery. Roste smerem doprava v pixlech.</param>
        /// <param name="yc">Y v rovine kamery. Roste smerem dolu v pixlech.</param>
        public bool Transform(float x, float y, ref float xc, ref float yc)
        {
            var p = new Vector3(x - offset.X, y - offset.Y, -offset.Z);
            p=Vector3.Transform(p, rotationWorld2Cam);

            // Bod ZA kamerou (Z <= 0) neni videt. Bez teto kontroly by ho perspektivni deleni
            // v Camera3DToCamera2D promitlo na zdanlive platny pixel (deleni zapornym Z prevrati
            // znamenka), takze napr. bod 4 m za robotem by vysel jako pixel pred nim.
            if (p.Z <= 0)
                return false;

            var pc = Camera3DToCamera2D(p);

            if (pc == null)
                return false;

            xc = pc.Value.X;
            yc = pc.Value.Y;

            return true;
        }

        /// <summary>
        /// Transformuje souradnice v rovine kamery (pocatek vlevo nahore) do roviny po niz jede robot (pocatek v miste robotu).
        /// Roli hraje nastavena orientace kamery pomoci SetOrientation.
        /// </summary>
        /// <param name="xc">X v rovine kamery. Roste smerem doprava v pixlech.</param>
        /// <param name="yc">Y v rovine kamery. Roste smerem dolu v pixlech.</param>
        /// <param name="x">Roste smerem na vychod v metrech.</param>
        /// <param name="y">Roste smerem na sever v metrech.</param>
        /// <remarks>
        /// </remarks>
        public bool TransformBack(float xc, float yc, ref float x, ref float y)
        {
            var point = Camera2DToCamera3D((int)xc, (int)yc, 1);

            var vect = Vector3.Transform(point, rotation);
            if (vect.Z > 0)
                return false;
            var d = -offset.Z / vect.Z;
            x = offset.X + vect.X * d;
            y = offset.Y + vect.Y * d;
            return true;
        }
        /// <summary>
        /// Transformuje souradnice v rovine color kamery (pocatek vlevo nahore) do svetovych souradnic robotu (pocatek v miste robotu).
        /// Roli hraje nastavena orientace kamery pomoci SetOrientation.
        /// </summary>
        /// <param name="points">Body v rovine kamery. Roste smerem doprava a dolu v pixlech.</param>
        /// <param name="depth">Hloubkova mapa korespondujici k bodum points</param>
        /// <returns>Pole tranformovanych bodu do svetovych souradnic. Pokud je A slozka bodu rovna 0 (vlastne cely bod bude identicky 0) je tento bod nevalidni.</returns>
        public virtual List<Common.Point4D> TransformBack(List<Point> points, Image<Gray16> depth)
        {
            float x =0;
            float y =0;
            var l = new List<Common.Point4D>(points.Count);

            foreach(var p in points)
            {
                if (TransformBack(p.X, p.Y, ref x, ref y))
                    l.Add(new Common.Point4D() { X = (float)x, Y = (float)y, Z = 0, A=1 });
                else
                    l.Add(new Common.Point4D() { X = 0, Y = 0, Z = 0, A = 0 });
            }
            return l;
        }
    }
}
