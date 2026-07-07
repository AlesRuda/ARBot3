using ARBot.Common;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Hloubkova projekce D435 pro Armbian/ARM64.
    /// POZOR: 3D zpetna projekce (TransformBack) vyuziva nativni funkci ColorPixel23D z NativeLib,
    /// ktera zatim NENI implementovana v ARM verzi (libNativeLib.so) - viz asm_linux_arm64.S.
    /// Grab RGB+Depth (D435Camera) funguje bez teto projekce; TransformBack proto na ARM vyhazuje
    /// NotSupportedException, dokud nebude ColorPixel23D doplneno do nativni knihovny.
    /// </summary>
    public class D435CameraProjection : CameraProjection
    {
        public D435CameraProjection(
            Intrinsics intrinsics,
            Intrinsics inverseIntrinsics,
            Intel.RealSense.Intrinsics colorIntrin,
            Intel.RealSense.Intrinsics depthIntrin,
            Intel.RealSense.Extrinsics color2Depth,
            Intel.RealSense.Extrinsics depth2Color) : base(intrinsics, inverseIntrinsics, System.Numerics.Matrix4x4.Identity, System.Numerics.Matrix4x4.Identity)
        {
        }

        /// <summary>
        /// Transformace bodu barevne roviny do svetovych souradnic pomoci hloubky.
        /// Na ARM zatim nepodporovano (chybi nativni ColorPixel23D v libNativeLib.so).
        /// </summary>
        public override List<Point4D> TransformBack(List<Point> points, Image<Gray16> depth)
        {
            throw new NotSupportedException(
                "D435 hloubkova projekce (ColorPixel23D) neni na ARM implementovana v libNativeLib.so.");
        }
    }
}
