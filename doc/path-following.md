# Sledování dráhy (path-following regulátor)

Obecný regulátor, který robota provede **dráhou složenou z waypointů** tak, aby každý
uzel projel v rámci předepsané tolerance (`RegulatorWayPoint.MaxPositionError`, dále `ε`)
**maximální možnou rychlostí** (uzly se neprojíždějí zastavením).

**Sjednocené rozhraní `IRegulator`** (`Control(IModelState) → RegulatorResult` + `IsFinished`): cíl
(jeden bod nebo celá dráha) drží regulátor uvnitř. Dvě implementace — **`PointRegulator`** (dojezd na
jeden bod; nahradil původní `Regulator` i `SimplRegulator`, jediný rozdíl byl `IMotionProfile`) a
**`PathResult`** (sledování dráhy). Nižší smyčka (`ControlLoop.Regulator`) je používá transparentně.

Doménová dokumentace navazuje na:
- `Src/ARBot.Common/Regulators` — `IRegulator` (`PointRegulator`, `PathResult`) + `IMotionProfile`
  (`TrapezoidMotionProfile`, `SqrtMotionProfile`).
- [imu-and-frames.md](imu-and-frames.md) — souřadnice: world **ENU** + matematická orientace
  (0 = východ, +CCW). Dráha, geometrie i orientace robota jsou v této konvenci.
- [ekf-fusion.md](ekf-fusion.md) — `IModelState` (póza + rychlosti) pochází z EKF.

---

## Architektura

Dvě oddělené vrstvy — **plánování** (co nejvíc předpočítat) a **exekuce** (živě z reálné pózy):

```
IPathPlanner.Plan(RegulatorWayPoint[])  ->  IRegulator              (jednorázově pro danou dráhu)
IRegulator.Control(IModelState)         ->  RegulatorResult         (každý tik řídicí smyčky)
```

- **`Plan`** z waypointů předpočítá geometrii rohů a **brzdnou obálku** rychlosti a vrátí
  `IRegulator` (konkrétně `PathResult`).
- **`Control`** každý tik lokalizuje robota na trase a z **aktuální pózy** (`IModelState`)
  spočítá řídicí zásah.

Divergence (drift, prokluz, chyba odhadu) se řeší na dvou úrovních:
1. **EKF** produkuje `IModelState` fúzí měření (poloha, rychlosti).
2. **Nižší řídicí smyčka** (`ControlLoop`) jede aktuální `IRegulator` (`ControlLoop.Regulator`); když delší dobu
   nedostane novou dráhu (`Profile.PathControlTimeOut`), **nouzově dobrzdí po poslední známé
   trase**. Vyšší smyčka (lokální mapa + OSM) určuje kudy jet, aplikuje `Plan` a výsledek
   atomicky předá nižší smyčce.

### Proč feedforward + přeplánování, ne proporcionální řízení (pure-pursuit `ω = v·κ`)
Statické proporcionální řízení na příčnou/směrovou odchylku ignoruje dynamiku (rychlostní
limit `ω`, `Ts = 100 ms`, zpoždění EKF) a v tomto setupu vede na **kmitání**. Místo toho
`Control` každý tik **přeplánuje manévr z reálné pózy** a zásah generuje přes `IMotionProfile`
(accel-limitovaná rampa, časově-optimální) — uzavřená smyčka do plánu přes dynamický profil,
nikoli přes gain. Bodová mechanika (natočení nosu + vazba dopředné rychlosti přes
`RegulationTime`) se recykluje z původního regulátoru; „zastav a otoč se" mid-path nezasáhne,
protože směrová odchylka je malá.

---

## Plánování — brzdná obálka + geometrie rohů

`Plan` počítá **jen zpětný průchod** (obálku omezení plynoucích z budoucnosti). Dopředný
(akcelerační) průchod se **nepočítá** — akceleraci řeší runtime živě, protože skutečnou
počáteční rychlost `Control` bere z `IModelState.Velocity`.

### Geometrie rohu (kruhový oblouk)
Ve vnitřním uzlu s úhlem zatáčky `θ` (změna směru mezi vstupním a výstupním úsekem) a tolerancí
`ε` se roh prokládá **kruhovým obloukem** o poloměru:

