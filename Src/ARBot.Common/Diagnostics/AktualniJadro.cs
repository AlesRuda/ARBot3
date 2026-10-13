using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// Cislo jadra CPU, na kterem vlakno PRAVE bezi (13. 10. 2026, <c>lp-vykon-retezu-na-arm</c>).
    ///
    /// <para>⚠️ <b>Ne <see cref="Thread.GetCurrentProcessorId"/>.</b> .NET ho na platformach, kde je dotaz
    /// na jadro drazsi nez cteni thread-static, vraci z mezipameti vlakna a obnovuje jen jednou za N volani
    /// (<c>ProcessorIdCache</c>, N az 5 000) - na Linuxu/ARM64 nejspis prave tak (sched_getcpu bez vDSO).
    /// Navigace se pta dvakrat za snimek, takze by cislo bylo zastarale presne po probuzeni vlakna, kdy
    /// ho potrebujeme. Na Linuxu se proto vola <c>sched_getcpu</c> z libc primo (presne v okamziku volani,
    /// desitky ns); na Windows je <see cref="Thread.GetCurrentProcessorId"/> presny (zmereno pri revizi).</para>
    /// </summary>
    public static class AktualniJadro
    {
        [DllImport("libc", EntryPoint = "sched_getcpu")]
        private static extern int sched_getcpu();

        private static readonly bool linux = OperatingSystem.IsLinux();
        private static volatile bool libcNejde;

        /// <summary>Cislo jadra (RK3588: 0-3 male A55, 4-7 velke A76); -1, kdyz ho nejde zjistit.</summary>
        public static int Cislo()
        {
            if (linux && !libcNejde)
            {
                try
                {
                    int c = sched_getcpu();
                    if (c >= 0) return c;
                }
                catch (DllNotFoundException) { libcNejde = true; }
                catch (EntryPointNotFoundException) { libcNejde = true; }
                return -1;
            }
            return linux ? -1 : Thread.GetCurrentProcessorId();
        }
    }
}
