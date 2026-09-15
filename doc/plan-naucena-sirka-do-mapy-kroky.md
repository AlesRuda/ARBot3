# Naučená šířka cesty do mapy — kroky implementace

> **Pro agentní pracovníky:** POVINNÝ SUB-SKILL: `superpowers:subagent-driven-development`
> (doporučeno) nebo `superpowers:executing-plans`. Kroky mají checkboxy (`- [ ]`) pro sledování.

**Cíl:** Naučenou šířku cesty z `RoadWidthEstimator`u dostat ke **korelaci s mapou** (`RoadScene`)
a do **kreslení** (`MapMsg`), aniž by se sáhlo na graf sítě a aniž by ji dostal renderer virtuální
kamery.

**Architektura:** Runtime drží překryv `nodeId → šířka`, spočítaný z důvěryhodných per-way odhadů
koridoru pravidlem maxima (totéž, co dělá `GraphBuilder`). `RoadScene` a `RoadNetwork.ToLogMessage`
překryv volitelně přijmou; `RoadWidthMapUpdater` (nový stupeň pipeline) rozhoduje, kdy přestavět,
a hotovou **neměnnou** scénu atomicky zamění korelátoru. Graf se nemění, perzistence není.

**Technologie:** .NET 10, C#, NUnit (`Assert.That`), build **vždy `-p:Platform=x64`**.

**Spec:** [plan-naucena-sirka-do-mapy.md](plan-naucena-sirka-do-mapy.md) — plán argumentuje ze
specu, čti obojí.

## Globální omezení

- **Jazyk:** čeština v komentářích, dokumentaci i jménech testů (schéma `Metoda_Scenar_Ocekavani`).
- **Build a testy:** `dotnet build Src/ARBot.slnx -p:Platform=x64`,
  `dotnet test Src/ARBot.Common.Tests -p:Platform=x64`. **Nikdy `AnyCPU`.**
- **Commit jen na výslovný pokyn autora.** Kroky „Commit" v tomhle plánu se **NEPROVÁDĚJÍ** samy od
  sebe — na konci úkolu se ohlásí hotovo a čeká se. *(Pravidlo `CLAUDE.md`, přebíjí výchozí zvyk
  plánů commitovat po každém úkolu.)*
- **Starou implementaci nemazat**, dokud novou nepotvrdí testy.
- **Diagnostika do `Trace`, ne `Debug`** (v Release buildu na zařízení `Debug` nezanechá stopu).
- **Výchozí chování se nesmí změnit**, dokud se nezapne `roadwidthmap=true`.
- Existující výchozí hodnoty ze specu: `RebuildThresholdM = 0,25`, `MinRebuildPeriodSec = 10`,
  parametr `roadwidthmap` výchozí `false`.

---

## Přehled souborů

| soubor | odpovědnost |
|---|---|
| `Src/ARBot.Common/Maps/OsmNav/Graph/RoadWidthOverrides.cs` | **nový** — neměnná mapa `nodeId → šířka` + továrna z per-way odhadů |
| `Src/ARBot.Common/Maps/OsmNav/Graph/RoadScene.cs` | **změna** — volitelný překryv v konstruktoru |
| `Src/ARBot.Common/Maps/OsmNav/Graph/RoadNetwork.cs` | **změna** — volitelný překryv v `ToLogMessage` |
| `Src/ARBot.Common/Localization/MapCorrelator.cs` | **změna** — `Scene` jde vyměnit, `Process` ji bere jednou |
| `Src/ARBot.Runtime/Robot/RoadWidthMapUpdater.cs` | **nový** — stupeň: práh + odstup → nová scéna a `MapMsg` |
| `Src/ARBot.Common/Configuration/ParamRegistry.cs` | **změna** — `roadwidthmap=` |
| `Src/ARBot.Runtime/Robot/ARBotRuntime.cs` | **změna** — založení updateru |

Testy: `Src/ARBot.Common.Tests/OsmNav.Tests/Graph/RoadWidthOverridesTests.cs`,
`Src/ARBot.Common.Tests/OsmNav.Tests/Graph/RoadSceneWidthOverrideTests.cs`,
`Src/ARBot.Common.Tests/Localization/MapCorrelatorSceneSwapTests.cs`,
`Src/ARBot.Runtime.Tests/TestRoadNetwork.cs` (**nový pomocník**),
`Src/ARBot.Runtime.Tests/RoadWidthMapUpdaterTests.cs`,
`Src/ARBot.Runtime.Tests/RoadWidthVirtualCameraIsolationTests.cs`.

> ⚠️ **`ARBot.Runtime.Tests` NEREFERENCUJE `ARBot.Common.Tests`** (jediná reference je
> `ARBot.Runtime`). `CorrelationTestScenes` tam tedy **není k dispozici** — proto úkol 4 začíná
> vlastním pomocníkem `TestRoadNetwork`. Projektovou referenci mezi testovacími projekty
> **nepřidávej**; zatáhla by celou sadu `ARBot.Common.Tests` do runtime testů.

---

## Úkol 1: `RoadWidthOverrides` — pravidlo maxima

**Soubory:**
- Vytvořit: `Src/ARBot.Common/Maps/OsmNav/Graph/RoadWidthOverrides.cs`
- Test: `Src/ARBot.Common.Tests/OsmNav.Tests/Graph/RoadWidthOverridesTests.cs`

**Rozhraní:**
- Konzumuje: `RoadNetwork.Edges` (`IReadOnlyList<Edge>`; `Edge.From/To : Node`, `Edge.WayId : long`),
  `Node.Id : long`, `Node.Width : double`.
- Poskytuje: `RoadWidthOverrides.Build(RoadNetwork network, Func<long, double?> naucenaSirkaCesty)`
  → `RoadWidthOverrides`; `bool TryGet(long nodeId, out double widthM)`; `int Count`;
  `static readonly RoadWidthOverrides Prazdny`.

- [ ] **Krok 1: Napiš padající test**

