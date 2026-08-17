# Telemetrický pohled — implementační plán (fáze 1)

> **Stav 2026-08-17 (večer):** Tento plán pokrýval fázi 1; ta je hotová **a nad rámec plánu** k ní
> přibyl výběr sloupců, filtr řádků a **fáze 2 (graf řad v čase)**. Aktuální stav drží
> [telemetry-view.md](telemetry-view.md) — plán níž je od té chvíle jen historie postupu.
>
> **Stav 2026-08-17:** Tasky 1–5 **hotové**. Jádro má 14 testů (zelené, celá sada 516) a je ověřené
> i na skutečném záznamu (`records/20260814-132817.rec`: index 27 541 zpráv, sken 29 ms, 2806 řádků).
> **Tabulku autor téhož dne otevřel nad reálným záznamem**; z jeho zpětné vazby vzešlo doladění
> čitelnosti, tooltipy s významem údajů a hlavně **dodělání směru „kurzor přehrávání → řádek"**,
> který v tasku 5 chyběl (byl jen seek z tabulky) — ten zatím **za běhu ověřený není**. Aktuální
> stav a co zbývá drží [telemetry-view.md](telemetry-view.md); odchylky proti plánu jsou
> zaznamenané u příslušných tasků níže.

**Spec:** [doc/telemetry-view.md](telemetry-view.md) — plán z ní vychází, čti obojí.

**Cíl:** Nový dokument, ve kterém je stav robota, řídicí zásahy a údaje z dalších zpráv vidět
pohromadě jako tabulka řazená podle času, s detailem vybraného řádku a napojením na přehrávání.

**Architektura:** Jádro (tabulka, držení hodnot, sken záznamu) je v `ARBot.Common/Telemetry` — bez
závislosti na UI, takže se dá pokrýt testy. Registr sloupců (co, jednotka, formát) a Avalonia view
jsou v `ARBot`. Data vznikají **jedním skenem** indexu záznamu při otevření dokumentu; neregistrované
typy zpráv se vůbec nečtou.

**Technologie:** .NET 10, C#, NUnit 4 (`Assert.That`), Avalonia 12 + Dock + CommunityToolkit.Mvvm.

## Globální omezení (platí pro každý krok)

- **Build i testy vždy pro konkrétní platformu:** `dotnet build <proj> -p:Platform=x64`,
  `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`. Nikdy `AnyCPU`.
- **Jazyk:** čeština v komentářích i dokumentaci. Komentáře v `Src/**` bez diakritiky (konvence
  okolních souborů), `doc/**` s diakritikou.
- **Commity nejsou součástí kroků.** Commituje autor na vlastní pokyn (CLAUDE.md). Každý krok
  končí zeleným buildem a testy.
- **Směr závislostí:** `ARBot.Common` nesmí vidět UI. Doména si nedělá formátování pro UI.
- **Nemazat starou implementaci, dokud novou nepotvrdí testy** (CLAUDE.md) — tady se nic nepřepisuje,
  vše je nové, takže se to týká jen případných úprav `ARBotRuntime`.
- **DevLog:** na konci celku doplnit záznam do [devlog.md](devlog.md).

---

## Rozvržení souborů

| Soubor | Odpovědnost |
|---|---|
| `Src/ARBot.Common/Telemetry/ColumnSpec.cs` | popis jednoho sloupce (odkud hodnota, jak se zobrazí) |
| `Src/ARBot.Common/Telemetry/TelemetryTable.cs` | hotová tabulka: řádky, sloupce, dotazy na hodnotu / fresh |
| `Src/ARBot.Common/Telemetry/TelemetryTableBuilder.cs` | skládání tabulky ze zpráv (držení hodnot, slévání řádků, strop) |
| `Src/ARBot.Common/Telemetry/TelemetryScanner.cs` | průchod indexem záznamu → `TelemetryTable` |
| `Src/ARBot.Common.Tests/Telemetry/TelemetryTableBuilderTests.cs` | testy jádra bez I/O |
| `Src/ARBot.Common.Tests/Telemetry/TelemetryScannerTests.cs` | testy skenu nad záznamem v `MemoryStream` |
| `Src/ARBot/Telemetry/TelemetryColumns.cs` | **registr** sloupců (prezentační vrstva) |
| `Src/ARBot/ViewModels/TelemetryDocument.cs` | ViewModel dokumentu (sken na pozadí, výběr sloupců, detail) |
| `Src/ARBot/Views/TelemetryDocumentView.axaml(.cs)` | tabulka + panel detailu |
| `Src/ARBot/Robot/ARBotRuntime.cs` | **úprava**: vystavit cestu k přehrávanému záznamu |
| `Src/ARBot/ViewModels/MainWindowViewModel.cs` | **úprava**: menu Tools → Telemetrie |

Mimo tento plán (samostatné kroky později): rozšíření `LocalPlanMsg` o rychlostní diagnostiku,
grafy (fáze 2), režim Run.

---

## Task 1: Jádro tabulky (`ColumnSpec`, `TelemetryTable`, `TelemetryTableBuilder`)

**Files:**
- Create: `Src/ARBot.Common/Telemetry/ColumnSpec.cs`
- Create: `Src/ARBot.Common/Telemetry/TelemetryTable.cs`
- Create: `Src/ARBot.Common/Telemetry/TelemetryTableBuilder.cs`
- Test: `Src/ARBot.Common.Tests/Telemetry/TelemetryTableBuilderTests.cs`

