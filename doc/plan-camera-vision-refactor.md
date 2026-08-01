# Implementační plán: synchronní vlákno-per-kamera vizuální cesta

Prováděcí plán k rozhodnutí [decisions.md 2026-08-01](decisions.md). **Cíl:** odstranit GC pauzy
(200–455 ms, ~13 % snímků) přechodem vizuální cesty z async fan-outu na **synchronní zpracování na
vlákně kamery + pull + poolované buffery**. Určeno pro agenta s plným kontextem; **kroky 3–4 vyžadují
potvrzení a ověření na HW člověkem** (agent nemá kameru).

> Pokud jako agent něčemu v plánu nerozumíš nebo narazíš na rozpor s kódem, **zastav se a zeptej** — část
> kontraktů (zejména pooling/threading v kroku 4) je záměrně nechána k finálnímu potvrzení, ne k hádání.

## Tvrdá pravidla (z [CLAUDE.md](../CLAUDE.md))
- **Build a testy vždy pod `x64`**: `dotnet build … -p:Platform=x64`, `dotnet test … -p:Platform=x64`.
- **Nemazat starou/zakomentovanou implementaci, dokud novou nepotvrdí unit testy.** (Managed vs. nativní
  transform, `DepthTraversabilityProcessor` ponechat, dokud `ICameraFrameProcessor` neprojde testy.)
- **Čeština** v komentářích/dokumentaci (ASCII bez diakritiky v kódu, jako okolní kód).
- **Verzování zpráv povinné**: při změně obsahu `CameraFrame` zvedni `FormatVersion` a v `FromData`
  přidej větev pro předchozí verzi (viz [record-replay.md](record-replay.md) → Verzování zpráv).
- **Po každém kroku**: build + zelené testy; u kroků s dopadem na HW napiš, co je odsimulované a co
  nutno ověřit na zařízení; na konci doplň [devlog.md](devlog.md).
- **Souřadnice**: world ENU (0=východ,+CCW), body FLU. Robot-rel. grid: X vpřed, Y vlevo.

## Nejdřív si přečti (orientace v kódu)
- Rozhodnutí a doména: [decisions.md 2026-08-01](decisions.md), [traversability-grid.md](traversability-grid.md),
  [record-replay.md](record-replay.md).
- Zprávy/měření: `Src/ARBot.Common/Devices/CameraFrame.cs`, `SensorBase.cs`,
  `Src/ARBot.Common/Logs/Message.cs` (serializační helpery, `Verze`).
- Vize (co se refaktoruje): `Src/ARBot.Common/Vision/{DepthTraversabilityProcessor,PolarTraversabilityGridMsg,PolarGridConfig}.cs`.
- Pipeline: `Src/ARBot.Common/Communication/{MessageSource,MessageTarget,MessageProcessor,RelaySource,RoleRouter,SensorMessageSource,RecordingTarget,MessageCatalog}.cs`.
- Runtime + drátování: `Src/ARBot/Robot/{ARBotRuntime,ARBotHW}.cs`, řídicí smyčka
  `Src/ARBot.Common/Runtime/ControlLoop.cs`, scheduler `Scheduler.cs`.
- Kamera (HAL): `Src/ARBot.HALWindows/Devices/Camera/D435Camera.cs` a `Src/ARBot.HALArmbian/…/D435Camera.cs`.
- UI konzumenti: `Src/ARBot/ViewModels/{RobotCentricDocument,ImageDocument}.cs`.
- Existující testy: `Src/ARBot.Common.Tests/Vision/DepthTraversabilityProcessorTest.cs`,
  `Src/ARBot.Common.Tests/Devices/CameraFrameSerializationTest.cs`.

## Cílová architektura (rekapitulace)
- Kamera běží vlastní vlákno: grab → **`ICameraFrameProcessor.Process(CameraFrame)`** synchronně dopočte
  probability + traversability grid (grid je součást `CameraFrame`).
- Kamery **nejsou** v pipeline přes `SensorMessageSource`; **`ControlLoop` je pulluje** (nejnovější frame),
  bere grid pro řízení a **posílá na `Stream`** pro záznam/UI.
- **Buffery kamery i kopie pro recorder/UI jsou poolované s explicitním release** (recyklace, ne `new`).
- Fúze a řídicí smyčka (malé zprávy) zůstávají.

---

## Krok 1 — grid do `CameraFrame` + `ICameraFrameProcessor` (synchronně, přes stávající Stream)
**Bez změny threading modelu** — jen přesun výpočtu. Nízké riziko, plně testovatelné.

