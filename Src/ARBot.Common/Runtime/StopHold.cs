using System;
using System.Collections.Generic;
using System.Diagnostics;
using ARBot.Common.Models;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Sev pro <b>drzene zastaveni</b> robota. Implementuje ho <see cref="ControlLoop"/>; mise
    /// a supervizori na nej zavisi pres tohle rozhrani, ne na cele smycce - stejny duvod jako
    /// u <see cref="Missions.IRegulatorHolder"/> (testovatelnost s fake objekty).
    ///
    /// <para><b>Proc to existuje.</b> Dosud slo robota zastavit jedine pres
    /// <c>IRegulatorHolder.Regulator = null</c>, jenze ta vlastnost nese DVE veci najednou -
    /// „kam jet" i „smim jet" - a vyhrava ten, kdo psal posledni. Hold to rozdeluje: vyssi smycky
    /// nastavuji regulator dal a o holdu nevedi. Viz doc/plan-drive-hold.md.</para>
    /// </summary>
    public interface IDriveHold
    {
        /// <summary>
        /// Pozada o zastaveni. Robot stoji, dokud se vraceny token neuvolni - a dokud ho drzi
        /// kdokoliv jiny. <paramref name="duvod"/> je povinny: jde do logu i na stranku, aby
        /// stojici robot nebyl zahada.
        /// </summary>
        StopHold StopRequest(string duvod);

        /// <summary>Drzi zastaveni aspon jeden token?</summary>
        bool IsHeld { get; }

        /// <summary>Duvody vsech drzenych tokenu (kopie; pro diagnostiku a stranku nahledu).</summary>
        IReadOnlyList<string> HoldReasons { get; }
    }

    /// <summary>
    /// Jeden <b>drzeny pozadavek na zastaveni</b>. Ziskava se z <see cref="IDriveHold.StopRequest"/>
    /// a uvolnuje <see cref="Dispose"/> - to je jedina cesta ven.
    ///
    /// <para><b>Zamerne bez finalizeru.</b> Kdyby token uvolnoval GC, robot by se rozjel proto, ze
    /// nekomu vypadla reference - to je ta horsi strana selhani. Zapomenuty token naopak znamena
    /// stojiciho robota (bezpecne) a <see cref="IDriveHold.HoldReasons"/> ho prozradi.</para>
    /// </summary>
    public sealed class StopHold : IDisposable
    {
        private readonly DriveHoldRegistry owner;
        private int uvolnen;   // 0 = drzi, 1 = uvolneno (Interlocked -> Dispose je idempotentni)

        internal StopHold(DriveHoldRegistry owner, string duvod)
        {
            this.owner = owner;
            Duvod = duvod;
        }

        /// <summary>Proc se drzi - text zadany pri <see cref="IDriveHold.StopRequest"/>.</summary>
        public string Duvod { get; }

        /// <summary>Je tenhle token jeste registrovany? (false po <see cref="Dispose"/>)</summary>
        public bool IsHeld => System.Threading.Volatile.Read(ref uvolnen) == 0;

        /// <summary>
        /// <b>Stoji robot SKUTECNE?</b> Mereno z posledniho stavu motoru (prirustek enkoderu), ne
        /// z toho, ze smycka poslala nulu.
        ///
        /// <para>⚠️ <b>Neznamy stav motoru je <c>false</c>, ne <c>true</c>.</b> Kdo ceka, aby smel
        /// udelat neco riskantniho, nesmi dostat „stoji" od senzoru, ktery mlci. Volajici si proto
        /// MUSI nest vlastni timeout - bez pripojenych motoru (<c>no_uart=true</c>) by cekal
        /// navzdy. Viz doc/plan-drive-hold.md, rozhodnuti 3.</para>
        /// </summary>
        public bool IsStopped => owner.Standing == true;

        /// <summary>Uvolni drzeni. Idempotentni; druhe a dalsi volani nic nedela.</summary>
        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref uvolnen, 1) != 0) return;
            owner.Release(this);
        }

        public override string ToString() => $"StopHold({Duvod}){(IsHeld ? string.Empty : " [uvolneny]")}";
    }

    /// <summary>
    /// Evidence drzenych <see cref="StopHold"/> a posledni znamy stav „stoji robot". Drzi ji
    /// <see cref="ControlLoop"/>, ktera ji na kazdem tiku krmi stavem motoru.
    ///
    /// <para><b>Rychla cesta je <see cref="IsHeld"/></b> (volatile int), protoze ji cte tik ridici
    /// smycky; seznam duvodu je pod zamkem a sahaji na nej jen diagnostika a registrace.</para>
    /// </summary>
    public sealed class DriveHoldRegistry : IDriveHold
    {
        private readonly object gate = new object();
        private readonly List<StopHold> drzene = new List<StopHold>();
        private volatile int pocet;
        private volatile object standing;   // null = neznamo, jinak boxovany bool

        /// <summary>Stoji robot? <c>null</c> = neni znamo (stav motoru jeste nedosel).</summary>
        public bool? Standing => (bool?)standing;

        /// <inheritdoc/>
        public bool IsHeld => pocet > 0;

        /// <inheritdoc/>
        public IReadOnlyList<string> HoldReasons
        {
            get
            {
                lock (gate)
                {
                    var vysledek = new string[drzene.Count];
                    for (int i = 0; i < drzene.Count; i++) vysledek[i] = drzene[i].Duvod;
                    return vysledek;
                }
            }
        }

        /// <inheritdoc/>
        public StopHold StopRequest(string duvod)
        {
            if (string.IsNullOrWhiteSpace(duvod))
                throw new ArgumentException("Duvod zastaveni je povinny - stojici robot bez duvodu "
                                            + "je nedohledatelna porucha.", nameof(duvod));

            var hold = new StopHold(this, duvod);
            bool prvni;
            lock (gate)
            {
                prvni = drzene.Count == 0;
                drzene.Add(hold);
                pocet = drzene.Count;
            }

            // Trace, ne Debug: v Release buildu na zarizeni je tohle jediná stopa, proc robot stoji.
            if (prvni) Trace.WriteLine($"StopHold: robot se zastavuje - {duvod}");
            else Trace.WriteLine($"StopHold: dalsi drzeni - {duvod} (celkem {pocet})");
            return hold;
        }

        internal void Release(StopHold hold)
        {
            bool posledni;
            lock (gate)
            {
                drzene.Remove(hold);
                pocet = drzene.Count;
                posledni = drzene.Count == 0;
            }

            if (posledni) Trace.WriteLine($"StopHold: uvolneno posledni drzeni ({hold.Duvod}) - robot smi jet.");
            else Trace.WriteLine($"StopHold: uvolneno drzeni ({hold.Duvod}), drzi dal {pocet}.");
        }

        /// <summary>
        /// Prevzeti stavu motoru z tiku ridici smycky. <paramref name="motor"/> <c>null</c> nebo
        /// zadny stav = <b>neznamo</b> (tedy NE „stoji") - viz <see cref="StopHold.IsStopped"/>.
        ///
        /// <para>⚠️ <b>Do „neznamo" patri i fail-ramec driveru</b> (<c>HasMeasurement == false</c>),
        /// a je to zradnejsi pripad nez mlceni: ramec dorazi, takze vypada jako merenie, ale jeho
        /// nuly nikdo nemeril — <c>SDC2160Ex</c> ho vyrabi po chybe portu. Bez tohoto rozliseni
        /// by odpojeny motorovy UART hlasil „robot stoji" prave tehdy, kdy o robotu nevime nic.</para>
        ///
        /// <para>Test je na PRESNOU nulu, bez epsilonu, stejne jako u nouzoveho zastaveni ve
        /// smycce: <c>LeftWheelSpeed</c> je nefiltrovany prirustek enkoderu, ktery je pri nulovem
        /// posunu presne 0.</para>
        /// </summary>
        public void NoteMotorState(IMotorState motor)
        {
            standing = motor == null || !motor.HasMeasurement
                ? null
                : (object)(motor.LeftWheelSpeed == 0 && motor.RightWheelSpeed == 0);
        }
    }
}
