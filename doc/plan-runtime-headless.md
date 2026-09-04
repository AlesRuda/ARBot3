# Runtime bez UI — návrh a implementační plán (fáze 1 a 2)

> Plán se plní **task po tasku**, kroky mají checkboxy (`- [ ]`). Každý task končí zeleným buildem
> a testy. Fáze 3 (webový náhled) dostane vlastní návrh až po ověření fáze 2 na zařízení.

**Cíl:** Řídicí runtime robota (`ARBotRuntime`, `ARBotHW`, řídicí smyčka, fúze, navigace, mise,
záznam) má být spustitelný **bez Avalonie**: na OrangePi přes ssh, jedním příkazem, bez displeje.
UI aplikace `ARBot` zůstane beze změny chování a runtime bude jen používat.

**Proč:** Avalonia bez displeje na zařízení vůbec nenastartuje, takže „spustit přes ssh“ dnes
nejde. Druhý důvod je odlehčení: Avalonia, Dock, Mapsui, fonty a renderovací smyčka žerou paměť
i CPU, které řízení nepotřebuje. **Kolik**, se změří až na zařízení — `PerfMsg` už nese CPU
procesu, takže srovnání UI proti headless bude přímo v záznamu. Odhad dopředu nedávat.

**Rozhodnutí autora (4. 9. 2026):**
- Nový projekt **`ARBot.Runtime`** mezi HAL a aplikacemi. Do `ARBot.Common` runtime jít nemůže:
  závisí na `ARBot.HAL` a směr je `Common ← HAL`; přesun by vrstvu obrátil.
- Headless aplikace umí **jen Run**. Prohlížení záznamů (`Mode.View`) zůstává v UI na Windows
  a v `ARBot.Analyze`.
- **Žádný systemd, žádná služba.** Headless se vždy spouští na základě uživatelského příkazu
  (ssh + příkaz). Restart po pádu není žádoucí — robot, který se sám znovu rozjede, je horší než
  robot, který stojí.

## Než začneš (předání práce)

**Výchozí stav:** poslední commit `6bf9a2d`, pracovní strom čistý. `ARBot.Common.Tests` pod `x64`:
**1 131 zelených testů** (4 přeskočené), změřeno 4. 9. 2026 — to je baseline, proti které se
porovnává.

**Přečti napřed:** [CLAUDE.md](../CLAUDE.md) (pravidla projektu, zejména platforma buildu a zákaz
commitu bez pokynu), [doc/architecture.md](architecture.md) (vrstvy; tenhle plán je mění),
[doc/record-replay.md](record-replay.md) (životní cyklus `Start/Stop`, stream, záznam),
[doc/configuration.md](configuration.md) (bootstrap parametrů, který se přesouvá).

### Co dnes platí (zjištěno 4. 9. 2026)

- Celé jádro běhu je `Src/ARBot/Robot/` — 4 soubory, 2 736 řádků. **Žádný neodkazuje na Avalonii,
  Dock ani `Dispatcher`.** Jediná zmínka o UI je komentář v `ARBotRuntime.cs:1339`.
- UI mluví s runtime přes jediný šev: singleton `ARBotRuntime.Current`, jeho `Stream.Connect(sink)`
  a pár vlastností (`Navigator`, `GlobalNavigator`, `RobotourMission`, `QrScanner`, `MapMessage`,
  `VisionMapMessage`, `FileSource`, `TeleportSimulatedRobot`). Používá ho 11 souborů ve
  `ViewModels` a `CrashLog.cs`. Namespace `ARBot.Robot` se **nemění**, aby se tyto odkazy
  nemusely sahat.
- Runtime dnes **nestartuje `Program.cs`, ale `MainWindowViewModel`** (příkazy Run/View/Stop,
  `autorun=`, `selftest=`, `open=`), vše přes `Dispatcher.UIThread`. `Program.cs` dělá jen
  bootstrap: `CrashLog.Install()`, `ParamStore.Build(...)`, výpis konfigurace,
  `ApplyMaxSpeedFromParams()`, `ApplySafeDistFromParams()`, pak Avalonia.
- `ARBot.csproj` **nereferencuje Common ani HAL přímo** — jen platformní HAL podle `Platform`
  (`OrangePI` → `HALArmbian`, jinak `HALWindows`), zbytek tranzitivně. Kopíruje do výstupu
  `config/*.cfg` a `OSM/*.osm` (`LinkBase`), bez nich `RepoPaths.RootOrBase()` na zařízení nemá
  odkud číst.