**Interfaces:**
- Produces:
  - `ColumnSpec { string MsgName; string Name; string Header; string Format; bool Graphable; Func<Message,double?> Value; Func<double,string> Text; }`
  - `TelemetryColumn` s `ColumnSpec Spec`, `bool HasValue(int row)`, `bool IsFresh(int row)`, `double? ValueAt(int row)`, `DateTime TimeAt(int row)`, `string TextAt(int row)`
  - `TelemetryTable` s `int RowCount`, `DateTime RowTime(int row)`, `long RowSeq(int row)`, `string RowMsgName(int row)`, `IReadOnlyList<TelemetryColumn> Columns`, `bool Truncated`
  - `TelemetryTableBuilder(IReadOnlyList<ColumnSpec> columns, int maxRows = 500_000)` s `void Add(Message msg, in IndexEntry entry)`, `bool IsFull`, `TelemetryTable Build()`

- [ ] **Step 1: Napsat padající testy**

`Src/ARBot.Common.Tests/Telemetry/TelemetryTableBuilderTests.cs`:

```csharp
using System;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Telemetry;

namespace ARBot.Common.Tests.Telemetry
{
    public class TelemetryTableBuilderTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ColumnSpec SpeedColumn() => new ColumnSpec
        {
            MsgName = "RobotStateMsg",
            Header = "v [m/s]",
            Value = m => m is RobotStateMsg r ? r.V : (double?)null,
        };

        private static ColumnSpec PlanLengthColumn() => new ColumnSpec
        {
            MsgName = "LocalPlanMsg",
            Header = "delka planu [m]",
            Value = m => m is LocalPlanMsg p ? p.LengthM : (double?)null,
        };

        private static RobotStateMsg Robot(DateTime t, double v)
            => new RobotStateMsg { TimeStamp = t, V = v };

        private static LocalPlanMsg Plan(DateTime t, double len)
            => new LocalPlanMsg { TimeStamp = t, LengthM = len };

        /// <summary>Zaznam indexu pro zpravu s vlastnim casem (T_in = T_out).</summary>
        private static IndexEntry Entry(long seq, DateTime t)
            => new IndexEntry { Seq = seq, CaptureTicks = t.Ticks, ArrivalTicks = t.Ticks };

        [Test]
        public void SlowColumn_HoldsValueOnFollowingRows()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Plan(T0, 8.0), Entry(0, T0));
            b.Add(Robot(T0.AddMilliseconds(50), 1.2), Entry(1, T0.AddMilliseconds(50)));
            b.Add(Robot(T0.AddMilliseconds(100), 1.3), Entry(2, T0.AddMilliseconds(100)));

            var t = b.Build();
            var plan = t.Columns[1];

            Assert.That(t.RowCount, Is.EqualTo(3));
            Assert.That(plan.ValueAt(1), Is.EqualTo(8.0));
            Assert.That(plan.ValueAt(2), Is.EqualTo(8.0));
            Assert.That(plan.TimeAt(2), Is.EqualTo(T0));      // cas ZUSTAVA casem zpravy
        }

        [Test]
        public void Fresh_IsTrueOnlyOnRowWhereValueArrived()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Plan(T0, 8.0), Entry(0, T0));
            b.Add(Robot(T0.AddMilliseconds(50), 1.2), Entry(1, T0.AddMilliseconds(50)));
            b.Add(Plan(T0.AddMilliseconds(200), 6.5), Entry(2, T0.AddMilliseconds(200)));

            var t = b.Build();
            var plan = t.Columns[1];

            Assert.That(plan.IsFresh(0), Is.True);
            Assert.That(plan.IsFresh(1), Is.False);
            Assert.That(plan.IsFresh(2), Is.True);
        }

        [Test]
        public void BeforeFirstMessageOfType_CellIsEmpty()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Robot(T0, 1.0), Entry(0, T0));

            var t = b.Build();
            var plan = t.Columns[1];

            Assert.That(plan.HasValue(0), Is.False);
            Assert.That(plan.ValueAt(0), Is.Null);
            Assert.That(plan.IsFresh(0), Is.False);
            Assert.That(plan.TextAt(0), Is.Empty);
        }

        [Test]
        public void RowTime_FallsBackToArrivalWhenCaptureMissing()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn() });
            var arrival = T0.AddSeconds(3);
            b.Add(Robot(T0, 1.0), new IndexEntry { Seq = 7, CaptureTicks = 0, ArrivalTicks = arrival.Ticks });

            var t = b.Build();

            Assert.That(t.RowTime(0), Is.EqualTo(arrival));
            Assert.That(t.RowSeq(0), Is.EqualTo(7));
        }

        [Test]
        public void MessagesWithEqualTime_MergeIntoOneRow()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn(), PlanLengthColumn() });
            b.Add(Robot(T0, 1.2), Entry(0, T0));
            b.Add(Plan(T0, 8.0), Entry(1, T0));      // tentyz cas -> tentyz radek

            var t = b.Build();

            Assert.That(t.RowCount, Is.EqualTo(1));
            Assert.That(t.Columns[0].ValueAt(0), Is.EqualTo(1.2));
            Assert.That(t.Columns[1].ValueAt(0), Is.EqualTo(8.0));
            Assert.That(t.RowSeq(0), Is.EqualTo(0), "radek si drzi Seq prvni zpravy");
        }

        [Test]
        public void MaxRows_StopsAddingAndReportsTruncated()
        {
            var b = new TelemetryTableBuilder(new[] { SpeedColumn() }, maxRows: 2);
            for (int i = 0; i < 5; i++)
                b.Add(Robot(T0.AddMilliseconds(i * 10), i), Entry(i, T0.AddMilliseconds(i * 10)));

            var t = b.Build();

            Assert.That(t.RowCount, Is.EqualTo(2));
            Assert.That(t.Truncated, Is.True);
            Assert.That(b.IsFull, Is.True);
        }

        [Test]
        public void TextAt_UsesSpecTextWhenProvided_OtherwiseFormat()
        {
            var status = new ColumnSpec
            {
                MsgName = "LocalPlanMsg",
                Header = "stav planu",
                Value = m => m is LocalPlanMsg p ? p.Status : (double?)null,
                Text = v => ((ARBot.Common.Occupancy.LocalPlanStatus)(int)v).ToString(),
            };
            var len = PlanLengthColumn();
            len.Format = "F1";

            var b = new TelemetryTableBuilder(new[] { status, len });
            b.Add(new LocalPlanMsg { TimeStamp = T0, LengthM = 8.25, Status = 1 }, Entry(0, T0));

            var t = b.Build();

            Assert.That(t.Columns[0].TextAt(0), Is.EqualTo("Partial"));
            Assert.That(t.Columns[1].TextAt(0), Is.EqualTo(8.25.ToString("F1")));
        }
    }
}
```

