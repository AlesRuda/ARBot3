using System;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Snimek stavu mise Robotour. Viz doc/robotour-mission.md.
    ///
    /// <para>Domenovy objekt, ktery si sam vyrabi svou log-zpravu (<see cref="ToLogMessage"/>) —
    /// konverzi vlastni domena, <c>Logs</c> zustava pasivni DTO (viz CLAUDE.md).</para>
    /// </summary>
    public sealed class MissionState
    {
        /// <summary>Faze automatu.</summary>
        public RobotourPhase Phase;

        /// <summary>Ktere zastaveni se obsluhuje (plati v servisnim okne).</summary>
        public RobotourStop Stop;

        /// <summary>Cas vstupu do soucasne faze.</summary>
        public DateTime PhaseEnteredAt;

        /// <summary>Uplynuly cas mise od <c>Start</c> [s].</summary>
        public double ElapsedSec;

        /// <summary>Depo [stupne] a jestli uz je zapamatovane.</summary>
        public bool HasDepot;
        public double DepotLatDeg, DepotLonDeg;

        /// <summary>Misto nakladky [stupne] + zdrojovy text kodu.</summary>
        public bool HasPickup;
        public double PickupLatDeg, PickupLonDeg;
        public string PickupCodeText;

        /// <summary>Misto vykladky [stupne] + zdrojovy text kodu.</summary>
        public bool HasDrop;
        public double DropLatDeg, DropLonDeg;
        public string DropCodeText;

        /// <summary>Duvod preruseni; prazdny, kdyz mise nebyla prerusena.</summary>
        public string AbortReason;

        /// <summary>Citace pro zaznam.</summary>
        public int CodesRead, CodesRejected, Timeouts;

        /// <summary>Je prave aktivni nouzove zastaveni?</summary>
        public bool EmergencyStop;

        /// <summary>Hlasi mise „kod nevidim"?</summary>
        public bool CodeNotSeen;

        /// <summary>Cas, ke kteremu stav plati.</summary>
        public DateTime TimeStamp;

        /// <summary>Prevod na log-zpravu (viz CLAUDE.md — konverzi vlastni domena).</summary>
        public Logs.MissionMsg ToLogMessage()
            => new Logs.MissionMsg
            {
                Phase = (int)Phase,
                Stop = (int)Stop,
                PhaseEnteredAt = PhaseEnteredAt,
                ElapsedSec = ElapsedSec,
                HasDepot = HasDepot,
                DepotLatDeg = DepotLatDeg,
                DepotLonDeg = DepotLonDeg,
                HasPickup = HasPickup,
                PickupLatDeg = PickupLatDeg,
                PickupLonDeg = PickupLonDeg,
                PickupCodeText = PickupCodeText,
                HasDrop = HasDrop,
                DropLatDeg = DropLatDeg,
                DropLonDeg = DropLonDeg,
                DropCodeText = DropCodeText,
                AbortReason = AbortReason,
                CodesRead = CodesRead,
                CodesRejected = CodesRejected,
                Timeouts = Timeouts,
                EmergencyStop = EmergencyStop,
                CodeNotSeen = CodeNotSeen,
                TimeStamp = TimeStamp,
            };
    }
}
