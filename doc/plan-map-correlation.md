# Korelace occupancy gridu s mapou — implementační plán (fáze 1–3)

> **Pro agentní workery:** POVINNÝ SUB-SKILL: `superpowers:subagent-driven-development` (doporučeno)
> nebo `superpowers:executing-plans`. Kroky jsou zaškrtávací (`- [ ]`).

> **Stav (2026-08-19): plán je DOKONČENÝ a jeho výpisy kódu jsou historický snímek z doby psaní.**
> Skutečnost drží kód a [map-correlation-localization.md](map-correlation-localization.md); po
> exekuci a finální review se několik míst změnilo (marže rastru, referenční skóre nejednoznačnosti,
> per-osové příznaky ve zprávě). Kde se plán a kód rozcházejí, platí kód.
>
> **Přejmenování (20. 8. 2026):** `MapCorrelatorConfig.Enabled` se jmenuje **`SendCorrections`** —
> výpisy níž nesou staré jméno záměrně, je to snímek. Zároveň přibyl parametr příkazové řádky
> `mapcorr=` (default `false`), který rozhoduje, jestli se korelátor **vůbec zakládá**; ten v plánu
> není vůbec.

**Spec:** [doc/map-correlation-localization.md](map-correlation-localization.md) — plán z ní vychází,
čti obojí. Argumentace „proč" je ve specifikaci, tady je „jak".

**Cíl:** Naměřit chybu polohy a kurzu z posunu mezi semantickým kanálem occupancy gridu (`LRoad`)
a vozovkou podle OSM mapy, a poslat ji do EKF jako dvě skalární osová měření plus kurz.

**Architektura:** `MapCorrelator` je samostatný `MessageProcessor` nad `OccupancyGridMsg` (vlastní
vlákno, `DropOldest`). Cyklus: póza z fúze → rastr mapy → důkazní oblak z gridu → hrubě-jemné
skenování `(dx, dy, φ)` → kovariance ze zakřivení skóre → rozhodnutí poslat/mlčet → měření do fúze
a zpráva do telemetrie. Nic z toho nezná trasu ani vybranou hranu.

**Technologie:** .NET 10, C#, MathNet.Numerics (lineární algebra), NUnit 4 (`Assert.That`).

## Globální omezení (platí pro každý krok)

- **Build i testy vždy pro konkrétní platformu:** `dotnet build <proj> -p:Platform=x64`,
  `dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64`. Nikdy `AnyCPU`.
- **Jazyk: čeština.** Komentáře v `Src/**` **bez diakritiky** (konvence okolních souborů),
  `doc/**` s diakritikou.
- **Commity nejsou součástí kroků.** Commituje autor na vlastní pokyn (CLAUDE.md). Každý krok končí
  zeleným buildem a testy; krok „Commit" v tomto plánu **neexistuje**.
- **Nemazat starou implementaci, dokud novou nepotvrdí testy.** Konkrétně: `Navigations/MapCorelator.cs`
  a `Navigations/PathMapCorelator.cs` (stará generace) se **nemažou ani nepřejmenovávají**.
- **Směr závislostí:** `ARBot.Common` nesmí vidět UI. `Localization` → `Occupancy` (jen zpráva),
  `Maps/OsmNav`, `Fusion`. Doménově na `Localization` nezávisí nikdo; jedinou výjimkou je registr telemetrických sloupců v `ARBot`, který si z něj bere výčet `MapCorrelationReason` (stejně jako u `GlobalNavStatus`).
- **Konvence zpráv:** doménový objekt si vyrábí zprávu metodou `ToLogMessage()`. Nezakládat
  `XxxMsg.FromDomain(...)`.
- **Souřadnice:** world ENU, matematická orientace (0 = východ, +CCW).
- **DevLog:** na konci celku doplnit záznam do [devlog.md](devlog.md).

## Struktura souborů

| Soubor | Odpovědnost |
|---|---|
| `Src/ARBot.Common/Maps/OsmNav/Graph/RoadScene.cs` | **přesun** z `Vision/Synthetic` — mapová pravda `IsRoad` |
| `Src/ARBot.Common/Localization/RoadRaster.cs` | rastr `IsRoad` zarovnaný s gridem (bitové pole) |
| `Src/ARBot.Common/Localization/MapCorrelatorConfig.cs` | prahy, rozsahy, úrovně skenování, `Validate()` |
| `Src/ARBot.Common/Localization/EvidenceCloud.cs` | důkazní buňky `(x, y, w)` vytažené ze zprávy gridu |
| `Src/ARBot.Common/Localization/CorrelationScorer.cs` | skóre jednoho kandidáta + hrubě-jemný sken |
| `Src/ARBot.Common/Localization/CorrelationCovariance.cs` | σ ze zakřivení skóre + vlastní osy |
| `Src/ARBot.Common/Localization/MapCorrelationResult.cs` | výsledek + rozhodnutí poslat/mlčet + `ToLogMessage()` |
| `Src/ARBot.Common/Localization/MapCorrelator.cs` | `MessageProcessor`: celý cyklus |
| `Src/ARBot.Common/Logs/MapCorrelationMsg.cs` | zpráva pro telemetrii a záznam |
| `Src/ARBot.Common/Fusion/Measurements.cs` | **rozšíření**: `AxisOffsetMeasurement` |
| `Src/ARBot.Common/Communication/MessageCatalog.cs` | **rozšíření**: registrace zprávy |
| `Src/ARBot.Common/Occupancy/LocalNavigator.cs` | **rozšíření**: zahození gridu při skoku pózy |
| `Src/ARBot/Robot/ARBotRuntime.cs` | **rozšíření**: zapojení korelátoru do pipeline |
| `Src/ARBot/Telemetry/TelemetryColumns.cs` | **rozšíření**: sloupce nové zprávy |

Testy zrcadlově v `Src/ARBot.Common.Tests/Localization/`, `.../Fusion/`, `.../Occupancy/`.

---

## Task 1: Přesun `RoadScene` do `Maps/OsmNav/Graph`

Mechanická změna: `RoadScene` má teď dva nezávislé konzumenty (virtuální kamera a lokalizace)
a „lokalizace závisí na `Vision.Synthetic`" je špatná zpráva o architektuře. Správnost hlídá
kompilátor a existující `RoadSceneTests`.

**Files:**
- Move: `Src/ARBot.Common/Vision/Synthetic/RoadScene.cs` → `Src/ARBot.Common/Maps/OsmNav/Graph/RoadScene.cs`
- Move: `Src/ARBot.Common.Tests/Vision/Synthetic/RoadSceneTests.cs` → `Src/ARBot.Common.Tests/OsmNav.Tests/Graph/RoadSceneTests.cs`
- Modify (usings): `Src/ARBot.Common/Vision/Synthetic/SyntheticFrameRenderer.cs`,
  `Src/ARBot.Common/Vision/Synthetic/SyntheticSceneOptions.cs`,
  `Src/ARBot.Common.Tests/Vision/Synthetic/SyntheticFrameRendererTests.cs`,
  `Src/ARBot.Common.Tests/Vision/Synthetic/SyntheticSceneTraversabilityTests.cs`,
  `Src/ARBot.HAL/Devices/Camera/VirtualCamera.cs`,
  `Src/ARBot.HAL.Tests/VirtualCameraTest.cs`, `Src/ARBot.HAL.Tests/VirtualHwOccupancyTest.cs`,
  `Src/ARBot/Robot/ARBotHW.cs`, `Src/ARBot/Robot/VirtualHWOptions.cs`

**Interfaces:**
- Consumes: nic (první task).
- Produces: `ARBot.Common.Maps.OsmNav.Graph.RoadScene` s nezměněným API —
  `RoadScene(RoadNetwork network, GeoReference origin)`, `bool IsRoad(double x, double y)`.