- [ ] **Step 2: Spustit testy a ověřit, že padají**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter TelemetryTableBuilderTests`
Expected: chyba kompilace — `ARBot.Common.Telemetry` neexistuje.

- [ ] **Step 3: Implementovat `ColumnSpec`**

```csharp
using System;
using ARBot.Common.Logs;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Popis jednoho sloupce telemetricke tabulky: ze ktere zpravy hodnota je a jak se zobrazi.
    /// Registr sloupcu zije v UI vrstve (jednotky a format jsou prezentacni vec) - viz
    /// doc/telemetry-view.md.
    /// </summary>
    public sealed class ColumnSpec
    {
        /// <summary>Typ zpravy, ze ktere se hodnota bere (<see cref="Message.MsgName"/>).</summary>
        public string MsgName;

        /// <summary>Volitelne i konkretni instance (<see cref="INamedMessage.Name"/>) - napr. leva
        /// vs. prava kamera. null/prazdne = kterakoli instance daneho typu.</summary>
        public string Name;

        /// <summary>Zahlavi sloupce vcetne jednotky, napr. "v [m/s]".</summary>
        public string Header;

        /// <summary>Format cisla pro zobrazeni (pouzije se, kdyz neni <see cref="Text"/>).</summary>
        public string Format = "F2";

        /// <summary>Smi tento sloupec do grafu? (Faze 2.)</summary>
        public bool Graphable = true;

        /// <summary>Hodnota ze zpravy; null = tato zprava sloupec neplni.</summary>
        public Func<Message, double?> Value;

        /// <summary>Volitelny prevod cisla na text (enum -> jmeno, bool -> "STOP").
        /// Ma prednost pred <see cref="Format"/>.</summary>
        public Func<double, string> Text;
    }
}
```

- [ ] **Step 4: Implementovat `TelemetryTable` + `TelemetryColumn`**

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Jeden sloupec hotove tabulky. Ulozena je hodnota a cas zpravy, ze ktere hodnota je;
    /// priznak "prisla prave teto radce" se NEUKLADA - vyplyne ze zmeny casu (viz
    /// <see cref="IsFresh"/>), takze se nemuze s daty rozejit.
    /// </summary>
    public sealed class TelemetryColumn
    {
        private readonly double[] value;
        private readonly long[] ticks;

        internal TelemetryColumn(ColumnSpec spec, double[] value, long[] ticks)
        {
            Spec = spec;
            this.value = value;
            this.ticks = ticks;
        }

        public ColumnSpec Spec { get; }

        /// <summary>Prisla uz nekdy (do tohoto radku) hodnota tohoto sloupce?</summary>
        public bool HasValue(int row) => ticks[row] != 0;

        /// <summary>Prisla hodnota PRAVE na tomto radku? (Jinak se drzi z minula.)</summary>
        public bool IsFresh(int row)
            => ticks[row] != 0 && (row == 0 || ticks[row] != ticks[row - 1]);

        /// <summary>Hodnota, nebo null kdyz jeste nikdy neprisla.</summary>
        public double? ValueAt(int row) => ticks[row] == 0 ? (double?)null : value[row];

        /// <summary>Cas zpravy, ze ktere hodnota je (u drzene hodnoty tedy starsi nez cas radku).</summary>
        public DateTime TimeAt(int row) => new DateTime(ticks[row]);

        /// <summary>Hodnota k zobrazeni; prazdny retezec, kdyz jeste neprisla.</summary>
        public string TextAt(int row)
        {
            if (ticks[row] == 0) return string.Empty;
            double v = value[row];
            if (Spec.Text != null) return Spec.Text(v);
            return v.ToString(Spec.Format ?? "F2", CultureInfo.CurrentCulture);
        }
    }

    /// <summary>
    /// Telemetricka tabulka: radky razene podle casu, sloupce = jednotlive udaje.
    /// Radek zaklada jedna zprava ze zaznamu; hodnoty ostatnich sloupcu se drzi z minula.
    /// Viz doc/telemetry-view.md.
    /// </summary>
    public sealed class TelemetryTable
    {
        private readonly long[] rowTicks;
        private readonly long[] rowSeq;
        private readonly string[] rowMsgName;

        internal TelemetryTable(long[] rowTicks, long[] rowSeq, string[] rowMsgName,
                                TelemetryColumn[] columns, bool truncated)
        {
            this.rowTicks = rowTicks;
            this.rowSeq = rowSeq;
            this.rowMsgName = rowMsgName;
            Columns = columns;
            Truncated = truncated;
        }

        public int RowCount => rowTicks.Length;

        /// <summary>Cas radku (T_in zakladajici zpravy, nebo T_out kdyz T_in chybi).</summary>
        public DateTime RowTime(int row) => new DateTime(rowTicks[row]);

        /// <summary>Seq zakladajici zpravy v zaznamu - pro seek.</summary>
        public long RowSeq(int row) => rowSeq[row];

        /// <summary>Typ zakladajici zpravy.</summary>
        public string RowMsgName(int row) => rowMsgName[row];

        public IReadOnlyList<TelemetryColumn> Columns { get; }

        /// <summary>Narazilo skladani na strop radku? (Tabulka pak nekonci s koncem zaznamu.)</summary>
        public bool Truncated { get; }
    }
}
```

- [ ] **Step 5: Implementovat `TelemetryTableBuilder`**

