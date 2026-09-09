using System;
using System.Diagnostics;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Models;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Mise Track: objezd mist ze souboru <c>*.track</c>.</b> Viz doc/track-mission.md.
    ///
    /// <para>Precte seznam zemepisnych souradnic, ke kazde najde <b>nejblizsi misto na siti
    /// cest</b> a postupne k nim jede; po dosazeni jednoho pokracuje na dalsi. Slovo
    /// <c>repeat</c> na konci souboru znamena „zacni znovu od prvniho", takze robot jezdi
    /// dokola.</para>
    ///
    /// <para>Ridi <see cref="GlobalNavigator"/> zadavanim <b>LLA cilu</b> — sama nezna ani graf
    /// cest, ani occupancy grid, ani regulatory, presne jako <see cref="RobotourMission"/>.
    /// <b>Spolecny ridici predek misi se zamerne nezavadi</b> (viz <c>RobotourMission</c>);
    /// spolecne je jen hlaseni stavu, a to resi rozhrani <see cref="IMissionStatus"/>.</para>
    ///
    /// <para><b>Proc je to samostatna mise a ne „Robotour bez QR":</b> Robotour je stavovy automat
    /// <i>doruceni</i> — kotvi depo, cte kody, otevira servisni okno pro cloveka a ma tri
    /// zastaveni s totoznym prubehem. Track nema ani jedno z toho: nekotvi depo (cile jsou
    /// absolutni souradnice, ne odsazeni od startu), nikdo s nim v prubehu nemluvi
    /// a <b>mezi body nezastavuje</b>. Ze dvou automatu by vznikl jeden s prazdnymi vetvemi.</para>
    ///
    /// <para><b>Dve pojistky, na kterych navrh stoji:</b></para>
    /// <list type="number">
    /// <item><b>Volba mise robota nerozjede.</b> Mise startuje sama (jako Robotour), ale automat
    /// jde <c>Idle → AwaitingEStop</c>, tedy ceka, az clovek nouzove zastaveni <b>zmackne</b>,
    /// a jeho <b>uvolneni</b> je pokyn „jed". Prvni pohyb tedy vzdycky vyzaduje cloveka
    /// u robota — tatáz zasada jako u webove volby mise (viz CLAUDE.md).</item>
    /// <item><b>Bod, ktery je od site dal nez <see cref="TrackConfig.MaxPointOffRoadM"/>, misi
    /// PRERUSI</b> — nepreskoci se. Tiche preskoceni by znamenalo, ze robot objel jinou trasu,
    /// nez clovek zadal, a poznalo by se to jen tim, co v ni NENI.</item>
    /// </list>
    ///
    /// <para><b>Zadny prechod neni implicitni</b> — vzdy z konkretni podminky, aby se ve zaznamu
    /// dalo dohledat, proc se mise posunula.</para>
    ///
    /// <para>Pozor na jmena: <see cref="StartMission"/>, ne <c>Start()</c> — to by kolidovalo se
    /// zdedenou metodou <c>MessageTarget</c>, ktera spousti vlakno stupne (past zapsana
    /// u Robotouru).</para>
    /// </summary>
    public sealed class TrackMission : MessageProcessor, IMissionStatus
    {
        private readonly object gate = new object();

        private readonly IGlobalGoalSink goals;
        private readonly TrackPlan plan;
        private readonly IRegulatorHolder control;
        private readonly IRouteProbe routes;
        private readonly TrackConfig config;

        // --- stav automatu ---
        private TrackPhase phase = TrackPhase.Idle;
        private int pointIndex;
        private int lap = 1;
        private int reached;

        private LLA activeTarget;
        private double activeOffRoadM, activeRouteLengthM;

        private DateTime missionStartedAt, phaseEnteredAt, lastTime, lastMessageAt;

        /// <summary>
        /// Je uz cas mise ukotveny v hodinach DAT? Timeouty bezi na casech zprav, ne na hodinach
        /// stroje — jinak by pri prehravani zaznamu (a v testech) merily rozdil dvou
        /// nesouvisejicich hodin a vyprsely by hned. Tatáz past jako u Robotouru.
        /// </summary>
        private bool anchored;

        private bool emergencyStop, standing = true;
        private bool regulatorCleared;
        private string abortReason = string.Empty;
        private int timeouts;

        /// <param name="goals">Prijemce LLA cilu (globalni navigace).</param>
        /// <param name="plan">Seznam mist ze souboru.</param>
        /// <param name="control">Drzitel regulatoru; <c>null</c> = mise regulator nezahazuje
        /// (pouzitelne v testech, v aplikaci se predava <c>ControlLoop</c>).</param>
        /// <param name="routes">Zkouska dosazitelnosti a <b>prichyceni na sit</b>; <c>null</c> =
        /// jede se na surove souradnice ze souboru (viz <see cref="NextTarget"/>).</param>
        /// <param name="config">Konfigurace; <c>null</c> = vychozi.</param>
        public TrackMission(IGlobalGoalSink goals, TrackPlan plan,
                            IRegulatorHolder control = null, IRouteProbe routes = null,
                            TrackConfig config = null)
            : base(OverflowPolicy.DropOldest, capacity: 32)
        {
            this.goals = goals ?? throw new ArgumentNullException(nameof(goals));
            this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
            this.control = control;
            this.routes = routes;
            this.config = config ?? new TrackConfig();
            this.config.Validate();
        }

        // ---------------- Diagnostika pro UI a testy ----------------

        /// <summary>Nastaveni, se kterym mise pracuje.</summary>
        public TrackConfig Config => config;

        /// <summary>Seznam mist, ktery mise objizdi.</summary>
        public TrackPlan Plan => plan;

        /// <summary>Faze automatu.</summary>
        public TrackPhase Phase { get { lock (gate) return phase; } }

        /// <summary>Index mista, ke kteremu se jede (od nuly).</summary>
        public int PointIndex { get { lock (gate) return pointIndex; } }

        /// <summary>Kolikate kolo se jede (od 1).</summary>
        public int Lap { get { lock (gate) return lap; } }

        /// <summary>Kolik mist se uz objelo celkem (pres vsechna kola).</summary>
        public int Reached { get { lock (gate) return reached; } }

        /// <summary>Cil, na ktery se prave jede (uz <b>prichyceny</b> na sit), nebo <c>null</c>.</summary>
        public LLA ActiveTarget { get { lock (gate) return activeTarget; } }

        /// <summary>Jak daleko lezel aktualni bod od site cest [m].</summary>
        public double ActiveOffRoadM { get { lock (gate) return activeOffRoadM; } }

        /// <summary>Duvod preruseni; prazdny, kdyz mise prerusena nebyla.</summary>
        public string AbortReason { get { lock (gate) return abortReason; } }

        /// <summary>Kolik timeoutu vyprselo.</summary>
        public int Timeouts { get { lock (gate) return timeouts; } }

        /// <summary>Posledni vyrobena zprava (diagnostika pro UI a telemetrii).</summary>
        public TrackMsg LastMessage { get; private set; }

        // --- IMissionStatus ---

        /// <inheritdoc/>
        public string MissionName => MissionStatusText.Track;

        /// <inheritdoc/>
        public string PhaseText
        {
            get
            {
                lock (gate)
                {
                    string zaklad = MissionStatusText.PhaseText(phase);
                    if (phase != TrackPhase.Driving) return zaklad;

                    // U jizdy je podstatne, KAM se jede - „jede k mistu" bez cisla by obsluze
                    // nerekl, jestli se mise vubec posouva.
                    return $"{zaklad} {pointIndex + 1}/{plan.Count}"
                           + (plan.Repeat ? $", kolo {lap}" : string.Empty);
                }
            }
        }

        /// <inheritdoc/>
        public MissionWait WaitingFor { get { lock (gate) return MissionStatusText.WaitFor(phase); } }

        /// <summary>
        /// Jak dlouho mise bezi — z hodin DAT (razitka zprav). Dokud mise nezacala, je to nula:
        /// rozdil proti <c>default(DateTime)</c> by dal ~64 miliard sekund (past z Robotouru).
        /// </summary>
        public TimeSpan Elapsed
        {
            get
            {
                lock (gate)
                {
                    if (missionStartedAt == default || lastTime <= missionStartedAt) return TimeSpan.Zero;
                    return lastTime - missionStartedAt;
                }
            }
        }

        // ---------------- Prikazy obsluhy ----------------

        /// <summary>
        /// „Start mise". Prechod <see cref="TrackPhase.Idle"/> →
        /// <see cref="TrackPhase.AwaitingEStop"/>; jina faze prikaz ignoruje (mise uz bezi).
        ///
        /// <para>Cas mise se <b>ukotvi az prvnim udajem, ktery prijde</b> — obsluha macka
        /// tlacitko, ale merit se musi v hodinach dat. Do te doby zadny timeout nebezi:
        /// „nemam podle ceho merit" nesmi znamenat „vyprselo".</para>
        /// </summary>
        public void StartMission()
        {
            lock (gate)
            {
                if (phase != TrackPhase.Idle) return;

                // MUSI to byt TimeBase (ne DateTime.UtcNow): lastTime pochazi z razitek zprav,
                // ktera jsou z TimeBase, a michanim zakladen by ElapsedSec vyslo o offset zony
                // mimo. Viz TimeBase a CLAUDE.md.
                var stamp = lastTime == default ? Common.TimeBase.Now : lastTime;
                anchored = false;
                missionStartedAt = stamp;
                EnterPhase(TrackPhase.AwaitingEStop, stamp);
            }
        }

        /// <summary>Start mise s <b>explicitnim casem</b> v hodinach dat — pro testy a prehravani.</summary>
        public void StartMission(DateTime now)
        {
            lock (gate)
            {
                if (phase != TrackPhase.Idle) return;

                anchored = true;
                missionStartedAt = now;
                lastTime = now;
                EnterPhase(TrackPhase.AwaitingEStop, now);
            }
        }

        /// <summary>
        /// Preruseni mise — z <b>kazdeho</b> stavu. Zastavuje <b>tvrde</b> (<c>Cancel()</c> a hned
        /// <c>Regulator = null</c>): tady je zastaveni dulezitejsi nez plynulost.
        /// </summary>
        public void Abort(string reason)
        {
            lock (gate)
            {
                if (phase == TrackPhase.Aborted) return;

                abortReason = reason ?? string.Empty;
                goals.Cancel();
                ClearRegulator();
                EnterPhase(TrackPhase.Aborted, lastTime);
                Trace.WriteLine("Track: mise PRERUSENA - " + abortReason);
            }
        }

        // ---------------- Vstupy ----------------

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            try
            {
                switch (msg)
                {
                    case MotorStateBase mot: OnMotors(mot, mot.TimeStamp); break;
                    case GlobalNavMsg nav: OnGlobalNav(nav); break;
                }
            }
            // ⚠️ Trace, ne Debug: v Release buildu (a ten bezi na zarizeni) by Debug.WriteLine
            // nezanechal po poruche zadnou stopu. Viz CLAUDE.md.
            catch (Exception ex) { Trace.WriteLine($"TrackMission: {ex}"); }
        }

        /// <summary>
        /// Stav motoru: nouzove zastaveni a „stoji uz robot?". <b>Tady se mise rozjizdi</b>
        /// (uvolneni stopu) a tady se po zastaveni zahazuje regulator.
        ///
        /// <para>Verejne schvalne: takhle jde automat prohnat zaznamem i z testu BEZ vlakna.</para>
        /// </summary>
        public void OnMotors(IMotorState motors, DateTime now)
        {
            lock (gate)
            {
                Advance(now);

                emergencyStop = motors != null && motors.IsEmergencyStop;
                // Chybejici stav motoru se pocita jako STOJICI (bezpecnejsi smer) a rychlosti se
                // porovnavaji na PRESNOU nulu: LeftWheelSpeed je z pristustku enkoderu, ne
                // filtrovana hodnota, takze kolem nuly nesumi (vzor z Robotouru).
                standing = motors == null || (motors.LeftWheelSpeed == 0 && motors.RightWheelSpeed == 0);

                switch (phase)
                {
                    case TrackPhase.AwaitingEStop:
                    case TrackPhase.Finished:
                        // Teprve kdyz robot STOJI, zahodit regulator, aby se nemohlo nic rozjet.
                        // Do te doby dobrzduje rizene po posledni draze.
                        if (standing && !regulatorCleared)
                        {
                            ClearRegulator();
                            regulatorCleared = true;
                        }

                        if (phase == TrackPhase.AwaitingEStop && emergencyStop)
                            EnterPhase(TrackPhase.AwaitingEStopRelease, now);
                        break;

                    case TrackPhase.AwaitingEStopRelease:
                        // Uvolneni stopu je pokyn „jed".
                        if (!emergencyStop) Depart(now);
                        break;
                }
            }
        }

        /// <summary>
        /// Hlaseni globalni navigace. <c>Arrived</c> posouva na dalsi misto, <c>NoRoute</c> misi
        /// prerusi (zotavovaci manevr neexistuje, takze zastaveni je jedina bezpecna odpoved).
        /// </summary>
        public void OnGlobalNav(GlobalNavMsg nav)
        {
            if (nav == null) return;
            lock (gate)
            {
                Advance(nav.TimeStamp);
                if (phase != TrackPhase.Driving) return;

                switch ((GlobalNavStatus)nav.Status)
                {
                    case GlobalNavStatus.Arrived: Arrive(nav.TimeStamp); break;
                    case GlobalNavStatus.NoRoute:
                        Abort($"na misto {pointIndex + 1}/{plan.Count} "
                              + $"({plan.PointText(pointIndex)}) nevede po siti trasa (NoRoute)");
                        break;
                }
            }
        }

        /// <summary>Beh casu: timeout jizdy a periodicka <see cref="TrackMsg"/>. Volatelne z testu.</summary>
        public void Tick(DateTime now)
        {
            lock (gate) { Advance(now); }
        }

        // ---------------- Vnitrek ----------------

        /// <summary>Posun casu: timeout jizdy a periodicka zprava.</summary>
        private void Advance(DateTime now)
        {
            if (now > lastTime) lastTime = now;

            // Prvni udaj v hodinach dat ukotvi mereni casu (viz StartMission()).
            if (!anchored && phase != TrackPhase.Idle)
            {
                anchored = true;
                missionStartedAt = now;
                phaseEnteredAt = now;
            }

            // ⚠️ Timeout ma JEN jizda. Stavy pod nouzovym zastavenim se nesmi utnout: ceka se na
            // cloveka, jak dlouho je potreba.
            if (phase == TrackPhase.Driving && anchored && config.DrivingTimeoutSec > 0
                && (now - phaseEnteredAt).TotalSeconds > config.DrivingTimeoutSec)
            {
                timeouts++;
                Abort($"timeout jizdy k mistu {pointIndex + 1}/{plan.Count} "
                      + $"(limit {config.DrivingTimeoutSec:F0} s)");
                return;
            }

            if ((now - lastMessageAt).TotalSeconds >= config.MessagePeriodSec) EmitState(now);
        }

        /// <summary>
        /// Odjezd na prvni misto po uvolneni nouzoveho zastaveni.
        /// </summary>
        private void Depart(DateTime now)
        {
            pointIndex = 0;
            lap = 1;
            if (!SetTarget(now)) return;
            EnterPhase(TrackPhase.Driving, now);
        }

        /// <summary>
        /// Dosazeni mista. <b>Mezi body se nezastavuje</b> — jen se prepne cil, protoze zadani
        /// mise zni „az misto dosahne, pojede na dalsi". <c>Cancel()</c> by robota zbytecne
        /// dobrzdil a zase rozjel.
        ///
        /// <para>Po poslednim bode se bud zacne dalsi kolo (<c>repeat</c>), nebo mise skonci.</para>
        /// </summary>
        private void Arrive(DateTime now)
        {
            reached++;
            Trace.WriteLine($"Track: dosazeno misto {pointIndex + 1}/{plan.Count} "
                            + $"({plan.PointText(pointIndex)}), celkem {reached}.");

            if (pointIndex + 1 < plan.Count)
            {
                pointIndex++;
            }
            else if (plan.Repeat)
            {
                // `repeat`: znovu od prvniho bodu. Kolo se pocita, aby slo ze zaznamu poznat,
                // kolikrat robot trasu objel.
                pointIndex = 0;
                lap++;
                Trace.WriteLine($"Track: repeat -> zacina kolo {lap}.");
            }
            else
            {
                // Konec: cil se zrusi, cimz robot RIZENE dobrzdi po draze, ktera uz existuje;
                // Regulator = null nastavi az OnMotors, kdyz robot skutecne stoji.
                goals.Cancel();
                regulatorCleared = false;
                EnterPhase(TrackPhase.Finished, now);
                Trace.WriteLine($"Track: HOTOVO - objeto {reached} mist.");
                return;
            }

            // Nove misto: znovu se prichycuje a zkousi dosazitelnost, protoze trasa se pocita
            // z aktualni polohy robota (ta uz je jina nez pri predchozim bodu).
            if (!SetTarget(now)) return;
            EnterPhase(TrackPhase.Driving, now);
        }

        /// <summary>
        /// Zada aktualni misto jako cil globalni navigace. Vraci <c>false</c>, kdyz se misto
        /// nepodarilo prijmout (mise je pak uz <see cref="TrackPhase.Aborted"/>).
        /// </summary>
        private bool SetTarget(DateTime now)
        {
            var raw = plan.Points[pointIndex];
            var target = NextTarget(raw, out activeOffRoadM, out activeRouteLengthM,
                                    out string reject);
            if (target == null)
            {
                Abort($"misto {pointIndex + 1}/{plan.Count} ({plan.PointText(pointIndex)}): "
                      + reject);
                return false;
            }

            activeTarget = target;
            goals.SetGoal(target);
            Trace.WriteLine($"Track: cil {pointIndex + 1}/{plan.Count} "
                            + $"({plan.PointText(pointIndex)}), prichyceno o "
                            + $"{activeOffRoadM:F1} m, trasa {activeRouteLengthM:F0} m.");
            return true;
        }

        /// <summary>
        /// <b>Nejblizsi misto na siti cest</b> k bodu ze souboru — a zaroven kontrola, ze na nej
        /// vede trasa.
        ///
        /// <para><b>Proc se cil prichycuje:</b> souradnice v souboru je bod, ktery si clovek klikl
        /// na mape, ne bod na ceste. Robot tam nemuze dojet — jede po siti — a <c>Navigator</c>
        /// pritom meri dojezd proti <b>surovemu</b> cili, takze pri odsazeni vetsim nez dojezdovy
        /// radius by <c>Arrived</c> nenastalo NIKDY a mise by u prvniho bodu uvizla navzdy (past
        /// nalezena u Robotouru 27. 8. 2026). Zadani mise („najde nejblizsi misto na mape a k nemu
        /// pojede") je tedy soucasne tou opravou.</para>
        ///
        /// <para>Bez <see cref="routes"/> (v testech) se jede na surove souradnice — chovani je pak
        /// stejne jako pred prichycenim a je to <b>videt</b>, protoze odstup vyjde nula.</para>
        /// </summary>
        private LLA NextTarget(LLA raw, out double offRoadM, out double routeLengthM,
                               out string reject)
        {
            offRoadM = 0;
            routeLengthM = 0;
            reject = string.Empty;

            if (routes == null) return raw;

            var probe = routes.Probe(raw);
            offRoadM = probe.OffRoadM;
            routeLengthM = probe.LengthM;

            if (!probe.Reachable)
            {
                reject = "na sit se nepodarilo najit trasu (Reachable = false)";
                return null;
            }
            if (probe.OffRoadM > config.MaxPointOffRoadM)
            {
                // ⚠️ Prerusit, ne preskocit. Viz hlavicka tridy, pojistka 2.
                reject = $"lezi {probe.OffRoadM:F0} m od site cest, limit je "
                         + $"{config.MaxPointOffRoadM:F0} m. Je ten bod na ceste?";
                return null;
            }

            // SnappedTarget muze byt null (zkouska bez site) - pak se jede na surovy bod.
            return probe.SnappedTarget ?? raw;
        }

        /// <summary>Prechod do faze; razitko je vzdy v hodinach dat.</summary>
        private void EnterPhase(TrackPhase next, DateTime now)
        {
            phase = next;
            phaseEnteredAt = now;
            EmitState(now);
        }

        /// <summary>Zahodi regulator (kdyz je drzitel k dispozici) — <c>null</c> = stat.</summary>
        private void ClearRegulator()
        {
            if (control != null) control.Regulator = null;
        }

        /// <summary>Vyrobi a posle <see cref="TrackMsg"/>.</summary>
        private void EmitState(DateTime now)
        {
            lastMessageAt = now;
            var raw = pointIndex >= 0 && pointIndex < plan.Count ? plan.Points[pointIndex] : null;

            var msg = new TrackMsg
            {
                Phase = (int)phase,
                PointIndex = pointIndex,
                PointCount = plan.Count,
                Lap = lap,
                Repeat = plan.Repeat,
                Reached = reached,
                RawLatitude = raw?.Latitude ?? 0,
                RawLongitude = raw?.Longitude ?? 0,
                TargetLatitude = activeTarget?.Latitude ?? 0,
                TargetLongitude = activeTarget?.Longitude ?? 0,
                OffRoadM = activeOffRoadM,
                RouteLengthM = activeRouteLengthM,
                AbortReason = abortReason,
                ElapsedSec = missionStartedAt == default || lastTime <= missionStartedAt
                             ? 0 : (lastTime - missionStartedAt).TotalSeconds,
                TimeStamp = now,
            };

            LastMessage = msg;
            EmitDerived(msg);
        }
    }
}
