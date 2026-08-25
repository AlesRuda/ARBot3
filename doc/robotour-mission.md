# Mise Robotour (`MissionController`) + čtení QR kódů

> **Jméno se změní na `RobotourMission`** (rozhodnutí 25. 8. 2026) — sourozenec
> [`FreeRunMission`](mission-freerun.md), která je hotová a dělala se **dřív**. Tento dokument ještě
> mluví o `MissionController`; při implementaci se použije nové jméno.
>
> **Společnou abstrakci misí nezavádět předem** — až tahle mise vznikne, teprve se ukáže, co je
> s FreeRunem opravdu společné. Vybírají se selektorem `mission=` (viz
> [mission-freerun.md](mission-freerun.md#která-mise-běží-mission)), který `robotour` dnes hlásí jako
> zatím neexistující.

Nejvyšší vrstva řízení: **stavový automat soutěžní jízdy**. Zapamatuje si start/depo, přečte z **pravé
kamery** QR kód s místem nakládky, dojede tam a zastaví, proběhne nakládka a přečtení dalšího QR kódu
s místem vykládky, dojede tam, vyloží a **vrátí se do depa**.

Řídí [`GlobalNavigator`](global-navigation-runtime.md) zadáváním **LLA cílů** — sama nezná ani graf
cest, ani occupancy grid, ani regulátory. Cesta k cíli, objíždění a detekce záseku jsou vrstvy pod ní.

> **Stav (2026-08-11): NÁVRH, nic z toho není implementované.** Dokument je zadání pro realizaci.
> Formát QR (`geo:`), dekodér (**ZBar**) i způsob potvrzení (**nouzové zastavení + potvrzení
> obsluhou**) jsou rozhodnuté podle předchozí generace robotu, která je v soutěži používala
> ([ARBot2](#dekodér--zbar)).

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

⟦servisní okno⟧ = AwaitingEStop ─▶ Servicing ─▶ AwaitingEStopRelease
```

| stav | co se děje | přechod dál |
|---|---|---|
| `Idle` | čeká na „Start mise" z UI | operátor |
| `ArmingAtDepot` | **čeká na kvalitní fix, inicializuje jím fúzi a zapamatuje depo** (viz [níže](#armingatdepot-kvalitní-fix-a-inicializace-fúze)) | fix OK |
| `AwaitingEStop` | robot **stojí a je pod napětím**; čeká, až obsluha zmáčkne nouzové zastavení. Scanner **vypnutý** | `IsEmergencyStop == true` |
| `Servicing` | nouzové zastavení drží → obsluha nakládá/vykládá; pokud se v tomto okně čeká kód, je zapnutý `QrScanner` a přečtený cíl se ukáže v UI k **potvrzení obsluhou** | potvrzení (kód a/nebo „hotovo") |
| `AwaitingEStopRelease` | vše potvrzeno; čeká na **uvolnění** nouzového zastavení | `IsEmergencyStop == false` |
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
4. **Depo se zapamatuje** (LLA + kurz) do `MissionMsg` i do stavového souboru
   (viz [Přežití restartu](#přežití-restartu)).

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

### Přijetí cíle: stroj kontroluje, člověk potvrzuje

Jedno chybné dekódování může poslat robota o stovky metrů jinam, proto dvě nezávislé pojistky:

**Stroj** (automaticky, ještě než se cíl vůbec nabídne):
- **sanity check vzdálenosti** — cíl musí být blíž než `MaxTargetDistanceM` (default 2000 m) od depa;
- **dosažitelnost v grafu** — `GoalField` po `InsertGoal` musí dát konečnou cost-to-goal; jinak by se
  `NoRoute` zjistilo až za jízdy;
- `QrConfirmations` (default **1**, jako v původním kódu) shodných dekódování — víc než jedno je
  levné, ale skutečnou pojistkou je až potvrzení obsluhou.

**Člověk** (ve stavu `Servicing`, pod drženým nouzovým zastavením): UI ukáže **přečtený text, z něj
odvozené souřadnice ve stupních, vzdálenost od depa a délku nalezené trasy** — a teprve po potvrzení
se cíl přijme. Obsluha tak nepotvrzuje „nějaký kód se přečetl", ale konkrétní, zkontrolovatelný cíl.

Přečtený text jde **doslova** do `QrCodeMsg` i `MissionMsg` → v záznamu je vidět, co robot přečetl,
i když to zamítl.

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

### Dekodér — ZBar

Bere se to, co v předchozí generaci **velmi dobře fungovalo**: **ZBar** přes C# binding
`zbar-sharp` (`ARBot2/Sources/dotnet/zbar-sharp-master/libzbar-cil/`). Zdroj bindingu se podle pravidla
„vše v repozitáři" **zkopíruje do `Src/ThirdParty/ZBar/`** (stejný vzor jako `Src/ThirdParty/Intel.RealSense`)
a zapíše do [build-and-platforms.md](build-and-platforms.md) jako externí (ne-NuGet) reference.

Dvě věci se proti původnímu kódu **musí** udělat jinak:

1. **Nepoužívat `System.Drawing`.** Původní kód volal `i.ToBitmap()` a přetížení
   `ImageScanner.Scan(System.Drawing.Image)`. `System.Drawing.Common` je na .NET dostupný **jen na
   Windows**, takže na Armbianu by to spadlo. Binding ale umí surová data:

   ```csharp
   using var img = new ZBar.Image {
       Width = (uint)w, Height = (uint)h,
       Format = ZBar.Image.FourCC('Y','8','0','0'),   // ZBar čte GREY/Y800
       Data = grayBytes,                              // z Image<Gray>
   };
   var symbols = scanner.Scan(img);                   // přetížení Scan(ZBar.Image)
   ```

   Tedy `Image<BGR32>` → `Image<Gray>` → `byte[]` → ZBar, bez jakéhokoli bitmapového mezikroku
   (a bez konverze do RGB3 a zpět, kterou dělalo původní přetížení — je tedy i rychlejší).
2. **Zajistit nativní `libzbar` na obou platformách.** Binding má `[DllImport("libzbar")]`:
   - **Windows/x64:** `libzbar.dll` + `libiconv.dll` (jsou v ARBot2 vedle bindingu) se kopírují do
     výstupu stejně jako `NativeLib.dll`;
   - **OrangePI/ARM64:** z balíčku Armbianu (`libzbar0`) → soubor je typicky `libzbar.so.0`, takže bude
     potřeba symlink `libzbar.so` **nebo** `NativeLibrary.SetDllImportResolver`. **Rezolver je lepší** —
     nezávisí na tom, co je na cílovém stroji nalinkované.

   Konfigurace scanneru zůstává z původního kódu: vypnout všechno (`SetConfiguration(0, Config.Enable, 0)`)
   a povolit jen `SymbolType.QRCODE` — ostatní symbologie jen zdržují a mohou plodit falešné nálezy.

Dekodér je za rozhraním `IQrDecoder { QrResult[] Decode(Image<Gray> img); }`, takže výměna (nebo
fallback, kdyby `libzbar` na zařízení chyběl) je lokální a testy si dodají vlastní implementaci.

## Zprávy a záznam

- **`QrCodeMsg`** (nová) — kamera, text, rohy, čas.
- **`MissionMsg`** (nová) — fáze, čas vstupu do fáze, uplynulý čas mise, depo (LLA), pickup/drop (LLA
  + zdrojový text kódu), důvod přerušení, čítače (kolik čtení, kolik timeoutů). Emituje se **při každé
  změně fáze** a periodicky (`MissionMessagePeriod`, 1 s) → ve View se dá přehrát celá mise.
- Konverzi vlastní doména: `MissionState.ToLogMessage()` (viz [CLAUDE.md](../CLAUDE.md)).

## Přežití restartu

Pád aplikace uprostřed soutěžní jízdy nesmí znamenat ztrátu **depa** (do kterého se má robot vrátit) —
je to jediná informace, kterou nelze získat znovu. `MissionController` proto po `ArmingAtDepot` (a při
každé změně fáze) zapíše malý stavový soubor (`logs/mission-state.json`: depo, fáze, cíle, časy) a při
startu nabídne **obnovení mise** místo nové. Obnovení je **explicitní volba operátora**, nikdy
automatická — robot, který po restartu sám vyrazí, je nebezpečný.

## Parametry

| parametr | default | pozn. |
|---|---|---|
| `QrCameraName` | `"Right"` | prázdné = skenovat všechny kamery |
| `QrConfirmations` | 1 | shodná dekódování; skutečná pojistka je potvrzení obsluhou |
| `QrDownscale` | 2 | podvzorkování před dekódováním |
| `QrSearchSec` | 10 s | po této době se v UI hlásí „kód nevidím" (skenuje se dál) |
| `DepotFixSec` | 5 s | jak dlouho musí fix nepřerušeně vyhovovat, než se průměrem inicializuje fúze |
| `MinSatellites` / `MaxHdop` / `MaxSpreadM` | 6 / 2,0 / 1,0 m | kvalita fixu v `ArmingAtDepot` |
| `MaxTargetDistanceM` | 2000 m | sanity check cíle z QR |
| `StateTimeouts` | per stav | **jen stavy bez člověka v cyklu** (jízda, `ArmingAtDepot`) |
| `MissionMessagePeriod` | 1 s | perioda `MissionMsg` |

Časový a rychlostní limit soutěže patří rovněž do konfigurace — **hodnoty doplnit z aktuálních
pravidel Robotour** (v tomto dokumentu se záměrně netvrdí, jaké jsou).

## Testy

Automat je čistá logika → testovatelný celý, bez HW, bez kamer a bez fúze:

- **průchod celou misí** s falešným `IGlobalGoalSink` a falešnými `QrCodeMsg` + `MotorStateBase` →
  očekávaná posloupnost fází a zadaných cílů (poslední = depo, **shodné se zapamatovaným**);
- **servisní okno:** bez zmáčknutého nouzového zastavení se **nezapne scanner** ani se nepokročí;
  bez potvrzení obsluhou se cíl **nepřijme**; bez uvolnění stopu se **nerozjede**;
- **nouzové zastavení za jízdy automat neposune** (zůstane v `Driving*`, cíl se nezruší);
- **`ArmingAtDepot`:** nekvalitní fix (málo satelitů / vysoký `Hdop` / velký rozptyl) misi neposune a
  **`InitializePosition` se nezavolá**; při vyhovujícím okně se zavolá **právě jednou** a s polohou
  rovnou průměru okna;
- **zastavení na stanovišti je dvoufázové** — `Arrived` → `Cancel()` (řízené dobrzdění), `Regulator = null`
  až když robot stojí; u `Aborted` naopak hned;
- **parser `geo:`:** sufixy `n/s/e/w`, mezery, minus, a hlavně **`InvariantCulture` i pod českým
  locale** (test s `CultureInfo.CurrentCulture = cs-CZ` — jinak by `49.2103` → 492103);
  nedekódovatelný/nevyhovující text → cíl se nepřijme;
- **zamítnutí cíle** mimo `MaxTargetDistanceM` a cíle, na který nevede trasa;
- **timeouty** stavů bez člověka → hlášení, nikdy tiché zaseknutí; stavy pod nouzovým zastavením
  timeout **nemají**;
- **servisní okno nezpůsobí falešný zásek** — cíl je zrušen, detektory globální vrstvy jsou vypnuté;
- **`Abort` z každého stavu** zastaví robota (`Cancel()` + `Regulator = null`);
- **obnovení po restartu** ze stavového souboru je opt-in, ne automatické;
- `QrScanner`: dekódování nad **checked-in testovacím obrázkem** (deterministické; když `libzbar`
  na build stroji chybí, test se `Assert.Ignore` — nesmí padat na prostředí), plus test, že
  vypnutý scanner nedekóduje nic, a test cesty `Image<BGR32>` → Y800 `byte[]` (rozměry, řádkování);
- roundtrip serializace `QrCodeMsg` / `MissionMsg`.

## Plán realizace (fáze)

Předpokládá hotové fáze 0–4 z [global-navigation-runtime.md](global-navigation-runtime.md)
(bez LLA cíle a bez dojezdu není co řídit).

0. ✅ **Nouzové zastavení v `ControlLoop`** — odběr `IsEmergencyStop` (`volatile` field vedle
   `lastImu`), `Drive(0, stojí ? 0 : rotace)`, příznak `EmergencyStop` v `DriveCommandMsg`
   (**FormatVersion 1 → 2**, starší záznamy se čtou dál), diagnostická property `LastMotorState`.
   Odometrie se pod stopem do fúze **nepouští**. 4 testy (dotáčí se → rotace zůstává; enkodéry na nule
   → nula; bez stavu motorů se nezastavuje; po uvolnění zásahy zas jdou).
   **Zbývá:** ⬜ vypnutí detektoru záseku v globální vrstvě (až ta vznikne),
   ⬜ **ověření na zařízení** (i nahrání upraveného MicroBasic skriptu).
1. ⬜ **ZBar do repa a na obě platformy** — `Src/ThirdParty/ZBar/` (binding z ARBot2), `libzbar.dll`
   pro x64, `libzbar.so` + `DllImportResolver` pro OrangePI; zápis do
   [build-and-platforms.md](build-and-platforms.md). *Ověřit na zařízení, že se knihovna nahraje.*
2. ⬜ **`QrScanner` + `QrCodeMsg`** (vč. `IQrDecoder`, `Enabled`, Y800 bez `System.Drawing`,
   ROI/podvzorkování) a **změření dekódování na OrangePI**.
3. ⬜ **`IMissionTargetParser`** — `geo:` parser portovaný z ARBot2 + sanity checky (vzdálenost,
   dosažitelnost).
4. ⬜ **`MissionController`** — automat včetně servisního okna nad `IsEmergencyStop`, `Paused` za
   jízdy, `MissionMsg`; testy nad falešnými sinky.
5. ⬜ **UI panel mise** — fáze, stav nouzového zastavení, přečtený kód **s odvozeným cílem,
   vzdáleností a délkou trasy**, tlačítka Start / Potvrdit / Abort.
6. ⬜ **Přežití restartu** (stavový soubor + opt-in obnovení).
7. ⬜ **Ověření na HW** — čtení kódů z pravé kamery na skutečném stanovišti, celý handshake
   s nouzovým zastavením, celá mise nasucho na krátké trase.

## Otevřené úkoly

- **Vizuální dojezd na QR kód** — rohy kódu v obraze dávají směr i vzdálenost; poslední ~3 m by šly
  řídit vidění místo GPS (viz stejný úkol v [global-navigation-runtime.md](global-navigation-runtime.md)).
- **Detekce nákladu** (senzor/kamera) jako doplněk k potvrzení obsluhou — dnes je jediným důkazem
  „náklad je naložen" stisk tlačítka.
- **Chování při `NoRoute` na cíl z QR** — dnes návrh hlásí a čeká na operátora. Rozumnou automatikou
  by bylo „zkus dojet co nejblíž a pak znovu", ale to je rozhodnutí o strategii soutěže, ne o kódu.
- **Rozpoznání startovní/cílové čáry nebo jiných značek soutěže**, pokud je pravidla zavedou.
