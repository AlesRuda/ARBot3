# EKF senzorická fúze

Rozšířený Kalmanův filtr pro fúzi senzorů (poloha, orientace, rychlost, úhlová rychlost).
Kód: **`ARBot.Common/Fusion/`**. Podrobný rozbor: [`doc/EKF_fuze_dokumentace.docx`](EKF_fuze_dokumentace.docx).

## Architektura

- **`Ekf`** — generická abstraktní třída (výpočet: predikce/korekce, Joseph form,
  čisté `PredictStep`/`UpdateStep` nad libovolným `(x,P)`; MathNet.Numerics).
- **`EKFModel : Ekf`** — konkrétní 2D model diferenciálního podvozku, stav
  **`[X, Y, θ, v, ω]`**, near-constant-velocity predikce, `Q(dt)` škáluje s časem.
  `v` i `ω` jsou **stavy** (fúze z více senzorů → robustnost vůči smyku).
- **Měřicí modely** (`IMeasurement`, `Measurements.cs`): poloha, orientace (wrap),
  rychlost, úhlová rychlost, póza. Každé měření nese čas pořízení, `h/H/R` a residuum.
- **`AsyncFusionEngine`** — asynchronní zpracování dle času **pořízení** (capture):
  buffer uzlů `{měření, x, P}` + `dirtyFrom` (líný přepočet ocasu), out-of-sequence
  (opožděná měření z kamery) přes replay, okno historie ~1 s, `GetStateAt(t)`
  (predikce do budoucna i rekonstrukce minulosti).
- **NIS + gating** (`Gating.cs`): `NIS = dᵀS⁻¹d`; `GateMode.Reject` (zahodit) nebo
  `Soft` (nafouknout R → nikdy se nezasekne, sám se zotaví z výpadku). Bezstavové,
  skládá se s replayem. `AsyncFusionEngine.Diagnostics()` reportuje NIS per měření.
- **`SlipDetector`** — při nefyzikálním zrychlení kol nafoukne R odometrie.
- **`GeoReference`** (`ARBot.Common/Coordinates`) — počátek lokální ENU roviny
  (LLA, kde `[X,Y]=[0,0]`), převod LLA↔lokální metry přes ECEF.

## Konvence

Stav v **ENU** (X východ, Y sever), orientace **matematicky** (0 = východ, +CCW).
Zdroj R pro orientaci: `IMUState.OrientationUncertainty` z VN100 (viz
[imu-and-frames.md](imu-and-frames.md)). Detaily rámců: tamtéž.

## Stav / poznámky

- Testy: `ARBot.Common.Tests/Fusion` (predikce, jakobián, Q, konvergence, fúze v/ω,
  smyk, wrap, OOSM replay, prune, NIS/gating).
- **Adaptivní odhad R/Q z reziduí je vědomě odložen** — je stavový a konfliktní
  s bezstavovým replayem; zatím per-měření R z kvality senzoru + fyzikální Q + NIS gating.
- **Legacy EKF** (`Common/EKF.cs`, `Models/EKFModel2/3*`) je vyřazen z kompilace
  (`<Compile Remove>` v `ARBot.Common.csproj`) — slouží jen jako referenční matematika.
- **Zbývá** (příště, v projektu `ARBot`): `SensorAdapters` napojující reálné senzory na
  engine + řídicí smyčka; ladění σ a prahů gatingu na reálných datech.

### Otevřený úkol: Pitch/Roll patří do stavu EKF (2026-08-11)

`RobotState.Roll`/`Pitch` dnes **nejsou součástí stavu filtru** — doplňuje je
[`ControlLoop`](../Src/ARBot.Common/Runtime/ControlLoop.cs) z **posledního IMU**, které proteklo jeho
`Consume` (`lastImu`). Dva problémy s tím:

