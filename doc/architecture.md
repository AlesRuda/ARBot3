# Architektura a struktura projektů

Zdroj je v `Src/`. Hlavní projekty a **směr závislostí** (šipka = „referencuje"):

```
ARBot (Avalonia app)  ──►  ARBot.HALWindows / ARBot.HALArmbian  ──►  ARBot.HAL  ──►  ARBot.Common
                      └────────────────────────────────────────────────────────►  (tranzitivně Common)
```

- **`ARBot.Common`** — doménové modely, algoritmy, EKF fúze (`Fusion/`), souřadnice,
  navigace. **Nesmí referencovat HAL** (je „dole").
- **`ARBot.HAL`** — rozhraní a společné ovladače senzorů (`IIMU`, `IGPS`,
  `IMotorControl`, `ISensor`, `IUart`, …). Referencuje `Common`.
- **`ARBot.HALWindows` / `ARBot.HALArmbian`** — platformové implementace (RealSense
  kamery apod.), viz [build-and-platforms.md](build-and-platforms.md).
- **`ARBot`** — Avalonia UI (Dock, MVVM) + kompozice HW (`Robot/ARBotHW.cs`).
  Referencuje platformový HAL (a tranzitivně Common).
- Testy: `ARBot.Common.Tests`, `ARBot.HAL.Tests` (NUnit).

## Kam co patří

- **Fúzní jádro** (EKF) je čistě doménové → `ARBot.Common/Fusion` (bez HAL, bez UI).
- **`SensorAdapters` (napojení reálných senzorů na fúzi) a řídicí smyčka robota patří
  do aplikace `ARBot`** — protože potřebují jak `ARBot.Common` (Fusion), tak `ARBot.HAL`
  (`IIMU`/`IGPS`/`GPSState`). Do `Common` ani `HAL` je dávat nelze (směr závislostí).
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
