# Měření výkonu — implementační plán (fáze 1 a 2)

> Plán se plní **task po tasku**, kroky mají checkboxy (`- [ ]`). Každý task končí zeleným buildem
> a testy.

**Spec:** [doc/perf-monitoring.md](perf-monitoring.md) — plán z ní vychází, čti obojí.

**Cíl:** Zjistit, jestli řídicí smyčka stíhá svou periodu, a když ne, která část to brzdí —
živě v panelu i zpětně ze záznamu.

**Architektura:** Měření sbírá `Scheduler` (jako jediný zná plánovaný i skutečný čas taktu)
a `MessageTarget` (fronty stupňů). Sběrač s **vlastním časovačem** je jednou za sekundu přečte
a pošle jako `PerfMsg` do streamu — tím jde současně do UI i do záznamu.

**Technologie:** .NET 10, C#, NUnit 4 (`Assert.That`), Avalonia 12 + Dock + CommunityToolkit.Mvvm.

**Rozsah tohoto plánu:** fáze 1 (smyčka + zpráva + panel) a 2 (stupně). **Fáze 3** (CPU, teplota,
frekvence, CPU čas taktu — vše platformní přes HAL) a **fáze 4** (`ARBot.Analyze perf`) dostanou
vlastní plán: jsou to jiné povahy práce a fáze 4 má smysl navrhovat až podle toho, jaká data se
nasbírají.

## Než začneš (předání práce)

**Výchozí stav:** poslední commit `1e0c56c`, **1017 zelených testů** (4 přeskočené) — to je
baseline, proti které se porovnává. Necommitované jsou jen `doc/perf-monitoring.md`,
`doc/plan-perf-monitoring.md` a `config/profil.cfg` (zkušební profil autora, **do commitu
nepatří**).

**Přečti napřed:** [CLAUDE.md](../CLAUDE.md) (pravidla projektu),
[doc/perf-monitoring.md](perf-monitoring.md) (spec — plán z ní argumentuje) a
[Src/ARBot/Views/README.md](../Src/ARBot/Views/README.md) (konvence UI; potřebné pro Task 7).

### Pasti, na které se v tomhle repozitáři naráží

Nejsou v kódu vidět a stály už čas — každá z nich se v projektu skutečně stala:

1. ⚠️ **`Stopwatch.ElapsedTicks` nejsou tiky `TimeSpan`.** Převod **musí** být
   `1000.0 / Stopwatch.Frequency`, nikdy `new TimeSpan(ticks)`. Na Windows to vychází stejně jen
   shodou okolností (QPC 10 MHz), na Linux/ARM64 je `Frequency` 1 GHz a časy by byly **100× delší**
   — přesně tahle záměna způsobila, že `TimeBase` běžel na OrangePi 100× rychleji. Týká se to
   Tasků 1, 2 a 3; vzor je v [`Performance.cs`](../Src/ARBot.Common/Common/Performance.cs).
2. **Build hlásí `MSB3027 / MSB3021` na zamčené `ARBot.exe`, když aplikace běží.** Není to chyba
   kódu. Ověřit samotný překlad jde přes `dotnet build … -t:Compile`; pro plný build je potřeba
   aplikaci zavřít.
3. **`[TestCase(null)]` u parametru typu `string` neprojde** (`NUnit1001`). Případ s `null` dej
   jako samostatný `[Test]`.
4. **Nový namespace může zastínit zkratku v cizím souboru.** Když vznikl
   `ARBot.Common.Tests.Configuration`, přestalo se v jiném testu překládat `Configuration.Profile`.
   Tenhle plán zavádí `ARBot.Common.Diagnostics` a `ARBot.Common.Tests.Diagnostics` — kdyby build
   spadl na nejednoznačnosti, je to tohle a řeší se plnou kvalifikací na místě použití.
5. **V UI se chybné bindingy neprojeví hláškou** — `FilteredTraceLogSink` oblast `Binding`
   odfiltrovává. Proto **musí mít každá šablona a každý sloupec `x:DataType`**: pak překlep chytí
   build jako `AVLN2000`. Plán to v Task 7 dodržuje; neodstraňuj to.
6. **`DataGrid` při virtualizaci recykluje buňky.** Nikdy nedávej do jedné buňky dva prvky
   s obousměrným bindingem na tutéž vlastnost a nepřepínej je `IsVisible` — v recyklovaném
   kontejneru si navzájem přepíšou hodnotu a **data se ztrácejí**. Podrobně v
   [Views/README.md](../Src/ARBot/Views/README.md). Tabulka stupňů v Task 7 je jen pro čtení,
   takže se jí to netýká, ale kdyby ji někdo zeditovatelnil, platí to.

### Jak ověřovat, co nejde otestovat

Panel (Task 7) **nejde ověřit automatem** — projekt nemá headless Avalonia testy. Maximum, co jde
udělat bez člověka, je build se statickou kontrolou bindingů (viz past 5) a testy jádra. Kroky
„ověř za běhu" v Tasku 6 a 7 tedy **vyžadují autora**; nehlas je jako hotové, dokud je někdo
neproklikne, a v dokumentaci to napiš pravdivě.

## Globální omezení (platí pro každý krok)

- **Build i testy vždy pro konkrétní platformu:** `dotnet build <proj> -p:Platform=x64`,
  `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`. Nikdy `AnyCPU`.
- **Jazyk:** čeština. Komentáře v `Src/**` **bez diakritiky**, `doc/**` s diakritikou.
- **Commity nejsou součástí kroků.** Commituje autor na vlastní pokyn ([CLAUDE.md](../CLAUDE.md)).
- **Směr závislostí:** `ARBot.Common` nesmí vidět UI ani projekt `ARBot`.
- **Měření nesmí stát znatelný čas.** Když odběratel metrik není nastaven, cesta se nesmí
  prodloužit o víc než jeden `if` — viz Task 2.
- **Nemazat starou implementaci, dokud novou nepotvrdí testy** (CLAUDE.md).
- **DevLog:** na konci celku doplnit záznam do [devlog.md](devlog.md).

---

## Rozvržení souborů

| Soubor | Odpovědnost |
|---|---|
| `Src/ARBot.Common/Diagnostics/TickStats.cs` | akumulátor statistik taktů + snímek |
| `Src/ARBot.Common/Diagnostics/ISchedulerMetrics.cs` | rozhraní, kterým `Scheduler` hlásí takty |
| `Src/ARBot.Common/Diagnostics/StageStats.cs` | snímek statistik jednoho stupně |
| `Src/ARBot.Common/Diagnostics/PerfCollector.cs` | vlastní časovač, sestaví a pošle `PerfMsg` |
| `Src/ARBot.Common/Runtime/Scheduler.cs` | **modify** — měření taktů |
| `Src/ARBot.Common/Communication/MessageTarget.cs` | **modify** — počítadla fronty |
| `Src/ARBot.Common/Logs/PerfMsg.cs` | zpráva |
| `Src/ARBot.Common/Communication/MessageCatalog.cs` | **modify** — registrace zprávy |
| `Src/ARBot.Common/Configuration/ParamRegistry.cs` | **modify** — prahy verdiktu |
| `Src/ARBot/Robot/ARBotRuntime.cs` | **modify** — napojení sběrače |
| `Src/ARBot/ViewModels/PerformanceDocument.cs` | ViewModel panelu |
| `Src/ARBot/Views/PerformanceDocumentView.axaml` (+ `.cs`) | View panelu |
| `Src/ARBot.Common.Tests/Diagnostics/*` | testy |

---

## Task 1: Akumulátor statistik taktů

Čistá logika bez závislostí — plně testovatelná, proto první.

**Files:**
- Create: `Src/ARBot.Common/Diagnostics/TickStats.cs`
- Test: `Src/ARBot.Common.Tests/Diagnostics/TickStatsTests.cs`