1. **Není poznat, které IMU vzorek poslalo.** [`IMUState`](../Src/ARBot.Common/Models/IMUState.cs) je
   `SensorStateBase`, ale **ne** `INamedMessage` — nenese žádnou identitu zdroje. Při více IMU tedy
   vyhrává prostě to, které dorazilo naposled, a Roll/Pitch mohou mezi tiky přeskakovat mezi čidly
   s jinou montáží i kvalitou. (Fúzní strana měření sice značkuje `Source` — `"IMU/heading"`,
   `"IMU/gyro"` — ale to jsou **konstanty**, takže ani tam se dvě IMU nerozliší.)
2. **Obchází to fúzi.** Roll/Pitch jdou mimo EKF: bez gatingu (divoký vzorek se nezahodí), bez
   kovariance, bez korektního vzorkování v čase `t` (`GetStateAt` je nedopředikuje, jen se přilepí
   poslední hodnota). Zbytek `RobotState` je přitom fúzovaný a časově konzistentní — je to nekonzistence
   v jednom objektu.

**Návrh:** přidat pitch/roll **do stavového vektoru EKF** (měření z IMU akcelerometru/YPR jako
regulérní `IMeasurement` s vlastním σ a gatingem) a `RobotState.Roll`/`Pitch` plnit z filtru jako
ostatní složky. Pak zmizí i `ControlLoop.lastImu` a smyčka nebude muset odebírat `IMUState`.

**Kdo to používá** (kontrola dopadu): `RobotState.ToWorldTransform()` /
`ToWorldTransformWithPosition()` (`Conversions.WorldToWorldTransform(Orientation, Pitch, Roll, …)`).
Jako mezikrok (kdyby se stav EKF rozšiřovat nechtěl) by stačilo dát `IMUState` identitu zdroje a
vybírat **konkrétní** IMU podle konfigurace — ale nekonzistenci s fúzí to neřeší.

### Otevřený úkol: diagnostika EKF do streamu a záznamu (2026-08-13)

Chování filtru dnes nejde zpětně prohlédnout ze záznamu — `AsyncFusionEngine.Diagnostics()`
vrací per-měření `Source / Time / Nis / Accepted`, ale nikam se to neemituje, takže je to vidět
jen za běhu v debuggeru. Když robot v simulaci „poskakoval", nedalo se odlišit, jestli je to
šum GPS, nebo gating zahazující měření.

**Zpráva už existuje a je připravená:** [`MeasurementDiagMsg`](../Src/ARBot.Common/Logs/MeasurementDiagMsg.cs)
má přesně potřebná pole (`Source`, `Z`, `DiagR`, `Nis`, `Accepted`, `TimeStamp`) a je
**zaregistrovaná v katalogu** (`MessageCatalog`), takže by se rovnou serializovala i přehrála
ve View. Jen ji nikdo neplní. (`EKFStepMsg` vedle ní je něco jiného — dump celých matic
z předchozí generace, na průběžný záznam příliš těžký.)

**Pozor na jeden detail, který určuje, kde se emituje:** NIS při `Enqueue` **ještě neexistuje**.
Měření se jen zařadí a buffer se označí za špinavý; `Nis`/`Accepted` plní až `EnsureValid()`
a při doražení opožděného měření se uzly **přepočítají**, takže se NIS může zpětně změnit.
Emitovat při vložení by tedy zapisovalo hodnotu, která ještě není spočtená. Nabízí se odběr
až usazených hodnot — např. `FusionProcessor` si periodicky (~10 Hz, bezpečně pod oknem
historie 1 s) přečte `Diagnostics()` a pošle záznamy novější než poslední odeslaný.

Doplnit bude potřeba `Z` a `DiagR` do `AsyncFusionEngine.MeasurementInfo` (dnes nese jen
`Source/Time/Nis/Accepted`).

Objem: ~155 měření/s (IMU 100 Hz, odometrie 50 Hz, GPS 5 Hz) ≈ 12 kB/s — proti obrazům z kamer
(~1,8 GB/min) zanedbatelné. Alternativa je periodický souhrn po zdrojích (počet, podíl přijatých,
průměrný a maximální NIS), ale ten neumožní dohledat konkrétní zahozené měření.

K tomu patří i dokovatelný dokument, který to zobrazí. **Nerozhodnuto, neimplementováno.**
