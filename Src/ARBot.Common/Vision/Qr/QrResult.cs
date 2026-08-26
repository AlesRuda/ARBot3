using System;
using ARBot.Common.Common;

namespace ARBot.Common.Vision.Qr
{
    /// <summary>
    /// Jeden precteny QR kod. Viz doc/robotour-mission.md.
    ///
    /// <para><b>Text jde DOSLOVA</b> az do zpravy a zaznamu — i kdyz ho mise pozdeji zamitne. Bez
    /// toho by po soutezi nebylo dohledatelne, co robot skutecne precetl.</para>
    /// </summary>
    public sealed class QrResult
    {
        /// <param name="text">Precteny text, doslova.</param>
        /// <param name="corners">Nalezene body kodu v obraze [px]; QR jich ma 3 nebo 4.</param>
        public QrResult(string text, Point2D[] corners)
        {
            Text = text ?? string.Empty;
            Corners = corners ?? Array.Empty<Point2D>();
        }

        /// <summary>Precteny text, doslova.</summary>
        public string Text { get; }

        /// <summary>
        /// Nalezene body kodu v obraze [px]. QR ma tri polohove znacky, takze bodu je typicky 3
        /// nebo 4 — <b>ne vzdy ctyri</b>.
        ///
        /// <para>Vedou se proto, ze z nich jde odvodit smer i vzdalenost kodu — je to vstup pro
        /// vedeny otevreny ukol „vizualni dojezd na QR kod".</para>
        /// </summary>
        public Point2D[] Corners { get; }

        /// <summary>Kamera, ze ktere se cetlo.</summary>
        public string CameraName { get; set; }

        /// <summary>Cas snimku, ve kterem se kod nasel.</summary>
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// Prevod na log-zpravu. Konverzi vlastni domena (viz CLAUDE.md) — <c>Logs</c> zustava
        /// pasivni DTO.
        /// </summary>
        public Logs.QrCodeMsg ToLogMessage()
        {
            var x = new double[Corners.Length];
            var y = new double[Corners.Length];
            for (int i = 0; i < Corners.Length; i++)
            {
                x[i] = Corners[i].X;
                y[i] = Corners[i].Y;
            }

            return new Logs.QrCodeMsg
            {
                CameraName = CameraName,
                Text = Text,
                CornersX = x,
                CornersY = y,
                TimeStamp = TimeStamp,
            };
        }
    }
}
