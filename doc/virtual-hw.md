# Virtuální HW (simulované senzory)

Simulované senzory, které se v aplikaci tváří jako reálné — robot „vidí" scénu odvozenou
z načtené OsmNav mapy a vlastní pózy, bez připojeného hardwaru. První (a zatím jediný)
obyvatel je **`VirtualCamera`** (náhrada D435: RGB + hloubka). Šev je ale navržený tak,
aby vedle ní později přibyly **virtuální GPS a IMU**.

> **Stav (2026-08-12): implementováno, na HW neověřeno.** `RoadScene`, `SyntheticFrameRenderer`,
> `VirtualCamera` i drátování (`SetRealHW`/`SetVirtualHW`) hotové a pokryté testy. Neintegrováno:
> sdílení mapy s `WorldViewDocument` (viz „Otevřené / budoucí").

## Účel

Dva doložené případy užití (určují míru věrnosti):

1. **Vývoj vizuální cesty bez HW** — ladit řetěz depth → polární grid → occupancy →
   lokální plánování na počítači bez kamer. Rozhoduje **geometrická** věrnost, ne
   fotorealismus.
2. **Reprodukovatelné automatické testy** — deterministický vstup pro testy vizuální
   cesty. Šum proto musí být seedovatelný a vypnutelný.

Mimo rozsah: uzavřená smyčka (simulace pohybu robota), fotorealistický vzhled,
jiné objekty než vozovka a okolní tráva.

## Architektura

Renderer je oddělený od kamery — tři jednotky s jasnou hranicí:

| Kde | Co | Proč tam |
|---|---|---|
| `ARBot.Common/Vision/Synthetic/RoadScene.cs` | Geometrie scény v lokální ENU rovině: úseky vozovky (osa + šířka) postavené z `RoadNetwork` přes `GeoReference` + prostorový index | Čistý algoritmus bez HW → `Common` (směr závislostí `Common ← HAL ← app`) |
| `ARBot.Common/Vision/Synthetic/SyntheticFrameRenderer.cs` | Vlastní vykreslení: (scéna, póza, projekce) → naplní `Image<BGR32>` + `Image<Gray16>` | Jádro, které se testuje deterministicky. Nezná `ICamera` ani senzory |
| `ARBot.HAL/Devices/Camera/VirtualCamera.cs` | `SensorBase<CameraFrame>, ICamera` — časování snímků, capture pool, `CreateProjector()` / `CreateDepthProjector()` | Bez platformní závislosti → do **`ARBot.HAL`**, ne do `HALWindows`/`HALArmbian`. Jedna kopie pro x64 i OrangePI a **nezávislá na Intel.RealSense** |

### Projekce

`CameraProjection` (`ARBot.Common/Coordinates`) implementuje `ICameraProjection`
i `IDepthCameraProjection` a staví se jen z `Intrinsics` + matic — bez RealSense.
`VirtualCamera` si vyrobí syntetické intrinsics ve stylu D435 (pinhole,
`Distortion.None`, `Fx = (W/2) / tan(HFOV/2)`) a **tutéž instanci použije k vykreslení
i vrátí z `CreateProjector()` / `CreateDepthProjector()`**.

To je záměr, ne úspora: vize dostane přesně tu projekci, kterou byl obraz myšlen, takže
neshoda v hloubkové cestě je skutečná chyba, ne artefakt simulace.

## Šev: `SetRealHW` / `SetVirtualHW`

```csharp
// ARBotHW
public void SetRealHW();                       // default (dnešní chování při initu)
public void SetVirtualHW(VirtualHWOptions o);  // vymění senzory za simulované
```

Obojí prochází existujícími `CameraStop()` / `CameraStart()`, které už dnes slouží
k výměně kamer za běhu — nezavádí se nový životní cyklus.

`VirtualHWOptions` nese **vše potřebné**:

- `RoadNetwork Network` — scéna,
- `GeoReference Origin` — počátek lokální ENU roviny,
- `Func<DateTime, RobotState> PoseAt` — zdroj pózy,
- montážní transformace kamer (jinak z `Profile.Left/RightCameraTransform`),
- parametry vzhledu a šumu (viz níže).

### Pořadí volání

`ARBotHW.Current` (a s ním kamery) vzniká v `ARBotRuntime.Start()` **dřív** než
`AsyncFusionEngine`. `SetVirtualHW` proto volá `ARBotRuntime.Start()` až **za** vytvořením
enginu, takže `PoseAt = t => engine.GetStateAt(t)` jde předat rovnou v opcích a nic se
nedodrátovává dodatečně.

### `GeoReference` není věcí kamery

Kamera si referenci **nezakládá** — dostane ji hotovou v `VirtualHWOptions` a jen ji použije.

Deklarovaný domov je `FusionConfig.GeoReference`, jehož komentář slibuje, že ji „GPS adapter
založí z prvního platného fixu". **To dnes nikdo nedělá** — pole nikdo nečte ani nenastavuje
a `GeoReference.ToLocal` se používá jen ve `WorldViewDocument`, který si referenci staví ad hoc
z GPS a pózy. Fúze bere `PositionMeasurement` rovnou v metrech, takže převod LLA → ENU zatím
nikde přes `GeoReference` neteče.

Až vlastník počátku vznikne (GPS adapter nebo virtuální GPS), v kameře se nemění nic.

### Sdílená `RoadNetwork` v runtime

`ARBotRuntime` drží načtenou síť v `RoadNetwork` a její počátek v `MapOrigin` (obojí veřejné,
`null` bez mapy) — naplní je při startu z `.osm` podle parametrů. Je to první krok k otevřenému
úkolu z [osm-nav.md](osm-nav.md) → „Napojení na řídicí smyčku".

`WorldViewDocument` si **zatím pořád načítá mapu vlastní cestou** — sdílení s runtime hotové
není (viz „Otevřené / budoucí").

### Parametry příkazové řádky

| Parametr | Význam |
|---|---|
| `virtualhw=true` | zapne simulované kamery místo D435 |
| `map=<cesta.osm>` | mapa, ze které se scéna renderuje (bez ní zůstane reálný HW) |
| `roadwidth=<m>` | výchozí šířka cesty pro uzly bez `width` (default 3) |

Zapnutí je **best-effort**: chybějící nebo vadná mapa simulaci jen nezapne (a zaloguje důvod),
nikdy neshodí start aplikace.

## Model světa

### Scéna (`RoadScene`)

Z `RoadNetwork` se **jednorázově** postaví seznam úseků v lokální ENU rovině:

- uzly přes `GeoReference.ToLocal`,
- šířka lineárně interpolovaná mezi `Node.Width` obou konců,
- obousměrné hrany se deduplikují (stejně jako v `RoadNetwork.ToLogMessage`).

Dotaz `IsRoad(x, y)`: bod leží na vozovce, když je jeho vzdálenost od osy některého úseku
menší než polovina lokální šířky — vozovka je tedy **sjednocení kapslí**.

Úseky jsou v uniformní mřížce; per snímek se předfiltrují na ty v dosahu kamery (typicky
jednotky), takže per-pixel test je triviální.

### Rasterizace (`SyntheticFrameRenderer`)

Pro každý pixel jeden paprsek a **dvě vodorovné roviny** — vozovka `z = 0`, tráva
`z = GrassHeight`:

1. Směr paprsku v prostoru kamery z `Camera2DToCamera3D[y, x]` — **tytéž tabulky, kterými
   vize hloubku zpátky rozbaluje**. Do světa se otočí maticí z `SetOrientation`, počátek
   je pozice kamery.
2. Spočítá se průsečík s oběma rovinami. Kandidát je ten, jehož bod odpovídá svému povrchu
   (na rovině vozovky musí `IsRoad` platit, na rovině trávy neplatit); z platných kandidátů
   vyhraje **bližší**.
3. Paprsek nad horizont nebo dál než `MaxRange` → hloubka **0** (neplatný pixel), jak se
   chová reálná D435.

Vyvýšená tráva tím dostane i **správnou okluzi**: na hraně vozovky je svislá stěna, která
ukrojí kousek vozovky za sebou. Vyplyne to ze dvou rovin samo, bez kódu navíc.

**Jednotky hloubky:** `Image<Gray16>` v **milimetrech**, 0 = neplatné — shodně s
`CameraProjection.GetPointCloud` a `CameraFrameProcessor` (obojí převádí `d * 0.001f`).

**RGB** se rastruje stejným paprskem, řeší se jen materiál: vozovka šedá, všechno ostatní
zelené — **včetně oblohy nad horizontem** (obloha jako samostatná barva je jeden parametr,
až bude potřeba). RGB a hloubka mají vlastní rozlišení i intrinsics (jako D435:
640×480 / 480×270).

### Šum a determinismus

Šum **nejde ze sekvence `Random`**, ale z hashe `(seed, frameIndex, pixelIndex)`. Důsledek:
výsledek nezávisí na pořadí zpracování ani na počtu vláken → jde renderovat paralelně
a snímek je bitově reprodukovatelný.

Tři nezávislé složky, každá vypnutelná nulou:

- šum senzoru na hloubce,
- drsnost trávy (rozptyl její výšky),
- šum barvy.

Při pevném seedu a nulovém šumu vyjde geometricky přesný obraz — deterministický vstup
pro testy.

### Parametry (s výchozími hodnotami)

| Parametr | Default | Poznámka |
|---|---|---|
| Rozlišení RGB / hloubky | 640×480 / 480×270 | jako `D435Camera` |
| HFOV RGB / hloubky | dle D435 | určuje `Fx`/`Fy` |
| `MaxRange` | 10 m | dál → hloubka 0 |
| Výška trávy | 0,10 m | 0 = tráva v rovině vozovky; **k detekci viz poznámka níže** |
| Drsnost trávy | 0,03 m | rozptyl výšky (per pixel, viz omezení níže) |
| Šum hloubky | 0,003 m | šum senzoru |
| Barva vozovky / trávy | šedá / zelená | |
| Amplituda šumu barvy | malá | 0 = čisté barvy |
| `Seed` | pevný | reprodukovatelnost |
| Snímková frekvence | 30 Hz | takt smyčky |

#### Jak vysoká tráva je vidět jako překážka

Klasifikátor polárního gridu povoluje odchylku výšky `MaxHeightDev(r) = 0,03 + 0,02·r`
([PolarGridConfig](../Src/ARBot.Common/Vision/PolarGridConfig.cs)). Výchozích **0,10 m tedy
projde jako překážka jen zhruba do 3,5 m**; dál se vejde do tolerance a bunky vyjdou jako
sjízdné. Ověřeno testem: při 0,25 m je tráva překážkou v celém dosahu gridu (5,5 m).

Není to chyba rendereru — geometrie odpovídá (round-trip test to potvrzuje). Je to vlastnost
klasifikátoru a je dobré ji mít na paměti při volbě výšky trávy: pro ladění detekce okraje
cesty na větší vzdálenost je potřeba tráva vyšší než výchozí hodnota.

## `VirtualCamera`

Tenká slupka nad rendererem:

- drží `CaptureFramePool` (stejně jako `D435Camera`),
- v `GetMeasurement()` odměří takt, vezme pózu přes `PoseAt`, zavolá renderer,
- vyplní `Name`, `TimeStamp`, `RGBTimeStamp`, `DepthTimeStamp`,
- **zavolá `FrameProcessor?.Process(frame)`** — aby byla pipeline identická s reálnou kamerou.

**Nemá `Swap`.** Reálné `Swap = true` u levé kamery je artefakt fyzické montáže; virtuální
kamera rovnou renderuje podle montážní transformace z `Profile`.

## Testy

`ARBot.Common.Tests` (kde už žijí OsmNav testy):

- **`RoadScene`** — převod uzlů do lokální roviny, `IsRoad` na ose a za okrajem,
  interpolace šířky mezi uzly.
- **Round-trip (nejcennější)** — vyrenderovat hloubku pro známou pózu nad rovnou vozovkou,
  prohnat ji `CameraProjection.GetPointCloud` a ověřit, že se body vrátí na `z ≈ 0` na vozovce
  a na `z ≈ GrassHeight` mimo ni. Testuje, že si renderer a vize rozumí — ne že renderer
  souhlasí sám se sebou.
- **Determinismus** — stejný seed + póza → identická data; jiný seed → jiná.
- **Nulový šum** — hloubka přesně na rovině (analyticky ověřitelné).
- **Klasifikace** — tráva projde `PolarTraversabilityGrid` jako nesjízdná, vozovka jako sjízdná.

## Stav ověření

| Co | Jak ověřeno |
|---|---|
| `RoadScene`, `SyntheticFrameRenderer` | `ARBot.Common.Tests` na `x64` (13 testů vč. round-tripu a klasifikace) |
| `VirtualCamera` | `ARBot.HAL.Tests` na `x64` (3 testy, bez HW) |
| Drátování v `ARBotHW` / `ARBotRuntime` | **jen překlad** — celá aplikace se sestaví pro `OrangePI` |
| Běh aplikace se simulovanými kamerami | **neověřeno** |

Aplikaci nelze na tomto stroji přeložit pro `x64`, protože chybí složka `RealSense 2.0/`
(viz [build-and-platforms.md](build-and-platforms.md) → Externí závislosti) a bez ní se
nesestaví `ARBot.HALWindows`. Platforma `OrangePI` ji nepoužívá (bere `ARBot.HALArmbian`
se zdrojově překládaným wrapperem), takže překlad aplikační vrstvy ověřit šlo — ale skutečný
běh, obraz v UI ani chování celé smyčky se simulovanými kamerami zatím ne.

## Otevřené / budoucí

- **Sdílení mapy s `WorldViewDocument`** — runtime už síť drží (`ARBotRuntime.RoadNetwork`),
  ale UI si ji pořád načítá vlastní cestou. Sjednotit, aby byla v aplikaci jedna.
- **Drsnost trávy je per pixel, ne per místo v terénu** — výška se rozhazuje podle pixelu
  a snímku, takže při pohybu robota „bliká" místo aby byla svázaná se zemí. Pro rozptyl výšky
  v buňce polárního gridu (kvůli čemuž tam je) to stačí; pro časovou konzistenci mezi snímky ne.
  Oprava: hashovat podle kvantované světové polohy zásahu a jednou zpřesnit průsečík.
- **Virtuální GPS a IMU** — přibydou do `VirtualHWOptions`, `SetVirtualHW` je založí vedle kamer.
- **Ground-truth póza.** Až budou virtuální GPS/IMU, přestane dávat smysl brát pózu z fúze —
  fúze by četla sama sebe přes vlastní virtuální senzory. Póza pak musí jít ze simulovaného
  pohybu (ground truth). Šev je stejný: `PoseAt` se namíří jinam, `VirtualCamera` se nemění.
- **Obloha jako samostatná barva** (dnes zelená jako tráva).
- **Objekty mimo vozovku** (překážky, zdi) — dnes scéna zná jen vozovku a trávu.
