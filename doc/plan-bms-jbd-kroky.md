# Driver chytré BMS JBD — implementační kroky

> **Pro agentní pracovníky:** plán se plní **task po tasku** (doporučeno
> `superpowers:subagent-driven-development`, jinak `superpowers:executing-plans`), kroky mají
> checkboxy (`- [ ]`). Každý task končí zeleným buildem a testy pod `x64`. **Nekomitovat bez
> pokynu autora** (viz [CLAUDE.md](../CLAUDE.md)) — commity v krocích níž jsou připravené texty,
> ne pokyn.

**Cíl:** Robot čte z BMS JBD (přes USB–RS485) stav nabití, proud, napětí článků, teplotu
a příznaky ochran, posílá je jako zprávu `BmsState` do pipeline a záznamu a ukazuje je na stránce
náhledu; varování o slabé baterii jde podle procent, bez BMS dál podle napětí.

**Návrh:** Protokol (`JbdProtocol`) je statická třída nad bajty bez portu — sestaví dotaz, najde
rámec v bufferu, ověří součet a rozebere data do `BmsState`. Tenký driver `JbdBms`
(`UartSensorBase<BmsState>`) jednou za sekundu pošle dva dotazy a vydá zprávu, po chybě zprávu
bez měření. `BatteryMonitor` zůstává jediným zdrojem pravdy pro stránku i `Trace`.

**Technologie:** .NET 10, C#, NUnit 4 (`<Using Include="NUnit.Framework" />` v testovacích
projektech), `System.IO.Ports` přes stávající `Uart`.

**Spec:** [plan-bms-jbd.md](plan-bms-jbd.md) — rozsah, protokol, rozhodnutí. Čti obojí.

## Globální omezení

- **Jazyk čeština** — komentáře, dokumentace, texty na stránce. Hlášky do `Trace` bez diakritiky
  (vzor `BatteryMonitor`), texty pro stránku s diakritikou. Jména testů bez diakritiky.
- **Build a testy pro konkrétní platformu, NE `AnyCPU`:** `-p:Platform=x64` u každého `dotnet`.
- **Jen čtení:** driver smí na port zapsat výhradně dotazy `DD A5 03 00 FF FD 77`
  a `DD A5 04 00 FF FC 77`.
- **Nic se podle BMS neřídí** — žádná změna v misích, navigaci ani `ControlLoop`.
- **Diagnostika poruch do `Trace`**, ne `Debug`, škrcená přes `PoruchaHlasic`; nový soubor driveru
  do výčtu `DiagnostikaPoruchTests.Soubory`.
- **Čas přes `TimeBase.Now`**, ne `DateTime.Now`.
- **Bez BMS se nic nemění:** prázdné `UartBms=` → BMS se nezakládá, stránka i varování jako dnes.
- **Zpráva je pasivní DTO** — převod z bajtů dělá `JbdProtocol`, ne `BmsState`.
- Výchozí `Profile.PortBms` je **prázdný na všech platformách** (cesta `by-id` se zjistí až na
  zařízení, fáze 4 specifikace).

## Review Focus

1. **Odpověď na předchozí dotaz dorazí pozdě** (odpověď na 0x03 přijde až po odeslání 0x04) —
   driver ji musí zahodit podle registru, ne ji rozebrat jako napětí článků. Test v Tasku 3.
2. **Bajt `0xDD` uprostřed dat** (např. napětí 0x0CDD) po ztrátě začátku rámce — hledání rámce
   nesmí vzít nesmyslnou délku a čekat na 200 bajtů. Test v Tasku 2.
3. **BMS trvale mlčí** — driver vydává zprávy bez měření, takže `SensorBase` ho nevidí jako tichý;
   `IsError` musí přesto ukázat chybu a `Trace` dostat jednu hlášku o výpadku, ne proud. Test v Tasku 3.
4. **Data z BMS zestárnou, motory dál hlásí** — varování se musí vrátit k napětí, ne držet staré
   procento. Test v Tasku 6.
5. **Neznámý bit ochrany** (jiný firmware) — popis ho nesmí zahodit ani shodit stránku. Test v Tasku 1.

---

### Task 1: Zpráva `BmsState` a příznaky ochran

**Files:**
- Create: `Src/ARBot.Common/Devices/BmsProtection.cs`
- Create: `Src/ARBot.Common/Devices/BmsState.cs`
- Modify: `Src/ARBot.Common/Communication/MessageCatalog.cs` (`RecordDefaults()`, ř. ~88)
- Modify: `Src/ARBot.Common.Tests/Communication/RecordCatalogTests.cs` (`RecordDefaults_ZnaStavyZarizeni`)
- Test: `Src/ARBot.Common.Tests/Devices/BmsStateTests.cs`

**Interfaces:**
- Produces: `enum BmsProtection : ushort` (bity JBD), `static string BmsProtectionText.Popis(BmsProtection)`,
  `class BmsState : SensorStateBase` s vlastnostmi `HasMeasurement`, `PackVoltage`, `Current`,
  `RemainingAh`, `NominalAh`, `SocPercent` (int), `Cycles` (int), `CellVoltages` (double[]),
  `Temperatures` (double[]), `BalanceMask` (uint), `Protection`, `ChargeFetOn`, `DischargeFetOn`,
  odvozené `CellMinV`, `CellMaxV`, `TempMaxC`, a `static BmsState NoMeasurement(DateTime t)`.

- [ ] **Step 1: Napiš padající testy**

`Src/ARBot.Common.Tests/Devices/BmsStateTests.cs`:

```csharp
using System;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using NUnit.Framework;

namespace ARBot.Common.Tests.Devices
{
    /// <summary>Zpráva z BMS (plan-bms-jbd.md): přežije záznam a popíše ochrany česky.</summary>
    [TestFixture]
    public class BmsStateTests
    {
        private static BmsState RoundTrip(BmsState s)
        {
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, Encoding.UTF8);
            w.Write(s);
            w.Flush();
            var r = new MessageReader(new MemoryStream(ms.ToArray()), Encoding.UTF8,
                                      MessageCatalog.RecordDefaults().ToPrototypeMap());
            return (BmsState)r.Read();
        }

        [Test]
        public void ZapisACteni_VratiVsechnaPole()
        {
            var t = new DateTime(2026, 10, 8, 12, 0, 0);
            var s = new BmsState
            {
                TimeStamp = t, PackVoltage = 13.21, Current = -4.3, RemainingAh = 9.6, NominalAh = 15,
                SocPercent = 64, Cycles = 12, CellVoltages = new[] { 3.28, 3.30, 3.31, 3.30 },
                Temperatures = new[] { 24.5, 23.0 }, BalanceMask = 0b0101,
                Protection = BmsProtection.CellUndervoltage, ChargeFetOn = true, DischargeFetOn = false,
            };

            var b = RoundTrip(s);

            Assert.Multiple(() =>
            {
                Assert.That(b.HasMeasurement, Is.True);
                Assert.That(b.TimeStamp, Is.EqualTo(t));
                Assert.That(b.PackVoltage, Is.EqualTo(13.21));
                Assert.That(b.Current, Is.EqualTo(-4.3));
                Assert.That(b.RemainingAh, Is.EqualTo(9.6));
                Assert.That(b.NominalAh, Is.EqualTo(15));
                Assert.That(b.SocPercent, Is.EqualTo(64));
                Assert.That(b.Cycles, Is.EqualTo(12));
                Assert.That(b.CellVoltages, Is.EqualTo(new[] { 3.28, 3.30, 3.31, 3.30 }));
                Assert.That(b.Temperatures, Is.EqualTo(new[] { 24.5, 23.0 }));
                Assert.That(b.BalanceMask, Is.EqualTo(0b0101u));
                Assert.That(b.Protection, Is.EqualTo(BmsProtection.CellUndervoltage));
                Assert.That(b.ChargeFetOn, Is.True);
                Assert.That(b.DischargeFetOn, Is.False);
            });
        }

        [Test]
        public void BezMereni_PrezijeZaznam()
        {
            var b = RoundTrip(BmsState.NoMeasurement(new DateTime(2026, 10, 8)));
            Assert.That(b.HasMeasurement, Is.False);
            Assert.That(b.CellVoltages, Is.Empty);
        }

        [Test]
        public void OdvozeneHodnoty_ZClankuATeplot()
        {
            var s = new BmsState { CellVoltages = new[] { 3.28, 3.31, 3.30 }, Temperatures = new[] { 20.0, 26.5 } };
            Assert.That(s.CellMinV, Is.EqualTo(3.28));
            Assert.That(s.CellMaxV, Is.EqualTo(3.31));
            Assert.That(s.TempMaxC, Is.EqualTo(26.5));
            Assert.That(double.IsNaN(new BmsState().CellMinV), Is.True, "bez clanku neni minimum, ne nula");
        }

        [Test]
        public void PopisOchran_Cesky_IVicePriznaku()
        {
            Assert.That(BmsProtectionText.Popis(BmsProtection.None), Is.EqualTo("žádná"));
            Assert.That(BmsProtectionText.Popis(BmsProtection.CellUndervoltage | BmsProtection.ShortCircuit),
                        Is.EqualTo("podpětí článku, zkrat"));
        }

        [Test]
        public void PopisOchran_NeznamyBit_NeztratiSe()
        {
            var p = (BmsProtection)(1 << 14);
            Assert.That(BmsProtectionText.Popis(p), Is.EqualTo("neznámý příznak 0x4000"));
        }
    }
}
```

V `RecordCatalogTests.RecordDefaults_ZnaStavyZarizeni` přidej do `Assert.Multiple` řádek:

```csharp
                Assert.That(k.ContainsKey(new BmsState().MsgName), Is.True, "BmsState");
```

- [ ] **Step 2: Spusť testy, musí spadnout**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~BmsStateTests|FullyQualifiedName~RecordCatalogTests"`
Expected: build FAIL — `BmsState` / `BmsProtection` neexistují.

- [ ] **Step 3: Napiš `BmsProtection.cs`**

```csharp
using System.Collections.Generic;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Příznaky zásahu ochrany BMS. Hodnoty bitů jsou bity registru ochran JBD (základní údaje,
    /// bajty 16–17), takže driver je předá bez převodu; jiná BMS by si je mapovala sama.
    /// Viz doc/plan-bms-jbd.md.
    /// </summary>
    [System.Flags]
    public enum BmsProtection : ushort
    {
        None = 0,
        CellOvervoltage = 1 << 0,
        CellUndervoltage = 1 << 1,
        PackOvervoltage = 1 << 2,
        PackUndervoltage = 1 << 3,
        ChargeOvertemp = 1 << 4,
        ChargeUndertemp = 1 << 5,
        DischargeOvertemp = 1 << 6,
        DischargeUndertemp = 1 << 7,
        ChargeOvercurrent = 1 << 8,
        DischargeOvercurrent = 1 << 9,
        ShortCircuit = 1 << 10,
        FrontEndError = 1 << 11,
        MosLocked = 1 << 12,
    }

    /// <summary>Český popis příznaků pro stránku náhledu a <c>Trace</c>.</summary>
    public static class BmsProtectionText
    {
        private static readonly (BmsProtection Priznak, string Text)[] Texty =
        {
            (BmsProtection.CellOvervoltage, "přepětí článku"),
            (BmsProtection.CellUndervoltage, "podpětí článku"),
            (BmsProtection.PackOvervoltage, "přepětí baterie"),
            (BmsProtection.PackUndervoltage, "podpětí baterie"),
            (BmsProtection.ChargeOvertemp, "přehřátí při nabíjení"),
            (BmsProtection.ChargeUndertemp, "mráz při nabíjení"),
            (BmsProtection.DischargeOvertemp, "přehřátí při vybíjení"),
            (BmsProtection.DischargeUndertemp, "mráz při vybíjení"),
            (BmsProtection.ChargeOvercurrent, "nadproud nabíjení"),
            (BmsProtection.DischargeOvercurrent, "nadproud vybíjení"),
            (BmsProtection.ShortCircuit, "zkrat"),
            (BmsProtection.FrontEndError, "chyba měřicího obvodu"),
            (BmsProtection.MosLocked, "spínače zamčené"),
        };

        /// <summary>Popis všech nastavených příznaků; neznámé bity se vypíšou číslem, nezahodí.</summary>
        public static string Popis(BmsProtection p)
        {
            if (p == BmsProtection.None) return "žádná";
            var casti = new List<string>();
            int znamo = 0;
            foreach (var (priznak, text) in Texty)
            {
                znamo |= (int)priznak;
                if ((p & priznak) != 0) casti.Add(text);
            }
            int nezname = (int)p & ~znamo;
            if (nezname != 0) casti.Add($"neznámý příznak 0x{nezname:X4}");
            return string.Join(", ", casti);
        }
    }
}
```