- JPEG/PNG kodek bez UI už existuje: `ImageMsg.Encode/Decode` přes SkiaSharp v Common, včetně
  nativních knihoven pro Linux arm64. (Relevantní pro fázi 3.)
- V repu **není** žádný HTTP server, WebSocket ani MJPEG. `ARBotTCPServer/Client` v Common existují,
  ale nikdo je nevolá; pro fázi 3 se nepoužijí.
- Precedens konzole nad HAL bez Avalonie: `Src/ARBot.Record` (Common + HAL + HALWindows), není
  v `ARBot.slnx`.

### Pasti, na které se v tomhle repozitáři naráží

1. **Build hlásí `MSB3027 / MSB3021` na zamčené `ARBot.exe`, když aplikace běží.** Není to chyba
   kódu; aplikaci zavřít.
2. **Přesun souborů mezi projekty = `git mv`**, ne kopie a smazání. Historie souboru
   `ARBotRuntime.cs` (1 856 řádků, měsíce práce) se má zachovat.
3. **Kopírování `config/` a `OSM/` z knihovny do výstupu exe.** `None` položky
   s `CopyToOutputDirectory` se z referencované knihovny do výstupu aplikace přenášejí
   (`GetCopyToOutputDirectoryItems`), ale **ověř to pohledem do `bin/`** obou aplikací. Kdyby ne,
   položky zůstanou v obou exe projektech a z knihovny se vyhodí.
4. **`Debug.WriteLine` v Release mlčí.** Bootstrap dnes vypisuje konfiguraci přes `Debug`, což
   v UI stačí (panel Debug output). V headless na zařízení běží Release, takže co má být vidět
   v konzoli, musí jít přes `Trace` nebo `Console`. Viz pravidlo v CLAUDE.md.
5. **`ARBotHW.Current.Init()` běží asynchronně** a kamery i porty se otevírají líně. Autorun proto
   čeká `WaitReady()` + 3 s ustálení; headless musí dělat totéž, jinak Run startuje nad polovičním
   HW.
6. **`Start()` podruhé nejdřív zavolá `Stop()`** — headless nesmí Run spouštět dvakrát.
7. **`[STAThread]`** patří jen k Avalonii na Windows; headless `Main` ho nemá.

### Jak ověřovat, co nejde otestovat

- Fáze 1 nemění chování: důkaz je **zelený build celého řešení pod `x64` i `OrangePI`** a stávající
  testy. UI aplikaci po přesunu spustit a projít Run → Stop v simulaci (virtuální HW), aby se
  vidělo, že šev drží.
- Fáze 2: headless spustit na Windows s `virtualhw=true mission=freerun record=...`, nechat běžet
  ~20 s, ukončit Ctrl+C, a záznam otevřít v UI (`View`). Když je záznam čitelný až do konce
  a končí korektně, `Stop()` doběhl.
- Na zařízení nic z toho neběželo — **první spuštění na OrangePi je na autorovi** a bude to
  zároveň první běh runtime mimo UI.

---

## Architektura po změně

```
ARBot.Common ← ARBot.HAL ← ARBot.HALWindows | ARBot.HALArmbian ← ARBot.Runtime ← ARBot (Avalonia UI)
                                                                              ← ARBot.Headless (konzole)
```

| projekt | typ | obsah | referuje |
|---|---|---|---|
| `ARBot.Runtime` | knihovna | `ARBot.Robot.*` (runtime, HW kompozice), `CrashLog`, `RuntimeBootstrap`; kopíruje `config/`, `OSM/` | platformní HAL podle `Platform` |
| `ARBot` | WinExe (Avalonia) | Views, ViewModels, Diagnostics (self-test, snímky), `FilteredTraceLogSink`, tenký `Program.cs` | `ARBot.Runtime` |
| `ARBot.Headless` | Exe (konzole) | `Program.cs`: bootstrap → čekání na HW → Run → čekání na signál → Stop | `ARBot.Runtime` |

Pravidlo z [doc/architecture.md](architecture.md) ř. 23–25 („řídicí smyčka patří do aplikace
`ARBot`, protože potřebuje Common i HAL“) se přepíše: patří do **`ARBot.Runtime`**, protože
potřebuje Common i HAL a **nesmí znát UI**. Test pravidla: `ARBot.Runtime.csproj` nemá žádný
`PackageReference` na Avalonia/Dock/Mapsui a `grep "using Avalonia" Src/ARBot.Runtime` je prázdný.

