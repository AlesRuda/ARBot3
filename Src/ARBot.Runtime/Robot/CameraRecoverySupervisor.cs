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
    /// na par sekund osleppi celeho robota. Supervizor proto nejdriv vezme
    /// <see cref="StopHold"/>, pocka, az robot SKUTECNE stoji, a teprve pak sahne na kamery.
    /// Bezpecnost ale na tehle choreografii nevisi: kdyby supervizor selhal nebo timeoutoval,
    /// ridici smycka dobrzdi sama, jakmile plan zestarne (<c>Profile.PathControlTimeOut</c>).
    /// Viz doc/plan-drive-hold.md.</para>
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
                    Zotav(rucne
                        ? "zotaveni kamer (rucne ze stranky)"
                        : $"zotaveni kamer ({string.Join(", ", ziveZadosti)})");
                }
                catch (Exception ex)
                {
                    // Supervizor nesmi spadnout - bez nej by kamera zustala mrtva do restartu sluzby.
                    Trace.WriteLine("CameraRecovery: pruchod selhal: " + ex.Message);
                }
            }
        }

        private void Zotav(string duvod)
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
                    return;
                }

                using (var drzeni = hold.StopRequest(duvod))
                {
                    var doKdy = TimeBase.Now + StaniTimeout;
                    while (!drzeni.IsStopped && TimeBase.Now < doKdy)
                        if (konec.Wait(TimeSpan.FromMilliseconds(100))) return;

                    if (!drzeni.IsStopped)
                        Trace.WriteLine($"CameraRecovery: robot se do {StaniTimeout.TotalSeconds:F0} s "
                                        + "nezastavil (nebo motory nehlasi) - zotaveni jde presto, "
                                        + "rizeni uz je pod drzenim.");
                    else
                        Trace.WriteLine("CameraRecovery: robot stoji, jde se na zotaveni kamer.");

                    hw.RecoverCameras("CameraRecovery");
                }

                Trace.WriteLine($"CameraRecovery: hotovo (celkem {Recoveries}x), drzeni uvolneno. "
                                + "Kamery se pripoji samy z noveho kontextu.");
            }
        }

        public void Dispose()
        {
            konec.Set();
            try { vlakno.Join(TimeSpan.FromSeconds(2)); } catch (Exception ex) { Debug.WriteLine(ex); }
            konec.Dispose();
        }
    }
        /// <summary>Kamery, u kterych uz se zotaveni nezkousi (dokud se samy nechytnou).</summary>
}