```csharp
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.OsmNav.Tests.Graph;

/// <summary>
/// Prevod per-way odhadu na sirku UZLU. Pravidlo je TOTEZ, jake uz pouziva GraphBuilder
/// (maximum pres cesty uzlem) - viz doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 2.
/// </summary>
public class RoadWidthOverridesTests
{
    [Test]
    public void NaucenaSirka_prepiseMapovou()
    {
        // StraightEastRoad ma vsechny uzly na mapovych 3 m; cesta se naucila 6 m.
        // (Tenhle test bezi v ARBot.Common.Tests, takze CorrelationTestScenes K DISPOZICI JE.)
        var net = CorrelationTestScenes.StraightEastRoad(CorrelationTestScenes.Origin(), 3.0);
        long wayId = net.Edges[0].WayId;

        var o = RoadWidthOverrides.Build(net, w => w == wayId ? 6.0 : (double?)null);

        Assert.That(o.TryGet(net.Edges[0].From.Id, out double w0), Is.True);
        Assert.That(w0, Is.EqualTo(6.0).Within(1e-9));
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadWidthOverridesTests"`
Očekávej: chyba překladu `CS0246: Typ nebo název oboru názvů RoadWidthOverrides se nenašel.`

- [ ] **Krok 3: Napiš minimální implementaci**

```csharp
using System;
using System.Collections.Generic;

namespace ARBot.Common.Maps.OsmNav.Graph
{
    /// <summary>
    /// Prekryv sirek cest: <c>nodeId → sirka [m]</c>. NEMENNY - predava se konzumentum
    /// (<see cref="RoadScene"/>, <see cref="RoadNetwork.ToLogMessage"/>) misto toho, aby se
    /// prepisoval graf. Viz doc/plan-naucena-sirka-do-mapy.md.
    /// </summary>
    public sealed class RoadWidthOverrides
    {
        private readonly Dictionary<long, double> sirky;

        /// <summary>Prazdny prekryv - konzument se pak chova presne jako bez nej.</summary>
        public static readonly RoadWidthOverrides Prazdny =
            new RoadWidthOverrides(new Dictionary<long, double>());

        private RoadWidthOverrides(Dictionary<long, double> sirky) => this.sirky = sirky;

        /// <summary>Kolik uzlu ma prekryv.</summary>
        public int Count => sirky.Count;

        /// <summary>Sirka uzlu z prekryvu; <c>false</c> = pouzij mapovou hodnotu.</summary>
        public bool TryGet(long nodeId, out double widthM) => sirky.TryGetValue(nodeId, out widthM);

        /// <summary>
        /// Slozi prekryv ze site a naucenych sirek per OSM way.
        ///
        /// <para><b>Pravidlo maxima je TOTEZ, jake pouziva <c>GraphBuilder</c></b> pri stavbe site:
        /// sirka uzlu = max pres cesty, ktere jim vedou. Kazda cesta prispeje svou naucenou sirkou,
        /// a kdyz ji nema, svou MAPOVOU hodnotou - jinak by hrana bez odhadu uzel zuzila.</para>
        ///
        /// <para>⚠️ <b>Znama mez:</b> tam, kde se chodnik dotyka vozovky, podedi chodnik jeji sirku.
        /// Je to dusledek toho, ze sirku nese UZEL, ne cesta (rozhodnuti autora 15. 9. 2026);
        /// s naucenymi sirkami je dopad vetsi nez s uniformnim defaultem. Viz spec.</para>
        /// </summary>
        /// <param name="network">Sit; nemeni se.</param>
        /// <param name="naucenaSirkaCesty">Pro <c>wayId</c> vrati duveryhodnou sirku, nebo
        /// <c>null</c>, kdyz zadnou nema.</param>
        public static RoadWidthOverrides Build(RoadNetwork network, Func<long, double?> naucenaSirkaCesty)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (naucenaSirkaCesty == null) throw new ArgumentNullException(nameof(naucenaSirkaCesty));

            var mapove = new Dictionary<long, double>();
            var vysledek = new Dictionary<long, double>();
            bool nejakaZmena = false;

            foreach (var e in network.Edges)
            {
                double? naucena = naucenaSirkaCesty(e.WayId);
                foreach (var n in new[] { e.From, e.To })
                {
                    double prispevek = naucena ?? n.Width;
                    if (naucena.HasValue) nejakaZmena = true;
                    if (!vysledek.TryGetValue(n.Id, out double cur) || prispevek > cur)
                        vysledek[n.Id] = prispevek;
                    mapove[n.Id] = n.Width;
                }
            }

            if (!nejakaZmena) return Prazdny;

            // Uzly, kde vysla tataz hodnota jako v mape, do prekryvu nepatri - prekryv ma nest
            // jen ROZDIL, aby se na nem dalo poznat, co uz se naucilo.
            foreach (var kv in mapove)
                if (vysledek.TryGetValue(kv.Key, out double w) && Math.Abs(w - kv.Value) < 1e-9)
                    vysledek.Remove(kv.Key);

            return new RoadWidthOverrides(vysledek);
        }
    }
}
```

- [ ] **Krok 4: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadWidthOverridesTests"`
Očekávej: PASS

- [ ] **Krok 5: Doplň zbylé testy pravidla**

```csharp
    [Test]
    public void CestaBezOdhadu_prispejeMapovouSirkou()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);

        var prekryv = RoadWidthOverrides.Build(net, _ => null);

        Assert.That(prekryv.Count, Is.Zero, "bez jedineho odhadu nesmi prekryv nic nest");
    }

    [Test]
    public void SdilenyUzel_dostaneMAXIMUM()
    {
        // T-krizovatka: dve cesty sdili prostredni uzel. Nauci se jen ta sirsi -> uzel ma jeji
        // sirku, protoze GraphBuilder pouziva stejne pravidlo. Je to ZNAMA MEZ, ne vada.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.TJunction(o, 2.0);
        long sirsi = net.Edges[0].WayId;

        var prekryv = RoadWidthOverrides.Build(net, w => w == sirsi ? 6.0 : (double?)null);

        long sdileny = net.Edges[0].To.Id;
        Assert.That(prekryv.TryGet(sdileny, out double w), Is.True);
        Assert.That(w, Is.EqualTo(6.0).Within(1e-9), "maximum, i kdyz druha cesta je uzka");
    }

    [Test]
    public void UzsiOdhadNezMapa_uzelZuzi()
    {
        // Prekryv NENI jednosmerna racna: kdyz je cesta uzsi, nez rika mapa, uzel se ma zuzit.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 4.0);
        long wayId = net.Edges[0].WayId;

        var prekryv = RoadWidthOverrides.Build(net, w => w == wayId ? 2.0 : (double?)null);

        Assert.That(prekryv.TryGet(net.Edges[0].From.Id, out double w2), Is.True);
        Assert.That(w2, Is.EqualTo(2.0).Within(1e-9));
    }
```

