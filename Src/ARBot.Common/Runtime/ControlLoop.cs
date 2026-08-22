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
    /// Roll/Pitch) a <see cref="IMotorState"/> (kvuli nouzovemu zastaveni), odvozene zpravy vysila
    /// pres <see cref="MessageProcessor.Output"/>.
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

        // Posledni stav motoru (kvuli nouzovemu zastaveni a aktualni rychlosti kol); stejny
        // volatile vzor jako lastImu. null = stav motoru jeste nedosel (napr. DummyMotors bez
        // zdroje zprav) -> nouzove zastaveni se NEuplatnuje, robot normalne jede.
        private volatile IMotorState lastMotor;

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

        /// <summary>
        /// DIAGNOSTIKA: posledni stav motoru, ktery smycka prevzala (podle nej se uplatnuje nouzove
        /// zastaveni a posuzuje, zda kola stoji). <c>null</c> = zadny stav jeste nedosel.
        /// </summary>
        public IMotorState LastMotorState => lastMotor;

        /// <summary>
        /// Volitelny zdroj SKUTECNE pozy (ground truth) - nenulovy jen pri virtualnim HW.
        /// Kdyz je nastaveny, smycka emituje <see cref="Logs.GroundTruthMsg"/> na temze tiku a se
        /// stejnym casem jako <see cref="RobotStateMsg"/>, takze rozdil obou zprav v jednom taktu
        /// je primo chyba odhadu (viz doc/virtual-hw.md).
        ///
        /// <para>Zamerne <c>Func</c>, a ne odkaz na simulovaneho robota: ridici smycka nema duvod
        /// vedet o simulaci, a virtualni HW se da za behu zapnout i vypnout (funkce smi vratit
        /// <c>null</c> - pak se nic neemituje).</para>
        /// </summary>
        public Func<DateTime, Logs.GroundTruthMsg> GroundTruthAt { get; set; }

        /// <param name="engine">Fuzni engine (dotazovany na tiku).</param>
        /// <param name="motor">Motory (Run: realny driver, Simulate: <see cref="DummyMotors"/>).</param>
        /// <param name="clock">Hodiny (zdroj "ted" pro <see cref="Pump"/>).</param>
        /// <param name="scheduler">Scheduler periodickych taktu.</param>
        /// <param name="period">Perioda taktu; default <c>Profile.Ts</c> ms.</param>
        /// <param name="wheelBase">Rozchod kol pro prepocet <c>dif = RotationSpeed * rozchod / 2</c>
        /// (dif je offset na kolo - viz OnTick); default <c>Profile.Rozchod</c>.</param>
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
        /// <remarks>
        /// OTEVRENY UKOL: <see cref="IMUState"/> nenese identitu zdroje (neni INamedMessage), takze pri
        /// dvou IMU (VN100 + T265) tady vyhrava "posledni dosle" a Roll/Pitch mohou mezi tiky preskakovat
        /// mezi cidly s jinou montazi a kvalitou. Navic to obchazi fuzi (bez gatingu, bez kovariance, bez
        /// dopredikovani do casu tiku). Spravne resen: mit pitch/roll ve STAVU EKF a brat je z
        /// <see cref="RobotState"/> jako ostatni slozky - pak tento Consume i <see cref="lastImu"/> zmizi.
        /// Viz doc/ekf-fusion.md → "Pitch/Roll patri do stavu EKF".
        /// </remarks>
        protected override void Consume(Message msg)
        {
            // Ridici smycka odebira posledni IMU (kvuli Roll/Pitch) a posledni stav motoru
            // (kvuli nouzovemu zastaveni - viz OnTick).
            if (msg is IMUState imu)
                lastImu = imu;
            else if (msg is IMotorState motorState)
                lastMotor = motorState;
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
            if (rs == null)
            {
                // Fuze neumi rict, kde robot je (tik je mimo okno historie - napr. po dlouhem
                // vypadku senzoru nebo zaseknuti smycky). Bezpecny stav = stat; radit podle
                // neznamé pozy je horsi nez nejet.
                lastForward = 0;
                motor.Drive(0, 0);
                if (frames != null)
                    for (int i = 0; i < frames.Count; i++)
                        ForwardFrame(frames[i]);
                return;
            }

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
            // Nouzove zastaveni: dopredna rychlost na nulu, rotace az kdyz robot SKUTECNE stoji.
            // Dokud se kola jeste toci, ma smysl drzet zatoceni podle regulatoru (jako kdyz se brzdi
            // v zatacce); jak robot stoji, rotaci nulujeme, aby se netocil na miste - a posledni
            // odeslany prikaz je (0,0), takze po uvolneni stopu nevznika zadny transient.
            //
            // Porovnava se na PRESNOU nulu, bez epsilonu: MotorStateBase.LeftWheelSpeed je
            // prirustek enkoderu / FramePickupPeriod (SDC2160Ex posila leftEnc - lastLeftEnc), tedy
            // nefiltrovana hodnota, ktera je pri nulovem prirustku presne 0. Motory jsou navic
            // rizene pozicne ve zpetne vazbe, takze "nepohnul se ani tik" znamena "stoji".
            //
            // Skutecne brzdeni (a bezpecnost) resi radic motorove jednotky, ktery na temze signalu
            // dela totez (MicroBasic skript v SDC2160Ex.cs). Tady jde o konzistenci softwaru
            // s realitou: DriveCommandMsg nesmi tvrdit "jedu", kdyz robot stoji, posledni odeslany
            // prikaz nesmi byt zastaraly a vyssi vrstvy musi vedet, ze stani neni zasek.
            // Viz doc/robotour-mission.md.
            var mot = lastMotor;
            bool emergencyStop = mot != null && mot.IsEmergencyStop;
            if (emergencyStop)
            {
                forvard = 0;
                bool standing = mot == null || (mot.LeftWheelSpeed == 0 && mot.RightWheelSpeed == 0);
                if (standing) rotationSpeed = 0;
            }

            lastForward = forvard;

            // dif je OFFSET NA KOLO, ne rozdil rychlosti kol: driver ho k jednomu kolu prictе a od
            // druheho odecte (MicroBasic skript: motor1 = -(curSpeed+curRotSpeed),
            // motor2 = curSpeed-curRotSpeed). Plati tedy vR - vL = omega*rozchod = 2*dif, takze
            // dif = omega * rozchod / 2. Bez pulky by robot zataceL DVAKRAT rychleji, nez regulator
            // chce. Totez pulkovani ma predchozi generace (Drive(ReqSpeed, ReqRotationSpeed*Rozchod/2))
            // i TrapezoidMotionProfile (rozchod2 = rozchod/2 jako rameno pro prepocet omega <-> kolo).
            //
            // OTEVRENY UKOL - ZNAMENKO OVERIT NA ZARIZENI: rotationSpeed je +CCW (vlevo), ale
            // IMotorControl.Drive dokumentuje dif>0 jako PRAVE otaceni a SDC2160Ex jeste posila
            // -CalcSpeed(dif). Z kodu se to rozhodnout neda (zalezi i na tom, ktere kolo je motor 1);
            // predchozi generace jela s +omega*Rozchod/2 bez prehozeni, takze to nejspis vychazi.
            // Zkouska: zadat male +omega pri nulove rychlosti a videt, kam se robot otoci.
            // Viz doc/path-following.md -> "Otevreny ukol: znamenko rotace overit na zarizeni".
            double dif = rotationSpeed * wheelBase / 2.0;
            motor.Drive(forvard, dif);

            EmitDerived(new RobotStateMsg(rs));

            // Ground truth (jen virtualni HW) - se STEJNYM casem jako RobotStateMsg, aby rozdil
            // obou zprav v jednom taktu byl primo chyba odhadu. Viz GroundTruthAt.
            var truthSource = GroundTruthAt;
            if (truthSource != null)
            {
                try
                {
                    var truth = truthSource(rs.TimeStamp);
                    if (truth != null) EmitDerived(truth);
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }

            EmitDerived(new DriveCommandMsg
            {
                Speed = forvard,
                RotationSpeed = rotationSpeed,
                Forvard = forvard,
                Dif = dif,
                EmergencyStop = emergencyStop,
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
