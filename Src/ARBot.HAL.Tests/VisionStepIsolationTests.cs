using System;
using ARBot.Common.Devices;
using ARBot.Common.Vision;
using ARBot.HAL.Devices.Camera;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// <b>Porucha vize se nesmí tvářit jako odpojená kamera</b> (nález auditu 15. 9. 2026).
    ///
    /// <para>Dopočet odvozených vlastností snímku (pravděpodobnost cesty z ONNX/RKNN, polární
    /// grid) běží <b>uvnitř</b> snímací smyčky ovladače — a ta má kolem sebe <c>try</c>, jehož
    /// <c>catch</c> hlásí „cteni snimku selhalo (odpojeno?)", <b>zboří pipeline</b> a jde do
    /// reconnectu. Softwarová chyba v segmentaci nebo v gridu tedy vypadala jako porucha USB
    /// a <b>přičítala se k reálným výpadkům D435</b>, které se zrovna vyšetřují
    /// (viz doc/hardware.md) — to je ta nejdražší možná záměna.</para>
    ///
    /// <para>Správná odpověď je opačná: surový snímek je v pořádku, jen k němu nejsou odvozená
    /// data. Snímek se tedy vydá dál a porucha se ohlásí — škrceně, protože snímků chodí 30/s.</para>
    /// </summary>
    public class VisionStepIsolationTests
    {
        private sealed class PadajiciProcesor : ICameraFrameProcessor
        {
            public int Volani;
            public void Process(CameraFrame frame)
            {
                Volani++;
                throw new InvalidOperationException("simulovana porucha segmentace");
            }
        }

        private sealed class FungujiciProcesor : ICameraFrameProcessor
        {
            public int Volani;
            public void Process(CameraFrame frame) => Volani++;
        }

        [Test]
        public void VyjimkaVeVizi_NeprojdeDoSmyckyKamery()
        {
            var p = new PadajiciProcesor();
            var frame = new CameraFrame { Name = "Left" };

            Assert.DoesNotThrow(() => CameraVisionStep.Run(p, frame, "Left"),
                                "vyjimka z vize by se ve smycce kamery precetla jako odpojeni");
            Assert.That(p.Volani, Is.EqualTo(1), "procesor se vubec nezavolal - test nic nemeri");
        }

        [Test]
        public void VyjimkaVeVizi_Ohlasi()
        {
            var p = new PadajiciProcesor();
            var frame = new CameraFrame { Name = "Left" };

            bool ohlaseno = CameraVisionStep.Run(p, frame, "Left");

            Assert.That(ohlaseno, Is.False, "vraci se, jestli vize DOBEHLA");
        }

        [Test]
        public void BezPoruchy_ProjdeBezeZmeny()
        {
            var p = new FungujiciProcesor();
            var frame = new CameraFrame { Name = "Left" };

            bool ok = CameraVisionStep.Run(p, frame, "Left");

            Assert.That(ok, Is.True);
            Assert.That(p.Volani, Is.EqualTo(1));
        }

        [Test]
        public void BezProcesoru_SeNicNedeje()
        {
            Assert.That(CameraVisionStep.Run(null, new CameraFrame(), "Left"), Is.True);
        }
    }
}