- [ ] **Step 4: Napiš `BmsState.cs`**

```csharp
using System;
using System.IO;
using System.Linq;
using ARBot.Common.Logs;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Stav baterie z chytré BMS (doc/plan-bms-jbd.md). Pasivní DTO — z bajtů ho plní
    /// <c>JbdProtocol</c> v HAL. Proud je <b>kladný při nabíjení</b> (konvence JBD).
    ///
    /// <para><see cref="HasMeasurement"/> = false je zástupná zpráva po chybě komunikace (vzor
    /// <see cref="MotorStateBase.HasMeasurement"/>): ostatní pole v ní nic neznamenají.</para>
    /// </summary>
    public sealed class BmsState : SensorStateBase
    {
        /// <summary>Verze formátu serializace (viz doc/record-replay.md → Verzování zpráv).</summary>
        public const int FormatVersion = 1;

        /// <summary>Strop počtu článků/čidel při čtení — ochrana proti poškozenému záznamu.</summary>
        private const int MaxPolozek = 64;

        /// <summary>Bezparametrický ctor (nutný pro Build/reflexi prototypů zpráv).</summary>
        public BmsState() : base(FormatVersion) { }

        /// <summary>Nese zpráva skutečné měření? <c>false</c> = zástupná zpráva po chybě komunikace.</summary>
        public bool HasMeasurement { get; set; } = true;
        /// <summary>Napětí baterie [V].</summary>
        public double PackVoltage { get; set; }
        /// <summary>Proud [A], kladný = nabíjení.</summary>
        public double Current { get; set; }
        /// <summary>Zbývající kapacita [Ah] podle počítání náboje v BMS.</summary>
        public double RemainingAh { get; set; }
        /// <summary>Jmenovitá kapacita [Ah] nastavená v BMS.</summary>
        public double NominalAh { get; set; }
        /// <summary>Stav nabití [%] 0–100.</summary>
        public int SocPercent { get; set; }
        /// <summary>Počet cyklů podle BMS.</summary>
        public int Cycles { get; set; }
        /// <summary>Napětí článků [V] v pořadí od B−.</summary>
        public double[] CellVoltages { get; set; } = Array.Empty<double>();
        /// <summary>Teploty čidel [°C].</summary>
        public double[] Temperatures { get; set; } = Array.Empty<double>();
        /// <summary>Maska právě vyvažovaných článků (bit 0 = článek 1).</summary>
        public uint BalanceMask { get; set; }
        /// <summary>Příznaky zásahu ochrany.</summary>
        public BmsProtection Protection { get; set; }
        /// <summary>Nabíjecí spínač sepnutý.</summary>
        public bool ChargeFetOn { get; set; }
        /// <summary>Vybíjecí spínač sepnutý.</summary>
        public bool DischargeFetOn { get; set; }

        /// <summary>Nejnižší napětí článku [V], <c>NaN</c> bez článků.</summary>
        public double CellMinV => CellVoltages.Length > 0 ? CellVoltages.Min() : double.NaN;
        /// <summary>Nejvyšší napětí článku [V], <c>NaN</c> bez článků.</summary>
        public double CellMaxV => CellVoltages.Length > 0 ? CellVoltages.Max() : double.NaN;
        /// <summary>Nejvyšší teplota [°C], <c>NaN</c> bez čidel.</summary>
        public double TempMaxC => Temperatures.Length > 0 ? Temperatures.Max() : double.NaN;

        /// <summary>Zástupná zpráva po chybě komunikace.</summary>
        public static BmsState NoMeasurement(DateTime t) => new BmsState { HasMeasurement = false, TimeStamp = t };

        /// <inheritdoc/>
        public override Message Build() => new BmsState();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            WriteMeta(bw);
            bw.Write(HasMeasurement);
            bw.Write(PackVoltage);
            bw.Write(Current);
            bw.Write(RemainingAh);
            bw.Write(NominalAh);
            bw.Write(SocPercent);
            bw.Write(Cycles);
            bw.Write(CellVoltages.Length);
            foreach (var v in CellVoltages) bw.Write(v);
            bw.Write(Temperatures.Length);
            foreach (var v in Temperatures) bw.Write(v);
            bw.Write(BalanceMask);
            bw.Write((ushort)Protection);
            bw.Write(ChargeFetOn);
            bw.Write(DischargeFetOn);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            ReadMeta(br);
            HasMeasurement = br.ReadBoolean();
            PackVoltage = br.ReadDouble();
            Current = br.ReadDouble();
            RemainingAh = br.ReadDouble();
            NominalAh = br.ReadDouble();
            SocPercent = br.ReadInt32();
            Cycles = br.ReadInt32();
            CellVoltages = CtiPole(br);
            Temperatures = CtiPole(br);
            BalanceMask = br.ReadUInt32();
            Protection = (BmsProtection)br.ReadUInt16();
            ChargeFetOn = br.ReadBoolean();
            DischargeFetOn = br.ReadBoolean();
        }

        private static double[] CtiPole(BinaryReader br)
        {
            int n = br.ReadInt32();
            if (n < 0 || n > MaxPolozek)
                throw new InvalidDataException($"BmsState: nesmyslny pocet polozek {n}");
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = br.ReadDouble();
            return a;
        }
    }
}
```

- [ ] **Step 5: Zaregistruj zprávu v katalogu záznamu**

V `MessageCatalog.RecordDefaults()`:

```csharp
        public static MessageCatalog RecordDefaults()
            => CommonDefaults()
                .Register(new GPSState())
                .Register(new MotorStateBase())
                .Register(new BmsState())
                .Register(new CameraFrame());
```

- [ ] **Step 6: Spusť testy, musí projít**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Expected: PASS celý projekt (včetně `RecordDefaults_ZnaVsechnyZpravyZCommon`).

- [ ] **Step 7: Commit (jen na pokyn autora)**

```bash
git add Src/ARBot.Common/Devices/BmsProtection.cs Src/ARBot.Common/Devices/BmsState.cs Src/ARBot.Common/Communication/MessageCatalog.cs Src/ARBot.Common.Tests/Devices/BmsStateTests.cs Src/ARBot.Common.Tests/Communication/RecordCatalogTests.cs
git commit -m "BMS: zprava BmsState a priznaky ochran, v katalogu zaznamu"
```

---

### Task 2: Protokol JBD (`JbdProtocol`)

**Files:**
- Create: `Src/ARBot.HAL/Devices/Bms/JbdProtocol.cs`
- Test: `Src/ARBot.HAL.Tests/JbdProtocolTests.cs`

**Interfaces:**
- Consumes: `BmsState`, `BmsProtection` (Task 1).
- Produces (namespace `ARBot.HAL.Devices.Bms`):
  - konstanty `JbdProtocol.Start = 0xDD`, `End = 0x77`, `ReadCmd = 0xA5`, `RegBasic = 0x03`,
    `RegCells = 0x04`, `BaudRate = 9600`, `RequestLength = 7`, `MaxDataLength = 64`, `MaxCells = 32`, `MaxSensors = 8`
  - `static byte[] ReadRequest(byte reg)`
  - `static ushort Checksum(IReadOnlyList<byte> b, int from, int count)`
  - `enum JbdVysledek { MaloDat, Ramec, Chyba }`, `readonly struct JbdRamec { byte Reg; byte Status; byte[] Data; }`
  - `static JbdVysledek TryExtract(List<byte> buf, out JbdRamec ramec, out string chyba)` — spotřebuje z `buf` zahozené a vrácené bajty
  - `static bool TryParseBasic(byte[] data, BmsState s, out int pocetClanku, out string chyba)`
  - `static bool TryParseCells(byte[] data, int pocetClanku, out double[] napeti, out string chyba)`

- [ ] **Step 1: Napiš padající testy**

