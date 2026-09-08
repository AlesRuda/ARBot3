using System;
using System.Numerics;
using ARBot.Common.Calibration;
using ARBot.Common.Missions;
using ARBot.Common.Regulators;
using ARBot.Common.Simulation;
using ARBot.HAL.Devices;
using ARBot.HAL.Devices.AHRS;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// <b>Kalibrace magnetometru od zacatku do konce nad VIRTUALNIM HW.</b>
    ///
    /// <para><b>Nacpak.</b> Jednotkove testy pokryvaji prolozeni (<c>MagCalFit</c>), kose pokryti
    /// a automat mise <b>kazde zvlast</b>. Tenhle test overuje, ze spolu mluvi: do simulace se
    /// vlozi <b>ZNAME</b> tvrde a mekke zelezo, robotem se „otoci rukou" a mise musi vratit
    /// PRAVE TA cisla. To je jedina kontrola, ktera by chytila zamenu framu, spatne poradi polí
    /// nebo obracenou inverzi — tedy chyby, ktere v jednotkovych testech projdou, protoze si
    /// obe strany plati stejnou konvenci.</para>
    ///
    /// <para>⚠️ <b>Neoveruje to zelezo skutecneho robota</b>, jen to, ze nas retez najde, co do
    /// nej vlozime. Terenni mereni to nenahrazuje. Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    [TestFixture]
    public class VirtualMagCalEndToEndTest
    {
        /// <summary>Vnucene tvrde zelezo [G] — rad odpovida tomu, co bylo namereno na robotu.</summary>
        private static readonly Vector3 HardIron = new Vector3(-0.274f, -0.058f, 0.076f);

        /// <summary>Vnucene mekke zelezo: xx, yy, zz, xy, xz, yz (symetricke).</summary>
        private static readonly double[] SoftIron = { 1.222, 1.175, 1.081, 0.005, 0.010, -0.012 };

        private sealed class StubRegulator : IRegulator
        {
            public RegulatorResult Control(ARBot.Common.Models.IModelState s) => new RegulatorResult();
            public bool IsFinished => false;
        }

        private sealed class Drzitel : IRegulatorHolder
        {
            public IRegulator Regulator { get; set; } = new StubRegulator();
        }

        private static VirtualSensorOptions Volby() => new VirtualSensorOptions
        {
            // Bez sumu: test ma overit KONVENCE a algebru, ne statistiku. Sum ma vlastni test.
            ImuHeadingNoiseRad = 0,
            ImuGyroNoiseRad = 0,
            MagNoiseG = 0,
            MagHardIronG = HardIron,
            MagSoftIron = SoftIron,
        };

        /// <summary>
        /// Odsimuluje rotacni test: obrat na rovine a dva obraty s protilehlymi naklony.
        /// Otaci se RUKOU (<see cref="SimulatedRobot.HandSpinRadPerSec"/>), protoze presne to
        /// dela obsluha — motory pri kalibraci stoji.
        /// </summary>
        private static void OdsimulujRotacniTest(SimulatedRobot robot, VirtualSensorOptions volby,
                                                 MagCalCollector sberac, DateTime t0)
        {
            const double omega = 0.5, dt = 0.01;
            var naklony = new[] { (0.0, 0.0), (0.40, 0.0), (0.40, Math.PI) };

            var t = t0;
            robot.HandSpinRadPerSec = omega;
            foreach (var (naklon, smer) in naklony)
            {
                volby.TiltRad = naklon;
                volby.TiltDirRad = smer;

                int n = (int)(2 * Math.PI / omega / dt);
                for (int i = 0; i < n; i++)
                {
                    t = t.AddSeconds(dt);
                    robot.Advance(t);
                    sberac.Add(VzorekIMU(robot, volby, t));
                }
            }
            robot.HandSpinRadPerSec = 0;
        }

        /// <summary>
        /// Vzorek jako by ho vyrobilo <see cref="VirtualImu"/> — tady se sklada rovnou, aby test
        /// nezavisel na vlakne senzoru a na skutecnem case.
        /// </summary>
        private static ARBot.Common.Models.IMUState VzorekIMU(
            SimulatedRobot robot, VirtualSensorOptions v, DateTime t)
        {
            double heading = robot.Theta, omega = robot.AngularSpeed;

            double b = v.MagFieldG, sklon = v.MagInclinationRad;
            var poleSvet = new Vector3((float)(b * Math.Cos(sklon)), 0f, (float)(-b * Math.Sin(sklon)));
            var gSvet = new Vector3(0f, 0f, -9.81f);

            var qKurz = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)-heading);
            var osa = new Vector3((float)Math.Cos(v.TiltDirRad + Math.PI / 2),
                                  (float)Math.Sin(v.TiltDirRad + Math.PI / 2), 0f);
            var qNaklon = Quaternion.CreateFromAxisAngle(osa, (float)-v.TiltRad);
            Vector3 DoTelesa(Vector3 x)
                => Vector3.Transform(Vector3.Transform(x, qKurz), qNaklon);

            var ideal = DoTelesa(poleSvet);
            var C = MathNet.Numerics.LinearAlgebra.Matrix<double>.Build.DenseOfArray(new[,] {
                { SoftIron[0], SoftIron[3], SoftIron[4] },
                { SoftIron[3], SoftIron[1], SoftIron[5] },
                { SoftIron[4], SoftIron[5], SoftIron[2] } });
            var x0 = MathNet.Numerics.LinearAlgebra.Vector<double>.Build
                .DenseOfArray(new double[] { ideal.X, ideal.Y, ideal.Z });
            var r = C.Inverse() * x0;
            var raw = new Vector3((float)r[0], (float)r[1], (float)r[2]) + v.MagHardIronG;

            return new ARBot.Common.Models.IMUState
            {
                Name = "VirtualIMU",
                HasAbsoluteHeading = true,
                MagnetometerRaw = raw,
                Magnetometer = raw,
                Acceleration = DoTelesa(gSvet),
                AngularVelocity = new Vector3(0f, 0f, (float)omega),
                TimeStamp = t,
            };
        }

        [Test]
        public void Mise_NajdeVnuceneZelezo_AZapiseHoDoRegistru23()
        {
            var volby = Volby();
            var ctl = new VirtualMagCalControl(volby);
            var d = new Drzitel();
            using var mise = new MagCalMission(ctl, d);

            mise.StartMission();

            // Referencni |B| se cte z registru 21, tedy z toho, co simulace opravdu vyrabi.
            Assert.That(mise.BRefG, Is.EqualTo(volby.MagFieldG).Within(1e-6));
            Assert.That(d.Regulator, Is.Null, "robot se pri kalibraci nesmi rozjet");

            var robot = new SimulatedRobot(0.5, new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc));
            var sberac = new MagCalCollector(mise.BRefG);
            OdsimulujRotacniTest(robot, volby, sberac,
                                 new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc));

            Assert.That(sberac.Coverage.Complete, Is.True, sberac.Coverage.MissingText());
            Assert.That(sberac.Usable, Is.True, sberac.Verdict);

            // TO PODSTATNE: vratilo se PRAVE to, co se vlozilo?
            var r = sberac.LastResult;
            Assert.Multiple(() =>
            {
                Assert.That(r.B[0], Is.EqualTo(HardIron.X).Within(0.01), "tvrde zelezo X");
                Assert.That(r.B[1], Is.EqualTo(HardIron.Y).Within(0.01), "tvrde zelezo Y");
                Assert.That(r.B[2], Is.EqualTo(HardIron.Z).Within(0.01), "tvrde zelezo Z");
                Assert.That(r.C[0, 0], Is.EqualTo(SoftIron[0]).Within(0.02), "mekke zelezo xx");
                Assert.That(r.C[1, 1], Is.EqualTo(SoftIron[1]).Within(0.02), "mekke zelezo yy");
                Assert.That(r.C[2, 2], Is.EqualTo(SoftIron[2]).Within(0.02), "mekke zelezo zz");
            });

            // A projde to celou cestou az do "senzoru"?
            Assert.That(ctl.LastWritten, Is.Null, "pred pokynem se zapisovat nesmi");
        }

        [Test]
        public void OtaceniRukou_ViditGyro_AleNeposunePolohu()
        {
            // Bez tohohle by kolektor (pokryti z INTEGROVANEHO gyra) stal, protoze mise zahodi
            // regulator a ControlLoop posila Drive(0,0) pri kazdem taktu.
            var t0 = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);
            var robot = new SimulatedRobot(0.5, t0);

            robot.HandSpinRadPerSec = 1.0;
            robot.Advance(t0.AddSeconds(1.0));

            Assert.Multiple(() =>
            {
                Assert.That(robot.AngularSpeed, Is.EqualTo(1.0).Within(1e-6),
                            "gyro musi otaceni rukou videt");
                Assert.That(robot.Theta, Is.EqualTo(1.0).Within(0.01));
                Assert.That(robot.X, Is.EqualTo(0).Within(1e-9), "otaceni na miste nesmi posunout");
                Assert.That(robot.Y, Is.EqualTo(0).Within(1e-9));
            });
        }

        [Test]
        public void VirtualniRegistr21_HlasiSimulovanePole_NeHodnotuZeSenzoru()
        {
            // Kdyby hlasil 0,4818 z registru skutecneho VN a simulace delala jine pole, mise by
            // normovala na jine |B| a vysledek by se nedal porovnat se vstupem.
            var volby = new VirtualSensorOptions { MagFieldG = 0.55 };
            var ctl = new VirtualMagCalControl(volby);

            var r = ctl.ReadRegister(IMagCalControl.RegReference);

            double b = Math.Sqrt(r[0] * r[0] + r[1] * r[1] + r[2] * r[2]);
            Assert.That(b, Is.EqualTo(0.55).Within(1e-6));
        }

        [Test]
        public void VirtualniRegistr47_HlasiNIC_NePredstiraNezavislouKontrolu()
        {
            // Simulace vlastni HSI algoritmus nema. Vymyslet cislo by znamenalo predstirat
            // nezavislou kontrolu, ktera neexistuje.
            var ctl = new VirtualMagCalControl(new VirtualSensorOptions());

            Assert.That(ctl.ReadRegister(IMagCalControl.RegCalculatedHsi), Is.Null);
        }

        [Test]
        public void VychoziRegistr23_JeJEDNOTKOVY()
        {
            // Jako po vnrestore.sh --clearmag: virtualni senzor zadnou palubni kompenzaci nedela.
            var r = new VirtualMagCalControl(new VirtualSensorOptions())
                .ReadRegister(IMagCalControl.RegCompensation);

            Assert.That(r.Length, Is.EqualTo(12));
            Assert.That(r[0], Is.EqualTo(1.0).Within(1e-9));
            Assert.That(r[9], Is.EqualTo(0.0).Within(1e-9));
        }
    }
}
