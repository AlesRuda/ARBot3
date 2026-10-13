using System;
using System.Collections.Generic;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Vision;
using NUnit.Framework;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// Montaz kamery z roviny zeme (<see cref="GroundPlaneMount"/>): na synteticke hloubce roviny se
    /// znamou montazi se musi montaz vratit, a vliv hlavniho bodu musi odpovidat odvozenemu vzorci
    /// (vid-leva-kamera-hlavni-bod, bod 7 pruzkumu).
    /// </summary>
    public class GroundPlaneMountTests
    {
        private const int W = 480, H = 270;
        private const double F = 241.5518;
        private const double PPx = 237.4966, PPy = 135.4676;      // hloubka leve D435 ze zaznamu
        private static double Deg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>Hloubka (z, mm) roviny z = 0 v tele pro kameru s montazi <paramref name="mount"/> a PP.</summary>
        private static byte[] RenderGround(Matrix4x4 mount, double ppx, double ppy, Func<int, int, double?> jinak = null)
        {
            var data = new byte[W * H * 2];
            var o = new Vector3(mount.M41, mount.M42, mount.M43);
            for (int v = 0; v < H; v++)
                for (int u = 0; u < W; u++)
                {
                    double? d = jinak?.Invoke(u, v);
                    if (d == null)
                    {
                        var ray = new Vector3((float)((u - ppx) / F), (float)((v - ppy) / F), 1f);
                        var dir = Vector3.TransformNormal(ray, mount);
                        if (dir.Z >= -1e-6f) continue;
                        d = -o.Z / dir.Z;                    // z-hloubka (ray ma z = 1)
                    }
                    if (d > 10) continue;
                    int mm = (int)Math.Round(d.Value * 1000);
                    int i = (v * W + u) * 2;
                    data[i] = (byte)(mm & 0xFF);
                    data[i + 1] = (byte)(mm >> 8);
                }
            return data;
        }

        private static GroundPlaneMount.Fit Proloz(byte[] depth, Matrix4x4 gateMount, double ppGateX, double ppGateY,
                                                   double ppX, double ppY)
        {
            var px = new List<GroundPlaneMount.GroundPixel>();
            GroundPlaneMount.SelectGroundPixels(depth, W, H, F, F, ppGateX, ppGateY, gateMount, 3, 0.5, 2.5, 0.15, px);
            var pts = new List<Vector3>();
            GroundPlaneMount.Deproject(px, F, F, ppX, ppY, pts);
            Assert.That(GroundPlaneMount.TryFit(pts, GroundPlaneMount.UpInCamera(gateMount), 0.02, 200, 1,
                                                10 * Math.PI / 180, 300, out var fit), Is.True, "prolozeni selhalo");
            return fit;
        }

        [TestCase(true)]
        [TestCase(false)]
        public void Decompose_VratiParametryProfile(bool leva)
        {
            var m = leva ? Profile.LeftCameraTransform : Profile.RightCameraTransform;
            var (yaw, pitch, roll, h) = GroundPlaneMount.Decompose(m);

            Assert.That(Deg(yaw), Is.EqualTo(leva ? 29.0 : -29.0).Within(1e-4));
            Assert.That(Deg(pitch), Is.EqualTo(leva ? -20.2 : -18.6).Within(1e-4));
            Assert.That(Deg(roll), Is.EqualTo(leva ? 0.9 : -1.1).Within(1e-4));
            Assert.That(h, Is.EqualTo(leva ? 0.522 : 0.525).Within(1e-6));
        }

        [Test]
        public void SyntetickaZem_VratiMontaz()
        {
            var mount = Profile.LeftCameraTransform;
            var depth = RenderGround(mount, PPx, PPy);

            var fit = Proloz(depth, mount, PPx, PPy, PPx, PPy);

            Assert.That(Deg(fit.Pitch), Is.EqualTo(-20.2).Within(0.03));
            Assert.That(Deg(fit.Roll), Is.EqualTo(0.9).Within(0.03));
            Assert.That(fit.HeightM, Is.EqualTo(0.522).Within(0.002));
            Assert.That(fit.RmsM, Is.LessThan(0.002), "jen kvantovani na mm");
        }

        /// <summary>
        /// Vysledek nezavisi na montazi pouzite k predvyberu - ta jen vybira body. Jinak by se merila
        /// predpokladana montaz, ne skutecna.
        /// </summary>
        [Test]
        public void PredvyberJinouMontazi_MeriSkutecnou()
        {
            var skutecna = Conversions.CameraToWorldTransform(Conversions.Deg2Rad(29), Conversions.Deg2Rad(-21.5),
                                                              Conversions.Deg2Rad(-0.4), new Vector3(0.0155f, 0.1f, 0.505f));
            var depth = RenderGround(skutecna, PPx, PPy);

            var fit = Proloz(depth, Profile.LeftCameraTransform, PPx, PPy, PPx, PPy);

            Assert.That(Deg(fit.Pitch), Is.EqualTo(-21.5).Within(0.03));
            Assert.That(Deg(fit.Roll), Is.EqualTo(-0.4).Within(0.03));
            Assert.That(fit.HeightM, Is.EqualTo(0.505).Within(0.002));
        }

        /// <summary>
        /// Leva D435: obraz je otoceny o 180°, takze spravny hlavni bod je (W−1−PPx, H−1−PPy), runtime ale
        /// pocita se surovym (dvoji prevraceni se zrusi). Zem vyrenderovana se SPRAVNYM PP a spoctena se
        /// SUROVYM musi dat sklon posunuty podle tan(p_rt) = tan(p) + δy + δx·tan(r)/cos(p) (odvozeni
        /// v doc/traversability-grid.md), se spravnym musi vyjit presne.
        /// </summary>
        [Test]
        public void HlavniBod_PosunSklonuOdpovidaVzorci()
        {
            var mount = Profile.LeftCameraTransform;
            double ppSpravnyX = W - 1 - PPx, ppSpravnyY = H - 1 - PPy;
            var depth = RenderGround(mount, ppSpravnyX, ppSpravnyY);

            var spravne = Proloz(depth, mount, ppSpravnyX, ppSpravnyY, ppSpravnyX, ppSpravnyY);
            var runtime = Proloz(depth, mount, ppSpravnyX, ppSpravnyY, PPx, PPy);

            double p = Conversions.Deg2Rad(-20.2), r = Conversions.Deg2Rad(0.9);
            double dx = (ppSpravnyX - PPx) / F, dy = (ppSpravnyY - PPy) / F;
            double ocekavany = Math.Atan(Math.Tan(p) + dy + dx * Math.Tan(r) / Math.Cos(p));

            Assert.That(Deg(spravne.Pitch), Is.EqualTo(-20.2).Within(0.03));
            Assert.That(Deg(runtime.Pitch), Is.EqualTo(Deg(ocekavany)).Within(0.03));
            Assert.That(Deg(runtime.Pitch) - Deg(spravne.Pitch), Is.EqualTo(-0.389).Within(0.03),
                        "svisla chyba PP leve kamery (+1,9 px) posune sklon o ~−0,39°");
            Assert.That(Deg(runtime.Roll), Is.EqualTo(0.9).Within(0.03), "naklon se meni az ve druhem radu");
        }

        /// <summary>Prekazka v pasmu predvyberu (kvadr 0,1 m nad zemi) nesmi sklon ani vysku pohnout.</summary>
        [Test]
        public void Prekazka_RansacJiOdfiltruje()
        {
            var mount = Profile.LeftCameraTransform;
            var o = new Vector3(mount.M41, mount.M42, mount.M43);
            var depth = RenderGround(mount, PPx, PPy, (u, v) =>
            {
                if (u < 200 || u > 320 || v < 120 || v > 200) return null;
                var dir = Vector3.TransformNormal(new Vector3((float)((u - PPx) / F), (float)((v - PPy) / F), 1f), mount);
                if (dir.Z >= 0) return null;
                return -(o.Z - 0.10) / dir.Z;              // rovina 0,10 m nad zemi (krabice)
            });

            var fit = Proloz(depth, mount, PPx, PPy, PPx, PPy);

            Assert.That(Deg(fit.Pitch), Is.EqualTo(-20.2).Within(0.05));
            Assert.That(fit.HeightM, Is.EqualTo(0.522).Within(0.003));
            Assert.That(fit.Inliers, Is.LessThan(fit.Points), "krabice nejsou inliery");
        }

        [Test]
        public void SvislaStena_NeniZem()
        {
            // Body jen na svisle stene 1,5 m pred kamerou: RANSAC ji najde, ale normala neni „nahoru".
            var pts = new List<Vector3>();
            var mount = Profile.LeftCameraTransform;
            var inv = Matrix4x4.Invert(mount, out var mi) ? mi : throw new InvalidOperationException();
            for (double y = -1; y <= 1; y += 0.05)
                for (double z = 0.05; z <= 0.4; z += 0.02)
                {
                    var hx = Math.Cos(Conversions.Deg2Rad(29)) * 1.5; var hy = Math.Sin(Conversions.Deg2Rad(29)) * 1.5;
                    pts.Add(Vector3.Transform(new Vector3((float)hx, (float)(hy + y), (float)z), inv));
                }

            bool ok = GroundPlaneMount.TryFit(pts, GroundPlaneMount.UpInCamera(mount), 0.02, 200, 1,
                                              10 * Math.PI / 180, 50, out _);

            Assert.That(ok, Is.False);
        }
    }
}