`Src/ARBot.HAL.Tests/JbdProtocolTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.HAL.Devices.Bms;

namespace ARBot.HAL.Tests
{
    /// <summary>
    /// Protokol JBD BMS nad bajty (doc/plan-bms-jbd.md). Součet v pomocné funkci <see cref="Ramec"/>
    /// se počítá ZVLÁŠŤ od produkčního kódu; pevné vektory (dotazy, odpověď 0x04) jsou spočítané ručně.
    /// </summary>
    public class JbdProtocolTests
    {
        /// <summary>Odpověď BMS: DD reg 00 len data chkH chkL 77, součet = 0x10000 − Σ(stav, délka, data).</summary>
        internal static byte[] Ramec(byte reg, byte[] data, byte status = 0)
        {
            int sum = status + data.Length + data.Sum(b => b);
            int chk = (0x10000 - sum) & 0xFFFF;
            return new byte[] { 0xDD, reg, status, (byte)data.Length }
                .Concat(data).Concat(new[] { (byte)(chk >> 8), (byte)chk, (byte)0x77 }).ToArray();
        }

        /// <summary>Data registru 0x03 (big-endian) se zadanými hodnotami.</summary>
        internal static byte[] ZakladniData(double napetiV = 13.16, double proudA = -4.3, double zbyvaAh = 9.6,
            double jmenovitaAh = 15.0, int cykly = 12, int vyvazovani = 0, int ochrany = 0, int soc = 64,
            int spinace = 0x03, int clanku = 4, double[] teplotyC = null)
        {
            teplotyC ??= new[] { 24.5 };
            var d = new List<byte>();
            void U16(int v) { d.Add((byte)(v >> 8)); d.Add((byte)v); }
            U16((int)Math.Round(napetiV * 100));
            U16((ushort)(short)Math.Round(proudA * 100));
            U16((int)Math.Round(zbyvaAh * 100));
            U16((int)Math.Round(jmenovitaAh * 100));
            U16(cykly);
            U16(0x5A21);                 // datum vyroby - nepouziva se
            U16(vyvazovani & 0xFFFF);
            U16(vyvazovani >> 16);
            U16(ochrany);
            d.Add(0x10);                 // verze SW
            d.Add((byte)soc);
            d.Add((byte)spinace);
            d.Add((byte)clanku);
            d.Add((byte)teplotyC.Length);
            foreach (var t in teplotyC) U16((int)Math.Round(t * 10) + 2731);
            return d.ToArray();
        }

        [Test]
        public void Dotazy_PresneNaBajt()
        {
            Assert.That(JbdProtocol.ReadRequest(JbdProtocol.RegBasic),
                        Is.EqualTo(new byte[] { 0xDD, 0xA5, 0x03, 0x00, 0xFF, 0xFD, 0x77 }));
            Assert.That(JbdProtocol.ReadRequest(JbdProtocol.RegCells),
                        Is.EqualTo(new byte[] { 0xDD, 0xA5, 0x04, 0x00, 0xFF, 0xFC, 0x77 }));
        }

        [Test]
        public void Ramec_PevnyVektorClanku_SeVytahne()
        {
            // 4 clanky po 3290 mV (0x0CDA); soucet 0x10000 − (0+8+4·(0x0C+0xDA)) = 0xFC60, rucne.
            var buf = new List<byte> { 0xDD, 0x04, 0x00, 0x08, 0x0C, 0xDA, 0x0C, 0xDA, 0x0C, 0xDA, 0x0C, 0xDA, 0xFC, 0x60, 0x77 };

            var v = JbdProtocol.TryExtract(buf, out var r, out _);

            Assert.That(v, Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x04));
            Assert.That(r.Data.Length, Is.EqualTo(8));
            Assert.That(buf, Is.Empty, "ramec se z bufferu spotrebuje");
        }

        [Test]
        public void Ramec_NeuplnyACastecny_ChceVicDat()
        {
            var cely = Ramec(0x04, new byte[] { 0x0C, 0xDA });
            var buf = cely.Take(5).ToList();
            Assert.That(JbdProtocol.TryExtract(buf, out _, out _), Is.EqualTo(JbdVysledek.MaloDat));
            buf.AddRange(cely.Skip(5));
            Assert.That(JbdProtocol.TryExtract(buf, out _, out _), Is.EqualTo(JbdVysledek.Ramec));
        }

        [Test]
        public void Ramec_SmetiPredZacatkem_SeZahodi()
        {
            var buf = new List<byte> { 0x00, 0x13, 0x77 };
            buf.AddRange(Ramec(0x04, new byte[] { 0x0C, 0xDA }));
            Assert.That(JbdProtocol.TryExtract(buf, out var r, out _), Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x04));
        }

        [Test]
        public void Ramec_OzvenaDotazu_SePreskoci()
        {
            var buf = JbdProtocol.ReadRequest(JbdProtocol.RegCells).ToList();
            buf.AddRange(Ramec(0x04, new byte[] { 0x0C, 0xDA }));
            Assert.That(JbdProtocol.TryExtract(buf, out var r, out _), Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x04));
        }

        [Test]
        public void Ramec_DDUprostredDat_NesmyslnaDelka_NeceKaNaDalsiBajty()
        {
            // Ztraceny zacatek ramce: zbyde "... 0C DD FF ..." - 0xFF jako delka je nad MaxDataLength.
            var buf = new List<byte> { 0xDD, 0x0C, 0x00, 0xFF, 0x01 };
            buf.AddRange(Ramec(0x03, ZakladniData()));
            Assert.That(JbdProtocol.TryExtract(buf, out var r, out _), Is.EqualTo(JbdVysledek.Ramec));
            Assert.That(r.Reg, Is.EqualTo(0x03));
        }

        [Test]
        public void Ramec_SpatnySoucet_JeChyba()
        {
            var b = Ramec(0x04, new byte[] { 0x0C, 0xDA });
            b[4] ^= 0x01;
            var buf = b.ToList();
            Assert.That(JbdProtocol.TryExtract(buf, out _, out var chyba), Is.EqualTo(JbdVysledek.Chyba));
            Assert.That(chyba, Does.Contain("soucet"));
            Assert.That(buf, Is.Empty, "vadny ramec se spotrebuje, nezustane viset");
        }

        [Test]
        public void Ramec_ChybovyStav_JeChyba()
        {
            var buf = Ramec(0x03, Array.Empty<byte>(), status: 0x80).ToList();
            Assert.That(JbdProtocol.TryExtract(buf, out _, out var chyba), Is.EqualTo(JbdVysledek.Chyba));
            Assert.That(chyba, Does.Contain("0x80"));
        }

        [Test]
        public void Zakladni_RozeberHodnoty()
        {
            var s = new BmsState();
            bool ok = JbdProtocol.TryParseBasic(
                ZakladniData(napetiV: 13.16, proudA: -4.3, zbyvaAh: 9.6, jmenovitaAh: 15, cykly: 12,
                             vyvazovani: 0x0001_0005, ochrany: 0x0002, soc: 64, spinace: 0x01, clanku: 4,
                             teplotyC: new[] { 24.5, -3.0 }),
                s, out int clanku, out var chyba);

            Assert.That(ok, Is.True, chyba);
            Assert.Multiple(() =>
            {
                Assert.That(clanku, Is.EqualTo(4));
                Assert.That(s.PackVoltage, Is.EqualTo(13.16).Within(1e-9));
                Assert.That(s.Current, Is.EqualTo(-4.3).Within(1e-9), "proud je se znamenkem");
                Assert.That(s.RemainingAh, Is.EqualTo(9.6).Within(1e-9));
                Assert.That(s.NominalAh, Is.EqualTo(15).Within(1e-9));
                Assert.That(s.Cycles, Is.EqualTo(12));
                Assert.That(s.BalanceMask, Is.EqualTo(0x0001_0005u), "spodni slovo = clanky 1-16, horni = 17-32");
                Assert.That(s.Protection, Is.EqualTo(BmsProtection.CellUndervoltage));
                Assert.That(s.SocPercent, Is.EqualTo(64));
                Assert.That(s.ChargeFetOn, Is.True);
                Assert.That(s.DischargeFetOn, Is.False);
                Assert.That(s.Temperatures, Is.EqualTo(new[] { 24.5, -3.0 }).Within(1e-9), "0,1 K - 273,1");
            });
        }

        [TestCase(0, TestName = "Zakladni_NulaClanku_Odmitne")]
        [TestCase(33, TestName = "Zakladni_PrilisClanku_Odmitne")]
        public void Zakladni_NesmyslnyPocetClanku(int clanku)
        {
            Assert.That(JbdProtocol.TryParseBasic(ZakladniData(clanku: clanku), new BmsState(), out _, out var chyba), Is.False);
            Assert.That(chyba, Does.Contain("clanku"));
        }

        [Test]
        public void Zakladni_StavNabitiNad100_Odmitne()
            => Assert.That(JbdProtocol.TryParseBasic(ZakladniData(soc: 101), new BmsState(), out _, out _), Is.False);

        [Test]
        public void Zakladni_KratkaData_Odmitne()
            => Assert.That(JbdProtocol.TryParseBasic(new byte[10], new BmsState(), out _, out _), Is.False);

        [Test]
        public void Zakladni_ChybiTeploty_Odmitne()
        {
            var d = ZakladniData(teplotyC: new[] { 20.0, 21.0 });
            Assert.That(JbdProtocol.TryParseBasic(d.Take(d.Length - 2).ToArray(), new BmsState(), out _, out _), Is.False);
        }

        [Test]
        public void Clanky_RozeberNapeti()
        {
            bool ok = JbdProtocol.TryParseCells(new byte[] { 0x0C, 0xDA, 0x0C, 0xD0 }, 2, out var v, out _);
            Assert.That(ok, Is.True);
            Assert.That(v, Is.EqualTo(new[] { 3.290, 3.280 }).Within(1e-9));
        }

        [Test]
        public void Clanky_JinyPocetNezZakladni_Odmitne()
            => Assert.That(JbdProtocol.TryParseCells(new byte[] { 0x0C, 0xDA }, 4, out _, out _), Is.False);
    }
}
```

- [ ] **Step 2: Spusť testy, musí spadnout**

Run: `dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~JbdProtocolTests"`
Expected: build FAIL — `ARBot.HAL.Devices.Bms` neexistuje.

- [ ] **Step 3: Napiš `JbdProtocol.cs`**

```csharp
using System.Collections.Generic;
using ARBot.Common.Devices;

namespace ARBot.HAL.Devices.Bms
{
    /// <summary>Výsledek hledání rámce v přijatých bajtech.</summary>
    public enum JbdVysledek
    {
        /// <summary>Rámec ještě není celý - číst dál.</summary>
        MaloDat,
        /// <summary>Platný rámec (součet sedí, stav 0).</summary>
        Ramec,
        /// <summary>Vadný rámec (součet, chybový stav) - spotřebovaný, důvod v <c>chyba</c>.</summary>
        Chyba,
    }

    /// <summary>Platná odpověď BMS: registr a data.</summary>
    public readonly struct JbdRamec
    {
        public JbdRamec(byte reg, byte status, byte[] data) { Reg = reg; Status = status; Data = data; }
        public byte Reg { get; }
        public byte Status { get; }
        public byte[] Data { get; }
    }

    /// <summary>
    /// Protokol BMS JBD (Jiabaida) nad bajty, bez portu - testovatelný bez hardwaru
    /// (doc/plan-bms-jbd.md). Jen ČTENÍ: dotazy 0x03 (základní údaje) a 0x04 (napětí článků).
    ///
    /// <para>Rámec dotazu <c>DD A5 reg 00 chkH chkL 77</c>, odpovědi <c>DD reg stav délka data
    /// chkH chkL 77</c>; součet = <c>0x10000 − Σ</c> bajtů od pole za registrem (u dotazu
    /// <c>reg</c> a <c>00</c>, u odpovědi stav, délka a data). Rozložení dat je z veřejné
    /// dokumentace; na skutečném SP04S020 se ověří ve fázi 4.</para>
    /// </summary>
    public static class JbdProtocol
    {
        public const byte Start = 0xDD;
        public const byte End = 0x77;
        public const byte ReadCmd = 0xA5;
        public const byte RegBasic = 0x03;
        public const byte RegCells = 0x04;
        public const int BaudRate = 9600;
        public const int RequestLength = 7;
        public const int MaxDataLength = 64;
        public const int MaxCells = 32;
        public const int MaxSensors = 8;
        private const int BasicMinLength = 23;

        /// <summary>Dotaz na čtení registru <paramref name="reg"/>.</summary>
        public static byte[] ReadRequest(byte reg)
        {
            var b = new byte[] { Start, ReadCmd, reg, 0x00, 0, 0, End };
            ushort chk = Checksum(b, 2, 2);
            b[4] = (byte)(chk >> 8);
            b[5] = (byte)chk;
            return b;
        }

        /// <summary>Součet JBD: <c>0x10000 − Σ</c> bajtů <c>b[from .. from+count)</c>.</summary>
        public static ushort Checksum(IReadOnlyList<byte> b, int from, int count)
        {
            int sum = 0;
            for (int i = from; i < from + count; i++) sum += b[i];
            return (ushort)(0x10000 - sum);
        }

        /// <summary>
        /// Najde v <paramref name="buf"/> první odpověď. Zahodí smetí před <c>0xDD</c>, ozvěnu
        /// vlastního dotazu (některé převodníky RS485) a „začátky" s nesmyslnou délkou (bajt
        /// <c>0xDD</c> uprostřed dat). Vrácený i vadný rámec z bufferu odebere.
        /// </summary>
        public static JbdVysledek TryExtract(List<byte> buf, out JbdRamec ramec, out string chyba)
        {
            ramec = default;
            chyba = null;
            while (true)
            {
                int start = buf.IndexOf(Start);
                if (start < 0) { buf.Clear(); return JbdVysledek.MaloDat; }
                if (start > 0) buf.RemoveRange(0, start);
                if (buf.Count < 4) return JbdVysledek.MaloDat;

                if (buf[1] == ReadCmd)
                {
                    // Ozvena dotazu (DD A5 reg 00 chk chk 77) - odpoved ma na tomhle miste registr.
                    if (buf.Count < RequestLength) return JbdVysledek.MaloDat;
                    buf.RemoveRange(0, RequestLength);
                    continue;
                }

                byte reg = buf[1];
                byte status = buf[2];
                int len = buf[3];
                if (len > MaxDataLength)
                {
                    buf.RemoveAt(0);   // 0xDD nebyl zacatek ramce
                    continue;
                }

                int celkem = 4 + len + 3;
                if (buf.Count < celkem) return JbdVysledek.MaloDat;
                if (buf[celkem - 1] != End)
                {
                    buf.RemoveAt(0);
                    continue;
                }

                ushort prijaty = (ushort)((buf[4 + len] << 8) | buf[5 + len]);
                ushort spocteny = Checksum(buf, 2, 2 + len);
                byte[] data = buf.GetRange(4, len).ToArray();
                buf.RemoveRange(0, celkem);

                if (prijaty != spocteny)
                {
                    chyba = $"spatny kontrolni soucet odpovedi (registr 0x{reg:X2})";
                    return JbdVysledek.Chyba;
                }
                if (status != 0)
                {
                    chyba = $"BMS vratila chybovy stav 0x{status:X2} (registr 0x{reg:X2})";
                    return JbdVysledek.Chyba;
                }
                ramec = new JbdRamec(reg, status, data);
                return JbdVysledek.Ramec;
            }
        }

        /// <summary>Rozebere data registru 0x03 do <paramref name="s"/> (bez napětí článků).</summary>
        public static bool TryParseBasic(byte[] d, BmsState s, out int pocetClanku, out string chyba)
        {
            pocetClanku = 0;
            chyba = null;
            if (d == null || d.Length < BasicMinLength)
            {
                chyba = $"zakladni udaje: kratka data ({d?.Length ?? 0} B)";
                return false;
            }
            int clanku = d[21];
            int cidel = d[22];
            if (clanku < 1 || clanku > MaxCells)
            {
                chyba = $"zakladni udaje: nesmyslny pocet clanku {clanku}";
                return false;
            }
            if (cidel > MaxSensors || d.Length < BasicMinLength + 2 * cidel)
            {
                chyba = $"zakladni udaje: {cidel} cidel v {d.Length} B";
                return false;
            }
            int soc = d[19];
            if (soc > 100)
            {
                chyba = $"zakladni udaje: stav nabiti {soc} %";
                return false;
            }

            s.PackVoltage = U16(d, 0) * 0.01;
            s.Current = (short)U16(d, 2) * 0.01;
            s.RemainingAh = U16(d, 4) * 0.01;
            s.NominalAh = U16(d, 6) * 0.01;
            s.Cycles = U16(d, 8);
            s.BalanceMask = (uint)U16(d, 12) | ((uint)U16(d, 14) << 16);
            s.Protection = (BmsProtection)(ushort)U16(d, 16);
            s.SocPercent = soc;
            s.ChargeFetOn = (d[20] & 0x01) != 0;
            s.DischargeFetOn = (d[20] & 0x02) != 0;
            var t = new double[cidel];
            for (int i = 0; i < cidel; i++) t[i] = (U16(d, BasicMinLength + 2 * i) - 2731) / 10.0;
            s.Temperatures = t;
            pocetClanku = clanku;
            return true;
        }

        /// <summary>Rozebere data registru 0x04: 2 B na článek v mV.</summary>
        public static bool TryParseCells(byte[] d, int pocetClanku, out double[] napeti, out string chyba)
        {
            napeti = null;
            chyba = null;
            if (d == null || pocetClanku < 1 || d.Length != 2 * pocetClanku)
            {
                chyba = $"napeti clanku: {d?.Length ?? 0} B pro {pocetClanku} clanku";
                return false;
            }
            napeti = new double[pocetClanku];
            for (int i = 0; i < pocetClanku; i++) napeti[i] = U16(d, 2 * i) * 0.001;
            return true;
        }

        private static int U16(byte[] d, int i) => (d[i] << 8) | d[i + 1];
    }
}
```

