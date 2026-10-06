using System;
using System.Collections.Generic;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Common.Diagnostics
{
    /// <summary>Stav baterie podle <see cref="BatteryMonitor"/>.</summary>
    public enum BatteryLevel
    {
        /// <summary>Zadne cerstve mereni (motory nehlasi, nebo hlasi jen zastupne ramce).</summary>
        Unknown = 0,
        /// <summary>Napeti nad prahem varovani (nebo je varovani vypnute).</summary>
        Ok = 1,
        /// <summary>Napeti pod prahem varovani - nabit.</summary>
        Low = 2,
    }

    /// <summary>Okamzity odecet: medián napeti [V] (NaN = neznamo) a stav.</summary>
    public readonly struct BatteryReading
    {
        public BatteryReading(double volts, BatteryLevel level) { Volts = volts; Level = level; }

        /// <summary>Median napeti za okno [V]; <c>NaN</c>, kdyz neni cerstve mereni.</summary>
        public double Volts { get; }

        /// <summary>Stav proti prahu varovani.</summary>
        public BatteryLevel Level { get; }
    }

    /// <summary>
    /// <b>Napeti baterie</b> z motorove jednotky (<see cref="MotorStateBase.Voltage"/>) jako medián
    /// za kratke okno a varovani pod prahem (<c>batwarn=</c>). Vzniklo po Robotouru 19. 9. 2026:
    /// robot ve 2. kole po 27 s zastavil s vybitou baterii a v zaznamu to bylo videt
    /// (12,1 V rano → 10,6 V v kole), jen to nikdo necetl - stranka napeti neukazovala a nic na nej
    /// nehlidalo (prov-baterie-na-strance).
    ///
    /// <para><b>Proc medián:</b> jednotlive vzorky z jednotky jsou hlucne (5-17 V), posledni hodnota
    /// je k nicemu. Zprava bez mereni (<see cref="MotorStateBase.HasMeasurement"/> = false, zastupny
    /// ramec driveru) se zahazuje - jeji napeti 0 by vypadalo jako vybita baterie.</para>
    ///
    /// <para><b>Hystereze:</b> do stavu <see cref="BatteryLevel.Low"/> pod prahem, zpet az nad
    /// prahem + <see cref="Hysteresis"/>. Napeti pod zatezi za jizdy kolisa a varovani nesmi blikat.</para>
    ///
    /// <para><b>Trace jen pri PRECHODU stavu</b> (do nizkeho a zpet), takze se nezahlti ani na horke
    /// ceste (motory ~90 zprav/s) a varovani zustane v zaznamu i bez otevrene stranky. Stranka nahledu
    /// cte stav z tehoz objektu (<see cref="Read"/>), aby se nerozesla s tim, co jde do Trace.</para>
    ///
    /// <para>Cas je razitko zpravy (<see cref="TimeBase"/>), stari mereni se meri proti
    /// <c>now</c> volajiciho - v testech jde podstrcit vlastni.</para>
    /// </summary>
    public sealed class BatteryMonitor : MessageTarget
    {
        /// <summary>Delka okna medianu [s].</summary>
        public const double DefaultWindowSec = 5.0;

        /// <summary>Nejmene vzorku v okne, nez se stav vyhodnoti (jeden ulet nesmi rozhodnout).</summary>
        public const int MinSamples = 5;

        private readonly double windowSec;
        private readonly Action<string> report;
        private readonly object gate = new object();
        private readonly Queue<(DateTime t, double v)> samples = new Queue<(DateTime, double)>();
        private readonly List<double> sortBuf = new List<double>();

        private BatteryLevel level = BatteryLevel.Unknown;
        private bool low;
        private double median = double.NaN;
        private DateTime lastSample = DateTime.MinValue;

        /// <param name="warnVolts">Prah varovani [V]; &lt;= 0 = nevarovat (napeti se dal ukazuje).</param>
        /// <param name="windowSec">Okno medianu [s].</param>
        /// <param name="hysteresis">O kolik nad prahem se varovani zrusi [V].</param>
        /// <param name="report">Kam hlasit prechody; null = <c>Trace.WriteLine</c>.</param>
        public BatteryMonitor(double warnVolts, double windowSec = DefaultWindowSec,
                              double hysteresis = 0.2, Action<string> report = null)
            : base(OverflowPolicy.DropOldest, 64)
        {
            WarnVolts = warnVolts;
            this.windowSec = windowSec > 0 ? windowSec : DefaultWindowSec;
            Hysteresis = Math.Max(0, hysteresis);
            this.report = report ?? (s => System.Diagnostics.Trace.WriteLine(s));
        }

        /// <summary>Prah varovani [V]; &lt;= 0 = vypnuto.</summary>
        public double WarnVolts { get; }

        /// <summary>Hystereze navratu ze stavu nizkeho napeti [V].</summary>
        public double Hysteresis { get; }

        /// <summary>Prida mereni ze zpravy motoru (zprava bez mereni se zahodi).</summary>
        public void Add(MotorStateBase m)
        {
            if (m == null || !m.HasMeasurement) return;
            Add(m.TimeStamp, m.Voltage);
        }

        /// <summary>Prida vzorek napeti [V] v case <paramref name="t"/>.</summary>
        public void Add(DateTime t, double volts)
        {
            if (!double.IsFinite(volts) || volts <= 0) return;
            string hlaska = null;
            lock (gate)
            {
                samples.Enqueue((t, volts));
                lastSample = t;
                while (samples.Count > 0 && (t - samples.Peek().t).TotalSeconds > windowSec)
                    samples.Dequeue();
                if (samples.Count < MinSamples) return;

                sortBuf.Clear();
                foreach (var s in samples) sortBuf.Add(s.v);
                sortBuf.Sort();
                int n = sortBuf.Count;
                median = n % 2 == 1 ? sortBuf[n / 2] : 0.5 * (sortBuf[n / 2 - 1] + sortBuf[n / 2]);

                bool wasLow = low;
                if (WarnVolts > 0)
                    low = low ? median < WarnVolts + Hysteresis : median < WarnVolts;
                level = low ? BatteryLevel.Low : BatteryLevel.Ok;

                if (low && !wasLow)
                    hlaska = string.Format(CultureInfo.InvariantCulture,
                        "BATERIE: napeti {0:F1} V (median {1:F0} s) pod prahem batwarn={2:F1} V - nabit.",
                        median, windowSec, WarnVolts);
                else if (!low && wasLow)
                    hlaska = string.Format(CultureInfo.InvariantCulture,
                        "BATERIE: napeti {0:F1} V zpet nad prahem ({1:F1} V + {2:F1} V hystereze).",
                        median, WarnVolts, Hysteresis);
            }
            // Mimo zamek - hlaseni muze tect do streamu (TraceInfoBridge).
            if (hlaska != null) report(hlaska);
        }

        /// <summary>
        /// Odecet v case <paramref name="now"/>: median a stav, nebo <see cref="BatteryLevel.Unknown"/>
        /// s <c>NaN</c>, kdyz posledni mereni je starsi nez okno (motory zmlkly - stare cislo by lhalo).
        /// </summary>
        public BatteryReading Read(DateTime now)
        {
            lock (gate)
            {
                if (level == BatteryLevel.Unknown || (now - lastSample).TotalSeconds > windowSec)
                    return new BatteryReading(double.NaN, BatteryLevel.Unknown);
                return new BatteryReading(median, level);
            }
        }

        /// <summary>Odecet ted (<see cref="TimeBase.Now"/>).</summary>
        public BatteryReading Read() => Read(TimeBase.Now);

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (msg is MotorStateBase m) Add(m);
        }
    }
}
