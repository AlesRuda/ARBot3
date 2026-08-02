using System;
using System.Collections.Generic;
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Regulators;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Periodicky uzel pipeline: ridici smycka. Na pevne mrizce (<c>Profile.Ts</c>) pres
    /// <see cref="IScheduler"/> vzorkuje odhad stavu z <see cref="AsyncFusionEngine"/>
    /// (<see cref="AsyncFusionEngine.GetStateAt"/>), doplni Roll/Pitch z posledniho IMU,
    /// spocte <see cref="RegulatorResult"/> pro dojeti na pevny waypoint (MVP), zavola
    /// <c>motor.Drive(...)</c> a emituje <see cref="RobotStateMsg"/> + <see cref="DriveCommandMsg"/>.
    ///
    /// Uzel je zaroven <see cref="MessageProcessor"/> - odebira <see cref="IMUState"/> (kvuli
    /// Roll/Pitch) a odvozene zpravy vysila pres <see cref="MessageProcessor.Output"/>.
    /// Scheduler nema vlastni vlakno; takty pumpuje volajici pres <see cref="IScheduler.PumpDue"/>
    /// (v Run casovac s <c>clock.Now</c>) nebo pomocna metoda <see cref="Pump"/>.
    ///
    /// <para><b>Vizualni cesta (krok 3):</b> je-li zadan <see cref="ICameraPullSource"/>, smycka si na
    /// kazdem tiku pullne nejnovejsi snimky kamer (styl <c>GetLastMeasurement</c> - kdyz neni novy
    /// snimek, kamera se vynecha), vezme z nich grid pro rizeni a cely <see cref="CameraFrame"/>
    /// (raw + grid) forwardne na <see cref="MessageProcessor.Output"/> (a tim na Stream/zaznam/UI).
    /// Kamery uz proto NEjsou v pipeline pres <c>SensorMessageSource</c>. Zaznam je bezztratovy vzhledem
    /// k pouzitym datum: zaznamena se presne to, co rizeni realne vzorkovalo. Viz
    /// doc/plan-camera-vision-refactor.md.</para>
    /// </summary>
    public sealed class ControlLoop : MessageProcessor
    {
        private readonly AsyncFusionEngine engine;
        private readonly IMotorControl motor;
        private readonly IClock clock;
        private readonly IScheduler scheduler;
        private readonly double wheelBase;
        private readonly ICameraPullSource cameras;   // volitelny pull kamer (krok 3); null = bez vize
        private readonly IDisposable registration;
        private readonly TimeSpan period;
        private readonly TimeSpan pathTimeout;

        // Posledni IMU (kvuli Roll/Pitch); ctou/zapisuji ruzna vlakna -> volatile reference.
        private volatile IMUState lastImu;

        // Aktualni regulator (nizsi smycka jej jede; vyssi smycka jej atomicky prehazuje pres Regulator).
        private volatile IRegulator regulator;
        private volatile bool regulatorFresh;   // Regulator byl nastaven; tik ho orazitkuje casem tk.
        private DateTime lastRegulatorTick;      // cas posledni aktualizace regulatoru (v case tiku)
        private double lastForward;              // posledni dopredna rychlost (pro nouzove dobrzdeni)

        /// <summary>
        /// Regulator, ktery smycka jede (bodovy <see cref="PointRegulator"/> nebo dráhový <see cref="PathResult"/>).
        /// Nastavuje ho vyssi ridici smycka (mapa/OSM -> <see cref="IPathPlanner.Plan"/>); vymena je atomicka
        /// (volatile). <c>null</c> = zadny cil -> robot stoji (bezpecny stav). Kdyz se regulator dele nez
        /// <see cref="Profile.PathControlTimeOut"/> neaktualizuje, smycka nouzove dobrzdi po posledni trase.
        /// Viz doc/path-following.md.
        /// </summary>
        public IRegulator Regulator
        {
            get => regulator;
            set { regulator = value; regulatorFresh = true; }
        }

        /// <param name="engine">Fuzni engine (dotazovany na tiku).</param>
        /// <param name="motor">Motory (Run: realny driver, Simulate: <see cref="DummyMotors"/>).</param>
        /// <param name="clock">Hodiny (zdroj "ted" pro <see cref="Pump"/>).</param>
        /// <param name="scheduler">Scheduler periodickych taktu.</param>
        /// <param name="period">Perioda taktu; default <c>Profile.Ts</c> ms.</param>
        /// <param name="wheelBase">Rozchod kol pro prepocet dif = RotationSpeed * rozchod; default <c>Profile.Rozchod</c>.</param>
        /// <param name="cameras">Volitelny pull kamer (krok 3): na kazdem tiku se pullnou nejnovejsi
        /// snimky a cely <see cref="CameraFrame"/> se forwardne na <see cref="MessageProcessor.Output"/>.
        /// null = smycka kamery nepulluje (napr. testy nebo bezvize rezim).</param>
        /// <param name="pathTimeout">Timeout zastaralosti drahy; default <c>Profile.PathControlTimeOut</c> ms.</param>
        public ControlLoop(AsyncFusionEngine engine, IMotorControl motor,
                           IClock clock, IScheduler scheduler,
                           TimeSpan? period = null, double? wheelBase = null,
                           ICameraPullSource cameras = null, TimeSpan? pathTimeout = null)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.motor = motor ?? throw new ArgumentNullException(nameof(motor));
            this.clock = clock;
            this.scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            this.wheelBase = wheelBase ?? Profile.Rozchod;
            this.cameras = cameras;
            this.period = period ?? TimeSpan.FromMilliseconds(Profile.Ts);
            this.pathTimeout = pathTimeout ?? TimeSpan.FromMilliseconds(Profile.PathControlTimeOut);

            registration = scheduler.Register(this.period, OnTick);
        }

        /// <summary>Vhodny helper pro Run: napumpuje scheduler aktualnim casem hodin.</summary>
        public void Pump()
        {
            if (clock != null)
                scheduler.PumpDue(clock.Now);
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            // Ridici smycka odebira jen posledni IMU (kvuli Roll/Pitch).
            if (msg is IMUState imu)
                lastImu = imu;
        }

        /// <summary>Jeden takt ridici smycky v case <paramref name="tk"/> (bod mrizky).</summary>
        private void OnTick(DateTime tk)
        {
            // Vize: pullni nejnovejsi snimky kamer (grid pro rizeni; cely ramec na Stream). Pullujeme
            // NA ZACATKU tiku, aby rizeni melo k dispozici nejcerstvejsi grid. Forward na Stream az
            // po vypoctu rizeni (viz nize), aby zaznam mel prirozene poradi: snimek -> stav -> prikaz.
            IReadOnlyList<CameraFrame> frames = null;
            try { frames = cameras?.PullLatest(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            RobotState rs = engine.GetStateAt(tk);

            // Roll/Pitch doplnime z posledniho IMU (EKF je nedrzi).
            var imu = lastImu;
            var ypr = imu?.YPR();
            if (ypr != null)
            {
                rs.Pitch = ypr.Pitch;
                rs.Roll = ypr.Roll;
            }

            // Rizeni regulatorem (Regulator). Vyssi smycka (mapa/OSM) jej nastavuje a obnovuje;
            // grid(y) z frames budou jeho vstupem. null = zadny cil -> stani; zastaraly regulator ->
            // nouzove dobrzdeni po posledni trase. Viz doc/path-following.md.
            var reg = regulator;
            if (regulatorFresh) { regulatorFresh = false; lastRegulatorTick = tk; }

            double forvard = 0, rotationSpeed = 0;
            if (reg != null)
            {
                var r = reg.Control(rs);
                rotationSpeed = r.RotationSpeed;
                if (tk - lastRegulatorTick > pathTimeout)
                {
                    // Zastarala draha: rizeni (smer) z posledni trasy, dopredna rychlost rampou k nule.
                    double decel = Profile.MaxDecceleration * period.TotalSeconds;
                    forvard = Math.Max(0, lastForward - decel);
                }
                else
                {
                    forvard = r.Speed;
                }
            }
            lastForward = forvard;

            double dif = rotationSpeed * wheelBase;   // dif>0 = vpravo
            motor.Drive(forvard, dif);

            EmitDerived(new RobotStateMsg(rs));
            EmitDerived(new DriveCommandMsg
            {
                Speed = forvard,
                RotationSpeed = rotationSpeed,
                Forvard = forvard,
                Dif = dif,
                TimeStamp = tk
            });

            // Forward pullnutych snimku na Stream (zaznam/UI). Cely CameraFrame (raw + grid), aby slo
            // zpetne overit chovani robota. Output je neblokujici fan-out (odberatele maji vlastni
            // fronty), takze forward na vlakne tiku nebrzdi rizeni.
            if (frames != null)
                for (int i = 0; i < frames.Count; i++)
                    ForwardFrame(frames[i]);
        }

        /// <summary>
        /// Forwardne surovy snimek kamery na <see cref="MessageProcessor.Output"/> (-> Stream). Nejde
        /// o odvozeny vysledek vypoctu, ale o pruchozi surove mereni; <see cref="MessageProcessor.Output"/>
        /// se ale pripojuje jen na Stream (ne do grafu zpracovani), takze je to bezpecny forward.
        /// </summary>
        private void ForwardFrame(CameraFrame frame)
        {
            if (frame != null) EmitDerived(frame);
        }

        /// <inheritdoc/>
        public override void Stop()
        {
            registration?.Dispose();
            base.Stop();
        }
    }
}
