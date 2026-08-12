using System;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;

namespace ARBot.Common.Vision.Synthetic
{
    /// <summary>
    /// Rasterizace simulovaneho pohledu kamery na scenu (viz doc/virtual-hw.md).
    /// <para>
    /// Na kazdy pixel jde jeden paprsek proti dvema vodorovnym rovinam - vozovce <c>z = 0</c>
    /// a trave <c>z = GrassHeightM</c>. Kandidat je ten prusecik, jehoz bod odpovida svemu povrchu
    /// (na rovine vozovky musi <see cref="RoadScene.IsRoad"/> platit, na rovine travy neplatit);
    /// z platnych vyhrava blizsi. Vyvysena trava tim dostane i spravnou okluzi hrany vozovky.
    /// </para>
    /// <para>
    /// Rasterizace je presna inverze rozbaleni ve <c>CameraFrameProcessor.BuildGrid</c>:
    /// bod v ramci robota = <c>Vector3.Transform(ray * d, projection.Transformation)</c>, takze
    /// ulozena hloubka je souradnice <b>Z v prostoru kamery</b> (ne euklidovska vzdalenost).
    /// </para>
    /// </summary>
    public sealed class SyntheticFrameRenderer
    {
        private readonly RoadScene scene;
        private readonly SyntheticSceneOptions options;

        /// <summary>Vysledek dotazu na povrch pod pixelem.</summary>
        private enum Surface { None, Road, Grass }

        /// <summary>Kanaly sumu - odlisuji nezavisle slozky pri stejnem pixelu a snimku.</summary>
        private const int ChannelDepth = 0;
        private const int ChannelGrass = 1;
        private const int ChannelColorB = 2;
        private const int ChannelColorG = 3;
        private const int ChannelColorR = 4;

        public SyntheticFrameRenderer(RoadScene scene, SyntheticSceneOptions options)
        {
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Vykresli hloubkovy obraz (Gray16 v milimetrech, 0 = neplatny pixel).
        /// </summary>
        /// <param name="projection">Projekce kamery (nese intrinsics i montazni transformaci).</param>
        /// <param name="pose">Poza robota v lokalni ENU rovine.</param>
        /// <param name="frameIndex">Poradi snimku - vstup do sumu (reprodukovatelnost).</param>
        /// <param name="depth">Cilovy obraz; prepise se cely.</param>
        public void RenderDepth(IDepthCameraProjection projection, RobotState pose, int frameIndex,
                                Image<Gray16> depth)
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            if (depth == null) throw new ArgumentNullException(nameof(depth));

            var table = projection.Camera2DToCamera3D;
            var m = projection.Transformation;
            var eye = m.Translation;

            int w = depth.Width, h = depth.Height;
            int tblH = table.GetLength(0), tblW = table.GetLength(1);
            var data = depth.Data;
            Array.Clear(data, 0, data.Length);

            double cos = Math.Cos(pose.Theta), sin = Math.Sin(pose.Theta);

            for (int y = 0; y < h; y++)
            {
                if (y >= tblH) break;
                for (int x = 0; x < w; x++)
                {
                    if (x >= tblW) break;

                    int pixel = y * w + x;
                    var s = Trace(table[y, x], m, eye, pose, cos, sin,
                                  GrassHeightAt(frameIndex, pixel), out double range);
                    if (s == Surface.None) continue;

                    if (options.DepthNoiseM > 0)
                        range += SyntheticNoise.Gaussian(options.Seed, frameIndex, pixel, ChannelDepth)
                                 * options.DepthNoiseM;

                    int mm = (int)Math.Round(range * 1000.0);
                    if (mm <= 0 || mm >= 65535) continue;

                    int o = (y * w + x) * 2;
                    data[o] = (byte)(mm & 0xff);
                    data[o + 1] = (byte)(mm >> 8);
                }
            }
        }

        /// <summary>
        /// Vystreli paprsek pixelu proti obema rovinam a vrati blizsi platny zasah.
        /// </summary>
        /// <param name="ray">Smer paprsku v prostoru kamery (x, y na jednotkove hloubce).</param>
        /// <param name="m">Transformace kamera -&gt; ramec robota.</param>
        /// <param name="eye">Pozice kamery v ramci robota.</param>
        /// <param name="pose">Poza robota (pro prevod zasahu do svetovych souradnic).</param>
        /// <param name="cos">Kosinus kurzu robota.</param>
        /// <param name="sin">Sinus kurzu robota.</param>
        /// <param name="grassHeight">Vyska roviny travy pro tento pixel (uz vcetne drsnosti) [m].</param>
        /// <param name="range">Hloubka zasahu = souradnice Z v prostoru kamery [m].</param>
        private Surface Trace(Point2D ray, in Matrix4x4 m, in Vector3 eye, RobotState pose,
                              double cos, double sin, double grassHeight, out double range)
        {
            range = 0;

            // Smer paprsku v ramci robota (jen rotacni cast - paprsek je vektor, ne bod).
            var dir = Vector3.TransformNormal(new Vector3(ray.X, ray.Y, 1f), m);
            if (Math.Abs(dir.Z) < 1e-9f) return Surface.None;   // paprsek rovnobezny s rovinami

            var best = Surface.None;
            double bestRange = double.PositiveInfinity;

            if (HitsPlane(0.0, dir, eye, pose, cos, sin, out double sRoad, out bool roadHere)
                && roadHere && sRoad < bestRange)
            {
                best = Surface.Road;
                bestRange = sRoad;
            }

            if (HitsPlane(grassHeight, dir, eye, pose, cos, sin, out double sGrass, out bool grassOnRoad)
                && !grassOnRoad && sGrass < bestRange)
            {
                best = Surface.Grass;
                bestRange = sGrass;
            }

            if (best == Surface.None || bestRange > options.MaxRangeM) return Surface.None;

            range = bestRange;
            return best;
        }

