using ARBot.Common.Common;

namespace ARBot.Common.Vision.Qr
{
    /// <summary>
    /// Dekoder QR kodu ze sedeho obrazu. Viz doc/robotour-mission.md.
    ///
    /// <para>Rozhrani existuje proto, aby byla vymena dekoderu (nebo fallback, kdyby ten skutecny
    /// na zarizeni chybel) <b>lokalni</b> zmena — a aby si testy mohly dodat vlastni implementaci
    /// a nebyly zavisle na tom, co je na build stroji nainstalovane.</para>
    /// </summary>
    public interface IQrDecoder
    {
        /// <summary>
        /// Najde v obrazu QR kody. Vraci prazdne pole, kdyz nic nenajde — <b>nikdy null</b> a nikdy
        /// vyjimku kvuli obrazu, ve kterem kod neni (to je normalni, ocekavany stav).
        /// </summary>
        QrResult[] Decode(Image<Gray> img);
    }
}
