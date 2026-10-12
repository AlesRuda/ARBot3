using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Runtime;

namespace ARBot.Robot
{
    /// <summary>
    /// <b>Zotaveni zaseknutych kamer za jizdy</b>, koordinovane s rizenim.
    ///
    /// <para><b>Co lecí.</b> Po zbourani pipeline se na Orange Pi stava, ze kazdy dalsi dotaz na
    /// sbernici hodi „failed to set power state" a kamera se do konce behu nevzpamatuje (zmereno:
    /// jednou 343 s, jindy 22 minut; robot mezitim jel s polovinou zorneho pole). Zarizeni je
    /// pritom zdrave, zaseknuty je vnitrni stav librealsense v nasem procesu — jedina znama lecba
    /// je zahodit sdileny kontext a zalozit novy. Viz doc/hardware.md.</para>
    ///
    /// <para><b>Proc to musi nekdo koordinovat.</b> Kontext sdileji VSECHNY kamery, takze zotaveni
    /// osleppi celeho robota. Supervizor proto nejdriv vezme
    /// <see cref="StopHold"/>, pocka, az robot SKUTECNE stoji, a teprve pak sahne na kamery.
    /// Bezpecnost ale na tehle choreografii nevisi: kdyby supervizor selhal nebo timeoutoval,
    /// ridici smycka dobrzdi sama, jakmile plan zestarne (<c>Profile.PathControlTimeOut</c>).
    /// Viz doc/plan-drive-hold.md.</para>
    ///
    /// <para><b>Drzeni trva do prvniho snimku kterekoli kamery</b> (od 12. 10. 2026,
    /// prov-zotaveni-kamer-slepy-po-uvolneni, rozhodnuti autora), nejdele <see cref="PrvniSnimekTimeout"/>.
    /// Recyklace odpoji vsechny kamery a D435 se pak pripoji samy az za 8–16 s (zmereno nad
    /// 4 zotavenimi, <c>ARBot.Analyze zotaveni</c>; navrh pocital s „par sekundami"). Do te doby se
    /// drzeni uvolnovalo hned po recyklaci, takze robot „smel jet" bez jedineho snimku.</para>
    ///
    /// <para>✅ <b>Ze to na D435 funguje, je ZMERENE</b> (13. 9. 2026): osm zaseku z osmi vyleceno,
    /// od zamrznuti do obnovy 28–36 s — proti 343 s a 22 minutam v epizodach, kde to skoncilo az
    /// restartem sluzby. ⚠️ Merilo se ale na <b>stojicim</b> robotu; koordinace s dobrzdenim za
    /// jizdy je zatim jen z testu.</para>
    ///
    /// <para>⚠️ <b>Na kazdou poruchu to ale nestaci.</b> T265 po zpackanem bootu se pripoji, ale
    /// nedava pozu — a to recyklace kontextu nelecí. Proto se to po
    /// <see cref="MaxZotaveniVOkne"/> zotavenich u dane kamery vzda: kazde zotaveni oslepi
    /// i ostatni kamery a zastavi robota, takze jet bez ni je mensi zlo.</para>
    /// </summary>
    public sealed class CameraRecoverySupervisor : IDisposable
    {
        private readonly ARBotHW hw;
        private readonly Func<IDriveHold> holdSource;
        private readonly Thread vlakno;
        private readonly ManualResetEventSlim konec = new ManualResetEventSlim(false);
        private readonly object gate = new object();

