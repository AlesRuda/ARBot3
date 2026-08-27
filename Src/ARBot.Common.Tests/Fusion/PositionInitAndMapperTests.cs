using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Runtime;
using ARBot.Common.Tests.Runtime;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Faze 0 globalni navigace (doc/global-navigation-runtime.md): inicializace polohy ve fuzi
    /// a prevod GPS / odometrie na merenia.
    /// </summary>
    public class PositionInitAndMapperTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Brno-ish, ale konkretni cislo je nepodstatne - dulezite je, ze je to VE STUPNICH.
        private const double LatDeg = 49.2103, LonDeg = 16.5991;

        private static GPSState Gps(double latDeg, double lonDeg, DateTime t,
                                    GPSState.FixQuality q = GPSState.FixQuality.GpsFix,
                                    double? speed = null,
                                    double? course = null,
                                    double? trueHeading = null)
            => new GPSState
            {
                // GPSState drzi RADIANY (viz GPSState.Latitude); zadani je ve stupnich, protoze
                // je citelnejsi.
                Latitude = ARBot.Common.Common.Conversions.Deg2Rad(latDeg),
                Longitude = ARBot.Common.Common.Conversions.Deg2Rad(lonDeg),
                Quality = q,
                NumberOfSatellites = 9,
                Hdop = 0.9,
                Speed = speed,
                DynamicOrientation = course,
                Orientation = trueHeading,
                TimeStamp = t,
            };

        private static MotorStateBase Odo(double vLeft, double vRight, DateTime t, bool estop = false)
            // Rychlosti kol jsou od verze 2 vlastni pole zpravy (uz se nedopocitavaji z prirustku
            // enkoderu a doby od vyzvednuti); enkodery jsou kumulativni.
            => new MotorStateBase(estop, vLeft * 0.1, vRight * 0.1, 24, 0, 0, vLeft, vRight)
            {
                TimeStamp = t,
            };

        // ---------------- InitializePosition ----------------

        [Test]
        public void InitializePosition_SetsStateAndCovariance()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            Assert.That(engine.IsPositionInitialized, Is.False);

            engine.InitializePosition(300, -150, 1.5, T0);

            Assert.That(engine.IsPositionInitialized, Is.True);
            var st = engine.GetStateAt(T0);
            Assert.That(st, Is.Not.Null);
            Assert.That(st.X, Is.EqualTo(300).Within(1e-9));
            Assert.That(st.Y, Is.EqualTo(-150).Within(1e-9));

            var P = engine.Model.P;
            Assert.That(P[EKFModel.IX, EKFModel.IX], Is.EqualTo(1.5 * 1.5).Within(1e-9));
            Assert.That(P[EKFModel.IY, EKFModel.IY], Is.EqualTo(1.5 * 1.5).Within(1e-9));
            // Poloha je znama nezavisle na zbytku stavu -> zadne korelace.
            Assert.That(P[EKFModel.IX, EKFModel.ITh], Is.EqualTo(0).Within(1e-12));
            Assert.That(P[EKFModel.IY, EKFModel.IV], Is.EqualTo(0).Within(1e-12));
        }

        /// <summary>
        /// Jadro duvodu, proc InitializePosition existuje: filtr startuje s <c>P0 = I</c> (sigma 1 m),
        /// takze o sve NULOVE poloze si mysli, ze ji zna na metr. Vzdaleny fix (pocatek ENU roviny je
        /// ve stredu mapy, robot stovky metru od nej) tim pretahne stav jen zlomkem cesty:
        /// <c>K = P/(P+R) = 1/(1+1,5²) ≈ 0,31</c>. Filtr se k pravde doplazi az za nekolik sekund a
        /// mezitim by se do occupancy gridu zapisovaly pozy stovky metru mimo.
        /// </summary>
        [Test]
        public void FarAwayFix_WithoutInit_OnlyCreeps_WithInit_IsExact()
        {
            // (a) bez inicializace: jeden fix 300 m daleko stav ani nepriblizne netrefi
            var noInit = new AsyncFusionEngine(new EKFModel());
            noInit.Enqueue(new PositionMeasurement(300, -150, 1.5, 1.5, T0, "GPS/position"));
            var stNoInit = noInit.GetStateAt(T0);
            Assert.That(stNoInit.X, Is.LessThan(150),
                        "bez inicializace stav k fixu jen pomalu leze (K ~ 0,31), netrefi ho");

            // (b) s inicializaci: stav je presne na fixu a dalsi fix uz je bezna korekce
            var init = new AsyncFusionEngine(new EKFModel());
            init.InitializePosition(300, -150, 1.5, T0);
            init.Enqueue(new PositionMeasurement(301, -150, 1.5, 1.5, T0.AddSeconds(0.2), "GPS/position"));
            var st = init.GetStateAt(T0.AddSeconds(0.2));
            Assert.That(st.X, Is.GreaterThan(299.5).And.LessThan(301.5));
        }

        // ---------------- Zahozeni merenia starsiho nez okno ----------------

        /// <summary>
        /// Merenie starsi nez okno historie se zahodi - to je spravne. Do 20. 8. 2026 se to ale
        /// dalo poznat JEN z <c>Debug.WriteLine</c>, ktery je <c>[Conditional("DEBUG")]</c>, takze
        /// v Release nezustala zadna stopa. Pri latenci korekce z korelace 194 ms (x64) az ~1,4 s
        /// (Debug) je to rozdil mezi "funkce jede" a "funkce nedela nic" - a telemetrie by v obou
        /// pripadech hlasila totez. Viz doc/map-correlation-localization.md.
        /// </summary>
        [Test]
        public void MerenieStarsiNezOkno_SeZahodi_APocitaSe()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            engine.InitializePosition(0, 0, 1.0, T0.AddSeconds(2));   // tBase = T0 + 2 s

            Assert.That(engine.DroppedTooOld, Is.EqualTo(0), "na zacatku nic zahozeneho");

            engine.Enqueue(new HeadingMeasurement(0.5, 0.1, T0, "MapCorr"));   // 2 s pozadu

            Assert.That(engine.DroppedTooOld, Is.EqualTo(1));
        }

        /// <summary>Merenie V okne se pocitadla nedotkne.</summary>
        [Test]
        public void MerenieVOkne_PocitadloNezvysi()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            engine.InitializePosition(0, 0, 1.0, T0);

            engine.Enqueue(new HeadingMeasurement(0.5, 0.1, T0.AddSeconds(0.2), "MapCorr"));

            Assert.That(engine.DroppedTooOld, Is.EqualTo(0));
        }

        /// <summary>
        /// Pocitadlo musi rozlisovat ZDROJ - jinak nepozna, jestli zahazuje korekce z korelace
        /// (podezrele, ta je pomala) nebo treba opozdeny GPS fix (bezne).
        /// </summary>
        [Test]
        public void PocitadloRozlisujeZdroj()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            engine.InitializePosition(0, 0, 1.0, T0.AddSeconds(2));

            engine.Enqueue(new HeadingMeasurement(0.5, 0.1, T0, "MapCorr"));
            engine.Enqueue(new HeadingMeasurement(0.5, 0.1, T0, "MapCorr"));
            engine.Enqueue(new PositionMeasurement(1, 1, 1, 1, T0, "GPS/position"));

            var bySource = engine.DroppedTooOldBySource();
            Assert.Multiple(() =>
            {
                Assert.That(engine.DroppedTooOld, Is.EqualTo(3));
                Assert.That(bySource["MapCorr"], Is.EqualTo(2));
                Assert.That(bySource["GPS/position"], Is.EqualTo(1));
            });
        }

        /// <summary>
        /// REGRESE: <c>Initialize*</c> uzly promaze, takze buffer je PRAZDNY i kdyz je filtr
        /// inicializovany. Hlaska o zahozeni sahala na <c>nodes[Count-1]</c> a padala na index -1
        /// (odhaleno testem 20. 8. 2026, v provozu by to shodilo prvni opozdene merenie po
        /// inicializaci). Tady je varianta s NEPRAZDNYM bufferem - produkcni cesta pres Prune.
        /// </summary>
        [Test]
        public void ZahozeniFunguje_IKdyzJeBufferNeprazdny()
        {
            var engine = new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(1));
            engine.InitializePosition(0, 0, 1.0, T0);

            // Naplneni bufferu a posun casu tak, aby Prune odsunul tBase.
            for (int i = 1; i <= 40; i++)
                engine.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0.AddMilliseconds(i * 100), "Odo"));
            engine.GetStateAt(T0.AddSeconds(4));   // vynuti prepocet a Prune

            engine.Enqueue(new HeadingMeasurement(0.5, 0.1, T0.AddMilliseconds(50), "MapCorr"));

            Assert.That(engine.DroppedTooOld, Is.EqualTo(1));
        }

        /// <summary>Vraceny prehled je KOPIE - volajici jim nesmi zamichat vnitrnim stavem.</summary>
        [Test]
        public void PrehledZdroju_JeKopie()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            engine.InitializePosition(0, 0, 1.0, T0.AddSeconds(2));
            engine.Enqueue(new HeadingMeasurement(0.5, 0.1, T0, "MapCorr"));

            var first = engine.DroppedTooOldBySource();
            engine.Enqueue(new HeadingMeasurement(0.5, 0.1, T0, "MapCorr"));

            Assert.That(first["MapCorr"], Is.EqualTo(1), "drive vraceny prehled se nesmi menit");
            Assert.That(engine.DroppedTooOldBySource()["MapCorr"], Is.EqualTo(2));
        }

        // ---------------- InitializeHeading ----------------

        [Test]
        public void InitializeHeading_SetsStateAndCovariance()
        {
            var engine = new AsyncFusionEngine(new EKFModel());

            engine.InitializeHeading(1.2, 0.1, T0);

            var st = engine.GetStateAt(T0);
            Assert.That(st, Is.Not.Null);
            Assert.That(st.Theta, Is.EqualTo(1.2).Within(1e-9));

            var P = engine.Model.P;
            Assert.That(P[EKFModel.ITh, EKFModel.ITh], Is.EqualTo(0.1 * 0.1).Within(1e-12));
            // Kurz je znamy nezavisle na zbytku stavu -> zadne korelace.
            Assert.That(P[EKFModel.ITh, EKFModel.IX], Is.EqualTo(0).Within(1e-12));
            Assert.That(P[EKFModel.IW, EKFModel.ITh], Is.EqualTo(0).Within(1e-12));
        }

        /// <summary>Stav ma zustat kanonicky - jinak by pozdejsi rezidua pocitala s 190 misto -170.</summary>
        [Test]
        public void InitializeHeading_NormalizujeUhel()
        {
            var engine = new AsyncFusionEngine(new EKFModel());

            engine.InitializeHeading(190.0 * Math.PI / 180.0, 0.1, T0);

            Assert.That(engine.GetStateAt(T0).Theta * 180.0 / Math.PI,
                        Is.EqualTo(-170.0).Within(1e-9));
        }

        [Test]
        public void InitializeHeading_RejectsNonPositiveStd()
            => Assert.That(() => new AsyncFusionEngine(new EKFModel()).InitializeHeading(0, 0, T0),
                           Throws.InstanceOf<ArgumentOutOfRangeException>());

        /// <summary>
        /// Inicializace polohy NESMI zahodit uz inicializovany kurz (v <c>ARBotRuntime</c> se volaji
        /// obe hned po sobe se stejnym casem).
        /// </summary>
        [Test]
        public void InitializePozice_NezahodiInicializovanyKurz()
        {
            var engine = new AsyncFusionEngine(new EKFModel());

            engine.InitializeHeading(1.2, 0.1, T0);
            engine.InitializePosition(300, -150, 1.5, T0);

            var st = engine.GetStateAt(T0);
            Assert.Multiple(() =>
            {
                Assert.That(st.Theta, Is.EqualTo(1.2).Within(1e-9));
                Assert.That(st.X, Is.EqualTo(300).Within(1e-9));
            });
        }

        /// <summary>
        /// Duvod, proc <c>InitializeHeading</c> vznikla (19. 8. 2026): kurz se drive jen POSILAL
        /// jako merenie. Filtr ale startuje s <c>P0 = I</c>, tedy sigma = 1 rad (57 deg), takze
        /// merenie o 170 deg vedle ma NIS ~8,7 proti chi2(1; 0,95) = 3,84 - a jakmile se zapnou
        /// prahy gatingu, <b>zahodi se</b>. Tataz latentni past, jakou u polohy popisuje
        /// <see cref="FarAwayFix_WithGating_WouldBeRejected"/>.
        ///
        /// <para>Dopad nebyl teoreticky: dokud kurz nekonvergoval, zapisoval <c>LocalNavigator</c>
        /// do world-kotveneho occupancy gridu bunky se spatnym kurzem a prvni korelace s mapou
        /// z nich vysla s OPACNYM znamenkem. Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        [Test]
        public void ChybnyStartovniKurz_SGatingem_BySeZahodil_SInicializaciJePresny()
        {
            double sto70 = 170.0 * Math.PI / 180.0;

            // (a) jen merenie + zapnuty gating -> zamitnuto, stav zustava na nule
            var byMeasurement = new AsyncFusionEngine(new EKFModel());
            byMeasurement.Enqueue(new HeadingMeasurement(sto70, 0.1, T0, "Start/heading")
            {
                GateThreshold = Gating.ChiSquareThreshold(1, 0.95),   // ~3,84
                GateMode = GateMode.Reject,
            });

            var diag = byMeasurement.Diagnostics();
            Assert.Multiple(() =>
            {
                Assert.That(diag.Count, Is.EqualTo(1));
                Assert.That(diag[0].Accepted, Is.False, "s prahem gatingu je chybny kurz zamitnut");
                Assert.That(diag[0].Nis, Is.GreaterThan(8.0));
                Assert.That(byMeasurement.GetStateAt(T0).Theta, Is.EqualTo(0).Within(1e-9),
                            "stav zustal na nule, tedy 170 deg mimo");
            });

            // (b) inicializace -> stav je presne na zadanem kurzu bez ohledu na gating
            var byInit = new AsyncFusionEngine(new EKFModel());
            byInit.InitializeHeading(sto70, 0.1, T0);
            Assert.That(byInit.GetStateAt(T0).Theta, Is.EqualTo(sto70).Within(1e-9));
        }

        /// <summary>
        /// A druhy, jeste tvrdsi duvod - LATENTNI past: gating se dnes neuplatnuje jen proto, ze
        /// nikdo merenim nenastavuje <see cref="IMeasurement.GateThreshold"/>. Jakmile se prahy
        /// zapnou (na to <see cref="Gating.ChiSquareThreshold"/> je), vzdaleny prvni fix se
        /// ZAHODI a filtr by robota uz nikdy nenasel. Inicializace to resi v obou svetech.
        /// </summary>
        [Test]
        public void FarAwayFix_WithGating_WouldBeRejected()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            var far = new PositionMeasurement(300, -150, 1.5, 1.5, T0, "GPS/position")
            {
                GateThreshold = Gating.ChiSquareThreshold(2, 0.95),   // ~6,0
                GateMode = GateMode.Reject,
            };

            engine.Enqueue(far);
            var diag = engine.Diagnostics();

            Assert.That(diag.Count, Is.EqualTo(1));
            Assert.That(diag[0].Accepted, Is.False, "s prahem gatingu je vzdaleny fix zamitnut");
            Assert.That(diag[0].Nis, Is.GreaterThan(1000), "NIS takoveho fixu je radove 10^4");
            Assert.That(engine.GetStateAt(T0).X, Is.EqualTo(0).Within(1e-9), "stav zustal na nule");
        }

        [Test]
        public void InitializePosition_DropsOlderMeasurements_KeepsNewer()
        {
            var engine = new AsyncFusionEngine(new EKFModel());
            // Merenie PRED inicializaci (poloha tehdy nemela vyznam).
            engine.Enqueue(ScalarStateMeasurement.Velocity(1.0, 0.05, T0, "Odo/speed"));
            // Merenie PO inicializaci - musi zustat a projevit se.
            engine.Enqueue(ScalarStateMeasurement.Velocity(0.5, 0.05, T0.AddSeconds(0.4), "Odo/speed"));

            engine.InitializePosition(10, 20, 1.0, T0.AddSeconds(0.2));

            var st = engine.GetStateAt(T0.AddSeconds(0.4));
            Assert.That(st, Is.Not.Null);
            Assert.That(st.V, Is.GreaterThan(0.2), "novejsi merenie rychlosti se ma zachovat");
            // Starsi merenie uz v okne neni.
            Assert.That(engine.Diagnostics().All(d => d.Time > T0.AddSeconds(0.2)), Is.True);
        }

        [Test]
        public void InitializePosition_RejectsNonPositiveStd()
            => Assert.That(() => new AsyncFusionEngine(new EKFModel()).InitializePosition(0, 0, 0, T0),
                           Throws.TypeOf<ArgumentOutOfRangeException>());

        // ---------------- GPS -> merenia ----------------

        /// <summary>
        /// JEDNOTKY: GPSState je ve STUPNICH, LLA v RADIANECH. Kdyby mapper predal stupne jako
        /// radiany, robot by skoncil stovky kilometru od pocatku - a nic by to nenahlasilo.
        /// </summary>
        [Test]
        public void Gps_IsInterpretedAsDegrees_NotRadians()
        {
            var cfg = new FusionConfig
            {
                // Pocatek presne na fixu -> spravny prevod musi dat (0,0).
                GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)),
            };
            var engine = new AsyncFusionEngine(new EKFModel(cfg));
            var mapper = new DefaultMeasurementMapper(cfg, engine);

            foreach (var m in mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0)))
                engine.Enqueue(m);

            var st = engine.GetStateAt(T0);
            Assert.That(Math.Sqrt(st.X * st.X + st.Y * st.Y), Is.LessThan(0.01),
                        "fix v pocatku roviny musi dat lokalni [0,0] - jinak se stupne pletou s radiany");
        }

        [Test]
        public void Gps_FirstUsableFix_InitializesPosition()
        {
            var cfg = new FusionConfig
            {
                GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)),
            };
            var engine = new AsyncFusionEngine(new EKFModel(cfg));
            var mapper = new DefaultMeasurementMapper(cfg, engine);

            Assert.That(engine.IsPositionInitialized, Is.False);

            // Fix ~200 m severne od pocatku.
            var far = LLA.FromDegrees(LatDeg, LonDeg);
            var farLla = new GeoReference(far).ToLLA(0, 200);
            foreach (var m in mapper.ToMeasurements(
                         Gps(Conversions.Rad2Deg(farLla.Latitude),
                             Conversions.Rad2Deg(farLla.Longitude), T0)))
                engine.Enqueue(m);

            Assert.That(engine.IsPositionInitialized, Is.True, "prvni pouzitelny fix ma polohu inicializovat");
            var st = engine.GetStateAt(T0);
            Assert.That(st.Y, Is.EqualTo(200).Within(1.0), "stav ma byt PRESNE na fixu, ne pritazeny gatingem");
        }

        [Test]
        public void Gps_NoFix_ProducesNothing()
        {
            var cfg = new FusionConfig { GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)) };
            var engine = new AsyncFusionEngine(new EKFModel(cfg));
            var mapper = new DefaultMeasurementMapper(cfg, engine);

            var res = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0, GPSState.FixQuality.Invalid)).ToList();

            Assert.That(res, Is.Empty, "bez platneho fixu nevznika zadne merenie");
            Assert.That(engine.IsPositionInitialized, Is.False);
        }

        [Test]
        public void Gps_WithoutGeoReference_AndWithoutEngine_ProducesNothing()
        {
            var cfg = new FusionConfig();   // GeoReference == null
            var mapper = new DefaultMeasurementMapper(cfg);   // bez engine = bez fallbacku

            Assert.That(mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0)).ToList(), Is.Empty);
            Assert.That(cfg.GeoReference, Is.Null, "bez fallbacku se pocatek nezaklada");
        }

        [Test]
        public void Gps_WithoutGeoReference_FallbackAnchorsAtFirstFix()
        {
            var cfg = new FusionConfig();
            var engine = new AsyncFusionEngine(new EKFModel(cfg));
            var mapper = new DefaultMeasurementMapper(cfg, engine);

            foreach (var m in mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0)))
                engine.Enqueue(m);

            Assert.That(cfg.GeoReference, Is.Not.Null, "fallback ma pocatek zalozit z prvniho fixu");
            Assert.That(Conversions.Rad2Deg(cfg.GeoReference.Origin.Latitude),
                        Is.EqualTo(LatDeg).Within(1e-9));
            var st = engine.GetStateAt(T0);
            Assert.That(Math.Sqrt(st.X * st.X + st.Y * st.Y), Is.LessThan(0.01));
        }

        [Test]
        public void Gps_Speed_OnlyAboveThreshold()
        {
            var cfg = new FusionConfig { GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)) };
            var engine = new AsyncFusionEngine(new EKFModel(cfg));
            var mapper = new DefaultMeasurementMapper(cfg, engine);
            engine.InitializePosition(0, 0, 1.0, T0);   // aby prvni fix nesel do inicializace

            var slow = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0.AddSeconds(0.1), speed: 0.05)).ToList();
            var fast = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0.AddSeconds(0.2), speed: 1.2)).ToList();

            Assert.That(slow.Any(m => m.Source == "GPS/speed"), Is.False, "pri stani je GPS rychlost sum");
            Assert.That(fast.Any(m => m.Source == "GPS/speed"), Is.True);
        }

        // ---------------- GPS kurz = DRUHA absolutni reference (25. 8. 2026) ----------------
        //
        // Fuze mela do teto zmeny JEDINOU absolutni referenci kurzu (IMU/heading z magnetometru),
        // takze bias kompasu nemela proti cemu zmerit: namereno, ze pri imubias=3 zustane chyba
        // kurzu na 3,0 stupne a odhad sedi na IMU na 100 % — kompas kurz DEFINUJE, ne vazi.
        // GPS kurz pritom zna (NmeaGps z VTG, uBloxGps jako atan2 z vektoru rychlosti) a namereno,
        // ze je NEVYCHYLENY (+0,20 deg proti pravde pri sumu 5,02 deg). Viz
        // doc/map-correlation-localization.md a doc/ekf-fusion.md.

        [Test]
        public void Gps_KurzNadZemi_DavaMereniGpsHeading()
        {
            var cfg = new FusionConfig { GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)) };
            var mapper = new DefaultMeasurementMapper(cfg);

            var m = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0, speed: 1.2, course: 0.7))
                          .FirstOrDefault(x => x.Source == "GPS/heading");

            Assert.That(m, Is.Not.Null, "z kurzu nad zemi ma vzniknout merenie kurzu");
            Assert.That(m!.Value[0], Is.EqualTo(0.7).Within(1e-9));
        }

        /// <summary>
        /// <b>Sigma kurzu klesa s rychlosti</b> — to je to podstatne. Kurz nad zemi neni merena
        /// velicina, je to <c>atan2</c> z vektoru rychlosti, takze <c>sigma ≈ sigma_pricne / v</c>.
        /// Konstantni sigma by tuhle zavislost zahodila a pri pomale jizde by filtr veril necemu,
        /// co je skoro nahodne.
        /// </summary>
        [Test]
        public void Gps_KurzNadZemi_SigmaKlesaSRychlosti()
        {
            var cfg = new FusionConfig
            {
                GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)),
                GpsCrossTrackStd = 0.3,
                GpsHeadingStd = 0.005,          // podlaha nizko, aby nezastinila zavislost
            };
            var mapper = new DefaultMeasurementMapper(cfg);

            double StdAt(double v) => Math.Sqrt(mapper
                .ToMeasurements(Gps(LatDeg, LonDeg, T0, speed: v, course: 0.0))
                .First(x => x.Source == "GPS/heading").NoiseCovariance[0, 0]);

            double slow = StdAt(0.5), fast = StdAt(3.0);

            TestContext.Out.WriteLine($"sigma kurzu: 0,5 m/s -> {slow * 180 / Math.PI:F1} deg, "
                                      + $"3,0 m/s -> {fast * 180 / Math.PI:F1} deg");

            Assert.That(fast, Is.LessThan(slow), "rychleji = presnejsi kurz");
            // atan(0,3/0,5) = 31 deg, atan(0,3/3,0) = 5,7 deg
            Assert.That(slow, Is.EqualTo(Math.Atan2(0.3, 0.5)).Within(1e-9));
            Assert.That(fast, Is.EqualTo(Math.Atan2(0.3, 3.0)).Within(1e-9));
        }

        [Test]
        public void Gps_KurzNadZemi_MaPodlahuSigmy()
        {
            var cfg = new FusionConfig
            {
                GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)),
                GpsCrossTrackStd = 0.3,
                GpsHeadingStd = 0.2,            // podlaha vys nez atan(0,3/10) = 0,03
            };
            var mapper = new DefaultMeasurementMapper(cfg);

            var m = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0, speed: 10.0, course: 0.0))
                          .First(x => x.Source == "GPS/heading");

            Assert.That(Math.Sqrt(m.NoiseCovariance[0, 0]), Is.EqualTo(0.2).Within(1e-9),
                        "pri vysoke rychlosti nesmi sigma spadnout pod fyzicky strop prijimace");
        }

        [Test]
        public void Gps_PriMaleRychlosti_KurzNepouzije()
        {
            var cfg = new FusionConfig { GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)) };
            var mapper = new DefaultMeasurementMapper(cfg);

            // Pod prahem je atan2 ze sumu rovnomerne rozdeleny uhel, tedy cista dezinformace.
            var slow = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0, speed: 0.05, course: 0.7)).ToList();
            Assert.That(slow.Any(x => x.Source == "GPS/heading"), Is.False);

            // A ZAPORNA rychlost taky ne: kurz nad zemi je pri jizde vzad o 180 stupnu jinde
            // a NMEA rychlost je bez znamenka, takze to nejde poznat. Radeji nic nez 180 stupnu vedle.
            var reverse = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0, speed: -1.2, course: 0.7)).ToList();
            Assert.That(reverse.Any(x => x.Source == "GPS/heading"), Is.False);
        }

        /// <summary>
        /// Dvouantennovy kurz (<c>uBlox HeadVeh</c>) je <b>skutecny kurz vozidla</b>, ne kurz nad
        /// zemi — plati tedy i pri stani a nezavisi na rychlosti. Ma proto prednost.
        /// </summary>
        [Test]
        public void Gps_DvouantennovyKurz_MaPrednost_APlatiIPriStani()
        {
            var cfg = new FusionConfig
            {
                GeoReference = new GeoReference(LLA.FromDegrees(LatDeg, LonDeg)),
                GpsHeadingStd = 0.02,
            };
            var mapper = new DefaultMeasurementMapper(cfg);

            var m = mapper.ToMeasurements(Gps(LatDeg, LonDeg, T0, speed: 0.0,
                                              course: 1.1, trueHeading: 0.4))
                          .FirstOrDefault(x => x.Source == "GPS/heading");

            Assert.That(m, Is.Not.Null, "kurz vozidla plati i pri stani");
            Assert.That(m!.Value[0], Is.EqualTo(0.4).Within(1e-9), "prednost ma kurz VOZIDLA");
            Assert.That(Math.Sqrt(m.NoiseCovariance[0, 0]), Is.EqualTo(0.02).Within(1e-9));
        }

        // ---------------- Odometrie -> merenia ----------------

        [Test]
        public void Odometry_StraightAhead_GivesSpeedAndZeroRate()
        {
            var cfg = new FusionConfig { WheelBase = 0.5 };
            var mapper = new DefaultMeasurementMapper(cfg);

            var res = mapper.ToMeasurements(Odo(0.8, 0.8, T0)).ToList();

            var v = res.Single(m => m.Source == "Odo/speed");
            var w = res.Single(m => m.Source == "Odo/rate");
            Assert.That(v.Value[0], Is.EqualTo(0.8).Within(1e-9));
            Assert.That(w.Value[0], Is.EqualTo(0.0).Within(1e-9));
        }

        /// <summary>
        /// Rychlejsi prave kolo = zatoceni VLEVO = omega &gt; 0 (matematicky smysl, +CCW).
        /// Znamenko zavisi na polarite enkoderu driveru -> <see cref="FusionConfig.OdoOmegaSign"/>;
        /// NEOVERENO NA ZARIZENI.
        /// </summary>
        [Test]
        public void Odometry_RightWheelFaster_TurnsLeft_PositiveOmega()
        {
            var cfg = new FusionConfig { WheelBase = 0.5 };
            var mapper = new DefaultMeasurementMapper(cfg);

            var w = mapper.ToMeasurements(Odo(0.4, 0.6, T0)).Single(m => m.Source == "Odo/rate");

            Assert.That(w.Value[0], Is.EqualTo((0.6 - 0.4) / 0.5).Within(1e-9));
            Assert.That(w.Value[0], Is.GreaterThan(0));
        }

        [Test]
        public void Odometry_OmegaSign_IsConfigurable()
        {
            var cfg = new FusionConfig { WheelBase = 0.5, OdoOmegaSign = -1.0 };
            var mapper = new DefaultMeasurementMapper(cfg);

            var w = mapper.ToMeasurements(Odo(0.4, 0.6, T0)).Single(m => m.Source == "Odo/rate");

            Assert.That(w.Value[0], Is.LessThan(0), "prepnute znamenko musi otocit smysl otaceni");
        }

        /// <summary>
        /// <b>Pod nouzovym zastavenim se odometrie pouziva NORMALNE.</b>
        ///
        /// <para>Do 27. 8. 2026 se zahazovala s oduvodnenim „kola stoji, ale robot muze byt tlacen, a
        /// hlavne je to stav, kdy do nej clovek zasahuje". <b>Autor to vyvratil:</b> ridici jednotka
        /// ma pod stopem prikaz STAT a motory jsou rizene pozicne ve zpetne vazbe, takze kola nemohou
        /// vyrobit nic jineho nez nulu — stop tedy odometrii <b>nijak nezhorsuje</b>. A ze robota
        /// muze clovek zvednout a prenest, plati stejne <i>bez</i> stisknuteho stopu, takze tim se ty
        /// dva stavy nerozlisi.</para>
        ///
        /// <para>Cena zahazovani byla vysoka: pod drzenym stopem fuze nemela ZADNOU vazbu na
        /// rychlost, takze polohu tahal sum GPS a odhad se za servisni okno (desitky sekund) rozesel
        /// o metry. Projevilo se to jako „robot na mape zbesile poskakuje" v misi Robotour, ktera je
        /// prvni vec, co stop drzi dlouho.</para>
        /// </summary>
        [Test]
        public void Odometry_UnderEmergencyStop_IsUsedNormally()
        {
            var mapper = new DefaultMeasurementMapper(new FusionConfig());

            var res = mapper.ToMeasurements(Odo(0.0, 0.0, T0, estop: true)).ToList();

            Assert.Multiple(() =>
            {
                var v = res.SingleOrDefault(m => m.Source == "Odo/speed");
                var w = res.SingleOrDefault(m => m.Source == "Odo/rate");

                Assert.That(v, Is.Not.Null, "stojici kola jsou plnohodnotne merenie v = 0");
                Assert.That(w, Is.Not.Null, "a omega = 0");
                Assert.That(v!.Value[0], Is.Zero);
                Assert.That(w!.Value[0], Is.Zero);
            });
        }

        /// <summary>
        /// Nenulova rychlost kol se pod stopem <b>prenese</b>, ne spolkne — mapper priznak
        /// nouzoveho zastaveni nerozlisuje vubec.
        ///
        /// <para>Test hlida jen tohle. <b>Nerika</b>, ze pri tlaceni robota odometrie odhali posun:
        /// pozicni smycka polohu drzi a s tlakem se pere, takze enkodery ukazou vychylku a navrat.
        /// Chova se tedy stejne jako bez stopu — a to je cely dukaz, ktery je potreba.</para>
        /// </summary>
        [Test]
        public void Odometry_UnderEmergencyStop_NenulovaRychlostSePrenese()
        {
            var mapper = new DefaultMeasurementMapper(new FusionConfig { WheelBase = 0.5 });

            var v = mapper.ToMeasurements(Odo(0.3, 0.3, T0, estop: true))
                          .Single(m => m.Source == "Odo/speed");

            Assert.That(v.Value[0], Is.EqualTo(0.3).Within(1e-9));
        }

        [Test]
        public void Imu_StillMapped_AfterAddingGpsAndOdometry()
        {
            var mapper = new DefaultMeasurementMapper(new FusionConfig());
            var imu = TestHelpers.MakeImu(T0, yaw: 0.5, omega: 0.1);

            var res = mapper.ToMeasurements(imu).ToList();

            Assert.That(res.Any(m => m.Source == "IMU/heading"), Is.True);
            Assert.That(res.Any(m => m.Source == "IMU/gyro"), Is.True);
        }
    }
}
