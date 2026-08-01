using System;
using ARBot.Common.Common;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Pool <see cref="CameraFrame"/> kopii pro ASYNCHRONNI odberatele suroveho snimku (recorder vzdy,
    /// UI kdyz otevrene) - krok 4 z doc/plan-camera-vision-refactor.md. Kamera pooluje sve capture
    /// buffery (<see cref="CaptureFramePool"/>) a forwardnuty ramec drzi reference na ne; async
    /// odberatel je vsak cte az POZDEJI (na sve fronte/UI vlakne), takze si musi porizet STABILNI kopii,
    /// nez kamera buffer recykluje.
    ///
    /// <para><b>Kontrakt (potvrzen clovekem 2026-08-01):</b> kazdy async odberatel ma svuj maly pool.
    /// <see cref="Acquire"/> synchronne (na vlakne producenta = tik) zkopiruje SUROVA data zdrojoveho
    /// ramce do volneho slotu (znovupouzite image buffery -> 0 alokaci v ustalenem stavu) a vrati ho;
    /// pri vycerpani vrati <c>null</c> = <b>best-effort drop</b> (odberatel snimek proste vynecha, zadna
    /// alokace ani blokace). Po zpracovani odberatel slot vrati pres <see cref="Release"/>.</para>
    ///
    /// <para><b>Grid se NEkopiruje</b> - predava se referenci: <see cref="CameraFrame.Grid"/> je per-snimek
    /// cerstve alokovany (procesor v <c>BuildGrid</c> vytvari novou instanci) a kamera ho NErecykluje,
    /// takze je po dobu zivota snimku immutable a bezpecne sdilitelny. Recykluji se jen velke image
    /// buffery.</para>
    ///
    /// Thread-safe (kratky zamek jen kolem vyberu/uvolneni slotu; memcpy bezi mimo zamek).
    /// </summary>
    public sealed class CameraFramePool
    {
        private readonly CameraFrame[] slots;
        private readonly bool[] inUse;
        private readonly object gate = new object();

        /// <param name="capacity">Pocet slotu (kopii v obehu). Musi pokryt hloubku fronty odberatele;
        /// pri vycerpani se snimek zahodi (best-effort). Default 4.</param>
        public CameraFramePool(int capacity = 4)
        {
            if (capacity < 1) capacity = 1;
            slots = new CameraFrame[capacity];
            inUse = new bool[capacity];
            for (int i = 0; i < capacity; i++) slots[i] = new CameraFrame();
        }

        /// <summary>Kapacita poolu (pocet slotu).</summary>
        public int Capacity => slots.Length;

        /// <summary>Pocet aktualne obsazenych slotu (diagnostika/testy).</summary>
        public int InUseCount
        {
            get { lock (gate) { int n = 0; for (int i = 0; i < inUse.Length; i++) if (inUse[i]) n++; return n; } }
        }

        /// <summary>
        /// Zkopiruje surova data <paramref name="src"/> do volneho slotu a vrati ho. Vraci <c>null</c>,
        /// kdyz je pool vycerpan (best-effort drop) nebo je <paramref name="src"/> null. Grid se predava
        /// referenci (viz trida). Kopie image bufferu znovupouziva existujici buffery slotu, pokud maji
        /// spravny rozmer (jinak realokuje).
        /// </summary>
        public CameraFrame Acquire(CameraFrame src)
        {
            if (src == null) return null;

            int idx = -1;
            lock (gate)
            {
                for (int i = 0; i < inUse.Length; i++)
                    if (!inUse[i]) { inUse[i] = true; idx = i; break; }
            }
            if (idx < 0) return null;   // vycerpano -> drop

            CopyInto(slots[idx], src);  // memcpy mimo zamek (slot je uz rezervovany)
            return slots[idx];
        }

        /// <summary>
        /// Vrati slot ziskany pres <see cref="Acquire"/> zpet do poolu. Ignoruje (vraci false) snimek,
        /// ktery do tohoto poolu nepatri (napr. prehravany ramec ve View) nebo je uz volny.
        /// </summary>
        public bool Release(CameraFrame frame)
        {
            if (frame == null) return false;
            lock (gate)
            {
                for (int i = 0; i < slots.Length; i++)
                    if (ReferenceEquals(slots[i], frame))
                    {
                        if (!inUse[i]) return false;
                        inUse[i] = false;
                        return true;
                    }
            }
            return false;
        }

        /// <summary>Zkopiruje metadata + surova image data ze <paramref name="src"/> do <paramref name="dst"/> (grid referenci).</summary>
        private static void CopyInto(CameraFrame dst, CameraFrame src)
        {
            dst.Name = src.Name;
            dst.TimeStamp = src.TimeStamp;
            dst.RGBTimeStamp = src.RGBTimeStamp;
            dst.DepthTimeStamp = src.DepthTimeStamp;
            dst.FrameNum = src.FrameNum;
            dst.DropedOutNum = src.DropedOutNum;
            dst.FrameReceivePeriod = src.FrameReceivePeriod;
            dst.FramePickupPeriod = src.FramePickupPeriod;

            dst.ImageRGB = CopyImage(src.ImageRGB, dst.ImageRGB);
            dst.ImageDepth = CopyImage(src.ImageDepth, dst.ImageDepth);
            dst.ImageProbability = CopyImage(src.ImageProbability, dst.ImageProbability);

            dst.Grid = src.Grid;   // reference (grid je per-snimek cerstvy a nerecykluje se)
        }

        /// <summary>
        /// Hluboka kopie image dat s recyklaci ciloveho bufferu: kdyz <paramref name="reuse"/> ma spravny
        /// rozmer, prepise se jeho <c>Data</c> (bez alokace), jinak se alokuje novy. <c>null</c> zdroj -> null.
        /// </summary>
        public static Image<T> CopyImage<T>(Image<T> src, Image<T> reuse) where T : IPixel, new()
        {
            if (src == null) return null;
            var dst = (reuse != null && reuse.Width == src.Width && reuse.Height == src.Height)
                ? reuse
                : new Image<T>(src.Width, src.Height);
            Array.Copy(src.Data, dst.Data, src.Data.Length);
            return dst;
        }

        /// <summary>
        /// Zajisti image spravneho rozmeru: znovupouzije <paramref name="existing"/>, kdyz sedi rozmer,
        /// jinak alokuje novy. Sdileny helper pro capture i consumer pooling (data se plni jinde).
        /// </summary>
        public static Image<T> Ensure<T>(Image<T> existing, int width, int height) where T : IPixel, new()
        {
            if (existing != null && existing.Width == width && existing.Height == height) return existing;
            return new Image<T>(width, height);
        }
    }

    /// <summary>
    /// Triple-buffer capture pool pro vlakno kamery (krok 4): drzi N znovupouzitych <see cref="CameraFrame"/>
    /// slotu s recyklovanymi image buffery. <see cref="Next"/> vrati dalsi slot round-robin, takze kamera
    /// nikdy nealokuje image buffery per grab a ctenar (<c>ControlLoop</c> pull) ma vzdy stabilni "latest".
    ///
    /// <para><b>Pouziva JEN vlakno kamery</b> (grab+Process pisou do slotu, vyber slotu je round-robin) ->
    /// bez zamku. Kontrakt triple-bufferu: pri N&gt;=3 kamera prepisuje az slot, ktery byl publikovan pred
    /// (N-1) grby - ctenar si mezitim stihl odnest svou (poolovanou) kopii pres <see cref="CameraFramePool"/>.
    /// Handoff "latest" resi <c>SensorBase.lastMeasurement</c> pod jeho zamkem.</para>
    /// </summary>
    public sealed class CaptureFramePool
    {
        private readonly CameraFrame[] slots;
        private int idx;

        /// <param name="count">Pocet slotu (>=2). Default 3 = triple buffer (kamera neblokuje, ctenar ma stabilni snimek).</param>
        public CaptureFramePool(int count = 3)
        {
            if (count < 2) count = 2;
            slots = new CameraFrame[count];
            for (int i = 0; i < count; i++) slots[i] = new CameraFrame();
        }

        /// <summary>Pocet slotu.</summary>
        public int Count => slots.Length;

        /// <summary>
        /// Vrati dalsi slot (round-robin) s pripravenymi RGB/Depth buffery (recyklovanymi, kdyz sedi rozmer)
        /// a vynulovanym <see cref="CameraFrame.Grid"/>. <see cref="CameraFrame.ImageProbability"/> se
        /// ZACHOVA - procesor si ji per slot recykluje (kazdy slot drzi vlastni prob buffer, tim je i
        /// probability triple-bufferovana). Kamera pak do bufferu jen naleje data (bez alokace).
        /// </summary>
        public CameraFrame Next(bool wantRgb, int rgbW, int rgbH, bool wantDepth, int depthW, int depthH)
        {
            var f = slots[idx];
            idx = (idx + 1) % slots.Length;

            f.ImageRGB = wantRgb ? CameraFramePool.Ensure(f.ImageRGB, rgbW, rgbH) : null;
            f.ImageDepth = wantDepth ? CameraFramePool.Ensure(f.ImageDepth, depthW, depthH) : null;
            f.Grid = null;   // procesor spocte cerstvy grid
            return f;
        }
    }
}