        /// <summary>Jak casto se kouka, jestli neni potreba zotaveni.</summary>
        private static readonly TimeSpan Perioda = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Jak dlouho se ceka, az robot po vzeti holdu skutecne zastavi. Dobrzdeni z plne rychlosti
        /// trva ~2,5 s; 10 s je tedy volne. <b>Po vyprseni se zotaveni udela stejne</b> —
        /// rozjeta kamera je dulezitejsi nez jistota, ze robot stoji, a rizeni uz je stejne pod
        /// holdem (tedy brzdi). Vypise se to ale jako varovani.
        /// </summary>
        private static readonly TimeSpan StaniTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Jak dlouho se po zotaveni drzi robot, nez nektera kamera posle prvni snimek. Zmereno
        /// 8–17 s od recyklace (12. 10. 2026). <b>Po vyprseni se drzeni uvolni stejne</b>: bez snimku
        /// lokalni navigace nenaplanuje drahu a robot stoji dal, kdezto supervizor zaseknuty v cekani
        /// by nemohl zkusit dalsi zotaveni. Vypise se to jako varovani.
        /// </summary>
        public static readonly TimeSpan PrvniSnimekTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Nejkratsi odstup mezi dvema zotavenimi. Kdyby recyklace kontextu nepomahala, bez tohohle
        /// by se robot zastavoval kazdou vterinu dokola — tedy horsi chovani nez porucha sama.
        /// </summary>
        private static readonly TimeSpan MinOdstup = TimeSpan.FromSeconds(60);

        /// <summary>
        /// Jak dlouhe okno se sleduje u <see cref="MaxZotaveniVOkne"/>.
        /// </summary>
        private static readonly TimeSpan OknoHistorie = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Kolik zotaveni v okne <see cref="OknoHistorie"/> uz znamena, ze to u dane kamery
        /// nema smysl zkouset dal.
        ///
        /// <para>⚠️ <b>Tohle je TRETI pokus o tohle kriterium a predchozi dva byly obejitelne</b>
        /// (obojí zmereno na robotu 13. 9. 2026):</para>
        /// <list type="number">
        ///   <item>„dalsi zadost do 3 minut" — <b>backoff si ten odstup sam natahl</b> nad prah,
        ///     takze se citac pokazde vynuloval (24 zotaveni za hodinu, vzdani nikdy).</item>
        ///   <item>„chytla se mezitim aspon na chvili" — T265 se po kazdem zotaveni na ~7 s
        ///     <b>skutecne pripoji</b> a teprve pak zjisti, ze nedava pozu. Tedy zase reset.</item>
        /// </list>
        ///
        /// <para>Proto se ted nemeri „pomohlo/nepomohlo", ale <b>primo to, co nam vadi</b>: jak
        /// casto kvuli te kamere jde cely robot dolu. Kriterium, ktere popisuje skodu, nejde
        /// obejit tim, jak se porucha chova.</para>
        /// </summary>
        private const int MaxZotaveniVOkne = 3;

        /// <summary>Kdy si o zotaveni rekla ktera kamera (posledni <see cref="OknoHistorie"/>).</summary>
        private readonly Dictionary<string, List<DateTime>> historie
            = new Dictionary<string, List<DateTime>>(StringComparer.Ordinal);

        /// <summary>Kamery, u kterych uz se zotaveni nezkousi (dokud se samy nechytnou).</summary>
        private readonly HashSet<string> vzdane = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Od kdy je vzdana kamera nepretrzite v poradku (nezada o zotaveni).</summary>
        private readonly Dictionary<string, DateTime> zdravaOd = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        /// <summary>
        /// Jak dlouho musi byt vzdana kamera v poradku, nez se na ni zotaveni zase zacne vztahovat.
        ///
        /// <para>⚠️ <b>Kratky okamzik nestaci:</b> T265 se po kazdem restartu vlastni pipeline na
        /// ~7 s pripoji a teprve pak zjisti, ze nedava pozu. Bez teto prodlevy by se vzdani zrusilo
        /// pri kazdem takovem zablesknuti a cely kolotoc by se rozjel znovu.</para>
        /// </summary>
        private static readonly TimeSpan ObnovaPo = TimeSpan.FromMinutes(2);

        private DateTime posledniZotaveni = DateTime.MinValue;
        private volatile bool zadostRucne;

        /// <summary>Kolikrat uz zotaveni probehlo (diagnostika na stranku a do logu).</summary>
        public int Recoveries { get; private set; }