- [ ] **Krok 6: Spusť a ověř, že prochází všechny čtyři**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadWidthOverridesTests"`
Očekávej: PASS, 4 testy

- [ ] **Krok 7: Ohlas hotovo a čekej** *(commit až na pokyn autora — viz Globální omezení)*

---

## Úkol 2: `RoadScene` a `ToLogMessage` přijmou překryv

**Soubory:**
- Změnit: `Src/ARBot.Common/Maps/OsmNav/Graph/RoadScene.cs` (konstruktor + `BuildSegments`)
- Změnit: `Src/ARBot.Common/Maps/OsmNav/Graph/RoadNetwork.cs:76` (`ToLogMessage`)
- Test: `Src/ARBot.Common.Tests/OsmNav.Tests/Graph/RoadSceneWidthOverrideTests.cs`

**Rozhraní:**
- Konzumuje: `RoadWidthOverrides.TryGet(long, out double)` z úkolu 1.
- Poskytuje: `new RoadScene(RoadNetwork, GeoReference, RoadWidthOverrides prekryv = null)`;
  `RoadNetwork.ToLogMessage(string? name = null, RoadWidthOverrides prekryv = null)`.
  **Oba nové parametry jsou volitelné a `null` = dnešní chování** — žádné existující volání se nemění.

- [ ] **Krok 1: Napiš padající test**

```csharp
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Tests.Localization;

namespace ARBot.Common.Tests.OsmNav.Tests.Graph;

/// <summary>
/// Prekryv sirek v RoadScene a v MapMsg. BEZ prekryvu se nesmi zmenit NIC - to je pojistka,
/// ze vychozi stav zustal presne takovy, jaky byl. Viz doc/plan-naucena-sirka-do-mapy.md.
/// </summary>
public class RoadSceneWidthOverrideTests
{
    [Test]
    public void SPrekryvem_seCestaRozsiri()
    {
        // Cesta vede na vychod, mapa rika 3 m (polosirka 1,5). Bod 2,5 m stranou je MIMO;
        // po naucenych 6 m (polosirka 3) uz je uvnitr.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var prekryv = RoadWidthOverrides.Build(net, _ => 6.0);

        var bez = new RoadScene(net, o);
        var s = new RoadScene(net, o, prekryv);

        Assert.That(bez.IsRoad(0, 2.5), Is.False, "pri mapovych 3 m je 2,5 m stranou mimo cestu");
        Assert.That(s.IsRoad(0, 2.5), Is.True, "po naucenych 6 m uz je uvnitr");
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadSceneWidthOverrideTests"`
Očekávej: chyba překladu — `RoadScene` nemá konstruktor se třemi parametry.

- [ ] **Krok 3: Uprav `RoadScene`**

V `RoadScene.cs` změň konstruktor a `BuildSegments`:

```csharp
        /// <param name="prekryv">Naucene sirky (<c>nodeId → sirka</c>); <c>null</c> = jen mapa.
        /// Scena je NEMENNA, takze prekryv se zapece pri stavbe - zmena znamena novou instanci
        /// a ta se konzumentovi ATOMICKY zamení. Viz doc/plan-naucena-sirka-do-mapy.md.</param>
        public RoadScene(RoadNetwork network, GeoReference origin, RoadWidthOverrides prekryv = null)
        {
            if (network == null) throw new ArgumentNullException(nameof(network));
            if (origin == null) throw new ArgumentNullException(nameof(origin));

            segments = BuildSegments(network, origin, prekryv);
```

a v `BuildSegments`:

```csharp
        private static Segment[] BuildSegments(RoadNetwork network, GeoReference origin,
                                               RoadWidthOverrides prekryv)
        {
            var list = new List<Segment>(network.Count);
            var seen = new HashSet<(long, long, long)>();

            static double Sirka(Node n, RoadWidthOverrides p)
                => p != null && p.TryGet(n.Id, out double w) ? w : n.Width;

            foreach (var e in network.Edges)
            {
                long a = e.From.Id, b = e.To.Id;
                var key = a < b ? (a, b, e.WayId) : (b, a, e.WayId);
                if (!seen.Add(key)) continue;

                var pa = origin.ToLocal(e.From.Location);
                var pb = origin.ToLocal(e.To.Location);

                list.Add(new Segment(pa.X, pa.Y, pb.X, pb.Y,
                                     (float)(Sirka(e.From, prekryv) * 0.5),
                                     (float)(Sirka(e.To, prekryv) * 0.5)));
            }

            return list.ToArray();
        }
```

- [ ] **Krok 4: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadSceneWidthOverrideTests"`
Očekávej: PASS

- [ ] **Krok 5: Přidej pojistku „bez překryvu se nezměnilo nic" a překryv v `MapMsg`**

```csharp
    [Test]
    public void BezPrekryvu_seChovaStejneJakoDriv()
    {
        // Pojistka proti tomu, aby se zmenilo vychozi chovani. Prazdny prekryv musi dat
        // TOTEZ co zadny.
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);

        var bez = new RoadScene(net, o);
        var prazdny = new RoadScene(net, o, RoadWidthOverrides.Prazdny);

        for (double y = -3; y <= 3; y += 0.25)
            Assert.That(prazdny.IsRoad(0, y), Is.EqualTo(bez.IsRoad(0, y)), $"y={y}");
    }

    [Test]
    public void MapMsg_neseSirkuZPrekryvu()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var prekryv = RoadWidthOverrides.Build(net, _ => 6.0);

        var msg = net.ToLogMessage("test", prekryv);

        Assert.That(msg.Nodes, Is.Not.Empty);
        foreach (var n in msg.Nodes)
            Assert.That(n.WidthMeters, Is.EqualTo(6.0).Within(1e-9));
    }
```