- [x] **Krok 1: Ověř, že testy `RoadScene` teď procházejí** (základní linie před přesunem)

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~RoadSceneTests
```

Očekávané: PASS. Zapiš si počet testů — po přesunu musí být stejný.

- [x] **Krok 2: Přesuň soubor a změň namespace**

```bash
git mv Src/ARBot.Common/Vision/Synthetic/RoadScene.cs Src/ARBot.Common/Maps/OsmNav/Graph/RoadScene.cs
```

V přesunutém souboru změň deklaraci namespace a zbytné `using`:

```csharp
using System;
using System.Collections.Generic;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Maps.OsmNav.Graph
{
```

(`using ARBot.Common.Maps.OsmNav.Graph;` se odstraní — je to teď vlastní namespace.)
V XML dokumentaci třídy přepiš odkaz na dokumentaci:

```csharp
    /// Geometrie vozovky z OsmNav site v lokalni ENU rovine: sjednoceni kapsli kolem os hran
    /// (polosirka se interpoluje mezi uzly). Mapova "pravda" pro dva nezavisle konzumenty -
    /// virtualni kameru (doc/virtual-hw.md) a korelaci s mapou (doc/map-correlation-localization.md).
```

- [x] **Krok 3: Přesuň testy**

```bash
git mv Src/ARBot.Common.Tests/Vision/Synthetic/RoadSceneTests.cs Src/ARBot.Common.Tests/OsmNav.Tests/Graph/RoadSceneTests.cs
```

V testu změň namespace a odstraň nepotřebný `using`:

```csharp
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.OsmNav.Graph;
```

Pozor na ten namespace: složka se jmenuje `OsmNav.Tests`, ale **segment `.Tests` se v namespace
zahazuje** — všech 26 souborů pod `OsmNav.Tests/*` používá `ARBot.Common.Tests.OsmNav.<Podsložka>`
(např. `EdgeTests.cs` a `RoadNetworkTests.cs` v témže `Graph/`). Jinak by v jedné fyzické složce
byly dva různé namespace.

- [x] **Krok 4: Přelož celé řešení a nech kompilátor najít zbylá místa**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
```

Očekávané: chyby `CS0246: The type or namespace name 'RoadScene' could not be found` v souborech
vyjmenovaných výše. V každém z nich přidej `using ARBot.Common.Maps.OsmNav.Graph;` (většina ho už
má kvůli `RoadNetwork` — tam nebude chyba vůbec). Opakuj build, dokud není zelený.

- [x] **Krok 5: Ověř, že se nic neztratilo**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~RoadSceneTests
```

Očekávané: PASS, **stejný počet testů jako v kroku 1**.

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
```

Očekávané: zelený build celého řešení (včetně `ARBot.HAL` a `ARBot` — konzumenti virtuální kamery).

---

## Task 2: `RoadRaster` — rastr mapy zarovnaný s gridem

Kandidátů skenování jsou stovky a každý se ptá na tisíce buněk. Prostorový dotaz `IsRoad` by to
neutáhl, proto se mapa jednou za cyklus vyhodnotí do bitového pole se **stejným rozlišením
a stejným zarovnáním** jako occupancy grid, rozšířeného o marži.

**Files:**
- Create: `Src/ARBot.Common/Localization/RoadRaster.cs`
- Test: `Src/ARBot.Common.Tests/Localization/RoadRasterTests.cs`

**Interfaces:**
- Consumes: `RoadScene.IsRoad(double, double)` z Tasku 1.
- Produces:
  - `RoadRaster.Build(RoadScene scene, int gridOriginX, int gridOriginY, int gridSize, double resolution, double marginM)` → `RoadRaster`
  - `bool RoadRaster.TryIsRoad(double worldX, double worldY, out bool isRoad)` — `false` = mimo rastr
  - `int RoadRaster.Size { get; }`, `int RoadRaster.OriginX { get; }`, `int RoadRaster.OriginY { get; }`

- [x] **Krok 1: Napiš padající testy**

Vytvoř `Src/ARBot.Common.Tests/Localization/RoadRasterTests.cs`:

```csharp
using ARBot.Common.Coordinates;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy rastru mapy pro korelaci (viz doc/map-correlation-localization.md).
/// Rastr ma stejne rozliseni i zarovnani jako occupancy grid, jen je o marzi vetsi.
/// </summary>
public class RoadRasterTests
{
    private static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Sit s jedinou hranou z pocatku 20 m na vychod, sirka 4 m (polosirka 2 m).</summary>
    private static RoadNetwork StraightEastRoad(GeoReference origin)
    {
        var a = new Node(1, origin.ToLLA(0, 0), 4.0);
        var b = new Node(2, origin.ToLLA(20, 0), 4.0);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 20.0, wayId: 1, traversalCost: 20.0);
        return builder.Build();
    }

    private static RoadRaster Build(double marginM = 3.0)
    {
        var origin = Origin();
        var scene = new RoadScene(StraightEastRoad(origin), origin);
        // Grid 256 bunek po 5 cm se stredem v (6,4 ; 0) => origin (0, -128).
        return RoadRaster.Build(scene, gridOriginX: 0, gridOriginY: -128,
                                gridSize: 256, resolution: 0.05, marginM: marginM);
    }

    [Test]
    public void Build_RozsiriRastrOMarziNaObeStrany()
    {
        var raster = Build(marginM: 3.0);

        // 3 m pri 5 cm = 60 bunek na kazdou stranu.
        Assert.That(raster.Size, Is.EqualTo(256 + 120));
        Assert.That(raster.OriginX, Is.EqualTo(-60));
        Assert.That(raster.OriginY, Is.EqualTo(-128 - 60));
    }

    [Test]
    public void TryIsRoad_NaOseVozovky_JeCesta()
    {
        var raster = Build();

        Assert.That(raster.TryIsRoad(10.0, 0.0, out bool isRoad), Is.True);
        Assert.That(isRoad, Is.True);
    }

    [Test]
    public void TryIsRoad_ZaPolosirkou_NeniCesta()
    {
        var raster = Build();

        Assert.That(raster.TryIsRoad(10.0, 3.0, out bool isRoad), Is.True);
        Assert.That(isRoad, Is.False);
    }

    [Test]
    public void TryIsRoad_MimoRastr_VraciFalse()
    {
        var raster = Build();

        // Daleko na zapad, mimo grid i marzi.
        Assert.That(raster.TryIsRoad(-50.0, 0.0, out _), Is.False);
    }

    [Test]
    public void TryIsRoad_SouhlasiSeScenouNaCelemRastru()
    {
        var origin = Origin();
        var scene = new RoadScene(StraightEastRoad(origin), origin);
        var raster = RoadRaster.Build(scene, 0, -128, 256, 0.05, 3.0);

        // Rastr nesmi byt jen "priblizne" scena - na strednich bodech bunek musi souhlasit presne.
        int checked_ = 0;
        for (int j = 0; j < raster.Size; j += 7)
        {
            for (int i = 0; i < raster.Size; i += 7)
            {
                double x = (raster.OriginX + i + 0.5) * 0.05;
                double y = (raster.OriginY + j + 0.5) * 0.05;
                Assert.That(raster.TryIsRoad(x, y, out bool isRoad), Is.True);
                Assert.That(isRoad, Is.EqualTo(scene.IsRoad(x, y)),
                            $"Rozpor v bunce ({i},{j}) = svet ({x:F3},{y:F3}).");
                checked_++;
            }
        }
        Assert.That(checked_, Is.GreaterThan(2000), "Test musi projit dost bunek, aby mel vypovidaci hodnotu.");
    }
}
```

- [x] **Krok 2: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~RoadRasterTests
```

Očekávané: chyba překladu `CS0246: The type or namespace name 'RoadRaster' could not be found`.

- [x] **Krok 3: Implementuj `RoadRaster`**

Vytvoř `Src/ARBot.Common/Localization/RoadRaster.cs`:

```csharp
using System;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Vozovka podle mapy predpocitana do bitoveho pole se STEJNYM rozlisenim a zarovnanim jako
    /// occupancy grid, rozsirena o marzi (aby kandidat skenovani nesahal mimo).
    /// Viz doc/map-correlation-localization.md.
    ///
    /// <para>Duvod existence: jeden cyklus korelace vyhodnoti stovky kandidatu a kazdy se pta na
    /// tisice bunek. Prostorovy dotaz <see cref="RoadScene.IsRoad"/> se proto zaplati JEDNOU za
    /// cyklus a dal se uz jen indexuje do pole.</para>
    ///
    /// <para><b>Mimo rastr neznamena "neni cesta".</b> <see cref="TryIsRoad"/> vraci <c>false</c>
    /// a volajici takovy dukaz PRESKOCI - jinak by okraj rastru systematicky tlacil odhad dovnitr.</para>
    /// </summary>
    public sealed class RoadRaster
    {
        private readonly byte[] bits;

        /// <summary>Pocet bunek na stranu.</summary>
        public int Size { get; }

        /// <summary>Velikost bunky [m] (stejna jako u gridu).</summary>
        public double Resolution { get; }

        /// <summary>Absolutni index nejzapadnejsiho sloupce rastru.</summary>
        public int OriginX { get; }

        /// <summary>Absolutni index nejjiznejsiho radku rastru.</summary>
        public int OriginY { get; }

        private RoadRaster(byte[] bits, int size, double resolution, int originX, int originY)
        {
            this.bits = bits;
            Size = size;
            Resolution = resolution;
            OriginX = originX;
            OriginY = originY;
        }

        /// <summary>
        /// Vyhodnoti <see cref="RoadScene.IsRoad"/> ve stredech bunek na oblasti gridu rozsirene
        /// o <paramref name="marginM"/> na kazdou stranu.
        /// </summary>
        /// <param name="scene">Mapova pravda.</param>
        /// <param name="gridOriginX">Absolutni index nejzapadnejsiho sloupce GRIDU.</param>
        /// <param name="gridOriginY">Absolutni index nejjiznejsiho radku GRIDU.</param>
        /// <param name="gridSize">Pocet bunek gridu na stranu.</param>
        /// <param name="resolution">Velikost bunky [m].</param>
        /// <param name="marginM">Marze za hranu gridu [m]; musi byt >= max. posun kandidata.</param>
        public static RoadRaster Build(RoadScene scene, int gridOriginX, int gridOriginY,
                                       int gridSize, double resolution, double marginM)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (gridSize <= 0) throw new ArgumentException("gridSize musi byt > 0.", nameof(gridSize));
            if (resolution <= 0) throw new ArgumentException("resolution musi byt > 0.", nameof(resolution));
            if (marginM < 0) throw new ArgumentException("marginM nesmi byt zaporna.", nameof(marginM));

            int margin = (int)Math.Ceiling(marginM / resolution);
            int size = gridSize + 2 * margin;
            int originX = gridOriginX - margin;
            int originY = gridOriginY - margin;

            var bits = new byte[(size * size + 7) / 8];
            for (int j = 0; j < size; j++)
            {
                double y = (originY + j + 0.5) * resolution;
                int rowBase = j * size;
                for (int i = 0; i < size; i++)
                {
                    double x = (originX + i + 0.5) * resolution;
                    if (!scene.IsRoad(x, y)) continue;
                    int bit = rowBase + i;
                    bits[bit >> 3] |= (byte)(1 << (bit & 7));
                }
            }
            return new RoadRaster(bits, size, resolution, originX, originY);
        }

        /// <summary>
        /// Rika mapa v tomto svetovem bode "cesta"? Vraci <c>false</c>, kdyz bod lezi MIMO rastr -
        /// pak je <paramref name="isRoad"/> bezvyznamny a dukaz se ma preskocit.
        /// </summary>
        public bool TryIsRoad(double worldX, double worldY, out bool isRoad)
        {
            int i = (int)Math.Floor(worldX / Resolution) - OriginX;
            int j = (int)Math.Floor(worldY / Resolution) - OriginY;
            if ((uint)i >= (uint)Size || (uint)j >= (uint)Size)
            {
                isRoad = false;
                return false;
            }
            int bit = i + j * Size;
            isRoad = (bits[bit >> 3] & (1 << (bit & 7))) != 0;
            return true;
        }
    }
}
```

- [x] **Krok 4: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~RoadRasterTests
```

Očekávané: PASS (5 testů).

---

## Task 3: `MapCorrelatorConfig` a `EvidenceCloud`

Konfigurace (včetně úrovní skenování) a vytažení důkazních buněk ze zprávy gridu. Obojí je pasivní
datová vrstva pro Tasky 4–6, proto v jednom kroku — samostatně by ani jedno nemělo testovatelný
přínos.

**Files:**
- Create: `Src/ARBot.Common/Localization/MapCorrelatorConfig.cs`
- Create: `Src/ARBot.Common/Localization/EvidenceCloud.cs`
- Test: `Src/ARBot.Common.Tests/Localization/EvidenceCloudTests.cs`
- Test: `Src/ARBot.Common.Tests/Localization/MapCorrelatorConfigTests.cs`

**Interfaces:**
- Consumes: `ARBot.Common.Logs.OccupancyGridMsg` (`Size`, `Resolution`, `OriginX`, `OriginY`,
  `Scale`, `Road`, `TimeStamp`, `CenterX(i)`, `CenterY(j)`).
- Produces:
  - `MapCorrelatorConfig` s poli: `Enabled`, `EvidenceThreshold`, `MinScore`, `AmbiguityMargin`,
    `AmbiguitySeparationM`, `MinEvidenceCells`, `Alpha`, `SigmaFloorM`, `SigmaFloorHeadingRad`,
    `SigmaCeilingM`, `SigmaCeilingHeadingRad`, `MaxOffsetM`, `MapRasterMarginM`, `MinPeriod`,
    `ScanLevel[] Levels`; metoda `void Validate()`; `double SearchRangeM { get; }`.
  - `ScanLevel` s poli `StepM`, `StepHeadingRad`, `HalfRangeM`, `HalfRangeHeadingRad`, `Stride`.
  - `EvidenceCloud.FromGrid(OccupancyGridMsg msg, float threshold)` → `EvidenceCloud`
    s `int Count`, `double[] X`, `double[] Y`, `float[] W`.

- [x] **Krok 1: Napiš padající testy konfigurace**

Vytvoř `Src/ARBot.Common.Tests/Localization/MapCorrelatorConfigTests.cs`:

```csharp
using System;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>Testy konfigurace korelatoru (viz doc/map-correlation-localization.md).</summary>
public class MapCorrelatorConfigTests
{
    [Test]
    public void Vychozi_JeVypnuty()
    {
        // Faze 3 zacina s Enabled = false - korelator pocita a hlasi, ale nekoriguje.
        Assert.That(new MapCorrelatorConfig().Enabled, Is.False);
    }

    [Test]
    public void Vychozi_ProjdeValidaci()
    {
        Assert.That(() => new MapCorrelatorConfig().Validate(), Throws.Nothing);
    }

    [Test]
    public void Vychozi_UrovneJdouOdHrubeKJemne()
    {
        var levels = new MapCorrelatorConfig().Levels;

        Assert.That(levels.Length, Is.EqualTo(3));
        for (int i = 1; i < levels.Length; i++)
        {
            Assert.That(levels[i].StepM, Is.LessThan(levels[i - 1].StepM), $"Uroven {i} neni jemnejsi.");
            Assert.That(levels[i].HalfRangeM, Is.LessThan(levels[i - 1].HalfRangeM),
                        $"Uroven {i} nema uzsi okno.");
        }
    }

    [Test]
    public void SearchRangeM_JePulokruhNejhrubsiUrovne()
    {
        var cfg = new MapCorrelatorConfig();

        Assert.That(cfg.SearchRangeM, Is.EqualTo(cfg.Levels[0].HalfRangeM));
    }

    [Test]
    public void Validate_MarzeRastruMensiNezRozsahHledani_Vyhodi()
    {
        // Kdyby byla marze mensi, kandidat by sahal mimo rastr a odhad by se tlacil dovnitr.
        var cfg = new MapCorrelatorConfig { MapRasterMarginM = 1.0 };

        Assert.That(() => cfg.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_NekladneAlfa_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { Alpha = 0 }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_DolniHraniceSigmaNadHorni_Vyhodi()
    {
        var cfg = new MapCorrelatorConfig { SigmaFloorM = 9.0, SigmaCeilingM = 5.0 };

        Assert.That(() => cfg.Validate(), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Vychozi_KrokHessianuJeHrubsiNezNejjemnejsiSken()
    {
        // Skore je kvuli rastru schodovite; na kroku skenu by druha derivace merila kvantizacni sum.
        var cfg = new MapCorrelatorConfig();

        Assert.That(cfg.HessianStepM, Is.GreaterThan(cfg.Levels[^1].StepM * 2));
        Assert.That(cfg.HessianStepHeadingRad, Is.GreaterThan(cfg.Levels[^1].StepHeadingRad * 2));
    }

    [Test]
    public void Validate_NekladnyKrokHessianu_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { HessianStepM = 0 }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Validate_ZadnaUroven_Vyhodi()
    {
        Assert.That(() => new MapCorrelatorConfig { Levels = new ScanLevel[0] }.Validate(),
                    Throws.TypeOf<ArgumentException>());
    }
}
```

- [x] **Krok 2: Napiš padající testy důkazního oblaku**

Vytvoř `Src/ARBot.Common.Tests/Localization/EvidenceCloudTests.cs`:

```csharp
using System;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy vytazeni dukaznich bunek ze zpravy gridu (viz doc/map-correlation-localization.md).
/// Konvence znamenka: LRoad kladne = "mimo cestu", zaporne = "cesta".
/// </summary>
public class EvidenceCloudTests
{
    /// <summary>Prazdna zprava gridu 8 x 8 po 0,5 m s pocatkem v (0,0).</summary>
    private static OccupancyGridMsg Grid()
        => new OccupancyGridMsg
        {
            Size = 8,
            Resolution = 0.5,
            OriginX = 0,
            OriginY = 0,
            Scale = 0.05f,
            BlockedThreshold = 1.0f,
            FreeThreshold = -1.0f,
            Occ = new sbyte[64],
            Road = new sbyte[64],
            TimeStamp = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
        };

    /// <summary>Zapise do bunky (i,j) log-odds hodnotu (prepocte se na fixed-point).</summary>
    private static void SetRoad(OccupancyGridMsg msg, int i, int j, float logOdds)
        => msg.Road[i + j * msg.Size] = (sbyte)Math.Round(logOdds / msg.Scale);

    [Test]
    public void FromGrid_SlabeBunkyVynecha()
    {
        var msg = Grid();
        SetRoad(msg, 1, 1, 0.2f);   // pod prahem
        SetRoad(msg, 2, 2, -0.2f);  // pod prahem (i v absolutni hodnote)

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(0));
    }

    [Test]
    public void FromGrid_VezmeObeZnamenka()
    {
        var msg = Grid();
        SetRoad(msg, 1, 1, 1.0f);   // mimo cestu
        SetRoad(msg, 2, 3, -1.0f);  // cesta

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(2));
    }

    [Test]
    public void FromGrid_SouradniceJsouStredyBunekVeSvete()
    {
        var msg = Grid();
        SetRoad(msg, 3, 2, -1.0f);

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(1));
        // Origin (0,0), 0,5 m bunka => stred bunky (3,2) je (1,75 ; 1,25).
        Assert.That(cloud.X[0], Is.EqualTo(1.75).Within(1e-9));
        Assert.That(cloud.Y[0], Is.EqualTo(1.25).Within(1e-9));
    }

    [Test]
    public void FromGrid_VahaJeLogOddsVcetneZnamenka()
    {
        var msg = Grid();
        SetRoad(msg, 1, 1, -1.0f);

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.W[0], Is.EqualTo(-1.0f).Within(0.03f));
    }

    [Test]
    public void FromGrid_IgnorujeKanalOcc()
    {
        var msg = Grid();
        msg.Occ[1 + 1 * msg.Size] = 100;  // silna geometricka prekazka

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        // Occ se korelace neucastni - jsou v nem veci, ktere v mape nejsou.
        Assert.That(cloud.Count, Is.EqualTo(0));
    }

    [Test]
    public void FromGrid_ChybejiciKanalRoad_DaPrazdnyOblak()
    {
        var msg = Grid();
        msg.Road = null;

        var cloud = EvidenceCloud.FromGrid(msg, threshold: 0.4f);

        Assert.That(cloud.Count, Is.EqualTo(0));
    }
}
```

- [x] **Krok 3: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~EvidenceCloudTests|FullyQualifiedName~MapCorrelatorConfigTests"
```

Očekávané: chyby překladu `CS0246` pro `MapCorrelatorConfig`, `ScanLevel`, `EvidenceCloud`.

- [x] **Krok 4: Implementuj `MapCorrelatorConfig`**

Vytvoř `Src/ARBot.Common/Localization/MapCorrelatorConfig.cs`:

```csharp
using System;

namespace ARBot.Common.Localization
{
    /// <summary>Jedna uroven hrube-jemneho skenovani (viz doc/map-correlation-localization.md).</summary>
    public sealed class ScanLevel
    {
        /// <summary>Krok posunu [m].</summary>
        public double StepM;

        /// <summary>Krok kurzu [rad].</summary>
        public double StepHeadingRad;

        /// <summary>Polovina okna posunu [m] (okolo stredu z predchozi urovne).</summary>
        public double HalfRangeM;

        /// <summary>Polovina okna kurzu [rad].</summary>
        public double HalfRangeHeadingRad;

        /// <summary>Podvzorkovani dukazu: bere se kazdy N-ty. 1 = vsechny.</summary>
        public int Stride = 1;
    }

    /// <summary>
    /// Konfigurace korelace occupancy gridu s mapou. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Vychozi hodnoty jsou odhad k naladeni nad zaznamy (faze 4), ne merena pravda.</b></para>
    /// </summary>
    public sealed class MapCorrelatorConfig
    {
        /// <summary>Posilat merenia do fuze? false = korelator jen pocita a hlasi zpravou.</summary>
        public bool Enabled = false;

        /// <summary>Absolutni hodnota LRoad, od ktere bunka vstupuje do korelace [log-odds].</summary>
        public float EvidenceThreshold = 0.4f;

        /// <summary>Pod timto skore korelator mlci (robot nejspis neni na mapovane ceste).</summary>
        public double MinScore = 0.25;

        /// <summary>O kolik musi byt maximum lepsi nez konkurent, aby shoda platila za jednoznacnou.</summary>
        public double AmbiguityMargin = 0.10;

        /// <summary>
        /// Od jake vzdalenosti od maxima se zacina hledat konkurencni maximum [m] - tedy zacatek
        /// sweepu podel osy (viz <see cref="CorrelationScorer.BestRivalAlongAxis"/>). Blizko maxima
        /// je skore skoro stejne u kazdeho kandidata, takze konkurent musi byt VZDALENY - jinak by
        /// nejednoznacnost hlasil kazdy cyklus. Musi byt <= <see cref="SearchRangeM"/>.
        /// </summary>
        public double AmbiguitySeparationM = 1.0;

        /// <summary>Min. pocet dukaznich bunek; pod tim se nekoreluje.</summary>
        public int MinEvidenceCells = 400;

        /// <summary>
        /// Skala kovariance ze zakriveni skore (C = -Alpha * H^-1). Skore neni log-verohodnost,
        /// takze zakriveni ma spravny TVAR, ne absolutni skalu. Startovni bod pro faze 4.
        /// </summary>
        public double Alpha = 0.05;

        /// <summary>
        /// Krok numericke druhe derivace skore pro posun [m]. Musi byt VYRAZNE VETSI nez rozliseni
        /// rastru: skore je kvuli rastru schodovite, takze na 5 cm by druha derivace merila
        /// kvantizacni sum, ne zakriveni maxima.
        /// </summary>
        public double HessianStepM = 0.20;

        /// <summary>Krok numericke druhe derivace skore pro kurz [rad]. Tentyz duvod jako u posunu.</summary>
        public double HessianStepHeadingRad = 2.0 * Math.PI / 180.0;

        /// <summary>Dolni hranice sigma posunu [m] - rozliseni gridu.</summary>
        public double SigmaFloorM = 0.05;

        /// <summary>Dolni hranice sigma kurzu [rad].</summary>
        public double SigmaFloorHeadingRad = 0.5 * Math.PI / 180.0;

        /// <summary>Nad touto sigma se osa posunu neposila [m].</summary>
        public double SigmaCeilingM = 5.0;

        /// <summary>Nad touto sigma se kurz neposila [rad].</summary>
        public double SigmaCeilingHeadingRad = 5.0 * Math.PI / 180.0;

        /// <summary>Nad timto posunem se nekoriguje vubec a hlasi se ztrata lokalizace [m].</summary>
        public double MaxOffsetM = 2.0;

        /// <summary>Rozsireni rastru mapy za hranu gridu [m]; musi byt >= <see cref="SearchRangeM"/>.</summary>
        public double MapRasterMarginM = 3.0;

        /// <summary>Min. odstup dvou korelaci - ochrana proti hustsim snapshotum.</summary>
        public TimeSpan MinPeriod = TimeSpan.FromMilliseconds(400);

        /// <summary>Zdroj merenia pro fuzi a telemetrii.</summary>
        public string MeasurementSource = "MapCorr";

        /// <summary>Urovne skenovani od nejhrubsi k nejjemnejsi.</summary>
        public ScanLevel[] Levels =
        {
            new ScanLevel { StepM = 0.40, StepHeadingRad = 4.0 * Math.PI / 180.0,
                            HalfRangeM = 2.5, HalfRangeHeadingRad = 8.0 * Math.PI / 180.0, Stride = 4 },
            new ScanLevel { StepM = 0.10, StepHeadingRad = 1.0 * Math.PI / 180.0,
                            HalfRangeM = 0.4, HalfRangeHeadingRad = 2.0 * Math.PI / 180.0, Stride = 1 },
            new ScanLevel { StepM = 0.05, StepHeadingRad = 0.5 * Math.PI / 180.0,
                            HalfRangeM = 0.1, HalfRangeHeadingRad = 0.5 * Math.PI / 180.0, Stride = 1 },
        };

        /// <summary>Nejvetsi posun, ktery muze kandidat mit = polovina okna nejhrubsi urovne [m].</summary>
        public double SearchRangeM => Levels.Length > 0 ? Levels[0].HalfRangeM : 0.0;

        /// <summary>Zkontroluje konzistenci; vyhodi <see cref="ArgumentException"/> pri chybe.</summary>
        public void Validate()
        {
            if (Levels == null || Levels.Length == 0)
                throw new ArgumentException("MapCorrelatorConfig.Levels musi mit aspon jednu uroven.");

            for (int i = 0; i < Levels.Length; i++)
            {
                var l = Levels[i];
                if (l.StepM <= 0 || l.StepHeadingRad <= 0)
                    throw new ArgumentException($"MapCorrelatorConfig.Levels[{i}]: kroky musi byt > 0.");
                if (l.HalfRangeM < 0 || l.HalfRangeHeadingRad < 0)
                    throw new ArgumentException($"MapCorrelatorConfig.Levels[{i}]: okna nesmi byt zaporna.");
                if (l.Stride < 1)
                    throw new ArgumentException($"MapCorrelatorConfig.Levels[{i}]: Stride musi byt >= 1.");
            }

            if (MapRasterMarginM < SearchRangeM)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.MapRasterMarginM ({MapRasterMarginM}) musi byt >= "
                    + $"SearchRangeM ({SearchRangeM}), jinak kandidat saha mimo rastr.");
            if (Alpha <= 0)
                throw new ArgumentException($"MapCorrelatorConfig.Alpha musi byt > 0, je {Alpha}.");
            if (HessianStepM <= 0 || HessianStepHeadingRad <= 0)
                throw new ArgumentException("MapCorrelatorConfig: kroky Hessianu musi byt > 0.");
            if (EvidenceThreshold <= 0)
                throw new ArgumentException("MapCorrelatorConfig.EvidenceThreshold musi byt > 0.");
            if (MinEvidenceCells < 1)
                throw new ArgumentException("MapCorrelatorConfig.MinEvidenceCells musi byt >= 1.");
            if (SigmaFloorM <= 0 || SigmaFloorHeadingRad <= 0)
                throw new ArgumentException("MapCorrelatorConfig: dolni hranice sigma musi byt > 0.");
            if (SigmaFloorM > SigmaCeilingM)
                throw new ArgumentException(
                    $"MapCorrelatorConfig: SigmaFloorM ({SigmaFloorM}) > SigmaCeilingM ({SigmaCeilingM}).");
            if (SigmaFloorHeadingRad > SigmaCeilingHeadingRad)
                throw new ArgumentException(
                    "MapCorrelatorConfig: SigmaFloorHeadingRad > SigmaCeilingHeadingRad.");
            if (MaxOffsetM <= 0)
                throw new ArgumentException("MapCorrelatorConfig.MaxOffsetM musi byt > 0.");
            if (AmbiguitySeparationM <= 0)
                throw new ArgumentException("MapCorrelatorConfig.AmbiguitySeparationM musi byt > 0.");
            if (AmbiguitySeparationM > SearchRangeM)
                throw new ArgumentException(
                    $"MapCorrelatorConfig.AmbiguitySeparationM ({AmbiguitySeparationM}) musi byt <= "
                    + $"SearchRangeM ({SearchRangeM}), jinak se konkurent nikdy nevzorkuje a test "
                    + "nejednoznacnosti je tise vypnuty.");
        }
    }
}
```

- [x] **Krok 5: Implementuj `EvidenceCloud`**

Vytvoř `Src/ARBot.Common/Localization/EvidenceCloud.cs`:

```csharp
using System;
using System.Collections.Generic;
using ARBot.Common.Logs;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Dukazni bunky pro korelaci: jen ty, kde ma semanticky kanal LRoad dost silne mineni.
    /// Souradnice jsou STREDY bunek ve svete [m], vaha je log-odds VCETNE ZNAMENKA
    /// (kladne = "mimo cestu", zaporne = "cesta"). Viz doc/map-correlation-localization.md.
    ///
    /// <para>Kanal Occ se NEUCASTNI: jsou v nem parkujici auta, chodci a stromy, ktere v mape
    /// nejsou, a systematicky by odhad tlacily stranou.</para>
    ///
    /// <para>Struktura je "pole misto objektu" (SoA) schvalne - skenovani jde pres oblak stovky krat
    /// za cyklus a chce sekvencni pristup do pameti.</para>
    /// </summary>
    public sealed class EvidenceCloud
    {
        /// <summary>Pocet dukaznich bunek.</summary>
        public int Count { get; }

        /// <summary>Svetove X stredu bunky [m].</summary>
        public double[] X { get; }

        /// <summary>Svetove Y stredu bunky [m].</summary>
        public double[] Y { get; }

        /// <summary>LRoad [log-odds] vcetne znamenka.</summary>
        public float[] W { get; }

        private EvidenceCloud(double[] x, double[] y, float[] w, int count)
        {
            X = x; Y = y; W = w; Count = count;
        }

        /// <summary>
        /// Vytahne ze snapshotu gridu bunky s <c>|LRoad| &gt;= threshold</c>.
        /// </summary>
        /// <param name="msg">Snapshot gridu (kanaly v lokalnim poradi <c>i + j * Size</c>).</param>
        /// <param name="threshold">Prah absolutni hodnoty LRoad [log-odds].</param>
        public static EvidenceCloud FromGrid(OccupancyGridMsg msg, float threshold)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            var xs = new List<double>();
            var ys = new List<double>();
            var ws = new List<float>();

            if (msg.Road != null)
            {
                for (int j = 0; j < msg.Size; j++)
                {
                    double y = msg.CenterY(j);
                    int rowBase = j * msg.Size;
                    for (int i = 0; i < msg.Size; i++)
                    {
                        float w = msg.Road[rowBase + i] * msg.Scale;
                        if (w > -threshold && w < threshold) continue;
                        xs.Add(msg.CenterX(i));
                        ys.Add(y);
                        ws.Add(w);
                    }
                }
            }

            return new EvidenceCloud(xs.ToArray(), ys.ToArray(), ws.ToArray(), ws.Count);
        }
    }
}
```

- [x] **Krok 6: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~EvidenceCloudTests|FullyQualifiedName~MapCorrelatorConfigTests"
```

Očekávané: PASS (16 testů).

---

## Task 4: `CorrelationScorer` — skóre jednoho kandidáta

Srdce korelace: pro kandidátní `(dx, dy, φ)` spočítat, jak dobře se důkazní oblak shodne s mapou.
Tenhle task staví i **společné testovací scény**, které použijí Tasky 5, 6 a 10.

**Files:**
- Create: `Src/ARBot.Common/Localization/CorrelationScorer.cs`
- Create: `Src/ARBot.Common.Tests/Localization/CorrelationTestScenes.cs`
- Test: `Src/ARBot.Common.Tests/Localization/CorrelationScorerTests.cs`

**Interfaces:**
- Consumes: `RoadRaster.TryIsRoad` (Task 2), `EvidenceCloud` a `MapCorrelatorConfig` (Task 3).
- Produces:
  - `CorrelationScorer(EvidenceCloud cloud, RoadRaster raster, double robotX, double robotY)`
  - `double CorrelationScorer.Score(double dx, double dy, double phi, int stride)`
  - testovací pomocník `CorrelationTestScenes` s `Origin()`, `StraightEastRoad(...)`,
    `TJunction(...)`, `ParallelRoads(...)`, `GridFromScene(...)`, `TestConfig()`,
    `GridSize`, `Resolution`

- [x] **Krok 1: Napiš společné testovací scény**

Vytvoř `Src/ARBot.Common.Tests/Localization/CorrelationTestScenes.cs`:

```csharp
using System;
using ARBot.Common.Coordinates;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Spolecne synteticke sceny a generator gridu pro testy korelace s mapou
/// (viz doc/map-correlation-localization.md).
///
/// <para><b>Jak se testuje:</b> grid se naplni podle mapy, ale POSUNUTE a OTOCENE o znamou chybu.
/// Korelator pak musi tu chybu najit. Zadna vize, zadny HW.</para>
/// </summary>
internal static class CorrelationTestScenes
{
    /// <summary>Bunek na stranu testovaciho gridu (9,6 m pri 10 cm - drzi testy rychle).</summary>
    public const int GridSize = 96;

    /// <summary>Velikost bunky testovaciho gridu [m].</summary>
    public const double Resolution = 0.1;

    public static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Jedna prima cesta podel osy X (na vychod), delka 60 m, stred v y = 0.</summary>
    public static RoadNetwork StraightEastRoad(GeoReference o, double width = 4.0)
    {
        var a = new Node(1, o.ToLLA(-30, 0), width);
        var b = new Node(2, o.ToLLA(30, 0), width);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 60.0, wayId: 1, traversalCost: 60.0);
        return builder.Build();
    }

    /// <summary>
    /// T-krizovatka: prima cesta podel X a odbocka na SEVER z bodu (0,0). Odbocka lame podelnou
    /// symetrii - bez ni je poloha "podel cesty" nepodminena.
    /// </summary>
    public static RoadNetwork TJunction(GeoReference o, double width = 4.0)
    {
        var a = new Node(1, o.ToLLA(-30, 0), width);
        var b = new Node(2, o.ToLLA(30, 0), width);
        var c = new Node(3, o.ToLLA(0, 0), width);
        var d = new Node(4, o.ToLLA(0, 20), width);

        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, c, 30.0, wayId: 1, traversalCost: 30.0);
        builder.AddEdge(c, b, 30.0, wayId: 1, traversalCost: 30.0);
        builder.AddEdge(c, d, 20.0, wayId: 2, traversalCost: 20.0);
        return builder.Build();
    }

    /// <summary>
    /// Cesta s ohybem: lomena linie, ktera u pocatku zatoci k severovychodu. Ohyb lame podelnou
    /// symetrii mirneji nez odbocka - test, ze korelace zvlada i zakrivenou cestu.
    /// </summary>
    public static RoadNetwork CurvedRoad(GeoReference o, double width = 4.0)
    {
        // Pozor: prvky musi mit pojmenovane slozky VSECHNY, jinak C# nenajde spolecny typ pole.
        var pts = new[]
        {
            (e: -30.0, n: 0.0), (e: -10.0, n: 0.0), (e: 0.0, n: 2.0),
            (e: 10.0, n: 8.0), (e: 25.0, n: 20.0),
        };

        // Uzly se vyrobi JEDNOU a mezi useky se sdileji (spolecny uzel = navazujici pas).
        var nodes = new Node[pts.Length];
        for (int k = 0; k < pts.Length; k++)
            nodes[k] = new Node(20 + k, o.ToLLA(pts[k].e, pts[k].n), width);

        var builder = new RoadNetwork.Builder();
        for (int k = 0; k + 1 < pts.Length; k++)
        {
            double de = pts[k + 1].e - pts[k].e, dn = pts[k + 1].n - pts[k].n;
            double len = Math.Sqrt(de * de + dn * dn);
            builder.AddEdge(nodes[k], nodes[k + 1], len, wayId: 200, traversalCost: len);
        }
        return builder.Build();
    }

    /// <summary>
    /// Soubezne cesty s rozestupem osy 2 m - vzor se OPAKUJE, takze posun o 2 m da konkurencni
    /// maximum skore skoro stejneho. Slouzi k testu nejednoznacnosti.
    ///
    /// <para><b>Proc jich je devet a ne tri:</b> pri trech se po posunu o rozestup namapuje vnejsi
    /// cesta do prazdna, shoda vyrazne klesne (mereno: konkurent jen 0,29 proti maximu 1,0) a scena
    /// nejednoznacnost NEVYROBI. Aby byl vzor v ramci gridu skutecne periodicky, musi cesty
    /// presahovat grid na obe strany - grid je 9,6 m, takze +-4 rozestupy staci s rezervou.
    /// Zjisteno integracnim testem 2026-08-19.</para>
    /// </summary>
    public static RoadNetwork ParallelRoads(GeoReference o, double width = 1.5, double spacing = 2.0,
                                            int halfCount = 4)
    {
        var builder = new RoadNetwork.Builder();
        for (int k = -halfCount; k <= halfCount; k++)
        {
            double y = k * spacing;
            var a = new Node(100 + 2 * (k + halfCount), o.ToLLA(-30, y), width);
            var b = new Node(101 + 2 * (k + halfCount), o.ToLLA(30, y), width);
            builder.AddEdge(a, b, 60.0, wayId: 200 + k, traversalCost: 60.0);
        }
        return builder.Build();
    }

    /// <summary>
    /// Naplni kanal Road podle mapy tak, jako by robot mel chybu pozy
    /// <paramref name="dx0"/>, <paramref name="dy0"/>, <paramref name="phi0"/>.
    ///
    /// <para>Model korelatoru: bunka, kterou robot vidi na ODHADOVANE pozici q, lezi ve skutecnosti
    /// na q' = R(phi0)*(q - p) + p + (dx0, dy0). Do gridu se tedy zapise, co mapa rika o q'.
    /// Spravna odpoved korelace je pak presne (dx0, dy0, phi0).</para>
    /// </summary>
    public static OccupancyGridMsg GridFromScene(RoadScene scene, double robotX, double robotY,
                                                 double dx0, double dy0, double phi0)
    {
        int originX = (int)Math.Floor(robotX / Resolution) - GridSize / 2;
        int originY = (int)Math.Floor(robotY / Resolution) - GridSize / 2;

        var msg = new OccupancyGridMsg
        {
            Size = GridSize,
            Resolution = Resolution,
            OriginX = originX,
            OriginY = originY,
            Scale = 0.05f,
            BlockedThreshold = 1.0f,
            FreeThreshold = -1.0f,
            Occ = new sbyte[GridSize * GridSize],
            Road = new sbyte[GridSize * GridSize],
            TimeStamp = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc),
        };

        double c = Math.Cos(phi0), s = Math.Sin(phi0);
        for (int j = 0; j < GridSize; j++)
        {
            for (int i = 0; i < GridSize; i++)
            {
                double qx = msg.CenterX(i), qy = msg.CenterY(j);
                double rx = qx - robotX, ry = qy - robotY;
                double tx = robotX + dx0 + (c * rx - s * ry);
                double ty = robotY + dy0 + (s * rx + c * ry);

                // -1 = cesta, +1 = mimo cestu (log-odds NEPRUJEZDNOSTI).
                float logOdds = scene.IsRoad(tx, ty) ? -1.0f : 1.0f;
                msg.Road[i + j * GridSize] = (sbyte)Math.Round(logOdds / msg.Scale);
            }
        }
        return msg;
    }

    /// <summary>
    /// Konfigurace pro testy: dve urovne skenovani misto tri. Nejjemnejsi krok 10 cm odpovida
    /// rozliseni testovaciho gridu - treti uroven by uz merila kvantizaci a testy jen zpomalila.
    /// </summary>
    public static MapCorrelatorConfig TestConfig()
        => new MapCorrelatorConfig
        {
            Levels = new[]
            {
                new ScanLevel { StepM = 0.40, StepHeadingRad = 4.0 * Math.PI / 180.0,
                                HalfRangeM = 2.0, HalfRangeHeadingRad = 8.0 * Math.PI / 180.0, Stride = 4 },
                new ScanLevel { StepM = 0.10, StepHeadingRad = 1.0 * Math.PI / 180.0,
                                HalfRangeM = 0.4, HalfRangeHeadingRad = 4.0 * Math.PI / 180.0, Stride = 1 },
            },
            MapRasterMarginM = 3.0,
        };

    /// <summary>Rastr mapy zarovnany s danou zpravou gridu.</summary>
    public static RoadRaster RasterFor(RoadScene scene, OccupancyGridMsg msg, MapCorrelatorConfig cfg)
        => RoadRaster.Build(scene, msg.OriginX, msg.OriginY, msg.Size, msg.Resolution, cfg.MapRasterMarginM);
}
```

- [x] **Krok 2: Napiš padající testy skóre**

Vytvoř `Src/ARBot.Common.Tests/Localization/CorrelationScorerTests.cs`:

```csharp
using System;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>Testy skore shody dukazniho oblaku s mapou (viz doc/map-correlation-localization.md).</summary>
public class CorrelationScorerTests
{
    private const double RobotX = 0.0;
    private const double RobotY = 0.0;

    /// <summary>Postavi scorer pro primou cestu a grid s danou chybou pozy.</summary>
    private static CorrelationScorer Build(double dx0, double dy0, double phi0, out MapCorrelatorConfig cfg)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
        cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, RobotX, RobotY, dx0, dy0, phi0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        return new CorrelationScorer(cloud, raster, RobotX, RobotY);
    }

    [Test]
    public void Score_BezChybyPozy_JeJednickaVNule()
    {
        var scorer = Build(0, 0, 0, out _);

        // Grid je presna kopie mapy => v (0,0,0) musi souhlasit KAZDA bunka.
        Assert.That(scorer.Score(0, 0, 0, stride: 1), Is.EqualTo(1.0).Within(1e-9));
    }

    [Test]
    public void Score_JeVMezichMinusJednaAzJedna()
    {
        var scorer = Build(0, 0, 0, out _);

        foreach (double d in new[] { -2.0, -0.5, 0.0, 0.5, 2.0 })
        {
            double s = scorer.Score(0, d, 0, stride: 1);
            Assert.That(s, Is.InRange(-1.0, 1.0), $"Skore mimo mez pri dy = {d}.");
        }
    }

    [Test]
    public void Score_MaximumJeVeSkutecnePricneChybe()
    {
        // Robot je ve skutecnosti 0,8 m severne od toho, kde si mysli.
        var scorer = Build(0.0, 0.8, 0.0, out _);

        double atTruth = scorer.Score(0.0, 0.8, 0.0, stride: 1);
        double atZero = scorer.Score(0.0, 0.0, 0.0, stride: 1);

        Assert.That(atTruth, Is.EqualTo(1.0).Within(0.02));
        Assert.That(atTruth, Is.GreaterThan(atZero));
    }

    [Test]
    public void Score_PodelPrimeCesty_JePlocheSkore()
    {
        // Klicove tvrzeni navrhu: podel prime cesty korelace NIC nerika.
        var scorer = Build(0, 0, 0, out _);

        double s0 = scorer.Score(0.0, 0.0, 0.0, stride: 1);
        double s1 = scorer.Score(1.5, 0.0, 0.0, stride: 1);

        Assert.That(s1, Is.EqualTo(s0).Within(0.02),
                    "Posun podel prime cesty nesmi skore menit - jinak by odhad predstiral podelnou informaci.");
    }

    [Test]
    public void Score_ChybaKurzu_SnizujeSkoreVNule()
    {
        var scorer = Build(0.0, 0.0, 5.0 * Math.PI / 180.0, out _);

        double atTruth = scorer.Score(0.0, 0.0, 5.0 * Math.PI / 180.0, stride: 1);
        double atZero = scorer.Score(0.0, 0.0, 0.0, stride: 1);

        Assert.That(atTruth, Is.GreaterThan(atZero + 0.05));
    }

    [Test]
    public void Score_Stride_DavaPodobnyVysledekJakoBezNeho()
    {
        var scorer = Build(0.0, 0.5, 0.0, out _);

        double full = scorer.Score(0.0, 0.5, 0.0, stride: 1);
        double sub = scorer.Score(0.0, 0.5, 0.0, stride: 4);

        Assert.That(sub, Is.EqualTo(full).Within(0.05));
    }

    [Test]
    public void Score_PrazdnyOblak_JeNula()
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
        var cfg = CorrelationTestScenes.TestConfig();
        var msg = CorrelationTestScenes.GridFromScene(scene, RobotX, RobotY, 0, 0, 0);
        msg.Road = null;

        var scorer = new CorrelationScorer(EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold),
                                           CorrelationTestScenes.RasterFor(scene, msg, cfg),
                                           RobotX, RobotY);

        Assert.That(scorer.Score(0, 0, 0, stride: 1), Is.EqualTo(0.0));
    }
}
```

- [x] **Krok 3: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~CorrelationScorerTests
```

Očekávané: chyba překladu `CS0246: The type or namespace name 'CorrelationScorer' could not be found`.

- [x] **Krok 4: Implementuj skóre**

Vytvoř `Src/ARBot.Common/Localization/CorrelationScorer.cs`:

```csharp
using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Skore shody dukazniho oblaku s mapou pro kandidatni chybu pozy.
    /// Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Model kandidata:</b> cely oblak se otoci o <c>phi</c> KOLEM ROBOTU a posune o
    /// <c>(dx, dy)</c>. Kdyz je maximum v <c>(dx*, dy*, phi*)</c>, znamena to "skutecna poloha je
    /// odhad + (dx*, dy*), skutecny kurz je odhad + phi*".</para>
    /// </summary>
    public sealed class CorrelationScorer
    {
        private readonly EvidenceCloud cloud;
        private readonly RoadRaster raster;
        private readonly double robotX;
        private readonly double robotY;

        /// <param name="cloud">Dukazni bunky z kanalu LRoad.</param>
        /// <param name="raster">Vozovka podle mapy zarovnana s gridem.</param>
        /// <param name="robotX">Odhadovana poloha robotu - stred rotace kandidata [m].</param>
        /// <param name="robotY">Odhadovana poloha robotu - stred rotace kandidata [m].</param>
        public CorrelationScorer(EvidenceCloud cloud, RoadRaster raster, double robotX, double robotY)
        {
            this.cloud = cloud ?? throw new ArgumentNullException(nameof(cloud));
            this.raster = raster ?? throw new ArgumentNullException(nameof(raster));
            this.robotX = robotX;
            this.robotY = robotY;
        }

        /// <summary>
        /// Normovane skore shody v rozsahu -1..1 (1 = dokonala shoda, 0 = zadna informace,
        /// zaporne = shoda naopak). Normovani delenim souctem vah dela skore POROVNATELNE mezi
        /// cykly, takze slouzi zaroven jako metrika kvality.
        /// </summary>
        /// <param name="dx">Kandidatni posun na vychod [m].</param>
        /// <param name="dy">Kandidatni posun na sever [m].</param>
        /// <param name="phi">Kandidatni chyba kurzu [rad].</param>
        /// <param name="stride">Bere se kazdy N-ty dukaz (hrube urovne skenovani). 1 = vsechny.</param>
        public double Score(double dx, double dy, double phi, int stride)
        {
            if (stride < 1) stride = 1;

            double c = Math.Cos(phi), s = Math.Sin(phi);
            double baseX = robotX + dx, baseY = robotY + dy;

            double num = 0.0, den = 0.0;
            for (int i = 0; i < cloud.Count; i += stride)
            {
                double rx = cloud.X[i] - robotX;
                double ry = cloud.Y[i] - robotY;
                double qx = baseX + (c * rx - s * ry);
                double qy = baseY + (s * rx + c * ry);

                // Mimo rastr = "nevim", ne "neni cesta" - takovy dukaz se PRESKOCI vcetne jmenovatele,
                // jinak by okraj rastru tlacil odhad dovnitr.
                if (!raster.TryIsRoad(qx, qy, out bool isRoad)) continue;

                double w = cloud.W[i];
                num += w * (isRoad ? -1.0 : 1.0);
                den += Math.Abs(w);
            }

            return den > 0.0 ? num / den : 0.0;
        }
    }
}
```

- [x] **Krok 5: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~CorrelationScorerTests
```

Očekávané: PASS (7 testů).

---

## Task 5: Hrubě-jemný sken

Prohledání okna `(dx, dy, φ)` po úrovních a nalezení maxima. Součástí je i **měření
nejednoznačnosti**: nejlepší konkurent, který je od maxima dost daleko.

**Files:**
- Modify: `Src/ARBot.Common/Localization/CorrelationScorer.cs` (přidání `Scan`)
- Create: `Src/ARBot.Common/Localization/ScanResult.cs`
- Test: `Src/ARBot.Common.Tests/Localization/CorrelationScanTests.cs`

**Interfaces:**
- Consumes: `CorrelationScorer.Score` (Task 4), `MapCorrelatorConfig.Levels` (Task 3).
- Produces:
  - `ScanResult CorrelationScorer.Scan(MapCorrelatorConfig cfg)`
  - `ScanResult` s vlastnostmi `Dx`, `Dy`, `Phi`, `Score`, `CoarsePeakScore`, `Candidates`
  - `double CorrelationScorer.BestRivalAlongAxis(ScanResult peak, double axisAngle, MapCorrelatorConfig cfg)`

- [x] **Krok 1: Napiš padající testy skenu**

Vytvoř `Src/ARBot.Common.Tests/Localization/CorrelationScanTests.cs`:

```csharp
using System;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy hrube-jemneho skenovani (viz doc/map-correlation-localization.md).
/// Grid se generuje se ZNAMOU chybou pozy a sken ji musi najit.
/// </summary>
public class CorrelationScanTests
{
    /// <summary>Sken nad danou siti a danou skutecnou chybou pozy.</summary>
    private static ScanResult Run(RoadNetwork network, double robotX, double robotY,
                                  double dx0, double dy0, double phi0)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(network, origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, robotX, robotY, dx0, dy0, phi0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        return new CorrelationScorer(cloud, raster, robotX, robotY).Scan(cfg);
    }

    [Test]
    public void Scan_PricnaChybaNaPrimeCeste_SeNajde()
    {
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0, 0.7, 0.0);

        Assert.That(result.Dy, Is.EqualTo(0.7).Within(0.15));
        Assert.That(result.Score, Is.GreaterThan(0.9));
    }

    [Test]
    public void Scan_ChybaKurzu_SeNajde()
    {
        var origin = CorrelationTestScenes.Origin();
        double phi0 = 4.0 * Math.PI / 180.0;
        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0, 0.0, phi0);

        Assert.That(result.Phi, Is.EqualTo(phi0).Within(1.5 * Math.PI / 180.0));
    }

    [Test]
    public void Scan_UOdbocky_NajdeIPodelnouChybu()
    {
        // Robot stoji 3 m zapadne od krizovatky, aby ji mel v zaberu gridu.
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.TJunction(origin), -3.0, 0.0, 0.8, 0.0, 0.0);

        Assert.That(result.Dx, Is.EqualTo(0.8).Within(0.25),
                    "Odbocka lame podelnou symetrii, takze dx musi byt najitelne.");
    }

    [Test]
    public void Scan_NaOhybu_NajdePricnouChybu()
    {
        // Robot stoji na ceste v miste ohybu.
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.CurvedRoad(origin), 0.0, 2.0, 0.0, 0.6, 0.0);

        Assert.That(result.Dy, Is.EqualTo(0.6).Within(0.25));
        Assert.That(result.Score, Is.GreaterThan(0.85));
    }

    [Test]
    public void Scan_NaPrimeCeste_PodelnaSlozkaNeniUrcena()
    {
        // Skutecna chyba je jen pricna; podelna slozka vyjde libovolne (skore je podel plche).
        // Test tvrdi jen to, ze se tim NEROZBIJE pricny odhad.
        var origin = CorrelationTestScenes.Origin();
        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0, -0.6, 0.0);

        Assert.That(result.Dy, Is.EqualTo(-0.6).Within(0.15));
    }

    /// <summary>Sken i konkurent podel zadane osy nad jednou scenou.</summary>
    private static (ScanResult Scan, double Rival) RunWithRival(RoadNetwork network,
                                                               double robotX, double robotY,
                                                               double axisAngle)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(network, origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, robotX, robotY, 0, 0, 0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        var scorer = new CorrelationScorer(cloud, raster, robotX, robotY);
        var scan = scorer.Scan(cfg);
        return (scan, scorer.BestRivalAlongAxis(scan, axisAngle, cfg));
    }

    [Test]
    public void Rival_SoubezneCesty_JeBlizkoMaxima()
    {
        // Osa 90 stupnu = NAPRIC cestami, tedy ten smer, ve kterem se soubezne cesty pletou.
        // Vzor se opakuje s periodou 2 m, takze konkurent musi byt skore blizko maxima - jinak
        // by test nejednoznacnosti v MapCorrelationResult nemel co merit.
        var origin = CorrelationTestScenes.Origin();
        var r = RunWithRival(CorrelationTestScenes.ParallelRoads(origin), 0, 0, Math.PI / 2);

        Assert.That(r.Rival, Is.GreaterThan(r.Scan.CoarsePeakScore - 0.5));
    }

    [Test]
    public void Rival_JednaCesta_NapricJeVyrazneHorsi()
    {
        // TOHLE je smysl mereni konkurenta PODEL OSY. Osa 90 stupnu = napric jedinou cestou:
        // posun napric cestu opusti, takze konkurent MUSI byt vyrazne horsi. (Kdyby se konkurent
        // meril ve 2D, nasel by se kandidat PODEL cesty se skore PRESNE stejnym - a kazda prima
        // cesta by se hlasila jako nejednoznacna, cimz by se potlacila i pricna korekce.)
        var origin = CorrelationTestScenes.Origin();
        var r = RunWithRival(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, Math.PI / 2);

        Assert.That(r.Rival, Is.LessThan(r.Scan.CoarsePeakScore - 0.3));
    }

    [Test]
    public void Rival_JednaCesta_PODELJeStejneDobry()
    {
        // Doplnek predchoziho testu: podel cesty je konkurent opravdu stejne dobry. Prave proto se
        // konkurent NESMI merit ve 2D - tohle cislo neni nejednoznacnost, ale znama neurcenost,
        // kterou uz vyjadruje nekonecna sigma volne osy.
        var origin = CorrelationTestScenes.Origin();
        var r = RunWithRival(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0.0);

        Assert.That(r.Rival, Is.EqualTo(r.Scan.CoarsePeakScore).Within(0.02));
    }

    [Test]
    public void Scan_PocetKandidatuOdpovidaUrovnim()
    {
        var origin = CorrelationTestScenes.Origin();
        var cfg = CorrelationTestScenes.TestConfig();

        int expected = 0;
        foreach (var l in cfg.Levels)
        {
            int nT = (int)Math.Round(l.HalfRangeM / l.StepM);
            int nH = (int)Math.Round(l.HalfRangeHeadingRad / l.StepHeadingRad);
            expected += (2 * nT + 1) * (2 * nT + 1) * (2 * nH + 1);
        }

        var result = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0, 0, 0, 0);

        Assert.That(result.Candidates, Is.EqualTo(expected));
    }
}
```

- [x] **Krok 2: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~CorrelationScanTests
```

Očekávané: chyba překladu `CS0246` pro `ScanResult` a `CS1061` pro `Scan`.

- [x] **Krok 3: Vytvoř `ScanResult`**

Vytvoř `Src/ARBot.Common/Localization/ScanResult.cs`:

```csharp
namespace ARBot.Common.Localization
{
    /// <summary>
    /// Vysledek hrube-jemneho skenovani korelace. Viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class ScanResult
    {
        /// <summary>Nalezeny posun na vychod [m].</summary>
        public double Dx;

        /// <summary>Nalezeny posun na sever [m].</summary>
        public double Dy;

        /// <summary>Nalezena chyba kurzu [rad].</summary>
        public double Phi;

        /// <summary>Skore v maximu (z NEJJEMNEJSI urovne).</summary>
        public double Score;

        /// <summary>
        /// Skore maxima na NEJHRUBSI urovni. Proti nemu se porovnava konkurent
        /// (<see cref="CorrelationScorer.BestRivalAlongAxis"/>) - oba pouzivaji stride nejhrubsi
        /// urovne, takze jsou to soumeritelna cisla.
        /// </summary>
        public double CoarsePeakScore;

        /// <summary>Kolik kandidatu se celkem vyhodnotilo (diagnostika ceny).</summary>
        public int Candidates;
    }
}
```

- [x] **Krok 4: Přidej `Scan` do `CorrelationScorer`**

Na konec třídy `Src/ARBot.Common/Localization/CorrelationScorer.cs` přidej tyto metody. **Žádný nový
`using` není potřeba** — pracuje se jen s `Math` a s polem konfigurace:

```csharp
        /// <summary>Tolerance rovnosti skore pri hledani maxima - viz remizove pravidlo v
        /// <see cref="Scan"/>.</summary>
        private const double TieEps = 1e-9;

        /// <summary>
        /// Hrube-jemne prohledani okna <c>(dx, dy, phi)</c>. Kazda uroven hleda v okne kolem maxima
        /// z predchozi, takze cena roste s poctem urovni, ne s velikosti okna.
        /// </summary>
        public ScanResult Scan(MapCorrelatorConfig cfg)
        {
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));
            if (cfg.Levels == null || cfg.Levels.Length == 0)
                throw new ArgumentException("MapCorrelatorConfig.Levels je prazdne.", nameof(cfg));

            double centerX = 0.0, centerY = 0.0, centerPhi = 0.0;
            double bestDx = 0.0, bestDy = 0.0, bestPhi = 0.0, best = double.NegativeInfinity;
            int bestDist = int.MaxValue;
            double coarsePeak = 0.0;
            int candidates = 0;

            for (int li = 0; li < cfg.Levels.Length; li++)
            {
                var lvl = cfg.Levels[li];
                bool isCoarse = li == 0;

                best = double.NegativeInfinity;
                bestDist = int.MaxValue;
                int nT = (int)Math.Round(lvl.HalfRangeM / lvl.StepM);
                int nH = (int)Math.Round(lvl.HalfRangeHeadingRad / lvl.StepHeadingRad);

                for (int ix = -nT; ix <= nT; ix++)
                {
                    double dx = centerX + ix * lvl.StepM;
                    for (int iy = -nT; iy <= nT; iy++)
                    {
                        double dy = centerY + iy * lvl.StepM;
                        for (int ip = -nH; ip <= nH; ip++)
                        {
                            double phi = centerPhi + ip * lvl.StepHeadingRad;
                            double sc = Score(dx, dy, phi, lvl.Stride);
                            candidates++;

                            // Vzdalenost kandidata od STREDU okna, merena v KROCICH (bez jednotek,
                            // takze se posun a kurz daji porovnat).
                            int dist = ix * ix + iy * iy + ip * ip;

                            if (sc > best + TieEps)
                            {
                                best = sc;
                                bestDx = dx; bestDy = dy; bestPhi = phi; bestDist = dist;
                            }
                            else if (sc > best - TieEps && dist < bestDist)
                            {
                                // REMIZA. Na plose je skore casto PRESNE stejne - posun PODEL prime
                                // cesty nemeni nic, co robot vidi. Pak se bere kandidat NEJBLIZ
                                // STREDU okna, tedy nejmensi korekce: kdyz data nedavaji zadny duvod
                                // jednu z remizovych moznosti preferovat, spravna odpoved je
                                // "neopravuj". Naivni "prvni vyhrava" vracelo OKRAJ okna a korelator
                                // pak hlasil nekolikametrovou korekci, kterou sam zamitl jako ztratu
                                // lokalizace. Zjisteno integracnim testem 2026-08-19.
                                if (sc > best) best = sc;
                                bestDx = dx; bestDy = dy; bestPhi = phi; bestDist = dist;
                            }
                        }
                    }
                }

                if (isCoarse) coarsePeak = best;
                centerX = bestDx; centerY = bestDy; centerPhi = bestPhi;
            }

            return new ScanResult
            {
                Dx = bestDx,
                Dy = bestDy,
                Phi = bestPhi,
                Score = best,
                CoarsePeakScore = coarsePeak,
                Candidates = candidates,
            };
        }

        /// <summary>
        /// Nejlepsi skore konkurencniho maxima posunuteho PODEL zadane osy. Slouzi k rozpoznani
        /// nejednoznacnosti (soubezna cesta).
        ///
        /// <para><b>Proc podel osy a ne v 2D:</b> na PRIME ceste je kandidat posunuty PODEL cesty
        /// presne stejne dobry jako maximum - posun podel prime cesty nemeni nic, co robot vidi.
        /// To ale NENI nejednoznacnost: je to tataz odpoved posunuta ve smeru, ktery uz odhad
        /// prohlasil za neznamy (nekonecna sigma volne osy), a ta osa se do fuze beztak neposila.
        /// Merit konkurenta ve 2D proto vyrabelo falesnou nejednoznacnost na kazde prime ceste
        /// a potlacovalo i dobre urcenou PRICNOU korekci - tedy hlavni vystup cele funkce.
        /// Konkurent posunuty podel URCENE osy je naopak nejednoznacnost skutecna (soubezna cesta).
        /// Zjisteno integracnim testem 2026-08-19; viz doc/map-correlation-localization.md.</para>
        /// </summary>
        /// <param name="peak">Nalezene maximum.</param>
        /// <param name="axisAngle">Smer LEPE urcene osy [rad] (z <see cref="CorrelationCovariance"/>).</param>
        /// <param name="cfg">Bere se z ni odstup konkurenta, rozsah hledani a stride nejhrubsi urovne.</param>
        public double BestRivalAlongAxis(ScanResult peak, double axisAngle, MapCorrelatorConfig cfg)
        {
            if (peak == null) throw new ArgumentNullException(nameof(peak));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            double ux = Math.Cos(axisAngle), uy = Math.Sin(axisAngle);

            // Krok z NEJJEMNEJSI urovne, ne z nejhrubsi. Konkurent je uzky vrchol (siroky asi jako
            // cesta), takze hrubym krokem se DA MINOUT: pri kroku 0,4 m se vzorkuje 1,0 / 1,4 / 1,8
            // a rival na presne 2,0 m (rozestup soubeznych cest) se nikdy netrefi - merenim overeno,
            // ze tam melo byt skore 0,958 misto nalezenych 0,625. Zjisteno integracnim testem
            // 2026-08-19.
            double step = cfg.Levels[cfg.Levels.Length - 1].StepM;

            // Stride ale ZUSTAVA z nejhrubsi urovne - jinak by se skore konkurenta porovnavalo
            // s CoarsePeakScore z jinak podvzorkovaneho oblaku, tedy nesoumeritelna cisla.
            int stride = cfg.Levels[0].Stride;

            double best = double.NegativeInfinity;
            for (double t = cfg.AmbiguitySeparationM; t <= cfg.SearchRangeM + 1e-9; t += step)
            {
                double a = Score(peak.Dx + t * ux, peak.Dy + t * uy, peak.Phi, stride);
                double b = Score(peak.Dx - t * ux, peak.Dy - t * uy, peak.Phi, stride);
                if (a > best) best = a;
                if (b > best) best = b;
            }
            return best;
        }
```

- [x] **Krok 5: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~CorrelationScanTests
```

Očekávané: PASS (9 testů).

---

## Task 6: `CorrelationCovariance` — σ ze zakřivení skóre

Jádro celého slibu: kovariance musí sama říct „napříč vím, podél nevím". Počítá se ze zakřivení
skóre v maximu; translační blok se rozloží na vlastní osy.

**Files:**
- Create: `Src/ARBot.Common/Localization/CorrelationCovariance.cs`
- Test: `Src/ARBot.Common.Tests/Localization/CorrelationCovarianceTests.cs`

**Interfaces:**
- Consumes: `CorrelationScorer.Score` (Task 4), `ScanResult` (Task 5), `MapCorrelatorConfig`
  (`Alpha`, `HessianStepM`, `HessianStepHeadingRad`, `SigmaFloorM`, `SigmaFloorHeadingRad`).
- Produces:
  - `CorrelationCovariance.Estimate(CorrelationScorer scorer, ScanResult peak, MapCorrelatorConfig cfg)`
  - `CorrelationCovariance.NoPeak()`
  - vlastnosti `SigmaTight`, `SigmaLoose`, `TightAxisAngle`, `SigmaPhi`, `HasPeak`

- [x] **Krok 0: Přidej šikmou scénu do `CorrelationTestScenes`**

Do `Src/ARBot.Common.Tests/Localization/CorrelationTestScenes.cs` přidej za `StraightEastRoad`:

```csharp
    /// <summary>
    /// Prima cesta pod 45 stupni. Na ceste podel osy je vazba kurz-translace presne nulova; sikma
    /// cesta ji vyrobi, takze se da otestovat marginalizace v degradovane ceste kovariance.
    /// </summary>
    public static RoadNetwork DiagonalRoad(GeoReference o, double width = 4.0)
    {
        var a = new Node(30, o.ToLLA(-21, -21), width);
        var b = new Node(31, o.ToLLA(21, 21), width);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 59.4, wayId: 300, traversalCost: 59.4);
        return builder.Build();
    }
```

- [x] **Krok 1: Napiš padající testy**

Vytvoř `Src/ARBot.Common.Tests/Localization/CorrelationCovarianceTests.cs`:

```csharp
using System;
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy kovariance korelace (viz doc/map-correlation-localization.md).
/// Klicove tvrzeni navrhu: anizotropie vznikne SAMA ze zakriveni skore - nic se nedetekuje.
/// </summary>
public class CorrelationCovarianceTests
{
    /// <summary>Spocte kovarianci nad danou siti a polohou robotu (bez chyby pozy).</summary>
    private static CorrelationCovariance Run(RoadNetwork network, double robotX, double robotY)
    {
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(network, origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, robotX, robotY, 0, 0, 0);
        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);

        var scorer = new CorrelationScorer(cloud, raster, robotX, robotY);
        return CorrelationCovariance.Estimate(scorer, scorer.Scan(cfg), cfg);
    }

    [Test]
    public void PrimaCesta_PodelnaSigmaJeVyrazneVetsiNezPricna()
    {
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        Assert.That(cov.HasPeak, Is.True);
        Assert.That(cov.SigmaLoose, Is.GreaterThan(cov.SigmaTight * 3.0),
                    "Na prime ceste musi byt jedna osa vyrazne mene urcena - to je jadro celeho slibu.");
    }

    [Test]
    public void PrimaCesta_UrcenaOsaMiriNapricCesty()
    {
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        // Cesta vede na vychod (0 rad), takze dobre urcena osa je sever/jih => +-90 stupnu.
        double deg = Math.Abs(cov.TightAxisAngle * 180.0 / Math.PI) % 180.0;
        Assert.That(deg, Is.EqualTo(90.0).Within(20.0));
    }

    [Test]
    public void PrimaCesta_MaMaximumIKdyzZakriveniPodelChybi()
    {
        // REGRESNI TEST. Na prime ceste je podelna druha derivace PRESNE nula, takze -H je jen
        // semidefinitni a neda se invertovat. Drive to zahodilo cely vysledek (HasPeak=false) -
        // tedy i PRICNOU korekci, ktera je hlavni vystup cele funkce. Viz
        // doc/map-correlation-localization.md, "Singularni H je na prime ceste NORMALNI STAV".
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        Assert.That(cov.HasPeak, Is.True,
                    "Prima cesta MUSI dat pouzitelny vysledek - jen s nekonecnou podelnou sigmou.");
        Assert.That(cov.SigmaTight, Is.LessThan(1.0), "Pricna slozka musi byt urcena.");
        Assert.That(double.IsPositiveInfinity(cov.SigmaLoose) || cov.SigmaLoose > 3.0, Is.True,
                    "Podelna slozka na prime ceste urcena byt nema.");
    }

    [Test]
    public void SikmaCesta_PricnaSlozkaJeUrcena()
    {
        // Sikma cesta drzi ALESPON to, ze pricna slozka je urcena a osa miri priblizne napric.
        //
        // POZOR - OTEVRENY UKOL, ktery tento test SCHVALNE netvrdi: na sikme ceste vychazi
        // SigmaLoose konecna (namereno 0,1848 m), i kdyz prima cesta zadnou podelnou informaci
        // nenese - skore je podel ni PRESNE ploche, zmereno pres +-1 m. Je to artefakt fitu
        // kvadraticke formy na "tent" skore. Kdyby se tady tvrdilo "SigmaLoose musi byt nekonecna",
        // test by cerveny zustal, dokud se ta vada neopravi - a to je rozhodnuti autora, ne tohoto
        // tasku. Cisla, odvozeni a dve neuspesne opravy jsou v
        // doc/map-correlation-localization.md, sekce Otevrene ukoly.
        var origin = CorrelationTestScenes.Origin();
        var cov = Run(CorrelationTestScenes.DiagonalRoad(origin), 0, 0);

        Assert.That(cov.HasPeak, Is.True);
        Assert.That(cov.SigmaTight, Is.LessThan(1.0), "Pricna slozka musi byt urcena i na sikme ceste.");

        // Cesta vede pod 45 stupni, takze dobre urcena osa miri napric = 135 stupnu.
        double deg = ((cov.TightAxisAngle * 180.0 / Math.PI) % 180.0 + 180.0) % 180.0;
        Assert.That(deg, Is.EqualTo(135.0).Within(20.0));
    }

    [Test]
    public void TKrizovatka_ObeSigmyJsouMale()
    {
        var origin = CorrelationTestScenes.Origin();
        var atJunction = Run(CorrelationTestScenes.TJunction(origin), -3.0, 0.0);
        var onStraight = Run(CorrelationTestScenes.StraightEastRoad(origin), 0, 0);

        Assert.That(atJunction.HasPeak, Is.True);
        // KONECNA sigma je tady to podstatne tvrzeni: odbocka lame podelnou symetrii, takze
        // podelna slozka prestava byt neurcena. Samo "mensi nez na prime ceste" by prosla
        // trivialne, kdyby na prime ceste bylo +Inf.
        Assert.That(double.IsFinite(atJunction.SigmaLoose), Is.True,
                    "U odbocky musi byt podelna slozka KONECNA, ne neurcena.");
        Assert.That(atJunction.SigmaLoose, Is.LessThan(2.0));
        Assert.That(atJunction.SigmaLoose, Is.LessThan(onStraight.SigmaLoose),
                    "U odbocky musi byt podelna slozka LEPE urcena nez na prime ceste.");
    }

    [Test]
    public void SigmaNikdyNespadnePodDolniHranici()
    {
        var origin = CorrelationTestScenes.Origin();
        var cfg = CorrelationTestScenes.TestConfig();
        var cov = Run(CorrelationTestScenes.TJunction(origin), -3.0, 0.0);

        Assert.That(cov.SigmaTight, Is.GreaterThanOrEqualTo(cfg.SigmaFloorM));
        Assert.That(cov.SigmaPhi, Is.GreaterThanOrEqualTo(cfg.SigmaFloorHeadingRad));
    }

    [Test]
    public void PlocheSkore_NemaMaximum()
    {
        // Grid bez jakekoli informace (vsechny bunky slabe) => zadny oblak, zadne zakriveni.
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
        var cfg = CorrelationTestScenes.TestConfig();

        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0, 0);
        Array.Clear(msg.Road, 0, msg.Road.Length);   // vse "nevim"

        var cloud = EvidenceCloud.FromGrid(msg, cfg.EvidenceThreshold);
        var raster = CorrelationTestScenes.RasterFor(scene, msg, cfg);
        var scorer = new CorrelationScorer(cloud, raster, 0, 0);

        var cov = CorrelationCovariance.Estimate(scorer, scorer.Scan(cfg), cfg);

        Assert.That(cov.HasPeak, Is.False);
    }

    [Test]
    public void NoPeak_NemaMaximum()
    {
        Assert.That(CorrelationCovariance.NoPeak().HasPeak, Is.False);
    }
}
```

- [x] **Krok 2: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~CorrelationCovarianceTests
```

Očekávané: chyba překladu `CS0246: The type or namespace name 'CorrelationCovariance' could not be found`.

- [x] **Krok 3: Implementuj kovarianci**

Vytvoř `Src/ARBot.Common/Localization/CorrelationCovariance.cs`:

```csharp
using System;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Kovariance odhadu korelace, spoctena ze ZAKRIVENI skore v maximu:
    /// <c>C = -Alpha * H^-1</c>, kde H je Hessian skore. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Proc takhle:</b> translacni blok se rozlozi na vlastni osy, takze na prime ceste
    /// vyjde jedna sigma mala (napric) a druha velka (podel) SAMA - nic se nedetekuje ani
    /// neprepina. U odbocky se sevrou obe.</para>
    ///
    /// <para><b>Skore neni log-verohodnost</b>, takze zakriveni ma spravny TVAR, ne absolutni skalu.
    /// Tu resi kalibracni <see cref="MapCorrelatorConfig.Alpha"/> (ladi se ve fazi 4). POZOR: skore
    /// je "tent" (<c>S ~ 1 - k*|d|</c>), takze zakriveni je ~ <c>1/h</c> a sigma ~ <c>sqrt(h)</c> -
    /// absolutni skala zavisi i na <see cref="MapCorrelatorConfig.HessianStepM"/>. Obe se proto
    /// ladi SPOLU a zmena kroku prepocita vsechny sigmy.</para>
    ///
    /// <para><b>ZNAMA VADA (otevreny ukol):</b> na ceste POD UHLEM k osam gridu vychazi podelne
    /// zakriveni nenulove, takze se hlasi FALESNA podelna jistota (namereno 0,18 m na sikme prime
    /// ceste, coz je "jisteji" nez skutecna T-krizovatka s 0,29 m). Pricina: skore neni lokalne
    /// kvadraticke, takze fit kvadraticke formy je principialne nepresny. Podrobne vcetne
    /// namerenych dat a dvou neuspesnych oprav v doc/map-correlation-localization.md, sekce
    /// Otevrene ukoly.</para>
    /// </summary>
    public readonly struct CorrelationCovariance
    {
        /// <summary>Sigma LEPE urcene osy posunu [m] (na ceste typicky napric).</summary>
        public double SigmaTight { get; }

        /// <summary>Sigma HORE urcene osy posunu [m] (na prime ceste podel).</summary>
        public double SigmaLoose { get; }

        /// <summary>Smer lepe urcene osy [rad], matematicky (0 = vychod).</summary>
        public double TightAxisAngle { get; }

        /// <summary>Marginalni sigma kurzu [rad] - vazba kurz &lt;-&gt; translace je vymarginalizovana
        /// v OBOU vetvich vypoctu, ne jen v te s inverzi.</summary>
        public double SigmaPhi { get; }

        /// <summary>
        /// Je vysledek pouzitelny? <c>false</c> jen pri skutecne degeneraci TRANSLACNIHO bloku
        /// (zadne zakriveni v zadnem smeru, nebo obracene znamenko). Nulove zakriveni v JEDNOM
        /// smeru <c>false</c> NEDAVA - to je na prime ceste normalni stav a resi ho nekonecna
        /// sigma te osy. Spatne zakriveni kurzu taky zahodi jen korekci kurzu, ne cely vysledek.
        /// </summary>
        public bool HasPeak { get; }

        private CorrelationCovariance(double sigmaTight, double sigmaLoose, double tightAxisAngle,
                                      double sigmaPhi, bool hasPeak)
        {
            SigmaTight = sigmaTight;
            SigmaLoose = sigmaLoose;
            TightAxisAngle = tightAxisAngle;
            SigmaPhi = sigmaPhi;
            HasPeak = hasPeak;
        }

        /// <summary>Vysledek "zadne pouzitelne maximum" - volajici ma mlcet.</summary>
        public static CorrelationCovariance NoPeak()
            => new CorrelationCovariance(double.PositiveInfinity, double.PositiveInfinity, 0.0,
                                         double.PositiveInfinity, false);

        /// <summary>
        /// Spocte kovarianci numerickou druhou derivaci skore okolo maxima.
        /// </summary>
        /// <param name="scorer">Skorovaci funkce (tentyz oblak i rastr jako pri skenovani).</param>
        /// <param name="peak">Nalezene maximum.</param>
        /// <param name="cfg">Kroky derivace, kalibrace a hranice sigma.</param>
        public static CorrelationCovariance Estimate(CorrelationScorer scorer, ScanResult peak,
                                                     MapCorrelatorConfig cfg)
        {
            if (scorer == null) throw new ArgumentNullException(nameof(scorer));
            if (peak == null) throw new ArgumentNullException(nameof(peak));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            double x = peak.Dx, y = peak.Dy, p = peak.Phi;
            double h = cfg.HessianStepM, hp = cfg.HessianStepHeadingRad;

            double S(double dx, double dy, double dphi) => scorer.Score(dx, dy, dphi, 1);

            double s0 = S(x, y, p);

            double sxx = (S(x + h, y, p) - 2 * s0 + S(x - h, y, p)) / (h * h);
            double syy = (S(x, y + h, p) - 2 * s0 + S(x, y - h, p)) / (h * h);
            double spp = (S(x, y, p + hp) - 2 * s0 + S(x, y, p - hp)) / (hp * hp);

            double sxy = (S(x + h, y + h, p) - S(x + h, y - h, p)
                          - S(x - h, y + h, p) + S(x - h, y - h, p)) / (4 * h * h);
            double sxp = (S(x + h, y, p + hp) - S(x + h, y, p - hp)
                          - S(x - h, y, p + hp) + S(x - h, y, p - hp)) / (4 * h * hp);
            double syp = (S(x, y + h, p + hp) - S(x, y + h, p - hp)
                          - S(x, y - h, p + hp) + S(x, y - h, p - hp)) / (4 * h * hp);

            var negH = Matrix<double>.Build.DenseOfArray(new[,]
            {
                { -sxx, -sxy, -sxp },
                { -sxy, -syy, -syp },
                { -sxp, -syp, -spp },
            });

            // Test pozitivni definitnosti. Chyti se JEN Cholesky - `Inverse()` je schvalne az za
            // try, aby jeho pripadne selhani vybublalo jako chyba a netvarilo se jako
            // semidefinitnost. (Spolknout neocekavanou vyjimku a tise zmenit vetev vypoctu je
            // presne ten druh chyby, ktery se pak hleda tyden.)
            bool positiveDefinite;
            try
            {
                negH.Cholesky();
                positiveDefinite = true;
            }
            catch (Exception)
            {
                positiveDefinite = false;
            }

            // IDEALNI CESTA: -H je pozitivne definitni, da se invertovat a sigma kurzu vyjde
            // MARGINALNI (vazba phi <-> translace zohlednena).
            if (positiveDefinite)
                return FromCovariance(cfg.Alpha * negH.Inverse(), cfg);

            // DEGRADOVANA CESTA. Na PRIME ceste je singularni -H NORMALNI STAV: posun podel cesty
            // nemeni nic, co robot vidi, takze podelna druha derivace je PRESNE nula. Zahodit kvuli
            // tomu cely vysledek by znamenalo neposlat na prime ceste NIC - a pricna korekce je
            // hlavni vystup cele funkce. Viz doc/map-correlation-localization.md.
            return FromCurvature(-sxx, -sxy, -syy, -spp, -sxp, -syp, cfg);
        }

        /// <summary>Idealni pripad: sigma z KOVARIANCE (mensi vlastni cislo = lepe urcena osa).</summary>
        private static CorrelationCovariance FromCovariance(Matrix<double> c, MapCorrelatorConfig cfg)
        {
            var e = Eigen2(c[0, 0], c[0, 1], c[1, 1]);
            // Nemelo by nastat: kdyz Cholesky prosla, je -H (a tedy i C) pozitivne definitni.
            if (e.Min <= 0 || double.IsNaN(e.Min) || double.IsNaN(e.Max)) return NoPeak();

            double sigmaPhi = c[2, 2] > 0
                ? Math.Max(Math.Sqrt(c[2, 2]), cfg.SigmaFloorHeadingRad)
                : double.PositiveInfinity;

            return new CorrelationCovariance(
                Math.Max(Math.Sqrt(e.Min), cfg.SigmaFloorM),
                Math.Max(Math.Sqrt(e.Max), cfg.SigmaFloorM),
                e.MinAngle, sigmaPhi, hasPeak: true);
        }

        /// <summary>
        /// Degradovany pripad: sigma ze ZAKRIVENI (vetsi vlastni cislo = lepe urcena osa), takze
        /// nulove zakriveni da nekonecnou sigmu misto zahozeni celeho vysledku.
        ///
        /// <para><b>Sigmy jsou MARGINALNI, stejne jako v idealni ceste</b> - druha promenna se
        /// vzdy vymarginalizuje Schurovym doplnkem. Brat sigmu primo z bloku -H by dalo sigmu
        /// PODMINENOU, a ta je systematicky MENSI nez marginalni (Schuruv doplnek je <= A_tt).
        /// Prilis mala sigma je nebezpecna: fuze by korelatoru verila vic, nez si zaslouzi. Navic
        /// by sigma pri prepnuti vetve skocila.</para>
        /// </summary>
        /// <param name="axx">Prvky -H: translacni blok (axx, axy, ayy), kurz (app), vazba (axp, ayp).</param>
        private static CorrelationCovariance FromCurvature(double axx, double axy, double ayy,
                                                           double app, double axp, double ayp,
                                                           MapCorrelatorConfig cfg)
        {
            // Dva prahy "plocho", KAZDY VE SVYCH JEDNOTKACH: translace [skore/m^2], kurz
            // [skore/rad^2]. Michat je nelze - pri vychozich hodnotach se lisi 3283x.
            double tol = cfg.Alpha / (cfg.SigmaCeilingM * cfg.SigmaCeilingM);
            double tolPhi = cfg.Alpha / (cfg.SigmaCeilingHeadingRad * cfg.SigmaCeilingHeadingRad);

            // TRANSLACE: vymarginalizovat kurz. Kdyz je plochy i kurz, je nulova i vazba, takze se
            // korekce vynecha - jinak by se delilo skoro nulou.
            double mxx = axx, mxy = axy, myy = ayy;
            if (app > tolPhi)
            {
                mxx -= axp * axp / app;
                mxy -= axp * ayp / app;
                myy -= ayp * ayp / app;
            }

            var e = Eigen2(mxx, mxy, myy);

            // Zadne zakriveni v ZADNEM smeru = zadne maximum (prazdny grid, sum).
            if (!(e.Max > tol)) return NoPeak();
            // Zakriveni obracene na spatnou stranu = sedlo nebo minimum, ne maximum.
            if (e.Min < -tol) return NoPeak();

            double sigmaTight = Math.Max(Math.Sqrt(cfg.Alpha / e.Max), cfg.SigmaFloorM);
            double sigmaLoose = e.Min > tol
                ? Math.Max(Math.Sqrt(cfg.Alpha / e.Min), cfg.SigmaFloorM)
                : double.PositiveInfinity;

            // KURZ: vymarginalizovat translaci. Plochy smer se VYNECHA (pseudoinverze) - je to
            // spravne osetreni "v tom smeru nevim nic", ne deleni nulou.
            double reducedPhi = app - TranslationAbsorbs(axp, ayp, axx, axy, ayy, tol);
            double sigmaPhi = reducedPhi > tolPhi
                ? Math.Max(Math.Sqrt(cfg.Alpha / reducedPhi), cfg.SigmaFloorHeadingRad)
                : double.PositiveInfinity;

            return new CorrelationCovariance(sigmaTight, sigmaLoose, e.MaxAngle, sigmaPhi,
                                             hasPeak: true);
        }

        /// <summary>
        /// Kolik informace o kurzu "spolkne" translace: <c>g^T * A_tt^+ * g</c>, kde <c>g</c> je
        /// vazba kurz-translace a <c>A_tt^+</c> pseudoinverze translacniho bloku. Smery se
        /// zakrivenim pod <paramref name="tol"/> se do souctu nezapocitavaji.
        /// </summary>
        private static double TranslationAbsorbs(double gx, double gy,
                                                 double axx, double axy, double ayy, double tol)
        {
            var e = Eigen2(axx, axy, ayy);
            double sum = 0.0;

            if (e.Max > tol)
            {
                double p = gx * Math.Cos(e.MaxAngle) + gy * Math.Sin(e.MaxAngle);
                sum += p * p / e.Max;
            }
            if (e.Min > tol)
            {
                double p = gx * Math.Cos(e.MinAngle) + gy * Math.Sin(e.MinAngle);
                sum += p * p / e.Min;
            }
            return sum;
        }

        /// <summary>Vysledek vlastniho rozkladu symetricke 2x2 matice.</summary>
        private readonly struct Eigen2Result
        {
            public readonly double Min, Max;
            /// <summary>Smer vlastniho vektoru k <see cref="Min"/> [rad].</summary>
            public readonly double MinAngle;
            /// <summary>Smer vlastniho vektoru k <see cref="Max"/> [rad].</summary>
            public readonly double MaxAngle;

            public Eigen2Result(double min, double max, double minAngle, double maxAngle)
            {
                Min = min; Max = max; MinAngle = minAngle; MaxAngle = maxAngle;
            }
        }

        /// <summary>Vlastni cisla a vektory symetricke 2x2 [[a,b],[b,d]] uzavrenym tvarem
        /// (deterministicke poradi, zadna zavislost na implementaci Evd).</summary>
        private static Eigen2Result Eigen2(double a, double b, double d)
        {
            double trace = a + d;
            double det = a * d - b * b;
            double disc = trace * trace - 4 * det;
            if (disc < 0) disc = 0;                 // numericky sum u skoro izotropniho pripadu
            double root = Math.Sqrt(disc);
            double max = 0.5 * (trace + root);
            double min = 0.5 * (trace - root);

            if (Math.Abs(b) > 1e-15)
                return new Eigen2Result(min, max, Math.Atan2(b, min - d), Math.Atan2(b, max - d));

            // Diagonalni pripad - osy jsou souradnicove.
            bool aIsMin = a <= d;
            return new Eigen2Result(min, max,
                                    aIsMin ? 0.0 : Math.PI / 2,
                                    aIsMin ? Math.PI / 2 : 0.0);
        }
    }
}
```

- [x] **Krok 4: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~CorrelationCovarianceTests
```

Očekávané: PASS (8 testů).

Kdyby test `PrimaCesta_PodelnaSigmaJeVyrazneVetsiNezPricna` selhal na příliš malém poměru,
**neupravuj toleranci** — je to jádro návrhu. Zkontroluj místo toho `HessianStepM`: příliš malý krok
měří kvantizaci rastru místo zakřivení maxima.

**Poznámka k historii tohoto tasku (2026-08-19):** první implementace tady spadla a odhalila
skutečnou vadu v návrhu — původní verze vyžadovala striktně pozitivně definitní `−H` (Cholesky)
a na přímé cestě, kde je podélné zakřivení **přesně nula**, zahazovala celý výsledek. To by
znamenalo, že korelátor na přímé cestě neposílá nic, tedy ani příčnou korekci, která je jeho hlavní
výstup. Odtud degradovaná cesta `FromCurvature` a regresní test
`PrimaCesta_MaMaximumIKdyzZakriveniPodelChybi`. Naměřené hodnoty u T-křižovatky z té doby:
`SigmaTight` 0,123 m, `SigmaLoose` 0,294 m, osa 90,8°, `SigmaPhi` 0,032 rad.

---

## Task 7: `AxisOffsetMeasurement` — skalární měření polohy podél osy

Šev do EKF. `PositionMeasurement` má `R` jen diagonální v osách světa, takže anizotropní kovarianci
otočenou do rámce cesty tam nedostaneš. Skalární měření podél zadané osy to řeší **konstrukcí**:
„podél nevím" se vyjádří tím, že se ta osa buď pošle s velkou σ, nebo vůbec.

**Files:**
- Modify: `Src/ARBot.Common/Fusion/Measurements.cs` (přidání třídy na konec, před uzavírací `}`)
- Test: `Src/ARBot.Common.Tests/Fusion/AxisOffsetMeasurementTests.cs`

**Interfaces:**
- Consumes: `IMeasurement`, `EKFModel.IX`, `EKFModel.IY`, `GateMode`.
- Produces: `AxisOffsetMeasurement(double axisX, double axisY, double value, double std, DateTime t, string source)`
  s `GateThreshold` a `GateMode` jako nastavitelnými vlastnostmi.

- [x] **Krok 1: Napiš padající testy**

Vytvoř `Src/ARBot.Common.Tests/Fusion/AxisOffsetMeasurementTests.cs`:

```csharp
using System;
using ARBot.Common.Fusion;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Testy skalarniho merenia polohy podel osy (viz doc/map-correlation-localization.md).
    /// Slouzi korelaci s mapou: dve merenia po vlastnich osach kovariance misto jedne
    /// PositionMeasurement s diagonalni R.
    /// </summary>
    [TestFixture]
    public class AxisOffsetMeasurementTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0);

        private static Vector<double> State(double x, double y, double theta = 0)
            => Vector<double>.Build.DenseOfArray(new[] { x, y, theta, 0.0, 0.0 });

        [Test]
        public void Predict_JeSkalarniProjekceNaOsu()
        {
            // Osa mirici na severovychod, normovana.
            var m = new AxisOffsetMeasurement(1, 1, value: 0, std: 0.1, T0, "MapCorr");

            var hx = m.Predict(State(3.0, 4.0));

            Assert.That(hx.Count, Is.EqualTo(1));
            Assert.That(hx[0], Is.EqualTo((3.0 + 4.0) / Math.Sqrt(2.0)).Within(1e-9));
        }

        [Test]
        public void Jacobian_MaJenSlozkyPolohy()
        {
            var m = new AxisOffsetMeasurement(0, 1, value: 0, std: 0.1, T0, "MapCorr");

            var h = m.Jacobian(State(0, 0));

            Assert.That(h.RowCount, Is.EqualTo(1));
            Assert.That(h[0, EKFModel.IX], Is.EqualTo(0.0).Within(1e-12));
            Assert.That(h[0, EKFModel.IY], Is.EqualTo(1.0).Within(1e-12));
            Assert.That(h[0, EKFModel.ITh], Is.EqualTo(0.0));
            Assert.That(h[0, EKFModel.IV], Is.EqualTo(0.0));
            Assert.That(h[0, EKFModel.IW], Is.EqualTo(0.0));
        }

        [Test]
        public void OsaSeNormuje()
        {
            // Nenormovana osa (dlouha 5) musi dat tentyz jakobian jako normovana.
            var m = new AxisOffsetMeasurement(3, 4, value: 0, std: 0.1, T0, "MapCorr");

            var h = m.Jacobian(State(0, 0));

            Assert.That(h[0, EKFModel.IX], Is.EqualTo(0.6).Within(1e-12));
            Assert.That(h[0, EKFModel.IY], Is.EqualTo(0.8).Within(1e-12));
        }

        [Test]
        public void NulovaOsa_Vyhodi()
        {
            Assert.That(() => new AxisOffsetMeasurement(0, 0, 0, 0.1, T0, "MapCorr"),
                        Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void NoiseCovariance_JeCtverecSigmy()
        {
            var m = new AxisOffsetMeasurement(1, 0, value: 0, std: 0.25, T0, "MapCorr");

            Assert.That(m.NoiseCovariance[0, 0], Is.EqualTo(0.0625).Within(1e-12));
        }

        [Test]
        public void MereniPodelX_KorigujeXaNechaY()
        {
            // Klicove tvrzeni: merenie podel jedne osy nesmi hybat kolmou slozkou.
            var model = new EKFModel();
            model.Update(new PositionMeasurement(0, 0, 0.5, 0.5, T0, "GPS"));

            for (int i = 0; i < 100; i++)
            {
                model.Predict(0.1);
                // "Skutecna X je 4" - rikame to jen podel osy X.
                model.Update(new AxisOffsetMeasurement(1, 0, value: 4.0, std: 0.1,
                                                      T0.AddSeconds(i * 0.1), "MapCorr"));
            }

            var s = model.Current(T0.AddSeconds(10));
            Assert.That(s.X, Is.EqualTo(4.0).Within(0.2));
            Assert.That(s.Y, Is.EqualTo(0.0).Within(0.2), "Merenie podel X nesmi tahnout Y.");
        }

        [Test]
        public void MereniPodelOtoceneOsy_NechaKolmouSlozku()
        {
            // TOHLE je vlastni test tvrzeni "merenie podel osy nehybe kolmou slozkou".
            // MereniPodelX_KorigujeXaNechaY ho NEOVERUJE: osa (1,0) je zarovnana se svetem a pri
            // theta = 0 zustava P_xy presne nulove, takze Y nehne ani spatne spocteny jakobian -
            // ten test by prosel i s vadnou implementaci.
            // Osa (3,4)/5 neni ani zarovnana, ani symetricka, takze spatne normovany NEBO prohozeny
            // jakobian kolmou slozku posune a test to pozna.
            var model = new EKFModel();
            const double ax = 0.6, ay = 0.8;      // (3,4)/5
            const double target = 4.0;            // 4 m podel osy, kolmo nic

            // Schvalne BEZ Predict: pohybovy model pri theta = 0 pridava sum jen do X (Q[IX,IV]),
            // cimz by translacni blok P prestal byt izotropni a kolma slozka by se pohnula
            // LEGITIMNE (skrz korelaci v P). Bez predikce zustava P izotropni a tvrzeni je ciste.
            for (int i = 0; i < 200; i++)
                model.Update(new AxisOffsetMeasurement(3, 4, target, 0.05,
                                                      T0.AddSeconds(i * 0.01), "MapCorr"));

            var s = model.Current(T0.AddSeconds(2));
            double along = ax * s.X + ay * s.Y;
            double across = -ay * s.X + ax * s.Y;

            Assert.That(along, Is.EqualTo(target).Within(0.05), "Slozka PODEL osy se ma zkorigovat.");
            Assert.That(across, Is.EqualTo(0.0).Within(1e-9), "Kolma slozka se hybat nesmi.");
        }

        [Test]
        public void Residual_JeRozdilBezZabaleni()
        {
            // Poloha neni uhel, takze se nic nezabaluje - jen rozdil.
            var m = new AxisOffsetMeasurement(1, 0, value: 5.0, std: 0.1, T0, "MapCorr");

            var res = m.Residual(m.Value, m.Predict(State(3.0, 0.0)));

            Assert.That(res[0], Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public void VychoziGateMode_JeReject()
        {
            var m = new AxisOffsetMeasurement(1, 0, 0, 0.1, T0, "MapCorr");

            Assert.That(m.GateMode, Is.EqualTo(GateMode.Reject));
            Assert.That(m.GateThreshold, Is.Null, "Bez explicitniho prahu se negatuje.");
        }
    }
}
```

- [x] **Krok 2: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~AxisOffsetMeasurementTests
```

Očekávané: chyba překladu `CS0246: The type or namespace name 'AxisOffsetMeasurement' could not be found`.

- [x] **Krok 3: Implementuj měření**

Do `Src/ARBot.Common/Fusion/Measurements.cs` přidej na konec (uvnitř namespace, za poslední třídu):

```csharp
    /// <summary>
    /// Merenie polohy PODEL JEDNE OSY: <c>h(x) = u . p</c>, kde <c>u</c> je jednotkovy vektor osy
    /// a <c>p = (X, Y)</c>. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>K cemu:</b> korelace s mapou zna polohu dobre v jednom smeru a spatne v kolmem
    /// (na prime ceste napric ano, podel ne). <see cref="PositionMeasurement"/> ma R jen
    /// DIAGONALNI v osach sveta, takze otocenou anizotropni kovarianci nepobere. Dve tato merenia
    /// po vlastnich osach kovariance to resi exaktne - a "podel nevim" je vyjadreno tim, ze se ta
    /// osa posle s velkou sigmou nebo vubec, ne trikem s nekonecnem.</para>
    /// </summary>
    public class AxisOffsetMeasurement : IMeasurement
    {
        private readonly Vector<double> z;
        private readonly Matrix<double> r;
        private readonly double ax;
        private readonly double ay;

        public DateTime TimeStamp { get; }
        public string Source { get; }
        public double? GateThreshold { get; set; }
        public GateMode GateMode { get; set; } = GateMode.Reject;

        /// <param name="axisX">Slozka osy na vychod (nemusi byt normovana).</param>
        /// <param name="axisY">Slozka osy na sever (nemusi byt normovana).</param>
        /// <param name="value">Namerena projekce polohy na osu [m].</param>
        /// <param name="std">Sigma merenia [m].</param>
        /// <param name="t">Cas porizeni.</param>
        /// <param name="source">Nazev zdroje pro logovani.</param>
        public AxisOffsetMeasurement(double axisX, double axisY, double value, double std,
                                     DateTime t, string source)
        {
            double len = Math.Sqrt(axisX * axisX + axisY * axisY);
            if (!(len > 0) || double.IsNaN(len) || double.IsInfinity(len))
                throw new ArgumentException("Osa merenia musi byt nenulovy konecny vektor.", nameof(axisX));

            ax = axisX / len;
            ay = axisY / len;
            z = Vector<double>.Build.Dense(1, value);
            r = Matrix<double>.Build.Dense(1, 1, std * std);
            TimeStamp = t;
            Source = source;
        }

        public Vector<double> Value => z;
        public Matrix<double> NoiseCovariance => r;

        public Vector<double> Predict(Vector<double> x)
            => Vector<double>.Build.Dense(1, ax * x[EKFModel.IX] + ay * x[EKFModel.IY]);

        public Matrix<double> Jacobian(Vector<double> x)
        {
            var H = Matrix<double>.Build.Dense(1, x.Count);
            H[0, EKFModel.IX] = ax;
            H[0, EKFModel.IY] = ay;
            return H;
        }

        public Vector<double> Residual(Vector<double> z, Vector<double> hx) => z - hx;
    }
```

- [x] **Krok 4: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~AxisOffsetMeasurementTests
```

Očekávané: PASS (9 testů).

- [x] **Krok 5: Ověř, že se nic nerozbilo ve zbytku fúze**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~ARBot.Common.Tests.Fusion
```

Očekávané: PASS (všechny testy fúze).

---

## Task 8: `MapCorrelationResult` — výsledek a rozhodnutí poslat/mlčet

Všechna pravidla „kdy korelátor mlčí" na jednom místě, testovaná bez vlákna a bez fúze.

**Files:**
- Create: `Src/ARBot.Common/Localization/MapCorrelationResult.cs`
- Modify: `Src/ARBot.Common/Localization/CorrelationCovariance.cs` (přidání `ForTest`)
- Test: `Src/ARBot.Common.Tests/Localization/MapCorrelationResultTests.cs`

**Interfaces:**
- Consumes: `ScanResult` (Task 5), `CorrelationCovariance` (Task 6), `MapCorrelatorConfig` (Task 3).
- Produces:
  - `MapCorrelationReason` (`Ok`, `TooFewEvidence`, `LowScore`, `Ambiguous`, `OffsetTooLarge`, `NoPeak`)
  - `MapCorrelationResult.From(DateTime t, ScanResult scan, CorrelationCovariance cov, int evidenceCells, double rivalAlongTight, double rivalAlongLoose, MapCorrelatorConfig cfg)`
  - pole `TimeStamp`, `Dx`, `Dy`, `Phi`, `Score`, `SecondBestScore`, `SigmaTight`, `SigmaLoose`,
    `TightAxisAngle`, `SigmaPhi`, `EvidenceCells`, `Candidates`, `EmitTightAxis`, `EmitLooseAxis`,
    `EmitHeading`, `Reason`, `ProcessingTime`; vlastnost `Emitted`
  - (metoda `ToLogMessage()` se přidá v Tasku 9)

- [x] **Krok 1: Napiš padající testy**

Vytvoř `Src/ARBot.Common.Tests/Localization/MapCorrelationResultTests.cs`:

```csharp
using System;
using ARBot.Common.Localization;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy pravidel "kdy korelator mlci" (viz doc/map-correlation-localization.md).
/// Poradi pravidel je soucasti kontraktu - proto se testuje i ono.
/// </summary>
public class MapCorrelationResultTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Sken s dobrym skore, jednoznacny, s malym posunem.</summary>
    private static ScanResult GoodScan()
        => new ScanResult
        {
            Dx = 0.1, Dy = 0.5, Phi = 0.01,
            Score = 0.9, CoarsePeakScore = 0.88,
            Candidates = 100,
        };

    /// <summary>Kovariance s dobre urcenou jednou osou a spatne urcenou druhou.</summary>
    private static CorrelationCovariance GoodCov(double sigmaTight = 0.1, double sigmaLoose = 2.0,
                                                 double sigmaPhi = 0.02)
        => CorrelationCovariance.ForTest(sigmaTight, sigmaLoose, Math.PI / 2, sigmaPhi);

    [Test]
    public void DobryVstup_JeOkAPosleVse()
    {
        var cfg = new MapCorrelatorConfig();
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(), evidenceCells: 5000, rivalAlongTight: 0.2, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok));
        Assert.That(r.EmitTightAxis, Is.True);
        Assert.That(r.EmitLooseAxis, Is.True);
        Assert.That(r.EmitHeading, Is.True);
        Assert.That(r.Emitted, Is.True);
    }

    [Test]
    public void MaloDukazu_Mlci()
    {
        var cfg = new MapCorrelatorConfig { MinEvidenceCells = 400 };
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(), evidenceCells: 399, rivalAlongTight: 0.2, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.TooFewEvidence));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void NizkeSkore_Mlci()
    {
        var cfg = new MapCorrelatorConfig { MinScore = 0.25 };
        var scan = GoodScan();
        scan.Score = 0.24;
        scan.CoarsePeakScore = 0.24;

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.LowScore));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void BlizkyKonkurent_JeNejednoznacne()
    {
        var cfg = new MapCorrelatorConfig { AmbiguityMargin = 0.10 };
        var scan = GoodScan();
        scan.CoarsePeakScore = 0.88;

        // Konkurent PODEL URCENE OSY je skore blizko maxima (rozdil 0,03 < 0,10) => nejednoznacne.

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, rivalAlongTight: 0.85, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ambiguous));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void ZadnyKonkurentPodelOsy_Nevadi()
    {
        var cfg = new MapCorrelatorConfig();
        var scan = GoodScan();

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, double.NegativeInfinity, double.NegativeInfinity, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok));
    }

    [Test]
    public void PrilisVelkyPosun_Mlci()
    {
        var cfg = new MapCorrelatorConfig { MaxOffsetM = 2.0 };
        var scan = GoodScan();
        scan.Dx = 1.8; scan.Dy = 1.8;   // norma 2,55 m

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), 5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.OffsetTooLarge));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void ZadneMaximum_Mlci()
    {
        var cfg = new MapCorrelatorConfig();
        var r = MapCorrelationResult.From(T0, GoodScan(), CorrelationCovariance.NoPeak(), 5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.NoPeak));
        Assert.That(r.Emitted, Is.False);
    }

    [Test]
    public void SigmaNadStropem_VynechaJenTuOsu()
    {
        var cfg = new MapCorrelatorConfig { SigmaCeilingM = 1.0 };
        // Podelna osa je horsi nez strop, pricna ne - typicky pripad na prime ceste.
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(sigmaTight: 0.1, sigmaLoose: 4.0),
                                          5000, 0.2, 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok));
        Assert.That(r.EmitTightAxis, Is.True);
        Assert.That(r.EmitLooseAxis, Is.False, "Neurcena osa se posilat nesmi.");
        Assert.That(r.Emitted, Is.True, "Cyklus stale poslal pricnou korekci.");
    }

    [Test]
    public void KonkurentPodelVolneOsy_VynechaJenTuOsu()
    {
        // Konkurent podel VOLNE osy nediskvalifikuje cely cyklus - rika jen, ze prave tahle osa je
        // nespolehliva. Bez tohoto pravidla by sla do fuze podelna korekce, kterou nehlida nic
        // (konkurent se meri podel URCENE osy) - a to je nebezpecne prave tam, kde SigmaLoose vyjde
        // omylem konecna. Viz "falesna podelna jistota" v doc/map-correlation-localization.md.
        var cfg = new MapCorrelatorConfig { AmbiguityMargin = 0.10 };
        var scan = GoodScan();
        scan.CoarsePeakScore = 0.88;

        // Obe osy pod stropem, ale volna ma blizkeho konkurenta (0,85 > 0,88 - 0,10).
        var r = MapCorrelationResult.From(T0, scan, GoodCov(sigmaTight: 0.1, sigmaLoose: 0.3),
                                          5000, rivalAlongTight: 0.2, rivalAlongLoose: 0.85, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.Ok),
                    "Konkurent podel VOLNE osy nesmi shodit cely cyklus.");
        Assert.That(r.EmitTightAxis, Is.True, "Urcena osa je v poradku a ma se poslat.");
        Assert.That(r.EmitLooseAxis, Is.False, "Nespolehliva volna osa se ma vynechat.");
        Assert.That(r.Emitted, Is.True);
    }

    [Test]
    public void SigmaKurzuNadStropem_VynechaJenKurz()
    {
        var cfg = new MapCorrelatorConfig { SigmaCeilingHeadingRad = 0.01 };
        var r = MapCorrelationResult.From(T0, GoodScan(), GoodCov(sigmaPhi: 0.05), 5000, 0.2, 0.2, cfg);

        Assert.That(r.EmitHeading, Is.False);
        Assert.That(r.EmitTightAxis, Is.True);
    }

    [Test]
    public void PoradiPravidel_MaloDukazuPredNizkymSkore()
    {
        // Kdyz plati oba duvody, hlasi se ten prvni - jinak by se v telemetrii ztratil
        // rozdil mezi "nemam data" a "mam data a nesouhlasi".
        var cfg = new MapCorrelatorConfig { MinEvidenceCells = 400, MinScore = 0.25 };
        var scan = GoodScan();
        scan.Score = 0.0;
        scan.CoarsePeakScore = 0.0;

        var r = MapCorrelationResult.From(T0, scan, GoodCov(), evidenceCells: 10, rivalAlongTight: 0.2, rivalAlongLoose: 0.2, cfg);

        Assert.That(r.Reason, Is.EqualTo(MapCorrelationReason.TooFewEvidence));
    }

    [Test]
    public void OpisujeVstupyDoVysledku()
    {
        var cfg = new MapCorrelatorConfig();
        var scan = GoodScan();
        var r = MapCorrelationResult.From(T0, scan, GoodCov(0.1, 2.0, 0.02), 5000, 0.31, 0.2, cfg);

        Assert.That(r.TimeStamp, Is.EqualTo(T0));
        Assert.That(r.Dx, Is.EqualTo(scan.Dx));
        Assert.That(r.Dy, Is.EqualTo(scan.Dy));
        Assert.That(r.Phi, Is.EqualTo(scan.Phi));
        Assert.That(r.Score, Is.EqualTo(scan.Score));
        Assert.That(r.SecondBestScore, Is.EqualTo(0.31), "Do vysledku jde konkurent PODEL OSY, ne pole ze skenu.");
        Assert.That(r.Candidates, Is.EqualTo(scan.Candidates));
        Assert.That(r.EvidenceCells, Is.EqualTo(5000));
        Assert.That(r.SigmaTight, Is.EqualTo(0.1));
        Assert.That(r.SigmaLoose, Is.EqualTo(2.0));
        Assert.That(r.SigmaPhi, Is.EqualTo(0.02));
    }
}
```

- [x] **Krok 2: Přidej testovací tovární metodu do `CorrelationCovariance`**

Testy potřebují vyrobit kovarianci s konkrétními σ bez skórování. Do
`Src/ARBot.Common/Localization/CorrelationCovariance.cs` přidej za `NoPeak()`:

```csharp
        /// <summary>
        /// Kovariance se zadanymi hodnotami - JEN PRO TESTY pravidel nad vysledkem, aby nemusely
        /// stavet cely oblak a rastr. V provozu se pouziva <see cref="Estimate"/>.
        /// </summary>
        public static CorrelationCovariance ForTest(double sigmaTight, double sigmaLoose,
                                                    double tightAxisAngle, double sigmaPhi)
            => new CorrelationCovariance(sigmaTight, sigmaLoose, tightAxisAngle, sigmaPhi, hasPeak: true);
```

- [x] **Krok 3: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~MapCorrelationResultTests
```

Očekávané: chyba překladu `CS0246` pro `MapCorrelationResult` a `MapCorrelationReason`.

- [x] **Krok 4: Implementuj výsledek a pravidla**

Vytvoř `Src/ARBot.Common/Localization/MapCorrelationResult.cs`:

```csharp
using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Proc korelace (ne)poslala korekci. Viz doc/map-correlation-localization.md.
    /// Jde do zpravy, aby bylo v telemetrii videt PROC se nekorigovalo.
    /// </summary>
    public enum MapCorrelationReason : byte
    {
        /// <summary>Shoda je pouzitelna; co se posle, rozhoduji stropy sigma.</summary>
        Ok = 0,

        /// <summary>Prilis malo dukaznich bunek - kamera jeste nedodala dost semantiky.</summary>
        TooFewEvidence = 1,

        /// <summary>Skore pod prahem - robot nejspis neni na mapovane ceste.</summary>
        LowScore = 2,

        /// <summary>Vzdaleny konkurent je skore blizko maxima (soubezna cesta, symetricka scena).</summary>
        Ambiguous = 3,

        /// <summary>Nalezeny posun je vetsi, nez se poza smi mylit - hlasi se ztrata lokalizace.</summary>
        OffsetTooLarge = 4,

        /// <summary>Zakriveni skore neodpovida maximu (plocha, sedlo, sum).</summary>
        NoPeak = 5,
    }

    /// <summary>
    /// Vysledek jednoho cyklu korelace gridu s mapou vcetne rozhodnuti, co poslat do fuze.
    /// Viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class MapCorrelationResult
    {
        /// <summary>Cas, ke kteremu vysledek plati (cas snapshotu gridu).</summary>
        public DateTime TimeStamp;

        /// <summary>Nalezeny posun na vychod [m]: skutecna poloha = odhad + Dx.</summary>
        public double Dx;

        /// <summary>Nalezeny posun na sever [m].</summary>
        public double Dy;

        /// <summary>Nalezena chyba kurzu [rad]: skutecny kurz = odhad + Phi.</summary>
        public double Phi;

        /// <summary>Skore shody v maximu (-1..1); zaroven metrika kvality.</summary>
        public double Score;

        /// <summary>Skore konkurenta podel URCENE osy (viz <see cref="CorrelationScorer.BestRivalAlongAxis"/>).</summary>
        public double SecondBestScore;

        /// <summary>Sigma lepe urcene osy posunu [m].</summary>
        public double SigmaTight;

        /// <summary>Sigma hore urcene osy posunu [m].</summary>
        public double SigmaLoose;

        /// <summary>Smer lepe urcene osy [rad], matematicky.</summary>
        public double TightAxisAngle;

        /// <summary>Sigma kurzu [rad].</summary>
        public double SigmaPhi;

        /// <summary>Kolik bunek gridu vstoupilo do korelace.</summary>
        public int EvidenceCells;

        /// <summary>Kolik kandidatu se vyhodnotilo (diagnostika ceny).</summary>
        public int Candidates;

        /// <summary>Poslat merenie podel lepe urcene osy?</summary>
        public bool EmitTightAxis;

        /// <summary>Poslat merenie podel hore urcene osy?</summary>
        public bool EmitLooseAxis;

        /// <summary>Poslat korekci kurzu?</summary>
        public bool EmitHeading;

        /// <summary>Proc se (ne)korigovalo.</summary>
        public MapCorrelationReason Reason;

        /// <summary>Doba vypoctu cyklu.</summary>
        public TimeSpan ProcessingTime;

        /// <summary>Poslalo se aspon neco?</summary>
        public bool Emitted => EmitTightAxis || EmitLooseAxis || EmitHeading;

        /// <summary>
        /// Slozi vysledek a rozhodne, co poslat.
        ///
        /// <para><b>Poradi pravidel je soucast kontraktu</b> (a je testovane): malo dukazu -&gt;
        /// nizke skore -&gt; prilis velky posun -&gt; zadne maximum -&gt; nejednoznacnost. Diky tomu
        /// se v telemetrii nesplete "nemam data" s "mam data a nesouhlasi". Nejednoznacnost je
        /// POSLEDNI schvalne: konkurent se meri podel URCENE osy, a ta bez maxima neexistuje.</para>
        ///
        /// <para>Stropy sigma se posuzuji PER OSU, takze bezny cyklus na prime ceste posle jen
        /// pricnou korekci a podelnou vynecha.</para>
        /// </summary>
        /// <param name="rivalAlongTight">Skore nejlepsiho konkurenta posunuteho PODEL URCENE osy
        /// (<see cref="CorrelationScorer.BestRivalAlongAxis"/>). Blizky konkurent tady znamena, ze
        /// registrace muze sedet na JINE ceste - potlaci se cely cyklus. Bez maxima predej
        /// <c>double.NegativeInfinity</c>.</param>
        /// <param name="rivalAlongLoose">Skore konkurenta podel VOLNE (kolme) osy. Blizky konkurent
        /// tady znamena jen to, ze TA JEDNA osa je nespolehliva - potlaci se pouze ona, zbytek
        /// cyklu jde dal. Kdyz se volna osa neposila (sigma nad stropem), muze byt
        /// <c>double.NegativeInfinity</c>.</param>
        public static MapCorrelationResult From(DateTime t, ScanResult scan, CorrelationCovariance cov,
                                                int evidenceCells, double rivalAlongTight,
                                                double rivalAlongLoose, MapCorrelatorConfig cfg)
        {
            if (scan == null) throw new ArgumentNullException(nameof(scan));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            var r = new MapCorrelationResult
            {
                TimeStamp = t,
                Dx = scan.Dx,
                Dy = scan.Dy,
                Phi = scan.Phi,
                Score = scan.Score,
                SecondBestScore = rivalAlongTight,
                SigmaTight = cov.SigmaTight,
                SigmaLoose = cov.SigmaLoose,
                TightAxisAngle = cov.TightAxisAngle,
                SigmaPhi = cov.SigmaPhi,
                EvidenceCells = evidenceCells,
                Candidates = scan.Candidates,
            };

            if (evidenceCells < cfg.MinEvidenceCells)
            {
                r.Reason = MapCorrelationReason.TooFewEvidence;
                return r;
            }
            if (scan.Score < cfg.MinScore)
            {
                r.Reason = MapCorrelationReason.LowScore;
                return r;
            }
            if (Math.Sqrt(scan.Dx * scan.Dx + scan.Dy * scan.Dy) > cfg.MaxOffsetM)
            {
                r.Reason = MapCorrelationReason.OffsetTooLarge;
                return r;
            }
            if (!cov.HasPeak)
            {
                r.Reason = MapCorrelationReason.NoPeak;
                return r;
            }
            // Nejednoznacnost se posuzuje AZ ZA NoPeak, protoze konkurent se meri podel osy - a ta
            // bez maxima neexistuje. Konkurent podel URCENE osy potlaci CELY cyklus: kdyz je vedle
            // stejne dobre reseni ve smeru, kde si myslime, ze polohu zname, muze registrace sedet
            // na jine ceste.
            double threshold = scan.CoarsePeakScore - cfg.AmbiguityMargin;
            if (rivalAlongTight > threshold)
            {
                r.Reason = MapCorrelationReason.Ambiguous;
                return r;
            }

            r.Reason = MapCorrelationReason.Ok;
            r.EmitTightAxis = cov.SigmaTight <= cfg.SigmaCeilingM;
            r.EmitHeading = cov.SigmaPhi <= cfg.SigmaCeilingHeadingRad;

            // Volna osa se posila jen kdyz projde stropem A NEMA blizkeho konkurenta. Konkurent
            // podel volne osy nediskvalifikuje cely cyklus - rika jen, ze prave tahle osa je
            // nespolehliva, takze se vynecha samostatne, stejne jako pri prekroceni stropu.
            //
            // Proc to tady vubec je: kdyz vyjde SigmaLoose omylem konecna (viz otevreny ukol
            // "falesna podelna jistota" v doc/map-correlation-localization.md), sla by do fuze
            // podelna korekce, kterou by NEHLIDAL zadny test nejednoznacnosti - konkurent se totiz
            // meri podel URCENE osy. Na prime ceste zarovnane s osami gridu je SigmaLoose nekonecna,
            // takze se tato podminka vubec neuplatni a falesna nejednoznacnost se nemuze vratit.
            r.EmitLooseAxis = cov.SigmaLoose <= cfg.SigmaCeilingM
                              && !(rivalAlongLoose > threshold);
            return r;
        }
    }
}
```

- [x] **Krok 5: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~MapCorrelationResultTests
```

Očekávané: PASS (12 testů).

---

## Task 9: `MapCorrelationMsg` — zpráva pro telemetrii a záznam

**Files:**
- Create: `Src/ARBot.Common/Logs/MapCorrelationMsg.cs`
- Modify: `Src/ARBot.Common/Localization/MapCorrelationResult.cs` (přidání `ToLogMessage()`)
- Modify: `Src/ARBot.Common/Communication/MessageCatalog.cs:61` (registrace vedle `OccupancyGridMsg`)
- Test: `Src/ARBot.Common.Tests/Localization/MapCorrelationMsgTests.cs`

**Interfaces:**
- Consumes: `Message`, `IHasCaptureTime`, `MapCorrelationResult` (Task 8).
- Produces:
  - `MapCorrelationMsg` s **18** poli: `Dx`, `Dy`, `Phi`, `Score`, `SecondBestScore`, `SigmaTight`,
    `SigmaLoose`, `TightAxisAngle`, `SigmaPhi`, `EvidenceCells`, `Candidates`, `Emitted`,
    `EmitTightAxis`, `EmitLooseAxis`, `EmitHeading`, `Reason` (`byte`), `ProcessingMs`, `TimeStamp`
  - `MapCorrelationMsg MapCorrelationResult.ToLogMessage()`

- [x] **Krok 1: Napiš padající testy**

Vytvoř `Src/ARBot.Common.Tests/Localization/MapCorrelationMsgTests.cs`:

```csharp
using System;
using System.IO;
using ARBot.Common.Communication;
using ARBot.Common.Localization;
using ARBot.Common.Logs;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy zpravy korelace s mapou (viz doc/map-correlation-localization.md).
/// Zpravu vyrabi domena metodou ToLogMessage() - konvence CLAUDE.md.
/// </summary>
public class MapCorrelationMsgTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    private static MapCorrelationResult Result()
    {
        var scan = new ScanResult
        {
            Dx = 0.15, Dy = -0.62, Phi = 0.021,
            Score = 0.87, CoarsePeakScore = 0.85, Candidates = 1375,
        };
        var cov = CorrelationCovariance.ForTest(0.12, 2.4, Math.PI / 2, 0.018);
        var r = MapCorrelationResult.From(T0, scan, cov, evidenceCells: 4211, rivalAlongTight: 0.31, rivalAlongLoose: 0.2, new MapCorrelatorConfig());
        r.ProcessingTime = TimeSpan.FromMilliseconds(12.5);
        return r;
    }

    [Test]
    public void ToLogMessage_OpisujeVsechnyUdaje()
    {
        var msg = Result().ToLogMessage();

        Assert.That(msg.TimeStamp, Is.EqualTo(T0));
        Assert.That(msg.Dx, Is.EqualTo(0.15).Within(1e-9));
        Assert.That(msg.Dy, Is.EqualTo(-0.62).Within(1e-9));
        Assert.That(msg.Phi, Is.EqualTo(0.021).Within(1e-9));
        Assert.That(msg.Score, Is.EqualTo(0.87).Within(1e-9));
        Assert.That(msg.SecondBestScore, Is.EqualTo(0.31).Within(1e-9));
        Assert.That(msg.SigmaTight, Is.EqualTo(0.12).Within(1e-9));
        Assert.That(msg.SigmaLoose, Is.EqualTo(2.4).Within(1e-9));
        Assert.That(msg.SigmaPhi, Is.EqualTo(0.018).Within(1e-9));
        Assert.That(msg.EvidenceCells, Is.EqualTo(4211));
        Assert.That(msg.Candidates, Is.EqualTo(1375));
        Assert.That(msg.ProcessingMs, Is.EqualTo(12.5).Within(1e-6));
        Assert.That(msg.Reason, Is.EqualTo((byte)MapCorrelationReason.Ok));
        Assert.That(msg.Emitted, Is.True);
        Assert.That(msg.EmitTightAxis, Is.True);
        Assert.That(msg.EmitLooseAxis, Is.True);
        Assert.That(msg.EmitHeading, Is.True);
    }

    [Test]
    public void JeOdvozenaZprava_NeniPrimarni()
    {
        // Odvozena zprava nesmi nest marker primarniho vstupu (jinak by ji replay bral jako senzor).
        Assert.That(new MapCorrelationMsg() is IPrimaryMessage, Is.False);
    }

    [Test]
    public void CasPorizeniJeCasSnapshotu()
    {
        var msg = Result().ToLogMessage();

        Assert.That(((IHasCaptureTime)msg).CaptureTime, Is.EqualTo(T0));
    }

    [Test]
    public void SerializaceJeObousmerna()
    {
        var original = Result().ToLogMessage();

        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);

        buffer.Position = 0;
        var loaded = new MapCorrelationMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(loaded.TimeStamp, Is.EqualTo(original.TimeStamp));
        Assert.That(loaded.Dx, Is.EqualTo(original.Dx).Within(1e-9));
        Assert.That(loaded.Dy, Is.EqualTo(original.Dy).Within(1e-9));
        Assert.That(loaded.Phi, Is.EqualTo(original.Phi).Within(1e-9));
        Assert.That(loaded.Score, Is.EqualTo(original.Score).Within(1e-9));
        Assert.That(loaded.SecondBestScore, Is.EqualTo(original.SecondBestScore).Within(1e-9));
        Assert.That(loaded.SigmaTight, Is.EqualTo(original.SigmaTight).Within(1e-9));
        Assert.That(loaded.SigmaLoose, Is.EqualTo(original.SigmaLoose).Within(1e-9));
        Assert.That(loaded.TightAxisAngle, Is.EqualTo(original.TightAxisAngle).Within(1e-9));
        Assert.That(loaded.SigmaPhi, Is.EqualTo(original.SigmaPhi).Within(1e-9));
        Assert.That(loaded.EvidenceCells, Is.EqualTo(original.EvidenceCells));
        Assert.That(loaded.Candidates, Is.EqualTo(original.Candidates));
        Assert.That(loaded.Emitted, Is.EqualTo(original.Emitted));
        Assert.That(loaded.EmitTightAxis, Is.EqualTo(original.EmitTightAxis));
        Assert.That(loaded.EmitLooseAxis, Is.EqualTo(original.EmitLooseAxis));
        Assert.That(loaded.EmitHeading, Is.EqualTo(original.EmitHeading));
        Assert.That(loaded.Reason, Is.EqualTo(original.Reason));
        Assert.That(loaded.ProcessingMs, Is.EqualTo(original.ProcessingMs).Within(1e-6));
    }

    [Test]
    public void NekonecnaSigma_PrezijeSerializaci()
    {
        // Pri Reason = NoPeak jsou sigmy PositiveInfinity - zaznam to musi snest.
        var r = MapCorrelationResult.From(T0, new ScanResult { Score = 0.9, CoarsePeakScore = 0.9 },
                                          CorrelationCovariance.NoPeak(), 5000, 0.2, 0.2, new MapCorrelatorConfig());
        var original = r.ToLogMessage();

        var buffer = new MemoryStream();
        using (var bw = new BinaryWriter(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            original.ToData(bw);
        buffer.Position = 0;
        var loaded = new MapCorrelationMsg();
        using (var br = new BinaryReader(buffer, System.Text.Encoding.UTF8, leaveOpen: true))
            loaded.FromData(br);

        Assert.That(double.IsPositiveInfinity(loaded.SigmaTight), Is.True);
        Assert.That(loaded.Reason, Is.EqualTo((byte)MapCorrelationReason.NoPeak));
    }

    [Test]
    public void JeVKataloguZprav()
    {
        // Bez registrace by se zprava pri replay preskocila jako neznamy typ.
        var catalog = MessageCatalog.CommonDefaults();

        Assert.That(catalog.Contains(new MapCorrelationMsg().MsgName), Is.True);
    }
}
```

- [x] **Krok 2: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~MapCorrelationMsgTests
```

Očekávané: chyba překladu `CS0246: The type or namespace name 'MapCorrelationMsg' could not be found`.

- [x] **Krok 3: Vytvoř zprávu**

Vytvoř `Src/ARBot.Common/Logs/MapCorrelationMsg.cs`:

```csharp
using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: vysledek jednoho cyklu korelace occupancy gridu s mapou.
    /// Viz doc/map-correlation-localization.md.
    ///
    /// <para>Nese i pripad, kdy se NEKORIGOVALO - <see cref="Emitted"/> a <see cref="Reason"/>.
    /// Bez toho by v telemetrii nebylo videt, proc korekce chybi.</para>
    /// </summary>
    [Serializable()]
    public class MapCorrelationMsg : Message, IHasCaptureTime
    {
        /// <summary>Nalezeny posun na vychod [m]: skutecna poloha = odhad + Dx.</summary>
        public double Dx;
        /// <summary>Nalezeny posun na sever [m].</summary>
        public double Dy;
        /// <summary>Nalezena chyba kurzu [rad]: skutecny kurz = odhad + Phi.</summary>
        public double Phi;
        /// <summary>Skore shody v maximu (-1..1); zaroven metrika kvality.</summary>
        public double Score;
        /// <summary>Skore konkurenta podel urcene osy (test nejednoznacnosti).</summary>
        public double SecondBestScore;
        /// <summary>Sigma lepe urcene osy posunu [m].</summary>
        public double SigmaTight;
        /// <summary>Sigma hore urcene osy posunu [m].</summary>
        public double SigmaLoose;
        /// <summary>Smer lepe urcene osy [rad], matematicky.</summary>
        public double TightAxisAngle;
        /// <summary>Sigma kurzu [rad].</summary>
        public double SigmaPhi;
        /// <summary>Kolik bunek gridu vstoupilo do korelace.</summary>
        public int EvidenceCells;
        /// <summary>Kolik kandidatu se vyhodnotilo.</summary>
        public int Candidates;
        /// <summary>Poslala se do fuze aspon jedna korekce? (OR pres tri priznaky niz.)</summary>
        public bool Emitted;
        /// <summary>Poslala se korekce podel LEPE urcene osy? Na prime ceste bezny stav: true.</summary>
        public bool EmitTightAxis;
        /// <summary>
        /// Poslala se korekce podel HORE urcene osy? Na prime ceste bezny stav FALSE - podelna sigma
        /// prerostla strop. Bez tohoto priznaku by "poslalo se jen napric" bylo v telemetrii
        /// k nerozeznani od "poslalo se vsechno", a prave to je otazka, kterou se tenhle podsystem
        /// pri ladeni pta nejcasteji.
        /// </summary>
        public bool EmitLooseAxis;
        /// <summary>Poslala se korekce kurzu?</summary>
        public bool EmitHeading;
        /// <summary>Duvod (<c>ARBot.Common.Localization.MapCorrelationReason</c> jako byte).</summary>
        public byte Reason;
        /// <summary>Doba vypoctu cyklu [ms].</summary>
        public double ProcessingMs;
        /// <summary>Cas, ke kteremu vysledek plati (cas snapshotu gridu).</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public MapCorrelationMsg() : base("MapCorrelationMsg", 1)
        {
        }

        /// <summary>Prototyp pro katalog zprav - <see cref="Message.Build"/> je abstraktni, takze
        /// tenhle override je POVINNY (stejne jako u vsech ostatnich <c>*Msg</c>).</summary>
        public override Message Build() => new MapCorrelationMsg();

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Dx);
            bw.Write(Dy);
            bw.Write(Phi);
            bw.Write(Score);
            bw.Write(SecondBestScore);
            bw.Write(SigmaTight);
            bw.Write(SigmaLoose);
            bw.Write(TightAxisAngle);
            bw.Write(SigmaPhi);
            bw.Write(EvidenceCells);
            bw.Write(Candidates);
            bw.Write(Emitted);
            bw.Write(EmitTightAxis);
            bw.Write(EmitLooseAxis);
            bw.Write(EmitHeading);
            bw.Write(Reason);
            bw.Write(ProcessingMs);
            Write(bw, TimeStamp);
        }

        public override void FromData(BinaryReader br)
        {
            Dx = br.ReadDouble();
            Dy = br.ReadDouble();
            Phi = br.ReadDouble();
            Score = br.ReadDouble();
            SecondBestScore = br.ReadDouble();
            SigmaTight = br.ReadDouble();
            SigmaLoose = br.ReadDouble();
            TightAxisAngle = br.ReadDouble();
            SigmaPhi = br.ReadDouble();
            EvidenceCells = br.ReadInt32();
            Candidates = br.ReadInt32();
            Emitted = br.ReadBoolean();
            EmitTightAxis = br.ReadBoolean();
            EmitLooseAxis = br.ReadBoolean();
            EmitHeading = br.ReadBoolean();
            Reason = br.ReadByte();
            ProcessingMs = br.ReadDouble();
            TimeStamp = ReadDateTime(br);
        }
    }
}
```

- [x] **Krok 4: Přidej `ToLogMessage()` do výsledku**

Do `Src/ARBot.Common/Localization/MapCorrelationResult.cs` přidej na konec třídy:

```csharp
        /// <summary>
        /// Snapshot vysledku jako zprava pro telemetrii a zaznam. Konverzi vlastni domena -
        /// zprava zustava pasivni DTO (viz CLAUDE.md).
        /// </summary>
        public Logs.MapCorrelationMsg ToLogMessage()
            => new Logs.MapCorrelationMsg
            {
                Dx = Dx,
                Dy = Dy,
                Phi = Phi,
                Score = Score,
                SecondBestScore = SecondBestScore,
                SigmaTight = SigmaTight,
                SigmaLoose = SigmaLoose,
                TightAxisAngle = TightAxisAngle,
                SigmaPhi = SigmaPhi,
                EvidenceCells = EvidenceCells,
                Candidates = Candidates,
                Emitted = Emitted,
                EmitTightAxis = EmitTightAxis,
                EmitLooseAxis = EmitLooseAxis,
                EmitHeading = EmitHeading,
                Reason = (byte)Reason,
                ProcessingMs = ProcessingTime.TotalMilliseconds,
                TimeStamp = TimeStamp,
            };
```

- [x] **Krok 5: Zaregistruj zprávu do katalogu**

V `Src/ARBot.Common/Communication/MessageCatalog.cs` přidej za řádek s `OccupancyGridMsg`:

```csharp
            c.Register(new OccupancyGridMsg());
            c.Register(new MapCorrelationMsg());
```

- [x] **Krok 6: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~MapCorrelationMsgTests
```

Očekávané: PASS (6 testů).

---

## Task 10: `MapCorrelator` — celý cyklus jako `MessageProcessor`

Sešití všeho předchozího: vlastní vlákno, fronta `DropOldest`, póza z fúze, měření do fúze, zpráva ven.

**Files:**
- Create: `Src/ARBot.Common/Localization/MapCorrelator.cs`
- Test: `Src/ARBot.Common.Tests/Localization/MapCorrelatorTests.cs`

**Interfaces:**
- Consumes: `MessageProcessor` (`Consume`, `EmitDerived`, `OverflowPolicy`),
  `AsyncFusionEngine.GetStateAt` / `Enqueue`, `RoadRaster` (T2), `EvidenceCloud` +
  `MapCorrelatorConfig` (T3), `CorrelationScorer` (T4, T5), `CorrelationCovariance` (T6),
  `AxisOffsetMeasurement` (T7), `MapCorrelationResult` (T8, T9), `Gating.ChiSquareThreshold`.
- Produces:
  - `MapCorrelator(AsyncFusionEngine engine, RoadScene scene, MapCorrelatorConfig config = null, int queueCapacity = 2)`
  - `MapCorrelationResult MapCorrelator.Process(OccupancyGridMsg msg)` — vrací `null`, když se cyklus přeskočil
  - diagnostika `ProcessedCycles`, `DroppedNoPose`, `ThrottledCycles`, `EmittedCorrections`, `LastResult`
  - `MapCorrelatorConfig Config { get; }`

- [x] **Krok 1: Napiš padající testy**

Vytvoř `Src/ARBot.Common.Tests/Localization/MapCorrelatorTests.cs`:

```csharp
using System;
using ARBot.Common.Fusion;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Testy celeho cyklu korelace (viz doc/map-correlation-localization.md).
/// Testuje se PRIMO Process(), ne pres vlakno - vlakno je zodpovednost MessageProcessoru
/// a testovat ho tady by delalo testy nedeterministickymi.
/// </summary>
public class MapCorrelatorTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Fuze s pouzitelnou pozou v case T0 (poloha 0,0, kurz 0).
    ///
    /// <para>Seed je schvalne o 200 ms PRED T0: <c>AsyncFusionEngine.Enqueue</c> zahazuje merenia
    /// s casem <c>&lt;= tBase</c>, a snapshot gridu ma cas presne T0. Kdyby se seedovalo taky v T0,
    /// korelatorem poslana merenia by se do fuze nikdy nedostala a test "posle korekci do fuze" by
    /// selhaval z duvodu, ktery s korelatorem nema nic spolecneho. 200 ms je dost na to, aby T0
    /// zustalo v okne historie. Zjisteno integracnim testem 2026-08-19.</para>
    /// </summary>
    private static AsyncFusionEngine EngineAtOrigin()
    {
        var seed = T0.AddSeconds(-0.2);
        var engine = new AsyncFusionEngine(new EKFModel());
        engine.InitializePosition(0, 0, 0.5, seed);
        engine.Enqueue(new PositionMeasurement(0, 0, 0.5, 0.5, seed, "GPS"));
        engine.Enqueue(new HeadingMeasurement(0, 0.05, seed, "Compass"));
        return engine;
    }

    private static RoadScene StraightRoad()
    {
        var origin = CorrelationTestScenes.Origin();
        return new RoadScene(CorrelationTestScenes.StraightEastRoad(origin), origin);
    }

    [Test]
    public void Process_NajdePricnouChybu()
    {
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0.0, 0.7, 0.0);
        var result = correlator.Process(msg);

        Assert.That(result, Is.Not.Null);
        Assert.That(result.Dy, Is.EqualTo(0.7).Within(0.15));
        Assert.That(correlator.ProcessedCycles, Is.EqualTo(1));
    }

    [Test]
    public void Process_BezPozy_ZahodiSnimek()
    {
        // Prazdna fuze neumi dat pozu -> snimek se zahodi, korelovat proti spatne poze je horsi.
        var engine = new AsyncFusionEngine(new EKFModel());
        var scene = StraightRoad();
        var correlator = new MapCorrelator(engine, scene, CorrelationTestScenes.TestConfig());

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0));

        Assert.That(result, Is.Null);
        Assert.That(correlator.DroppedNoPose, Is.EqualTo(1));
        Assert.That(correlator.ProcessedCycles, Is.EqualTo(0));
    }

    [Test]
    public void Process_DrivNezMinPeriod_Preskoci()
    {
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.MinPeriod = TimeSpan.FromMilliseconds(400);
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var first = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0);
        var second = CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0);
        second.TimeStamp = first.TimeStamp.AddMilliseconds(100);

        Assert.That(correlator.Process(first), Is.Not.Null);
        Assert.That(correlator.Process(second), Is.Null);
        Assert.That(correlator.ThrottledCycles, Is.EqualTo(1));
    }

    [Test]
    public void Process_Vypnuty_NicNeposleDoFuze()
    {
        var engine = EngineAtOrigin();
        int before = engine.Diagnostics().Count;

        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.Enabled = false;
        var correlator = new MapCorrelator(engine, scene, cfg);

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.7, 0));

        Assert.That(result, Is.Not.Null, "Vypnuty korelator ma dal POCITAT a hlasit.");
        Assert.That(correlator.EmittedCorrections, Is.EqualTo(0));
        Assert.That(engine.Diagnostics().Count, Is.EqualTo(before), "Do fuze nesmelo nic prijit.");
    }

    [Test]
    public void Process_Zapnuty_PosleKorekciDoFuze()
    {
        var engine = EngineAtOrigin();
        int before = engine.Diagnostics().Count;

        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.Enabled = true;
        var correlator = new MapCorrelator(engine, scene, cfg);

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.7, 0));

        Assert.That(result.Emitted, Is.True);
        Assert.That(correlator.EmittedCorrections, Is.GreaterThan(0));
        Assert.That(engine.Diagnostics().Count, Is.GreaterThan(before));
    }

    [Test]
    public void Process_MimoMapovanouCestu_Mlci()
    {
        // Grid tvrdi "vsude cesta", mapa tvrdi "nikde" -> zadna pouzitelna shoda.
        var scene = StraightRoad();
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.Enabled = true;
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        // Robot 200 m severne od cesty: rastr je tam cely "neni cesta", grid rikame "cesta".
        var msg = CorrelationTestScenes.GridFromScene(scene, 0, 200, 0, 0, 0);
        for (int i = 0; i < msg.Road.Length; i++)
            msg.Road[i] = (sbyte)Math.Round(-1.0f / msg.Scale);

        var result = correlator.Process(msg);

        Assert.That(result.Emitted, Is.False);
        Assert.That(result.Reason, Is.Not.EqualTo(MapCorrelationReason.Ok));
        Assert.That(correlator.EmittedCorrections, Is.EqualTo(0));
    }

    [Test]
    public void Process_SoubezneCesty_NeposleNic()
    {
        // Vzor se skoro opakuje s periodou 2 m, takze shoda je nejednoznacna a korigovat by
        // znamenalo riskovat preskok na vedlejsi cestu.
        var origin = CorrelationTestScenes.Origin();
        var scene = new RoadScene(CorrelationTestScenes.ParallelRoads(origin), origin);
        var cfg = CorrelationTestScenes.TestConfig();
        cfg.Enabled = true;
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, cfg);

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0, 0));

        Assert.That(result.Reason, Is.EqualTo(MapCorrelationReason.Ambiguous));
        Assert.That(correlator.EmittedCorrections, Is.EqualTo(0));
    }

    [Test]
    public void Process_VyplniDobuVypoctu()
    {
        var scene = StraightRoad();
        var correlator = new MapCorrelator(EngineAtOrigin(), scene, CorrelationTestScenes.TestConfig());

        var result = correlator.Process(CorrelationTestScenes.GridFromScene(scene, 0, 0, 0, 0.5, 0));

        Assert.That(result.ProcessingTime, Is.GreaterThan(TimeSpan.Zero));
        Assert.That(correlator.LastResult, Is.SameAs(result));
    }

    [Test]
    public void Konstruktor_NeplatnaKonfigurace_Vyhodi()
    {
        var cfg = new MapCorrelatorConfig { MapRasterMarginM = 0.1 };   // < SearchRangeM

        Assert.That(() => new MapCorrelator(EngineAtOrigin(), StraightRoad(), cfg),
                    Throws.TypeOf<ArgumentException>());
    }
}
```

- [x] **Krok 2: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~MapCorrelatorTests
```

Očekávané: chyba překladu `CS0246: The type or namespace name 'MapCorrelator' could not be found`.

- [x] **Krok 3: Implementuj korelátor**

Vytvoř `Src/ARBot.Common/Localization/MapCorrelator.cs`:

```csharp
using System;
using System.Diagnostics;
using ARBot.Common.Communication;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Koreluje semanticky kanal occupancy gridu s vozovkou podle mapy a vysledek posila do fuze
    /// jako dve skalarni osova merenia plus kurz. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>Vlakno:</b> je to <see cref="MessageProcessor"/> nad snapshotem gridu
    /// (<see cref="OccupancyGridMsg"/>), tedy VLASTNI vlakno. Nekrade cas planovaci - tik
    /// <c>LocalNavigator</c> smi trvat 15 ms a korelace by se do nej nevesla. Fronta je
    /// <see cref="OverflowPolicy.DropOldest"/> s malou kapacitou: kdyz korelace nestiha, je spravne
    /// zpracovat NEJNOVEJSI snapshot.</para>
    ///
    /// <para><b>Nezna trasu.</b> Mapovou pravdou je cela sit (<see cref="RoadScene"/>). Korelovat
    /// proti vybrane trase by byla potvrzovaci zaujatost - kdyby robot odbocil jinam, prilepilo by
    /// ho to k trase.</para>
    /// </summary>
    public sealed class MapCorrelator : MessageProcessor
    {
        private readonly AsyncFusionEngine engine;
        private readonly RoadScene scene;
        private readonly MapCorrelatorConfig config;
        private readonly Stopwatch sw = new Stopwatch();

        private DateTime lastProcessedAt = DateTime.MinValue;

        /// <summary>Konfigurace (po sestaveni se nemeni).</summary>
        public MapCorrelatorConfig Config => config;

        /// <summary>DIAGNOSTIKA: kolik cyklu se dopocitalo.</summary>
        public long ProcessedCycles { get; private set; }

        /// <summary>DIAGNOSTIKA: kolik snapshotu se zahodilo, protoze fuze neumela dat pozu.</summary>
        public long DroppedNoPose { get; private set; }

        /// <summary>DIAGNOSTIKA: kolik snapshotu se preskocilo kvuli <see cref="MapCorrelatorConfig.MinPeriod"/>.</summary>
        public long ThrottledCycles { get; private set; }

        /// <summary>DIAGNOSTIKA: kolik merenia se celkem poslalo do fuze.</summary>
        public long EmittedCorrections { get; private set; }

        /// <summary>Posledni vysledek (diagnostika pro UI).</summary>
        public MapCorrelationResult LastResult { get; private set; }

        /// <param name="engine">Fuze - dotazuje se na pozu v case snapshotu a posila do ni merenia.</param>
        /// <param name="scene">Vozovka podle mapy (cela sit, ne trasa).</param>
        /// <param name="config">Konfigurace; null = vychozi.</param>
        /// <param name="queueCapacity">Kapacita vstupni fronty (DropOldest); default 2.</param>
        public MapCorrelator(AsyncFusionEngine engine, RoadScene scene,
                             MapCorrelatorConfig config = null, int queueCapacity = 2)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.engine = engine ?? throw new ArgumentNullException(nameof(engine));
            this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
            this.config = config ?? new MapCorrelatorConfig();
            this.config.Validate();
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (!(msg is OccupancyGridMsg grid)) return;

            try
            {
                var result = Process(grid);
                if (result != null) EmitDerived(result.ToLogMessage());
            }
            catch (Exception ex) { Debug.WriteLine($"MapCorrelator: {ex}"); }
        }

        /// <summary>
        /// Jeden cyklus korelace. Vraci <c>null</c>, kdyz se cyklus preskocil (nedostupna poza nebo
        /// <see cref="MapCorrelatorConfig.MinPeriod"/>).
        ///
        /// <para>Verejne schvalne: takhle se da cyklus spustit nad zaznamem i z testu BEZ vlakna,
        /// takze testy zustanou deterministicke.</para>
        /// </summary>
        public MapCorrelationResult Process(OccupancyGridMsg msg)
        {
            if (msg == null) throw new ArgumentNullException(nameof(msg));

            if (lastProcessedAt != DateTime.MinValue && msg.TimeStamp - lastProcessedAt < config.MinPeriod)
            {
                ThrottledCycles++;
                return null;
            }

            // (1) Poza v case snapshotu. null = mimo okno historie -> zahodit; korelovat proti
            //     spatne poze je horsi nez nekorelovat.
            var pose = engine.GetStateAt(msg.TimeStamp);
            if (pose == null)
            {
                DroppedNoPose++;
                return null;
            }

            sw.Restart();

            // (2) Mapa do rastru zarovnaneho s gridem (jednou za cyklus - dal se uz jen indexuje).
            var raster = RoadRaster.Build(scene, msg.OriginX, msg.OriginY, msg.Size, msg.Resolution,
                                          config.MapRasterMarginM);

            // (3) Dukazni bunky ze semantiky (kanal Occ se neucastni).
            var cloud = EvidenceCloud.FromGrid(msg, config.EvidenceThreshold);

            // (4) Hrube-jemne skenovani + (5) kovariance ze zakriveni skore.
            var scorer = new CorrelationScorer(cloud, raster, pose.X, pose.Y);
            var scan = scorer.Scan(config);
            var cov = cloud.Count >= config.MinEvidenceCells
                ? CorrelationCovariance.Estimate(scorer, scan, config)
                : CorrelationCovariance.NoPeak();

            // (6) Konkurencni maxima - test nejednoznacnosti. Bez maxima nema osa smysl, takze se
            //     konkurent nemeri a nejednoznacnost nikdy nezasahne.
            double rivalTight = cov.HasPeak
                ? scorer.BestRivalAlongAxis(scan, cov.TightAxisAngle, config)
                : double.NegativeInfinity;

            // Volna osa se hlida jen kdyz se ma opravdu poslat. Na prime ceste zarovnane s osami
            // gridu je SigmaLoose nekonecna, takze se tenhle vypocet vubec nespusti - dva dalsi
            // desitky vyhodnoceni skore se plati jen tam, kde ta osa neco ovlivni.
            double rivalLoose = cov.HasPeak && cov.SigmaLoose <= config.SigmaCeilingM
                ? scorer.BestRivalAlongAxis(scan, cov.TightAxisAngle + Math.PI / 2, config)
                : double.NegativeInfinity;

            var result = MapCorrelationResult.From(msg.TimeStamp, scan, cov, cloud.Count,
                                                   rivalTight, rivalLoose, config);

            sw.Stop();
            result.ProcessingTime = sw.Elapsed;

            lastProcessedAt = msg.TimeStamp;
            ProcessedCycles++;
            LastResult = result;

            if (config.Enabled) SendMeasurements(result, pose);
            return result;
        }

        /// <summary>
        /// Posle korekce do fuze. Osy jsou VLASTNI osy translacni kovariance, takze R zustava
        /// diagonalni v tom spravnem ramci - viz doc/map-correlation-localization.md.
        /// </summary>
        private void SendMeasurements(MapCorrelationResult r, Fusion.RobotState pose)
        {
            double gate = Gating.ChiSquareThreshold(1);

            // Lepe urcena osa (na ceste typicky napric) a osa k ni kolma.
            double tx = Math.Cos(r.TightAxisAngle), ty = Math.Sin(r.TightAxisAngle);
            double lx = -ty, ly = tx;

            double trueX = pose.X + r.Dx;
            double trueY = pose.Y + r.Dy;

            if (r.EmitTightAxis)
            {
                engine.Enqueue(new AxisOffsetMeasurement(tx, ty, tx * trueX + ty * trueY,
                                                         r.SigmaTight, r.TimeStamp, config.MeasurementSource)
                { GateThreshold = gate });
                EmittedCorrections++;
            }
            if (r.EmitLooseAxis)
            {
                engine.Enqueue(new AxisOffsetMeasurement(lx, ly, lx * trueX + ly * trueY,
                                                         r.SigmaLoose, r.TimeStamp, config.MeasurementSource)
                { GateThreshold = gate });
                EmittedCorrections++;
            }
            if (r.EmitHeading)
            {
                engine.Enqueue(new HeadingMeasurement(pose.Theta + r.Phi, r.SigmaPhi,
                                                      r.TimeStamp, config.MeasurementSource)
                { GateThreshold = gate });
                EmittedCorrections++;
            }
        }
    }
}
```

- [x] **Krok 4: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~MapCorrelatorTests
```

Očekávané: PASS (9 testů).

Kdyby `Process_BezPozy_ZahodiSnimek` selhal (fúze pózu vrátí), ověř `GetStateAt` na prázdném
engine — test pak postav tak, že `msg.TimeStamp` je daleko **před** `T0` (mimo okno historie).

Kdyby `Process_SoubezneCesty_NeposleNic` vrátilo jiný důvod než `Ambiguous` (typicky `LowScore`),
**neupravuj tvrzení testu** — dolaď scénu `ParallelRoads` (parametry `width`, `spacing`, `halfCount`) tak,
aby konkurenční maximum skutečně vzniklo. Smysl testu je, že se nejednoznačnost pozná; kdyby se
uznávala jako „nízké skóre", ztratil by se rozdíl mezi „nejsem na cestě" a „nevím na které jsem".

- [x] **Krok 5: Ověř celou sadu**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64
```

Očekávané: PASS celá sada (nová jádra nesmí rozbít nic existujícího).

---

## Task 11: Pojistka proti skoku pózy v `LocalNavigator`

Po korekci je obsah gridu vůči nové póze posunutý. Malé korekce se vyperou samy (clamp ±5, paměť
~2,5 s), ale **skok** je jiná věc — a nechrání to jen před korelátorem, ale i před znovuzachycením
GPS, které umí skočit o metry.

**Files:**
- Create: `Src/ARBot.Common/Occupancy/PoseJumpDetector.cs`
- Modify: `Src/ARBot.Common/Occupancy/LocalNavigator.cs` (v `Process(CameraFrame)` po získání pózy)
- Test: `Src/ARBot.Common.Tests/Occupancy/PoseJumpDetectorTests.cs`

**Interfaces:**
- Consumes: nic (samostatná třída).
- Produces:
  - `PoseJumpDetector` s `double ToleranceM { get; set; }`, `bool Check(double x, double y, double v, DateTime t)`, `void Reset()`
  - `LocalNavigator.PoseJumpToleranceM { get; set; }`, `LocalNavigator.GridResets { get; }`

- [x] **Krok 1: Napiš padající testy**

Vytvoř `Src/ARBot.Common.Tests/Occupancy/PoseJumpDetectorTests.cs`:

```csharp
using System;
using ARBot.Common.Occupancy;

namespace ARBot.Common.Tests.Occupancy;

/// <summary>
/// Testy detekce skoku pozy (viz doc/map-correlation-localization.md, "Zpetna vazba na grid").
/// Skok = poza se posunula vic, nez vysvetli rychlost.
/// </summary>
public class PoseJumpDetectorTests
{
    private static readonly DateTime T0 = new DateTime(2026, 8, 19, 12, 0, 0, DateTimeKind.Utc);

    [Test]
    public void PrvniPoza_NeniSkok()
    {
        var d = new PoseJumpDetector();

        Assert.That(d.Check(10, 20, v: 1.0, T0), Is.False);
    }

    [Test]
    public void PohybOdpovidajiciRychlosti_NeniSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, v: 2.0, T0);

        // Za 0,5 s pri 2 m/s se ceka 1 m; ujel presne 1 m.
        Assert.That(d.Check(1.0, 0, v: 2.0, T0.AddSeconds(0.5)), Is.False);
    }

    [Test]
    public void PosunNadToleranci_JeSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, v: 0.0, T0);

        // Stoji, a presto se posunul o 2 m.
        Assert.That(d.Check(2.0, 0, v: 0.0, T0.AddSeconds(0.1)), Is.True);
    }

    [Test]
    public void MalaKorekce_NeniSkok()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, v: 0.0, T0);

        // Typicka korekce korelatoru - jednotky cm.
        Assert.That(d.Check(0.05, 0.03, v: 0.0, T0.AddSeconds(0.1)), Is.False);
    }

    [Test]
    public void PohybVzad_SePosuzujePodleVzdalenosti()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, v: 1.0, T0);

        // Absolutni hodnota rychlosti - couvani neni skok.
        Assert.That(d.Check(-0.5, 0, v: -1.0, T0.AddSeconds(0.5)), Is.False);
    }

    [Test]
    public void CasPozadu_NeniSkok()
    {
        // Snimek z druhe kamery muze prijit s casem drive nez predchozi; to neni skok pozy.
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, v: 1.0, T0.AddSeconds(1));

        Assert.That(d.Check(0.1, 0, v: 1.0, T0), Is.False);
    }

    [Test]
    public void Reset_ZapomeneStav()
    {
        var d = new PoseJumpDetector { ToleranceM = 0.5 };
        d.Check(0, 0, v: 0.0, T0);
        d.Reset();

        // Po resetu je dalsi poza znovu "prvni", takze skok nehlasi.
        Assert.That(d.Check(50.0, 0, v: 0.0, T0.AddSeconds(0.1)), Is.False);
    }
}
```

- [x] **Krok 2: Spusť testy a ověř, že padají**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~PoseJumpDetectorTests
```

Očekávané: chyba překladu `CS0246: The type or namespace name 'PoseJumpDetector' could not be found`.

- [x] **Krok 3: Implementuj detektor**

Vytvoř `Src/ARBot.Common/Occupancy/PoseJumpDetector.cs`:

```csharp
using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Pozna, ze se poza posunula VIC, nez vysvetli rychlost - tedy ze nekdo lokalizaci skokem
    /// prepsal (korekce z korelace s mapou, znovuzachyceni GPS). Grid je world-kotveny, takze po
    /// takovem skoku je jeho obsah na spatnem miste a je lepsi ho zahodit.
    /// Viz doc/map-correlation-localization.md ("Zpetna vazba na grid").
    ///
    /// <para>Male korekce (jednotky cm za cyklus) se schvalne NEHLASI - ty se vyperou samy diky
    /// clampu a kratke pameti gridu; resamplovat grid by je jen rozmazalo.</para>
    /// </summary>
    public sealed class PoseJumpDetector
    {
        private bool hasPrevious;
        private double prevX;
        private double prevY;
        private DateTime prevTime;

        /// <summary>O kolik smi poza "pretect" nad to, co vysvetli rychlost, nez je to skok [m].</summary>
        public double ToleranceM { get; set; } = 0.5;

        /// <summary>Zapomene predchozi pozu (dalsi <see cref="Check"/> skok nehlasi).</summary>
        public void Reset() => hasPrevious = false;

        /// <summary>
        /// Zaznamena pozu a vrati <c>true</c>, kdyz je to skok.
        /// </summary>
        /// <param name="x">Poloha na vychod [m].</param>
        /// <param name="y">Poloha na sever [m].</param>
        /// <param name="v">Rychlost ve smeru orientace [m/s] (znamenko nehraje roli).</param>
        /// <param name="t">Cas, ke kteremu poza plati.</param>
        public bool Check(double x, double y, double v, DateTime t)
        {
            if (!hasPrevious)
            {
                Remember(x, y, t);
                return false;
            }

            double dt = (t - prevTime).TotalSeconds;

            // Cas pozadu: snimky dvou kamer maji jine casy grabu a mohou prijit prehozene.
            // To neni skok pozy - jen se stav prepise a jede se dal.
            if (dt <= 0)
            {
                Remember(x, y, t);
                return false;
            }

            double moved = Math.Sqrt((x - prevX) * (x - prevX) + (y - prevY) * (y - prevY));
            double explained = Math.Abs(v) * dt;

            Remember(x, y, t);
            return moved > explained + ToleranceM;
        }

        private void Remember(double x, double y, DateTime t)
        {
            prevX = x; prevY = y; prevTime = t;
            hasPrevious = true;
        }
    }
}
```

- [x] **Krok 4: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~PoseJumpDetectorTests
```

Očekávané: PASS (7 testů).

- [x] **Krok 5: Zapoj detektor do `LocalNavigator`**

V `Src/ARBot.Common/Occupancy/LocalNavigator.cs` přidej k ostatním `private readonly` polím:

```csharp
        private readonly PoseJumpDetector poseJump = new PoseJumpDetector();
```

K veřejným vlastnostem přidej:

```csharp
        /// <summary>
        /// O kolik smi poza pretect nad to, co vysvetli rychlost, nez se grid zahodi [m].
        /// Viz doc/map-correlation-localization.md ("Zpetna vazba na grid").
        /// </summary>
        public double PoseJumpToleranceM
        {
            get => poseJump.ToleranceM;
            set => poseJump.ToleranceM = value;
        }

        /// <summary>DIAGNOSTIKA: kolikrat se grid zahodil kvuli skoku pozy.</summary>
        public long GridResets { get; private set; }
```

A v metodě `Process(CameraFrame frame)` **za** kontrolu `if (pose == null)` (tedy hned před
`try { ProcessCore(...) }`) vlož:

```csharp
            // Skok pozy (korekce z korelace s mapou, znovuzachyceni GPS) znamena, ze obsah gridu je
            // na spatnem miste. Zahodit je bezpecnejsi i levnejsi nez resamplovat.
            if (poseJump.Check(pose.X, pose.Y, pose.V, pose.TimeStamp))
            {
                grid.Clear();
                GridResets++;
            }
```

- [x] **Krok 6: Ověř, že se lokální navigace nerozbila**

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64 --filter FullyQualifiedName~ARBot.Common.Tests.Occupancy
```

Očekávané: PASS. Pokud některý existující test `LocalNavigator` teď selže na prázdném gridu, je to
**skutečné zjištění**: jeho scénář obsahuje skok pózy. Uprav test tak, aby pózu posouval spojitě
(nebo mu nastav `PoseJumpToleranceM` na velkou hodnotu) — ale **do detektoru nezasahuj**.

---

## Task 12: Zapojení do runtime a telemetrie

Poslední díl: korelátor v pipeline aplikace a jeho údaje v telemetrickém pohledu. Testy tady nejsou
(je to složení a UI registr), ověřuje se buildem, celou sadou a v fázi 5 během.

**Files:**
- Modify: `Src/ARBot/Robot/ARBotRuntime.cs` (za blok `GlobalNavigator`, viz `ARBotRuntime.cs:319`)
- Modify: `Src/ARBot/Telemetry/TelemetryColumns.cs` (nová sekce sloupců)

**Interfaces:**
- Consumes: `MapCorrelator` (T10), `MapCorrelationMsg` (T9), `RoadScene` (T1),
  `MapCorrelationReason` (T8).
- Produces: `ARBotRuntime.MapCorrelator { get; private set; }`

- [x] **Krok 1: Zapoj korelátor do pipeline**

V `Src/ARBot/Robot/ARBotRuntime.cs` přidej k veřejným vlastnostem (vedle `GlobalNavigator`):

```csharp
        /// <summary>
        /// Korelace occupancy gridu s mapou (odhad chyby polohy). Bez mapy nevznikne.
        /// Viz doc/map-correlation-localization.md.
        /// </summary>
        public ARBot.Common.Localization.MapCorrelator MapCorrelator { get; private set; }
```

A **za** celý blok `if (RoadNetwork != null && fusionConfig.GeoReference != null) { ... globalNav ... }`
vlož:

```csharp
            // Korelace occupancy gridu s mapou: z posunu mezi semantikou (LRoad) a vozovkou podle
            // OSM se odhadne chyba polohy a kurzu. Vlastni vlakno nad snapshotem gridu, takze tik
            // LocalNavigatoru zustava nedotceny. Nezna trasu - mapovou pravdou je cela sit.
            // Viz doc/map-correlation-localization.md.
            if (RoadNetwork != null && fusionConfig.GeoReference != null)
            {
                var correlator = new ARBot.Common.Localization.MapCorrelator(
                    engine,
                    new RoadScene(RoadNetwork, fusionConfig.GeoReference),
                    new ARBot.Common.Localization.MapCorrelatorConfig());

                MapCorrelator = correlator;
                stages.Add(correlator);
                // Odebira POUZE OccupancyGridMsg z lokalni vrstvy (ne cely Stream - tam tecou
                // CameraFrame s ~1 MB obrazu).
                connections.Add(navigator.Output.Connect(correlator));
                connections.Add(correlator.Output.Connect(stream));
            }
```

Zkontroluj, že soubor má `using ARBot.Common.Maps.OsmNav.Graph;` (kvůli `RoadScene` po Tasku 1);
pokud ne, přidej ho.

- [x] **Krok 2: Přelož aplikaci**

```bash
dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64
```

Očekávané: zelený build. Kdyby `stages` nebo `connections` měly jiné názvy, použij ty skutečné
z okolí bloku `globalNav`.

- [x] **Krok 3: Přidej sloupce do telemetrie**

V `Src/ARBot/Telemetry/TelemetryColumns.cs` přidej za sekci `// --- globalni navigace ---` novou
sekci (`using ARBot.Common.Localization;` doplň k ostatním `using`):

```csharp
            // --- korelace s mapou (odhad polohy) ---
            Num<MapCorrelationMsg>("korel dx [m]", m => m.Dx,
                "Naměřený posun na východ: skutečná poloha = odhad + dx. Trvale nenulová hodnota "
                + "znamená systematickou chybu lokalizace. Viz doc/map-correlation-localization.md."),
            Num<MapCorrelationMsg>("korel dy [m]", m => m.Dy,
                "Naměřený posun na sever: skutečná poloha = odhad + dy."),
            Num<MapCorrelationMsg>("korel fi [°]", m => Deg(m.Phi),
                "Naměřená chyba kurzu: skutečný kurz = odhad + fi."),
            Num<MapCorrelationMsg>("korel skore", m => m.Score,
                "Shoda semantiky gridu s vozovkou podle mapy (-1 až 1). Zároveň metrika kvality: "
                + "pod prahem korelátor mlčí, protože robot nejspíš není na mapované cestě.", "F3"),
            Num<MapCorrelationMsg>("korel konkurent", m => m.SecondBestScore,
                "Skóre nejlepšího vzdáleného konkurenta. Když se přiblíží skóre maxima, je shoda "
                + "nejednoznačná (souběžná cesta) a nekoriguje se.", "F3"),
            Num<MapCorrelationMsg>("korel sig- [m]", m => m.SigmaTight,
                "Sigma LÉPE určené osy posunu — na cestě typicky napříč. Malá hodnota = příčné "
                + "poloze se dá věřit."),
            Num<MapCorrelationMsg>("korel sig+ [m]", m => m.SigmaLoose,
                "Sigma HŮŘE určené osy posunu — na přímé cestě podél. Velká hodnota je správná "
                + "odpověď, ne chyba: podélná poloha bez odbočky není určená."),
            Num<MapCorrelationMsg>("korel sig fi [°]", m => Deg(m.SigmaPhi),
                "Sigma naměřené chyby kurzu."),
            Num<MapCorrelationMsg>("korel bunek", m => m.EvidenceCells,
                "Kolik buněk gridu vstoupilo do korelace. Malé číslo = semantika ještě nemá dost "
                + "dat (souvisí s okluzním pravidlem InShadow).", "F0"),
            Flag<MapCorrelationMsg>("korel", m => m.Emitted,
                "Poslala se do fúze aspoň jedna korekce? Když ne, důvod je ve sloupci „korel duvod“."),
            Flag<MapCorrelationMsg>("korel os-", m => m.EmitTightAxis,
                "Poslala se korekce podél LÉPE určené osy (na cestě typicky napříč)? Na přímé cestě "
                + "je to běžný stav."),
            Flag<MapCorrelationMsg>("korel os+", m => m.EmitLooseAxis,
                "Poslala se korekce podél HŮŘE určené osy (podél cesty)? Na přímé cestě má být "
                + "vypnutá — podélná sigma přeroste strop. Když svítí trvale, něco předstírá "
                + "podélnou jistotu."),
            Flag<MapCorrelationMsg>("korel kurz", m => m.EmitHeading,
                "Poslala se korekce kurzu?"),
            Enum<MapCorrelationMsg, MapCorrelationReason>("korel duvod", m => m.Reason,
                "Proč se (ne)korigovalo: Ok / málo důkazů / nízké skóre / nejednoznačné / "
                + "příliš velký posun / žádné maximum."),
            Num<MapCorrelationMsg>("korel vypocet [ms]", m => m.ProcessingMs,
                "Doba výpočtu jednoho cyklu korelace. Diagnostika zátěže (na ARM je to hlídané).", "F1"),
```

- [x] **Krok 4: Přelož a spusť celou sadu**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
```

Očekávané: zelený build celého řešení.

```bash
dotnet test Src/ARBot.Common.Tests/ARBot.Common.Tests.csproj -p:Platform=x64
```

Očekávané: PASS celá sada.

- [x] **Krok 5: Aktualizuj stavovou tabulku ve specifikaci**

V [map-correlation-localization.md](map-correlation-localization.md) přepiš sekci „Stav" — části
jádro / `AxisOffsetMeasurement` / zpráva + telemetrie / napojení na runtime jsou **hotové**, fáze 4
a 5 (ladění nad záznamy, měření na OrangePI) **nejsou**. Napiš výslovně, co je odsimulované
a co se musí ověřit na zařízení.

- [x] **Krok 6: Doplň DevLog**

Přidej záznam dne do [devlog.md](devlog.md) (nejnovější nahoru, pravidla v hlavičce souboru).

---

## Co plán nepokrývá

Fáze 4 a 5 ze specifikace nejsou implementační kroky, ale měřicí práce, a plán je proto neobsahuje:

- **Fáze 4 — ladění `α`, prahů a σ nad záznamy a virtuálním HW.** Vstupem je telemetrie z Tasku 12:
  porovnat rozptyl `korel dx`/`korel dy` proti hlášené σ a doladit `Alpha`. Sem patří i rozšíření
  `VirtualHwOccupancyTest` o injektovanou chybu pózy.
- **Fáze 5 — měření na OrangePI** (`-p:Platform=OrangePI`) a ověření na HW: doba cyklu korelace,
  chování `DropOldest` fronty a skutečný dopad korekce na jízdu.

Otevřené úkoly (eskalace stavu „lokalizace nepodložená mapou", pyramida rastru, kanál `Occ` jako
druhá evidence, přežití restartu) jsou vedené ve specifikaci, ne tady.