### Co se přesouvá a co zůstává

**Do `ARBot.Runtime`** (namespace beze změny):

| soubor | proč |
|---|---|
| `Robot/ARBotRuntime.cs` | jádro |
| `Robot/ARBotHW.cs` | kompozice HW |
| `Robot/NeoPixelProcessor.cs` | odběratel streamu bez UI |
| `Robot/VirtualHWOptions.cs` | volby simulace |
| `CrashLog.cs` | závisí jen na `ARBotRuntime.Stop`; obě aplikace ho potřebují |
| nový `RuntimeBootstrap.cs` | bootstrap parametrů vytažený z `Program.cs` |

**Zůstává v `ARBot`:** `Program.cs` (už jen bootstrap + Avalonia), `App.axaml(.cs)`,
`ViewLocator`, `FilteredTraceLogSink` (Avalonia sink), celý `Diagnostics/` (self-test měří UI
čítače, `ScreenCapture`/`ScreenRecorder` potřebují UI; `Ffmpeg`, `GifWriter`, `ShellOpen` jsou
sice bez Avalonie, ale slouží jen snímkům — **nestěhovat, dokud je nikdo mimo UI nepotřebuje**),
`Telemetry/`, Views, ViewModels včetně partial tříd `AutoRun`, `SelfTest`, `OpenViews`, `Capture`.

### `RuntimeBootstrap`

Statická třída v `ARBot.Runtime`, namespace `ARBot.Robot`:

```csharp
public static class RuntimeBootstrap
{
    /// Sestaví ParamStore z příkazové řádky, vypíše účinnou konfiguraci a přenese
    /// maxspeed= / safedist= do Profile. Vrací null při úspěchu, jinak chybovou hlášku.
    /// O ukončení procesu rozhoduje volající (UI: Exit(2); headless: stderr + return 2).
    public static string TryConfigure(string[] commandLine, Action<string> log);
}
```

- `log` je odběratel řádků konfigurace. UI mu předá `Debug.WriteLine` (dnešní chování, výpis jde
  do Info zprávy a záznamu), headless `Trace.WriteLine` (konzole + `TraceInfoBridge` do záznamu).
- `ApplyMaxSpeedFromParams` a `ApplySafeDistFromParams` se přesunou dovnitř **beze změny logiky**
  — obě jsou bezpečnostní strop a musí se nastavit dřív, než vznikne první driver motoru nebo
  `LocalPlannerConfig` (viz komentář v dnešním `Program.cs`).
- Pořadí volání zůstává: `CrashLog.Install()` → `TryConfigure` → aplikace.

### `ARBot.Headless`

`Program.Main(string[] args)`, bez `[STAThread]`:

1. `Trace.Listeners.Add(new ConsoleTraceListener())` — konzole je jediný displej.
2. `CrashLog.Install()`.
3. `RuntimeBootstrap.TryConfigure(Environment.GetCommandLineArgs(), Trace.WriteLine)`; při chybě
   hláška na stderr a **return 2** (stejný kód jako UI).
4. Výpis na konzoli, co se chystá: režim HW (`virtualhw=`), mise (`mission=`), záznam (`record=`),
   a věta „robot se rozjede bez dalšího pokynu“, když je mise zapnutá — stejná výstraha, jakou
   dnes dává autorun.
5. `ARBotHW.Current.WaitReady()`, pak `Task.Delay(3000)` (ustálení, stejná konstanta jako
   `AutoRunSettleMs`; **jedna konstanta, ne dvě** — přesunout do `ARBotRuntime` jako
   `public const int HwSettleMs` a autorun ji odtud číst).
6. `ARBotRuntime.Current.Start(Mode.Run)`. Cesta záznamu se řeší parametrem `record=` uvnitř
   `Start`, stejně jako u autorunu.
7. Čekání na ukončení: `Console.CancelKeyPress` (Ctrl+C) **a** `PosixSignalRegistration` pro
   `SIGTERM` (ssh session zavřená, `kill`). Oba nastaví `ManualResetEventSlim`; první stisk
   ukončuje řádně, proces si `Cancel = true` nechá, aby ho .NET nezabil dřív, než doběhne `Stop()`.