- [ ] **Step 4: Spusť testy, musí projít**

Run: `dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~JbdProtocolTests"`
Expected: PASS (18 testů).

- [ ] **Step 5: Commit (jen na pokyn autora)**

```bash
git add Src/ARBot.HAL/Devices/Bms/JbdProtocol.cs Src/ARBot.HAL.Tests/JbdProtocolTests.cs
git commit -m "BMS: protokol JBD nad bajty (dotazy, hledani ramce, rozbor 0x03/0x04)"
```

---

### Task 3: Driver `JbdBms` a rozhraní `IBms`

**Files:**
- Create: `Src/ARBot.HAL/IBms.cs`
- Create: `Src/ARBot.HAL/Devices/Bms/JbdBms.cs`
- Modify: `Src/ARBot.Common.Tests/Devices/DiagnostikaPoruchTests.cs` (výčet `Soubory`)
- Test: `Src/ARBot.HAL.Tests/JbdBmsDriverTests.cs`

**Interfaces:**
- Consumes: `JbdProtocol` (Task 2), `BmsState` (Task 1), `IUart`, `UartSensorBase<T>`, `PoruchaHlasic`.
- Produces: `interface IBms : ISensor { BmsState GetLastMeasurement(); event EventHandler<BmsState> MeasurementArived; }`;
  `public class JbdBms : UartSensorBase<BmsState>, IBms` s ctorem
  `JbdBms(IUart uart, TimeSpan? perioda = null, TimeSpan? odpovedDo = null, bool start = true, Action<string> report = null)`,
  `Name == "JbdBms"`, `const int ChybPoSobeProPoruchu = 3`.

- [ ] **Step 1: Napiš padající testy**

`Src/ARBot.HAL.Tests/JbdBmsDriverTests.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ARBot.Common.Devices;
using ARBot.HAL.Devices.Bms;

namespace ARBot.HAL.Tests
{
    /// <summary>Driver BMS JBD nad náhradním portem, který odpovídá jako BMS (doc/plan-bms-jbd.md).</summary>
    public class JbdBmsDriverTests
    {
        /// <summary>Port, kterému test řekne, co má na který dotaz vrátit (null = mlčet).</summary>
        private sealed class BmsUart : IUart
        {
            private readonly ConcurrentQueue<byte> rx = new ConcurrentQueue<byte>();
            public readonly List<byte[]> Zapsano = new List<byte[]>();
            public Func<byte[], byte[]> Odpovez = _ => null;

            public void Write(byte[] buffer)
            {
                lock (Zapsano) Zapsano.Add(buffer.ToArray());
                var o = Odpovez(buffer);
                if (o != null) foreach (var b in o) rx.Enqueue(b);
            }

            public int Read(byte[] buffer, int offset, int count)
            {
                int n = 0;
                while (n < count && rx.TryDequeue(out var b)) buffer[offset + n++] = b;
                return n;
            }

            public bool IsOpen => true;
            public int ReadTimeout { get; set; }
            public byte[] Read(int count) => Array.Empty<byte>();
            public Task<byte[]> ReadAsync(int count) => Task.FromResult(Array.Empty<byte>());
            public string ReadLine() => null;
            public string ReadAll() => null;
            public Task<string> ReadLineAsync() => Task.FromResult<string>(null);
            public void WriteLine(string txt) { }
            public void CancelRead() { }
        }

        /// <summary>Driver bez vlastního vlákna: test si měření vyzvedne sám.</summary>
        private sealed class TestBms : JbdBms
        {
            public TestBms(IUart uart, List<string> hlasky = null)
                : base(uart, perioda: TimeSpan.Zero, odpovedDo: TimeSpan.FromMilliseconds(50),
                       start: false, report: s => hlasky?.Add(s)) { }
            public BmsState Zmer() => GetMeasurement();
        }

        private static readonly byte[] Clanky4 = { 0x0C, 0xDA, 0x0C, 0xDA, 0x0C, 0xD0, 0x0C, 0xDA };

        /// <summary>Zdravá BMS: na 0x03 základní údaje, na 0x04 čtyři články.</summary>
        private static byte[] ZdravaBms(byte[] dotaz) => dotaz[2] switch
        {
            0x03 => JbdProtocolTests.Ramec(0x03, JbdProtocolTests.ZakladniData(soc: 64)),
            0x04 => JbdProtocolTests.Ramec(0x04, Clanky4),
            _ => null,
        };

        [Test]
        public void ZdravaBms_JedenCyklus_VydaMereni()
        {
            var uart = new BmsUart { Odpovez = ZdravaBms };
            var s = new TestBms(uart).Zmer();

            Assert.That(s.HasMeasurement, Is.True);
            Assert.That(s.SocPercent, Is.EqualTo(64));
            Assert.That(s.CellVoltages, Is.EqualTo(new[] { 3.290, 3.290, 3.280, 3.290 }).Within(1e-9));
        }

        [Test]
        public void ZapisujeJenDvaDotazyNaCteni()
        {
            var uart = new BmsUart { Odpovez = ZdravaBms };
            var bms = new TestBms(uart);
            bms.Zmer();
            bms.Zmer();

            var povolene = new[] { JbdProtocol.ReadRequest(0x03), JbdProtocol.ReadRequest(0x04) };
            Assert.That(uart.Zapsano, Is.Not.Empty);
            Assert.That(uart.Zapsano.All(z => povolene.Any(p => p.SequenceEqual(z))), Is.True,
                        "driver smi BMS jen cist - zadny zapis parametru ani spinacu");
        }

        [Test]
        public void OpozdenaOdpovedNaPredchoziDotaz_SeZahodi()
        {
            // BMS odpovi na 0x04 nejdriv opozdenou odpovedi na 0x03 a az pak napetim clanku.
            var uart = new BmsUart
            {
                Odpovez = d => d[2] == 0x03
                    ? JbdProtocolTests.Ramec(0x03, JbdProtocolTests.ZakladniData())
                    : JbdProtocolTests.Ramec(0x03, JbdProtocolTests.ZakladniData())
                        .Concat(JbdProtocolTests.Ramec(0x04, Clanky4)).ToArray(),
            };

            var s = new TestBms(uart).Zmer();

            Assert.That(s.HasMeasurement, Is.True);
            Assert.That(s.CellVoltages.Length, Is.EqualTo(4), "ramec 0x03 se nesmi rozebrat jako clanky");
        }

        [Test]
        public void MlcenlivaBms_ZpravaBezMereni()
        {
            var s = new TestBms(new BmsUart()).Zmer();
            Assert.That(s, Is.Not.Null);
            Assert.That(s.HasMeasurement, Is.False);
        }

        [Test]
        public void TrvaleMlceni_IsError_AJednaHlaskaOVypadku()
        {
            var hlasky = new List<string>();
            var uart = new BmsUart();
            var bms = new TestBms(uart, hlasky);

            for (int i = 0; i < JbdBms.ChybPoSobeProPoruchu + 5; i++) bms.Zmer();

            Assert.That(bms.IsError, Is.True, "zpravy bez mereni nesmi senzor schovat pred hlidanim ticha");
            Assert.That(hlasky.Count(h => h.Contains("neodpovida")), Is.EqualTo(1));

            uart.Odpovez = ZdravaBms;
            bms.Zmer();
            Assert.That(bms.IsError, Is.False);
            Assert.That(hlasky.Count(h => h.Contains("zase odpovida")), Is.EqualTo(1));
        }

        [Test]
        public void SpatnySoucet_ZpravaBezMereni()
        {
            var uart = new BmsUart
            {
                Odpovez = d =>
                {
                    var r = ZdravaBms(d);
                    r[5] ^= 0x01;
                    return r;
                },
            };
            Assert.That(new TestBms(uart).Zmer().HasMeasurement, Is.False);
        }

        [Test]
        public void Stop_DobehneVcas()
        {
            var bms = new JbdBms(new BmsUart(), perioda: TimeSpan.FromSeconds(1),
                                 odpovedDo: TimeSpan.FromMilliseconds(500), report: _ => { });
            var sw = System.Diagnostics.Stopwatch.StartNew();
            bms.Stop();
            Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
            Assert.That(bms.IsRunning, Is.False);
        }
    }
}
```

- [ ] **Step 2: Spusť testy, musí spadnout**

Run: `dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~JbdBmsDriverTests"`
Expected: build FAIL — `JbdBms` neexistuje.

- [ ] **Step 3: Napiš `IBms.cs`**

```csharp
using System;
using ARBot.Common.Devices;

namespace ARBot.HAL
{
    /// <summary>BMS baterie (doc/plan-bms-jbd.md) — jen čte stav, nic neřídí.</summary>
    public interface IBms : ISensor
    {
        /// <summary>Poslední měření; bez nového měření null.</summary>
        BmsState GetLastMeasurement();

        /// <summary>Vyvoláno po příchodu nového měření (i zprávy bez měření po chybě).</summary>
        event EventHandler<BmsState> MeasurementArived;
    }
}
```