**Interfaces:**
- Produces:
  `TickStats(TimeSpan period)`;
  `void AddTick(DateTime planned, double delayMs, double durationMs, int processorId)`;
  `void AddMissed(int count)`;
  `TickSnapshot TakeSnapshot()` — **vynuluje** akumulátor;
  `readonly struct TickSnapshot` s poli `TickCount`, `MissedTicks`,
  `OccupancyAvgPct`, `OccupancyMaxPct`, `DelayAvgMs`, `DelayMaxMs`,
  `WorstTickTime` (DateTime), `WorstProcessorId` (int),
  `IReadOnlyList<CoreSnapshot> Cores`;
  `readonly struct CoreSnapshot { int ProcessorId; int TickCount; double AvgMs; }`

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Diagnostics/TickStatsTests.cs`:

```csharp
using System;
using System.Linq;
using ARBot.Common.Diagnostics;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Akumulator statistik taktu ridici smycky. Viz doc/perf-monitoring.md.
    /// </summary>
    public class TickStatsTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private static TickStats Stats() => new TickStats(TimeSpan.FromMilliseconds(100));

        [Test]
        public void Obsazenost_JePomerDobyKPeriode()
        {
            var s = Stats();
            s.AddTick(T0, delayMs: 0, durationMs: 20, processorId: 0);
            s.AddTick(T0, delayMs: 0, durationMs: 40, processorId: 0);

            var snap = s.TakeSnapshot();
            Assert.That(snap.TickCount, Is.EqualTo(2));
            Assert.That(snap.OccupancyAvgPct, Is.EqualTo(30.0).Within(1e-9));
            Assert.That(snap.OccupancyMaxPct, Is.EqualTo(40.0).Within(1e-9));
        }

        [Test]
        public void NejhorsiTakt_NeseCasIJadro()
        {
            var s = Stats();
            s.AddTick(T0, delayMs: 1, durationMs: 20, processorId: 3);
            s.AddTick(T0.AddSeconds(1), delayMs: 2, durationMs: 90, processorId: 5);
            s.AddTick(T0.AddSeconds(2), delayMs: 3, durationMs: 30, processorId: 3);

            var snap = s.TakeSnapshot();
            Assert.That(snap.WorstTickTime, Is.EqualTo(T0.AddSeconds(1)));
            Assert.That(snap.WorstProcessorId, Is.EqualTo(5));
        }

        [Test]
        public void Zpozdeni_PrumerIMaximum()
        {
            var s = Stats();
            s.AddTick(T0, delayMs: 2, durationMs: 10, processorId: 0);
            s.AddTick(T0, delayMs: 8, durationMs: 10, processorId: 0);

            var snap = s.TakeSnapshot();
            Assert.That(snap.DelayAvgMs, Is.EqualTo(5.0).Within(1e-9));
            Assert.That(snap.DelayMaxMs, Is.EqualTo(8.0).Within(1e-9));
        }

        [Test]
        public void RozpadPoJadrech_SeparujeJadraAPocitaPrumer()
        {
            // Kvuli big.LITTLE na RK3588: ze samotneho prumeru nejde poznat, ze cast taktu
            // bezela na uspornem jadru. Viz doc/perf-monitoring.md, „Nestejna jadra".
            var s = Stats();
            s.AddTick(T0, delayMs: 0, durationMs: 20, processorId: 4);
            s.AddTick(T0, delayMs: 0, durationMs: 30, processorId: 4);
            s.AddTick(T0, delayMs: 0, durationMs: 80, processorId: 1);

            var cores = s.TakeSnapshot().Cores.OrderBy(c => c.ProcessorId).ToList();
            Assert.That(cores, Has.Count.EqualTo(2));
            Assert.That(cores[0].ProcessorId, Is.EqualTo(1));
            Assert.That(cores[0].TickCount, Is.EqualTo(1));
            Assert.That(cores[0].AvgMs, Is.EqualTo(80.0).Within(1e-9));
            Assert.That(cores[1].ProcessorId, Is.EqualTo(4));
            Assert.That(cores[1].TickCount, Is.EqualTo(2));
            Assert.That(cores[1].AvgMs, Is.EqualTo(25.0).Within(1e-9));
        }

        [Test]
        public void ZameskaneTakty_SeScitaji()
        {
            var s = Stats();
            s.AddMissed(2);
            s.AddMissed(1);
            Assert.That(s.TakeSnapshot().MissedTicks, Is.EqualTo(3));
        }

        [Test]
        public void TakeSnapshot_Vynuluje()
        {
            // Sberac cte jednou za sekundu a kazdy snimek ma pokryvat POUZE svuj interval -
            // jinak by se prumer pocital pres cely beh a spicka by se v nem utopila.
            var s = Stats();
            s.AddTick(T0, delayMs: 0, durationMs: 50, processorId: 0);
            s.AddMissed(1);
            s.TakeSnapshot();

            var druhy = s.TakeSnapshot();
            Assert.That(druhy.TickCount, Is.Zero);
            Assert.That(druhy.MissedTicks, Is.Zero);
            Assert.That(druhy.Cores, Is.Empty);
        }

        [Test]
        public void PrazdnySnimek_NeniDeleniNulou()
        {
            var snap = Stats().TakeSnapshot();
            Assert.That(snap.TickCount, Is.Zero);
            Assert.That(snap.OccupancyAvgPct, Is.Zero);
            Assert.That(snap.DelayAvgMs, Is.Zero);
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter TickStatsTests`
Čekej: chyba překladu — `TickStats` neexistuje.

- [x] **Krok 3: Napiš implementaci**

`Src/ARBot.Common/Diagnostics/TickStats.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ARBot.Common.Diagnostics
{
    /// <summary>Statistika taktu za jeden interval sberu.</summary>
    public readonly struct TickSnapshot
    {
        public int TickCount { get; init; }
        public int MissedTicks { get; init; }

        /// <summary>Prumerna obsazenost periody [%] - doba taktu deleno period.</summary>
        public double OccupancyAvgPct { get; init; }
        /// <summary>Nejvetsi obsazenost periody [%] v intervalu.</summary>
        public double OccupancyMaxPct { get; init; }

        public double DelayAvgMs { get; init; }
        public double DelayMaxMs { get; init; }

        /// <summary>Cas taktu, ktery trval nejdele - kotva pro dohledani v ostatnich datech.</summary>
        public DateTime WorstTickTime { get; init; }
        /// <summary>Jadro, na kterem ten takt bezel.</summary>
        public int WorstProcessorId { get; init; }

        /// <summary>Rozpad po jadrech; prazdne, kdyz v intervalu nebyl zadny takt.</summary>
        public IReadOnlyList<CoreSnapshot> Cores { get; init; }
    }

    /// <summary>Kolik taktu a jak dlouhych probehlo na jednom jadru.</summary>
    public readonly struct CoreSnapshot
    {
        public int ProcessorId { get; init; }
        public int TickCount { get; init; }
        public double AvgMs { get; init; }
    }

    /// <summary>
    /// Akumuluje statistiku taktu ridici smycky mezi dvema odecty.
    ///
    /// <para><b>Rozpad po jadrech</b> je tu kvuli tomu, ze cilove zarizeni (RK3588) ma ctyri
    /// vykonna a ctyri usporna jadra: tataz prace tam trva ruzne dlouho podle toho, kde bezi,
    /// a vlakno se mezi nimi stehuje volne. Ze samotneho prumeru by to neslo poznat. Ktera jadra
    /// jsou vykonna se ZAMERNE nikam nezapisuje - vyjde to z dat. Viz doc/perf-monitoring.md.</para>
    ///
    /// <para>Zapisuje vlakno scheduleru, cte sberac - proto zamek. Pri 10 Hz je jeho cena
    /// zanedbatelna.</para>
    /// </summary>
    public sealed class TickStats
    {
        private readonly object sync = new object();
        private readonly double periodMs;
        private readonly Dictionary<int, (int Count, double SumMs)> cores
            = new Dictionary<int, (int, double)>();

        private int tickCount;
        private int missed;
        private double sumDurationMs;
        private double maxDurationMs;
        private double sumDelayMs;
        private double maxDelayMs;
        private DateTime worstTime;
        private int worstCore;

        public TickStats(TimeSpan period)
        {
            periodMs = period.TotalMilliseconds;
            if (periodMs <= 0) throw new ArgumentOutOfRangeException(nameof(period));
        }

        /// <summary>Zaznamena jeden probehly takt.</summary>
        public void AddTick(DateTime planned, double delayMs, double durationMs, int processorId)
        {
            lock (sync)
            {
                tickCount++;
                sumDurationMs += durationMs;
                sumDelayMs += delayMs;
                if (delayMs > maxDelayMs) maxDelayMs = delayMs;
                if (tickCount == 1 || durationMs > maxDurationMs)
                {
                    maxDurationMs = durationMs;
                    worstTime = planned;
                    worstCore = processorId;
                }

                cores.TryGetValue(processorId, out var c);
                cores[processorId] = (c.Count + 1, c.SumMs + durationMs);
            }
        }

        /// <summary>Zaznamena takty, ktere se nestihly vydat vcas.</summary>
        public void AddMissed(int count)
        {
            if (count <= 0) return;
            lock (sync) { missed += count; }
        }

        /// <summary>
        /// Vrati statistiku za dosud nasbirany interval a VYNULUJE ji. Kazdy snimek tak pokryva
        /// jen svuj interval - jinak by se prumer pocital pres cely beh a spicka by se v nem
        /// utopila.
        /// </summary>
        public TickSnapshot TakeSnapshot()
        {
            lock (sync)
            {
                var list = new List<CoreSnapshot>(cores.Count);
                foreach (var kv in cores)
                    list.Add(new CoreSnapshot
                    {
                        ProcessorId = kv.Key,
                        TickCount = kv.Value.Count,
                        AvgMs = kv.Value.Count == 0 ? 0 : kv.Value.SumMs / kv.Value.Count,
                    });

                var snap = new TickSnapshot
                {
                    TickCount = tickCount,
                    MissedTicks = missed,
                    OccupancyAvgPct = tickCount == 0 ? 0 : 100.0 * (sumDurationMs / tickCount) / periodMs,
                    OccupancyMaxPct = tickCount == 0 ? 0 : 100.0 * maxDurationMs / periodMs,
                    DelayAvgMs = tickCount == 0 ? 0 : sumDelayMs / tickCount,
                    DelayMaxMs = maxDelayMs,
                    WorstTickTime = worstTime,
                    WorstProcessorId = worstCore,
                    Cores = list,
                };

                tickCount = 0; missed = 0;
                sumDurationMs = 0; maxDurationMs = 0;
                sumDelayMs = 0; maxDelayMs = 0;
                worstTime = default; worstCore = 0;
                cores.Clear();
                return snap;
            }
        }
    }
}
```

- [x] **Krok 4: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter TickStatsTests`
Čekej: PASS (7 testů).

---

## Task 2: Měření ve Scheduleru

**Files:**
- Create: `Src/ARBot.Common/Diagnostics/ISchedulerMetrics.cs`
- Modify: `Src/ARBot.Common/Runtime/Scheduler.cs`
- Test: `Src/ARBot.Common.Tests/Diagnostics/SchedulerMetricsTests.cs`

**Interfaces:**
- Consumes: nic z Tasku 1 (rozhraní je nezávislé)
- Produces:
  `interface ISchedulerMetrics { void OnTicksDue(DateTime firstPlanned, DateTime now, int count); void OnTickCompleted(DateTime planned, double durationMs, int processorId); }`;
  `Scheduler.Metrics { get; set; }` typu `ISchedulerMetrics` (null = neměří se)

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Diagnostics/SchedulerMetricsTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using ARBot.Common.Diagnostics;
using ARBot.Common.Runtime;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Scheduler hlasi takty odberateli metrik. Je to jedine misto, ktere zna planovany
    /// i skutecny cas taktu. Viz doc/perf-monitoring.md.
    /// </summary>
    public class SchedulerMetricsTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private sealed class Zaznamnik : ISchedulerMetrics
        {
            public readonly List<(DateTime first, DateTime now, int count)> Due = new();
            public readonly List<(DateTime planned, double ms, int cpu)> Completed = new();

            public void OnTicksDue(DateTime firstPlanned, DateTime now, int count)
                => Due.Add((firstPlanned, now, count));

            public void OnTickCompleted(DateTime planned, double durationMs, int processorId)
                => Completed.Add((planned, durationMs, processorId));
        }

        [Test]
        public void VcasnyTakt_HlasiJedenTakt()
        {
            var s = new Scheduler();
            var z = new Zaznamnik();
            s.Metrics = z;
            s.Register(TimeSpan.FromMilliseconds(100), _ => { });

            s.PumpDue(T0);                       // t0 = kotva, prvni takt
            s.PumpDue(T0.AddMilliseconds(100));  // druhy takt presne na mrizce

            Assert.That(z.Due, Has.Count.EqualTo(2));
            Assert.That(z.Due[1].count, Is.EqualTo(1), "vcas = jeden takt, zadny zameskany");
            Assert.That(z.Completed, Has.Count.EqualTo(2));
        }

        [Test]
        public void OpozdenyPump_HlasiVICE_TAKTU_NAJEDNOU()
        {
            // Tohle je jadro veci: pri zpozdeni o 300 ms vyda Scheduler tri takty za sebou
            // (viz Scheduler.cs, `while (now >= r.NextTick)`). Bez teto metriky se to nepozna.
            var s = new Scheduler();
            var z = new Zaznamnik();
            s.Metrics = z;
            s.Register(TimeSpan.FromMilliseconds(100), _ => { });

            s.PumpDue(T0);
            s.PumpDue(T0.AddMilliseconds(350));

            Assert.That(z.Due[1].count, Is.EqualTo(3), "100, 200 a 300 ms");
            Assert.That(z.Due[1].first, Is.EqualTo(T0.AddMilliseconds(100)));
            Assert.That(z.Due[1].now, Is.EqualTo(T0.AddMilliseconds(350)));
        }

        [Test]
        public void DobaTaktu_SeMeri()
        {
            var s = new Scheduler();
            var z = new Zaznamnik();
            s.Metrics = z;
            s.Register(TimeSpan.FromMilliseconds(100), _ => System.Threading.Thread.Sleep(20));

            s.PumpDue(T0);

            Assert.That(z.Completed, Has.Count.EqualTo(1));
            Assert.That(z.Completed[0].ms, Is.GreaterThan(10),
                        "Sleep(20) se musi projevit; volny prah kvuli rozliseni casovace");
            Assert.That(z.Completed[0].planned, Is.EqualTo(T0));
        }

        [Test]
        public void BezOdberatele_SchedulerFunguje()
        {
            var s = new Scheduler();
            int volani = 0;
            s.Register(TimeSpan.FromMilliseconds(100), _ => volani++);

            s.PumpDue(T0);
            s.PumpDue(T0.AddMilliseconds(100));

            Assert.That(volani, Is.EqualTo(2));
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter SchedulerMetricsTests`
Čekej: chyba překladu — `ISchedulerMetrics` a `Scheduler.Metrics` neexistují.

- [x] **Krok 3: Napiš rozhraní**

`Src/ARBot.Common/Diagnostics/ISchedulerMetrics.cs`:

```csharp
using System;

namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// Odberatel metrik periodickych taktu. Implementuje ho sberac, hlasi do nej
    /// <c>Scheduler</c>.
    ///
    /// <para><b>Proc zrovna Scheduler.</b> Jako jediny zna PLANOVANY cas taktu i SKUTECNY cas,
    /// kdy ho nekdo vyzvedl, takze zpozdeni spocte zadarmo; a protoze callback sam vola, zmeri
    /// na temze miste i dobu prace. Casovac, ktery ho pumpuje, o svem zpozdeni nevi nic.
    /// Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public interface ISchedulerMetrics
    {
        /// <summary>
        /// Ohlasi, kolik taktu jedne registrace se vydava najednou.
        /// <paramref name="count"/> &gt; 1 znamena, ze <paramref name="count"/>-1 taktu se
        /// nestihlo vcas a dohanime je.
        /// </summary>
        void OnTicksDue(DateTime firstPlanned, DateTime now, int count);

        /// <summary>Ohlasi dokonceny takt: jak dlouho trval a na kterem jadru bezel.</summary>
        void OnTickCompleted(DateTime planned, double durationMs, int processorId);
    }
}
```

- [x] **Krok 4: Doplň měření do Scheduleru**

V `Src/ARBot.Common/Runtime/Scheduler.cs` přidej `using System.Diagnostics;`
a `using System.Threading;`, dále vlastnost a měření. **Nemaž stávající komentáře.**

Vlastnost (za `private readonly List<Registration> regs`):

```csharp
        /// <summary>
        /// Volitelny odberatel metrik taktu; <c>null</c> = nemeri se. Kdyz neni nastaven, stoji
        /// mereni jeden test na null za takt - viz doc/perf-monitoring.md, „Rizika".
        /// </summary>
        public ARBot.Common.Diagnostics.ISchedulerMetrics Metrics { get; set; }

        /// <summary>Prevod tiku Stopwatch na ms. NESMI to byt new TimeSpan(ticks) - viz Performance.</summary>
        private static readonly double TickToMs = 1000.0 / Stopwatch.Frequency;
```

Ve `PumpDue` uvnitř `foreach (var r in regs)` nahraď `while` cyklus tímto (počítá takty
a hlásí je):

```csharp
                    int vydano = 0;
                    DateTime prvni = r.NextTick;
                    while (now >= r.NextTick)
                    {
                        due.Add((r.OnTick, r.NextTick));
                        r.NextTick = r.NextTick + r.Interval;
                        vydano++;
                    }
                    if (vydano > 0)
                        davky.Add((prvni, vydano));
```

Nad `lock (sync)` deklaruj `var davky = new List<(DateTime first, int count)>();`
a hned za blok `lock` (před voláním callbacků) přidej:

```csharp
            // Hlaseni az MIMO zamek - odberatel metrik nesmi drzet zamek scheduleru.
            var m = Metrics;
            if (m != null)
                foreach (var d in davky)
                    m.OnTicksDue(d.first, now, d.count);
```

A nakonec nahraď smyčku volající callbacky:

```csharp
            foreach (var d in due)
            {
                if (m == null) { d.cb(d.t); continue; }

                int cpu = Thread.GetCurrentProcessorId();
                long t0 = Stopwatch.GetTimestamp();
                try { d.cb(d.t); }
                finally
                {
                    m.OnTickCompleted(d.t, (Stopwatch.GetTimestamp() - t0) * TickToMs, cpu);
                }
            }
```

- [x] **Krok 5: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter SchedulerMetricsTests`
Čekej: PASS (4 testy).

- [x] **Krok 6: Ověř, že se nerozbily stávající testy scheduleru a smyčky**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Čekej: vše zelené, **1028 úspěšných** (baseline 1017 + 7 z Tasku 1 + 4 z Tasku 2).
`Scheduler` je základ řídicí smyčky, takže regrese by se projevila v testech `ControlLoop`
a `LocalNavigator`.

---

## Task 3: Počítadla stupňů (fáze 2)

**Files:**
- Create: `Src/ARBot.Common/Diagnostics/StageStats.cs`
- Modify: `Src/ARBot.Common/Communication/MessageTarget.cs`
- Test: `Src/ARBot.Common.Tests/Diagnostics/StageStatsTests.cs`

**Interfaces:**
- Produces:
  `readonly struct StageSnapshot { string Name; int QueueLength; long Processed; long Dropped; double AvgMs; double MaxMs; }`;
  na `MessageTarget`: `string StageName { get; }`, `StageSnapshot TakeStageSnapshot()`

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Diagnostics/StageStatsTests.cs`:

```csharp
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Pocitadla stupne: kolik zprav proslo, kolik se ZAHODILO a jak dlouho trvalo zpracovani.
    /// Zahozene se dosud nepocitaly vubec - stupen mohl tise ztracet data.
    /// Viz doc/perf-monitoring.md.
    /// </summary>
    public class StageStatsTests
    {
        private sealed class Pomaly : MessageTarget
        {
            private readonly int spanimMs;
            public Pomaly(int spanimMs, OverflowPolicy policy, int capacity)
                : base(policy, capacity) { this.spanimMs = spanimMs; }

            protected override void Consume(Message msg) => Thread.Sleep(spanimMs);
        }

        [Test]
        public void ZpracovaneZpravy_SePocitaji()
        {
            using var t = new Pomaly(0, OverflowPolicy.Block, 0);
            t.Start();
            t.Post(new Info("a"));
            t.Post(new Info("b"));
            t.Stop();

            var snap = t.TakeStageSnapshot();
            Assert.That(snap.Processed, Is.EqualTo(2));
            Assert.That(snap.Dropped, Is.Zero);
        }

        [Test]
        public void ZahozeneZpravy_SePocitaji()
        {
            // DropNewest s kapacitou 1: konzument spi, takze dalsi zpravy nemaji kam.
            using var t = new Pomaly(200, OverflowPolicy.DropNewest, capacity: 1);
            t.Start();
            for (int i = 0; i < 20; i++) t.Post(new Info("x"));

            Assert.That(t.TakeStageSnapshot().Dropped, Is.GreaterThan(0),
                        "pri plne fronte a DropNewest se musi neco zahodit");
            t.Stop();
        }

        [Test]
        public void DobaZpracovani_SeMeri()
        {
            using var t = new Pomaly(20, OverflowPolicy.Block, 0);
            t.Start();
            t.Post(new Info("a"));
            t.Stop();

            var snap = t.TakeStageSnapshot();
            Assert.That(snap.MaxMs, Is.GreaterThan(10));
        }

        [Test]
        public void TakeStageSnapshot_NulujeJenPrirustkoveUdaje()
        {
            // Fronta je STAV (musi zustat), zpracovane a zahozene jsou PRIRUSTKY za interval.
            using var t = new Pomaly(0, OverflowPolicy.Block, 0);
            t.Start();
            t.Post(new Info("a"));
            t.Stop();
            t.TakeStageSnapshot();

            var druhy = t.TakeStageSnapshot();
            Assert.That(druhy.Processed, Is.Zero);
            Assert.That(druhy.MaxMs, Is.Zero);
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter StageStatsTests`
Čekej: chyba překladu — `StageSnapshot` a `TakeStageSnapshot` neexistují.

- [x] **Krok 3: Napiš `StageStats.cs`**

```csharp
namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// Stav a vykon jednoho stupne pipeline za interval sberu.
    ///
    /// <para><b>Zahozene zpravy jsou to hlavni.</b> Stupne bezi na vlastnich vlaknech s frontou
    /// a politikou preteceni (DropOldest/DropNewest) - dosud se ale zahozeni nikde nepocitalo,
    /// takze stupen mohl tise ztracet data a nikdo to nepoznal. Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public readonly struct StageSnapshot
    {
        public string Name { get; init; }
        /// <summary>Aktualni delka fronty (STAV, ne prirustek).</summary>
        public int QueueLength { get; init; }
        /// <summary>Zpracovanych zprav za interval.</summary>
        public long Processed { get; init; }
        /// <summary>ZAHOZENYCH zprav za interval.</summary>
        public long Dropped { get; init; }
        /// <summary>Prumerna doba zpracovani jedne zpravy [ms] za interval.</summary>
        public double AvgMs { get; init; }
        /// <summary>Nejdelsi zpracovani jedne zpravy [ms] za interval.</summary>
        public double MaxMs { get; init; }
    }
}
```

- [x] **Krok 4: Doplň počítadla do `MessageTarget`**

V `Src/ARBot.Common/Communication/MessageTarget.cs` přidej `using System.Diagnostics;`
a `using System.Threading;`, pak pole a metody:

```csharp
        // --- Pocitadla vykonu (diagnostika) -------------------------------------------------
        // Interlocked, ne zamek: zapisuje vlakno konzumenta i vlakna producentu a mereni nesmi
        // stat znatelny cas. Viz doc/perf-monitoring.md.
        private long processed;
        private long dropped;
        private long durationTicks;
        private long maxDurationTicks;
        private int queueLength;

        private static readonly double StageTickToMs = 1000.0 / Stopwatch.Frequency;

        /// <summary>Jmeno stupne pro diagnostiku; vychozi je nazev typu.</summary>
        public virtual string StageName => GetType().Name;

        /// <summary>
        /// Vrati statistiku od posledniho odectu a prirustkove udaje VYNULUJE. Delka fronty je
        /// stav, ta se nenuluje.
        /// </summary>
        public StageSnapshot TakeStageSnapshot() => new StageSnapshot
        {
            Name = StageName,
            QueueLength = Volatile.Read(ref queueLength),
            Processed = Interlocked.Exchange(ref processed, 0),
            Dropped = Interlocked.Exchange(ref dropped, 0),
            AvgMs = ZmerPrumer(),
            MaxMs = Interlocked.Exchange(ref maxDurationTicks, 0) * StageTickToMs,
        };

        private double ZmerPrumer()
        {
            long ticks = Interlocked.Exchange(ref durationTicks, 0);
            long n = Volatile.Read(ref processedForAvg);
            Interlocked.Exchange(ref processedForAvg, 0);
            return n == 0 ? 0 : ticks * StageTickToMs / n;
        }

        private long processedForAvg;
```

V `Post` (metoda, kde je `if (writer.TryWrite(msg)) return;`) uprav evidenci fronty a zahození —
nahraď tělo rychlé cesty:

```csharp
            if (writer.TryWrite(msg))
            {
                Interlocked.Increment(ref queueLength);
                return;                            // rychla cesta (unbounded / je misto / drop politika)
            }
            Interlocked.Increment(ref dropped);
```

> ⚠️ U politik `DropOldest`/`DropNewest` vrací `TryWrite` **true i tehdy, když se něco zahodilo** —
> kanál zahodí jinou zprávu, ne tuhle. Přesný počet zahozených proto z `TryWrite` nezjistíš;
> `queueLength` se koriguje v `ConsumeLoop` (níž) a rozdíl mezi zapsanými a zpracovanými
> zprávami je právě to zahození. Proto se `dropped` dopočítá i tam.

V `ConsumeLoop` obal `Consume(msg)`:

```csharp
                while (reader.TryRead(out var msg))
                {
                    int zbyva = Interlocked.Decrement(ref queueLength);
                    if (zbyva < 0) Interlocked.Exchange(ref queueLength, 0);

                    long t0 = Stopwatch.GetTimestamp();
                    try { Consume(msg); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.ToString()); }
                    finally
                    {
                        long dt = Stopwatch.GetTimestamp() - t0;
                        Interlocked.Add(ref durationTicks, dt);
                        Interlocked.Increment(ref processed);
                        Interlocked.Increment(ref processedForAvg);

                        long max = Volatile.Read(ref maxDurationTicks);
                        while (dt > max)
                        {
                            long puvodni = Interlocked.CompareExchange(ref maxDurationTicks, dt, max);
                            if (puvodni == max) break;
                            max = puvodni;
                        }
                    }
                }
```

- [x] **Krok 5: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter StageStatsTests`
Čekej: PASS (4 testy). Pokud `ZahozeneZpravy_SePocitaji` padne, znamená to, že `TryWrite`
u zvolené politiky nevrací `false` — pak se počet zahozených musí odvodit z rozdílu zapsaných
a zpracovaných zpráv; uprav `Post` tak, aby si vedl počítadlo zapsaných, a `dropped` počítej
jako `zapsane - processed - queueLength`.

- [x] **Krok 6: Spusť celou sadu**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Čekej: vše zelené — `MessageTarget` je základ všech stupňů, takže regrese by se projevila široce.

---

## Task 4: Zpráva `PerfMsg`

**Files:**
- Create: `Src/ARBot.Common/Logs/PerfMsg.cs`
- Modify: `Src/ARBot.Common/Communication/MessageCatalog.cs`
- Test: `Src/ARBot.Common.Tests/Diagnostics/PerfMsgSerializationTests.cs`

**Interfaces:**
- Consumes: `TickSnapshot`, `CoreSnapshot` (Task 1), `StageSnapshot` (Task 3)
- Produces: `PerfMsg` (verze 1) s poli podle specu; `PerfVerdict { Ok, Warning, Error }`

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Diagnostics/PerfMsgSerializationTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Round-trip <see cref="PerfMsg"/> pres plnou serializaci. Zprava jde do zaznamu, takze
    /// rozbor po jizde stoji na tom, ze prezije zapis na disk. Viz doc/perf-monitoring.md.
    /// </summary>
    public class PerfMsgSerializationTests
    {
        private static PerfMsg RoundTrip(PerfMsg msg)
        {
            var enc = Encoding.UTF8;
            var ms = new MemoryStream();
            var w = new MessageWriter(ms, enc);
            w.Write(msg);
            w.Flush();

            var map = MessageCatalog.CommonDefaults().ToPrototypeMap();
            var reader = new MessageReader(new MemoryStream(ms.ToArray()), enc, map);
            return reader.Read() as PerfMsg;
        }

        [Test]
        public void RoundTrip_ZachovaVsechnaPole()
        {
            var od = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
            var src = new PerfMsg
            {
                From = od,
                To = od.AddSeconds(1),
                TickCount = 10,
                MissedTicks = 2,
                OccupancyAvgPct = 31.5,
                OccupancyMaxPct = 92.0,
                DelayAvgMs = 1.5,
                DelayMaxMs = 12.0,
                WorstTickTime = od.AddMilliseconds(400),
                WorstProcessorId = 5,
                ProcessCpuPct = 42.0,
                MachineCpuPct = -1,
                Verdict = PerfVerdict.Warning,
                Cores = new List<PerfMsg.CoreEntry>
                {
                    new PerfMsg.CoreEntry { ProcessorId = 1, TickCount = 4, AvgMs = 80 },
                    new PerfMsg.CoreEntry { ProcessorId = 5, TickCount = 6, AvgMs = 25 },
                },
                Stages = new List<PerfMsg.StageEntry>
                {
                    new PerfMsg.StageEntry { Name = "FusionProcessor", QueueLength = 3,
                                             Processed = 120, Dropped = 4, AvgMs = 1.2, MaxMs = 9.9 },
                },
            };

            var back = RoundTrip(src);

            Assert.That(back, Is.Not.Null);
            Assert.That(back.From, Is.EqualTo(src.From));
            Assert.That(back.To, Is.EqualTo(src.To));
            Assert.That(back.TickCount, Is.EqualTo(10));
            Assert.That(back.MissedTicks, Is.EqualTo(2));
            Assert.That(back.OccupancyMaxPct, Is.EqualTo(92.0).Within(1e-9));
            Assert.That(back.DelayMaxMs, Is.EqualTo(12.0).Within(1e-9));
            Assert.That(back.WorstTickTime, Is.EqualTo(src.WorstTickTime));
            Assert.That(back.WorstProcessorId, Is.EqualTo(5));
            Assert.That(back.ProcessCpuPct, Is.EqualTo(42.0).Within(1e-9));
            Assert.That(back.MachineCpuPct, Is.EqualTo(-1).Within(1e-9), "-1 = neznamo");
            Assert.That(back.Verdict, Is.EqualTo(PerfVerdict.Warning));

            Assert.That(back.Cores, Has.Count.EqualTo(2));
            Assert.That(back.Cores[1].ProcessorId, Is.EqualTo(5));
            Assert.That(back.Cores[1].AvgMs, Is.EqualTo(25).Within(1e-9));

            Assert.That(back.Stages, Has.Count.EqualTo(1));
            Assert.That(back.Stages[0].Name, Is.EqualTo("FusionProcessor"));
            Assert.That(back.Stages[0].Dropped, Is.EqualTo(4));
        }

        [Test]
        public void RoundTrip_PrazdneSeznamy()
        {
            var back = RoundTrip(new PerfMsg { From = DateTime.UtcNow, To = DateTime.UtcNow });
            Assert.That(back.Cores, Is.Empty);
            Assert.That(back.Stages, Is.Empty);
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter PerfMsgSerializationTests`
Čekej: chyba překladu — `PerfMsg` neexistuje.

- [x] **Krok 3: Napiš zprávu**

`Src/ARBot.Common/Logs/PerfMsg.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>Verdikt o tom, jestli rizeni stiha.</summary>
    public enum PerfVerdict { Ok = 0, Warning = 1, Error = 2 }

    /// <summary>
    /// Vykon rizeni za jeden interval sberu (~1 s): stiha ridici smycka svou periodu, ktera cast
    /// ji brzdi a jak je na tom stroj.
    ///
    /// <para><b>Proc jedna zprava a ne CSV.</b> Ve streamu jde soucasne do UI (zivy ukazatel) i do
    /// zaznamu (rozbor po jizde), takze obe pouziti maji tataz data a nic se nemusi parovat.</para>
    ///
    /// <para><b>Proc nese i MAXIMUM, nejen prumer.</b> Nestihani je typicky spickove - ojedinely
    /// dlouhy takt by se v prumeru za sekundu ztratil. <see cref="WorstTickTime"/> je kotva, podle
    /// ktere se v ostatnich zpravach dohleda, co robot v tu chvili delal.</para>
    ///
    /// <para>Viz doc/perf-monitoring.md.</para>
    /// </summary>
    [Serializable()]
    public class PerfMsg : Message
    {
        /// <summary>Takty na jednom jadru - kvuli nestejnym jadrum RK3588.</summary>
        public struct CoreEntry
        {
            public int ProcessorId;
            public int TickCount;
            public double AvgMs;
        }

        /// <summary>Stav a vykon jednoho stupne pipeline.</summary>
        public struct StageEntry
        {
            public string Name;
            public int QueueLength;
            public long Processed;
            public long Dropped;
            public double AvgMs;
            public double MaxMs;
        }

        /// <summary>Zacatek intervalu.</summary>
        public DateTime From;
        /// <summary>Konec intervalu; delka nemusi byt presne 1 s, kdyz se sberac opozdi.</summary>
        public DateTime To;

        public int TickCount;
        /// <summary>Takty, ktere se nestihly vydat vcas (dnes se dohaneji).</summary>
        public int MissedTicks;

        /// <summary>Prumerna obsazenost periody [%]. HLAVNI CISLO.</summary>
        public double OccupancyAvgPct;
        /// <summary>Nejvetsi obsazenost periody [%] v intervalu.</summary>
        public double OccupancyMaxPct;

        public double DelayAvgMs;
        public double DelayMaxMs;

        /// <summary>Cas nejdelsiho taktu - kotva pro dohledani v ostatnich zpravach.</summary>
        public DateTime WorstTickTime;
        /// <summary>Jadro, na kterem nejdelsi takt bezel.</summary>
        public int WorstProcessorId;

        /// <summary>Vytizeni procesu [%] z CELEHO stroje (ne z jednoho jadra); -1 = neznamo.</summary>
        public double ProcessCpuPct = -1;
        /// <summary>Vytizeni stroje [%]; -1 = neznamo (fáze 3).</summary>
        public double MachineCpuPct = -1;

        public PerfVerdict Verdict;

        public List<CoreEntry> Cores = new List<CoreEntry>();
        public List<StageEntry> Stages = new List<StageEntry>();

        public PerfMsg() : base("PerfMsg", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            Write(bw, From);
            Write(bw, To);
            bw.Write(TickCount);
            bw.Write(MissedTicks);
            bw.Write(OccupancyAvgPct);
            bw.Write(OccupancyMaxPct);
            bw.Write(DelayAvgMs);
            bw.Write(DelayMaxMs);
            Write(bw, WorstTickTime);
            bw.Write(WorstProcessorId);
            bw.Write(ProcessCpuPct);
            bw.Write(MachineCpuPct);
            bw.Write((int)Verdict);

            bw.Write(Cores?.Count ?? 0);
            foreach (var c in Cores ?? new List<CoreEntry>())
            {
                bw.Write(c.ProcessorId);
                bw.Write(c.TickCount);
                bw.Write(c.AvgMs);
            }

            bw.Write(Stages?.Count ?? 0);
            foreach (var s in Stages ?? new List<StageEntry>())
            {
                bw.Write(s.Name ?? string.Empty);
                bw.Write(s.QueueLength);
                bw.Write(s.Processed);
                bw.Write(s.Dropped);
                bw.Write(s.AvgMs);
                bw.Write(s.MaxMs);
            }
        }

        public override void FromData(BinaryReader br)
        {
            From = ReadDateTime(br);
            To = ReadDateTime(br);
            TickCount = br.ReadInt32();
            MissedTicks = br.ReadInt32();
            OccupancyAvgPct = br.ReadDouble();
            OccupancyMaxPct = br.ReadDouble();
            DelayAvgMs = br.ReadDouble();
            DelayMaxMs = br.ReadDouble();
            WorstTickTime = ReadDateTime(br);
            WorstProcessorId = br.ReadInt32();
            ProcessCpuPct = br.ReadDouble();
            MachineCpuPct = br.ReadDouble();
            Verdict = (PerfVerdict)br.ReadInt32();

            int coreCount = br.ReadInt32();
            Cores = new List<CoreEntry>(coreCount);
            for (int i = 0; i < coreCount; i++)
                Cores.Add(new CoreEntry
                {
                    ProcessorId = br.ReadInt32(),
                    TickCount = br.ReadInt32(),
                    AvgMs = br.ReadDouble(),
                });

            int stageCount = br.ReadInt32();
            Stages = new List<StageEntry>(stageCount);
            for (int i = 0; i < stageCount; i++)
                Stages.Add(new StageEntry
                {
                    Name = br.ReadString(),
                    QueueLength = br.ReadInt32(),
                    Processed = br.ReadInt64(),
                    Dropped = br.ReadInt64(),
                    AvgMs = br.ReadDouble(),
                    MaxMs = br.ReadDouble(),
                });
        }

        public override Message Build() => new PerfMsg();

        public override string ToString()
            => string.Format("PerfMsg obsazenost {0:F0}/{1:F0}% takty={2} zameskane={3} {4}",
                             OccupancyAvgPct, OccupancyMaxPct, TickCount, MissedTicks, Verdict);
    }
}
```

- [x] **Krok 4: Zaregistruj zprávu do katalogu**

V `Src/ARBot.Common/Communication/MessageCatalog.cs` k ostatním `c.Register(...)` přidej:

```csharp
            c.Register(new PerfMsg());
```

- [x] **Krok 5: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter PerfMsgSerializationTests`
Čekej: PASS (2 testy). Když padne na neznámý typ zprávy, chybí registrace z kroku 4.

---

## Task 5: Sběrač

**Files:**
- Create: `Src/ARBot.Common/Diagnostics/PerfCollector.cs`
- Modify: `Src/ARBot.Common/Configuration/ParamRegistry.cs` (prahy)
- Test: `Src/ARBot.Common.Tests/Diagnostics/PerfCollectorTests.cs`

**Interfaces:**
- Consumes: `TickStats`/`TickSnapshot` (Task 1), `ISchedulerMetrics` (Task 2),
  `MessageTarget.TakeStageSnapshot` (Task 3), `PerfMsg` (Task 4)
- Produces:
  `PerfCollector(TimeSpan period, TimeSpan interval, IMessageSink sink, Func<DateTime> now)`;
  `void AddStage(MessageTarget stage)`; `ISchedulerMetrics Metrics { get; }`;
  `PerfMsg BuildSnapshot()` — sestaví zprávu bez odeslání (kvůli testům);
  `void Start()` / `Dispose()`

- [x] **Krok 1: Přidej prahy do registru parametrů**

V `Src/ARBot.Common/Configuration/ParamRegistry.cs` do kategorie `K_DIAG` přidej:

```csharp
            Konst("perf", ParamType.Bool, "true", K_DIAG,
                  "Meri, jestli ridici smycka stiha svou periodu (zprava PerfMsg 1x za sekundu). "
                  + "Viz doc/perf-monitoring.md.");
            Konst("perfwarn", ParamType.Double, "70", K_DIAG,
                  "Obsazenost periody [%], od ktere se hlasi varovani. Hodnota je zatim odhad - "
                  + "naostro se nastavi az podle prvniho mereni na zarizeni.");
```

> Prahy jsou v registru schválně: naostro je půjde nastavit bez překladu, až se ukáže, jaké
> hodnoty jsou na Pi normální. Zameškaný takt je **chyba vždy**, ten práh nemá.

- [x] **Krok 2: Napiš padající test**

`Src/ARBot.Common.Tests/Diagnostics/PerfCollectorTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Diagnostics
{
    /// <summary>
    /// Sberac sestavi PerfMsg z metrik smycky a stupnu. Viz doc/perf-monitoring.md.
    /// </summary>
    public class PerfCollectorTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private sealed class Jimka : IMessageSink
        {
            public readonly List<Message> Zpravy = new();
            public void Post(Message msg) => Zpravy.Add(msg);
        }

        private static PerfCollector Sberac(Jimka j, Func<DateTime> now)
            => new PerfCollector(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1), j, now);

        [Test]
        public void Snimek_PrenaseMetrikySmycky()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 1);
            c.Metrics.OnTickCompleted(T0, durationMs: 50, processorId: 3);

            var msg = c.BuildSnapshot();
            Assert.That(msg.TickCount, Is.EqualTo(1));
            Assert.That(msg.OccupancyAvgPct, Is.EqualTo(50.0).Within(1e-9));
            Assert.That(msg.Cores, Has.Count.EqualTo(1));
            Assert.That(msg.Cores[0].ProcessorId, Is.EqualTo(3));
        }

        [Test]
        public void ZameskaneTakty_SeSpocitajiZDavky()
        {
            // OnTicksDue s count=3 znamena: jeden vcas, dva zameskane.
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0.AddMilliseconds(350), 3);

            Assert.That(c.BuildSnapshot().MissedTicks, Is.EqualTo(2));
        }

        [Test]
        public void Verdikt_ChybaPriZameskanemTaktu()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 2);

            Assert.That(c.BuildSnapshot().Verdict, Is.EqualTo(PerfVerdict.Error));
        }

        [Test]
        public void Verdikt_VarovaniPriVysokeObsazenosti()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 1);
            c.Metrics.OnTickCompleted(T0, durationMs: 95, processorId: 0);   // 95 % periody

            Assert.That(c.BuildSnapshot().Verdict, Is.EqualTo(PerfVerdict.Warning));
        }

        [Test]
        public void Verdikt_OkKdyzSeStiha()
        {
            var j = new Jimka();
            using var c = Sberac(j, () => T0);

            c.Metrics.OnTicksDue(T0, T0, 1);
            c.Metrics.OnTickCompleted(T0, durationMs: 10, processorId: 0);

            Assert.That(c.BuildSnapshot().Verdict, Is.EqualTo(PerfVerdict.Ok));
        }

        [Test]
        public void Interval_JeOdPoslednihoOdectu()
        {
            var cas = T0;
            var j = new Jimka();
            using var c = Sberac(j, () => cas);

            c.BuildSnapshot();
            cas = T0.AddSeconds(1);
            var msg = c.BuildSnapshot();

            Assert.That(msg.From, Is.EqualTo(T0));
            Assert.That(msg.To, Is.EqualTo(T0.AddSeconds(1)));
        }
    }
}
```

- [x] **Krok 3: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter PerfCollectorTests`
Čekej: chyba překladu — `PerfCollector` neexistuje.

- [x] **Krok 4: Napiš sběrač**

`Src/ARBot.Common/Diagnostics/PerfCollector.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Configuration;
using ARBot.Common.Logs;

namespace ARBot.Common.Diagnostics
{
    /// <summary>
    /// Jednou za interval sestavi <see cref="PerfMsg"/> z metrik ridici smycky a stupnu a posle ji
    /// do streamu - tim jde soucasne do UI i do zaznamu.
    ///
    /// <para>⚠️ <b>Ma VLASTNI casovac, ne ridici mrizku.</b> Kdyby visel na scheduleru, prestal by
    /// posilat prave ve chvili, kdy se nestiha - tedy kdyz je nejvic potreba. Nezavisly casovac
    /// navic zachyti i pripad, kdy rizeni stoji uplne. Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public sealed class PerfCollector : IDisposable
    {
        private sealed class Metriky : ISchedulerMetrics
        {
            private readonly PerfCollector owner;
            public Metriky(PerfCollector owner) { this.owner = owner; }

            public void OnTicksDue(DateTime firstPlanned, DateTime now, int count)
            {
                // Prvni takt je vcasny, zbytek jsou zameskane a dohanene.
                if (count > 1) owner.ticks.AddMissed(count - 1);
                owner.lastDelayMs = Math.Max(0, (now - firstPlanned).TotalMilliseconds);
            }

            public void OnTickCompleted(DateTime planned, double durationMs, int processorId)
                => owner.ticks.AddTick(planned, owner.lastDelayMs, durationMs, processorId);
        }

        private readonly TickStats ticks;
        private readonly TimeSpan interval;
        private readonly IMessageSink sink;
        private readonly Func<DateTime> now;
        private readonly List<MessageTarget> stages = new List<MessageTarget>();
        private readonly Process process = Process.GetCurrentProcess();
        private readonly double warnPct;

        private Timer timer;
        private DateTime lastTake;
        private double lastDelayMs;
        private TimeSpan lastCpu;
        private DateTime lastCpuAt;

        public PerfCollector(TimeSpan period, TimeSpan interval, IMessageSink sink, Func<DateTime> now)
        {
            this.interval = interval;
            this.sink = sink ?? throw new ArgumentNullException(nameof(sink));
            this.now = now ?? throw new ArgumentNullException(nameof(now));
            ticks = new TickStats(period);
            Metrics = new Metriky(this);

            lastTake = now();
            lastCpu = process.TotalProcessorTime;
            lastCpuAt = lastTake;

            warnPct = ParamStore.Current.GetDouble("perfwarn", 70);
        }

        /// <summary>Odberatel, ktery se predava do <c>Scheduler.Metrics</c>.</summary>
        public ISchedulerMetrics Metrics { get; }

        /// <summary>Zaradi stupen do mereni. Vola se pred <see cref="Start"/>.</summary>
        public void AddStage(MessageTarget stage)
        {
            if (stage != null) stages.Add(stage);
        }

        /// <summary>Spusti vlastni casovac.</summary>
        public void Start()
        {
            if (timer != null) return;
            int ms = Math.Max(100, (int)interval.TotalMilliseconds);
            timer = new Timer(_ =>
            {
                try { sink.Post(BuildSnapshot()); }
                catch (Exception ex) { Debug.WriteLine(ex); }
            }, null, ms, ms);
        }

        /// <summary>
        /// Sestavi zpravu za interval od posledniho odectu a metriky vynuluje.
        /// Verejne kvuli testum - v behu ji vola casovac.
        /// </summary>
        public PerfMsg BuildSnapshot()
        {
            DateTime t = now();
            var snap = ticks.TakeSnapshot();

            var msg = new PerfMsg
            {
                From = lastTake,
                To = t,
                TickCount = snap.TickCount,
                MissedTicks = snap.MissedTicks,
                OccupancyAvgPct = snap.OccupancyAvgPct,
                OccupancyMaxPct = snap.OccupancyMaxPct,
                DelayAvgMs = snap.DelayAvgMs,
                DelayMaxMs = snap.DelayMaxMs,
                WorstTickTime = snap.WorstTickTime,
                WorstProcessorId = snap.WorstProcessorId,
                ProcessCpuPct = ZmerCpuProcesu(t),
                MachineCpuPct = -1,          // fáze 3 (HAL)
            };

            foreach (var c in snap.Cores)
                msg.Cores.Add(new PerfMsg.CoreEntry
                {
                    ProcessorId = c.ProcessorId, TickCount = c.TickCount, AvgMs = c.AvgMs,
                });

            foreach (var s in stages)
            {
                var st = s.TakeStageSnapshot();
                msg.Stages.Add(new PerfMsg.StageEntry
                {
                    Name = st.Name, QueueLength = st.QueueLength, Processed = st.Processed,
                    Dropped = st.Dropped, AvgMs = st.AvgMs, MaxMs = st.MaxMs,
                });
            }

            msg.Verdict = Verdikt(msg);
            lastTake = t;
            return msg;
        }

        /// <summary>
        /// Zameskany takt je chyba VZDY (prah nema): znamena, ze se rizeni uz nestiha na mrizce.
        /// Obsazenost nad prahem je varovani - jeste se stiha, ale rezerva dochazi.
        /// </summary>
        private PerfVerdict Verdikt(PerfMsg m)
        {
            if (m.MissedTicks > 0) return PerfVerdict.Error;
            if (m.OccupancyMaxPct >= warnPct) return PerfVerdict.Warning;
            return PerfVerdict.Ok;
        }

        /// <summary>
        /// Vytizeni procesu v procentech CELEHO stroje. TotalProcessorTime je soucet pres vsechna
        /// jadra, proto se deli poctem jader - jinak by na 8 jadrech vychazelo az 800 %
        /// (linuxovy zvyk z `top`). Viz doc/perf-monitoring.md.
        /// </summary>
        private double ZmerCpuProcesu(DateTime t)
        {
            try
            {
                var cpu = process.TotalProcessorTime;
                double wallMs = (t - lastCpuAt).TotalMilliseconds;
                double cpuMs = (cpu - lastCpu).TotalMilliseconds;
                lastCpu = cpu;
                lastCpuAt = t;

                if (wallMs <= 0) return -1;
                return 100.0 * cpuMs / (wallMs * Math.Max(1, Environment.ProcessorCount));
            }
            catch { return -1; }
        }

        public void Dispose()
        {
            timer?.Dispose();
            timer = null;
            process?.Dispose();
        }
    }
}
```

- [x] **Krok 5: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter PerfCollectorTests`
Čekej: PASS (6 testů).

- [x] **Krok 6: Doplň strážný test registru**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamRegistryGuardTests`
Čekej: **padne** — `perf` a `perfwarn` jsou v registru, ale nikdo je nečte
(`perfwarn` čte `PerfCollector`, ale `perf` zatím nikdo). Task 6 to napraví; do té doby je to
očekávaný mezistav.

---

## Task 6: Napojení v runtime

**Files:**
- Modify: `Src/ARBot/Robot/ARBotRuntime.cs`

**Interfaces:**
- Consumes: `PerfCollector` (Task 5), `Scheduler.Metrics` (Task 2)

- [x] **Krok 1: Vytvoř sběrač a napoj ho na scheduler**

V `Src/ARBot/Robot/ARBotRuntime.cs` hned za `var scheduler = new Scheduler();` přidej:

```csharp
            // Mereni vykonu rizeni (parametr perf=). Sberac ma VLASTNI casovac, ne ridici mrizku -
            // jinak by prestal posilat prave kdyz se nestiha. Viz doc/perf-monitoring.md.
            if (Program.GetParamBool("perf", true))
            {
                perf = new ARBot.Common.Diagnostics.PerfCollector(
                    TimeSpan.FromMilliseconds(Profile.Ts),
                    TimeSpan.FromSeconds(1),
                    stream,
                    () => DateTime.UtcNow);
                scheduler.Metrics = perf.Metrics;
            }
```

Deklaruj pole vedle ostatních (u `private FileMessageSource fileSource;`):

```csharp
        /// <summary>Sberac metrik vykonu (parametr perf=); null, kdyz se nemeri.</summary>
        private ARBot.Common.Diagnostics.PerfCollector perf;
```

- [x] **Krok 2: Zařaď stupně a spusť sběrač**

Za `foreach (var s in sources) s.Start();` (tam, kde už stupně běží) přidej:

```csharp
            if (perf != null)
            {
                foreach (var s in stages)
                    perf.AddStage(s);
                perf.Start();
            }
```

- [x] **Krok 3: Ukliď sběrač při zastavení**

V `Src/ARBot/Robot/ARBotRuntime.cs` je v zastavovací sekvenci krok „2) Zastav casovac
scheduleru" (`schedTimer?.Dispose(); schedTimer = null;`, kolem řádku 166). **Hned za něj**
přidej:

```csharp
                // Sberac metrik ma vlastni casovac, takze se zastavuje zvlast.
                perf?.Dispose();
                perf = null;
```

Pořadí je záměrné: sběrač se zastaví **až po** časovači scheduleru, ale **před** odpojením grafu
(krok 3) — aby poslední snímek ještě mohl odejít do streamu.

- [x] **Krok 4: Ověř build**

Spusť: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Čekej: build prochází.

- [x] **Krok 5: Ověř strážný test registru**

Spusť: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter ParamRegistryGuardTests`
Čekej: PASS — `perf` už se čte v runtime, `perfwarn` v `PerfCollector`.

- [x] **Krok 6: Ověř za běhu, že zpráva vzniká**

Spusť:

```bash
dotnet run --project Src/ARBot/ARBot.csproj -p:Platform=x64 -- config=config/simulace-freerun.cfg selftest=true st_seconds=10 st_record=true st_name=perf
```

Pak nad vzniklým záznamem:

```bash
dotnet run --project Src/ARBot.Analyze/ARBot.Analyze.csproj -p:Platform=x64 -- types <cesta k .rec>
```

Čekej: ve výpisu typů je `PerfMsg` a jeho počet je zhruba roven délce běhu v sekundách.

---

## Task 7: Panel *Tools → Výkon*

**Files:**
- Create: `Src/ARBot/ViewModels/PerformanceDocument.cs`
- Create: `Src/ARBot/Views/PerformanceDocumentView.axaml` (+ `.axaml.cs`)
- Modify: `Src/ARBot/ViewModels/MainWindowViewModel.cs`
- Modify: `Src/ARBot/Views/MainWindow.axaml`

**Interfaces:**
- Consumes: `PerfMsg` (Task 4)
- Produces: `PerformanceDocument` s `Id = "Performance"`

- [x] **Krok 1: Napiš ViewModel**

`Src/ARBot/ViewModels/PerformanceDocument.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Robot;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ARBot.ViewModels
{
    /// <summary>Radek tabulky stupnu v panelu vykonu.</summary>
    public sealed class StageRow
    {
        public string Name { get; init; }
        public int Queue { get; init; }
        public long Processed { get; init; }
        public long Dropped { get; init; }
        public double AvgMs { get; init; }
        public double MaxMs { get; init; }
    }

    /// <summary>
    /// Dokument „Vykon": stiha ridici smycka svou periodu? Cte <see cref="PerfMsg"/> ze streamu,
    /// takze funguje i pri prehravani zaznamu (ve View se zpravy z behu prehraji).
    ///
    /// <para>Ukazuje POSLEDNI sekundu. Rozdeleni pres cely beh je uloha rozboru zaznamu
    /// (fáze 4), ne panelu. Viz doc/perf-monitoring.md.</para>
    /// </summary>
    public partial class PerformanceDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.PerformanceDocumentView);

        private IDisposable feed;

        [ObservableProperty] private string occupancy = "—";
        [ObservableProperty] private string delay = "—";
        [ObservableProperty] private string missed = "—";
        [ObservableProperty] private string worst = "—";
        [ObservableProperty] private string cpu = "—";
        [ObservableProperty] private string verdict = "—";

        /// <summary>Barva verdiktu: zelena / oranzova / cervena.</summary>
        [ObservableProperty] private string verdictColor = "#4CAF50";

        public ObservableCollection<StageRow> Stages { get; } = new ObservableCollection<StageRow>();
        public ObservableCollection<string> Cores { get; } = new ObservableCollection<string>();

        public PerformanceDocument()
        {
            Id = "Performance";
            Title = "Výkon";

            if (Avalonia.Controls.Design.IsDesignMode)
                return;

            // Tentyz zpusob pripojeni jako VirtualSensorsDocument - stream muze byt null,
            // kdyz runtime jeste nebezi.
            try { feed = ARBotRuntime.Current?.Stream?.Connect(this); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        void IMessageSink.Post(Message msg)
        {
            if (msg is not PerfMsg p) return;
            Dispatcher.UIThread.Post(() => Zobraz(p));
        }

        private void Zobraz(PerfMsg p)
        {
            Occupancy = $"{p.OccupancyAvgPct:F0} % (max {p.OccupancyMaxPct:F0} %)";
            Delay = $"{p.DelayAvgMs:F1} ms (max {p.DelayMaxMs:F1} ms)";
            Missed = p.MissedTicks.ToString();
            Worst = p.TickCount == 0
                    ? "—"
                    : $"{p.WorstTickTime:HH:mm:ss.fff} na jádru {p.WorstProcessorId}";
            Cpu = p.ProcessCpuPct < 0 ? "—" : $"{p.ProcessCpuPct:F0} %";

            Verdict = p.Verdict switch
            {
                PerfVerdict.Error => "NESTÍHÁ",
                PerfVerdict.Warning => "dochází rezerva",
                _ => "OK",
            };
            VerdictColor = p.Verdict switch
            {
                PerfVerdict.Error => "#E05252",
                PerfVerdict.Warning => "#E0A052",
                _ => "#4CAF50",
            };

            Cores.Clear();
            foreach (var c in p.Cores)
                Cores.Add($"jádro {c.ProcessorId}: {c.TickCount}×, průměr {c.AvgMs:F1} ms");

            Stages.Clear();
            foreach (var s in p.Stages)
                Stages.Add(new StageRow
                {
                    Name = s.Name, Queue = s.QueueLength, Processed = s.Processed,
                    Dropped = s.Dropped, AvgMs = s.AvgMs, MaxMs = s.MaxMs,
                });
        }

        public void Dispose()
        {
            feed?.Dispose();
            feed = null;
        }
    }
}
```

- [x] **Krok 2: Napiš View**

`Src/ARBot/Views/PerformanceDocumentView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ARBot.ViewModels"
             x:Class="ARBot.Views.PerformanceDocumentView"
             x:DataType="vm:PerformanceDocument">
    <Design.DataContext><vm:PerformanceDocument/></Design.DataContext>

    <DockPanel Margin="8">
        <Border DockPanel.Dock="Top" CornerRadius="4" Padding="10,6" Margin="0,0,0,8"
                Background="{Binding VerdictColor}">
            <TextBlock Text="{Binding Verdict}" FontWeight="Bold" Foreground="White"/>
        </Border>

        <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*" RowDefinitions="Auto,Auto,Auto,Auto,Auto"
              Margin="0,0,0,8">
            <TextBlock Grid.Row="0" Grid.Column="0" Text="Obsazenost periody" Margin="0,0,12,2"/>
            <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding Occupancy}"/>
            <TextBlock Grid.Row="1" Grid.Column="0" Text="Zpoždění taktu" Margin="0,0,12,2"/>
            <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding Delay}"/>
            <TextBlock Grid.Row="2" Grid.Column="0" Text="Zameškané takty" Margin="0,0,12,2"/>
            <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding Missed}"/>
            <TextBlock Grid.Row="3" Grid.Column="0" Text="Nejhorší takt" Margin="0,0,12,2"/>
            <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding Worst}"/>
            <TextBlock Grid.Row="4" Grid.Column="0" Text="CPU procesu" Margin="0,0,12,2"/>
            <TextBlock Grid.Row="4" Grid.Column="1" Text="{Binding Cpu}"/>
        </Grid>

        <ItemsControl DockPanel.Dock="Top" ItemsSource="{Binding Cores}" Margin="0,0,0,8"/>

        <DataGrid ItemsSource="{Binding Stages}" IsReadOnly="True"
                  AutoGenerateColumns="False" GridLinesVisibility="Horizontal">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Stupeň" x:DataType="vm:StageRow"
                                    Binding="{Binding Name}" Width="*"/>
                <DataGridTextColumn Header="Fronta" x:DataType="vm:StageRow"
                                    Binding="{Binding Queue}"/>
                <DataGridTextColumn Header="Zpracováno" x:DataType="vm:StageRow"
                                    Binding="{Binding Processed}"/>
                <DataGridTextColumn Header="Zahozeno" x:DataType="vm:StageRow"
                                    Binding="{Binding Dropped}"/>
                <DataGridTextColumn Header="Průměr [ms]" x:DataType="vm:StageRow"
                                    Binding="{Binding AvgMs, StringFormat=\{0:F1\}}"/>
                <DataGridTextColumn Header="Max [ms]" x:DataType="vm:StageRow"
                                    Binding="{Binding MaxMs, StringFormat=\{0:F1\}}"/>
            </DataGrid.Columns>
        </DataGrid>
    </DockPanel>
</UserControl>
```

`Src/ARBot/Views/PerformanceDocumentView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace ARBot.Views
{
    /// <summary>View panelu „Vykon" - logika je v <see cref="ARBot.ViewModels.PerformanceDocument"/>.</summary>
    public partial class PerformanceDocumentView : UserControl
    {
        public PerformanceDocumentView()
        {
            InitializeComponent();
        }
    }
}
```

- [x] **Krok 3: Přidej příkaz do `MainWindowViewModel`**

Za `OpenConfiguration` přidej (týž vzor):

```csharp
        /// <summary>
        /// Otevre (nebo aktivuje) panel „Vykon": stiha ridici smycka svou periodu, ktera cast ji
        /// brzdi a jak je na tom stroj. Viz doc/perf-monitoring.md.
        /// </summary>
        [RelayCommand]
        private void OpenPerformance()
        {
            var dock = _factory.DocumentDock;
            if (dock == null)
                return;

            var existing = dock.VisibleDockables?.FirstOrDefault(d => d.Id == "Performance");
            if (existing != null)
            {
                _factory.SetActiveDockable(existing);
                if (Layout is not null) _factory.SetFocusedDockable(Layout, existing);
                return;
            }

            var doc = new PerformanceDocument();
            _factory.AddDockable(dock, doc);
            _factory.SetActiveDockable(doc);
            if (Layout is not null)
                _factory.SetFocusedDockable(Layout, doc);
        }
```

- [x] **Krok 4: Přidej položku do menu Tools**

V `Src/ARBot/Views/MainWindow.axaml` za položku *Konfigurace*:

```xml
                <MenuItem Header="Výkon" Command="{Binding OpenPerformanceCommand}"
                          ToolTip.Tip="Stíhá řídicí smyčka svou periodu? Obsazenost, zameškané takty, fronty stupňů."/>
```

- [x] **Krok 5: Ověř build**

Spusť: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Čekej: build prochází bez chyb.

- [ ] **Krok 6: Ověř panel za běhu** — ČEKÁ NA AUTORA (bez headless Avalonia testů to automat neověří)

Spusť:

```bash
dotnet run --project Src/ARBot/ARBot.csproj -p:Platform=x64 -- config=config/simulace-freerun.cfg
```

Spusť Run, otevři *Tools → Výkon*. Ověř: obsazenost se aktualizuje jednou za sekundu, verdikt je
zelený, tabulka stupňů má řádky a `Zahozeno` je 0. Pak zkus zátěž (otevři víc pohledů) a sleduj,
jestli obsazenost roste.

- [x] **Krok 7: Spusť celou sadu testů a build obou platforem**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64
dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64
dotnet build Src/ARBot.Common/ARBot.Common.csproj -p:Platform=OrangePI
```

Čekej: vše zelené.

---

## Task 8: Dokumentace

- [x] **Krok 1: Aktualizuj stav ve specu**

V `doc/perf-monitoring.md` nahraď blok „Stav 2026-09-01" skutečností: co je hotové (fáze 1 a 2),
kolik testů, co je ověřeno za běhu a co ne (zejména že **na zařízení to neběželo**).

- [x] **Krok 2: Přidej odkaz do rozcestníku**

Do `CLAUDE.md` do sekce „Doménová dokumentace":

```markdown
- [doc/perf-monitoring.md](doc/perf-monitoring.md) — **měření výkonu řízení**: stíhá řídicí smyčka
  svou periodu? Obsazenost periody, zpoždění a **zameškané takty** ze `Scheduler`u, fronty
  a **zahozené zprávy** ze stupňů, CPU procesu — jednou za sekundu jako `PerfMsg` do streamu
  (tedy do UI i do záznamu) a panel *Tools → Výkon*. Zapíná `perf=` (výchozí true), práh varování
  `perfwarn=`. **Dva nálezy čekají na měření, ne na opravu:** scheduler zameškané takty
  **dohání** (vědomá kompenzace reentrančního guardu časovače) a krok rampy dobrzdění se počítá
  z periody, ne ze skutečného odstupu. Fáze 3 (teplota, frekvence, CPU stroje) a 4
  (`ARBot.Analyze perf`) zbývají.
```

- [x] **Krok 3: Doplň DevLog**

Do `doc/devlog.md` nahoru přidej záznam dne podle pravidel v hlavičce toho souboru. Zmiň:
co vzniklo, že měření sedí ve `Scheduler`u a proč, že sběrač má vlastní časovač a proč, a **že
zahozené zprávy stupňů se dosud nepočítaly vůbec**.

---

## Co zůstane neověřené

- **Chování na zařízení.** Všechno ověření je na Windows; hodnoty obsazenosti na RK3588 budou jiné
  a **práh `perfwarn` je zatím odhad**.
- **Rozpad po jádrech nemá jak se ověřit na vývojovém stroji** — big.LITTLE je vlastnost cílového
  HW. Test pokrývá jen správnost agregace, ne to, že se rozdíl mezi jádry skutečně projeví.
- **Cena měření.** Že zapnutí `perf=true` samo nezhorší obsazenost, se dá poznat až A/B během
  na zařízení (`perf=false` vs. `perf=true`).
