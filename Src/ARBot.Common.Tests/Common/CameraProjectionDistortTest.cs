using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using NUnit.Framework;
using System.Numerics;

namespace ARBot.Common.Tests.Common
{
    /// <summary>
    /// Testy tabulky zkresleni v <see cref="CameraProjection"/> (<c>toDistortCache</c>).
    ///
    /// <para>Kryje dve chyby z prekryvu pretizeni <c>ToDistort(int,int)</c> / <c>ToDistort(float,float)</c>:
    /// (a) konstruktor plnil cache pres int pretizeni, ktere cetlo prave plnenou (prazdnou) cache,
    /// takze v ni zustaly same nuly a <see cref="CameraProjection.UnDistort{T}(Image{T})"/> vracel
    /// konstantni obraz; (b) vetev "mimo rozsah" volala sama sebe (nekonecna rekurze).</para>
    /// </summary>
    [TestFixture]
    public class CameraProjectionDistortTest
    {
        private const int W = 16;
        private const int H = 12;

        /// <summary>Intrinsics bez zkresleni - <c>ToDistort</c> je pak identita, takze
        /// UnDistort ma vratit presnou kopii vstupu.</summary>
        private static Intrinsics MakeIntrinsics() => new Intrinsics
        {
            Width = W,
            Height = H,
            PPx = W / 2f,
            PPy = H / 2f,
            Fx = 10f,
            Fy = 10f,
            Model = Intrinsics.Distortion.None,
            Coeffs = new float[5],
        };

        private static CameraProjection MakeProjection()
            => new CameraProjection(MakeIntrinsics(), MakeIntrinsics(),
                                    Matrix4x4.Identity, Matrix4x4.Identity);

        /// <summary>Obraz s jedinecnou hodnotou v kazdem pixelu (aby slo poznat prohozeni i konstantu).</summary>
        private static Image<Gray> Ramp(int w, int h)
        {
            var img = new Image<Gray>(w, h);
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                    img[x, y].Value = (byte)(1 + (x + y * w) % 255);
            return img;
        }

        [Test]
        public void UnDistort_BezZkresleni_VraciKopiiVstupu()
        {
            var proj = MakeProjection();
            var src = Ramp(W, H);

            var dst = proj.UnDistort(src);

            // Pred opravou byla toDistortCache same nuly -> vsechny pixely = src[0,0].
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    Assert.That(dst[x, y].Value, Is.EqualTo(src[x, y].Value),
                                $"pixel [{x},{y}] neodpovida vstupu");
        }

        [Test]
        public void UnDistort_ObrazVetsiNezIntrinsics_Nezacykli()
        {
            // Obraz vetsi nez intrinsics -> UnDistort sahne na souradnice MIMO rozsah cache,
            // tedy do vetve, ktera se pred opravou volala rekurzivne (StackOverflow).
            var proj = MakeProjection();
            var src = Ramp(W + 4, H + 4);

            var dst = proj.UnDistort(src);

            Assert.That(dst.Width, Is.EqualTo(W + 4));
            Assert.That(dst.Height, Is.EqualTo(H + 4));
            // Bez zkresleni je mapovani identita i mimo rozsah cache.
            for (int x = 0; x < W + 4; x++)
                for (int y = 0; y < H + 4; y++)
                    Assert.That(dst[x, y].Value, Is.EqualTo(src[x, y].Value),
                                $"pixel [{x},{y}] neodpovida vstupu");
        }
    }
}
