# IMU, souřadnicové systémy a VN100

## Souřadnicové konvence (platí v celém projektu)

- **World = ENU**: X na východ, Y na sever, Z nahoru. Orientace **matematicky**:
  0 = východ, roste **proti** směru hodinových ručiček (+CCW).
- **Body = FLU**: X vpřed, Y vlevo, Z nahoru.
- Převod orientace ↔ **azimut** (0 = sever, +CW, jako kompas) přes
  `Conversions.Orientation2Azimut` / `Azimut2Orientation`.
- `YawPitchRoll.Yaw` = matematická orientace (0 = východ, +CCW) — NE „vzhledem k severu".

## Projekce kamery: kde je posunutí kamery (`CameraProjection`)

`SetOrientation(transform)` si z montážní matice odvodí `rotationWorld2Cam` jako **inverzi celé
transformace, tedy včetně translace** (`M41..M43` se před inverzí vrací zpět). `Vector3.Transform`
translaci matice uplatňuje — **posunutí kamery se proto už NESMÍ odečítat ručně**. Přesně na tom
`Transform` do 2026-08-14 padal: `new Vector3(x - offset.X, y - offset.Y, -offset.Z)` ho započetlo
podruhé, bod na zemi se promítl ~95 px vedle a blízké body metoda zahodila jako „mimo obraz". Chyba
je úměrná posunutí kamery, takže na kameře v počátku (typická testovací projekce) není vidět vůbec.

Invariant, který to hlídá: **`Transform` musí být inverzní k mapování `Camera2DToCamera3D` +
`Transformation`** (paprsek protnutý s rovinou `z = 0`) — z toho se rendruje virtuální scéna
i staví polární grid. Testuje `VirtualHwOccupancyTest.ProjekceTamZpet_JeInverzniKRenderu`.

### Otevřený úkol: ověřit `TransformBack` (nalezeno 2026-08-14)

⬜ `CameraProjection.TransformBack` (pixel → bod na zemi) vypadá na **stejnou třídu chyby** jako
opravený `Transform`: aplikuje `rotation` — matici **s translací** — na *směrový vektor* paprsku
(`Vector3.Transform(point, rotation)`), takže se do směru přičte posunutí kamery. Při ladění
occupancy vracela metoda pro většinu pixelů `false` a pro zbytek nesmyslné souřadnice (pro bod
zhruba (1; 2) m vyšlo (76; 152)).

**Neověřeno a neopraveno** — bylo mimo rozsah tehdejšího ladění. Používá ho `TargetPoly`
(polygon dosahu kamery na vozovce). Před opravou dohledat všechny konzumenty a napsat na to test
po vzoru `VirtualHwOccupancyTest.ProjekceTamZpet_JeInverzniKRenderu` (round-trip proti mapování
`Camera2DToCamera3D` + `Transformation`, tedy proti témuž invariantu jako u `Transform`).

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

> **Pozor — `IMUState` nenese identitu zdroje** (je `SensorStateBase`, ale ne `INamedMessage`).
> Při dvou IMU (VN100 + T265) tedy nelze poznat, od kterého vzorek je. `ControlLoop` z toho plní
> `RobotState.Pitch`/`Roll` metodou „poslední došlé vyhrává" — mezi tiky to může přeskakovat mezi
> čidly s jinou montáží a kvalitou. (Pitch/roll jsou u obou absolutní z gravitace, takže nejde o chybu
> framu, ale o nekonzistenci kvality — a hlavně to obchází fúzi.) Otevřený úkol a návrh řešení:
> [ekf-fusion.md → Pitch/Roll patří do stavu EKF](ekf-fusion.md).

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
