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
    public abstract class SensorBase<TState>:IDisposable, ISensor where TState: class
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
        /// Posledni vzorek, pokud je vyzvednut tak null
        /// </summary>
        public TState GetLastMeasurement()
        {
            TState v = null;
            Start();
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
        public void Stop()
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
                    isError = false;
                }
                catch (Exception ex)
                {
                    isError = true;
                    Debug.WriteLine(ex.ToString());
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
