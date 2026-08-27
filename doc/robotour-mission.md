# Mise Robotour (`RobotourMission`) + čtení QR kódů

> **Stav (2026-08-26): fáze 2–5 hotové, spustitelné z aplikace, na HW neověřeno.** Čtení QR,
> `geo:` parser, stavový automat i **UI panel** — viz [Plán realizace](#plán-realizace-fáze).
> Zbývá **jen celé ověření na zařízení (fáze 7)** — fáze 6 (přežití restartu) byla **zrušena**
> (rozhodnutí autora 27. 8. 2026, viz [decisions.md](decisions.md)).
>
> **Jak to vyzkoušet v simulaci:** `mission=robotour` + panel *Tools → Mise Robotour* („Start mise")
> + panel *Tools → Virtuální senzory*, kde je **červené tlačítko nouzového zastavení** — bez něj se
> servisní okno projít nedalo, protože virtuální motory hlásily stop natvrdo jako `false`.
> V sekci „QR kód do virtuální kamery" jsou **tlačítka s hotovými kódy stanovišť** (nakládka /
> vykládka) a kód se staví na **1,0 m** — z 1,2 m se nepřečte (viz [virtual-hw.md](virtual-hw.md)).
>
> ✅ **Celý průchod misí autor v simulaci proklikal 27. 8. 2026** a funguje. Vyšly z toho tři
> opravy: kód se staví na **1,0 m** (z 1,2 m se nepřečte), stanoviště mají v panelu **tlačítka
> s hotovými kódy** a zkouška dosažitelnosti přestala zamítat cíle **za robotem** (viz
> [decisions.md](decisions.md)). Aplikace nemá testovací projekt, takže UI zůstává ověřené
> proklikáním a kompilací, ne testy.
>
> Kód: [`RobotourMission`](../Src/ARBot.Common/Missions/RobotourMission.cs),
> [`RobotourConfig`](../Src/ARBot.Common/Missions/RobotourConfig.cs),
> [`RobotourPhase`](../Src/ARBot.Common/Missions/RobotourPhase.cs),
> [`MissionState`](../Src/ARBot.Common/Missions/MissionState.cs),
> [`GeoUriTargetParser`](../Src/ARBot.Common/Missions/GeoUriTargetParser.cs),
> [`QrScanner`](../Src/ARBot.Common/Vision/Qr/QrScanner.cs),
> zprávy [`MissionMsg`](../Src/ARBot.Common/Logs/MissionMsg.cs) /
> [`QrCodeMsg`](../Src/ARBot.Common/Logs/QrCodeMsg.cs),
> testy [`RobotourMissionTests`](../Src/ARBot.Common.Tests/Missions/RobotourMissionTests.cs),
> [`MissionTargetParserTests`](../Src/ARBot.Common.Tests/Missions/MissionTargetParserTests.cs),
> [`QrScannerTests`](../Src/ARBot.Common.Tests/Vision/QrScannerTests.cs).
>
> **Jméno je `RobotourMission`** (rozhodnutí 25. 8. 2026) — sourozenec
> [`FreeRunMission`](mission-freerun.md), která se dělala **dřív**. Text níže místy ještě mluví
> o `MissionController`; je to totéž.
>
> **Společná abstrakce misí se nezavedla** — a po implementaci je vidět proč: společné mají jen to,
> že obě produkují cíl, ale na **jinou vrstvu** (FreeRun mrkev pro lokální plánovač, tato mise LLA
> pro globální navigaci). Vybírají se selektorem `mission=` (viz
> [mission-freerun.md](mission-freerun.md#která-mise-běží-mission)).
>
> ⚠️ **Tři odchylky od tohoto návrhu:**
> - **Potvrzení obsluhou zrušeno** (rozhodnutí autora 26. 8. 2026). Mise je **simulace autonomního
>   doručení**: robot musí úkol vykonat bez zásahu operátora a interagují s ním jen **odesílatel**
>   a **odběratel**, výhradně **QR kódem a stop tlačítkem**. Detail:
>   [Přijetí cíle](#přijetí-cíle-rozhoduje-jen-stroj).
> - **Dekodér je ZXing.Net, ne ZBar** — binding z ARBot2 nebyl k dispozici a ZXing nepotřebuje
>   nativní knihovnu. **Fáze 1 tím celá padá.** Důvody a co se tím platí:
>   [decisions.md](decisions.md), 26. 8. 2026.
> - **Metoda se jmenuje `StartMission()`, ne `Start()`** — zděděné `MessageTarget.Start()` spouští
>   *vlákno* stupně (dělá to runtime), takže by se „start mise" dal splést se „start vlákna".
>   Kolizi ohlásil kompilátor (CS0114) až po prvním buildu; totéž se stalo u vlastnosti `Stop`,
>   která je teď `CurrentStop`.

Nejvyšší vrstva řízení: **stavový automat soutěžní jízdy**. Zapamatuje si start/depo, přečte z **pravé
kamery** QR kód s místem nakládky, dojede tam a zastaví, proběhne nakládka a přečtení dalšího QR kódu
s místem vykládky, dojede tam, vyloží a **vrátí se do depa**.

Řídí [`GlobalNavigator`](global-navigation-runtime.md) zadáváním **LLA cílů** — sama nezná ani graf
cest, ani occupancy grid, ani regulátory. Cesta k cíli, objíždění a detekce záseku jsou vrstvy pod ní.

> Formát QR (`geo:`) je rozhodnutý podle předchozí generace robotu, která ho v soutěži
> používala; dekodér se změnil a **potvrzení obsluhou zaniklo** (viz hlavička a níže).

## Vrstvy

```
MissionController  ← tento dokument         stavový automat: depo → nakládka → vykládka → depo
      │  IGlobalGoalSink.SetGoal(LLA) / Cancel()        ▲ GlobalNavMsg (Arrived / NoRoute / Stuck)
      ▼                                                 │
GlobalNavigator    global-navigation-runtime.md         trasa po OSM síti, hlídání postupu
      ▼
LocalNavigator     occupancy-and-local-planning.md      occupancy grid + lokální plán
      ▼
ControlLoop        path-following.md                    regulace, motory
```

`QrScanner` je **samostatný** `MessageProcessor` vedle mise (viz [Čtení QR](#čtení-qr-kódů)); mise
o kamerách nic neví, jen odebírá `QrCodeMsg`.

## Stavový automat

Všechna tři zastavení (depo, nakládka, vykládka) mají **totožný průběh** — obsluha zmáčkne nouzové
zastavení, teprve pak se něco děje, a jízda pokračuje až po jeho uvolnění. Je to proto jeden
**opakovaně použitý podautomat „servisní okno"**:

```
        ┌──────────────── Aborted ◀── operátor / fatální chyba (z každého stavu)
        │
Idle ─▶ ArmingAtDepot ─▶ ⟦servisní okno @depo: čtení QR nakládky⟧ ─▶ DrivingToPickup
     ─▶ ⟦servisní okno @nakládka: nakládka + čtení QR vykládky⟧   ─▶ DrivingToDrop
     ─▶ ⟦servisní okno @vykládka: vykládka⟧                       ─▶ DrivingToDepot ─▶ Finished

⟦servisní okno, kde se čte kód⟧ = AwaitingEStop ─▶ Servicing ─▶ AwaitingEStopRelease
⟦servisní okno u vykládky⟧      = AwaitingEStop ─▶ AwaitingEStopRelease      (není co číst)
```

> **Jediné dva lidské vstupy jsou QR kód a stop tlačítko** (rozhodnutí autora 26. 8. 2026): stisk
> otevře okno, uvolnění je „hotovo". Žádné potvrzování v UI — viz
> [Přijetí cíle](#přijetí-cíle-rozhoduje-jen-stroj).

| stav | co se děje | přechod dál |
|---|---|---|
| `Idle` | čeká na „Start mise" z UI | operátor |
| `ArmingAtDepot` | **čeká na kvalitní fix, inicializuje jím fúzi a zapamatuje depo** (viz [níže](#armingatdepot-kvalitní-fix-a-inicializace-fúze)) | fix OK |
| `AwaitingEStop` | robot **stojí a je pod napětím**; čeká, až obsluha zmáčkne nouzové zastavení. Scanner **vypnutý** | `IsEmergencyStop == true` |
| `Servicing` | nouzové zastavení drží → člověk nakládá a ukazuje QR; zapnutý `QrScanner`. **Chodí se sem jen tam, kde se kód čte** — u vykládky se rovnou čeká na uvolnění | kód prošel strojovými kontrolami. Uvolnění stopu **bez kódu** → zpět na `AwaitingEStop` (další pokus) |
| `AwaitingEStopRelease` | cíl přijat (nebo u vykládky není co číst); čeká na **uvolnění** nouzového zastavení — to je signál „hotovo" | `IsEmergencyStop == false` |
| `DrivingToPickup` / `DrivingToDrop` / `DrivingToDepot` | `GlobalNavigator.SetGoal(cíl)`, hlídá `GlobalNavMsg` | `Arrived` |
| `Finished` | stojí, mise hotová, souhrn do logu | — |
| `Aborted` | okamžité zastavení (`Cancel()` + `Regulator = null`), důvod v `MissionMsg` | operátor |

**Nouzové zastavení za jízdy žádný stav nemá** — o zastavení se stará `ControlLoop` a po uvolnění se
jede dál k témuž cíli (viz [níže](#nouzové-zastavení-řeší-controlloop-ne-stavový-automat)).

**Žádný přechod není implicitní** — vždy z konkrétní podmínky, aby se ve záznamu dalo dohledat, proč
se mise posunula. Timeouty (`StateTimeouts`) mají **jen stavy bez člověka v cyklu**: jízda a `ArmingAtDepot`.
Stavy pod nouzovým zastavením timeout **nemají** — čeká se na obsluhu, jak dlouho je potřeba; jen se
měří a loguje uplynulý čas.

**`Arrived` chodí z globální vrstvy a stojí na póze z EKF** (vzdálenost od cíle ≤
`NavigatorOptions.ArrivalRadiusMeters`, default 3 m). Tolerance se nastavuje podle toho, že
**stanoviště je větší než chyba dojezdu** — ne aby byla co nejmenší. Žádné ruční tlačítko „jsem na
místě" tím není potřeba; kdyby se ukázalo, že stanoviště je menší než chyba EKF, řešením je vizuální
dojezd na QR kód, ne obsluha místo senzoru.

### Zastavení na stanovišti: dvě fáze

Na `Arrived` mise **nejdřív zruší cíl** (`GlobalNavigator.Cancel()` → `LocalNavigator.ClearGoal()`),
což robota **řízeně dobrzdí** cestou, která už dnes existuje: nižší smyčka dojede po poslední dráze a
watchdog `Profile.PathControlTimeOut` ji ukončí (a dráha se přitom pořád hlídá proti mapě, takže
kdyby na ní přece jen byla překážka, řízení se zahodí okamžitě). **Teprve když robot stojí**, nastaví
mise `Regulator = null`, aby se nemohlo nic rozjet.

Tvrdá varianta (`Regulator = null` hned) je vyhrazená pro `Aborted` — tam je zastavení důležitější než
plynulost.

### Nouzové zastavení řeší `ControlLoop`, ne stavový automat

`IMotorState.IsEmergencyStop` **už existuje** a teče ve zprávách `MotorStateBase`
(`MotorStateBase : SensorStateBase : IPrimaryMessage`, takže `RoleRouter` ji posílá do grafu zpracování
a **`ControlLoop.Consume` ji už dnes dostává** — jen ji zahazuje). `MotorControlDocument` stav
zobrazuje. V HAL není potřeba nic nového.

Reakce patří do **nejnižší smyčky**:

> **`ControlLoop` si drží poslední `IsEmergencyStop` (stejný `volatile` vzor jako dnešní `lastImu`) a
> dokud je `true`, posílá `Drive(0, aktuální rychlost == 0 ? 0 : požadovaná rotace)`.
> Všechny ostatní smyčky běží dál.**

Ten podmíněný člen je přesně to, co má být, a řeší obě situace jedním pravidlem:

- **robot se ještě dotáčí** (kola se točí): rotace zůstane, takže dobrzdění je pořád **řízené** — auto
  se při brzdění taky nepřestane ovládat;
- **robot už stojí**: rotace se vynuluje, takže se **netočí na místě** pod rukama obsluhy, a hlavně je
  poslední odeslaný příkaz `(0, 0)` — v okamžiku uvolnění stopu tedy není žádný transient. (Tohle byla
  ta ~100 ms otočka, na kterou jsem se ptal; tímhle zmizí a *navíc* se nepřijde o řízené brzdění.)

Zapojení je v [`ControlLoop`](../Src/ARBot.Common/Runtime/ControlLoop.cs) doslova pár řádků: `forvard`
a `rotationSpeed` už jsou spočítané z regulátoru, jen se před `dif = rotationSpeed * wheelBase`
upraví. Do `DriveCommandMsg` se přidá příznak, že zásah zkrátilo nouzové zastavení — ať je v záznamu
vidět **proč** byla nula.

„Aktuální rychlost" se bere **z motorů, ne z fúze** (bez latence filtru) a porovnává se **na přesnou
nulu**, bez epsilonu:

```csharp
bool stojí = m == null || (m.LeftWheelSpeed == 0 && m.RightWheelSpeed == 0);
```

Epsilon by tu byl zbytečný a jen by zaváděl další parametr k ladění. `MotorStateBase.LeftWheelSpeed`
je `LeftEncoder / FramePickupPeriod`, kde `LeftEncoder` je **přírůstek** enkodéru za rámec
([`SDC2160Ex`](../Src/ARBot.HAL/Devices/MotorDriver/SDC2160Ex.cs) posílá `leftEnc - lastLeftEnc`) — není
to filtrovaná ani derivovaná hodnota, která by kolem nuly šumět mohla. Když se nepohnul ani jeden tik,
je přírůstek **přesně** 0 a rychlost přesně 0,0. A protože jsou motory řízené **pozičně ve zpětné
vazbě**, „nepohnul se ani tik" opravdu znamená „stojí". Chybějící stav motorů se počítá jako stojící
(bezpečnější směr); mimochodem i chybová větev driveru vrací `MotorStateBase(true, 0, 0, …)`, tedy
konzistentně stop + nulové rychlosti.

#### Totéž pravidlo se doplnilo do řadiče (změna firmwaru)

Při dohledávání typu `ActualSpeed` vyšlo najevo, že **MicroBasic skript v SDC2160 nouzové zastavení
ošetřuje sám** — nulováním `reqSpeed` i `reqRotSpeed`, přičemž `curSpeed`/`curRotSpeed` k nule dojedou
přes svou `acceleration`, tedy **pozvolna** (varianta s okamžitým `curSpeed=0` je ve skriptu záměrně
zakomentovaná). Rotaci ale nuloval **hned**, takže dobrzdění bylo vždy „rovně" a hostitelské pravidlo
by se k motorům nedostalo.

Skript se proto **upravil na stejné pravidlo** (zdroj je komentář v hlavičce
[`SDC2160Ex.cs`](../Src/ARBot.HAL/Devices/MotorDriver/SDC2160Ex.cs); předchozí varianta je tam
zachovaná zakomentovaná, dokud se nová neověří na zařízení):

```basic
di3=GetValue(_DI, 3)
if di3=0 then
    reqSpeed=0
    if curSpeed=0 or acceleration<=0 then
        reqRotSpeed=0
    end if
end if
```

Tři věci, na kterých to stojí:

- **`curSpeed=0` je dosažitelné přesně.** Rampa je celočíselná a končí clampem `curSpeed=1000*reqSpeed`,
  což je při `reqSpeed=0` přesná nula — žádné dojíždění k nule „asymptoticky".
- **Pojistka `acceleration<=0`.** Kdyby dopředná rampa nemohla postupovat (`VAR 1` nenastavená nebo
  nulová), `curSpeed` by nuly **nikdy** nedosáhl a robot by se pod nouzovým zastavením otáčel na místě
  pořád. To je jediná cesta, jak z „drž rotaci, dokud jedeš" udělat nekonečnou otočku, takže se uzavírá
  přímo v podmínce.
- **Watchdog zůstal jiný záměrně.** Vyprší-li 500 ms bez zprávy od hosta, nulují se **obě** složky
  hned: u mrtvého hosta je poslední rotační příkaz **zastaralý** a slepé zatočení při dojezdu je horší
  než dojezd rovně. U nouzového zastavení host žije a jeho zatočení je aktuální — proto ta asymetrie.

**Nasazení a ověření.** Skript v repozitáři je *zdroj*, ne kompilovaný kód — do jednotky se nahrává
zvlášť (Roborun+ / MicroBasic upload), takže **dokud se nenahraje, chování robota se nezmění**. A protože
jde o cestu nouzového zastavení, ověřuje se **na zařízení**: (a) stop při jízdě rovně → zastaví rovně;
(b) stop v zatáčce → dotočí a zastaví, **a pak se netočí na místě**; (c) uvolnění → plynule pokračuje;
(d) zabitý host → obě složky na nulu.

Hostitelské pravidlo v `ControlLoop` tím **nezaniká** — obě vrstvy teď dělají totéž, každá ze svých dat
(host z přírůstků enkodérů, řadič ze své rampy). Řadič je ten, kdo skutečně brzdí; host drží
konzistenci softwaru s realitou (`DriveCommandMsg` nesmí tvrdit „jedu 0,8 m/s", když robot stojí; žádný
zastaralý příkaz; vyšší vrstvy musí vědět, že stání není zásek) a je to jediná varianta, která funguje
i pro `DummyMotors`, simulaci a případný jiný driver.

Po uvolnění začne regulátor generovat zásahy sám a jízda plynule pokračuje. Je to **výrazně lepší než
stav `Paused` v misi**, který jsem měl v předchozí verzi návrhu:

- **celý řetěz zůstává teplý** — `LocalNavigator` dál integruje snímky a plánuje, `ControlLoop` dál
  osvěžuje regulátor, takže watchdog `Profile.PathControlTimeOut` **nevyprší** a po uvolnění existuje
  aktuální, proti aktuální mapě ověřený plán. Robot nemusí nic znovu zadávat ani rozjíždět;
- **mapa se mezitím dál plní** — stání pod stopem je vlastně příležitost: kamery dosvítí okolí;
- **žádný stav v automatu, žádné znovuzadávání cíle** — mise o stopu za jízdy vůbec nemusí vědět.

Tři vlastnosti, které z toho návrh dělá:

1. **Robot nikdy neskenuje, když může jet.** Scanner je zapnutý **výhradně** ve stavu `Servicing`,
   tedy jen pod drženým nouzovým zastavením. Obsluha, která stojí u robotu s krabicí v ruce, tak má
   fyzickou garanci, ne jen softwarovou.
2. **Nouzové zastavení je signál mise jen ve stavech, které na něj čekají** (`AwaitingEStop`,
   `AwaitingEStopRelease`). Zmáčknutí stopu za jízdy tedy automat neposune — o zastavení se stará
   `ControlLoop`. Proto musí UI **jasně ukazovat, na co mise čeká**, aby obsluha nemačkala stop v
   domnění, že tím něco odemkne.
**Dopad na globální vrstvu:** detektor záseku „nehýbu se" musí být pod nouzovým zastavením **vypnutý** —
jinak by po 10 s stání u nakládky vyhlásil zásek a začal zavírat hrany v mapě. Viz
[global-navigation-runtime.md](global-navigation-runtime.md#tři-detektory-tři-různé-významy).

*Až se to implementuje, patří tahle vlastnost `ControlLoop` také do [path-following.md](path-following.md).*

### `ArmingAtDepot`: kvalitní fix a inicializace fúze

Start mise je jediné místo, kde robot **stojí, má čas a nikam nespěchá** — proto se tady dělá to
nejdůležitější měření celé jízdy:

1. **Čeká se na kvalitní fix.** Kritéria z [`GPSState`](../Src/ARBot.Common/Devices/GPSState.cs)
   (`IsFixed` / `Quality`, `NumberOfSatellites`, `Hdop`), držená **nepřerušeně** po `DepotFixSec` (5 s).
2. **Fixy z toho okna se průměrují.** Robot stojí, takže průměr je poctivější než jediný vzorek — a
   **rozptyl** z okna dá zdarma jak kontrolu kvality (velký rozptyl = čekej dál), tak realistickou
   `std` pro filtr.
3. **`AsyncFusionEngine.InitializePosition(x, y, std, t)`** — teprve tímhle se filtr dozví, kde je.
   Do té doby se nikam nejede (globální vrstva stejně nemá LLA).
4. **Depo se zapamatuje** (LLA + kurz) do `MissionMsg` — tedy do záznamu, ne do stavového souboru:
   mise **nepřežívá restart** (viz [Přežití restartu](#přežití-restartu-zrušeno)).

Že to dělá mise a ne filtr, je záměr: „tomuhle fixu už věřím tak, že podle něj postavím počátek" je
**rozhodnutí té vrstvy, která ví, že robot stojí v depu** — ne vlastnost měřicí cesty. Filtr k tomu
jen poskytuje funkci (viz
[global-navigation-runtime.md](global-navigation-runtime.md#důsledek-který-se-musí-ošetřit-první-fix-by-dnešní-filtr-zahodil)).

**Depo je zároveň nejpřesněji zaměřený bod celé mise** — a je to dobře, protože je to jediný cíl, který
robot nedostane z QR kódu a ke kterému se musí vrátit.

### Zastavení na místě je součást stavu, ne vedlejší efekt

Při vstupu do servisního okna se **aktivně** volá `GlobalNavigator.Cancel()` a nastaví
`ControlLoop.Regulator = null` (u `ControlLoop` je `null` = stát, bezpečný stav). Nespoléhá se na to,
že „robot dojel a tím zastavil": watchdog `Profile.PathControlTimeOut` by dobrzďoval 500 ms a mise by
mezitím považovala robota za stojícího.

Zároveň tím **vypadnou detektory záseku** globální vrstvy (nemají aktivní cíl), takže stání při
nakládce nikdo nevyhodnotí jako zásek. To je návrhový důvod, proč je detektor A v
[global-navigation-runtime.md](global-navigation-runtime.md) vázaný na aktivní cíl.

**Návrat do depa je normální cíl** (`SetGoal(depotLLA)`), ne zrušení cíle — jede se k němu po síti a
uzavřené hrany zůstávají v platnosti. `Cancel()` znamená vždy jen „přestaň jezdit".

## Cíl z QR kódu — formát `geo:`

**Kde se který kód čte** (dvě čtení celkem):

| servisní okno | čte se kód? | co obsahuje |
|---|---|---|
| **depo** (start mise) | **ano** | místo **nakládky** |
| **místo nakládky** | **ano** | místo **vykládky** |
| **místo vykládky** | ne | nic — dál se jede na zapamatované depo |


Formát je daný tím, co používala **předchozí generace robotu** (ARBot2, `ReadQRLLA`) a co se v soutěži
osvědčilo: text kódu začíná `geo:`, pak zeměpisná šířka a délka **ve stupních** oddělené čárkou, s
volitelným písmenem světové strany:

```
geo:49.2103,16.5991           →  N 49,2103°  E 16,5991°
geo: 49.2103 N, 16.5991 E     →  totéž (mezery i sufixy jsou přípustné)
geo:12.34S,45.67W             →  záporná šířka i délka
```

Pravidla parsování (převzatá 1:1, včetně důvodů):

- porovnání **case-insensitive** (`ToLower`), protože kód může být vysázený jakkoli;
- sufix `n`/`s` u šířky a `e`/`w` u délky určuje **znaménko**; bez sufixu se bere hodnota, jak je
  (tedy i s minusem);
- čísla **vždy `CultureInfo.InvariantCulture`** — desetinná tečka bez ohledu na locale stroje.
  *(Toto je jediná chyba, kterou lze v tomto řetězu udělat tiše a fatálně: pod českým locale by
  `double.Parse("49.2103")` dalo 492103 a robot by odjel do vesmíru.)*
- výsledek je `LLA` v **radiánech** (`Conversions.Deg2Rad`), jak systém vyžaduje;
- **cokoli jiného než `geo:` se zamítne** — nedekódovaný ani nesrozumitelný kód nikdy neposune misi.

Parser zůstane za rozhraním `IMissionTargetParser` (jedna metoda, `string → LLA?`), aby šel rozšířit,
kdyby pravidla formát změnila — ale výchozí a jediná implementace je tahle.

### Přijetí cíle: rozhoduje jen stroj

> **Změna 26. 8. 2026 (rozhodnutí autora): potvrzení obsluhou zrušeno.** Mise je **simulace
> autonomního doručení** — robot musí úkol vykonat **bez zásahu operátora** a jediní, kdo s ním
> interagují, jsou **odesílatel** v místě nakládky a **odběratel** v místě vykládky, a to výhradně
> **QR kódem a stop tlačítkem na robotu**. Tlačítko „Potvrdit" v panelu i metoda `Confirm()` jsou
> pryč; kód, který projde strojovými kontrolami, se přijme **sám** a mise se posune.
>
> Co to znamená pro pojistky: **zbývá jen ta strojová**, takže její tři kontroly (formát, vzdálenost,
> dosažitelnost) nesou celou váhu. Proto taky musí být **vidět, když zamítnou** — jinak zamítnutý
> kód vypadá jako nepřečtený (viz `RejectReason` v `MissionMsg`).
>
> **Uvolnění stopu je nově plnohodnotný signál:** u vykládky znamená „je vyloženo" (nic se nečte),
> a v okně, kde se kód čeká, znamená uvolnění **bez** přečteného kódu „člověk odešel" → mise se vrátí
> na `AwaitingEStop` a čeká na další pokus. Nikdy neodjede bez cíle.

Jedno chybné dekódování může poslat robota o stovky metrů jinam, proto se cíl pouští dál až po
kontrolách:

**Stroj** (automaticky, jediná pojistka):
- **sanity check vzdálenosti** — cíl musí být blíž než `MaxTargetDistanceM` (default 2000 m) od depa;
- **odstup od cesty** — cíl musí ležet blíž než `MaxTargetOffRoadM` (default **15 m**) od nejbližší
  hrany sítě; co je dál, je **nedosažitelné**. Viz „Přichycení cíle na cestu" níže.
- **dosažitelnost v grafu** — `GoalField` po `InsertGoal` musí dát konečnou cost-to-goal; jinak by se
  `NoRoute` zjistilo až za jízdy. Počítá to
  [`GlobalNavigator.Probe`](../Src/ARBot.Common/Maps/OsmNav/Navigation/GlobalNavigator.cs) nad
  **vlastním, zahoditelným** `GoalField`, takže zkouška nesahá na aktivní cíl.
  > ⚠️ **Zkouška musí zkoušet obě orientace mapmatchnuté hrany** — jinak zamítá cíle **za robotem**.
  > Na obousměrné cestě jsou oba směry stejně daleko, takže `NearestNode` vybírá podle pořadí hran,
  > ne podle kurzu; když padne na směr od cíle, je cena nekonečná (otočka na téže cestě není v grafu
  > přechod, `GraphBuilder` U-turn vynechává). `Navigator.Update` i `Router` přitom obě orientace
  > zkoušejí a berou levnější, takže **jet se tam dá**. Do 27. 8. 2026 to zkouška nedělala a
  > zamítala dobré cíle hláškou „nevede trasa (je mimo mapu?)" — našlo se to na cíli 50 m **za**
  > robotem na rovné cestě.
- `QrConfirmations` (default **1**, jako v původním kódu) shodných dekódování — víc než jedno je
  levné a od zrušení potvrzování je to **jediná** pojistka nad rámec kontrol výše.

Panel to všechno ukazuje (**přečtený text, souřadnice ve stupních, vzdálenost od depa, odstup od cesty
a délku nalezené trasy**), ale už jen jako **informaci a záznam** — ne jako podklad k rozhodnutí.
Člověk do přijetí cíle nevstupuje.

Přečtený text jde **doslova** do `QrCodeMsg` i `MissionMsg` → v záznamu je vidět, co robot přečetl,
i když to zamítl.

### Přichycení cíle na cestu

Souřadnice v QR kódu je místo, kde **stojí člověk s krabicí** — u zdi, na chodníku, kdekoliv. Robot
jezdí po síti, takže se cíl **přichytí** (kolmý průmět na nejbližší hranu) a jede se na ten průmět.
Dělá to `GlobalNavigator.Probe`, který vrací `SnappedTarget` a `OffRoadM`; mise pak jezdí a měří
dojezd proti **přichycenému** cíli.

**Bez přichycení to není kosmetika, ale zásek:** `Navigator` porovnává polohu s `GoalField.GoalPoint`,
a to je **surový** cíl. Odsazení větší než `ArrivalRadiusMeters` (3 m) tedy znamená, že `Arrived`
nenastane **nikdy** — robot dojede na cestu, zastaví se u průmětu a čeká; a protože jízda k cíli
nemá timeout (`DrivingTimeoutSec = 0`), čeká napořád.

**Přichytit ale jde cokoliv** — `RoadNetwork.NearestEdge` žádný limit vzdálenosti nemá, takže cíl
uprostřed pole 300 m od silnice se k té silnici přichytí a vyšel by jako dosažitelný; robot by odjel
úplně jinam, než kde člověk stojí, a ohlásil by dojezd. Limit `MaxTargetOffRoadM` je to, co z
přichycení dělá **kontrolu**: co je dál, je nedosažitelné a kód se zamítne s vlastním důvodem.

> **15 m je volené úsudkem, ne z dat** — druhá taková hodnota vedle `MaxSpreadM`. Úvaha: hrana v OSM
> je *osa* cesty, takže člověk na kraji dvoumetrové pěšiny je ~1 m od osy, u vchodu do budovy vedle
> cesty klidně 5–10 m, k tomu chyba souřadnice v kódu. Skutečný odstup **jde do záznamu**
> (`MissionMsg.AcceptedOffRoadM`, verze 6) a vypisuje ho panel, takže se po prvních bězích dá
> nastavit z čísel.

⚠️ **Pozor na dvojí význam souřadnic v `MissionMsg`:** od verze 6 jsou `AcceptedLatDeg/LonDeg`
**přichycené**, ve verzích 2–5 jsou to tytéž bajty, ale surový cíl. Surová souřadnice zůstává
čitelná v `AcceptedCodeText`.

**Přichycení se týká jen cílů z QR kódu.** Depo je zapamatovaná **vlastní** póza robota (dojel tam po
cestě), takže se nepřichycuje. Cíl z příkazové řádky (`goal=lat,lon`) taky ne — `GoalField.GoalPoint`
tam zůstává surový, tedy `goal=` mimo cestu má pořád starý problém s dojezdem.

### Když kód není ve výhledu: řeší to obsluha, ne robot

QR se čte z **pravé** kamery (`ARBotHW.RightCamera`, `Name == "Right"`; předchozí generace používala
levou — je to konfigurace, `QrCameraName`). Kód tedy musí být **napravo** od robota.

**Robot se za kódem nesmí rozhlížet otočkou** — v okamžiku čtení drží obsluha nouzové zastavení, takže
motory jsou mrtvé, a to je celý smysl toho handshake. Jakékoli „zametání otočkou" by znamenalo rozjezd
robota ve chvíli, kdy u něj někdo stojí s nákladem v ruce. Proto: když se do `QrSearchSec` (10 s) nic
nedekóduje, mise to **hlásí v UI** („kód nevidím") a řešení je na obsluze — posunout kód, přisunout
robota, nebo nouzové zastavení uvolnit, robota přesměrovat a stop znovu zmáčknout. Automat v `Servicing`
prostě dál skenuje, dokud nemá co potvrdit.

Levné zmírnění: `QrCameraName` smí být **prázdné = skenovat všechny kamery**. Pod nouzovým zastavením
je výpočetní čas zdarma a odpadá tím celá otázka, na kterou stranu robot dojel.

## Čtení QR kódů

### Kde a kdy

- **Vlastní `MessageProcessor` `QrScanner`** (fronta `DropOldest`, kapacita 1) odebírající `CameraFrame`
  — jen ty s `Name == QrCameraName`. Vlastní vlákno: dekódování nesmí zdržet ani vlákno kamery, ani
  misi, ani řídicí tik.
- **Vypnutý, dokud ho mise nezapne** (`Enabled`) — a mise ho zapíná **jen pod drženým nouzovým
  zastavením** (stav `Servicing`). Za jízdy je to čistá režie a nikoho nezajímá. **Tím je výkonová
  otázka z velké části mimo hru.**
- Čte se z `CameraFrame.ImageRGB` (`Image<BGR32>` → `Image<Gray>` přes existující
  `Image<T>.ConvertTo`), volitelně z ROI a s podvzorkováním (`QrDownscale`), protože kód velikosti
  A5 z 2 m má v 640×480 dost pixelů i po zmenšení na polovinu.
- Výstup: **`QrCodeMsg`** (nová zpráva: název kamery, text, 4 rohy v obraze, čas) na `Stream` →
  **do záznamu**, takže po soutěži je dohledatelné, co se kdy přečetlo.

### Čte se jen ve stoje — záměrně

Při 0,8 m/s a expozici pár ms je rolling-shutter rozmazání takové, že se dekódování stane loterií.
Všechna čtení v automatu proto probíhají **ve stoje** — a shodou okolností přesně tam, kde je robot
navíc pod nouzovým zastavením. Původní implementace to řešila stejně, jen hrubě: blokující smyčka
s `ReqSpeed = 0; ReqRotationSpeed = 0` a `state.Read(Profile.Ts)`. Tady je to místo toho stav automatu
nad frontou zpráv, takže to nezablokuje vlákno ani řídicí tik.

### Dekodér — ZXing.Net (návrh počítal se ZBarem)

Původní volba byla **ZBar**, protože v předchozí generaci robotu (ARBot2, binding `zbar-sharp`)
**velmi dobře fungoval v soutěži**. Ten důvod nezmizel, zmizel předpoklad: **ARBot2 na stroji není**,
takže binding nebyl odkud vzít. Skutečná implementace je proto
[`ZXingQrDecoder`](../Src/ARBot.Common/Vision/Qr/ZXingQrDecoder.cs) nad NuGetem `ZXing.Net`.
Odůvodnění a co se tím platí: [decisions.md](decisions.md), 26. 8. 2026.

Hlavní důsledek: **ZXing je čistě managed, takže celá starost o nativní knihovnu zmizela** —
žádná `libzbar.dll`, žádný `DllImportResolver` pro `libzbar.so.0` na Armbianu. Build pro
`-p:Platform=OrangePI` prochází bez čehokoli navíc.

Co z návrhu **zůstalo v platnosti**:

- **Žádné `System.Drawing`.** Původní kód volal `i.ToBitmap()`; `System.Drawing.Common` je na .NET
  jen na Windows, takže na Armbianu by to spadlo. Cesta je `Image<BGR32>` → `Image<Gray>` (Y800,
  1 bajt na pixel) → dekodér, bez bitmapového mezikroku. ZXing dostane ta data přes
  `RGBLuminanceSource` s `BitmapFormat.Gray8`.

  > Převod **není součástí čtení QR** — je to obecná operace
  > [`Image<T>.ToGray(downscale)`](../Src/ARBot.Common/Common/Image.cs). Vznikla tady jako
  > `QrImage.ToGray`, ale nic na ní není QR-specifické, takže se přesunula na `Image` (podnět
  > autora, 26. 8. 2026) a testuje ji
  > [`ImageToGrayTests`](../Src/ARBot.Common.Tests/Common/ImageToGrayTests.cs) — včetně pixel typů,
  > které scanner nikdy nevidí. Barvu bere z **`IPixel.R/G/B`** — kanály, které kvůli tomu na
  > [`IPixel`](../Src/ARBot.Common/Common/IPixel.cs) vznikly; proč ne z `Values` ani `Color` je
  > v [decisions.md](decisions.md), 26. 8. 2026.
- **Povolený je jen QR** (`PossibleFormats = { QR_CODE }`) — ostatní symbologie jen zdržují a mohou
  plodit falešné nálezy. Test to hlídá čárovým kódem, který se nesmí ohlásit.
- **Podvzorkování vybírá pixely, neprůměruje.** QR je binární vzor s ostrými hranami, takže
  průměrování by rozmazalo právě to, co dekodér potřebuje.
- **Dekodér je za rozhraním `IQrDecoder { QrResult[] Decode(Image<Gray> img); }`**, takže výměna
  (i zpět za ZBar) je lokální změna a testy si dodají vlastní implementaci.

`TryHarder` je zapnuté: skenuje se **výhradně** pod drženým nouzovým zastavením, kdy robot stojí a
výpočetní čas je zdarma — tam je správné zaplatit za úspěšnost čtení.

> ⚠️ **Úspěšnost čtení není naměřená.** Testovací obraz se kóduje **týmž** ZXingem, takže testy
> dokazují *cestu* (BGR32 → Y800 → dekodér, včetně podvzorkování na polovinu), ne to, jak se kód
> čte z reálné kamery na stanovišti. To je vedený krok „ověření na HW".

## Zprávy a záznam

- **`QrCodeMsg`** (nová) — kamera, text, rohy, čas.
- **`MissionMsg`** (nová, **verze 2**) — fáze, čas vstupu do fáze, uplynulý čas mise, depo (LLA),
  pickup/drop (LLA + zdrojový text kódu), důvod přerušení, čítače (kolik čtení, kolik timeoutů).
  Emituje se **při každé změně fáze** a periodicky (`MissionMessagePeriod`, 1 s) → ve View se dá
  přehrát celá mise.
  **Verze 4 přidala důvod zamítnutí kódu** (a text, který se zamítl). Tři důvody — nesrozumitelný,
  příliš daleko od depa, bez trasy v grafu — se z pohledu obsluhy chovají stejně („nic se nestalo"),
  ale znamenají úplně jiné řešení. Bez nich zamítnutí vypadá jako **nepřečtený kód**; přesně to se
  26. 8. 2026 stalo autorovi u cíle 71 km daleko.
  **Verze 3 přidala kvalitu fixu v depu** (družice, HDOP, rozptyl okna, jeho limit, počet vzorků).
  Bez ní je „čeká se na kvalitní fix" **nediagnostikovatelné**: mise stojí, panel neumí říct proč a
  jediný způsob, jak to zjistit, je přečíst si kód — přesně to se 26. 8. 2026 stalo. Rozptyl se
  počítá **průběžně**, ne teprve u plného okna, aby obsluha nehádala 5 s.
  **Verze 2 přidala cíl z QR kódu** (`HasAcceptedCode`, souřadnice, text kódu, vzdálenost od depa,
  **délka trasy**). Nese hlavně tu délku trasy: počítá ji zkouška dosažitelnosti a nikde jinde
  v záznamu není. **Verze 5 změnila význam téhož kola** — dřív to byl cíl *nabídnutý k potvrzení*,
  dnes **přijatý** (potvrzování zaniklo). Bajty jsou tytéž, takže se stará verze pozná **jen podle
  čísla**. Starší záznamy ji nemají
  (`HasPending` zůstane `false`).
  **Verze 6 (27. 8. 2026) přidala odstup cíle od sítě** (`AcceptedOffRoadM`) a **znovu změnila význam
  týchž souřadnic**: od ní jsou `AcceptedLatDeg/LonDeg` cíl **přichycený na cestu**, ve verzích 2–5
  surový z kódu. Surový zůstává čitelný v `AcceptedCodeText`, takže se z dvojice dá odstup ověřit.
  Odstup nikde jinde v záznamu není — přichycení ho spočítá a zahodilo by ho — a je to jediná cesta,
  jak `MaxTargetOffRoadM` nastavit z dat místo z úsudku.
- Konverzi vlastní doména: `MissionState.ToLogMessage()` (viz [CLAUDE.md](../CLAUDE.md)).

## Přežití restartu (ZRUŠENO)

❌ **Mise restart přežít nemusí** — rozhodnutí autora 27. 8. 2026. Fáze 6 se nebude dělat, stavový
soubor `logs/mission-state.json` **nevznikne** a v kódu po tom nezůstala žádná stopa (nic z toho
nebylo napsané). Zdůvodnění: [decisions.md](decisions.md).

**Co to znamená v provozu:** po pádu nebo restartu aplikace se mise spouští **od začátku** — tedy
tlačítkem *Start mise* na místě, kde robot stojí. Protože `ArmingAtDepot` postaví depo z aktuálního
fixu, **depo se přepíše na to, kde robot právě je**; když spadl uprostřed trasy, není to původní
depo a robot se „vrátí" jinam. Kdo restartuje uprostřed jízdy, musí s robotem nejdřív zpátky do depa.

<details>
<summary>Původní návrh (pro případ, že by se to někdy vracelo)</summary>

Pád aplikace uprostřed soutěžní jízdy nesmí znamenat ztrátu **depa** (do kterého se má robot vrátit) —
je to jediná informace, kterou nelze získat znovu. `MissionController` proto po `ArmingAtDepot` (a při
každé změně fáze) zapíše malý stavový soubor (`logs/mission-state.json`: depo, fáze, cíle, časy) a při
startu nabídne **obnovení mise** místo nové. Obnovení je **explicitní volba operátora**, nikdy
automatická — robot, který po restartu sám vyrazí, je nebezpečný.

</details>

## Parametry

Skutečné názvy po implementaci — [`QrScannerConfig`](../Src/ARBot.Common/Vision/Qr/QrScannerConfig.cs)
a [`RobotourConfig`](../Src/ARBot.Common/Missions/RobotourConfig.cs). Obě mají `Validate()`, takže
nesmyslná hodnota skončí výjimkou při startu, ne divným chováním za jízdy.

| parametr | kde | default | pozn. |
|---|---|---|---|
| `CameraName` | scanner | `"Right"` | prázdné = skenovat všechny kamery; z příkazové řádky `qrcamera=` |
| `Confirmations` | scanner | 1 | shodná dekódování **po sobě**; od zrušení potvrzování je to jediná pojistka nad rámec strojových kontrol |
| `Downscale` | scanner | 2 | podvzorkování před dekódováním |
| `DepotFixSec` | mise | 5 s | jak dlouho musí fix nepřerušeně vyhovovat; `depotfix=` |
| `MinSatellites` / `MaxHdop` / `MaxSpreadM` | mise | 6 / 2,0 / **2,5 m** | kvalita fixu v `ArmingAtDepot`; `MaxSpreadM` je **RMS** odchylka, viz níže |
| `MinInitStdM` | mise | 0,3 m | **podlaha** nejistoty pro `InitializePosition` (viz níže) |
| `QrSearchSec` | mise | 10 s | po této době se hlásí „kód nevidím" (skenuje se **dál**) |
| `MaxTargetDistanceM` | mise | 2000 m | sanity check cíle z QR |
| `MaxTargetOffRoadM` | mise | **15 m** | největší přípustný odstup cíle od sítě cest; dál = **nedosažitelné** (hodnota z úsudku, viz „Přichycení cíle na cestu") |
| `ArmingTimeoutSec` / `DrivingTimeoutSec` | mise | **0 / 0 = neomezovat** | **jen stavy bez člověka v cyklu** (jízda, `ArmingAtDepot`) |
| `MissionMessagePeriodSec` | mise | 1 s | perioda `MissionMsg` |

> ⚠️ **Past, na kterou mise narazila naostro (26. 8. 2026):** `GPSState.Latitude/Longitude` byly
> tehdy ve **stupních**, ale mise z nich stavěla `new LLA(...)` (radiány), takže rozptyl okna vyšel
> astronomický a okno se **vždy zamítlo** — mise uvízla v `ArmingAtDepot` a vypadalo to jako „nedočká
> se fixu". Léčba byla systémová: **`GPSState` je teď v radiánech** jako všechno ostatní, takže ten
> zápis je správný. Viz [decisions.md](decisions.md).

**`MaxSpreadM` proti návrhu vzrostl z 1,0 na 2,5 m a měří se jinak** (rozhodnutí 26. 8. 2026, měřeno
testem). Dvě samostatné vady: (a) návrh mluvil o „rozptylu", ale první implementace brala
**největší** odchylku — a ta s rostoucím `n` **roste** i u dokonale gaussovského šumu, takže by delší
okno kritérium *přitvrzovalo*, přesně naopak, než má; (b) prah 1,0 m byl **pod nominálním šumem GPS**
(σ = 1,5 m v simulaci i u spotřebního přijímače ve stoje), takže by se mise **nezarmovala nikdy**.
Teď je to **RMS** odchylka a prah je nad normálním šumem — zamítat se mají jen abnormální fixy
(multipath skáče o desítky metrů). Podrobně: [decisions.md](decisions.md).

**`MinInitStdM` v návrhu nebyl a musel vzniknout** ze dvou důvodů: `InitializePosition` vyhodí
výjimku na `std <= 0` (a v simulaci může být rozptyl okna přesně nulový), a hlavně — samotný rozptyl
okna **nezahrnuje systematickou chybu GPS**, takže by filtru tvrdil víc jistoty, než je pravda. Je to
tedy tentýž druh nepoctivosti σ, jaký se řešil u korelace s mapou.

**Timeouty jsou ve výchozím stavu vypnuté** (0 = neomezovat, rozhodnutí autora 26. 8. 2026). Mechanismus
existuje a je otestovaný, ale správné hodnoty nejsou známé — a timeout, který vyprší dřív, než měl,
misi **přeruší** (zotavovací manévr neexistuje), což je na soutěži horší než čekání. Zapnout se dá
kdykoli nastavením obou hodnot.

**Časový a rychlostní limit soutěže v konfiguraci nejsou** — hodnoty z pravidel Robotour nejsou
známé a mrtvý přepínač, který nikdo nevyhodnocuje, je horší než jeho absence. Až budou známé, patří
sem (včetně toho, *co* se má při jejich překročení stát — to je rozhodnutí o strategii soutěže).

## Testy

**Hotovo: 54 testů** (23 automat, 17 parser, 10 scanner, 4 dekodér), celá sada `ARBot.Common.Tests`
prochází (877 testů). Automat je čistá logika → testovatelný celý, bez HW, bez kamer a bez fúze.

**Dvě skutečné vady, které testy a kompilátor našly** (obojí by se za jízdy hledalo mnohem hůř):

- **Mísení hodin.** `Start()` bral čas ze `DateTime.UtcNow`, ale automat měří v časech **zpráv**.
  Při přehrávání záznamu (a v testech) se ty dvě hodiny rozcházejí, takže `ArmingAtDepot` vypršel
  *okamžitě* — mise se ukončila dřív, než přišel první fix. Léčba: čas se **ukotví až prvním
  údajem, který přijde**, a dokud není ukotvený, žádný timeout neběží („nemám podle čeho měřit"
  nesmí znamenat „vypršelo"). `StartMission(DateTime)` umí čas zadat explicitně.
- **Kolize `Start()` / `Stop`** se zděděným `MessageTarget.Start()` / `Stop()`, které spouští a
  zastavuje **vlákno stupně**. Nahlásil to až kompilátor (CS0114 / CS0108) po prvním buildu
  aplikace. Splést tyhle dvě věci by dalo buď misi, která se sama rozjede, nebo stupeň, který nikdy
  nezačne odebírat zprávy. Odtud jména `StartMission()` a `CurrentStop`.

Pokryté případy:

- **průchod celou misí** s falešným `IGlobalGoalSink` a falešnými `QrCodeMsg` + `MotorStateBase` →
  očekávaná posloupnost fází a zadaných cílů (poslední = depo, **shodné se zapamatovaným**);
- **servisní okno:** bez zmáčknutého nouzového zastavení se **nezapne scanner** ani se nepokročí;
  přečtený kód se přijme **sám, bez potvrzování**; bez uvolnění stopu se **nerozjede**;
- **nouzové zastavení za jízdy automat neposune** (zůstane v `Driving*`, cíl se nezruší);
- **`ArmingAtDepot`:** nekvalitní fix (málo satelitů / vysoký `Hdop` / velký rozptyl) misi neposune a
  **`InitializePosition` se nezavolá**; při vyhovujícím okně se zavolá **právě jednou** a s polohou
  rovnou průměru okna;
- **zastavení na stanovišti je dvoufázové** — `Arrived` → `Cancel()` (řízené dobrzdění), `Regulator = null`
  až když robot stojí; u `Aborted` naopak hned;
- **parser `geo:`:** sufixy `n/s/e/w`, mezery, minus, a hlavně **`InvariantCulture` i pod českým
  locale** (test s `CultureInfo.CurrentCulture = cs-CZ` — jinak by `49.2103` → 492103);
  nedekódovatelný/nevyhovující text → cíl se nepřijme;
- **zamítnutí cíle** mimo `MaxTargetDistanceM`, cíle dál než `MaxTargetOffRoadM` od cesty a cíle, na
  který nevede trasa; a naopak **jízda na přichycený cíl**, ne na souřadnici z kódu;
- **timeouty** stavů bez člověka → hlášení, nikdy tiché zaseknutí; stavy pod nouzovým zastavením
  timeout **nemají**;
- **servisní okno nezpůsobí falešný zásek** — cíl je zrušen, detektory globální vrstvy jsou vypnuté;
- **`Abort` z každého stavu** zastaví robota (`Cancel()` + `Regulator = null`);
- `QrScanner`: **vypnutý scanner nedekóduje nic** (dekodér se ani nezavolá), snímek z jiné kamery se
  ignoruje, prázdné jméno kamery skenuje všechny, cesta `Image<BGR32>` → Y800 `byte[]` (rozměry,
  jas, podvzorkování) a to, že dekodér dostane **už podvzorkovaný** obraz;
- dekodér: kód se **zakóduje a přečte zpátky** (i po podvzorkování na polovinu), obraz bez kódu dá
  prázdné pole a **ne výjimku**, čárový kód se nehlásí.
  *Odchylka od návrhu:* obraz se **generuje**, nečte se checked-in soubor — je to deterministické,
  bez binárky v repozitáři a nezávislé na tom, co je na build stroji (takže není potřeba ani
  `Assert.Ignore` na chybějící knihovnu). Cena je uvedená výš: testuje se cesta, ne úspěšnost čtení;
- roundtrip serializace `QrCodeMsg` / `MissionMsg` + **registrace v katalogu zpráv** (bez ní index
  zprávu ukáže, ale `Read` vrátí `null` a tváří se to jako chybějící stupeň).

## Plán realizace (fáze)

Předpokládá hotové fáze 0–4 z [global-navigation-runtime.md](global-navigation-runtime.md)
(bez LLA cíle a bez dojezdu není co řídit).

0. ✅ **Nouzové zastavení v `ControlLoop`** — odběr `IsEmergencyStop` (`volatile` field vedle
   `lastImu`), `Drive(0, stojí ? 0 : rotace)`, příznak `EmergencyStop` v `DriveCommandMsg`
   (**FormatVersion 1 → 2**, starší záznamy se čtou dál), diagnostická property `LastMotorState`.
   ~~Odometrie se pod stopem do fúze **nepouští**.~~ **Zrušeno 27. 8. 2026** — odometrie se používá
   normálně; ten výjimkový stav způsoboval, že fúze v servisním okně neměla žádnou vazbu na rychlost
   a póza se rozešla o metry. Viz [decisions.md](decisions.md).
   4 testy (dotáčí se → rotace zůstává; enkodéry na nule
   → nula; bez stavu motorů se nezastavuje; po uvolnění zásahy zas jdou).
   **Zbývá:** ⬜ vypnutí detektoru záseku v globální vrstvě (až ta vznikne),
   ⬜ **ověření na zařízení** (i nahrání upraveného MicroBasic skriptu).
1. ❌ **ZBar do repa a na obě platformy** — **zrušeno**, dekodér je ZXing.Net (čistě managed), takže
   nativní knihovna ani rezolver nejsou potřeba. Viz [decisions.md](decisions.md), 26. 8. 2026.
2. ✅ **`QrScanner` + `QrCodeMsg`** — `IQrDecoder`, `Enabled` (výchozí vypnuto), Y800 bez
   `System.Drawing`, podvzorkování, počítání shodných čtení. 14 testů.
   **Zbývá:** ⬜ ROI (vědomě nepostaveno — není známo, kde v obraze kód je, takže by to byl
   spekulativní parametr), ⬜ **změření dekódování na OrangePI**.
3. ✅ **`IMissionTargetParser`** — [`GeoUriTargetParser`](../Src/ARBot.Common/Missions/GeoUriTargetParser.cs)
   portovaný podle ARBot2, 17 testů (včetně toho na `cs-CZ`). Sanity checky (vzdálenost od depa,
   dosažitelnost v grafu) jsou **v misi**, ne v parseru — parser zůstal `string → LLA?`.
   Dosažitelnost počítá [`GlobalNavigator.Probe`](../Src/ARBot.Common/Maps/OsmNav/Navigation/GlobalNavigator.cs)
   nad **vlastním, zahoditelným** `GoalField`, takže zkouška nesahá na aktivní cíl.
4. ✅ **`RobotourMission`** — automat včetně servisního okna nad `IsEmergencyStop`, dvoufázové
   zastavení, timeouty jen u stavů bez člověka, `MissionMsg`; 23 testů nad falešnými sinky.
   Napojeno na `mission=robotour` (zakládá i `QrScanner`).
   **Zbývá:** ⬜ vypnutí detektoru záseku globální vrstvy při servisním okně je zajištěné *nepřímo*
   (cíl je zrušen ⇒ detektory bez aktivního cíle vypadnou) — je to otestované na úrovni mise, ne
   proti skutečnému `GlobalNavigatoru`.
5. ✅ **UI panel mise** —
   [`RobotourMissionDocument`](../Src/ARBot/ViewModels/RobotourMissionDocument.cs), menu
   *Tools → Mise Robotour*: fáze, **na co se čeká**, stav nouzového zastavení, přečtený kód
   s odvozeným cílem, vzdáleností a délkou trasy, zapamatované cíle, čítače a tlačítka
   Start / Přerušit.
   - **Stav se čte ze `MissionMsg` na Streamu**, ne z instance mise, takže panel funguje i při
     **přehrávání záznamu** (celá jízda se dá přehrát fázi po fázi). Příkazy potřebují živou misi;
     když neběží, panel to **řekne přímo v UI** místo aby tlačítka tiše nic nedělala.
   - **„Na co se čeká" je vlastní řádek**, protože nouzové zastavení je signál mise **jen ve stavech,
     které na něj čekají** — obsluha, která ho zmáčkne za jízdy, by jinak čekala, že tím něco
     odemkla.
   - Kvůli tomu vznikl i **ovládání nouzového zastavení v simulaci** (viz níže; od 27. 8. 2026 je to
     červené tlačítko s viditelnou aretací, ne zaškrtávátko) a `MissionMsg` povyrostla na **verzi 2**.
   - ✅ **Průchod proklikán autorem 27. 8. 2026** a funguje. Vyšly z toho tři opravy: QR se staví na
     **1,0 m** (z 1,2 m se nepřečte), stanoviště mají v panelu **tlačítka s hotovými kódy** a zkouška
     dosažitelnosti přestala zamítat cíle **za robotem**. UI samo testy nemá — aplikace nemá testovací
     projekt — takže je ověřené proklikáním a kompilací.
6. ❌ **Přežití restartu** (stavový soubor + opt-in obnovení) — **zrušeno** 27. 8. 2026, rozhodnutí
   autora: mise restart přežít nemusí. Viz [decisions.md](decisions.md) a
   [Přežití restartu](#přežití-restartu-zrušeno).
7. ⬜ **Ověření na HW** — čtení kódů z pravé kamery na skutečném stanovišti, celý handshake
   s nouzovým zastavením, celá mise nasucho na krátké trase.

### Jak to pustit

Profil *ARBot - mise Robotour, rovna mapa* v `Src/ARBot/Properties/launchSettings.json`, nebo:

```bash
ARBot.exe virtualhw=true no_uart=true mission=robotour map=OSM/SyntetickyRovny.osm
```

Parametry z příkazové řádky: `depotfix=` (okno kvalitního fixu v depu),
`qrcamera=` (kamera pro čtení; **prázdné = všechny**).

Pak v aplikaci:

1. **Tools → Mise Robotour** → *Start mise*. Mise čeká na kvalitní fix a inicializuje jím fúzi
   (`ArmingAtDepot`), pak přejde na *Čeká na nouzové zastavení*.
2. **Tools → Virtuální senzory** → zmáčknout **červené tlačítko nouzového zastavení** (zaaretuje se —
   hlava se zapustí). Mise vstoupí do servisního okna a **zapne scanner**.
3. Ukázat pravé kameře QR kód s `geo:` cílem. Panel mise ukáže text, souřadnice, vzdálenost od depa,
   **odstup od cesty** a délku trasy a **rovnou ho přijme** — nic se nepotvrzuje.
4. Odtrhnout nouzové zastavení → robot vyrazí na nakládku. Totéž na nakládce, u vykládky se kód nečte.

Krok 3 jde v simulaci projít taky: panel má v servisním okně sekci **„QR kód do virtuální kamery"**,
která postaví desku s kódem vpravo od robota čelem k němu (a ta **zmizí sama, až se kód přečte**).
Tlačítka **Nakládka / Vykládka** vyplní hotové kódy stanovišť současné testovací mapy; vzdálenost
nechat na **1,0 m** — z větší se kód nepřečte.
Panel zároveň ukazuje **obraz té kamery**, takže je vidět, jestli je kód ve výhledu. Detail:
[virtual-hw.md](virtual-hw.md#qr-kód-ve-scéně-svislé-desky-26-8-2026).

> **Mise se sama nerozjede** — čeká na *Start mise*. Bezobslužný běh tedy zůstane v `Idle` a jen
> periodicky hlásí `MissionMsg` (ověřeno: 15s běh = 15 zpráv, nic nespadlo). Vědomě k tomu **není**
> přepínač „spusť misi sama": robot, který vyrazí bez člověka, je nebezpečný.

## Otevřené úkoly

- **Vizuální dojezd na QR kód** — rohy kódu v obraze dávají směr i vzdálenost; poslední ~3 m by šly
  řídit vidění místo GPS (viz stejný úkol v [global-navigation-runtime.md](global-navigation-runtime.md)).
- **Detekce nákladu** (senzor/kamera). Od zrušení potvrzování je jediným důkazem „náklad je naložen"
  **uvolnění stop tlačítka** — tedy gesto člověka, ne měření. Skutečný senzor by z toho udělal fakt.
- ~~**Limit vzdálenosti cíle od silniční sítě.**~~ **Hotovo 27. 8. 2026** — cíl se přichycuje na
  nejbližší hranu a odstup se porovnává s `MaxTargetOffRoadM`; viz „Přichycení cíle na cestu" výše.
  Zbývá **nastavit limit z dat** (dnes je z úsudku) — odstup se měří a jde do záznamu.
- **Chování při `NoRoute` na cíl z QR** — dnes návrh hlásí a čeká na operátora. Rozumnou automatikou
  by bylo „zkus dojet co nejblíž a pak znovu", ale to je rozhodnutí o strategii soutěže, ne o kódu.
- **Rozpoznání startovní/cílové čáry nebo jiných značek soutěže**, pokud je pravidla zavedou.