- [ ] **Step 4: Napiš `JbdBms.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;

namespace ARBot.HAL.Devices.Bms
{
    /// <summary>
    /// BMS JBD přes RS485 (převodník USB–RS485, 9600 Bd) — doc/plan-bms-jbd.md. Jednou za
    /// <c>perioda</c> pošle dotaz 0x03 a 0x04 a vydá <see cref="BmsState"/>; po chybě komunikace
    /// zprávu BEZ měření. <b>Jen čtení</b> - jiné bajty na port nepíše.
    ///
    /// <para>Protože po chybě vydává zprávu (ne null), hlídání ticha v <see cref="SensorBase{TState}"/>
    /// výpadek nevidí - proto vlastní <see cref="IsError"/> podle počtu chyb po sobě. Výpadek a obnova
    /// jdou do <c>Trace</c> jednou (přechod), jednotlivé chyby škrceně přes <see cref="PoruchaHlasic"/>.</para>
    /// </summary>
    public class JbdBms : UartSensorBase<BmsState>, IBms
    {
        /// <summary>Kolik nepovedených cyklů po sobě je porucha.</summary>
        public const int ChybPoSobeProPoruchu = 3;

        private readonly TimeSpan perioda;
        private readonly TimeSpan odpovedDo;
        private readonly Action<string> report;
        private readonly PoruchaHlasic hlasic = new PoruchaHlasic();
        private readonly List<byte> rx = new List<byte>(128);
        private readonly byte[] tmp = new byte[128];
        private DateTime dalsiDotaz = DateTime.MinValue;
        private volatile int chybPoSobe;
        private bool hlasenVypadek;

        /// <param name="uart">Port převodníku USB–RS485 (<see cref="JbdProtocol.BaudRate"/>).</param>
        /// <param name="perioda">Perioda dotazů; výchozí 1 s.</param>
        /// <param name="odpovedDo">Jak dlouho čekat na odpověď jednoho dotazu; výchozí 500 ms.</param>
        /// <param name="start">Spustit čtecí vlákno hned (test ho nechá stát a měří sám).</param>
        /// <param name="report">Kam hlásit výpadek a obnovu; null = <c>Trace.WriteLine</c>.</param>
        public JbdBms(IUart uart, TimeSpan? perioda = null, TimeSpan? odpovedDo = null,
                      bool start = true, Action<string> report = null)
            : base(uart)
        {
            this.perioda = perioda ?? TimeSpan.FromSeconds(1);
            this.odpovedDo = odpovedDo ?? TimeSpan.FromMilliseconds(500);
            this.report = report ?? (s => Trace.WriteLine(s));
            uart.ReadTimeout = 100;   // kratke cteni - odpoved hlida vlastni termin odpovedDo
            if (start) Start();
        }

        /// <inheritdoc/>
        public override string Name => "JbdBms";

        /// <inheritdoc/>
        public override bool IsError => base.IsError || chybPoSobe >= ChybPoSobeProPoruchu;

        /// <inheritdoc/>
        protected override BmsState GetMeasurement()
        {
            if (!PockejNaDalsiDotaz()) return null;

            var t = TimeBase.Now;
            var s = new BmsState { TimeStamp = t };
            byte[] d = Dotaz(JbdProtocol.RegBasic, out string chyba);
            if (d != null && JbdProtocol.TryParseBasic(d, s, out int clanku, out chyba))
            {
                d = Dotaz(JbdProtocol.RegCells, out chyba);
                if (d != null && JbdProtocol.TryParseCells(d, clanku, out var napeti, out chyba))
                {
                    s.CellVoltages = napeti;
                    Uspech();
                    return s;
                }
            }

            if (stopRequired) return null;
            Selhani(chyba ?? "neznama chyba");
            return BmsState.NoMeasurement(t);
        }

        /// <summary>Počká na termín dalšího dotazu; false = žádost o zastavení.</summary>
        private bool PockejNaDalsiDotaz()
        {
            var now = TimeBase.Now;
            if (dalsiDotaz == DateTime.MinValue) dalsiDotaz = now;
            while (!stopRequired && TimeBase.Now < dalsiDotaz)
                Thread.Sleep(20);
            dalsiDotaz += perioda;
            if (dalsiDotaz < TimeBase.Now) dalsiDotaz = TimeBase.Now + perioda;   // nedohanet
            return !stopRequired;
        }

        /// <summary>Pošle dotaz a vrátí data odpovědi na TENTÝŽ registr, nebo null s důvodem.</summary>
        private byte[] Dotaz(byte reg, out string chyba)
        {
            chyba = null;
            rx.Clear();
            uart.Write(JbdProtocol.ReadRequest(reg));
            var konec = TimeBase.Now + odpovedDo;
            while (!stopRequired && TimeBase.Now < konec)
            {
                int n = uart.Read(tmp, 0, tmp.Length);
                if (n <= 0)
                {
                    Thread.Sleep(5);
                    continue;
                }
                for (int i = 0; i < n; i++) rx.Add(tmp[i]);

                while (true)
                {
                    var v = JbdProtocol.TryExtract(rx, out var ramec, out string ch);
                    if (v == JbdVysledek.MaloDat) break;
                    if (v == JbdVysledek.Chyba)
                    {
                        chyba = ch;
                        return null;
                    }
                    if (ramec.Reg == reg) return ramec.Data;
                    // Odpoved na jiny registr (opozdena z minuleho dotazu) - zahodit, cist dal.
                }
            }
            if (!stopRequired)
                chyba = $"BMS neodpovedela do {odpovedDo.TotalMilliseconds:0} ms (registr 0x{reg:X2})";
            return null;
        }

        private void Uspech()
        {
            if (hlasenVypadek)
            {
                report($"{Name}: BMS zase odpovida.");
                hlasenVypadek = false;
            }
            chybPoSobe = 0;
        }

        private void Selhani(string chyba)
        {
            hlasic.Hlas($"{Name}: {chyba}", $"{Name}: {chyba}");
            chybPoSobe++;
            if (chybPoSobe >= ChybPoSobeProPoruchu && !hlasenVypadek)
            {
                report($"{Name}: BMS neodpovida ({ChybPoSobeProPoruchu}x po sobe) - stav baterie neni znamy.");
                hlasenVypadek = true;
            }
        }
    }
}
```

Do `DiagnostikaPoruchTests.Soubory` přidej řádek (za `VN100IMUBinary.cs`):

```csharp
            Path.Combine("Src", "ARBot.HAL", "Devices", "Bms", "JbdBms.cs"),
```

- [ ] **Step 5: Spusť testy, musí projít**

Run: `dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~JbdBmsDriverTests"`
Expected: PASS (7 testů).
Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~DiagnostikaPoruchTests"`
Expected: PASS.

- [ ] **Step 6: Commit (jen na pokyn autora)**

```bash
git add Src/ARBot.HAL/IBms.cs Src/ARBot.HAL/Devices/Bms/JbdBms.cs Src/ARBot.HAL.Tests/JbdBmsDriverTests.cs Src/ARBot.Common.Tests/Devices/DiagnostikaPoruchTests.cs
git commit -m "BMS: driver JbdBms (jen cteni, zprava bez mereni po chybe, vypadek do Trace)"
```

---

### Task 4: Zapojení do HW a runtime (skutečná BMS)

**Files:**
- Modify: `Src/ARBot.Common/Configuration/Profile.cs:163-175` (`PortBms`)
- Modify: `Src/ARBot.Common/Configuration/ParamRegistry.cs:~70` (`UartBms`)
- Modify: `Src/ARBot.Runtime/Robot/ARBotHW.cs` (vlastnosti ř. ~87-94, `portBms` ř. 139, `Init` ř. 158-160, `SetRealHW` ř. ~579, `MotionSensorsStop` ř. 400)
- Modify: `Src/ARBot.Runtime/Robot/ARBotRuntime.cs:~2435` (`BuildSensorSources`)
- Modify: `Src/ARBot.Runtime/Web/WebStatus.cs:~1206` (`KlicMereni`), `Post` ř. ~310 není potřeba měnit
- Test: `Src/ARBot.Common.Tests/Configuration/*` (stávající strážní testy), build celého řešení

**Interfaces:**
- Consumes: `JbdBms`, `IBms`, `JbdProtocol.BaudRate`.
- Produces: `ARBotHW.Bms` (`IBms`), parametr `ParamRegistry.UartBms` (`StringParam`), `Profile.PortBms`.

- [ ] **Step 1: `Profile.PortBms`** — do všech tří větví `#if IsX64 / #elif IsARM64 / #else` přidej
  za `PortGPS` stejný řádek (výchozí prázdný všude, cesta `by-id` se doplní ve fázi 4):

```csharp
        public static string PortBms = null;
```

  a do souhrnného komentáře nad blokem doplň větu:
  `PortBms (chytrá BMS JBD přes USB–RS485) je zatím všude prázdný - cesta by-id se zjistí až na robotu (doc/plan-bms-jbd.md, fáze 4).`

- [ ] **Step 2: Parametr `UartBms`** — v `ParamRegistry.cs` za `UartGPS`:

```csharp
        public static readonly StringParam UartBms = Text("UartBms", Profile.PortBms, K_HW,
              "Seriovy port chytre BMS JBD (prevodnik USB-RS485, 9600 Bd). Prazdny = BMS se nezaklada "
              + "(vychozi na vsech platformach, dokud se na robotu nezjisti cesta /dev/serial/by-id). "
              + "Jen cteni. Viz doc/plan-bms-jbd.md.");
```

- [ ] **Step 3: `ARBotHW`** — vlastnosti za `IMU`:

```csharp
        public IBms Bms { get; set; }
```

  za `UartAHRS`:

```csharp
        protected IUart UartBms { get; set; }
```

  pole: `private string portAHRS, portMotor, portGPS, portBms;` a v `Init()` za `portGPS = …`:

```csharp
            portBms = ParamRegistry.UartBms.Value;
```

  v `SetRealHW()` za blokem GPS:

```csharp
            if (!noUart && !string.IsNullOrEmpty(portBms))
            {
                UartBms = new Uart("UartBms", portBms, JbdProtocol.BaudRate);
                Bms = new JbdBms(UartBms);
                sensors.Add(Bms);
            }
```

  v `MotionSensorsStop()` rozšiř obě pole a nulování:

```csharp
            foreach (var s in new object[] { Motor, GPS, IMU, Bms })
            ...
            Bms = null;
            ...
            foreach (var u in new object[] { UartMotor, UartGPS, UartAHRS, UartBms })
            ...
            UartBms = null;
```

  a `using ARBot.HAL.Devices.Bms;` nahoru. Uprav i hlášku `no_uart` na „UART senzory (IMU/GPS/motor/BMS)".

- [ ] **Step 4: Zdroj zpráv** — v `ARBotRuntime.BuildSensorSources` za GPS:

```csharp
            // BMS (BmsState) - jen pro stranku, zaznam a BatteryMonitor; nic se podle ni neridi.
            if (hw.Bms != null)
            {
                var bms = hw.Bms;
                var src = new SensorMessageSource<BmsState>(
                    h => bms.MeasurementArived += h, h => bms.MeasurementArived -= h);
                connections.Add(src.Connect(router));
                sources.Add(src);
            }
```

- [ ] **Step 5: Stáří měření na stránce** — v `WebStatus.KlicMereni` za `IGPS`:

```csharp
            ARBot.HAL.IBms => nameof(ARBot.Common.Devices.BmsState),
```

- [ ] **Step 6: Build a testy**

Run: `dotnet build Src/ARBot.slnx -p:Platform=x64`
Expected: build OK, 0 chyb.
Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~Configuration"`
Expected: PASS (strážní testy registru: jméno pole `UartBms` ↔ klíč `UartBms`).

- [ ] **Step 7: Commit (jen na pokyn autora)**

```bash
git add Src/ARBot.Common/Configuration/Profile.cs Src/ARBot.Common/Configuration/ParamRegistry.cs Src/ARBot.Runtime/Robot/ARBotHW.cs Src/ARBot.Runtime/Robot/ARBotRuntime.cs Src/ARBot.Runtime/Web/WebStatus.cs
git commit -m "BMS: parametr UartBms, zalozeni v ARBotHW, zdroj zprav, stari mereni na strance"
```

---

### Task 5: Simulovaná BMS (`VirtualBms`) a panel

