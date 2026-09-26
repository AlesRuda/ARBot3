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

### Klín mezi zornými poli barvy (`wedgefill=`, 12. 9. 2026)

**Praktické pozorování z terénu** (autor): zorná pole RGB obou kamer se ve směru jízdy
nepřekrývají, takže při jízdě rovně vzniká úzký klín buněk `Unknown`, a ten sráží dopřednou
rychlost.

**Změřeno a potvrzeno** (`ARBot.Analyze wedge` nad `records/test/20260907-170728.rec`):

| údaj | hodnota | odkud |
|---|---|---|
| HFOV barvy | **55,0°** (640×480) | intrinsiky v záznamu |
| HFOV hloubky | 89,6° (480×270) | intrinsiky v záznamu |
| montážní yaw kamer | ±29,3° | `CameraFrame.Projection.Transformation` |
| pokrytí barvou | −56,9…−1,8° a +1,8…+56,8° | dopočet |
| **mezera mezi nimi** | **3,7°** = 0,19 m ve 3 m, 0,38 m v 6 m | dopočet |
| „chybí jen semantika" ve směru jízdy | **4,1 %** buněk (po stranách 1,4 %) | occupancy grid |
| totéž podle vzdálenosti | 3,5 % v 1 m → **7,3 % ve 4 m** | occupancy grid |
| příčná šířka díry, na které se zastaví paprsek vpřed | **0,10 m** (p50 i p90), ve 2,3 m | occupancy grid |

⚠️ **Katalogových 69° platí pro 16:9**; při 640×480 má barva 55°, a právě ten rozdíl mezeru dělá.
Hloubka klín pokrývá (89,6°), ale `Free` vyžaduje **oba** kanály, takže buňka zůstane `Unknown`.

**Léčba: `WedgeFiller`** — buňce v klínu dopíše semantiku **interpolovanou z nejbližších buněk
příčně vlevo a vpravo**. ⚠️ **Není to „prohlásit neznámo za sjízdné":** konstanta „sjízdné" by do
mapy zapsala cestu i tam, kde je tráva nebo díra, a ta lež by se šířila dál (grid čte i korelace
s mapou). Interpolace přes 19 cm mezeru mezi dvěma pozorováními **téhož** povrchu nové tvrzení
nevyrábí — když je vlevo i vpravo tráva, vyjde tráva.

Čtyři pojistky (každou hlídá test v `WedgeFillerTest`):

1. **Geometrie se nedoplňuje nikdy** — jen semantický kanál. Překážku vidí hloubka a ta v klínu
   funguje; vymýšlet „volno" tam, kde může být překážka, by bylo nebezpečné.
2. **Doplní se jen buňka, která je `Unknown` právě kvůli semantice** — geometrii má potvrzenou
   jako volnou a semantika je mezi prahy. Buňka, kterou barva rozhodla, se nedotkne, a vzorek se
   **přičítá** jako další důkaz, nepřepisuje.
3. **Musí být podpora z obou stran** do `WedgeFillMaxGapM` (0,6 m). Jinak je to extrapolace.
4. **Doplněním nemůže vzniknout překážka** — zápis se ořízne pod práh `BlockedThreshold`.
   Doplnění smí rychlost jen povolit, nikdy ji samo zakázat.

#### ⚠️ Kolik to stojí: záleží na záznamu, a hodně

První měření proběhlo nad `20260907-170728.rec` a vyšlo z něj, že klín je okrajový (20,0 %
zastavení, zisk +0,8 %). **Autor na to namítl, že to neodpovídá tomu, co vidí v terénu**, a ukázal
na `20260912-125851.rec` — a měl pravdu. Tentýž nástroj nad tím záznamem (1155 gridů, celý běh):

| na čem se zastaví paprsek vpřed | 7. 9. | **12. 9.** |
|---|---|---|
| **chybí SEMANTIKA** (geometrie je) ← klín | 20,0 % | **72,6 %** |
| chybí geometrie (semantika je) | 40,7 % | 18,1 % |
| překážka (geometrie + semantika) | 32,7 % | 2,9 % |
| chybí obojí | 5,3 % | 5,5 % |

Na záznamu z 12. 9. je `freeAhead` p50 jen **1,52 m** proti **4,58 m**, které by dala samotná
geometrie — semantika tedy ukrajuje **tři metry**. A převedeno na to, co je v terénu vidět:
**robot je pod 0,6 m/s ve 40,5 % vzorků, zatímco bez semantiky by to bylo 3,3 %.**