        /// <param name="holdSource">Zdroj <see cref="IDriveHold"/> (ridici smycka). Smycka vznika
        /// az se spustenim mise a muze se vymenit, proto funkce, ne hodnota. <c>null</c> = zotaveni
        /// probehne BEZ zastaveni robota (a rekne to).</param>
        public CameraRecoverySupervisor(ARBotHW hw, Func<IDriveHold> holdSource)
        {
            this.hw = hw ?? throw new ArgumentNullException(nameof(hw));
            this.holdSource = holdSource;

            vlakno = new Thread(Smycka) { IsBackground = true, Name = "CameraRecovery" };
            vlakno.Start();
        }

        /// <summary>
        /// Vynuti zotaveni pri nejblizsim pruchodu, i kdyz zadna kamera o nej nezada a bez ohledu
        /// na <see cref="MinOdstup"/>. Pouziva to tlacitko na strance nahledu — clovek u robota
        /// vidi vic nez citac selhanych dotazu.
        /// </summary>
        public void RequestRecovery() => zadostRucne = true;

        private void Smycka()
        {
            while (!konec.Wait(Perioda))
            {
                try
                {
                    bool rucne = zadostRucne;
                    var zadaji = hw.CamerasNeedingRecovery();

                    // Vzdana kamera se vraci do hry, az je NEPRETRZITE v poradku - viz ObnovaPo.
                    foreach (var jmeno in new List<string>(vzdane))
                    {
                        if (zadaji.Contains(jmeno)) { zdravaOd.Remove(jmeno); continue; }

                        if (!zdravaOd.TryGetValue(jmeno, out var od))
                        {
                            zdravaOd[jmeno] = TimeBase.Now;
                            continue;
                        }
                        if (TimeBase.Now - od < ObnovaPo) continue;

                        vzdane.Remove(jmeno);
                        historie.Remove(jmeno);
                        zdravaOd.Remove(jmeno);
                        Trace.WriteLine($"CameraRecovery: {jmeno} je {ObnovaPo.TotalMinutes:F0} min "
                                        + "v poradku - zotaveni pro ni zase plati.");
                    }

                    // Zadosti od vzdanych kamer se ignoruji: zotaveni jim nepomaha a pritom
                    // oslepi i ty zdrave.
                    var ziveZadosti = zadaji.FindAll(j => !vzdane.Contains(j));
                    if (!rucne && ziveZadosti.Count == 0) continue;
                    if (!rucne && TimeBase.Now - posledniZotaveni < MinOdstup) continue;

                    // Vzdat to u kamery, kvuli ktere uz robot sel dolu prilis casto. NEmeri se
                    // „pomohlo/nepomohlo" (obe takova kriteria sla obejit - viz MaxZotaveniVOkne),
                    // ale primo cetnost skody. Rucni zadost se nepocita, za tu odpovida clovek.
                    if (!rucne)
                    {
                        foreach (var jmeno in ziveZadosti)
                        {
                            if (!historie.TryGetValue(jmeno, out var kdy))
                                historie[jmeno] = kdy = new List<DateTime>();
                            kdy.RemoveAll(t => TimeBase.Now - t > OknoHistorie);
                            if (kdy.Count + 1 < MaxZotaveniVOkne) continue;

                            vzdane.Add(jmeno);
                            Trace.WriteLine($"CameraRecovery: {jmeno} si vyzadala {kdy.Count + 1} "
                                            + $"zotaveni za {OknoHistorie.TotalMinutes:F0} min - dal to "
                                            + "za ni zkouset nebudu. Kazde zotaveni oslepi i ostatni "
                                            + "kamery a zastavi robota, takze jet bez ni je mensi zlo. "
                                            + "Tohle uz softwarem nespravim: CHCE TO FYZICKY ODPOJIT "
                                            + "A ZAPOJIT KAMERU. Az se chytne, zotaveni pro ni zase "
                                            + "zacne platit.");
                        }

                        ziveZadosti = ziveZadosti.FindAll(j => !vzdane.Contains(j));
                        if (ziveZadosti.Count == 0) continue;   // zbyly uz jen vzdane

                        foreach (var jmeno in ziveZadosti)
                            historie[jmeno].Add(TimeBase.Now);
                    }

                    zadostRucne = false;
                    bool zabralo = Zotav(rucne
                        ? "zotaveni kamer (rucne ze stranky)"
                        : $"zotaveni kamer ({string.Join(", ", ziveZadosti)})");

                    // Rucni zadost, ktera prisla BEHEM zotaveni, se zahodi - ale jen kdyz zotaveni
                    // zabralo (prisel snimek): dalsi by hned strhlo kamery, ktere se zrovna pripojily
                    // (od 12. 10. 2026 trva drzeni do prvniho snimku, celkem 10-25 s, a obsluha muze
                    // kliknout znovu). Kdyz se kamery nevratily, klik plati a zotaveni probehne znovu.
                    if (zadostRucne && zabralo)
                    {
                        zadostRucne = false;
                        Trace.WriteLine("CameraRecovery: rucni zadost behem zotaveni, ktere zabralo, se ignoruje.");
                    }
                }
                catch (Exception ex)
                {
                    // Supervizor nesmi spadnout - bez nej by kamera zustala mrtva do restartu sluzby.
                    Trace.WriteLine("CameraRecovery: pruchod selhal: " + ex.Message);
                }
            }
        }