8. `ARBotRuntime.Current.Stop()` — dojede fronty, uzavře záznam. Pak **return 0**.
9. Neošetřená výjimka: projde `CrashLog` (zapíše `logs/crash-*.log`, dopíše záznam) a proces
   spadne s nenulovým kódem — tak, jak to dělá UI.

Parametr `autorun=` headless **ignoruje** s hláškou (Run je jediný důvod existence procesu).
`selftest=`, `open=`, `worldshot=`, `telemetryshot=` se ignorují tiše — jsou to UI parametry
a registr je zná, takže neshodí start.

Spuštění na zařízení (jde do `doc/headless.md`):

```bash
dotnet ARBot.Headless.dll config=config/orangepi.cfg mission=robotour record=Records/jizda.rec
```

Ukončení: Ctrl+C v ssh, nebo `kill <pid>` z druhé session.

### Fáze 3 — webový náhled

> **HOTOVO 4. 9. 2026** — vlastní dokument [plan-headless-web.md](plan-headless-web.md) (návrh
> i tasky), doména v [headless.md](headless.md). Gate „až po ověření fáze 2 na zařízení" autor
> **vědomě přeskočil** — na HW se to nedalo vyzkoušet. Návrh je proto postavený tak, aby náhled bez
> publika nestál nic a šel vypnout (`web=0`); naměřeno 13,9 % → 14,3 % CPU procesu.
> Rámec níž zůstává jako záznam původního zadání; od něj se návrh odchýlil ve třech věcech —
> **vlastní HTTP nad `TcpListener`** místo `HttpListener` (ten na Windows bez admin práv neumí jiný
> prefix než localhost), **occupancy grid na půdorysu** (to, co robot vidí, ne jen mapa) a
> **`POST /stop`** jako jediný zásah. Zdůvodnění je v tom dokumentu a v [decisions.md](decisions.md).

`HttpListener` v headless procesu, port z parametru `web=` (0 = vypnuto). Tři cesty: `/` s HTML
stránkou, která si každou sekundu obnovuje dva obrázky a text stavu mise; `/camera.jpg` s posledním
snímkem kamery mise (`ImageMsg` kodek); `/world.png` s půdorysem ze SkiaSharpu přímo z
`RoadNetwork`, `RobotStateMsg`, trajektorie a mrkve. Server je odběratel streamu jako každý jiný
(`IMessageSink`, latest-wins, žádná fronta). Mapsui ani dlaždice se nepoužijí. **Návrh až po
fázi 2**, kdy bude co ukazovat a bude známo, kolik CPU headless bez náhledu stojí.

---

## Tasky

> **Stav 4. 9. 2026: Tasky 1–4 hotové, Task 5 = tato zpráva autorovi.** Výsledky jsou u kroků
> kurzívou; souhrn a co ověřit na zařízení je v [headless.md](headless.md).

### Task 1 — projekt `ARBot.Runtime` a přesun souborů (fáze 1)

- [x] Založit `Src/ARBot.Runtime/ARBot.Runtime.csproj`: `net10.0`, knihovna,
      `Platforms=x64;x86;OrangePI` (bez AnyCPU), `Nullable`/`ImplicitUsings` shodně s `ARBot.csproj`.
- [x] Přenést z `ARBot.csproj` podmíněné reference na `HALArmbian`/`HALWindows` a `None` položky
      `config/*.cfg`, `OSM/*.osm` s `LinkBase`. **Referenci `FTD2XX_NET.dll` (jen `x64`) přesunout
      také** — používá ji `ARBotHW.cs:154`; v `ARBot` po přesunu nemá co dělat.
- [x] `git mv` čtyř souborů z `Src/ARBot/Robot/` a `Src/ARBot/CrashLog.cs` do `Src/ARBot.Runtime/`
      (adresář `Robot/` zachovat kvůli orientaci). *Git je vede jako `R` (přejmenování), historie drží.*
- [x] `ARBot.csproj`: odebrat přesunuté reference a položky, přidat `ProjectReference` na
      `ARBot.Runtime`. *Navíc, s čím plán nepočítal: `CrashLog` a `ARBotRuntime.BuildCatalog` byly
      `internal` a `TelemetryDocument`/`Program.cs` je volají z jiné assembly → `public`.*
