using System;
using System.Threading;
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

        private long vycerpano, roztrzeno;

        /// <summary>Kolikrat se kopie nevesla - pool vycerpan (best-effort drop).</summary>
        public long ExhaustedDrops => Interlocked.Read(ref vycerpano);

        /// <summary>Kolikrat se kopie zahodila, protoze kamera zdrojovy slot behem kopirovani prepisovala
        /// (<see cref="CameraFrame.CaptureGeneration"/>).</summary>
        public long TornDrops => Interlocked.Read(ref roztrzeno);

        /// <summary>Pocet aktualne obsazenych slotu (diagnostika/testy).</summary>
        public int InUseCount
        {
            get { lock (gate) { int n = 0; for (int i = 0; i < inUse.Length; i++) if (inUse[i]) n++; return n; } }
        }

        /// <summary>
        /// Zkopiruje surova data <paramref name="src"/> do volneho slotu a vrati ho. Vraci <c>null</c>,
        /// kdyz je pool vycerpan (best-effort drop), kdyz kamera zdrojovy slot behem kopirovani
        /// prepisovala (<see cref="CameraFrame.CaptureGeneration"/> licha nebo zmenena - smes dvou zapisu se
        /// nevrati; kdyz kamera slot mezi predanim a kopii stihla CELY naplnit znovu, vrati se konzistentni
        /// novejsi snimek) nebo je <paramref name="src"/> null. Grid se predava referenci (viz trida).
        /// Kopie image bufferu znovupouziva existujici buffery slotu, pokud maji spravny rozmer.
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
            if (idx < 0) { Interlocked.Increment(ref vycerpano); return null; }   // vycerpano -> drop

            // Seqlock: verze pred kopii (acquire), kopie, bariera, verze po kopii. Licha = kamera prave
            // pise; jina po kopii = zacala psat behem kopirovani. Zapisuje jen vlakno kamery.
            int pred = Volatile.Read(ref src.CaptureGeneration);
            if ((pred & 1) == 0)
            {
                CopyInto(slots[idx], src);  // memcpy mimo zamek (slot je uz rezervovany)
                Interlocked.MemoryBarrier();
                if (Volatile.Read(ref src.CaptureGeneration) == pred)
                    return slots[idx];
            }
            Interlocked.Increment(ref roztrzeno);
            lock (gate) inUse[idx] = false;
            return null;
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

            dst.HasPose = src.HasPose;                       // od verze 6 - jinak by se poza ztratila
            dst.PoseAtCaptureX = src.PoseAtCaptureX;
            dst.PoseAtCaptureY = src.PoseAtCaptureY;
            dst.PoseAtCaptureTheta = src.PoseAtCaptureTheta;

            dst.ImageRGB = CopyImage(src.ImageRGB, dst.ImageRGB);
            dst.ImageDepth = CopyImage(src.ImageDepth, dst.ImageDepth);
            dst.ImageProbability = CopyImage(src.ImageProbability, dst.ImageProbability);

            dst.Grid = src.Grid;             // reference (grid je per-snimek cerstvy a nerecykluje se)
            dst.PathEdges = src.PathEdges;   // reference (stejny kontrakt jako Grid - cerstvy seznam per snimek)
            dst.Projection = src.Projection; // reference (popis projekce je per kamera nemenny)
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
    /// bez zamku. Kontrakt: kamera prepisuje az slot, ktery byl publikovan pred (N-1) grby - ctenar si
    /// mezitim stihl odnest svou (poolovanou) kopii pres <see cref="CameraFramePool"/>.
    /// Handoff "latest" resi <c>SensorBase.lastMeasurement</c> pod jeho zamkem.</para>
    ///
    /// <para><b>Vychozi pocet slotu 8, ne 3</b> (13. 10. 2026, <c>lp-navigator-zivy-slot-kamery</c>): pri
    /// 30 sn/s to je ~233 ms, nez kamera slot prepise, proti ~67 ms u triple bufferu. Duvod: nekteri
    /// odberatele (navigace do 13. 10. 2026, koridor, mise, QR) cetli ZIVY slot az ze sve fronty, takze
    /// v 2-23 % planu nesla navigace cas pozdejsiho snimku. Pameti je dost (~1,6 MB na slot a kameru).
    /// Prepis behem kopirovani hlida verze slotu <see cref="CameraFrame.CaptureGeneration"/>
    /// (<see cref="Next"/> ji nastavi na lichou, <see cref="Publish"/> na sudou).</para>
    /// </summary>
    public sealed class CaptureFramePool
    {
        /// <summary>Vychozi pocet slotu (viz trida).</summary>
        public const int DefaultCount = 8;

        private readonly CameraFrame[] slots;
        private int idx;

        /// <param name="count">Pocet slotu (>=2). Vychozi <see cref="DefaultCount"/>.</param>
        public CaptureFramePool(int count = DefaultCount)
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

            // Verze na LICHOU (zacina zapis) DRIV nez se slot zmeni; bariera, aby ji ctenar videl pred
            // novymi daty. Slot, ktery se nepublikoval (snimek preskocen), zustava lichy, jen s jinou hodnotou.
            int g = f.CaptureGeneration;
            Volatile.Write(ref f.CaptureGeneration, (g & 1) == 0 ? g + 1 : g + 2);
            Interlocked.MemoryBarrier();

            f.ImageRGB = wantRgb ? CameraFramePool.Ensure(f.ImageRGB, rgbW, rgbH) : null;
            f.ImageDepth = wantDepth ? CameraFramePool.Ensure(f.ImageDepth, depthW, depthH) : null;
            f.Grid = null;        // procesor spocte cerstvy grid
            f.PathEdges = null;   // procesor spocte cerstve hrany (jinak by ve slotu zustaly stare)
            f.Projection = null;  // procesor doplni popis projekce (dtto - jinak by zustal stary)
            f.HasPose = false;    // pozu doplni kamera; jinak by ve slotu zustala poza minuleho snimku
            return f;
        }

        /// <summary>
        /// Snimek ve slotu je hotovy (data, vize, poza i metadata) a predava se dal: verze na SUDOU (release -
        /// vsechny zapisy do slotu jsou videt drive nez ona). Vola ji <c>SensorBase.Process</c> po zapisu
        /// metadat, pod zamkem, pod kterym snimek zverejni - drivery ne. ⚠️ Bez toho zustane slot lichy
        /// a <see cref="CameraFramePool.Acquire"/> ho bude povazovat za rozepsany - kopie (navigace, zaznam,
        /// web) by se nevytvorila nikdy.
        /// </summary>
        public static void Publish(CameraFrame f)
        {
            if (f == null) return;
            int g = f.CaptureGeneration;
            if ((g & 1) == 1) Volatile.Write(ref f.CaptureGeneration, g + 1);
        }
    }
}