⚠️ **Poučení o měřidle:** průměr `VBrake` přes celý běh je na tuhle otázku **špatná veličina** —
většinu času je `VBrake` na stropu, takže se v něm rozdíl rozpustí (proto vycházelo „+0,8 %").
Správně se ptát „**jak často** robot leze", a měřit to na **víc záznamech**: jeden běh není
vzorek, dva běhy z téhož robota se liší čtyřnásobně.

#### Co léčba udělá (a co ne)

Nad `20260912-125851.rec`: `freeAhead` p50 **1,52 → 2,22 m**, čas pod 0,6 m/s **40,5 → 33,9 %**,
průměr `VBrake` **0,908 → 0,952 m/s**. Tedy asi **pětina ztráty**, ne celá — zbytek je řetěz
dalších děr, ne jedna. Širší klín to nespraví (12° dá 34,1 %, 20° už jen 33,3 % — nasycuje se).

⚠️ **Cesta k těm číslům byla řada oprav, každá nalezená měřením, ne úvahou** — každou hlídá test,
protože každá vypadala jako „rozumná opatrnost" a přitom léčbu vypínala:

| co bylo špatně | proč to nefungovalo | oprava |
|---|---|---|
| doplňovaly se jen buňky s `LRoad == 0` | buňky v klínu mají **slabý** vzorek z okraje zorného pole, ne žádný | doplní se každá, která je `Unknown` kvůli semantice |
| soused musel být **rozhodnutý** | rozhodnutý z obou stran je jen **17,3 %** případů, slabý aspoň jeden **78,6 %** | stačí jakýkoli vzorek |
| důvěra 0,5 | sousedé jsou sami těsně pod prahem (−0,95 / −1,10 proti −1,00), půlka na rozhodnutí nestačí (13,3 % zápisů) | výchozí **1,0** |
| doplňovalo se až od 0,5 m | **16–20 %** zastavení je blíž, a krátká vzdálenost bolí nejvíc | `MinRangeM` = **0,3 m** (navazuje na `FootprintRadiusM`) |
| klín jen úhlový | ve 0,35 m je 3,7° široké **1,8 cm**, tedy méně než buňka → neprošla žádná | minimální šířka **jedné buňky** |

⚠️ **A jedna chyba byla v měřidle, ne v léčbě:** nemodelovalo `FootprintRadiusM` (plánovač bere
buňky pod robotem jako sjízdné), takže tvrdilo, že robot leze pod 0,2 m/s ve 39,7 % času. Po
opravě je ten podíl **nulový** — robot pod 0,2 m/s nejede nikdy, protože půdorys dá vždy aspoň
0,3 m volna.

Zapíná se `wedgefill=<stupně>` (výchozí **6**, tedy naměřených 3,7° s rezervou na nepřesnost
montáže); **`wedgefill=0` vrací přesně původní chování** pro A/B. Měřidlo je
`ARBot.Analyze wedge` (`--wedgefill=`, `--wedgeconf=`, `--limit=`).

⚠️ **Na zařízení to neběželo** — ověřeno buildem, testy a měřením nad záznamem ze zařízení.

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
psát nesmí — jinak by se robot naučil, že cesta je všude, kam zabloudí. Pomohlo by jen na zdánlivou
překážku z hloubky pod celým půdorysem a zápis podle chybné pózy by smazal skutečnou překážku ve
slepé zóně; čeká, až se takový `RobotBlocked` objeví v záznamu (`lp-zapis-volna-pod-robotem`).

**Na robotu ověřeno 25. 9. 2026** (Track, `20260925-142428.rec`): po posunu gridu korekcí pózy
dvakrát `EscapingBlocked`, robot pokaždé za 6–10 s vyjel a pokračoval
(`lp-grid-posun-pomalou-korekci`).

---

## Vzdálenostní pole a rychlostní stropy

Z masky `BLOCKED` → **euklidovský distance transform** (Felzenszwalb–Huttenlocher, dva průchody,
O(N)) → pole `d[buňka]` = vzdálenost k nejbližšímu neprůjezdnému místu [m].

```
d < SafeDist                → neprůjezdné (tvrdá podmínka, nikdy se neporuší)

SMĚROVÝ model (výchozí od 3. 9. 2026, envelope=directional):
  closing      = max(0, −t · ∇d)                       // rychlost přibližování k překážce na jednotku v (0..1)
  v_along(d)   = v_max · (d − SafeDist) / EdgeMarginM   // ořezáno na ⟨0; v_max⟩ — podél překážky
  v_closing    = √(2 · a · (d − SafeDist)) / closing    // kolmo: ubrzdit před SafeDist (closing = 0 → bez omezení)
  v_env        = min(v_along, v_closing)

RADIÁLNÍ model (původní, envelope=radial, pro A/B):
  v_clear(d)   = v_max · (d − SafeDist) / (PrefDist − SafeDist)     // ořezáno na ⟨0; v_max⟩
```

`SafeDist = 0,40 m` je **tvrdý** minimální odstup. **Směrový model** rozlišuje, kam dráha míří:
`t` je směr dráhy ve vzorku, `∇d` gradient pole odstupů (centrální diference ze snímku; EDT je
1-lipschitzovský, takže `|∇d| ≤ 1`; na hřebeni pole, tedy uprostřed cesty, je ~0). Riziko u okraje je
**kolmé** — jak rychle se k němu robot blíží — a to řeší `v_closing` jako brzdnou dráhu k hranici.
Jízda **podél** okraje robota nepřibližuje, jediné, před čím tam rampa chrání, je příčná chyba
sledování dráhy; proto je `EdgeMarginM` úzké pásmo (0,15 m, v simulaci je příčná chyba sledování
p50 0,01–0,05 m, na HW přeměřit). Uzel dostává **minimum obálky přes vzorky svého okna**, ne obálku
minima odstupu — ve směrovém modelu záleží u každého vzorku i na směru.

*Proč:* radiální rampa trestala **blízkost** okraje bez ohledu na směr. FreeRun v pravé polovině
2 m cesty jede 0,5 m od trávy, tedy ve čtvrtině rampy `SafeDist..PrefDist`, a robot jel trvale
0,3 m/s (naměřeno 3. 9. 2026, jakmile obálka začala řídit). Směrově vyjde při 0,5 m podél okraje
`0,8 · v_max`. Tráva je přitom podle zadání soutěže zeď (vjezd = konec), takže se řešil model, ne
sémantika. Původní `PrefDist = 0,80 m` („bezpečně volno pro průjezd i otáčení") platí jen
v radiálním režimu. Tatáž obálka dává i **cenu hran A\*** (`VCost(d, closing)`), takže plánovač
zdražuje přibližování k překážce, ne jízdu podél ní. Rozhodnutí: [decisions.md](decisions.md), 3. 9. 2026.

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

**Půdorys robota je sjízdný (od 3. 9. 2026).** Kamery se sklonem ~20° zem těsně před robotem nevidí
(slepá zóna ~0,5 m) a `Free` vyžaduje potvrzení oběma kanály, takže buňka pod robotem je po startu
`Unknown`, `s_free` je 0 a robot leze `MinCostSpeed`, dokud zónu nepřejede — naměřeno **10 s při
0,05 m/s** na každém startu (a na 2 m dlouhé mapě navždy). Proto se vzorky dráhy blíže než
`FootprintRadiusM` (0,3 m) od robota berou při výpočtu `s_free` jako sjízdné, **pokud nejsou
`BLOCKED`**. Je to fakt, ne domněnka: robot na nich stojí. Do gridu se nic nezapisuje, průjezdnost
ani únik se nemění (robot v trávě se dál plouží). Zamítnuté alternativy: zapsat půdorys do gridu jako
`Free` + cesta (robot v trávě by si pod sebou vyrobil cestu a únik by ztratil spouštěč), presumovat
`Free` v celé slepé zóně (domněnka o prostoru bez senzoru), zvýšit podlahu (maskuje příčinu).

### ⚠️ Nález 7. 9. 2026: v terénu drží rychlost dole `VAlong`, a to hned u robota

Změřeno nad `records/test/20260907-170728.rec` (452 s FreeRunu venku, `maxspeed=1`,
`envelope=directional`, `safedist=0.4`, `backproject=hist`) nástrojem
**`ARBot.Analyze envelope`**. Rekonstrukce z gridu sedí na hlášený `MinClearanceM`
**do jedné buňky v 96,9 %**, takže čísla nejsou domněnka:

| co | kolik |
|---|---|
| **`VAlong`** (odstup od překážky) vázal | **77,2 %** plánů |
| nic (plná rychlost) | 12,7 % |
| `VBrake` (hranice potvrzeného) | 5,9 % |
| `VClosing` (přibližování) | 4,2 % |
| plán na podlaze 0,05 m/s **už v prvním uzlu** | **53,1 %** |
| z plánů na podlaze vázal `VAlong` | **99,8 %** |
| medián nejmenšího odstupu na dráze | **0,403 m** (= `SafeDist`) |
| vázající uzel od robota (p50 / p90) | **0,00 / 0,00 m** |
| medián příkazované rychlosti | **0,05 m/s** (průměr 0,24) |

Tři věci, které z toho plynou a bez rozpadu po uzlech je vidět nebylo:

1. **Není to „nevidím".** `VBrake` má p50 1,00 m/s a volno před robotem p50 1,40 m — půdorys
   robota (`FootprintRadiusM`, 3. 9.) svou práci dělá. Hypotéza „robot se plouží skrz neověřený
   prostor", kvůli které vznikl `LocalPlanReport`, na tomhle záznamu **neplatí** (0,3 % případů
   na podlaze). Hranice potvrzeného je přitom vždy `Unknown`, nikdy `Blocked` — jen leží dost daleko.
2. **Váže uzel u robota, ne konec dráhy.** Medián i p90 vzdálenosti vázajícího uzlu je 0,00 m,
   takže to robota skutečně zdržuje (kdyby vázal konec dráhy, dojede tam za půl sekundy a plán je
   jiný). Minimum přes plán by tyhle dva případy nerozlišilo.
3. **`SafeDist` je zároveň tvrdá hranice A\* i nulový bod rampy.** A\* blíž než `SafeDist` nejde,
   takže v těsném místě je **nejlepší legální dráha zároveň ta, které obálka dá nulu** — 14,1 %
   plánů má odstup *právě* 0,40 m. Plná rychlost podél překážky vyžaduje odstup ≥ 0,55 m
   (`SafeDist + EdgeMarginM`), tedy volný kanál ≥ 1,10 m na obě strany.

**Čím je ta překážka.** Buňka, která odstup dává, je v 58,9 % blokovaná geometrií (hloubka)
a ve 40,6 % semantikou (barva). Souvislá skvrna má p50 **29 buněk** a 52,1 % případů má ≥ 20 buněk
(tedy skutečná hrana), ale **41,5 % má skvrnu do 4 buněk** — izolovaný šum v mapě, který srazí
rychlost na podlahu úplně stejně jako zeď. Volný kanál napříč dráhou u robota je přitom p50
**3,80 m** a kamerový koridor p50 3,60 m (souhlasí), a užší než 1,10 m je jen v 0,8 % —
**cesta je široká, robot jen jede 0,4 m od něčeho blokovaného**.

⚠️ **Příčina, proč jede tak blízko, změřená není.** *(Stav k 7. 9. Hlavní mechanismus nalezen
8. 9. 2026 — je to vyhlazování dráhy, viz sekci níž; tenhle odstavec zůstává, protože podíl
obou příčin v tom záznamu změřený pořád není.)* Kandidáti: chyba kurzu z VN100 (v tomtéž
záznamu p50 −24° proti GPS kurzu, viz [imu-and-frames.md](imu-and-frames.md)) rotuje jak mrkev
FreeRunu, tak zápis do gridu, a při paměti gridu ~2,5 s se buňky zapsané při jiné chybě kurzu
rozmazávají; k tomu 41,5 % skvrn ≤ 4 buňky. **Nejdřív opravit kurz, pak přeměřit** — ladit obálku
nad mapou kreslenou špatným kurzem nemá smysl.

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

### ✅ Příčina nalezena 8. 9. 2026: **vyhlazování zahodí odstup, který cena A\* koupila**

Otázka z nálezu výše zněla *„proč robot nejede středem široké cesty, když je cena = jízdní čas?"*.
Odpověď je v postprocessingu: **A\* skutečně naplánuje časově optimální dráhu, ale
string-pulling ji pak nahradí nejkratší legální lomenou čarou** — a legalita je jen tvrdé
`d ≥ SafeDist`, nikoli cena. `LocalPathPlanner.SegmentPassable` volá `Passable()`, tedy **totéž
pravidlo, které rozhoduje o průjezdnosti, ne `VCost()`**. Vyhlazení tak dráhu **přitiskne zpátky
na hranici tvrdého odstupu** přesně tam, kde se jí A\* vyhýbal.

**Změřeno na syntetické scéně** (rovný volný kanál 3,80 m jako v záznamu, robot v počátku, cíl
2,8 m přímo vpřed, jediná překážka **2×2 buňky** = 10×10 cm, 0,45 m stranou od spojnice;
`SafeDist` 0,40, `EdgeMarginM` 0,15, `MaxSpeed` 1,0):

| scéna | délka dráhy **A\*** | dráha po vyhlazení | `MinClearance` | `Speed` uzlu 0 |
|---|---|---|---|---|
| bez překážky | 2,80 m | přímka | 9,05 m | **1,00 m/s** |
| skvrna 0,45 m od spojnice | 2,92 m | **přímka** (2 uzly) | **0,450 m** | **0,33 m/s** |
| **táž scéna, `EdgeMarginM` = 2,0** | **4,59 m** | **táž přímka** (2 uzly) | **0,450 m** | **0,05 m/s** |
| skvrna 1,2 m od spojnice | 2,80 m | přímka | 1,200 m | 1,00 m/s |

Rozhoduje **třetí řádek**: s dvoumetrovou rampou cena A\* blízkost trestá tak, že plánovač jde
**objížďkou o 1,79 m delší** (4,59 proti 2,80 m) — a **výstup je pořád ta samá přímka
s odstupem 0,450 m**. Vyhlazení tedy celou objížďku zahodí; **na geometrii výsledné dráhy nemá
cenová funkce vliv**, rozhoduje o ní jen tvrdý odstup. To vysvětluje i to, proč je v záznamu
medián nejmenšího odstupu **0,403 m**: `√65 · 0,05 m` je **první kvantum vzdálenostního pole nad
`SafeDist`** — dráha leží přesně na mezi toho, co je ještě legální.

**Druhý násobič je okno uzlu.** Rychlost uzlu *k* je minimum obálky přes **oba** sousední úseky
a `PathResult.Control` strop uzlu vynucuje **podél celého úseku** (viz komentář 3b tamtéž). Po
vyhlazení jsou úseky dlouhé metry, takže **jeden vzorek, který se otře o skvrnu velikosti dlaně,
zastropuje několik metrů jízdy** — v tabulce výše srazila skvrna 10×10 cm celý 2,8m úsek z 1,00
na 0,33 m/s. Sedí to s tím, že v záznamu je vázající uzel v p50 i p90 **0,00 m** od robota
(uzel 0 = celý první, nejdelší vyhlazený úsek) a že **41,5 % skvrn má ≤ 4 buňky**.

**Třetí věc: rampa `VAlong` je útes, ne rampa.** Ve směrovém modelu je cena nad
`SafeDist + EdgeMarginM` = **0,55 m plochá**, takže A\* nemá důvod jít dál než 0,55 m — a pod ní
spadne z plné rychlosti na nulu na **0,15 m**. Vyhlazení dráhu zaparkuje přesně do toho pásma
0,40–0,55 m, kde 15 cm příčné chyby (nebo posun mapy o tři buňky) znamená plazení. V radiálním
modelu (`envelope=radial`) je rampa 0,40–0,80 m, ale ta byla nahrazena právě proto, že trestala
i jízdu **podél** okraje.

### ✅ Léčba (8. 9. 2026): vyhlazování posuzuje ČAS a ověřuje RAMPU (`smooth=`)

`StringPull` přijme zkratku, jen když platí **obojí** (`PathSmoothingMode.TimeAware`, výchozí;
`smooth=passable` vrátí původní pravidlo pro A/B):

1. **Předpovězená rampa se vejde pod obálku** v každém vzorku úseku. Rampou se rozumí to, co
   regulátor skutečně odjede: drží strop vjezdového uzlu (`PathResult.Control` bod 3b) a včas
   dobrzdí na strop výjezdového (`PathPlanner` → `VLimit` → `Dist2Speed`), tedy
   `v(s) = min(v_vjezd, √(v_výjezd² + 2a·(L−s)))`.
2. **Nezhorší to jízdní čas** proti jemnému dělení — tedy proti tomu, co by robot dostal, kdyby
   každá buňka byla vlastní uzel (to je dosažitelný krajní případ, ne teorie).

Rychlosti krajních uzlů se berou ze **zpětné brzdné obálky** podél buněčné dráhy, ne z holé
obálky: robot v tom místě stejně pojede jen tak rychle, jak se stihne zbrzdit na to, co je dál.
Bez toho by reference tvrdila, že se smí jet naplno až do buňky před skvrnou a tam skokem
zpomalit — fyzikálně nemožné, takže by se zamítala i sloučení, která nic nestojí.

**Brzdný zákon si drží profil: `IMotionProfile.Dist2MaxSpeed(dist, endSpeed)`** (přibylo do rozhraní
8. 9. 2026) — *„nejvyšší rychlost, kterou smím mít ve vzdálenosti `dist` před bodem, kde mám být na
`endSpeed`"*. Plánovač si ho neopisuje; volá ho na třech místech (zpětná brzdná obálka v referenci,
strop `vCruise` a předpověď `v(s)` v každém vzorku) a je to **tatáž instance profilu, kterou dostane
`PathPlanner`** (`ARBotRuntime` ji vyrobí jednou a předá do obou; jinak by se ověřovala jiná rampa,
než která se pojede). Čas se pak integruje **přes tytéž vzorky**, ne uzavřeným vzorcem — ten by
předpokládal konstantní deceleraci, kterou třeba `SqrtMotionProfile` nemá.

