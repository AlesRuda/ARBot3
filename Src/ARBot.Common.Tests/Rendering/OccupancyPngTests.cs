using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Rendering;

namespace ARBot.Common.Tests.Rendering
{
    /// <summary>
    /// Kodovani occupancy gridu do PNG (presunuto z WorldViewDocument 4. 9. 2026, aby na nej
    /// videl i webovy nahled headless a ARBot.Analyze). Viz doc/plan-headless-web.md.
    /// </summary>
    public class OccupancyPngTests
    {
        /// <summary>Grid 4x4, kde bunka (1,2) je neprujezdna a (0,0) potvrzene volna.</summary>
        private static OccupancyGridMsg Grid()
        {
            var og = new OccupancyGridMsg
            {
                Size = 4, Resolution = 0.05, OriginX = 0, OriginY = 0,
                Scale = 1f, BlockedThreshold = 0.5f, FreeThreshold = -0.5f,
                Occ = new sbyte[16], Road = new sbyte[16],
            };
            // State() cte oba kanaly: nad BlockedThreshold = neprujezdno, pod FreeThreshold = volno.
            og.Occ[1 + 2 * 4] = 100; og.Road[1 + 2 * 4] = 100;
            og.Occ[0] = -100; og.Road[0] = -100;
            return og;
        }

        [Test]
        public void PrazdnyNeboNulovyGrid_VratiNull()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OccupancyPng.Encode(null), Is.Null);
                Assert.That(OccupancyPng.Encode(new OccupancyGridMsg { Size = 0 }), Is.Null);
                Assert.That(OccupancyPng.Encode(new OccupancyGridMsg { Size = 4, Occ = null }), Is.Null);
            });
        }

        [Test]
        public void Grid_ZakodujeSeJakoPng_SpravnychRozmeru()
        {
            byte[] png = OccupancyPng.Encode(Grid());

            Assert.That(png, Is.Not.Null);
            // Magicke bajty PNG: 89 50 4E 47
            Assert.That(png[0], Is.EqualTo(0x89));
            Assert.That(png[1], Is.EqualTo((byte)'P'));
            Assert.That(png[2], Is.EqualTo((byte)'N'));
            Assert.That(png[3], Is.EqualTo((byte)'G'));

            using var bmp = SkiaSharp.SKBitmap.Decode(png);
            Assert.Multiple(() =>
            {
                Assert.That(bmp.Width, Is.EqualTo(4));
                Assert.That(bmp.Height, Is.EqualTo(4));
            });
        }

        [Test]
        public void SeverJeNahore_ANeprujezdnaBunkaJeCervena()
        {
            // Radek 0 obrazu je SEVERNI hrana = nejvyssi j. Bunka (i=1, j=2) je tedy
            // na radku (Size-1-j) = 1.
            using var bmp = SkiaSharp.SKBitmap.Decode(OccupancyPng.Encode(Grid()));

            var blocked = bmp.GetPixel(1, 4 - 1 - 2);
            var free = bmp.GetPixel(0, 4 - 1 - 0);
            var unknown = bmp.GetPixel(3, 3);

            Assert.Multiple(() =>
            {
                Assert.That(blocked.Red, Is.GreaterThan(blocked.Green), "neprujezdna je cervena");
                Assert.That(blocked.Alpha, Is.GreaterThan(0));
                Assert.That(free.Green, Is.GreaterThan(free.Red), "potvrzene volna je zelena");
                Assert.That(unknown.Alpha, Is.EqualTo(0), "nezname je pruhledne");
            });
        }
    }

    /// <summary>Verejne kodovani obrazu (obalka nad privatnim EncodeSkia) - viz ImageMsg.EncodeJpeg.</summary>
    public class ImageEncodeTests
    {
        [Test]
        public void Bgr32_SeZakodujeDoJpeg()
        {
            var img = new ARBot.Common.Common.Image<ARBot.Common.Common.BGR32>(8, 4);
            byte[] jpeg = ImageMsg.EncodeJpeg(img);

            Assert.That(jpeg, Is.Not.Null);
            Assert.That(jpeg.Length, Is.GreaterThan(4));
            // Magicke bajty JPEG: FF D8
            Assert.That(jpeg[0], Is.EqualTo(0xFF));
            Assert.That(jpeg[1], Is.EqualTo(0xD8));

            using var back = SkiaSharp.SKBitmap.Decode(jpeg);
            Assert.Multiple(() =>
            {
                Assert.That(back.Width, Is.EqualTo(8));
                Assert.That(back.Height, Is.EqualTo(4));
            });
        }

        [Test]
        public void Gray_SeZakodujeDoJpeg()
        {
            // Pravdepodobnost cesty z RGB (CameraFrame.ImageProbability) je Gray, step 1.
            var img = new ARBot.Common.Common.Image<ARBot.Common.Common.Gray>(8, 4);
            byte[] jpeg = ImageMsg.EncodeJpeg(img);

            Assert.That(jpeg, Is.Not.Null);
            Assert.That(jpeg[0], Is.EqualTo(0xFF));
            Assert.That(jpeg[1], Is.EqualTo(0xD8));
        }

        [Test]
        public void NullObraz_Vyhodi()
            => Assert.Throws<System.ArgumentNullException>(() => ImageMsg.EncodeJpeg(null));
    }
}
