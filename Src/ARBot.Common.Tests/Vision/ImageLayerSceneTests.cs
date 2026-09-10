using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Vision;
using NUnit.Framework;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// <b>Scéna vrstvy</b> — rozliseni obrazu, ktery vrstva POKRYVA, proti jejimu vlastnimu
    /// rozliseni.
    ///
    /// <para><b>Nacpak.</b> Pravdepodobnost ze site je 128×128, ale pokryva cely barevny snimek
    /// 640×480. Bez teto informace zobrazovac vrstvu vykreslil jako <b>ctverec</b> vedle podkladu
    /// (prostrednich 75 % sirky obrazu) a hodnota <c>p</c> pod kurzorem se hlasila jen v levem
    /// hornim rohu 128×128 px, a jeste pro jiny bod scény. Nalez 10. 9. 2026, viz doc/devlog.md
    /// a Src/ARBot/Views/README.md.</para>
    /// </summary>
    public class ImageLayerSceneTests
    {
        private static ImageLayer Prob(int w, int h, int sceneW = 0, int sceneH = 0)
            => new ImageLayer
            {
                Name = "Cam/Probability",
                Kind = LayerKind.Probability,
                Gray = new Image<Gray>(w, h),
                SceneWidth = sceneW,
                SceneHeight = sceneH,
            };

        [Test]
        public void BezScény_JeScénaRozmerVrstvy()
        {
            // Samostatne prisly Blob "backproject" zdroj nezna - pak se nesmi nic domyslet.
            var l = Prob(128, 128);

            Assert.Multiple(() =>
            {
                Assert.That(l.EffectiveSceneWidth, Is.EqualTo(128));
                Assert.That(l.EffectiveSceneHeight, Is.EqualTo(128));
            });
        }

        [Test]
        public void ProbabilityZeSnimku_MaScénuBarevnehoObrazu()
        {
            // ⚠️ Tohle je ta vada: 128×128 pokryva 640×480, ne 128×128.
            var frame = new CameraFrame
            {
                Name = "Left",
                ImageRGB = new Image<BGR32>(640, 480),
                ImageProbability = new Image<Gray>(128, 128),
            };

            var layer = MessageImageLayers.Extract(frame)
                                          .Single(l => l.Kind == LayerKind.Probability);

            Assert.Multiple(() =>
            {
                Assert.That((layer.Width, layer.Height), Is.EqualTo((128, 128)),
                            "vlastni rozliseni vrstvy se nemeni - do gridu i do hranic cesty jde nativni");
                Assert.That((layer.EffectiveSceneWidth, layer.EffectiveSceneHeight), Is.EqualTo((640, 480)));
            });
        }

        [Test]
        public void BarvaIHloubka_MajiScénuRovnouSvemuRozmeru()
        {
            var frame = new CameraFrame
            {
                Name = "Left",
                ImageRGB = new Image<BGR32>(640, 480),
                ImageDepth = new Image<Gray16>(848, 480),
            };

            var vrstvy = MessageImageLayers.Extract(frame).ToList();
            var rgb = vrstvy.Single(l => l.Kind == LayerKind.Color);
            var depth = vrstvy.Single(l => l.Kind == LayerKind.Depth);

            Assert.Multiple(() =>
            {
                Assert.That((rgb.EffectiveSceneWidth, rgb.EffectiveSceneHeight), Is.EqualTo((640, 480)));
                Assert.That((depth.EffectiveSceneWidth, depth.EffectiveSceneHeight), Is.EqualTo((848, 480)));
            });
        }

        [Test]
        public void TryPixel_PrepocitavaKazdouOsuZVLAST()
        {
            var l = Prob(128, 128, 640, 480);

            Assert.Multiple(() =>
            {
                // Stred scény -> stred vrstvy v OBOU osach. Jedno spolecne meritko (640/128 = 5)
                // by svisle minulo o tretinu: 240/5 = 48 misto 64.
                Assert.That(l.TryPixel(320, 240, 640, 480, out int x, out int y), Is.True);
                Assert.That((x, y), Is.EqualTo((64, 64)));

                // Prava dolni ctvrtina scény musi skoncit v prave dolni ctvrtine vrstvy - drive
                // se sem hodnota nehlasila vubec (x >= 128 => null).
                Assert.That(l.TryPixel(639, 479, 640, 480, out x, out y), Is.True);
                Assert.That((x, y), Is.EqualTo((127, 127)));

                Assert.That(l.TryPixel(0, 0, 640, 480, out x, out y), Is.True);
                Assert.That((x, y), Is.EqualTo((0, 0)));
            });
        }

        [Test]
        public void TryPixel_MimoScénu_JeFalse()
        {
            var l = Prob(128, 128, 640, 480);

            Assert.Multiple(() =>
            {
                Assert.That(l.TryPixel(640, 100, 640, 480, out _, out _), Is.False);
                Assert.That(l.TryPixel(100, 480, 640, 480, out _, out _), Is.False);
                Assert.That(l.TryPixel(-1, 0, 640, 480, out _, out _), Is.False);
                Assert.That(l.TryPixel(0, -1, 640, 480, out _, out _), Is.False);
            });
        }

        [Test]
        public void TryPixel_ZadnaScéna_JeIdentita()
        {
            // Podkladova vrstva: scéna == vlastni rozmer, takze se nesmi nic prepocitavat.
            var l = new ImageLayer { Kind = LayerKind.Color, Color = new Image<BGR32>(640, 480) };

            Assert.That(l.TryPixel(123, 456, 0, 0, out int x, out int y), Is.True);
            Assert.That((x, y), Is.EqualTo((123, 456)));
        }

        [Test]
        public void TryPixel_NikdyNevratiPixelMimoVrstvu()
        {
            // Zaokrouhlovani: pro KAZDY bod scény musi vyjit platny index, jinak by cteni
            // hodnoty pod kurzorem vyhodilo vyjimku (a ta se v ImageDocument jen spolkne).
            var l = Prob(128, 128, 640, 480);

            for (int sy = 0; sy < 480; sy++)
                for (int sx = 0; sx < 640; sx++)
                {
                    Assert.That(l.TryPixel(sx, sy, 640, 480, out int x, out int y), Is.True);
                    Assert.That(x, Is.InRange(0, 127));
                    Assert.That(y, Is.InRange(0, 127));
                }
        }
    }
}
