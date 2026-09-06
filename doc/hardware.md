# Hardware a připojení

> **Pozor: tyto údaje jsou specifické pro konkrétní kus robota / vývojový stroj**
> (přiřazení portů závisí na zapojení a OS). Hodnoty pro Orange Pi jsou **změřené na
> zařízení** (31. 8. 2026), windowsové COM porty jsou orientační. Zdroj pravdy je
> konfigurace a kompozice HW v `Src/ARBot/Robot/ARBotHW.cs`. Živý stav senzorů
> ukazuje panel **Sensors** v aplikaci (`ARBotHW.Current.Sensors`, vlastnost `ISensor.IsError`).

## Senzory a ovladače

| Zařízení | Rozhraní (HAL) | Windows (vývoj) | Orange Pi (změřeno 31. 8. 2026) | Ovladač |
|---|---|---|---|---|
| VN100 IMU (VectorNav) | `IIMU` | **COM5 @ 115200** | `ttyUSB0` — převodník CP2102, **skutečný UART 115200** | `VN100IMU` (ASCII) / `VN100IMUBinary` (binární) |
| Motorový driver SDC2160Ex | `IMotorControl` | UART, **COM9** | `ttyACM0` — Roboteq má vlastní USB CDC | `SDC2160Ex` |
| GPS u-blox | `IGPS` | UART, **COM8** | `ttyACM1` — vlastní USB CDC | `uBloxGps` |
| Kamera D435 (hloubka) | `ICamera` | USB (RealSense) | USB 3.0, **dva kusy** (`8086:0b07`) | `D435Camera` (platformový HAL) |
| Kamera T265 (tracking) | pose → `IMUState` | USB (RealSense) | USB 2.0, hlásí se jako Movidius VPU (`03e7:2150`) | `T265TrackingCamera` |

### RealSense: jeden kontext, boot T265 před D435 (3. 9. 2026)

Všechny RealSense drivery v `ARBot.HALArmbian` (obě `D435Camera` i `T265TrackingCamera`) sdílí
**jeden `Context`** a všechny dotazy `QueryDevices` jdou přes `RealSenseShared.Query` pod jedním
zámkem. `T265TrackingCamera` navíc v konstruktoru **synchronně nabootuje T265** (`RealSenseShared.BootT265`,
strop 10 s) — a protože ji `ARBotHW.SetRealHW` zakládá jako první kameru, stane se to dřív, než se
rozjedou pipeline D435.

