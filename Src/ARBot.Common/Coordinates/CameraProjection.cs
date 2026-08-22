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
            => info ??= CameraProjectionInfo.Capture(intrinsics, inverseIntrinsics, from, to, transformation,
                                                     colorIntrinsics, colorToDepth, depthToColor);

        // Barevna intrinsika a extrinsiky color<->depth. Doplneno 21. 8. 2026: kamera je zna, ale
        // do ARBot.Common nemely kudy vylezt (D435CameraProjection je drzel v privatnich polich jen
        // pro nativni ColorPixel23D, na ARM je konstruktor zahazoval). Prepocet hranic cesty do
        // metru je potrebuje - viz Vision/ColorEdgeProjector.
        private Intrinsics colorIntrinsics;
        private Matrix4x4 colorToDepth = Matrix4x4.Identity;
        private Matrix4x4 depthToColor = Matrix4x4.Identity;

        /// <summary>
        /// Doplni popis o <b>barevnou</b> intrinsiku a extrinsiky color↔depth (volitelne; identita
        /// = zarovnane streamy). Voli to ten, kdo projekci stavi z kamery - HAL.
        /// </summary>
        public void SetColorAlignment(Intrinsics color, Matrix4x4? colorToDepthExtrinsics = null,
                                      Matrix4x4? depthToColorExtrinsics = null)
        {
            colorIntrinsics = color;
            colorToDepth = colorToDepthExtrinsics ?? Matrix4x4.Identity;
            depthToColor = depthToColorExtrinsics ?? Matrix4x4.Identity;
            info = null;         // popis se prepocita
            edgeProjector = null;   // i prepocet pixel -> metry
        }

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
            // POZOR (2026-08-14): rotationWorld2Cam je inverze CELE transformace, tedy VCETNE
            // posunuti kamery (viz SetOrientation - M41..M43 se pred inverzi vraci zpet).
            // Vector3.Transform translaci matice uplatnuje, takze rucni odecteni offsetu navic
            // ho zapocitalo DVAKRAT: bod zeme se promitl o ~95 px vedle (u Profile.LeftCameraOff,
            // vyska 0,52 m) a blizke body metoda dokonce zahodila jako "mimo obraz". Dopad:
            // OccupancyIntegrator vzorkoval oba kanaly ze spatnych pixelu -> plocha mimo cestu se
            // neoznacila jako nesjizdna. Overeno round-tripem pixel -> zem -> pixel
            // (VirtualHwOccupancyTest.ProjekceTamZpet_JeInverzniKRenderu): puvodne chyba ~95 px,
            // nyni < 0,5 px.
            //
            // Puvodni (chybna) varianta - ponechana do overeni na HW, viz CLAUDE.md:
            //     var p = new Vector3(x - offset.X, y - offset.Y, -offset.Z);
            //     p = Vector3.Transform(p, rotationWorld2Cam);
            var p = Vector3.Transform(new Vector3(x, y, 0f), rotationWorld2Cam);

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
        /// Transformuje souradnice v rovine color kamery (pocatek vlevo nahore) do souradnic robotu
        /// (pocatek v miste robotu) <b>pomoci hloubky</b>. Roli hraje orientace nastavena
        /// <see cref="SetOrientation"/>.
        ///
        /// <para><b>Zmena 21. 8. 2026: hloubka se opravdu pouziva.</b> Do te doby tato metoda
        /// parametr <c>depth</c> <b>ignorovala</b> a promitala paprsek na rovinu zeme — u pixelu
        /// blizko horizontu to davalo body ve stovkach metru (nameren maximum 444–803 m). Skutecny
        /// vypocet delaly az prepisy v <c>D435CameraProjection</c>, ktere ale volaly nativni
        /// <c>ColorPixel23D</c> — a to v <c>NativeLib</c> <b>neni</b> (na ARM varianta rovnou
        /// vyhazovala <c>NotSupportedException</c>). Ted to umi baze pro vsechny platformy stejne,
        /// takze ty prepisy zmizely. Viz doc/map-correlation-localization.md.</para>
        ///
        /// <para>Barevny pixel se na hloubkovy prevadi podle
        /// <see cref="SetColorAlignment"/> (intrinsika barevneho streamu + extrinsiky color↔depth).
        /// <b>Kdyz je barevna intrinsika neznama</b>, bere se intrinsika teto projekce — tedy
        /// predpoklad, ze body pochazi z TOHO SAMEHO streamu, ktery projekce popisuje.</para>
        /// </summary>
        /// <param name="points">Body v rovine barevne kamery. Roste doprava a dolu v pixlech.</param>
        /// <param name="depth">Hloubkova mapa odpovidajici snimku.</param>
        /// <returns>Body v souradnicich robotu; <c>A == 0</c> = neplatny (chybi hloubka, mimo obraz,
        /// mimo dosah senzoru).</returns>
        public virtual List<Common.Point4D> TransformBack(List<Point> points, Image<Gray16> depth)
        {
            var l = new List<Common.Point4D>(points?.Count ?? 0);
            if (points == null) return l;

            var projector = EdgeProjector();
            foreach (var p in points)
                l.Add(projector.ToRobot(p.X, p.Y, depth));
            return l;
        }

        // Prepocet pixel -> metry drzi Vision/ColorEdgeProjector (ma testy); tady se jen cachuje,
        // protoze se stavi z nemennych parametru projekce.
        private ARBot.Common.Vision.ColorEdgeProjector edgeProjector;

        private ARBot.Common.Vision.ColorEdgeProjector EdgeProjector()
            => edgeProjector ??= new ARBot.Common.Vision.ColorEdgeProjector(
                   colorIntrinsics ?? intrinsics, intrinsics, this, colorToDepth, depthToColor);
    }
}
