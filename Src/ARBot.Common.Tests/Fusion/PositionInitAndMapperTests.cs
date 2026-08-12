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
                                    double? speed = null)
            => new GPSState
            {
                Latitude = latDeg,
                Longitude = lonDeg,
                Quality = q,
                NumberOfSatellites = 9,
                Hdop = 0.9,
                Speed = speed,
                TimeStamp = t,
            };

        private static MotorStateBase Odo(double vLeft, double vRight, DateTime t, bool estop = false)
            => new MotorStateBase(estop, vLeft * 0.1, vRight * 0.1, 24, 0, 0)
            {
                FramePickupPeriod = TimeSpan.FromMilliseconds(100),
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

        [Test]
        public void Odometry_UnderEmergencyStop_IsIgnored()
        {
            var mapper = new DefaultMeasurementMapper(new FusionConfig());

            var res = mapper.ToMeasurements(Odo(0.0, 0.0, T0, estop: true)).ToList();

            Assert.That(res, Is.Empty, "pod nouzovym zastavenim se odometrie nepouziva");
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
