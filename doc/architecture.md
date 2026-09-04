# Architektura a struktura projektů

Zdroj je v `Src/`. Hlavní projekty a **směr závislostí** (šipka = „referencuje"):

```
ARBot (Avalonia UI)     ──►  ARBot.Runtime  ──►  ARBot.HALWindows / ARBot.HALArmbian  ──►  ARBot.HAL  ──►  ARBot.Common
ARBot.Headless (konzole) ──►  (týž ARBot.Runtime)                                      (tranzitivně Common)
```

- **`ARBot.Common`** — doménové modely, algoritmy, EKF fúze (`Fusion/`), souřadnice,
  navigace. **Nesmí referencovat HAL** (je „dole").
- **`ARBot.HAL`** — rozhraní a společné ovladače senzorů (`IIMU`, `IGPS`,
  `IMotorControl`, `ISensor`, `IUart`, …). Referencuje `Common`.
- **`ARBot.HALWindows` / `ARBot.HALArmbian`** — platformové implementace (RealSense
  kamery apod.), viz [build-and-platforms.md](build-and-platforms.md).
- **`ARBot.Runtime`** (od 4. 9. 2026) — **řídicí runtime bez UI**: `ARBotRuntime` (graf zpracování,
  fúze, řízení, mise, záznam), kompozice HW `ARBotHW`, `CrashLog`, `RuntimeBootstrap` (bootstrap
  parametrů). Namespace zůstal `ARBot.Robot`. Sám si vybírá platformový HAL podle `Platform`
  a nese do výstupu `config/*.cfg` a `OSM/*.osm`. **Nesmí znát UI** — žádný `PackageReference`
  na Avalonia/Dock/Mapsui; test pravidla je `grep "using Avalonia" Src/ARBot.Runtime` prázdný.
- **`ARBot`** — Avalonia UI (Dock, MVVM); s runtime mluví přes `ARBotRuntime.Current`
  (`Stream.Connect(sink)` a pár vlastností). Referencuje jen `ARBot.Runtime`.
- **`ARBot.Headless`** — konzolová aplikace pro ssh na OrangePi: bootstrap → čekání na HW → Run →
  Ctrl+C/SIGTERM → Stop. Jen Run, žádná služba. Viz [headless.md](headless.md).
- Testy: `ARBot.Common.Tests`, `ARBot.HAL.Tests`, `ARBot.Runtime.Tests` (NUnit).

## Kam co patří

- **Fúzní jádro** (EKF) je čistě doménové → `ARBot.Common/Fusion` (bez HAL, bez UI).
- **`SensorAdapters` (napojení reálných senzorů na fúzi), kompozice HW a řídicí smyčka robota
  patří do `ARBot.Runtime`** — protože potřebují jak `ARBot.Common` (Fusion), tak `ARBot.HAL`
  (`IIMU`/`IGPS`/`GPSState`), a **nesmí znát UI**, aby šly spustit bez displeje. Do `Common`
  ani `HAL` je dávat nelze (směr závislostí: přesun by vrstvu obrátil). Do 4. 9. 2026 to bylo
  v aplikaci `ARBot` (`Robot/`), viz [decisions.md](decisions.md).
- **UI aplikace `ARBot`** — Views, ViewModels, `Diagnostics/` (self-test, snímky obrazovky:
  měří UI čítače a potřebují Avalonii), `Telemetry/`, `FilteredTraceLogSink`, tenký `Program.cs`.
- **`ARBot.Headless`** — jen `Program.cs` nad `ARBot.Runtime`; co potřebují obě aplikace,
  patří do runtime, ne sem.
- **UI dokovatelné dokumenty/nástroje**: viz
  [Src/ARBot/ARBot/Views/README.md](../Src/ARBot/ARBot/Views/README.md).

## Konvence: převod doménového stavu na zprávu — `ToLogMessage()`

Doménové/algoritmické objekty si **samy vyrábějí svou log/telemetrickou zprávu** metodou
**`ToLogMessage()`** (vrací příslušný `*Msg` odvozený z [`Message`](../Src/ARBot.Common/Logs/Message.cs)),
případně `ToLogMessages()` pro více zpráv. **Konverzi vlastní doména, ne zpráva.**

- Směr závislosti je **doména → `Logs` (její zpráva)**, ne naopak. Zpráva (`Message`) zůstává **pasivní DTO**
  (nese jen data + serializaci `ToData`/`FromData`) a nezná doménový typ, ze kterého vznikla.
- Nezakládej opačné statické tovární metody na zprávě (`XxxMsg.FromDomain(...)`) — místo toho přidej
  `ToLogMessage()` na doménový objekt.
- Zavedeno napříč projektem: `ICP`, `Collider2`, `EKFStep`, `VoronoiNavigation`/`RRT` (navigace),
  `RoadNetwork` (OsmNav) → `MapMsg`, atd.

## Poznámka: probíhající migrace z ARBot2

Část kódu se portuje ze staršího ARBotu (ARBot2). Starý/nekompilovatelný kód bývá vyřazen
z buildu přes `<Compile Remove>` v `.csproj` (např. legacy EKF) a slouží jako reference —
nemazat, dokud novou implementaci nepotvrdí testy.
