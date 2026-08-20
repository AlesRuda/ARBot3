# Virtuální HW (simulované senzory)

Simulované senzory, které se v aplikaci tváří jako reálné — robot „vidí" scénu odvozenou
z načtené OsmNav mapy, **jezdí po ní** a měří se zašuměnými GPS/IMU/odometrií, bez připojeného
hardwaru. Obyvatelé: **`VirtualCamera`** (náhrada D435), **`VirtualMotors`**, **`VirtualGps`**
a **`VirtualImu`** nad společným ground-truth modelem **`SimulatedRobot`**.

> **Stav (2026-08-13): implementováno a otestováno včetně uzavřené smyčky.**
> Běh celé aplikace se simulovaným HW zatím neověřen.

## Účel

Tři doložené případy užití (určují míru věrnosti):

1. **Vývoj vizuální cesty bez HW** — ladit řetěz depth → polární grid → occupancy →
   lokální plánování na počítači bez kamer. Rozhoduje **geometrická** věrnost, ne
   fotorealismus.
2. **Reprodukovatelné automatické testy** — deterministický vstup pro testy vizuální
   cesty. Šum proto musí být seedovatelný a vypnutelný.
3. **Uzavřená smyčka** — robot v simulaci skutečně jede: regulátor řídí motory, ty posouvají
   ground truth, senzory ho zašuměně měří a fúze ho odhaduje zpět.

Mimo rozsah: fotorealistický vzhled, jiné objekty než vozovka a okolní tráva, prokluz kol
a dynamika podvozku.

## Architektura

Renderer je oddělený od kamery — tři jednotky s jasnou hranicí:

| Kde | Co | Proč tam |
|---|---|---|
| `ARBot.Common/Vision/Synthetic/RoadScene.cs` | Geometrie scény v lokální ENU rovině: úseky vozovky (osa + šířka) postavené z `RoadNetwork` přes `GeoReference` + prostorový index | Čistý algoritmus bez HW → `Common` (směr závislostí `Common ← HAL ← app`) |
| `ARBot.Common/Vision/Synthetic/SyntheticFrameRenderer.cs` | Vlastní vykreslení: (scéna, póza, projekce) → naplní `Image<BGR32>` + `Image<Gray16>` | Jádro, které se testuje deterministicky. Nezná `ICamera` ani senzory |
| `ARBot.HAL/Devices/Camera/VirtualCamera.cs` | `SensorBase<CameraFrame>, ICamera` — časování snímků, capture pool, `CreateProjector()` / `CreateDepthProjector()` | Bez platformní závislosti → do **`ARBot.HAL`**, ne do `HALWindows`/`HALArmbian`. Jedna kopie pro x64 i OrangePI a **nezávislá na Intel.RealSense** |
| `ARBot.Common/Simulation/SimulatedRobot.cs` | **Ground truth**: skutečná póza (X, Y, Θ), rychlosti kol, integrály enkodérů; `Drive`, `SetAcceleration`, `Advance(t)` | Čistý model pohybu bez HW → `Common`. Thread-safe: motory do něj píšou z řídicí smyčky, senzory čtou každý ze svého vlákna |
| `ARBot.HAL/Devices/MotorDriver/VirtualMotors.cs` | `IMotorControl` — příkazy do simulátoru, zpět `MotorStateBase` (odometrie) | Slupka nad modelem, stejný vzor jako `VirtualCamera` |
| `ARBot.HAL/Devices/GPS/VirtualGps.cs` | `IGPS` — pravá póza → `GeoReference` → LLA + šum → `GPSState` | |
| `ARBot.HAL/Devices/AHRS/VirtualImu.cs` | `IIMU` — pravý kurz jako kvaternion + `omega` z gyra + šum → `IMUState` | |

### Projekce

