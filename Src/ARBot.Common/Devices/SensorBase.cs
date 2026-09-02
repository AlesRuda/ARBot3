using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Predek pro sensory
    /// </summary>
    public abstract class SensorBase<TState>:IDisposable, ISensor, IControllableSensor where TState: class
    {
        protected Task task;
        protected bool stopRequired = false;
        protected object lck = new object();
        protected DateTime? lastPickupTimeStamp = null;
        protected DateTime? lastTimeStamp = null;
        protected uint? lastFrameNum = null;
        protected uint frameNum = 0;
        bool disposed = false;

        /// <summary>
        /// Vyvolano (mimo zamek) po prichodu noveho mereni v ramci zpracovani na pozadi.
        /// </summary>
        public event EventHandler<TState> MeasurementArived;


        private bool isError = false;
        /// <summary>
        /// Pehem zpracovani doslo k chybe.
        /// </summary>
        public virtual bool IsError => isError;

        protected virtual void Pickedup(TState s)
        {
            var ss = s as SensorStateBase;
            if (ss != null)
                lastPickupTimeStamp = ss.TimeStamp;
            lastMeasurement = null;
        }

        protected TState lastMeasurement;
        /// <summary>
        /// Posledni vzorek, pokud je vyzvednut tak null.
        ///
        /// <para><b>Senzor NESPOUSTI</b> (zmena 21. 8. 2026). Driv tady bylo <c>Start()</c>, takze
        /// vyzvednuti mereni senzor rozjelo — a zastavit se pak nedal vubec: pull kamer v runtime
        /// nebo detailni okno v UI ho do jednoho tiku zapnuly zpatky. Kdo chce mereni, musi si
        /// senzor spustit sam (<see cref="Start"/>); v pipeline to dela
        /// <c>SensorMessageSource(controlSensor: true)</c>, v UI dokumenty senzoru a rucne panel
        /// senzoru. Vraci <c>null</c>, dokud senzor nebezi (stejne jako kdyz jen neni novy vzorek).</para>
        /// </summary>
        public TState GetLastMeasurement()
        {
            TState v = null;
            lock (lck)
            {
                v = lastMeasurement;
                Pickedup(v);
            }
            return v;
        }
        public bool IsRunning => task != null;

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public abstract string Name { get; }

        /// <summary>
        /// Spusti smycku zpracovani
        /// </summary>
        public void Start()
        {
            if (!IsRunning)
            {
                stopRequired = false;
                task = Task.Factory.StartNew(() => Process(), TaskCreationOptions.LongRunning);
            }
        }

        /// <summary>
        /// Ukoncuje smycku zpracovani
        /// </summary>
        public virtual void Stop()
        {
            if(IsRunning)
            {
                stopRequired = true;
                task?.Wait();
            }
        }
        /// <summary>
        /// Ziskava mereni senzoru. Ceka az dorazi zmerena hodnota.
        /// </summary>
        /// <returns></returns>
        protected abstract TState GetMeasurement();

        /// <summary>
        /// Smycka zpracovani
        /// </summary>
        /// <summary>Idle-backoff [ms] pri chybe/prazdnem mereni, aby smycka nebusy-spinovala.</summary>
        protected virtual int IdleBackoffMs => 20;

        /// <summary>Horni mez backoffu [ms] pri trvale chybe (odpojeny senzor polluje pomalu).</summary>
        protected virtual int MaxErrorBackoffMs => 1000;

        // Pocet po sobe jdoucich chyb (0 = OK). Rizeni exponencialniho backoffu a throttlingu logu,
        // aby trvale chybujici senzor (napr. odpojeny UART) NEspaloval CPU ani nealokoval stack-trace
        // stringy kazdou iteraci (jinak periodicky gen2 churn na jeho vlakne - viz devlog 2026-08-01).
        private int errStreak;

        protected void Process()
        {
            while (!stopRequired)
            {
                try
                {
                    var v = GetMeasurement();
                    lock (lck)
                    {
                        var ss = v as SensorStateBase;
                        if (ss != null)
                        {
                            var ts = ss.TimeStamp;
                            ss.FrameNum = frameNum++;
                            ss.FrameReceivePeriod = ts - (lastTimeStamp ?? ts);
                            ss.FramePickupPeriod = ts - (lastPickupTimeStamp ?? ts);
                            if (lastMeasurement is SensorStateBase)
                                ss.DropedOutNum = (lastMeasurement as SensorStateBase).DropedOutNum + 1;
                            else
                                ss.DropedOutNum = 0;
                            lastTimeStamp = ts;
                        }
                        lastMeasurement = v;
                    }

                    if (v != null)
                        OnMeasurement(v);
                    else
                        // Zadne mereni (typicky nedostupny senzor/zavreny port): kratky
                        // backoff, aby smycka nebusy-spinovala a nezaplavovala Debug log.
                        System.Threading.Thread.Sleep(IdleBackoffMs);
                    isError = false;
                    errStreak = 0;
                }
                catch (Exception ex)
                {
                    isError = true;
                    // Trvale chybujici senzor (odpojeny UART): NElogovat kazdou iteraci - ex.ToString()
                    // alokuje cely stack-trace string (zbytecny GC churn na vlakne senzoru). Logujeme
                    // jen prvni chybu a pak rIdce jen ex.Message; backoff roste exponencialne (mrtvy
                    // senzor pak polluje ~1x/s misto 50x/s).
                    //
                    // TRACE, ne Debug (2. 9. 2026): Debug.WriteLine je [Conditional("DEBUG")],
                    // takze v Release buildu - a prave ten bezi na zarizeni - nezustane po
                    // poruche senzoru ZADNA stopa. Tady je to obzvlast draze: tohle je
                    // OBECNA chybova cesta VSECH senzoru, takze bez ni se u nefunkcniho
                    // senzoru nedozvis vubec nic. Throttling vyse (prvni chyba a pak kazda
                    // 64.) plati dal, takze proud nezaplavi.
                    if (errStreak == 0 || (errStreak & 63) == 0)
                        Trace.WriteLine($"{Name}: {ex.Message}");
                    errStreak++;
                    int backoff = Math.Min(MaxErrorBackoffMs, IdleBackoffMs << Math.Min(6, errStreak));
                    System.Threading.Thread.Sleep(backoff);
                }
            }
            task = null;
        }

        /// <summary>
        /// Hook volany (mimo zamek) po prichodu a ulozeni noveho mereni. Vychozi implementace
        /// vyvola udalost MeasurementArived; potomci mohou prepsat.
        /// </summary>
        protected virtual void OnMeasurement(TState v)
        {
            MeasurementArived?.Invoke(this, v);
        }
        // Public implementation of Dispose pattern callable by consumers.
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing)
        {
            if (disposed)
                return;

            if (disposing)
            {
                Stop();
            }

            disposed = true;
        }

        ~SensorBase()
        {
            Dispose(false);
        }
    }
}
