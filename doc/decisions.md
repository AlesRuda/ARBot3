# Deník rozhodnutí (decisions log)

Chronologický záznam **netriviálních rozhodnutí** na projektu — hlavně to, co by se jinak
„zahrabalo" a čeho se v kódu nedá vyčíst *proč*. Slouží jako sdílená paměť napříč sezeními
i lidmi (viz [CLAUDE.md](../CLAUDE.md), pravidlo „vše v repozitáři").

**Jak přispívat:** nové rozhodnutí přidej **nahoru** do sekce „Rozhodnutí" jako krátký blok:
*co* se rozhodlo, *proč* (kontext / alternativy), *důsledky* a *odkazy* (soubory, doc).
Absolutní datum (ne „minulý týden"). Detailní doménovou dokumentaci nech v příslušném
`doc/*.md`; sem patří jen rozhodnutí + odůvodnění + odkaz.

---

## Rozhodnutí

### 2026-09-01 — Zaseknutý stream kamery se pozná podle počtu timeoutů, ne podle přítomnosti na USB
**Co:** `D435Camera.GetMeasurement` (oba HAL) počítá **po sobě jdoucí timeouty**. Po třech
(`StallTimeoutsBeforeRestart`, tj. ~3 s bez snímku při požadovaných 30/s) pipeline zbourá a
příště ji `EnsureConnected` nastartuje znovu. `DevicePresent()` vrací **`bool?`** — `true` je,
`false` není, **`null` = nepodařilo se zjistit** — a ptá se **až po překročení prahu**, ne při
každém timeoutu.

**Proč:** stream se umí zaseknout, **aniž by se kamera odpojila od USB**. Přesně to se stalo
31. 8. 2026 na OrangePi: pravá D435 přestala dodávat snímky, v `dmesg` **žádné odpojení**
(jen opakované `USBDEVFS_CLEAR_HALT`), takže `DevicePresent()` vracel true, pipeline se
nezbourala, `connected` zůstalo true a `IsError` hlásil **OK**. Kamera tedy mlčela navždy a
panel *Sensors* ji ukazoval jako zdravou.

**Proč se na USB neptáme při každém timeoutu:** `DevicePresent()` volá `ctx.QueryDevices()`
a to nad běžícími streamy **není zdarma ani neomylné** — při častém volání samo selže na
`failed to set power state` (změřeno 1. 9. 2026). Původní kód takové selhání vydával za
odpojení, takže by driver u kamery, která je na místě, hlásil „kamera odpojena" a šlo by se
hledat kabel. Odtud i to `null`: **selhání dotazu není důkaz, že kamera chybí.**

**Proč prah 3 a ne 1:** čerstvě nastartovaná pipeline dodá první snímek až za **~1–2 s**
(změřeno na zařízení). Agresivnější restart by se zacyklil a snímky by nedorazily nikdy —
ověřeno pokusem s prahem 3 timeoutů po 10 ms, kde za 45 s proběhlo 30 restartů a **nula**
snímků. Vedlejší efekt zvoleného prahu: skutečné odpojení se ohlásí až po ~3 s místo ~1 s;
to je nepodstatné, protože reconnect se pak zkouší každou sekundu.

**Ověřeno na zařízení** (1. 9. 2026): s uměle zkráceným timeoutem větev proběhne, `StallRestarts`
roste, `IsError` hlásí CHYBA a pipeline se znovu chytne; s produkčním nastavením obě kamery
40 s na 30 fps a **nula** zásekových restartů. Diagnostika: nová vlastnost
`D435Camera.StallRestarts` — rostoucí číslo znamená, že se problém opakuje a drží ho jen tahle
záchrana.

**Odkazy:** `Src/ARBot.HALArmbian/Devices/Camera/D435Camera.cs`,
`Src/ARBot.HALWindows/Devices/Camera/D435Camera.cs`, [devlog.md](devlog.md) 1. 9. 2026,
[OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md) (fyzická stránka).


### 2026-08-31 — `Uart.Read(int)` čte přes vnitřní buffer (GPS ztrácela 92 % měření)
**Co:** `Uart.Read(int count)` už nesahá na port po jednotlivých bajtech — při probuzení si
vezme **všechno, co v portu je**, do vnitřního bufferu (8 kB) a další volání se obsluhují
z něj. Smyčka i `Thread.Sleep(10)` při prázdném portu zůstaly.

**Proč to byla vada:** UBX parser čte po **jednom bajtu** (`UBXMessage.Parse` volá `Read(1)`
na každý bajt hlavičky a takhle přeskakuje i všechny NMEA věty). Původní `Read(1)` sáhl na
port a když zrovna nic nebylo, spal 10 ms — takže se **za jedno probuzení zpracoval jeden
bajt**. u-blox posílá 13,1 kB/s, takže parser stíhal ~1/12 toku.

**Změřeno na zařízení 31. 8. 2026** (aplikace vypnutá, port volný):
- přijímač skutečně posílá **NAV-PVT 9,90 Hz** (rozestupy medián 100 ms, min 92, max 107)
  a k tomu **199 NMEA vět/s**, z toho ~170/s jsou GSV/GSA o viditelných družicích, které
  driver vůbec nepoužívá;
- staré čtení vytáhlo **0,88 NAV-PVT/s**, s bufferem **10,09/s** (obojí stejný benchmark);
- **skutečný driver** `uBloxGps` po opravě dodává **9,99 Hz** (před opravou to autor
  pozoroval v UI jako 0,8 Hz s občasným skokem na 3,2 Hz).

Ten občasný skok na 3,2 Hz se skokem v čísle snímku byl tentýž jev z druhé strany: parser se
občas trefil do už naplněného bufferu a dodal dvě měření těsně za sebou.

**Proč ne jiné řešení:** nabízelo se vypnout v přijímači NMEA (ušetřilo by 87 % dat) nebo
snížit jeho rychlost. Obojí je konfigurace **cizího zařízení uloženého v jeho flash** — vada
byla v našem čtení a měla by se opravit tam. Vypnutí NMEA je stále legitimní optimalizace
navíc, ne náhrada.

**Důsledky a na co si dát pozor:** co leží ve vnitřním bufferu, **už není v portu**. Ostatní
čtecí metody (`Read(byte[],off,count)`, `ReadLine()`, `ReadAll()`) proto nejdřív vybírají
tenhle buffer — jinak by `ReadLine()` přečetl řádek, kterému chybí začátek. Dnes žádný senzor
styly nemíchá (u-blox `Read(int)`, VN100 `Read(buf,off,len)`, motor `ReadLine()`), ale tiše
se to rozejít nesmí. **Unit test na to není** — `Uart` je natvrdo nad `SerialPort`; ověřeno
měřením na zařízení, včetně kontroly, že se nerozbily zbylé dvě cesty (IMU 8022 B/s
s rozestupem 0xFA po 80 B, motor 386 řádků/s včetně `DI=`).

**Odkazy:** `Uart.cs`, `UBXMessage.cs`, [devlog.md](devlog.md) 31. 8. 2026.

### 2026-08-31 — `TimeBase` musí sčítat `Stopwatch.Elapsed.Ticks`, ne `ElapsedTicks`
**Co:** `TimeBase.Now` počítalo `start + sw.ElapsedTicks`. Opraveno na
`start + sw.Elapsed.Ticks` (Src/ARBot.Common/Common/TimeBase.cs). Stejná záměna byla
i v `Performance.ToString()` (`new TimeSpan(surové_tiky)`), tam se převádí přes
`Stopwatch.Frequency`.

**Proč to byla vada:** `Stopwatch.ElapsedTicks` (bez tečky) vrací **surové tiky časovače**
v jednotkách `Stopwatch.Frequency`, kdežto `DateTime`/`TimeSpan` počítají ve 100 ns.
Na Windows je QPC frekvence shodou okolností **10 MHz = `TimeSpan.TicksPerSecond`**, takže
záměna nic nedělá a je **neviditelná**. Na Linux/ARM64 (OrangePi) je `Frequency`
**1 GHz**, tedy 100× jinak — a celý čas aplikace běžel **stonásobnou rychlostí**.

**Jak se to projevilo (a proč to trvalo najít):** příznak vypadal jako vada kamery —
overlay hlásil **0,3 Hz**, ale čísla snímků přibývala desítkami za sekundu. Obojí je
tentýž jev: perioda příjmu se počítá z razítek, takže 30 Hz (33 ms) se hlásilo jako
3,3 s = 0,3 Hz. Druhá stopa byl čas snímku **07:12** proti hodinám 22:46 — po ~5 minutách
běhu byla razítka o ~8 hodin napřed. Změřeno na zařízení: stará varianta 100,0×, nová 1,0×.

**Důsledky:** razítkuje se z `TimeBase` na 45 místech včetně všech senzorů, takže hodiny
byly aspoň **konzistentní** — ale `dt` mezi měřeními vycházelo 100× větší, což rozbíjí
predikci EKF, integraci rychlosti i regulaci, a všechno, co se poměřuje s reálným časem
(okno historie fúze 3 s = reálných 30 ms, timeouty, Hz v UI). **Záznamy pořízené na Pi
před touto opravou mají 100× roztažená razítka** a nedají se brát jako měření.

**Pojistka:** `Src/ARBot.Common.Tests/Common/TimeBaseTests.cs` — jeden test porovnává
postup `TimeBase` s nezávislým `Stopwatch`em (meze ±100 %, chyba je 100×, takže se
neschová), druhý na platformách s odlišnou frekvencí hlídá, že se obojí tiky nerovnají.
Pozor: test **nesmí** porovnávat s `DateTime.Now` — `TimeBase` záměrně nesleduje skoky
systémových hodin.

**Odkazy:** [devlog.md](devlog.md) 31. 8. 2026, `TimeBase.cs`, `Performance.cs`.

### 2026-08-29 — Robot vystavuje vlastní WiFi (AP `arbot`) a AP jede na `hostapd`
**Co:** WiFi na OrangePi je přepnutá z klienta VatNet na **vlastní AP `arbot`**
(`192.168.7.1`, WPA2, kanál 6). AP obsluhuje **`hostapd` mimo NetworkManager**
(`wlan0` je z NM vyjmuté), adresu, DHCP a NAT drží `arbot-ap-net.service`.
Ethernet dostal dvojici NM profilů **DHCP → pád na přímé spojení** (`192.168.66.1`,
robot rozdává adresy notebooku).

**Proč:** na soutěži není k dispozici žádný router, ale je potřeba se k robotu dostat
z notebooku i z mobilu — a k tomu stahovat velké objemy dat, na což je WiFi pomalá.
AP řeší přístup, kabel objem; profil `eth-direct` s `ipv4.method=shared` znamená, že
notebook dostane adresu sám a v terénu se nic nenastavuje ručně.

**Proč `hostapd`, a ne AP režim v NM:** vyzkoušeny všechny tři cesty. `iwd` AP neumí
(`net.connman.iwd.InvalidArguments` z `AccessPoint.Start()`). NM s backendem
`wpa_supplicant` AP **zdánlivě** postaví — `type AP`, SSID je vidět — ale klient
neprojde: `WLC_E_DEAUTH_IND(6) reason=17`. Sám `wpa_supplicant` k tomu v logu píše
*„nl80211 driver interface is not designed to be used with ap_scan=2"*; jeho AP režim
je náhražka a pro nl80211 je určený `hostapd`. Na `hostapd` klient projde handshake
i DHCP na první pokus. **Slepá ulička: vypnutí PMF** (`wifi-sec.pmf 1`) odstraní
z logu `wl_cfg80211_external_auth`, ale `reason=17` zůstane.

**Důsledek:** deska umí **buď** AP, **nebo** klienta. `wpa_supplicant` 2.11 neumí
klienta s ovladačem `bcmdhd` (`wl_set_multi_akm: Failed to set join_pref` →
`ASSOC-REJECT`, navenek klamavé „WRONG_KEY" — proto se kdysi přešlo na `iwd`).
**Robot se tedy na cizí WiFi nepřipojí**, dokud se ručně nepřepne zpátky (postup je
v POSTUP.md kroku 3); internet bere po kabelu.

**Pasti, které to stálo:**
- **Netplan** (systém je řízený jím, ne čistým NM) zahazoval u AP profilu
  `ipv4.method: shared` při každém zápisu. Od přechodu na `hostapd` je to mimo hru,
  u ethernetových profilů to platí dál.
- **Restart NetworkManageru nechá viset jeho `dnsmasq`**, ten drží `192.168.66.1:53`
  a `eth-direct` pak nekonečně cyklí. Léčba je zabít osiřelý proces.

**Odkazy:** [OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md) kroky 3 a 4.

### 2026-08-27 — Chybový rámec driveru se odlišuje příznakem, ne nouzovým zastavením
**Co:** `IMotorState` má nově `HasMeasurement` (výchozí `true`, default interface implementation),
`MotorStateBase` je **verze 3** a oba drivery (`SDC2160`, `SDC2160Ex`) v chybové větvi vrací
`hasMeasurement: false`. `DefaultMeasurementMapper` z takového rámce **nevyrobí žádné měření**.

**Proč.** Při neparsovatelné odpovědi (nebo nedostupném portu) vracel driver
`MotorStateBase(estop: true, 0, 0, …)`. Stop je tam **správně** — je to fail-safe „nevím, co se
děje, ať robot stojí" — ale nuly v enkodérech a rychlostech **nikdo neměřil**. Fúze tedy dostala
**„stojím" právě v okamžiku, kdy o robotu nevíme nic**, a robot se přitom může pohybovat (dobrzďuje,
jede ze setrvačnosti).

**Proč to nejde poznat podle stopu** (a proč to je vlastní příznak): pod drženým nouzovým zastavením
je nulová rychlost **plnohodnotné měření** — řídicí jednotka má příkaz stát a motory jsou řízené
pozičně ve zpětné vazbě (viz rozhodnutí o odometrii pod stopem níže). Po chybě parsování je táž nula
**výmysl**. Stop ty dva stavy nerozlišuje, takže na něm to rozhodnutí nesmí viset.

**Starý záznam se čte jako `true`.** Zástupné rámce v něm jsou, ale od měřených **nejdou rozeznat**,
takže tvrdit o nich cokoli jiného by bylo vymýšlení — a opačná volba by z každého staršího záznamu
udělala samou nedůvěru.

**Ověřeno na správném místě:** test driveru krmí `SDC2160Ex` neparsovatelnou odpovědí a hlídá obojí
(rámec merenie nenese, stop platí dál); ověřeno i to, že bez opravy ten test **padá**. Projeví se to
ale jen na reálném železe — v simulaci `VirtualMotors` chybovou větev nemají.

**Odkazy:** `IMotorState.HasMeasurement`, `MotorStateBase` (FormatVersion 3),
`DefaultMeasurementMapper.FromOdometry`, testy `MotorDriverErrorFrameTests`,
`PositionInitAndMapperTests.Odometry_FrameWithoutMeasurement_IsIgnored`.

### 2026-08-27 — Mise Robotour nemusí přežít restart; fáze 6 zrušena
**Co:** Fáze 6 plánu mise (stavový soubor `logs/mission-state.json` + opt-in obnovení mise po
restartu) **se dělat nebude**. Rozhodnutí autora. Nic z ní nebylo napsané, takže v kódu po tom
nezůstává žádná stopa; zůstává jen popis původního návrhu v
[robotour-mission.md](robotour-mission.md#přežití-restartu-zrušeno), kdyby se to někdy vracelo.

**Co se tím zahazuje** (aby to bylo vidět, až na to někdo narazí): původní argument zněl, že **depo
je jediná informace, kterou nelze získat znovu** — cíle se dají znovu přečíst z QR kódu, ale depo
vzniká jednou, v `ArmingAtDepot`, z fixu na místě startu.

**Důsledek, se kterým se od teď počítá:** po pádu nebo restartu aplikace se mise spouští **od
začátku**, tlačítkem *Start mise* tam, kde robot stojí. `ArmingAtDepot` postaví depo z aktuálního
fixu, takže **depo se přepíše na současnou polohu** — kdo restartuje uprostřed trasy, dostane jiné
depo a robot se „vrátí" jinam. Léčba je provozní, ne softwarová: s robotem nejdřív zpátky do depa.

**Co to zjednodušuje:** odpadá zápis stavu při každé změně fáze (I/O v automatu, který jinak sahá
jen na zprávy), verzování toho souboru vedle už tak verzované `MissionMsg`, a celá otázka, co dělat
se souborem, který patří k jinému běhu nebo jiné mapě.

**Odkazy:** [robotour-mission.md](robotour-mission.md) — hlavička a Plán realizace, fáze 6.

### 2026-08-27 — Zkouška dosažitelnosti zkouší obě orientace hrany (byla pesimističtější než jízda)
**Co:** `GlobalNavigator.Probe` bere cost-to-goal jako **minimum přes mapmatchnutou hranu a její
reverzní** (`FindReverse`), ne jen přes tu, kterou vrátil `NearestNode`.

**Proč.** Nahlásil autor: cíl `geo:50.029,14.5204` byl zamítnut jako „nevede trasa (je mimo mapu?)",
i když podle mapy leží na cestě. Změřeno: je **49,5 m západně** a 0,9 m od osy cesty — tedy **za
robotem**, který mířil na východ (θ = −0,2°).

Řetěz příčin:
- `NearestNode` mapmatchne polohu na nejbližší **orientovanou** hranu. Na obousměrné cestě jsou oba
  směry **geometricky totožné**, takže rozhoduje tie-break „dřív přidaná hrana vyhrává" — **ne kurz
  robota**.
- Otočka na téže cestě **není v grafu přechod** (`GraphBuilder` U-turn u téhož `WayId` vynechává),
  takže z hrany mířící od cíle je cost-to-goal nekonečná.
- **Jet se tam ale dá:** `Navigator.Update` (a stejně `Router`) po mapmatchi zkoušejí **obě**
  orientace a berou levnější. Ověřeno testem — jízda na týž cíl vrátila `Driving` a mrkev správným
  směrem, zatímco zkouška hlásila „nedosažitelné".

Zkouška tedy byla **pesimističtější než jízda, kterou má předpovědět** — což je nejhorší možný směr
chyby: zamítne dobrý cíl a obsluha nemá co opravit. Vada je z 26. 8., kdy `Probe` vznikla; s dnešním
přichycováním na cestu nesouvisí (rozhodnutí níže).

**Pro reachability stačí minimum obou cost-to-goal**, ne vážené porovnání jako v `Navigator`
(`(1−t)·traversal + cost` vs. `t·traversal_rev + cost_rev`) — to řeší, **kterým** směrem jet, ne
jestli to jde.

**Odkazy:** `GlobalNavigator.Probe`, test `GlobalNavigatorTests.Probe_GoalBehindRobot_IsReachable`,
[robotour-mission.md](robotour-mission.md#přijetí-cíle-rozhoduje-jen-stroj).

### 2026-08-27 — Cíl z QR kódu se přichycuje na cestu; co je daleko od sítě, je nedosažitelné
**Co:** `GlobalNavigator.Probe` vrací kromě dosažitelnosti i **cíl přichycený na síť**
(`SnappedTarget` = kolmý průmět na nejbližší hranu) a **odstup od ní** (`OffRoadM`). Mise Robotour
jezdí na ten **průmět**, ne na souřadnici z kódu, a cíl dál než `MaxTargetOffRoadM` (default 15 m)
**zamítá** jako nedosažitelný. Pokyn autora.

**Proč přichycovat.** Souřadnice v QR kódu je místo, kde **stojí člověk s krabicí** — robot jezdí po
síti, takže tam dojet nemůže. Dosud se cíl posílal do navigace surový a `GoalField.InsertGoal` si ho
sice na hranu přichytil sám (rozřízl ji průmětem), ale `GoalField.GoalPoint` zůstával **surový** —
a právě proti němu měří `Navigator` dojezd. **Odsazení větší než `ArrivalRadiusMeters` (3 m) tedy
znamenalo, že `Arrived` nenastane nikdy:** robot dojel na cestu, zastavil se u průmětu a čekal.
A protože jízda k cíli nemá timeout (`DrivingTimeoutSec = 0`), čekal by napořád. Nebyla to teoretická
vada — QR kód na stanovišti bude od osy cesty odsazený skoro vždycky.

**Proč to nestačí a musí k tomu limit.** `RoadNetwork.NearestEdge` **žádný limit nemá**, takže
přichytit jde cokoliv: cíl uprostřed pole 300 m od silnice se k té silnici přichytí a vyjde jako
dosažitelný. Robot by odjel na cestu **úplně jinam**, než kde člověk stojí, a ohlásil dojezd — což je
horší než zaseknutí, protože to vypadá jako úspěch. Limit je to, co z přichycení dělá **kontrolu**.
Naléhavost vzrostla zrušením potvrzování obsluhou (26. 8.): strojové kontroly jsou jediná pojistka.

**Kde limit bydlí a proč tam.** Měří síť (`Probe` vrátí vzdálenost), ale **posuzuje mise**
(`RobotourConfig.MaxTargetOffRoadM`) — stejné dělení jako u parseru `geo:` a sanity checků: „co je
ještě přijatelné" je pravidlo úlohy, ne vlastnost grafu. `Probe` proto `OffRoadM` **nehodnotí**
a `Reachable` pořád znamená „vede v grafu cesta", ne „cíl leží na cestě".

**15 m je z úsudku, ne z dat** — druhá taková hodnota vedle `MaxSpreadM`, a je to přiznané. Úvaha:
hrana v OSM je *osa* cesty, takže člověk na kraji dvoumetrové pěšiny je ~1 m od osy, u vchodu do
budovy vedle cesty klidně 5–10 m, k tomu chyba souřadnice, kterou někdo do kódu vložil. Proto odstup
**jde do záznamu** (`MissionMsg.AcceptedOffRoadM`, verze 6) a vypisuje ho panel: po prvních bězích se
dá nastavit z čísel.

**Důsledky.**
- `MissionMsg` je **verze 6** a **mění význam** `AcceptedLatDeg/LonDeg`: od ní jsou přichycené, ve
  verzích 2–5 surové. Bajty jsou tytéž, pozná se to **jen podle čísla verze**. Surová souřadnice
  zůstává čitelná v `AcceptedCodeText`, takže z dvojice jde odstup zpětně ověřit.
- **Netýká se to depa** (je to zapamatovaná vlastní póza — robot tam dojel po cestě) ani cíle
  z příkazové řádky (`goal=lat,lon`). Tam `GoalPoint` zůstává surový, takže `goal=` mimo cestu má
  **pořád** starý problém s dojezdem. Vědomě neřešeno: změnit `GoalPoint` na průmět by změnilo
  význam dojezdu všem uživatelům `GoalField` naráz.

**Odkazy:** [robotour-mission.md](robotour-mission.md#přichycení-cíle-na-cestu),
`GlobalNavigator.Probe`, `RouteProbeResult`, `RobotourConfig.MaxTargetOffRoadM`,
testy `GlobalNavigatorTests.Probe_*` a `RobotourMissionTests.*Cest*`.

### 2026-08-27 — Odometrie se pod nouzovým zastavením používá normálně (výjimka zrušena)
**Co:** `DefaultMeasurementMapper.FromOdometry` už **nerozlišuje** nouzové zastavení. Do té doby pod
ním odometrii zahazovala.

**Proč byla ta výjimka špatná** (argument autora, 27. 8. 2026). Původní zdůvodnění znělo „kola stojí,
ale robot může být tlačen, a hlavně je to stav, kdy do něj člověk zasahuje" — a neobstojí:

- **Řídicí jednotka má pod stopem příkaz STÁT** a motory jsou řízené **pozičně ve zpětné vazbě**,
  takže kola nemohou vyrobit nic jiného než nulu. Stop odometrii nijak **nezhoršuje** — pokud vůbec,
  dělá „v = 0" *jistější*.
- **Tlačení robota na tom nic nemění** (upřesnění autora). Poziční smyčka drží polohu, takže se
  s tlakem **pere a dorovnává ji** — enkodéry ukážou výchylku a návrat, ne čistý posun. Odometrie
  tedy pod stopem ani netvrdí „jedu", ani neprozradí, že byl robot posunut; chová se **stejně jako
  bez stopu**. Není to argument pro ani proti — jen další doklad, že stop v tomhle nic nerozlišuje.
- **Odnesení robota** (kola se netočí, robot se hýbe) je stejně možné **bez** stisknutého stopu.
  Stopem se ty dva stavy nerozliší, takže na něm nemá smysl to rozhodnutí věšet. Je to univerzální
  limit kolové odometrie, ne vlastnost nouzového zastavení.

**Co to způsobovalo.** Pod drženým stopem neměla fúze **žádnou vazbu na rychlost** — stav má `v` i
`ω`, takže rychlost volně driftovala a polohu tahal šum GPS (σ 1,5 m, 5 Hz). Za desítky sekund
servisního okna se odhad rozešel o metry. Projevilo se to jako „robot na mapě zběsile poskakuje"
v misi Robotour, protože ta je **první věc, která stop drží dlouho**. Naměřený kontrast: jízda bez
mise (`goal=`, odometrie tekla) měla chybu pózy p50 **0,164 m** a 2 skoky ze 399 vzorků.

**Zamítnutá alternativa:** posílat pod stopem umělé „v = 0" s velkou σ (zero-velocity update). Bylo
by to zbytečné — reálná odometrie *už* nulu hlásí a má svou naměřenou σ, takže nová konstanta k ladění
by nic nepřinesla.

⚠️ **Otevřený důsledek:** chybová větev driveru
([`SDC2160Ex`](../Src/ARBot.HAL/Devices/MotorDriver/SDC2160Ex.cs)) při selhání parsování **vyrábí**
`MotorStateBase(true, 0, 0, …)` — tedy stop a nuly. Takový rámec teď fúze vezme jako „stojím", i když
se robot může pohybovat. Není to regrese (před zavedením té výjimky to platilo taky), ale je to
skutečná děra: správné rozlišení je „je to měření, nebo zástupný rámec po chybě", ne „je stisknutý
stop". Léčba chce příznak v `MotorStateBase` (a tedy verzi zprávy) — vedeno jako otevřený úkol.

**Odkazy:** [`DefaultMeasurementMapper`](../Src/ARBot.Common/Runtime/DefaultMeasurementMapper.cs),
testy `Odometry_UnderEmergencyStop_IsUsedNormally` a `..._NenulovaRychlostSePrenese`.

### 2026-08-26 — Mise Robotour běží bez operátora; potvrzování cíle zrušeno
**Co:** `RobotourMission.Confirm()` i tlačítko „Potvrdit" v panelu jsou **pryč**. Kód, který projde
strojovými kontrolami, se přijme **sám** a mise se posune. Jediné lidské vstupy jsou **QR kód** a
**stop tlačítko na robotu**. Rozhodnutí autora.

**Proč.** Úloha je **simulace autonomního delivery procesu**: robot má úkol vykonat bez zásahu
operátora. Jediní, kdo s ním interagují, jsou **odesílatel** v místě nakládky a **odběratel** v místě
vykládky — a ti u sebe nemají žádné UI, jen kód a tlačítko. Potvrzovací krok v panelu tedy modeloval
někoho, kdo v té úloze **není**.

**Důsledky, které to mělo v automatu.**
- **Uvolnění stopu se stalo plnohodnotným signálem.** U vykládky znamená „je vyloženo" (nic se
  nečte), takže se tam do stavu `Servicing` vůbec nechodí — po stisku se rovnou čeká na uvolnění.
- **Uvolnění bez přečteného kódu** znamená „člověk odešel": mise se vrátí na `AwaitingEStop` a čeká
  na další pokus. **Nikdy neodjede bez cíle** — to je jediná nová větev, kterou si změna vynutila.
- **Invariant „skenuje se výhradně pod drženým stopem" musel zesílit.** Dřív stačilo, že `Servicing`
  se opouští potvrzením; teď v něm lze stop pustit, takže se scanner vypíná na tom přechodu.
- **Váha pojistek se přesunula.** Zbyla jen strojová (formát, vzdálenost od depa, dosažitelnost),
  takže **musí být vidět, když zamítne** — jinak zamítnutý kód vypadá jako nepřečtený. To je taky
  důvod, proč `MissionMsg` nese `RejectReason`.
- `MissionMsg` je **verze 5**: totéž kolo polí dřív znamenalo „cíl nabídnutý k potvrzení", teď
  „**přijatý** cíl". Bajty jsou tytéž, takže se stará verze pozná **jen podle čísla** — viz
  [record-replay.md](record-replay.md).

**Co zůstalo operátorovi:** „Start mise" (robot se sám nerozjede — tatáž úvaha jako u obnovení po
restartu) a „Přerušit" jako **bezpečnostní** zásah, ne krok úlohy.

**Odkazy:** [`RobotourMission.AcceptTarget`](../Src/ARBot.Common/Missions/RobotourMission.cs),
[robotour-mission.md](robotour-mission.md#přijetí-cíle-rozhoduje-jen-stroj).

### 2026-08-26 — `GPSState.Latitude/Longitude` jsou v RADIÁNECH (dřív stupně)
**Co:** [`GPSState`](../Src/ARBot.Common/Devices/GPSState.cs) drží zeměpisné souřadnice v
**radiánech**, tedy v téže jednotce jako `LLA`, `GeoReference` a zbytek systému. `FormatVersion`
1 → 2, starší záznamy se při čtení převádějí. Rozhodnutí autora.

**Proč.** Do té doby byl `GPSState` **jediné místo s jinou konvencí**. A protože
`new LLA(gps.Latitude, gps.Longitude)` je ta nejpřirozenější věc, kterou člověk napíše, byla to
**tichá a fatální** past — 50 „radiánů" je platné číslo, takže se záměna nikde neohlásí a projeví se
až chováním o desítky tisíc kilometrů dál. Že to není teoretické riziko, dokazují dva zásahy:
`DefaultMeasurementMapper` na to musel mít varovný komentář („záměna znamená posun o stovky
kilometrů bez jediného hlášení"), a **mise Robotour do ní stejně spadla** — uvízla v `ArmingAtDepot`,
protože body v okně fixů byly desítky radiánů od sebe a rozptyl vyšel astronomický. Našlo se to až
spuštěním v aplikaci; **testy vadu potvrzovaly**, protože si jejich pomocník převáděl na radiány taky.

Podstata rozhodnutí tedy není „radiány jsou lepší jednotka", ale: **ať je nejpřirozenější zápis
správný.** Komentář, který varuje před pastí, je slabší nástroj než past neexistující.

**Cena.** Převod se přesunul na **okraje**: drivery (NMEA, u-blox — oba parsují stupně) převádějí
dovnitř, UI a telemetrie zpátky na stupně pro zobrazení. Dotčeno 8 souborů; `VirtualGps` se naopak
zjednodušila (dřív `Rad2Deg`, teď nic). **Archivní záznamy**: layout se nezměnil, takže se stará
verze pozná jen podle čísla verze — viz [record-replay.md](record-replay.md), sekce o změně jednotky.

**Odkazy:** [`GpsStateUnitsTests`](../Src/ARBot.Common.Tests/Devices/GpsStateUnitsTests.cs)
(mimo jiné test, že starý záznam ve stupních se převede),
[`DefaultMeasurementMapper`](../Src/ARBot.Common/Runtime/DefaultMeasurementMapper.cs),
[robotour-mission.md](robotour-mission.md).

### 2026-08-26 — Kvalita fixu v depu se měří RMS, ne maximem (a prah je 2,5 m)
**Co:** `RobotourConfig.MaxSpreadM` je **efektivní (RMS) odchylka** fixů od průměru, ne největší,
a výchozí hodnota je **2,5 m** (návrh měl 1,0 m). Tatáž veličina se pak hlásí filtru jako `std`.

**Proč — dvě samostatné vady.**

**(a) Maximum je špatná statistika.** Největší odchylka s rostoucím `n` **roste** i u dokonale
gaussovského šumu, takže by delší čekání kritérium **přitvrzovalo** — přesně naopak, než má
(`DepotFixSec` se zvýší, aby se okno zlepšilo, a ono se tím zamítne). RMS naopak konverguje k σ
senzoru, takže prah je fyzikálně čitelný údaj („šum fixu musí být pod X") nezávislý na délce okna.

**(b) Prah 1,0 m byl pod nominálním šumem, takže mise by se nezarmovala NIKDY.** Virtuální GPS má
σ = 1,5 m a spotřební přijímač ve stoje driftuje podobně, takže i normální fix by okno zamítl —
v simulaci i na zařízení. Zamítat se mají jen **abnormální** fixy (multipath skáče o desítky metrů),
ne normální šum. Našel to test, který krmí misi šumem odpovídajícím simulaci.

**Důsledek pro `std`, kterou dostane filtr:** hlásí se šum **jednoho vzorku**, ne standardní chyba
průměru (`σ/√n`). Je to vědomě konzervativní: průměrování stahuje **náhodnou** část šumu, ale ne
**bias** fixu (multipath, ionosféra), a ten je na téhle škále dominantní. Tvrdit filtru `σ/√n` by
byla tatáž nepoctivost σ, jaká se řešila u korelace s mapou — jen z druhé strany.

**Odkazy:** [`RobotourConfig`](../Src/ARBot.Common/Missions/RobotourConfig.cs),
[`RobotourMission.OnGps`](../Src/ARBot.Common/Missions/RobotourMission.cs),
[robotour-mission.md](robotour-mission.md).

### 2026-08-26 — `IPixel` má kanály `R`/`G`/`B`; barvu se nesmí čtít z `Values`
**Co:** [`IPixel`](../Src/ARBot.Common/Common/IPixel.cs) má nové (jen ke čtení) `byte R`, `G`, `B`.
`BGR`, `BGR32` a `RGB` je měly už předtím, doplnily se do `Gray`, `Gray16`, `Gray32`. Je to **jediná
slibená cesta k barvě** nezávisle na pixel typu; `Image<T>.ToGray` ji používá.

**Proč.** `ToGray` původně čtla barvu z `IPixel.Values` jako „`[0]` je R, když je délka ≥ 3".
Pro dnešní typy to **náhodou vychází** — `Values` se u všech tří barevných typů plní z pojmenovaných
vlastností, takže je vždy `[R,G,B]` bez ohledu na to, že `BGR` a `RGB` mají obrácené *rozložení
v paměti*. Ale rozhraní u `Values` **neslibuje ani délku, ani pořadí** (dokumentace říká jen „pole
int reprezentující pixel"), takže to byla nedokumentovaná domněnka. Pixel typ s jinou reprezentací
barvy — YUV nebo HSV, na které jsou v `IPixel` zakomentované `XmlInclude`, tedy někdy existovaly —
by podstrčil `[Y,U,V]`, vzorec pro jas by z toho spočítal nesmysl a **nikde by to nespadlo**.
Podnět autora.

**Proč ne `Color`:** je to `class`, takže její čtení v cyklu přes obraz alokuje na **každý pixel**
(u 640×480 tři sta tisíc na snímek). Kanály jsou `byte`, tedy zdarma.

**Důsledky.**
- `ToGray` je o větev kratší: váhy BT.601 dávají dohromady rovných 1000 a kanály šedého pixelu
  vracejí tutéž hodnotu, takže **šedý zdroj projde přesně** (`(299+587+114)·V/1000 == V`) bez
  zvláštního případu, a výsledek se vejde do bajtu bez omezování.
- **Jedna změna chování:** u `Gray16`/`Gray32` kanály vracejí **nejvyšší bajt** (hodnotu
  škálovanou), ne saturaci na 255, jak jsem to napsal poprvé. Důvod je konzistence — `Color` na
  týchž třídách tuhle konvenci **už měla**, a kdyby se kanály rozhodly jinak, tentýž pixel by hlásil
  jinou barvu přes `R` a jinou přes `Color.R`. Vedlejší přínos: u hloubkového obrazu v milimetrech
  by saturace udělala bílou placku, škálování zachová průběh.

**Odkazy:** [`Image<T>.ToGray`](../Src/ARBot.Common/Common/Image.cs),
[`PixelChannelTests`](../Src/ARBot.Common.Tests/Common/PixelChannelTests.cs) (klíčový test: `BGR`
a `RGB` mají obrácené bajty, ale kanály musí dát totéž),
[`ImageToGrayTests`](../Src/ARBot.Common.Tests/Common/ImageToGrayTests.cs).

### 2026-08-26 — QR dekodér: ZXing.Net místo ZBaru (a fáze 1 tím celá padá)
**Co:** dekodér QR kódů je **ZXing.Net** (NuGet `ZXing.Net` 0.16.11) za rozhraním
[`IQrDecoder`](../Src/ARBot.Common/Vision/Qr/IQrDecoder.cs), ne ZBar, jak počítal návrh
[robotour-mission.md](robotour-mission.md).

**Proč.** Návrh vybral ZBar podle toho, že se **osvědčil v soutěži** u předchozí generace robotu
(ARBot2) — to je dobrý důvod a nezmizel. Zmizel ale předpoklad: **ARBot2 na stroji není** (prohledán
`D:\Work` i profil), takže binding `zbar-sharp`, který se měl podle pravidla „vše v repozitáři"
zkopírovat do `Src/ThirdParty/ZBar/`, nebyl odkud vzít.

Druhá polovina důvodu je platná i s ARBot2 po ruce: ZBar za sebou táhne **nativní `libzbar` na obou
platformách** — `libzbar.dll` + `libiconv.dll` pro x64 a `DllImportResolver` pro `libzbar.so.0` na
Armbianu, protože soubor se tam jmenuje jinak. ZXing.Net je **čistě managed**, takže na ARM64 není co
řešit; ověřeno buildem `-p:Platform=OrangePI`. **Tím celá fáze 1 návrhu („ZBar do repa a na obě
platformy", včetně „ověřit na zařízení, že se knihovna nahraje") přestala existovat** — to je hlavní
důsledek, ne volba knihovny.

**Co se tím kupuje a co platí.** Kupuje se: žádná nativní závislost, žádný rezolver, jedna
`PackageReference`. Platí se tím, že **úspěšnost čtení je neznámá** — ZBar měl naměřenou soutěžní
historii, ZXing ji tady nemá. Testy dokazují jen *cestu* (`Image<BGR32>` → Y800 → dekodér, včetně
podvzorkování na polovinu), protože testovací obraz se **kóduje týmž ZXingem**; úspěšnost na
skutečném stanovišti je proto vedená jako krok „ověření na HW". Kdyby se ukázala slabá, výměna zpět
za ZBar je **lokální změna za `IQrDecoder`** — přesně proto to rozhraní v návrhu je.

**Odkazy:** [`ZXingQrDecoder`](../Src/ARBot.Common/Vision/Qr/ZXingQrDecoder.cs),
[`Image<T>.ToGray`](../Src/ARBot.Common/Common/Image.cs) (převod bez `System.Drawing` — to zůstalo
z návrhu v platnosti, `System.Drawing.Common` je jen na Windows),
[robotour-mission.md](robotour-mission.md).

### 2026-08-25 — Korekce z korelace se gatují `Soft`; tvrdý gate byl vada
**Co:** `MapCorrelatorConfig.GateMode` je nový a výchozí je **`GateMode.Soft`** (`R' = R × NIS/práh`)
místo dosavadního tvrdého `Reject`. `mapcorrgate=reject` vrátí původní chování pro A/B.

**Proč.** První měření korekcí naostro (`ARBot.Analyze corrections`) nad scénou, kde je co opravovat
— mapa vidění = mapa jízdy, skutečný drift `wheelslip=1.03,0.97 imubias=3,0.2`. Dva běhy na variantu,
příčná chyba pózy p50: **bez korekcí 0,674 / 0,675 m, s tvrdým gatem 0,847 / 0,816 m, se Soft
0,589 / 0,636 m**. Tvrdý gate tedy dělal výsledek **horší, než když se nekorigovalo vůbec**, a
zamítal 41,7 / 45,8 % korekcí (NIS p50 3,6, p90 až 124).

**Není to vada korelátoru** — ověřeno zvlášť: po odečtení vlastní chyby fúze je jeho vlastní chyba
0,02–0,06 m a `sd(z) = 0,74`. Innovace je velká proto, že **chyba pózy je velká**. Tvrdý gate ale
zamítá podle velikosti innovace, takže vyhodí **právě ty velké korekce, které jsou potřeba**, a co
projde, je vybrané podle toho, že už souhlasí — zaujatý podvzorek. Že je Soft správná odpověď, navíc
říkala dokumentace už od rozvahy o přímé korekci: nesouhlas je *přechodný*, stačí jím projít.

**Důsledky.**
- Se Soft se nezahodí **nic** (0 % zamítnutých), takže gate už nechrání proti korelaci, která se
  skutečně mýlí (špatná mapa, špatná kalibrace kamer). **Roste tím váha podmínky 3**, která je pořád
  otevřená. GPS to nezastoupí — má σ 1,5 m proti 0,088 m korelace, takže submetrový odtah v jejím
  NIS vůbec nevidí (změřeno: pózu to odtáhlo o 0,37 m a GPS NIS se nezměnilo).
- Zisk je **malý** (6–13 %) a zbytková chyba ~0,6 m, protože chyba kurzu zůstává 3,0° — přesně na
  vnuceném `imubias=3`. Kurzový bias drift znovu vyrábí rychleji, než ho příčná korekce stahuje.
  **Dokud se neopraví bezmocná korekce kurzu, je strop toho, co korelace na driftujícím robotu
  zachrání, nízký.**
- Drží to test `Vychozi_KorekceSeGatujiSoft`.

**Odkazy:** `Src/ARBot.Common/Localization/MapCorrelatorConfig.cs`, `MapCorrelator.cs`,
`Src/ARBot.Analyze/CorrectionsReport.cs` (nový),
[map-correlation-localization.md](map-correlation-localization.md).

### 2026-08-25 — Odstup korelací je dekorelační čas (3 s), ne ochrana proti hustým snapshotům
**Co:** `MapCorrelatorConfig.MinPeriod` **400 ms → 3 s**, a je to teď *definované* jako dekorelační
čas chybové posloupnosti, ne jako pojistka proti zaplavení.

**Proč.** Grid drží ~2,5 s historie, takže dva bližší cykly korelují z velké části téhož
nahromaděného oblaku — jejich chyby nejsou nezávislé, ale fúze je jako nezávislé bere a kovarianci
zužuje rychleji, než informace opravňuje. Dosud to byla *přiznaná aproximace*; teď je změřená:
autokorelace chyby dala ρ(1) = 0,44–0,66, ρ(2) už kolem nuly, činitel nadsazení `1+2Σρ` **1,88–2,44**
na třech bězích.

**Že je to fyzikální konstanta, a ne artefakt vzorkování**, ukázaly tytéž tři běhy: periody se lišily
o 42 % (1,17 / 1,56 / 1,66 s — korelátor je výpočtově vázaný), a dekorelační čas přesto vyšel týž
(2,85 / 2,93 / 3,31 s). Po zavedení odstupu je ρ(1) **záporná** (−0,23 / −0,29) a činitel nadsazení
**1,00** — každé měření je nezávislé konstrukcí, ne opravným součinitelem.

**Druhý, nezávislý důvod pro tentýž odstup.** Jeden cyklus stojí **1,31 s** (medián, oblak 45 000
buněk) — tedy celé jádro, ne „čtvrt jádra"; starší odhad 126 ms byl o řádek mimo. Při odstupu 3 s
klesne zátěž na ~40 %. A bez odstupu byla **frekvence měření dána rychlostí CPU**: na rychlejším
stroji by fúze byla *víc* přesvědčená o tomtéž. Ta 400ms hranice byla přitom v praxi **mrtvá** —
cyklus trvá 1,3 s, takže se nikdy neuplatnila.

**Zamítnuté alternativy.** (a) *Nafouknout σ o √VIF* — zachová frekvenci, ale rozvolní gating (velká
σ pustí outliery) a nechá tu závislost na rychlosti stroje. (b) *Nechat být, protože se dvě chyby
skoro vyruší* (σ konzervativní 1,35× proti nadsazení √2,1 = 1,45×) — poctivost by stála na vyrušení
dvou chyb, které se hnou každá jinak.

**Důsledky.**
- Korekce z korelace tečou **2,5× řidčeji**. U funkce, která je ve výchozím stavu vypnutá a jejíž
  σ je mírně konzervativní, je to bezpečný směr.
- Drží to test `Vychozi_OdstupKorelaciPokryvaDekorelacniCas` (dolní hranice, ne přesná hodnota).

**Odkazy:** `Src/ARBot.Common/Localization/MapCorrelatorConfig.cs`,
`Src/ARBot.Analyze/TimeCorrelationReport.cs` (nový),
[map-correlation-localization.md](map-correlation-localization.md).

### 2026-08-25 — Póza cestuje ve zprávě o korelaci; σ 1,25× konzervativní se vědomě neopravuje
**Co:** (a) `MapCorrelationMsg` **verze 5** nese `PoseX/PoseY/PoseTheta` + `HasPose` — pózu, proti
které se korelovalo; (b) naměřené `sd(z) ≈ 0,80` (σ je asi 1,25× větší, než by musela být)
se **nepřepočítává** do `Alpha`.

**Proč (a).** `Dx`/`Dy` je posun proti *té* póze („skutečná poloha = póza + d"), takže bez ní je
hlášené číslo neinterpretovatelné — nenulový posun může být stejně dobře chyba pózy, kterou korelátor
**správně našel**. Přesně na tom se týž den spletlo měřidlo (`ARBot.Analyze sigma`): dohledávalo
odhad z `RobotStateMsg` podle razítka a chybu **fúze** účtovalo **korelátoru** (vychýlení 0,191 m
proti skutečným 0,007–0,023 m). Konvence je stejná jako u `RoadCorridorMsg.PoseX` a ze stejného
důvodu — párování podle razítka nepřežije seek a `GetStateAt` vrací pózu z fixed-lag smootheru.
Změřeno, o kolik ta aproximace lhala: **p50 0,000–0,004 m, max 0,035 m**, tedy byla v pořádku;
teď je to navíc exaktní.

**Proč (b).** Zmenšit σ znamená **zvětšit autoritu** korelátoru proti GPS, a právě tu ty tři podmínky
gatují — utahovat ji těsně před „pustit naostro" je opačný směr. Navíc `|z| > 2` vyšlo u 0–8 % cyklů
(očekává se ~5 %), takže chvosty jsou v pořádku a σ není hrubě mimo; a je to jedna scéna
s `n = 12–13` na běh. **Podmínka č. 1 („honestní σ") je tím splněná** v konzervativním směru.

**Důsledky.** Poctivost σ se od teď měří `sd(z)`, ne poměrem souhrnného rozptylu k mediánu σ — σ se
cyklus od cyklu mění 3× a velké chyby padají právě na cykly s velkou σ. Zbývá ověřit `sd(z)` na
odbočce a šikmé cestě.

**Odkazy:** `Src/ARBot.Common/Logs/MapCorrelationMsg.cs`,
`Src/ARBot.Common/Localization/MapCorrelationResult.cs`, `MapCorrelator.cs`,
`Src/ARBot.Analyze/SigmaReport.cs`,
[map-correlation-localization.md](map-correlation-localization.md).

### 2026-08-25 — Honestní σ korelace je výchozí, a její reference je fyzikální veličina
**Co:** (a) referenční množství informativního důkazu se měří v **m²·log-odds**, ne v počtech
buněk (`MapCorrelatorConfig.ReferenceInformativeEvidence`, dřív `…Weight`); (b) **výchozí hodnota
je 37,5**, tedy honestní σ je zapnutá — dosud bylo `0` = vypnuto; (c) `Validate()` odmítne hodnotu
nad 1 000 m²·log-odds, aby stará hodnota `15000` z příkazové řádky spadla hlasitě.

**Proč.** Oprava z rána téhož dne (σ škálovaná vahou informativního důkazu) se nemohla stát
výchozím stavem, protože reference byla **vázaná na rozlišení gridu**: počet informativních buněk
roste jako `1/plocha buňky`, takže při 5 cm je jich čtyřikrát víc než při 10 cm, i když robot nevidí
ani o kousek víc světa — jsou to tytéž hloubkové pixely rozkrájené jemněji. Změřeno: surová váha
1 536 proti 6 144 (přesně 4×), takže σ by při jiném rozlišení vyšla **poloviční**.

Násobení plochou buňky to spraví přesně. **Krokem derivace `h` se naopak nedělí, a to schválně:**
pásmo informativních buněk má šířku `2h`, a právě tahle závislost vykrátí `σ ~ √h`, kterou má „tent"
skóre — past, kterou dokumentace kovariance dosud přiznávala („obě se ladí spolu, změna kroku
přepočítá všechny sigmy"). Změřeno: bez škálování σ 0,1342 → 0,1897 m při zdvojnásobení kroku
(přesně √2), se škálováním 0,1768 m v obou případech. Vada zmizela **mimochodem**.

Zapnutí naostro: součin `Alpha · ReferenceInformativeEvidence` nastavuje jen absolutní škálu,
přesně jako předtím `Alpha` sama — zapnutím tedy **nevzniká žádná nová vazba na scénu**, jen σ
začne vědět o množství důkazu. Nechat to za přepínačem by znamenalo, že kdo zapne `mapcorr=true`
a zapomene `mapcorrref=`, měří známo rozbitý estimátor.

**Důsledky.**
- `MapCorrelationMsg` je **verze 4**; hodnota z verze 3 (žila jediný den) se při čtení **zahodí** —
  jsou to jiné jednotky a tiše ji vydávat za m²·log-odds by znamenalo srovnávat nečíslo.
- Absolutní σ v jednotkových testech se posunuly (`CorrelationTestScenes.TestConfig` jede
  s produkčním výchozím stavem). Historická čísla v `doc/` se měřila s konstantní `Alpha`;
  reprodukují se `ReferenceInformativeEvidence = 0`.
- **Tři podmínky, než korekce pustit naostro, platí dál** — tohle je podmínka č. 1 posunutá, ne
  splněná: zbývá časová korelace mezi cykly (1,28×) a systematické vychýlení +0,10 m.
- `MinEvidenceCells` (400) a `SigmaFloorM` (0,05 m) zůstávají vázané na rozlišení gridu. Nikde nic
  neškálují, ale je to tatáž vada — jen v prahu, ne v σ.

**Odkazy:** `Src/ARBot.Common/Localization/CorrelationScorer.cs`, `EvidenceCloud.cs`,
`CorrelationCovariance.cs`, `MapCorrelatorConfig.cs`,
[map-correlation-localization.md](map-correlation-localization.md).

### 2026-08-22 — Virtuální kamera visí na ground truth (default) a simulace umí systematické chyby
**Co:** (a) `camerapose=` má **výchozí hodnotu `truth`** — virtuální kamery renderují ze
`SimulatedRobot`, ne z odhadu fúze; (b) simulace dostala **prokluz kol** (`wheelslip=`) a **bias
kurzu a gyra** (`imubias=`), obojí ve výchozím stavu vypnuté; (c) skutečná póza jde do záznamu
jako `GroundTruthMsg` na témže tiku a se stejným razítkem jako `RobotStateMsg`.

**Proč.** Autorova diagnóza: „spousta problémů plyne z mého rozhodnutí přišpendlit virtuální kameru
a GPS na EKF". Zčásti seděla — GPS na odhadu nikdy nevisela (`VirtualGps` čte ground truth od
začátku), ale kamera ano, a to strukturálně skrývá chybu odhadu. Léčba (`camerapose=truth`) už
existovala od rána téhož dne, jenže jako nevýchozí volba: výchozím režimem simulace tak byl ten,
ve kterém **lokalizaci změřit nelze**. Kamera přišroubovaná k odhadu je navíc fyzikální nesmysl.

Samotné přepnutí ale nestačilo. Model pohybu je ideální (žádný prokluz, odometrie hlásí přesné
rychlosti kol) a IMU hlásí **absolutní** kurz + bílý šum. Všechny chyby proto měly nulovou střední
hodnotu a byly ohraničené: odhad kolem pravdy jen šumí a **nikam nedriftuje**. Případ, který má
hranová lokalizace léčit — pomalu rostoucí chyba polohy a kurzu — v simulaci vůbec nevznikal, takže
se musel vnucovat ručně (`poseerror=`). To je *známá odpověď*, ne úloha. Prokluz kol a bias gyra
jsou systematické: neprůměrují se pryč a chyba roste s časem.

Bez ground truth v záznamu by se konvergence dala posoudit zase jen proti vnucené známé hodnotě —
odhad v záznamu byl, skutečnost nikde.

**Důsledky.**
- Záznamy pořízené do 22. 8. 2026 běžely na `fusion`; kdo reprodukuje starší běh, zadá
  `camerapose=fusion` explicitně. Čísla obou testů z téhož dne platí beze změny (jely
  s explicitním `truth`).
- `SimulatedRobot` nově rozlišuje **nominální** veličiny (enkodéry, rychlosti kol → odometrie)
  od **skutečných** (poloha, `Speed`, `AngularSpeed` → GPS, gyro). Bez prokluzu jsou totožné
  a rychlá větev v `Step` drží dosavadní výsledky bit po bitu.
- Nastavení žije v jedné sdílené instanci `ARBotHW.VirtualSensors` a mění se za běhu (panel
  *Tools → Virtuální senzory*, který zároveň živě měří chybu lokalizace a její RMS).
- **Ověřeno za běhu** (22. 8. 2026): A/B self-test dal chybu polohy p50 0,304 m bez korekcí vs.
  0,027 m s nimi. **Ale robot přitom stál** — self-test nemá jak zadat cíl navigace
  (`goal=lat,lon` neexistuje), takže **prokluz kol zůstává za běhu neověřený** a čísla popisují
  usazení odhadu u stojícího robota. Panel „Virtuální senzory" za běhu neotevřen.

**Odkazy:** [virtual-hw.md](virtual-hw.md#systematické-chyby-prokluz-kol-a-bias-imu-22-8-2026),
`Src/ARBot.Common/Simulation/SimulatedRobot.cs`, `Src/ARBot.Common/Logs/GroundTruthMsg.cs`,
`Src/ARBot.HAL/Devices/VirtualSensorOptions.cs`, `Src/ARBot/ViewModels/VirtualSensorsDocument.cs`.


### 2026-08-20 — Korelace: přímá korekce pózy, ne stav pro posun mapa↔GPS — NÁVRH, NEROZHODNUTO
**Co:** korelace s mapou koriguje **přímo pózu** ve fúzi (tedy tak, jak to dnes dělá), ale až po
splnění **tří podmínek** níž. Estimace posunu mapa↔GPS jako samostatného stavu EKF se **odkládá**
jako záložní varianta pro případ, že bude potřeba absolutní poloha nezávislá na mapě.

> **Revize téhož dne.** Zápis původně doporučoval opačně — stavovou variantu jako cíl a přímý zásah
> jako mezikrok. Otočila to autorova otázka „je potřeba vůbec ten posun odhadovat EKF? nestačilo by se
> vrátit k přímému zásahu? ano, bude se přetahovat GPS s kamerou, vadí to?". Vadí to méně, než jsem
> myslel, a stavová varianta stojí víc, než jsem přiznával. Původní analýza zůstává níž, protože
> většina platí dál — změnil se závěr, ne fakta.

**Proč přímý zásah stačí.** „Přetahování" GPS s kamerou není kmitání, jsou to dvě měření téže
veličiny a filtr je zváží podle σ. S naměřenými čísly (σ korelace 0,105 m, σ virtuální GPS 2,12 m)
je poměr vah `(2,12/0,105)² ≈ 400`, takže korelace přehlasuje GPS **~400:1** a póza sedne prakticky
na mapu. Nic neosciluje.

> **Oprava čísla (21. 8. 2026).** Poměr 400:1 je nadsazený asi o řád a autor to odhadl správně
> („odhad vlivu 1:400 nebude taky úplně reálný"). Tři chyby: 2,12 m je **2D radiální** σ GPS
> (= 1,5·√2), kdežto osové měření korelace je 1D (per osu je σ GPS `GpsPosStd` = 1,5 m); naměřená
> σ korelace je 0,150 m, ne 0,105; a hlavně se **nepočítala kadence** — GPS jde 5 Hz, korelace
> 1,74 Hz. Po opravě **~35:1** na těsné ose, ~18:1 na volné. I to je strop, protože sousední cykly
> korelace nejsou nezávislé. Závěr rozhodnutí („přetahování nevadí, hlídat se musí honestní σ")
> tím **nepadá** — jen je ta jednička v poměru blíž, než se psalo. Rozpad čísla a měření:
> [map-correlation-localization.md](map-correlation-localization.md#naměřeno-21-8-2026-debug-vs-release-nad-dvěma-záznamy).

A **to je pro jízdu žádoucí**: mrkev, trasa, cíle misí i výdejní místa jsou mapově relativní, takže
póza v mapovém rámci dává správnou mrkev vůči cestě. Nesouhlas s GNSS rámcem by vadil jen u něčeho,
co jde mimo mapu — a u tohoto robota nic takového není. K tomu: **absolutní přesnost je stejně
omezená chybou mapy** — kdo se lokalizuje proti mapě, nemůže být absolutně přesnější než ta mapa.
Oddělovat rámce se vyplatí jen s použitím pro absolutní polohu, které nejde přes mapu.

**Co se tím ztratí — jediná vážná věc.** Při 400:1 přestane být **GPS nezávislou kontrolou**.
Specifikace vede riziko „souběžná cesta blíž než `SearchRangeM` → přeskočení na vedlejší cestu";
kdyby se korelace zachytila na paralelní cestě dva metry vedle, se stavem by ji GPS k póze nepustila
a projevilo by se to jako nesmyslný posun, kdežto při přímém zásahu **si pózu unese a nikdo to
nezastaví**.

Jde to ale koupit zpět **mnohem levněji než celým stavem**: explicitní strop na nesouhlas s GPS —
„nepřijmi korekci, která posadí pózu dál než N·σ_GPS od GPS oblaku". Jedna podmínka
v `SendMeasurements` proti třem novým stavům, druhému rámci a povinnosti u každého čísla říkat,
ve kterém rámci je.

**Tři podmínky, bez kterých to nepustit** (všechny už jsou na seznamu otevřených úkolů):
1. **Honestní σ** — [otevřený úkol č. 1](map-correlation-localization.md). Bez toho korelace
   přehlasuje GPS 400:1 na základě jistoty, kterou si nezasloužila. **To je skutečný problém, ne to
   přetahování.**
2. **Rychlostní limit na aplikovanou korekci** — jinak je to krok a rozbije grid
   (`PoseJumpDetector`, tolerance 0,5 m), mrkev i regulátor. `MaxOffsetM` omezuje naměřený posun, ne
   aplikovaný krok.
3. **Strop na nesouhlas s GPS** — náhrada za ztracenou nezávislou kontrolu (viz výš).

**Gating:** výpočet nemožnosti v [map-correlation-localization.md](map-correlation-localization.md)
platí i tady, ale s podstatným rozdílem — u přímé korekce je nesouhlas **přechodný**, ne trvalý.
Póza se posune do mapového rámce a inovace klesne k nule; stačí projít tím přechodem, na což je
`GateMode.Soft` (`R' = R × NIS/prah`). U stavové varianty by nesouhlas dostal místo ve stavu, ale
tuhle výhodu `Soft` dorovná i bez ní.

**Co z původní analýzy platí dál:**

- **Kamera neměří polohu, měří vztah k cestě.** Podporují to naměřená čísla: příčná chyba nalezena
  s přesností jednotek **mm**, podélná na přímé cestě **vůbec**. Obě role, které autor chce, padnou
  na dvě složky s různou observovatelností:

  | složka | observovatelná | poznámka |
  |---|---|---|
  | **napříč** cestou | pořád | naměřeno na jednotky mm (vnucená chyba, 19. 8. 2026) |
  | **podél** cesty | jen na struktuře — odbočka, ohyb, změna šířky | jinak nejistota roste, na odbočce skokem klesne |

  „Odbočení zpřesní pozici" je doslova observovatelnost podélné složky. Stejný vzor jako uzavření
  smyčky v SLAMu.
- **GPS může lhát a mapa může být špatně nakreslená** ve tvaru i v pozici. To se nemění; mění se jen
  odpověď na to, kde ten nesouhlas nechat bydlet — v póze místo ve vlastním stavu.
- **Atribuce není potřeba ani možná.** Jestli je chyba v mapě nebo v GPS, dává stejný pozorovatelný
  jev a z dat se to oddělit nedá — a pro použití na tom nezáleží.

**Co jsem (asistent) přeceňoval:**
- *Paměť přes výpadky korelace.* Tvrdil jsem, že ji dá jen stav. Po dopočtu je ten argument slabý:
  po korekci je `P` utažené, jeden GPS fix má zesílení ~0,0025, takže korekce odtéká na časové škále
  **desítek sekund**. Přímý zásah paměť drží taky, jen ne navždy.
- *Výhoda „aplikovat posun na mapu, ne na robota".* Zachrání grid od zahazování, ale **mrkev se
  posune tak jako tak** — a mrkev robota řídí.
- *Prezentovatelnost.* U přímého zásahu problém vůbec nevzniká: jeden rámec, jedna mapa, jedna póza.
  Autorova námitka („bude se to blbě prezentovat") tedy nakonec argumentuje **pro** jednodušší
  variantu.

**Kdy by stavová varianta byla potřeba:** až bude použití pro absolutní polohu nezávislou na mapě
(návrat do depa podle GNSS, hlášení polohy mimo mapový rámec, fúze s jiným zdrojem mapy). Dnes to
není vidět. Rozhodující konstantou by pak byl **procesní šum na posunu** — moc velký pohltí i
skutečnou chybu lokalizace, moc malý nestíhá pootočenou mapu ani plovoucí bias GPS. A aditivní
translace pohltí *rotaci* mapy jen lokálně, takže by to nesměla být konstanta.

**Jak to ověřit: dvě mapy.** Platí bez ohledu na zvolenou variantu a `poseerror` na to nestačí —
vnucuje chybu do „kameriny představy o tom, kde je", což je fyzikálně nesmysl, a protože kamera
renderuje z odhadu, posunutí odhadu posune i obraz (naměřeno, `Dx` stálo na 0,800). Správně je
vnutit chybu do **mapy pro kameru** a nechat robota navigovat na pravé. U přímé korekce je pak
předpověď „póza zkonverguje k pravdě + vnucený posun a zůstane tam". Mechanismus a past (posun držet
pod polovinou šířky cesty) viz
[virtual-hw.md](virtual-hw.md#dvě-mapy--vnucená-chyba-do-mapy-pro-kameru).

**Co změřit před rozhodnutím:** jak velký je nesouhlas GPS↔mapa v praxi (na **reálném** záznamu — ve
virtuálním HW je „pravda" z definice GPS), jak často robot potká podélnou strukturu, a jaká σ
korelace vychází po opravě úkolu č. 1.

**Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) (naměřená data,
otevřené úkoly, výpočet nemožnosti), [virtual-hw.md](virtual-hw.md),
[global-navigation-runtime.md](global-navigation-runtime.md), [ekf-fusion.md](ekf-fusion.md).

### 2026-08-19 — Kurz se v EKF INICIALIZUJE, nejen měří (revize dřívějšího rozhodnutí) — ROZHODNUTO/HOTOVO
**Co:** vznikla `AsyncFusionEngine.InitializeHeading(theta, std, t)` jako obdoba
`InitializePosition` a `ARBotRuntime.InitializeStartPose` ji volá místo dřívějšího
`HeadingMeasurement`. Sdílené jádro obou inicializací je v jednom privátním `InitializeAxesLocked`,
aby se nemohly rozejít. Kdo kurz nezná (GPS fix ho nenese), posílá ho dál jako měření — ta cesta
zůstává.

**Proč:** dřívější odůvodnění „na rozdíl od polohy je chyba kurzu omezená a filtr si ho srovná"
neobstálo ve dvou bodech. (1) Při `P0 = I` je σ kurzu **1 rad (57°)**, takže měření o 170° vedle —
a to nastane, kdykoli robot míří na západ — má NIS ~8,7 proti χ²(1; 0,95) = 3,84 a po zapnutí
gatingu by se **zahodilo**. Je to tatáž latentní past, kterou u polohy popisuje
`InitializePosition`. (2) „Filtr si ho srovná" znamená, že po nějakou dobu je kurz špatný — a
`LocalNavigator` mezitím zapisuje do **world-ukotveného** occupancy gridu buňky s tím špatným
kurzem. Grid se neresampluje, takže tam zůstanou ležet; první korelace s mapou z nich vycházela
s **opačným znaménkem** a hlásila přitom `Reason = Ok`. Argument autora: když kurz znám, není důvod
ho filtru tajit a nechat ho k němu dojít přes měření, které se tam stejně hned posílá.

**Alternativy, které se zavrhly:** (a) kumulativní rotace v `PoseJumpDetector` — léčí symptom,
grid se pořád jednou znečistí a musí se zahodit; (b) podmínit zápisy do gridu konvergencí kurzu —
zdrží naplnění gridu a přidá další prahovou konstantu.

**Důsledky:** první korelace je poprvé správná — naměřeno 4 ze 4 běhů (−0,479 až −0,487 m proti
vnuceným −0,500), určená osa −6,3 až −6,8° místo −51 až −89°. Ustálený stav nedotčen. Nezávisle na
tom `PoseJumpDetector` nově hlídá i **rotaci** (`Check(x, y, theta, v, omega, t)`, `ToleranceRad`
default 5°) — abrupt skok kurzu byl pro pojistku dřív neviditelný plošně, ne jen při startu.

**Odkazy:** [`AsyncFusionEngine.cs`](../Src/ARBot.Common/Fusion/AsyncFusionEngine.cs),
[`PoseJumpDetector.cs`](../Src/ARBot.Common/Occupancy/PoseJumpDetector.cs),
[map-correlation-localization.md](map-correlation-localization.md),
[virtual-hw.md](virtual-hw.md).

### 2026-08-19 — Kovariance korelace: σ z Hessiánu, ale s dvěma větvemi — ROZHODNUTO/HOTOVO
**Co:** `CorrelationCovariance` počítá σ ze zakřivení skóre. Když je `−H` pozitivně definitní, jde
cestou `C = α·(−H)⁻¹`; když Cholesky spadne, počítá σ **přímo ze zakřivení** a plochému směru dá
`+∞`. V obou větvích se druhá proměnná vymarginalizuje Schurovým doplňkem.

**Proč:** singulární `−H` je na přímé cestě **normální stav**, ne chyba — posun podél přímé cesty
nemění nic, co robot vidí, takže podélná druhá derivace je přesně nula. První verze na tom vracela
`NoPeak` a zahazovala **celý** výsledek včetně příčné korekce, tedy hlavního výstupu, v nejčastější
situaci. Marginalizace je tam proto, že podmíněné σ jsou systematicky **menší** než marginální
(Schurův doplněk je ⪯ `A_tt`), a příliš malá σ je nebezpečná: fúze by korelátoru věřila víc, než si
zaslouží.

**Neuzavřené:** na cestě pod úhlem k osám gridu vychází podélná σ omylem konečná (0,18 m). Příčina
je principiální — skóre není lokálně kvadratické, je to „tent" `S ≈ 1 − k·|d|`. Dvě opravy selhaly;
detail, naměřená data i kandidáti k dalšímu zkoušení jsou v
[map-correlation-localization.md](map-correlation-localization.md), Otevřené úkoly.

### 2026-08-19 — Nejednoznačnost korelace se měří podél os, ne ve 2D — ROZHODNUTO/HOTOVO
**Co:** konkurenční maximum se hledá **podél určené osy** (a podél kolmé, když se ta má posílat), ne
mezi všemi kandidáty ve 2D. Konkurent podél určené osy potlačí celý cyklus; konkurent podél volné osy
potlačí **jen tu osu**.

**Proč:** ve 2D je na přímé cestě kandidát posunutý **podél** cesty skóre přesně stejný jako maximum.
To ale není nejednoznačnost — je to tatáž odpověď posunutá ve směru, který odhad už prohlásil za
neznámý, a ta osa se do fúze beztak neposílá. Původní pravidlo proto hlásilo `Ambiguous` na **každé**
přímé cestě a potlačilo i příčnou korekci určenou na 11 cm. Kolmý směr se hlídá zvlášť proto, že bez
toho by šla do fúze podélná korekce, kterou nekontroluje nic — což je nebezpečné právě tam, kde
podélná σ vyjde omylem konečná (viz předchozí záznam). Vedlejší důsledek: pořadí rozhodovacích
pravidel se změnilo, nejednoznačnost je poslední, protože bez maxima neexistuje osa, podél které měřit.

### 2026-08-19 — Remíza ve skenu se rozhoduje ve prospěch středu okna — ROZHODNUTO/HOTOVO
**Co:** při shodném skóre bere `CorrelationScorer.Scan` kandidáta **nejblíž středu okna**, ne
prvního nalezeného. Vzdálenost se měří v krocích (bez jednotek).

**Proč:** naivní „první vyhrává" vracelo na ploché části skóre **okraj okna** — maximum se přilepilo
na `dx = −2,4 m` a korelátor pak sám sebe zamítl jako `OffsetTooLarge`. Když data nedávají důvod
jednu z remízových možností preferovat, správná odpověď je „neopravuj": priorem je současný odhad
pózy. Tatáž třída vady (remíza + „první vyhrává" = posun k okraji) se v tomhle návrhu objevila
dvakrát, podruhé u prohledávání směrů — stojí za zapamatování.

### 2026-08-17 — Graf telemetrie se kreslí vlastním controlem, ne OxyPlotem — ROZHODNUTO/HOTOVO
**Co:** `TelemetryChartControl` (vlastní `Control.Render`) místo grafové knihovny. Autor měl dobrou
zkušenost s **OxyPlotem** a explicitně ho zmínil.

**Proč:** oficiální `OxyPlot.Avalonia` je ve verzi 2.1.0 a cílí na **Avalonii 11**; projekt drží
Avalonii 12. Pro dvanáctku existuje jen neoficiální fork `Oxyplot.Avalonia12` (2.1.2) od jednoho
vydavatele se **162 staženími** — na knihovnu, kterou by měl robot vozit v produkci, je to příliš
málo ověřená a příliš snadno opuštěná závislost. Přesně ten typ problému, který už projekt řeší
u `Avalonia.Controls.DataGrid` (verze nad 12.0.0 si vynucují Avalonii ≥ 12.0.5 a build spadne na
`NU1605`). Data jsou navíc už ve sloupcových polích a projekt kreslené controly má (kompas, umělý
horizont, robot-centrický pohled), takže vlastní kreslení nebylo drahé.

**Důsledky:** funkce, které by knihovna dala zadarmo, se dopisují ručně — hotové je odečítátko
hodnot pod myší (obdoba trackeru), lupa času i hodnot, obálka min/max u hustých dat, kurzor
přehrávání a klik = skok v přehrávání. Chybí anotace, výběr obdélníkem, export obrázku a legenda
v ploše grafu. **Rozhodnutí se má přehodnotit, až OxyPlot (nebo jiná knihovna) vydá oficiální
podporu Avalonie 12** — cena přechodu je jeden control, protože `TelemetrySeries` je na kreslení
nezávislá.

**Odkazy:** [doc/telemetry-view.md](telemetry-view.md), `Src/ARBot/Views/Controls/TelemetryChartControl.cs`,
`Src/ARBot.Common/Telemetry/TelemetrySeries.cs`.

### 2026-08-16 — Vzdálenosti se počítají na WGS84, ne na kouli; `ProjectOntoSegment` zůstává výjimkou — ROZHODNUTO/HOTOVO
**Co:** `GreatCircle` bere `Ellipsoid` (výchozí `Wgs84`) a počítá geodetiku (Vincenty) místo
haversinu na pevné kouli R = 6 371 000 m. `LLA.Distance(Ellipsoid, …)` na něj deleguje, aby byl
v aplikaci jediný výpočet vzdálenosti.

**Proč:** modely se rozcházely. `GeoReference` převádí na lokální metry přes WGS84 (ECEF),
`GreatCircle` měřil na kouli — na šířce 50° vyšlo 10,000 m v ENU jako 9,969 m v grafu (−0,31 %).
Ve směru východ–západ je totiž směrodatný poloměr křivosti v prvním vertikálu N(50°) ≈ 6 390 693 m,
ne střední poloměr koule. Délky hran v grafu se tím rozcházely s metrickým světem, ve kterém robot
jede. Koule zůstává dostupná jako `Ellipsoid.Sphere` (nebo libovolný `new Ellipsoid(r, r)`) —
vzorec se pro `a == b` sám degeneruje na great-circle.

**Výjimka, která zůstala:** `LLA.ProjectOntoSegment` dál používá jedinou střední kouli. Měřítko se
při projekci na úsečku **vykrátí** (parametr `t` i poměry vzdáleností vyjdou stejné), takže přesnější
poloměry nic nezpřesní — jen posunou poslední bity vráceného bodu. A právě na nich visí degenerovaný
split cílové hrany (cíl přesně v uzlu, `t` = 0 nebo 1): pokus o „sjednocení" i tady shodil regresní
test `GoalFieldSplitTests.DeadEndGoal_RobotOnGoalSegment_FiniteCost`. **Důsledek:** až se to bude
měnit, musí to být spolu s poctivým ošetřením degenerovaného splitu, ne mimochodem.

**Nedotčeno:** stará generace kódu (`Driver/`, `Maps/Map.cs`, `Logs/Marker.cs`, `MapPoint.cs`)
používá `Ellipsoid.Sphere` jako součást vlastní konvence transformací — tam se nesahalo.

**Odkazy:** `Src/ARBot.Common/Coordinates/{GreatCircle,LLA}.cs`,
`Src/ARBot.Common.Tests/OsmNav.Tests/Geo/GreatCircleEllipsoidTests.cs`.

### 2026-08-11 — Lokální mapa patří do WORLD pohledu; rozjetá dráha se hlídá proti mapě — ROZHODNUTO/HOTOVO
Dvě korekce z revize předchozí implementace (obojí vzešlo z připomínek při review):
- **Vrstvy occupancy + plán jsou ve world pohledu, ne v robot-centrickém.** Robot-centrický pohled je
  svázaný s robotem **včetně orientace**, takže world-kotvená akumulovaná mapa by se v něm s každou
  zatáčkou otáčela — pro mapu matoucí (a rotace navíc mizí smysl toho, že je grid osově srovnaný).
  Ve world pohledu leží mapa pevně, robot se po ní pohybuje a sedí to na podklad (OSM/MBTiles).
  Robot-centrický pohled zůstává tomu, co je robot-centrické z podstaty: polárním gridům z kamer.
  Důsledek: rastr místo bitmapy s rotací (`MRaster` v obdélníku, PNG), cíl se zadává **Ctrl+klikem**
  do mapy (převod Web Mercator → lokální ENU přes stejný `GeoReference` jako ostatní lokální vrstvy).
- **Když nový plán nevznikne, kontroluje se rozjetá dráha proti AKTUÁLNÍ mapě.** Původní „plán bez
  dráhy regulátor nepřepisuje" byla díra: mapa se mezitím změnila a na trase, po které robot jede,
  už může být překážka. Watchdog nižší smyčky dobrzdí až po `PathControlTimeOut` (500 ms) a z 0,8 m/s
  je brzdná dráha dalších ~1 m — pozdě. Nově se každý cyklus ověřuje úsek, na který je robot fakticky
  zavázaný (`v²/(2a) + v·Ts + rezerva` od průmětu robotu na dráhu); při kolizi (`Blocked` nebo odstup
  pod `SafeDist`; `Unknown` kolize NENÍ) se řízení zahodí **okamžitě** (`Regulator = null`) a hlásí se
  `LocalPlanStatus.AbortedCollision`. Volná dráha se nezahazuje — dobrzdění zůstává řízené na
  watchdogu; stojící robot nouzově nezastavuje (nulová brzdná dráha).
- **Odkazy:** `Src/ARBot.Common/Occupancy/LocalNavigator.cs`,
  `Src/ARBot/ViewModels/WorldViewDocument.cs`, [occupancy-and-local-planning.md](occupancy-and-local-planning.md).

### 2026-08-11 — `GetStateAt` mimo okno historie vrací `null`; occupancy se kreslí bitmapou — ROZHODNUTO/HOTOVO
Dvě rozhodnutí z dotažení lokální navigace do runtime (detaily v
[occupancy-and-local-planning.md](occupancy-and-local-planning.md)):
- **`AsyncFusionEngine.GetStateAt(t)` vrací pro `t < tBase` `null`** místo dosavadního tichého
  fallbacku na bazový stav. Ten vracel pózu až o `HistoryWindow` (1 s) starou, aniž to volající poznal
  — při 0,8 m/s je to 80 cm. Zapsat takovou pózu do lokální mapy ji otráví mnohem hůř, než když jeden
  snímek chybí. `ControlLoop` na `null` **zastaví** (bezpečný stav), `LocalNavigator` snímek **zahodí**.
  *Hranice okna sama je uvnitř* (`t == tBase` vrací bazový stav) — jinak by první tik, jehož čas je
  shodný s časem prvního měření, zbytečně zastavil; odhalil to existující test `ControlLoopTests`.
  Případ „ještě nedošlo žádné měření" zůstal beze změny, aby se při startu emitoval `RobotStateMsg`.
- **Occupancy vrstva se kreslí jako rastr, ne po buňkách.** 65 536 buněk jako featury/kreslené obdélníky
  by UI zabilo. Grid je **osově srovnaný se světem**, takže z něj stačí udělat obrázek a položit ho do
  obdélníku — právě world-kotvení, které dělá akumulaci levnou, dělá levnou i vizualizaci.
  *(Upřesněno záznamem výše: vrstva se přesunula do world pohledu, takže rastr je `MRaster`/PNG bez
  jakékoli rotace; původní varianta s `WriteableBitmap` a afinní transformací v robot-centrickém
  pohledu odpadla i s tou rotací.)*
- **Odkazy:** `Src/ARBot.Common/Fusion/AsyncFusionEngine.cs`, `Src/ARBot.Common/Runtime/ControlLoop.cs`,
  `Src/ARBot.Common/Occupancy/LocalNavigator.cs`, `Src/ARBot/Views/Controls/RobotCentricControl.cs`.

### 2026-08-10 — Azimutové hranice gridu zamítnuty; azimut se hledá přes SLOUPEC obrazu — ROZHODNUTO/HOTOVO
**Koriguje níže uvedený návrh z téhož dne.** Návrh počítal s tím, že se do `PolarTraversabilityGrid`
přidá tabulka **azimutových hranic** (pole A+1 úhlů) a zápis do occupancy pak z bodu `(x,y)` najde
azimutovou buňku binárním hledáním v úhlu. **Při implementaci se ukázalo, že je to geometricky
neproveditelné:** u sklopené kamery **není sloupec obrazu konstantním azimutem** — azimut pozemního
bodu na jednom sloupci se mění s řádkem (u naší geometrie sklon 20°, HFOV ~77° o ~0,15 rad, tedy
skoro o celou šířku azimutové buňky). Jediná hodnota na hranici by byla systematicky špatná; odhalil
to test, který měl hranice ověřit (těžiště buňky vycházelo mimo vlastní buňku).
- **Řešení:** bod země se **promítne do obrazu** (`ICameraProjection.Transform`, rovina `z = 0`) a
  azimutová buňka se vezme z jeho **sloupce**. Tím se **přesně invertuje** mapování, které použil
  `CameraFrameProcessor.BuildGrid` (azimut = skupina `ColumnsPerCell` sloupců) — lookup sedí přesně,
  nikoli přibližně. Radiální prstenec se bere ze vzdálenosti, protože přesně tak ho počítal i `BuildGrid`.
  Stejný vzor (bod země → pixel → vzorek) už v repozitáři používá `PathEdgeFinder`.
- **Důsledky:** `AzimuthEdges` v gridu nevznikly (formát záznamu se o ně nerozšířil); místo nich jsou
  na gridu `AzimuthBinFromColumn(column, edgeColumnTrim)` a `RadialBin(range)`. `CameraFrame`
  **FormatVersion 3 → 4** kvůli `Projection` (samotné) zůstává. Renderer polárního gridu si azimutové
  hranice dál rekonstruuje z těžišť — a je teď zřejmé, že jinak to ani nejde.
- **Odkazy:** `Src/ARBot.Common/Occupancy/OccupancyIntegrator.cs`,
  `Src/ARBot.Common/Vision/PolarTraversabilityGrid.cs`,
  `Src/ARBot.Common.Tests/Vision/PolarGridLookupTest.cs`,
  [occupancy-and-local-planning.md](occupancy-and-local-planning.md).

### 2026-08-10 — Occupancy grid a lokální plánování: návrh — ROZHODNUTO/ČÁSTEČNĚ IMPLEMENTOVÁNO
Sloučení sjízdnosti z hloubky (`CameraFrame.Grid`) a z barvy (`CameraFrame.ImageProbability`) do
jednoho kartézského occupancy gridu + plánovač, který z něj vyrobí `RegulatorWayPoint[]` pro
`IPathPlanner`. Celý návrh je v [occupancy-and-local-planning.md](occupancy-and-local-planning.md);
sem jen rozhodnutí a *proč*:
- **Grid kotvený ve světě (ENU), kruhový buffer jen v posunu.** Rotace robotu se mapy nedotkne;
  alternativa „grid natočený s robotem" by vyžadovala resampling každý tik (rozmazává, dražší).
  Cenou je závislost na kvalitě lokalizace — řeší se clampem log-odds a krátkou pamětí (jednotky
  sekund), ne dokonalou lokalizací.
- **Dva rovnocenné kanály `LOcc` (geometrie) + `LRoad` (sémantika), log-odds ve `sbyte`.** Dvě různé
  modality s různou charakteristikou chyb; sloučením do jednoho čísla by zmizela informace, *který*
  z nich průjezd zakázal (ladění, diagnostika). Pro jízdu jsou ale rovnocenné — stačí, aby jeden
  nedovolil průjezd. `sbyte` (měřítko 0,1, clamp ±5) → 128 KB celkem, vejde se do L2.
- **„Nemám data o cestě" ≠ „není to cesta".** `LRoad` blokuje jen pod prahem s dostatečnou jistotou —
  jinak by robot po startu nikdy nevyjel (RGB kanál je zpočátku všude nulový). Symetrické k
  `Unknown ≠ Free` u polárního gridu.
- **Skrz `UNKNOWN` se smí plánovat, ale nesmí se do něj vjet** — a neřeší se to zvláštním pravidlem,
  nýbrž jediným invariantem rychlostní obálky: *nikdy nejeď rychleji, než z čeho zastavíš na hranici
  potvrzeně průjezdného* (`v ≤ sqrt(2·a·s_free)`). Robot k nejasnému místu dojede, senzory ho cestou
  dosvítí, a buď se otevře, nebo ho přeplánování objede, nebo robot zastaví na hranici.
- **Cena plánování = jízdní čas** (`délka / v_limit(d)`), tvrdý odstup `SafeDist` zvlášť jako
  neprůchodnost. Tím se požadavek „drž se dál, ale smíš blíž za cenu nižší rychlosti" stane jedinou
  cenovou funkcí — žádné ruční vyvažování vzdálenosti proti délce, žádný druhý režim.
  Nový `Profile.PrefDist = 0,8 m` = odkud výš už se rychlost neomezuje; mezi `SafeDist` a `PrefDist`
  **lineární** rampa (u bočního odstupu nejde o brzdnou dráhu — ta je výhradně v `v_brake`).
- **A\* na 5 cm mřížce, ne hybrid-A\*/lattice/RRT** — kinematiku a dynamiku už řeší `PathPlanner` +
  `PathResult` (geometrie rohů, brzdná obálka, feedforward); duplikovat ji v plánovači je zbytečné.
- **`MaxPositionError` waypointu = skutečná volná rezerva** (`d_min − SafeDist`). Zaoblení rohu
  obloukem, které z ε ukusuje, tak nikdy nezasáhne do bezpečnostního odstupu.
- **Žádná hystereze plánu.** Držet plán spočtený nad starší mapou = jet proti důkazům, které robot
  už má → riziko kolize. Každý cyklus plný přepočet a validace proti aktuálnímu gridu. Riziko
  oscilace (skákání mezi objetím zleva/zprava) se řeší **poctivější cenou** — započtením času
  otočení `|Δθ|/ω_max` z aktuálního kurzu — ne lepivostí v čase.
- **Póza z EKF v čase pořízení snímku, per kamera zvlášť** (`GetStateAt(frame.TimeStamp)`). Jen tak
  se snímky obou kamer zarovnají správně (100 ms = 8 cm = 1,6 buňky při 0,8 m/s). `GetStateAt` je
  zamčené, umí dotaz do minulosti a `Enqueue` řadí podle času, ne podle příchodu — stojí to na
  předpokladu, že zpracování kamery trvá výrazně déle než IMU/GPS/motorů.
- **`GetStateAt(t)` vrací `null` mimo okno historie** místo tichého fallbacku na bazový stav (ten
  vracel pózu až o vteřinu starou, aniž to volající poznal). Snímek se pak zahodí — zapsat ho se
  špatnou pózou otráví mapu hůř, než když jeden chybí. `ControlLoop` na `null` zastaví.
- **Ve View navigace neběží** (jen přehrávání zpráv) → occupancy grid a plán se **zaznamenávají**
  jako zprávy. Projekce ukládaná do `CameraFrame` je investice do budoucího `Simulate` a offline
  analýzy, ne pro View; cache se neserializují (odvozené, ~5 MB) a staví se líně per kamera.
- **Odkazy:** [occupancy-and-local-planning.md](occupancy-and-local-planning.md),
  [traversability-grid.md](traversability-grid.md), [path-following.md](path-following.md),
  [ekf-fusion.md](ekf-fusion.md).
- **Upřesnění z implementace:** (a) *azimutové hranice zamítnuty* — viz záznam výše; (b) za hranicí
  potvrzeného je strop `MinCostSpeed` (~5 cm/s), ne přesná nula, protože `PathPlanner` chápe
  `Speed == 0` jako „bez stropu" a tvrdé zastavení může zadrhnout (stání prostor nedosvítí);
  (c) konec dráhy je vždy hranicí známého, jinak by poslední uzel dostal plnou rychlost; (d) přidána
  **eskapovací zóna** `EscapeRadius` (0,5 m) — bez ní by robot zastavený blíž než `SafeDist` neměl
  průjezdnou výchozí buňku a nemohl by odjet.

### 2026-08-09 — Hranice cesty (`PathEdges`): počítá `CameraFrameProcessor`, ukládají se do `CameraFrame` — ROZHODNUTO/HOTOVO
Volání `cu.PathEdges(...)` v `D435Camera.GetMeasurement` **výsledek odjakživa zahazovalo** (i před
refaktorem vizuální cesty šel jen do lokální proměnné) a downstream konzument `PathEdgeFinder` si hrany
počítal znovu sám — navíc se v runtime vůbec nevolal. Rozhodnutí:
- **Výpočet vlastní `CameraFrameProcessor`** (odvozené entity rámce patří jemu, ne HAL kameře): dostává
  volitelný `IComputeUnit` a hrany počítá z `frame.ImageProbability` **bez fallbacku** — bez jednotky se
  hrany prostě nepočítají (`PathEdges = null`). Souřadnice hran se škálují do prostoru `ImageRGB`
  (konvence `PathEdgeFinderItem.Edges`).
- **Úložiště je `CameraFrame.PathEdges`** (`List<PathEdge>`, per snímek čerstvý seznam — sdílí se referencí
  jako `Grid`) a serializuje se s rámcem (**FormatVersion 2 → 3**, čtecí větve pro v1/v2 zachovány).
- **`PathEdgeFinder.Process` už hrany nedetekuje** — bere předem spočtené `PathEdgeFinderItem.Edges`
  (plněné z `CameraFrame.PathEdges`); parametr `NativeComputeUnit sc` odstraněn, stará detekce ponechána
  zakomentovaná do ověření (pravidlo CLAUDE.md).
- **Runtime:** `ARBotRuntime` předává procesoru per-kamera `NativeComputeUnit` s minimálními rozměry
  agregačního pole (pro `PathEdges` se používá jen bezstavový nativní `FindPathEdge`).
- **Odkazy:** `Src/ARBot.Common/Vision/CameraFrameProcessor.cs`, `Src/ARBot.Common/Devices/CameraFrame.cs`,
  `Src/ARBot.Common/Common/PathEdgeFinder.cs`, [doc/record-replay.md](record-replay.md) (verzování zpráv).

### 2026-08-04 — World pohled: mapový engine **Mapsui** (vs. vlastní tile control) — ROZHODNUTO/HOTOVO
Nový world (geo) pohled potřebuje mapu s dlaždicovým podkladem, zoom/pan a vrstvami. Zvažovány dvě cesty:
(a) **vlastní** slippy-map `Control` přes `DrawingContext` (jako `RobotCentricControl`) — bez závislostí,
plná kontrola nad offline/ARM, ale hodně kódu (dlaždicová matematika, async stahování, disková cache,
gesta); (b) knihovna **Mapsui**. Zvoleno **(b) Mapsui** — hotový pan/zoom/vrstvy, rychlé zprovoznění,
existuje dedikovaný balíček **`Mapsui.Avalonia12`** kompatibilní s Avalonia 12.0.3 (ověřeno restore+build).
- **Důsledky:** přidány NuGet závislosti `Mapsui.Avalonia12`, `Mapsui.Nts` (čáry/geometrie),
  `BruTile.MbTiles` (offline). ViewModel vlastní Mapsui `Map`; View mu ho přiřadí do `MapControl.Map`
  v code-behind (mimo design-time). Mapsui renderuje přes **SkiaSharp** → na ARM64 nutno ověřit nativní
  assety na zařízení (build neblokuje).
- **Offline/ARM:** podklad je plně vypínatelný a na ARM je výchozí `None` ⇒ na OrangePI žádné pokusy
  o internet (splněn požadavek zadání).
- **Nezvoleno teď:** vyhledávání (geocoding) a podklady Mapy.cz/Google (API klíč + ToS omezení).
- **Odkazy:** [doc/world-view.md](world-view.md), `Src/ARBot/ViewModels/WorldViewDocument.cs`, `Src/ARBot/ARBot.csproj`.

### 2026-08-04 — Názvosloví geometrie: `ProjectOnto…` (projekce) vs `Intersection` (průsečík) — ROZHODNUTO/HOTOVO
Napříč kódem se pro **projekci bodu na přímku/úsek** (pata kolmice) používalo matoucí sloveso `Intersect`
(`MapWay.Intersect`, `NavigationBase.Intersect`), zatímco `Intersection` (`Line2D`/`LineSegment2D`) znamená
**skutečný průsečík dvou přímek** — dvě různé operace se zaměnitelnými názvy. Sjednocená konvence:
- **Projekce bodu** na přímku/úsek → `ProjectOnto…`:
  - `ProjectOntoLine(...)` = na **nekonečnou** přímku, `pos` neomezené (může být mimo úsek).
  - `ProjectOntoSegment(...)` = na **úsek**, t ořezané do [0,1].
- **Průsečík** dvou přímek/úseček → podstatné jméno `Intersection(...)` (beze změny).
- **Provedeno:** `MapWay.Intersect`→`ProjectOntoLine`, `NavigationBase.Intersect`→`ProjectOntoLine`
  (+ volání v `Map.cs`), a `Line2D.Intersection(Point2D)`→`ProjectOntoLine` (byla to projekce/pata kolmice,
  ne průsečík — call-sity v `Points2Lines`, `PathEdgeFinder`, testech). Skutečné průsečíky
  `Line2D.Intersection(Line2D)`/statická a `Line2D.CircleIntersect` i `Graph.Intersect` (jiná doména) ponechány.
- **Souvislý úklid:** `ProjectOntoSegment` přesunut z krátkovlnné `GeoSegment` **do `LLA`** jako instanční
  metoda (konzistentně s `LLA.Distance`; `GeoSegment` smazán) — na přání „věci na jednom místě".
- **Neuzavřeno (možný další krok):** nested `NavigationBase.IntersectI` (drží výsledek projekce) a rodina
  `NearestPoint`/`Project`/`Closest` (Map, PathMapCorelator, MotionArc) zůstávají — širší sjednocení odloženo.
**Ověřeno x64:** celá sada 321 / 4 skip / 0 fail, appka `ARBot` build zeleno.
**Odkazy:** `Maps/MapWay.cs`, `Navigations/NavigationBase.cs`, `Maps/Map.cs`, `Coordinates/LLA.cs`.

### 2026-08-04 — Sjednocení geo: OsmNav `GeoPoint`/`GeoMath` → systémové `LLA`/`GreatCircle` — ROZHODNUTO/HOTOVO
OsmNav měl vlastní lehký geotyp `GeoPoint` (`record struct`, **stupně**) + `GeoMath` (Haversine +
projekce na úsek). Zbytek systému (GPS, `ARBotState`, mapy) používá `ARBot.Common.Coordinates.LLA`.
Sjednoceno na `LLA`, `GeoPoint`/`GeoMath` **smazány**.
- **Proč (i přes rozdíly):** není to čistý duplikát jako `Point2DF` — `LLA` je **radiány + class + altitude/
  ellipsoid**, `GeoPoint` byl **stupně + value struct**. Rozhodlo, že **lokalizace produkuje `LLA`**
  (GPS/EKF) → až se OsmNav napojí na řídicí smyčku, poloha do `Navigator.Update` přijde jako `LLA` bez
  konverzního švu. Jednotný geotyp v celém systému.
- **Náhrady:** `GeoMath.HaversineMeters` → `GreatCircle.Distance` (haversine, R=6371000 — **numericky
  identické**). `GeoMath.ProjectOntoSegment` → **double** equirectangular projekce (přesně jako původní math;
  finálně `LLA.ProjectOntoSegment`, viz záznam výše). `GeoReference` (ECEF ENU) se pro projekci nepoužil:
  jeho `ToLocal` vrací `Point2D` (**float**) → ztráta přesnosti (~2e-6 na split ceně) shodila oracle testy;
  `double` projekce je vrátila přesně. Konstrukce ze stupňů: přidán `LLA.FromDegrees`.
- **Jednotky:** OSM je ve stupních; převod deg→rad je jen na hranici (`GraphBuilder`: `LLA.FromDegrees`;
  testy taktéž). Vnitřek počítá v radiánech.
- **Dotčeno:** `Node`, `RoadNetwork`, `GoalField`, `Navigator`, `Router`, `GraphBuilder` (6 zdrojů) +
  testy (`new GeoPoint(→LLA.FromDegrees(`, geo testy přepsány na nové API). `HALArmbian`/`HALWindows`
  se `GeoPoint` netýkají. **Ověřeno x64:** OsmNav 76/76, celá sada 321 / 4 skip / 0 fail.
**Odkazy:** `Coordinates/{LLA,GreatCircle}.cs`, `Maps/OsmNav/{Graph,Routing,Navigation,Osm}/…`,
[osm-nav.md](osm-nav.md) (sekce „Geo — sdílený Coordinates stack").

### 2026-08-04 — Sjednocení `Point2DF` → `Point2D` (odstranění duplicitního float bodu) — ROZHODNUTO/HOTOVO
Navazuje na sjednocení `Point2D` (níže). `ARBot.Common` měl **dva** float bodové typy: `Point2D`
a `Point2DF` (oba `[StructLayout(Sequential)]`, 2× `float`). `Point2DF` sloužil jen jako **blittable
nosič** pro nativní interop (pole `Point2DF[]`/`Point2DF[,]`: `Depth2XYZ`, `DepthTransform*`, `Segment2`)
a pro tabulku `IDepthCameraProjection.Camera2DToCamera3D`. `Point2DF` **smazán**, vše převedeno na `Point2D`.
- **Proč bezpečné:** oba typy mají identický nativní layout (Sequential, 2× float) → **ABI beze změny**,
  nativní strana nic nepozná. `Point2DF` se nikde nepoužíval přes operátory (`+`/`−`/`/`) ani `.Distance`,
  jen konstrukce `new Point2DF(x,y)` a pole → **žádný sémantický konflikt** (na rozdíl od `Point2D`/`Vector2D`).
  Přesnost se nemění (oba float).
- **Dopad na projekty:** `ICameraProjection`/`IDepthCameraProjection` člen `Camera2DToCamera3D` je teď
  `Point2D[,]`; implementace v `CameraProjection` a fake projekce v testech upraveny. `HALWindows`
  (nativní import v `D435CameraProjection`) upraven. `HALArmbian` **dědí** z `CameraProjection` a `Point2DF`
  nikde nejmenuje → beze změny, ARM build netřeba.
- **Orphan:** `ARBot.Common.Tests1` (není v `ARBot.slnx`) přejmenován pro konzistenci, ale nebuildí se.
- **Ověřeno x64:** `ARBot.Common` + `ARBot.HALWindows` build zeleno; testy 318 / 4 skip / 0 fail. Přeskočené
  jsou `Segment_*` (pre-existing) — ta cesta přes `Point2D[,]` ověřena kompilací + ABI-identitou, ne během.
**Odkazy:** `Common/Point2D.cs` (Point2DF.cs smazán), `Algorithms/ComputeUnit/NativeComputeUnit.cs`,
`Coordinates/{ICameraProjection,CameraProjection}.cs`, `HALWindows/Devices/Camera/D435CameraProjection.cs`.

### 2026-08-04 — Sjednocení `Point2D`: OsmNav/Colider převeden na sdílený `ARBot.Common.Point2D` (float) — ROZHODNUTO/HOTOVO
Nakopírovaný modul `Maps/OsmNav` přinesl vlastní `Colider.Point2D` (`readonly record struct`, **double**),
který kolidoval jménem se stávajícím `ARBot.Common.Point2D` (**float**). Sjednoceno na jeden typ:
- **Ponechán `ARBot.Common.Point2D` (float), OsmNav-verze smazána.** `ARBot.Common.Point2D` je základní
  bodový typ celého kódu (a `[StructLayout]`); float zůstává. (Pozn.: do nativního interopu jde `Point2DF`,
  ne `Point2D` — interop tím není dotčen.)
- **Přijata algebra bod/vektor z `ARBot.Common`.** Tam `Point2D − Point2D → Vector2D` (a `Vector2D` je
  **double**, nese `Length`/`Angle`), kdežto OsmNav `Point2D` slučoval bod i vektor (měl `Length`, `Angle`,
  skalární `*`, `−`→`Point2D`). Nelze přetížit podle návratového typu → **`MotionArc` přepsán** do této algebry.
- **`MotionArc` přepsán bez alokací.** `Vector2D` je *class* (reference typ); použít ho v O(1) analytickém
  `Project` by zaneslo alokace do hot-path (proti jeho návrhu). Proto pozice = `Point2D` (float), ale posuny,
  rotace a vzdálenosti se počítají v **lokálních `double`** (helpery `Offset`/`Rotate`/`Hypot`) — přesné
  a bez heap alokací. Ostatní Colider soubory (`Obstacle`, `RobotState`, `TrajectoryPredictor`) berou
  `Point2D` jen jako pozici → beze změny.
- **Přesnost (float) — vědomý kompromis.** Geo vrstva je mimo (má vlastní `GeoPoint` double). Colider je
  lokální planární matematika; jediné citlivé místo je `Math.Abs(d − Radius)` (rozdíl velkých téměř stejných
  čísel) u téměř rovných oblouků: práh `StraightYawRate = 1e-4` dovolí poloměr až ~10 km → ulp(float) ~1 mm.
  Proti `SafetyMargin = 0.5 m` a horizontu ~2 m funkčně nevadí; kdyby vadilo, řešením je zvednout
  `StraightYawRate`. Ověřeno: OsmNav 76/76, celá sada 318 zeleno (tolerance `1e-6`/`1e-9` přežily).
**Odkazy:** `Src/ARBot.Common/Maps/OsmNav/Colider/MotionArc.cs`, `Common/{Point2D,Vector2D}.cs`,
`Src/ARBot.Common.Tests/OsmNav.Tests/Colider/Point2DTests.cs`.

### 2026-08-02 — Sjednocení regulátorů: jedno `IRegulator`, jeden bodový regulátor přes `IMotionProfile` — ROZHODNUTO/HOTOVO
Navazuje na regulátor sledování dráhy (níže). Sjednocení, aby nižší smyčka regulovala transparentně na bod
i na dráhu:
- **`IRegulator` = `IPathController`** (splynuly): `Control(IModelState) → RegulatorResult` + `IsFinished`.
  Cíl (bod / dráha) drží regulátor uvnitř; profilové metody (`Dist2Speed`, …) z rozhraní zmizely (jsou v
  `IMotionProfile`). Dvě implementace: `PointRegulator` (bod) a `PathResult` (dráha).
- **`PointRegulator` nahradil `Regulator` i `SimplRegulator`.** Jediný rozdíl mezi nimi byl `IMotionProfile`
  (lichoběžník vs. odmocnina) a koeficient `stability` — obojí je teď parametr profilu. Vznikl
  `SqrtMotionProfile` (odmocninový zákon z `SimplRegulator`, ale **konzistentně** — `SimplRegulator.Control`
  počítal rotaci buggy). Staré třídy **smazány** až po důkazu parity (`PointRegulator(Trapezoid)` bit-identický
  s `Regulator.Control` přes mřížku stavů; `SqrtMotionProfile` == odmocninový zákon `SimplRegulator`), pak
  paritní testy překlopeny na golden/closed-form.
- **`ControlLoop.Path` → `ControlLoop.Regulator`** (typ `IRegulator`). Nižší smyčka teď jede libovolný regulátor.
**Odkazy:** `Src/ARBot.Common/Regulators/{IRegulator,PointRegulator,SqrtMotionProfile}.cs`, `ControlLoop.cs`,
[path-following.md](path-following.md). **Stav:** hotové, build + 242 testů zeleno.

### 2026-08-02 — Regulátor sledování dráhy: feedforward + brzdná obálka, ne proporcionální řízení — ROZHODNUTO/HOTOVO (Fáze 1–5)
Nový obecný regulátor, který robota vede **dráhou z waypointů** tak, aby každý uzel projel v rámci
`MaxPositionError` (ε) **maximální rychlostí** (uzly bez zastavení). Klíčová rozhodnutí a *proč*:
- **Feedforward + přeplánování z pózy, ne pure-pursuit `ω=v·κ`.** Statické proporcionální řízení na
  odchylku ignoruje dynamiku (accel-limit `ω`, `Ts=100 ms`, zpoždění EKF) a v tomto setupu **kmitá**
  (ověřeno z praxe). Zásah se místo toho každý tik generuje přes accel-limitovaný profil (`IMotionProfile`),
  uzavřená smyčka jde do plánu přes dynamiku, ne přes gain. Recykluje se bodová mechanika starého regulátoru.
- **Rohy kruhovým obloukem, ne klotoidou.** Chyba oblouk-vs-klotoida je na reálných parametrech **≤ ~5 mm**
  proti ε=100 mm (< 5 %), přechodová a hluboko pod nejistotou EKF (cm). Rozhoduje malý náběhový úhel
  `ω²/(2α)≈8°`. Klotoida se nevyplatí. Kryto rezervou `PathEpsilonMargin≈1 cm`.
- **Plán počítá jen zpětnou brzdnou obálku, ne dopředný průchod.** Akceleraci řeší runtime živě —
  `startSpeed = IModelState.Velocity`. Plán drží jen `VLimit(uzel)` = strop, ze kterého jde splnit budoucnost.
- **`τ_look ≈ 3·Ts` (lookahead úměrný rychlosti).** Analýza odchylky vs. `L_d` (viz doc): drží odchylku
  1–5 % ε a stabilitu při všech rychlostech (v ostrém rohu je `v` malé → `L_d` malé; `L_d/(v·Ts)` konstantní).
- **`ControlLoop.Path` jako settable property + watchdog, bez výchozí dráhy.** Vyšší smyčka (mapa/OSM)
  atomicky přehazuje dráhu; `null` = stání (bezpečný stav); zastaralá dráha (`PathControlTimeOut`) = dobrzdění
  po poslední trase. Nahrazuje dřívější pevný waypoint + starý `Regulator` v `ControlLoop`.
- **Staré regulátory (`Regulator`, `SimplRegulator`) ponechány beze změny chování** (pravidlo „nemazat staré
  dokud nové nepotvrdí testy"); `Control` narovnán na jeden waypoint (Fáze 1). Nový kód proven 237 testy
  (parita profilu, plánovač, simulace sledování, integrace).
**Odkazy:** [path-following.md](path-following.md), `Src/ARBot.Common/Regulators/{IMotionProfile,TrapezoidMotionProfile,IPathPlanner,IRegulator,PathPlanner,PathResult}.cs`, `Src/ARBot.Common/Runtime/ControlLoop.cs`, `Src/ARBot.Common/Configuration/Profile.cs`. **Stav:** Fáze 1–5 hotové (rozhraní `IPathController` později sjednoceno do `IRegulator` — viz záznam výše), build+237 testů zeleno; **ověření na HW čeká** (dynamika motorů, τ_look sweep na record/replay + selftestu, vyšší smyčka = plánovač trasy zatím neexistuje).

### 2026-08-01 — Dominantní zdroj GC pauz byla SERIALIZACE, ne kamerové buffery — ROZHODNUTO/OPRAVENO
Po nasazení kroku 4 (pooling kamerových bufferů) **200–455 ms záseky přetrvaly** (HW: `compute_ms` max 345 ms,
~11 % snímků >100 ms; `wait_ms` malý → pull OK). Root-cause: **`MessageWriter.Write` serializoval každou zprávu
přes novou `MemoryStream` + `ms.ToArray()`** — u `CameraFrame` (~1,8 MB nekomprimované) několik **LOH** alokací
na snímek (~90 MB/s na vlákně recorderu) → periodická blokující gen2 GC, která pauzovala i vlákno kamery
uprostřed `Process` (odtud špičky v `compute_ms`). **Pooling image bufferů (krok 4) to nemohl vyřešit** —
churn byl v serializaci, ne v grabu. **Oprava:** `MessageWriter` serializuje do **jedné znovupoužité
`MemoryStream`** a zapisuje přímo z `GetBuffer()` (0 alokací/zprávu, wire formát beze změny). Doplněno
poolování transientů `BuildGrid` (`acc`/`dev`/plane-fit `List`). **Poučení:** měř, kde je churn — dominantní
zdroj (40×) byl jinde, než plán předpokládal (`Src/ARBot.Common/Communication/MessageWriter.cs`,
`Src/ARBot.Common/Vision/CameraFrameProcessor.cs`). **Stav:** opraveno, build+testy zelené; **HW re-test čeká.**

### 2026-08-01 — BackProject (probability) je vstup pro řízení robota — ROZHODNUTO
Otevřená otázka „je RGB→probability (BackProject, ~25 ms/snímek) potřeba pro **řízení**, nebo **jen pro
vizualizaci**?" (viz [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md), „Rozhodnout před krokem
3/4") je rozhodnuta: **BackProject bude použit pro řízení robota.** Proto se `ImageProbability` počítá
**vždy** (když je RGB k dispozici) na vlákně kamery v `CameraFrameProcessor` — **nedělá se z něj volitelný/
on-demand výpočet** a neschovává se za flag. Důsledek pro krok 4: probability buffer je součást poolovaného
capture slotu (recykluje se jako RGB/Depth), takže „vždy počítat" nepřidává alokace v ustáleném stavu.
**Odkazy:** `Src/ARBot.Common/Vision/CameraFrameProcessor.cs` (ComputeProbability, reuse bufferu).

### 2026-08-01 — Synchronní vlákno-per-kamera pro vizuální cestu (proti GC pauzám z alokací) — HOTOVO (kroky 1–4)
Přepracovat **vizuální cestu** (kamera → vize) z dnešního async fan-outu (`SensorSource` → `RoleRouter`
→ `Stream` → N `MessageProcessor` stupňů) na **synchronní zpracování na vlákně kamery + pull**. Body:

1. **`CameraFrame` nese i odvozené** (probability, traversability grid). Grid jako **strukturovaná data**
   (`PolarCell[]` + `RadialEdge[]`), **NE `Image<PolarCell>`** (`IPixel` mismatch — buňka není pixel; a
   `RadialEdge[]` se do `Image` nevejde; reuse serializace/resize nic neušetří).
2. **`ICameraFrameProcessor`** — jedna sdílená platformně-nezávislá implementace (výpočet jede přes
   `NativeComputeUnit`), **per-kamera konfigurace** (projekce + Left/Right transform). `Process(CameraFrame)`
   se volá **synchronně v rámci kamery** a dopočte probability + grid. **Blokuje vlákno kamery** — to je
   žádoucí backpressure (kamera zpracuje, kolik stihne; ostatní snímky driver zahodí bez alokace).
3. **Kamery nejsou v pipeline přes `SensorSource`.** Běží vlastní vlákno (grab + `Process` → nejnovější
   frame v **poolovaných** bufferech). **`ControlLoop` je pulluje** (čte nejnovější grid pro řízení) a
   posílá frame na `Stream` pro záznam/UI. (Forward = jen neblokující `Post`; RT tik nezatěžovat víc.)
4. **Buffery kamery i kopie pro async odběratele (recorder, UI) jsou POOLOVANÉ s explicitním release** —
   recyklace, ne `new`. **Tvrdý požadavek** (jinak je refaktor zbytečný, viz níže).

**Proč:** Změřené GC pauzy **200–455 ms (~13 % snímků)** — periodické blokující gen2/LOH z per-snímek
alokací velkých `Image` (~1,8 MB/snímek × 30 fps ≈ 54 MB/s do LOH). Srovnání se starým **ARBot2**
(WPF/.NET 4.8) ukázalo, že tam to nevadilo **ne** frameworkem (`.NET 10` GC je lepší) ani recyklací
bufferů (starý app taky `new`oval per snímek), ale **architekturou**: pull + synchronní zpracování na
vlákně kamery + **jeden živý frame** + **málo vláken**. Nový async fan-out: 30 fps alokace, stejný frame
v mnoha (neomezených) frontách na mnoha vláknech → vysoký a dlouho žijící LOH churn + víc GC koordinace.

**Klíčový princip (jinak refaktor nemá smysl):** **GC tlak ≠ memcpy.** Zisk je v **recyklaci**, ne ve
vyhýbání se kopiím. Robot **vždy nahrává** (záznam je nutný pro zpětné prozkoumání) a odběratelé surového
framu jsou **dva** (recorder vždy, UI když otevřené) — takže „běžný stav bez kopie" ani „jeden vlastník"
neplatí. Řešení: **každý async odběratel má vlastní pool kopií** a po použití buffer **vrátí**; kamera
recykluje své buffery. Memcpy 1,5 MB ≈ 0,3 ms CPU a **nealokuje** (cíl je reused) → **~0 alokací/snímek**
v ustáleném stavu vs. dnešních ~54 MB/s. Kopie `new` každý snímek = jen posun alokace, bez zisku.
(Alternativa: refcountovaný sdílený pool bez memcpy; při málu odběratelích volíme per-konzument kopie —
jednodušší vlastnictví.)

**Důsledky / omezení:**
- Pod přetížením (recorder nestíhá disk) pool kopií vyschne → best-effort drop záznamu, nebo dočasný
  `new` (churn zpět). Ustálený stav 0.
- Mění model vizuální cesty z [record-replay.md](record-replay.md) (kroky 1–9). **Fúze** (reaktivní nad
  měřeními) a **řídicí smyčka** (periodická) zůstávají — pracují s malými zprávami.
- **`PolarTraversabilityGridMsg` zanikne** (grid je v `CameraFrame`); struktury (`PolarCell`, `RadialEdge`,
  klasifikace) i výpočet (`BuildGrid`, nativní transform, ekvivalenční test) **zůstávají**,
  `DepthTraversabilityProcessor` → `ICameraFrameProcessor`.
- `CameraFrame.ToData/FromData` + grid → **bump `FormatVersion`**.

**Sekvence (inkrementálně, ať se nerozbije naráz):**
1. `ICameraFrameProcessor` + grid v `CameraFrame`, voláno **synchronně v kameře**; zatím přes stávající Stream.
2. Konzumenti (robot-centric, overlay) na `CameraFrame.Grid`; `PolarTraversabilityGridMsg` pryč.
3. **Pull přes `ControlLoop`** + odpojit `SensorSource` pro kamery.
4. **Pooling** bufferů + per-konzument kopie s release (recorder, UI).

**Stav:** kroky 1–4 **naimplementovány** (build x64 i OrangePI + testy zelené). Kroky 1–2 ověřeny na HW
(1 kamera, `wait` avg 37→13 ms). Kroky 3–4 (pull přes `ControlLoop`, pooling + per-konzument kopie s release)
čekají na **HW ověření pod zátěží** (klíčová brána: `logs/traversability-timing-*.csv` — churn ~0, bez
periodických 200–455 ms špiček; integrita záznamu ve View bez tearingu). **Prováděcí plán (pro agenta):**
[plan-camera-vision-refactor.md](plan-camera-vision-refactor.md). **Odkazy:** [record-replay.md](record-replay.md),
`Src/ARBot.Common/Devices/{CameraFrame,CameraFramePool}.cs`, `Src/ARBot.Common/Runtime/{ControlLoop,ICameraPullSource}.cs`,
`Src/ARBot.Common/Vision/CameraFrameProcessor.cs`, `Src/ARBot/Robot/ARBotRuntime.cs` (HwCameraPullSource),
`Src/ARBot.Common/Communication/RecordingTarget.cs`, `Src/ARBot/ViewModels/ImageDocument.cs`,
analýza latence: `logs/traversability-timing.csv`, [devlog.md 2026-07-30](devlog.md).

### 2026-07-29 — Polární grid sjízdnosti z hloubkové kamery (robot-centrický, per-kamera)
Nový pipeline stupeň `DepthTraversabilityProcessor`: depth → point cloud → **polární grid** sjízdnosti
→ `PolarTraversabilityGridMsg`. Klíčová rozhodnutí návrhu:
- **Robot-centrický** (jen transformace kamery vůči tělu, ne světová póza) — detekce nezávisí na
  lokalizaci.
- **Per-kamera** grid s vlastním fitem roviny — redundance při výpadku kamery, mizí systematický
  z-offset mezi kamerami (různý pitch), v překryvu dva nezávislé hlasy pro kartézskou vrstvu.
- **Azimut = konstantní počet sloupců** (`ColumnsPerCell`, N=16 → 30 buněk), ne konstantní Δθ —
  celočíselné mapování obraz→buňka, dělitelnost šířky; reálné úhly z `Camera2DToCamera3D`.
- **Radiálně Δr = max(5 cm, pro cíl bodů)** — 5 cm blízko (návaznost na kartézský occupancy ~5 cm),
  roste s dálkou; **tvrdá podlaha 8 bodů → `Unknown`** (a `Unknown` ≠ `Free`, nezapisovat jako sjízdné).
- Buňka nese i **`Confidence`** (váha pro agregaci) a **`EdgeRange`** (sub-buňková náběžná hrana pro
  „vejde se robot" místo plného TSDF — 2D distance transform + přesná hrana).
- **Depth→cloud managed** (přes projekci), ne nativní `Segment2` (padá na x64) — plně testovatelné.
- **Proč tyto parametry:** hustota depth bodů na plochu klesá ~1/r² (konstantní úhlové vzorkování),
  polární grid s rostoucím Δr drží ~konstantní počet bodů/buňku; odvození řádek→vzdálenost viz
  [doc/traversability-grid.md](traversability-grid.md).
- **Zapojení do runtime:** v **Run** jako stupeň grafu (`ARBotRuntime.WireRun`), projekce líně z živé
  kamery + `Profile.Left/RightCameraTransform`. Ve **View** se grid **nepřepočítává, jen přehrává**
  zaznamenaný (rozhodnuto 2026-07-30) — přepočet ze záznamu odložen, protože živé intrinsics se
  nezaznamenávají (offline projekce by chtěla nominální intrinsics nebo rozšíření formátu `.rec`).
- **Vizualizace:** dokument je obecně **robot-centrický** (`RobotCentricDocument`/`RobotCentricControl`),
  grid sjízdnosti je první vrstva (časem RGB sjízdnost, okraje vozovky). Tvar robotu je ve sdílené
  `RobotGlyph` (parametr orientace + pozice) — použitelné i pro budoucí world view.
- **`RadialEdge { Range, Row }`:** radiální hrana nese metry **i řádek depth obrazu**, kde se láme →
  grid jde vykreslit přímo přes depth snímek (bez samostatného obrázku tříd, který by zbytečně nafukoval
  data). Overlay přes depth se tak počítá z `PolarTraversabilityGridMsg` (sloupce z `ColumnsPerCell`,
  řádky z `Row`).
- **Stav:** geometrie + klasifikace ověřeny syntetickým testem (kamera shora); prahy/šumový model
  se doladí na reálných datech.
- **Odkazy:** `Src/ARBot.Common/Vision/{DepthTraversabilityProcessor,PolarTraversabilityGridMsg,PolarGridConfig}.cs`,
  test `Src/ARBot.Common.Tests/Vision/DepthTraversabilityProcessorTest.cs`, registrace v `MessageCatalog.CommonDefaults`.

### 2026-07-25 — `Blob` → `ImageMsg`; obraz jako `Image`, bez `BlobType`/`Data`; komprese v serializaci
Původní `Blob` (BlobType + syrové `Data` + lazy JPEG) přejmenován na **`ImageMsg`** a přepracován:
nese přímo netypový **`Common.Image`** (pixel typ = identita, `PixelTypeName`), `Data` a `BlobType`
zrušeny. Serializaci obrazu řeší statické `ImageMsg.Write(bw, Image, Compression)` /
`ReadImage(bw)` (rekonstrukce přes `Image.Create` z uloženého názvu typu), komprese
`None/Deflate/Jpeg/Png` je per-zpráva ve vlastnosti `Comp`. Vizuální „druh" (`LayerKind`
Color/Probability/Depth) se v `MessageImageLayers` odvozuje z pixel typu (BGR32/RGB/BGR→Color,
Gray→Probability, Gray16→Depth) místo dřívějšího `BlobType`.
- **Proč:** čistší model (obraz je obraz, ne generický blob dat), self-popisný záznam a
  volitelná komprese na jednom místě; odstranění duplicitní identity (BlobType vs pixel typ).
- **Enablery:** netypový base `Common.Image` (z něj dědí `Image<T>`) + `Image.Create(name,w,h)`.
- **Rozsah:** aktivní cesta (`BackProjectProcessor`, `MessageImageLayers`, `ImageDocument`,
  katalog, recording limit `"ImageMsg"`, `ARBot.Record`) převedena; legacy `ToLogMessage`
  (LocalMap/GridNavigation…) převedeny na `Image<Gray>`; mrtvé/nekompilované ARBot2 soubory
  (Driver, MessageQueue komentář) ponechány. Testy převedeny, build 0 chyb, Common 200 / HAL 12.

### 2026-07-25 — Verzování zpráv: `Message.Verze` + větvení `FromData` podle uložené verze
Každá `Message` nese verzi formátu, ve kterém vznikla (`Message(name, verze)`). Rámec záznamu
verzi ukládá (`MessageWriter`: `MsgName:délka:Verze`) a `MessageReader` ji před `FromData` nastaví
na uloženou hodnotu. Pravidlo: `ToData` píše vždy aktuální layout; `FromData` větví podle
`this.Verze` a starší formát namigruje do aktuálního modelu; **při každé změně obsahu zprávy se
verzní konstanta zvedne** a přidá se čtecí větev pro předchozí verzi.
- **Vynuceno typem:** `SensorStateBase(int verze)` verzi **vyžaduje** (nemá bezparametrický ctor),
  takže každý senzorový stav musí předat svou konstantu (konvence `public const int FormatVersion`).
- **Proč:** dopředná kompatibilita — starý `.rec` musí jít přehrát i po změně zpráv.
- **Důsledek:** princip a I/O tok rozepsány v [record-replay.md → Verzování zpráv](record-replay.md).
  Dle tohoto principu je od 2026-07-25 hotová i serializace `CameraFrame` (`FormatVersion`,
  `FromData` větví podle `Verze`); surové framy se ale defaultně nezaznamenávají (limit 0, RGB je v
  záznamu jako JPEG `Blob`).

### 2026-07-25 — Run rozdělen na „Run without log" / „Run and log"; jméno záznamu `yyyyMMdd-HHmmss.rec`
Menu **Runtime** má dvě varianty spuštění: bez záznamu a se záznamem. „Run and log" pojmenuje
výstup automaticky `yyyyMMdd-HHmmss.rec` ve složce **`records/` v kořeni repa** (sidecar index
`.rec.idx` řeší runtime; složka se vytvoří). Kořen se hledá směrem nahoru přes marker `.git`
(`MainWindowViewModel.RepoRootOrBase`), fallback = `AppContext.BaseDirectory` (nasazení bez repa,
např. na Pi). `records/` je v `.gitignore` (velké binární logy se necommitují).
- **Proč:** dřívější „Run" volal `Start(Mode.Run)` bez cesty → runtime nenahrával. Uživatel chce
  vědomou volbu a bezklikové logování s časovým razítkem; záznamy mít na stabilním místě (ne pod
  `bin`, které se maže při Clean).
- **Důsledek:** `MainWindowViewModel.RunAndLog` + `RepoRootOrBase`, menu **Runtime → Run and log**;
  cesta se vypíše do Debug output. Přehrání přes **Runtime → View…**.

### 2026-07-25 — Paměť/poznatky výhradně v repu, žádná externí paměť
Poznatky, poznámky a rozhodnutí se ukládají jen do repa (`doc/*.md`, README, komentáře v kódu).
Externí „memory" úložiště harnessu (`~/.claude/…`) se **nepoužívá** — je mimo git a nejde sdílet
s týmem. Tento soubor vznikl jako „catch-all" na rozhodnutí, která nezapadají do konkrétního
doménového docu.
- **Proč:** potenciální spolupráce více lidí; CLAUDE.md se navíc čte na začátku každého sezení,
  takže repo je zároveň paměť napříč sezeními.
- **Důsledek:** CLAUDE.md = rozcestník „vždy v kontextu"; detaily v `doc/` (načítají se při práci
  v dané oblasti). Viz [CLAUDE.md](../CLAUDE.md).

### 2026-07-25 — Backpressure UI dokumentů: „latest-wins + Background flush" (povinný vzor)
Dokumenty přijímající data z `MeasurementArived` / `IMessageSink.Post` nesmí postovat na UI
vlákno každou zprávu — jen uloží nejnovější (starší zahodí) a koalescovaně naplánují jeden
`Flush` na `DispatcherPriority.Background`.
- **Proč:** producent (kamera ~30 Hz, IMU/motor ~100 Hz, backproject) přetékal dispatcher frontu
  → UI zamrzalo a zpracovávalo staré framy („stall → dávka stovek Hz → zpět"). `RelaySource`
  fan-out běží na vlákně producenta a nemá frontu, takže odběratel musí být neblokující.
- **Důsledek:** aplikováno v `CameraDocument`, `D435TestDocument`, `IMUDocument`, `GpsDocument`,
  `MotorControlDocument`, `ImageDocument` (dict pending per zdroj); `DebugOutputTool` obdobně.
  Vzor a šablona kódu: [Src/ARBot/Views/README.md](../Src/ARBot/Views/README.md).

### 2026-07-25 — DebugOutputTool: virtualizovaný list řádků místo jednoho `string`
Debug/Trace výstup drží `ObservableCollection<string>` zobrazenou virtualizovaným `ListBox`em
(dřív jeden velký `string` v `TextBox`).
- **Proč:** velký `TextBox` se při každé aktualizaci celý přeskládával (`BidiData` na UI vlákně)
  a s délkou logu ztrácel responzivitu.
- **Důsledek:** koalescované dávkové přidávání + ořez s hysterezí (`MaxLines`); render jen
  viditelných řádků. Soubor `Src/ARBot/ViewModels/DebugOutputTool.cs`.

### 2026-07-25 — Řídicí smyčka + UART odolné vůči nedostupným portům
Časovač `ControlLoop.Pump` má reentrancy guard (`Interlocked`), `Uart.ReOpen` je neblokující
(timestamp backoff místo `Thread.Sleep`), blokující čtení jde přerušit přes `IUart.CancelRead`
a `SensorBase.Process` má idle-backoff.
- **Proč:** při nedostupných COM portech blokoval `Drive()` ~3 s v `ReOpen` a `System.Threading.Timer`
  callbacky se překrývaly → exploze vláken (~180) a zamrznutí UI; blokující `Read` navíc věsel
  `SensorBase.Stop()` (`task.Wait()`).
- **Důsledek:** soubory `Uart.cs`, `UartSensorBase.cs`, `SensorBase.cs`, `ARBotRuntime.cs`,
  `SDC2160Ex.cs`/`SDC2160.cs`; test `ARBot.HAL.Tests/UartCancelReadTests.cs`. `Stop()` senzoru
  nejdřív nastaví `stopRequired`, pak `CancelRead()` (pořadí kvůli race).

### 2026-07-25 — Znovuotevírání dokovacích nástrojů přes sdílený `ReopenTool`
Nástroje (Sensors overview, Debug output) mají v `DockFactory` stabilní referenci a v menu
příkaz, který je znovuotevře přes společný `MainWindowViewModel.ReopenTool` (ošetřuje stavy
pinned/hidden/odpojený).
- **Proč:** `DebugOutputTool` se po zavření nedal znovu otevřít (nikde nedržená reference).
- **Důsledek:** `DockFactory.DebugOutput`, menu **Tools → Debug output**.

### 2026-07-25 — Nativní knihovna se staví CMakem a NENÍ v gitu
`NativeFuncs/bin/NativeLib.dll` (a `libNativeLib.so`) jsou build artefakty CMake, ne git.
Nesmí se mazat spolu s `bin`/`obj` — `ARBot.Common.csproj` je pro x64 kopíruje bez `Exists`
guardu, takže jinak build padá (`MSB3030`).
- **Proč:** zjištěno při čištění `bin/obj` (omylem smazána `NativeLib.dll`).
- **Důsledek:** postup rebuildu (vcvars + `cmake --preset windows-x64`) v
  [doc/build-and-platforms.md](build-and-platforms.md).

---

## Dříve učiněná rozhodnutí (kanonicky v doc/ nebo CLAUDE.md)

Rozhodnutí z dřívějška, jejichž odůvodnění je už rozepsané jinde — zde jen jako rozcestník
(přesná data viz git historie):

- **Build jen pod konkrétní platformou (x64 / OrangePI), ne AnyCPU** — kvůli nativním
  závislostem (Intel.RealSense). → [build-and-platforms.md](build-and-platforms.md), [CLAUDE.md](../CLAUDE.md)
- **Vlastní MSBuild platforma `OrangePI`** (ne `ARM64` = Windows-on-ARM, ne RID) a solution
  `.slnx` místo `.sln`. → [build-and-platforms.md](build-and-platforms.md)
- **Platformově dedikovaný HAL** (`HALWindows` 2.47 / `HALArmbian` 2.53, stejný namespace). →
  [architecture.md](architecture.md), [build-and-platforms.md](build-and-platforms.md)
- **Souřadnicové konvence:** world ENU + matematická orientace, body FLU. →
  [imu-and-frames.md](imu-and-frames.md)
- **EKF senzorická fúze** (přepis na generický `Ekf` → `EKFModel`, async replay). →
  [ekf-fusion.md](ekf-fusion.md)
- **Pipeline zpráv pro záznam/přehrávání** (`MessageSource`/`Target`, role, taps). →
  [record-replay.md](record-replay.md)
- **Při migracích nemazat starou/zakomentovanou implementaci, dokud ji nepotvrdí testy.** →
  [CLAUDE.md](../CLAUDE.md)
- **Jazyk: čeština** (komunikace, komentáře, dokumentace). → [CLAUDE.md](../CLAUDE.md)