`CameraProjection` (`ARBot.Common/Coordinates`) implementuje `ICameraProjection`
i `IDepthCameraProjection` a staví se jen z `Intrinsics` + matic — bez RealSense.
`VirtualCamera` si vyrobí syntetické intrinsics ve stylu D435 (pinhole,
`Distortion.None`, `Fx = (W/2) / tan(HFOV/2)`) a **tutéž instanci použije k vykreslení
i vrátí z `CreateProjector()` / `CreateDepthProjector()`**.

To je záměr, ne úspora: vize dostane přesně tu projekci, kterou byl obraz myšlen, takže
neshoda v hloubkové cestě je skutečná chyba, ne artefakt simulace.

## Šev: `SetNoHW` / `SetRealHW` / `SetVirtualHW`

```csharp
// ARBotHW
public HwMode Mode { get; }                    // None / Real / Virtual
public void SetNoHW();                         // uvolní VŠECHNO (kamery i UART senzory)
public void SetRealHW();                       // skutečné kamery + IMU/GPS/motor
public void SetVirtualHW(VirtualHWOptions o);  // simulované senzory
```

**Po startu aplikace neběží žádný hardware** (`HwMode.None`) — `Init()` jen zjistí porty
a nic neotevře. Co se založí, určuje `ARBotRuntime.RequestedHwMode`, a stane se to
až v `ARBotRuntime.Start(Run)`.

`SetRealHW` i `SetVirtualHW` volají na začátku `SetNoHW()`, takže **přepnutí je čisté v obou
směrech**. Dřív to byla jednosměrka: `SetRealHW` zakládal kamery jen `if (LeftCamera == null)`,
a po virtuálním HW ta pole null nejsou — skutečné kamery se tedy už nikdy nevrátily. Proto taky
po přechodu Run → View zůstávaly viset virtuální kamery a renderovaly na pozadí.

`SetNoHW` uvolňuje i **UART porty** (`UartAHRS`/`UartMotor`/`UartGPS`); bez toho by je následný
`SetRealHW` nemohl znovu otevřít.

### Volba režimu

- **Parametr `virtualhw=true`** → požadovaný režim `Virtual`; bez něj `Real`.
  Samostatný parametr na „žádný HW" nemá smysl — to je stav po startu, než se pustí Run.
- **Menu `Runtime → Hardware`** (Žádný / Reálný / Virtuální) mění požadovaný režim; přepínat
  lze **jen se zastaveným runtime**. `Žádný` uvolní HW hned, ostatní dva se projeví až při Startu.
- **Bez mapy se virtuální HW nezaloží a zůstane `None`** — záměrně *ne* fallback na reálný,
  aby se při žádosti o simulaci nerozjely skutečné kamery.
- **`Start(View)` hardware uvolní** — přehrávání záznamu ho nepotřebuje.

Obojí prochází existujícími `CameraStop()` / `CameraStart()`, které už dnes slouží
k výměně kamer za běhu — nezavádí se nový životní cyklus.

`VirtualHWOptions` nese **vše potřebné**:

- `RoadNetwork Network` — scéna,
- `GeoReference Origin` — počátek lokální ENU roviny,
- `Func<DateTime, RobotState> PoseAt` — zdroj pózy,
- montážní transformace kamer (jinak z `Profile.Left/RightCameraTransform`),
- parametry vzhledu a šumu (viz níže).

### Pořadí volání

`ARBotHW.Current` vzniká v `ARBotRuntime.Start()` **dřív** než `AsyncFusionEngine`.
`SetVirtualHW` proto volá `ARBotRuntime.Start()` až **za** vytvořením enginu, takže
`PoseAt = t => engine.GetStateAt(t)` jde předat rovnou v opcích a nic se nedodrátovává dodatečně.

**Právě proto menu jen nastavuje požadovaný režim a nezakládá HW samo.** Virtuální hardware
nejde vytvořit dřív než fúzi (zdroj pózy) a mapu, takže jediné místo, kde to jde udělat
konzistentně pro oba režimy, je `Start`.

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

