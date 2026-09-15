using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.Robot.Web;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// <b>Fail-ramec motoroveho driveru nesmi otevrit zadnou bezpecnostni branu.</b>
    ///
    /// <para><b>Nac to je:</b> obe brany na strance nahledu (vyber mise, zapis kalibrace do
    /// senzoru) stoji na jedine fyzicke pojistce — <i>drzenem nouzovem zastaveni</i>.
    /// <c>SDC2160Ex</c> ale po chybe (odpojeny USB prevodnik, neparsovatelna odpoved) vyrabi
    /// kazdych 500 ms nahradni ramec s <c>IsEmergencyStop = true</c>; to je fail-safe pro
    /// <i>ridici smycku</i> (at robot stoji), ne mereni tlacitka. Ramec je navic <b>cerstvy</b>,
    /// takze kontrola stari (<c>MotorFreshSec</c>) ho pusti — a brana by se otevrela prave
    /// v okamziku, kdy o stavu robotu nevime nic. Rozlisuje to
    /// <see cref="ARBot.Common.Models.IMotorState.HasMeasurement"/>.</para>
    /// </summary>
    [NonParallelizable]
    public class WebStatusFailRamecTests
    {
        /// <summary>Fail-ramec driveru: stop je v nem fail-safe konstanta, nic z nej nebylo zmereno.</summary>
        private static MotorStateBase FailRamec()
            => new MotorStateBase(true, 0, 0, 0, 0, 0, 0, 0, hasMeasurement: false);

        private static MagCalMsg HotovaKalibrace() => new MagCalMsg
        {
            Phase = (int)MagCalPhase.Ready,
            Verdict = "HOTOVO",
            MissingText = string.Empty,
            FilledAzimuthBins = 24, TiltGroups = 3, TiltedGroups = 2, HasOppositeTilts = true,
            Samples = 3770, Condition = 412.5, SdMagnitudeG = 0.0012, SdInclinationDeg = 0.21,
            BRefG = 0.4818,
            Vnwrg23 = "1.2,0.0,0.0,0.0,1.1,0.0,0.0,0.0,1.0,-0.274,-0.058,0.076",
        };

        [Test]
        public void FailRamec_NepustiVyberMise()
        {
            var s = new WebStatus { AwaitingMission = true };
            s.Post(FailRamec());

            var duvod = s.MissionBlockedReason();

            Assert.That(duvod, Is.Not.Null,
                        "odpojeny motorovy UART neni stisknute tlacitko - misi vybrat nelze");
            Assert.That(duvod, Does.Contain("motory"),
                        "duvod musi rict, CO je spatne - obsluha u robota vidi jen tuhle vetu");
        }

        [Test]
        public void FailRamec_NepustiZapisKalibrace()
        {
            var s = new WebStatus();
            s.Post(FailRamec());
            s.Post(HotovaKalibrace());

            Assert.That(s.MagCalWriteBlockedReason(), Does.Contain("motory"),
                        "do senzoru se nezapisuje, kdyz o stavu stopu nevime nic");
        }
    }
}
