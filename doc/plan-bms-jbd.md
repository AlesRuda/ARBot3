# Plán: driver chytré BMS JBD — stav baterie do záznamu a na stránku

**Stav:** fáze 1–3 **hotové v kódu 8. 10. 2026** (ověřeno testy a headless se simulovanou BMS), **na zařízení neběželo**; kroky: [plan-bms-jbd-kroky.md](plan-bms-jbd-kroky.md). BMS **JBD-SP04S020
(60 A)** je objednaná, na zařízení zatím není. Registr: `hw-bms-jbd-driver`.

## Proč

Robot o baterii ví jen napětí z motorové jednotky (`MotorStateBase.Voltage`) a to je u LiFePO4
špatné měřidlo: mezi ~20 a 90 % nabití je křivka skoro plochá, takže `BatteryMonitor` varuje až
těsně před koncem. Dnešní jednoduchá BMS umí baterii odpojit, ale nic nehlásí — robot se
o vybití dozví tím, že zhasne (Robotour 19. 9. 2026, 2. kolo). Nová BMS hlásí stav nabití
z počítání náboje, proud z baterie, napětí každého článku, teplotu a důvod zásahu ochrany.
Podrobnosti o baterii a výběru BMS: [hardware.md](hardware.md), „Napájení — trakční baterie".

## Rozsah (rozhodnutí autora)

- **Jen vidět a zaznamenat.** Data jdou na stránku náhledu, do panelu *Sensors*, do `Trace`
  a do záznamu. **Nic se podle nich neřídí** — mise nezačíná ani nekončí podle stavu nabití,
  robot kvůli BMS nezastaví. Varování o slabé baterii přejde z napětí na procenta.
- **Jen čtení.** Driver posílá výhradně dotazy na čtení (0x03, 0x04), nikdy zápis. Ochrany
  a parametry BMS se nastavují aplikací v mobilu přes Bluetooth modul.
- **Připojení přes RS485** (převodník USB–RS485, nejlépe FTDI kvůli stabilní cestě
  v `/dev/serial/by-id/`). UART BMS patří Bluetooth modulu; ⚠️ jeho pin VCC je plus baterie.

## Uspořádání

Dvě vrstvy — protokol bez portu a tenký driver nad ním. Protokol jde tak otestovat celý na
pevných bajtech, bez hardwaru; po příchodu BMS se do testů doplní skutečně zachycené rámce.
(Zamítnuto: všechno v jednom driveru jako `SDC2160Ex` — parsování by šlo testovat jen spolu
s portem; cizí knihovna — pro .NET žádná zavedená není a protokol jsou dva dotazy.)

```
BMS ──RS485── USB převodník ── Uart ── JbdBms (1 Hz: 0x03 základní údaje, 0x04 napětí článků)
                                          │ BmsState
                                          ▼
                     router → stream → záznam (.rec)
                                     → BatteryMonitor → stránka náhledu, Trace
```

| Soubor | Co |
|---|---|
| `ARBot.Common/Devices/BmsState.cs` | Zpráva `BmsState : SensorStateBase`, verze 1 |
| `ARBot.Common/Devices/BmsProtection.cs` | `[Flags] enum BmsProtection` + český popis příznaků (pro stránku i `Trace`) |
| `ARBot.HAL/IBms.cs` | `IBms : ISensor` — `GetLastMeasurement()`, `MeasurementArived` (vzor `IGPS`) |
| `ARBot.HAL/Devices/Bms/JbdProtocol.cs` | Sestavení dotazů, hledání rámce, kontrolní součet, rozbor odpovědí → `BmsState` |
| `ARBot.HAL/Devices/Bms/JbdBms.cs` | `JbdBms : UartSensorBase<BmsState>, IBms` |
| `ARBot.HAL/Devices/Bms/VirtualBms.cs` | Simulace: stav nabití a proud nastavitelné v panelu *Virtuální senzory* |

### Zpráva `BmsState`