**Files:**
- Create: `Src/ARBot.HAL/Devices/Bms/VirtualBms.cs`
- Modify: `Src/ARBot.HAL/Devices/VirtualSensorOptions.cs:~93` (za `BatteryVoltage`)
- Modify: `Src/ARBot.Runtime/Robot/ARBotHW.cs:~680` (`SetVirtualHW`)
- Modify: `Src/ARBot/ViewModels/VirtualSensorsDocument.cs` (vlastnosti ř. ~96, načtení ř. ~178, handlery ř. ~224)
- Modify: `Src/ARBot/Views/VirtualSensorsDocumentView.axaml:~150`
- Test: `Src/ARBot.HAL.Tests/VirtualBmsTest.cs`

**Interfaces:**
- Consumes: `IBms`, `BmsState`, `VirtualSensorOptions`.
- Produces: `VirtualSensorOptions.BmsPresent` (bool, výchozí true), `BmsSocPercent` (double, 80),
  `BmsCurrentA` (double, −3,0); `public sealed class VirtualBms : SensorBase<BmsState>, IBms`
  s ctorem `VirtualBms(VirtualSensorOptions options = null, int periodMs = 1000)`, `Name == "VirtualBms"`.

- [ ] **Step 1: Napiš padající test**

`Src/ARBot.HAL.Tests/VirtualBmsTest.cs`:

```csharp
using System;
using System.Threading;
using ARBot.Common.Devices;
using ARBot.HAL.Devices;
using ARBot.HAL.Devices.Bms;

namespace ARBot.HAL.Tests
{
    public class VirtualBmsTest
    {
        [Test]
        public void HlasiStavZNastaveni()
        {
            var o = new VirtualSensorOptions { BmsSocPercent = 18, BmsCurrentA = -2.5 };
            BmsState s = null;
            using var bms = new VirtualBms(o, periodMs: 20);
            bms.MeasurementArived += (_, m) => s ??= m;
            SpinWait.SpinUntil(() => s != null, TimeSpan.FromSeconds(2));

            Assert.That(s, Is.Not.Null);
            Assert.That(s.HasMeasurement, Is.True);
            Assert.That(s.SocPercent, Is.EqualTo(18));
            Assert.That(s.Current, Is.EqualTo(-2.5));
            Assert.That(s.CellVoltages.Length, Is.EqualTo(4));
        }

        [Test]
        public void OdpojenaBms_NicNeposila()
        {
            var o = new VirtualSensorOptions { BmsPresent = false };
            int pocet = 0;
            using var bms = new VirtualBms(o, periodMs: 20);
            bms.MeasurementArived += (_, _) => Interlocked.Increment(ref pocet);
            Thread.Sleep(200);
            Assert.That(pocet, Is.EqualTo(0), "odpojena BMS = monitor se vrati k napeti z motoru");
        }
    }
}
```

- [ ] **Step 2: Spusť test, musí spadnout**

Run: `dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~VirtualBmsTest"`
Expected: build FAIL — `VirtualBms`, `BmsPresent` neexistují.

- [ ] **Step 3: Nastavení simulace** — do `VirtualSensorOptions` za `BatteryVoltage`:

```csharp
        /// <summary>Je v simulaci BMS? false = jako robot bez BMS: varování podle napětí z motorů.</summary>
        public bool BmsPresent { get; set; } = true;

        /// <summary>Stav nabití hlášený virtuální BMS [%] (zkouška <c>batwarnsoc=</c> na stránce).</summary>
        public double BmsSocPercent { get; set; } = 80;

        /// <summary>Proud hlášený virtuální BMS [A], kladný = nabíjení.</summary>
        public double BmsCurrentA { get; set; } = -3.0;
```

- [ ] **Step 4: Napiš `VirtualBms.cs`**

```csharp
using System;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Devices;

namespace ARBot.HAL.Devices.Bms
{
    /// <summary>
    /// Simulovaná BMS: hlásí stav nabití a proud z <see cref="VirtualSensorOptions"/> (panel
    /// <i>Virtuální senzory</i>), aby šla stránka náhledu a varování proklikat bez hardwaru.
    /// Napětí článků je jen ilustrační (lineární v procentech), ne model LiFePO4.
    /// </summary>
    public sealed class VirtualBms : SensorBase<BmsState>, IBms
    {
        private readonly VirtualSensorOptions options;
        private readonly int periodMs;

        /// <inheritdoc/>
        public override string Name => "VirtualBms";

        public VirtualBms(VirtualSensorOptions options = null, int periodMs = 1000)
        {
            this.options = options ?? new VirtualSensorOptions();
            this.periodMs = Math.Max(1, periodMs);
            Start();
        }

        /// <inheritdoc/>
        protected override BmsState GetMeasurement()
        {
            for (int cekano = 0; cekano < periodMs && !stopRequired; cekano += 10)
                Thread.Sleep(Math.Min(10, periodMs));
            if (stopRequired || !options.BmsPresent) return null;

            double soc = Math.Clamp(options.BmsSocPercent, 0, 100);
            double clanek = 3.0 + 0.0033 * soc;
            return new BmsState
            {
                TimeStamp = TimeBase.Now,
                PackVoltage = 4 * clanek,
                Current = options.BmsCurrentA,
                NominalAh = 15,
                RemainingAh = 15 * soc / 100,
                SocPercent = (int)Math.Round(soc),
                CellVoltages = new[] { clanek, clanek, clanek, clanek },
                Temperatures = new[] { 22.0 },
                ChargeFetOn = true,
                DischargeFetOn = true,
            };
        }
    }
}
```

- [ ] **Step 5: Spusť test, musí projít**

Run: `dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~VirtualBmsTest"`
Expected: PASS (2 testy).

- [ ] **Step 6: Simulace v `ARBotHW.SetVirtualHW`** — za `IMU = new VirtualImu(…)`:

```csharp
            sensors.Add(Bms = new VirtualBms(VirtualSensors));
```

- [ ] **Step 7: Panel** — `VirtualSensorsDocument.cs` za `batteryVoltage`:

```csharp
        /// <summary>Je v simulaci BMS (jinak varování podle napětí z motorů).</summary>
        [ObservableProperty] private bool bmsPresent;

        /// <summary>Stav nabití virtuální BMS [%] - zkouška <c>batwarnsoc=</c>.</summary>
        [ObservableProperty] private decimal bmsSocPercent;

        /// <summary>Proud virtuální BMS [A], kladný = nabíjení.</summary>
        [ObservableProperty] private decimal bmsCurrentA;
```

  v načtení za `BatteryVoltage = …`:

```csharp
            BmsPresent = options.BmsPresent;
            BmsSocPercent = (decimal)options.BmsSocPercent;
            BmsCurrentA = (decimal)options.BmsCurrentA;
```

  handlery za `OnBatteryVoltageChanged`:

```csharp
        /// <summary>Platí hned při dalším vzorku BMS (tatáž instance nastavení).</summary>
        partial void OnBmsPresentChanged(bool value) => options.BmsPresent = value;

        partial void OnBmsSocPercentChanged(decimal value)
        {
            if (value < 0m || value > 100m) return;
            options.BmsSocPercent = (double)value;
        }

        partial void OnBmsCurrentAChanged(decimal value) => options.BmsCurrentA = (double)value;
```

  `VirtualSensorsDocumentView.axaml` za blok „napětí baterie":

```xml
            <!-- Virtualni BMS: zkouska varovani podle procent (batwarnsoc=) a navratu k napeti bez BMS. -->
            <StackPanel Orientation="Horizontal" Spacing="8">
                <CheckBox IsChecked="{Binding BmsPresent}" Content="BMS" Foreground="White"
                          ToolTip.Tip="Bez BMS se varuje podle napětí z motorů (batwarn=)."/>
                <TextBlock Text="nabití [%]" Foreground="White" VerticalAlignment="Center"/>
                <NumericUpDown Value="{Binding BmsSocPercent}" Increment="1" Minimum="0" Maximum="100"
                               FormatString="F0" Width="110" IsEnabled="{Binding BmsPresent}"
                               ToolTip.Tip="Pod batwarnsoc= (výchozí 20 %) svítí na stránce náhledu NABÍT."/>
                <TextBlock Text="proud [A]" Foreground="White" VerticalAlignment="Center"/>
                <NumericUpDown Value="{Binding BmsCurrentA}" Increment="0.5" Minimum="-60" Maximum="30"
                               FormatString="F1" Width="110" IsEnabled="{Binding BmsPresent}"/>
            </StackPanel>
```

- [ ] **Step 8: Build celého řešení**

Run: `dotnet build Src/ARBot.slnx -p:Platform=x64`
Expected: build OK.

- [ ] **Step 9: Commit (jen na pokyn autora)**

```bash
git add Src/ARBot.HAL/Devices/Bms/VirtualBms.cs Src/ARBot.HAL/Devices/VirtualSensorOptions.cs Src/ARBot.HAL.Tests/VirtualBmsTest.cs Src/ARBot.Runtime/Robot/ARBotHW.cs Src/ARBot/ViewModels/VirtualSensorsDocument.cs Src/ARBot/Views/VirtualSensorsDocumentView.axaml
git commit -m "BMS: simulovana BMS a jeji nastaveni v panelu Virtualni senzory"
```

---

### Task 6: Varování podle stavu nabití (`BatteryMonitor` + `batwarnsoc=`)

**Files:**
- Modify: `Src/ARBot.Common/Diagnostics/BatteryMonitor.cs`
- Modify: `Src/ARBot.Common/Configuration/ParamParsers.cs` (za `CorridorInliersPercent`)
- Modify: `Src/ARBot.Common/Configuration/ParamRegistry.cs` (za `BatWarn`)
- Modify: `Src/ARBot.Runtime/Robot/ARBotRuntime.cs:1234`
- Test: `Src/ARBot.Common.Tests/Diagnostics/BatteryMonitorTests.cs`

**Interfaces:**
- Consumes: `BmsState`, `BmsProtection`, `BmsProtectionText` (Task 1).
- Produces: `BatteryReading.Bms` (`BmsState`, null = stav z napětí motorů), nový ctor
  `BatteryReading(double volts, BatteryLevel level, BmsState bms)`; `BatteryMonitor` ctor
  s posledním parametrem `double warnSoc = 0`, vlastnost `WarnSocPercent`, konstanta
  `SocHysteresis = 5.0`, metoda `Add(BmsState b)`; parametr `ParamRegistry.BatWarnSoc`.

- [ ] **Step 1: Napiš padající testy** — do `BatteryMonitorTests` přidej:

```csharp
        // ---------------- BMS (hw-bms-jbd-driver, doc/plan-bms-jbd.md) ----------------

        private static BatteryMonitor MonitorBms(double warnSoc = 20, List<string> hlasky = null)
            => new BatteryMonitor(11.6, report: s => hlasky?.Add(s), warnSoc: warnSoc);

        private static BmsState Bms(double t, int soc, BmsProtection ochrana = BmsProtection.None)
            => new BmsState
            {
                TimeStamp = T0.AddSeconds(t), SocPercent = soc, PackVoltage = 13.2, Current = -3,
                CellVoltages = new[] { 3.3, 3.3, 3.3, 3.3 }, Protection = ochrana,
                ChargeFetOn = true, DischargeFetOn = true,
            };

        [Test]
        public void Bms_CerstvaData_VarujePodleProcent_NePodleNapeti()
        {
            var m = MonitorBms();
            Feed(m, 0, 2, 12.4);              // napeti z motoru by bylo v poradku
            m.Add(Bms(2, soc: 15));

            var r = m.Read(T0.AddSeconds(2));
            Assert.That(r.Level, Is.EqualTo(BatteryLevel.Low));
            Assert.That(r.Bms, Is.Not.Null);
            Assert.That(r.Volts, Is.EqualTo(13.2), "s BMS se ukazuje napeti baterie z BMS");
        }

        [Test]
        public void Bms_Zestarla_NavratKNapetiZMotoru()
        {
            var m = MonitorBms();
            m.Add(Bms(0, soc: 15));
            Feed(m, 0, 8, 12.4);

            var r = m.Read(T0.AddSeconds(8));
            Assert.That(r.Bms, Is.Null, "stare procento nesmi viset na strance");
            Assert.That(r.Level, Is.EqualTo(BatteryLevel.Ok));
            Assert.That(r.Volts, Is.EqualTo(12.4).Within(1e-9));
        }

        [Test]
        public void Bms_Hystereze_AJenPrechodyDoTrace()
        {
            var hlasky = new List<string>();
            var m = MonitorBms(20, hlasky);
            m.Add(Bms(0, 15));
            m.Add(Bms(1, 22));   // pod 20 + 5 -> porad nizka
            Assert.That(m.Read(T0.AddSeconds(1)).Level, Is.EqualTo(BatteryLevel.Low));
            m.Add(Bms(2, 26));
            Assert.That(m.Read(T0.AddSeconds(2)).Level, Is.EqualTo(BatteryLevel.Ok));
            m.Add(Bms(3, 27));

            Assert.That(hlasky.Count, Is.EqualTo(2), string.Join(" | ", hlasky));
            Assert.That(hlasky[0], Does.Contain("15 %"));
        }

        [Test]
        public void Bms_Ochrana_HlasiNastaveniAZruseniJednou()
        {
            var hlasky = new List<string>();
            var m = MonitorBms(20, hlasky);
            m.Add(Bms(0, 50));
            m.Add(Bms(1, 50, BmsProtection.ChargeUndertemp));
            m.Add(Bms(2, 50, BmsProtection.ChargeUndertemp));
            m.Add(Bms(3, 50));

            Assert.That(hlasky.Count, Is.EqualTo(2), string.Join(" | ", hlasky));
            Assert.That(hlasky[0], Does.Contain("mráz při nabíjení"));
            Assert.That(hlasky[1], Does.Contain("zrusena"));
        }

        [Test]
        public void Bms_ZpravaBezMereni_NicNemeni()
        {
            var m = MonitorBms();
            m.Add(BmsState.NoMeasurement(T0));
            Assert.That(m.Read(T0).Bms, Is.Null);
            Assert.That(m.Read(T0).Level, Is.EqualTo(BatteryLevel.Unknown));
        }

        [Test]
        public void Bms_PrahNula_NeVaruje()
        {
            var m = MonitorBms(warnSoc: 0);
            m.Add(Bms(0, 3));
            Assert.That(m.Read(T0).Level, Is.EqualTo(BatteryLevel.Ok));
        }
```

  (na začátek souboru `using ARBot.Common.Devices;` už je.)

- [ ] **Step 2: Spusť testy, musí spadnout**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~BatteryMonitorTests"`
Expected: build FAIL — `warnSoc`, `Add(BmsState)`, `BatteryReading.Bms` neexistují.

- [ ] **Step 3: Uprav `BatteryReading`**

```csharp
    /// <summary>Okamzity odecet: napeti [V] (NaN = neznamo), stav a pripadne cerstve mereni z BMS.</summary>
    public readonly struct BatteryReading
    {
        public BatteryReading(double volts, BatteryLevel level) : this(volts, level, null) { }

        public BatteryReading(double volts, BatteryLevel level, BmsState bms)
        {
            Volts = volts;
            Level = level;
            Bms = bms;
        }

        /// <summary>Napeti [V]: z BMS, kdyz je cerstva, jinak median z motorove jednotky; <c>NaN</c> = neznamo.</summary>
        public double Volts { get; }

        /// <summary>Stav proti prahu varovani (s BMS podle procent, bez ni podle napeti).</summary>
        public BatteryLevel Level { get; }

        /// <summary>Posledni mereni z BMS, kdyz je cerstve; jinak <c>null</c>.</summary>
        public BmsState Bms { get; }
    }
```

- [ ] **Step 4: Uprav `BatteryMonitor`** — ctor a nová pole:

```csharp
        /// <summary>O kolik procent nad prahem se varovani podle BMS zrusi.</summary>
        public const double SocHysteresis = 5.0;

        private BmsState bms;
        private DateTime bmsAt = DateTime.MinValue;
        private bool lowSoc;
        private BmsProtection lastProtection = BmsProtection.None;

        /// <param name="warnVolts">Prah varovani [V]; &lt;= 0 = nevarovat (napeti se dal ukazuje).</param>
        /// <param name="windowSec">Okno medianu [s] - a zaroven jak dlouho plati posledni mereni z BMS.</param>
        /// <param name="hysteresis">O kolik nad prahem se varovani zrusi [V].</param>
        /// <param name="report">Kam hlasit prechody; null = <c>Trace.WriteLine</c>.</param>
        /// <param name="warnSoc">Prah varovani podle BMS [%]; &lt;= 0 = nevarovat.</param>
        public BatteryMonitor(double warnVolts, double windowSec = DefaultWindowSec,
                              double hysteresis = 0.2, Action<string> report = null, double warnSoc = 0)
            : base(OverflowPolicy.DropOldest, 64)
        {
            WarnVolts = warnVolts;
            WarnSocPercent = warnSoc;
            this.windowSec = windowSec > 0 ? windowSec : DefaultWindowSec;
            Hysteresis = Math.Max(0, hysteresis);
            this.report = report ?? (s => System.Diagnostics.Trace.WriteLine(s));
        }

        /// <summary>Prah varovani podle BMS [%]; &lt;= 0 = vypnuto.</summary>
        public double WarnSocPercent { get; }
```

  nová metoda za `Add(DateTime, double)`:

```csharp
        /// <summary>
        /// Prida mereni z BMS (zprava bez mereni se zahodi). Dokud je cerstve, ma prednost pred
        /// napetim z motoru: varuje se podle procent a do Trace jdou prechody stavu nabiti a ochran.
        /// </summary>
        public void Add(BmsState b)
        {
            if (b == null || !b.HasMeasurement) return;
            List<string> hlasky = null;
            lock (gate)
            {
                bool wasLow = lowSoc;
                if (WarnSocPercent > 0)
                    lowSoc = lowSoc ? b.SocPercent < WarnSocPercent + SocHysteresis : b.SocPercent < WarnSocPercent;
                else
                    lowSoc = false;

                if (lowSoc && !wasLow)
                    (hlasky ??= new List<string>()).Add(string.Format(CultureInfo.InvariantCulture,
                        "BATERIE: stav nabiti {0} % (BMS) pod prahem batwarnsoc={1:0} % - nabit.",
                        b.SocPercent, WarnSocPercent));
                else if (!lowSoc && wasLow)
                    (hlasky ??= new List<string>()).Add(string.Format(CultureInfo.InvariantCulture,
                        "BATERIE: stav nabiti {0} % zpet nad prahem ({1:0} % + {2:0} % hystereze).",
                        b.SocPercent, WarnSocPercent, SocHysteresis));

                if (b.Protection != lastProtection)
                    (hlasky ??= new List<string>()).Add(b.Protection == BmsProtection.None
                        ? "BMS: ochrana zrusena."
                        : "BMS: ochrana - " + BmsProtectionText.Popis(b.Protection) + ".");
                lastProtection = b.Protection;

                bms = b;
                bmsAt = b.TimeStamp;
            }
            if (hlasky != null)
                foreach (var h in hlasky) report(h);
        }
```

  `Read(now)` — na začátek zámku:

```csharp
                if (bms != null && (now - bmsAt).TotalSeconds <= windowSec)
                    return new BatteryReading(bms.PackVoltage, lowSoc ? BatteryLevel.Low : BatteryLevel.Ok, bms);
```

  `Consume`:

```csharp
        protected override void Consume(Message msg)
        {
            if (msg is MotorStateBase m) Add(m);
            else if (msg is BmsState b) Add(b);
        }
```

  Do souhrnného komentáře třídy připiš odstavec: „**S BMS** (od fáze 3 `hw-bms-jbd-driver`): dokud
  je poslední `BmsState` mladší než okno, platí její napětí a varuje se podle stavu nabití
  (`batwarnsoc=`); bez ní dál podle napětí z motorů."

- [ ] **Step 5: Parametr `batwarnsoc`** — `ParamParsers`:

```csharp
        /// <summary>Prah varovani stavu nabiti z BMS [%]: 0 az 100 (0 = nevarovat).</summary>
        public static ParamParseResult BatWarnSoc(string text)
            => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
               && v >= 0 && v <= 100
               ? ParamParseResult.Valid()
               : ParamParseResult.Invalid("cekam procento stavu nabiti: 0 az 100 (vychozi 20; 0 = nevarovat)");
```

  `ParamRegistry` za `BatWarn`:

```csharp
        public static readonly DoubleParam BatWarnSoc = Num("batwarnsoc", "20", K_HW,
              "Prah varovani stavu nabiti baterie [%] podle BMS; pod nim cervena radka v hlavicce stranky "
              + "nahledu a hlaska do Trace (jen pri prechodu, navrat o 5 % vys). Plati, dokud chodi cerstva "
              + "data z BMS (UartBms=); bez nich se varuje podle napeti (batwarn=). 0 = nevarovat.",
              ParamParsers.BatWarnSoc);
```

  `ARBotRuntime.cs:1234`:

```csharp
            Battery = new ARBot.Common.Diagnostics.BatteryMonitor(ParamRegistry.BatWarn.Value,
                                                                  warnSoc: ParamRegistry.BatWarnSoc.Value);
```

- [ ] **Step 6: Spusť testy, musí projít**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Expected: PASS celý projekt (nové testy monitoru i strážní testy registru).

- [ ] **Step 7: Commit (jen na pokyn autora)**

```bash
git add Src/ARBot.Common/Diagnostics/BatteryMonitor.cs Src/ARBot.Common/Configuration/ParamParsers.cs Src/ARBot.Common/Configuration/ParamRegistry.cs Src/ARBot.Runtime/Robot/ARBotRuntime.cs Src/ARBot.Common.Tests/Diagnostics/BatteryMonitorTests.cs
git commit -m "BMS: varovani podle stavu nabiti (batwarnsoc=), ochrany do Trace, bez BMS dal napeti"
```

---

### Task 7: Stránka náhledu

**Files:**
- Modify: `Src/ARBot.Runtime/Web/WebStatus.cs` (tělo `ToJson` ř. ~600, `AppendHead` ř. ~733, `BatteryWarnVolts` ř. ~930, JS `popisky` ř. ~1359, JS hlavička ř. ~1493)
- Test: `Src/ARBot.Runtime.Tests/Web/WebPreviewServerTests.cs` (sekce baterie ř. ~905)

**Interfaces:**
- Consumes: `BatteryReading.Bms`, `BatteryMonitor.WarnSocPercent`, `BmsProtectionText.Popis`.
- Produces: JSON klíče `batterySoc`, `batteryCurrent`, `batteryCells`, `batteryTemp`, `batteryAh`
  (tabulka) a `bmsProtection` (hlavička); `batteryLow` s BMS v procentech.

- [ ] **Step 1: Napiš padající testy** — do sekce baterie v `WebPreviewServerTests`:

