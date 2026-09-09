using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// <b>Stav mise Track</b> — kterym mistem ze seznamu se robot prave zabyva. Viz
    /// doc/track-mission.md.
    ///
    /// <para><b>Proc vlastni zprava a ne <see cref="MissionMsg"/>:</b> ta je robotourovska
    /// (depo, QR kod, nakladka) a rozsirovat ji o cizi pole by znamenalo, ze polovina zpravy je
    /// vzdycky prazdna a nikdo nevi, ktera. FreeRun ma z tehoz duvodu
    /// <see cref="FreeRunMsg"/>.</para>
    ///
    /// <para><b>Nese SUROVY i PRICHYCENY cil</b>, protoze bez obou se nedá vylozit, kam robot
    /// vlastne jel: v souboru je misto, ktere si clovek klikl na mape, ale jede se na jeho
    /// kolmy prumet na sit cest. <see cref="OffRoadM"/> je rozdil mezi nimi — a je to ten udaj,
    /// podle ktereho lze <c>TrackConfig.MaxPointOffRoadM</c> nastavit z dat misto usudkem.</para>
    /// </summary>
    [Serializable()]
    public class TrackMsg : Message, IHasCaptureTime
    {
        /// <summary>Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).</summary>
        public const int FormatVersion = 1;

        /// <summary>Faze mise (<c>TrackPhase</c> jako int, aby zprava prezila doplneni hodnot).</summary>
        public int Phase;

        /// <summary>Index mista, ke kteremu se jede (od nuly); <c>-1</c> = zadne.</summary>
        public int PointIndex;

        /// <summary>Kolik mist seznam obsahuje.</summary>
        public int PointCount;

        /// <summary>Kolikate kolo se jede (od 1); pri <c>repeat</c> roste.</summary>
        public int Lap;

        /// <summary>Ma se po poslednim bode zacit znovu od prvniho?</summary>
        public bool Repeat;

        /// <summary>Kolik mist se uz objelo celkem (pres vsechna kola).</summary>
        public int Reached;

        /// <summary>Zemepisna sirka mista ze SOUBORU [rad].</summary>
        public double RawLatitude;

        /// <summary>Zemepisna delka mista ze SOUBORU [rad].</summary>
        public double RawLongitude;

        /// <summary>Zemepisna sirka cile PRICHYCENEHO na sit [rad]; nula, kdyz se neprichycovalo.</summary>
        public double TargetLatitude;

        /// <summary>Zemepisna delka cile PRICHYCENEHO na sit [rad]; nula, kdyz se neprichycovalo.</summary>
        public double TargetLongitude;

        /// <summary>Jak daleko lezel bod ze souboru od site cest [m] — tedy o kolik se prichycenim posunul.</summary>
        public double OffRoadM;

        /// <summary>Delka nalezene trasy na aktualni cil [m]; nula, kdyz se nezkousela.</summary>
        public double RouteLengthM;

        /// <summary>Duvod preruseni; prazdny, kdyz mise prerusena nebyla.</summary>
        public string AbortReason;

        /// <summary>Jak dlouho mise bezi [s] (z hodin DAT, ne stroje).</summary>
        public double ElapsedSec;

        /// <summary>Cas, ke kteremu stav plati (hodiny DAT).</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public TrackMsg() : base("TrackMsg", FormatVersion) { }

        /// <inheritdoc/>
        public override Message Build() => new TrackMsg();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Phase);
            bw.Write(PointIndex);
            bw.Write(PointCount);
            bw.Write(Lap);
            bw.Write(Repeat);
            bw.Write(Reached);
            bw.Write(RawLatitude);
            bw.Write(RawLongitude);
            bw.Write(TargetLatitude);
            bw.Write(TargetLongitude);
            bw.Write(OffRoadM);
            bw.Write(RouteLengthM);
            bw.Write(AbortReason ?? string.Empty);
            bw.Write(ElapsedSec);
            Write(bw, TimeStamp);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            Phase = br.ReadInt32();
            PointIndex = br.ReadInt32();
            PointCount = br.ReadInt32();
            Lap = br.ReadInt32();
            Repeat = br.ReadBoolean();
            Reached = br.ReadInt32();
            RawLatitude = br.ReadDouble();
            RawLongitude = br.ReadDouble();
            TargetLatitude = br.ReadDouble();
            TargetLongitude = br.ReadDouble();
            OffRoadM = br.ReadDouble();
            RouteLengthM = br.ReadDouble();
            AbortReason = br.ReadString();
            ElapsedSec = br.ReadDouble();
            TimeStamp = ReadDateTime(br);
        }
    }
}
