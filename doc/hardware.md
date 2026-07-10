# Hardware a připojení

> **Pozor: tyto údaje jsou specifické pro konkrétní kus robota / vývojový stroj**
> (přiřazení COM portů závisí na zapojení a OS). Ber je jako orientační; zdroj pravdy je
> konfigurace a kompozice HW v `Src/ARBot/ARBot/Robot/ARBotHW.cs`. Živý stav senzorů
> ukazuje panel **Sensors** v aplikaci (`ARBotHW.Current.Sensors`, vlastnost `ISensor.IsError`).

## Senzory a ovladače

| Zařízení | Rozhraní (HAL) | Připojení (pozorováno) | Ovladač |
|---|---|---|---|
| VN100 IMU (VectorNav) | `IIMU` | **COM5 @ 115200** | `VN100IMU` (ASCII) / `VN100IMUBinary` (binární) |
| Motorový driver SDC2160Ex | `IMotorControl` | UART, **COM9** | `SDC2160Ex` |
| GPS u-blox | `IGPS` | UART | `uBloxGps` |
| Kamera D435 (hloubka) | `ICamera` | USB (RealSense) | `D435Camera` (platformový HAL) |
| Kamera T265 (tracking) | pose → `IMUState` | USB (RealSense) | `T265TrackingCamera` |

Poznámky:
- Přiřazení portů výše bylo odečteno za běhu (VN100 na COM5 potvrzeno; motor na COM9 byl
  v jedné relaci hlášen jako chyba „port nenalezen" — tj. buď jiný port, nebo odpojeno).
- VN100 má konfiguraci (reference frame rotation, binární výstup) uloženou ve flash —
  detaily a montáž viz [imu-and-frames.md](imu-and-frames.md).
- Výběr platformového HAL (D435/T265 wrapper) viz [build-and-platforms.md](build-and-platforms.md).
