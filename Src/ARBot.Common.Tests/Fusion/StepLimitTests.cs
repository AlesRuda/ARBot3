using System;
using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// <b>Limit kroku filtru na jedno merenie (<see cref="IMeasurement.MaxStep"/>).</b>
    ///
    /// <para><b>Nacpak.</b> Na Robotouru 19. 9. 2026 prisel kazdy skok pozy (0,6-4 m) do 0,1 s po
    /// prijatem mereni koridoru: filtr nahromadeny drift stahl v jednom kroku, robot se skokem ocitl
    /// v blokovane casti gridu a presel do uniku. Zahodit velke merenie nejde (tvrdy gate delal
    /// vysledek horsi nez nekorigovat, zmereno 25. 8. 2026) a zvetsit sigmu drift jen zakonzervuje.
    /// Lecba: krok filtru podel osy merenia se omezi tim, ze se R nafoukne PRAVE TAK, aby krok vysel
    /// na limit - filtr zustane konzistentni (P se zmensi jen umerne tomu, co prijal) a zbytek
    /// inovace ceka na dalsi merenie. Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    [TestFixture]
    public class StepLimitTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 21, 10, 0, 0, DateTimeKind.Utc);

        /// <summary>Model s pozou (0,0) a sigmou polohy <paramref name="sigmaPos"/> v obou osach.</summary>
        private static EKFModel Model(double sigmaPos)
        {
            var m = new EKFModel();
            m.SetPose(0, 0, 0);
            m.P[EKFModel.IX, EKFModel.IX] = sigmaPos * sigmaPos;
            m.P[EKFModel.IY, EKFModel.IY] = sigmaPos * sigmaPos;
            return m;
        }

        private static AxisOffsetMeasurement Pricne(double value, double std, double? maxStep)
            => new AxisOffsetMeasurement(0, 1, value, std, T0, "Corridor") { MaxStep = maxStep };

        [Test]
        public void BezLimitu_seNicNemeni()
        {
            var a = Model(2.0); var b = Model(2.0);
            a.Update(Pricne(6.0, 0.1, null));
            b.Update(Pricne(6.0, 0.1, null));
            Assert.That(a.Current(T0).Y, Is.EqualTo(b.Current(T0).Y).Within(1e-12));
            Assert.That(a.Current(T0).Y, Is.GreaterThan(5.0), "bez limitu filtr drift stahne skoro cely");
            Assert.That(a.LastStepLimited, Is.False);
            Assert.That(a.LastInflation, Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void VelkaInovace_krokJePresneLimit()
        {
            var m = Model(2.0);
            m.Update(Pricne(6.0, 0.1, 0.25));
            Assert.That(m.LastAccepted, Is.True, "limit merenie NEZAHAZUJE");
            Assert.That(m.LastStepLimited, Is.True);
            Assert.That(m.Current(T0).Y, Is.EqualTo(0.25).Within(1e-9), "krok podel osy merenia = limit");
            Assert.That(m.Current(T0).X, Is.EqualTo(0).Within(1e-9), "kolmou osu merenie nehybe");
            Assert.That(m.LastInflation, Is.GreaterThan(1.0));
        }

        [Test]
        public void MalaInovace_projdeBezeZmeny()
        {
            var bez = Model(2.0); var s = Model(2.0);
            bez.Update(Pricne(0.1, 0.1, null));
            s.Update(Pricne(0.1, 0.1, 0.25));
            Assert.That(s.Current(T0).Y, Is.EqualTo(bez.Current(T0).Y).Within(1e-12));
            Assert.That(s.LastStepLimited, Is.False);
            Assert.That(s.LastInflation, Is.EqualTo(1.0).Within(1e-12));
        }

        [Test]
        public void OmezenyKrok_nechaFiltruVetsiNejistotu()
        {
            // Konzistence: kdyz filtr prijal jen cast inovace, nesmi si zmensit P jako po celem
            // mereni - jinak by byl sebejisty o poloze, ktera je porad metry vedle.
            var bez = Model(2.0); var s = Model(2.0);
            bez.Update(Pricne(6.0, 0.1, null));
            s.Update(Pricne(6.0, 0.1, 0.25));
            Assert.That(s.P[EKFModel.IY, EKFModel.IY], Is.GreaterThan(bez.P[EKFModel.IY, EKFModel.IY] * 10));
            Assert.That(s.P[EKFModel.IY, EKFModel.IY], Is.LessThan(4.0), "ale neco prijal, P musi klesnout");
        }

        [Test]
        public void ZbytekInovace_seDotahnePoDavkach()
        {
            // Drift 6 m, limit 0,25 m na merenie: poza se ma k mereni dotahnout po davkach,
            // KAZDY krok pod limitem, a nakonec dojet - to je cely smysl (misto skoku rampa).
            var m = Model(2.0);
            double prev = 0;
            int kroku = 0;
            for (int i = 0; i < 60; i++)
            {
                m.Update(Pricne(6.0, 0.1, 0.25));
                double y = m.Current(T0).Y;
                Assert.That(y - prev, Is.LessThanOrEqualTo(0.25 + 1e-9), $"krok {i} nad limitem");
                Assert.That(y - prev, Is.GreaterThanOrEqualTo(-1e-9), $"krok {i} couva");
                prev = y;
                kroku++;
                if (y > 5.99) break;
            }
            Assert.That(m.Current(T0).Y, Is.GreaterThan(5.99), "nakonec se dotahne");
            Assert.That(kroku, Is.GreaterThanOrEqualTo(24), "6 m po 0,25 m je nejmin 24 kroku");
        }

        [Test]
        public void LimitSeSkladaSeSoftGatem_platiPrisnejsi()
        {
            // Soft gate nafoukne R podle NIS; limit kroku podle P a inovace. Vysledek musi drzet
            // OBOJI, tedy krok nesmi prekrocit limit ani kdyz uz Soft gate R nafoukl.
            var m = Model(2.0);
            m.Update(new AxisOffsetMeasurement(0, 1, 6.0, 0.1, T0, "Corridor")
            {
                GateThreshold = Gating.ChiSquareThreshold(1), GateMode = GateMode.Soft, MaxStep = 0.25,
            });
            Assert.That(m.LastAccepted, Is.True);
            Assert.That(m.Current(T0).Y, Is.LessThanOrEqualTo(0.25 + 1e-9));
            Assert.That(m.Current(T0).Y, Is.GreaterThan(0.2), "Soft sam by tu dal vic; limit ho jen dorazi na 0,25");
        }

        [Test]
        public void KurzMaLimitTaky()
        {
            var m = Model(0.5);
            m.P[EKFModel.ITh, EKFModel.ITh] = 1.0;
            double lim = 2.0 * Math.PI / 180;
            m.Update(new HeadingMeasurement(0.5, 0.01, T0, "Corridor") { MaxStep = lim });
            Assert.That(m.Current(T0).Theta, Is.EqualTo(lim).Within(1e-9));
        }

        [Test]
        public void VicerozmerneMerenie_limitIgnoruje()
        {
            // Uzavreny tvar plati pro skalarni merenie; u vektoroveho se limit neuplatni (a nesmi
            // spadnout). Koridor i kompas posilaji skalary, GPS poloha je 2-D.
            var bez = Model(2.0); var s = Model(2.0);
            bez.Update(new PositionMeasurement(6, 0, 0.1, 0.1, T0, "GPS"));
            s.Update(new PositionMeasurement(6, 0, 0.1, 0.1, T0, "GPS") { MaxStep = 0.25 });
            Assert.That(s.Current(T0).X, Is.EqualTo(bez.Current(T0).X).Within(1e-12));
            Assert.That(s.LastStepLimited, Is.False);
        }

        [Test]
        public void NekladnyLimit_seBereJakoVypnuty()
        {
            var bez = Model(2.0); var s = Model(2.0);
            bez.Update(Pricne(6.0, 0.1, null));
            s.Update(Pricne(6.0, 0.1, 0));
            Assert.That(s.Current(T0).Y, Is.EqualTo(bez.Current(T0).Y).Within(1e-12));
        }
    }
}