```
R = ε · cos(θ/2) / (1 − cos(θ/2))
```

- Tečná délka rohu `R·tan(θ/2)` se **oseká** tak, aby nepřesáhla ½ kratšího sousedního úseku
  (rohy se nesmí překrývat) — jinak se `R` zmenší (a v limitu vynutí zpomalení/zastavení).
- `θ → 0` (rovný průjezd): žádné omezení. `θ → π` (otočka): `R → 0` → zastavení a otočka.

#### Volba oblouk vs. klotoida
Robot reálně jede klotoidu (křivost náběhá, protože `ω` je accel-limitovaná), plánujeme ale
obloukem, protože je to uzavřená forma a chyba je zanedbatelná. Rozhoduje **úhel projetý během
náběhu rotace**:

```
φ_rampa = ω_max² / (2α) ≈ 0,52² / (2·0,98) ≈ 0,14 rad ≈ 8°
```

(hodnoty z `Profile`: `ω_max = π/6`, `α = a/(rozchod/2) = 0,20/0,205 ≈ 0,98 rad/s²`.)
Náběh 8° proti běžné zatáčce 30–90° je malý ⇒ oblouk je dobrá aproximace. Klotoidní posun
`Δ ≈ L_c²/(24R)`, `L_c = v·(ω_max/α)`, dává pro `ε = 0,1 m`:

| θ | R [m] | v [m/s] | Δ (chyba) |
|---|---|---|---|
| 90° | 0,24 | 0,13 (ω-limit) | 0,8 mm |
| 60° | 0,65 | 0,34 | 2,1 mm |
| ~40° | 1,53 | 0,80 (v_max) | **~5,0 mm** ← nejhorší |
| 30° | 2,83 | 0,80 (v_max) | 2,7 mm |

Nejhorší ~5 mm proti `ε = 100 mm` (< 5 %), navíc přechodová a hluboko pod nejistotou EKF
(centimetry). Řešíme malou **rezervou na `ε`** (viz níže), ne přesnou klotoidou.

### Vrcholové stropy rychlosti a zpětný průchod
V každém uzlu strop `v_uzel = min(v_max, ω_max·R, waypoint.Speed)`. Přes celou dráhu pak
**zpětný průchod** s `MaxDecceleration`:

```
v_vstup(úsek) = min(v_uzel, √(v_výstup² + 2·d·L))       // od konce, d = decelerace, L = délka úseku
```

Výsledkem je **brzdná obálka `v_strop(s)`** = *„nejvyšší rychlost v místě `s` na trase, ze
které ještě stihnu splnit vše budoucí (rohy, koncové zastavení)"*. Příklad nutnosti: úsek 2 m
následovaný úsekem 10 cm s koncem v 0 — do druhého úseku nelze vletět naplno, vstupní rychlost
`≤ √(2·d·0,10)`, a strop se propaguje zpět na první úsek.

---

## Exekuce — `Control(IModelState)` každý tik

Zásah = dvě nezávisle počítaná čísla: **dopredná rychlost** a **rotační rychlost `ω`**.

1. **Lokalizace** na trase: arc-length `s` lokálním hledáním kolem posledního `s`
   (`PathResult` drží malý mutable stav progresu). Nová dráha ⇒ globální re-lokalizace.
