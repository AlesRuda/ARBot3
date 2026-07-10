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

## Poznámka: probíhající migrace z ARBot2

Část kódu se portuje ze staršího ARBotu (ARBot2). Starý/nekompilovatelný kód bývá vyřazen
z buildu přes `<Compile Remove>` v `.csproj` (např. legacy EKF) a slouží jako reference —
nemazat, dokud novou implementaci nepotvrdí testy.