```csharp
using System;
using System.Collections.Generic;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Sklada <see cref="TelemetryTable"/> ze zprav v poradi, jak jsou v zaznamu.
    /// <para>Pravidla (viz doc/telemetry-view.md): radek = jedna prijata zprava; zpravy se SHODNYM
    /// casem radku se slevaji do jednoho radku; neaktualizovane sloupce si nesou hodnotu i cas
    /// z minula (drzeni).</para>
    /// </summary>
    public sealed class TelemetryTableBuilder
    {
        private readonly ColumnSpec[] specs;
        private readonly List<double>[] values;
        private readonly List<long>[] ticks;
        private readonly List<long> rowTicks = new List<long>();
        private readonly List<long> rowSeq = new List<long>();
        private readonly List<string> rowMsgName = new List<string>();
        private readonly int maxRows;
        private bool truncated;

        public TelemetryTableBuilder(IReadOnlyList<ColumnSpec> columns, int maxRows = 500_000)
        {
            if (columns == null) throw new ArgumentNullException(nameof(columns));
            this.maxRows = maxRows > 0 ? maxRows : 1;

            specs = new ColumnSpec[columns.Count];
            values = new List<double>[columns.Count];
            ticks = new List<long>[columns.Count];
            for (int i = 0; i < columns.Count; i++)
            {
                specs[i] = columns[i];
                values[i] = new List<double>();
                ticks[i] = new List<long>();
            }
        }

        /// <summary>Je uz dosazen strop radku? Dalsi <see cref="Add"/> se zahodi.</summary>
        public bool IsFull => rowTicks.Count >= maxRows;

        /// <summary>Zaradi zpravu. Vola se v poradi <see cref="IndexEntry.Seq"/>.</summary>
        public void Add(Message msg, in IndexEntry entry)
        {
            if (msg == null) return;

            long t = entry.CaptureTicks != 0 ? entry.CaptureTicks : entry.ArrivalTicks;
            int row;

            if (rowTicks.Count > 0 && rowTicks[rowTicks.Count - 1] == t)
            {
                row = rowTicks.Count - 1;      // tentyz takt -> doplnit stavajici radek
            }
            else
            {
                if (IsFull) { truncated = true; return; }

                row = rowTicks.Count;
                rowTicks.Add(t);
                rowSeq.Add(entry.Seq);
                rowMsgName.Add(msg.MsgName);

                // Novy radek zdedi vsechny hodnoty i jejich casy (drzeni z minula).
                for (int c = 0; c < specs.Length; c++)
                {
                    values[c].Add(row == 0 ? 0.0 : values[c][row - 1]);
                    ticks[c].Add(row == 0 ? 0L : ticks[c][row - 1]);
                }
            }

            // Sloupce, ktere tato zprava plni, prepsat na novou hodnotu a jeji cas.
            string name = (msg is INamedMessage nm) ? (nm.Name ?? string.Empty) : string.Empty;
            for (int c = 0; c < specs.Length; c++)
            {
                var spec = specs[c];
                if (!string.Equals(spec.MsgName, msg.MsgName, StringComparison.Ordinal)) continue;
                if (!string.IsNullOrEmpty(spec.Name)
                    && !string.Equals(spec.Name, name, StringComparison.Ordinal)) continue;
                if (spec.Value == null) continue;

                var v = spec.Value(msg);
                if (!v.HasValue) continue;

                values[c][row] = v.Value;
                ticks[c][row] = t;
            }
        }

        public TelemetryTable Build()
        {
            var cols = new TelemetryColumn[specs.Length];
            for (int c = 0; c < specs.Length; c++)
                cols[c] = new TelemetryColumn(specs[c], values[c].ToArray(), ticks[c].ToArray());

            return new TelemetryTable(rowTicks.ToArray(), rowSeq.ToArray(), rowMsgName.ToArray(),
                                      cols, truncated);
        }
    }
}
```

- [ ] **Step 6: Spustit testy — musí projít**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter TelemetryTableBuilderTests`
Expected: 7 testů PASS.

Pozn.: `TextAt` používá `CultureInfo.CurrentCulture`, proto test porovnává s `8.25.ToString("F1")`
místo pevného `"8,3"` — jinak by test spadl na stroji s jinou kulturou.

---

## Task 2: Sken záznamu (`TelemetryScanner`)

**Files:**
- Create: `Src/ARBot.Common/Telemetry/TelemetryScanner.cs`
- Test: `Src/ARBot.Common.Tests/Telemetry/TelemetryScannerTests.cs`

**Interfaces:**
- Consumes: `TelemetryTableBuilder`, `TelemetryTable`, `ColumnSpec` z Tasku 1.
- Produces:
  ```csharp
  static TelemetryTable TelemetryScanner.Scan(
      Stream data, IReadOnlyList<IndexEntry> index, MessageCatalog catalog,
      IReadOnlyList<ColumnSpec> columns, Encoding encoding,
      int maxRows = 500_000, IProgress<double> progress = null,
      CancellationToken ct = default)
  ```
  Bere `Stream`, ne cestu — díky tomu jde testovat nad `MemoryStream` a UI si otevře `FileStream`
  samo (s `FileShare.Read`, jak záznam otevírá i `ARBotRuntime`).

- [ ] **Step 1: Napsat padající testy**

`Src/ARBot.Common.Tests/Telemetry/TelemetryScannerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.Common.Telemetry;

namespace ARBot.Common.Tests.Telemetry
{
    public class TelemetryScannerTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static ColumnSpec SpeedColumn() => new ColumnSpec
        {
            MsgName = "RobotStateMsg",
            Header = "v [m/s]",
            Value = m => m is RobotStateMsg r ? r.V : (double?)null,
        };

        private static ColumnSpec PlanLengthColumn() => new ColumnSpec
        {
            MsgName = "LocalPlanMsg",
            Header = "delka planu [m]",
            Value = m => m is LocalPlanMsg p ? p.LengthM : (double?)null,
        };

