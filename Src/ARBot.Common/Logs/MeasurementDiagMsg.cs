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
    public class MeasurementDiagMsg : Message, IHasCaptureTime
    {
        /// <summary>Nazev zdroje merenia (napr. "VN100/heading").</summary>
        public string Source;
        /// <summary>Namerena hodnota z.</summary>
        public double[] Z;
        /// <summary>Diagonala kovariance sumu R.</summary>
        public double[] DiagR;
        /// <summary>NIS (normalized innovation squared) merenia.</summary>
        public double Nis;
        /// <summary>Zda bylo merenie prijato (neprijate = zahozene gatingem).</summary>
        public bool Accepted;
        /// <summary>Cas porizeni merenia.</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public MeasurementDiagMsg() : base("MeasurementDiagMsg", 1)
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
        }

        public override void FromData(BinaryReader br)
        {
            Source = br.ReadString();
            Z = ReadDoubles(br);
            DiagR = ReadDoubles(br);
            Nis = br.ReadDouble();
            Accepted = br.ReadBoolean();
            TimeStamp = ReadDateTime(br);
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
