using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: jeden precteny QR kod (viz doc/robotour-mission.md).
    ///
    /// <para><b>Text je DOSLOVA ten precteny</b> — i kdyz ho mise pozdeji zamitne. Po soutezi je
    /// tohle jediny zpusob, jak dohledat, co robot kdy precetl a proc se (ne)posunul.</para>
    /// </summary>
    [Serializable()]
    public class QrCodeMsg : Message, IHasCaptureTime
    {
        /// <summary>Verze formatu serializace (viz doc/record-replay.md → Verzovani zprav).</summary>
        public const int FormatVersion = 1;

        /// <summary>Kamera, ze ktere se cetlo.</summary>
        public string CameraName;

        /// <summary>Precteny text, doslova.</summary>
        public string Text;

        /// <summary>
        /// Nalezene body kodu v obraze [px]. QR ma tri polohove znacky, takze bodu je <b>3 nebo 4</b>,
        /// ne vzdy ctyri. Vstup pro vedeny otevreny ukol „vizualni dojezd na QR kod".
        /// </summary>
        public double[] CornersX, CornersY;

        /// <summary>Cas snimku, ve kterem se kod nasel.</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public QrCodeMsg() : base("QrCodeMsg", FormatVersion)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(CameraName ?? string.Empty);
            bw.Write(Text ?? string.Empty);

            int n = CornersX == null ? 0 : CornersX.Length;
            bw.Write(n);
            for (int i = 0; i < n; i++)
            {
                bw.Write(CornersX[i]);
                bw.Write(CornersY != null && i < CornersY.Length ? CornersY[i] : 0);
            }

            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            CameraName = br.ReadString();
            Text = br.ReadString();

            int n = br.ReadInt32();
            CornersX = new double[n];
            CornersY = new double[n];
            for (int i = 0; i < n; i++)
            {
                CornersX[i] = br.ReadDouble();
                CornersY[i] = br.ReadDouble();
            }

            TimeStamp = ReadDateTime(br);
        }

        public override Message Build() => new QrCodeMsg();

        public override string ToString() => $"QrCodeMsg {CameraName} '{Text}'";
    }
}