```csharp
        /// <summary>Monitor s čerstvým měřením z BMS (čas = TimeBase.Now).</summary>
        private static ARBot.Common.Diagnostics.BatteryMonitor BaterieBms(int soc,
            ARBot.Common.Devices.BmsProtection ochrana = ARBot.Common.Devices.BmsProtection.None)
        {
            var m = new ARBot.Common.Diagnostics.BatteryMonitor(11.6, report: _ => { }, warnSoc: 20);
            m.Add(new ARBot.Common.Devices.BmsState
            {
                TimeStamp = ARBot.Common.Common.TimeBase.Now, SocPercent = soc, PackVoltage = 13.21,
                Current = -4.3, RemainingAh = 9.6, NominalAh = 15, Cycles = 12,
                CellVoltages = new[] { 3.28, 3.30, 3.31, 3.30 }, Temperatures = new[] { 24.5 },
                Protection = ochrana, ChargeFetOn = true, DischargeFetOn = true,
            });
            return m;
        }

        [Test]
        public void Bms_ProcentaProudClankyVTabulce()
        {
            var bat = BaterieBms(64);
            string json = new WebStatus { BatterySource = () => bat }.ToJson(running: true);

            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"battery\":13.21"));
                Assert.That(json, Does.Contain("\"batterySoc\":64"));
                Assert.That(json, Does.Contain("\"batteryCurrent\":-4.3"));
                Assert.That(json, Does.Contain("\"batteryCells\":\"3.280–3.310 V (Δ 30 mV)\""));
                Assert.That(json, Does.Contain("\"batteryTemp\":24.5"));
                Assert.That(json, Does.Contain("\"batteryAh\":\"9.6 / 15.0 Ah, 12 cyklů\""));
                Assert.That(json, Does.Not.Contain("batteryLow"));
                Assert.That(json, Does.Not.Contain("bmsProtection"));
            });
        }

        [Test]
        public void Bms_PodPrahem_HlavickaVProcentech()
        {
            var bat = BaterieBms(15);
            string json = new WebStatus { BatterySource = () => bat }.ToJson(running: true);
            Assert.That(json, Does.Contain("\"batteryLow\":\"15 % (práh 20 %)\""));
        }

        [Test]
        public void Bms_Ochrana_VHlavicce()
        {
            var bat = BaterieBms(50, ARBot.Common.Devices.BmsProtection.ChargeUndertemp);
            string json = new WebStatus { BatterySource = () => bat }.ToJson(running: true);
            Assert.That(json, Does.Contain("\"bmsProtection\":\"mráz při nabíjení\""));
        }
```

- [ ] **Step 2: Spusť testy, musí spadnout**

Run: `dotnet test Src/ARBot.Runtime.Tests/ARBot.Runtime.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~WebPreviewServerTests"`
Expected: FAIL (nové klíče v JSON chybí).

- [ ] **Step 3: Tabulka** — v `ToJson` za řádkem s `"battery"`:

```csharp
                if (bat?.Bms is ARBot.Common.Devices.BmsState bmsMer)
                {
                    sb.Append(",\"batterySoc\":").Append(bmsMer.SocPercent.ToString(CultureInfo.InvariantCulture));
                    sb.Append(",\"batteryCurrent\":").Append(bmsMer.Current.ToString("0.0", CultureInfo.InvariantCulture));
                    if (bmsMer.CellVoltages.Length > 0)
                        Str(sb, "batteryCells", string.Format(CultureInfo.InvariantCulture,
                            "{0:0.000}–{1:0.000} V (Δ {2:0} mV)", bmsMer.CellMinV, bmsMer.CellMaxV,
                            (bmsMer.CellMaxV - bmsMer.CellMinV) * 1000));
                    if (double.IsFinite(bmsMer.TempMaxC))
                        sb.Append(",\"batteryTemp\":").Append(bmsMer.TempMaxC.ToString("0.0", CultureInfo.InvariantCulture));
                    Str(sb, "batteryAh", string.Format(CultureInfo.InvariantCulture,
                        "{0:0.0} / {1:0.0} Ah, {2} cyklů", bmsMer.RemainingAh, bmsMer.NominalAh, bmsMer.Cycles));
                }
```

- [ ] **Step 4: Hlavička** — v `AppendHead` nahraď blok varování baterie:

```csharp
            // VAROVANI BATERIE do hlavicky - tam obsluha s mobilem kouka, tabulka je az dole.
            // S BMS v procentech (batwarnsoc=), bez ni v napeti z motoru (batwarn=).
            var bat = Battery();
            if (bat.HasValue && bat.Value.Level == ARBot.Common.Diagnostics.BatteryLevel.Low)
            {
                string text = bat.Value.Bms is ARBot.Common.Devices.BmsState bl
                    ? string.Format(CultureInfo.InvariantCulture, "{0} % (práh {1:0} %)", bl.SocPercent, BatteryWarnSoc())
                    : string.Format(CultureInfo.InvariantCulture, "{0:0.0} V (práh {1:0.0} V)", bat.Value.Volts, BatteryWarnVolts());
                sb.Append(",\"batteryLow\":\"").Append(Escape(text)).Append('"');
            }
            // Zasah ochrany BMS (napr. mraz pri nabijeni) - cervene, s duvodem.
            if (bat?.Bms is ARBot.Common.Devices.BmsState bp && bp.Protection != ARBot.Common.Devices.BmsProtection.None)
                sb.Append(",\"bmsProtection\":\"")
                  .Append(Escape(ARBot.Common.Devices.BmsProtectionText.Popis(bp.Protection))).Append('"');
```

  za `BatteryWarnVolts()`:

```csharp
        /// <summary>Prah varovani podle BMS [%] z monitoru (pro text varovani), nebo NaN.</summary>
        private double BatteryWarnSoc()
        {
            try { return BatterySource?.Invoke()?.WarnSocPercent ?? double.NaN; }
            catch { return double.NaN; }
        }
```

- [ ] **Step 5: JavaScript** — do `popisky` za `battery:'…'` (a `battery` přejmenuj):

```js
 cpu:'CPU procesu [%]',missedTicks:'zameškané takty',battery:'baterie [V] (z BMS, jinak medián 5 s z motorů)',
 batterySoc:'baterie [%] (BMS)',batteryCurrent:'proud baterie [A] (+ nabíjení)',batteryCells:'články',
 batteryTemp:'teplota BMS [°C]',batteryAh:'kapacita',
```

  a za řádek s `h.batteryLow` v hlavičce:

```js
 if(h.bmsProtection)
  m+=(m?'<br>':'')+'<span class=""chyba"">BMS: ochrana — '+h.bmsProtection+'</span>';
```

  (JS je ve verbatim řetězci C#, uvozovky se zdvojují jako v okolních řádcích.)

- [ ] **Step 6: Spusť testy, musí projít**

Run: `dotnet test Src/ARBot.Runtime.Tests/ARBot.Runtime.Tests.csproj -p:Platform=x64`
Expected: PASS celý projekt (i původní tři testy baterie bez BMS).

- [ ] **Step 7: Commit (jen na pokyn autora)**

```bash
git add Src/ARBot.Runtime/Web/WebStatus.cs Src/ARBot.Runtime.Tests/Web/WebPreviewServerTests.cs
git commit -m "BMS: stav nabiti, proud, clanky a ochrany na strance nahledu"
```

---

### Task 8: Celkové ověření, simulace a dokumentace

**Files:**
- Modify: `doc/configuration.md` (počet parametrů v úvodu: dnes „85 parametrů" — skutečný počet zjisti níž)
- Modify: `CLAUDE.md` (odkaz u `configuration.md`: počet klíčů)
- Modify: `doc/plan-bms-jbd.md` (řádek **Stav**)
- Modify: `doc/hardware.md` (sekce „Napájení — trakční baterie": driver hotový v kódu)
- Modify: `doc/ukoly.yaml` (téma `hw-bms-jbd-driver`: fáze 1–3 `hotovo` s datem, stav tématu `v-kodu`), pak `dotnet run tools/ukoly.cs` **a hned** `dotnet run tools/menu.cs`
- Modify: `doc/devlog.md` (záznam dne)

- [ ] **Step 1: Všechny testy**

Run (každý zvlášť):
`dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
`dotnet test Src/ARBot.HAL.Tests/ARBot.HAL.Tests.csproj -p:Platform=x64`
`dotnet test Src/ARBot.Runtime.Tests/ARBot.Runtime.Tests.csproj -p:Platform=x64`
Expected: PASS všude; zapiš počty testů do DevLogu.

- [ ] **Step 2: Simulace bez UI** — headless s virtuálním HW a webem (mapa jako v ostatních
  bezobslužných bězích, např. `map=OSM/Hviezdoslavova.osm`):

Run: `dotnet run --project Src/ARBot.Headless -p:Platform=x64 -- virtualhw=true map=OSM/Hviezdoslavova.osm web=8080 batwarnsoc=90`
Expected: stránka `http://localhost:8080` ukazuje řádky `baterie [%] (BMS)` = 80, `proud baterie` −3,0,
`články`, `kapacita`; v hlavičce „baterie 80 % (práh 90 %) — NABÍT"; v seznamu senzorů `VirtualBms`
se stářím měření ≤ 1 s. V `Trace` jedna hláška „BATERIE: stav nabiti 80 % (BMS) pod prahem…".
Ukončit Ctrl+C.

- [ ] **Step 3: Návrat k napětí v UI** — spusť aplikaci `ARBot` s `virtualhw=true`, v *Tools →
  Virtuální senzory* odškrtni **BMS**; do 5 s se na stránce řádky BMS ztratí a řádek baterie ukáže
  napětí z motorů (12,8 V), panel *Sensors* ukáže `VirtualBms` jako CHYBA (mlčí).

- [ ] **Step 3b: Telemetrický pohled** — otevři záznam z Step 2 (`records/…rec`, režim View)
  v *Telemetrii*: pokud zpráva `BmsState` a její údaje ve výběru sloupců nejsou, zapiš to jako
  nové téma do registru (`hw-bms-telemetrie`) — v tomhle plánu se pohled **nemění**.

- [ ] **Step 4: Počet parametrů** — zjisti skutečný počet:

Run: `grep -c "public static readonly .*Param " Src/ARBot.Common/Configuration/ParamRegistry.cs`
a číslo v úvodu `doc/configuration.md` i v odkazu v `CLAUDE.md` nastav podle panelu *Tools →
Konfigurace* (počet řádků), pokud se liší od výstupu grepu, ber panel.

- [ ] **Step 5: Dokumentace a registr** — viz Files výše; v `plan-bms-jbd.md` stav
  „fáze 1–3 hotové v kódu (datum), **na zařízení neběželo** — BMS ještě nedorazila"; v registru
  `stav: v-kodu`, `vyreseno:` datum. Po `ukoly.cs` dořeš případná `VAROVÁNÍ`.

- [ ] **Step 6: Commit (jen na pokyn autora)**

```bash
git add doc/configuration.md CLAUDE.md doc/plan-bms-jbd.md doc/hardware.md doc/ukoly.yaml doc/ukoly.md web/pages/historie.html doc/devlog.md
git commit -m "BMS: dokumentace, registr ukolu, DevLog"
```

---

### Task 9 (fáze 4, až BMS dorazí): ověření na zařízení

Ruční runbook, ne kód — výsledky do `doc/hardware.md` a registru.

- [ ] Zapojit BMS a převodník USB–RS485 (FTDI) na volný port hubu; UART BMS jen Bluetooth modul,
  **pin VCC na UART nikam**.
- [ ] Na Orange Pi `ls -l /dev/serial/by-id/` → cesta převodníku; zapsat do `config/pi-provoz.cfg`
  jako `UartBms=<cesta>` a do `Profile.PortBms` větve `IsARM64`.
- [ ] Zachytit skutečné odpovědi: `stty -F <port> 9600 raw -echo; (printf '\xDD\xA5\x03\x00\xFF\xFD\x77'; sleep 0.3; printf '\xDD\xA5\x04\x00\xFF\xFC\x77') > <port> & timeout 1 xxd <port>`
  — bajty uložit jako testovací vektory do `JbdProtocolTests` (nový test „Skutecny ramec SP04S020").
  Pokud převodník vrací ozvěnu, je vidět jako `DD A5 …` před odpovědí.
- [ ] Porovnat se stránkou a s aplikací v mobilu: procenta, napětí článků, **znaménko proudu**
  při jízdě (má být záporné) a na nabíječce (kladné), počet čidel.
- [ ] Porovnat napětí na svorkách (multimetr), `PackVoltage` z BMS a napětí z Roboteqa — tím se
  uzavře `hw-baterie-napeti-nizke`.

Fáze 5 specifikace (`ARBot.Analyze battery`, spotřeba ze záznamu) **není součástí tohoto plánu**
— má smysl až nad záznamy se skutečnou BMS; založí se samostatně po Tasku 9.