Pak uprav `RoadNetwork.ToLogMessage`:

```csharp
    /// <param name="prekryv">Naucene sirky uzlu; <c>null</c> = jen mapa. Diky tomu kresli
    /// World pohled i webovy pudorys TOTEZ, proti cemu se koreluje. Viz
    /// doc/plan-naucena-sirka-do-mapy.md.</param>
    public MapMsg ToLogMessage(string? name = null, RoadWidthOverrides? prekryv = null)
    {
        var msg = new MapMsg { Name = name ?? string.Empty };
        var index = new Dictionary<long, int>();
        var seen = new HashSet<(long, long, long)>();

        foreach (var e in _edges)
        {
            int fi = AddNode(msg, index, e.From, prekryv);
            int ti = AddNode(msg, index, e.To, prekryv);
            // ... zbytek beze zmeny ...
        }
        return msg;

        static int AddNode(MapMsg msg, Dictionary<long, int> index, Node n, RoadWidthOverrides? p)
        {
            if (index.TryGetValue(n.Id, out int i)) return i;
            i = msg.Nodes.Count;
            index[n.Id] = i;
            msg.Nodes.Add(new MapMsg.MapNode
            {
                Id = n.Id,
                LatDeg = Conversions.Rad2Deg(n.Location.Latitude),
                LonDeg = Conversions.Rad2Deg(n.Location.Longitude),
                WidthMeters = p != null && p.TryGet(n.Id, out double w) ? w : n.Width,
            });
            return i;
        }
    }
```

- [ ] **Krok 6: Spusť celou sadu — nic jiného se nesmělo rozbít**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64`
Očekávej: PASS, 0 neúspěšných (výchozí je 1499 + nové)

- [ ] **Krok 7: Ohlas hotovo a čekej**

---

## Úkol 3: Vyměnitelná scéna v `MapCorrelator`u

**Soubory:**
- Změnit: `Src/ARBot.Common/Localization/MapCorrelator.cs:34` (pole `scene`) a `Process`
- Test: `Src/ARBot.Common.Tests/Localization/MapCorrelatorSceneSwapTests.cs`

**Rozhraní:**
- Poskytuje: `MapCorrelator.Scene { get; set; }` — setter odmítne `null`.

- [ ] **Krok 1: Napiš padající test**

```csharp
using ARBot.Common.Localization;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Tests.Localization;

/// <summary>
/// Scena korelatoru jde vymenit za behu. RoadScene je NEMENNA, takze jde o atomickou zamenu
/// reference - korelator na svem vlakne vidi bud starou, nebo novou, nikdy rozpracovanou.
/// Viz doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 5.
/// </summary>
public class MapCorrelatorSceneSwapTests
{
    [Test]
    public void ScenaJdeVymenit()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var korelator = new MapCorrelator(new ARBot.Common.Fusion.AsyncFusionEngine(
                                              new ARBot.Common.Fusion.EKFModel()),
                                          new RoadScene(net, o),
                                          CorrelationTestScenes.TestConfig());

        var nova = new RoadScene(net, o, RoadWidthOverrides.Build(net, _ => 6.0));
        korelator.Scene = nova;

