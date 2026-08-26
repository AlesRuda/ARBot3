using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: stav mise Robotour (viz doc/robotour-mission.md).
    ///
    /// <para>Emituje se pri <b>kazde zmene faze</b> a periodicky — ve View se tak da prehrat cela
    /// mise. Bez ni je mise v zaznamu neviditelna a po soutezi nejde dohledat, proc se posunula
    /// (nebo neposunula) tam, kam se posunula.</para>
    ///
    /// <para><b>Zdrojove texty kodu jdou doslova</b>, takze je videt i to, co robot precetl a
    /// zamitl.</para>
    /// </summary>
    [Serializable()]
    public class MissionMsg : Message, IHasCaptureTime
    {
        /// <summary>Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).</summary>
        public const int FormatVersion = 1;

        /// <summary>Faze (<c>RobotourPhase</c> jako int, aby zprava prezila doplneni hodnot vyctu).</summary>
        public int Phase;

        /// <summary>Ktere zastaveni se obsluhuje (<c>RobotourStop</c> jako int); plati v servisnim okne.</summary>
        public int Stop;

        /// <summary>Cas vstupu do soucasne faze.</summary>
        public DateTime PhaseEnteredAt;

        /// <summary>Uplynuly cas mise od <c>Start</c> [s].</summary>
        public double ElapsedSec;

        /// <summary>Depo [stupne] — jediny cil, ktery robot nedostane z kodu.</summary>
        public double DepotLatDeg, DepotLonDeg;

        /// <summary>Je depo uz zapamatovane? (Nula je platna souradnice, proto vlastni priznak.)</summary>
        public bool HasDepot;

        /// <summary>Misto nakladky [stupne] a zdrojovy text jeho kodu.</summary>
        public double PickupLatDeg, PickupLonDeg;
        public bool HasPickup;
        public string PickupCodeText;

        /// <summary>Misto vykladky [stupne] a zdrojovy text jeho kodu.</summary>
        public double DropLatDeg, DropLonDeg;
        public bool HasDrop;
        public string DropCodeText;

        /// <summary>Duvod preruseni; prazdny, kdyz mise nebyla prerusena.</summary>
        public string AbortReason;

        /// <summary>Citace: kolik kodu se precetlo, kolik se zamitlo, kolik timeoutu vyprselo.</summary>
        public int CodesRead, CodesRejected, Timeouts;

        /// <summary>Je prave aktivni nouzove zastaveni? (Aby bylo v zaznamu videt, na co mise ceka.)</summary>
        public bool EmergencyStop;

        /// <summary>Hlasi mise „kod nevidim"?</summary>
        public bool CodeNotSeen;

        /// <summary>Cas, ke kteremu stav plati.</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public MissionMsg() : base("MissionMsg", FormatVersion)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Phase);
            bw.Write(Stop);
            Write(bw, PhaseEnteredAt);
            bw.Write(ElapsedSec);

            bw.Write(HasDepot);
            bw.Write(DepotLatDeg);
            bw.Write(DepotLonDeg);

            bw.Write(HasPickup);
            bw.Write(PickupLatDeg);
            bw.Write(PickupLonDeg);
            bw.Write(PickupCodeText ?? string.Empty);

            bw.Write(HasDrop);
            bw.Write(DropLatDeg);
            bw.Write(DropLonDeg);
            bw.Write(DropCodeText ?? string.Empty);

            bw.Write(AbortReason ?? string.Empty);
            bw.Write(CodesRead);
            bw.Write(CodesRejected);
            bw.Write(Timeouts);
            bw.Write(EmergencyStop);
            bw.Write(CodeNotSeen);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            Phase = br.ReadInt32();
            Stop = br.ReadInt32();
            PhaseEnteredAt = ReadDateTime(br);
            ElapsedSec = br.ReadDouble();

            HasDepot = br.ReadBoolean();
            DepotLatDeg = br.ReadDouble();
            DepotLonDeg = br.ReadDouble();

            HasPickup = br.ReadBoolean();
            PickupLatDeg = br.ReadDouble();
            PickupLonDeg = br.ReadDouble();
            PickupCodeText = br.ReadString();

            HasDrop = br.ReadBoolean();
            DropLatDeg = br.ReadDouble();
            DropLonDeg = br.ReadDouble();
            DropCodeText = br.ReadString();

            AbortReason = br.ReadString();
            CodesRead = br.ReadInt32();
            CodesRejected = br.ReadInt32();
            Timeouts = br.ReadInt32();
            EmergencyStop = br.ReadBoolean();
            CodeNotSeen = br.ReadBoolean();
            TimeStamp = ReadDateTime(br);
        }

        public override Message Build() => new MissionMsg();

        public override string ToString()
            => $"MissionMsg faze={Phase} t={ElapsedSec:F0}s kody={CodesRead}/{CodesRejected}";
    }
}