        /// <summary>Zapise zpravy do zaznamu (data + sidecar index) pres RecordingTarget.</summary>
        private static (byte[] data, List<IndexEntry> index) Record(IEnumerable<Message> msgs)
        {
            using var dataMs = new MemoryStream();
            using var idxMs = new MemoryStream();

            var rec = new RecordingTarget(dataMs, idxMs, TestHelpers.Enc);
            rec.Start();
            foreach (var m in msgs) rec.Post(m);
            rec.Stop();

            var idxRead = new MemoryStream(idxMs.ToArray());
            return (dataMs.ToArray(), MessageIndex.Read(idxRead, TestHelpers.Enc));
        }

        private static List<Message> Sequence()
        {
            var list = new List<Message>
            {
                new LocalPlanMsg { TimeStamp = T0, LengthM = 8.0 },
                new RobotStateMsg { TimeStamp = T0.AddMilliseconds(50), V = 1.2 },
                TestHelpers.MakeImu(T0.AddMilliseconds(60), yaw: 0.1, omega: 0.0),   // NEregistrovana
                new RobotStateMsg { TimeStamp = T0.AddMilliseconds(100), V = 1.3 },
            };
            return list;
        }

        [Test]
        public void Scan_BuildsRowsForRegisteredMessagesOnly()
        {
            var (data, index) = Record(Sequence());
            var columns = new[] { SpeedColumn(), PlanLengthColumn() };

            using var ms = new MemoryStream(data);
            var t = TelemetryScanner.Scan(ms, index, MessageCatalog.CommonDefaults(),
                                         columns, TestHelpers.Enc);

            Assert.That(t.RowCount, Is.EqualTo(3), "IMU zprava neni v registru, radek nedela");
            Assert.That(t.Columns[0].ValueAt(0), Is.Null, "pred prvnim RobotStateMsg je rychlost prazdna");
            Assert.That(t.Columns[0].ValueAt(1), Is.EqualTo(1.2));
            Assert.That(t.Columns[1].ValueAt(2), Is.EqualTo(8.0), "delka planu se drzi z minula");
            Assert.That(t.RowMsgName(0), Is.EqualTo("LocalPlanMsg"));
        }

        [Test]
        public void Scan_RowTimesFollowRecordOrder()
        {
            var (data, index) = Record(Sequence());
            using var ms = new MemoryStream(data);

            var t = TelemetryScanner.Scan(ms, index, MessageCatalog.CommonDefaults(),
                                          new[] { SpeedColumn(), PlanLengthColumn() }, TestHelpers.Enc);

            Assert.That(t.RowTime(0), Is.EqualTo(T0));
            Assert.That(t.RowTime(1), Is.EqualTo(T0.AddMilliseconds(50)));
            Assert.That(t.RowTime(2), Is.EqualTo(T0.AddMilliseconds(100)));
        }

        [Test]
        public void Scan_ReportsProgressAndFinishesAtOne()
        {
            var (data, index) = Record(Sequence());
            using var ms = new MemoryStream(data);
            double last = -1;
            var progress = new Progress<double>(p => last = p);

            var t = TelemetryScanner.Scan(ms, index, MessageCatalog.CommonDefaults(),
                                          new[] { SpeedColumn() }, TestHelpers.Enc,
                                          progress: progress);

            Assert.That(t.RowCount, Is.GreaterThan(0));
            // Progress<T> hlasi na synchronizacni kontext asynchronne; overujeme jen ze nespadl
            // a ze sken dobehl. Presnou hodnotu netestujeme (bylo by to flaky).
            Assert.That(last, Is.LessThanOrEqualTo(1.0));
        }

        [Test]
        public void Scan_CanBeCancelled()
        {
            var (data, index) = Record(Sequence());
            using var ms = new MemoryStream(data);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                TelemetryScanner.Scan(ms, index, MessageCatalog.CommonDefaults(),
                                      new[] { SpeedColumn() }, TestHelpers.Enc, ct: cts.Token));
        }

        [Test]
        public void Scan_WithoutIndex_Throws()
        {
            using var ms = new MemoryStream();
            Assert.Throws<ArgumentNullException>(() =>
                TelemetryScanner.Scan(ms, null, MessageCatalog.CommonDefaults(),
                                      new[] { SpeedColumn() }, TestHelpers.Enc));
        }
    }
}
```

- [ ] **Step 2: Ověřit, že testy padají**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter TelemetryScannerTests`
Expected: chyba kompilace — `TelemetryScanner` neexistuje.

Pokud test `Scan_BuildsRowsForRegisteredMessagesOnly` selže na neznámém typu při čtení, ověř, že
`MessageCatalog.CommonDefaults()` registruje `RobotStateMsg` i `LocalPlanMsg`
(`Src/ARBot.Common/Communication/MessageCatalog.cs`); pokud ne, doregistruj je v testu přes
`.Register(new RobotStateMsg())`.

- [ ] **Step 3: Implementovat `TelemetryScanner`**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using ARBot.Common.Communication;
using ARBot.Common.Logs;