        Assert.That(korelator.Scene, Is.SameAs(nova));
        Assert.That(korelator.Scene.IsRoad(0, 2.5), Is.True, "nova scena uz ma naucenou sirku");
    }

    [Test]
    public void ScenaNesmiBytNull()
    {
        var o = CorrelationTestScenes.Origin();
        var net = CorrelationTestScenes.StraightEastRoad(o, 3.0);
        var korelator = new MapCorrelator(new ARBot.Common.Fusion.AsyncFusionEngine(
                                              new ARBot.Common.Fusion.EKFModel()),
                                          new RoadScene(net, o),
                                          CorrelationTestScenes.TestConfig());

        Assert.That(() => korelator.Scene = null, Throws.ArgumentNullException);
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~MapCorrelatorSceneSwapTests"`
Očekávej: chyba překladu — `MapCorrelator` nemá `Scene`.

- [ ] **Krok 3: Uprav `MapCorrelator`**

Pole a vlastnost:

```csharp
        private volatile RoadScene scene;

        /// <summary>
        /// Vozovka podle mapy. <b>Jde vymenit za behu</b> — naucena sirka cesty
        /// (doc/plan-naucena-sirka-do-mapy.md). <see cref="RoadScene"/> je NEMENNA, takze zamena
        /// reference je atomicka: vlakno stupne uvidi bud starou, nebo novou, nikdy rozpracovanou.
        /// </summary>
        public RoadScene Scene
        {
            get => scene;
            set => scene = value ?? throw new ArgumentNullException(nameof(value));
        }
```

V `Process` si scénu vyzvedni **jednou na začátku** a dál používej jen lokální proměnnou:

```csharp
            // Scenu vyzvedni JEDNOU - muze se vymenit za behu a rastr se skorovanim musi videt
            // TOTEZ. Tataz vada uz jednou byla u pozy v CorridorLocalizer.Process (23. 8. 2026).
            var scena = scene;
            ...
            var raster = RoadRaster.Build(scena, msg.OriginX, msg.OriginY, msg.Size, msg.Resolution, ...);
```

*(Projdi celé tělo `Process` a nahraď **každé** další použití pole `scene` lokální `scena`.)*

- [ ] **Krok 4: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~MapCorrelator"`
Očekávej: PASS (nové i všechny stávající `MapCorrelatorTests`)

- [ ] **Krok 5: Ověř, že v `Process` nezůstalo přímé čtení pole**

Spusť: `grep -n "scene" Src/ARBot.Common/Localization/MapCorrelator.cs`
Očekávej: pole a vlastnost; **uvnitř `Process` už žádný výskyt** `scene` kromě řádku `var scena = scene;`

- [ ] **Krok 6: Ohlas hotovo a čekej**

---

## Úkol 4: `RoadWidthMapUpdater` — kdy přestavět

**Soubory:**
- Vytvořit: `Src/ARBot.Runtime/Robot/RoadWidthMapUpdater.cs`
- Test: `Src/ARBot.Runtime.Tests/RoadWidthMapUpdaterTests.cs`

**Rozhraní:**
- Konzumuje: `RoadWidthOverrides.Build` (úkol 1), `RoadScene(net, origin, prekryv)` (úkol 2),
  `MapCorrelator.Scene` (úkol 3), `ARBot.Common.Localization.RoadWidthEstimator.TryGetWidth(long, out double)`.
- Poskytuje: `new RoadWidthMapUpdater(RoadNetwork, GeoReference, RoadWidthEstimator, Action<RoadScene> naScenu, Action<MapMsg> naMapu, RoadWidthMapUpdaterConfig)`;
  `bool Zkus(DateTime t)` (veřejné schválně — jde volat z testu bez vlákna, stejně jako
  `MapCorrelator.Process`); `long Rebuilds { get; }`.

- [ ] **Krok 0: Napiš pomocníka pro síť** (`ARBot.Runtime.Tests` nemá `CorrelationTestScenes`)

Vytvoř `Src/ARBot.Runtime.Tests/TestRoadNetwork.cs`:

```csharp
using ARBot.Common.Coordinates;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Runtime.Tests;

/// <summary>
/// Minimalni sit pro testy runtime. ⚠️ Zamerne SE NEPOUZIVA CorrelationTestScenes
/// z ARBot.Common.Tests - tenhle projekt ho nereferencuje a pridavat referenci mezi dvema
/// testovacimi projekty by zataholo celou cizi sadu.
/// </summary>
internal static class TestRoadNetwork
{
    public static GeoReference Origin() => GeoReference.FromDegrees(50.0, 14.0);

    /// <summary>Jedna prima cesta podel osy X (na vychod), delka 60 m, stred v y = 0.</summary>
    public static RoadNetwork StraightEastRoad(GeoReference o, double width = 3.0)
    {
        var a = new Node(1, o.ToLLA(-30, 0), width);
        var b = new Node(2, o.ToLLA(30, 0), width);
        var builder = new RoadNetwork.Builder();
        builder.AddEdge(a, b, 60.0, wayId: 1, traversalCost: 60.0);
        return builder.Build();
    }
}
```

- [ ] **Krok 1: Napiš padající test**

```csharp
using System;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Robot;

namespace ARBot.Runtime.Tests;

/// <summary>
/// Kdy se mapa prestavi. Prah je proti kolisani odhadu v centimetrech, odstup proti rade cest,
/// ktere se usadi tesne po sobe. Viz doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 6.
/// </summary>
public class RoadWidthMapUpdaterTests
{
    private static readonly DateTime T0 = new DateTime(2026, 9, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Estimator naplneny tak, ze vsechny cesty maji duveryhodnou sirku.</summary>
    private static RoadWidthEstimator Odhad(double sirka)
    {
        var e = new RoadWidthEstimator(new RoadWidthEstimatorConfig { MinSamples = 1 });
        for (long way = 0; way < 64; way++) e.Add(way, sirka);
        return e;
    }

    [Test]
    public void PrvniDuveryhodnaSirka_prestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        RoadScene scena = null; MapMsg mapa = null;
        var u = new RoadWidthMapUpdater(net, o, Odhad(6.0), s => scena = s, m => mapa = m,
                                        new RoadWidthMapUpdaterConfig());

        bool prestaveno = u.Zkus(T0);

        Assert.That(prestaveno, Is.True);
        Assert.That(scena, Is.Not.Null);
        Assert.That(scena.IsRoad(0, 2.5), Is.True, "nova scena ma naucenou sirku");
        Assert.That(mapa, Is.Not.Null);
        Assert.That(u.Rebuilds, Is.EqualTo(1));
    }
}
```

- [ ] **Krok 2: Spusť test a ověř, že padá**

Spusť: `dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadWidthMapUpdaterTests"`
Očekávej: chyba překladu — `RoadWidthMapUpdater` neexistuje.

- [ ] **Krok 3: Napiš konfiguraci a updater**

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using ARBot.Common.Communication;
using ARBot.Common.Localization;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Coordinates;

namespace ARBot.Robot
{
    /// <summary>Nastaveni <see cref="RoadWidthMapUpdater"/>.</summary>
    public sealed class RoadWidthMapUpdaterConfig
    {
        /// <summary>O kolik se musi duveryhodna sirka lisit od te zapecene, aby se prestavovalo [m].
        /// ⚠️ ODHAD, ne mereni - nastavit z prvniho zaznamu (viz spec).</summary>
        public double RebuildThresholdM = 0.25;

        /// <summary>Nejmensi odstup mezi prestavbami [s]. ⚠️ Taky odhad.</summary>
        public double MinRebuildPeriodSec = 10.0;
    }

    /// <summary>
    /// Prenasi NAUCENOU SIRKU cesty z koridoru do <see cref="RoadScene"/> korelatoru a do
    /// <see cref="MapMsg"/> (World pohled, webovy pudorys).
    ///
    /// <para><b>Tik a data jsou dve ruzne veci:</b> tikem je <see cref="RoadCorridorMsg"/> ze
    /// streamu (chodi prave tehdy, kdy se odhad mohl zmenit - emituje se i u zamitnutych cyklu),
    /// daty je <see cref="RoadWidthEstimator"/> primo, protoze zprava nese jednu namerenou sirku,
    /// ne verdikt kvality per hrana.</para>
    ///
    /// <para>⚠️ <b>Scenu virtualni kamery to NEDOSTANE</b> - ta se stavi zvlast v <c>ARBotHW</c>
    /// a nikdo ji prekryv nepreda. Kdyby ho dostala, simulace by renderovala cestu podle odhadu
    /// a koridor by meril SAM SEBE (tataz past jako camerapose=fusion).</para>
    ///
    /// <para>⚠️ <b>Stupen je STAVOVY</b> - zaruka record/replay plati pro cerstvou instanci.
    /// Viz doc/plan-naucena-sirka-do-mapy.md.</para>
    /// </summary>
    public sealed class RoadWidthMapUpdater : MessageProcessor
    {
        private readonly RoadNetwork network;
        private readonly GeoReference origin;
        private readonly RoadWidthEstimator odhady;
        private readonly Action<RoadScene> naScenu;
        private readonly Action<MapMsg> naMapu;
        private readonly RoadWidthMapUpdaterConfig config;
        private readonly string mapName;

        /// <summary>Sirky, se kterymi se stavelo naposled (per way).</summary>
        private readonly Dictionary<long, double> zapecene = new Dictionary<long, double>();
        private DateTime posledniPrestavba = DateTime.MinValue;

        public RoadWidthMapUpdater(RoadNetwork network, GeoReference origin,
                                   RoadWidthEstimator odhady,
                                   Action<RoadScene> naScenu, Action<MapMsg> naMapu,
                                   RoadWidthMapUpdaterConfig config = null,
                                   string mapName = null, int queueCapacity = 4)
            : base(OverflowPolicy.DropOldest, queueCapacity)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.origin = origin ?? throw new ArgumentNullException(nameof(origin));
            this.odhady = odhady ?? throw new ArgumentNullException(nameof(odhady));
            this.naScenu = naScenu ?? throw new ArgumentNullException(nameof(naScenu));
            this.naMapu = naMapu ?? throw new ArgumentNullException(nameof(naMapu));
            this.config = config ?? new RoadWidthMapUpdaterConfig();
            this.mapName = mapName ?? string.Empty;
        }

        /// <summary>DIAGNOSTIKA: kolikrat se mapa prestavela.</summary>
        public long Rebuilds { get; private set; }

        /// <summary>
        /// Prestav, kdyz je to potreba. Verejne schvalne - takhle jde updater prohnat testem
        /// i zaznamem BEZ vlakna (stejny duvod jako u <c>MapCorrelator.Process</c>).
        /// </summary>
        public bool Zkus(DateTime t)
        {
            if (!JeCoPrestavet()) return false;

            // Skok casu vzad (seek, novy beh) odstup RESETUJE - jinak by se po nem uz
            // neprestavelo nikdy. Tataz past jako u MapCorrelator.MinPeriod.
            if (posledniPrestavba != DateTime.MinValue && t >= posledniPrestavba
                && (t - posledniPrestavba).TotalSeconds < config.MinRebuildPeriodSec)
                return false;

            var prekryv = RoadWidthOverrides.Build(network, NaucenaSirka);
            naScenu(new RoadScene(network, origin, prekryv));
            naMapu(network.ToLogMessage(mapName, prekryv));

            ZapecUzite();
            posledniPrestavba = t;
            Rebuilds++;
            Trace.WriteLine($"Naucena sirka cesty: mapa prestavena ({prekryv.Count} uzlu).");
            return true;
        }

        private double? NaucenaSirka(long wayId)
            => odhady.TryGetWidth(wayId, out double w) ? w : (double?)null;

        /// <summary>Lisi se nektera duveryhodna sirka od te, se kterou se stavelo naposled?</summary>
        private bool JeCoPrestavet()
        {
            foreach (var e in network.Edges)
            {
                if (!odhady.TryGetWidth(e.WayId, out double w)) continue;
                if (!zapecene.TryGetValue(e.WayId, out double stara)
                    || Math.Abs(w - stara) > config.RebuildThresholdM)
                    return true;
            }
            return false;
        }

        private void ZapecUzite()
        {
            foreach (var e in network.Edges)
                if (odhady.TryGetWidth(e.WayId, out double w)) zapecene[e.WayId] = w;
        }

        /// <inheritdoc/>
        protected override void Consume(Message msg)
        {
            if (!(msg is RoadCorridorMsg m)) return;
            try { Zkus(m.TimeStamp); }
            catch (Exception ex) { Trace.WriteLine($"RoadWidthMapUpdater: {ex}"); }
        }
    }
}
```

- [ ] **Krok 4: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadWidthMapUpdaterTests"`
Očekávej: PASS

