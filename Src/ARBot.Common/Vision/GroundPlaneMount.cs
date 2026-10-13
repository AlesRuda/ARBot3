using System;
using System.Collections.Generic;
using System.Numerics;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// <b>Montaz hloubkove kamery z roviny zeme</b>: vyska, sklon a naklon kamery vuci zemi, na ktere
    /// robot stoji, z jednoho hloubkoveho snimku. Jadro prikazu <c>ARBot.Analyze montaz</c>
    /// (vid-kalibrace-kamer-bias, bod 7 pruzkumu, 13. 10. 2026).
    ///
    /// <para><b>Konvence</b> je tataz jako <see cref="Common.Conversions.CameraToWorldTransform"/> a montaz
    /// v <c>Profile</c>: ramec kamery x vpravo, y dolu, z osa (u leve D435 ramec ULOZENEHO, uz otoceneho
    /// obrazu), telo FLU. Telesove „nahoru" v ramci kamery je treti sloupec rotace
    /// <c>(M13, M23, M33) = (−sin r, −cos p·cos r, sin p·cos r)</c>, takze z normaly roviny zeme jde primo
    /// <c>pitch = atan2(n_z, −n_y)</c> (zaporny = dolu), <c>roll = −asin(n_x)</c> a vyska = vzdalenost
    /// stredu kamery od roviny. <b>Yaw z roviny zeme urcit nejde</b> (otoceni kolem svisle osy normalu
    /// nemeni), stejne jako posun X/Y.</para>
    ///
    /// <para><b>Postup:</b> (1) pixely, ktere podle zadane montaze a hlavniho bodu lezi v pasmu dosahu
    /// a blizko zeme (<see cref="SelectGroundPixels"/>) - jen predvyber, tolerance je volna; (2) jejich
    /// body v ramci kamery s LIBOVOLNYM hlavnim bodem (<see cref="Deproject"/>) - tytez pixely, takze
    /// rozdil variant je cisty vliv hlavniho bodu; (3) robustni prolozeni roviny v ramci kamery
    /// (<see cref="TryFit"/>: RANSAC s pevnym semenem a pak totalni nejmensi ctverce na inlierech).
    /// Vysledek nezavisi na montazi pouzite k predvyberu (ta jen vybira body), takze meri skutecnou
    /// montaz vuci zemi pred robotem.</para>
    /// </summary>
    public static class GroundPlaneMount
    {
        /// <summary>Pixel kandidata na zem: souradnice ve snimku a hloubka (z) [m].</summary>
        public readonly struct GroundPixel
        {
            public readonly ushort U, V;
            public readonly float DepthM;
            public GroundPixel(int u, int v, float depthM) { U = (ushort)u; V = (ushort)v; DepthM = depthM; }
        }

        /// <summary>Vysledek prolozeni roviny zeme.</summary>
        public readonly struct Fit
        {
            /// <summary>Jednotkova normala roviny orientovana „nahoru" (k robotu), v ramci kamery.</summary>
            public readonly Vector3 Up;
            /// <summary>Vzdalenost stredu kamery od roviny [m] - vyska kamery nad zemi.</summary>
            public readonly double HeightM;
            /// <summary>Pocet bodu na vstupu a inlieru konecneho prolozeni.</summary>
            public readonly int Points, Inliers;
            /// <summary>RMS kolmych odchylek inlieru od roviny [m].</summary>
            public readonly double RmsM;

            public Fit(Vector3 up, double heightM, int points, int inliers, double rmsM)
            { Up = up; HeightM = heightM; Points = points; Inliers = inliers; RmsM = rmsM; }

            /// <summary>Sklon kamery [rad] v konvenci <c>Profile</c> (zaporny = osa miri dolu).</summary>
            public double Pitch => Math.Atan2(Up.Z, -Up.Y);

            /// <summary>Naklon kamery [rad] v konvenci <c>Profile</c>.</summary>
            public double Roll => -Math.Asin(Math.Clamp(Up.X, -1f, 1f));
        }

        /// <summary>
        /// Rozklad montazni matice (<see cref="Common.Conversions.CameraToWorldTransform"/>) na yaw, pitch,
        /// roll [rad] a vysku (Z posunu) [m] - presna inverze pro tutez konvenci.
        /// </summary>
        public static (double Yaw, double Pitch, double Roll, double HeightM) Decompose(Matrix4x4 m)
            => (Math.Atan2(m.M11, -m.M12), Math.Atan2(m.M33, -m.M23), -Math.Asin(Math.Clamp(m.M13, -1f, 1f)), m.M43);

        /// <summary>Telesove „nahoru" v ramci kamery podle montazni matice (treti sloupec rotace).</summary>
        public static Vector3 UpInCamera(Matrix4x4 m) => Vector3.Normalize(new Vector3(m.M13, m.M23, m.M33));

        /// <summary>
        /// Predvyber kandidatu na zem z hloubkoveho snimku <c>Gray16</c> (little-endian, mm, 0 = neplatne).
        /// Bod se spocte s hlavnim bodem <paramref name="ppx"/>, <paramref name="ppy"/>, prevede montazi
        /// <paramref name="mount"/> do tela a bere se, kdyz je jeho vodorovna vzdalenost v
        /// [<paramref name="rMinM"/>, <paramref name="rMaxM"/>] a |z| ≤ <paramref name="maxAbsZM"/>.
        /// Bere se kazdy <paramref name="step"/>-ty pixel v obou smerech.
        /// </summary>
        public static void SelectGroundPixels(byte[] depth16, int width, int height, double fx, double fy,
                                              double ppx, double ppy, Matrix4x4 mount, int step,
                                              double rMinM, double rMaxM, double maxAbsZM, List<GroundPixel> output)
        {
            output.Clear();
            if (depth16 == null || depth16.Length < width * height * 2) return;
            if (step < 1) step = 1;
            double r2Min = rMinM * rMinM, r2Max = rMaxM * rMaxM;
            for (int v = 0; v < height; v += step)
            {
                double ry = (v - ppy) / fy;
                for (int u = 0; u < width; u += step)
                {
                    int o = (v * width + u) * 2;
                    int raw = depth16[o] | (depth16[o + 1] << 8);
                    if (raw <= 0 || raw >= 65535) continue;
                    double d = raw * 0.001;
                    double rx = (u - ppx) / fx;
                    var pb = Vector3.Transform(new Vector3((float)(rx * d), (float)(ry * d), (float)d), mount);
                    double r2 = (double)pb.X * pb.X + (double)pb.Y * pb.Y;
                    if (r2 < r2Min || r2 > r2Max || Math.Abs(pb.Z) > maxAbsZM) continue;
                    output.Add(new GroundPixel(u, v, (float)d));
                }
            }
        }

        /// <summary>Body pixelu v ramci kamery s danym hlavnim bodem (pinhole bez zkresleni, jako D435).</summary>
        public static void Deproject(IReadOnlyList<GroundPixel> pixels, double fx, double fy, double ppx, double ppy,
                                     List<Vector3> output)
        {
            output.Clear();
            for (int i = 0; i < pixels.Count; i++)
            {
                var p = pixels[i];
                double d = p.DepthM;
                output.Add(new Vector3((float)((p.U - ppx) / fx * d), (float)((p.V - ppy) / fy * d), (float)d));
            }
        }

        /// <summary>
        /// Robustni prolozeni roviny body v ramci kamery: RANSAC (<paramref name="iterations"/> trojic,
        /// pevne <paramref name="seed"/> - vysledek je reprodukovatelny) s prahem kolme odchylky
        /// <paramref name="thresholdM"/>, pak dvakrat totalni nejmensi ctverce (nejmensi vlastni vektor
        /// kovariance) na inlierech. Normala se orientuje tak, aby stred kamery byl nad rovinou.
        /// <c>false</c>, kdyz je bodu malo, inlieru malo, nebo rovina neni zem (normala se od
        /// <paramref name="expectedUp"/> odchyluje vic nez <paramref name="maxTiltRad"/>).
        /// </summary>
        public static bool TryFit(IReadOnlyList<Vector3> pts, Vector3 expectedUp, double thresholdM, int iterations,
                                  int seed, double maxTiltRad, int minInliers, out Fit fit)
        {
            fit = default;
            int n = pts.Count;
            if (n < Math.Max(3, minInliers)) return false;

            // RANSAC na podvyberu (pocitani inlieru na vsech bodech by bylo zbytecne drahe).
            var rnd = new Random(seed);
            int vzorek = Math.Min(n, 2000);
            int[] idx = new int[vzorek];
            for (int i = 0; i < vzorek; i++) idx[i] = vzorek == n ? i : rnd.Next(n);

            Vector3 bestN = default; double bestD = 0; int bestCount = -1;
            for (int it = 0; it < iterations; it++)
            {
                var a = pts[rnd.Next(n)]; var b = pts[rnd.Next(n)]; var c = pts[rnd.Next(n)];
                var nn = Vector3.Cross(b - a, c - a);
                float len = nn.Length();
                if (len < 1e-6f) continue;
                nn /= len;
                double dd = -Vector3.Dot(nn, a);
                int count = 0;
                for (int i = 0; i < vzorek; i++)
                    if (Math.Abs(Vector3.Dot(nn, pts[idx[i]]) + dd) <= thresholdM) count++;
                if (count > bestCount) { bestCount = count; bestN = nn; bestD = dd; }
            }
            if (bestCount < 3) return false;

            // Totalni nejmensi ctverce na inlierech, dvakrat (inliery podle zpresnene roviny).
            Vector3 normal = bestN; double offset = bestD;
            int inl = 0; double rms = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                double sx = 0, sy = 0, sz = 0; int m = 0;
                for (int i = 0; i < n; i++)
                {
                    var p = pts[i];
                    if (Math.Abs(Vector3.Dot(normal, p) + offset) > thresholdM) continue;
                    sx += p.X; sy += p.Y; sz += p.Z; m++;
                }
                if (m < Math.Max(3, minInliers)) return false;
                double cx = sx / m, cy = sy / m, cz = sz / m;
                double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
                for (int i = 0; i < n; i++)
                {
                    var p = pts[i];
                    if (Math.Abs(Vector3.Dot(normal, p) + offset) > thresholdM) continue;
                    double dx = p.X - cx, dy = p.Y - cy, dz = p.Z - cz;
                    xx += dx * dx; xy += dx * dy; xz += dx * dz; yy += dy * dy; yz += dy * dz; zz += dz * dz;
                }
                var cov = Matrix<double>.Build.DenseOfArray(new[,] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } });
                var evd = cov.Evd(MathNet.Numerics.LinearAlgebra.Symmetricity.Symmetric);
                // Nejmensi vlastni cislo -> normala (vlastni cisla symetricke matice jsou vzestupne).
                int k = 0;
                for (int j = 1; j < 3; j++) if (evd.EigenValues[j].Real < evd.EigenValues[k].Real) k = j;
                var ev = evd.EigenVectors.Column(k);
                normal = Vector3.Normalize(new Vector3((float)ev[0], (float)ev[1], (float)ev[2]));
                offset = -(normal.X * cx + normal.Y * cy + normal.Z * cz);
                inl = m;
            }

            // Orientace: stred kamery (0,0,0) nad rovinou -> offset > 0, normala „nahoru".
            if (offset < 0) { normal = -normal; offset = -offset; }

            // RMS konecnych inlieru.
            int cnt = 0; double s2 = 0;
            for (int i = 0; i < n; i++)
            {
                double r = Vector3.Dot(normal, pts[i]) + offset;
                if (Math.Abs(r) > thresholdM) continue;
                s2 += r * r; cnt++;
            }
            rms = cnt > 0 ? Math.Sqrt(s2 / cnt) : double.NaN;
            inl = cnt;
            if (inl < Math.Max(3, minInliers)) return false;

            double cosTilt = Vector3.Dot(normal, Vector3.Normalize(expectedUp));
            if (cosTilt < Math.Cos(maxTiltRad)) return false;

            fit = new Fit(normal, offset, n, inl, rms);
            return true;
        }
    }
}
