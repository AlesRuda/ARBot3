using ARBot.Common;
using ARBot.Common.Algorithms.ComputeUnit;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace HALWindows
{
    public class D435CameraProjection: CameraProjection
    {
#if IsX64
        [DllImport("NativeLib.dll", EntryPoint = "ColorPixel23D", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        private static extern void ColorPixel23D(
            Point[] colorXYs,
            [In, Out]Point4D[] cameradPoints,
            int colorXYsCount,
            byte[] depthData, // v c se to povazuje za pole short, ale tady jde jen o predani pointeru
            float depth_scale,
            float depth_min, float depth_max,
            ref Intel.RealSense.Intrinsics color_intrin,
            ref Intel.RealSense.Intrinsics depth_intrin,
		    ref Intel.RealSense.Extrinsics color_to_depth,
            ref Intel.RealSense.Extrinsics depth_to_color,
		    Point2DF[,] transform,
            float[] rotate);
#endif

        Intel.RealSense.Intrinsics colorIntrin;
        Intel.RealSense.Intrinsics depthIntrin;
        Intel.RealSense.Extrinsics color2Depth;
        Intel.RealSense.Extrinsics depth2Color;
        public D435CameraProjection(
            Intrinsics intrinsics, 
            Intrinsics inverseIntrinsics,
            Intel.RealSense.Intrinsics colorIntrin,
            Intel.RealSense.Intrinsics depthIntrin,
            Intel.RealSense.Extrinsics color2Depth,
            Intel.RealSense.Extrinsics depth2Color) : base(intrinsics, inverseIntrinsics, System.Numerics.Matrix4x4.Identity, System.Numerics.Matrix4x4.Identity)
        {
            this.colorIntrin = colorIntrin;
            this.depthIntrin = depthIntrin;
            this.color2Depth = color2Depth;
            this.depth2Color = depth2Color;
        }

        /// <summary>
        /// Transformuje souradnice v rovine color kamery (pocatek vlevo nahore) do svetovych souradnic robotu (pocatek v miste robotu).
        /// Roli hraje nastavena orientace kamery pomoci SetOrientation.
        /// </summary>
        /// <param name="points">Body v rovine kamery. Roste smerem doprava a dolu v pixlech.</param>
        /// <param name="depth">Hloubkova mapa korespondujici k bodum points</param>
        /// <returns>Pole tranformovanych bodu do svetovych souradnic. Pokud je A slozka bodu rovna 0 (vlastne cely bod bude identicky 0) je tento bod nevalidni.</returns>
        public override List<Point4D> TransformBack(List<Point> points, Image<Gray16> depth)
        {
            var l = new Point4D[points.Count];

            var t = NativeComputeUnit.Transformation(Transformation);

            ColorPixel23D(points.ToArray(), l, points.Count, depth.Data,
                0.001f, 0.6f, 8, ref colorIntrin, ref depthIntrin, ref color2Depth, ref depth2Color,
                camera2DToCamera3DCache, t);

            var r = new Point4D[points.Count];

            NativeComputeUnit.TransformPoint4DImpl(r, t, l, points.Count);

            return r.ToList();
        }

    }
}
