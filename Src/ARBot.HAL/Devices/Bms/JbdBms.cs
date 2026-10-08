using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;

namespace ARBot.HAL.Devices.Bms
{
    /// <summary>
    /// BMS JBD přes RS485 (převodník USB–RS485, 9600 Bd) — doc/plan-bms-jbd.md. Jednou za
    /// <c>perioda</c> pošle dotaz 0x03 a 0x04 a vydá <see cref="BmsState"/>; po chybě komunikace
    /// zprávu BEZ měření. <b>Jen čtení</b> - jiné bajty na port nepíše.
    ///
    /// <para>Protože po chybě vydává zprávu (ne null), hlídání ticha v <see cref="SensorBase{TState}"/>
    /// výpadek nevidí - proto vlastní <see cref="IsError"/> podle počtu chyb po sobě. Výpadek a obnova
    /// jdou do <c>Trace</c> jednou (přechod), jednotlivé chyby škrceně přes <see cref="PoruchaHlasic"/>.</para>
    /// </summary>
    public class JbdBms : UartSensorBase<BmsState>, IBms
    {
        /// <summary>Kolik nepovedených cyklů po sobě je porucha.</summary>
        public const int ChybPoSobeProPoruchu = 3;

        private readonly TimeSpan perioda;
        private readonly TimeSpan odpovedDo;
        private readonly Action<string> report;
        private readonly PoruchaHlasic hlasic = new PoruchaHlasic();
        private readonly List<byte> rx = new List<byte>(128);
        private readonly byte[] tmp = new byte[128];
        private DateTime dalsiDotaz = DateTime.MinValue;
        private volatile int chybPoSobe;
        private bool hlasenVypadek;

        /// <param name="uart">Port převodníku USB–RS485 (<see cref="JbdProtocol.BaudRate"/>).</param>
        /// <param name="perioda">Perioda dotazů; výchozí 1 s.</param>
        /// <param name="odpovedDo">Jak dlouho čekat na odpověď jednoho dotazu; výchozí 500 ms.</param>
        /// <param name="start">Spustit čtecí vlákno hned (test ho nechá stát a měří sám).</param>
        /// <param name="report">Kam hlásit výpadek a obnovu; null = <c>Trace.WriteLine</c>.</param>
        public JbdBms(IUart uart, TimeSpan? perioda = null, TimeSpan? odpovedDo = null,
                      bool start = true, Action<string> report = null)
            : base(uart)
        {
            this.perioda = perioda ?? TimeSpan.FromSeconds(1);
            this.odpovedDo = odpovedDo ?? TimeSpan.FromMilliseconds(500);
            this.report = report ?? (s => Trace.WriteLine(s));
            uart.ReadTimeout = 100;   // kratke cteni - odpoved hlida vlastni termin odpovedDo
            if (start) Start();
        }

        /// <inheritdoc/>
        public override string Name => "JbdBms";

        /// <inheritdoc/>
        public override bool IsError => base.IsError || chybPoSobe >= ChybPoSobeProPoruchu;

        /// <inheritdoc/>
        protected override BmsState GetMeasurement()
        {
            if (!PockejNaDalsiDotaz()) return null;

            var t = TimeBase.Now;
            var s = new BmsState { TimeStamp = t };
            byte[] d = Dotaz(JbdProtocol.RegBasic, out string chyba);
            if (d != null && JbdProtocol.TryParseBasic(d, s, out int clanku, out chyba))
            {
                // Stav nabiti, proud a ochrany uz jsou platne. Napeti clanku je navic: kdyz se
                // nepovede (jiny firmware muze 0x04 vratit v jine delce), zprava vyjde bez nich -
                // jinak by jedna nerozebratelna odpoved vypnula i varovani podle procent.
                d = Dotaz(JbdProtocol.RegCells, out string chybaClanku);
                if (d != null && JbdProtocol.TryParseCells(d, clanku, out var napeti, out chybaClanku))
                    s.CellVoltages = napeti;
                else if (!stopRequired)
                    hlasic.Hlas($"{Name}: clanky", $"{Name}: {chybaClanku} - zprava bez napeti clanku");
                if (stopRequired) return null;
                Uspech();
                return s;
            }

            if (stopRequired) return null;
            Selhani(chyba ?? "neznama chyba");
            return BmsState.NoMeasurement(t);
        }

        /// <summary>Počká na termín dalšího dotazu; false = žádost o zastavení.</summary>
        private bool PockejNaDalsiDotaz()
        {
            var now = TimeBase.Now;
            if (dalsiDotaz == DateTime.MinValue) dalsiDotaz = now;
            while (!stopRequired && TimeBase.Now < dalsiDotaz)
                Thread.Sleep(20);
            dalsiDotaz += perioda;
            if (dalsiDotaz < TimeBase.Now) dalsiDotaz = TimeBase.Now + perioda;   // nedohanet
            return !stopRequired;
        }

        /// <summary>Pošle dotaz a vrátí data odpovědi na TENTÝŽ registr, nebo null s důvodem.</summary>
        private byte[] Dotaz(byte reg, out string chyba)
        {
            chyba = null;
            rx.Clear();
            uart.Write(JbdProtocol.ReadRequest(reg));
            var konec = TimeBase.Now + odpovedDo;
            while (!stopRequired && TimeBase.Now < konec)
            {
                int n = uart.Read(tmp, 0, tmp.Length);
                if (n <= 0)
                {
                    Thread.Sleep(5);
                    continue;
                }
                for (int i = 0; i < n; i++) rx.Add(tmp[i]);

                while (true)
                {
                    var v = JbdProtocol.TryExtract(rx, out var ramec, out string ch);
                    if (v == JbdVysledek.MaloDat) break;
                    if (v == JbdVysledek.Chyba)
                    {
                        chyba = ch;
                        return null;
                    }
                    if (ramec.Reg == reg) return ramec.Data;
                    // Odpoved na jiny registr (opozdena z minuleho dotazu) - zahodit, cist dal.
                }
            }
            if (!stopRequired)
                chyba = $"BMS neodpovedela do {odpovedDo.TotalMilliseconds:0} ms (registr 0x{reg:X2})";
            return null;
        }

        private void Uspech()
        {
            if (hlasenVypadek)
            {
                report($"{Name}: BMS zase odpovida.");
                hlasenVypadek = false;
            }
            chybPoSobe = 0;
        }

        private void Selhani(string chyba)
        {
            hlasic.Hlas($"{Name}: {chyba}", $"{Name}: {chyba}");
            chybPoSobe++;
            if (chybPoSobe >= ChybPoSobeProPoruchu && !hlasenVypadek)
            {
                report($"{Name}: BMS neodpovida ({ChybPoSobeProPoruchu}x po sobe) - stav baterie neni znamy.");
                hlasenVypadek = true;
            }
        }
    }
}
