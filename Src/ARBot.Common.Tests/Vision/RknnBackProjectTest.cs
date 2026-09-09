using System;
using System.IO;
using ARBot.Common.Vision.Nn;
using NUnit.Framework;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// Testy NPU cesty (<see cref="RknnBackProject"/>). Vlastni inferenci **nejde otestovat jinde
    /// nez na Orange Pi** — potrebuje NPU a <c>librknnrt.so</c>. Testovatelne je proto to, co na
    /// hardwaru nezavisi: predzpracovani (jiny kontrakt nez u ONNX cesty!) a vyber implementace
    /// podle pripony. Mereni na zarizeni je v doc/semantic-segmentation.md.
    /// </summary>
    public class RknnBackProjectTest
    {
        [Test]
        public void FillInput_PosilaSYROVEBajty_NeDeleneNa0az1()
        {
            // Klicovy rozdil proti OnnxBackProject.FillInput: NPU si normalizaci dela samo
            // (mean/std zadane pri prevodu modelu), takze se sem posilaji pixely 0..255.
            // Kdyby se sem omylem poslalo 0..1, model by videl skoro cernou a vysledek by byl
            // tise spatny - ne chyba.
            var src = new byte[] { 10, 20, 30, 255 };   // BGR32: B=10, G=20, R=30
            var dst = new byte[3];

            RknnBackProject.FillInput(src, dst, rOffset: 2, bOffset: 0);

            Assert.That(dst[0], Is.EqualTo(30), "prvni kanal ma byt R, nezmeneny");
            Assert.That(dst[1], Is.EqualTo(20));
            Assert.That(dst[2], Is.EqualTo(10));
        }

        [Test]
        public void FillInput_BgrPoradiNechaBajtyJakJsou()
        {
            var src = new byte[] { 10, 20, 30, 255 };
            var dst = new byte[3];

            RknnBackProject.FillInput(src, dst, rOffset: 0, bOffset: 2);

            Assert.That(dst[0], Is.EqualTo(10));
            Assert.That(dst[2], Is.EqualTo(30));
        }

        [Test]
        public void FillInput_MalyZdrojJeChyba()
        {
            Assert.That(() => RknnBackProject.FillInput(new byte[4], new byte[6], 2, 0),
                        Throws.ArgumentException);
        }

        [Test]
        public void ChybejiciModelJeSrozumitelnaChyba()
        {
            // Musi spadnout na CHYBEJICIM SOUBORU, ne az na chybejici knihovne - jinak by se na
            // vyvojovem stroji nedalo rozlisit "spatna cesta" od "tohle neni Orange Pi".
            Assert.That(() => new RknnBackProject(Path.Combine(Path.GetTempPath(), "neexistuje-arbot.rknn")),
                        Throws.TypeOf<FileNotFoundException>());
        }

        [Test]
        public void PrazdnaCestaJeChyba()
        {
            Assert.That(() => new RknnBackProject(null), Throws.ArgumentException);
        }

        // --- vyber implementace podle pripony --------------------------------------------

        [TestCase("models/Model61.1.rknn", true)]
        [TestCase("models/Model61.1.RKNN", true)]
        [TestCase("models/Model61.1_int8.onnx", false)]
        [TestCase("model.tflite", false)]
        [TestCase("", false)]
        public void IsRknn_PoznaModelProNpu(string path, bool ocekavano)
        {
            Assert.That(NnBackProject.IsRknn(path), Is.EqualTo(ocekavano));
        }

        // --- vychozi model z registru ----------------------------------------------------

        /// <summary>
        /// VYCHOZI <c>npumodel=</c> musi v repu existovat a byt to <c>.rknn</c>. Nacist ho tady
        /// nejde (chce NPU a <c>librknnrt.so</c>), ale prave proto ma smysl aspon tohle: soubor
        /// zapomenuty v repu nebo v nasazeni by se poznal teprve na robotu — a vypadal by jako
        /// porucha kamery, ne jako chybejici model. Tentyz duvod jako u <c>nnmodel=</c>
        /// (viz <c>OnnxBackProjectTest.VychoziModelZRegistruExistujeAJdeNacist</c>).
        ///
        /// <para>Preskoci se JEN kdyz v <c>models/</c> nejsou zadne <c>.rknn</c> vubec (tedy
        /// nebezime nad pracovni kopii repa). Kdyz tam jsou a chybi zrovna ten vychozi, je to
        /// CHYBA, ne duvod k preskoceni.</para>
        /// </summary>
        [Test]
        public void VychoziNpuModelZRegistruExistuje()
        {
            string dir = Path.Combine(ARBot.Common.Configuration.RepoPaths.RootOrBase(), "models");
            if (!Directory.Exists(dir) || Directory.GetFiles(dir, "*.rknn").Length == 0)
                Assert.Ignore("V models/ nejsou zadne .rknn modely (viz models/README.md).");

            string def = ARBot.Common.Configuration.ParamRegistry.NpuModel.Def.Default;
            Assert.That(NnBackProject.IsRknn(def), Is.True,
                        $"Vychozi npumodel='{def}' neni .rknn - do NPU cesty by se dostal ONNX.");

            string p = ARBot.Common.Configuration.RepoPaths.Resolve(def);
            Assert.That(File.Exists(p), Is.True,
                        $"Vychozi npumodel='{def}' neexistuje ({p}). Bud se soubor zapomnel pridat "
                        + "do repa, nebo je spatne default v ParamRegistry. Prevod: models/onnx2rknn.py.");
        }

        [Test]
        public void Open_ProOnnxVratiCpuImplementaci()
        {
            var p = Path.Combine(ARBot.Common.Configuration.RepoPaths.RootOrBase(),
                                 "models", "Model61.1_int8.onnx");
            if (!File.Exists(p)) Assert.Ignore("Model models/Model61.1_int8.onnx neni k dispozici.");

            using var bp = NnBackProject.Open(p);
            Assert.That(bp, Is.TypeOf<OnnxBackProject>());
        }
    }
}
