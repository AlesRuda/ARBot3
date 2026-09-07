using System;
using System.IO;
using ARBot.Common.Common;
using ARBot.Common.Configuration;
using ARBot.Common.Vision.Nn;
using NUnit.Framework;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// Testy <see cref="OnnxBackProject"/> (semanticka segmentace sjizdnosti neuronovou siti).
    ///
    /// <para>Predzpracovani a postprocessing jsou vydelene do statickych metod prave proto, aby
    /// se daly testovat BEZ modelu - model je velky binarni soubor v <c>models/</c>, ktery na
    /// stroji byt nemusi. Testy, ktere ho potrebuji, se v jeho nepritomnosti preskoci
    /// (<see cref="Assert.Ignore(string)"/>), ne selzou.</para>
    /// </summary>
    public class OnnxBackProjectTest
    {
        /// <summary>Vychozi model; kdyz chybi, integracni testy se preskoci.</summary>
        private static string ModelPath()
        {
            var p = Path.Combine(RepoPaths.RootOrBase(), "models", "Model61.1_int8.onnx");
            return File.Exists(p) ? p : null;
        }

        private static OnnxBackProject Open()
        {
            var p = ModelPath();
            if (p == null)
                Assert.Ignore("Model models/Model61.1_int8.onnx neni k dispozici (viz models/README.md).");
            return new OnnxBackProject(p);
        }

        // --- Predzpracovani (bez modelu) -------------------------------------------------

        [Test]
        public void FillInput_PrehodiKanalyDoRgbAZnormuje()
        {
            // Jeden pixel BGR32: B=10, G=20, R=30 (poradi bajtu v pameti je B,G,R,X).
            var src = new byte[] { 10, 20, 30, 255 };
            var dst = new float[3];

            // rOffset=2, bOffset=0 => poradi RGB (jako ARBot2 EdgeTPUDll).
            OnnxBackProject.FillInput(src, dst, rOffset: 2, bOffset: 0);

            Assert.That(dst[0], Is.EqualTo(30f / 255f).Within(1e-6));
            Assert.That(dst[1], Is.EqualTo(20f / 255f).Within(1e-6));
            Assert.That(dst[2], Is.EqualTo(10f / 255f).Within(1e-6));
        }

        [Test]
        public void FillInput_BgrPoradiNechaBajtyJakJsou()
        {
            var src = new byte[] { 10, 20, 30, 255 };
            var dst = new float[3];

            OnnxBackProject.FillInput(src, dst, rOffset: 0, bOffset: 2);

            Assert.That(dst[0], Is.EqualTo(10f / 255f).Within(1e-6));
            Assert.That(dst[2], Is.EqualTo(30f / 255f).Within(1e-6));
        }

        [Test]
        public void FillInput_MalyZdrojJeChyba()
        {
            var dst = new float[6];   // dva pixely => potreba 8 bajtu
            Assert.That(() => OnnxBackProject.FillInput(new byte[4], dst, 2, 0), Throws.ArgumentException);
        }

        // --- Postprocessing (bez modelu) -------------------------------------------------

        [Test]
        public void FillProbability_JedenKanalSkalujeNa0Az255()
        {
            var outp = new float[] { 0f, 0.5f, 1f };
            var dst = new byte[3];

            OnnxBackProject.FillProbability(outp, dst, channels: 1, traversableChannel: 0);

            Assert.That(dst[0], Is.EqualTo(0));
            Assert.That(dst[1], Is.EqualTo(128));
            Assert.That(dst[2], Is.EqualTo(255));
        }

        [Test]
        public void FillProbability_PrahJeStejneRozhodnutiJakoArgmax()
        {
            // Klicova vlastnost: prah 128 nad vysledkem musi dat TOTEZ co puvodni ARBot2
            // rozhodnuti out[0] < out[1]. Proto se normalizuje souctem - soucet sigmoid
            // neni presne 1. Tady p0=0,40 p1=0,45 => sjizdno, ale surovych 0,45 by dalo 115.
            var outp = new float[] { 0.40f, 0.45f };
            var dst = new byte[1];

            OnnxBackProject.FillProbability(outp, dst, channels: 2, traversableChannel: 1);

            Assert.That(dst[0], Is.GreaterThan(128), "normalizace souctem chybi - prah by nesedel s argmaxem");
            Assert.That(dst[0], Is.EqualTo((byte)Math.Round(255 * 0.45 / 0.85)).Within(1));
        }

        [Test]
        public void FillProbability_NesjizdnoJePodPrahem()
        {
            var outp = new float[] { 0.62f, 0.31f };
            var dst = new byte[1];

            OnnxBackProject.FillProbability(outp, dst, channels: 2, traversableChannel: 1);

            Assert.That(dst[0], Is.LessThan(128));
        }

        [Test]
        public void FillProbability_NuloveKanalyNedeliNulou()
        {
            var outp = new float[] { 0f, 0f };
            var dst = new byte[1];

            Assert.That(() => OnnxBackProject.FillProbability(outp, dst, 2, 1), Throws.Nothing);
            Assert.That(dst[0], Is.EqualTo(0));
        }

        // --- Se skutecnym modelem --------------------------------------------------------

        [Test]
        public void Model_MaOcekavanyTvarASizeVraciVystup()
        {
            using var bp = Open();

            Assert.That(bp.InputWidth, Is.GreaterThan(0));
            Assert.That(bp.OutputChannels, Is.GreaterThanOrEqualTo(1));
            // Size() musi vracet rozmer VYSTUPU - podle nej si volajici alokuje probability obraz.
            Assert.That(bp.Size(640, 480), Is.EqualTo(new System.Drawing.Size(bp.OutputWidth, bp.OutputHeight)));
        }

        [Test]
        public void Process_ZmensiSiVstupSamAVyplniCelyVystup()
        {
            using var bp = Open();

            // Snimek v rozmeru, ktery zada Size() - presne tak volá CameraFrameProcessor.
            var size = bp.Size(640, 480);
            var src = new Image<BGR32>(size.Width, size.Height);
            var dst = new Image<Gray>(size.Width, size.Height);
            var rnd = new Random(1);
            rnd.NextBytes(src.Data);

            Assert.That(() => bp.Process(src, dst), Throws.Nothing);

            // Vystup nesmi zustat prazdny (model neco spocital).
            bool nenulovy = false;
            foreach (var b in dst.Data) if (b != 0) { nenulovy = true; break; }
            Assert.That(nenulovy, Is.True, "model vratil same nuly");
        }

        [Test]
        public void Process_ZvladneIJinyRozmerZdroje()
        {
            using var bp = Open();

            var size = bp.Size(0, 0);
            var src = new Image<BGR32>(640, 480);      // plny snimek kamery, ne zmenseny
            var dst = new Image<Gray>(size.Width, size.Height);

            Assert.That(() => bp.Process(src, dst), Throws.Nothing);
        }

        [Test]
        public void Process_JeDeterministicky()
        {
            using var bp = Open();

            var size = bp.Size(0, 0);
            var src = new Image<BGR32>(size.Width, size.Height);
            var a = new Image<Gray>(size.Width, size.Height);
            var b = new Image<Gray>(size.Width, size.Height);
            new Random(7).NextBytes(src.Data);

            bp.Process(src, a);
            bp.Process(src, b);

            Assert.That(b.Data, Is.EqualTo(a.Data));
        }

        [Test]
        public void Process_NealokujeVUstalenemStavu()
        {
            using var bp = Open();

            var size = bp.Size(0, 0);
            var src = new Image<BGR32>(size.Width, size.Height);
            var dst = new Image<Gray>(size.Width, size.Height);
            new Random(3).NextBytes(src.Data);

            bp.Process(src, dst);          // rozehrati (JIT, prvni alokace uvnitr ORT)
            bp.Process(src, dst);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5; i++) bp.Process(src, dst);
            long perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / 5;

            // Process bezi na vlakne kamery pri kazdem snimku - alokace by tam delaly GC tlak.
            // Par set bajtu na pole nazvu/hodnot pri Run je v poradku, kilobajty uz ne.
            Assert.That(perFrame, Is.LessThan(1024), $"Process alokuje {perFrame} B na snimek");
        }

        [Test]
        public void Process_SpatnyRozmerCileJeChyba()
        {
            using var bp = Open();

            var size = bp.Size(0, 0);
            var src = new Image<BGR32>(size.Width, size.Height);
            var dst = new Image<Gray>(size.Width + 1, size.Height);

            Assert.That(() => bp.Process(src, dst), Throws.ArgumentException);
        }

        [Test]
        public void ChybejiciModelJeSrozumitelnaChyba()
        {
            Assert.That(() => new OnnxBackProject(Path.Combine(Path.GetTempPath(), "neexistuje-arbot.onnx")),
                        Throws.TypeOf<FileNotFoundException>());
        }
    }
}