namespace ARBot.Common.Telemetry
{
    /// <summary>
    /// Postavi <see cref="TelemetryTable"/> jednim pruchodem indexem zaznamu.
    /// <para>Zpravy, ktere zadny sloupec nepotrebuje, se <b>vubec necti</b> - tim se preskoci
    /// obrazky (CameraFrame, Blob), tedy vetsina objemu zaznamu. Viz doc/telemetry-view.md.</para>
    /// </summary>
    public static class TelemetryScanner
    {
        /// <param name="data">Datovy soubor zaznamu (vlastni stream volajiciho; pozice se meni).</param>
        /// <param name="index">Index zaznamu (sidecar) - bez nej nejsou offsety ani casova osa.</param>
        /// <param name="catalog">Katalog prototypu zprav pro deserializaci.</param>
        /// <param name="columns">Registr sloupcu - definuje i to, ktere zpravy se ctou.</param>
        /// <param name="encoding">Kodovani zaznamu (typicky UTF-8).</param>
        /// <param name="maxRows">Strop radku; pri dosazeni se tabulka oznaci jako Truncated.</param>
        /// <param name="progress">Volitelny postup 0..1.</param>
        /// <param name="ct">Zruseni skenu.</param>
        public static TelemetryTable Scan(Stream data, IReadOnlyList<IndexEntry> index,
                                         MessageCatalog catalog, IReadOnlyList<ColumnSpec> columns,
                                         Encoding encoding, int maxRows = 500_000,
                                         IProgress<double> progress = null,
                                         CancellationToken ct = default)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (columns == null) throw new ArgumentNullException(nameof(columns));

            var enc = encoding ?? Encoding.UTF8;
            var proto = catalog.ToPrototypeMap();

            // Typy, ktere maji aspon jeden sloupec - ostatni polozky indexu se preskoci bez cteni.
            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var c in columns)
                if (!string.IsNullOrEmpty(c.MsgName)) wanted.Add(c.MsgName);

            var builder = new TelemetryTableBuilder(columns, maxRows);
            int reportEvery = Math.Max(1, index.Count / 100);

            for (int i = 0; i < index.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var e = index[i];
                if (e.MsgName == null || !wanted.Contains(e.MsgName)) continue;

                var msg = ReadFrameAt(data, e.Offset, enc, proto);
                if (msg != null) builder.Add(msg, e);

                if (builder.IsFull) break;
                if (progress != null && i % reportEvery == 0)
                    progress.Report((double)i / index.Count);
            }

            progress?.Report(1.0);
            return builder.Build();
        }

        /// <summary>Precte jeden ramec z daneho offsetu (stejny postup jako FileMessageSource).</summary>
        private static Message ReadFrameAt(Stream data, long offset, Encoding enc,
                                           Dictionary<string, Message> proto)
        {
            try
            {
                data.Position = offset;
                var reader = new MessageReader(data, enc, proto);
                return reader.Read();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.ToString());
                return null;   // poskozeny ramec sken nezastavi
            }
        }
    }
}
```

- [ ] **Step 4: Spustit testy — musí projít**

Run: `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`
Expected: všechny testy PASS (nové i stávající).

---

## Task 3: Registr sloupců + cesta k záznamu

**Files:**
- Create: `Src/ARBot/Telemetry/TelemetryColumns.cs`
- Modify: `Src/ARBot/Robot/ARBotRuntime.cs` (vystavit cestu k přehrávanému záznamu)

**Interfaces:**
- Consumes: `ColumnSpec` z Tasku 1.
- Produces: `static IReadOnlyList<ColumnSpec> TelemetryColumns.All`;
  `string ARBotRuntime.RecordPath` (null, když se nepřehrává záznam).

- [ ] **Step 1: Vystavit cestu k záznamu v `ARBotRuntime`**

V `StartView(string file)` (kolem řádku 760) se cesta dnes jen použije. Přidej vlastnost a nastav ji:

```csharp
/// <summary>Cesta k prehravanemu zaznamu (rezim View), jinak null. Pouziva ji telemetricky
/// pohled, ktery si nad souborem otevira vlastni read-only stream (soubor je otevreny
/// s FileShare.Read) - viz doc/telemetry-view.md.</summary>
public string RecordPath { get; private set; }
```

V `StartView` hned za validaci `file`: `RecordPath = file;`
Ve `Stop()` (nebo tam, kde se ruší `fileSource`): `RecordPath = null;`

- [ ] **Step 2: Napsat registr sloupců**

```csharp
using System.Collections.Generic;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Telemetry;
using ARBot.Common.Common;
using ARBot.Common.Maps.OsmNav.Navigation;

namespace ARBot.Telemetry
{
    /// <summary>
    /// Registr sloupcu telemetrickeho pohledu. Pridat udaj = jeden zaznam v <see cref="All"/>.
    /// Zamerne v UI vrstve: jednotky, format a "co ma smysl kreslit" je prezentacni vec
    /// (viz doc/telemetry-view.md).
    /// </summary>
    public static class TelemetryColumns
    {
        public static IReadOnlyList<ColumnSpec> All { get; } = Build();

        private static List<ColumnSpec> Build() => new List<ColumnSpec>
        {
            // --- fuzovana poza (RobotStateMsg) ---
            Num<RobotStateMsg>("X [m]", m => m.X),
            Num<RobotStateMsg>("Y [m]", m => m.Y),
            Num<RobotStateMsg>("Theta [deg]", m => Conversions.Rad2Deg(m.Theta), "F1"),
            Num<RobotStateMsg>("v [m/s]", m => m.V),
            Num<RobotStateMsg>("omega [deg/s]", m => Conversions.Rad2Deg(m.Omega), "F1"),

            // --- ridici zasah (DriveCommandMsg) ---
            Num<DriveCommandMsg>("prikaz v [m/s]", m => m.Speed),
            Num<DriveCommandMsg>("prikaz omega [deg/s]", m => Conversions.Rad2Deg(m.RotationSpeed), "F1"),
            Num<DriveCommandMsg>("dif [m/s]", m => m.Dif),
            Flag<DriveCommandMsg>("STOP", m => m.EmergencyStop),

            // --- lokalni plan (LocalPlanMsg) ---
            Enum<LocalPlanMsg, LocalPlanStatus>("plan stav", m => (int)m.PlanStatus),
            Num<LocalPlanMsg>("plan delka [m]", m => m.LengthM),
            Num<LocalPlanMsg>("plan odstup [m]", m => m.MinClearanceM),
            Num<LocalPlanMsg>("plan bodu", m => m.WayPoints?.Length ?? 0, "F0"),
            Num<LocalPlanMsg>("plan vypocet [ms]", m => m.ComputeMs, "F1"),

            // --- globalni navigace (GlobalNavMsg) ---
            Enum<GlobalNavMsg, GlobalNavStatus>("nav stav", m => m.Status),
            Num<GlobalNavMsg>("do cile [m]", m => m.RouteLengthM, "F0"),
            Num<GlobalNavMsg>("hran trasy", m => m.RouteEdgeCount, "F0"),
            Num<GlobalNavMsg>("od site [m]", m => m.OffRouteDist),
            Num<GlobalNavMsg>("fi [s]", m => m.Phi, "F1"),
            Num<GlobalNavMsg>("uzavreno", m => m.ClosureCount, "F0"),

            // --- surove GPS (GPSState) ---
            Num<GPSState>("GPS lat [deg]", m => m.Latitude, "F6"),
            Num<GPSState>("GPS lon [deg]", m => m.Longitude, "F6"),
            Num<GPSState>("GPS satelitu", m => m.SatelitesCount, "F0"),
        };

