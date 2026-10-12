using System;
using System.Collections.Generic;
using System.Threading;
using ARBot.Common.Devices;
using ARBot.HAL;

namespace ARBot.Robot
{
    /// <summary>
    /// <b>Ceka na prvni snimek kterekoli kamery</b> po zotaveni (<see cref="CameraRecoverySupervisor"/>).
    ///
    /// <para><b>Proc (12. 10. 2026, prov-zotaveni-kamer-slepy-po-uvolneni, rozhodnuti autora).</b>
    /// Recyklace sdileneho kontextu RealSense odpoji VSECHNY kamery, i zdravou, a D435 se pak
    /// pripoji samy az za 8–16 s (zmereno nad 4 zotavenimi, <c>ARBot.Analyze zotaveni</c>). Drzene
    /// zastaveni se do te doby uvolnovalo hned po recyklaci, takze robot „smel jet", ale nemel jediny
    /// snimek: stranka ukazovala „jede", detektor zaseku globalni navigace byl ozbrojeny a starsi
    /// binarky pri tom tocily na miste. Drzi se proto, dokud nektera kamera neposle snimek.</para>
    ///
    /// <para><b>Pouziti:</b> prihlasit se k udalostem kamer (konstruktor) JESTE PRED zotavenim, po nem
    /// zavolat <see cref="Ostre"/> a <see cref="Cekej"/>. Snimky pred <see cref="Ostre"/> se nepocitaji -
    /// zdrava kamera posila do sveho uvolneni dal a ty snimky jsou ze stareho kontextu.</para>
    /// </summary>
    public sealed class PrvniSnimekKamery : IDisposable
    {
        private readonly List<ICamera> kamery = new List<ICamera>();
        private readonly ManualResetEventSlim prisel = new ManualResetEventSlim(false);
        private volatile bool ostre;
        private volatile string kamera;

        /// <param name="kamery">Kamery, jejichz snimek staci (<c>null</c> polozky se preskoci).</param>
        public PrvniSnimekKamery(IEnumerable<ICamera> kamery)
        {
            if (kamery == null) return;
            foreach (var k in kamery)
            {
                if (k == null) continue;
                k.MeasurementArived += Snimek;
                this.kamery.Add(k);
            }
        }

        /// <summary>Kolik kamer se hlida. Nula = neni na co cekat.</summary>
        public int PocetKamer => kamery.Count;

        /// <summary>Jmeno kamery, ktera poslala prvni pocitany snimek (<c>null</c> = zadna).</summary>
        public string Kamera => kamera;

        /// <summary>Od ted se snimky pocitaji.</summary>
        public void Ostre() => ostre = true;

        private void Snimek(object sender, CameraFrame f)
        {
            if (!ostre || f == null) return;
            if (kamera == null) kamera = (sender as ISensor)?.Name ?? f.Name;
            prisel.Set();
        }

        /// <summary>
        /// Pocka na prvni pocitany snimek. <c>true</c> = prisel; <c>false</c> = vyprsel
        /// <paramref name="timeout"/>, zrusilo se (<paramref name="zrusit"/>), nebo se nehlida zadna kamera.
        /// </summary>
        public bool Cekej(TimeSpan timeout, WaitHandle zrusit = null)
        {
            if (kamery.Count == 0) return false;
            var handles = zrusit != null ? new[] { prisel.WaitHandle, zrusit } : new[] { prisel.WaitHandle };
            return WaitHandle.WaitAny(handles, timeout) == 0;
        }

        /// <summary>
        /// Odhlasi se od kamer. <see cref="ManualResetEventSlim"/> se zamerne nedisposuje: udalost
        /// kamery muze dobehnout na jejim vlakne az po odhlaseni a <c>Set</c> na zrusenem objektu by
        /// hodil vyjimku do smycky driveru.
        /// </summary>
        public void Dispose()
        {
            ostre = false;
            foreach (var k in kamery)
                k.MeasurementArived -= Snimek;
            kamery.Clear();
        }
    }
}
