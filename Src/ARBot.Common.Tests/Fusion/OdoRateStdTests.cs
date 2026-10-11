using System;
using ARBot.Common.Configuration;
using ARBot.Common.Fusion;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// <b>Vaha kol v uhlovem kanalu fuze</b> (<c>odoratestd=</c>, tema <c>lok-fuze-rucni-otoceni</c>).
    ///
    /// <para>Do 11. 10. 2026 platilo <c>OdoRateStd</c> 0,10 a dokumentace tvrdila, ze gyro kola
    /// „prehlasuje ~30 : 1". To byl informacni podil (plati pro konstantni ω). S procesnim sumem
    /// <c>SigmaAngAccel</c> 2 rad/s² rozhoduje posledni merenie a kola nesla <b>24 %</b> integralu ω
    /// (zmereno <c>ARBot.Analyze gyrokola</c> nad peti zaznamy tremi cestami). Test drzi ten
    /// mechanismus: gyro 100 Hz hlasi 0, kola 91 Hz hlasi posun δ, a meri se, kolik z δ skonci
    /// v odometrickem kurzu (integral fuzni ω).</para>
    /// </summary>
    [TestFixture]
    public class OdoRateStdTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc);

        /// <summary>Podil posunu kol na integralu ω za <paramref name="sec"/> s (gyro 0, kola δ).</summary>
        private static double PodilKol(FusionConfig cfg, double sec = 20)
        {
            const double Delta = 0.02;
            var e = new AsyncFusionEngine(new EKFModel(cfg), TimeSpan.FromSeconds(1));
            int ig = 0, io = 0;
            DateTime posledni = T0;
            while (true)
            {
                double tg = ig * 0.010, to = 0.003 + io * 0.011;   // gyro 100 Hz, kola ~91 Hz
                if (tg > sec && to > sec) break;
                if (tg <= to)
                {
                    posledni = T0.AddSeconds(tg); ig++;
                    e.Enqueue(ScalarStateMeasurement.AngularRate(0.0, cfg.GyroRateStd, posledni, "IMU/gyro"));
                }
                else
                {
                    posledni = T0.AddSeconds(to); io++;
                    e.Enqueue(ScalarStateMeasurement.AngularRate(Delta, cfg.OdoRateStd, posledni, "Odo/rate"));
                }
            }
            // Integral od 2 s (po usazeni) do konce.
            var a = e.GetStateAt(T0.AddSeconds(sec - 1));
            var b = e.GetStateAt(posledni);
            double dt = (posledni - a.TimeStamp).TotalSeconds;
            return Conversions(b.OdomTheta - a.OdomTheta) / (Delta * dt);
        }

        private static double Conversions(double a) => ARBot.Common.Common.Conversions.NormalizeOrientation(a);

        [Test]
        public void Vychozi_KolaNesouMalo()
        {
            double p = PodilKol(new FusionConfig());
            Assert.That(p, Is.InRange(0.04, 0.08), "pri odoratestd=0.3 nesou kola ~5,9 % integralu ω");
        }

        [Test]
        public void StaraHodnota_KolaNesouCtvrtinu()
        {
            // Proc se to menilo: pri 0,10 nebyla kola „1/30", ale ctvrtina.
            double p = PodilKol(new FusionConfig { OdoRateStd = FusionConfig.OdoRateStdLegacy });
            Assert.That(p, Is.InRange(0.20, 0.28));
        }

        [Test]
        public void Vychozi_JeTriDesetiny_AParametrHoCteZKonfigurace()
        {
            Assert.That(new FusionConfig().OdoRateStd, Is.EqualTo(0.3).Within(1e-12));
            Assert.That(double.Parse(ParamRegistry.OdoRateStd.Def.Default, System.Globalization.CultureInfo.InvariantCulture),
                        Is.EqualTo(new FusionConfig().OdoRateStd).Within(1e-12),
                        "default parametru se cte z FusionConfig - dve mista se jednou rozejdou");
            Assert.That(FusionConfig.OdoRateStdLegacy, Is.EqualTo(0.10), "s tim jely zaznamy do 11. 10. 2026");
        }

        [TestCase("0.3", true)]
        [TestCase("1", true)]
        [TestCase("0", false)]
        [TestCase("-0.1", false)]
        [TestCase("nesmysl", false)]
        public void Parametr_PrijmeJenKladne(string text, bool ok)
            => Assert.That(ParamRegistry.OdoRateStd.Def.Parse(text).Ok, Is.EqualTo(ok));
    }
}
