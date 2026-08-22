using System;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Prepocet pixelu <b>barevneho</b> obrazu na metricky bod v ramci <b>robotu</b> pomoci
    /// hloubkoveho obrazu. Managed nahrada nativniho <c>ColorPixel23D</c>.
    ///
    /// <para><b>Proc managed</b> (21. 8. 2026): nativni <c>ColorPixel23D</c> <b>v NativeLib dnes
    /// vubec neni</b>, takze <c>D435CameraProjection.TransformBack(points, depth)</c> je mrtva cesta
    /// na vsech platformach (na ARM to navic vyhazuje <c>NotSupportedException</c> explicitne).
    /// Hranova lokalizace na tomhle prepoctu stoji — viz doc/map-correlation-localization.md.</para>
    ///
    /// <para><b>Postup je shodny s originalem</b> (<c>ColorPixel23D</c> →
    /// <c>rs2_project_color_pixel_to_depth_pixel</c>):
    /// <list type="number">
    /// <item>barevny pixel se prepocita na <b>hloubkovy</b> — hledanim podel epipolary mezi
    /// <c>depth_min</c> a <c>depth_max</c> s pomoci extrinsik color↔depth,</item>
    /// <item>z tabulky smeru (<see cref="IDepthCameraProjection.Camera2DToCamera3D"/>) se vezme
    /// paprsek a bod je <c>(ray.x·d, ray.y·d, d)</c> v prostoru kamery,</item>
    /// <item>nakonec montazni transformace kamery
    /// (<see cref="IDepthCameraProjection.Transformation"/>) do ramce robotu — v originalu to byl
    /// samostatny <c>TransformPoint4DImpl</c> volany z C#.</item>
    /// </list></para>
    ///
    /// <para><b>Extrinsiky jsou vstup, ne domnenka.</b> Kdyz jsou <b>identita</b> (virtualni kamera,
    /// zarovnane streamy), hledani degeneruje na prepocet intrinsik a je <b>exaktni</b>. U realne
    /// D435 jsou senzory ~15 mm od sebe a extrinsiky je potreba dodat, jinak vznika chyba radu
    /// 1,5 cm pri 3 m. Dnes se do <c>ARBot.Common</c> nedostanou (zijou jako
    /// <c>Intel.RealSense.Extrinsics</c> v <c>HALWindows</c>), takze je musi protahnout HAL —
    /// <b>na realnem HW proto neovereno</b>.</para>
    ///
    /// <para><b>Zkresleni objektivu se zanedbava</b> na barevne strane (dopredna projekce je
    /// dirkova); hloubkova strana jde pres tabulku smeru, ktera zkresleni resi. U virtualni kamery
    /// zadne zkresleni neni.</para>
    ///
    /// <para>Instance se drzi <b>per kamera</b> (geometrie je stala) a je bezstavova vuci snimku,
    /// takze ji smi pouzivat vlakno kamery bez zamku.</para>
    /// </summary>
    public sealed class ColorPixelTo3D
    {
        private readonly Intrinsics color;
        private readonly Intrinsics depth;
        private readonly IDepthCameraProjection projection;
        private readonly Matrix4x4 colorToDepth;
        private readonly Matrix4x4 depthToColor;
        private readonly bool aligned;          // extrinsiky jsou identita -> staci prepocet intrinsik
        private readonly float depthScale;
        private readonly float minRangeM;
        private readonly float maxRangeM;

        /// <param name="colorIntrinsics">Intrinsika barevneho streamu (v jeho pixelech).</param>
        /// <param name="depthIntrinsics">Intrinsika hloubkoveho streamu.</param>
        /// <param name="depthProjection">Hloubkova projekce - tabulka smeru + montazni transformace.</param>
        /// <param name="colorToDepth">Extrinsika barevna → hloubkova kamera; null/identita = zarovnane streamy.</param>
        /// <param name="depthToColor">Extrinsika hloubkova → barevna kamera; null/identita = zarovnane streamy.</param>
        /// <param name="depthScale">Prevod hodnoty hloubky na metry (RealSense: 0,001 = mm).</param>
        /// <param name="minRangeM">Dolni mez dosahu (original: 0,6 m).</param>
        /// <param name="maxRangeM">Horni mez dosahu (original: 8 m).</param>
        public ColorPixelTo3D(Intrinsics colorIntrinsics, Intrinsics depthIntrinsics,
                                  IDepthCameraProjection depthProjection,
                                  Matrix4x4? colorToDepth = null, Matrix4x4? depthToColor = null,
                                  float depthScale = 0.001f, float minRangeM = 0.6f, float maxRangeM = 8f)
        {
            color = colorIntrinsics ?? throw new ArgumentNullException(nameof(colorIntrinsics));
            depth = depthIntrinsics ?? throw new ArgumentNullException(nameof(depthIntrinsics));
            projection = depthProjection ?? throw new ArgumentNullException(nameof(depthProjection));
            this.colorToDepth = colorToDepth ?? Matrix4x4.Identity;
            this.depthToColor = depthToColor ?? Matrix4x4.Identity;
            aligned = this.colorToDepth.IsIdentity && this.depthToColor.IsIdentity;
            this.depthScale = depthScale;
            this.minRangeM = minRangeM;
            this.maxRangeM = maxRangeM;
        }

        /// <summary>
        /// Bod v ramci robotu [m] pro pixel barevneho obrazu. <c>A == 0</c> = neplatny (chybejici
        /// nebo nasycena hloubka, mimo hloubkovy obraz, mimo dosah senzoru).
        /// </summary>
        public Point4D ToRobot(int colorX, int colorY, Image<Gray16> depthImage)
        {
            if (depthImage == null) return default;

            if (!FindDepthPixel(colorX, colorY, depthImage, out int dx, out int dy))
                return default;

            int raw = depthImage[dx, dy].Value;
            if (raw == 0 || raw == ushort.MaxValue) return default;      // stejne jako original

            float d = raw * depthScale;
            if (d < minRangeM || d > maxRangeM) return default;

            var inCamera = Ray(dx, dy, d);
            var inRobot = Vector3.Transform(inCamera, projection.Transformation);
            return new Point4D { X = inRobot.X, Y = inRobot.Y, Z = inRobot.Z, A = 1 };
        }

        /// <summary>
        /// Barevny pixel → hloubkovy pixel. Pri zarovnanych streamech prepocet intrinsik (exaktni),
        /// jinak hledani podel epipolary jako <c>rs2_project_color_pixel_to_depth_pixel</c>: konce
        /// usecky se ziskaji promitnutim paprsku v <c>min</c>/<c>max</c> dosahu do hloubkoveho
        /// obrazu a podel te usecky se hleda pixel, ktery se zpetne promitne <b>nejblize</b>
        /// vstupnimu barevnemu pixelu.
        /// </summary>
        private bool FindDepthPixel(int colorX, int colorY, Image<Gray16> depthImage, out int dx, out int dy)
        {
            if (aligned)
            {
                double xn = (colorX - color.PPx) / color.Fx;
                double yn = (colorY - color.PPy) / color.Fy;
                dx = (int)Math.Round(depth.PPx + xn * depth.Fx);
                dy = (int)Math.Round(depth.PPy + yn * depth.Fy);
                return dx >= 0 && dy >= 0 && dx < depthImage.Width && dy < depthImage.Height;
            }

            dx = dy = 0;

            // Konce usecky: paprsek barevneho pixelu v minimalnim a maximalnim dosahu, prevedeny
            // do hloubkove kamery a promitnuty do jejiho obrazu.
            var pMin = Vector3.Transform(Deproject(color, colorX, colorY, minRangeM), colorToDepth);
            var pMax = Vector3.Transform(Deproject(color, colorX, colorY, maxRangeM), colorToDepth);
            if (!Project(depth, pMin, out float sx, out float sy)) return false;
            if (!Project(depth, pMax, out float ex, out float ey)) return false;

            Clamp(ref sx, ref sy, depthImage.Width, depthImage.Height);
            Clamp(ref ex, ref ey, depthImage.Width, depthImage.Height);

            // Krok po pixelu podel usecky (pri realne zakladne je dlouha jen jednotky pixelu).
            int steps = (int)Math.Max(Math.Abs(ex - sx), Math.Abs(ey - sy)) + 1;
            double best = double.MaxValue;
            for (int i = 0; i < steps; i++)
            {
                double t = steps == 1 ? 0 : (double)i / (steps - 1);
                int cx = (int)Math.Round(sx + (ex - sx) * t);
                int cy = (int)Math.Round(sy + (ey - sy) * t);
                if (cx < 0 || cy < 0 || cx >= depthImage.Width || cy >= depthImage.Height) continue;

                int raw = depthImage[cx, cy].Value;
                if (raw == 0 || raw == ushort.MaxValue) continue;
                float d = raw * depthScale;

                // Zpetna projekce do barevneho obrazu: vidi tenhle hloubkovy pixel tentyz bod?
                var back = Vector3.Transform(Ray(cx, cy, d), depthToColor);
                if (!Project(color, back, out float bx, out float by)) continue;

                double dist = (bx - colorX) * (bx - colorX) + (by - colorY) * (by - colorY);
                if (dist < best) { best = dist; dx = cx; dy = cy; }
            }
            return best < double.MaxValue;
        }

        /// <summary>Bod v prostoru kamery z tabulky smeru: <c>(ray.x·d, ray.y·d, d)</c>.</summary>
        private Vector3 Ray(int dx, int dy, float d)
        {
            var table = projection.Camera2DToCamera3D;
            if (table == null || dy >= table.GetLength(0) || dx >= table.GetLength(1))
                return Deproject(depth, dx, dy, d);
            var ray = table[dy, dx];
            return new Vector3((float)(ray.X * d), (float)(ray.Y * d), d);
        }

        /// <summary>Pixel + hloubka → bod v prostoru te kamery (dirkovy model).</summary>
        private static Vector3 Deproject(Intrinsics i, float u, float v, float d)
            => new Vector3((u - i.PPx) / i.Fx * d, (v - i.PPy) / i.Fy * d, d);

        /// <summary>Bod v prostoru kamery → pixel (dirkovy model). <c>false</c> = za kamerou.</summary>
        private static bool Project(Intrinsics i, Vector3 p, out float u, out float v)
        {
            u = v = 0;
            if (p.Z <= 1e-6f) return false;
            u = p.X / p.Z * i.Fx + i.PPx;
            v = p.Y / p.Z * i.Fy + i.PPy;
            return true;
        }

        private static void Clamp(ref float x, ref float y, int w, int h)
        {
            x = Math.Max(0, Math.Min(x, w - 1));
            y = Math.Max(0, Math.Min(y, h - 1));
        }
    }
}