Načtená síť se navíc publikuje na `Stream` jako `MapMsg` (`ARBotRuntime.MapMessage`) — až
**úplně nakonec** `WireRun`, kdy je připojený záznam i otevřené dokumenty. World view ji tedy
vykreslí sám a současně se uloží do záznamu, takže se **přehraje i ve View**. Pohled otevřený
až za běhu by jednorázovou zprávu prošvihl (Stream zprávy nepřehrává), proto si ji při otevření
vyzvedne z `ARBotRuntime.MapMessage`.

`WorldViewDocument` si pořád **umí** načíst mapu i vlastní cestou (file picker) — ta cesta zůstává.

### Parametry příkazové řádky

| Parametr | Význam |
|---|---|
| `virtualhw=true` | zapne simulovaný HW (kamery, motory, GPS, IMU) místo reálného |
| `map=<cesta.osm>` | mapa, ze které se scéna renderuje (bez ní zůstane reálný HW) |
| `roadwidth=<m>` | výchozí šířka cesty pro uzly bez `width` (default 3) |
| `start=lat,lon[,kurz]` | známá počáteční póza → vloží se do EKF (**platí i pro reálný HW**); bez ní se v simulaci přichytí na nejbližší cestu |
| `poseerror=vpřed,vlevo[,stupně]` | umělá chyba pózy vnucená do renderu kamer (metry v rámci robotu, kurz ve stupních) — viz níž |

Zapnutí je **best-effort**: chybějící nebo vadná mapa simulaci jen nezapne (a zaloguje důvod),
nikdy neshodí start aplikace.

## Pohyb: `SimulatedRobot` a virtuální motory

Model pohybu je **ideální plus rampa zrychlení** — žádný prokluz ani systematická chyba.
Rychlost každého kola se rampuje k cíli podle `SetAcceleration`, pak se integruje unicycle:

```
v = (vL + vR) / 2        omega = (vR − vL) / rozchod
X += v·cosΘ·dt           Y += v·sinΘ·dt           Θ += omega·dt
```

### Motory jsou přesná inverze odometrie

`VirtualMotors.Drive(v, difSpeed)` rozloží příkaz na `vR = v + difSpeed`, `vL = v − difSpeed`.
Když pak `DefaultMeasurementMapper` spočítá z hlášených rychlostí kol `omega = (vR − vL)/rozchod`,
vyjde **přesně to `omega`, které chtěl regulátor** (`difSpeed = omega·rozchod/2`). Je to táž
symetrie jako u kamery (renderer = inverze rozbalení hloubky) a ze stejného důvodu: neshoda pak
znamená skutečnou chybu, ne artefakt simulace.

Dvojitou negaci ve skutečném driveru (`SDC2160Ex.Drive` posílá `-CalcSpeed(...)` u obou složek —
kompenzace zapojení motorů) simulace **nekopíruje**; to je detail hardwaru, ne konvence rozhraní.

> **Pozor na zastaralý komentář.** `IMotorControl.Drive` v první větě tvrdí „Positive value -
> right rotation, left motor is faster", ale hned pod tím uvádí `difSpeed = omega·rozchod/2`
> a MicroBasic skript v jednotce má u rotační rychlosti „kladná hodnota je v matematickém smyslu".
> Platí formule: **kladné `difSpeed` = otáčení doleva (CCW)**. První věta je relikt.

### Start robota — známá póza jde rovnou do EKF

Parametr `start=lat,lon[,kurzDeg]` **není věcí simulace**: pokud je zadaný, vloží se poloha
rovnou do filtru přes `AsyncFusionEngine.InitializePosition` a kurz se pošle jako
`HeadingMeasurement`. Platí to **i pro reálný robot** — když vím, kam jsem ho postavil, nemá
smysl to filtru tajit a čekat, až polohu určí první GPS fix. Následná měření polohu jen korigují.

Je to přesně případ, pro který `InitializePosition` vznikla: filtr startuje s `P0 = I` (σ = 1 m),
takže první fix stovky metrů daleko by gating zahodil a robot by se „nenašel". Rozhodnutí
„téhle poloze už věřím" patří volajícímu.

Kurz se **inicializuje taky** (`AsyncFusionEngine.InitializeHeading`), od 19. 8. 2026.

