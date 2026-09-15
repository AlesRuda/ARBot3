using System;
using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Regulators;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Runtime
{
    /// <summary>
    /// <b>Držené zastavení</b> (<see cref="StopHold"/>): několik nezávislých zdrojů může držet
    /// robota v klidu, zatímco vyšší smyčky dál nastavují regulátor. Viz doc/plan-drive-hold.md.
    /// </summary>
    public class StopHoldTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static MotorStateBase Motory(double vlevo, double vpravo)
            => new MotorStateBase(false, 0, 0, 24, 0, 0, vlevo, vpravo);

        // --- Evidence držení (bez řídicí smyčky) ------------------------------------------------

        [Test]
        public void BezDuvoduSeDrzeniNezalozi()
        {
            var registr = new DriveHoldRegistry();

            // Stojící robot bez zapsaného důvodu je nedohledatelná porucha - proto výjimka,
            // ne mlčky přijatý prázdný řetězec.
            Assert.Throws<ArgumentException>(() => registr.StopRequest(null));
            Assert.Throws<ArgumentException>(() => registr.StopRequest("  "));
            Assert.That(registr.IsHeld, Is.False);
        }

        [Test]
        public void RobotStojiDokudNepustiVsichni()
        {
            var registr = new DriveHoldRegistry();

            var kamery = registr.StopRequest("restart kamer");
            var servis = registr.StopRequest("servisni okno");
            Assert.That(registr.IsHeld, Is.True);
            Assert.That(registr.HoldReasons, Is.EquivalentTo(new[] { "restart kamer", "servisni okno" }));

            // Uvolnění JEDNOHO zdroje robota nerozjede - to je celý smysl počítaného držení.
            kamery.Dispose();
            Assert.That(registr.IsHeld, Is.True, "druhy zdroj drzi dal");
            Assert.That(kamery.IsHeld, Is.False, "uvolneny token uz nedrzi");
            Assert.That(registr.HoldReasons, Is.EquivalentTo(new[] { "servisni okno" }));

            servis.Dispose();
            Assert.That(registr.IsHeld, Is.False, "po uvolneni vsech smi robot jet");
            Assert.That(registr.HoldReasons, Is.Empty);
        }

        [Test]
        public void DisposeJeIdempotentni()
        {
            var registr = new DriveHoldRegistry();
            var a = registr.StopRequest("a");
            var b = registr.StopRequest("b");

            a.Dispose();
            a.Dispose();   // druhé volání nesmí odregistrovat cizí držení
            a.Dispose();

            Assert.That(registr.IsHeld, Is.True, "b drzi porad");
            Assert.That(registr.HoldReasons, Is.EquivalentTo(new[] { "b" }));
        }

        /// <summary>
        /// ⚠️ <b>Neznámý stav motorů není stání.</b> Kdo čeká, aby směl udělat něco riskantního,
        /// nesmí dostat „stojí" od senzoru, který mlčí — volající si musí nést vlastní timeout.
        /// Rozhodnutí 3 v doc/plan-drive-hold.md.
        /// </summary>
        [Test]
        public void NeznamyStavMotoruNeniStani()
        {
            var registr = new DriveHoldRegistry();
            var hold = registr.StopRequest("test");

            Assert.That(hold.IsStopped, Is.False, "bez jedineho stavu motoru se nesmi tvrdit, ze robot stoji");

            registr.NoteMotorState(Motory(0, 0));
            Assert.That(hold.IsStopped, Is.True);

            registr.NoteMotorState(null);
            Assert.That(hold.IsStopped, Is.False, "kdyz motory prestanou hlasit, stani uz neni jiste");
        }

        /// <summary>
        /// ⚠️ <b>Fail-rámec driveru také není stání</b> — a je to horší případ než mlčení:
        /// rámec <i>přijde</i>, takže vypadá jako měření, ale jeho nuly nikdo neměřil
        /// (<c>SDC2160Ex</c> ho vyrábí po chybě portu). Kdo se na něj spolehne, dostane „stojí"
        /// právě v okamžiku, kdy o robotu neví nic — a <c>CameraRecoverySupervisor</c> na
        /// <see cref="StopHold.IsStopped"/> čeká, než sáhne na kamery.
        /// </summary>
        [Test]
        public void FailRamecMotoruNeniStani()
        {
            var registr = new DriveHoldRegistry();
            var hold = registr.StopRequest("test");

            registr.NoteMotorState(Motory(0, 0));
            Assert.That(hold.IsStopped, Is.True);

            // Odpojil se USB prevodnik: driver posila nahradni ramec s nulami, ktere nikdo nemeril.
            registr.NoteMotorState(new MotorStateBase(true, 0, 0, 0, 0, 0, 0, 0, hasMeasurement: false));
            Assert.That(hold.IsStopped, Is.False, "nuly z fail-ramce nejsou merene stani");
        }

        [Test]
        public void StaniSeMeriZKolNeZPrikazu()
        {
            var registr = new DriveHoldRegistry();
            var hold = registr.StopRequest("test");

            registr.NoteMotorState(Motory(0.30, 0.30));
            Assert.That(hold.IsStopped, Is.False, "kola se toci");

            // Presna nula, bez epsilonu - LeftWheelSpeed je nefiltrovany prirustek enkoderu.
            registr.NoteMotorState(Motory(0, 0.02));
            Assert.That(hold.IsStopped, Is.False, "jedno kolo se jeste toci");

            registr.NoteMotorState(Motory(0, 0));
            Assert.That(hold.IsStopped, Is.True);
        }

        // --- Napojení na řídicí smyčku ----------------------------------------------------------

        /// <summary>Motory zaznamenavajici prikazy (test double).</summary>
        private sealed class SpyMotors : IMotorControl
        {
            public readonly List<double> Forvard = new List<double>();
            public string Name => "Spy";
            public bool IsError => false;
            public void Drive(double forvard, double dif) => Forvard.Add(forvard);
            public void SetAcceleration(double a) { }
            public IMotorState GetLastMeasurement() => new MotorStateBase(false, 0, 0, 0, 0, 0, 0, 0);
            public event EventHandler<IMotorState> MeasurementArived { add { } remove { } }
        }

        /// <summary>
        /// Postaví smyčku s naplánovanou dráhou a odpumpuje <paramref name="taktu"/> taktů;
        /// <paramref name="pred"/> se zavolá před taktem s jeho pořadím (tam se bere/pouští hold).
        /// </summary>
        private static SpyMotors Rozjed(int taktu, Action<int, ControlLoop> pred, out ControlLoop loop)
        {
            var mapper = new DefaultMeasurementMapper();
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();
            var ts = TimeSpan.FromMilliseconds(20);

            loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler, period: ts);
            var profile = new TrapezoidMotionProfile(Profile.MaxAllowedSpeed, Profile.MaxAllowedRotationSpeed,
                                                     Profile.MaxAcceleration, Profile.Rozchod);
            loop.Regulator = new PathPlanner(profile).Plan(new[]
            {
                new RegulatorWayPoint { X = 0, Y = 0 },
                new RegulatorWayPoint { X = 20, Y = 0 },
            });

            for (int i = 0; i < taktu; i++)
            {
                pred?.Invoke(i, loop);
                var imu = TestHelpers.MakeImu(T0.AddMilliseconds(i * 20), yaw: 0, omega: 0);
                foreach (var m in mapper.ToMeasurements(imu))
                    engine.Enqueue(m);

                // Regulator se "obnovuje" kazdy takt (jinak by po 500 ms zabralo dobrzdeni
                // zastarale drahy a merilo by se neco jineho).
                loop.Regulator = loop.Regulator;
                scheduler.PumpDue(imu.TimeStamp);
            }
            loop.Stop();
            return motor;
        }

        [Test]
        public void DrzeniZastaviRobota_ALE_RAMPOU()
        {
            // Dobrzdeni z plne rychlosti pri MaxDecceleration 0,5 m/s^2 trva ~2,4 s, tedy ~120
            // taktu po 20 ms - proto to okno. Kratsi okno by test "nakonec ma stat" shodilo,
            // ackoli rampa funguje spravne.
            const int Taktu = 200, Drzet = 30;
            var motor = Rozjed(Taktu, (i, loop) =>
            {
                if (i == Drzet) loop.StopRequest("test");
            }, out _);

            double predDrzenim = motor.Forvard[Drzet - 1];
            Assert.That(predDrzenim, Is.GreaterThan(0.05), "robot mel pred drzenim jet");

            // Prvni takt s drzenim NESMI byt skok na nulu - brzdi se rampou MaxDecceleration.
            double krok = Profile.MaxDecceleration * 0.020;
            Assert.That(motor.Forvard[Drzet], Is.EqualTo(predDrzenim - krok).Within(1e-9),
                        "drzeni ma brzdit rampou, ne tvrdou nulou");

            // A po dost taktech je robot zastaveny.
            Assert.That(motor.Forvard[Taktu - 1], Is.EqualTo(0).Within(1e-9), "nakonec ma stat");
        }

        [Test]
        public void PoUvolneniSmiRobotZaseJet()
        {
            const int Taktu = 240, Drzet = 20, Pustit = 190;
            StopHold hold = null;
            var motor = Rozjed(Taktu, (i, loop) =>
            {
                if (i == Drzet) hold = loop.StopRequest("test");
                if (i == Pustit) hold.Dispose();
            }, out var loop);

            Assert.That(motor.Forvard[Pustit - 1], Is.EqualTo(0).Within(1e-9), "pri drzeni ma stat");
            Assert.That(motor.Forvard[Taktu - 1], Is.GreaterThan(0), "po uvolneni se ma zase rozjet");

            // Regulator drzenim NEZMIZEL - hold rika "nejed", ne "nemas kam".
            Assert.That(loop.Regulator, Is.Not.Null);
            Assert.That(loop.IsHeld, Is.False);
        }

        [Test]
        public void DrzeniSeHlasiVeZprave()
        {
            var mapper = new DefaultMeasurementMapper();
            var engine = new AsyncFusionEngine(new EKFModel());
            var scheduler = new Scheduler();
            var motor = new SpyMotors();
            var loop = new ControlLoop(engine, motor, new VirtualClock(), scheduler,
                                       period: TimeSpan.FromMilliseconds(20));

            var cmds = new List<DriveCommandMsg>();
            var collector = new DelegateTarget(m => { if (m is DriveCommandMsg d) lock (cmds) cmds.Add(d); });
            collector.Start();

            using (loop.Output.Connect(collector))
            {
                StopHold hold = null;
                for (int i = 0; i < 6; i++)
                {
                    if (i == 2) hold = loop.StopRequest("restart kamer");
                    if (i == 4) hold.Dispose();
                    var imu = TestHelpers.MakeImu(T0.AddMilliseconds(i * 20), yaw: 0, omega: 0);
                    foreach (var m in mapper.ToMeasurements(imu)) engine.Enqueue(m);
                    scheduler.PumpDue(imu.TimeStamp);
                }
            }
            loop.Stop();
            collector.Stop();

            List<DriveCommandMsg> kopie;
            lock (cmds) kopie = new List<DriveCommandMsg>(cmds);
            Assert.That(kopie.Count, Is.GreaterThanOrEqualTo(5));

            // Bez priznaku ve zprave by v zaznamu byly nuly bez vysvetleni.
            Assert.That(kopie[1].Held, Is.False);
            Assert.That(kopie[2].Held, Is.True);
            Assert.That(kopie[3].Held, Is.True);
            Assert.That(kopie[4].Held, Is.False);
        }

        /// <summary>
        /// Příznak musí přežít i skutečný zápis na disk (verze 3 zprávy) — jinak by ho záznam
        /// z robota nenesl a „proč tu stál" by se ze souboru vyčíst nedalo.
        /// </summary>
        [Test]
        public void DriveCommandMsg_PreziSerializaci()
        {
            var zprava = new DriveCommandMsg
            {
                Speed = 0.5, RotationSpeed = 0.1, Forvard = 0.5, Dif = 0.05,
                EmergencyStop = false, Held = true, TimeStamp = T0,
            };

            var enc = System.Text.Encoding.UTF8;
            var ms = new System.IO.MemoryStream();
            var w = new MessageWriter(ms, enc);
            w.Write(zprava);
            w.Flush();

            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
            var reader = new MessageReader(new System.IO.MemoryStream(ms.ToArray()), enc, map);
            var zpet = reader.Read() as DriveCommandMsg;

            Assert.That(zpet, Is.Not.Null);
            Assert.That(zpet.Held, Is.True);
            Assert.That(zpet.EmergencyStop, Is.False);
            Assert.That(zpet.Forvard, Is.EqualTo(0.5).Within(1e-12));
        }
    }
}