- [ ] **Krok 5: Doplň testy prahu, odstupu a skoku času**

```csharp
    [Test]
    public void PodPrahem_neprestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        var odhad = Odhad(6.0);
        var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 0 });
        u.Zkus(T0);

        // Zmena o 0,10 m je pod prahem 0,25 -> nic se prestavovat nema.
        foreach (var e in net.Edges) for (int i = 0; i < 40; i++) odhad.Add(e.WayId, 6.10);

        Assert.That(u.Zkus(T0.AddSeconds(60)), Is.False);
        Assert.That(u.Rebuilds, Is.EqualTo(1));
    }

    [Test]
    public void NadPrahemAleVOdstupu_neprestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        var odhad = Odhad(6.0);
        var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 10 });
        u.Zkus(T0);

        foreach (var e in net.Edges) for (int i = 0; i < 40; i++) odhad.Add(e.WayId, 2.0);

        Assert.That(u.Zkus(T0.AddSeconds(5)), Is.False, "odstup jeste neuplynul");
        Assert.That(u.Zkus(T0.AddSeconds(11)), Is.True, "po odstupu uz ano");
    }

    [Test]
    public void SkokCasuVzad_odstupResetuje()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        var odhad = Odhad(6.0);
        var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 10 });
        u.Zkus(T0);
        foreach (var e in net.Edges) for (int i = 0; i < 40; i++) odhad.Add(e.WayId, 2.0);

        Assert.That(u.Zkus(T0.AddSeconds(-30)), Is.True, "po skoku vzad se odstup nesmi drzet");
    }

    [Test]
    public void BezDuveryhodnehoOdhadu_neprestavi()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
        // Prazdny estimator = zadna cesta nema duveryhodnou sirku.
        var u = new RoadWidthMapUpdater(net, o, new RoadWidthEstimator(), _ => { }, _ => { },
                                        new RoadWidthMapUpdaterConfig());

        Assert.That(u.Zkus(T0), Is.False);
        Assert.That(u.Rebuilds, Is.Zero);
    }

    [Test]
    public void DveCerstveInstance_dajiTyzPocetPrestaveb()
    {
        // Record/replay: stupen je stavovy, takze zaruka plati pro CERSTVE instance.
        static long Beh()
        {
            var o = TestRoadNetwork.Origin();
            var net = TestRoadNetwork.StraightEastRoad(o, 3.0);
            var odhad = new RoadWidthEstimator(new RoadWidthEstimatorConfig { MinSamples = 1 });
            var u = new RoadWidthMapUpdater(net, o, odhad, _ => { }, _ => { },
                                            new RoadWidthMapUpdaterConfig { MinRebuildPeriodSec = 1 });
            for (int i = 0; i < 20; i++)
            {
                foreach (var e in net.Edges) odhad.Add(e.WayId, 3.0 + i * 0.1);
                u.Zkus(T0.AddSeconds(i));
            }
            return u.Rebuilds;
        }

        Assert.That(Beh(), Is.EqualTo(Beh()));
    }
```