> **Změna rozhodnutí.** Do té doby se kurz jen posílal jako měření s odůvodněním „na rozdíl od
> polohy je jeho chyba omezená a filtr si ho srovná". Neplatí to ze dvou důvodů. Za prvé: při
> `P0 = I` je σ kurzu **1 rad (57°)**, takže měření o 170° vedle — a přesně to nastane, když robot
> míří na západ — má NIS ~8,7 proti χ²(1; 0,95) = 3,84 a po zapnutí gatingu by se **zahodilo**.
> Je to tatáž latentní past, jakou u polohy popisuje `InitializePosition`. Za druhé: „filtr si ho
> srovná" znamená, že po nějakou dobu je kurz špatný — a `LocalNavigator` mezitím zapisuje do
> **world-ukotveného** occupancy gridu buňky s tím špatným kurzem. První korelace s mapou z nich
> vycházela s **opačným znaménkem**. Když kurz znám, není důvod ho filtru tajit a nechat ho k němu
> dojít přes měření. Podrobně v [map-correlation-localization.md](map-correlation-localization.md).

Bez `start=` se póza hádá **jen v simulaci**: robot se přichytí na nejbližší hranu sítě
(`RoadNetwork.NearestEdge`) od středu mapy a natočí se podél ní, takže vždy stojí na cestě.
Na reálném HW se bez zadání nic neinicializuje a zůstává původní chování (první GPS fix).

Vedlejší, ale praktický efekt: kamera bere pózu z fúze, takže **bez inicializace by nedodávala
snímky až do prvního fixu**. Se zadaným startem jede od začátku.

## Senzory: GPS a IMU

Obojí čte ground truth a přidává šum; frekvence jsou blízko skutečným zařízením.

| Senzor | Takt | Co produkuje |
|---|---|---|
| `VirtualImu` | 100 Hz | `IMUState`: `Rotation` (kvaternion z pravého kurzu), `AngularVelocity.Z` = `omega`, `OrientationUncertainty.X` = σ kurzu |
| `VirtualGps` | 5 Hz | `GPSState`: `Latitude`/`Longitude` ve **stupních**, `Quality = GpsFix`, `Speed`, `NumberOfSatellites`, `Hdop` |
| `VirtualMotors` | 50 Hz | `MotorStateBase`: rychlosti kol + integrály enkodérů, `IsEmergencyStop = false` |

**Tři pasti, které testy hlídají** (všechny jsou v kódu explicitně varované):

- `GPSState.Latitude/Longitude` jsou ve **stupních**, `LLA` drží radiány. Mapper na to má
  varování velkými písmeny — chyba znamená posun o stovky kilometrů bez jediného hlášení.
- `IMUState.Rotation` je kvaternion, ze kterého mapper bere `YPR().Yaw`; musí vyjít přesně
  skutečný kurz v ENU konvenci.
- `AngularVelocity.Z` je v **body** rámci.

Šum je stejného druhu jako u kamery — čistá funkce `(seed, pořadí vzorku, kanál)`, každá složka
vypnutelná nulou. Volba „ideální motory" se týká **modelu pohybu**, ne senzorů: odometrie hlásí
přesné rychlosti kol, ale GPS a IMU šum mají, jinak by fúze neměla co opravovat.

| Parametr | Default |
|---|---|
| σ polohy GPS | 1,5 m |
| σ rychlosti GPS | 0,1 m/s |
| σ kurzu IMU | 1° |
| σ gyra | 0,5 °/s |
| družic ve fixu | 12 |
| HDOP | 0,9 |

Družice a HDOP jsou **jen kosmetika pro UI a logy** — simulace geometrii družic nemodeluje.
Konstanty tam přesto jsou proto, že prázdná (nulová) hodnota v telemetrii vypadá jako rozbitý
údaj; HDOP se do virtuálního fixu doplnil až 17. 8. 2026, starší záznamy ze simulace mají nulu.

### Rychlost kol měří driver (oprava, `MotorStateBase` verze 2)

