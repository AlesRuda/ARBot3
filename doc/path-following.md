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