        /// <returns><c>true</c> = po zotaveni prisel snimek nektere kamery (zabralo).</returns>
        private bool Zotav(string duvod)
        {
            lock (gate)
            {
                posledniZotaveni = TimeBase.Now;
                Recoveries++;

                var hold = holdSource?.Invoke();
                if (hold == null)
                {
                    // Bez ridici smycky (robot ceka na misi) neni co zastavovat - a je to v poradku.
                    Trace.WriteLine($"CameraRecovery: {duvod} - ridici smycka nebezi, "
                                    + "zotaveni jde rovnou.");
                    hw.RecoverCameras("CameraRecovery");
                    posledniZotaveni = TimeBase.Now;
                    return false;
                }

                if (!ZotavPodDrzenim(hold, duvod, hw.DepthCameras(), () => hw.RecoverCameras("CameraRecovery"),
                                     konec.WaitHandle, StaniTimeout, PrvniSnimekTimeout, out bool prisel))
                    return false;   // konec behu

                // MinOdstup se meri od KONCE zotaveni: od 12. 10. 2026 drzeni trva do prvniho snimku
                // (celkem 10-25 s, nejdele ~45 s), a mereno od zacatku by z 60 s volne jizdy zbylo malo.
                posledniZotaveni = TimeBase.Now;
                Trace.WriteLine($"CameraRecovery: hotovo (celkem {Recoveries}x), drzeni uvolneno.");
                return prisel;
            }
        }