- [ ] **Krok 6: Spusť a ověř všech šest**

Spusť: `dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadWidthMapUpdaterTests"`
Očekávej: PASS, 6 testů

- [ ] **Krok 7: Ohlas hotovo a čekej**

---

## Úkol 5: Parametr `roadwidthmap=` a napojení v runtime

**Soubory:**
- Změnit: `Src/ARBot.Common/Configuration/ParamRegistry.cs` (za `MapCorrRef`, sekce `K_FUZE`)
- Změnit: `Src/ARBot.Runtime/Robot/ARBotRuntime.cs` — za blok koridoru (kolem řádku 680)
- Test: `Src/ARBot.Runtime.Tests/RoadWidthMapUpdaterTests.cs` (doplnit)

**Rozhraní:**
- Konzumuje: `RoadWidthMapUpdater` (úkol 4), `MapCorrelator.Scene` (úkol 3),
  `CorridorLocalizer.Widths : RoadWidthEstimator`.
- Poskytuje: `ParamRegistry.RoadWidthMap : BoolParam` (klíč `roadwidthmap`, default `"false"`).

- [ ] **Krok 1: Přidej parametr do registru**

```csharp
        public static readonly BoolParam RoadWidthMap = Bool("roadwidthmap", "false", K_FUZE,
              "Propsat NAUCENOU SIRKU cesty z koridoru do mapy: do RoadScene korelatoru "
              + "a do MapMsg (World pohled, webovy pudorys). Vyzaduje corridor=true. "
              + "⚠️ Vychozi vypnuto - bez toho by se zmereny odhad zacal propisovat do korelace "
              + "driv, nez kdokoli videl jediny zaznam ze zarizeni. ⚠️ Scenu VIRTUALNI KAMERY to "
              + "nedostane nikdy (jinak by simulace renderovala podle odhadu a koridor by meril "
              + "sam sebe). Viz doc/plan-naucena-sirka-do-mapy.md.");
```

- [ ] **Krok 2: Napiš padající test na výchozí stav**

```csharp
    [Test]
    public void VychoziHodnotaParametru_jeVypnuto()
    {
        // ParamDef.Default je VEREJNE POLE typu string (ne vlastnost DefaultText - ta neexistuje).
        Assert.That(ARBot.Common.Configuration.ParamRegistry.RoadWidthMap.Default, Is.EqualTo("false"));
    }
```

- [ ] **Krok 3: Spusť test a ověř, že prochází**

Spusť: `dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~VychoziHodnotaParametru"`
Očekávej: PASS

- [ ] **Krok 4: Napoj updater v `ARBotRuntime`**

Za blok, který zakládá `CorridorLocalizer` (končí `connections.Add(corridor.Output.Connect(stream));`),
přidej — a `corridor` si pro to musíš vytáhnout do proměnné viditelné za blokem:

```csharp
            // Naucena sirka cesty do mapy: RoadScene korelatoru + MapMsg (kresleni).
            // ⚠️ Scena virtualni kamery se stavi zvlast v ARBotHW a prekryv NEDOSTANE - kdyby ho
            // dostala, simulace by renderovala podle odhadu a koridor by meril sam sebe
            // (tataz past jako camerapose=fusion). Viz doc/plan-naucena-sirka-do-mapy.md.
            if (!ParamRegistry.RoadWidthMap.Value)
            {
                Trace.WriteLine("roadwidthmap=false: naucena sirka do mapy nejde (vychozi stav).");
            }
            else if (CorridorLocalizer == null)
            {
                Trace.WriteLine("roadwidthmap=true, ale corridor=false -> neni odkud brat sirky; "
                                + "updater se nezaklada.");
            }
            else
            {
                var widthUpdater = new RoadWidthMapUpdater(
                    RoadNetwork, fusionConfig.GeoReference, CorridorLocalizer.Widths,
                    s => { if (MapCorrelator != null) MapCorrelator.Scene = s; },
                    m => { MapMessage = m; stream.Publish(m); },
                    new RoadWidthMapUpdaterConfig(),
                    MapMessage?.Name);

                stages.Add(widthUpdater);
                connections.Add(stream.Connect(widthUpdater));
            }
```

⚠️ **Jméno mapy ber z `MapMessage?.Name`, ne z `mapPath`.** Proměnná `mapPath` je lokální
v **jiné** metodě (`ARBotRuntime.cs:1252`, čtení mapy) a v místě drátování **není v dosahu**;
`MapMessage` se tam plní na řádku 1282 právě z `Path.GetFileName(mapPath)`, takže jméno je totéž.

⚠️ **Pozor na pořadí:** blok musí být **až za** založením `MapCorrelator`u i `CorridorLocalizer`u,
protože oba používá. Zkontroluj, že `MapCorrelator` je vlastnost naplněná dřív (dnes kolem řádku 616).

- [ ] **Krok 5: Build celého řešení**

Spusť: `dotnet build Src/ARBot.slnx -p:Platform=x64`
Očekávej: `Počet chyb: 0`

- [ ] **Krok 6: Spusť všechny tři testovací projekty**

Spusť:
```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64
dotnet test Src/ARBot.HAL.Tests -p:Platform=x64
dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64
```
Očekávej: všude `Neúspěšné: 0`

- [ ] **Krok 7: Ohlas hotovo a čekej**

---

## Úkol 6: Test oddělení od virtuální kamery

**Soubory:**
- Test: `Src/ARBot.Runtime.Tests/RoadWidthVirtualCameraIsolationTests.cs` (nový)

**Rozhraní:** konzumuje vše z úkolů 1–5. Nic nového neposkytuje.

> **Proč vlastní úkol:** tohle je jediná pojistka proti tomu, aby se simulace začala dívat na
> vlastní odhad. Je to **bezpečnostní tvrzení**, ne detail — patří mu vlastní soubor a vlastní
> branka recenzenta.