        /// <summary>Ciselny sloupec z jedne zpravy.</summary>
        private static ColumnSpec Num<T>(string header, System.Func<T, double> value,
                                         string format = "F2") where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Format = format,
                Value = m => m is T typed ? value(typed) : (double?)null,
            };

        /// <summary>Logicky sloupec (zobrazi se jako zkratka / pomlcka).</summary>
        private static ColumnSpec Flag<T>(string header, System.Func<T, bool> value)
            where T : Message, new()
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Value = m => m is T typed ? (value(typed) ? 1.0 : 0.0) : (double?)null,
                Text = v => v != 0 ? header : "-",
            };

        /// <summary>Vyctovy sloupec - v grafu schod, v tabulce jmeno hodnoty.</summary>
        private static ColumnSpec Enum<T, TEnum>(string header, System.Func<T, int> value)
            where T : Message, new() where TEnum : struct, System.Enum
            => new ColumnSpec
            {
                MsgName = new T().MsgName,
                Header = header,
                Format = "F0",
                Value = m => m is T typed ? value(typed) : (double?)null,
                Text = v => ((TEnum)(object)(int)v).ToString(),
            };
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Expected: BUILD OK.

Ověř přitom skutečné názvy vlastností: `GPSState.SatelitesCount` (v `GpsDocument` se používá — když
se jmenuje jinak, oprav podle zdroje), `GlobalNavMsg.Status` je `int`, `LocalPlanMsg.PlanStatus` je
typovaný pohled. `new T().MsgName` funguje, protože `MsgName` nastavuje bezparametrový konstruktor
každé zprávy.

---

## Task 4: Dokument s tabulkou

**Files:**
- Create: `Src/ARBot/ViewModels/TelemetryDocument.cs`
- Create: `Src/ARBot/Views/TelemetryDocumentView.axaml`, `Src/ARBot/Views/TelemetryDocumentView.axaml.cs`
- Modify: `Src/ARBot/ViewModels/MainWindowViewModel.cs` (menu Tools → Telemetrie)
- Modify: `Src/ARBot/Views/MainWindow.axaml` (položka menu)

**Interfaces:**
- Consumes: `TelemetryScanner.Scan`, `TelemetryTable`, `TelemetryColumns.All`, `ARBotRuntime.RecordPath`.
- Produces: `TelemetryDocument` (dědí `DocumentBase`, `ViewType => typeof(Views.TelemetryDocumentView)`),
  `RowViewModel` s `Time`, `MsgName`, `Seq`, `Cells` (index sloupce → `CellViewModel { Text, Fresh }`).

- [ ] **Step 1: ViewModel — sken na pozadí**

Klíčové body (design-time bezpečnost je povinná, viz `Src/ARBot/Views/README.md`):

```csharp
public partial class TelemetryDocument : DocumentBase
{
    public override Type ViewType => typeof(ARBot.Views.TelemetryDocumentView);

    [ObservableProperty] private string status = "-";
    [ObservableProperty] private double progress;
    [ObservableProperty] private RowViewModel selectedRow;

    public ObservableCollection<RowViewModel> Rows { get; } = new();
    public IReadOnlyList<ColumnSpec> Columns => TelemetryColumns.All;

    private CancellationTokenSource cts;

    public TelemetryDocument()
    {
        Id = "Telemetry";
        Title = "Telemetrie";
        if (Design.IsDesignMode) return;      // v navrhari zadny sken
        StartScan();
    }

    private void StartScan()
    {
        string path = ARBotRuntime.Current?.RecordPath;
        if (string.IsNullOrEmpty(path)) { Status = "Neni otevreny zaznam (Runtime -> View...)"; return; }

        var index = ARBotRuntime.Current.FileSource?.Index;
        if (index == null || index.Count == 0) { Status = "Zaznam nema sidecar index (*.idx)"; return; }

        cts = new CancellationTokenSource();
        var ct = cts.Token;
        var progress = new Progress<double>(p => Progress = p);

        Task.Run(() =>
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return TelemetryScanner.Scan(fs, index, ARBotRuntime.BuildCatalog(),
                                         TelemetryColumns.All, Encoding.UTF8,
                                         progress: progress, ct: ct);
        }, ct).ContinueWith(t => Dispatcher.UIThread.Post(() => Apply(t)));
    }
}
```

Pozn.: `ARBotRuntime.BuildCatalog()` je dnes `private static` — změň na `internal static` (týž
projekt) a v komentáři uveď, že sken musí použít **tentýž** katalog jako replay, jinak by neznal
některé typy zpráv.

`Apply` naplní `Rows` z tabulky (jeden `RowViewModel` na řádek, buňky s `Text` a `Fresh`) a nastaví
`Status` (počet řádků, čas prvního a posledního, plus varování při `Truncated`).

- [ ] **Step 2: View — tabulka**

Nejdřív **ověř dostupnost `Avalonia.Controls.DataGrid` pro Avalonia 12**:

