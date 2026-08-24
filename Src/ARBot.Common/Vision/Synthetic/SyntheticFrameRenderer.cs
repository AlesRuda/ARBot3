using System;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Fusion;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Simulation;

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
                                  GrassHeightAt(frameIndex, pixel), out double range, options.MaxRangeM);
                    if (s == Surface.None) continue;

                    if (options.DepthNoiseM > 0)
                        range += DeterministicNoise.Gaussian(options.Seed, frameIndex, pixel, ChannelDepth)
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
        /// <param name="maxRange">Za timto dosahem se zasah zahodi [m]. Hloubka predava
        /// <see cref="SyntheticSceneOptions.MaxRangeM"/> (limit senzoru), <b>barva nekonecno</b> —
        /// barevna kamera vidi az k horizontu.</param>
        private Surface Trace(Point2D ray, in Matrix4x4 m, in Vector3 eye, RobotState pose,
                              double cos, double sin, double grassHeight, out double range,
                              double maxRange)
        {
            range = 0;

            // Smer paprsku v ramci robota (jen rotacni cast - paprsek je vektor, ne bod).
            var dir = Vector3.TransformNormal(new Vector3(ray.X, ray.Y, 1f), m);
            if (Math.Abs(dir.Z) < 1e-9f) return Surface.None;   // paprsek rovnobezny s rovinami

            var best = Surface.None;
            double bestRange = double.PositiveInfinity;

            bool hitRoad = HitsPlane(0.0, dir, eye, pose, cos, sin, out double sRoad, out bool roadHere);
            if (hitRoad && roadHere && sRoad < bestRange)
            {
                best = Surface.Road;
                bestRange = sRoad;
            }

            bool hitGrass = HitsPlane(grassHeight, dir, eye, pose, cos, sin,
                                      out double sGrass, out bool grassOnRoad);
            if (hitGrass && !grassOnRoad && sGrass < bestRange)
            {
                best = Surface.Grass;
                bestRange = sGrass;
            }

            // SVISLA STENA na rozhrani vozovky a travy. Bez ni v hloubce vznikala tenka dira podel
            // cele hranice cesty (nalezeno 23. 8. 2026): kdyz paprsek protne rovinu travy jeste NAD
            // vozovkou a rovinu vozovky uz ZA jejim okrajem, neplati ani jedna podminka a pixel
            // propadl jako Surface.None. Fyzikalne ale tráva neni papir - ma vysku, takze na okraji
            // cesty stoji svisla hrana a prave do ni paprsek narazi.
            //
            // Zasah se hleda bisekci na IsRoad mezi obema prusecíky: hledany bod je ten, kde paprsek
            // v horizontalni rovine prekroci okraj cesty. Lezi tedy VZDY mezi nimi - nikdy ne bliz
            // nez rovina travy, takze se tim trava nerendruje driv, nez kde skutecne je.
            if (best == Surface.None && hitRoad && hitGrass && !roadHere && grassOnRoad)
            {
                double sWall = FindRoadBoundary(sGrass, sRoad, dir, eye, pose, cos, sin);
                if (sWall > 0)
                {
                    best = Surface.Grass;   // je to bocni stena travniku, ne vozovka
                    bestRange = sWall;
                }
            }

            if (best == Surface.None || bestRange > maxRange) return Surface.None;

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
        /// <para>Geometrie je jinak <b>tataz jako u hloubky</b> (tyz <c>Trace</c>), takze vyvysena
        /// trava spravne <b>zakryva vozovku za sebou</b>. Do 24. 8. 2026 se tu protinala jen rovina
        /// vozovky, tedy trava se chovala jako papir bez vysky.</para>
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

            // Lezi trava v rovine vozovky? Pak nema co zaclonit a staci jedna rovina. Drsnost
            // travu zvedа i pri nulove vysce, takze se musi brat v potaz taky.
            bool flatGrass = options.GrassHeightM <= 0 && options.GrassRoughnessM <= 0;

            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int pixel = y * w + x;

                    // TYZ paprsek jako u hloubky, tedy proti OBEMA rovinam vcetne svisle steny
                    // travy - jen bez omezeni dosahu, protoze barevna kamera vidi az k horizontu.
                    //
                    // Driv se tu protinala jen rovina vozovky z = 0 (nalezeno 24. 8. 2026), takze
                    // barva se chovala, jako by trava byla papir: vyvysena trava NEZAKRYVALA cestu
                    // za sebou. Pro vizualni cestu (probability -> PathEdges -> koridor) to
                    // znamenalo, ze grassheight= nemela zadny efekt a hranice cesty se kreslila
                    // i tam, kde ji ve skutecnosti neni videt.
                    // RYCHLA CESTA pro travu v rovine vozovky (vychozi stav): obe roviny splyvaji,
                    // takze staci jeden prusecik a plati presne to, co delal puvodni kod. Neni to
                    // jen optimalizace pro pohodli - dvouroviny render je 2,2x pomalejsi (nameřeno
                    // 24. 8. 2026: 89 -> 40 snimku za 15 s), takze bez teto vetve by zdrazil
                    // i beh, ktery vyvysenou travu vubec nechce.
                    bool road = false;
                    if (y < tblH && x < tblW)
                    {
                        if (flatGrass)
                        {
                            var dir = Vector3.TransformNormal(new Vector3(table[y, x].X, table[y, x].Y, 1f), m);
                            road = Math.Abs(dir.Z) >= 1e-9f
                                   && HitsPlane(0.0, dir, eye, pose, cos, sin, out _, out bool onRoad)
                                   && onRoad;
                        }
                        else
                        {
                            road = Trace(table[y, x], m, eye, pose, cos, sin,
                                         GrassHeightAt(frameIndex, pixel), out _,
                                         double.PositiveInfinity) == Surface.Road;
                        }
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

            double v = value + DeterministicNoise.Gaussian(options.Seed, frameIndex, pixel, channel) * options.ColorNoise;
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
                   + DeterministicNoise.Gaussian(options.Seed, frameIndex, pixel, ChannelGrass)
                     * options.GrassRoughnessM;
        }

        /// <summary>
        /// Protne paprsek s vodorovnou rovinou v dane vysce a rekne, zda zasah lezi na vozovce.
        /// </summary>
        /// <returns>true, kdyz je prusecik pred kamerou.</returns>
        /// <summary>
        /// Vzdalenost, ve ktere paprsek prekroci okraj cesty - tedy zasah svisle steny mezi
        /// vozovkou a travou. Bisekce na <c>IsRoad</c> mezi parametrem, kde je paprsek jeste NAD
        /// cestou (<paramref name="sOnRoad"/>), a tim, kde uz je za jejim okrajem
        /// (<paramref name="sOffRoad"/>).
        /// </summary>
        /// <remarks>
        /// 24 pulení staci: pri rozsahu jednotek metru je vysledna presnost pod desetinu milimetru,
        /// tedy hluboko pod rozlisenim hloubky (1 mm). Cena je zanedbatelna - vetev se uplatni jen
        /// na tenke care pixelu podel hranice (radove tisicina obrazu).
        /// </remarks>
        private double FindRoadBoundary(double sOnRoad, double sOffRoad, in Vector3 dir, in Vector3 eye,
                                        RobotState pose, double cos, double sin)
        {
            for (int i = 0; i < 24; i++)
            {
                double mid = 0.5 * (sOnRoad + sOffRoad);
                double hx = eye.X + mid * dir.X;
                double hy = eye.Y + mid * dir.Y;

                if (scene.IsRoad(pose.X + hx * cos - hy * sin, pose.Y + hx * sin + hy * cos))
                    sOnRoad = mid;
                else
                    sOffRoad = mid;
            }
            return 0.5 * (sOnRoad + sOffRoad);
        }

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
