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

        // --- Vychozi model z registru ----------------------------------------------------

        /// <summary>
        /// VYCHOZI <c>nnmodel=</c> musi existovat a jit nacist. Bez tohohle testu by se
        /// prepsani defaultu na soubor, ktery nekdo zapomene pridat do repa nebo do nasazeni,
        /// poznalo teprve za behu na robotu - a vypadalo by to jako porucha kamery.
        ///
        /// <para>Preskoci se JEN kdyz v <c>models/</c> nejsou zadne modely (tedy nebezime nad
        /// pracovni kopii repa). Kdyz tam modely jsou a chybi zrovna ten vychozi, je to CHYBA,
        /// ne duvod k preskoceni.</para>
        /// </summary>
        [Test]
        public void VychoziModelZRegistruExistujeAJdeNacist()
        {
            string dir = Path.Combine(RepoPaths.RootOrBase(), "models");
            if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*.onnx").Length == 0)
                Assert.Ignore("V models/ nejsou zadne .onnx modely (viz models/README.md).");

            string p = RepoPaths.Resolve(ParamRegistry.NnModel.Def.Default);
            Assert.That(File.Exists(p), Is.True,
                        $"Vychozi nnmodel='{ParamRegistry.NnModel.Def.Default}' neexistuje ({p}). "
                        + "Bud se soubor zapomnel pridat do repa, nebo je spatne default v ParamRegistry.");

            using var bp = new OnnxBackProject(p);
            Assert.That(bp.OutputChannels, Is.GreaterThanOrEqualTo(2), "Model ma mit aspon dva kanaly vystupu.");
            Assert.That(bp.InputWidth, Is.GreaterThan(0));
        }

        /// <summary>
        /// Vychozi model je OPTIMALIZOVANY graf (<c>models/onnxopt.py</c>) a ta optimalizace ma
        /// byt EXAKTNI - odstranuje jen vypocet, ktery na vysledku nic nemeni (dve 1x1 konvoluce
        /// za sebou bez nelinearity mezi nimi, a 1x1 konvoluce za nearest-Resize, ktera s nim
        /// komutuje). Test to hlida proti ZDROJOVEMU modelu, aby se pripadna regrese v tom
        /// skriptu projevila jako selhany test, ne jako *tise horsi segmentace*.
        ///
        /// <para>Meri se <b>shoda rozhodnuti</b>, ne shoda cisel: preskladana aritmetika ma jiny
        /// zaokrouhlovaci sum, takze hodnoty se lisit MUSI (namereno 1,2e-6 pred kvantizaci na
        /// bajt). Rozhodnuti se lisit nesmi - vyjimka je jen pixel, ktery lezi na prahu, kde
        /// o preklopeni rozhoduje uz samo zaokrouhleni na bajt.</para>
        /// </summary>
        [Test]
        public void OptimalizovanyModelRozhodujeStejneJakoZdrojovy()
        {
            string opt = RepoPaths.Resolve(ParamRegistry.NnModel.Def.Default);
            // "_opt" je konvence onnxopt.py; zdrojovy model se jmenuje stejne bez ni.
            string src = opt.Replace("_opt.onnx", ".onnx", StringComparison.Ordinal);
            if (src == opt) Assert.Ignore($"Vychozi model '{Path.GetFileName(opt)}' neni varianta '_opt'.");
            if (!File.Exists(opt) || !File.Exists(src))
                Assert.Ignore("Vychozi nebo zdrojovy model neni k dispozici (viz models/README.md).");

            using var a = new OnnxBackProject(src);
            using var b = new OnnxBackProject(opt);
            Assert.That(b.InputWidth, Is.EqualTo(a.InputWidth), "Optimalizace nesmi zmenit rozmer vstupu.");
            Assert.That(b.OutputWidth, Is.EqualTo(a.OutputWidth), "Optimalizace nesmi zmenit rozmer vystupu.");

            // Deterministicky vstup s plochami i hranami - na cistem sumu model nikde nerozhoduje
            // jasne, takze by test nemeril to, co se v provozu deje.
            var img = new Image<BGR32>(a.InputWidth, a.InputHeight);
            var rnd = new Random(20260909);
            for (int y = 0; y < a.InputHeight; y++)
                for (int x = 0; x < a.InputWidth; x++)
                {
                    int i = (y * a.InputWidth + x) * 4;
                    bool blok = ((x / 16) + (y / 16)) % 2 == 0;
                    int zaklad = blok ? 40 + y / 2 : 150 - x / 3;
                    img.Data[i + 0] = (byte)Math.Clamp(zaklad + rnd.Next(-8, 9), 0, 255);
                    img.Data[i + 1] = (byte)Math.Clamp(zaklad + 20 + rnd.Next(-8, 9), 0, 255);
                    img.Data[i + 2] = (byte)Math.Clamp(zaklad + 10 + rnd.Next(-8, 9), 0, 255);
                    img.Data[i + 3] = 255;
                }

            var da = new Image<Gray>(a.OutputWidth, a.OutputHeight);
            var db = new Image<Gray>(b.OutputWidth, b.OutputHeight);
            a.Process(img, da);
            b.Process(img, db);

            int jineRozhodnuti = 0, naPrahu = 0, maxRozdil = 0;
            for (int i = 0; i < da.DataLength; i++)
            {
                int va = da.Data[i], vb = db.Data[i];
                maxRozdil = Math.Max(maxRozdil, Math.Abs(va - vb));
                if ((va >= 128) == (vb >= 128)) continue;
                jineRozhodnuti++;
                if (Math.Abs(va - 128) <= 1 && Math.Abs(vb - 128) <= 1) naPrahu++;
            }

            Assert.That(maxRozdil, Is.LessThanOrEqualTo(2),
                        $"Optimalizace zmenila pravdepodobnost o {maxRozdil}/255 - to uz neni zaokrouhlovaci sum.");
            Assert.That(jineRozhodnuti - naPrahu, Is.Zero,
                        $"Optimalizovany model rozhodl jinak na {jineRozhodnuti - naPrahu} pixelech "
                        + $"(z {da.DataLength}), mimo prah. Optimalizace ma byt exaktni.");
        }
    }
}
