using System;
using System.Diagnostics;
using System.Threading;
using Intel.RealSense;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// <b>Jeden sdílený RealSense <see cref="Context"/> pro všechny drivery</b> (obě D435 i T265)
    /// a jediná fronta na dotazy <c>QueryDevices</c>.
    ///
    /// <para><b>Proč (3. 9. 2026, rozbor minidumpu z Orange Pi):</b> každý driver měl vlastní
    /// <see cref="Context"/> a periodicky volal <c>ctx.QueryDevices()</c>. V RSUSB backendu
    /// librealsense 2.53 ale <b>každý</b> <c>query_devices</c> volá <c>tm_boot</c>, který
    /// nenabootovanou T265 (Movidius <c>03e7:2150</c>) otevře a nahraje do ní firmware. Tři
    /// kontexty = tři konkurenční bootery téže kamery. Výsledek: SIGSEGV v <c>tm_boot</c>
    /// (<c>tm2/tm-boot.h:25</c>, <c>dev-&gt;open(0)</c> na zařízení, které mezi výčtem a otevřením
    /// zmizelo — právě se přehlašovalo na <c>8087:0b37</c>) a T265, která po zpackaném bootu
    /// nikdy nedala pózu (v záznamech jen 100 Hz <c>IMUState</c> z VN100). K tomu dotazy nad
    /// běžícími streamy padaly na „failed to set power state" (viděno už 1. 9.).</para>
    ///
    /// <para><b>Co to řeší:</b> (1) jeden kontext → jeden device watcher a jeden booter;
    /// (2) <see cref="Query"/> serializuje všechny dotazy zámkem, takže <c>tm_boot</c> nikdy neběží
    /// dvakrát naráz; (3) <see cref="BootT265"/> nabootuje T265 <b>jednou, synchronně, před
    /// startem D435</b> (volá konstruktor <c>T265TrackingCamera</c>, který <c>ARBotHW.SetRealHW</c>
    /// zakládá jako první kameru), takže boot neprobíhá pod rukama streamujícím pipeline.</para>
    ///
    /// <para><b>Co to neřeší:</b> race při fyzickém odpojení kamery přesně během dotazu — ten je
    /// v librealsense a náš zámek na něj nedosáhne. Diagnostika jde do <see cref="Trace"/>
    /// (viz hlavička <c>T265TrackingCamera</c>). <b>Ověřeno jen buildem, na zařízení ne.</b></para>
    /// </summary>
    internal static class RealSenseShared
    {
        private static readonly object gate = new object();
        private static Context context;

        /// <summary>Movidius před nahráním firmware — takhle se T265 hlásí po zapnutí napájení.</summary>
        private const string MovidiusName = "Movidius";

        /// <summary>Sdílený kontext (líně založený). Nikdy se nedisposuje — žije s procesem.</summary>
        public static Context Context
        {
            get
            {
                lock (gate)
                    return context ??= new Context();
            }
        }

        /// <summary>
        /// Zjistí, zda je mezi vyčtenými zařízeními takové, které splní <paramref name="match"/>.
        /// <b>Jediné místo, kudy se smí volat <c>QueryDevices</c>.</b>
        /// </summary>
        /// <returns>true = je; false = dotaz prošel a zařízení mezi vyčtenými není (opravdu chybí);
        /// <b>null = dotaz sám selhal</b> (typicky „failed to set power state" nad běžícími streamy) —
        /// to NENÍ důkaz, že zařízení chybí.</returns>
        public static bool? Query(string caller, Func<Device, bool> match)
        {
            try
            {
                lock (gate)
                {
                    using (var devices = Context.QueryDevices())
                    {
                        foreach (var d in devices)
                        {
                            using (d)
                            {
                                if (match(d))
                                    return true;
                            }
                        }
                    }
                }
                return false;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{caller}: QueryDevices selhalo: {ex.Message}");
                return null;
            }
        }

        /// <summary>Shoda podle sériového čísla, nebo (bez sériového čísla) podle podřetězce jména.</summary>
        public static Func<Device, bool> BySerialOrName(string sn, string namePart)
            => d =>
            {
                if (sn != null)
                    return d.Info[CameraInfo.SerialNumber] == sn;
                var name = d.Info[CameraInfo.Name];
                return name != null && name.IndexOf(namePart, StringComparison.OrdinalIgnoreCase) >= 0;
            };

        /// <summary>
        /// Pošle T265 <b>hardware reset</b> a počká, až se znovu vyčte (po resetu se hlásí jako
        /// Movidius a firmware do ní nahraje <c>tm_boot</c> v dalším dotazu; změřeno ~5 s).
        ///
        /// <para><b>K čemu:</b> T265, kterou klient opustil bez <c>Stop</c> (pád procesu, SIGPIPE),
        /// zůstane ve stavu „T265 is running!" a další <c>pipeline.Start</c> selže s „Device is
        /// busy"; a po zpackaném bootu nedává pózu, i když gyro/accel chodí. Obojí léčí jen reset
        /// (ověřeno sondou 3. 9. 2026). Volající si musí předem zbourat vlastní pipeline — reset
        /// zneplatní všechny handle na zařízení.</para>
        /// </summary>
        /// <returns>true = T265 je po resetu zpět a vyčtená.</returns>
        public static bool HardwareResetT265(string caller, TimeSpan timeout)
        {
            bool sent = false;
            try
            {
                lock (gate)
                {
                    using (var devices = Context.QueryDevices())
                    {
                        foreach (var d in devices)
                        {
                            using (d)
                            {
                                if (!BySerialOrName(null, "T265")(d)) continue;
                                d.HardwareReset();
                                sent = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Reset je asynchronni a zarizeni pri nem zmizi - libusb obcas vrati chybu
                // (EAGAIN) i kdyz reset prosel. Rozhodne az to, jestli se T265 vrati.
                Trace.WriteLine($"{caller}: hardware reset T265 - vyjimka pri odeslani: {ex.Message}");
            }
            if (!sent)
            {
                Trace.WriteLine($"{caller}: hardware reset T265 - zarizeni nenalezeno.");
                return false;
            }
            Trace.WriteLine($"{caller}: hardware reset T265 odeslan, cekam na navrat.");
            Thread.Sleep(1000);   // nechat zarizeni odpadnout ze sbernice, nez se zacne hledat
            return BootT265(caller, timeout);
        }

        /// <summary>
        /// Nabootuje T265 (je-li připojená jako nenabootovaný Movidius) a počká, až se přehlásí
        /// jako T265. Jeden dotaz <c>QueryDevices</c> boot spustí sám (librealsense v něm po
        /// nahrání firmware čeká 2 s a vyčte znovu); tady se to pod zámkem opakuje, dokud T265
        /// není vidět nebo nevyprší <paramref name="timeout"/>.
        /// </summary>
        /// <returns>true = T265 je vyčtená; false = není (chybí, nebo se do timeoutu nepřehlásila).</returns>
        public static bool BootT265(string caller, TimeSpan timeout)
        {
            var sw = Stopwatch.StartNew();
            bool sawMovidius = false;
            while (true)
            {
                bool? t265 = Query(caller, BySerialOrName(null, "T265"));
                if (t265 == true)
                {
                    if (sawMovidius)
                        Trace.WriteLine($"{caller}: T265 nabootovala za {sw.Elapsed.TotalSeconds:F1} s.");
                    return true;
                }

                bool? movidius = Query(caller, BySerialOrName(null, MovidiusName));
                if (movidius == false && t265 == false)
                    return false;                       // ani Movidius, ani T265 - kamera chybi
                if (movidius == true && !sawMovidius)
                {
                    sawMovidius = true;
                    Trace.WriteLine($"{caller}: nalezen nenabootovany Movidius - nahravam firmware T265.");
                }

                if (sw.Elapsed > timeout)
                {
                    Trace.WriteLine($"{caller}: T265 se do {timeout.TotalSeconds:F0} s nepřehlásila "
                                    + $"(Movidius videt: {movidius}, dotaz selhal: {movidius == null || t265 == null}).");
                    return false;
                }
                Thread.Sleep(500);
            }
        }
    }
}
