# IMU, souřadnicové systémy a VN100

## Souřadnicové konvence (platí v celém projektu)

- **World = ENU**: X na východ, Y na sever, Z nahoru. Orientace **matematicky**:
  0 = východ, roste **proti** směru hodinových ručiček (+CCW).
- **Body = FLU**: X vpřed, Y vlevo, Z nahoru.
- Převod orientace ↔ **azimut** (0 = sever, +CW, jako kompas) přes
  `Conversions.Orientation2Azimut` / `Azimut2Orientation`.
- `YawPitchRoll.Yaw` = matematická orientace (0 = východ, +CCW) — NE „vzhledem k severu".

## IMUState — které pole je v jakém framu

`ARBot.Common/Models/IMUState.cs` (viz `<remarks>` třídy):

- **BODY frame** (surová měření senzoru): `Magnetometer`, `Acceleration`,
  `AngularAcceleration`, `AngularVelocity`.
- **Referenční frame ZDROJE** (není u všech senzorů stejný): `Rotation`, `Velocity`,
  `Translation`.
  - **VN100** (má magnetometr): `Rotation` je absolutní atitude v ENU (sever z mag).
  - **T265** (nemá magnetometr): vlastní VIO frame — pitch/roll absolutní (gravitace),
    ale **yaw a poloha jen relativní** (bez severu, NENÍ ENU). Fúze z T265 bere pitch/roll
    absolutně, yaw/polohu jen jako relativní (delta) nebo po zarovnání.
- `OrientationUncertainty` (yaw/pitch/roll 1σ, rad) = zdroj kovariance R pro orientaci.

## VN100 (VectorNav)

Dva drivery v `ARBot.HAL/Devices/AHRS/`:

- **`VN100IMU`** — ASCII výstup.
- **`VN100IMUBinary`** — binární výstup; navíc čte **attitude uncertainty (YprU)** →
  `IMUState.OrientationUncertainty`. Bere VN **Ypr** (yaw = azimut z magnetometru),
  převádí na ENU math (`Azimut2Orientation`) a surové gyro/accel/mag **FRD→FLU**
  (negace Y, Z → `AngularVelocity.Z` je rovnou ENU yaw rate CCW+, `Acceleration.Z` +g nahoru).

### Montáž a reference frame na konkrétním robotu (ověřeno)

- Fyzická montáž VN100: **X dozadu**, Y vpravo, Z nahoru.
- Na senzoru je uložená (ve flash, přežívá vypnutí) **reference frame rotation
  `diag(-1,1,-1)`** → výstup je robotem zarovnaný **FRD** (X vpřed, Y vpravo, Z dolů) / NED.
- Připojení: **COM5, 115200**. Registr 26 (reference frame rotation) je aktivní a uložený;
  ověřeno read-only diagnostikou (VNRRG).
- Živý binární paket začíná `FA 14 00 07 02 03 …` = skupiny Imu|Attitude, masky
  `0x0700` (Mag|Accel|Gyro) a `0x0302` (Ypr|YprU|YprRate) — přesně layout, který
  `VN100IMUBinary` dekóduje.

> Pozn.: dřívější „yaw 180° na severu" byla stará/špatná konfigurace senzoru; po factory
> resetu + novém nastavení reference frame je heading správně. Frame se řeší na senzoru
> (reference frame rotation), NE softwarovým offsetem.

### Konfigurace senzoru

Konfigurace VN100 (včetně reference frame rotation a binárního výstupu) je uložena
**ve flash senzoru** a přežívá vypnutí — v kódu se persistentně nezapisuje. Export
nastavení z VectorNav Control Center je v rootu repa
(`vn100-2026-7-8-nastavei z arbot2.sencfg`). Diagnostika jde dělat read-only přes
`VNRRG` (žádný zápis/flash) — např. registry 1 (model), 6/7 (async), 8/9/27 (YPR/qtn/YMR),
26 (reference frame rotation).
