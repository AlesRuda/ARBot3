# Self-test — bezobslužné měření výkonu

Aplikace `ARBot` umí **bezobslužný self-test**: s parametrem `selftest=true` sama otevře zadaná okna,
spustí Run, po zadaný čas nechá běžet, zastaví, spočte **souhrn z diagnostického CSV** a **ukončí se**.
Slouží k reprodukovatelnému A/B měření variant vizuální cesty (GC pauzy / latence) bez ruční obsluhy —
viz [decisions.md 2026-08-01](decisions.md), [devlog.md](devlog.md).

Kód: [`Src/ARBot/Diagnostics/SelfTest.cs`](../Src/ARBot/Diagnostics/SelfTest.cs) (config + souhrn),
[`Src/ARBot/ViewModels/MainWindowViewModel.SelfTest.cs`](../Src/ARBot/ViewModels/MainWindowViewModel.SelfTest.cs)
(orchestrace: čekání na HW → otevři okna → Run → čekej → Stop → souhrn → exit).

## Parametry (tvar `klíč=hodnota`, bool = `true`/`false`)

| parametr | default | význam |
|---|---|---|
| `selftest` | `false` | zapne self-test |
| `st_seconds` | `30` | doba běhu Run [s] |
| `st_record` | `false` | Run se záznamem (`records/*.rec`) nebo bez |
| `st_images` | `false` | otevřít okno Images (obrazové vrstvy + overlay) |
| `st_images_active` | `false` | zviditelnit tab Images (jinak zůstane na pozadí — ověří gate viditelnosti) |
| `st_robot` | `true` | otevřít okno Robot-centric |
| `st_name` | `baseline` | štítek varianty (jen do hlavičky souhrnu) |
| `st_out` | `logs/selftest-result.txt` | cesta k souboru souhrnu |
| `no_uart` | `false` | přeskočit UART senzory (IMU/GPS/motor) — čte `ARBotHW` |

**Výstup:** `logs/selftest-result.txt` (přepisuje se) + běhový `logs/traversability-timing-*.csv`.
Souhrn na kameru: `frames, compute avg/p50/p95/max, >100ms %, gen2 (∑ během Process), wait_avg, cam_alloc_avg`.

## Spuštění

Build (x64): `dotnet build Src/ARBot/ARBot.csproj -p:Platform=x64`, pak spustit exe z output složky
(`Src/ARBot/bin/x64/Debug/net10.0/ARBot.exe`) s parametry. Příklad:

```bash
ARBot.exe selftest=true st_name=baseline st_seconds=30 st_robot=true no_uart=true
```

## Doporučené varianty (A/B pro perf)

Spusť postupně, po každé pošli `logs/selftest-result.txt` (jeden řádek/kamera):

```bash
# 1) baseline: čistá vizuální cesta (očekáváme gen2=0, žádné >100ms mimo warmup)
ARBot.exe selftest=true st_name=baseline   st_seconds=30 st_robot=true no_uart=true

# 2) record: totéž + záznam na disk (ověří fix MessageWriter pod zápisem streamu)
ARBot.exe selftest=true st_name=record     st_seconds=30 st_robot=true no_uart=true st_record=true

# 3) images: + okno Images (měří churn WriteableBitmap v ImageDocument)
ARBot.exe selftest=true st_name=images      st_seconds=30 st_robot=true st_images=true no_uart=true

# 4) uart: baseline s připojenými/odpojenými UART senzory (měří jejich příspěvek)
ARBot.exe selftest=true st_name=uart        st_seconds=30 st_robot=true

# 5) full: nejhorší případ (záznam + obě okna + UART)
ARBot.exe selftest=true st_name=full        st_seconds=30 st_robot=true st_images=true st_record=true
```

> **Pozn.:** self-test běží plnou UI aplikaci (kvůli měření UI churnu), jen skriptovaně. Kamera se
> připojuje líně, proto se `st_seconds` počítá od startu Run; warmup (seq 0) se v souhrnu vynechává.
