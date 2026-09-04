using System;
using System.Diagnostics;
using System.Threading;
using ARBot.Common.Communication;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Most z <see cref="Trace"/> do proudu zprav: kazdy radek zapsany pres <c>Debug.WriteLine</c>
    /// / <c>Trace.WriteLine</c> se zabali do <see cref="Info"/> a posle na <see cref="Output"/>.
    /// Odtud jde do zaznamu, takze <b>debugovaci vystup je soucasti nahravky</b> a da se precist
    /// zpetne - i z behu na zarizeni, kde k oknu Debug output nikdo nesedi.
    /// Viz doc/record-replay.md.
    ///
    /// <para><b>Neblokuje producenta.</b> Zapis do logu se jen vlozi do fronty
    /// (<see cref="OverflowPolicy.DropOldest"/>) a odesle se z vlastniho vlakna. Logovat muze
    /// i ridici smycka nebo vlakno kamery a ty se nesmi zdrzet; pri zahlceni se radeji zahodi
    /// nejstarsi (pocitadlo <see cref="Dropped"/>).</para>
    ///
    /// <para><b>Nezacykli se.</b> Odberatele <see cref="Output"/> (zaznam, UI) samy loguji - bez
    /// ochrany vznikne smycka log -&gt; Info -&gt; odberatel -&gt; log. Brani se dvema zpusoby:
    /// <list type="number">
    /// <item>po dobu rozesilani je sber na tomtez vlakne potlaceny - to pokryva odberatele, kteri
    /// bezi synchronne (fan-out <c>RelaySource</c> bezi na vlakne volajiciho);</item>
    /// <item><b>strop poctu radku za sekundu</b> (<see cref="MaxPerSecond"/>) - odberatel s vlastni
    /// frontou loguje z JINEHO vlakna, kam thread-static ochrana nedosahne, takze smycka pres
    /// hranici vlakna se da utnout jen omezenim mnozstvi. Kolik se zahodilo, rekne souhrnny radek
    /// na zacatku dalsi sekundy.</item>
    /// </list>
    /// Bez stropu test <c>Odberatel_KteryLoguje_Nezacykli</c> vyrobil pres 24 000 zprav za 200 ms.</para>
    /// </summary>
    public sealed class TraceInfoBridge : MessageProcessor
    {
        /// <summary>Oblast pouzita, kdyz zapis neprisel s <see cref="TraceLogContext"/>
        /// (tedy bezny <c>Debug.WriteLine</c> z naseho kodu).</summary>
        public const string DefaultArea = "App";

        /// <summary>Uroven pouzita, kdyz zapis neprisel s <see cref="TraceLogContext"/>.</summary>
        public const string DefaultLevel = "Debug";

        // Potlaceni sberu na vlakne, ktere prave rozesila - viz ochrana proti zacykleni.
        [ThreadStatic] private static bool suppress;

        private readonly Listener listener;
        private readonly Func<DateTime> now;
        private readonly object rateLock = new object();
        private bool attached;

        // Okno rychlostniho stropu (viz MaxPerSecond).
        private long windowTicks;
        private int windowCount;
        private int windowDropped;

        /// <summary>Nejvyssi pocet radku za sekundu, ktery se pusti do proudu. Co je nad, se zahodi
        /// a nahradi souhrnnym radkem. Chrani pred smyckou log -&gt; Info -&gt; odberatel -&gt; log
        /// a pred zaplavou z cizich knihoven.</summary>
        public int MaxPerSecond { get; set; } = 200;

        /// <param name="capacity">Kapacita fronty; pri zaplneni se zahazuje nejstarsi.</param>
        /// <param name="clock">Zdroj casu; null = <see cref="ARBot.Common.Common.TimeBase.Now"/>
        /// (testy si ho podvrhnou). <b>TimeBase, ne <c>DateTime.Now</c>:</b> zpravy Info lezi
        /// v zaznamu vedle mereni, ktera jsou razitkovana z TimeBase, a seek podle casu i telemetrie
        /// je porovnavaji. Viz CLAUDE.md.</param>
        public TraceInfoBridge(int capacity = 512, Func<DateTime> clock = null)
            : base(OverflowPolicy.DropOldest, capacity)
        {
            now = clock ?? (() => ARBot.Common.Common.TimeBase.Now);
            listener = new Listener(this);
        }

        /// <summary>Zaregistruje se do <see cref="Trace.Listeners"/>. Opakovane volani nic nedela.</summary>
        public void Attach()
        {
            if (attached) return;
            Trace.Listeners.Add(listener);
            attached = true;
        }

        /// <summary>Odregistruje se z <see cref="Trace.Listeners"/>.</summary>
        public void Detach()
        {
            if (!attached) return;
            Trace.Listeners.Remove(listener);
            attached = false;
        }

        /// <inheritdoc/>
        public override void Stop()
        {
            Detach();
            base.Stop();
        }

        /// <summary>Zabali radek do <see cref="Info"/> a zaradi ho k odeslani.</summary>
        private void Capture(string text)
        {
            if (suppress || string.IsNullOrEmpty(text)) return;

            string area = TraceLogContext.Area is { Length: > 0 } a ? a : DefaultArea;
            string level = TraceLogContext.Level is { Length: > 0 } l ? l : DefaultLevel;

            if (!TryPassRateLimit(out int droppedInPrevWindow)) return;
            if (droppedInPrevWindow > 0)
                Post(Make($"TraceInfoBridge: zahozeno {droppedInPrevWindow} radku (strop "
                          + $"{MaxPerSecond}/s - zahlceni nebo smycka v logovani)", DefaultArea, "Warning"));

            // Post s DropOldest neblokuje; kdyz se nestiha, prijdeme o nejstarsi radky.
            Post(Make(text, area, level));
        }

        private Info Make(string text, string area, string level)
            => new Info(text) { TimeStamp = now(), Area = area, Level = level };

        /// <summary>
        /// Pusti radek, jen kdyz se v aktualni sekunde vejde do <see cref="MaxPerSecond"/>.
        /// Pri prechodu do noveho okna vrati, kolik se v tom predchozim zahodilo (pro souhrn).
        /// </summary>
        private bool TryPassRateLimit(out int droppedInPrevWindow)
        {
            droppedInPrevWindow = 0;
            long ticks = Environment.TickCount64;

            lock (rateLock)
            {
                if (ticks - windowTicks >= 1000)
                {
                    droppedInPrevWindow = windowDropped;
                    windowTicks = ticks;
                    windowCount = 0;
                    windowDropped = 0;
                }

                if (windowCount >= MaxPerSecond)
                {
                    windowDropped++;
                    return false;
                }
                windowCount++;
                return true;
            }
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Rozesilani je jedine misto, kde se smi logovat "dovnitr" - potlacit sber, jinak smycka.
            bool prev = suppress;
            suppress = true;
            try { EmitDerived(msg); }
            finally { suppress = prev; }
        }

        /// <summary>Naslouchac, ktery predava radky do <see cref="Capture"/>.</summary>
        private sealed class Listener : TraceListener
        {
            private readonly TraceInfoBridge owner;
            private readonly System.Text.StringBuilder partial = new System.Text.StringBuilder();

            public Listener(TraceInfoBridge owner) => this.owner = owner;

            /// <summary>Write bez konce radku - kouskuje se, odesle se az na WriteLine.</summary>
            public override void Write(string message)
            {
                if (string.IsNullOrEmpty(message)) return;
                lock (partial) partial.Append(message);
            }

            public override void WriteLine(string message)
            {
                string text;
                lock (partial)
                {
                    if (partial.Length > 0)
                    {
                        partial.Append(message);
                        text = partial.ToString();
                        partial.Clear();
                    }
                    else text = message;
                }
                owner.Capture(text);
            }
        }
    }
}