2. **Dopredná rychlost** — jeden dotaz na profil, míří na **nejbližší uzel před robotem**:
   ```
   startSpeed = IModelState.Velocity          // skutečná rychlost z EKF (živá akcelerace i brzdění)
   endSpeed   = VLimit[další uzel]             // strop z brzdné obálky v tom uzlu
   dist       = vzdálenost robota k dalšímu uzlu
   v_zásah    = Dist2Speed(dist, startSpeed, endSpeed)
   ```
   `Dist2Speed` sám zrychluje (je-li místo) i brzdí (blíží-li se pomalejší uzel). Stačí **jen
   nejbližší uzel**, protože `VLimit[další]` už přes zpětný průchod folduje veškerou budoucnost
   (z něj se dá ubrzdit na `VLimit[další+1]` atd.) — indukcí je to bezpečné.

   **Plus strop uzlu, ze kterého se právě odjíždí (od 3. 9. 2026):** `v_zásah ≤ WayPoints[seg].Speed`,
   je-li zadán (`Speed > 0`). Indukce výše totiž pokrývá jen podmínky *při průjezdu* uzly před robotem;
   strop **podél úseku** (boční odstup od překážek, který `LocalPathPlanner` ukládá do uzlu, z něhož
   úsek vychází) se z ní nedostane. Projevilo se to naplno u dvoubodové dráhy robot → mrkev, kde má
   poslední uzel `Speed = 0` (zastavení): odstupový strop prvního úseku žil jen v uzlu 0, ten smyčka
   nikdy nečetla, a robot jel **0,86 m/s při stropu 0,30** (záznam `20260903-132131`). Bere se
   **vlastní strop waypointu, ne `VLimit[seg]`** — `VLimit` nese i strop z geometrie rohu (u otočky
   nula), a ten je podmínka při průjezdu uzlem, ne podél úseku za ním; jinak by robot po otočce už
   nevyjel. `Speed = 0` zůstává „bez stropu", takže producenti drah bez stropu nic nepoznají.
   Testy: `PathControllerTests.StropStartovnihoUzlu_*`, `StropUzluPlatiPodelCelehoUseku_*`,
   `BezStropuNaStartu_*`. Viz [devlog.md](devlog.md), 3. 9. 2026.
3. **Rotační rychlost** — z **lookahead bodu** ve vzdálenosti `L_d = τ_look·v` na trase: úhel
   k němu = směrová odchylka → `Rot2RotSpeed` → `ω`. Lookahead slouží **jen k řízení směru**.
4. **Vazba** — `SpeedLimit` srazí dopřednou rychlost, je-li směrová odchylka velká (robot se
   nejdřív natočí); při odchylce > π/2 dopredná = 0 (otočka na místě).

---

## Analýza odchylky od trasy vs. vzdálenost cílového bodu `L_d`

