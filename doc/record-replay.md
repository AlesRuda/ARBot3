# Record / Replay pipeline, runtime a vize

Zaznamenání běhu robota (senzorická měření, řídicí požadavky, mezivýsledky), jeho pozdější
přehrání a (výhledově) přepočet pro porovnání. Aplikace `ARBot` běží v jednom z režimů:
**Run** (reálné řízení + záznam), **View** (zobrazení záznamu), a **Simulate** (přepočet nad
záznamem) — **Simulate je zatím odložen** (viz §„Odložený Simulate").

> Dokument je návrhová reference pro implementaci. Konceptuální model je v §„Model za běhu";
> konkrétní API, rozhodnutí a pořadí kroků v §„Implementační kontrakt"; co existuje vs. co vznikne
> v §„Stav implementace".

## Rozsah teď: Run + View

| | Kořenový zdroj | Zpracování | Motory | Záznam | Nástroje |
|---|---|---|---|---|---|
| **Run** | reálné senzory (`ARBotHW`) | vize + fúze + řízení | reálné `Drive` | best-effort (per-typ drop) | volitelně |
| **View** | soubor | ne (jen přehrání/seek) | ne | ne | ano (prohlížení + navigace) |

Životní cyklus: `Start(Mode)` / `Stop()`, bez živého přepínání (stop → start jiného/stejného).

## Model za běhu

### Jeden `Stream` (singleton, fan-out bez fronty)

`ARBotRuntime.Current.Stream` (`MessageSource`) je jediný veřejný proud. Sám **nemá frontu** —
rozbočovač: při každé zprávě zavolá `Post` na každého odběratele. Teče na něj **sjednocení
surových ∪ odvozených** zpráv.

- **Odběratel** = `IMessageSink` (typicky `MessageTarget` s **vlastní frontou + vláknem**),
  připojený `Stream.Connect(...)`: `RecordingTarget`, UI dokumenty, síťová telemetrie.
- Fronta je v každém odběrateli → pomalý odběratel nebrzdí ostatní ani producenta.
- **Pravidlo:** `Stream.Emit` běží na vlákně producenta → odběratelé musí být **neblokující**
  (drop). Bezztrátový (blokující) odběr jen offline.

### Před `Stream`em: zdroj → router → zpracování

- **Role zprávy:** *primární* = senzorová měření (potomci `SensorStateBase`) + externí příkazy;
  nesou marker **`IPrimaryMessage`**. *Odvozené* = vše ostatní (`RobotStateMsg`, `Blob`,
  `DriveCommandMsg`, diagnostika).
- **Router:** primární zprávu pošle **do zpracování i na `Stream`** (surový passthrough, aby ho
  záznam/UI viděly); odvozenou zprávu **ze souboru** pošle jen na `Stream` (míjí zpracování).
  V Run senzory produkují jen primární.

### Zpracování = paralelní stupně, dva druhy uzlů

Stupně běží nezávisle a **paralelně** (každý vlastní fronta+vlákno), propojené explicitně.

- **Reaktivní** (řízené příchozí zprávou; `MessageProcessor.Consume` + type-switch):
  - **Vize** (`CameraFrame` → `Blob` sjízdnosti; výhledově i pose měření do fúze).
  - **Fúze** (`IMeasurement` → `AsyncFusionEngine.Enqueue`). Jen **agreguje a udržuje
    dotazovatelný odhad** (`GetStateAt(t)` predikuje dopředu). **Netikuje, neemituje, nevydává řízení.**
    Vstup dnes ze `SensorStateBase` přes mapper; výhledově i **lokalizace srovnáním obrazu s mapou**.
- **Periodické** (řízené schedulerem nad `IClock`, ne zprávou):
  - **Řídicí smyčka** (perioda `Profile.Ts`): bere **nejaktuálnější informace** — `engine.GetStateAt(t_k)`
    (predikce k času tiku; funguje i při výpadku měření → dead-reckoning / bezpečné zastavení).
    Spočítá `RegulatorResult`, zavolá `motor.Drive(...)`, emituje `RobotStateMsg` (vzorek v `t_k`)
    a `DriveCommandMsg`.

**Paralelismus fúze × řízení je bezpečný**, protože `AsyncFusionEngine` bude **thread-safe**
(interní zámek). Fúze (`Enqueue`, reaktivní vlákno) i řízení (`GetStateAt`, vlákno scheduleru)
sdílejí engine bez datového závodu. Žádná umělá serializace není potřeba.

### Scheduler nad `IClock`

Periodické uzly pohání scheduler vázaný na `IClock`. **V Run:** `SystemClock` + reálný časovač
→ `scheduler.PumpDue(clock.Now)`. Mřížka `Ts = Profile.Ts`, kotvená k času `Start` (`t0`), ticky
`t0 + k·Ts`. (Pumpování virtuálním časem z `FileMessageSource` je věc odloženého Simulate.)

### Motory

Řídicí smyčka je nezávislá na režimu — vždy volá `motor.Drive(...)`. Injektuje se jiná
`IMotorControl`: **Run** = reálný driver (`SDC2160Ex`), (**Simulate** = `DummyMotors`), **View**
= smyčka neběží. Mapování `forvard = RegulatorResult.Speed`, `dif = RegulatorResult.RotationSpeed
* Profile.Rozchod` (`dif>0` = vpravo).

### Záznam = best-effort (řídicí smyčka má přednost)

V Run je priorita řídicí smyčka; záznam ji nesmí zdržet a je postradatelný. `RecordingTarget`
zahazuje při nestíhání podle **per-typ retence** (sémantika `MessageQueue`): typ bez konfigurace /
limit 0 → hned zahozen; větší limit → přežije déle. **Drop se děje v `Post`** (na vlákně producenta),
ne v `Consume` — jinak by fronta blokovala `Stream.Emit`. **Bloby dostanou nízký limit → zahazují se
první.** Per-typ limity plní runtime (config). Bezztrátový (`Block`) jen offline.

### Dva časy: `T_in` (pořízení) a `T_out` (příchod)

Kvůli budoucí věrné reprodukci (a protože latence zpracování ovlivňuje, co řídicí smyčka „viděla":
měření pořízené v `T_in` se na stavu projeví až v `T_out`, kdy doběhne zpracování — u řetězce
vize → lokální mapa → řízení je latence proměnná), **každá zaznamenaná zpráva nese oba časy:**

- **`T_in`** = `Message`/`SensorStateBase.TimeStamp` (pořízení; už existuje).
- **`T_out`** = čas příchodu na `Stream` (stampuje `RecordingTarget`); ukládá se do indexu jako
  **`ArrivalTicks`**.

Teď se `T_out` jen **zaznamenává** (levné). Věrný, `T_out`-řízený replay a fúze aplikující po `T_in`
(out-of-sequence) je součást odloženého Simulate.

## View — navigace v záznamu (index-aware seek)

Jedna globální timeline; nástroj ukazuje seznam/pozici z indexu. Dokumenty o indexu nic neví, jen
sledují `Stream`. Index (`MessageIndex`, sidecar `*.idx`) nese `Seq`, `CaptureTicks` (`T_in`),
**`ArrivalTicks` (`T_out`)**, `MsgName`, **`Name`** (z `INamedMessage`), `Offset`, `Length`.

`FileMessageSource` (stavový automat: **Playing** / **Paused**):
- **Play:** kurzor jde sekvenčně, emituje zprávu za zprávou (časovaně / co nejrychleji).
- **SeekTo(pozice)** (jen v Paused): rekonstrukce stavu — **zpětný průchod indexem od kurzoru**,
  sbírá první výskyt (= poslední ≤ pozice) pro každou dosud neviděnou `(MsgName, Name)`; končí,
  když má všechny klíče (množina je z indexu známá), nebo dojde na začátek. Nalezené `(Offset,
  Length)` přečte **náhodně** (samostatný čtenář rámce z `Offset`, ne bufferovaný sekvenční reader)
  a emituje na `Stream`. Klíč začínající až za kurzorem se nenajde (správně).

Příklad: `Blob X`@80,100; `IMU`@81,85,91,95,101; seek na 90 → `Blob X`@80 + `IMU`@85.

## Determinismus

„Teď" jde z `IClock`. V Run porovnání neprobíhá → stačí tolerance / best-effort. `AsyncFusionEngine`
umí out-of-sequence po `T_in`. Přesná reprodukce (Simulate) je odložena — bude vyžadovat `T_out`-řízený
replay a smíří se s reziduální nejistotou (plánování vláken, FP; porovnání s tolerancí ~1e-6 stejný stroj).

---

## Implementační kontrakt

Skeleton API (orientační C#, názvy dodržet) a rozhodnutí.

```csharp
// ARBot.Common.Logs
public interface IPrimaryMessage { }               // marker surového vstupu; nese SensorStateBase
public class DriveCommandMsg : Message, IHasCaptureTime {   // odvozená, jen debug
    public double Speed, RotationSpeed, Forvard, Dif; public DateTime TimeStamp;
}

// ARBot.Common.Devices  (IMotorControl SEM přesunout z ARBot.HAL)
public interface IMotorControl : ISensor {
    void Drive(double forvard, double dif); void SetAcceleration(double a);
    IMotorState GetLastMeasurement(); event EventHandler<IMotorState> MeasurementArived;
}
public sealed class DummyMotors : IMotorControl {
    // Drive/SetAcceleration = no-op; Name="Dummy"; IsError=false;
    // GetLastMeasurement() vrací dummy IMotorState (nuly); MeasurementArived se nevyvolává.
}

// ARBot.Common.Runtime
public interface IScheduler {
    IDisposable Register(TimeSpan interval, Action<DateTime> onTick);   // mřížka t0 + k·interval
    void PumpDue(DateTime now);
}

// ARBot.Robot (app) — obdoba ARBotHW.Current
public sealed class ARBotRuntime {
    public static ARBotRuntime Current { get; }
    public MessageSource Stream { get; }
    public Mode Mode { get; }
    public void Start(Mode mode, string file = null);   // file pro View
    public void Stop();
}
public enum Mode { Run, View /*, Simulate (odloženo) */ }
```

### Klíčová rozhodnutí

- **`AsyncFusionEngine` thread-safe:** interní zámek kolem `Enqueue`, `GetStateAt`, `Diagnostics`
  (a vnitřních `EnsureValid`/`Prune`). Umožní fúzi a řízení jako **paralelní** stupně bez race.
- **Fúze netikuje:** z `FusionProcessor` odstranit `PumpTicks`/`RunControlStep`; `Consume` jen
  `engine.Enqueue`. Emisi `RobotStateMsg` převezme řídicí smyčka. (Golden-replay test upravit.)
- **Řídicí smyčka = periodický uzel** (`Profile.Ts`): drží referenci na `AsyncFusionEngine`,
  na tiku `t_k` dotaz `GetStateAt(t_k)`, `IRegulator.Control(...)`, `motor.Drive(...)`, emit
  `RobotStateMsg` + `DriveCommandMsg`. **MVP: dojet na zadané (x,y)** přes `IRegulator` (pevný
  waypoint); kamera/mapa/plán zatím vůbec (žádná plán-zpráva ani plánovač).
- **`RobotState : IModelState`.** `IModelState` je oříznut (odstraněny `IHistoryItem`, `Clone`,
  `Interpolate`). `RobotState` doplnit/namapovat: `Orientation`←`Theta`, `Velocity`←`V`,
  `OrientationVelocity`←`Omega`, `X`/`Y` (get/set), **`Roll`/`Pitch`** (z posledního IMU, ne z EKF);
  `Rotation` = `Matrix4x4` z (`Orientation`,`Pitch`,`Roll`); `Trasnformation` = `Rotation` + posun
  (`X`,`Y`). Konvence úhlů/os dle [imu-and-frames.md](imu-and-frames.md) (world ENU, 0=východ, +CCW).
- **Router:** `if (msg is IPrimaryMessage) → zpracování i Stream; else (odvozené ze souboru) → jen Stream`.
- **Záznam Run:** `RecordingTarget` s per-typ retencí (`MessageQueue` sémantika), drop v `Post`,
  bloby nízký limit; stampuje `T_out` (`ArrivalTicks`).
- **`SeekTo`:** stavový automat Play/Paused; náhodné čtení rámce z `Offset` samostatným čtenářem
  (bufferovaný sekvenční `MessageReader` nelze jen přeseekovat).
- **Start/Stop pořadí:** cíle (odběratelé) startovat **před** zdroji; při Stop opačně (drain).
  `ARBotRuntime` počká na dokončení async initu `ARBotHW` (dnes `Task.Run`), než začne drátovat Run.

### Upřesnění k implementaci (gotchas ověřené proti kódu)

- **`MessageTarget.Post` není `virtual`** — pro drop v `Post` ho udělat `virtual` (nebo přidat
  pre-filter hook), aby ho `RecordingTarget` mohl přepsat.
- **`T_out` se zachytí v `Post`** (vlákno producenta), ale zapisuje se v `Consume` (jiné vlákno,
  později) → přenést čas frontou (obálka `{ Message, ArrivalTicks }`), nespoléhat na čas v `Consume`.
- **`MessageQueue` „limit 0 → zahodit" NEPLATÍ** dle kódu: `Enqueue` počítá `cnt = CountLimit - Count`,
  při limitu 0 je `cnt = 0 ≥ 0` → zprávu **přijme**. Zahazuje jen typ **bez `cfg` záznamu**. Runtime
  proto musí `cfg` naplnit per-typ (bloby nízký limit); případně upravit sémantiku v `MessageQueue`,
  ať „0 = zahodit" skutečně platí.
- **`ARBotHW.Current`** dnes spouští `Init` přes `Task.Run` **bez uložení Tasku** → nelze awaitnout;
  zpřístupnit init Task (nebo `IsReady`) pro `ARBotRuntime` před drátováním Run.
- **Řídicí smyčka emituje na `Stream`** → `Stream.Emit` je `protected`, takže smyčka musí být
  `MessageProcessor` (má `Output`/`EmitDerived`) nebo držet relay-source; ne prostý objekt.
- **`FileMessageSource` pro View:** rozšířit ctor o **index**; stav **Play/Paused**; kurzor = `Seq`;
  `SeekTo(Seq)`; **oddělený čtenář rámce** (bufferovaný sekvenční `MessageReader` nelze přeseekovat
  sdílením `stream.Position`). Po `SeekTo` Play pokračuje z uložené `Seq`, ne ze `stream.Position`.
- **Router je v Run+View fakticky passthrough** — větev „odvozené ze souboru → jen `Stream`" se
  uplatní až v Simulate.
- **Golden-replay test:** po přesunu emise `RobotStateMsg` z `FusionProcessor` do řídicí smyčky musí
  referenci v testu emitovat řídicí smyčka (test upravit, aby čekal na její ticky).

### Výchozí volby pro zbylé drobnosti (neblokující, lze změnit)

- **Roll/Pitch do `RobotState`:** řídicí smyčka drží poslední `IMUState` (odebírá ho) a při
  vzorkování `GetStateAt(t_k)` doplní `Roll`/`Pitch` z něj do vráceného `RobotState`. (EKF je nedrží.)
- **Per-typ retence v `RecordingTarget`:** vlastní per-typ počítadla přímo v (nyní `virtual`) `Post`
  (ne samostatná třída `MessageQueue`); dekrement v `Consume`. Default limity konfigurovatelné v
  runtime: bloby nízké (např. 2), ostatní vysoké (např. 100), typ bez limitu = neomezený.
- **`DriveCommandMsg`:** `ToData/FromData/Build` dle vzoru `RobotStateMsg` + registrace v
  `MessageCatalog.CommonDefaults()`.
- **`IRegulator`:** pro MVP použít `Regulator` s parametry z `Profile` (`MaxAllowedSpeed`,
  `MaxAllowedRotationSpeed`, `MaxAcceleration`, `Rozchod`).
- **Časovač scheduleru v Run:** `System.Threading.Timer` (nebo dedikované vlákno) volající
  `PumpDue(clock.Now)` s jemným tikem (≈ `Profile.Ts`).
- **Čekatelný init `ARBotHW`:** zpřístupnit init `Task` (nebo `IsReady`), aby `ARBotRuntime.Start(Run)`
  počkal před drátováním grafu.

### Pořadí kroků (po každém build `-p:Platform=x64` + zelené testy)

1. **`IMotorControl` → `ARBot.Common.Devices`** (+ `DummyMotors`); drivery v HAL přepnout na Common rozhraní.
2. **`IPrimaryMessage`** marker na `SensorStateBase` (+ příkazové typy).
3. **`AsyncFusionEngine` thread-safe** (zámek).
4. **`RobotState` += Roll/Pitch → `IModelState`**.
5. **Záznam:** `RecordingTarget` best-effort (per-typ retence, drop v `Post`) + **`T_out`/`ArrivalTicks`**
   a **`Name`** do `MessageIndex`.
6. **`IScheduler`** + **řídicí smyčka** jako periodický uzel (`Profile.Ts`); **odstranit
   `FusionProcessor.PumpTicks`**; `RegulatorResult → Drive`; `DriveCommandMsg`; MVP „dojet na (x,y)".
7. **Router** (`IPrimaryMessage`): primární → zpracování i `Stream`.
8. **`ARBotRuntime`** (`Mode {Run,View}`, `Start/Stop`, `Stream`, drátování grafu per režim);
   **migrace dokumentů na odběr `Stream`u** (`ImageDocument`, `CameraDocument`…) místo vlastního feedu;
   `OpenImages` = `Stream.Connect(doc)`; vize (`BackProjectProcessor`) je součást runtime grafu v Run.
9. **View navigace:** `FileMessageSource` Play/Paused + `SeekTo` (náhodné čtení) + navigační tool.

## Kde to je (namespaces)

- **Pipeline** — `Src/ARBot.Common/Communication/`: `MessageSource`, `MessageTarget`, `MessageProcessor`,
  `IMessageSink`, `OverflowPolicy`, `SensorMessageSource`, `RecordingTarget`, `MessageQueue`,
  `MessageIndex`, `FileMessageSource`, `MessageCatalog`, `MessageReader`/`MessageWriter`.
- **Zprávy** — `Src/ARBot.Common/Logs/`: `Message`, `RobotStateMsg`, `MeasurementDiagMsg`,
  `DriveCommandMsg`(nový), `Blob`, `IHasCaptureTime`, `IPrimaryMessage`(nový).
- **Měření / zařízení** — `Src/ARBot.Common/Devices/`: `SensorStateBase`(+`IMUState`, `GPSState`,
  `MotorStateBase`, `CameraFrame`), `IMotorControl`(přesun sem), `DummyMotors`(nový).
- **Runtime** — `Src/ARBot.Common/Runtime/`: `IClock`/`SystemClock`/`VirtualClock`, `IScheduler`(nový),
  `IMeasurementMapper`/`DefaultMeasurementMapper`, `FusionProcessor`, řídicí smyčka(nová), `ComparisonTarget`(pro Simulate).
- **Vize** — `Src/ARBot.Common/Vision/`: `ImageLayer`, `MessageImageLayers`, `BackProjectProcessor`.
- **App** — `Src/ARBot`: `ARBotRuntime`(nový, `ARBot.Robot`), `ARBotHW`, `ImageDocument`.
- **Nástroj** — `Src/ARBot.Record` (konzole, není v `ARBot.slnx`).

## Stav implementace

**Hotovo:** pipeline primitiva, `SensorMessageSource`, `RecordingTarget` (zatím `Block`, bez `T_out`),
`MessageIndex` (bez `Name`/`ArrivalTicks`), `FileMessageSource` (jen Play, bez `SeekTo`),
`FusionProcessor` (zatím **s** `PumpTicks` — bude odstraněn), `BackProjectProcessor`, `ImageDocument`
(vlastní feed — bude migrován), hodiny, mapper, `Blob` JPEG (SkiaSharp), `MessageQueue`, `ComparisonTarget`.

**Zatím není:** `ARBotRuntime`+režimy, `IPrimaryMessage`+router, `IScheduler`+periodická řídicí smyčka,
`DummyMotors`, `IMotorControl` v Common, thread-safe engine, best-effort per-typ záznam, `T_out`+`Name`
v indexu, `SeekTo`, `DriveCommandMsg`, `RobotState` Roll/Pitch, migrace dokumentů na `Stream`.

## Odložený Simulate

Věrná reprodukce běhu (přepočet ze záznamu + porovnání) je odložena, protože je rozsáhlá: vyžaduje
**`T_out`-řízený replay** (měření se do fúze zařadí po `T_in`, ale „zviditelní" se v `T_out`),
reprodukci **lokální mapy a vize** (řetězec vize → mapa → řízení), a i tak zůstane **reziduální
nejistota**. Enabling hook zavádíme hned: **záznam nese `T_in` i `T_out`** (viz výše), takže se pak
Simulate postaví bez přepisu formátu. Součástí bude `DummyMotors`, `ComparisonTarget`, scheduler
pumpovaný `VirtualClock`em z `FileMessageSource`, a rozhodnutí, zda vize při přepočtu bere surový
`CameraFrame` (nutná serializace) nebo zaznamenaný RGB `Blob`.

## Otevřené úkoly

- **Runtime + režimy + scheduler + periodická řídicí smyčka** (viz Implementační kontrakt).
- **Revize `FusionConfig`** — duplicitní rozchod (`FusionConfig.WheelBase = 0.5` vs `Profile.Rozchod = 0.41`);
  projít obsah `FusionConfig` a **sjednotit zdroj rozchodu** (řízení bere `Profile.Rozchod`).
- **Simulate** (viz §Odložený Simulate) vč. serializace `CameraFrame` / rozhodnutí o zdroji vize.
- **Akcelerace barevných převodů přes `NativeComputeUnit`.** `MessageImageLayers` dělá RGB/BGR → BGR32
  **dočasně** managed přes `Image<T>.ConvertTo`. `NativeComputeUnit` je od SIMD/HW akcelerace — má
  `CopyRGB24ToBGR32` / `CopyBGR24ToBGR32`, ale zatím jen nad typovými poli a `IntPtr`, ne nad managed
  `byte[]→byte[]`. Doplnit `byte[]→byte[]` varianty a nasměrovat tam převody (zvážit `NativeLib` závislost).
- **High-level plánovač cesty** na mapě + **lokalizace srovnáním obrazu s mapou** (vstup do fúze) — později.
- **Síťová telemetrie** přes `ARBotTCP*` (best-effort `MessageSource`/`Target`).
