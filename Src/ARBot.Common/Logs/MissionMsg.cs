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
        /// <summary>
        /// Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).
        ///
        /// <para><b>Verze 2</b> (2026-08-26) pridala <b>nabidnuty cil</b> (<see cref="HasPending"/>
        /// a spol.) — tedy to, co obsluha vidi PRED potvrzenim. Bez toho by se do zaznamu nikdy
        /// nedostala <see cref="PendingRouteLengthM"/>: delku trasy pocita zkouska dosazitelnosti
        /// a nikde jinde v zaznamu neni, takze by pak neslo dohledat, na zaklade CEHO obsluha cil
        /// potvrdila (nebo nepotvrdila).</para>
        ///
        /// <para><b>Verze 3</b> (2026-08-26) pridala <b>kvalitu fixu</b> v depu
        /// (<see cref="HasFixInfo"/> a spol.). Bez ni je „ceka se na kvalitni fix"
        /// nediagnostikovatelne: mise stoji, panel neni schopen rict proc, a jediny zpusob, jak to
        /// zjistit, je precist si kod.</para>
        ///
        /// <para><b>Verze 4</b> (2026-08-26) pridala <b>duvod zamitnuti kodu</b>
        /// (<see cref="RejectReason"/>). Tri duvody (nesrozumitelny / prilis daleko / bez trasy) se
        /// z pohledu obsluhy chovaji stejne („nic se nestalo"), ale znamenaji uplne jine reseni —
        /// a bez nich to vypada, ze se kod vubec <i>neprecetl</i>.</para>
        /// </summary>
        public const int FormatVersion = 4;

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

        /// <summary>
        /// <b>Nabidnuty cil</b> (verze 2): precteny kod, ktery uz prosel strojovymi kontrolami a
        /// ceka na potvrzeni obsluhou. Presne to, co ma obsluha pred sebou, kdyz se rozhoduje —
        /// takze v zaznamu je pak videt, na zaklade ceho potvrdila (nebo nepotvrdila).
        /// </summary>
        public bool HasPending;
        /// <summary>Nabidnuty cil [stupne]; plati jen kdyz <see cref="HasPending"/>.</summary>
        public double PendingLatDeg, PendingLonDeg;
        /// <summary>Zdrojovy text nabidnuteho kodu, doslova.</summary>
        public string PendingCodeText;
        /// <summary>Vzdalenost nabidnuteho cile od depa [m] — proti <c>MaxTargetDistanceM</c>.</summary>
        public double PendingDistanceFromDepotM;
        /// <summary>
        /// Delka trasy na nabidnuty cil [m] ze zkousky dosazitelnosti; 0 = zkouska neprobehla
        /// (mise nema <c>IRouteProbe</c>). <b>Nikde jinde v zaznamu tenhle udaj neni.</b>
        /// </summary>
        public double PendingRouteLengthM;

        /// <summary>Citace: kolik kodu se precetlo, kolik se zamitlo, kolik timeoutu vyprselo.</summary>
        public int CodesRead, CodesRejected, Timeouts;

        /// <summary>Je prave aktivni nouzove zastaveni? (Aby bylo v zaznamu videt, na co mise ceka.)</summary>
        public bool EmergencyStop;

        /// <summary>Hlasi mise „kod nevidim"?</summary>
        public bool CodeNotSeen;

        /// <summary>
        /// <b>Kvalita fixu v depu</b> (verze 3) — proc se (ne)pokracuje z <c>ArmingAtDepot</c>.
        /// <c>false</c>, dokud zadny fix nedosel.
        /// </summary>
        public bool HasFixInfo;
        /// <summary>Splnuje posledni fix kriteria (druzice, HDOP, platnost)?</summary>
        public bool FixQualityOk;
        /// <summary>Druzice a HDOP posledniho fixu.</summary>
        public int FixSatellites;
        public double FixHdop;
        /// <summary>Kolik fixu je v okne, a jejich efektivni (RMS) rozptyl [m].</summary>
        public int FixSamples;
        public double FixSpreadM;
        /// <summary>Limit rozptylu [m] — aby zprava byla citelna bez znalosti konfigurace.</summary>
        public double FixSpreadLimitM;

        /// <summary>
        /// <b>Duvod zamitnuti posledniho kodu</b> (verze 4); prazdny, kdyz se nic nezamitlo.
        /// Prijaty kod ho <b>maze</b>, aby v UI nestrasil stary.
        /// </summary>
        public string RejectReason;
        /// <summary>Text kodu, ktery se zamitl — doslova, at je videt CO se zamitlo.</summary>
        public string RejectedCodeText;
        /// <summary>Vzdalenost zamitnuteho cile od depa [m]; 0, kdyz se nedal ani rozebrat.</summary>
        public double RejectedDistanceM;

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

            // Verze 2: nabidnuty cil.
            bw.Write(HasPending);
            bw.Write(PendingLatDeg);
            bw.Write(PendingLonDeg);
            bw.Write(PendingCodeText ?? string.Empty);
            bw.Write(PendingDistanceFromDepotM);
            bw.Write(PendingRouteLengthM);

            // Verze 3: kvalita fixu v depu.
            bw.Write(HasFixInfo);
            bw.Write(FixQualityOk);
            bw.Write(FixSatellites);
            bw.Write(FixHdop);
            bw.Write(FixSamples);
            bw.Write(FixSpreadM);
            bw.Write(FixSpreadLimitM);

            // Verze 4: duvod zamitnuti kodu.
            bw.Write(RejectReason ?? string.Empty);
            bw.Write(RejectedCodeText ?? string.Empty);
            bw.Write(RejectedDistanceM);
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

            // Verze 2 pridala nabidnuty cil. Starsi zaznamy ho nemaji - HasPending zustane false
            // a UI/rozbor musi priznat, ze co obsluha pred potvrzenim videla, uz nezjisti.
            if (Verze >= 2)
            {
                HasPending = br.ReadBoolean();
                PendingLatDeg = br.ReadDouble();
                PendingLonDeg = br.ReadDouble();
                PendingCodeText = br.ReadString();
                PendingDistanceFromDepotM = br.ReadDouble();
                PendingRouteLengthM = br.ReadDouble();
            }
            else
            {
                HasPending = false;
                PendingCodeText = string.Empty;
            }

            // Verze 3 pridala kvalitu fixu. Starsi zaznamy ji nemaji - pak nejde dohledat, proc
            // mise v ArmingAtDepot stala (presne ta situace, kvuli ktere ty udaje vznikly).
            if (Verze >= 3)
            {
                HasFixInfo = br.ReadBoolean();
                FixQualityOk = br.ReadBoolean();
                FixSatellites = br.ReadInt32();
                FixHdop = br.ReadDouble();
                FixSamples = br.ReadInt32();
                FixSpreadM = br.ReadDouble();
                FixSpreadLimitM = br.ReadDouble();
            }
            else HasFixInfo = false;

            // Verze 4 pridala duvod zamitnuti. Starsi zaznam ho nema - pak nejde dohledat, proc se
            // precteny kod neprijal (presne ta situace, kvuli ktere ten udaj vznikl).
            if (Verze >= 4)
            {
                RejectReason = br.ReadString();
                RejectedCodeText = br.ReadString();
                RejectedDistanceM = br.ReadDouble();
            }
            else
            {
                RejectReason = string.Empty;
                RejectedCodeText = string.Empty;
            }
        }

        public override Message Build() => new MissionMsg();

        public override string ToString()
            => $"MissionMsg faze={Phase} t={ElapsedSec:F0}s kody={CodesRead}/{CodesRejected}";
    }
}