⚠️ **`Dist2Speed` na to NENÍ.** Není to průběh rychlosti po dráze, ale **jeden krok regulátoru** —
diskrétní lichoběžník s periodou 0,1 s, činitelem 0,9 a `startSpeed` = okamžitá rychlost robotu, ne
strop úseku. V nule vrací nulu, takže použitý jako `v(s)` rozpadne i volnou plochu (naměřeno
**41 uzlů**), a `RegulatorResult` je třída, takže by to alokovalo na každý vzorek. Vztah obou metod
hlídá `MotionProfileParityTests.Dist2MaxSpeed_JeHorniMeziPrikazuDist2Speed_KdyzSeDoMistaVjizdiPodStropem`:
**dokud robot do místa vjíždí pod stropem, příkaz ho nepřekročí.** Ten předpoklad není formalita —
když už robot jede rychleji, než obálka dovoluje, regulátor vrací nejlepší možné brzdění, ne
nesplnitelný strop (`Dist2Speed(0,05, v=0,4, v_e=0)` = **0,252** proti stropu 0,141; z 0,4 m/s se na
pěti centimetrech zastavit nedá). Že robot vjíždí pod stropem, drží zpětný průchod plus strop úseku.

⚠️ **Obálka a profil brzdí každý podle své konstanty** (`LocalPlannerConfig.MaxDeceleration` proti
`IMotionProfile.Acceleration`, dnes obě 0,50 z `Profile`). Kdyby profil brzdil **pomaleji**, poruší
se obálka i **bez jakéhokoli slučování** — proto na to `LocalPathPlanner` v konstruktoru upozorní
do `Trace`. Tuhle vazbu mezi dvěma konfiguracemi jinak nikdo nehlídá; `VBrake`/`VClosing` zůstávají
na `cfg.MaxDeceleration`, protože `LocalPlannerConfig` profil nezná.
do `Trace`. Tuhle vazbu mezi dvěma konfiguracemi jinak nikdo nehlídá.

