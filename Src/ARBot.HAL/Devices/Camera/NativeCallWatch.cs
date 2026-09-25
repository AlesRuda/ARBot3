using System;
using System.Diagnostics;
using System.Threading;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// <b>Hlídač nativních volání RealSense</b> (<c>pipeline.Stop</c>, <c>Dispose</c>, <c>Start</c>,
    /// dotaz na zařízení). Volání se obalí <see cref="Guard"/>; když se do <see cref="LimitSec"/>
    /// nevrátí, jde do <see cref="Trace"/> hlášení <b>kde</b> vlákno kamery zatuhlo a runtime přes
    /// <see cref="OnHang"/> pořídí minidump (<c>HangWatchdog</c>).
    ///
    /// <para><b>Proč (24. 9. 2026):</b> v <c>records/test/20260923-143515.rec</c> levé D435 ve
    /// 14:40:53 zamrzla barva, driver ohlásil „restart pipeline" — a pak <b>už nikdy nic</b>: žádné
    /// připojení, žádná chyba dotazu, a supervizor zotavení nezasáhl, protože kamera o pomoc
    /// nežádala (<c>RecoveryNeeded</c> roste jen při neúspěšném dotazu, ke kterému se vlákno
    /// nedostalo). Pravá kamera jela dál do konce, levá byla mrtvá. Vlákno tedy zatuhlo v některém
    /// nativním volání — ve kterém, se ze záznamu zjistit nedá, a přesně to má hlídač příště říct.</para>
    ///
    /// <para><b>Nic neléčí:</b> zatuhlé nativní volání .NET přerušit neumí a recyklovat sdílený
    /// kontext pod ním by byl nativní pád. Zapíše jen důkaz; léčba je restart procesu.</para>
    ///
    /// <para>Je tady (v <c>ARBot.HAL</c>) a ne v runtime, protože driver runtime nevidí (směr
    /// závislostí). Minidump umí jen runtime, proto háček <see cref="OnHang"/>.</para>
    /// </summary>
    public static class NativeCallWatch
    {
        /// <summary>
        /// Limit [s]; ≤ 0 = hlídač vypnutý. Runtime ho nastaví z <c>hangwatch=</c> (výchozí 20 s):
        /// běžně trvají tahle volání desítky ms, restart pipeline do ~2 s.
        /// </summary>
        public static double LimitSec = 20;

        /// <summary>
        /// Co udělat při zatuhnutí navíc k hlášení (runtime: minidump). Argument je popis operace.
        /// Volá se z vlákna časovače, jednou za zatuhnutí.
        /// </summary>
        public static Action<string> OnHang;

        /// <summary>Kolikrát hlídač vystřelil — diagnostika a testy.</summary>
        public static int Hangs => hangs;
        private static int hangs;

        /// <summary>
        /// Ohraničí nativní volání. Token zahoď (Dispose) hned po návratu z volání.
        /// </summary>
        /// <param name="co">Kdo a co, např. <c>"Left 740112071040: pipeline.Stop"</c>.</param>
        public static IDisposable Guard(string co)
        {
            double limit = LimitSec;
            return limit > 0 ? new Token(co ?? "?", limit) : (IDisposable)Vypnuto.Instance;
        }

        private sealed class Vypnuto : IDisposable
        {
            public static readonly Vypnuto Instance = new Vypnuto();
            public void Dispose() { }
        }

        private sealed class Token : IDisposable
        {
            private readonly string co;
            private readonly double limit;
            private readonly Stopwatch bezi = Stopwatch.StartNew();
            private Timer casovac;
            private int vystrelil;

            public Token(string co, double limit)
            {
                this.co = co;
                this.limit = limit;
                // Jednorázový: stav zatuhlého vlákna se nemění, opakované hlášení by jen plnilo journal.
                casovac = new Timer(_ => Vystrel(), null, TimeSpan.FromSeconds(limit), Timeout.InfiniteTimeSpan);
            }

            private void Vystrel()
            {
                if (Interlocked.Exchange(ref vystrelil, 1) != 0) return;
                Interlocked.Increment(ref hangs);
                try
                {
                    // Trace, ne Debug: na zařízení běží Release (pravidlo v CLAUDE.md).
                    Trace.WriteLine($"NativeCallWatch: '{co}' se nevratilo do {limit:F0} s -> vlakno kamery "
                                    + "ZATUHLO v nativnim volani RealSense. Kamera se bez restartu procesu "
                                    + "neprobere a supervizor zotaveni o tom nevi (kamera o nic nezada).");
                    OnHang?.Invoke(co);
                }
                catch (Exception ex)
                {
                    // Diagnostika nesmí shodit proces.
                    Trace.WriteLine("NativeCallWatch: hlaseni zatuhnuti selhalo: " + ex.Message);
                }
            }

            public void Dispose()
            {
                var t = Interlocked.Exchange(ref casovac, null);
                if (t == null) return;
                t.Dispose();
                if (Volatile.Read(ref vystrelil) != 0)
                    Trace.WriteLine($"NativeCallWatch: '{co}' nakonec dobehlo po {bezi.Elapsed.TotalSeconds:F1} s.");
            }
        }
    }
}