        /// <summary>
        /// Vykresli barevny obraz (BGR32): vozovka seda, vse ostatni zelene jako trava - vcetne
        /// oblohy nad horizontem (viz doc/virtual-hw.md).
        /// </summary>
        /// <remarks>
        /// Na rozdil od hloubky se NEuplatnuje <see cref="SyntheticSceneOptions.MaxRangeM"/>:
        /// barevna kamera vidi az k horizontu, dosah je omezeni hloubkoveho senzoru.
        /// </remarks>
        public void RenderColor(IDepthCameraProjection projection, RobotState pose, int frameIndex,
                                Image<BGR32> rgb)
        {
            if (projection == null) throw new ArgumentNullException(nameof(projection));
            if (pose == null) throw new ArgumentNullException(nameof(pose));
            if (rgb == null) throw new ArgumentNullException(nameof(rgb));

            var table = projection.Camera2DToCamera3D;
            var m = projection.Transformation;
            var eye = m.Translation;

            int w = rgb.Width, h = rgb.Height;
            int tblH = table.GetLength(0), tblW = table.GetLength(1);
            var data = rgb.Data;

            double cos = Math.Cos(pose.Theta), sin = Math.Sin(pose.Theta);

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int pixel = y * w + x;

                    bool road = false;
                    if (y < tblH && x < tblW)
                    {
                        var dir = Vector3.TransformNormal(new Vector3(table[y, x].X, table[y, x].Y, 1f), m);
                        // Zajima nas jen povrch, ne vzdalenost - vozovka je videt i za dosah hloubky.
                        road = Math.Abs(dir.Z) >= 1e-9f
                               && HitsPlane(0.0, dir, eye, pose, cos, sin, out _, out bool onRoad)
                               && onRoad;
                    }

                    int o = pixel * 4;
                    data[o] = Noisy(road ? options.RoadB : options.GrassB, frameIndex, pixel, ChannelColorB);
                    data[o + 1] = Noisy(road ? options.RoadG : options.GrassG, frameIndex, pixel, ChannelColorG);
                    data[o + 2] = Noisy(road ? options.RoadR : options.GrassR, frameIndex, pixel, ChannelColorR);
                    data[o + 3] = 255;
                }
        }

        /// <summary>Prida sum k barevne slozce a orizne do rozsahu bajtu.</summary>
        private byte Noisy(byte value, int frameIndex, int pixel, int channel)
        {
            if (options.ColorNoise <= 0) return value;

            double v = value + SyntheticNoise.Gaussian(options.Seed, frameIndex, pixel, channel) * options.ColorNoise;
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)Math.Round(v);
        }

        /// <summary>
        /// Vyska roviny travy pro dany pixel: stredni hodnota zdrsnena sumem. Drsnost se tyka
        /// jen travy - vozovka zustava presna rovina.
        /// </summary>
        private double GrassHeightAt(int frameIndex, int pixel)
        {
            if (options.GrassRoughnessM <= 0) return options.GrassHeightM;

            return options.GrassHeightM
                   + SyntheticNoise.Gaussian(options.Seed, frameIndex, pixel, ChannelGrass)
                     * options.GrassRoughnessM;
        }

        /// <summary>
        /// Protne paprsek s vodorovnou rovinou v dane vysce a rekne, zda zasah lezi na vozovce.
        /// </summary>
        /// <returns>true, kdyz je prusecik pred kamerou.</returns>
        private bool HitsPlane(double height, in Vector3 dir, in Vector3 eye, RobotState pose,
                               double cos, double sin, out double s, out bool onRoad)
        {
            s = (height - eye.Z) / dir.Z;
            onRoad = false;
            if (s <= 0 || double.IsNaN(s) || double.IsInfinity(s)) return false;

            // Zasah v ramci robota -> svetove souradnice (rotace o kurz + posun na pozici robota).
            double hx = eye.X + s * dir.X;
            double hy = eye.Y + s * dir.Y;

            onRoad = scene.IsRoad(pose.X + hx * cos - hy * sin,
                                  pose.Y + hx * sin + hy * cos);
            return true;
        }
    }
}