**Proč:** v RSUSB backendu librealsense 2.53 spouští *každý* `query_devices` i `tm_boot`, který
nenabootovanou T265 (Movidius `03e7:2150`) otevře a nahraje firmware. Tři kontexty polling po
1 s = tři konkurenční bootery. Důsledky změřené 3. 9. 2026: SIGSEGV v `tm_boot`
(`tm2/tm-boot.h:25`, `dev->open(0)` na zařízení, které se právě přehlašovalo na `8087:0b37`;
minidump + gdb) a T265 bez pózy (v záznamech jen 100 Hz `IMUState` z VN100, T265 by přidala 200 Hz;
smyčka „pipeline pripojena → kamera odpojena (timeout)", protože selhání dotazu se bralo za odpojení).
Rozbor: [devlog.md](devlog.md), 3. 9. 2026.

**Co je jinak v chování:** selhání `QueryDevices` (`null`) už není odpojení ani u T265; pipeline se
bourá až po `StallTimeoutsBeforeRestart` (5) timeoutech po sobě, stejně jako u D435 (tam 3).
`StallRestarts` roste s každým restartem. **Hardware reset** (`RealSenseShared.HardwareResetT265`,
`HardwareResets`): pošle se, když `pipeline.Start` selže na „T265 is running" / „Device is busy"
(kameru opustil klient bez `Stop`, typicky po pádu procesu) nebo po 3 restartech pipeline po sobě bez
jediné pózy; nejvýš jednou za 60 s, protože při příčině, kterou reset nespraví (tma → `SLAM_ERROR
Vision`), by se kamera jinak resetovala dokola a každý reset je ~5 s výpadku i pro gyro/accel.

**Sdílený kontext a boot ověřeny na zařízení 3. 9. 2026 večer** (T265 po replugu nabootovala,
pipeline se připojila, žádný pád); **hardware reset jen buildem**. Póza ten večer nepřišla z jiného
důvodu: firmware hlásil `SLAM_ERROR Vision` — **v místnosti byla tma** a VIO bez obrazu nenastartuje
(gyro 200 Hz a accel 62 Hz přitom chodily). Ověřit za světla. Kontrola: v Info zprávách záznamu
`T265 nabootovala za …` / `pipeline pripojena` bez `restart pipeline`, `IMUState` ~300 Hz.
Přes USB 2 librealsense odmítá streamovat fisheye („use USB 3 or only stream poses"), takže obraz
z rybích ok jde zkontrolovat jen na USB 3.

### T265 nebyla napojená do pipeline (6. 9. 2026)

Při rozboru záznamu `20260906-082403.rec` se ukázalo, že o T265 v něm **není ani zmínka** — žádná
zpráva, žádný řádek logu. Příčina není v kameře: `ARBotHW` ji zakládá bezpodmínečně, ale
`ARBotRuntime.BuildSensorSources` drátoval do pipeline jen `hw.IMU`, `hw.GPS` a `hw.Motor` —
**`hw.TrackingCamera` nikde**. Kamera tedy běžela, otáčela pipeline, soupeřila na USB s oběma D435
a její `MeasurementArived` **nikdo neodebíral**.

Na stránce se přitom ukazovala jako senzor se stářím „—", což vypadalo jako porucha kamery. Tím
padá i domněnka z [devlogu](devlog.md) 3. 9. („T265 by přidala 200 Hz") — nepřidala by nic ani při
plné funkčnosti.

**Napojeno 6. 9. 2026** na pokyn autora („měla by se používat, pokud naběhne"). Protože T265 nemá
magnetometr, její yaw je relativní a **jako kurz se poslat nesmí** — používá se jeho změna. Návrh
a důvody: [ekf-fusion.md](ekf-fusion.md#t265-vio-relativní-yaw-se-používá-jako-úhlová-rychlost-2026-09-06).

### ⚠️ Pravá D435: zamrzlý barevný stream (6. 9. 2026, neuzavřeno)

**Pozorování autora:** „z pravé kamery chodí pořád stejný snímek". Ověřeno nad záznamem
`20260906-082403.rec` (11 minut, `ARBot.Analyze cameras`):

| kamera | barva | hloubka | razítko barvy |
|---|---|---|---|
| `Left 740112071040` | 100 různých ze 100 | 100 různých | 100 různých |
| `Right 740112071021` | **1 různý ze 100** | 100 různých | **1 různé** |

Platí to **od začátku do konce záznamu** a je to **týž obraz** (otisk `9843eac7a2c6d82f` na začátku
i na konci). Zamrzlý snímek je přitom **skutečná, dobře exponovaná fotka** ulice, ne černo ani šum —
stream tedy nejmíň jeden platný snímek dodal a pak se zastavil.

**Kde vada je:** razítko barevného snímku (`CameraFrame.RGBTimeStamp`, z `colorFrame.Timestamp`)
**stojí**, zatímco hloubkové běží. Driver přitom kopíruje obojí při každém grabu ze
`frames.ColorFrame` / `frames.DepthFrame` — takže librealsense vrací v každém framesetu **tentýž
barevný snímek** a naše kopírování je v pořádku. Vada je v librealsense, na USB, nebo v senzoru.

**Není to trvalá vlastnost té kamery:** táž jednotka (sériové číslo `740112071021`) 2. 9. 2026
v záznamu `20260902-225743.rec` dodávala 60 různých barevných snímků ze 60.

⚠️ **Aplikace to do 6. 9. 2026 nepoznala.** Snímky chodily 10 Hz, `ISensor.IsError` hlásil OK, na
stránce náhledu svítila kamera zeleně se stářím 12 ms — a přitom „cesta z RGB" (a tím occupancy grid
a celá mise FreeRun) běžela nad nehybnou fotkou.

**Léčba (6. 9. 2026):** `StreamFreezeWatch` v `ARBot.HAL` sleduje razítka jednotlivých streamů
a když některé stojí **přes 5 s**, driver **zboří pipeline** a příště ji nastartuje znovu — stejnou
cestou jako u zaseknutého streamu bez snímků. Během té doby kamera poctivě **hlásí chybu**
(`connected = false` → `IsError`), takže je porucha konečně vidět na stránce i v panelu místo
klamného OK. Počítadlo `FrozenStreamRestarts` se vede zvlášť od `StallRestarts`: „snímky nechodí"
a „snímky chodí, ale jsou pořád stejné" jsou jiné poruchy.

Práh 5 s je volný schválně — pomalá barva je legitimní (automatická expozice v šeru srazí snímkovou
frekvenci pod periodu čtení, takže se opakované snímky běžně objevují), ale **razítko se při tom
pořád hýbe**. Pět sekund je ~50 čtení; to už žádná expozice nevysvětlí. Tatáž konstanta jako
u T265 („5 s bez pózy → restart pipeline").

**Neověřeno na zařízení** — jestli se pravá D435 restartem pipeline probere, ukáže až běh na robotu;
je možné, že se zasekne znovu a bude to vidět jako rostoucí `FrozenStreamRestarts`. Viz
[devlog.md](devlog.md), 6. 9. 2026.

### Sériové porty na Orange Pi

Na Pi **nejede žádný onboard UART** — všechny tři sériové periferie visí na USB.
Jediný živý `/dev/ttyS*` je `ttyS7` a drží si ho bluetooth (`brcm_patchram_plus`);
`/dev/ttyS0`, které bylo do 31. 8. 2026 v kódu jako odhad, **vůbec neexistuje**.

V `ARBotHW.Init` jsou proto zapsaná jména z `/dev/serial/by-id`, ne `ttyUSB0`/`ttyACM0`:

```
UartAHRS=/dev/serial/by-id/usb-Silicon_Labs_CP2102_USB_to_UART_Bridge_Controller_0001-if00-port0
UartMotor=/dev/serial/by-id/usb-Roboteq_Motor_Controller_SDC2XXX-if00
UartGPS=/dev/serial/by-id/usb-u-blox_AG_-_www.u-blox.com_u-blox_GNSS_receiver-if00
```

**Proč `by-id` a ne `ttyACM0`:** čísla uzlů se přidělují podle pořadí enumerace USB,
takže prohození GPS a motoru po restartu nebo po přepojení kabelu je reálné — a bylo by
**tiché**, protože oba jsou `ttyACM*` a oba se otevřou. Jméno v `by-id` plyne z USB
deskriptoru. (Zbývající past: převodník CP2102 má sériové číslo `0001`, takže druhý
CP2102 v systému by na jméno kolidoval. Dnes je tam jediný.)

**Rychlost má význam jen u IMU.** Motor i GPS jdou přes USB CDC-ACM, kde je nastavená
rychlost bezvýznamná — data tečou stejně na 9600 i na 921600 (proměřeno). `Uart` jim ji
sice nastavuje (115200, resp. 921600), ale nic to nedělá. Skutečný UART je jen za CP2102
k VN100 a tam na 115200 opravdu záleží.

**Motor po startu mlčí, dokud mu něco nepošleš.** Roboteq nezačne posílat telemetrii
(`DI= / C= / V= / A=`), dokud nepřijme první bajt — pasivní posluch tam vidí 0 B na všech
rychlostech. Není to závada. Reálný driver to nepozná, protože `SDC2160Ex` hned
v konstruktoru posílá `^ECHOF 1`. Odpověď na `?FID`: `Roboteq v1.7 SDC2XXX 10/13/2016`.

Porty **znovu najde skript [`OrangePi5Ultra/find-serial-ports.sh`](../OrangePi5Ultra/find-serial-ports.sh)**
(pasivně, bez zápisu do portů): inventura `by-id` / `lsusb` / živých `ttyS*`, pak posluch
a rozpoznání podle toho, co která periferie vysílá, a nakonec výpis hotových `Uart*=`
parametrů.

Poznámky:
- Přiřazení COM portů na Windows bylo odečteno za běhu (VN100 na COM5 potvrzeno; motor na
  COM9 byl v jedné relaci hlášen jako chyba „port nenalezen" — tj. buď jiný port, nebo odpojeno).
- VN100 má konfiguraci (reference frame rotation, binární výstup) uloženou ve flash —
  detaily a montáž viz [imu-and-frames.md](imu-and-frames.md).
- Výběr platformového HAL (D435/T265 wrapper) viz [build-and-platforms.md](build-and-platforms.md).
