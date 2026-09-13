namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Kamera, ktera umi rict, ze se <b>z vlastniho vypadku nedostane</b>, a umi uvolnit vsechny
    /// handle na zarizeni, aby slo pouzit tvrdsi zotaveni.
    ///
    /// <para><b>Proc to je (13. 9. 2026).</b> Po zbourani pipeline se na Orange Pi stava, ze kazdy
    /// dalsi dotaz na sbernici hodi „failed to set power state" a kamera se do konce behu
    /// nevzpamatuje (zmereno: jednou 343 s, jindy 22 minut). Zarizeni je pritom zdrave — jiny
    /// proces si ho zabere bez problemu — takze zaseknuty je vnitrni stav librealsense/libusb
    /// v NASEM procesu. Jedina znama lecba je zahodit sdileny kontext a zalozit novy, a to
    /// nejde udelat zevnitr driveru: kontext sdileji vsechny kamery, takze to musi nekdo
    /// <b>koordinovat</b> (a zastavit pritom robota). Viz doc/hardware.md a
    /// doc/plan-drive-hold.md.</para>
    ///
    /// <para>Rozhrani je v <c>ARBot.HAL</c>, aby na nej videl runtime bez reference na platformovy
    /// HAL — stejny duvod jako u <c>ICamera</c>.</para>
    /// </summary>
    public interface IRecoverableCamera
    {
        /// <summary>
        /// Kamera se <b>opakovane nedokaze pripojit</b> a trva to tak dlouho, ze uz to neni
        /// prechodny stav. Z tehohle se driver sam nedostane.
        ///
        /// <para>Zapocitavaji se <b>obe podoby zaseku</b> (zmereno 13. 9. 2026): dotaz na sbernici
        /// bud hodi „failed to set power state", <b>nebo projde a kameru nenajde</b> — druhe
        /// nastane, kdyz si zarizeni po teardownu vezme zpatky <c>uvcvideo</c>. Puvodne se pocitala
        /// jen prvni podoba, takze ta druha zotaveni NIKDY nespustila a kamera zustala mrtva.</para>
        ///
        /// <para>⚠️ <b>Od skutecne odpojene kamery to odlisuje jen to, ze uz nekdy bezela.</b>
        /// Implementace proto hlasi <c>true</c> az po prvnim uspesnem pripojeni — u chybejiciho
        /// kabelu by jinak zotaveni bralo dolu i zdrave kamery.</para>
        /// </summary>
        bool RecoveryNeeded { get; }

        /// <summary>Kolikrat po sobe se nepodarilo pripojit (diagnostika do logu).</summary>
        int FailedQueries { get; }

        /// <summary>
        /// <b>Pozada</b> kameru, aby uvolnila handle na zarizeni. Jen nastavi priznak - na
        /// zarizeni sahne az smycka driveru na SVEM vlakne.
        ///
        /// <para>⚠️ <b>Proc pres zadost a ne primo.</b> Prvni verze (13. 9. 2026) bourala pipeline
        /// primo z vlakna supervizora a <b>na zarizeni to zatuhlo</b>: vlakno kamery sedelo
        /// v <c>TryWaitForFrames</c> a soubezny <c>pipeline.Stop()</c> z ciziho vlakna se uz
        /// nevratil. Zustal drzeny <c>StopHold</c>, robot stal a spravil to az restart sluzby.
        /// Pipeline librealsense NENI bezpecna pro soubezny Stop a Wait.</para>
        /// </summary>
        void RequestRelease();

        /// <summary>
        /// Potvrzeni, ze kamera handle skutecne uvolnila a na kontext uz nesaha. Dokud tohle nehlasi
        /// <b>kazda</b> kamera, nesmi se se sdilenym kontextem hnout.
        /// </summary>
        bool Released { get; }

        /// <summary>
        /// Zrusi zadost a pusti smycku zpatky k pripojovani (uz z noveho kontextu). Vynuluje
        /// i citace poruchy - po zotaveni se zacina od zacatku, jinak by <see cref="RecoveryNeeded"/>
        /// zustalo hned znovu true a zotaveni by se spustilo dokola.
        /// </summary>
        void ResumeAfterRecovery();
    }
}
