using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ARBot.Common.Configuration;
using ARBot.Common.Devices;
using ARBot.HAL.Devices.MotorDrivers;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Rampy motorove jednotky zvlast pro beznou jizdu a nouzove zastaveni (skript
    /// <c>RizeniDiffPodvozku.mbs</c> 2.2, <see cref="MotorRamps"/>; rozhodnuti autora 5. 10. 2026,
    /// registr <c>hw-motor-rampa-jednotky</c>). Skript sam se tu spustit neda — testy hlidaji to,
    /// co na hostiteli jde: co driver posle, jak precte hlaseni skriptu, a ze se kopie skriptu
    /// u driveru a jeho vychozi hodnoty nerozesly s primarnim zdrojem a s <see cref="Profile"/>.
    /// </summary>
    public class MotorRampsTests
    {
        /// <summary>UART s nachystanymi radky, ktery si zapamatuje, co se poslalo.</summary>
        private sealed class ScriptedUart : IUart
        {
            private readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
            public readonly ConcurrentQueue<string> Written = new ConcurrentQueue<string>();

            public void Feed(params string[] text)
            {
                foreach (var s in text) lines.Enqueue(s);
            }

            public bool IsOpen => true;
            public int ReadTimeout { get; set; }
            public string ReadLine() => lines.TryDequeue(out var s) ? s : null;
            public byte[] Read(int count) => Array.Empty<byte>();
            public int Read(byte[] buffer, int offset, int count) => 0;
            public Task<byte[]> ReadAsync(int count) => Task.FromResult(Array.Empty<byte>());
            public string ReadAll() => null;
            public Task<string> ReadLineAsync() => Task.FromResult<string>(null);
            public void Write(byte[] buffer) { }
            public void WriteLine(string txt) => Written.Enqueue(txt);
            public void CancelRead() { }
        }

        /// <summary>Plny rozsah rychlosti 2,0 m/s, aby jednotky vychazely kulate (1000·a/2).</summary>
        private sealed class TestDriver : SDC2160Ex
        {
            public TestDriver(IUart uart)
                : base(uart, maxPossibleSpeed: 2.0, speedLimit: 2.0, wheelCircumference: 1.0, enc2Rotation: 1000) { }

            public void ReadOneFrame() => GetMeasurement();
            public void ObnovCteni() => stopRequired = false;
        }

        private static TestDriver StoppedDriver(ScriptedUart uart)
        {
            var driver = new TestDriver(uart);
            driver.Stop();
            while (uart.ReadLine() != null) { }
            driver.ObnovCteni();
            return driver;
        }

        [Test]
        public void SetRamps_PosilaBeznouINouzovouRampuVJednotkachSkriptu()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);
            while (uart.Written.TryDequeue(out _)) { }

            driver.SetRamps(new MotorRamps(0.4, 1.0));

            var w = uart.Written.ToList();
            Assert.That(w, Does.Contain("!VAR 1 200"), "bezna jizda 0,4 m/s² = 200 tisicin z 2 m/s za s");
            Assert.That(w, Does.Contain("!VAR 2 200"), "rotacni rampa = bezna (jako SetAcceleration)");
            Assert.That(w, Does.Contain("!VAR 9 500"), "nouzove zastaveni");
            Assert.That(w.Any(x => x.StartsWith("!VAR 8")), Is.False,
                        "samostatne bezne brzdeni (VAR 8) je zrusene - jedna rampa kvuli symetrii");
        }

        [Test]
        public void HlaseniSkriptu_ED_SePrectePrevedeNaMs2()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);
            Assert.That(driver.ScriptEmergencyDeceleration, Is.Null, "pred prvnim ramcem nevime");

            uart.Feed("ED=500", "T=1000", "DI=1", "?C=0:0", "?V=240", "?A=0:0");
            driver.ReadOneFrame();

            Assert.That(driver.ScriptEmergencyDeceleration, Is.EqualTo(1.0).Within(1e-9));
        }

        /// <summary>
        /// Stary skript (pod 2.2) <c>ED=</c> neposila a pod nouzovym zastavenim brzdi BEZNOU rampou.
        /// Musi to byt videt v Trace (tedy v zaznamu ze zarizeni), jinak by se po nasazeni nove
        /// binarky bez noveho skriptu brzdna draha tise prodlouzila.
        /// </summary>
        [Test]
        public void StaryScript_BezED_SeOhlasiDoTrace()
        {
            var uart = new ScriptedUart();
            var driver = StoppedDriver(uart);
            var zpravy = new List<string>();
            var listener = new Zachyt(zpravy);
            Trace.Listeners.Add(listener);
            try
            {
                for (int i = 0; i < 120; i++)
                {
                    uart.Feed("DI=1", "?C=0:0", "?V=240", "?A=0:0");
                    driver.ReadOneFrame();
                }
            }
            finally { Trace.Listeners.Remove(listener); }

            Assert.That(driver.ScriptEmergencyDeceleration, Is.Null);
            Assert.That(zpravy.Count(z => z.Contains("NEHLASI nouzovou rampu")), Is.EqualTo(1),
                        "ohlasit prave jednou, ne kazdy ramec");
        }

        private sealed class Zachyt : TraceListener
        {
            private readonly List<string> cil;
            public Zachyt(List<string> cil) { this.cil = cil; }
            public override void Write(string message) { lock (cil) cil.Add(message); }
            public override void WriteLine(string message) { lock (cil) cil.Add(message); }
        }

        // ---------------- Skript: kopie u driveru a vychozi hodnoty ----------------

        private static string Koren()
        {
            string koren = RepoPaths.RootOrBase();
            string mbs = Path.Combine(koren, "Src", "RoboRun", "RizeniDiffPodvozku.mbs");
            if (!File.Exists(mbs)) Assert.Ignore("Bezi bez repa - skript neni k dispozici.");
            return koren;
        }

        /// <summary>Radky bez okrajovych mezer a bez prazdnych radku (porovnava se obsah, ne formatovani).</summary>
        private static List<string> Normalizuj(string text)
            => text.Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        /// <summary>
        /// Kopie skriptu v komentari <c>SDC2160Ex</c> se musi shodovat s primarnim zdrojem —
        /// 1. 10. 2026 se radek <c>T=</c> dostal jen do kopie a nahrat se mel jiny skript, nez
        /// jaky se cetl u driveru.
        /// </summary>
        [Test]
        public void KopieSkriptuUDriveru_ShodnaSMbs()
        {
            string koren = Koren();
            string mbs = File.ReadAllText(Path.Combine(koren, "Src", "RoboRun", "RizeniDiffPodvozku.mbs"));
            string cs = File.ReadAllText(Path.Combine(koren, "Src", "ARBot.HAL", "Devices", "MotorDriver", "SDC2160Ex.cs"));

            int a = cs.IndexOf("' var 1 - dopredna akcelerace", StringComparison.Ordinal);
            int b = cs.IndexOf("end while", a, StringComparison.Ordinal);
            Assert.That(a, Is.GreaterThanOrEqualTo(0), "kopie skriptu v SDC2160Ex nenalezena");
            string kopie = cs.Substring(a, b + "end while".Length - a);

            int ma = mbs.IndexOf("' var 1 - dopredna akcelerace", StringComparison.Ordinal);
            int mb = mbs.IndexOf("end while", ma, StringComparison.Ordinal);
            string zdroj = mbs.Substring(ma, mb + "end while".Length - ma);

            Assert.That(Normalizuj(kopie), Is.EqualTo(Normalizuj(zdroj)));
        }

        /// <summary>
        /// Vychozi rampy ve skriptu (plati, dokud host nic neposle — napr. po restartu jednotky)
        /// musi odpovidat <see cref="Profile"/>, jinak by jednotka po restartu brzdila jinak, nez
        /// se o ni vi.
        /// </summary>
        [Test]
        public void VychoziRampyVeSkriptu_OdpovidajiProfile()
        {
            string mbs = File.ReadAllText(Path.Combine(Koren(), "Src", "RoboRun", "RizeniDiffPodvozku.mbs"));
            int Hodnota(string jmeno)
            {
                var m = Regex.Match(mbs, "^" + jmeno + @"=(\d+)", RegexOptions.Multiline);
                Assert.That(m.Success, Is.True, jmeno + " ve skriptu nenalezeno");
                return int.Parse(m.Groups[1].Value);
            }

            Assert.Multiple(() =>
            {
                Assert.That(Hodnota("defAcc"),
                            Is.EqualTo(MotorAcceleration.ToScriptUnits(Profile.MaxAcceleration, Profile.MaxTheoreticalSpeed)));
                Assert.That(Hodnota("defEmDec"),
                            Is.EqualTo(MotorAcceleration.ToScriptUnits(Profile.EmergencyDeceleration, Profile.MaxTheoreticalSpeed)));
                Assert.That(mbs, Does.Contain("print(\"Version 2.2\\r\")"));
            });
        }
    }
}