**Strop uzlu se tím zároveň mění na obálku V UZLU** (dřív minimum přes obě sousední úsečky).
To je ta druhá polovina věci a bez ní by první nefungovala: kdyby uzel dál nesl minimum, sloučený
úsek by se **celý** jel rychlostí svého nejhoršího místa a rampa by se nikdy neodjela. Bezpečnostní
argument se tím vymění, ne oslabí:

| | do 8. 9. 2026 | od 8. 9. 2026 |
|---|---|---|
| strop uzlu | minimum obálky přes okno | obálka v uzlu |
| co chrání vnitřek úseku | „každý vzorek zastropuje aspoň jeden uzel" (platí konstrukcí) | „plánovač ověřil, že se rampa vejde pod obálku" (bod 1 výše) |
| předpoklad | žádný | plánovač brzdí konzervativněji než regulátor (`MaxDecceleration` 0,5 proti 1,0 v profilu) |

Obě poloviny jsou **pár**: u `smooth=passable` a u **úniku** (`EscapingBlocked`) se rampa neověřuje,
takže tam zůstává i původní minimum přes okno.

**Cena kroku v referenci** je táž funkce jako v A\* (`VCost`), ale **bez** `UnknownCostFactor`
a bez ceny otočení: obojí je složka *plánovací* ceny (preference), ne čas. `gScore` by referenci
dalo zadarmo, ale nese je — a nafouknutá reference by zkratky přes neznámo přijímala příliš
ochotně. Cena otočení se místo toho přičítá **zvlášť na obou stranách** (zkratka z prvního uzlu
mívá jiný směr než první krok A\*; kvantování do 8 směrů je až 22,5°, což při `ω = π/6` dělá
0,75 s). Do rampy samotné se otáčení neplete — to je věc geometrie rohů ve vrstvě pod tím.

⚠️ **Past, která to málem shodila: zbývající dráhu měř od STŘEDU BUŇKY, ne ze spojitého `t`.**
Obálka je funkce odstupu *buňky*, kdežto rampa je spojitá funkce dráhy; při vzorkování po půl
buňce se obě strany rozešly o **1–2 %** — a protože tam, kde obálka *je* brzdná křivka, mají
vyjít úplně stejně, stačilo to, aby se rovnoměrné zpomalování nesloučilo vůbec (14 uzlů na dráze
0,75 m). Po srovnání obou stran na tytéž diskrétní pozice vychází rampa == obálka a slučuje se
bez jakéhokoli prahu; zbyla jen numerická rezerva 1e-6. **Nezaváděj místo toho toleranci** —
jedna verze téhle změny ji měla (5 %) a byla to jen zakrytá nekonzistence.

**Naměřeno** (Release, x64), `smooth=passable` → `time`:

| scéna | uzlů | `MinClearance` | `Speed` uzlu 0 | čas plánu |
|---|---|---|---|---|
| volný kanál, jedna skvrna 2×2 buňky 0,45 m stranou | 2 → **5** | 0,450 → **0,600 m** | 0,333 → **1,000** | 0,35 → 0,21 ms |
| **grid 256, koridor 3,8 m, 8 skvrn 10×10 cm** | 5 → **19** | **0,403** → **0,492 m** | **0,050** → **0,488** | 1,84 → **1,52** ms |
| jízda **kolmo** ke zdi (0,75 m, `VClosing` klesá s každou buňkou) | 2 → **4** | — | 0,173 → **0,706** | — |

Prostřední řádek reprodukuje terén: bez léčby vyjde `MinClearance` **0,403 m** a rychlost
**0,050 m/s**, tedy **přesně mediány ze záznamu** `20260907-170728.rec` — a to jen z osmi skvrn
velikosti dlaně v jinak volném koridoru. Poslední řádek je ta rampa: robot u sebe dostane
**0,706 m/s** místo 0,173 a na strop u trávy dobrzdí, místo aby se plazil od začátku.

**Plánování se nezpomalilo, naopak** (1,84 → 1,52 ms): rampa se ověřuje na týchž vzorcích, po
kterých se stejně kontroluje průjezdnost, a z průchodu se vyskočí na první porušení.

⚠️ **Co to NEOPRAVÍ.** Cena je nad `SafeDist + EdgeMarginM` = 0,55 m **plochá**, takže ani dokonale
poctivé vyhlazování nepovede robota středem 3,8m kanálu — skončí na 0,55 m od trávy. „Jet středem"
je jiná páka (tvar obálky / `EdgeMarginM`) a míchat ji sem by znamenalo dvě rozhodnutí v jednom.
A pořadí pořád platí: **kolik z toho v terénu dělá vyhlazování a kolik rozmazání gridu chybou
kurzu, změřené není** — zisk se má měřit až nad záznamem se správným kurzem. Na HW to neběželo.
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

### ✅ Cílová zóna místo bodu („poloměr mrkve", 14. 9. 2026)

**Nález.** Cílem A\* byla **jediná buňka**, takže mrkev položená do trávy nebo těsně k překážce
byla nedosažitelná **jako celek**: plán skončil na nejbližší bezpečné buňce, stav `GoalBlocked`
a `stopAtEnd` — robot tam **zastavil a čekal**, ačkoli jiná část cílové zóny dosažitelná byla.
Naměřeno `ARBot.Analyze localplan` nad `records/test/20260914-170945.rec` (jízda na Hviezdoslavově,
14 827 plánů):

| stav plánu | podíl |
|---|---|
| `Ok` | 42,9 % |
| `GoalBlocked` (mrkev v neprůjezdné buňce) | 24,2 % |
| `GoalUnsafe` (mrkev těsně u překážky) | 19,0 % |
| `Partial` | 13,9 % |

**Mrkev nedosažitelná (rozdíl > 0,30 m) v 52 % plánů**, `|požadovaný − dosažený cíl|` p50 0,366 m,
**p90 2,52 m**, max 7,74 m. Na časové ose to jde ruku v ruce s plazením: kde rozdíl skočil na
1,4–2,6 m, tam je příkazovaná rychlost 0,00–0,15 m/s a robot ujel **0,1–0,6 m za 10 s**; kde je
rozdíl 0,02 m, jede 1,0 m/s a ujede 8–9 m. K prvnímu bodu trasy tak robot jel 44 m **9,5 minuty**.

**Léčba.** Cíl je **zóna o poloměru `R`**, ne bod. Test cíle v `Search()` je
`Dist2Cells(...) ≤ R²` a A\* vrátí **první vytaženou** buňku zóny, tedy tu **nejlevnější na
dojetí podle svého vlastního kritéria (času)** — ne geometricky nejbližší. Ten rozdíl je podstatný:
geometricky nejbližší bod zóny může ležet **za** tou překážkou, kvůli které je střed nedosažitelný.
Při `R = 0` se test degeneruje přesně na původní `ci == iG && cj == jG`.

