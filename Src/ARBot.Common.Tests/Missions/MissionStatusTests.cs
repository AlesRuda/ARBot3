using System;
using ARBot.Common.Missions;

namespace ARBot.Common.Tests.Missions
{
    /// <summary>
    /// <see cref="IMissionStatus"/> a <see cref="MissionStatusText"/>: jednotne hlaseni „jaka mise,
    /// v jake fazi, na co ceka".
    ///
    /// <para>Hlavni test je <see cref="KazdaFazeRobotour_MaOdpoved"/> — jde nad <b>vsemi</b>
    /// hodnotami vyctu, takze nova faze mise nemuze projit bez rozhodnuti, na co v ni robot ceka.
    /// Zbytek hlida, ze se prevod da udelat i nad <b>prectenou zpravou</b> (bere <c>int</c>), coz je
    /// duvod, proc „na co se ceka" neni ulozene v <c>MissionMsg</c>: je to funkce faze, kterou zprava
    /// uz nese, takze se dopocita i pro starsi zaznamy.</para>
    /// </summary>
    public class MissionStatusTests
    {
        [Test]
        public void KazdaFazeRobotour_MaOdpoved()
        {
            foreach (RobotourPhase faze in Enum.GetValues(typeof(RobotourPhase)))
            {
                var wait = MissionStatusText.WaitFor(faze);
                string text = MissionStatusText.PhaseText(faze);

                Assert.That(text, Is.Not.Empty, $"faze {faze} nema nazev");
                Assert.That(Enum.IsDefined(typeof(MissionWait), wait), Is.True,
                            $"faze {faze} se mapuje na neznamou hodnotu {wait}");
                if (wait != MissionWait.None)
                    Assert.That(MissionStatusText.WaitText(wait), Is.Not.Empty,
                                $"ceka se na {wait}, ale nema to text");
            }
        }

        [TestCase(RobotourPhase.Idle, MissionWait.MissionStart)]
        [TestCase(RobotourPhase.ArmingAtDepot, MissionWait.GpsFix)]
        [TestCase(RobotourPhase.AwaitingEStop, MissionWait.EmergencyStopPressed)]
        [TestCase(RobotourPhase.Servicing, MissionWait.QrCode)]
        [TestCase(RobotourPhase.AwaitingEStopRelease, MissionWait.EmergencyStopReleased)]
        [TestCase(RobotourPhase.DrivingToPickup, MissionWait.Arrival)]
        [TestCase(RobotourPhase.DrivingToDrop, MissionWait.Arrival)]
        [TestCase(RobotourPhase.DrivingToDepot, MissionWait.Arrival)]
        public void FazeRobotour_MapujeNaOcekavaneCekani(RobotourPhase faze, MissionWait ocekavane)
        {
            Assert.That(MissionStatusText.WaitFor(faze), Is.EqualTo(ocekavane));
        }

        [TestCase(RobotourPhase.Finished)]
        [TestCase(RobotourPhase.Aborted)]
        public void KoncoveFaze_NecekajiNaNic(RobotourPhase faze)
        {
            Assert.Multiple(() =>
            {
                Assert.That(MissionStatusText.WaitFor(faze), Is.EqualTo(MissionWait.None));
                // Prazdny text je zamer: stranka pak radek "ceka se na" vubec neukaze.
                Assert.That(MissionStatusText.WaitText(MissionWait.None), Is.Empty);
            });
        }

        [Test]
        public void PrevodZeZpravy_DavaTotezCoZeZiveMise()
        {
            // MissionMsg nese fazi jako int; rozbor zaznamu musi dostat tentyz vysledek jako UI.
            foreach (RobotourPhase faze in Enum.GetValues(typeof(RobotourPhase)))
            {
                Assert.That(MissionStatusText.WaitFor((int)faze), Is.EqualTo(MissionStatusText.WaitFor(faze)));
                Assert.That(MissionStatusText.PhaseText((int)faze), Is.EqualTo(MissionStatusText.PhaseText(faze)));
            }
        }

        [Test]
        public void NeznamaFazeZNovejsihoZaznamu_SePriznaCislem()
        {
            // Starsi ctenar nad novejsim zaznamem: nesmi predstirat, ze hodnotu zna, ani spadnout.
            Assert.Multiple(() =>
            {
                Assert.That(MissionStatusText.PhaseText(99), Does.Contain("99"));
                Assert.That(MissionStatusText.WaitFor(99), Is.EqualTo(MissionWait.None));
                Assert.That(MissionStatusText.WaitText((MissionWait)99), Does.Contain("99"));
            });
        }

        [Test]
        public void JmenaMisi_OdpovidajiHodnotamParametruMission()
        {
            // Stranka podle jmena pozna, kterou misi ma zvyraznit; musi to byt tytez retezce,
            // jake zna registr parametru (a switch v ARBotRuntime).
            Assert.Multiple(() =>
            {
                Assert.That(MissionStatusText.FreeRun, Is.EqualTo("freerun"));
                Assert.That(MissionStatusText.Robotour, Is.EqualTo("robotour"));
            });
        }
    }
}
