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
        /// <summary>
        /// Žádost o zastavení smyčky. <b>volatile</b> schválně: píše ji vlákno, které volá
        /// <see cref="Stop"/>, a čte ji smyčka <see cref="Process"/> na vlákně senzoru — bez toho
        /// si ji JIT smí držet v registru a smyčka by se zastavení nikdy nedozvěděla.
        /// </summary>
        protected volatile bool stopRequired = false;
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

        // Kdy naposledy dorazilo skutecne merenie (nebo kdy se senzor spustil). Slouzi k detekci
        // TICHEHO senzoru - viz SilentTimeout. Volatile: pise vlakno senzoru, cte volajici IsError.
        private volatile object lastOkAtBox;

        /// <summary>
        /// Jak dlouho smi senzor <b>mlčet</b>, než se to počítá za poruchu.
        /// <see cref="TimeSpan.Zero"/> = hlídání vypnuté.
        ///
        /// <para>⚠️ <b>Nač to je (nález auditu 15. 9. 2026).</b> Po odpojení USB převodníku zůstane
        /// <c>sp.IsOpen</c> <c>true</c>, <c>Read</c> vrací 0 bajtů a ovladač vrátí <c>null</c>
        /// <b>bez výjimky</b> — takže <see cref="Process"/> nastaví <c>isError = false</c>
        /// a senzor se tváří zdravě. V Release buildu je pak IMU/GPS mrtvé, stav na stránce
        /// náhledu zelený a v journalu ani řádek. <b>„Nic neměřím" musí být chyba, ne ticho.</b></para>
        ///
        /// <para>Výchozích 5 s je s velkou rezervou nad periodou všech dnešních senzorů (IMU 100 Hz,
        /// GPS 10 Hz, motor 2 Hz, kamery 30 Hz). Počítá se <b>od startu</b>, ne od nuly — senzor
        /// dostane okno na náběh.</para>
        /// </summary>
        public virtual TimeSpan SilentTimeout => TimeSpan.FromSeconds(5);

        /// <summary>
        /// Jak dlouho <see cref="Stop"/> čeká na doběhnutí vlákna, než to vzdá a jen to ohlásí.
        ///
        /// <para>⚠️ <b>Nač to je (nález auditu 15. 9. 2026).</b> Čekalo se <b>bez timeoutu</b>.
        /// Ovladač, který uvízne uvnitř <c>GetMeasurement</c> (u-blox točil <c>while (pos == null)</c>
        /// bez kontroly zastavení, <c>sp.ReadLine()</c> měl nekonečný <c>ReadTimeout</c>), tím
        /// zastaví <c>ARBotRuntime.Stop()</c> — a ten běží pod zámkem, takže zatuhne celý runtime
        /// (kandidát na zatuhnutí <c>Start()</c> ze 14. 9. 2026).</para>
        ///
        /// <para>Vzdát se čekání je <b>bezpečnější než čekat</b>: vlákno senzoru jen čte port
        /// a nic neřídí, kdežto zatuhlý <c>Stop()</c> znamená, že se nezastaví ani řídicí smyčka.</para>
        /// </summary>
        public virtual TimeSpan StopTimeout => TimeSpan.FromSeconds(3);

        /// <summary>
        /// Pehem zpracovani doslo k chybe.
        ///
        /// <para>Chybou je i <b>ticho</b> delší než <see cref="SilentTimeout"/> (jen když senzor
        /// běží — nespuštěný senzor neměří, a to porucha není).</para>
        /// </summary>
        public virtual bool IsError => isError || JeTichy();

        /// <summary>Mlčí senzor déle, než smí? Viz <see cref="SilentTimeout"/>.</summary>
        private bool JeTichy()
        {
            var prah = SilentTimeout;
            if (prah <= TimeSpan.Zero || !IsRunning)
                return false;
            if (lastOkAtBox is not DateTime od)
                return false;                       // jeste nestartoval
            return Common.TimeBase.Now - od > prah;
        }

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
                // Prah ticha se pocita od startu - senzor dostane okno na nabehnuti.
                lastOkAtBox = Common.TimeBase.Now;
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
                var t = task;
                // Cekani S TIMEOUTEM - viz StopTimeout. Zatuhly ovladac nesmi zastavit cely runtime.
                if (t != null && !t.Wait(StopTimeout))
                {
                    // Trace, ne Debug: v Release (na zarizeni) je tohle jedina stopa po tom, ze
                    // senzor zustal viset - a ze se tedy port neuvolnil pro dalsi start.
                    Trace.WriteLine($"{Name}: vlakno senzoru nedobehlo do {StopTimeout.TotalSeconds:0.#} s "
                                  + "(ovladac uvizl ve cteni) - pokracuje se bez nej.");
                    // task se ZAMERNE nenuluje: dokud vlakno zije, IsRunning ma rikat pravdu
                    // a opakovany Start() nesmi rozjet druhe vlakno nad tymz portem.
                }
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
                    {
                        lastOkAtBox = Common.TimeBase.Now;   // hlidani ticha - viz SilentTimeout
                        OnMeasurement(v);
                    }
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