⚠️ **Past, která k tomu patří: heuristika se musí měřit k OKRAJI zóny**, tedy `max(0, d − R)`.
Kdyby se dál měřila ke *středu*, byla by `h > 0` i na cílových buňkách, pořadí vytahování z fronty
by přestalo odpovídat ceně a A\* by vracel dražší dosažitelný bod. Projevilo by se to jako *tiše
horší dráha*, ne jako chyba. Hlídá to `VraciNejlevnejsiBodZony_NeGeometrickyNejblizsi` — porovnává
cenu plánu do zóny proti nejlevnějšímu z plánů do jednotlivých bodů té zóny braných jako bod.

**Poloměr je vlastnost CÍLE, ne plánovače** (`Plan(..., goalRadiusM)`, `ILocalGoalSink.SetGoal(...)`):

- **průjezdní mrkev** — `carrotradius=` (výchozí **0 = bod**). Není to cíl, ale směr; zvětšit její
  zónu znamená pustit robota dál od trasy, což je jiná změna chování a **nemá změřenou potřebu**.
- **dojezd do cíle** — použije se **dojezdový poloměr** (`NavigatorOptions.ArrivalRadiusMeters`),
  protože dojet kamkoli do něj už znamená, že cíl byl dosažen. Platí to vždy a `carrotradius`
  to nevypíná.

⚠️ **Zóna mrkve je při dojezdu menší než zóna dojezdu, a to o dvě věci**: o `ArrivalZoneMarginM`
(výchozí 0,5 m — rezerva, aby robot nezastavoval přesně na hranici, kde o dosažení rozhoduje šum
EKF/GPS) a o **odstup mrkve od cíle** (z trojúhelníkové nerovnosti je pak každý přijatý bod zaručeně
i uvnitř zóny dojezdu). Bez toho by robot mohl zastavit uvnitř zóny mrkve, ale **vně** zóny dojezdu,
a `Arrived` by nenastalo **nikdy** — je to táž past, na kterou už narazil
[Track](track-mission.md) i [Robotour](robotour-mission.md) u nepřichycených bodů.

⚠️ **Zóna nesmí spolknout skutečnou poruchu:** když je neprůjezdná celá, zůstává `GoalBlocked`
(klasifikace se proto dělá přes **celou zónu**, ne podle středu). Jinak by mrkev ve zdi začala
vypadat jako dojezd.

⚠️ **Není to lék na špatnou mapu.** Když je mrkev nedosažitelná proto, že grid hlásí překážku, která
tam není (rozmazání chybou kurzu), zóna to **zakryje** místo opraví. V témže záznamu byl podíl
`Blocked` buněk p50 **27,7 %** (max 51,0 %) a kurz byl rozbitý, takže to není teoretická výhrada —
ukazatelem je právě to p90 = 2,52 m: kdyby šlo jen o mrkev na kraji cesty, minulo by se to
o decimetry. Stojí za to se na grid v těch okamžicích podívat (`ARBot.Analyze grid`).

⚠️ **Na HW to neběželo**; ověřeno buildem a testy (`LocalPlannerGoalZoneTests`, 10 testů,
+ 2 v `GlobalNavigatorTests`).

⚠️ Pozor, **oba dnešní běhy selhaly každý jinak**: běh z 17:06 skončil `NoRoute`, tedy globální
vrstva nenašla trasu po síti — na to zóna nesahá vůbec.

### Postprocessing → `RegulatorWayPoint[]`