        /// <summary>
        /// <b>Jadro zotaveni pod drzenym zastavenim</b>, vytazene kvuli testum - poradi kroku je
        /// bezpecnostni: (1) vezme hold, (2) prihlasi se k udalostem snimku <paramref name="kamery"/>,
        /// (3) pocka na stani (nejdele <paramref name="staniTimeout"/>), (4) zavola
        /// <paramref name="zotav"/>, (5) teprve POTOM zacne snimky pocitat a drzi, dokud nektera kamera
        /// neposle snimek (nejdele <paramref name="snimekTimeout"/>). Pocitat snimky uz pred (4) by
        /// pustilo drzeni hned - zdrava kamera posila ze stareho kontextu az do sveho uvolneni.
        /// </summary>
        /// <returns><c>false</c> = preruseno (<paramref name="konec"/>); hold se v obou pripadech uvolni.</returns>
        /// <param name="snimekPrisel"><c>true</c> = po zotaveni prisel snimek nektere kamery.</param>
        public static bool ZotavPodDrzenim(IDriveHold hold, string duvod, IEnumerable<ARBot.HAL.ICamera> kamery,
                                           Action zotav, WaitHandle konec, TimeSpan staniTimeout, TimeSpan snimekTimeout,
                                           out bool snimekPrisel)
        {
            snimekPrisel = false;
            using (var drzeni = hold.StopRequest(duvod))
            using (var snimek = new PrvniSnimekKamery(kamery))   // prihlasit PRED zotavenim
            {
                var doKdy = TimeBase.Now + staniTimeout;
                while (!drzeni.IsStopped && TimeBase.Now < doKdy)
                    if (konec.WaitOne(TimeSpan.FromMilliseconds(100))) return false;

                if (!drzeni.IsStopped)
                    Trace.WriteLine($"CameraRecovery: robot se do {staniTimeout.TotalSeconds:F0} s "
                                    + "nezastavil (nebo motory nehlasi) - zotaveni jde presto, "
                                    + "rizeni uz je pod drzenim.");
                else
                    Trace.WriteLine("CameraRecovery: robot stoji, jde se na zotaveni kamer.");

                // Vyjimka ze zotaveni (typicky novy kontext RealSense nejde zalozit) NESMI preskocit cekani
                // na snimek: RecoverCameras pusti kamery k pripojovani ve finally, takze by drzeni padlo
                // hned a robot by byl slepy mimo drzeni - presne to, co tahle metoda odstranuje (nalez
                // revize 12. 10. 2026). Proto zalogovat a cekat dal.
                try { zotav(); }
                catch (Exception ex)
                {
                    Trace.WriteLine($"CameraRecovery: zotaveni vyhodilo vyjimku, na snimek se presto ceka: {ex.Message}");
                }

                // Drzet dal, dokud nektera kamera neposle snimek z noveho kontextu - viz hlavicka tridy.
                snimek.Ostre();
                var odZotaveni = TimeBase.Now;
                bool prisel = snimek.Cekej(snimekTimeout, konec);
                if (konec.WaitOne(0)) return false;
                snimekPrisel = prisel;
                if (prisel)
                    Trace.WriteLine($"CameraRecovery: prvni snimek po zotaveni za "
                                    + $"{(TimeBase.Now - odZotaveni).TotalSeconds:F1} s ({snimek.Kamera}) "
                                    + "- drzeni se uvolnuje.");
                else if (snimek.PocetKamer == 0)
                    Trace.WriteLine("CameraRecovery: zadna hloubkova kamera neni - na snimek se neceka.");
                else
                    Trace.WriteLine($"CameraRecovery: zadna kamera do {snimekTimeout.TotalSeconds:F0} s "
                                    + "po zotaveni neposlala snimek - drzeni se uvolnuje presto. Bez snimku "
                                    + "lokalni navigace nenaplanuje drahu a robot stoji dal, ale UZ NE pod drzenim "
                                    + "(stranka ukaze „jede“, detektor zaseku globalni navigace se ozbroji).");
                return true;
            }
        }

        public void Dispose()
        {
            konec.Set();
            bool dobehlo = false;
            try { dobehlo = vlakno.Join(TimeSpan.FromSeconds(2)); } catch (Exception ex) { Debug.WriteLine(ex); }
            // ⚠️ Udalost se rusi JEN kdyz vlakno dobehlo. Jinak muze byt jeste uvnitr zotaveni
            // (RecoverCameras ceka az 5 s na uvolneni kamer) a pri navratu sahne na konec.WaitHandle
            // nebo konec.Wait - na zrusenem objektu to hodi vyjimku mimo try ve smycce, tedy
            // neosetrenou vyjimku na vlakne a pad procesu (nalez revize 12. 10. 2026; stara vada,
            // nova cekani na snimek pridala jen dalsi misto). Neuvolneny event si vezme GC.
            if (dobehlo) konec.Dispose();
        }
    }
        /// <summary>Kamery, u kterych uz se zotaveni nezkousi (dokud se samy nechytnou).</summary>
}