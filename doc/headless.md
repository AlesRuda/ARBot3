# Runtime bez UI — `ARBot.Headless`

Řídicí runtime robota (`ARBotRuntime`, `ARBotHW`, řídicí smyčka, fúze, navigace, mise, záznam)
spustitelný **bez Avalonie**: na OrangePi přes ssh, jedním příkazem, bez displeje. Vzniklo
4. 9. 2026 podle [plan-runtime-headless.md](plan-runtime-headless.md) (fáze 1 a 2).

**Proč:** Avalonia bez displeje na zařízení vůbec nenastartuje, takže „spustit přes ssh" dřív
nešlo. Druhý důvod je odlehčení: Avalonia, Dock, Mapsui, fonty a renderovací smyčka stojí paměť
i CPU, které řízení nepotřebuje. **Kolik**, se změří až na zařízení — `PerfMsg` nese CPU procesu,
takže srovnání UI proti headless bude přímo v záznamu ([perf-monitoring.md](perf-monitoring.md)).

## Co to je a co ne

- **Jen Run.** Prohlížení záznamů (`Mode.View`) zůstává v UI na Windows a v `ARBot.Analyze`.
- **Žádný systemd, žádná služba, žádný restart po pádu.** Spouští se vždy uživatelským příkazem
  (ssh + příkaz). Robot, který se sám znovu rozjede, je horší než robot, který stojí. Viz
  [decisions.md](decisions.md), 4. 9. 2026.
- **Konzole je jediný displej.** `Trace` má `ConsoleTraceListener`, takže vše, co runtime hlásí
  do `Trace` (stav senzorů, mise, plánovač, `record=`), jde na stdout a zároveň přes
  `TraceInfoBridge` do záznamu. `Debug.WriteLine` v Release mlčí (pravidlo v CLAUDE.md).
- **Chování Run je totožné s UI** — obě aplikace volají tentýž `ARBotRuntime.Current.Start(Mode.Run)`
  nad tímtéž `ARBot.Runtime`; headless jen nemá odběratele v UI.

## Architektura

```
ARBot.Common ← ARBot.HAL ← ARBot.HALWindows | ARBot.HALArmbian ← ARBot.Runtime ← ARBot (Avalonia UI)
                                                                              ← ARBot.Headless (konzole)
```

| projekt | typ | obsah | platformy |
|---|---|---|---|
| `ARBot.Runtime` | knihovna | `ARBot.Robot.*` (runtime, HW), `CrashLog`, `RuntimeBootstrap`; kopíruje `config/`, `OSM/` | `x64`, `x86`, `OrangePI` |
| `ARBot` | WinExe (Avalonia) | Views, ViewModels, Diagnostics, tenký `Program.cs` | `x64`, `OrangePI` |
| `ARBot.Headless` | Exe (konzole) | `Program.cs` | `x64`, `OrangePI` |
| `ARBot.Runtime.Tests` | NUnit | `RuntimeBootstrapTests` | jen `x64` |

Detail vrstev v [architecture.md](architecture.md).

### `RuntimeBootstrap.TryConfigure`

Společný začátek obou aplikací: složí `ParamStore` z příkazové řádky, vypíše účinnou konfiguraci
do předaného `log` (UI `Debug.WriteLine`, headless `Trace.WriteLine` — obě mají `[Conditional]`,
proto se předávají lambdou) a přenese `maxspeed=` / `safedist=` do `Profile` **beze změny logiky**
proti dřívějšímu `Program.Main`. Vrací `null`, nebo hlášku „Chyba konfigurace: …"; o ukončení
procesu rozhoduje volající (kód `RuntimeBootstrap.ExitCodeBadConfig` = 2 v obou aplikacích).
Pořadí `CrashLog.Install()` → `TryConfigure` → aplikace je záměrné, viz komentář ve třídě
a [configuration.md](configuration.md).

### Co dělá `ARBot.Headless/Program.cs`

1. `Trace.Listeners.Add(new ConsoleTraceListener())`.
2. `CrashLog.Install()`.
3. `RuntimeBootstrap.TryConfigure(...)`; při chybě hláška na stderr a **návrat 2**.
4. Úvodní řádek: režim HW (`virtualhw=`), mise (`mission=`), záznam (`record=`); se zapnutou misí
   výstraha **„robot se rozjede bez dalšího pokynu"** (stejná jako u autorunu v UI).
5. `ARBotHW.Current.WaitReady()` a pak `ARBotRuntime.HwSettleMs` (3 s) na ustálení — **jedna
   konstanta pro autorun v UI i headless**. Kamery a porty se otevírají líně, bez čekání by Run
   startoval nad polovičním HW.
6. `ARBotRuntime.Current.Start(Mode.Run)` — přesně jednou. Cestu záznamu řeší `record=` uvnitř `Start`.
7. Čeká na `Console.CancelKeyPress` (Ctrl+C), `SIGTERM` (`kill <pid>`) nebo `SIGHUP` (zavřená ssh
   session). První signál spustí řádné ukončení; proces si nechá `Cancel = true`, aby ho .NET
   nezabil dřív, než doběhne `Stop()`. Signál během čekání na HW ukončí proces bez Run.
8. `ARBotRuntime.Current.Stop()` — dojede fronty, uzavře záznam, zastaví motory. **Návrat 0.**
9. Neošetřená výjimka projde `CrashLog` (zapíše `logs/crash-*.log` vedle aplikace, dopíše záznam,
   zastaví zdroje) a proces spadne s nenulovým kódem — jako UI.