Napětí baterie [V], proud [A] (**kladný = nabíjení**, konvence JBD), zbývající a jmenovitá
kapacita [Ah], stav nabití [%], počet cyklů, `double[]` napětí článků [V], `double[]` teploty
[°C], maska vyvažovaných článků, `BmsProtection` (příznaky ochran), stav nabíjecího a vybíjecího
spínače a `HasMeasurement` — po chybě komunikace driver vydá zprávu **bez měření**, jako
`SDC2160Ex`. Registrovat v `MessageCatalog.RecordDefaults()` (jinak spadne `RecordCatalogTests`).
Zpráva je pasivní DTO; převod z bajtů dělá `JbdProtocol`.

### Protokol JBD (z veřejné dokumentace — ověřit na kusu ve fázi 4)

- 9600 Bd, 8N1. Dotaz `DD A5 <reg> 00 <součet 2 B> 77`, tedy `DD A5 03 00 FF FD 77`
  a `DD A5 04 00 FF FC 77`.
- Odpověď `DD <reg> <stav> <délka> <data…> <součet 2 B> 77`; stav 0x00 = v pořádku.
  Součet = `0x10000 − Σ(stav, délka, data)` (u dotazu přes `reg` a `délka`).
- **0x03 základní údaje** (big-endian): napětí ×10 mV, proud ×10 mA se znaménkem, zbývající
  a jmenovitá kapacita ×10 mAh, cykly, datum výroby, vyvažování (2 × 16 bitů), ochrany (16 bitů),
  verze SW, stav nabití [%], stav spínačů (bit 0 nabíjení, bit 1 vybíjení), počet článků, počet
  čidel a teploty ×0,1 K (°C = (v − 2731) / 10).
- **0x04 napětí článků:** 2 B na článek v mV.

### Zapojení do runtime

- Parametr **`UartBms`** (`K_HW`, výchozí `Profile.PortBms`). **Prázdný = BMS se nezakládá** —
  výchozí je prázdný na obou platformách, dokud se ve fázi 4 nezjistí cesta `by-id`; do té doby
  se zapíná v `config/pi-provoz.cfg`. Rychlost je konstanta 9600 jako u ostatních zařízení.
- `ARBotHW.SetRealHW`: blok hlídaný `!noUart && !empty` (vzor GPS/motor); `MotionSensorsStop`
  BMS i jeho `Uart` uklidí. `SetVirtualHW`: `VirtualBms`.
- `ARBotRuntime.BuildSensorSources`: `SensorMessageSource<BmsState>`.
- `WebStatus.KlicMereni`: případ `IBms → nameof(BmsState)` (stáří měření v seznamu senzorů).
- Panel *Sensors* ukáže BMS sám (je to `ISensor`).

### Varování a stránka náhledu

- `BatteryMonitor` bere vedle `MotorStateBase` i `BmsState`. **Dokud jsou data z BMS čerstvá**
  (stejné okno 5 s), varuje podle **stavu nabití**: nový parametr **`batwarnsoc=`** [%], výchozí
  **20**, s hysterezí. Když BMS chybí nebo data zestárnou, vrátí se k napětí a `batwarn=` —
  starší záznamy a jízda bez BMS fungují beze změny.
- Do `Trace` jen **přechody**: pod práh / nad práh, nastavení a zrušení příznaku ochrany,
  výpadek a obnovení komunikace s BMS.
- Stránka: v tabulce řádky napětí, **stav nabití [%]**, **proud [A]**, rozsah článků s rozdílem
  (3,28–3,31 V, Δ 30 mV), teplota a „zbývá / jmenovitě Ah, cykly" (samostatné řádky jako ostatní
  údaje stránky). V hlavičce červeně „**baterie 18 % (práh 20 %) — NABÍT**" a
  „**BMS: ochrana — <důvod>**". Bez BMS se stránka nemění.