Do verze 1 hlásil `MotorStateBase` v `LeftEncoder`/`RightEncoder` **přírůstek od posledního
vyzvednutí** a rychlost z něj dopočítával jako `LeftEncoder / FramePickupPeriod`.
`FramePickupPeriod` se ale odvozuje od `lastPickupTimeStamp`, který nastavuje **jedině
`GetLastMeasurement()`** — a v runtime se motory odebírají **událostí** (`MotorSource`),
takže je nevyzvedával nikdo kromě UI dokumentu `MotorControlDocument`. Bez otevřeného okna
motorů tak `LeftWheelSpeed` vracela 0 a do EKF teklo `Velocity(0)` a `AngularRate(0)`
padesátkrát za sekundu; s otevřeným oknem se rychlost počítala přes interval překreslování UI.
Týkalo se to **i reálného robota**.

Od verze 2 platí:

- **rychlost kol je vlastní pole zprávy**, které plní driver ze **svého** vzorkovacího intervalu.
  Rychlost je tak vlastnost měření v jeho čase, ne vlastnost toho, kdo a kdy si ho přečetl —
  což je podstatné pro EKF i `SlipDetector`;
- **enkodéry jsou kumulativní**, takže si libovolný odběratel spočítá přírůstek přes svůj
  interval, a nepřijde o něj, ani když nějaký vzorek přeskočí.

Tím zmizel sdílený stav mezi oběma cestami odběru, místo aby se zdvojoval. Mimochodem přesně
takhle to dělal původní (dnes zakomentovaný) driver `MD23` — `left + last.LeftEncoder`
kumulativně a rychlost zvlášť; odchýlil se až `SDC2160Ex`.

Starší záznamy (verze 1) se načtou dál, ale **rychlosti kol v nich nejsou** a zpětně je nelze
dopočítat: enkodér je v nich přírůstek a doba vyzvednutí se neserializovala.

### Časování

`SimulatedRobot` integruje **na vyžádání**: `Advance(t)` pod zámkem, každý senzor si před čtením
posune stav na svůj čas. Hodiny jsou `TimeBase.Now`, tedy tytéž, které razítkují snímky kamery.
Za běhu to bitově reprodukovatelné není (`dt` z reálných hodin); v testech se `Advance(t)` volá
s explicitními časy, takže tam determinismus je.

### Póza kamery zůstává z fúze

Kamera bere pózu dál z `engine.GetStateAt(t)`, ne z ground truth — **vědomé rozhodnutí**.
Důsledek: obraz vždy „sedí" s odhadem, takže **chyba lokalizace není v obraze vidět**. Kdyby
bylo potřeba ji zviditelnit, stačí `PoseAt` namířit na `SimulatedRobot`; nic dalšího se nemění.

Praktický důsledek: `GetStateAt` vrací `null`, dokud fúze nemá inicializovanou polohu, takže
kamera začne dodávat snímky **až po prvním virtuálním GPS fixu** (do té doby snímky přeskakuje).

### Umělá chyba pózy (`poseerror`)

Předchozí odstavec má nepříjemný důsledek pro **korelaci occupancy gridu s mapou**: obraz i
ukotvení gridu vycházejí z téže pózy, takže grid s mapou souhlasí vždycky a korelátor hlásí
`Dx = Dy = 0` **strukturálně** — i kdyby byl rozbitý. Ve virtuálním HW tedy jeho výsledek sám
o sobě nic nedokazuje (viz [map-correlation-localization.md](map-correlation-localization.md)).

Léčba je vnutit do **renderovací** cesty známý posun:

```
kamera renderuje z:  P_odhad ⊕ e          grid se ukotví na:  P_odhad (beze změny)
```

Obsah gridu se tím proti mapě posune o `−e`, což je přesně totéž, jako kdyby robot ve skutečnosti
stál na `P_odhad + e`. Protože korelátor hlásí „skutečná poloha = odhad + D", musí vyjít **`D = e`**
— predikce se známou odpovědí. Ověřeno na jednotky milimetrů (tabulka měření je ve specifikaci
korelace).

