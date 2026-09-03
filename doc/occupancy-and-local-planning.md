# Occupancy grid a lokální plánování

Sloučení obou pohledů na sjízdnost z `CameraFrame` — **geometrického** (`Grid`, polární grid
z hloubky) a **sémantického** (`ImageProbability`, sjízdnost z barvy) — do jednoho
**kartézského occupancy gridu** akumulovaného v čase, a plánování průjezdné dráhy nad ním.
Výstupem je `RegulatorWayPoint[]` pro [`IPathPlanner`](path-following.md).

> **Stav (2026-08-11):** **hotové a napojené na runtime** — grid, integrátor, vzdálenostní pole,
> plánovač, `LocalNavigator` v grafu (Run), zprávy do záznamu, vrstvy v robot-centrickém pohledu
> a zadání cíle klikem. **Neověřeno na HW** (kamery nejsou namontované): vše je odsimulované nad
> syntetickou kamerou, výkon celého řetězu na OrangePI změřený není. Během implementace se ukázalo,
> že navržené „azimutové hranice" jsou geometricky neproveditelné; nahradila je projekce bodu země
> do obrazu (viz [Zápis do gridu](#zápis-do-gridu--jeden-gather-průchod)). Rozhodnutí a odůvodnění:
> [decisions.md 2026-08-10](decisions.md).

Kód: [`Src/ARBot.Common/Occupancy/`](../Src/ARBot.Common/Occupancy/) —
[`OccupancyGrid`](../Src/ARBot.Common/Occupancy/OccupancyGrid.cs),
[`OccupancyIntegrator`](../Src/ARBot.Common/Occupancy/OccupancyIntegrator.cs),
[`ClearanceField`](../Src/ARBot.Common/Occupancy/ClearanceField.cs),
[`LocalPathPlanner`](../Src/ARBot.Common/Occupancy/LocalPathPlanner.cs),
[`LocalNavigator`](../Src/ARBot.Common/Occupancy/LocalNavigator.cs) + konfigurace.
Zprávy: [`OccupancyGridMsg`](../Src/ARBot.Common/Logs/OccupancyGridMsg.cs),
[`LocalPlanMsg`](../Src/ARBot.Common/Logs/LocalPlanMsg.cs).
Testy: `Src/ARBot.Common.Tests/Occupancy/`, `Src/ARBot.Common.Tests/Vision/PolarGridLookupTest.cs`.

Navazuje na:
- [traversability-grid.md](traversability-grid.md) — polární grid sjízdnosti (vstup, `CameraFrame.Grid`).
- [path-following.md](path-following.md) — `IPathPlanner` / `PathResult` (odběratel výstupu).
- [ekf-fusion.md](ekf-fusion.md) — `AsyncFusionEngine.GetStateAt` (póza v čase snímku).
- [imu-and-frames.md](imu-and-frames.md) — world **ENU**, matematická orientace (0 = východ, +CCW).
- [osm-nav.md](osm-nav.md) — globální navigace; časem dodá cíl a bude porovnávat grid s mapou.

## Tok dat

```
CameraFrame (Grid + ImageProbability + Projection)
        │
        │  póza v case snimku: AsyncFusionEngine.GetStateAt(frame.TimeStamp)   ← per kamera zvlášť
        ▼
  OccupancyIntegrator ──▶ OccupancyGrid (LOcc + LRoad, log-odds, kruhový buffer)
                                  │
                          ClearanceField (EDT) ──▶ d[buňka] = vzdálenost k neprůjezdnému
                                  │
                          LocalPathPlanner (A*, cena = čas jízdy + otočení)
                                  │
                          RegulatorWayPoint[] ──▶ IPathPlanner.Plan ──▶ ControlLoop.Regulator
```

---

## Zapojení do runtime

`MessageProcessor` **`LocalNavigator`** („vyšší řídicí smyčka") na vlastním vlákně:
odebírá `CameraFrame` z `ControlLoop.Output` (řídicí smyčka je po pullu forwarduje), výstupem je
nastavení `ControlLoop.Regulator` (atomická výměna, volatile — už existuje) plus zprávy na `Stream`.
**Neběží na tiku `ControlLoop`** — tik má zůstat deterministický, plánovač smí občas trvat 15 ms.

Vstupní fronta je `OverflowPolicy.DropOldest` s malou kapacitou: když plánovač nestíhá, je správné
zpracovat **nejnovější** snímek a staré zahodit (stará mapa je horší než žádná).

Cyklus na jeden snímek:

1. `AsyncFusionEngine.GetStateAt(frame.TimeStamp)` — póza v čase pořízení **toho** snímku;
   `null` → snímek se **zahodí**.
2. `OccupancyIntegrator.Integrate(...)` — zápis obou kanálů (grid se přitom přecentruje na robota).
3. Bez cíle se dál nepokračuje — **mapa se ale akumuluje**, takže než přijde cíl, robot už okolí zná.
4. `ClearanceField.Build` + `LocalPathPlanner.Plan` — vždy celé znovu z aktuálního stavu gridu.
5. `IPathPlanner.Plan(waypointy)` → `ControlLoop.Regulator`.
6. **Když nový plán nevznikl**, robot jede dál po poslední předané dráze — ta se proto **každý cyklus
   ověřuje proti aktuální mapě** (viz níže).

### Když nový plán nevznikne: kontrola rozjeté dráhy

Nechat dojet poslední trasu a spolehnout se na watchdog **nestačí**: mapa se mezitím změnila a na té
trase už může být překážka. Watchdog nižší smyčky dobrzdí až po `Profile.PathControlTimeOut` (500 ms)
a z 0,8 m/s je brzdná dráha dalších ~1 m — to je pozdě.

Proto se dráha, po které robot právě jede, **každý cyklus kontroluje proti aktuálnímu poli
vzdáleností**:

- Kontroluje se **jen úsek, na který je robot fakticky zavázaný** — od jeho polohy (průmět na dráhu)
  dopředu o `v²/(2a) + v·Ts + rezerva`. Dál do budoucna nemá smysl: tam překážku vyřeší příští
  úspěšné přeplánování objezdem.
- **Kolize** = `Blocked` nebo odstup pod `SafeDist`. `Unknown` kolize **není** — to řeší rychlostní
  obálka.
- Při kolizi se `ControlLoop.Regulator` zahodí **okamžitě** (`null` = robot stojí, bezpečný stav),
  ne až watchdogem. Hlásí se stavem `LocalPlanStatus.AbortedCollision` (jde do záznamu i UI).
- Když je dráha volná, řízení se **nezahazuje** — dobrzdění zůstává na watchdogu, tedy řízené.
- Stojící robot (`v ≈ 0`) nouzově nezastavuje: brzdná dráha je nulová, není co řešit.

Zapojení v `ARBotRuntime.WireRun`; `ARBotRuntime.Navigator` je vystavený UI kvůli
`SetGoal`. Barevná projekce se sestavuje stejným líným vzorem jako hloubková
(`BuildColorProjectionResolver`, `ICamera.CreateProjector()`).

### Póza: dotaz na EKF v čase pořízení snímku, per kamera zvlášť

`LocalNavigator` si pro **každý snímek zvlášť** vyžádá `AsyncFusionEngine.GetStateAt(frame.TimeStamp)`.
Jen tak se snímky z obou kamer (jiné časy grabu) zarovnají do gridu na správné místo — při
0,8 m/s je 100 ms rozdílu 8 cm, tedy 1,6 buňky.

Je to bezpečné a korektní:
- `GetStateAt` je pod zámkem (volání z třetího vlákna je v pořádku),
- umí **dotaz do minulosti** (najde poslední checkpoint ≤ t a dopředikuje do t; není to smoother
  — nepoužívá pozdější měření, ale je to platný filtrovaný odhad v čase t),
- `Enqueue` řadí měření **podle času, ne podle pořadí příchodu**, takže opožděné měření
  z pomalejšího senzoru se zařadí správně.

Předpoklad, na kterém to stojí: **zpracování kamery trvá výrazně déle než IMU / GPS / motorů**,
takže v okamžiku, kdy dorazí `CameraFrame`, jsou ostatní měření z jeho času už ve fúzi.

**Okno historie.** `FusionConfig.HistoryWindow = 1 s`. Když je snímek starší, `GetStateAt` vrací
`null` (viz změna níže) a snímek se **zahodí** — zapsat ho s neznámou pózou by mapu otrávilo
mnohem hůř, než když jeden snímek chybí.

> **Změna v `AsyncFusionEngine` (součást tohoto kroku):** `GetStateAt(t)` pro `t <= tBase`
> (tedy mimo okno historie) vrací **`null`** místo dosavadního tichého fallbacku na bazový stav.
> Tichý fallback vracel pózu až o vteřinu starou, aniž by to volající poznal. `ControlLoop`
> na `null` reaguje zastavením (bezpečný stav). Případ „ještě nedorazilo žádné měření"
> (`initialized == false`) zůstává beze změny, aby se při startu emitoval `RobotStateMsg`.

### Režimy Run / View / Simulate

- **Run** — `LocalNavigator` běží, póza z `AsyncFusionEngine`.
- **View** — navigace **neběží**, přehrávají se jen zaznamenané zprávy. Aby šlo zpětně vidět,
  co navigace dělala, **zaznamenává se `OccupancyGridMsg` a `LocalPlanMsg`** (viz Serializace).
- **Simulate** (odložený režim) — až vznikne, EKF se zrekonstruuje ze zaznamenaných surových
  měření a `LocalNavigator` poběží nad záznamem beze změny kódu. Proto se do `CameraFrame`
  ukládá i projekce kamery (viz níže) — dnes ji nikdo nečte, je to investice do Simulate
  a do offline analýzy.

---

## Datový model `OccupancyGrid`

### Kotvení: world ENU, kruhový buffer jen v posunu

Osy gridu jsou **pevně srovnané se světem (ENU)**, buňka se adresuje absolutním indexem
`floor(x / res)`; do bufferu se jde přes `& (N-1)`. Posun robotu → přepočet originu a
**vynulování jen nově vstoupivších pruhů** (O(šířka), ne O(N)). **Rotace robotu se mapy vůbec
nedotkne.**

*Alternativa* „grid natočený s robotem" by vyžadovala resampling každý tik — rozmazává a je dražší.
Zamítnuto.

*Cena:* mapa dědí chybu lokalizace (hlavně yaw drift EKF). Řeší se clampem log-odds a krátkou
pamětí, ne dokonalou lokalizací — na horizontu jednotek sekund je drift zanedbatelný a víc
historie stejně nepotřebujeme.

### Rozměry

| parametr | default | pozn. |
|---|---|---|
| `Resolution` | 0,05 m | parametr |
| `N` | 256 | mocnina dvou kvůli maskování |
| pokrytí | 12,8 × 12,8 m | robot ve středu |

Dosah kamer je ~5 m dopředu; zbytek je paměť za robotem — potřebná při objíždění (překážka
opustí zorné pole) a při couvání.

### Dva kanály, log-odds ve `sbyte`

```csharp
sbyte LOcc    // geometrie:  překážka × volno        (z CameraFrame.Grid / depth)
sbyte LRoad   // sémantika:  cesta × mimo cestu      (z CameraFrame.ImageProbability / RGB)
```

Fixed-point měřítko 0,1 (rozsah ±12,7), clamp na **±5** → `p ∈ ⟨0,007; 0,993⟩`.
Dva kanály à 64 KB = **128 KB celkem** — vejde se do L2, žádná alokace za běhu.

**Proč dva a ne jeden:** „je tam překážka" a „není to cesta" jsou dvě různá pozorování z různých
senzorických modalit s různou charakteristikou chyb. Sloučit je do jednoho čísla znamená ztratit
možnost říct *který* z nich zakázal průjezd — a to je informace, kterou chceme mít při ladění
i při diagnostice. Jsou si ale **rovnocenné**: pro jízdu platí, že stačí, aby jeden z nich
průjezd nedovolil (viz stavy buňky).

**Zapomínání:** clamp ±5 dává přirozenou dobu přepsání (z plně obsazené na volnou ~25 pozorování
při `l_free = −0,4`, tj. 2,5 s při 10 Hz). Volitelně pomalý decay k nule (průchod 65 k buněk je
zanedbatelný).

`MaxZ` (2,5D, převisy/podjezdy) se zatím **neukládá** — přidá se, až bude potřeba.

---

## Zápis do gridu — jeden „gather" průchod

Pro každý `CameraFrame`:

1. Póza **v čase `frame.TimeStamp`** (viz výše); `null` → snímek zahodit.
2. Určit AABB zorného pole ve světových buňkách.
3. Pro každou buňku uvnitř: střed → do robot-rel. rámce → **promítnout do hloubkového obrazu**
   (`ICameraProjection.Transform`, rovina země `z = 0`) → **azimutová buňka z jeho SLOUPCE**,
   radiální prstenec ze vzdálenosti → update **`LOcc`**.
4. Tutéž buňku promítnout **color projekcí** do `ImageProbability` → vzorek → update **`LRoad`**.

**Proč gather** (od kartézských buněk k senzoru) a ne scatter: blízko robotu je polární buňka
menší než 5 cm (víc polárních buněk na jednu kartézskou), daleko je větší (jedna polární přes
mnoho kartézských). Scatter by daleko dělal díry; gather je korektní v obou směrech a bez
aliasingu. Objem ~5 000 buněk × 2 kamery × 10 Hz → zanedbatelné.

Oba kroky jsou korektní právě proto, že jde o **buňku země** — rovinný předpoklad tam přesně platí.
Stejný vzor (bod země → pixel → vzorek) už v repozitáři používá `PathEdgeFinder`.

### Proč se azimut hledá přes sloupec obrazu, a ne přes úhel

Původní návrh počítal s tabulkou **azimutových hranic** (pole A+1 úhlů) uložených v gridu. **Je to
geometricky neproveditelné:** u sklopené kamery **není sloupec obrazu konstantním azimutem** —
azimut pozemního bodu na jednom sloupci se mění s řádkem. Pro naši geometrii (sklon 20°, HFOV ~77°)
je ta změna ~0,15 rad, tedy skoro celá šířka azimutové buňky. Jediná hodnota na hranici by tedy
byla systematicky špatná (ověřeno testem `PolarGridLookupTest.SloupecObrazuNeniKonstantniAzimut`,
který to na návrhu odhalil).

Řešení je **promítnout bod země do obrazu a vzít jeho sloupec** — tím se **přesně invertuje**
mapování, které použil `CameraFrameProcessor.BuildGrid` (azimut = skupina `ColumnsPerCell` sloupců).
Radiální prstenec se bere ze vzdálenosti, protože přesně tak ho počítal i `BuildGrid`. Zpětný
lookup tedy sedí *přesně*, nikoli přibližně (test `BodZeme_PresSloupecObrazu_NajdeSpravnouBunku`).
API na gridu: `AzimuthBinFromColumn(column, edgeColumnTrim)` a `RadialBin(range)`.

### Okluze a dosah semantického kanálu

- **Okluze:** pro každý azimut se najde nejbližší prstenec s překážkou; od jeho náběžné hrany dál
  se `LRoad` **nevzorkuje** (barva by tam patřila překážce, ne zemi za ní).
- **Za dosahem hloubky se `LRoad` vzorkovat SMÍ** (`RoadBeyondDepthRange`, default zapnuto) — barva
  dohlédne dál než použitelná hloubka a je to jediný zdroj informace o cestě před robotem. Důvěra
  vzorku lineárně klesá mezi `RoadFullRangeM` (3 m) a `RoadMaxRangeM` (8 m).

### Rozšíření `CameraFrame` (FormatVersion 3 → 4)

- **`CameraFrame.Projection`** — neutrální DTO (bez závislosti na RealSense): `Intrinsics`,
  `inverseIntrinsics`, `from`, `to`, `Transformation`. ≈ 150 B/snímek proti ~1 MB obrazů.
  Cache `toDistortCache` / `camera2DToCamera3DCache` se **neserializují** (jsou odvozené a velké —
  640×480 ≈ 5 MB) — staví se líně při načtení a drží se **per kamera**, ne per snímek.
  Získává se z `IDepthCameraProjection.Info` (default implementace vrací `null`, aby testovací
  projekce nemusely nic doplňovat).

### Update model

Inverzní senzorový model škálovaný důvěrou buňky:

```
Obstacle → L += l_occ  · Confidence        (l_occ  = +0,85)
Free     → L += l_free · Confidence        (l_free = −0,40)
Unknown  → nic                              ← Unknown ≠ Free
clamp(L, ±5)
```

Dvě kamery = dva nezávislé hlasy, log-odds je sčítá; mírná přehnaná jistota v překryvu je
ošetřená clampem.

---

## Stavy buňky

Z obou kanálů se odvozuje trojice stavů. Kanály jsou **rovnocenné** — neprůjezdnost od
kteréhokoli z nich stačí:

| stav | podmínka | plánování | jízda |
|---|---|---|---|
| `FREE` | oba kanály **jistě** průjezdné | ano | plná rychlost dle odstupu |
| `BLOCKED` | **kterýkoli** kanál jistě neprůjezdný | ne | — |
| `UNKNOWN` | jinak (včetně „o cestě nic nevím") | ano, s penalizací | **nesmí se do ní vjet** |

**Zásadní detail, symetrický k `Unknown ≠ Free`:** „nemám o cestě data" **≠** „není to cesta".
`LRoad` smí blokovat jen pod prahem *s dostatečnou jistotou* (`LRoad < −l_θ`), ne při `LRoad ≈ 0`.
Jinak by robot stál hned po startu, protože RGB kanál je zpočátku všude nulový.

---


### Únik z blokované buňky (18. 8. 2026)

Buňka je `BLOCKED`, když ji zablokuje **kterýkoli** kanál — pro plánování je to správně, oběma se
vyhýbáme. Rozdíl mezi kanály ale začne být podstatný ve chvíli, kdy robot v blokované buňce **už
stojí**: plánovač vracel `RobotBlocked`, žádnou dráhu, a robot tam zůstal stát navždy.

**Nález ze záznamu `20260818-093903.rec`:** robot dobrzdil z 1,1 m/s mimo koridor a od 09:39:19.5 do
konce záznamu (5 s, 47 plánů) hlásil `RobotBlocked`. Buňka pod ním měla `LOcc = −4,85` (**hloubka na
záporném dorazu: jistě volno**) a `LRoad = +5,00` (**barva na kladném dorazu: jistě mimo cestu**).
Nejbližší nezablokovaná buňka byla **0,05 m** daleko — jedna buňka. Nebyla to tedy fyzická překážka,
ale okraj cesty; robot uvázl 5 cm od svobody.

Relaxace gridu by nepomohla: `LRoad` sedí na clampu a robot stojí, takže žádné nové pozorování
nepřichází — a buňku pod sebou dopředu hledící kamera nikdy neuvidí. Evidence-based zapomínání
(které grid má) se tedy nemá o co opřít.

**Dělicí čára je proto kanál, ne vzdálenost:**

> Ven se smí přes buňky blokované **semantikou** (z trávy zpátky na cestu). Přes buňky blokované
> **geometrií** se nesmí nikdy — do zdi se nejede.

Chování (`LocalPathPlanner.PlanEscape`, stav `EscapingBlocked`):

- Cílem hledání **není cíl mise**, ale **nejbližší buňka průjezdná běžným pravidlem**
  (není `BLOCKED` a má odstup ≥ `SafeDist`) — odtud může pokračovat normální plánování.
- Hledá se **uniformní cenou** (Dijkstra, bez heuristiky — cíl není bod) a jen do
  `EscapeMaxLength` (default 1,5 m). Když je nejbližší legální buňka dál, vrací se `RobotBlocked`:
  bloudit metry mimo cestu je horší než stát a nechat to na vyšší vrstvě.
- **Výchozí buňka je vždy průjezdná** — robot na ní stojí, takže z ní odjet musí i tehdy, když ji
  blokuje geometrie (typicky posun mapy chybou lokalizace). Do *další* geometricky blokované buňky
  se nevjede.
- Průjezd semanticky blokovanou buňkou je dražší (`EscapeBlockedCostFactor`, default 4×), aby únik
  mimo cestu strávil co nejméně.
- **Rychlost neřeší žádný zvláštní strop.** Uvnitř skvrny není před robotem nic potvrzeně sjízdného,
  takže brzdná obálka srazí rychlost na `MinCostSpeed` sama — únik je popojetí krokem. Kdyby se to
  v praxi ukázalo jako příliš pomalé, je to na samostatný knoflík.
- Na konci úniku robot **zastaví** (`finalGoal: true`) a další cyklus už plánuje běžně.

Dvě návaznosti, bez kterých by to nefungovalo:

- **`LocalNavigator.PathCollides`** by únikovou dráhu okamžitě zahodil jako kolizi (vede přes
  `BLOCKED` a s malým odstupem). Pro únikovou dráhu se proto kolize posuzuje **jen podle geometrie**
  — tedy tímtéž pravidlem, jakým se plánovala.
- **`GlobalNavigator.OnLocalPlan`**: `EscapingBlocked` záměrně nepadne ani do „selhání", ani do
  „platný plán". Série selhání se vynuluje (uváznutí nesmí nakonec zavřít hranu, která je
  v pořádku) a detektor záseku zůstane odzbrojený, dokud únik trvá.

**Co se nezměnilo:** pravidlo „kterýkoli kanál blokuje ⇒ `BLOCKED`" pro běžné plánování. Mění se
výhradně chování ve chvíli, kdy robot v blokované buňce už stojí (regresní test na to je).

**Odloženo:** zapisovat pod půdorysem robotu důkaz „volno" do kanálu **hloubky** (robot tam
prokazatelně stojí, a je to jediná buňka, kterou kamera nikdy neuvidí). Do semantického kanálu se
psát nesmí — jinak by se robot naučil, že cesta je všude, kam zabloudí.

---

## Vzdálenostní pole a rychlostní stropy

Z masky `BLOCKED` → **euklidovský distance transform** (Felzenszwalb–Huttenlocher, dva průchody,
O(N)) → pole `d[buňka]` = vzdálenost k nejbližšímu neprůjezdnému místu [m].

```
d < SafeDist                → neprůjezdné (tvrdá podmínka, nikdy se neporuší)
v_clear(d) = v_max · (d − SafeDist) / (PrefDist − SafeDist)     // ořezáno na ⟨0; v_max⟩
```

`SafeDist = 0,40 m` je **tvrdý** minimální odstup; `PrefDist = 0,80 m` je vzdálenost, od které
už rychlost neomezujeme (dál je pro průjezd i otáčení bezpečně volno). Mezi nimi **lineární**
rampa — u *bočního* odstupu nejde o brzdnou dráhu, ta patří výhradně do `v_brake` níže.

Robot se modeluje **opsanou kružnicí** (pro diferenciál, který se točí na místě, je to poctivý
model). Zpřesnění na kapsli (`OsmNav.Colider.RobotFootprint`) je možné později.

**Těsný start = únik, žádná zóna (od 3. 9. 2026).** Do té doby tu byla „eskapovací zóna": v okolí
`EscapeRadius` (0,5 m) od výchozí buňky se připouštěl i menší odstup než `SafeDist`, aby robot, který
zastavil blíž u překážky, měl odkud odjet. Zóna ale byla **symetrická** (pustila robota i blíž
k překážce) a **posouvala se s robotem**, takže když ležel cíl za okrajem cesty, robot se k trávě
doplížil po buňce s libovolně velkým `SafeDist` — naměřeno s mrkví FreeRunu v trávě a `SafeDist`
0,7 m. Dnes je pravidlo průjezdnosti **tvrdé a bez výjimek** (`SafeDist` se neslevuje nikde) a robot
stojící těsně u překážky (odstup pod `SafeDist`) se řeší **stejně jako robot v blokované buňce**:
únikem (`EscapingBlocked`) k nejbližší buňce, odkud jde plánovat běžně, kde zastaví. Únik míří
k nejbližší bezpečné buňce, ne k cíli, takže vede vždy **pryč** od překážky. **Hystereze půl buňky:**
únik se spouští až pod `SafeDist − Resolution/2`, končí na plném `SafeDist`; bez ní by robot, který
vyjel na buňku těsně nad `SafeDist`, po šumu gridu příště znovu „unikal" a na hranici kmital.
Rozhodnutí: [decisions.md](decisions.md), 3. 9. 2026.

**Cíl v nesjízdné nebo těsné buňce se hlásí zvlášť (od 3. 9. 2026).** `GoalBlocked` = cílová buňka
je `BLOCKED` (tráva, překážka), `GoalUnsafe` = volná, ale s odstupem pod `SafeDist`. V obou
případech plán vede k nejbližší bezpečné buňce a **na konci zastaví** (koncová rychlost 0). Dřív to
vycházelo jako `Partial` (stav pro legitimní „cíl za horizontem") a na konci dráhy jako
`AlreadyAtGoal`, takže mrkev položená do trávy vypadala jako „už jsem v cíli". Co s tím udělat,
rozhoduje **producent cíle** (mise, globální navigace), ne plánovač — ten neví, jestli cesta končí,
nebo mrkev jen přestřelila zatáčku. `GlobalNavigator.OnLocalPlan` s nimi zatím zachází jako
s `Partial` (plán platný, ne selhání), aby se chování nezměnilo potichu; reakce je otevřená.

### Rychlostní obálka — jeden invariant místo zvláštních pravidel

> **Nikdy nejeď rychleji, než z čeho zastavíš na hranici potvrzeně průjezdného.**

```
v_brake(s) = sqrt(2 · a_dec · s_free)      // s_free = vzdálenost po trase k první ne-FREE buňce
v = min(v_max, v_clear(d), v_brake(s))
```

Tímhle jediným pravidlem se řeší požadavek „skrz neznámo smím plánovat, ale nesmím do něj vjet":
robot naplánuje cestu skrz `UNKNOWN`, jede k němu, a jak se blíží, kamery místo dosvítí — buď se
otevře (obálka povolí dřív, než robot vůbec stihne zpomalit), nebo se ukáže jako `BLOCKED` a
přeplánování ho objede. Žádná zvláštní logika, žádná ručně nastavená „velmi nízká rychlost".

Dvě upřesnění, která vyplynula z implementace:

- **Za hranicí potvrzeného je strop `MinCostSpeed` (~5 cm/s), ne přesná nula.** Důvody jsou dva:
  (a) `PathPlanner` chápe `Speed == 0` u uzlu jako *„bez stropu"*, takže nula by strop naopak
  zrušila (nula patří jen poslednímu uzlu, kde znamená zastavení); (b) tvrdé zastavení může
  zadrhnout — stání samo prostor nedosvítí, zatímco plouživý pohyb ho vyjasní. Za 100 ms tiku je to
  5 mm. Tvrdá garance zůstává jinde: buňky `BLOCKED` na dráze nejsou a `SafeDist` se neporuší.
- **Konec dráhy je vždy hranicí známého** (horizont plánu / kraj gridu), takže brzdná obálka platí
  i tam, kde je za posledním uzlem prostě „konec plánu". Bez toho by poslední uzel dostal plnou
  rychlost a robot by do neověřeného prostoru vlétl s brzdnou dráhou ~1 m. Protože se grid každý
  tik přecentruje na robota, horizont se před robotem posouvá a v otevřeném prostoru se to nikdy
  neprojeví (z 0,8 m/s se ubrzdí na 1,07 m, horizont je 3 m dál).

Prakticky: při 10 Hz přeplánování a dohledu 5 m je hranice potvrzeného obvykle mnohem dál než
brzdná dráha (`0,8² / (2·0,3) ≈ 1,07 m`), takže robot jede naplno a invariant se neprojeví.
Zabere přesně tam, kde má — zatáčka za roh, hrana kopce, oslněná kamera.

---

## Plánovač cesty

**A\* na téže mřížce, 8-okolí, cena = čas jízdy.**

```
c(hrana)  = délka / v_limit(d) · (1 + w_unknown·[UNKNOWN])
c(start)  = |Δθ| / ω_max                        // čas otočení z aktuálního kurzu do prvního úseku
```

Celý požadavek *„drž se od překážek dál, ale když není místa dost, smíš blíž za cenu nižší
rychlosti — a minimální odstup se přitom nesmí porušit"* se tím převede na **jednu cenu = jízdní
čas**. Široký koridor je rychlý → levný; úzký je pomalý → drahý, ale **použitelný**, když nic
lepšího není. Tvrdý odstup je zvlášť, jako neprůchodnost — nikdy se neporuší. Žádné ruční
vyvažování „vzdálenost proti délce", žádný druhý režim.

**Proč A\* a ne hybrid-A\* / lattice / RRT:** kinematiku a dynamiku už řeší vrstva pod tím
(`PathPlanner` = geometrie rohů + brzdná obálka, `PathResult` = feedforward + lookahead).
Duplikovat ji v plánovači je zbytečné. 65 k buněk je pro A\* v C# jednotky ms.

**Cíl** přijde zvenčí (zatím kliknutím v `RobotCentricDocument`, časem z `OsmNav`) a typicky leží
mimo grid → promítne se na hranici gridu ve směru k němu. Cíl v neprůjezdném / bez cesty →
nejbližší dosažitelná buňka, respektive zastavení a hlášení; odstup se neporušuje ani nouzově.

### Postprocessing → `RegulatorWayPoint[]`

1. Řetěz buněk → **string-pulling**: slučuj do úsečky, dokud podél ní platí `d ≥ SafeDist`.
2. Pro každý waypoint:
   - `Speed` = `min v` (viz obálka výše) na následujícím úseku,
   - `MaxPositionError` = `clamp(d_min − SafeDist, ε_min, ε_max)` — **tolerance ε předaná
     plánovači je přesně volná rezerva**, takže zaoblení rohu obloukem (které z ε ukusuje)
     nikdy nezasáhne do bezpečnostního odstupu. `IPathPlanner` už dnes ε konzumuje; teď mu ho
     konečně někdo spočítá z reality.
3. **Poslední waypoint na horizontu ≠ zastavení.** Lokální plán má horizont ~5 m; kdyby končil
   `Speed = 0`, robot by se plazil. Koncová rychlost = `v_brake` na hranici potvrzeného.
   Skutečná nula je jen na skutečném cíli.

### Přeplánování a stabilita

**Každý cyklus plný přepočet z aktuálního gridu. Nic, co neprošlo validací proti aktuálnímu
gridu, se neodešle.** Držet plán spočtený nad starší mapou znamená jet proti důkazům, které už
robot má — to je nepřijatelné bez ohledu na to, co by to řešilo. Jediné, co smí jet „staré", je
watchdog `Profile.PathControlTimeOut` — a ten jen brzdí.

Riziko oscilace (plán skáče mezi objetím zleva a zprava, protože obě homotopické třídy mají
skoro stejnou cenu a jedna překlopená buňka posune argmin) se **neřeší lepivostí v čase**, ale
**poctivější cenou**: započtením času otočení `|Δθ|/ω_max` z aktuálního kurzu. Cesta vyžadující
otočku o 90° na místě opravdu trvá déle — to není trik, to je fyzika, kterou cena dosud
ignorovala. Strana se překlopí, jen když je druhá varianta lepší víc, než stojí to otočení.

K tomu **deterministické tie-breaking** v A\* (stejný vstup → stejný výstup) a v selftestu měřit
počet překlopení plánu. Nic dalšího se nepřidává, dokud se neprokáže, že je to potřeba.

---

## Serializace a vizualizace

- **`OccupancyGridMsg`** (~128 KB, default 2 Hz — `LocalNavigator.gridMessagePeriod`) — oba kanály
  jako `sbyte` pole + origin + rozlišení + prahy. Kanály se posílají v **lokálním** pořadí
  (`i + j*Size`), takže příjemce neřeší kruhový buffer. Proti ~1,8 GB/min obrazů zanedbatelné.
- **`LocalPlanMsg`** — cíl (požadovaný i skutečně dosažený), `RegulatorWayPoint[]`, stav plánování
  (i důvod selhání), min. odstup a doba výpočtu.
- Obojí se **zaznamenává**, takže ve View jde zpětně vidět, co robot věděl a kudy chtěl jet.
  Navigace ve View **neběží** (jen se přehrává).
- Vrstvy jsou ve **[world pohledu](world-view.md)** ([`WorldViewDocument`](../Src/ARBot/ViewModels/WorldViewDocument.cs)),
  ne v robot-centrickém: occupancy grid je **world-kotvený a akumulovaný**, takže v pohledu spojeném
  s robotem (včetně orientace) by se s každou zatáčkou **otáčel**, což je pro mapu matoucí. Ve world
  pohledu leží pevně a robot se po ní pohybuje — a navíc sedí na podklad (OSM / MBTiles).
  Robot-centrický pohled zůstává tomu, co je robot-centrické z podstaty: polárním gridům z kamer.
- **Occupancy se kreslí jako rastr, ne po buňkách.** 65 536 buněk nelze dělat jako featury; grid je
  osově srovnaný s ENU, takže se zakóduje do PNG a vloží jako `MRaster` v obdélníku (Web Mercator je
  konformní, na 12,8 m je zkreslení neznatelné). Přepočítává se jen při nové zprávě.
- Přepínače vrstev **Lokální mapa** / **Lokální plán** v panelu world pohledu.
- Cíl se zadává **Ctrl + klikem** do mapy (Ctrl proto, aby se to nepletlo s pan/zoom). Převod
  Web Mercator → lokální ENU jde přes tentýž `GeoReference` jako ostatní lokální vrstvy, takže bez
  GPS fixu a pózy cíl zadat nelze.

## Testy

Vrstva je čistě algoritmická (bez HW), takže jde otestovat celá:

- syntetický polární grid → occupancy → očekávaný koridor;
- průjezd branou 0,9 m — musí projít, se sníženou rychlostí;
- brána 0,6 m — musí odmítnout, `SafeDist` neporušen;
- každý waypoint: `MaxPositionError ≤` skutečná volná rezerva;
- `UNKNOWN` před robotem → rychlost klesá k nule na hranici potvrzeného, robot do něj nevjede;
- stabilita: počet překlopení plánu na syntetické scéně se symetrickou překážkou;
- A/B nad reálným `.rec`, až bude záznam z namontovaných kamer.

## Parametry

| parametr | hodnota | kde |
|---|---|---|
| `Resolution` | 0,05 m | `OccupancyGridConfig` |
| `N` | 256 (12,8 m) | `OccupancyGridConfig` |
| `l_occ` / `l_free` / clamp | +0,85 / −0,40 / ±5 | `OccupancyGridConfig` |
| `Scale` (krok fixed-pointu) | 0,05 | `OccupancyGridConfig` |
| prahy `BlockedThreshold` / `FreeThreshold` | +1,0 / −1,0 | `OccupancyGridConfig` |
| `RoadFullRangeM` / `RoadMaxRangeM` | 3,0 / 8,0 m | `OccupancyIntegratorConfig` |
| `UnknownCostFactor` | 3,0 | `LocalPlannerConfig` |
| hystereze úniku | půl buňky (`Resolution/2`) | `LocalPathPlanner.Plan` (odvozené, ne parametr; `EscapeRadius` zrušen 3. 9. 2026) |
| `HorizonM` | 6,0 m | `LocalPlannerConfig` |
| `MinCostSpeed` | 0,05 m/s | `LocalPlannerConfig` |
| `SafeDist` | 0,40 m | `Profile` (existuje) |
| `PrefDist` | 0,80 m | `Profile` (**nový**) |
| `MaxDecceleration` | 0,30 m/s² | `Profile` (existuje) |
| `MaxAllowedRotationSpeed` | π/6 rad/s | `Profile` (existuje) |
| `HistoryWindow` | 1 s | `FusionConfig` (existuje) |

## Plán realizace (fáze)

1. ✅ **`OccupancyGrid` + config** — kruhový buffer, log-odds, posun, stavy buněk; 18 testů.
2. ✅ **Rozšíření `CameraFrame`** — `Projection`, **FormatVersion 3 → 4** (čtecí větve v1–v3
   zachovány); roundtrip testy. *`AzimuthEdges` zamítnuty — viz výše.*
3. ✅ **`AsyncFusionEngine.GetStateAt` → `null` mimo okno** + `ControlLoop` na null zastaví; 5 testů.
4. ✅ **`OccupancyIntegrator`** — gather zápis obou kanálů; 15 testů nad syntetickou kamerou.
5. ✅ **`ClearanceField`** (EDT) + rychlostní stropy; 11 testů (vč. srovnání s hrubou silou).
6. ✅ **`LocalPathPlanner`** — A\*, string-pulling, waypointy; 20 testů (koridory, brány, neznámo,
   determinismus, cena otočení).
7. ✅ **`LocalNavigator`** — vlastní vlákno, napojení v `WireRun`, `ControlLoop.Regulator`, cíl z UI,
   kontrola rozjeté dráhy proti aktuální mapě; 10 testů.
8. ✅ **Zprávy + vizualizace** (`OccupancyGridMsg`, `LocalPlanMsg`, vrstvy ve world pohledu,
   Ctrl+klik = cíl) a záznam; 6 testů round-tripu.
9. ⬜ **Ověření na HW** — celý řetěz (integrace + EDT + A\*) na OrangePI. **Zatím jen odsimulované**;
   self-test potvrdil jen to, že runtime s novým uzlem čistě nastartuje a skončí (bez kamer).

## Otevřené úkoly

- **Není okluzní pravidlo `InShadow` příliš přísné?** (nalezeno 2026-08-14) V měření nad virtuálním
  HW zahodilo **~5 200 z ~12 000** kandidátů na barevný vzorek — tedy většinu. Důsledek: semantický
  kanál dostane řádově míň dat než geometrický (`road ≈ 3 800` vs. `occ ≈ 8 600` zápisů na snímek)
  a plocha mimo cestu se potvrzuje pomalu. Záměr pravidla je správný (za první překážkou v daném
  azimutu patří barva té překážce, ne zemi za ní), ale stíní se **celý zbytek paprsku**, včetně
  míst, kam kamera zjevně vidí. K rozmyšlení: stínit jen do určité vzdálenosti za překážkou, nebo
  podle její výšky, případně vzorek jen zeslabit (nižší confidence) místo úplného zahození.
  Měřicí nástroj je `VirtualHwOccupancyTest.Diagnostika_PricnyProfilSemantiky` (pole `ColorShadowed`
  v `OccupancyIntegrator.IntegrateStats`).
- **Dvě EDT místo jedné** (zvlášť překážky, zvlášť okraj cesty), kdyby bylo potřeba v nouzi
  vyjet z cesty. Zatím schválně jedna společná maska — rozdělí se, až se ukáže, že to chybí.
- **`MaxZ` per buňka** (2,5D) pro převisy a podjezdy.
- **Kapslový footprint** místo opsané kružnice, pokud bude opsaná kružnice moc konzervativní.
- **Korelace s mapou a odhad polohy** (druhý algoritmus nad gridem) — vstupem bude právě tento
  grid. Proto je world-kotvený a má oddělený kanál `LRoad`: porovnávat okraje cesty z RGB proti
  šířkám cest z OSM (`OsmNav.Graph.Node.Width`) je silnější signál než geometrické překážky.
  V repozitáři jsou z předchozí generace robotu `Navigations/MapCorelator` a `PathMapCorelator`
  — při návrhu se na ně podívat.
- **Simulate** — až vznikne, `LocalNavigator` poběží nad záznamem beze změny (proto projekce
  v rámci).
- **Výkon na ARM** — změřit celý řetěz (integrace + EDT + A\*) na OrangePI.