- ⚠️ **„BMS odpojila vybíjení" se na stránce NEUKAZUJE** (upřesněno při plánu kroků 8. 10. 2026):
  když BMS rozepne vybíjecí spínač, ztratí napájení i Orange Pi, takže by ten řádek nikdy nikdo
  neviděl. Stav spínačů jde jen do záznamu; viditelné jsou ochrany na straně nabíjení (mráz,
  přehřátí, nadproud při nabíjení), kdy robot dál běží.

## Chyby a odolnost

- Napětí článků (0x04) je navíc: když jeho odpověď nejde rozebrat, zpráva vyjde se stavem nabití,
  proudem a ochranami z 0x03, jen bez článků (finální review 8. 10. 2026 — jiný firmware může
  vracet jinou délku a varování podle procent by jinak vypadlo).
- Port BMS má vypnuté hlášení timeoutu čtení (`Uart.ReportReadTimeouts = false`): „zatím nic
  nepřišlo" je u dotazování běžné a ticho hlásí driver sám.
- Špatný součet, stav ≠ 0, krátký rámec, žádná odpověď do 500 ms → zpráva bez měření a hláška
  přes `PoruchaHlasic` (klíč podle druhu poruchy). Mlčení > 5 s → `IsError` (umí `SensorBase`).
- Hledání rámce: zahodit bajty do `0xDD`, omezit délku; **vlastní dotaz vrácený jako ozvěna**
  (některé převodníky RS485) přeskočit.
- Kontrola hodnot: 1–32 článků, nejvýš 8 čidel, stav nabití 0–100 %; mimo rozsah = vadný rámec,
  ne měření.
- Odpojení převodníku: port znovu otevře `Uart.ReOpen`, driver pokračuje.
- Nové soubory driveru přidat do výčtu v `DiagnostikaPoruchTests` (diagnostika jen do `Trace`).

## Testy

| Oblast | Co |
|---|---|
| `JbdProtocol` | Dotazy přesně na bajt; rozbor ukázkových odpovědí (záporný proud, teploty, příznaky); chybové případy výše včetně smetí před `0xDD` a ozvěny |
| `JbdBms` | Náhradní `IUart` s frontou bajtů (vzor `ScriptedUart` v `MotorDriverErrorFrameTests`): cyklus → `BmsState`; mlčení → zpráva bez měření; `Stop()` do 3 s; **zapisuje jen 0x03 a 0x04** |
| `BmsState` | Zápis a čtení (`StateSerializationTest`), registrace v katalogu záznamu |
| `BatteryMonitor` | Přednost procent při čerstvých datech BMS, návrat k napětí, hystereze, hlášky jen při přechodu |
| `WebStatus` | JSON a řádky hlavičky s BMS i bez ní |

**Ověří až zařízení:** komunikaci přes RS485, skutečné rozložení bajtů u SP04S020 (firmware se
liší), znaménko proudu, počet čidel, ozvěnu převodníku a cestu `by-id`.

## Fáze

1. **Protokol a zpráva** — `JbdProtocol`, `BmsState`, `BmsProtection`, katalog; jen kód a testy.
2. **Driver a zapojení** — `JbdBms`, `IBms`, `UartBms` + `Profile.PortBms`, `ARBotHW` (real
   i virtual, úklid), zdroj zpráv, `KlicMereni`, `VirtualBms` + panel *Virtuální senzory*.
   Ověřit, jestli telemetrický pohled ukáže `BmsState` sám.
3. **Varování a stránka** — `BatteryMonitor` + `batwarnsoc=`, stránka náhledu, `Trace`;
   [configuration.md](configuration.md) (počet klíčů).
4. **Ověření na zařízení** (po příchodu BMS) — zachytit skutečné rámce a přidat je jako testovací
   data, porovnat stav nabití a proud s aplikací v mobilu, doplnit cestu `by-id`
   do `Profile.PortBms` a `config/pi-provoz.cfg`.
5. **(volitelně) `ARBot.Analyze battery`** — spotřeba ze záznamu: průměrný a špičkový proud,
   Wh/km. Číslo, které při výběru baterie chybělo.

Fáze 1–3 nepotřebují hardware a v simulaci jdou proklikat celé.