### 1a. Struktury gridu přesunout k `CameraFrame`
- Ponech `PolarCell`, `RadialEdge`, `TraversabilityClass` (dnes v `PolarTraversabilityGridMsg.cs`) — buď
  je nech v `ARBot.Common.Vision`, nebo přesuň do `Devices`; klíčové je, že `CameraFrame` na ně bude mít
  pole. Doporučeno: nová lehká třída/struct `PolarTraversabilityGrid` (bez `Message` dědičnosti) nesoucí
  `AzimuthCount, ColumnsPerCell, RadialEdge[] RadialEdges, PolarCell[] Cells` (+ helper `RadialCount`,
  indexer `[a,r]`). `ComputeMs` (diagnostika) nech jako pole tam.
- `CameraFrame` dostane `public PolarTraversabilityGrid Grid { get; set; }` (per kamera; může být null).

### 1b. Serializace `CameraFrame` (POZOR na verzování)
- `CameraFrame.FormatVersion` 1 → **2**. V `ToData` zapiš navíc grid (za stávající pole): flag „má grid",
  a pokud ano, `AzimuthCount, ColumnsPerCell, RadialEdges (len + {Range float, Row int}), Cells (len +
  {Count int, MeanX/Y/Z/StdZ/MaxZ/EdgeRange/Confidence float, Class byte})`.
- `FromData`: `case 2:` čte i grid; `case 1:` čte starý layout beze gridu (Grid=null). **Nezapomeň** — bez
  větve pro verzi 1 se starší `.rec` rozbijí.
- Diagnostické `ComputeMs` se **neserializuje** (jako dosud u zprávy).

### 1c. `ICameraFrameProcessor`
```csharp
// ARBot.Common.Vision
public interface ICameraFrameProcessor {
    // Dopocte odvozene vlastnosti (Probability, Grid) primo do frame. Vola se SYNCHRONNE.
    void Process(CameraFrame frame);
}
```
- Implementace `CameraFrameProcessor : ICameraFrameProcessor` (jedna, platformně nezávislá):
  - per-kamera konfigurace: `IDepthCameraProjection` (depth) + volitelně `IBackProject` + `PolarGridConfig`.
    Konstruktor bere projekci (nebo resolver dle `frame.Name`) — viz `BuildDepthProjectionResolver` v
    `ARBotRuntime`, tu logiku sem přenes/naparametrizuj.
  - `Process`: (1) pokud je `IBackProject`, spočti `frame.ImageProbability` (jako dnešní `BackProjectProcessor`);
    (2) spočti grid z `frame.ImageDepth` **přesunutou logikou z `DepthTraversabilityProcessor.BuildGrid`**
    (včetně nativní cesty `UseNativeTransform`, znovupoužitého `cloud` bufferu, `GetRadialEdges` cache) a
    ulož do `frame.Grid` (+ `frame.Grid.ComputeMs`).
- **`BuildGrid` neduplikuj** — přesuň jádro (accumulace, fit roviny `PlaneParams`, klasifikace, confidence,
  `RadialBin` půlením, native/managed větev) do `CameraFrameProcessor`. `DepthTraversabilityProcessor`
  zatím **ponech** (nemazat), dokud testy nepotvrdí nový kód (pravidlo CLAUDE.md).

### 1d. Volání v kameře
- V `D435Camera.GetMeasurement` (obě platformy) po naplnění `imageRGB/imageDepth` zavolej
  `frameProcessor?.Process(frame)` **před** returnem (synchronně, na vlákně kamery). `frameProcessor` se
  do kamery injektuje (property/ctor). Když je null, chová se jako dnes (jen grab).
- V `ARBotRuntime.WireRun` vytvoř `CameraFrameProcessor` per kamera (s projekcí přes stávající lazy
  resolver + `Profile.Left/RightCameraTransform`, `UseNativeTransform=true`) a nastav ho kamerám.
- **Zatím ponech** stávající drátování (SensorMessageSource → Stream) i `BackProjectProcessor` a
  `DepthTraversabilityProcessor` v grafu **vypnuté/odpojené** až v kroku 2 — v kroku 1 může běžet obojí
  paralelně kvůli srovnání, ale ať se grid nepočítá dvakrát: buď dočasně nech starou cestu a novou
  nezapojuj do Streamu, nebo naopak. (Cíl kroku 1: nový výpočet existuje a je otestovaný.)

### 1e. Testy (akceptační kritérium kroku 1)
- Přesuň/uprav testy z `DepthTraversabilityProcessorTest` na `CameraFrameProcessor`:
  `FlatGround_AllFree`, `RaisedSector_ProducesObstacle`, **`NativeTransform_MatchesManaged`** (zachovej!).
