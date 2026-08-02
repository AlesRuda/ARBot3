# Deník rozhodnutí (decisions log)

Chronologický záznam **netriviálních rozhodnutí** na projektu — hlavně to, co by se jinak
„zahrabalo" a čeho se v kódu nedá vyčíst *proč*. Slouží jako sdílená paměť napříč sezeními
i lidmi (viz [CLAUDE.md](../CLAUDE.md), pravidlo „vše v repozitáři").

**Jak přispívat:** nové rozhodnutí přidej **nahoru** do sekce „Rozhodnutí" jako krátký blok:
*co* se rozhodlo, *proč* (kontext / alternativy), *důsledky* a *odkazy* (soubory, doc).
Absolutní datum (ne „minulý týden"). Detailní doménovou dokumentaci nech v příslušném
`doc/*.md`; sem patří jen rozhodnutí + odůvodnění + odkaz.

---

## Rozhodnutí

### 2026-08-02 — Sjednocení regulátorů: jedno `IRegulator`, jeden bodový regulátor přes `IMotionProfile` — ROZHODNUTO/HOTOVO
Navazuje na regulátor sledování dráhy (níže). Sjednocení, aby nižší smyčka regulovala transparentně na bod
i na dráhu:
- **`IRegulator` = `IPathController`** (splynuly): `Control(IModelState) → RegulatorResult` + `IsFinished`.
  Cíl (bod / dráha) drží regulátor uvnitř; profilové metody (`Dist2Speed`, …) z rozhraní zmizely (jsou v
  `IMotionProfile`). Dvě implementace: `PointRegulator` (bod) a `PathResult` (dráha).
- **`PointRegulator` nahradil `Regulator` i `SimplRegulator`.** Jediný rozdíl mezi nimi byl `IMotionProfile`
  (lichoběžník vs. odmocnina) a koeficient `stability` — obojí je teď parametr profilu. Vznikl
  `SqrtMotionProfile` (odmocninový zákon z `SimplRegulator`, ale **konzistentně** — `SimplRegulator.Control`
  počítal rotaci buggy). Staré třídy **smazány** až po důkazu parity (`PointRegulator(Trapezoid)` bit-identický
  s `Regulator.Control` přes mřížku stavů; `SqrtMotionProfile` == odmocninový zákon `SimplRegulator`), pak
  paritní testy překlopeny na golden/closed-form.
- **`ControlLoop.Path` → `ControlLoop.Regulator`** (typ `IRegulator`). Nižší smyčka teď jede libovolný regulátor.
**Odkazy:** `Src/ARBot.Common/Regulators/{IRegulator,PointRegulator,SqrtMotionProfile}.cs`, `ControlLoop.cs`,
[path-following.md](path-following.md). **Stav:** hotové, build + 242 testů zeleno.

### 2026-08-02 — Regulátor sledování dráhy: feedforward + brzdná obálka, ne proporcionální řízení — ROZHODNUTO/HOTOVO (Fáze 1–5)
Nový obecný regulátor, který robota vede **dráhou z waypointů** tak, aby každý uzel projel v rámci
`MaxPositionError` (ε) **maximální rychlostí** (uzly bez zastavení). Klíčová rozhodnutí a *proč*:
- **Feedforward + přeplánování z pózy, ne pure-pursuit `ω=v·κ`.** Statické proporcionální řízení na
  odchylku ignoruje dynamiku (accel-limit `ω`, `Ts=100 ms`, zpoždění EKF) a v tomto setupu **kmitá**
  (ověřeno z praxe). Zásah se místo toho každý tik generuje přes accel-limitovaný profil (`IMotionProfile`),
  uzavřená smyčka jde do plánu přes dynamiku, ne přes gain. Recykluje se bodová mechanika starého regulátoru.
- **Rohy kruhovým obloukem, ne klotoidou.** Chyba oblouk-vs-klotoida je na reálných parametrech **≤ ~5 mm**
  proti ε=100 mm (< 5 %), přechodová a hluboko pod nejistotou EKF (cm). Rozhoduje malý náběhový úhel
  `ω²/(2α)≈8°`. Klotoida se nevyplatí. Kryto rezervou `PathEpsilonMargin≈1 cm`.
- **Plán počítá jen zpětnou brzdnou obálku, ne dopředný průchod.** Akceleraci řeší runtime živě —
  `startSpeed = IModelState.Velocity`. Plán drží jen `VLimit(uzel)` = strop, ze kterého jde splnit budoucnost.
- **`τ_look ≈ 3·Ts` (lookahead úměrný rychlosti).** Analýza odchylky vs. `L_d` (viz doc): drží odchylku
  1–5 % ε a stabilitu při všech rychlostech (v ostrém rohu je `v` malé → `L_d` malé; `L_d/(v·Ts)` konstantní).
- **`ControlLoop.Path` jako settable property + watchdog, bez výchozí dráhy.** Vyšší smyčka (mapa/OSM)
  atomicky přehazuje dráhu; `null` = stání (bezpečný stav); zastaralá dráha (`PathControlTimeOut`) = dobrzdění
  po poslední trase. Nahrazuje dřívější pevný waypoint + starý `Regulator` v `ControlLoop`.
- **Staré regulátory (`Regulator`, `SimplRegulator`) ponechány beze změny chování** (pravidlo „nemazat staré
  dokud nové nepotvrdí testy"); `Control` narovnán na jeden waypoint (Fáze 1). Nový kód proven 237 testy
  (parita profilu, plánovač, simulace sledování, integrace).
**Odkazy:** [path-following.md](path-following.md), `Src/ARBot.Common/Regulators/{IMotionProfile,TrapezoidMotionProfile,IPathPlanner,IRegulator,PathPlanner,PathResult}.cs`, `Src/ARBot.Common/Runtime/ControlLoop.cs`, `Src/ARBot.Common/Configuration/Profile.cs`. **Stav:** Fáze 1–5 hotové (rozhraní `IPathController` později sjednoceno do `IRegulator` — viz záznam výše), build+237 testů zeleno; **ověření na HW čeká** (dynamika motorů, τ_look sweep na record/replay + selftestu, vyšší smyčka = plánovač trasy zatím neexistuje).

### 2026-08-01 — Dominantní zdroj GC pauz byla SERIALIZACE, ne kamerové buffery — ROZHODNUTO/OPRAVENO
Po nasazení kroku 4 (pooling kamerových bufferů) **200–455 ms záseky přetrvaly** (HW: `compute_ms` max 345 ms,
~11 % snímků >100 ms; `wait_ms` malý → pull OK). Root-cause: **`MessageWriter.Write` serializoval každou zprávu
přes novou `MemoryStream` + `ms.ToArray()`** — u `CameraFrame` (~1,8 MB nekomprimované) několik **LOH** alokací
na snímek (~90 MB/s na vlákně recorderu) → periodická blokující gen2 GC, která pauzovala i vlákno kamery
uprostřed `Process` (odtud špičky v `compute_ms`). **Pooling image bufferů (krok 4) to nemohl vyřešit** —
churn byl v serializaci, ne v grabu. **Oprava:** `MessageWriter` serializuje do **jedné znovupoužité
`MemoryStream`** a zapisuje přímo z `GetBuffer()` (0 alokací/zprávu, wire formát beze změny). Doplněno
poolování transientů `BuildGrid` (`acc`/`dev`/plane-fit `List`). **Poučení:** měř, kde je churn — dominantní
zdroj (40×) byl jinde, než plán předpokládal (`Src/ARBot.Common/Communication/MessageWriter.cs`,
`Src/ARBot.Common/Vision/CameraFrameProcessor.cs`). **Stav:** opraveno, build+testy zelené; **HW re-test čeká.**

### 2026-08-01 — BackProject (probability) je vstup pro řízení robota — ROZHODNUTO
Otevřená otázka „je RGB→probability (BackProject, ~25 ms/snímek) potřeba pro **řízení**, nebo **jen pro
vizualizaci**?" (viz [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md), „Rozhodnout před krokem
3/4") je rozhodnuta: **BackProject bude použit pro řízení robota.** Proto se `ImageProbability` počítá
**vždy** (když je RGB k dispozici) na vlákně kamery v `CameraFrameProcessor` — **nedělá se z něj volitelný/
on-demand výpočet** a neschovává se za flag. Důsledek pro krok 4: probability buffer je součást poolovaného
capture slotu (recykluje se jako RGB/Depth), takže „vždy počítat" nepřidává alokace v ustáleném stavu.
**Odkazy:** `Src/ARBot.Common/Vision/CameraFrameProcessor.cs` (ComputeProbability, reuse bufferu).

### 2026-08-01 — Synchronní vlákno-per-kamera pro vizuální cestu (proti GC pauzám z alokací) — HOTOVO (kroky 1–4)
Přepracovat **vizuální cestu** (kamera → vize) z dnešního async fan-outu (`SensorSource` → `RoleRouter`
→ `Stream` → N `MessageProcessor` stupňů) na **synchronní zpracování na vlákně kamery + pull**. Body:

1. **`CameraFrame` nese i odvozené** (probability, traversability grid). Grid jako **strukturovaná data**
   (`PolarCell[]` + `RadialEdge[]`), **NE `Image<PolarCell>`** (`IPixel` mismatch — buňka není pixel; a
   `RadialEdge[]` se do `Image` nevejde; reuse serializace/resize nic neušetří).
2. **`ICameraFrameProcessor`** — jedna sdílená platformně-nezávislá implementace (výpočet jede přes
   `NativeComputeUnit`), **per-kamera konfigurace** (projekce + Left/Right transform). `Process(CameraFrame)`
   se volá **synchronně v rámci kamery** a dopočte probability + grid. **Blokuje vlákno kamery** — to je
   žádoucí backpressure (kamera zpracuje, kolik stihne; ostatní snímky driver zahodí bez alokace).
3. **Kamery nejsou v pipeline přes `SensorSource`.** Běží vlastní vlákno (grab + `Process` → nejnovější
   frame v **poolovaných** bufferech). **`ControlLoop` je pulluje** (čte nejnovější grid pro řízení) a
   posílá frame na `Stream` pro záznam/UI. (Forward = jen neblokující `Post`; RT tik nezatěžovat víc.)
4. **Buffery kamery i kopie pro async odběratele (recorder, UI) jsou POOLOVANÉ s explicitním release** —
   recyklace, ne `new`. **Tvrdý požadavek** (jinak je refaktor zbytečný, viz níže).

**Proč:** Změřené GC pauzy **200–455 ms (~13 % snímků)** — periodické blokující gen2/LOH z per-snímek
alokací velkých `Image` (~1,8 MB/snímek × 30 fps ≈ 54 MB/s do LOH). Srovnání se starým **ARBot2**
(WPF/.NET 4.8) ukázalo, že tam to nevadilo **ne** frameworkem (`.NET 10` GC je lepší) ani recyklací
bufferů (starý app taky `new`oval per snímek), ale **architekturou**: pull + synchronní zpracování na
vlákně kamery + **jeden živý frame** + **málo vláken**. Nový async fan-out: 30 fps alokace, stejný frame
v mnoha (neomezených) frontách na mnoha vláknech → vysoký a dlouho žijící LOH churn + víc GC koordinace.

**Klíčový princip (jinak refaktor nemá smysl):** **GC tlak ≠ memcpy.** Zisk je v **recyklaci**, ne ve
vyhýbání se kopiím. Robot **vždy nahrává** (záznam je nutný pro zpětné prozkoumání) a odběratelé surového
framu jsou **dva** (recorder vždy, UI když otevřené) — takže „běžný stav bez kopie" ani „jeden vlastník"
neplatí. Řešení: **každý async odběratel má vlastní pool kopií** a po použití buffer **vrátí**; kamera
recykluje své buffery. Memcpy 1,5 MB ≈ 0,3 ms CPU a **nealokuje** (cíl je reused) → **~0 alokací/snímek**
v ustáleném stavu vs. dnešních ~54 MB/s. Kopie `new` každý snímek = jen posun alokace, bez zisku.
(Alternativa: refcountovaný sdílený pool bez memcpy; při málu odběratelích volíme per-konzument kopie —
jednodušší vlastnictví.)

**Důsledky / omezení:**
- Pod přetížením (recorder nestíhá disk) pool kopií vyschne → best-effort drop záznamu, nebo dočasný
  `new` (churn zpět). Ustálený stav 0.
- Mění model vizuální cesty z [record-replay.md](record-replay.md) (kroky 1–9). **Fúze** (reaktivní nad
  měřeními) a **řídicí smyčka** (periodická) zůstávají — pracují s malými zprávami.
- **`PolarTraversabilityGridMsg` zanikne** (grid je v `CameraFrame`); struktury (`PolarCell`, `RadialEdge`,
  klasifikace) i výpočet (`BuildGrid`, nativní transform, ekvivalenční test) **zůstávají**,
  `DepthTraversabilityProcessor` → `ICameraFrameProcessor`.
- `CameraFrame.ToData/FromData` + grid → **bump `FormatVersion`**.

**Sekvence (inkrementálně, ať se nerozbije naráz):**
1. `ICameraFrameProcessor` + grid v `CameraFrame`, voláno **synchronně v kameře**; zatím přes stávající Stream.
2. Konzumenti (robot-centric, overlay) na `CameraFrame.Grid`; `PolarTraversabilityGridMsg` pryč.
3. **Pull přes `ControlLoop`** + odpojit `SensorSource` pro kamery.
4. **Pooling** bufferů + per-konzument kopie s release (recorder, UI).

**Stav:** kroky 1–4 **naimplementovány** (build x64 i OrangePI + testy zelené). Kroky 1–2 ověřeny na HW
(1 kamera, `wait` avg 37→13 ms). Kroky 3–4 (pull přes `ControlLoop`, pooling + per-konzument kopie s release)
čekají na **HW ověření pod zátěží** (klíčová brána: `logs/traversability-timing-*.csv` — churn ~0, bez
periodických 200–455 ms špiček; integrita záznamu ve View bez tearingu). **Prováděcí plán (pro agenta):**
[plan-camera-vision-refactor.md](plan-camera-vision-refactor.md). **Odkazy:** [record-replay.md](record-replay.md),
`Src/ARBot.Common/Devices/{CameraFrame,CameraFramePool}.cs`, `Src/ARBot.Common/Runtime/{ControlLoop,ICameraPullSource}.cs`,
`Src/ARBot.Common/Vision/CameraFrameProcessor.cs`, `Src/ARBot/Robot/ARBotRuntime.cs` (HwCameraPullSource),
`Src/ARBot.Common/Communication/RecordingTarget.cs`, `Src/ARBot/ViewModels/ImageDocument.cs`,
analýza latence: `logs/traversability-timing.csv`, [devlog.md 2026-07-30](devlog.md).

### 2026-07-29 — Polární grid sjízdnosti z hloubkové kamery (robot-centrický, per-kamera)
Nový pipeline stupeň `DepthTraversabilityProcessor`: depth → point cloud → **polární grid** sjízdnosti
→ `PolarTraversabilityGridMsg`. Klíčová rozhodnutí návrhu:
- **Robot-centrický** (jen transformace kamery vůči tělu, ne světová póza) — detekce nezávisí na
  lokalizaci.
- **Per-kamera** grid s vlastním fitem roviny — redundance při výpadku kamery, mizí systematický
  z-offset mezi kamerami (různý pitch), v překryvu dva nezávislé hlasy pro kartézskou vrstvu.
- **Azimut = konstantní počet sloupců** (`ColumnsPerCell`, N=16 → 30 buněk), ne konstantní Δθ —
  celočíselné mapování obraz→buňka, dělitelnost šířky; reálné úhly z `Camera2DToCamera3D`.
- **Radiálně Δr = max(5 cm, pro cíl bodů)** — 5 cm blízko (návaznost na kartézský occupancy ~5 cm),
  roste s dálkou; **tvrdá podlaha 8 bodů → `Unknown`** (a `Unknown` ≠ `Free`, nezapisovat jako sjízdné).
- Buňka nese i **`Confidence`** (váha pro agregaci) a **`EdgeRange`** (sub-buňková náběžná hrana pro
  „vejde se robot" místo plného TSDF — 2D distance transform + přesná hrana).
- **Depth→cloud managed** (přes projekci), ne nativní `Segment2` (padá na x64) — plně testovatelné.
- **Proč tyto parametry:** hustota depth bodů na plochu klesá ~1/r² (konstantní úhlové vzorkování),
  polární grid s rostoucím Δr drží ~konstantní počet bodů/buňku; odvození řádek→vzdálenost viz
  [doc/traversability-grid.md](traversability-grid.md).
- **Zapojení do runtime:** v **Run** jako stupeň grafu (`ARBotRuntime.WireRun`), projekce líně z živé
  kamery + `Profile.Left/RightCameraTransform`. Ve **View** se grid **nepřepočítává, jen přehrává**
  zaznamenaný (rozhodnuto 2026-07-30) — přepočet ze záznamu odložen, protože živé intrinsics se
  nezaznamenávají (offline projekce by chtěla nominální intrinsics nebo rozšíření formátu `.rec`).
- **Vizualizace:** dokument je obecně **robot-centrický** (`RobotCentricDocument`/`RobotCentricControl`),
  grid sjízdnosti je první vrstva (časem RGB sjízdnost, okraje vozovky). Tvar robotu je ve sdílené
  `RobotGlyph` (parametr orientace + pozice) — použitelné i pro budoucí world view.
- **`RadialEdge { Range, Row }`:** radiální hrana nese metry **i řádek depth obrazu**, kde se láme →
  grid jde vykreslit přímo přes depth snímek (bez samostatného obrázku tříd, který by zbytečně nafukoval
  data). Overlay přes depth se tak počítá z `PolarTraversabilityGridMsg` (sloupce z `ColumnsPerCell`,
  řádky z `Row`).
- **Stav:** geometrie + klasifikace ověřeny syntetickým testem (kamera shora); prahy/šumový model
  se doladí na reálných datech.
- **Odkazy:** `Src/ARBot.Common/Vision/{DepthTraversabilityProcessor,PolarTraversabilityGridMsg,PolarGridConfig}.cs`,
  test `Src/ARBot.Common.Tests/Vision/DepthTraversabilityProcessorTest.cs`, registrace v `MessageCatalog.CommonDefaults`.

### 2026-07-25 — `Blob` → `ImageMsg`; obraz jako `Image`, bez `BlobType`/`Data`; komprese v serializaci
Původní `Blob` (BlobType + syrové `Data` + lazy JPEG) přejmenován na **`ImageMsg`** a přepracován:
nese přímo netypový **`Common.Image`** (pixel typ = identita, `PixelTypeName`), `Data` a `BlobType`
zrušeny. Serializaci obrazu řeší statické `ImageMsg.Write(bw, Image, Compression)` /
`ReadImage(bw)` (rekonstrukce přes `Image.Create` z uloženého názvu typu), komprese
`None/Deflate/Jpeg/Png` je per-zpráva ve vlastnosti `Comp`. Vizuální „druh" (`LayerKind`
Color/Probability/Depth) se v `MessageImageLayers` odvozuje z pixel typu (BGR32/RGB/BGR→Color,
Gray→Probability, Gray16→Depth) místo dřívějšího `BlobType`.
- **Proč:** čistší model (obraz je obraz, ne generický blob dat), self-popisný záznam a
  volitelná komprese na jednom místě; odstranění duplicitní identity (BlobType vs pixel typ).
- **Enablery:** netypový base `Common.Image` (z něj dědí `Image<T>`) + `Image.Create(name,w,h)`.
- **Rozsah:** aktivní cesta (`BackProjectProcessor`, `MessageImageLayers`, `ImageDocument`,
  katalog, recording limit `"ImageMsg"`, `ARBot.Record`) převedena; legacy `ToLogMessage`
  (LocalMap/GridNavigation…) převedeny na `Image<Gray>`; mrtvé/nekompilované ARBot2 soubory
  (Driver, MessageQueue komentář) ponechány. Testy převedeny, build 0 chyb, Common 200 / HAL 12.

### 2026-07-25 — Verzování zpráv: `Message.Verze` + větvení `FromData` podle uložené verze
Každá `Message` nese verzi formátu, ve kterém vznikla (`Message(name, verze)`). Rámec záznamu
verzi ukládá (`MessageWriter`: `MsgName:délka:Verze`) a `MessageReader` ji před `FromData` nastaví
na uloženou hodnotu. Pravidlo: `ToData` píše vždy aktuální layout; `FromData` větví podle
`this.Verze` a starší formát namigruje do aktuálního modelu; **při každé změně obsahu zprávy se
verzní konstanta zvedne** a přidá se čtecí větev pro předchozí verzi.
- **Vynuceno typem:** `SensorStateBase(int verze)` verzi **vyžaduje** (nemá bezparametrický ctor),
  takže každý senzorový stav musí předat svou konstantu (konvence `public const int FormatVersion`).
- **Proč:** dopředná kompatibilita — starý `.rec` musí jít přehrát i po změně zpráv.
- **Důsledek:** princip a I/O tok rozepsány v [record-replay.md → Verzování zpráv](record-replay.md).
  Dle tohoto principu je od 2026-07-25 hotová i serializace `CameraFrame` (`FormatVersion`,
  `FromData` větví podle `Verze`); surové framy se ale defaultně nezaznamenávají (limit 0, RGB je v
  záznamu jako JPEG `Blob`).

### 2026-07-25 — Run rozdělen na „Run without log" / „Run and log"; jméno záznamu `yyyyMMdd-HHmmss.rec`
Menu **Runtime** má dvě varianty spuštění: bez záznamu a se záznamem. „Run and log" pojmenuje
výstup automaticky `yyyyMMdd-HHmmss.rec` ve složce **`records/` v kořeni repa** (sidecar index
`.rec.idx` řeší runtime; složka se vytvoří). Kořen se hledá směrem nahoru přes marker `.git`
(`MainWindowViewModel.RepoRootOrBase`), fallback = `AppContext.BaseDirectory` (nasazení bez repa,
např. na Pi). `records/` je v `.gitignore` (velké binární logy se necommitují).
- **Proč:** dřívější „Run" volal `Start(Mode.Run)` bez cesty → runtime nenahrával. Uživatel chce
  vědomou volbu a bezklikové logování s časovým razítkem; záznamy mít na stabilním místě (ne pod
  `bin`, které se maže při Clean).
- **Důsledek:** `MainWindowViewModel.RunAndLog` + `RepoRootOrBase`, menu **Runtime → Run and log**;
  cesta se vypíše do Debug output. Přehrání přes **Runtime → View…**.

### 2026-07-25 — Paměť/poznatky výhradně v repu, žádná externí paměť
Poznatky, poznámky a rozhodnutí se ukládají jen do repa (`doc/*.md`, README, komentáře v kódu).
Externí „memory" úložiště harnessu (`~/.claude/…`) se **nepoužívá** — je mimo git a nejde sdílet
s týmem. Tento soubor vznikl jako „catch-all" na rozhodnutí, která nezapadají do konkrétního
doménového docu.
- **Proč:** potenciální spolupráce více lidí; CLAUDE.md se navíc čte na začátku každého sezení,
  takže repo je zároveň paměť napříč sezeními.
- **Důsledek:** CLAUDE.md = rozcestník „vždy v kontextu"; detaily v `doc/` (načítají se při práci
  v dané oblasti). Viz [CLAUDE.md](../CLAUDE.md).

### 2026-07-25 — Backpressure UI dokumentů: „latest-wins + Background flush" (povinný vzor)
Dokumenty přijímající data z `MeasurementArived` / `IMessageSink.Post` nesmí postovat na UI
vlákno každou zprávu — jen uloží nejnovější (starší zahodí) a koalescovaně naplánují jeden
`Flush` na `DispatcherPriority.Background`.
- **Proč:** producent (kamera ~30 Hz, IMU/motor ~100 Hz, backproject) přetékal dispatcher frontu
  → UI zamrzalo a zpracovávalo staré framy („stall → dávka stovek Hz → zpět"). `RelaySource`
  fan-out běží na vlákně producenta a nemá frontu, takže odběratel musí být neblokující.
- **Důsledek:** aplikováno v `CameraDocument`, `D435TestDocument`, `IMUDocument`, `GpsDocument`,
  `MotorControlDocument`, `ImageDocument` (dict pending per zdroj); `DebugOutputTool` obdobně.
  Vzor a šablona kódu: [Src/ARBot/Views/README.md](../Src/ARBot/Views/README.md).

### 2026-07-25 — DebugOutputTool: virtualizovaný list řádků místo jednoho `string`
Debug/Trace výstup drží `ObservableCollection<string>` zobrazenou virtualizovaným `ListBox`em
(dřív jeden velký `string` v `TextBox`).
- **Proč:** velký `TextBox` se při každé aktualizaci celý přeskládával (`BidiData` na UI vlákně)
  a s délkou logu ztrácel responzivitu.
- **Důsledek:** koalescované dávkové přidávání + ořez s hysterezí (`MaxLines`); render jen
  viditelných řádků. Soubor `Src/ARBot/ViewModels/DebugOutputTool.cs`.

### 2026-07-25 — Řídicí smyčka + UART odolné vůči nedostupným portům
Časovač `ControlLoop.Pump` má reentrancy guard (`Interlocked`), `Uart.ReOpen` je neblokující
(timestamp backoff místo `Thread.Sleep`), blokující čtení jde přerušit přes `IUart.CancelRead`
a `SensorBase.Process` má idle-backoff.
- **Proč:** při nedostupných COM portech blokoval `Drive()` ~3 s v `ReOpen` a `System.Threading.Timer`
  callbacky se překrývaly → exploze vláken (~180) a zamrznutí UI; blokující `Read` navíc věsel
  `SensorBase.Stop()` (`task.Wait()`).
- **Důsledek:** soubory `Uart.cs`, `UartSensorBase.cs`, `SensorBase.cs`, `ARBotRuntime.cs`,
  `SDC2160Ex.cs`/`SDC2160.cs`; test `ARBot.HAL.Tests/UartCancelReadTests.cs`. `Stop()` senzoru
  nejdřív nastaví `stopRequired`, pak `CancelRead()` (pořadí kvůli race).

### 2026-07-25 — Znovuotevírání dokovacích nástrojů přes sdílený `ReopenTool`
Nástroje (Sensors overview, Debug output) mají v `DockFactory` stabilní referenci a v menu
příkaz, který je znovuotevře přes společný `MainWindowViewModel.ReopenTool` (ošetřuje stavy
pinned/hidden/odpojený).
- **Proč:** `DebugOutputTool` se po zavření nedal znovu otevřít (nikde nedržená reference).
- **Důsledek:** `DockFactory.DebugOutput`, menu **Tools → Debug output**.

### 2026-07-25 — Nativní knihovna se staví CMakem a NENÍ v gitu
`NativeFuncs/bin/NativeLib.dll` (a `libNativeLib.so`) jsou build artefakty CMake, ne git.
Nesmí se mazat spolu s `bin`/`obj` — `ARBot.Common.csproj` je pro x64 kopíruje bez `Exists`
guardu, takže jinak build padá (`MSB3030`).
- **Proč:** zjištěno při čištění `bin/obj` (omylem smazána `NativeLib.dll`).
- **Důsledek:** postup rebuildu (vcvars + `cmake --preset windows-x64`) v
  [doc/build-and-platforms.md](build-and-platforms.md).

---

## Dříve učiněná rozhodnutí (kanonicky v doc/ nebo CLAUDE.md)

Rozhodnutí z dřívějška, jejichž odůvodnění je už rozepsané jinde — zde jen jako rozcestník
(přesná data viz git historie):

- **Build jen pod konkrétní platformou (x64 / OrangePI), ne AnyCPU** — kvůli nativním
  závislostem (Intel.RealSense). → [build-and-platforms.md](build-and-platforms.md), [CLAUDE.md](../CLAUDE.md)
- **Vlastní MSBuild platforma `OrangePI`** (ne `ARM64` = Windows-on-ARM, ne RID) a solution
  `.slnx` místo `.sln`. → [build-and-platforms.md](build-and-platforms.md)
- **Platformově dedikovaný HAL** (`HALWindows` 2.47 / `HALArmbian` 2.53, stejný namespace). →
  [architecture.md](architecture.md), [build-and-platforms.md](build-and-platforms.md)
- **Souřadnicové konvence:** world ENU + matematická orientace, body FLU. →
  [imu-and-frames.md](imu-and-frames.md)
- **EKF senzorická fúze** (přepis na generický `Ekf` → `EKFModel`, async replay). →
  [ekf-fusion.md](ekf-fusion.md)
- **Pipeline zpráv pro záznam/přehrávání** (`MessageSource`/`Target`, role, taps). →
  [record-replay.md](record-replay.md)
- **Při migracích nemazat starou/zakomentovanou implementaci, dokud ji nepotvrdí testy.** →
  [CLAUDE.md](../CLAUDE.md)
- **Jazyk: čeština** (komunikace, komentáře, dokumentace). → [CLAUDE.md](../CLAUDE.md)
