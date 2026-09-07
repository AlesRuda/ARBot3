using System;
using ARBot.Common.Vision;
using NUnit.Framework;

namespace ARBot.Common.Tests.Vision
{
    /// <summary>
    /// Testy <see cref="SegmentationMetrics"/> — meridlo kvality segmentace se testuje proti
    /// ZNAMYM odpovedim, protoze spatne pocitana metrika nevypada jako chyba, ale jako vysledek.
    /// </summary>
    public class SegmentationMetricsTest
    {
        /// <summary>Prahy jsou 128, takze 200 = sjizdno, 50 = nesjizdno.</summary>
        private const byte Ano = 200;
        private const byte Ne = 50;

        [Test]
        public void Compare_ShodaVeVsem_PresnostAIoUJednicka()
        {
            var pred = new byte[] { Ano, Ano, Ne, Ne };
            var truth = new byte[] { 255, 255, 0, 0 };

            var c = SegmentationMetrics.Compare(pred, truth, 4);

            Assert.That(c.Total, Is.EqualTo(4));
            Assert.That(c.TruePositive, Is.EqualTo(2));
            Assert.That(c.TrueNegative, Is.EqualTo(2));
            Assert.That(c.FalsePositive, Is.EqualTo(0));
            Assert.That(c.FalseNegative, Is.EqualTo(0));
            Assert.That(c.Accuracy, Is.EqualTo(1.0));
            Assert.That(c.IoU, Is.EqualTo(1.0));
            Assert.That(c.Precision, Is.EqualTo(1.0));
            Assert.That(c.Recall, Is.EqualTo(1.0));
        }

        [Test]
        public void Compare_UplnyOpak_PresnostAIoUNula()
        {
            var pred = new byte[] { Ano, Ano, Ne, Ne };
            var truth = new byte[] { 0, 0, 255, 255 };

            var c = SegmentationMetrics.Compare(pred, truth, 4);

            Assert.That(c.Accuracy, Is.EqualTo(0.0));
            Assert.That(c.IoU, Is.EqualTo(0.0));
            Assert.That(c.Precision, Is.EqualTo(0.0));
            Assert.That(c.Recall, Is.EqualTo(0.0));
        }

        /// <summary>
        /// Jadro toho, proc metrika neni jen procento: "vsechno je cesta" nad snimkem, kde je
        /// cesty 80 %, ma presnost 0,80 — a IoU taky 0,80, ale <b>precision 0,80 pri recall 1,00</b>
        /// to prozradi. Kdyby se merila jen presnost, vypadala by ta odpoved dobre.
        /// </summary>
        [Test]
        public void Compare_VsechnoJeCesta_PresnostJePodilCesty()
        {
            var pred = new byte[10];
            var truth = new byte[10];
            for (int i = 0; i < 10; i++)
            {
                pred[i] = Ano;              // metoda tvrdi cestu vsude
                truth[i] = i < 8 ? (byte)255 : (byte)0;   // pravda: 8 z 10 je cesta
            }

            var c = SegmentationMetrics.Compare(pred, truth, 10);

            Assert.That(c.Accuracy, Is.EqualTo(0.8).Within(1e-12));
            Assert.That(c.Recall, Is.EqualTo(1.0), "cestu nasla celou");
            Assert.That(c.Precision, Is.EqualTo(0.8).Within(1e-12), "ale pridala i 2 pixely, kde cesta neni");
            Assert.That(c.FalsePositive, Is.EqualTo(2));
            Assert.That(c.FalseNegative, Is.EqualTo(0));
        }

