using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: <b>otaceni z kol nesouhlasi s gyrem</b> — stav hlidace prokluzu
    /// (<see cref="Diagnostics.WheelSlipMonitor"/>, tema <c>lok-fuze-rucni-otoceni</c>).
    ///
    /// <para><b>Kdy chodi:</b> pri zacatku epizody, jednou za sekundu, dokud trva, a pri jejim konci
    /// (<see cref="Level"/> = 0 s delkou a integraly cele epizody). Mimo prokluz nechodi.</para>
    ///
    /// <para><b>Nacpak:</b> do 11. 10. 2026 nesla kola 24 % integralu uhlove rychlosti fuze
    /// a prokluz nebo otoceni robotem rukou sly do kurzu (1. 10. 2026 −19°) — a nikdo to nehlasil.</para>
    /// </summary>
    [Serializable()]
    public class WheelSlipMsg : Message, IHasCaptureTime
    {
        /// <summary>Uroven (<see cref="Diagnostics.WheelSlipLevel"/> jako byte): 1 = prokluz trva,
        /// 0 = prokluz skoncil (koncova zprava).</summary>
        public byte Level;

        /// <summary>Druh (<see cref="Diagnostics.WheelSlipKind"/> jako byte): kola stoji / proti /
        /// kola vic nez robot / robot vic nez kola.</summary>
        public byte Kind;

        /// <summary>Prumerna uhlova rychlost z kol v poslednim okne [rad/s]; NaN = nevi se.</summary>
        public double OdoRate = double.NaN;

        /// <summary>Prumerna uhlova rychlost z gyra v poslednim okne [rad/s]; NaN = nevi se.</summary>
        public double GyroRate = double.NaN;

        /// <summary>Prumerna dopredna rychlost z kol v poslednim okne [m/s]; NaN = nevi se.</summary>
        public double Speed = double.NaN;

        /// <summary>Prah na rozdil prumeru v poslednim okne [rad/s].</summary>
        public double Threshold = double.NaN;

        /// <summary>Jak dlouho prokluz trva [s]; v koncove zprave delka cele epizody.</summary>
        public double EpisodeSec;

        /// <summary>Odhad zacatku epizody (cas dat; <c>default</c> mimo epizodu). <see cref="TimeStamp"/>
        /// koncove zpravy je okamzik, kdy se konec ohlasil, ne konec prokluzu.</summary>
        public DateTime EpisodeStart;

        /// <summary>Otoceni podle kol od zacatku epizody [rad].</summary>
        public double OdoAngle;

        /// <summary>Otoceni podle gyra od zacatku epizody [rad].</summary>
        public double GyroAngle;

        /// <summary>Lidsky popis (tentyz text jde na stranku nahledu).</summary>
        public string Text = string.Empty;

        /// <summary>Konec okna, ke kteremu stav plati (cas dat).</summary>
        public DateTime TimeStamp;

        /// <summary>Verze formatu serializace (viz doc/record-replay.md -> Verzovani zprav).</summary>
        public const int FormatVersion = 1;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public WheelSlipMsg() : base("WheelSlipMsg", FormatVersion)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Level);
            bw.Write(Kind);
            bw.Write(OdoRate);
            bw.Write(GyroRate);
            bw.Write(Speed);
            bw.Write(Threshold);
            bw.Write(EpisodeSec);
            Write(bw, EpisodeStart);
            bw.Write(OdoAngle);
            bw.Write(GyroAngle);
            bw.Write(Text ?? string.Empty);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            Level = br.ReadByte();
            Kind = br.ReadByte();
            OdoRate = br.ReadDouble();
            GyroRate = br.ReadDouble();
            Speed = br.ReadDouble();
            Threshold = br.ReadDouble();
            EpisodeSec = br.ReadDouble();
            EpisodeStart = ReadDateTime(br);
            OdoAngle = br.ReadDouble();
            GyroAngle = br.ReadDouble();
            Text = br.ReadString();
            TimeStamp = ReadDateTime(br);
        }

        public override Message Build() => new WheelSlipMsg();

        public override string ToString() => $"WheelSlipMsg L{Level} K{Kind} {EpisodeSec:F1}s {Text}";
    }
}
