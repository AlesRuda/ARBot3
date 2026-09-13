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
        /// <summary>
        /// Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).
        ///
        /// <para><b>Verze 2</b> (2026-09-12) pridala <see cref="AllLatitudes"/> /
        /// <see cref="AllLongitudes"/>, tedy <b>CELY seznam mist</b>, ne jen to, ktere se prave
        /// obsluhuje. Bez nej nesel na pudorys nakreslit objezd jako celek — a prave ten je pri
        /// dohledu nad zavodem potreba videt dopredu, ne az po bodech. Ve verzi 1 se cte prazdny
        /// seznam a kresli se jen aktualni misto.</para>
        /// </summary>
        public const int FormatVersion = 2;

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

        /// <summary>
        /// <b>Vsechna mista ze souboru</b> [rad], v poradi objezdu — sirky (verze 2).
        /// Prazdne pole = zprava je stara verze nebo seznam neni.
        ///
        /// <para><b>Surova</b> mista, ne prichycena: prichyceni se dela az pri jizde na ten bod
        /// (z aktualni polohy robota), takze pro zbytek seznamu zadne neexistuje. Rozdil je
        /// jednotky metru a merny udaj <see cref="OffRoadM"/> zprava nese zvlast.</para>
        ///
        /// <para>Nese se v KAZDE zprave, ne jen v prvni: odberatel „latest-wins" (webovy nahled)
        /// drzi posledni zpravu, takze seznam poslany jen jednou by pri prvni periodicke zprave
        /// zmizel.</para>
        /// </summary>
        public double[] AllLatitudes;

        /// <summary>Delky vsech mist ze souboru [rad]; stejna delka jako <see cref="AllLatitudes"/>.</summary>
        public double[] AllLongitudes;

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

            // Verze 2: cely seznam mist. Delka se pise vzdy (i nula), aby cteni nemuselo hadat.
            int pocet = AllLatitudes == null || AllLongitudes == null
                        ? 0 : Math.Min(AllLatitudes.Length, AllLongitudes.Length);
            bw.Write(pocet);
            for (int i = 0; i < pocet; i++)
            {
                bw.Write(AllLatitudes[i]);
                bw.Write(AllLongitudes[i]);
            }
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

            // Verze 1 (zaznamy do 12. 9. 2026) seznam nenesla - zustava prazdny, ne null,
            // aby ho volajici nemusel testovat na null i na delku.
            if (Verze < 2)
            {
                AllLatitudes = new double[0];
                AllLongitudes = new double[0];
                return;
            }

            int pocet = br.ReadInt32();
            AllLatitudes = new double[pocet];
            AllLongitudes = new double[pocet];
            for (int i = 0; i < pocet; i++)
            {
                AllLatitudes[i] = br.ReadDouble();
                AllLongitudes[i] = br.ReadDouble();
            }
        }
    }
}
