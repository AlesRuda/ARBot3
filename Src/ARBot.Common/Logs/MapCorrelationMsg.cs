using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: vysledek jednoho cyklu korelace occupancy gridu s mapou.
    /// Viz doc/map-correlation-localization.md.
    ///
    /// <para>Nese i pripad, kdy se NEKORIGOVALO - <see cref="Emitted"/> a <see cref="Reason"/>.
    /// Bez toho by v telemetrii nebylo videt, proc korekce chybi.</para>
    /// </summary>
    [Serializable()]
    public class MapCorrelationMsg : Message, IHasCaptureTime
    {
        /// <summary>Nalezeny posun na vychod [m]: skutecna poloha = odhad + Dx.</summary>
        public double Dx;
        /// <summary>Nalezeny posun na sever [m].</summary>
        public double Dy;
        /// <summary>Nalezena chyba kurzu [rad]: skutecny kurz = odhad + Phi.</summary>
        public double Phi;
        /// <summary>Skore shody v maximu (-1..1); zaroven metrika kvality.</summary>
        public double Score;
        /// <summary>Skore nejlepsiho VZDALENEHO konkurenta podel URCENE osy (test nejednoznacnosti).</summary>
        public double SecondBestScore;
        /// <summary>
        /// Skore nejlepsiho vzdaleneho konkurenta podel VOLNE (kolme) osy; <c>-inf</c>, kdyz se
        /// nemeril. Bez nej by pri <c>Reason = Ok</c> a vypnute volne ose nebylo poznat, jestli ji
        /// vynechal strop sigma (zdravy stav), nebo tenhle konkurent (priznak vady "falesna podelna
        /// jistota" - viz doc/map-correlation-localization.md).
        /// </summary>
        public double SecondBestScoreLoose;
        /// <summary>Sigma lepe urcene osy posunu [m].</summary>
        public double SigmaTight;
        /// <summary>Sigma hore urcene osy posunu [m].</summary>
        public double SigmaLoose;
        /// <summary>Smer lepe urcene osy [rad], matematicky.</summary>
        public double TightAxisAngle;
        /// <summary>Sigma kurzu [rad].</summary>
        public double SigmaPhi;
        /// <summary>Kolik bunek gridu vstoupilo do korelace.</summary>
        public int EvidenceCells;
        /// <summary>Kolik kandidatu se vyhodnotilo.</summary>
        public int Candidates;
        /// <summary>Poslala se do fuze aspon jedna korekce? (OR pres tri priznaky niz.)</summary>
        public bool Emitted;
        /// <summary>Poslala se korekce podel LEPE urcene osy? Na prime ceste bezny stav: true.</summary>
        public bool EmitTightAxis;
        /// <summary>
        /// Poslala se korekce podel HORE urcene osy? Na prime ceste bezny stav FALSE - podelna sigma
        /// prerostla strop. Bez tohoto priznaku by "poslalo se jen napric" bylo v telemetrii
        /// k nerozeznani od "poslalo se vsechno", a prave to je otazka, kterou se tenhle podsystem
        /// pri ladeni pta nejcasteji.
        /// </summary>
        public bool EmitLooseAxis;
        /// <summary>Poslala se korekce kurzu?</summary>
        public bool EmitHeading;
        /// <summary>Duvod (<c>ARBot.Common.Localization.MapCorrelationReason</c> jako byte).</summary>
        public byte Reason;
        /// <summary>Doba vypoctu cyklu [ms].</summary>
        public double ProcessingMs;
        /// <summary>Cas, ke kteremu vysledek plati (cas snapshotu gridu).</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public MapCorrelationMsg() : base("MapCorrelationMsg", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Dx);
            bw.Write(Dy);
            bw.Write(Phi);
            bw.Write(Score);
            bw.Write(SecondBestScore);
            bw.Write(SecondBestScoreLoose);
            bw.Write(SigmaTight);
            bw.Write(SigmaLoose);
            bw.Write(TightAxisAngle);
            bw.Write(SigmaPhi);
            bw.Write(EvidenceCells);
            bw.Write(Candidates);
            bw.Write(Emitted);
            bw.Write(EmitTightAxis);
            bw.Write(EmitLooseAxis);
            bw.Write(EmitHeading);
            bw.Write(Reason);
            bw.Write(ProcessingMs);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            Dx = br.ReadDouble();
            Dy = br.ReadDouble();
            Phi = br.ReadDouble();
            Score = br.ReadDouble();
            SecondBestScore = br.ReadDouble();
            SecondBestScoreLoose = br.ReadDouble();
            SigmaTight = br.ReadDouble();
            SigmaLoose = br.ReadDouble();
            TightAxisAngle = br.ReadDouble();
            SigmaPhi = br.ReadDouble();
            EvidenceCells = br.ReadInt32();
            Candidates = br.ReadInt32();
            Emitted = br.ReadBoolean();
            EmitTightAxis = br.ReadBoolean();
            EmitLooseAxis = br.ReadBoolean();
            EmitHeading = br.ReadBoolean();
            Reason = br.ReadByte();
            ProcessingMs = br.ReadDouble();
            TimeStamp = ReadDateTime(br);
        }

        public override Message Build() => new MapCorrelationMsg();
    }
}