- [x] Přidat projekt do `Src/ARBot.slnx` s platformami `OrangePI`, `x64`, `x86` jako u HAL.
- [x] Přepsat komentář `ARBotRuntime.cs:1339` (odkaz na `MainWindowViewModel`) na neutrální
      „odběratel v UI“.
- [x] `dotnet build Src/ARBot.slnx -p:Platform=x64` a `-p:Platform=OrangePI` zelené. *0 chyb obojí.*
- [x] Ověřit past 3: v `Src/ARBot/bin/x64/Debug/net10.0/` jsou `config/` a `OSM/`. *Ověřeno tvrdě:
      složky smazané a rebuild je vrátil z knihovny (2/2 profily, 18/18 map). Položky jsou jen v knihovně.*
- [x] `dotnet test Src/ARBot.Common.Tests -p:Platform=x64` = baseline. *Nejdřív 1130 + 1 pád:
      strážní test `KazdyParametrSeVAplikaciNekdeCte` skenoval jen `Src/ARBot`, po přesunu tam čtení
      parametrů nebyla → skenuje `ARBot`, `ARBot.Runtime`, `ARBot.Headless`. Pak 1131 = baseline.*
- [x] Spustit UI, virtuální HW, Run → Stop; panel Robotour otevře a čte `RobotourMission`. *Bez
      klikání: self-test `selftest=true st_seconds=10 virtualhw=true mission=robotour open=robotour,debug`
      → Run 10 s (78/86 snímků sjízdnosti, 79 překreslení), Stop, exit 0, žádný crash log.*

### Task 2 — `RuntimeBootstrap` (fáze 1)

- [x] Nový `Src/ARBot.Runtime/Robot/RuntimeBootstrap.cs` podle návrhu výše; těla
      `ApplyMaxSpeedFromParams`/`ApplySafeDistFromParams` přesunout **doslova** z `Program.cs`.
      *Navíc `ExitCodeBadConfig = 2`, aby UI i headless sdílely jedno číslo.*
- [x] `Program.cs` v `ARBot` zkrátit: `CrashLog.Install()`, `TryConfigure(..., Debug.WriteLine)`,
      při chybě stejné hlášky a `Environment.Exit(2)`, pak Avalonia. Komentáře o pořadí
      (proč před UI) přenést k `RuntimeBootstrap`, ne smazat. *⚠️ Past: `Debug.WriteLine` i
      `Trace.WriteLine` mají `[Conditional]`, takže se jako skupina metod do `Action<string>` nepřeloží
      (CS1618) — předávají se lambdou `s => Debug.WriteLine(s)`.*
- [x] Testy: nový projekt **`Src/ARBot.Runtime.Tests`** (NUnit 4, `x64`, referuje `ARBot.Runtime`)
      s testy: (a) chybný profil (`config=` na neexistující soubor) vrátí chybovou hlášku a nevyhodí;
      (b) `maxspeed=0.5` se propíše do `Profile.MaxAllowedSpeed`; (c) bez parametru se `Profile`
      nemění. Pozor: `Profile` je statický — test po sobě hodnotu **vrátí**.
      Pokud `ParamStore.Build` drží globální stav, který se v jednom procesu nedá znovu postavit,
      testy (b) a (c) sloučit do jednoho a zapsat to sem jako past. *`ParamStore.Build` přepisuje
      `Current` a jde volat opakovaně (dělají to už `ParamHandleTests`), takže (b) a (c) jsou zvlášť;
      navíc (d) cizí argument bez `=` není chyba. 4 testy, `[NonParallelizable]`, `TearDown` vrací
      `Profile` i prázdný store.*
- [x] Build obou platforem + testy zelené. Přidat `ARBot.Runtime.Tests` do `ARBot.slnx` jen pro `x64`
      (jako `ARBot.Analyze`).

### Task 3 — `ARBot.Headless` (fáze 2)

- [x] Založit `Src/ARBot.Headless/ARBot.Headless.csproj`: `Exe`, `net10.0`,
      `Platforms=x64;OrangePI`, `ProjectReference` na `ARBot.Runtime`. Žádný NuGet.
- [x] `Program.cs` podle kroků 1–9 výše. `HwSettleMs` přesunout do `ARBotRuntime` a autorun ve
      ViewModelu ho odtud číst (jedna konstanta). *Navíc `SIGHUP` (zavřená ssh session posílá HUP,
      ne TERM) a signál během čekání na HW ukončí proces bez Run.*