**Parametry:** vše z registru ([configuration.md](configuration.md)). `autorun=true` se **ignoruje
s hláškou** (Run startuje vždy). UI parametry `selftest=`, `open=`, `worldshot=`, `telemetryshot=`
a `st_*` se ignorují tiše — registr je zná, start neshodí.

## Spuštění

Build / publish (z Windows):

```bash
dotnet build Src/ARBot.Headless/ARBot.Headless.csproj -p:Platform=x64
dotnet publish Src/ARBot.Headless/ARBot.Headless.csproj -p:Platform=OrangePI -r linux-arm64 --self-contained false -o <cíl>
```

Publish pro `linux-arm64` má ~45 MB a obsahuje `config/`, `OSM/`, `ARBot.HALArmbian`,
`libSkiaSharp.so`, `libSystem.IO.Ports.Native.so`. ⚠️ **`libNativeLib.so` v něm z tohoto stroje
není** — `ARBot.Common.csproj` ji kopíruje jen `Exists`, a `.so` se cross-kompiluje ve WSL
([build-and-platforms.md](build-and-platforms.md)). Stejně to má i UI aplikace; na zařízení ji
autor dosud dodával vedle.

Na zařízení (ssh), v adresáři s aplikací:

```bash
dotnet ARBot.Headless.dll config=config/orangepi.cfg mission=robotour record=Records/jizda.rec
```

Na Windows pro zkoušku v simulaci:

```bash
dotnet Src/ARBot.Headless/bin/x64/Debug/net10.0/ARBot.Headless.dll virtualhw=true mission=freerun map=OSM/SyntetickyRovny.osm record=records/headless-test.rec
```

**Ukončení:** Ctrl+C v ssh, nebo `kill <pid>` z druhé session. Zavření ssh session pošle `SIGHUP`,
který se chová stejně (řádný `Stop()`); kdo chce běh přežívající odpojení, pustí ho pod `tmux`/`nohup`
— ale pak nemá jak robota zastavit jinak než `kill` z druhé session nebo nouzovým tlačítkem.

**Návratové kódy:** `0` řádně ukončeno signálem, `2` vadná konfigurace (hláška na stderr),
jiný = pád (viz `logs/crash-*.log`).

**Kde hledat stopy:** `logs/crash-<datum>.log` vedle aplikace (`CrashLog`), záznam podle `record=`
(relativně proti `RepoPaths.RootOrBase()`, což je na zařízení adresář aplikace), stdout ssh session
(rozumné je ho přesměrovat: `… 2>&1 | tee logs/beh.log`).

## Stav ověření

**Windows, 4. 9. 2026 (fáze 1 a 2):**

- Build celého `ARBot.slnx` pod `x64` i `OrangePI` bez chyb; `ARBot.Common.Tests` 1 131 zelených
  (baseline), `ARBot.Runtime.Tests` 4.
- UI po přesunu: self-test s virtuálním HW, `mission=robotour`, `open=robotour,debug`, Run 10 s → Stop
  → exit 0, bez crash logu (78/86 snímků sjízdnosti, 79 překreslení robot-centric pohledu).
- Past 3 (kopírování `config/`, `OSM/` z knihovny do výstupu exe) ověřena smazáním obou složek
  v `Src/ARBot/bin/…` a rebuildem: vrátily se z `ARBot.Runtime` (2/2 profily, 18/18 map).
- Headless s virtuálním HW, `mission=freerun`, `record=`: 25 s běhu, mise se rozjela (0,80 m/s),
  Ctrl+C (`GenerateConsoleCtrlEvent`) → **`Stop()` trval 4 ms**, proces skončil 19 ms po signálu,
  **kód 0**. Záznam 476 MB + 240 KB index, `ARBot.Analyze types` ho přečte celý bez hlášení
  poškození: 5 034 zpráv (`RobotStateMsg` 217, `FreeRunMsg` 258, `LocalPlanMsg` 258, `PerfMsg` 21,
  `Info` 46, `GroundTruthMsg` 217 …).
- `config=neexistuje.cfg` → kód 2, stderr „Chyba konfigurace: Konfiguracni soubor '…' neexistuje."
- Publish `-p:Platform=OrangePI -r linux-arm64` projde.

**Na OrangePi nic z toho neběželo** — první spuštění je na autorovi a bude to zároveň první běh
runtime mimo UI. Co ověřit:

1. Start přes ssh podle příkazu výše; že `config/` a `OSM/` leží u aplikace a profil se načte.
2. Že `WaitReady()` + 3 s stačí i pro skutečné kamery a porty (v UI to stačilo pro autorun).
3. Ctrl+C a `kill <pid>`: záznam se uzavře (`.rec` + `.rec.idx` čitelné v `ARBot.Analyze`),
   motory se zastaví, kód 0. Zavření ssh session (`SIGHUP`) totéž.
4. Odečíst z `PerfMsg` CPU procesu proti běhu s UI — to je číslo, které rozhodne o fázi 3.
5. Zda na zařízení `PosixSignalRegistration` pro `SIGTERM`/`SIGHUP` funguje (na Windows ano,
   mapované na konzolové události).

## Fáze 3 — webový náhled (jen rámec)

`HttpListener` v headless procesu, port z parametru `web=` (0 = vypnuto): `/` HTML stránka
obnovující dva obrázky a text stavu mise, `/camera.jpg` poslední snímek kamery mise (`ImageMsg`
kodek přes SkiaSharp už v Common), `/world.png` půdorys ze SkiaSharpu z `RoadNetwork`,
`RobotStateMsg`, trajektorie a mrkve. Server jako odběratel streamu (`IMessageSink`, latest-wins).
Mapsui ani dlaždice ne. **Návrh až po ověření fáze 2 na zařízení**, kdy bude známo, kolik CPU
headless bez náhledu stojí.