1. Řetěz buněk → **string-pulling**: slučuj do úsečky, dokud podél ní platí `d ≥ SafeDist`,
   **dokud se pod obálku vejde předpovězená rampa a dokud sloučení nezhorší jízdní čas**
   (od 8. 9. 2026, `smooth=`; viz [léčbu výš](#-léčba-8-9-2026-vyhlazování-posuzuje-čas-a-ověřuje-rampu-smooth)).
   Samotné `d ≥ SafeDist` optimalizuje **délku**, kdežto A\* optimalizoval **čas** — a ten rozdíl
   zahazoval objížďku, kterou cena koupila.
2. Pro každý waypoint:
   - `Speed` = obálka **v tom uzlu** (do 8. 9. 2026 minimum přes obě sousední úsečky — viz léčbu
     výš; `smooth=passable` a únik si původní minimum drží),
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
  (i důvod selhání), min. odstup a doba výpočtu. **Od verze 2 (7. 9. 2026) navíc rozpad rychlostní
  obálky u každého waypointu** — `EnvClearanceM`, `EnvClosing`, `EnvFreeAheadM`, `EnvVClearance`,
  `EnvVBrake` (pět `float` na uzel, ~1,5 MB za 8 minut záznamu). Waypoint nese *výslednou* `Speed`;
  tohle je její **rozpad**, tedy proč je zrovna taková. **Po uzlech, ne jako minimum přes plán:**
  „leze už od sebe" a „za dva metry se cesta zužuje" jsou pro léčbu úplně jiné situace a jedno
  číslo je splácne — a právě tenhle rozdíl rozhodl nález z 7. 9. (níž). Minima přes mezilehlé uzly
  (`MinVClear`, `MinVBrake`, `MinFreeAheadM`, `MinWayPointSpeed`, `SpeedLimitedBy`) jsou dnes
  **dopočítané vlastnosti**, ne uložená data — jeden zdroj pravdy. Do verze 1 rozpad zůstával
  jen v `LocalPlanResult`, tedy v paměti běžící aplikace, a ze záznamu se dal pouze rekonstruovat.
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
- A/B nad reálným `.rec`, až bude záznam z namontovaných kamer;
- **rozpad obálky je po uzlech** a `Speed` uzlu se rovná `max(podlaha, min(VClearance, VBrake))`
  (`LocalPathPlannerTest.RozpadObalky_JePoUzlech_A_ZnaOdstupKazdehoUzlu`), a přežije záznam
  (`OccupancyMessagesTest.LocalPlanMsg_RozpadObalky_JePoUzlech_RoundTrip`).
- **vyhlazování je cenově poctivé** (8. 9. 2026): skvrna 2×2 buňky stranou od spojnice nesmí
  srazit rychlost u robota, objížďka, kterou cena A\* koupila, musí ve výsledku zůstat, pomalé
  místo na konci dráhy nesmí zdržet její začátek (`TestCase` proti `smooth=passable`), jízda kolmo
  k překážce se složí do rampy o pár uzlech, a hlavně **předpovězená rampa nikde nepřekročí
  obálku** (`Vyhlazovani_PredpovezenaRampa_NikdeNeprekrociObalku` — počítá odstup i přibližování
  nezávisle z pole vzdáleností; bez kontroly rampy v plánovači padá).

## Parametry

| parametr | hodnota | kde |
|---|---|---|
| `Resolution` | 0,05 m | `OccupancyGridConfig` |
| `N` | 256 (12,8 m) | `OccupancyGridConfig` |
| `l_occ` / `l_free` / clamp | +0,85 / −0,40 / ±5 | `OccupancyGridConfig` |
| `Scale` (krok fixed-pointu) | 0,05 | `OccupancyGridConfig` |
| prahy `BlockedThreshold` / `FreeThreshold` | +1,0 / −1,0 | `OccupancyGridConfig` |
| `RoadFullRangeM` / `RoadMaxRangeM` | 3,0 / 8,0 m | `OccupancyIntegratorConfig` |
| `WedgeFillDeg` / `wedgefill=` | 6,0° (0 = vypnuto) | `OccupancyIntegratorConfig` (klín mezi zornými poli barvy, 12. 9. 2026) |
| `WedgeFillRangeM` / `WedgeFillMaxGapM` / `WedgeFillConfidence` | 6,0 m / 0,6 m / 0,5 | `OccupancyIntegratorConfig` |
| `UnknownCostFactor` | 3,0 | `LocalPlannerConfig` |
| `Envelope` / `envelope=` | `Directional` | `LocalPlannerConfig` (model stropu z odstupu; `radial` = původní, 3. 9. 2026) |
| `EdgeMarginM` | 0,15 m | `LocalPlannerConfig` (šířka podélné rampy nad `SafeDist`, směrový model) |
| `FootprintRadiusM` | 0,3 m | `LocalPlannerConfig` (půdorys = sjízdné pro `s_free`, 3. 9. 2026) |
| hystereze úniku | půl buňky (`Resolution/2`) | `LocalPathPlanner.Plan` (odvozené, ne parametr; `EscapeRadius` zrušen 3. 9. 2026) |
| `HorizonM` | 6,0 m | `LocalPlannerConfig` |
| `GoalRadiusM` | 0 (cíl je bod) | `LocalPlannerConfig` — **výchozí** poloměr cílové zóny; per-cíl ho přebíjí `Plan(..., goalRadiusM)` (14. 9. 2026) |
| `CarrotRadiusM` / `carrotradius=` | 0 (bod) | `GlobalNavigatorConfig` — zóna **průjezdní** mrkve |
| `ArrivalZoneMarginM` | 0,5 m | `GlobalNavigatorConfig` — o kolik je zóna mrkve při dojezdu menší než zóna dojezdu |
| `MinCostSpeed` | 0,05 m/s | `LocalPlannerConfig` |
| `SafeDist` | 0,40 m | `Profile` (existuje) |
| `PrefDist` | 0,80 m | `Profile` (jen radiální režim) |
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

## Hrboly a klopení — co o nich říkají záznamy z Robotouru (22. 9. 2026)

Měřeno `ARBot.Analyze bumps` nad `records/Robotour2026/` (Kolo3b **799 m**, Kolo4 287 m,
Kolo3-návrat 125 m, Kolo3a 103 m, Kolo2 18 m; rychlost p50 0,93 m/s). Cílem bylo **rozhodnout před
psaním kódu**, jestli platí předpoklad, na kterém stojí oba záměry — `lp-drsnost-povrchu-rychlostni-strop`
i `lp-reflex-klopeni-zadni-kolo`: že *ráz přední nápravy ohlašuje klopení, které o rozvor později
způsobí zadní pasivní kolo*.

### ⚠️ Přes CELÝ záznam dvojice špiček není — ale průměr přes 799 m je špatná otázka

Nejdřív se měřilo přes celý záznam a vyšlo to záporně (tabulka níž). Autor pak upřesnil, **kdy**
se na trati zakoplo o kořeny pod asfaltem (Kolo3b, **14:12:20–14:13:00**), a v tom okně vypadají
data jinak. Obojí platí a neodporuje si: v těch 40 s je **22 m** jízdy z 799, tedy **2,8 %** —
několik skutečných zakopnutí se v celozáznamovém průměru utopí. Proto má měřidlo `--from=`/`--to=`
a `--detail=`; **napřed se ptej tam, kde člověk něco viděl.**

### Přes celý záznam: tři nezávislé testy, všechny záporné

| test | co by ukázal rozvor | co vyšlo (Kolo3b, 677 m jízdy) |
|---|---|---|
| autokorelace energie klopení **v dráze** | vrchol na rozvoru | jednotvárný pokles 0,29 → 0,03, **žádný vrchol**; na obvodu kola (0,508 m) `r` = 0,059, stejně jako u sousedů |
| **ozvěna po silném rázu** (průměr přes 521 nejsilnějších) | hrbolek na jednom místě | hladký pokles 14,8× → ~2×, mezi 0,15 a 2 m **nic** |
| párování špiček | vrchol v histogramu odstupů | 91,3 % spárovaných, ale **náhodou by jich vyšlo 93,7 %** — tedy *pod* náhodou; histogram od nejmenšího koše jen klesá |

Kolo4 to reprodukuje (90,9 % proti náhodným 89,8 %). Přes celý záznam přijde práh překračující ráz
jednou za **0,51 m**, tedy to na téhle úrovni není řada oddělených nárazů, ale souvislá vibrace.
Že jde o skutečný terén a ne o šum senzoru, je ověřené: `|gyroY|` je v jízdě **270×** větší než ve
stání.

### ⚠️ Ozvěna na rozvoru: ANI POTVRZENÁ, ANI VYLOUČENÁ — a jeden mezizávěr byl odvolán

Autor 22. 9. 2026 doplnil geometrii, kterou záznam nenese: zadní kolo je **volně otočná ostruha**
**mezi** hnanými koly, rozvor při přímé jízdě **~0,35 m**, ale při manévrování se mění. Ostruha
tedy nejede ve stopě předních kol — **jenže většina problémových defektů je příčná nebo tak velká,
že zasáhne všechna kola**. Tím vzniká ostrá předpověď: ozvěna se má objevit **na 0,35 m**, a to jen
u **příčných** defektů (malá odezva do strany) a při **přímé** jízdě. Měřidlo tuhle podmínku umí
(`--rozvor=`, `--straight=`, dělení podle odezvy do strany).

⚠️ **Odvolání.** Mezitímco stálo, že „v úseku s kořeny druhý vrchol JE, na 0,25–0,30 m". **Neobstálo
to.** Vrchol vyšel z **jednoho** okna (14:12:20–14:13:00) na **jedné** ose dráhy; jakmile se změnilo
okno nebo osa, zmizel:

| úsek | vrchol (odometrie) | hodnota **na 0,35 m** / pozadí | totéž pro *příčný defekt + rovně* |
|---|---|---|---|
| 14:11:10–14:12:00 (n = 11) | 1,15 m | 1,60 / 1,52 | 1,18 / 1,50 |
| 14:12:20–14:13:00 (n = 17) | 0,28 m | 2,74 / 0,78 | 0,80 / 0,92 |
| 14:15:30–14:16:20 (n = 10) | 1,70 m | 2,48 / 1,80 | 0,54 / 1,72 |
| 14:11:10–14:13:00 (n = 47) | 0,60 m | 1,76 / 1,81 | 1,42 / 1,86 |

Rozhodující je **poslední sloupec**: právě tam, kde má být ozvěna nejsilnější, není v **žádném**
z úseků nad pozadím. A vrchol putuje mezi 0,28 a 1,70 m, což je chování šumu, ne geometrie.

⚠️ **Proč to data nerozhodnou — a je to vlastnost úlohy, ne nedbalost měření:**
- **Čistých událostí je málo.** Po podmínce „příčný + rovně" zbývá v jednom úseku **6–12** událostí.
- **Osa dráhy je právě v okamžiku události nespolehlivá.** Odometrie se integruje z týchž kol, která
  přes hrbol šplhají a prokluzují; náhradní osa „rychlost *před* událostí × čas" zase nepočítá se
  skutečným zpomalením. Obě dávají systematicky jinou odpověď (0,28 proti 0,23 m) a **žádná z nich
  není pravda**. Proto se tisknou obě.
- **Rozvor sám není konstanta** — ostruha se vytáčí, takže se podélný odstup mezi událostmi mění
  a případná ozvěna se rozmaže přes interval, ne do jednoho koše.
- **Ostruha je lehce zatížená** (váha je na hnaných kolech), takže její ráz může být prostě mnohem
  slabší než ráz přední nápravy.

✅ **Co by to rozhodlo:** jeden **záměrný pokus** místo dolování ze závodních dat — přejet
**jednu známou příčnou překážku** (lať, práh) několikrát rovně a při dvou zřetelně různých
rychlostech (např. 0,4 a 1,1 m/s). Události jsou pak opakované, se známou geometrií a s poměrem
rychlostí 2,75, takže se ozvěna (pevná vzdálenost) od kmitu karoserie (pevný čas) oddělí
jednoznačně — dnešní poměr 1,38 rozlišil předpovědi jen o 0,09 m. Je to práce na deset minut
u robota a nahradí libovolné množství dalšího měření nad Robotourem.

### ⚠️ Klopení ale malé NENÍ — a první verze tohohle dokumentu to tvrdila špatně

Zde stálo, že největší výchylky úhlu (18,06°, 10,72°, 9,69°) jsou artefakty, protože je gyro
nepotvrzuje (0,82°, 0,00°, 1,76°). **Bylo to obráceně: vadná byla kontrolní veličina.**
`GyroNoseDown` sčítala rychlost klopení přes celé okno, jenže zakopnutí je **kmitavé** — fáze nosem
nahoru a nosem dolů se v součtu vyrušily. Po opravě (integrál se při změně smyslu nuluje, tedy měří
největší *souvislou* rotaci dopředu) gyro tytéž události potvrzuje: **14,09°**, 2,92°, **14,35°**,
a celozáznamové maximum je **19,32°**.

Skutečné hodnoty nad Kolem 3b: klopení dopředu p50 **1,4°**, p90 3,0°, **max 18,06°**; rozkmit
vrchol-vrchol p90 5,1° a **max 19,78°**. Zakopnutí 14:12:52 je typické: robot se předními koly
vyhoupne na **+9,7°** (nos nahoru), za 0,75 s se sklopí na **−9,9°** a pak se vrátí rychlostí
−95 °/s; rychlost kol přitom kolísá 0,30–0,86 m/s, jak kolo šplhá a padá. To je **skutečné
zakopnutí, ne šum** — a odpovídá tomu, co autor na trati viděl.

Proto se klopení měří oběma cestami: VN100 sráží rychlost klopení na **0,69×** (směrnice
0,689 / 0,692 / 0,693 ve třech záznamech), tedy filtrovaný úhel krátký ráz spíš podhodnocuje.

### ✅ Co v datech JE: drsnost je vlastnost ÚSEKU, ne bodu

Nad Kolem 3b je nejhorší pětimetrové okno **2,5× drsnější než medián** a podobnost drsnosti dvou
míst klesá s jejich vzdáleností pomalu: `r` = **0,80** na 5 m, 0,65 na 10 m, 0,54 na 15 m, 0,44 na
20 m, 0,33 na 25 m, 0,19 na 30 m — dekorelační délka řádu **20 m**.

To **přeformulovává go/no-go** zapsané u `lp-drsnost-povrchu-rychlostni-strop` („korelace
drsnost → náklon jen do ~1 m znamená, že robot nestihne brzdit"). Ta otázka předpokládá, že se musí
předpovědět *tenhle* kořen z kamery na 1,5 m, kde šum hloubky (4,6 cm ve 3 m) sotva stačí. Když ale
drsnost drží přes desítky metrů, stačí poznat, že **tenhle úsek je hrbolatý**, a strop postavit
z toho, po čem robot **už projel** — z IMU, bez kamery, a problém s dosahem hloubky zmizí.

⚠️ **Než se z toho udělá kanál mapy, musí se drsnost normalizovat na jednotku dráhy.** Dnešní
měřidlo (RMS rychlosti klopení v okně) koreluje s rychlostí okna `r` = 0,134 nad Kolem 3b (kde se
jelo skoro konstantně), ale **0,822 nad Kolem 4** (kde se rychlost měnila 0,29–1,02 m/s). Bez
normalizace by mapa drsnosti byla zčásti mapou rychlosti a strop by se honil za vlastním ocasem:
zpomal → vypadá hladce → zrychli. Táž kruhovost jako u `camerapose=fusion`.

### ⚠️ Byla nerovnost vidět v hloubkové kameře? Na 1–3 m NE

Tahle otázka se v projektu dvakrát odbyla jako nezodpověditelná ze záznamu („polární grid svou
drsnost `StdZ` do záznamu neposílá, chtělo by to replay snímků"). **Byl to omyl:** grid se ukládá
**uvnitř `CameraFrame`** (`CameraFrame.Grid`, FormatVersion 2, od 1. 8. 2026) i s `StdZ`, `MeanZ`,
`MaxZ` a třídou buňky. Nemuselo se přehrávat nic, stačilo se podívat.

**Jak se to páruje.** Hrbol je v okamžiku, kdy na něj najede přední náprava, **pod počátkem
tělesového rámce**. O Δs metrů dřív tedy ležel Δs **před** robotem, takže se v gridu z dřívějšího
snímku hledá pás kolem `x = Δs`. ⚠️ Pózu to nepoužívá **schválně** — v Kole 3b skákala korekcemi
až o 4 m, kdežto ujetá dráha za pár sekund je spolehlivá. V pásu se bere **nejhorší** buňka
(|y| ≤ 0,30 m, tedy stopa kol na ±0,205 m), protože kolo najede na to nejvyšší, co mu leží v cestě,
ne na průměr; výška se měří proti **mediánu téhož prstence v témže snímku** (absolutní `MeanZ` by
měřilo hlavně sklon terénu a montáž kamery).

Nad oknem 14:12:20–14:13:00 (1 025 snímků s gridem, 17 událostí, 2 160 nahlédnutí) proti
**kontrolní skupině** obyčejných míst (5 677 nahlédnutí):

| jak daleko to ještě bylo | `StdZ` p90 hrbol / kontrola | nejvyšší bod p90 hrbol / kontrola |
|---|---|---|
| 0,5–1,0 m | 0,44 / 0,46 cm (**0,95×**) | 4,53 / 4,05 cm (1,12×) |
| 1,0–1,5 m | 0,48 / 0,49 cm (**0,98×**) | 5,68 / 4,64 cm (1,22×) |
| 1,5–2,0 m | 0,62 / 0,67 cm (**0,91×**) | 8,98 / 7,47 cm (1,20×) |
| 2,0–2,5 m | 1,36 / 0,81 cm (1,68×) | 7,95 / 8,56 cm (0,93×) |
| 2,5–3,0 m | 0,93 / 0,91 cm (**1,02×**) | 9,04 / 8,24 cm (1,10×) |

**Na vzdálenostech, kde by se dalo zpomalit, se místo hrbolu od obyčejné vozovky nepozná.** Poměry
kolísají kolem 1,0 bez trendu a ukazatele si navzájem odporují (na 2,0–2,5 m je `StdZ` 1,68×, ale
nejvyšší bod 0,93×). Totéž pro podíl buněk označených `Obstacle`: 2,6 / 2,5 / 9,0 / 8,4 / 5,2 %
proti kontrole 8,6 / 4,4 / 4,9 / 1,2 / 1,1 % — v nejbližším koši je kontrola **vyšší**.
⚠️ Koš 3,5–4,0 m (`StdZ` 10,6×, nejvyšší bod 91 cm) se **nesmí číst jako „daleko je to vidět"** —
v pásu jsou na té vzdálenosti skutečné překážky (zeleň, zeď, lidé), tedy něco úplně jiného než
kořen.

⚠️ **Je v tom ale rozpor, který tohle měření nevysvětlí.** Z náklonu plyne, že přední kola stoupla
o **~6 cm** (9,7° při rozvoru 0,35 m), a to během ~0,13 m dráhy — takový útvar by v hloubce vidět
být **měl**. Buď je prostorové párování hrubší, než se zdá (chyba dráhy, boční posun, velikost
buňky ~7 × 9 cm ve 2 m), nebo `StdZ` takový útvar nezachytí, protože leží celý uvnitř jedné buňky
a proložení roviny ho pohltí. Rozhodnout to jde **jen pokusem se známou překážkou** — tam se ví,
kde překážka je, a pairing přestane být zdrojem pochybností.

### ⚠️ Dvě vady měřidla, obě nalezené až daty (a obě by tiše lhaly)

1. **Vyrušení kmitu v kontrolní veličině** — popsáno výš; vedlo k závěru „velké náklony jsou
   artefakty", tedy k **opačnému** tvrzení, než jaké data nesou. Poučení: když se dvě měřidla téže
   věci rozejdou o řád, podezřelé je to **kontrolní**, ne měřené — zvlášť když je měřené jednodušší.
2. **Konvence určená z okna místo z celého záznamu** — na 40s okně je klidových vzorků pár set,
   osy se v bloku 1 nerozliší a výběr padne na `ypr.Roll` a gyro X místo `ypr.Pitch` a gyro Y.
   Varování se vytisklo, ale zbytek reportu už počítal ze špatné osy. Montáž senzoru se během
   jízdy nemění, takže se konvence určuje **vždy z celého záznamu** a teprve pak se ořezává okno.

### Vedlejší nález: konvence klopení jsou v pořádku, ale ověřit se musely

`YawPitchRoll(q, Euler.zxy)` ukládá do pole `Pitch` úhel **prostřední** rotace, tedy kolem osy X —
a při tělesovém rámci FLU je rotace kolem X náklon do strany. Podle algebry by tedy jména polí
neodpovídala fyzice. **Rozhodla data, ne úvaha:** proti náklonu z gravitace vychází
`ypr.Pitch` ↔ klopení dopředu `r` = **0,971** (do strany −0,434) a `ypr.Roll` ↔ náklon do strany
`r` = **0,987** (dopředu −0,476). Jména tedy sedí, klopení je `ypr.Pitch` a jeho rychlost `gyroY`
(`r` = −0,92 proti ose X 0,04). Blok 1 měřidla tenhle test dělá při každém běhu.
⚠️ **Samotné „robot stojí" na tenhle test nestačí:** v depu s robotem hýbe obsluha a akcelerometr
to vidí — bez sekundového vyhlazení vyšel náklon z gravitace `sd` 5,6° proti 1,3° z atitudy
a korelace spadla na 0,12.

## Otevřené úkoly (→ registr)

Stav a data vede [registr úkolů](ukoly.md); tady je jen seznam, co se téhle oblasti týká.

- **[Okluzní pravidlo zahazuje většinu barevných vzorků](ukoly.md#vid-inshadow-zahazuje-vzorky)** —
  není `InShadow` příliš přísné? V měření nad virtuálním HW zahodilo **~5 200 z ~12 000** kandidátů
  na barevný vzorek — tedy většinu. Důsledek: semantický kanál dostane řádově míň dat než
  geometrický (`road ≈ 3 800` vs. `occ ≈ 8 600` zápisů na snímek) a plocha mimo cestu se potvrzuje
  pomalu. Záměr pravidla je správný (za první překážkou v daném azimutu patří barva té překážce, ne
  zemi za ní), ale stíní se **celý zbytek paprsku**, včetně míst, kam kamera zjevně vidí.
  K rozmyšlení: stínit jen do určité vzdálenosti za překážkou, nebo podle její výšky, případně
  vzorek jen zeslabit (nižší confidence) místo úplného zahození. Měřicí nástroj je
  `VirtualHwOccupancyTest.Diagnostika_PricnyProfilSemantiky` (pole `ColorShadowed`
  v `OccupancyIntegrator.IntegrateStats`).
- (bez tématu v registru) **Dvě EDT místo jedné** (zvlášť překážky, zvlášť okraj cesty), kdyby bylo potřeba v nouzi
  vyjet z cesty. Zatím schválně jedna společná maska — rozdělí se, až se ukáže, že to chybí.
- (bez tématu v registru) **`MaxZ` per buňka** (2,5D) pro převisy a podjezdy.
- **[Obtížně sjízdný povrch (hrbol, prasklina) jako rychlostní strop v lokální mapě](ukoly.md#lp-drsnost-povrchu-rychlostni-strop)** —
  třetí kanál gridu s drsností (polární grid ji měří, kartézský zahazuje) a strop `VSurface` v obálce,
  **plochý** od `hrbol − brzdná dráha` po `hrbol + rozvor` (klopí zadní pasivní kolo, ne přední náprava;
  brzdění na hrbolu klopení zhoršuje); nejdřív měření ze záznamů, aby třetí strop nebrzdil všude jako
  `VAlong` 7. 9. Doplňuje ho reflex z IMU nezávislý na mapě (`lp-reflex-klopeni-zadni-kolo`).
- (bez tématu v registru) **Kapslový footprint** místo opsané kružnice, pokud bude opsaná kružnice moc konzervativní.
- **[Korelace occupancy gridu s mapou jako oprava polohy a kurzu](ukoly.md#lok-korelace-gridu-s-mapou)** —
  druhý algoritmus nad gridem, kvůli kterému je grid world-kotvený a má oddělený kanál `LRoad`:
  porovnávat okraje cesty z RGB proti šířkám cest z OSM (`OsmNav.Graph.Node.Width`) je silnější
  signál než geometrické překážky; podrobně v [map-correlation-localization.md](map-correlation-localization.md).
- **[Režim Simulate — věrný přepočet běhu nad záznamem](ukoly.md#nast-rezim-simulate)** —
  až vznikne, `LocalNavigator` poběží
  nad záznamem beze změny (proto projekce v rámci).
- **[Výkon řetězu hloubka → grid → EDT → A\* na ARM není změřený](ukoly.md#lp-vykon-retezu-na-arm)** —
  změřit celý řetěz (integrace + EDT + A\*) na OrangePI.
- **[Robot se venku plazil rychlostí 0,05 m/s — může za to vyhlazování dráhy](ukoly.md#lp-robot-se-plazi-vyhlazovani)** —
  přeměřit obálku po opravě kurzu: pustit `ARBot.Analyze envelope` na nový záznam, tentokrát už
  s rozpadem **ve zprávě** (`LocalPlanMsg` verze 2), takže se nic nerekonstruuje; otázka je, kolik
  z odstupu 0,40 m byl špatný kurz a kolik zůstane.
- **[Izolované skvrny `Blocked` do 4 buněk brzdí robota jako zeď](ukoly.md#lp-filtr-izolovanych-bunek)** —
  ve 41,5 % plánů dává odstup skvrna do 4 buněk (0,01 m²); **neopravovat naslepo** — dokud je kurz
  vedle, není jasné, jestli je to šum klasifikace, nebo rozmazání gridu chybou pózy, a morfologický
  filtr by ve druhém případě jen zamaskoval příčinu; rozhodne přeměření výš.
- (bez tématu v registru) **Má být `SafeDist` zároveň nulovým bodem rampy?** Dnes v těsném místě dostane nejlepší legální
  dráha nulový podélný strop, takže robot leze 0,05 m/s (14,1 % plánů má odstup *právě* `SafeDist`).
  Rozumné alternativy: nulový bod rampy o půl buňky pod `SafeDist`, nebo podlaha `MinCostSpeed`
  vyšší v místě, kde je odstup přesně na hranici a `closing` je nulové (jízda **podél**). Obojí ale
  slevuje z bezpečnosti, takže **až po přeměření** — může se ukázat, že po opravě kurzu robot
  u okraje vůbec nejezdí.
- **[Robot se venku plazil rychlostí 0,05 m/s — může za to vyhlazování dráhy](ukoly.md#lp-robot-se-plazi-vyhlazovani)** —
  doladit vyhlazování na datech ze zařízení: (a) **počet uzlů** — na realistické scéně 5 → 19; víc
  uzlů zdraží `PathPlanner` a nafoukne `LocalPlanMsg`, změřit na Pi; (b) předpoklad „plánovač brzdí
  konzervativněji než regulátor" (`MaxDecceleration` 0,5 proti 1,0 v profilu) — drží, ale je to
  vazba mezi dvěma konfiguracemi, která se nikde nekontroluje; (c) jestli po opravě kurzu vůbec
  zbude co léčit; (d) dvě alternativy, které se **nedělaly**, protože by se míchaly do jednoho
  rozhodnutí: dráhu po vyhlazení **odtlačit** gradientem vzdálenostního pole a rozšířit plochou
  část obálky (`EdgeMarginM`), aby A\* mělo vůbec důvod jet středem. Pořadí zůstává **nejdřív
  kurz**: zisk se má měřit nad záznamem se správným kurzem.
