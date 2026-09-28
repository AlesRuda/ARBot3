using System;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Missions;

namespace ARBot.Robot
{
    /// <summary>
    /// Stav robota → LED pasek (<see cref="NeoPixelProcessor"/>). Odebira proud zprav runtime
    /// a nastavuje priznaky procesoru; animaci a posilani na pasek dela procesor sam.
    ///
    /// <para><b>Pravidla prevzata z ARBot2</b> (<c>State.cs</c>, <c>Program.cs</c> - tam je
    /// nastavoval ridici takt primo):</para>
    /// <list type="bullet">
    /// <item><b>couvani</b> = prikazana rychlost &lt; 0,</item>
    /// <item><b>brzda</b> = pokles prikazane rychlosti mezi takty o vic nez
    /// <see cref="BrakeDropMps"/>; navic (ARBot3) <b>drzene zastaveni</b>
    /// (<see cref="DriveCommandMsg.Held"/>). Pokles se podrzi <see cref="BrakeHold"/>, jinak by
    /// pri 10 Hz taktu a 20 Hz animaci jen probliknul,</item>
    /// <item><b>blinkry</b> = prikazana rotace nad ±<see cref="BlinkerRadPerSec"/> (+ = vlevo),</item>
    /// <item><b>nouzove zastaveni</b> = <see cref="DriveCommandMsg.EmergencyStop"/>,</item>
    /// <item><b>predni svetla</b> = <c>Alert</c>, kdyz mise ceka, az obsluha STISKNE nouzove
    /// zastaveni (ARBot2 <c>WaitStop</c>); jinak <c>KnightRider</c>.</item>
    /// </list>
    ///
    /// <para>⚠️ <b>Stav mise se bere ze ZPRAV</b> (<see cref="MissionMsg"/>, <see cref="TrackMsg"/>),
    /// ne z <c>ARBotRuntime.CurrentMission</c>: jeho vlastnosti berou zamek mise a <c>Post</c> sem
    /// chodi z vlakna, ktere muze zamek mise drzet (mise publikuje zpravy) - presne tak 17. 9. 2026
    /// zatuhl runtime pri volbe mise (viz doc/headless.md).</para>
    /// </summary>
    public sealed class NeoPixelBridge : IMessageSink, IDisposable
    {
        /// <summary>Pokles prikazane rychlosti mezi takty, ktery rozsviti brzdu [m/s] (ARBot2: 0,3).</summary>
        public const double BrakeDropMps = 0.3;

        /// <summary>Jak dlouho brzda po poklesu sviti.</summary>
        public static readonly TimeSpan BrakeHold = TimeSpan.FromSeconds(0.5);

        /// <summary>Prikazana rotace, od ktere sviti blinkr [rad/s] (ARBot2: 0,2).</summary>
        public const double BlinkerRadPerSec = 0.2;

        private readonly NeoPixelProcessor leds;
        private double? lastSpeed;
        private DateTime brakeUntil = DateTime.MinValue;

        public NeoPixelBridge(NeoPixelProcessor leds)
        {
            this.leds = leds ?? throw new ArgumentNullException(nameof(leds));
        }

        /// <inheritdoc/>
        public void Post(Message msg)
        {
            switch (msg)
            {
                case DriveCommandMsg d:
                    OnDrive(d);
                    break;
                case MissionMsg m:
                    SetFront(MissionStatusText.WaitFor((RobotourPhase)m.Phase, (RobotourStop)m.Stop));
                    break;
                case TrackMsg t:
                    SetFront(MissionStatusText.WaitFor((TrackPhase)t.Phase));
                    break;
            }
        }

        private void OnDrive(DriveCommandMsg d)
        {
            if (lastSpeed is double prev && prev - d.Speed > BrakeDropMps)
                brakeUntil = d.TimeStamp + BrakeHold;
            lastSpeed = d.Speed;

            leds.Backward = d.Speed < 0;
            leds.Break = d.Held || d.TimeStamp < brakeUntil;
            leds.LeftBlinker = d.RotationSpeed > BlinkerRadPerSec;
            leds.RightBlinker = d.RotationSpeed < -BlinkerRadPerSec;
            leds.EmergencyStop = d.EmergencyStop;
        }

        private void SetFront(MissionWait wait)
            => leds.FrontLights = wait == MissionWait.EmergencyStopPressed
                ? NeoPixelProcessor.FrontLightsEnum.Alert
                : NeoPixelProcessor.FrontLightsEnum.KnightRider;

        /// <summary>
        /// Odpojeni od runtime (Stop): vratit klidovy stav, jinak by po zastaveni runtime pasek
        /// dal ukazoval posledni takt - treba blinkr nebo brzdu.
        /// </summary>
        public void Dispose()
        {
            leds.Backward = false;
            leds.Break = false;
            leds.LeftBlinker = false;
            leds.RightBlinker = false;
            leds.EmergencyStop = false;
            leds.FrontLights = NeoPixelProcessor.FrontLightsEnum.KnightRider;
        }
    }

    /// <summary>
    /// Dve uvolneni v PORADI: nejdriv odpojit od streamu, pak most (klidovy stav). Obracene by
    /// posledni zprava ze streamu mohla priznaky po vynulovani znovu nastavit.
    /// </summary>
    internal sealed class DisposeBoth : IDisposable
    {
        private readonly IDisposable first, second;
        public DisposeBoth(IDisposable first, IDisposable second) { this.first = first; this.second = second; }
        public void Dispose()
        {
            try { first?.Dispose(); } finally { second?.Dispose(); }
        }
    }
}
