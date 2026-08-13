# Globální navigace v runtime (`GlobalNavigator`)

Napojení [OSM navigace](osm-nav.md) na řídicí smyčku: vrstva, která dostane **cíl v LLA**, drží
trasu po silniční/pěší síti a **krmí [`LocalNavigator`](occupancy-and-local-planning.md) lokálním
cílem**. Navíc si o každém úseku trasy vede **metadata o postupu**, aby poznala, že robot bloudí,
zasekl se, nebo že mapou uváděná cesta reálně **není průchozí** — a v takovém případě hranu v grafu
uzavře a přeplánuje.

> **Stav (2026-08-13): fáze 0–3 hotové, fáze 4–6 neimplementované.** Robot jede k cíli po síti
> a trasa je vidět v mapě; detekce záseku/bloudění/přehrazení (fáze 4) a ověření na HW (fáze 6)
> zbývají. Podrobně v [Plánu realizace](#plán-realizace-fáze).

Nad touto vrstvou stojí ještě mise soutěže — [robotour-mission.md](robotour-mission.md).

## Místo ve stohu vrstev

```
MissionController (Robotour)      cíl = LLA, čeká na dojezd            robotour-mission.md
        │  SetGoal(LLA) / Cancel()          ▲ GlobalNavStatus
        ▼                                   │
GlobalNavigator (tento dokument)  trasa po síti + hlídání postupu
        │  SetGoal(x, y [, corridor])  ← lokální cíl v ENU (metry), "mrkev" na trase
        ▼
LocalNavigator (occupancy grid)   A* nad occupancy gridem → RegulatorWayPoint[]
        │  ControlLoop.Regulator
        ▼
ControlLoop                       tik, feedforward + lookahead, motory
```

**Každá vrstva mluví jen s tou pod sebou a předává jí jediný cíl.** Globální vrstva nezná occupancy
grid ani regulátory; lokální nezná OSM. Jediné pouto je bod cíle (+ volitelná šířka koridoru) a
zpětná vazba stavem plánování (`LocalPlanMsg`).

## Prerekvizita (fáze 0): GPS a odometrie do EKF

Bez toho tato vrstva **nemůže existovat** a je to dnes reálně nedodělané:

- [`DefaultMeasurementMapper`](../Src/ARBot.Common/Runtime/DefaultMeasurementMapper.cs) mapuje jen
  `IMUState` → kurz + úhlová rychlost. **`GPSState` ani stav motorů se do fúze nepřevádí vůbec**
  (v komentáři je to přiznané: „GPS / odometrie / kamera se doplní později"), takže stav EKF `[X, Y, θ, v, ω]`
  nemá **žádné měření polohy ani rychlosti** — X, Y jsou fakticky nesmyslné.
- [`FusionConfig.GeoReference`](../Src/ARBot.Common/Fusion/FusionConfig.cs) existuje, ale **nikdo ji
  nenastavuje ani nečte**. `GeoReference` je přitom jediný korektní most LLA ↔ lokální ENU.
- World pohled si dnes `GeoReference` sestavuje **ad hoc** z posledního `GPSState` a posledního
  `RobotStateMsg` (`WorldViewDocument.BuildGeoReference` posune počátek o `−(X, Y)`). To je vizualizační
  provizorium — pro navigaci je nepoužitelné, protože počátek roviny se s každým fixem mění.

Co je potřeba doplnit (detail patří do [ekf-fusion.md](ekf-fusion.md), tady jen rozsah):

1. **`GPSState` → `PositionMeasurement`** (třída už existuje v `Measurements.cs`) s `R` z kvality fixu.
   Volitelně GPS kurz/rychlost (nad `GpsMinSpeed`).
2. **Stav motorů → `Velocity` / `AngularRate`** (odometrie; `SlipDetector` na to už čeká).
3. **`GeoReference` musí být dostupná runtime vrstvám** — dnes je zavřená v `FusionConfig`, který
   `AsyncFusionEngine` drží přes `model.Config`. Vystaví se read-only na `AsyncFusionEngine`, aby
   `GlobalNavigator`, occupancy grid i UI používaly **tutéž** rovinu. World pohled pak své
   provizorium zahodí.

### Počátek ENU roviny bere z OSM mapy, ne z prvního fixu

Počátek lokální ENU roviny je **bod z načtené OSM mapy** — konkrétně **střed bounding boxu mapy**
(deterministický, nezávislý na pořadí uzlů, blízko oblasti pohybu). **Zakládá se v rámci načtení
mapy**: `ARBotRuntime` po sestavení sítě vyplní `FusionConfig.GeoReference`, takže první
`PositionMeasurement` už ji má hotovou a nikdo nemusí řešit „co když ještě není". Fallback zůstává ten,
který `FusionConfig` už dnes popisuje: **není-li mapa načtená, založí ji GPS adaptér z prvního platného
fixu**. Je to výrazně lepší než „vždycky z prvního fixu":

- **je znám před prvním fixem** — grid, trasa i navigace mají jednu rovinu od okamžiku startu, ne až
  od chvíle, kdy chytne GPS;
- **je stejný napříč běhy i napříč záznamy** — dva běhy nad stejnou mapou mají srovnatelné souřadnice
  (dnes by se mezi běhy lišily o desítky metrů podle toho, kde robot zapnul);
- odpadá tím celá otázka „co když se počátek posune" — počátek je vlastnost **mapy**, kterou nikdo
  za běhu nemění.

Počátek se posílá v `MapMsg`, takže UI i případný offline nástroj používají tentýž.

#### Důsledek, který se musí ošetřit: první fix filtr netrefí

Počátek uprostřed mapy znamená, že robot startuje **stovky metrů od `[0, 0]`** — a to dnešní filtr
neustojí. Stav začíná na nule a **počáteční kovariance je `P0 = DenseIdentity(5)`**
([`EKFModel`](../Src/ARBot.Common/Fusion/EKFModel.cs)), tedy σ = **1 m** pro polohu: filtr si o své
(nulové) poloze myslí, že ji zná na metr. Selže to **dvakrát, každou z jiné strany**:

- **Dnes** (prahy gatingu nikdo nenastavuje — `IMeasurement.GateThreshold` je `null`, takže se
  [gating](../Src/ARBot.Common/Fusion/Ekf.cs) vůbec neuplatní) se vzdálený fix **přijme**, ale
  Kalmanův zesílení je `K = P/(P+R) = 1/(1 + 1,5²) ≈ 0,31` — stav se k pravdě **plazí** několik sekund
  a mezitím se do occupancy gridu zapisují pózy stovky metrů mimo. *(Doloženo testem
  `FarAwayFix_WithoutInit_OnlyCreeps_WithInit_IsExact`.)*
- **Jakmile se prahy zapnou** (na to `Gating.ChiSquareThreshold` je a je to plánované), má takový fix
  `NIS ≈ 300²/3,25 ≈ 2,7·10⁴` proti χ² prahu (2 DOF, 0,95 → ≈ 6,0), takže ho `GateMode.Reject`
  **zahodí** a filtr by robota **nikdy nenašel**. *(Doloženo testem
  `FarAwayFix_WithGating_WouldBeRejected`.)*

Inicializace to řeší v obou světech — proto nestačí „počkat, ono se to dofúzuje".

Řešení: **explicitní inicializace polohy jako funkce fúze**, ne magie schovaná v cestě měření
(✅ hotovo, [`AsyncFusionEngine`](../Src/ARBot.Common/Fusion/AsyncFusionEngine.cs)):

```csharp
bool IsPositionInitialized { get; }
void InitializePosition(double x, double y, double std, DateTime t);   // nastaví X,Y a blok P = std²
```

Kromě polohy a její kovariance **vynuluje i korelace polohy se zbytkem stavu** (poloha je teď známá
nezávisle na kurzu i rychlostech) a **zahodí měření starší než `t`** (poloha před inicializací nemá
význam), zatímco novější zůstanou a přepočítají se z nového základu.

Je to lepší než „první měření polohy se chová jinak než ostatní": rozhodnutí *„tomuhle fixu už věřím
tak, že podle něj postavím počátek"* je **rozhodnutí volajícího**, ne vlastnost filtru. Volající je
`MissionController`, který na začátku mise stejně čeká na kvalitní fix a průměruje ho
(viz [robotour-mission.md → ArmingAtDepot](robotour-mission.md#armingatdepot-kvalitní-fix-a-inicializace-fúze)).
Filtr jen dostane hotovou polohu a k ní poctivou nejistotu.

**Pro běh bez mise** (ruční jízda, ladění, cíl klikem v mapě) zapojí `ARBotRuntime` jednoduchý
fallback: první fix, který projde minimální kvalitativní podmínkou, zavolá `InitializePosition` sám.
Je záměrně hloupější než misní varianta (neprůměruje, nepamatuje si depo) — jen aby robot věděl, kde je.

Bez tohohle by occupancy grid navíc nejdřív vycentroval na střed mapy a při prvním fixu skočil.

*Pozn.: tenhle problém je latentní už dnes — jen se neprojeví, protože GPS do filtru nevstupuje vůbec.*

Dokud fáze 0 není hotová, `GlobalNavigator` se dá vyvíjet a testovat jen nad syntetickými pózami
(což je ale plnohodnotné — vrstva je čistě algoritmická, viz [Testy](#testy)).

## Vlastnictví sítě a životní cyklus pole

- **`RoadNetwork` je property `ARBotRuntime`**, ne lokální proměnná v UI. Dnes se `.osm` parsuje ve
  [`WorldViewDocument.LoadOsmMapAsync`](../Src/ARBot/ViewModels/WorldViewDocument.cs), síť se použije
  na `MapMsg` a **zahodí se**. Nově:

  ```csharp
  public RoadNetwork RoadNetwork { get; private set; }   // ARBotRuntime, immutable po sestavení
  public GlobalNavigator GlobalNavigator { get; private set; }
  ```

  Runtime síť sestaví, emituje z ní `MapMsg` na `Stream` a UI kreslí **přesně tu síť, po které se
  naviguje** — pohled a navigace se nemohou rozejít. Načtení má dva vstupy, oba končí v téže property:
  - **parametr příkazové řádky `osm=<cesta>`** — hlavní cesta pro soutěž: robot musí nastartovat
    s mapou bez jakéhokoli klikání v UI (stejný vzor jako ostatní parametry, `Program.GetParam`);
  - **načtení z UI** (výběr souboru ve world pohledu) — UI o stavbu **požádá runtime** a jen odebírá
    výsledný `MapMsg`; sama si síť nestaví.

  Načtení mapy za běhu znamená **novou síť i nové pole** — proto se seznam uzavřených hran drží
  odděleně podle `(WayId, From.Id, To.Id)` a po přestavbě se znovu aplikuje (viz níže).
- **Jeden `GoalField` na celou misi.** Cíl se mění **výhradně** přes `GlobalNavigator.SetGoal(LLA)`;
  ten uvnitř udělá `field.ClearGoal(); field.InsertGoal(lla)` — to je jen interní přepnutí cíle pole,
  **nikoli zrušení navigace**. Overlay značek (a tím i naše uzavřené hrany) to **přežije**.

  Proto je i **návrat do depa normální cíl** (`SetGoal(depotLLA)`), ne „zrušení cíle": depo je místo
  jako každé jiné, jede se k němu po síti a uzavřené hrany zůstávají v platnosti — robot při návratu
  znovu nezajede do slepé uličky, kterou už jednou odhalil. `Cancel()` je vyhrazený **jen** pro
  „přestaň jezdit" (nouzové zastavení, nakládka, přerušení mise), nikdy pro změnu cíle.
- **Dopravní profil:** pro soutěž pěší/parkový (`TravelProfile.Pedestrian()`), tj. `footway`/`path`/
  `track` povolené, `oneway` se ignoruje. Profil je parametr — pro jiné nasazení `Bicycle()`/`Car()`.
- **Stavba sítě je jednorázová a může trvat.** Děje se při Startu mimo řídicí cestu; **dobu stavby na
  OrangePI je nutné změřit** (mapa parku je malá, ale `Bratislava.osm` v repu není).

## Cyklus `GlobalNavigator`

`MessageProcessor` na vlastním vlákně (stejný vzor jako `LocalNavigator` — řídicí tik zůstává
deterministický). Odebírá **`RobotStateMsg`** z `ControlLoop.Output` (hodinky cyklu, 10 Hz) a
**`LocalPlanMsg`** z `LocalNavigator.Output` (zpětná vazba o tom, jak se lokální vrstvě vede).
Nepřipojuje se na celý `Stream` — tam tečou `CameraFrame` s ~1 MB obrazů, které tu nikoho nezajímají.

Vlastní práce jen každých `ReplanPeriod` (default 200 ms); mezi tím se póza jen zapamatuje:

1. **Póza → LLA** přes `GeoReference` (`ToLLA(state.X, state.Y)`).
2. **`Navigator.Update(lla)`** → `NavigationFix` (namapovaná hrana, cílový uzel, off-route vzdálenost,
   `Arrived`, `NoRoute`). Tenký sledovač gradientu se nemění — celá „inteligence" je v `GoalField`.
3. **Potenciál postupu φ** (viz níže) a zápis do `RouteProgress` pro aktuální hranu.
4. **Detektory záseku** A/B/C → případná reakce do overlaye (`SignApplier`).
5. **Mrkev (carrot)**: poslední bod trasy uvnitř lokální mapy → `ILocalGoalSink.SetGoal(x, y, corridor)`.
6. **Zprávy:** `GlobalNavMsg` každý cyklus, `GraphNavigationMsg` (geometrie trasy) při změně trasy
   nebo jednou za `RouteMessagePeriod`.

### Předání dolů = „mrkev" na trase, **na okraji lokální mapy**

Zásadní rozhodnutí. `LocalPathPlanner` cíl mimo grid **promítne na hranici gridu po přímce** ke cíli —
u vzdáleného cíle by ta přímka mířila přes domy a zahrady a robot by se pořád tlačil do překážky místo
aby sledoval cestu. Proto globální vrstva předává **bod ležící na trase**. A leží **co nejdál, dokud
je ještě v lokální mapě**:

> **Mrkev = poslední bod trasy, který je ještě uvnitř gridu** (zmenšeného o `CarrotMarginM`), počítáno
> postupem po lomené čáře trasy od průmětu robota **k prvnímu výstupu z gridu**.

**Proč až na okraj a ne „pár metrů dopředu":** blízká mrkev dělá z lokálního plánovače krátkozraké
zvíře. V **bludišti** (a park se živým plotem, zdmi a slepými odbočkami se tak chová) by robot
naplánoval cestu k bodu 5 m daleko, vjel do chodby, která o 3 m dál končí, a teprve tam by zjistil, že
se musí vracet — přesto, že to occupancy grid **v tu chvíli už věděl**. Mrkev na okraji mapy nutí A\*
prohledat **celou známou mapu**, takže slepá odbočka se odmítne dřív, než do ní robot vjede. Mapu, kterou
si robot zaplatil integrací snímků, má smysl využít naplno.

Detaily pravidla:

- **První výstup z gridu, ne poslední.** Kdyby se trasa z gridu vynořila a zase se do něj vrátila
  (zatáčka mimo dohled), byl by pozdější kus trasy uvnitř gridu **nespojený** s robotem a cíl na něm
  by lokální plánovač nedokázal poctivě obsloužit. Proto se postup po trase zastaví na prvním výstupu.
- **`CarrotMarginM`** (default 0,5 m) drží cíl mimo krajní pruh gridu — tam se buňky teprve „vsouvají"
  při přecentrování a EDT je u kraje ořezaná.
- **Cíl uvnitř gridu ⇒ mrkev = skutečný cíl.** Žádný zvláštní „finální dojezd" tím není potřeba:
  jakmile je cíl v mapě, jede se přímo na něj, poslední waypoint dostane `Speed = 0` (to
  `LocalPathPlanner` už umí — „skutečná nula je jen na skutečném cíli") a robot na cíli zastaví.
  Parametr `FinalApproachM` z předchozí verze návrhu tím **zaniká**.
- Rychlostní obálku, odstupy a objezdy překážek řeší **výhradně** lokální vrstva. Globální vrstva
  nikdy nepočítá rychlost.

**Dojezd (`Arrived`)** je přesně to, co už `Navigator` počítá: vzdálenost **pózy z EKF** (přes
`GeoReference` na `LLA`) od cíle pole ≤ `NavigatorOptions.ArrivalRadiusMeters`. Nic víc se nepřidává —
žádná podmínka „a musí stát", žádné ruční potvrzení:

- **stanoviště je větší než chyba dojezdu.** Místo nakládky/vykládky je plocha o metrech, zatímco
  robot dojede s chybou EKF/GPS řádu metru. Tolerance dojezdu tedy nemá být „co nejmenší", ale
  „menší než stanoviště" — default **3 m** (z 12 m).
- **zastavení si zařídí odběratel.** `Arrived` je hlášení, ne manévr: mise na něj reaguje zrušením
  cíle, což robota řízeně dobrzdí (viz mise). Čekat na `|v| ≈ 0` jako součást podmínky dojezdu by
  jen posouvalo tutéž informaci o dvě sekundy později.

*Otevřená hranice:* pokud by se ukázalo, že stanoviště je menší, než EKF trefí, není řešením utahovat
toleranci, ale **dojet vizuálně** (poloha a velikost QR kódu v obraze dá směr i vzdálenost) — viz
[Otevřené úkoly](#otevřené-úkoly).

#### Důsledek: `LocalPlannerConfig.HorizonM` je potřeba zvednout

`HorizonM` (dnes **6,0 m**) není radius, ale **maximální délka plánované dráhy**
([`LocalPathPlanner`](../Src/ARBot.Common/Occupancy/LocalPathPlanner.cs) přeruší expanzi, jakmile
`lenFromStart >= HorizonM`). Mrkev na okraji gridu je až 5,9 m vzdušně daleko — ale **cesta k ní přes
bludiště může mít 20 i 30 m**. S dnešním horizontem by se plán utnul v polovině a mrkev by byl
nedosažitelný pokaždé, když cesta není skoro přímá.

Proto `HorizonM` = **25 m** (bezpečně nad nejdelší rozumnou dráhou uvnitř 12,8 m gridu). Cena je jen
výpočetní: A\* smí v nejhorším expandovat celý grid (65 k buněk), což je v C# jednotky ms — a hlavně
se to stane **jen** v opravdovém bludišti, protože A\* s cenou = jízdní čas jde stejně nejdřív
rovně za nosem. **Změřit na OrangePI** (patří do fáze 6 spolu se zbytkem řetězu).

### Mimo trasu

`Navigator` off-route neřeší explicitně (jiná poloha jen přečte pole jinde) — a to je správné, dokud
je robot blízko sítě. Když `NavigationFix.OffRouteDist > OffRouteMaxM` (default 15 m), přestává mít
mrkev na hraně smysl (mezi robotem a sítí může být cokoli): mrkev = **nejbližší bod trasy**, stav
`OffRoute`, a je to hlášená (nikoli tichá) situace. Vyšší vrstva se může rozhodnout misi přerušit.

Napětí, které tu zůstává vědomě nevyřešené: **když je špatná lokalizace, je špatná i mrkev** a robot
sjede z cesty, protože grid mu to dovolí (tráva je geometricky sjízdná). Protijedem je semantický
kanál `LRoad` v occupancy gridu; systémové řešení je korelace okrajů cesty s šířkami z OSM
(`Node.Width`) — otevřený úkol už v [occupancy-and-local-planning.md](occupancy-and-local-planning.md).

## Metadata o postupu a detekce záseku

### Jeden potenciál místo hromady heuristik

`GoalField` dává **cost-to-goal v sekundách** na hranu. Ze stejných dílů, ze kterých si `Navigator`
vybírá směr, se dá složit **spojitý potenciál**:

```
φ = (1 − t) · BaseTraversalCost(chosen) + CostToGoal(chosen)      [s]
```

kde `t` je parametr průmětu robota na hranu. φ je **skalár, který při postupu k cíli monotónně klesá**,
a to i přes křižovatky (žádné skoky) — a co je důležité, klesá i tehdy, když robot **objíždí** překážku
jinou cestou, protože pole je goal-rooted a nezávisí na tom, odkud jsme přijeli. Proti prostému
„vzdušná vzdálenost k cíli" (která při objíždění roste) je to poctivá míra postupu.

### `RouteProgress` — co si vedeme k úseku

Slovník **per hrana** (klíč pro trvalou identitu je `(WayId, From.Id, To.Id)`, nikoli `Edge.Index` —
ten platí jen pro jednu instanci `RoadNetwork`):

| položka | k čemu |
|---|---|
| `FirstEnteredAt`, `LastSeenAt`, `TimeOnEdge` | jak dlouho se na hraně zdržujeme |
| `EnterCount` | **kolikrát jsme na hranu vjeli** — opakované vjezdy = jezdíme v kruhu |
| `TravelledM` | ujetá dráha (z odometru) po dobu, kdy jsme byli na této hraně |
| `MaxT`, `CurrentT` | nejdál dosažený průmět — „ujel jsem 20 m, ale `t` se posunulo o 0,05" je bloudění |
| `PhiAtEntry`, `PhiBest` | potenciál při vjezdu a nejlepší dosažený |
| `PlanFailures` | počet `NoRoute` / `RobotBlocked` / `AbortedCollision` z `LocalPlanMsg` na této hraně |
| `StoppedSec` | doba, kdy robot stál, ačkoli měl jet |
| `Closure` | zda a kdy byla hrana uzavřena/penalizována, kolikrát a proč |

K tomu **klouzavé okno** `(odometr, φ)` (kruhový buffer posledních ~30 m jízdy) — nezávislé na hranách,
protože bloudění se pozná právě přes hranice hran.

### Tři detektory, tři různé významy

Záměrně **oddělené**, protože každý znamená něco jiného a chce jinou reakci:

**A — nehýbu se.** Za `NoMotionSec` (default 10 s) se odometr posunul o méně než `MinMotionM` (0,5 m),
přičemž je aktivní cíl a lokální vrstva hlásí platný plán (tedy robot *měl* jet). Interpretace:
mechanický zásek, kolo v příkopu, watchdog cyklí, motor nereaguje.

Detektor je **vypnutý**, když robot legitimně stojí, a to ze dvou nezávislých důvodů:
- **není aktivní cíl** (mise čeká na nakládku) — stání je pak správné chování;
- **`IMotorState.IsEmergencyStop == true`** — pod nouzovým zastavením robot stojí, i když má cíl
  i platný plán, protože `ControlLoop` mu nuluje rychlost (viz
  [robotour-mission.md → Nouzové zastavení](robotour-mission.md#nouzové-zastavení-řeší-controlloop-ne-stavový-automat)).
  Bez téhle podmínky by každé zmáčknutí stopu za jízdy po 10 s vyrobilo **falešný zásek** a v horším
  případě by robot začal zavírat hrany v mapě kvůli tomu, že u něj někdo stál.

Ostatní dva detektory se gatovat nemusí: **B** běží proti *ujeté dráze* (robot nejede ⇒ okno se
neposouvá ⇒ nic se nevyhodnotí) a **C** potřebuje selhání lokálního plánování, která pod stopem
nenastanou.

**B — nepostupuju k cíli (bloudím).** Za posledních `ProgressWindowM` (20 m) ujeté dráhy neklesl φ
alespoň o `ProgressGain · ProgressWindowM / v_profile` (default `ProgressGain = 0,3`, tj. „stačí, když
jsme se k cíli přiblížili aspoň třetinou toho, co jsme ujeli"). Interpretace: objíždění dokola,
oscilace mezi dvěma variantami, chybná lokalizace, nebo cesta, která nikam nevede.

**C — cesta je přehrazená (mapa lže).** Robot fakticky stojí (jako A, ale s krátkým prahem) **a**
posledních `BlockedPlanCount` (default 20 ≈ 2 s) výsledků lokálního plánování hlásí `NoRoute` /
`RobotBlocked`, nebo `Partial`, u něhož se vzdálenost `ReachedGoal → RequestedGoal` přestala zmenšovat.
Interpretace: **napříč celou šířkou cesty je překážka**, kterou mapa nezná — přehrazený vjezd, závora,
plot, spadlý strom.

*Zpřesnění (fáze 4b): průřez napříč cestou.* Nejsilnější důkaz přehrazení je „všechny buňky na
kolmici k cestě v šířce `Node.Width + margin` jsou `Blocked`". Ten test **musí proběhnout na vlákně
`LocalNavigatoru`**, protože grid vlastní ono (grid se přecentrovává; čtení z cizího vlákna by dalo
nekonzistentní geometrii). Proto má handoff volitelný parametr **`corridorWidthM`**: když ho globální
vrstva pošle a plán nevznikne, `LocalNavigator` průřez otestuje a výsledek přidá do `LocalPlanResult`/
`LocalPlanMsg` jako `CorridorBlocked`. Globální vrstva pak nemusí nic hádat.

### Eskalace reakcí

| detektor | reakce (v tomto pořadí) |
|---|---|
| **A** | (1) čekat `EscalateSec` (default 5 s) — lokální vrstva možná právě dobrzďuje; (2) `Recovery` — couvnutí/otočka na místě; **dnes taková recovery neexistuje** (otevřený úkol); (3) po `MaxRecoveries` se s hranou zachází jako u C |
| **B** | **soft penalizace** hrany: `SetTraversalCost(e, base · PenaltyFactor)` (default 5×) — hrana se nezakáže, jen zdraží, takže robot zkusí jinudy a sem se vrátí jen když nic jiného není. Falešný poplach tak nezničí trasu. Při opakování na téže hraně → C |
| **C** | **`SignApplier.CloseRoad(e)` a zároveň `CloseRoad(FindReverse(e))`** — fyzická zábrana blokuje oba směry. LPA\* přepočítá jen dotčenou část pole, trasa se objeví objezdem automaticky. Když se tím `CostToGoal` stane ∞ → stav `NoRoute` a rozhodnutí předá výš |

**Zapomínání uzavření.** Uzavření dostane `ClosureTtl` (default 300 s); po jeho vypršení se hrana
nevrací do plné ceny, ale na **soft penalizaci** — kdo ví, jestli tam ta překážka pořád je, ale
preferovat ji nebudeme. Pokud se přehrazení potvrdí znovu (`ClosureCount > MaxClosures`, default 2),
uzavření je **trvalé pro celou misi**.

**Autoritativní seznam uzavření drží `GlobalNavigator`** (`(WayId, From, To)`, čas, důvod, počet), ne
jen overlay v poli: dá se tak znovu aplikovat po případné přestavbě sítě, poslat do zprávy a zobrazit
v UI (a ručně zrušit).

## Stav a zprávy

`GlobalNavStatus`: `NoGoal`, `Building` (staví se pole), `Driving`, `GoalInMap` (cíl už je v lokální
mapě, mrkev = cíl), `Arrived`, `OffRoute`, `NoRoute`, `StuckNoMotion`, `StuckNoProgress`, `RoadBlocked`.

- **`MapMsg`** — už existuje; nově ji emituje **runtime** (jednorázově po sestavení sítě), takže UI
  kreslí navigovanou síť. Vrstva „Mapa" ve [world pohledu](world-view.md) se nemění.
- **`GraphNavigationMsg`** — už existuje **a už je ve world pohledu vykreslená** jako vrstva
  „trasa/graf" (vrcholy v lokálním ENU, `HightLight` = zvýrazněná cesta, `Path` = trasa,
  `Collision` = ⇒ použijeme pro **uzavřené hrany**, `Start`/`Target`/`Result` = robot/cíl/mrkev).
  Znovupoužít ji je jednoznačně lepší než zavádět novou geometrickou zprávu. Emituje se při **změně
  trasy** nebo jednou za `RouteMessagePeriod` (default 2 s) — je to největší z těchto zpráv.
- **`GlobalNavMsg`** (nová, malá, každý cyklus) — cíl (LLA), poloha (LLA), klíč namapované hrany,
  φ [s], zbývající vzdálenost [m], off-route vzdálenost, mrkev (ENU), `GlobalNavStatus`, stavy
  detektorů (A/B/C + jejich čítače) a počet uzavření. Do záznamu → ve View je zpětně vidět **celý
  příběh globální navigace**, včetně toho, proč se která hrana zavřela.
- Konverzi vlastní doména: `RouteProgress.ToLogMessage()` / stavový objekt → `GlobalNavMsg`
  (nikoli `GlobalNavMsg.FromDomain`, viz [CLAUDE.md](../CLAUDE.md)).

## Rozhraní a testovatelnost

Aby šla vrstva testovat bez occupancy gridu a bez HW (a aby `OsmNav` nezávisel na `Occupancy`):

```csharp
/// Příjemce lokálního cíle (implementuje LocalNavigator).
public interface ILocalGoalSink
{
    void SetGoal(double worldX, double worldY, double corridorWidthM = 0);
    void ClearGoal();
}
```

`LocalNavigator` ty metody **už má** — jde jen o extrakci rozhraní (+ volitelný `corridorWidthM`).
Stejným způsobem `GlobalNavigator` vystaví `IGlobalGoalSink { void SetGoal(LLA); void Cancel(); }`
pro vrstvu mise. Póza chodí zprávou, takže test si ji zadá sám; `GeoReference` je vstupem konstruktoru.

## Zapojení v `ARBotRuntime.WireRun`

```
loop.Output ────────────────▶ LocalNavigator ─────▶ stream
loop.Output (RobotStateMsg) ─┐
LocalNavigator.Output ───────┴▶ GlobalNavigator ──▶ stream
                                    │ ILocalGoalSink.SetGoal(ENU)
                                    ▼ LocalNavigator
MissionController ──IGlobalGoalSink.SetGoal(LLA)──▶ GlobalNavigator
```

Fronta `DropOldest`, kapacita ~16 (chodí dva druhy zpráv po 10 Hz; práce je řádově desítky µs).
`ARBotRuntime.GlobalNavigator` se vystaví UI (zadání cíle, ruční zrušení uzavření). **Ctrl+klik v
mapě** dnes zadává cíl přímo lokální vrstvě; po zapojení půjde do globální vrstvy jako LLA cíl
(lokální cíl klikem zůstane jako ladicí režim, přepínačem).

## Parametry

| parametr | default | pozn. |
|---|---|---|
| `ReplanPeriod` | 200 ms | jak často se počítá globální cyklus |
| `CarrotMarginM` | 0,5 m | o kolik se grid zmenší, než se hledá výstup trasy |
| `HorizonM` | **25 m** (z 6,0) | `LocalPlannerConfig` — **délka** dráhy, viz [výše](#důsledek-localplannerconfighorizonm-je-potřeba-zvednout) |
| `ArrivalRadiusMeters` | **3,0 m** (z 12,0) | `NavigatorOptions`; „menší než stanoviště", ne „co nejmenší" |
| `OffRouteMaxM` | 15,0 m | nad tím mrkev = nejbližší bod trasy |
| `NoMotionSec` / `MinMotionM` | 10 s / 0,5 m | detektor A |
| `ProgressWindowM` / `ProgressGain` | 20 m / 0,3 | detektor B |
| `BlockedPlanCount` | 20 (≈2 s) | detektor C |
| `PenaltyFactor` | 5× | soft penalizace hrany |
| `ClosureTtl` / `MaxClosures` | 300 s / 2 | zapomínání uzavření |
| `EscalateSec` / `MaxRecoveries` | 5 s / 2 | eskalace u detektoru A |
| `RouteMessagePeriod` | 2 s | perioda `GraphNavigationMsg` |

Vše v `GlobalNavigatorConfig` (žádné konstanty v kódu).

## Testy

Vrstva je čistě algoritmická → testovatelná celá, bez HW i bez fúze (`ARBot.Common.Tests/OsmNav.Tests/`):

- syntetická síť (malé `.osm` v testovacích datech) + posloupnost póz → očekávané mrkve na trase,
  správná volba směru, dojezd (radius **i** podmínka zastavení);
- **mrkev na okraji mapy:** trasa vedoucí z gridu → mrkev je poslední bod uvnitř (po odečtení
  `CarrotMarginM`); trasa, která grid opustí a **vrátí se** → mrkev je na **prvním** výstupu, ne na
  pozdějším kusu; cíl uvnitř gridu → mrkev = přesně cíl;
- **přehrazení:** krmit `LocalPlanMsg` se `NoRoute` → očekávat `CloseRoad(e)` **i reverzní hrany** a
  novou trasu objezdem (asertovat konkrétní hrany trasy);
- **uzavření přežije změnu cíle** — po `SetGoal(depo)` (tj. interně `ClearGoal` + `InsertGoal`) trasa
  znovu nevede přes uzavřenou hranu;
- **znovuaplikování uzavření po přestavbě sítě** (načtení mapy za běhu) podle `(WayId, From, To)`;
- **bloudění:** póza se hýbe, φ neklesá → soft penalizace, ne uzavření;
- **zásek:** póza stojí při aktivním cíli → `StuckNoMotion` a eskalace; při neaktivním cíli **nic**;
- **mimo trasu:** póza 30 m od sítě → stav `OffRoute`, mrkev = nejbližší bod trasy;
- `ClosureTtl` → hrana se otevře na soft penalizaci, po druhém potvrzení je trvale zavřená;
- roundtrip `GlobalNavMsg` (serializace) a `RouteProgress` → zpráva;
- A/B nad reálným `.rec` ze soutěžní trasy, až bude.

## Plán realizace (fáze)

0. 🟡 **Prerekvizita — GPS + odometrie do EKF.** ✅ `InitializePosition` + `IsPositionInitialized`
   + `GeoReference` vystavená z `AsyncFusionEngine`; ✅ `GPSState` → `PositionMeasurement` (+ rychlost
   nad prahem) a odometrie → `v`/`ω` v `DefaultMeasurementMapper`; ✅ `GeoReference` **ze středu obalky
   uzlů OSM mapy** zakládaná při načtení mapy (`map=`, nezávisle na `virtualhw`), ✅ fallback
   auto-inicializace z prvního použitelného fixu pro běh bez mise; ✅ **world pohled používá tutéž
   referenci** (`ARBotRuntime.MapOrigin`) — jeho původní ad hoc počátek z posledního fixu se posouval
   s **každým** fixem, takže trasa, occupancy i plán poskakovaly se šumem GPS; ad hoc varianta zůstala
   jen jako fallback pro běh bez mapy. **Zbývá:** ⬜ **ověření znaménka odometrického `ω` na zařízení**
   (`FusionConfig.OdoOmegaSign`), ⬜ ladění σ a prahů gatingu na reálných datech.
1. ✅ **`RoadNetwork` jako property `ARBotRuntime`**: property + `MapOrigin` + parametr `map=<cesta>`,
   `MapMsg` emitovaná runtimem (world view ji kreslí a dostane ji i pohled otevřený za běhu),
   **`ILocalGoalSink`** (+ `corridorWidthM`) v `ARBot.Common/Runtime` — záměrně mimo `Occupancy`
   i `OsmNav`, aby na sobě ty dvě vrstvy nezávisely.
   *(UI si mapu pořád umí načíst i vlastní cestou přes file picker — ta zůstává.)*
2. ✅ **`GlobalNavigator` skeleton**: póza → LLA → `Navigator.Update` → **mrkev na okraji gridu** →
   `SetGoal`; `HorizonM` na 25 m; `ArrivalRadiusMeters` na 3 m; `GlobalNavStatus`, `GlobalNavMsg`
   (registrovaná v katalogu → teče do záznamu a přehraje se ve View); testy nad syntetickou sítí.
3. ✅ **Trasa a její zobrazení**: `Router.Plan` → `GraphNavigationMsg` (při změně trasy nebo jednou
   za `RouteMessagePeriod`), vrstva „Trasa / graf" se rozsvítí. Cíl se zadává Ctrl+klikem, který nově
   míří do globální vrstvy jako LLA (kus fáze 5 předtažený, aby šlo fáze 2–3 vůbec vyzkoušet).
   **Zbývá:** ⬜ **inkrementální mapmatching** (hledat jen v okolí poslední hrany, plný scan jako
   fallback — `GoalField.NearestNode` dnes prochází všechny hrany; na malé mapě parku to zatím stačí).
4. ⬜ **`RouteProgress` + detektory A/B/C + eskalace** (penalizace / `CloseRoad` + TTL), seznam
   uzavření, zobrazení uzavřených hran v mapě. **4b:** průřez koridorem v `LocalNavigatoru`
   (`corridorWidthM` → `CorridorBlocked`).
5. ⬜ **Cíl z UI jako LLA** (Ctrl+klik → globální vrstva), panel stavu globální navigace.
6. ⬜ **Ověření na HW** — celý řetěz na OrangePI: doba stavby sítě, doba cyklu, chování na reálné trase.

## Otevřené úkoly

- **Recovery manévr** (couvnutí / otočka na místě) v lokální vrstvě — dnes neexistuje; detektor A
  bez něj umí jen čekat a pak uzavřít hranu.
- **Vizuální dojezd na cíl.** Poslední ~3 m řídit podle vidění (u QR kódu dává jeho poloha a velikost
  v obraze směr i vzdálenost) — GPS na ±2 m je pro „zastav u kódu" na hraně použitelnosti.
- **Koridor trasy jako cena v lokálním A\*** — dnes je z trasy jen jediný bod (mrkev). Měkká
  preference blízkosti osy cesty (šířka z `Node.Width`) by robota držela na cestě i tam, kde je
  vedle geometricky volno.
- **Korelace occupancy gridu s mapou pro odhad polohy** — už otevřené v
  [occupancy-and-local-planning.md](occupancy-and-local-planning.md); pro globální navigaci je to
  nejsilnější léčba na „špatná lokalizace ⇒ špatná mrkev".
- **Zdroj `.osm` dat** a jejich verzování v repu (dnes je `OSM/` mimo git — viz stav pracovní kopie).
- **Uzavření napříč běhy** — přežití restartu (soutěžní jízda po havárii aplikace).