**Rámec je robotu** (FLU: vpřed, vlevo, kurz), protože otevřená vada „falešná podélná jistota" je
právě o rozdílu podél vs. napříč cesty. Do světových složek to převádí
`VirtualPoseError.ExpectedWorldOffset(theta)`.

Kód: [`ARBot.Common/Simulation/VirtualPoseError.cs`](../Src/ARBot.Common/Simulation/VirtualPoseError.cs)
(čistá funkce, má testy), sdílená instance na `ARBotHW.VirtualPoseError`, vlepená v `ARBotRuntime`
do `PoseAt`. Nastavit ji jde dvěma cestami:

- **`poseerror=vpřed,vlevo[,stupně]`** na příkazové řádce — pro reprodukovatelné bezobslužné měření.
- **Nástroj nad virtuální kamerou** — dvojklik na `Left`/`Right` v panelu Sensors otevře
  `VirtualCameraDocument`: standardní náhled plus panel, kde jde chybu měnit za běhu a kde se
  **očekávané** hodnoty ukazují vedle **naměřených** z `MapCorrelationMsg`.

Tři vlastnosti, na kterých záleží:

- **Sdílená oběma kamerami.** Kdyby měla levá jinou chybu než pravá, fúzovaný grid by nedával smysl.
- **Nemutuje stav fúze.** `Apply` vrací kopii — jinak by injektáž prosákla zpátky do filtru
  a experiment by se sežral sám.
- **GPS a IMU dál měří pravdu.** Kdyby se zkazily i ony, filtr se rozjede a známá odpověď zmizí.
  Chyba je záměrně jen na straně obrazu.

> **Až se zapnou korekce** (`MapCorrelatorConfig.Enabled = true`), přestane to být statická pravda:
> korelátor začne odhad tlačit, kamera renderuje z odhadu, a vnucený posun se rozjede do zpětné
> vazby. Pak to měří **konvergenci smyčky**, ne přesnost odhadu — jiný experiment, který je potřeba
> číst jinak.

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

K pohybu a senzorům:

- **`SimulatedRobot`** — jízda rovně posune polohu o `v·t`; otáčení na místě mění kurz o `omega·t`
  a **doleva** pro kladné `difSpeed`; rampa zrychlení omezí přírůstek rychlosti.
- **Motory jsou inverzí odometrie** — `Drive(v, difSpeed)` → `MotorState` → mapper → zpět `v`
  a `2·difSpeed/rozchod`.
- **GPS round-trip** — pravé (x, y) → `GPSState` → mapper se stejnou `GeoReference` → zpět (x, y)
  v mezích šumu. Chytá past se stupni.
- **IMU round-trip** — pravý kurz → `IMUState` → mapper → `HeadingMeasurement` se stejným yaw
  (ošetřené přetečení přes ±π).
- **Uzavřená smyčka (hlavní)** — simulátor + všechny tři senzory + **skutečný `AsyncFusionEngine`
  a `DefaultMeasurementMapper`** → jízda rovně i v oblouku → odhad fúze sleduje ground truth
  v dané toleranci. Tenhle test říká, jestli to celé funguje; ostatní jen lokalizují chybu.
- **`SetVirtualHW`** — po zavolání jsou `hw.Motor`, `hw.GPS` i `hw.IMU` virtuální a `Sensors`
  je obsahuje.

## Stav ověření

| Co | Jak ověřeno |
|---|---|
| `RoadScene`, `SyntheticFrameRenderer` | `ARBot.Common.Tests` na `x64` (13 testů vč. round-tripu a klasifikace) |
| `SimulatedRobot` | `ARBot.Common.Tests` (3 testy: přímá jízda, otáčení, rampa) |
| `VirtualCamera` | `ARBot.HAL.Tests` (3 testy, bez HW) |
| `VirtualMotors` | `ARBot.HAL.Tests` (2 testy vč. round-tripu přes mapper) |
| `VirtualGps`, `VirtualImu` | `ARBot.HAL.Tests` (3 testy, round-trip přes mapper) |
| **Uzavřená smyčka přes skutečnou fúzi** | `ARBot.HAL.Tests` (1 test; chyba polohy ~0,2 m, kurzu ~0,01 rad po jízdě rovně i v oblouku) |
| Drátování v `ARBotHW` / `ARBotRuntime` | **jen překlad** (`x64` i `OrangePI`) |
| Běh aplikace se simulovaným HW | **neověřeno** |