- [x] Ošetřit `autorun=true` hláškou „v headless se ignoruje, Run startuje vždy“.
- [x] Přidat do `ARBot.slnx` s platformami `OrangePI`, `x64`.
- [x] Build `x64` i `OrangePI` zelený; `dotnet publish -p:Platform=OrangePI -r linux-arm64` projde
      (jen ověření, že se skládají nativní knihovny; nasazení dělá autor). *45 MB s `config/`, `OSM/`,
      HALArmbian, `libSkiaSharp.so`, `libSystem.IO.Ports.Native.so`. `libNativeLib.so` v něm z tohoto
      stroje NENÍ (`Exists` guard v `Common.csproj`, `.so` se staví ve WSL) — stav před změnou, stejně
      to má UI.*
- [x] Ověření na Windows: `dotnet run --project Src/ARBot.Headless -p:Platform=x64 -- virtualhw=true
      mission=freerun record=Records/headless-test.rec`, ~20 s, Ctrl+C. Záznam otevřít v UI (View)
      a zkontrolovat, že jde až do konce a obsahuje `MissionMsg`/`RobotStateMsg`. Zapsat sem
      výsledek včetně toho, jak dlouho `Stop()` trval. *Běh 25 s s `map=OSM/SyntetickyRovny.osm`,
      mise FreeRun jela 0,80 m/s. Ctrl+C poslán `GenerateConsoleCtrlEvent` z druhého procesu
      (skript v scratchpadu, protože harness Ctrl+C neumí): **`Stop()` trval 4 ms**, proces skončil 19 ms
      po signálu, kód 0. Záznam 476 MB + 240 KB index; místo UI View ho přečetl `ARBot.Analyze types`
      (objektivnější, hlásí poškození): 5 034 zpráv, `RobotStateMsg` 217, `FreeRunMsg` 258 (mise FreeRun
      nemá `MissionMsg`, ten je Robotour), `LocalPlanMsg` 258, `PerfMsg` 21, bez hlášení poškození.*
- [x] Ověření: `config=neexistuje.cfg` skončí kódem 2 s hláškou na stderr. *Kód 2, stderr
      „Chyba konfigurace: Konfiguracni soubor 'D:\Work\ARBot3\neexistuje.cfg' neexistuje.", stdout prázdný.*

### Task 4 — dokumentace

- [x] [doc/architecture.md](architecture.md): nový diagram vrstev, přepsat pravidlo o řídicí
      smyčce, přidat řádek `ARBot.Runtime` a `ARBot.Headless` do „Kam co patří“.
- [x] [doc/record-replay.md](record-replay.md) sekce „Kde to je (namespaces)“: `ARBotRuntime`,
      `ARBotHW` → `Src/ARBot.Runtime`.
- [x] Nový `doc/headless.md`: co headless je a není (jen Run, žádná služba), příkaz spuštění
      přes ssh, ukončení, návratové kódy, kde hledat `crash-*.log`, a co se **neověřilo** na
      zařízení. Odkaz z `CLAUDE.md` do seznamu doménové dokumentace.
- [x] [doc/build-and-platforms.md](build-and-platforms.md): dva nové projekty a jejich platformy.
      *Navíc tabulka všech projektů a jejich `Platforms`, `FTD2XX_NET` teď z `ARBot.Runtime.csproj`.*
- [x] [doc/decisions.md](decisions.md): záznam „runtime do vlastního projektu, ne do Common“
      (směr závislostí) a „headless bez systemd“ (uživatelský příkaz, žádný automatický restart).
- [x] [doc/devlog.md](devlog.md): záznam dne s pravdivým stavem ověření (Windows ano, OrangePi ne).
      *Navíc [configuration.md](configuration.md): dvě zmínky `Program.Main` u `maxspeed`/`safedist`
      → `RuntimeBootstrap.TryConfigure`.*

### Task 5 — předání autorovi

- [x] Ohlásit hotovo, nekomitovat. Sepsat, co má autor ověřit na OrangePi: start přes ssh, že
      `config/` a `OSM/` leží u aplikace, Ctrl+C uzavře záznam, a odečíst z `PerfMsg` CPU procesu
      proti běhu s UI. *Seznam je v [headless.md](headless.md), sekce „Stav ověření“.*
- [ ] Teprve po tom ověření navrhnout fázi 3.
