using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Navigation;
using ARBot.Common.Models;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// <b>Mise Robotour: stavovy automat soutezni jizdy.</b> Viz doc/robotour-mission.md.
    ///
    /// <para>Zapamatuje si start/depo, precte z prave kamery QR kod s mistem nakladky, dojede tam a
    /// zastavi, probehne nakladka a precteni dalsiho kodu s mistem vykladky, dojede tam, vylozi a
    /// <b>vrati se do depa</b>.</para>
    ///
    /// <para>Ridi <see cref="GlobalNavigator"/> zadavanim <b>LLA cilu</b> — sama nezna ani graf cest,
    /// ani occupancy grid, ani regulatory. Cesta k cili, objizdeni a detekce zaseku jsou vrstvy pod
    /// ni. Sourozencem je <see cref="FreeRunMission"/>; <b>spolecna abstrakce misi se zamerne
    /// nezavadi</b>, protoze spolecneho je mezi nimi jen to, ze obe produkuji cil — a to na jinou
    /// vrstvu (FreeRun mrkev pro lokalni planovac, tato mise LLA pro globalni navigaci).</para>
    ///
    /// <para><b>Tri pojistky servisniho okna</b>, na kterych cely navrh stoji:</para>
    /// <list type="number">
    /// <item>Robot <b>nikdy neskenuje, kdyz muze jet</b> — scanner je zapnuty vyhradne ve stavu
    /// <see cref="RobotourPhase.Servicing"/>, tedy pod drzenym nouzovym zastavenim. Obsluha stojici
    /// u robotu s krabici v ruce ma fyzickou garanci, ne jen softwarovou.</item>
    /// <item><b>Cil pousti dal jen strojove kontroly</b> (format kodu, vzdalenost od depa,
    /// dosazitelnost v grafu). <b>Zadne potvrzeni operatorem</b> — mise je simulace autonomniho
    /// doruceni, takze robot musi ukol vykonat bez zasahu operatora a jediny, kdo s nim interaguje,
    /// je <b>odesilatel</b> v miste nakladky a <b>odberatel</b> v miste vykladky, a to vyhradne
    /// <b>QR kodem a stop tlacitkem na robotu</b> (rozhodnuti autora 26. 8. 2026).</item>
    /// <item><b>Nouzove zastaveni za jizdy tento automat neposune</b> — o zastaveni se stara
    /// <c>ControlLoop</c> a po uvolneni se jede dal k temuz cili. Mise o stopu za jizdy nemusi vedet.</item>
    /// </list>
    ///
    /// <para><b>Zadny prechod neni implicitni</b> — vzdy z konkretni podminky, aby se ve zaznamu dalo
    /// dohledat, proc se mise posunula.</para>
    /// </summary>
    public sealed class RobotourMission : MessageProcessor
    {
        private readonly object gate = new object();

        private readonly IGlobalGoalSink goals;
        private readonly IPositionInitializer fusion;
        private readonly GeoReference origin;
        private readonly IRegulatorHolder control;
        private readonly IQrScannerControl scanner;
        private readonly IMissionTargetParser parser;
        private readonly IRouteProbe routes;
        private readonly RobotourConfig config;

        // --- stav automatu ---
        private RobotourPhase phase = RobotourPhase.Idle;
        private RobotourStop stop = RobotourStop.Depot;
        private DateTime missionStartedAt;
        private DateTime phaseEnteredAt;
        /// <summary>
        /// Je uz cas mise ukotveny v hodinach DAT? Automat mericky bezi na casech zprav, ne na
        /// hodinach stroje — jinak by pri prehravani zaznamu (a v testech) merily timeouty rozdil
        /// dvou nesouvisejicich hodin a vyprsely by hned.
        /// </summary>
        private bool anchored;
        private DateTime servicingSince;
        private DateTime lastTime;
        private DateTime lastMessageAt;

        private LLA depot, pickup, drop;
        private string pickupCodeText = string.Empty, dropCodeText = string.Empty;

        private LLA acceptedTarget;
        private string acceptedCodeText = string.Empty;
        private double acceptedRouteLengthM;
        private double acceptedDistanceFromDepotM;

        /// <summary>
        /// Jak daleko od site lezel <b>surovy</b> cil z kodu [m] — tedy o kolik se posunul
        /// prichycenim. Do zpravy to jde proto, aby slo <c>MaxTargetOffRoadM</c> nastavit
        /// z namerenych bezu misto usudkem.
        /// </summary>
        private double acceptedOffRoadM;

        private bool emergencyStop, standing = true;
        private bool regulatorCleared;
        private bool codeNotSeen;
        private string abortReason = string.Empty;
        private int codesRead, codesRejected, timeouts;

        // --- okno fixu v depu ---
        private readonly List<LLA> fixWindow = new List<LLA>();
        private DateTime fixWindowStart;

        // Diagnostika armovani: proc se (ne)pokracuje. Jde do zpravy i do UI - bez toho mise
        // v ArmingAtDepot stoji a nikdo nevi proc.
        private bool hasFixInfo, fixQualityOk;
        private int fixSatellites, fixSamples;
        private double fixHdop, fixSpreadM;

        // Duvod zamitnuti posledniho kodu. Bez nej se tri uplne jine situace (nesrozumitelny kod /
        // prilis daleko / bez trasy) tvari jako "nic se nestalo" a vypada to, ze se kod NEPRECETL.
        private string rejectReason = string.Empty, rejectedCodeText = string.Empty;
        private double rejectedDistanceM;

        /// <param name="goals">Prijemce LLA cilu (globalni navigace).</param>
        /// <param name="fusion">Inicializace polohy filtru (v depu).</param>
        /// <param name="origin">Pocatek lokalni ENU roviny — tentyz, se kterym pocita fuze.</param>
        /// <param name="control">Drzitel regulatoru; <c>null</c> = mise regulator nezahazuje
        /// (pouzitelne v testech, v aplikaci se predava <c>ControlLoop</c>).</param>
        /// <param name="scanner">Vypinac scanneru QR; <c>null</c> = mise scanner neridi.</param>
        /// <param name="parser">Parser cile z kodu; <c>null</c> = <see cref="GeoUriTargetParser"/>.</param>
        /// <param name="routes">Zkouska dosazitelnosti cile; <c>null</c> = kontrola se preskoci
        /// (a <c>NoRoute</c> se pak zjisti az za jizdy).</param>
        /// <param name="config">Konfigurace; <c>null</c> = vychozi.</param>
        public RobotourMission(IGlobalGoalSink goals, IPositionInitializer fusion, GeoReference origin,
                               IRegulatorHolder control = null, IQrScannerControl scanner = null,
                               IMissionTargetParser parser = null, IRouteProbe routes = null,
                               RobotourConfig config = null)
            : base(OverflowPolicy.DropOldest, capacity: 32)
        {
            this.goals = goals ?? throw new ArgumentNullException(nameof(goals));
            this.fusion = fusion ?? throw new ArgumentNullException(nameof(fusion));
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));
            this.control = control;
            this.scanner = scanner;
            this.parser = parser ?? new GeoUriTargetParser();
            this.routes = routes;
            this.config = config ?? new RobotourConfig();
            this.config.Validate();
        }

        // ---------------- Diagnostika pro UI a testy ----------------

        /// <summary>Nastaveni, se kterym mise pracuje.</summary>
        public RobotourConfig Config => config;

        /// <summary>Faze automatu.</summary>
        public RobotourPhase Phase { get { lock (gate) return phase; } }

        /// <summary>Ktere zastaveni se obsluhuje (plati v servisnim okne).</summary>
        public RobotourStop CurrentStop { get { lock (gate) return stop; } }

        /// <summary>Zapamatovane depo, nebo <c>null</c>. Jediny cil, ktery nejde z QR kodu.</summary>
        public LLA Depot { get { lock (gate) return depot; } }

        /// <summary>
        /// Naposledy <b>prijaty</b> cil z QR kodu (uz prosel strojovymi kontrolami), nebo
        /// <c>null</c>. Prijeti je automaticke — potvrzeni operatorem neexistuje.
        /// </summary>
        public LLA LastAcceptedTarget { get { lock (gate) return acceptedTarget; } }

        /// <summary>Delka trasy na naposledy prijaty cil [m]; ukazuje se v UI a jde do zaznamu.</summary>
        public double AcceptedRouteLengthM { get { lock (gate) return acceptedRouteLengthM; } }

        /// <summary>
        /// Posledni PRECTENY text kodu, doslova — i kdyz se zamitl. Bez toho by nebylo videt, co
        /// robot precetl a proc se neposunul.
        /// </summary>
        public string LastCodeText { get; private set; } = string.Empty;

        /// <summary>Kolik kodu se precetlo.</summary>
        public int ReadCodes { get { lock (gate) return codesRead; } }

        /// <summary>Kolik kodu se zamitlo (nesrozumitelne, prilis daleko, bez trasy).</summary>
        public int RejectedCodes { get { lock (gate) return codesRejected; } }

        /// <summary>Kolik timeoutu vyprselo.</summary>
        public int Timeouts { get { lock (gate) return timeouts; } }

        /// <summary>Hlasi mise „kod nevidim"? UI to musi ukazat — resenim je obsluha.</summary>
        public bool CodeNotSeen { get { lock (gate) return codeNotSeen; } }

        /// <summary>Duvod preruseni; prazdny, kdyz mise nebyla prerusena.</summary>
        public string AbortReason { get { lock (gate) return abortReason; } }

        /// <summary>Posledni vyrobena zprava (diagnostika pro UI a telemetrii).</summary>
        public MissionMsg LastMessage { get; private set; }

        // ---------------- Prikazy obsluhy ----------------

        /// <summary>
        /// „Start mise" z UI. Prechod <see cref="RobotourPhase.Idle"/> →
        /// <see cref="RobotourPhase.ArmingAtDepot"/>; jina faze prikaz ignoruje (mise uz bezi).
        ///
        /// <para><b>Jmenuje se to <c>StartMission</c>, ne <c>Start</c>, zamerne:</b> zdedene
        /// <c>MessageTarget.Start()</c> spousti VLAKNO stupne (dela to runtime pri startu) a
        /// splest tyhle dve veci by znamenalo bud mise, ktera se sama rozjede, nebo stupen, ktery
        /// nikdy nezacne odebirat zpravy.</para>
        ///
        /// <para>Cas mise se <b>ukotvi az prvnim udajem, ktery prijde</b> (<see cref="anchored"/>) —
        /// obsluha macka tlacitko, ale merit se musi v hodinach dat. Do te doby zadny timeout nebezi:
        /// „nemam podle ceho merit" nesmi znamenat „vyprselo".</para>
        /// </summary>
        public void StartMission()
        {
            lock (gate)
            {
                if (phase != RobotourPhase.Idle) return;

                // Razitko pro zpravu: cas poslednich dat, a dokud zadna nejsou, hodiny aplikace.
                // MUSI to byt TimeBase (ne DateTime.UtcNow): lastTime pochazi z razitek zprav, ktera
                // jsou z TimeBase, a michanim zakladen by missionStartedAt (a tim ElapsedSec) vyslo
                // o offset zony mimo. Viz TimeBase a CLAUDE.md.
                var stamp = lastTime == default ? ARBot.Common.Common.TimeBase.Now : lastTime;
                anchored = false;
                missionStartedAt = stamp;
                fixWindow.Clear();
                EnterPhase(RobotourPhase.ArmingAtDepot, stamp);
            }
        }

        /// <summary>
        /// Start mise s <b>explicitnim casem</b> v hodinach dat — pro testy a prehravani zaznamu,
        /// kde se hodiny stroje nesmi michat do mereni.
        /// </summary>
        public void StartMission(DateTime now)
        {
            lock (gate)
            {
                if (phase != RobotourPhase.Idle) return;

                anchored = true;
                missionStartedAt = now;
                lastTime = now;
                fixWindow.Clear();
                EnterPhase(RobotourPhase.ArmingAtDepot, now);
            }
        }

        /// <summary>
        /// Potvrzeni obsluhou ve servisnim okne: „tento cil je spravny" / „nakladka hotova".
        ///
        /// <para><b>Bez nej se cil neprijme</b> — je to druha, nezavisla pojistka vedle strojovych
        /// kontrol. Kdyz se v tomto okne kod ceka a jeste zadny neprosel, prikaz nic nedela.</para>
        /// </summary>
        /// <summary>
        /// Prijme cil z precteneho kodu a posune misi na cekani, az clovek <b>uvolni stop</b>.
        ///
        /// <para><b>Zadne potvrzeni operatorem tu neni a byt nema.</b> Do 26. 8. 2026 to byl krok
        /// <c>Confirm()</c> s tlacitkem v UI; autor ho zrusil, protoze mise je simulace autonomniho
        /// doruceni: robot ma ukol vykonat bez zasahu operatora a interaguji s nim jen odesilatel
        /// a odberatel — <b>QR kodem a stop tlacitkem</b>. Pojistkou proti chybnemu dekodovani
        /// zustavaji strojove kontroly, ktere kod prosel jeste pred timhle volanim.</para>
        /// </summary>
        private void AcceptTarget(LLA target, DateTime now)
        {
            if (stop == RobotourStop.Depot)
            {
                pickup = target;
                pickupCodeText = acceptedCodeText;
            }
            else
            {
                drop = target;
                dropCodeText = acceptedCodeText;
            }

            // Precteno -> uz neni co skenovat. Zustava to zapnute jen do teto chvile.
            SetScanner(false);
            EnterPhase(RobotourPhase.AwaitingEStopRelease, now);
        }

        /// <summary>
        /// Preruseni mise — z <b>kazdeho</b> stavu. Zastavuje <b>tvrde</b> (<c>Cancel()</c> a hned
        /// <c>Regulator = null</c>): tady je zastaveni dulezitejsi nez plynulost, na rozdil od
        /// dojezdu na stanoviste.
        /// </summary>
        public void Abort(string reason)
        {
            lock (gate)
            {
                if (phase == RobotourPhase.Aborted) return;

                abortReason = reason ?? string.Empty;
                goals.Cancel();
                ClearRegulator();
                SetScanner(false);
                EnterPhase(RobotourPhase.Aborted, lastTime);
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
                    case GPSState gps: OnGps(gps); break;
                    case MotorStateBase mot: OnMotors(mot, mot.TimeStamp); break;
                    case QrCodeMsg qr: OnQrCode(qr); break;
                    case GlobalNavMsg nav: OnGlobalNav(nav); break;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"RobotourMission: {ex}"); }
        }

        /// <summary>
        /// Fix z GPS. Zajima jen stav <see cref="RobotourPhase.ArmingAtDepot"/>, kde se ceka na
        /// <b>kvalitni fix drzeny neprerusene</b> po <see cref="RobotourConfig.DepotFixSec"/> a jeho
        /// prumerem se inicializuje fuze.
        ///
        /// <para>Verejne schvalne: takhle jde automat prohnat zaznamem i z testu BEZ vlakna.</para>
        /// </summary>
        public void OnGps(GPSState gps)
        {
            if (gps == null) return;
            lock (gate)
            {
                Advance(gps.TimeStamp);
                if (phase != RobotourPhase.ArmingAtDepot) return;

                // Diagnostika PRVNI, jeste nez se fix pripadne zamitne: „ceka se na kvalitni fix"
                // musi jit precist s duvodem, jinak mise stoji a nikdo nevi proc (26. 8. 2026).
                hasFixInfo = true;
                fixSatellites = gps.NumberOfSatellites;
                fixHdop = gps.Hdop;
                fixQualityOk = FixQualityOk(gps);

                if (!fixQualityOk)
                {
                    // Preruseni serie: „drzeny neprerusene" znamena neprerusene.
                    fixWindow.Clear();
                    fixSamples = 0;
                    fixSpreadM = 0;
                    EmitState(gps.TimeStamp);
                    return;
                }

                if (fixWindow.Count == 0) fixWindowStart = gps.TimeStamp;

                // GPSState drzi RADIANY, tedy tutez jednotku jako LLA (od 26. 8. 2026). Prave tady
                // se 26. 8. projevila ta pred-tim platna zamena: mise uvizla v ArmingAtDepot,
                // protoze body v okne byly desitky radianu od sebe a rozptyl vysel astronomicky.
                fixWindow.Add(new LLA(gps.Latitude, gps.Longitude));

                // Rozptyl se pocita PRUBEZNE, ne teprve u plneho okna: je to ten udaj, ktery
                // vysvetluje, proc se nikam nepokracuje, a cekat s nim do konce okna znamena
                // nechat obsluhu 5 s hadat.
                fixSamples = fixWindow.Count;
                var running = MeanOf(fixWindow);
                fixSpreadM = fixWindow.Count > 1 ? RmsDeviationM(fixWindow, running) : 0;

                if ((gps.TimeStamp - fixWindowStart).TotalSeconds < config.DepotFixSec)
                {
                    EmitState(gps.TimeStamp);
                    return;
                }

                // Robot stoji, takze prumer je poctivejsi nez jediny vzorek — a rozptyl z okna da
                // zdarma jak kontrolu kvality, tak realistickou std pro filtr.
                //
                // Kriterium je EFEKTIVNI odchylka (RMS), ne maximalni. Maximum s rostoucim n ROSTE
                // i u dokonale gaussovskeho sumu, takze by delsi cekani kriterium PRITUZOVALO -
                // presne naopak, nez ma. RMS naopak konverguje k sigma senzoru, takze prah je
                // fyzikalne cteny udaj ("sum fixu musi byt pod X"), ne funkce delky okna.
                //
                // Je to zaroven TATAZ velicina, kterou se pak hlasi filtru: co projde jako
                // "dost tichy fix", tim se filtr taky inicializuje.
                var mean = running;
                double spread = fixSpreadM;

                if (spread > config.MaxSpreadM)
                {
                    // Velky rozptyl = cekej dal, i kdyz kazdy jednotlivy fix vypadal kvalitne.
                    fixWindow.Clear();
                    fixSamples = 0;
                    EmitState(gps.TimeStamp);
                    return;
                }

                var local = origin.ToLocal(mean);
                // Hlasi se sum JEDNOHO vzorku, ne standardni chyba prumeru (sigma/sqrt(n)).
                // Zamerne konzervativni: prumerovani stahuje NAHODNOU cast sumu, ale ne BIAS fixu
                // (multipath, ionosfera), a ten je na teto skale dominantni. Tvrdit filtru
                // sigma/sqrt(n) by byla tataz nepoctivost sigmy, jaka se resila u korelace s mapou.
                double std = Math.Max(spread, config.MinInitStdM);
                fusion.InitializePosition(local.X, local.Y, std, gps.TimeStamp);

                depot = mean;
                fixWindow.Clear();

                stop = RobotourStop.Depot;
                regulatorCleared = false;
                EnterPhase(RobotourPhase.AwaitingEStop, gps.TimeStamp);
            }
        }

        /// <summary>
        /// Stav motoru: nese <b>nouzove zastaveni</b> a informaci, jestli robot stoji.
        ///
        /// <para>Nouzove zastaveni je signal mise <b>jen ve stavech, ktere na nej cekaji</b>
        /// (<see cref="RobotourPhase.AwaitingEStop"/>, <see cref="RobotourPhase.AwaitingEStopRelease"/>).
        /// Zmacknuti stopu za jizdy tedy automat neposune.</para>
        /// </summary>
        public void OnMotors(IMotorState motors, DateTime now)
        {
            lock (gate)
            {
                Advance(now);

                emergencyStop = motors != null && motors.IsEmergencyStop;
                // Chybejici stav motoru se pocita jako STOJICI (bezpecnejsi smer) a rychlosti se
                // porovnavaji na PRESNOU nulu: LeftWheelSpeed je z pristustku enkoderu, ne
                // filtrovana hodnota, takze kolem nuly nesumi (viz doc/robotour-mission.md).
                standing = motors == null || (motors.LeftWheelSpeed == 0 && motors.RightWheelSpeed == 0);

                switch (phase)
                {
                    case RobotourPhase.AwaitingEStop:
                    case RobotourPhase.Finished:
                        // Druha faze zastaveni: teprve kdyz robot STOJI, zahodit regulator, aby se
                        // nemohlo nic rozjet. Do te doby dobrzduje rizene po posledni draze.
                        if (standing && !regulatorCleared)
                        {
                            ClearRegulator();
                            regulatorCleared = true;
                        }

                        if (phase == RobotourPhase.AwaitingEStop && emergencyStop)
                        {
                            servicingSince = now;
                            codeNotSeen = false;

                            // Kde se kod NECTE (vykladka), neni v servisnim okne co delat - ceka se
                            // uz jen na uvolneni stopu. „Vylozeno" JE to uvolneni; zadne potvrzeni
                            // v UI neexistuje (viz AcceptTarget).
                            if (!CodeExpected(stop))
                            {
                                EnterPhase(RobotourPhase.AwaitingEStopRelease, now);
                                break;
                            }

                            SetScanner(true);
                            EnterPhase(RobotourPhase.Servicing, now);
                        }
                        break;

                    case RobotourPhase.Servicing:
                        // Clovek pustil stop, aniz kod ukazal. Nesmi to znamenat odjezd bez cile ani
                        // zaseknuti - ceka se na dalsi pokus. A hlavne: scanner MUSI jit dolu, aby
                        // platilo „skenuje se vyhradne pod drzenym stopem".
                        if (!emergencyStop)
                        {
                            SetScanner(false);
                            EnterPhase(RobotourPhase.AwaitingEStop, now);
                        }
                        break;

                    case RobotourPhase.AwaitingEStopRelease:
                        if (!emergencyStop) Depart(now);
                        break;
                }
            }
        }

        /// <summary>
        /// Precteny QR kod. Zajima jen servisni okno, ve kterem se kod ceka; jinde se ignoruje
        /// (u vykladky se necte nic).
        ///
        /// <para>Strojove kontroly (vzdalenost od depa, dosazitelnost v grafu) se delaji <b>tady</b>,
        /// jeste nez se cil vubec nabidne obsluze.</para>
        /// </summary>
        public void OnQrCode(QrCodeMsg code)
        {
            if (code == null) return;
            lock (gate)
            {
                Advance(code.TimeStamp);
                if (phase != RobotourPhase.Servicing || !CodeExpected(stop)) return;

                // Text jde do zaznamu DOSLOVA, i kdyz ho pak zamitneme.
                LastCodeText = code.Text ?? string.Empty;
                codesRead++;

                var target = parser.Parse(code.Text);
                if (target == null)
                {
                    Reject(code.Text, 0, "kod je nesrozumitelny (necekany format; ceka se geo:sirka,delka)",
                           code.TimeStamp);
                    return;
                }

                double distanceFromDepot = 0;
                if (depot != null)
                {
                    distanceFromDepot = GreatCircle.Sphere.Distance(depot, target);
                    if (distanceFromDepot > config.MaxTargetDistanceM)
                    {
                        Reject(code.Text, distanceFromDepot,
                               $"cil je prilis daleko: {distanceFromDepot:F0} m od depa, "
                               + $"limit je {config.MaxTargetDistanceM:F0} m",
                               code.TimeStamp);
                        return;
                    }
                }

                // Cil z kodu je misto, kde stoji clovek s krabici - ne bod na ceste. Robot jezdi
                // po siti, takze se cil PRICHYTI na nejblizsi hranu a jede se na ten prumet;
                // odstup od site je pritom kontrola, jestli to jeste dava smysl.
                double routeLength = 0, offRoad = 0;
                var goalTarget = target;
                if (routes != null)
                {
                    var probe = routes.Probe(target);

                    // Prichytit jde COKOLIV (NearestEdge limit nema), takze bez teto kontroly by
                    // cil uprostred pole vysel jako dosazitelny a robot by odjel na cestu uplne
                    // jinam, nez kde clovek stoji - a ohlasil dojezd.
                    if (probe.OffRoadM > config.MaxTargetOffRoadM)
                    {
                        Reject(code.Text, probe.OffRoadM,
                               $"cil je prilis daleko od cesty: {probe.OffRoadM:F0} m od nejblizsi "
                               + $"hrany site, limit je {config.MaxTargetOffRoadM:F0} m",
                               code.TimeStamp);
                        return;
                    }

                    // Bez teto kontroly by se NoRoute zjistilo az za jizdy.
                    if (!probe.Reachable)
                    {
                        Reject(code.Text, distanceFromDepot,
                               "na cil nevede po siti zadna trasa (je mimo mapu?)", code.TimeStamp);
                        return;
                    }

                    routeLength = probe.LengthM;
                    offRoad = probe.OffRoadM;

                    // Prichyceny cil je ten, na ktery se jezdi: Navigator meri dojezd proti
                    // GoalField.GoalPoint (surovy cil), takze cil odsazeny vic nez o
                    // ArrivalRadiusMeters by NIKDY neohlasil Arrived a mise by uvizla v jizde.
                    if (probe.SnappedTarget != null) goalTarget = probe.SnappedTarget;
                }

                // Prijaty kod maze duvod zamitnuti - jinak by v panelu strasil stary.
                rejectReason = string.Empty;
                rejectedCodeText = string.Empty;
                rejectedDistanceM = 0;

                acceptedTarget = goalTarget;
                acceptedCodeText = LastCodeText;
                acceptedRouteLengthM = routeLength;
                acceptedDistanceFromDepotM = distanceFromDepot;
                acceptedOffRoadM = offRoad;
                codeNotSeen = false;

                // Kod prosel strojovymi kontrolami -> cil je PRIJATY a mise se posune sama.
                AcceptTarget(goalTarget, code.TimeStamp);
            }
        }

        /// <summary>
        /// Hlaseni globalni navigace. <c>Arrived</c> posouva automat na stanoviste,
        /// <c>NoRoute</c> misi prerusi (dnes neexistuje zotavovaci manevr, takze je zastaveni jedina
        /// bezpecna odpoved — viz otevrene ukoly v doc/robotour-mission.md).
        /// </summary>
        public void OnGlobalNav(GlobalNavMsg nav)
        {
            if (nav == null) return;
            lock (gate)
            {
                Advance(nav.TimeStamp);
                if (!IsDriving(phase)) return;

                switch ((GlobalNavStatus)nav.Status)
                {
                    case GlobalNavStatus.Arrived: Arrive(nav.TimeStamp); break;
                    case GlobalNavStatus.NoRoute:
                        Abort($"na cil nevede po siti trasa (NoRoute) ve fazi {phase}");
                        break;
                }
            }
        }

        /// <summary>
        /// Beh casu: timeouty stavu <b>bez cloveka v cyklu</b> a periodicka
        /// <see cref="MissionMsg"/>. Volatelne primo z testu.
        /// </summary>
        public void Tick(DateTime now)
        {
            lock (gate) { Advance(now); }
        }

        // ---------------- Vnitrek ----------------

        /// <summary>Posun casu: timeouty, hlaseni „kod nevidim" a periodicka zprava.</summary>
        private void Advance(DateTime now)
        {
            if (now > lastTime) lastTime = now;

            // Prvni udaj v hodinach dat ukotvi mereni casu (viz StartMission()).
            if (!anchored && phase != RobotourPhase.Idle)
            {
                anchored = true;
                missionStartedAt = now;
                phaseEnteredAt = now;
            }

            switch (phase)
            {
                case RobotourPhase.ArmingAtDepot:
                    if (Expired(config.ArmingTimeoutSec, now)) { TimedOut(now); return; }
                    break;

                case RobotourPhase.DrivingToPickup:
                case RobotourPhase.DrivingToDrop:
                case RobotourPhase.DrivingToDepot:
                    if (Expired(config.DrivingTimeoutSec, now)) { TimedOut(now); return; }
                    break;

                case RobotourPhase.Servicing:
                    // Stavy pod nouzovym zastavenim timeout NEMAJI — ceka se na obsluhu, jak dlouho
                    // je potreba. Jen se hlasi, ze kod neni videt, a skenuje se DAL.
                    if (CodeExpected(stop) && acceptedTarget == null
                        && (now - servicingSince).TotalSeconds > config.QrSearchSec)
                        codeNotSeen = true;
                    break;
            }

            if ((now - lastMessageAt).TotalSeconds >= config.MissionMessagePeriodSec)
                EmitState(now);
        }

        /// <summary>Vyprsel timeout dane faze? <c>0</c> = neomezovat.</summary>
        private bool Expired(double limitSec, DateTime now)
            => anchored && limitSec > 0 && (now - phaseEnteredAt).TotalSeconds > limitSec;

        /// <summary>
        /// Timeout stavu bez cloveka v cyklu. <b>Nikdy tiche zaseknuti</b> — mise nema zotavovaci
        /// manevr, takze jedina bezpecna odpoved je zastavit a nechat to na obsluze.
        /// </summary>
        private void TimedOut(DateTime now)
        {
            timeouts++;
            Abort($"timeout stavu {phase} (limit prekrocen v {now:HH:mm:ss})");
        }

        /// <summary>
        /// Dojezd na stanoviste. <b>Dve faze:</b> nejdriv se zrusi cil, coz robota <b>rizene</b>
        /// dobrzdi cestou, ktera uz existuje (nizsi smycka dojede po posledni draze a watchdog ji
        /// ukonci); <c>Regulator = null</c> se nastavi teprve, az robot stoji — to resi
        /// <see cref="OnMotors"/>.
        /// </summary>
        private void Arrive(DateTime now)
        {
            goals.Cancel();
            regulatorCleared = false;

            if (phase == RobotourPhase.DrivingToDepot)
            {
                EnterPhase(RobotourPhase.Finished, now);
                return;
            }

            stop = phase == RobotourPhase.DrivingToPickup ? RobotourStop.Pickup : RobotourStop.Drop;
            EnterPhase(RobotourPhase.AwaitingEStop, now);
        }

        /// <summary>
        /// Odjezd ze stanoviste po uvolneni nouzoveho zastaveni. <b>Navrat do depa je normalni cil</b>
        /// (<c>SetGoal</c>), ne zruseni cile — jede se k nemu po siti a uzavrene hrany zustavaji
        /// v platnosti.
        /// </summary>
        private void Depart(DateTime now)
        {
            LLA target;
            RobotourPhase next;

            switch (stop)
            {
                case RobotourStop.Depot: target = pickup; next = RobotourPhase.DrivingToPickup; break;
                case RobotourStop.Pickup: target = drop; next = RobotourPhase.DrivingToDrop; break;
                default: target = depot; next = RobotourPhase.DrivingToDepot; break;
            }

            if (target == null)
            {
                // Nemelo by se stat: bez cile se do AwaitingEStopRelease neda dostat. Kdyby ano, je
                // zastaveni jedina bezpecna odpoved.
                Abort($"chybi cil pro odjezd ze zastaveni {stop}");
                return;
            }

            goals.SetGoal(target);
            regulatorCleared = false;
            EnterPhase(next, now);
        }

        /// <summary>
        /// Zamitne precteny kod <b>s duvodem</b> a hned to ohlasi zpravou.
        ///
        /// <para>Duvod je podstatny: tri zamitnuti (nesrozumitelny kod / prilis daleko / bez trasy)
        /// se z pohledu obsluhy chovaji stejne, ale znamenaji uplne jine reseni. Bez nej to vypada,
        /// ze se kod vubec neprecetl (nalezeno pri praci s panelem 26. 8. 2026).</para>
        /// </summary>
        private void Reject(string text, double distanceM, string reason, DateTime now)
        {
            codesRejected++;
            rejectedCodeText = text ?? string.Empty;
            rejectedDistanceM = distanceM;
            rejectReason = reason;
            EmitState(now);
        }

        /// <summary>Ceka se v tomhle servisnim okne QR kod? U vykladky ne.</summary>
        private static bool CodeExpected(RobotourStop s)
            => s == RobotourStop.Depot || s == RobotourStop.Pickup;

        private static bool IsDriving(RobotourPhase p)
            => p == RobotourPhase.DrivingToPickup
               || p == RobotourPhase.DrivingToDrop
               || p == RobotourPhase.DrivingToDepot;

        /// <summary>Vstup do faze: orazitkuje ji a <b>vzdy</b> emituje zpravu (kazda zmena faze je v zaznamu).</summary>
        private void EnterPhase(RobotourPhase next, DateTime now)
        {
            phase = next;
            phaseEnteredAt = now;
            EmitState(now);
        }

        private void SetScanner(bool on)
        {
            if (scanner != null) scanner.Enabled = on;
        }

        private void ClearRegulator()
        {
            if (control != null) control.Regulator = null;
        }

        /// <summary>Kvalita fixu podle <see cref="GPSState"/> — kriteria z konfigurace.</summary>
        private bool FixQualityOk(GPSState gps)
            => gps.IsFixed
               && gps.NumberOfSatellites >= config.MinSatellites
               && gps.Hdop > 0 && gps.Hdop <= config.MaxHdop;

        /// <summary>Prumer fixu v okne (aritmeticky ve stupnich — okno je metrove, takze to staci).</summary>
        private static LLA MeanOf(List<LLA> window)
        {
            double lat = 0, lon = 0;
            foreach (var p in window) { lat += p.Latitude; lon += p.Longitude; }
            return new LLA(lat / window.Count, lon / window.Count);
        }

        /// <summary>
        /// Efektivni (RMS) odchylka fixu od prumeru [m] — <b>zaroven</b> kriterium kvality okna
        /// a <c>std</c> hlasena filtru. Ze je to tataz velicina, je zamer: co projde jako „dost
        /// tichy fix", tim se filtr taky inicializuje.
        /// </summary>
        private static double RmsDeviationM(List<LLA> window, LLA mean)
        {
            double sum = 0;
            foreach (var p in window)
            {
                double d = GreatCircle.Sphere.Distance(mean, p);
                sum += d * d;
            }
            return Math.Sqrt(sum / window.Count);
        }

        /// <summary>Postavi snimek stavu, ulozi ho jako <see cref="LastMessage"/> a posle do Stream.</summary>
        private void EmitState(DateTime now)
        {
            var state = new MissionState
            {
                Phase = phase,
                Stop = stop,
                PhaseEnteredAt = phaseEnteredAt,
                // Dokud mise nezacala, neni od CEHO merit - a rozdil proti default(DateTime) dava
                // ~64 miliard sekund, coz by neslo jen o kosmetiku v UI: ta hodnota tece i do
                // zaznamu (nalezeno v bezici aplikaci 26. 8. 2026).
                ElapsedSec = missionStartedAt == default ? 0
                                                        : Math.Max(0, (now - missionStartedAt).TotalSeconds),
                HasDepot = depot != null,
                DepotLatDeg = depot != null ? Conversions.Rad2Deg(depot.Latitude) : 0,
                DepotLonDeg = depot != null ? Conversions.Rad2Deg(depot.Longitude) : 0,
                HasPickup = pickup != null,
                PickupLatDeg = pickup != null ? Conversions.Rad2Deg(pickup.Latitude) : 0,
                PickupLonDeg = pickup != null ? Conversions.Rad2Deg(pickup.Longitude) : 0,
                PickupCodeText = pickupCodeText,
                HasDrop = drop != null,
                DropLatDeg = drop != null ? Conversions.Rad2Deg(drop.Latitude) : 0,
                DropLonDeg = drop != null ? Conversions.Rad2Deg(drop.Longitude) : 0,
                DropCodeText = dropCodeText,
                AbortReason = abortReason,
                HasAcceptedCode = acceptedTarget != null,
                AcceptedLatDeg = acceptedTarget != null ? Conversions.Rad2Deg(acceptedTarget.Latitude) : 0,
                AcceptedLonDeg = acceptedTarget != null ? Conversions.Rad2Deg(acceptedTarget.Longitude) : 0,
                AcceptedCodeText = acceptedCodeText,
                AcceptedDistanceFromDepotM = acceptedDistanceFromDepotM,
                AcceptedRouteLengthM = acceptedRouteLengthM,
                AcceptedOffRoadM = acceptedOffRoadM,
                HasFixInfo = hasFixInfo,
                FixQualityOk = fixQualityOk,
                FixSatellites = fixSatellites,
                FixHdop = fixHdop,
                FixSamples = fixSamples,
                FixSpreadM = fixSpreadM,
                FixSpreadLimitM = config.MaxSpreadM,
                RejectReason = rejectReason,
                RejectedCodeText = rejectedCodeText,
                RejectedDistanceM = rejectedDistanceM,
                CodesRead = codesRead,
                CodesRejected = codesRejected,
                Timeouts = timeouts,
                EmergencyStop = emergencyStop,
                CodeNotSeen = codeNotSeen,
                TimeStamp = now,
            };

            lastMessageAt = now;
            var msg = state.ToLogMessage();
            LastMessage = msg;
            EmitDerived(msg);
        }
    }
}
