using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena (debug) zprava: lehky snimek merenia vlozeneho do EKF - zdroj, hodnota z,
    /// diagonala kovariance sumu R a vysledny NIS / prijeti gatingem. Slouzi jen k nahledu
    /// "co se vlozilo do filtru"; NE serializace samotnych IMeasurement trid.
    /// </summary>
    [Serializable()]
    public class MeasurementDiagMsg : Message, IHasCaptureTime, INamedMessage
    {
        /// <summary>Nazev zdroje merenia (napr. "VN100/heading").</summary>
        public string Source;
        /// <summary>Namerena hodnota z.</summary>
        public double[] Z;
        /// <summary>Diagonala kovariance sumu R.</summary>
        public double[] DiagR;
        /// <summary>NIS (normalized innovation squared) merenia.</summary>
        public double Nis;
        /// <summary>Zda bylo merenie prijato (neprijate = zahozene gatingem NEBO pozdni).</summary>
        public bool Accepted;
        /// <summary>Cas porizeni merenia.</summary>
        public DateTime TimeStamp;

        /// <summary>
        /// Verdikt fuze jako <see cref="ARBot.Common.Fusion.MeasurementVerdict"/> (verze 2).
        /// Samo <see cref="Accepted"/> nerozlisi „prislo pozde" od „zamitl gating" - a to jsou
        /// dva zcela jine problemy. Viz doc/map-correlation-localization.md.
        /// </summary>
        public byte Verdict;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        /// <summary>
        /// Jmeno instance = <see cref="Source"/>. Diky tomu jde v indexu zaznamu i v telemetrickem
        /// pohledu odlisit radky podle zdroje merenia (stejne jako "Left"/"Right" u kamer) — jinak
        /// by se korekce z korelace michaly s GPS v jednom sloupci.
        /// </summary>
        string INamedMessage.Name => Source;

        /// <summary>
        /// <para><b>Verze 2</b> (2026-08-21) pridala <see cref="Verdict"/>. Zaroven je to prvni
        /// verze, ktera se opravdu <b>publikuje</b> - do te doby byla zprava jen v katalogu
        /// a nikdo ji nevytvoril, takze zadny zaznam s verzi 1 realne neexistuje; cteni verze 1
        /// je tu jen pro poradek.</para>
        /// </summary>
        public MeasurementDiagMsg() : base("MeasurementDiagMsg", 2)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Source ?? string.Empty);
            WriteDoubles(bw, Z);
            WriteDoubles(bw, DiagR);
            bw.Write(Nis);
            bw.Write(Accepted);
            Write(bw, TimeStamp);
            bw.Write(Verdict);
        }

        public override void FromData(BinaryReader br)
        {
            Source = br.ReadString();
            Z = ReadDoubles(br);
            DiagR = ReadDoubles(br);
            Nis = br.ReadDouble();
            Accepted = br.ReadBoolean();
            TimeStamp = ReadDateTime(br);
            if (Verze < 2)
            {
                // Stary zaznam verdikt nenesl - dopocitat z Accepted, aby stara data nevypadala
                // jako "zahozeno pro stari" (to je jina diagnoza).
                Verdict = (byte)(Accepted ? Fusion.MeasurementVerdict.Accepted
                                          : Fusion.MeasurementVerdict.GatedOut);
                return;
            }
            Verdict = br.ReadByte();
        }

        /// <summary>Zapis ve formatu verze 1 - jen pro test cteni starych zaznamu.</summary>
        public void ToDataV1ForTest(BinaryWriter bw)
        {
            bw.Write(Source ?? string.Empty);
            WriteDoubles(bw, Z);
            WriteDoubles(bw, DiagR);
            bw.Write(Nis);
            bw.Write(Accepted);
            Write(bw, TimeStamp);
        }

        public override Message Build() => new MeasurementDiagMsg();

        private static void WriteDoubles(BinaryWriter bw, double[] a)
        {
            bw.Write((short)(a?.Length ?? 0));
            if (a != null)
                foreach (var v in a) bw.Write(v);
        }

        private static double[] ReadDoubles(BinaryReader br)
        {
            int n = br.ReadInt16();
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = br.ReadDouble();
            return a;
        }
    }
}