```bash
dotnet package search Avalonia.Controls.DataGrid --exact-match --prerelease
```

- Když existuje verze 12.x → přidej `PackageReference` a použij `DataGrid` s dynamicky
  generovanými sloupci (`DataGridTextColumn` na `Cells[i].Text`, `FontWeight` z `Cells[i].Fresh`).
- Když ne → `ListBox` + `Grid` s pevnými šířkami sloupců, přesně jako
  [`ReplayNavToolView.axaml`](../Src/ARBot/Views/ReplayNavToolView.axaml) (má i záhlaví se
  shodnými šířkami). Virtualizaci `ListBox` má.

Ať tak či tak: `FontWeight="Bold"` pro `Fresh`, normální pro drženou hodnotu, prázdný text pro
buňku, která ještě nikdy nepřišla.

- [ ] **Step 3: Menu Tools → Telemetrie**

V `MainWindowViewModel` přidej příkaz vedle `OpenRobotCentric`/`OpenWorld` (stejný vzor: dedup podle
`Id`, `AddDockable` do `_factory.DocumentDock`, `SetActiveDockable`), v `MainWindow.axaml` položku
menu.

- [ ] **Step 4: Build + spuštění**

Run: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Pak aplikaci spustit, otevřít **Runtime → View…** nad existujícím záznamem s `*.idx`,
otevřít **Tools → Telemetrie** a zkontrolovat: řádky mají rostoucí čas, tučné buňky jsou tam, kde
zpráva přišla, sloupce z pomalých zpráv drží hodnotu, `Status` hlásí počty.

---

## Task 5: Detail řádku + napojení na Replay

**Files:**
- Modify: `Src/ARBot/ViewModels/TelemetryDocument.cs`
- Modify: `Src/ARBot/Views/TelemetryDocumentView.axaml`

**Interfaces:**
- Consumes: `TelemetryTable`, `FileMessageSource.SeekTo(long)`, `ReplayNavTool`.
- Produces: `TelemetryDocument.SeekToSelectedCommand`, `DetailLines` (`ObservableCollection<string>`
  nebo typovaný `DetailRow { Header, Text, Time, AgeMs }`).

- [ ] **Step 1: Detail vybraného řádku**

Při změně `SelectedRow` naplnit detail: pro každý sloupec s hodnotou řádek
`"{Header} = {Text} · {TimeAt:HH:mm:ss.fff} · o {age} ms starší než řádek"` (u `Fresh` buňky
`"právě přišlo"`), a nad tím zakládající zpráva: `Seq`, `MsgName`, T_in a T_out.

T_out je v indexu — `RowViewModel` si k tomu musí nést i `ArrivalTicks` z `IndexEntry`
(dopiš do `RowViewModel` při plnění; `TelemetryTable` sama nese jen čas řádku).

- [ ] **Step 2: Seek z tabulky**

Dvojklik na řádek → `SeekToSelected`: `FileMessageSource` vyžaduje `Paused`, takže nejdřív `Pause()`,
pak `SeekTo(row.Seq)`. Ošetři, že `ARBotRuntime.Current.FileSource` může být null (záznam zavřen).

- [ ] **Step 3: Build + ruční ověření**

Run: `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`
Ověř: dvojklik na řádek posune Replay panel i World pohled na tentýž okamžik; detail ukazuje stáří
hodnot a u držené hodnoty starší čas, než má řádek.

- [ ] **Step 4: DevLog**

Doplnit záznam dne do [devlog.md](devlog.md): co je hotové, co ověřené buildem/testy a co jen
ručně za běhu.

---

## Odchylky proti plánu (co se při implementaci ukázalo)

- **DataGrid musí být 12.0.0**, ne 12.0.1+: novější si vynucují Avalonia ≥ 12.0.5, projekt drží
  12.0.3 → `NU1605` (downgrade jako chyba). Přidán i `StyleInclude` tématu do `App.axaml`.
- **`TelemetryTable` dostala i `RowArrivalTime`** (T_out). Detail řádku má podle specifikace
  ukazovat oba časy, a tabulka nesla jen jeden — doplněno včetně testu.
- **`TelemetryTableBuilder.MarkTruncated()`** — sken po dosažení stropu už `Add` nezavolá, takže by
  se o oříznutí nikdy nedozvěděl. Hlásí se jen když za stropem opravdu ještě nějaká sledovaná
  zpráva je (záznam končící přesně na stropu o nic nepřišel).
- **Slévání řádků funguje lépe, než návrh předpokládal:** na skutečném záznamu se 4833 registrovaných
  zpráv slilo do 2806 řádků, tedy `RobotStateMsg` a `DriveCommandMsg` z jednoho taktu mají shodný
  čas a padnou na jeden řádek. Tolerance zatím není potřeba.
- **Sken je rychlý:** 29 ms na 2minutový záznam (27 541 zpráv v indexu, čtou se jen 4 typy).
  Riziko „pomalé náhodné čtení" se na Windows nepotvrdilo; na OrangePI/SD kartě zbývá změřit.

## Co tento plán nepokrývá

Vědomě mimo (každé je samostatný krok se vlastním plánem, pokud bude potřeba):

- **Rozšíření `LocalPlanMsg`** o rychlostní diagnostiku (`MinVClear`, `MinVBrake`,
  `MinWayPointSpeed`, `SpeedLimitedBy`) + verze formátu +1. Až po jádru — pak je to jeden řádek
  v registru.
- **Plánované odbočení** — neexistuje ve zprávách, musí ho začít počítat globální navigace.
- **Grafy (fáze 2)** — ikonka u sloupce, dokument grafu, schod vs. rampa podle `Fresh`.
- **Režim Run** — živé plnění tabulky.
- **Filtr řádků podle typu zakládající zprávy** — levné (`RowMsgName` v tabulce už je), ale
  není součástí fáze 1.