## Otevřené / budoucí

- **Drsnost trávy je per pixel, ne per místo v terénu** — výška se rozhazuje podle pixelu
  a snímku, takže při pohybu robota „bliká" místo aby byla svázaná se zemí. Pro rozptyl výšky
  v buňce polárního gridu (kvůli čemuž tam je) to stačí; pro časovou konzistenci mezi snímky ne.
  Oprava: hashovat podle kvantované světové polohy zásahu a jednou zpřesnit průsečík.
- **Chyba lokalizace není v obraze vidět** — plyne z rozhodnutí brát pózu kamery z fúze
  (viz „Póza kamery zůstává z fúze"). Přepnutí na ground truth je jednořádkové, až bude potřeba.
- **Prokluz a dynamika podvozku** — model je ideální, takže odometrie proti skutečnosti nedriftuje
  a fúze opravuje jen šum GPS/IMU.
- **Obloha jako samostatná barva** (dnes zelená jako tráva).
- **Objekty mimo vozovku** (překážky, zdi) — dnes scéna zná jen vozovku a trávu.

## Rampa je v (dopředná, rozdíl), ne po kolech

**Nalezeno 18. 8. 2026** rozborem záznamu `20260818-093903.rec`: robot dostal požadavek na zatáčku
+30 °/s, ale otáčel se jen +5,8 °/s — a přitom fúze, odometrie i směrnice `theta` spolu souhlasily
do desetiny. Kola prostě příkaz nevykonala.

**Příčina:** `SimulatedRobot.Step` rampoval **každé kolo zvlášť**. Dokud je aspoň jedno kolo pod
limitem zrychlení, rozdíl rychlostí (a tím `ω`) se dorovná; **jakmile jsou saturovaná obě, rozdíl se
zmrazí** — obě kola se mění stejným krokem. V tom záznamu řídicí smyčka požádala současně o skok
rychlosti 1,20 → 0,17 m/s a o zatáčku, obě kola šla na dorazovou deceleraci (0,5 m/s²) a rozdíl
zůstal dvě sekundy zmražený. Nešlo o asymetrii vlevo/vpravo: při zatáčce doprava byl rozdíl ustavený
**dřív**, než kola do limitu narazila.

**Skutečný řadič to tak nedělá** — a to rozhodlo. `Src/RoboRun/RizeniDiffPodvozku.mbs` (tentýž
skript je v komentáři u [`SDC2160Ex`](../Src/ARBot.HAL/Devices/MotorDriver/SDC2160Ex.cs)) rampuje
**zvlášť dopřednou a zvlášť rotační složku**, každou svou akcelerací (`var 1` / `var 2`; náš driver
posílá do obou tutéž hodnotu), a saturaci řeší tak, že **ustoupí dopředná rychlost**:

```basic
'pri otaceni omezim doprednou rychlost, aby nebyla prekrocena maximalni mozna rychlost kazdeho z kol
if curSpeed>1000000-Abs(curRotSpeed) then curSpeed=1000000-Abs(curRotSpeed)
```

Totéž je i v C# variantě [`SDC2160.Drive`](../Src/ARBot.HAL/Devices/MotorDriver/SDC2160.cs)
(„pokud by bylo kolo rychlejsi jak maxPossibleSpeed, tak sniz doprednou rychlost"). **Rotace má
absolutní přednost** — i na cestě nouzového zastavení, kde se `reqRotSpeed` nuluje teprve až robot
stojí. Protože jsou obě rampy nezávislé, náraz do akceleračního limitu v dopředné složce nemůže
rotační rampu vůbec zdržet: skutečný řadič rozdíl kol nikdy nezmrazí.

**Simulace to teď kopíruje.** Stav je `(speedForward, speedDif)`, každá složka má svou rampu, a po
rampě se dopředná složka srazí na `±(maxWheelSpeed − |speedDif|)`. `maxWheelSpeed` chodí z
`VirtualHWOptions.MaxWheelSpeed` (default `Profile.MaxTheoreticalSpeed` — týž zdroj, jaký dostává
driver jako `maxPossibleSpeed`); dřív simulace strop rychlosti kola **neměla vůbec**.

Pokryto testy v `ARBot.Common.Tests/Simulation/SimulatedRobotTests.cs`: regrese na tento nález
(na starém modelu vracela 0,0 rad/s místo 0,428), zrcadlová symetrie a saturace (rotace se drží,
dopředná ustoupí, žádné kolo nepřekročí maximum).

**Pojistka je na hostovi, ne ve skriptu.** Hodnota zrychlení jde do rampy v řídicí jednotce
(`curSpeed += time * acceleration`) a nesmyslná hodnota tam nadělá víc škody než chybějící příkaz:
**záporná** by rampu hnala od cíle až na saturaci, tedy na plnou rychlost opačným směrem;
**nula** rampu zmrazí, takže už jedoucí robot nezastaví ani pod nouzovým zastavením (a protože se
rotace nuluje až při `curSpeed = 0`, jel by dál i v zatáčce). Skript se proti tomu bránit nemůže —
když je rampa mrtvá, nemá čím brzdit. Hlídá to proto
[`MotorAcceleration.ToUnits`](../Src/ARBot.HAL/Devices/MotorDriver/MotorAcceleration.cs), společný
pro oba drivery: bere velikost (zápornou hodnotu nepustí) a nikdy neposílá nulu — i malé zrychlení,
které by se zaokrouhlilo k nule, zvedne na 1 a zapíše to do Debug outputu.

## Přesun robota za běhu (Shift + klik ve World pohledu)

Vývojářská pomůcka: **Shift + klik** do mapy přesune *simulovaného* robota na to místo, aby se dala
zkoušet scénáře bez restartu běhu (Ctrl + klik zůstává cílem plánovače). **Kurz se nemění** — klik
dává jen polohu.

Platí jen v **Run s virtuálním HW**; ve View a s reálným hardwarem
[`ARBotRuntime.TeleportSimulatedRobot`](../Src/ARBot/Robot/ARBotRuntime.cs) vrátí `false` a napíše
důvod do Debug outputu. Pohled o runtime nic neví — jen se zeptá přes `TeleportRequested`.

Podstatné je, že se **nemění jen poloha**. Tři věci na sobě závisí a musí se srovnat naráz:

| Co | Proč |
|---|---|
| `SimulatedRobot.X/Y` (ground truth) | odtud měří virtuální senzory |
| `engine.InitializePosition(x, y, …)` (fúze) | jinak by EKF držel starou polohu a s teleportem se „přetahoval" — je to tatáž cesta, jakou se vkládá startovní póza |
| rozjetá dráha + regulátor | dráha vede odjinud; regulátor se nuluje hned (robot stojí), dráhu zahodí navigátor na svém vlákně přes `RequestPathReset()` |

**Occupancy grid se nečistí.** Integrátor ho na novou pózu vycentruje sám při dalším snímku a nově
vstoupivší pruhy vynuluje; při skoku delším než je grid (12,8 m) se tím vyčistí celý. Po krátkém
skoku tedy část staré mapy zůstane — vědomé rozhodnutí (2026-08-18), protože `Recenter` už dělá to
podstatné a zvláštní mazání by bylo další cesta ke stejnému cíli.

**Trajektorie v mapě** se při skoku pózy delším než 2 m začne kreslit znovu (stopa je záznam
*spojitého* pohybu, čára přes půl mapy by ji jen znečitelnila).
