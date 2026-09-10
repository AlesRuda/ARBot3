using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Robot.Web;
using NUnit.Framework;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// <b>Vrstva „cesta z RGB" ve webovem nahledu</b> (<c>?layer=prob</c>) se posila v rozmeru
    /// BAREVNEHO snimku, ne v nativnim rozliseni site.
    ///
    /// <para><b>Nacpak.</b> Sit pocita ve 128×128, ale pokryva cely snimek 640×480 (squash
    /// 4:3 → 1:1 je replika treninku, viz doc/semantic-segmentation.md). Stranka ma
    /// <c>img{width:100%}</c>, takze vysku bere z pomeru souboru — nativni ctverec by scénu
    /// svisle natahl o tretinu a nesedel by na to, co je videt v RGB vrstve. Nalez 10. 9. 2026,
    /// viz doc/devlog.md.</para>
    /// </summary>
    [NonParallelizable]
    public class WebStatusProbLayerTests
    {
        private const int RgbW = 64, RgbH = 48, ProbW = 16, ProbH = 16;

        private static WebStatus SeSnimkem()
        {
            var s = new WebStatus();
            // Bez zajmu se snimek ani nezkopiruje (lizny render) - stejne to dela server pri
            // kazdem /camera.jpg.
            s.NoteCameraInterest();
            s.Post(new CameraFrame
            {
                Name = "Left",
                ImageRGB = new Image<BGR32>(RgbW, RgbH),
                ImageProbability = new Image<Gray>(ProbW, ProbH),
            });
            return s;
        }

        /// <summary>Rozmer z hlavicky JPEG (marker SOF0..SOF3); <c>(0,0)</c> = nenalezeno.</summary>
        private static (int w, int h) JpegRozmer(byte[] jpeg)
        {
            for (int i = 2; i + 9 < jpeg.Length; )
            {
                if (jpeg[i] != 0xFF) { i++; continue; }
                byte m = jpeg[i + 1];
                if (m >= 0xC0 && m <= 0xC3)
                    return (jpeg[i + 7] << 8 | jpeg[i + 8], jpeg[i + 5] << 8 | jpeg[i + 6]);
                int len = jpeg[i + 2] << 8 | jpeg[i + 3];
                i += 2 + (len > 0 ? len : 2);
            }
            return (0, 0);
        }

        [Test]
        public void VrstvaProb_JeVRozmeruBarevnehoSnimku()
        {
            var jpeg = SeSnimkem().RenderCameraJpeg("Left", "prob");

            Assert.That(jpeg, Is.Not.Null);
            Assert.That(JpegRozmer(jpeg), Is.EqualTo((RgbW, RgbH)),
                        "128x128 pokryva cely snimek, takze se do nej ma natahnout");
        }

        [Test]
        public void VrstvaRGB_ZustavaVeSvemRozmeru()
        {
            var jpeg = SeSnimkem().RenderCameraJpeg("Left", null);

            Assert.That(jpeg, Is.Not.Null);
            Assert.That(JpegRozmer(jpeg), Is.EqualTo((RgbW, RgbH)));
        }

        [Test]
        public void BezBarevnehoSnimku_SePravdepodobnostPosleJakJe()
        {
            // Nema se z ceho odvodit scéna - dohadovat pomer stran by bylo horsi nez ho nechat.
            var s = new WebStatus();
            s.NoteCameraInterest();
            s.Post(new CameraFrame { Name = "Left", ImageProbability = new Image<Gray>(ProbW, ProbH) });

            var jpeg = s.RenderCameraJpeg("Left", "prob");

            Assert.That(jpeg, Is.Not.Null);
            Assert.That(JpegRozmer(jpeg), Is.EqualTo((ProbW, ProbH)));
        }
    }
}