- Round-trip `CameraFrame` **v2 s gridem** (rozšiř `CameraFrameSerializationTest`) + **verze 1 → čte se
  bez gridu** (starý layout) + neznámá verze hází.
- `dotnet test -p:Platform=x64` zelené (vč. dosavadních 207).

**HW brána (člověk):** po kroku 2 (až se přepnou konzumenti) ověřit, že grid v UI vypadá stejně jako dřív.

---

## Krok 2 — konzumenti na `CameraFrame.Grid`; `PolarTraversabilityGridMsg` pryč
- `RobotCentricDocument` a `ImageDocument`: místo odběru `PolarTraversabilityGridMsg` čti `frame.Grid`
  z `CameraFrame` (obě přicházejí na `Stream`). Overlay v `ImageDocument` (rasterizace z `RadialEdge.Row`
  × `ColumnsPerCell`) beze změny logiky, jen zdroj dat = `frame.Grid`.
- `RobotCentricControl` bere `PolarTraversabilityGrid` (přejmenovaný typ) místo `…GridMsg`.
- Odstraň z grafu `DepthTraversabilityProcessor` a `BackProjectProcessor` (probability teď dělá
  `CameraFrameProcessor` v kameře) — **až teď** je smaž/odpoj a po zelených testech odstraň i
  `PolarTraversabilityGridMsg` z `MessageCatalog.CommonDefaults` a soubor (dodrž „nemazat dokud testy
  nepotvrdí"). `ControlLoop.Consume`/`FusionProcessor` beze změny.
- Diagnostický CSV log (`logs/traversability-timing.csv`) přesuň do `CameraFrameProcessor` (measure
  `wait` = teď − `frame.TimeStamp` na začátku Process, `compute` = doba Process) nebo doplň v kameře.

**Akceptační kritérium:** build + testy zelené; robot-centric i overlay stále fungují.
**HW brána (člověk):** Run, zkontrolovat grid v UI + overlay; porovnat `logs/*.csv` (mělo by být beze
změny latence — churn řešíme až krokem 4).

---

## Stav po krocích 1–2 (změřeno 2026-08-01, 1 kamera na HW)
Kroky 1–2 hotové a ověřené (log `logs/traversability-timing-<cam>.csv`):
- **`wait` se zhroutil** (avg 37→**13 ms**, max 540→**34 ms**) — synchronní přesun odstranil čekání ve
  frontě. ✔
- **`compute` teď měří probability (BackProject) + grid dohromady** (dřív dvě vlákna) → číslo NENÍ 1:1
  srovnatelné s dřívějškem; grid-only podlaha ~26 ms, s BackProjectem ~51 ms (p50).
- **GC špičky trvají** (max ~494 ms, ~10 % >100 ms) — **očekávané**, alokace se kroky 1–2 neměnily; to je
  cíl **kroku 4 (pooling)**.

### Rozhodnout před krokem 3/4: BackProject (probability) ~25 ms/snímek — ROZHODNUTO (2026-08-01)
`Process` počítá `ImageProbability` přes BackProject (~25 ms). **Rozhodnuto:** RGB probability je **potřeba
pro řízení robota** → **necháváme, jak je** (počítá se vždy, když je RGB k dispozici; žádný flag/on-demand).
Viz [decisions.md 2026-08-01](decisions.md). Buffer probability je součástí poolovaného capture slotu
(krok 4), takže „vždy počítat" nepřidává alokace v ustáleném stavu.

### ROZHODNUTO (2026-08-01) — body kroku 3
- **3.1 Forward na `Stream` = `ControlLoop`.** ControlLoop si na tiku pullne nejnovější frame z kamery
  (styl `GetLastMeasurement`: **vrací null, když není nový snímek**), **forwardne CELÝ `CameraFrame`**
  (raw + grid) na `Stream` a provede řízení. Sémantika: **bezztrátové vzhledem k použitým datům** —
  zaznamená se přesně to, co řízení reálně použilo (pokud se stíhá). Snímky nad rámec tiku se
  nezaznamenají (řízení je nesampluje) — přijatelné a konzistentní pro zpětnou analýzu.
- **DŮLEŽITÉ:** do `Stream`u jde **celý `CameraFrame`, ne jen grid** (kvůli zpětné kontrole chování
  robota). → V kroku 4 to znamená, že **raw buffery jdou async recorderu/UI** → platí kopie/pooling s
  release (memcpy do reused bufferu, ne `new`).
- **3.2 Kamery z `ARBotHW.Current`** (číst aktuální hodnotu za běhu). **POZOR na vrstvy:** `ControlLoop`
  je v `ARBot.Common`, `ARBotHW` je v app (`ARBot.Robot`) → Common **nesmí** referencovat app/HAL. Proto
  **ne** přímý `ARBotHW.Current` z ControlLoopu, ale **injektovaná abstrakce** (Common rozhraní/delegát,
  např. `Func<IReadOnlyList<CameraFrame>>` nebo `ICameraPullSource`), kterou app (`ARBotRuntime`) naplní
  čtením `ARBotHW.Current` + pull kamer. Splní záměr „ber z ARBotHW za běhu" a zachová směr závislostí
  (`Common ← HAL ← app`).
- **3.3 Fúze z kamer** (lokalizace) — mimo rozsah, později.

## Krok 3 — pull přes `ControlLoop`, odpojit `SensorMessageSource` pro kamery
**HOTOVO (2026-08-01)** — build x64 i OrangePI + testy zelené; **HW ověření pod zátěží čeká.**
Implementace: `ICameraPullSource` (Common) → `ControlLoop` pull + forward celého `CameraFrame` na `Output`
(→ Stream); `ARBotRuntime.HwCameraPullSource` čte `ARBotHW.Current` za běhu; kamery vyňaty z `BuildSensorSources`.
Test `ControlLoopTests.OnTick_PullsCameras_AndForwardsFrameToOutput`.

### Model
- Kamera běží vlastní vlákno (dnes `SensorBase.Process`): grab → `Process` → publikuje **nejnovější**
  frame (atomický handoff, viz krok 4). **Nefanoutuje** na Stream sama.
- `ControlLoop` na svém tiku (`Profile.Ts`): přečte nejnovější frame(y) z kamer (pull, jako
  `GetLastMeasurement`), vezme `frame.Grid` pro řízení, a **`Post`ne** frame na `Stream` pro záznam/UI
  (neblokující). Grid je malý → může jít referencí.

### Body kroku 3 — ROZHODNUTO (viz „ROZHODNUTO (2026-08-01) — body kroku 3" výše)
- **3.1** Forwarduje **`ControlLoop`**, na Stream jde **celý `CameraFrame`** (raw + grid). Bezztrátové
  vzhledem k použitým datům (zaznamená se, co řízení sampluje). Pull vrací **null**, když není nový snímek.
- **3.2** Kamery přes **injektovanou abstrakci** naplněnou z `ARBotHW.Current` v `ARBotRuntime`
  (`ControlLoop` v Common nesmí referencovat HAL/app).
- **3.3** Fúze z kamer — mimo rozsah.

### Změny
- `ARBotRuntime.WireRun`: kamery **neregistrovat** jako `SensorMessageSource` (viz `BuildSensorSources` —
  vyjmi kamery, ostatní senzory ponech). Přidat do `ControlLoop` pull kamer + forward na `Stream`.
- Zachovej, že ostatní senzory (IMU, GPS, motor) jdou dál přes router/Stream.

**Akceptační kritérium:** build + testy (uprav `ControlLoopTests`, pokud se dotknou); grid teče na Stream
z ControlLoopu.
**HW brána (člověk):** Run — grid v UI, řízení funguje, záznam obsahuje CameraFrame s gridem; View přehraje.

---

## Krok 4 — pooling bufferů + kopie s release (jádro zisku; nejrizikovější)
**HOTOVO (2026-08-01)** — kontrakt potvrzen člověkem (varianta „per-consumer pool + release / plný plán");
build x64 i OrangePI + unit testy poolu zelené. **HW ověření pod zátěží je KLÍČOVÁ brána a stále čeká.**
Implementace: `CaptureFramePool` (triple-buffer v kameře, obě `D435Camera`), `CameraFramePool` (per-konzument
kopie s Acquire/Release, best-effort drop) v `RecordingTarget` i `ImageDocument`; `CameraFrameProcessor`
recykluje probability + resize buffer. `RobotCentricDocument` grid nekopíruje (reference — grid je per-snímek
immutable). Testy `CameraFramePoolTest`.
**Konkurenční/lifetime kód. Unit testy tohle plně nechytnou → nutné HW ověření pod zátěží (sleduj `logs/*.csv`
a integritu záznamu/obrazu).**

### 4a. Pool capture bufferů v kameře (triple-buffer handoff)
- Kamera drží **3 sady** `CameraFrame` bufferů (RGB `Image<BGR32>`, depth `Image<Gray16>`, prob
  `Image<Gray>`, grid pole). Grab+Process píše do „back" sady; po dokončení **atomicky swap** na „latest".
  Čtenář (`ControlLoop`) čte „latest" pod krátkým zámkem a rychle si odnese, co potřebuje (viz 4b). Kamera
  smí přepsat sadu, až **není** „latest" ani právě čtená. Triple buffer → kamera nikdy neblokuje, čtenář
  má vždy stabilní snímek.
- **Kontrakt (potvrď):** kdo drží zámek a jak dlouho; co když čtenář nestíhá (kamera prostě přepisuje
  starší back buffer — nejnovější vždy k dispozici). `Image.Data` má setter — pool může buffery držet a
  jen refillovat, nebo držet celé `Image<T>` instance.

### 4b. Kopie pro async odběratele (recorder, UI) — POOLOVANÉ s release
- **GC tlak ≠ memcpy.** Každý async odběratel surového framu (recorder vždy; UI když otevřené) má **vlastní
  malý pool kopií** (např. 4 sady). Při forwardu: vezmi **volný** buffer z poolu odběratele, memcpy do něj,
  předej; odběratel po zpracování **vrátí** (release). Reuse → 0 alokací/snímek v ustáleném stavu.
- **Recorder:** `RecordingTarget` po serializaci vrátí buffer do poolu. **Nejdřív ověř retenci** (bude se
  zpráva vůbec zapisovat?) → pokud drop, kopii nedělej. Při vyschnutí poolu: **best-effort drop záznamu**
  (ne `new`, ne blokace RT).
- **UI:** `ImageDocument` po vyrenderování do `WriteableBitmap` (což už je kopie) buffer vrátí; nebo dej UI
  vlastní pooled kopii. Zvaž i recyklaci `WriteableBitmap` (TODO ve `Views/README.md`).
- **Malý `Grid`** se nekopíruje (levný, může jít referencí — ale pozor: pokud grid ukazuje do poolované
  sady, buď ho kopíruj taky, nebo zajisti, že čtenář si ho odnese při handoffu). **Doporučeno:** grid je
  součást „latest" a čtenář si při pullu odnese referenci na jeho pole; jelikož grid je malý, klidně ho
  při handoffu zkopíruj (levné) a surové buffery nech poolované.

### 4c. Kdo dělá memcpy a na kterém vlákně — ROZHODNUTO/HOTOVO
Kopii dělá **každý async odběratel sám ve svém `Post()`** (na vlákně producenta = tik, synchronně), do
svého vlastního `CameraFramePool`. `ControlLoop` při forwardu jen předá referenci na poolovaný capture slot
kamery; `RecordingTarget.Post` a `ImageDocument.Post` si z něj hned udělají stabilní kopii (memcpy ~0,3 ms/
buffer) a slot uvolní až po zpracování (serializace / render). Tím je záznam bezztrátový vzhledem k pullnutým
datům (dokud pool nevyschne → best-effort drop). `RobotCentricDocument` nekopíruje (čte jen grid referencí).

**Akceptační kritérium (co jde otestovat):** unit test poolu (acquire/release, vyschnutí → drop),
serializace beze změny, žádná regrese v gridu.
**HW brána (člověk, KLÍČOVÁ):** Run ~60 s → `logs/traversability-timing.csv`: **`compute` i celkové Δ bez
periodických 200–455 ms špiček** (churn ~0). Zkontrolovat **integritu záznamu** (přehrát ve View — obraz
i grid bez tearingu) a živý obraz v UI. Pod zátěží ověřit best-effort drop (žádný pád/koruce).

---

## Co se NESMÍ rozbít (regresní checklist)
- View přehrávání `.rec` (starých v1 i nových v2) — verzování!
- Robot-centric pohled + overlay přes depth (zarovnání `RadialEdge.Row`).
- Řídicí smyčka (motor.Drive), fúze, ostatní senzory (IMU/GPS/motor) přes Stream.
- Nativní vs. managed transform ekvivalence (test zachovat).
- Build pod `x64` i `OrangePI` (nemazat platformní HAL cesty).

## Doporučené pořadí a brány
1 → build+test → 2 → build+test → **HW ověření (člověk)** → 3 → build+test → **HW ověření** → 4 →
build+test → **HW ověření pod zátěží (člověk)**. Po každém kroku commit + devlog. Kroky 3–4 nezačínat bez
potvrzení otevřených bodů člověkem.

## Na konci
- Aktualizovat [record-replay.md](record-replay.md) (nový model vizuální cesty), [traversability-grid.md](traversability-grid.md)
  (grid v `CameraFrame`, `ICameraFrameProcessor`), doplnit [devlog.md](devlog.md), a v
  [decisions.md 2026-08-01](decisions.md) přepnout stav z „návrh" na „hotovo" s odkazy na commity.
