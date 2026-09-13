using System.Globalization;
using System.Linq;
using ARBot.Common.Calibration;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

using Vector3 = System.Numerics.Vector3;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// <b>Kalibrace zapsana do registru 23 musi v CIDLE delat totez, co nas
    /// <see cref="MagCalResult.Apply"/> dela nad daty z driveru.</b>
    ///
    /// <para>Jsou v tom dve nezavisle veci a kazda se 11. 9. 2026 jednou spletla:</para>
    /// <list type="number">
    /// <item><b>Vzorec.</b> VN aplikuje <c>C·(m − b)</c> — <b>zmereno</b> na senzoru a zaroven
    ///   <b>dolozeno ICD</b> (VN100 ICD v3.1.0.0: <c>CalibratedMag = C · (MeasuredMag − B)</c>).</item>
    /// <item><b>Ramec.</b> Registr 23 se aplikuje <b>pred</b> reference frame rotation
    ///   (registr 26), kdezto nas fit bezi nad <c>MagnetometerRaw</c>, tedy <b>po</b> ni i po
    ///   prevodu FRD→FLU v driveru. Mezi nimi lezi <c>diag(−1, −1, +1)</c>.</item>
    /// </list>
    ///
    /// <para>⚠️ <b>Bez te transformace ma kalibrace obracene znamenko biasu v X a Y</b>, tedy
    /// hard-iron offset <b>pricita misto odecitani</b> — u prvni skutecne kalibrace o
    /// <b>0,22 G vodorovne</b>, coz je vic nez vodorovna slozka zemskeho pole (~0,20 G).
    /// Kompas by prestal reagovat na otaceni.</para>
    ///
    /// <para><b>Jak se ramec zmeril</b> (cisty bias <c>+0,25</c> v jedne ose registru 23, posun
    /// vystupu v registru 20, tri opakovani prolozena identitou): X <b>+0,251</b>,
    /// Y <b>−0,250</b>, Z <b>+0,249</b>, krizove cleny pod 0,002 G. Odvozeni z kodu
    /// (registr 26 × <c>FrdToFlu</c>) dalo tutez diagonalu.</para>
    ///
    /// <para>⚠️ <b>Merit se to musi pres VSECHNY OSY a s NEJEDNOTKOVOU matici.</b> Z osy X samotne
    /// vyjde „VN bias pricita" (opak pravdy) a pri <c>C = I</c> nejde odlisit <c>C·m − b</c> od
    /// <c>C·(m − b)</c>. Obe pasti stály 11. 9. 2026 zbytecny zapis do senzoru.</para>
    /// </summary>
    public class MagCalVnBiasTests
    {
        /// <summary>Reference frame rotation (registr 26) naseho robota.</summary>
        private static readonly double[] Reg26 = { -1, 1, -1 };

        /// <summary>Prevod FRD→FLU v driveru (<c>VN100IMUBinary.FrdToFlu</c>).</summary>
        private static readonly double[] FrdToFlu = { 1, -1, -1 };

        private static Matrix<double> Mat(double xx, double yy, double zz,
                                          double xy, double xz, double yz)
            => Matrix<double>.Build.DenseOfArray(new[,] {
                { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } });

        private static Vector<double> Vec(double x, double y, double z)
            => Vector<double>.Build.DenseOfArray(new[] { x, y, z });

        /// <summary>
        /// <b>Cela cesta, jak ji projde skutecne mereni</b>: pole ve FLU → do ramce cidla →
        /// kompenzace registrem 23 (<c>C·(m − b)</c>) → registr 26 → <c>FrdToFlu</c> → zpet FLU.
        /// </summary>
        private static Vector3 PresSenzor(string vnwrg23, Vector3 flu)
        {
            var p = vnwrg23.Split(',').Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
            Assert.That(p, Has.Length.EqualTo(12), "registr 23 ma dvanact cisel");

            // FLU → ramec cidla je inverze cesty ven, tedy tataz diagonala (involuce).
            double[] t = { FrdToFlu[0] * Reg26[0], FrdToFlu[1] * Reg26[1], FrdToFlu[2] * Reg26[2] };
            double sx = t[0] * flu.X, sy = t[1] * flu.Y, sz = t[2] * flu.Z;

            // Kompenzace v ramci cidla: C·(m − b).
            double x = sx - p[9], y = sy - p[10], z = sz - p[11];
            double cx = p[0] * x + p[1] * y + p[2] * z;
            double cy = p[3] * x + p[4] * y + p[5] * z;
            double cz = p[6] * x + p[7] * y + p[8] * z;

            // Zpet do FLU (registr 26 a pak FrdToFlu = tataz diagonala).
            return new Vector3((float)(t[0] * cx), (float)(t[1] * cy), (float)(t[2] * cz));
        }

        private static MagCalResult Vysledek()
            // Cisla z prvni skutecne kalibrace (20260910-170809.rec, --bref=0.4897).
            => new MagCalResult(
                Mat(1.121575, 1.103300, 1.022071, 0.007452, 0.007101, 0.021040),
                Vec(0.110929, -0.014435, 0.049725),
                condition: 134.2, sdMagnitudeG: 0.00198, sdInclinationDeg: 2.083, samples: 19275);

        [Test]
        public void SenzorSKalibraci_DelaTOTEZ_CoApply()
        {
            var r = Vysledek();
            string reg = r.ToVnwrg23();

            // Nekolik smeru pole, at se projevi i mimodiagonalni cleny a obe zaporne osy.
            foreach (var m in new[]
            {
                new Vector3(0.16f, -0.14f, 0.41f),
                new Vector3(-0.30f, 0.22f, 0.35f),
                new Vector3(0.45f, 0.05f, -0.18f),
                new Vector3(0f, 0f, 0.48f),
                new Vector3(0.20f, 0.20f, 0.20f),
            })
            {
                var nas = r.Apply(m);
                var senzor = PresSenzor(reg, m);

                Assert.That(senzor.X, Is.EqualTo(nas.X).Within(1e-5), $"X pro {m}");
                Assert.That(senzor.Y, Is.EqualTo(nas.Y).Within(1e-5), $"Y pro {m}");
                Assert.That(senzor.Z, Is.EqualTo(nas.Z).Within(1e-5), $"Z pro {m}");
            }
        }

        [Test]
        public void BiasXaY_JdouDoRegistru_S_OBRACENYM_Znamenkem()
        {
            // Tohle je ta vada, kvuli ktere test vznikl: hard-iron offset +0,111 G ve FLU X
            // musi do registru jit jako −0,111, jinak ho cidlo PRICTE misto odecteni.
            var r = Vysledek();
            var p = r.ToVnwrg23().Split(',')
                     .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(p[9], Is.EqualTo(-r.B[0]).Within(1e-6), "X se preklapi");
                Assert.That(p[10], Is.EqualTo(-r.B[1]).Within(1e-6), "Y se preklapi");
                Assert.That(p[11], Is.EqualTo(r.B[2]).Within(1e-6), "Z zustava");
                Assert.That(p[9], Is.LessThan(0),
                            "stred elipsoidy je v +X, v registru proto musi byt zaporne cislo");
            });
        }

        [Test]
        public void MimodiagonalniCleny_SeZ_MeniZnamenko()
        {
            // C_s = T·C·T pri T = diag(−1,−1,1): meni se prave cleny, kde je prave jeden index Z.
            var r = Vysledek();
            var p = r.ToVnwrg23().Split(',')
                     .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

            Assert.Multiple(() =>
            {
                Assert.That(p[0], Is.EqualTo(r.C[0, 0]).Within(1e-6), "diagonala se nemeni");
                Assert.That(p[4], Is.EqualTo(r.C[1, 1]).Within(1e-6));
                Assert.That(p[8], Is.EqualTo(r.C[2, 2]).Within(1e-6));
                Assert.That(p[1], Is.EqualTo(r.C[0, 1]).Within(1e-6), "XY se nemeni (obe osy preklopene)");
                Assert.That(p[2], Is.EqualTo(-r.C[0, 2]).Within(1e-6), "XZ se preklapi");
                Assert.That(p[5], Is.EqualTo(-r.C[1, 2]).Within(1e-6), "YZ se preklapi");
                Assert.That(p[6], Is.EqualTo(-r.C[2, 0]).Within(1e-6));
                Assert.That(p[7], Is.EqualTo(-r.C[2, 1]).Within(1e-6));
            });
        }

        [Test]
        public void PoradiCisel_JePO_RADCICH()
        {
            // Poradi je C00,C01,C02,C10,C11,C12,C20,C21,C22 - u symetricke matice to nerozlisi,
            // tak se bere NEsymetricka. Znamenka drzi test vyse, tady jde o poradi.
            var r = new MagCalResult(
                Matrix<double>.Build.DenseOfArray(new[,] {
                    { 1.0, 0.2, 0.3 }, { 0.4, 1.1, 0.5 }, { 0.6, 0.7, 1.2 } }),
                Vec(0, 0, 0), 10, 0.001, 0.1, 100);

            var p = r.ToVnwrg23().Split(',')
                     .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();

            // Diagonala a XY beze zmeny, cleny se Z preklopene.
            Assert.That(p.Take(9), Is.EqualTo(new[] { 1.0, 0.2, -0.3, 0.4, 1.1, -0.5, -0.6, -0.7, 1.2 })
                                     .Within(1e-9));
        }

        [Test]
        public void DesetinnaTecka_NeCarka()
        {
            // V ceskem prostredi by carka rozbila prikaz oddeleny carkami.
            var r = Vysledek();
            string s = r.ToVnwrg23();

            Assert.That(s.Split(',').Length, Is.EqualTo(12));
            Assert.That(s, Does.Contain("."));
        }
    }
}