        [Test]
        public void Compare_PulNaPul_MetrikySeLisi()
        {
            // pravda: 4 pixely cesty; metoda najde 2 z nich a 2 pripise jinam
            var pred = new byte[] { Ano, Ano, Ne, Ne, Ano, Ano, Ne, Ne };
            var truth = new byte[] { 255, 255, 255, 255, 0, 0, 0, 0 };

            var c = SegmentationMetrics.Compare(pred, truth, 8);

            Assert.That(c.TruePositive, Is.EqualTo(2));
            Assert.That(c.FalseNegative, Is.EqualTo(2));
            Assert.That(c.FalsePositive, Is.EqualTo(2));
            Assert.That(c.TrueNegative, Is.EqualTo(2));
            Assert.That(c.Accuracy, Is.EqualTo(0.5));
            Assert.That(c.IoU, Is.EqualTo(2.0 / 6.0).Within(1e-12), "IoU je prisnejsi nez presnost");
            Assert.That(c.PredictedFraction, Is.EqualTo(0.5));
            Assert.That(c.TruthFraction, Is.EqualTo(0.5));
        }

        /// <summary>
        /// Prah 128 je hranice VCETNE — stejne jako v <c>BackProjectReport</c>
        /// a <c>OccupancyIntegratorConfig.RoadNeutral</c>. Kdyby se to rozeslo, cisla z ruznych
        /// meridel by se prestala dat srovnavat, a nikdo by nepoznal proc.
        /// </summary>
        [Test]
        public void Compare_Prah128JeVcetne()
        {
            var pred = new byte[] { 127, 128 };
            var truth = new byte[] { 255, 255 };

            var c = SegmentationMetrics.Compare(pred, truth, 2);

            Assert.That(c.TruePositive, Is.EqualTo(1), "128 uz je sjizdno");
            Assert.That(c.FalseNegative, Is.EqualTo(1), "127 jeste neni");
        }

        /// <summary>Maska 0/1 (jak ji uklada notebook) projde se snizenym prahem pravdy.</summary>
        [Test]
        public void Compare_MaskaNulaJedna_ProjdeSVlastnimPrahem()
        {
            var pred = new byte[] { Ano, Ne };
            var truth = new byte[] { 1, 0 };

            var c = SegmentationMetrics.Compare(pred, truth, 2, truthThreshold: 1);

            Assert.That(c.Accuracy, Is.EqualTo(1.0));
        }

        /// <summary>
        /// Kdyz cestu netvrdi ani pravda, ani metoda, IoU <b>neexistuje</b> — nula by tvrdila
        /// "uplne mimo", ackoli je odpoved bezchybna.
        /// </summary>
        [Test]
        public void Compare_ZadnaCestaNikde_IoUJeNaN()
        {
            var pred = new byte[] { Ne, Ne };
            var truth = new byte[] { 0, 0 };

            var c = SegmentationMetrics.Compare(pred, truth, 2);

            Assert.That(c.Accuracy, Is.EqualTo(1.0));
            Assert.That(c.IoU, Is.NaN);
            Assert.That(c.Precision, Is.NaN);
            Assert.That(c.Recall, Is.NaN);
        }

        [Test]
        public void Soucet_SkladaMaticeZeSnimku()
        {
            var a = SegmentationMetrics.Compare(new byte[] { Ano, Ne }, new byte[] { 255, 0 }, 2);
            var b = SegmentationMetrics.Compare(new byte[] { Ano, Ne }, new byte[] { 0, 255 }, 2);

            var soucet = a + b;

            Assert.That(soucet.Total, Is.EqualTo(4));
            Assert.That(soucet.TruePositive, Is.EqualTo(1));
            Assert.That(soucet.TrueNegative, Is.EqualTo(1));
            Assert.That(soucet.FalsePositive, Is.EqualTo(1));
            Assert.That(soucet.FalseNegative, Is.EqualTo(1));
            Assert.That(soucet.Accuracy, Is.EqualTo(0.5));
        }

        [Test]
        public void Compare_KratsiPole_Vyhodi()
        {
            Assert.Throws<ArgumentException>(
                () => SegmentationMetrics.Compare(new byte[2], new byte[4], 4));
            Assert.Throws<ArgumentException>(
                () => SegmentationMetrics.Compare(new byte[4], new byte[2], 4));
            Assert.Throws<ArgumentNullException>(
                () => SegmentationMetrics.Compare(null, new byte[4], 4));
        }
    }
}
