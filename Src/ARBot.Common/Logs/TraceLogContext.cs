using System;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Doplnkovy kontext k zapisu do <see cref="System.Diagnostics.Trace"/> - uroven a oblast.
    ///
    /// <para><b>Proc:</b> <c>TraceListener</c> vidi jen hotovy retezec; kdo ho zapsal a s jakou
    /// zavaznosti uz ne. Producenti, kteri to vedi (typicky most z logovani Avalonie), si tedy
    /// kolem sveho <c>Trace.WriteLine</c> nastavi tenhle kontext a <see cref="TraceInfoBridge"/>
    /// z nej doplni pole <see cref="Info.Area"/> a <see cref="Info.Level"/>. Alternativa - vlepit
    /// uroven do textu a pak ji parsovat zpatky - by byla krehka.</para>
    ///
    /// <para>Kontext je <b>per vlakno</b> a plati jen po dobu <see cref="Scope"/>, protoze
    /// <c>Trace.WriteLine</c> se vola synchronne na vlakne producenta.</para>
    /// </summary>
    public static class TraceLogContext
    {
        [ThreadStatic] private static string area;
        [ThreadStatic] private static string level;

        /// <summary>Oblast pro zapisy z aktualniho vlakna; prazdne = neznamo.</summary>
        public static string Area => area ?? string.Empty;

        /// <summary>Uroven pro zapisy z aktualniho vlakna; prazdne = neznamo.</summary>
        public static string Level => level ?? string.Empty;

        /// <summary>
        /// Nastavi kontext do konce bloku <c>using</c>. Vnorene pouziti obnovi predchozi hodnoty.
        /// </summary>
        public static IDisposable Scope(string area, string level) => new Handle(area, level);

        private sealed class Handle : IDisposable
        {
            private readonly string prevArea;
            private readonly string prevLevel;

            public Handle(string a, string l)
            {
                prevArea = TraceLogContext.area;
                prevLevel = TraceLogContext.level;
                TraceLogContext.area = a;
                TraceLogContext.level = l;
            }

            public void Dispose()
            {
                TraceLogContext.area = prevArea;
                TraceLogContext.level = prevLevel;
            }
        }
    }
}
