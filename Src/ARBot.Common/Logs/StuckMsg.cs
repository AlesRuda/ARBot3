using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: <b>robot stoji pri jizde</b> — stav hlidace uvaznuti
    /// (<see cref="Diagnostics.StuckMonitor"/>, tema <c>nav-uvaznuti-neohlasene</c>).
    ///
    /// <para><b>Kdy chodi:</b> pri kazde zmene urovne (vcetne konce stani, kdy nese
    /// <see cref="Level"/> = 0) a jednou za sekundu, dokud robot stoji dele nez prah. Mimo stani
    /// nechodi — zaznam z ni proto rekne, KDY a PROC robot stal, aniz by se nafoukl.</para>
    ///
    /// <para><b>Nacpak:</b> do 8. 10. 2026 uvaznuti neohlasil nikdo — globalni navigace i mise
    /// hlasily „jede" a robot stal do zasahu obsluhy (1. 10. 2026 132 + 42 s v <c>RobotBlocked</c>,
    /// 18. 9. ~6 min v <c>AlreadyAtGoal</c>, Kolo4 268 s v <c>GoalUnsafe</c>).</para>
    /// </summary>
    [Serializable()]
    public class StuckMsg : Message, IHasCaptureTime
    {
        /// <summary>Uroven (<see cref="Diagnostics.StuckLevel"/> jako byte): 0 = nestoji / stani skoncilo,
        /// 1 = stoji (muze se vyresit samo), 2 = uvazl (ceka na zasah obsluhy).</summary>
        public byte Level;

        /// <summary>
        /// Jak dlouho zpatky robot neujel 0,5 m [s] — klouzave okno (i v koncove zprave je to okno
        /// v okamziku konce, tedy kratke). Delka cele epizody je <see cref="EpisodeSec"/>.
        /// </summary>
        public double StandingSec;

        /// <summary>
        /// Jak dlouho trva cele stani od jeho zacatku [s]; v koncove zprave (<see cref="Level"/> = 0)
        /// jeho celkova delka. Lisi se od <see cref="StandingSec"/> pri plizeni a pri pomalem rozjezdu.
        /// </summary>
        public double EpisodeSec;

        /// <summary>Pricina (<see cref="Diagnostics.StuckCause"/> jako byte).</summary>
        public byte Cause;

        /// <summary>Stav posledniho lokalniho planu (<c>LocalPlanStatus</c> jako int); -1 = zadny.</summary>
        public int PlanStatus = -1;

        /// <summary>Jak stary je posledni lokalni plan [s]; NaN = zadny nebyl.</summary>
        public double PlanAgeSec = double.NaN;

        /// <summary>Vzdalenost k mrkvi [m] z posledniho planu; NaN = nevi se.</summary>
        public double GoalDistanceM = double.NaN;

        /// <summary>Cim je blokovana bunka pod robotem (<c>CellBlockReason</c> jako byte).</summary>
        public byte StartBlock;

        /// <summary>Odstup bunky pod robotem od prekazky [m]; NaN = nevi se.</summary>
        public double StartClearanceM = double.NaN;

        /// <summary>Posledni povel dopredne rychlosti ridici smycky [m/s]; NaN = nevi se.</summary>
        public double CommandSpeed = double.NaN;

        /// <summary>Lidsky popis (tentyz text jde na stranku nahledu a do Trace).</summary>
        public string Text = string.Empty;

        /// <summary>Cas, ke kteremu stav plati (cas dat - takt ridici smycky).</summary>
        public DateTime TimeStamp;

        /// <summary>Verze formatu serializace (viz doc/record-replay.md -> Verzovani zprav).</summary>
        public const int FormatVersion = 1;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public StuckMsg() : base("StuckMsg", FormatVersion)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Level);
            bw.Write(StandingSec);
            bw.Write(EpisodeSec);
            bw.Write(Cause);
            bw.Write(PlanStatus);
            bw.Write(PlanAgeSec);
            bw.Write(GoalDistanceM);
            bw.Write(StartBlock);
            bw.Write(StartClearanceM);
            bw.Write(CommandSpeed);
            bw.Write(Text ?? string.Empty);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            Level = br.ReadByte();
            StandingSec = br.ReadDouble();
            EpisodeSec = br.ReadDouble();
            Cause = br.ReadByte();
            PlanStatus = br.ReadInt32();
            PlanAgeSec = br.ReadDouble();
            GoalDistanceM = br.ReadDouble();
            StartBlock = br.ReadByte();
            StartClearanceM = br.ReadDouble();
            CommandSpeed = br.ReadDouble();
            Text = br.ReadString();
            TimeStamp = ReadDateTime(br);
        }

        public override Message Build() => new StuckMsg();

        public override string ToString() => $"StuckMsg L{Level} {StandingSec:F0}s {Text}";
    }
}
