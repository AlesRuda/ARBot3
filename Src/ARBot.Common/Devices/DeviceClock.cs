using System;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Prevod casu ZARIZENI (citac v ms, ktery posila samo zarizeni s kazdym vzorkem) na cas
    /// aplikace (<see cref="ARBot.Common.Common.TimeBase"/>). Vznikl pro motorovou jednotku
    /// (<c>SDC2160Ex</c>, radek <c>T=</c>), ale na zarizeni nezavisi.
    ///
    /// <para><b>Proc.</b> Razitko z casu PRICHODU nese jitter linky: Roboteq posila po USB CDC
    /// v davkach, takze razitka mela vzor 12 / 12 / 9 ms, ackoli jednotka vzorkuje pravidelne
    /// po 11 ms. Rychlost <c>Δenkoder / Δrazitko</c> pak hlasila po kratkem intervalu o 33 % vic
    /// a EKF, ktery merenie drzi dopredu, z toho nadsadil drahu o ~1,9 %
    /// (<c>lok-fuze-poza-pred-koly</c>, doc/ekf-fusion.md). Cas zarizeni ten jitter nema.</para>
    ///
    /// <para><b>Jak.</b> Posun hodin <c>o = prichod − cas zarizeni</c> je skutecny posun plus
    /// latence linky, a latence je vzdy &gt;= 0 — nejlepsi odhad posunu je tedy <b>minimum</b>
    /// (vzorek s nejmensi latenci). Nove minimum se prevezme hned; jinak odhad smi stoupat
    /// nejvys rychlosti <see cref="MaxDriftPpm"/>, aby stacil driftu krystalu zarizeni (na ktery
    /// by ciste minimum nedosahlo, kdyby hodiny zarizeni sly pomaleji). Razitko vzorku je
    /// <c>cas zarizeni + posun</c>: interval mezi vzorky je tak interval ZARIZENI (presny), jen
    /// absolutni cas nese minimalni latenci linky (konstantu, jednotky ms).</para>
    ///
    /// <para><b>Resynchronizace.</b> Restart zarizeni (nebo jeho skriptu) vynuluje citac. Pozna
    /// se dvema zpusoby: cas zarizeni poskoci DOPREDU vic, nez mohl ubehnout (posun klesne
    /// o vic nez <see cref="ResyncThresholdMs"/>), nebo latence zustane nad prahem dele nez
    /// <see cref="ResyncPersistMs"/> casu aplikace. Zahlceni linky (aplikace chvili necte a pak
    /// dohani davku) dela vysokou latenci jen po dobu cteni davky, takze resync nespusti.</para>
    /// </summary>
    public sealed class DeviceClock
    {
        private bool synced;
        private long prevTick;
        private long deviceMs;          // rozbaleny cas zarizeni od synchronizace [ms]
        private long originTicks;       // cas aplikace odpovidajici deviceMs = 0 [DateTime.Ticks]
        private DateTime highLatencySince;
        private bool highLatency;

        /// <param name="modulus">Citac zarizeni bezi modulo tato hodnota [ms].</param>
        public DeviceClock(long modulus)
        {
            if (modulus <= 0) throw new ArgumentOutOfRangeException(nameof(modulus));
            Modulus = modulus;
        }

        /// <summary>Citac zarizeni bezi 0 .. Modulus−1 [ms].</summary>
        public long Modulus { get; }

        /// <summary>
        /// Nejvyssi rychlost, kterou smi odhad posunu stoupat [ppm]. Musi byt nad driftem krystalu
        /// zarizeni (bezne do ~100 ppm); vic znamena rychlejsi stoupani razitek mezi vzorky
        /// s minimalni latenci (500 ppm = 0,5 ms za sekundu).
        /// </summary>
        public double MaxDriftPpm { get; set; } = 500;

        /// <summary>Latence (nebo skok dopredu) nad tuto mez je podezrela [ms].</summary>
        public double ResyncThresholdMs { get; set; } = 1000;

        /// <summary>Jak dlouho smi vysoka latence trvat, nez se hodiny resynchronizuji [ms].</summary>
        public double ResyncPersistMs { get; set; } = 2000;

        /// <summary>Kolikrat se hodiny resynchronizovaly (vcetne prvni synchronizace).</summary>
        public int Syncs { get; private set; }

        /// <summary>Latence posledniho vzorku proti odhadu posunu [ms] (diagnostika).</summary>
        public double LastLatencyMs { get; private set; }

        /// <summary>Zapomene synchronizaci (dalsi vzorek zacina znovu).</summary>
        public void Reset() => synced = false;

        /// <summary>
        /// Zpracuje vzorek: citac zarizeni <paramref name="tick"/> [ms] prisel v case aplikace
        /// <paramref name="arrival"/>. Vrati razitko vzorku v case aplikace a interval ZARIZENI
        /// od predchoziho vzorku [ms] (<c>null</c> u prvniho vzorku a po resynchronizaci).
        /// Citac mimo rozsah 0 .. Modulus−1 vrati <c>null</c> a stav nemeni.
        /// </summary>
        public (DateTime Time, long? DeltaMs)? Map(long tick, DateTime arrival)
        {
            if (tick < 0 || tick >= Modulus)
                return null;

            if (!synced)
                return Sync(tick, arrival);

            long d = tick - prevTick;
            if (d < 0) d += Modulus;
            long dev = deviceMs + d;

            // Stoupani posunu omezene driftem: pocita se z intervalu ZARIZENI.
            long origin = originTicks + (long)(d * MaxDriftPpm / 100.0);   // d [ms] * ppm * 1e-6 * 1e4 ticku/ms
            long candidate = arrival.Ticks - dev * TimeSpan.TicksPerMillisecond;
            double latencyMs = (candidate - origin) / (double)TimeSpan.TicksPerMillisecond;

            // Cas zarizeni poskocil dopredu vic, nez mohl ubehnout: restart citace / nesmysl.
            if (latencyMs < -ResyncThresholdMs)
                return Sync(tick, arrival);

            // Vysoka latence: davka po zahlceni linky, nebo restart, po kterem citac jde
            // "pozadu". Dlouho trvajici = restart.
            if (latencyMs > ResyncThresholdMs)
            {
                if (!highLatency) { highLatency = true; highLatencySince = arrival; }
                else if ((arrival - highLatencySince).TotalMilliseconds > ResyncPersistMs)
                    return Sync(tick, arrival);
            }
            else highLatency = false;

            if (candidate < origin) { origin = candidate; latencyMs = 0; }

            originTicks = origin;
            deviceMs = dev;
            prevTick = tick;
            LastLatencyMs = latencyMs;
            return (new DateTime(origin + dev * TimeSpan.TicksPerMillisecond, arrival.Kind), d);
        }

        private (DateTime Time, long? DeltaMs) Sync(long tick, DateTime arrival)
        {
            synced = true;
            highLatency = false;
            prevTick = tick;
            deviceMs = 0;
            originTicks = arrival.Ticks;
            LastLatencyMs = 0;
            Syncs++;
            return (arrival, null);
        }
    }
}