- [ ] **Krok 1: Napiš test**

```csharp
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Runtime.Tests;

/// <summary>
/// ⚠️ Scena, ze ktere renderuje VIRTUALNI KAMERA, nesmi dostat naucenou sirku. Kdyby ji dostala,
/// simulace by renderovala cestu podle odhadu a koridor by meril SAM SEBE - tataz past jako
/// camerapose=fusion (nalezena 22. 8. 2026). Viz doc/plan-naucena-sirka-do-mapy.md, rozhodnuti 3.
/// </summary>
public class RoadWidthVirtualCameraIsolationTests
{
    [Test]
    public void ScenaRendereru_prekryvNedostane()
    {
        var o = TestRoadNetwork.Origin();
        var net = TestRoadNetwork.StraightEastRoad(o, 3.0);

        // Presne tak, jak scenu stavi ARBotHW (bez prekryvu).
        var rendererScena = new RoadScene(net, o);
        // A takhle ta, kterou dostane korelator po nauceni sirky.
        var korelatorScena = new RoadScene(net, o, RoadWidthOverrides.Build(net, _ => 6.0));

        Assert.That(korelatorScena.IsRoad(0, 2.5), Is.True, "korelator uz naucenou sirku ma");
        Assert.That(rendererScena.IsRoad(0, 2.5), Is.False,
                    "renderer musi zustat na MAPOVE sirce - jinak by koridor meril sam sebe");
    }

    [Test]
    public void ArbotHwStaviScenuBezPrekryvu()
    {
        // Strazny test proti tomu, aby nekdo prekryv do ARBotHW dopsal. Kdyby se konstruktor
        // sceny v ARBotHW zmenil na tripararametrovy, tenhle test to ma zviditelnit.
        // RepoPaths zije v ARBot.Common/Configuration, takze je odsud dostupny pres
        // ARBot.Runtime -> ARBot.Common. Bez repa (nasazeni na zarizeni) se test preskoci.
        string root = ARBot.Common.Configuration.RepoPaths.RootOrBase();
        string cesta = System.IO.Path.Combine(root, "Src", "ARBot.Runtime", "Robot", "ARBotHW.cs");
        if (!System.IO.File.Exists(cesta)) Assert.Ignore("Bezi bez repa - neni co kontrolovat.");

        string zdroj = System.IO.File.ReadAllText(cesta);

        Assert.That(zdroj, Does.Contain("new RoadScene(options.Network, options.Origin)"),
                    "ARBotHW musi stavet scenu BEZ prekryvu - viz rozhodnuti 3 ve specu");
    }
}
```

- [ ] **Krok 2: Spusť a ověř**

Spusť: `dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~RoadWidthVirtualCameraIsolationTests"`
Očekávej: PASS

- [ ] **Krok 3: Ohlas hotovo a čekej**

---

## Úkol 7: Ověření v simulaci a dokumentace

**Soubory:**
- Změnit: `doc/plan-naucena-sirka-do-mapy.md` (stav: hotovo / co ověřeno)
- Změnit: `doc/map-correlation-localization.md` (sekce o plánu → co je hotové)
- Změnit: `CLAUDE.md` (odrážka korelace s mapou; počet klíčů registru **+1**)
- Změnit: `doc/devlog.md` (záznam dne, **nejnovější nahoru**)

- [ ] **Krok 1: Projeď simulaci**

Spusť:
```bash
dotnet run --project Src/ARBot --property:Platform=x64 -- virtualhw=true map=OSM/SyntetickyRovny.osm corridor=true roadwidthmap=true mapcorr=true
```
Sleduj: v `Trace` musí být `Naucena sirka cesty: mapa prestavena (N uzlu).` a ve World pohledu se
má šířka cesty po pár sekundách změnit. **Zapiš, co jsi viděl** — kolik přestaveb a za jak dlouho.

- [ ] **Krok 2: Ověř, že výchozí stav nic nemění**

Spusť totéž **bez** `roadwidthmap=true`.
Očekávej: v `Trace` `roadwidthmap=false: naucena sirka do mapy nejde (vychozi stav).` a mapa se
nemění.

- [ ] **Krok 3: Aktualizuj počet klíčů v `CLAUDE.md`**

Spusť: `grep -cE '^\s+public static readonly \w+Param ' Src/ARBot.Common/Configuration/ParamRegistry.cs`
a nastav to číslo v řádku „(`ARBot.Common/Configuration`, **N** klíčů s popisem a typem)".

- [ ] **Krok 4: Dopiš dokumentaci a DevLog**

Do DevLogu patří: co je hotové, **co bylo ověřeno jen v simulaci**, že **na HW neběželo nic**,
a další krok (změřit prahy 0,25 m / 10 s z reálného záznamu).

- [ ] **Krok 5: Finální ověření**

Spusť:
```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
dotnet test Src/ARBot.Common.Tests -p:Platform=x64
dotnet test Src/ARBot.HAL.Tests -p:Platform=x64
dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64
```
Očekávej: `Počet chyb: 0` a všude `Neúspěšné: 0`.

- [ ] **Krok 6: Ohlas hotovo a čekej na pokyn ke commitu**

---

## Mapování na spec

| požadavek specu | úkol |
|---|---|
| `RoadWidthOverrides`, pravidlo maxima (rozhodnutí 2) | 1 |
| Překryv v `RoadScene` a `ToLogMessage` (komponenty) | 2 |
| Bez překryvu se nezmění nic (test 2 ve specu) | 2, krok 5 |
| Vyměnitelná scéna, jedna scéna na cyklus (rozhodnutí 5; testy 7) | 3 |
| Práh + odstup (rozhodnutí 6; test 4) | 4 |
| Record/replay nad čerstvými instancemi (test 8) | 4 |
| `roadwidthmap=`, výchozí vypnuto (rozhodnutí 4; test 6) | 5 |
| Oddělení od virtuální kamery (rozhodnutí 3; test 5) | 6 |
| Chyby a okrajové stavy (tabulka ve specu) | 4 (bez odhadu), 5 (`corridor=false`) |
| Fáze 4 — ověření v simulaci | 7 |