`L_d` (vzdálenost cílového bodu = „lookahead") je jediný ladicí parametr sledování. Táhnou ho
dva protichůdné efekty:

**Efekt A — seříznutí zatáčky (ustálený, geometrický).** Aim-ahead trajektorie je rovnější než
oblouk trasy `R`; odchylka ≈ sagitta tětivy délky `L_d`:

```
e_A ≈ L_d² / (8R)
```

Roste s `L_d²`, horší v ostrých rozích. **Směr: robot se od waypointu vzdaluje** (zatáčí dřív,
projede rovněji) ⇒ ukusuje z tolerance — proto rezerva na `ε`.

**Efekt B — stabilita (přechodový, dynamický).** Malé `L_d` = vysoký efektivní gain ⇒ při
`Ts = 100 ms`, rychlostním limitu `ω` a zpoždění EKF hrozí kmitání. Spodní mez
`L_d ≳ (2–3)·v·Ts`.

### Řešení: `L_d` úměrné rychlosti
`v` klesá právě tam, kde je `R` malé (ostré rohy jedeme pomalu), takže **`L_d = τ_look·v`**
srovná obě meze naráz:
- Stabilita: `L_d/(v·Ts) = τ_look/Ts` — nezávislé na rychlosti. Pro `τ_look = 3·Ts = 0,3 s`
  je lookahead vždy 3 řídicí kroky.
- Seříznutí: v ostrém rohu je `v` (a tím `L_d`) malé ⇒ `e_A` malé.

Pro `τ_look = 0,3 s` (reálné `Profile`, `ε = 0,1`):

| θ | R [m] | v [m/s] | L_d [m] | e_A |
|---|---|---|---|---|
| 90° | 0,24 | 0,13 (ω-limit) | 0,038 | 0,8 mm |
| 60° | 0,65 | 0,34 | 0,102 | 2,0 mm |
| ~40° | 1,53 | 0,80 (v_max) | 0,240 | **4,7 mm** ← nejhorší |
| 30° | 2,83 | 0,80 (v_max) | 0,240 | 2,5 mm |
| rovinka | ∞ | 0,80 | 0,240 | 0 |

**Odchylka ≈ 1–5 mm (1–5 % z `ε`) v celém rozsahu rychlostí.** Nejhůř kolem θ≈40°, kde `v`
dosedne na `v_max` — stejný režim jako nejhorší případ oblouk-vs-klotoida (oba efekty míří
stejným směrem, robot dál od waypointu, a sčítají se).

**Přechod (návrat na trasu):** po vychýlení `e₀` návrat přes ~`2–3·L_d`; pro `τ_look ≥ 0,3 s`
přetlumeno (bez překmitu). Při `v_max`: `L_d = 0,24 m` → návrat ~0,5–0,7 m.

### Důsledky pro implementaci
- **Rezerva na `ε`:** `oblouk (~5 mm) + seříznutí e_A (~1–5 mm)` ⇒ rezervovat **~1 cm** z `ε`,
  tj. plánovat fillety na `δ = ε − ~10 mm` (konzervativně).
- **Zákon lookahead:** `L_d = τ_look·v`, `τ_look ≈ 3·Ts`, se spodním floorem (~`2·v·Ts`, aby
  v klidu `L_d` nespadlo na 0). `τ_look` v `Profile`.
- **Validace** (record/replay + selftest): měřit max příčnou odchylku (pod tabulku), vzdálenost
  průjezdu waypointem (≤ `ε`), počet změn znaménka `ω` (detektor kmitání). Sweep
  `τ_look ∈ {0,2; 0,3; 0,5 s}`.

---

## Plán realizace (fáze)

1. **Narovnání `Control` na jeden waypoint** + XML dokumentace dnešního chování; zrušit
   `MaxWayPoints`/pole. Obě staré implementace zůstávají, testy zelené.
2. **`IMotionProfile`** — vytáhnout kinematiku (`Speed2Dist`, `Dist2Speed`, `Rot2RotSpeed`,
   `SpeedLimit`); `TrapezoidMotionProfile` s paritou vůči `Regulator`.
3. **`IPathPlanner.Plan` + `PathResult`** — geometrie rohů + zpětná brzdná obálka; unit testy
   vč. příkladu 2 m + 10 cm.
4. **`PathResult.Control`** — lokalizace + lookahead + zásah; simulační testy (v `ε`, konec ~0,
   nekmitání).
5. **Integrace** `ControlLoop` (volatile `IRegulator` v `ControlLoop.Regulator`, atomická výměna, watchdog
   `PathControlTimeOut`) + `Profile`; record/replay + selftest.
6. **Dokumentace + DevLog** (tento dokument, `decisions.md`, odkaz z `CLAUDE.md`).

## Převod ω → `dif` a otevřený úkol: ověřit znaménko rotace na HW

[`ControlLoop.OnTick`](../Src/ARBot.Common/Runtime/ControlLoop.cs) předává výstup regulátoru motorům
jako `motor.Drive(forvard, dif)`, kde

```
dif = rotationSpeed * Rozchod / 2        // rotationSpeed [rad/s], matematicky (+CCW)
```

**Půlka tam patří**, protože `dif` je **offset na kolo**, ne rozdíl rychlostí kol: driver ho k jednomu
kolu přičte a od druhého odečte, takže `vR − vL = ω·Rozchod = 2·dif`. Shodují se na tom tři nezávislé
zdroje — předchozí generace robotu (`Drive(ReqSpeed, ReqRotationSpeed * Rozchod / 2)`),
[`TrapezoidMotionProfile`](../Src/ARBot.Common/Regulators/TrapezoidMotionProfile.cs) (používá
`rozchod2 = rozchod/2` jako rameno pro převod ω ↔ rychlost kola) a MicroBasic skript v
[`SDC2160Ex`](../Src/ARBot.HAL/Devices/MotorDriver/SDC2160Ex.cs)
(`motor1 = −(curSpeed+curRotSpeed)`, `motor2 = curSpeed−curRotSpeed`). Bez půlky robot zatáčel
**dvakrát rychleji, než regulátor chtěl** — opraveno 2026-08-12, hlídá test
`RotationSpeed_ToDif_IsHalfWheelBase`. `ControlLoop` je jediné místo v repu, kde se ω na `dif` převádí.

### ✅ Vyřešeno 2026-08-14: rychlost uzamčená vazbou na dobu rotace

**Robot jel ~0,1 m/s, i když plán i `PathPlanner` povolovaly 1,2 m/s.** Změřeno v běžící aplikaci
(diagnostika `LocalNavigator` → Debug output); occupancy grid i obě plánovací vrstvy jsou přitom
v pořádku a nic nesrážejí:

```
plan:      v=1,20 m/s  VClear=1,20 VBrake=1,20 freeAhead=5,3 m
draha:     vLimit[0]=1,20 m/s  delka=6,0 m  nejostrejsiRoh=0°
regulator: vCmd=0,95 -> v=0,05 m/s  beta=-11,9° Trot=0,786 s lookahead=0,15 m
```

Rychlost sráží [`TrapezoidMotionProfile.SpeedLimit`](../Src/ARBot.Common/Regulators/TrapezoidMotionProfile.cs):

```
v ≤ d / (stability · T_rot)          stability = 4
d = max(LookaheadMin, LookaheadTime · v) = max(0,15; 0,3·v)
```

Dosazeno: `0,15 / (4 · 0,786) = 0,048 m/s` — sedí na setinu.

**Proč je to západka:** `d` se počítá z **aktuální** rychlosti, takže omezovač závisí na vlastním
výstupu. Dokud je robot pomalý, `d` leží na podlaze `LookaheadMin` = 0,15 m a strop zůstává nízký —
soustava se z toho stavu sama nedostane. Aby při `T_rot = 0,786 s` vyšlo 1,2 m/s, musel by být
lookahead **3,8 m**.

Druhý faktor: `MaxAllowedRotationSpeed = π/6`, tedy jen **30°/s** — proto trvá dorovnání pouhých
12° celých 0,79 s.

Odhad dopadu jednotlivých zásahů (samostatně, při `T_rot` = 0,79 s):

| změna | výsledný strop |
|---|---|
| `stability` 4 → 1 | 0,19 m/s |
| lookahead z **řízené** rychlosti místo měřené (rozbije západku) | 0,09 m/s |
| obojí | 0,36 m/s |
| + `MaxAllowedRotationSpeed` π/6 → π/2 (`T_rot` klesne ~3×) | ~1,1 m/s |

#### Oprava: cíl řízení je UZEL DRÁHY, ne virtuální bod

Robot se natáčí na **nejbližší uzel dráhy před sebou** a do **téže** vzdálenosti se váže dopředná
rychlost. Směr i vzdálenost tedy pocházejí z jednoho bodu — jsou to dvě strany téhož: *mířím tam
a tam se musím stihnout natočit*.

Dřív se mířilo na **virtuální bod na ideální trase** ve vzdálenosti `L_d = max(LookaheadMin,
LookaheadTime·v)`. Ten sice drží menší boční odchylku, ale:

- `L_d` se počítá z **aktuální** rychlosti → omezovač závisel na vlastním výstupu → **západka**
  (nízká rychlost → `L_d` na podlaze 0,15 m → nízký strop → nízká rychlost);
- a hlavně: zkrácením dohledu se uměle blokuje rychlost, i když geometrie dráhy nic takového nežádá.

**Přesnost průjezdu se tedy řídí hustotou waypointů** (a jejich `MaxPositionError`), ne umělým
zkrácením dohledu. Kde je potřeba projet přesně, nasází lokální plánovač uzly blízko sebe — a protože
se dráha přeplánovává každý snímek z *aktuální* pózy robota, boční odchylka se průběžně vynuluje sama.

**Uzel blíž než `L_d` se přeskakuje.** Bez toho by to nefungovalo ze dvou důvodů: směr k bodu, na
kterém robot prakticky stojí, je špatně podmíněný (azimut poskakuje o desítky stupňů), a vzdálenost
k němu jde k nule → `SpeedLimit` by robota zastavil na **každém** uzlu. `L_d` tady tedy neurčuje cíl,
jen práh „tenhle uzel už mám za sebou".

**Proč ne zbývající délka celé dráhy:** na dojezd už je brzdná obálka o krok dřív (`vCmd` = minimum
přes uzly z `Dist2Speed`) — byla by to duplicita. `SpeedLimit` neřeší dojezd, ale geometrii řízení.

*(Zajímavost: `distToNext` se v `Control` počítalo už předtím a nikde se nepoužívalo — původní návrh
nejspíš mířil sem a napojení na virtuální bod byla odbočka.)*

Hlídají dva testy (`PathControllerTests`), oba ověřené tak, že bez opravy padají:

| test | bez opravy |
|---|---|
| `Straight_ReachesFullSpeed` (rovinka 20 m, s odchylkou kurzu i bez ní) | rozjel se jen na **0,19 z 0,80 m/s** |
| `ManyCollinearWaypoints_DoesNotStallAtEach` (uzel po 1 m) | bez přeskakování **0,62 z 0,80 m/s** + padají i rohové testy |

Původní varianta je ponechaná zakomentovaná do ověření na HW (viz CLAUDE.md).

#### Co zůstává otevřené

⬜ **`MaxAllowedRotationSpeed = π/6` (30°/s) je nízké.** Po opravě už rychlost nezamyká, ale zůstává
mezí toho, jak rychle se robot srovná na cílový uzel. Jestli robot mechanicky unese víc, se musí
potvrdit na zařízení — „maximální **dovolená**" není technická mez. Souvisí s tím i `LookaheadMin`
(0,15 m), který teď funguje jako práh přeskoku uzlu: příliš malý = míří se na uzly těsně před robotem
(neklidný azimut), příliš velký = přeskakují se i rohy, které se měly projet. **Neověřeno na HW.**

⬜ **`beta = −11,9°` na rovné 6m dráze** je samo o sobě dost. Může jít o důsledek plazení (robot se
nestihl srovnat) — po rozjezdu bude vidět, jestli odchylka zmizí, nebo je to samostatná chyba
ve sledování dráhy.

### ⬜ Otevřený úkol: znaménko rotace ověřit na zařízení

**Znaménko** je jiná otázka než faktor a **z kódu se rozhodnout nedá** — musí se změřit na robotu.
Papírová nesrovnalost:

- `rotationSpeed` je matematické, **+CCW = vlevo**;
- `IMotorControl.Drive` dokumentuje `difSpeed > 0` jako **pravé** otáčení;
- `SDC2160Ex.Drive` navíc posílá `!VAR 4 −CalcSpeed(difSpeed)`, tedy do řadiče jde `−dif`, přičemž
  `VAR 4` je ve skriptu dokumentovaná jako „+ = matematický smysl";
- skript pak počítá `motor1 = −(curSpeed+curRotSpeed)`, `motor2 = curSpeed−curRotSpeed`, takže výsledek
  závisí i na tom, **které kolo je motor 1** a jak jsou motory namontované (proto ta asymetrická
  negace).

Složením těch čtyř míst může znaménko vyjít správně i obráceně; předchozí generace jela s
`+ω·Rozchod/2` **bez explicitního přehození** a fungovala, což mluví pro to, že to celé vychází.
**Autorův odhad: komentář `dif>0 = vpravo` je správný a nesrovnalost je jen zdánlivá.**

**Zkouška na robotu** (jedna, rozhodne obojí):

1. Zadat malé konstantní `+ω` (např. 0,3 rad/s) při nulové dopředné rychlosti a sledovat, **kam se
   robot otočí**. Vlevo (CCW) = řetěz je konzistentní, nechat být. Vpravo = někde v kompozici je
   přehození; opravit **na jednom místě** a zdůvodnit v [decisions.md](decisions.md).
2. Týmž pokusem porovnat **odometrické ω** (`Odo/rate` = `(vR − vL)/rozchod`, viz
   [ekf-fusion.md](ekf-fusion.md)) proti gyroskopu (`IMUState.AngularVelocity.Z`). Musí mít **stejné
   znaménko** — jinak je fúze proti sobě váží a kurz se rozjede; pojistka je
   `FusionConfig.OdoOmegaSign`.

*Proč to nespravovat „naslepo": je to příkazová cesta. Otočené znaménko rotace znamená, že robot
zatáčí od dráhy místo k ní — regulátor pak divergovaně kmitá a při nešťastné konstelaci ujede z cesty.
Hádat tady je dražší než změřit.*
