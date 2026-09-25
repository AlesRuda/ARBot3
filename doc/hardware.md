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

### Výpadky kamer za provozu — rozbor (11. 9. 2026, NEOVĚŘENO NA HW)

Rešerše k opakovaným výpadkům D435 za provozu („kamera se odmlčí a bez zásahu se neprobere").
**Nic z toho není změřené na robotu** — je to rozbor existujících měření, zdrojáků librealsense
a cizích hlášení. Plán testu je na konci sekce; **při nejbližší práci u robota se dělá jako první.**

**Je to cizí, dobře zdokumentovaný problém, ne vada našeho kódu.** Táž porucha se táhne
librealsense napříč verzemi 2.35–2.50 ([#9191](https://github.com/IntelRealSense/librealsense/issues/9191),
[#5412](https://github.com/IntelRealSense/librealsense/issues/5412)) a Intel ji **nemá vyřešenou** —
#9191 je otevřená, bez root cause, s jediným doporučením „restart aplikace". Naše léčba
(detekce + zbourání pipeline + reconnect) je přesně to, k čemu ve vláknech všichni dojdou.

#### ❌ Hypotéza `CLEAR_HALT` 1 → ~72 je VYVRÁCENÁ měřením (14. 9. 2026)

**Odpojení T265 na `CLEAR_HALT` nesáhlo.** Přímé srovnání na zařízení, týž runtime, tytéž dvě
D435, normalizované na dobu běhu služby:

| boot | T265 | běh služby | `CLEAR_HALT` | **za minutu** |
|---|---|---|---|---|
| 13. 9. 15:56–21:32 | **ano** | 335,4 min | 2477 | **7,39** |
| 14. 9. 16:42–17:25 | ne | 42,4 min | 333 | **7,85** |
| 14. 9. 19:20–22:14 | ne | 168,2 min | 1195 | **7,10** |

Čekalo se 72× dolů; naměřeno **7,4 → 7,1**, tedy nic. Přepočteno na referenční okno 375 s vychází
bez T265 **~44 událostí**, kdežto referenční hodnota je **1**. Ta sedmdesátka nebyla způsobená
T265 — obě čísla (1 i 72) pocházejí z jiné zátěže než z našeho runtime, takže se s ním
neporovnávají.

⚠️ **A hlavně: `CLEAR_HALT` u nás NENÍ podpis poruchy, je to šum.** Teče **plynule 3–15 za minutu
v každé minutě** (obě kamery, endpointy 0x82 i 0x84), zatímco zamrznutí streamu přijde jednou za
~17 minut. Událost, která nastane ~120× častěji než porucha, o poruše nic neříká — jako měřidlo
je tedy `CLEAR_HALT` **mrtvý** a nemá smysl podle něj nic dalšího rozhodovat. Sedí k tomu i to, že
**chybí celá zbylá trojice z #9191** (`EP not empty`, `refuse reset`, `Can't enqueue URB`):
za 2,9 hodiny běhu **nula výskytů**.

<details>
<summary>Původní znění hypotézy (ponecháno kvůli dohledatelnosti)</summary>


V [POSTUP.md](../OrangePi5Ultra/POSTUP.md) je z 1. 9. 2026 změřeno, že dvě D435 na jednom hubu
jedou **375 s na 30/30 fps bez timeoutu**, ale po přidání T265 vyskočí `CLEAR_HALT` **z 1 na ~72**
a prvních ~100 s kolísá snímková frekvence na 20–30 fps.

**To číslo tehdy nikdo nespojil s ničím dalším — a přitom `USBDEVFS_CLEAR_HALT for active endpoint`
je přesně dmesg podpis poruchy z #9191/#5412** (chodí ve trojici s `EP not empty, refuse reset`
a `Can't enqueue URB while manually clearing toggle`; u nás v dmesg 31. 8. 2026 u zaseknuté pravé
D435). Souběh s T265 tedy **stejnou třídu chyby zmnožuje 72×**. Je to zatím nejsilnější vodítko
k [zamrzlému barevnému streamu](#-pravá-d435-zamrzlý-barevný-stream-6-9-2026-neuzavřeno) výš.

</details>

#### Propustnost to není — spočítáno

Jedeme RGB 640×480 a hloubku 480×270, obojí 30 fps (`D435Camera.Init`):

| | na kameru | dvě kamery |
|---|---|---|
| RGB 640×480@30 | ~147–221 Mbps (dle formátu) | |
| hloubka 480×270@30 | ~62 Mbps | |
| **celkem** | **~210–283 Mbps** | **~420–570 Mbps** |

Proti USB3 (5 Gbps hrubě, reálně ~3,2) je to **13–18 %**. Intelí pravidlo „nepoužívej jeden USB3
řadič pro víc RealSense zařízení" míří na sestavy 1280×720+ a na naše rozlišení nedosáhne;
souhlasí s tím i to naměřené 375 s bez timeoutu. **Hub a sdílená linka jsou tedy z podezřelých
venku** — zbývá souběh s T265 a hostitelský řadič.

⚠️ **Obrácená strana té tabulky: na USB 2.0 se dvě kamery NEVEJDOU** (480 Mbps hrubě, reálně
~280–320 proti potřebným 420–570). Přesně proto 2. 9. 2026 „nejely vůbec", když po bootu naskočily
jako `speed=480` — viz POSTUP.md, kde je i léčba (fyzický replug).

#### RSUSB vs. kernel backend — a proč na té volbě T265 nevisí

Na Pi jedeme **RSUSB backend** (volba při bring-upu, důvod v devlogu zapsaný není). Intel ho pro
produkci nedoporučuje a explicitně u něj uvádí **limitaci pro multi-cam**
([#9157](https://github.com/IntelRealSense/librealsense/issues/9157)); my na něm máme tři zařízení.

**Dosud se mělo za to, že RSUSB opustit nelze kvůli T265. To je omyl.** T265 **není UVC zařízení**
(hlásí se jako Movidius `03e7:2150` → `8087:0b37`), takže ji V4L2 obsloužit ani nemůže — jde přes
`src/tm2` nad libusb, a to **v obou backendech stejně**. `FORCE_RSUSB_BACKEND` řídí jen UVC/HID
cestu, tedy D435. Přechod na kernel backend by tedy T265 **nevzal**.
⚠️ Ověřeno jen ze struktury zdrojáků `v2.53.1` (`rsusb-backend` a `linux` jsou UVC backendy, `tm2`
stojí vedle nich), **ne buildem**. Nepřímo to podporuje i to, že cizí hlášení „s RSUSB T265 nejede"
u nás neplatí — univerzální pravidlo tam žádné není.

Cena přechodu je **kernel patching**, které je na 5.x/6.x notoricky rozbité (řada otevřených issues:
„Exec format error", selhání na 5.15) a na Armbianu s Rockchip kernelem to bude horší než na Ubuntu.

#### Co od změny verze čekat: nic

**Žádné hlášení neukazuje, že by ten výpadek vyřešila jakákoli verze.** Reportér #9191 prošel
2.38.1 → 2.45.0 bez efektu. Zkusit jinou verzi je levné, ale očekávaný přínos je blízký nule —
a nahoru stejně nelze, viz [build-and-platforms.md](build-and-platforms.md).

#### Podezřelý, který zbyl: hostitelský řadič (RK3588 / DWC3)

Na RK3588 je zdokumentované, že USB3 UVC kamera streamuje **10–276 s a pak umře**
([rockchip-linux/kernel#349](https://github.com/rockchip-linux/kernel/issues/349),
[Arducam forum](https://forum.arducam.com/t/b0495-usb3-2-3mp-ar0234-complete-stream-failure-on-orange-pi-5-max-rk3588-xhci-extensive-testing-fx3-firmware-fix-request/9185));
jediné, co tam zabralo, byl vynucený pád na USB 2.0. ⚠️ Je to **jiná kamera** (Arducam AR0234), tedy
důkaz o řadiči, ne o RealSense. Pro nás je podstatné, že jeden z našich USB3-A portů je **OTG řadič
`fc000000` přepnutý overlayem `dwc3-host`** (viz POSTUP.md krok 1) — a ten nález míří přesně na DWC3.

#### ✅ Test u robota PROVEDEN (14. 9. 2026) — T265 výpadky D435 nezpůsobovala

Kroky 1 a 2 plánu jsou odbyté. `lsusb -t`: obě D435 visí na hubu `2-1` pod jedním řadičem
(`xhci-hcd.7.auto`, tedy OTG `fc000000` přepnutý na host), VN100 a GPS jdou po **jiné sběrnici**
(`bus 001`, USB2 root hub) — sdílenou linku s kamerami tedy nemají.

Změřeno přes hranici odpojení T265 (13. 9. večer), vše na stojícím robotu bez mise,
normalizované na dobu běhu služby:

| | s T265 (13. 9.) | bez T265 (14. 9.) |
|---|---|---|
| běh služby | 335,4 min | 210,6 min (dva boty) |
| `CLEAR_HALT` / min | 7,39 | 7,10–7,85 |
| **zamrznutí streamu / hod** | **3,4** (19×) | **3,6–4,2** (13×) |
| tvrdé záseky / hod | 2,3 (13×: Left 8, Right 5) | 1,4 (4×: **jen Right**) |

- **Zamrzání streamu se nezměnilo vůbec** (3,4 → 3,6–4,2 za hodinu). To je ta porucha, kvůli které
  se T265 podezřívala, a ta odpojením nezmizela.
- **Tvrdé záseky** (`failed to set power state` → recyklace kontextu) klesly z 2,3 na 1,4 za hodinu.
  ⚠️ **Není to důkaz o USB:** 13. 9. si o zotavení říkala **sama T265** 35×, a protože kontext
  sdílejí všechny kamery, každé takové zotavení strhlo obě D435 dolů — teardown navíc je právě to,
  z čeho zásek vzniká. Ubylo tedy nejspíš **vynucených teardownů**, ne rušení na sběrnici.
  Navíc se ten den několikrát měnil kód zotavení, takže se čísla srovnávají jen orientačně.
- ✅ **Podezření na PORT se naopak potvrdilo.** Všechny 4 dnešní záseky jsou na `Right`, a ta sedí
  na portu **`2-1.3`** — tedy na tom, na kterém bylo 13. 9. po prohození kamer 8 záseků z 8.
  Bilance je teď **12 z 12 na `2-1.3`**, ať na něm visí kterákoli kamera. Přiřazení portu ke kameře
  je změřené, ne odvozené: během 3,5minutového výpadku `Left` chodil `CLEAR_HALT` **jen z `2-1.3`**
  (kontrolní okno se streamem obou kamer má 15 : 15).
- **Další krok:** už ne T265 ani backend/verze SDK, ale **fyzická větev `2-1.3`** — vyměnit kabel,
  přesadit kameru na port mimo hub, případně jinou větev hubu.
  ▶️ **Uděláno 14. 9. 2026 večer:** autor robota vypnul a kameru z `2-1.3` přepnul na **volný port
  téhož hubu**. Vyhodnotit 15. 9.

#### Párování kamera ↔ USB sériové číslo (kvůli přesazování na jiné porty)

Čísla portů se přepnutím kabelu mění, takže bilance záseků „na portu X" se po každém přesazení
musí navázat znovu. Kamera se pozná z `/sys/bus/usb/devices/<port>/serial` — ⚠️ **jenže tam je
JINÉ číslo, než jakým se kamera hlásí v logu** (USB deskriptor proti sériovému číslu z
librealsense). Dvojice jsou změřené 14. 9. 2026:

| v logu (librealsense) | `/sys/…/serial` (USB) | port 14. 9. |
|---|---|---|
| `Left 740112071040` | `828313020627` | `2-1.2` |
| `Right 740112071021` | `828313020236` | `2-1.3` ← přesazena |

Sloupec s portem platí jen k tomu datu; první dva jsou trvalé. Zjištění portu pak je:

```bash
for d in /sys/bus/usb/devices/2-1.*; do echo -n "$d: "; cat $d/serial 2>/dev/null; done
```

*(Přiřazení se poprvé muselo dobývat oklikou — během výpadku jedné pipeline chodil `CLEAR_HALT`
jen z jednoho portu. Díky téhle tabulce už to podruhé potřeba není.)*

#### Kolik běhu je potřeba, aby „zlepšilo se to" něco znamenalo

Výchozí míry bez T265 (14. 9.): **tvrdý zásek 1,4/hod**, **zamrznutí streamu 3,6–4,2/hod**.
Z toho plyne, co je a co není důkaz — při 1,4/hod:

| čistý běh | čekaný počet záseků | „nula" znamená |
|---|---|---|
| 1 h | 1,4 | **nic** (i beze změny vyjde nula ve čtvrtině případů) |
| 3 h | 4,2 | slušný signál (p ≈ 1,5 %) |
| 5 h | 7 | přesvědčivé (p ≈ 0,1 %) |

⚠️ **Měř čistý běh služby, ne hodiny na hodinách** — restarty a reprodukční testy dobu běhu
krátí a bez normalizace vyjde zlepšení tam, kde jen kratší dobu běželo.

✅ **Driver od 11. 9. 2026 hlásí typ USB linky** (`UsbLinkCheck` v `ARBot.HAL`, obě platformy,
14 testů). Do té doby se z `Device.Info` četlo jen `SerialNumber` a `Name`, takže kamera naběhlá na
480 Mbps vypadala jako porucha streamu, ne jako špatná linka — a přesně to 2. 9. 2026 stálo hodinu
hledání zvenčí. Nově je za hláškou „pipeline pripojena" i `USB 3.2`, a při USB 2.0 **varování
i s léčbou** (fyzický replug), protože hloubka a barva se tam pro dvě kamery nevejdou — viz výpočet
výš. **Neznámá hodnota není poplach** (librealsense ten údaj u některých zařízení nehlásí), stejná
konvence jako u brány kvality GPS. Čtení je v `try`, takže diagnostika nemůže shodit připojení —
precedens z 6. 9. 2026, kdy hlídka zamrzlého streamu shodila každý grab. **Na HW neověřeno**
(na Windows bez kamery se to neprojeví; smysl to dá až na Pi).

**Co je naopak z podezřelých venku:** **VN100 a GPS na témž hubu.** Na USB3 hubu jdou USB2 zařízení
přes transaction translator, tedy po fyzicky oddělených vodičích než SuperSpeed lanes — o šířku
pásma kamerám nekonkurují. A objemem to není nic: VN100 ~9 kB/s, GPS míň.

### ✅ Proč se kamera po restartu pipeline někdy už nevzpamatuje: **zasekne se NÁŠ PROCES** (12. 9. 2026, ZMĚŘENO NA HW)

Záznam `records/test/20260912-125851.rec` (mise Track) + `dmesg` a `journalctl` z Orange Pi. Poprvé
je celý řetěz změřený, ne odvozený — a **zaseknutou kameru se podařilo zastihnout živou**.

**Co se stalo (časy z Pi):**

| čas | co |
|---|---|
| 13:03:34 | `USBDEVFS_CLEAR_HALT` na endpointu **0x82 = hloubka** kamery `2-1.3` (Left) |
| 13:03:36 | hlídka: `Left: HLOUBKA zamrzla (5,0 s stejné razítko, barva 0,0 s) -> restart pipeline` |
| 13:03:39 | **jádro: `uvcvideo 2-1.3:1.1 … Found UVC 1.50 device`** — kernelový ovladač si kameru vzal zpátky |
| 13:03:36–13:09:19 | `QueryDevices selhalo: failed to set power state`, **300 pokusů / 343 s**, do restartu služby |
| 13:09:32 | restart služby → obě kamery se vyčetly a jedou |

**Zařízení bylo celou dobu zdravé.** V `dmesg` není o `2-1.3` mezi 13:03:39 a 13:09:32 **ani řádka** —
žádné odpojení, žádný reset portu, žádná chyba. Druhá D435 (`2-1.2`) i T265 jely bez přerušení
(změřeno z indexu záznamu: Right 9,4 Hz před i po, T265 199,7 Hz před i po). **Vada je tedy v našem
procesu, ne v kameře ani na sběrnici.**

#### Zaseknutá kamera se podařilo zastihnout živou a **vyzkoušet léčbu** — dvě hypotézy padly

Táž porucha nastala znovu (13:37, jiný běh), takže šlo experimentovat na zaseknuté kameře, zatímco
služba běžela. První pohled sváděl na `uvcvideo`:

```
2-1.3:1.0 … 1.4  driver=uvcvideo     <- zaseknutá Left
2-1.2:1.0 … 1.4  driver=usbfs        <- fungující Right (drží ji librealsense)
```

Po zbourání pipeline libusb pustí rozhraní a jádro na ně znovu naváže `uvcvideo` (vzniknou
i `/dev/video*` uzly, časem přesně v okamžiku zbourání). Vypadalo to tedy, že RSUSB backend nemůže
rozhraní zabrat zpět. **Měření to ale vyvrátilo — obě přímé léčby selhaly:**

| co se zkusilo | výsledek |
|---|---|
| **reset USB portu** (`ioctl USBDEVFS_RESET`, jako `ales` bez rootu — udev dává uzlům `crw-rw-rw-`) | zařízení se v `dmesg` znovu vyčetlo, `QueryDevices` **selhává dál** |
| **odpojit `uvcvideo`** ze všech pěti rozhraní (`/sys/bus/usb/drivers/uvcvideo/unbind`) | žádné rozhraní nemá ovladač, `QueryDevices` **selhává dál** |
| **zabrat rozhraní JINÝM procesem** (`USBDEVFS_CLAIMINTERFACE` z pythonu) | **všech pět rozhraní zabráno bez problému** |

**Zařízení je tedy zcela volné a zdravé — zaseknutý je náš proces.** `uvcvideo` je následek
(uvolněná rozhraní jádro prostě zase obsadí), ne příčina. Poisonovaná je vnitřní stav
librealsense/libusb v tom **jednom** procesu, a **nic na úrovni OS to nespraví** — proto pomůže
jedině restart služby. `lsof` k tomu ukazuje, že proces má na uzlu zaseknuté kamery pořád jeden
otevřený deskriptor.

⚠️ **Co se NEZKOUŠELO a proč:** `rs-enumerate-devices` (je na Pi v `/usr/local/bin`) by dal přímý
důkaz na úrovni librealsense, ale **druhý kontext v jiném procesu je přesně to, co 3. 9. 2026
vyrobilo SIGSEGV v `tm_boot`** — nad běžící službou se to dělat nemá. Až s zastavenou službou.

A protože `D435Camera.EnsureConnected` se bez `DevicePresent() == true` o `pipeline.Start` ani
nepokusí, je to **slepá ulička až do konce běhu**.

**Jak často to je** (z journalu, všechny běhy): **82 zamrznutí streamu** (57 barva, 25 hloubka) —
naprostá většina se **sama zotaví za 2–13 s**. Slepá ulička je vzácnější, ale drahá:

| kdy | kamera | trvání | konec |
|---|---|---|---|
| 11. 9. 22:06 | T265 | **1845 s** (1601 pokusů) | sama |
| 12. 9. 13:03 | Left | 343 s (300 pokusů) | restart služby |
| 12. 9. 13:13 | Left | 8 s | sama |
| 12. 9. 13:37 | Left | 89 s+ | (zastiženo živé) |

**Dopad na řízení:** robot jel dál po jedné kameře — `LocalPlanMsg` spadlo z **17,7 na 9,4 Hz**
(plánuje se na snímek), mise doběhla. Není to tedy fatální porucha, ale polovina zorného pole.

⚠️ **Hlídka zahodila i funkční barvu.** Zamrzla hloubka, barva chodila — a `Teardown` bourá pipeline
celou. Při `backproject=npu` je přitom barva to, z čeho se počítá pravděpodobnost cesty.

#### ✅ Léčba nalezena, naimplementována a ZMĚŘENA NA SKUTEČNÉ PORUŠE (13. 9. 2026)

**Recyklace sdíleného `Context`u zaseknutou kameru probere.** Dělá to
`CameraRecoverySupervisor` (viz [plan-drive-hold.md](plan-drive-hold.md)) — po 15 selhaných
dotazech vezme `StopHold`, počká, až robot skutečně stojí, vymění kontext a pustí.

**Změřeno na spontánní poruše** (13. 9. 2026, 16:49:55–16:50:24, robot stál bez mise):

| čas | co |
|---|---|
| 16:49:55 | `Left: BARVA zamrzla (5,0 s stejné razítko)` → teardown |
| 16:49:57–16:50:13 | `QueryDevices selhalo: failed to set power state` **15×** — týž zásek jako dřív |
| 16:50:14 | supervizor vzal hold, potvrzeno stání |
| 16:50:14–16 | obě D435 uvolnily handle **na svém vlákně** |
| 16:50:16 | kontext vyměněn, hold uvolněn (držel 2 s) |
| 16:50:24 | **obě kamery připojené zpátky** |

**Od zamrznutí do obou kamer zpátky 29 s** proti 343 s a 22 minutám v předchozích epizodách,
kde to skončilo až restartem služby. Druhá, zdravá kamera projde recyklací s sebou (kontext je
sdílený) a vrátí se také.

**Druhá epizoda týž den potvrdila první** (17:05:07 zamrzla barva → 17:05:25 supervizor →
17:05:35 obě kamery zpátky, **28 s** proti 29 s u první). Doba je daná hlavně prahem detekce:
~18 s čekání na 15 selhaných dotazů, pak ~10 s na výměnu kontextu a připojení. Kdyby bylo potřeba
rychleji, škrtá se na `QueryFailuresBeforeRecovery` — ten práh ale chrání před ojedinělým
selháním dotazu nad běžícími streamy (vidáno 1. 9. 2026).

#### ⚠️ Statistika sezení: léčba funguje, ale LEVÁ kamera je něčím jiná

Sezení 13. 9. 2026, 16:17–17:52 (95 min), robot **stál bez mise**:

| kamera | zamrznutí streamu | z toho se sama zotavila | z toho skončila zaseknutím |
|---|---|---|---|
| **Left** | 5 | **0** | **5** — všechna vyléčil supervizor |
| **Right** | 4 | **4** (za 2–4 s) | 0 |

**Zotavení vyšlo 5× z 5**, pokaždé za 28–29 s. Ale ten rozpad podle kamer je nápadný a na vzorku
devíti už není náhoda k odmávnutí (Fisherův test p ≈ 0,008): **levá kamera se po zamrznutí zasekne
pokaždé, pravá nikdy**. To ukazuje na něco fyzického — port, kabel, větev hubu — ne na obecnou
vlastnost driveru. **Další diagnostický krok je prohodit kamery mezi porty**: jestli se zaseknutí
přestěhuje s portem, je to železo; jestli s kamerou, je to ta kamera.

⚠️ **A hlavně: supervizor je obvaz, ne léčba příčiny.** Levá kamera v 17:05:35 naběhla po zotavení
a **za 13 s zamrzla znovu** (17:05:48) → další zaseknutí → další zotavení v 17:06:27. Za jízdy
by to znamenalo, že robot každých pár minut na ~28 s zastaví. Pořád je to nesrovnatelně lepší než
mrtvá kamera do konce běhu, ale **není to vyřešené**.

⚠️ Podíl zaseknutí je tu **5 z 9**, tedy mnohem víc než dřívější odhad 4 % (3 z 82 přes celý
journal) — ten starší je nejspíš vedle, epizody se v něm hledaly zpětně a část mohla skončit
restartem služby, aniž se to poznalo.

⚠️ **Mechanismus to pořád nevysvětluje, jen léčí.**
Co bylo vyzkoušené a **nefunguje** (nezkoušej znovu): ~~reset USB portu~~ a ~~odpojení
`uvcvideo`~~ — obojí změřeno 12. 9. 2026, viz tabulka výš.

#### Prohození kamer mezi porty (13. 9. 2026 večer) — předběžně to vypadá na PORT

Autor prohodil kamery mezi USB porty (ověřeno na sériových číslech: `2-1.2` a `2-1.3` si je
vyměnily). Předtím se zasekávala vždy **Left** (SN 740112071040), 5× z 5; pravá nikdy.

**Tvrdý zásek (podoba A) zůstal na portu `2-1.3`, ne u kamery.** Sedm epizod dohromady:

| kdy | port `2-1.3` | port `2-1.2` |
|---|---|---|
| před prohozením | **Left**: 5 zamrznutí → **5× tvrdý zásek** | **Right**: 4 zamrznutí → 4× samo za 2–4 s |
| po prohození | **Right**: **3× tvrdý zásek** (18:42, 19:10, 19:49) | **Left**: 1× podoba B (18:56) |

Zásek podoby A tedy **osmkrát z osmi padl na port `2-1.3`**, ať na něm visela kterákoli
kamera. Podezřelý je tedy **port / kabel / větev hubu**, ne kamerová jednotka.

⚠️ **Co to NEDOKAZUJE:** pět epizod před prohozením se měřilo v době, kdy se detekovala jen
podoba A — podoba B (kamera se ztratí z výčtu, v logu nic) mohla u pravé kamery projít bez
povšimnutí, takže poměr 5:0 může být nadsazený. Po opravě detekce už jsou obě podoby vidět
a další data budou srovnatelná.

Rozhodovací pravidlo pro ně platí dál: **zásek na „Right" = port/kabel, zásek na „Left" = ta
kamera.** (Jména driveru se váží na sériové číslo, takže se prohozením nezměnila.)

#### ⚠️ Zásek má DVĚ podoby a ta druhá se dlouho schovávala

Po prohození portů se ukázalo, že tentýž zásek umí vypadat dvojím způsobem:

| podoba | co dělá dotaz na sběrnici | jak se pozná |
|---|---|---|
| **A** | hodí `failed to set power state` | v logu každou vteřinu `QueryDevices selhalo` |
| **B** | **projde a kameru nenajde** | v logu **nic** — driver mlčky zkouší dál |

Obojí má tutéž příčinu: po teardownu si zařízení vezme zpátky `uvcvideo` a librealsense se
k němu už nedostane (ověřeno `readlink` na `/sys/bus/usb/devices/2-1.*/…:1.0/driver`).

⚠️ **Podoba B nespouštěla zotavení vůbec**, protože `RecoveryNeeded` počítalo jen *selhané* dotazy.
Kamera tak zůstala mrtvá a **v logu po ní nebyla ani stopa** — z venku se to pozná jen podle
rostoucího stáří senzoru na stránce. Opraveno 13. 9. 2026: počítá se **každý neúspěšný pokus
o připojení**. Skutečně odpojený kabel od toho odliší příznak `everConnected` (kamera, která nikdy
nenaběhla, zotavení nespouští — jinak by u chybějícího kabelu bral lék dolů i zdravé kamery).

#### ⚠️ T265 se při tom rozbila jinak — a tuhle poruchu recyklace kontextu NEVYLÉČÍ

Po prohození začala T265 dělat něco jiného než D435: **připojí se, ale nedává pózu**
(`5 s bez pozy` → restart pipeline → dokola), její vlastní hardware reset selže na
`failed to set power state` a **nepomůže ani restart služby**. To je dokumentovaný stav po
zpackaném bootu (viz sekce o `tm_boot` výš) — chce to **fyzicky odpojit a zapojit kameru**,
softwarem se to spravit nedá. ✅ **Potvrzeno týž den:** po replugu (19:04) je T265 hned zdravá —
hlásí se jako `8087:0b37`, stáří zpráv 0,01 s a hlídka `5 s bez pozy` mlčí.

⚠️ **A z toho vzešla regrese, kterou bylo nutné hned opravit.** T265 byla do zotavení zapojena
týž den (implementuje `IRecoverableCamera`, takže se před výměnou kontextu uvolní — to je správně
a funguje). Jenže protože její poruchu recyklace **nelecí**, hlásila si o zotavení pořád dokola
a supervizor kvůli ní **bral každých 60 s dolů i obě zdravé D435** a pokaždé zastavil robota.
Léčba: **odstup se po neúspěšném zotavení zdvojnásobuje** (60 s → 2 → 4 … nejvýš 15 min) a do logu
jde hláška, která rovnou říká, co s tím (fyzický replug). Ověřeno na robotu.

**Poučení, které platí obecně:** lék, který oslepí všechny kamery naraz, se **nesmí opakovat
donekonečna**. Šedesátisekundový odstup, se kterým to bylo napsané, na to nestačil — u poruchy,
kterou lék neřeší, byl pořád horší než porucha sama.

### ⚠️ Levá D435 po restartu pipeline ZTICHLA úplně — jiná třída záseku (23. 9. 2026)

`records/test/20260923-143515.rec` (mise Track, Modřany). Průběh podle `ARBot.Analyze log`:

| čas | co |
|---|---|
| 14:38:44 | pravá D435: barva zamrzla → restart pipeline → 15× `QueryDevices selhalo: failed to set power state` |
| 14:39:02–04 | supervizor: `StopHold`, recyklace kontextu, **obě kamery zpět** (14:39:16/17) |
| 14:40:53 | **levá** D435: barva zamrzla → „restart pipeline (celkem 1x)" |
| 14:40:53–14:45:58 | o levé kameře **ani řádka**, snímky z ní žádné (poslední po 338 s), pravá jede do konce |

Na rozdíl od záseků 12.–18. 9. tu **vlákno kamery nehlásí nic**: ne neúspěšné dotazy (ty by po
15 pokusech spustily supervizor, od posledního zotavení uběhlo 109 s, tedy nad limitem 60 s),
ne připojení, ne chybu. Vlákno tedy **zatuhlo v nativním volání** RealSense (`pipeline.Stop`,
`Dispose`, `QueryDevices` nebo `Start`) — které to bylo, se ze záznamu zjistit nedá. T265 přitom
logovala dál ~45×/min, sdílený zámek dotazů tedy volný byl.

**Podezřelé místo v kódu:** hlídka zamrzlého streamu volala `Teardown()` **uvnitř
`using (frames)`**, tedy zastavovala pipeline, zatímco vlákno drželo její neuvolněný frameset.
Všechny ostatní cesty (timeout, výjimka) bourají až po uvolnění. Stejná cesta 13. 9. doběhla, takže
to deterministické není — **je to podezření, ne dokázaná příčina.** Od 24. 9. 2026 se bourá až po
uvolnění framesetu a nativní volání hlídá `NativeCallWatch` (limit `hangwatch=`, 20 s): při
zatuhnutí zapíše do `Trace`, **ve kterém volání** vlákno visí, a runtime pořídí minidump
(`logs/hang-kamera-*.dmp`). Neléčí to: zatuhlé nativní volání nejde přerušit a recyklovat kontext
pod ním by byl nativní pád; kamera zůstane mrtvá do restartu procesu. **Autor 24. 9. rozhodl, že
se to tak má nechat** — restart služby by přerušil misi, robot jede dál s jednou kamerou (viz
[decisions.md](decisions.md), 24. 9. 2026). Registr:
`hw-d435-vlakno-zatuhlo-po-restartu`. ⚠️ **Na zařízení neběželo.**

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
- **Identita VN100:** model **VN-100S-BB**, firmware **3.0.0.0**, HW revize **7**, sériové číslo
  **100016133**. Zdroj je hlavička exportu `vn100-2026-7-8-nastavei z arbot2.sencfg` (kořen repa,
  8. 7. 2026); že je to **týž kus, který je dnes v robotu**, potvrdil autor 25. 9. 2026.
  `deploy/vnprobe.sh` čte z identifikačních registrů jen registr 1 (model); FW je v registru 4,
  HW revize ve 2 a sériové číslo ve 3.
  **Aktuální FW od VectorNavu je v3.1.0.0 (březen 2023)** (autor, 25. 9. 2026), náš kus je tedy
  o verzi pozadu. Co 3.1 opravuje, zjištěné není. Jako příčina driftu kurzu z 23. 9. je FW
  hypotéza, ne nález (týž FW při dobrých jízdách 12. a 18. 9.) — viz registr
  `lok-freerun-kurz-staci-na-zapad`.
- Výběr platformového HAL (D435/T265 wrapper) viz [build-and-platforms.md](build-and-platforms.md).
