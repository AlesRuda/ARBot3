# EKF senzorická fúze

Rozšířený Kalmanův filtr pro fúzi senzorů (poloha, orientace, rychlost, úhlová rychlost).
Kód: **`ARBot.Common/Fusion/`**. Podrobný rozbor: [`doc/EKF_fuze_dokumentace.docx`](EKF_fuze_dokumentace.docx).

## Architektura

- **`Ekf`** — generická abstraktní třída (výpočet: predikce/korekce, Joseph form,
  čisté `PredictStep`/`UpdateStep` nad libovolným `(x,P)`; MathNet.Numerics).
- **`EKFModel : Ekf`** — konkrétní 2D model diferenciálního podvozku, stav
  **`[X, Y, θ, v, ω]`**, near-constant-velocity predikce, `Q(dt)` škáluje s časem.
  `v` i `ω` jsou **stavy** (fúze z více senzorů → robustnost vůči smyku).
- **Měřicí modely** (`IMeasurement`, `Measurements.cs`): poloha, orientace (wrap),
  rychlost, úhlová rychlost, póza. Každé měření nese čas pořízení, `h/H/R` a residuum.
- **`AsyncFusionEngine`** — asynchronní zpracování dle času **pořízení** (capture):
  buffer uzlů `{měření, x, P}` + `dirtyFrom` (líný přepočet ocasu), out-of-sequence
  (opožděná měření z kamery) přes replay, okno historie ~1 s, `GetStateAt(t)`
  (predikce do budoucna i rekonstrukce minulosti).
- **NIS + gating** (`Gating.cs`): `NIS = dᵀS⁻¹d`; `GateMode.Reject` (zahodit) nebo
  `Soft` (nafouknout R → nikdy se nezasekne, sám se zotaví z výpadku). Bezstavové,
  skládá se s replayem. `AsyncFusionEngine.Diagnostics()` reportuje NIS per měření.
- ✅ **Brána na konečnost** (od 15. 9. 2026, nález auditu): měření s `NaN`/`±∞` v hodnotě
  nebo v `R` se do okna **vůbec nedostane** (`AsyncFusionEngine.Enqueue`, počítadlo
  `DroppedNotFinite` + rozpad po zdrojích) a krok, který nekonečno **vyrobí** (typicky
  singulární `S` při nulovém `R`), měření zamítne (`Ekf.UpdateStep`).
  ⚠️ **Gating na to nestačí a je to důvod, proč ta brána vznikla:** `nis > práh` je pro `NaN`
  **nepravdivé**, takže `NaN` projde jako platné měření — a jelikož se uzly bufferu při každém
  opožděném měření přepočítávají, zapeče se do checkpointů i do `xBase/pBase` a zpátky už cesta
  nevede. Jedno poškozené měření by tedy otrávilo filtr **natrvalo**. Zdroje jsou reálné:
  poškozený rámec z UARTu, `YprU = 0` ze senzoru, degenerovaná kovariance z korelace s mapou.
- **`SlipDetector`** — při nefyzikálním zrychlení kol nafoukne R odometrie.
- **`GeoReference`** (`ARBot.Common/Coordinates`) — počátek lokální ENU roviny
  (LLA, kde `[X,Y]=[0,0]`), převod LLA↔lokální metry přes ECEF.

## Konvence

Stav v **ENU** (X východ, Y sever), orientace **matematicky** (0 = východ, +CCW).
Zdroj R pro orientaci: `IMUState.OrientationUncertainty` z VN100 (viz
[imu-and-frames.md](imu-and-frames.md)). Detaily rámců: tamtéž.

## Jak filtr pozorovat

Co je o EKF vidět a kde:

| Co | Kde | Jak často |
|---|---|---|
| fúzovaná póza X, Y, θ, v, ω | `RobotStateMsg` → telemetrie, World pohled | každý řídicí takt |
| **nejistota σ polohy / x / y / kurzu / v / ω** | `RobotStateMsg.Covariance` → telemetrie a graf, viz [telemetry-view.md](telemetry-view.md) | každý řídicí takt |
| NIS a verdikt jednotlivého měření | `MeasurementDiagMsg` (zapíná `measdiag=`) → telemetrie | per měření |
| korekce z mapy včetně pózy, proti které se korelovalo | `MapCorrelationMsg` (verze 5) | per cyklus korelátoru |
| rozbory po jízdě | `ARBot.Analyze heading` / `sigma` / `corrections` | offline |

**Nejistota je pozorovatelná od 2. 9. 2026.** Kovariance ve `RobotStateMsg` tekla na Stream i do
záznamu už dávno, ale **nikdo ji nezobrazoval**, takže o vývoji filtru nebylo vidět nic. Převod na
σ dělá [`Fusion/StateSigma.cs`](../Src/ARBot.Common/Fusion/StateSigma.cs) (odmocniny diagonály `P`
podle rozvržení `EKFModel`: `IX=0, IY=1, ITh=2, IV=3, IW=4`); vrací `double?` a při chybějící nebo
rozpadlé kovarianci `null`, protože nula by lhala („filtr si je jistý"). Protože se nic neposílá
navíc, **σ jde pustit i na starší záznamy**.

`RobotStateMsg` je **vzorek koncového stavu** na řídicím taktu, ne krok filtru. To je dané tím, že
engine je **asynchronní s okny historie a zpětnými opravami** — „jeden krok" v něm nemá odpovídající
věc, a měření chodí různě rychle (IMU ~100 Hz).

> **Zpráva `EKFStepMsg` byla 25. 9. 2026 smazána.** Nesla celý krok klasického EKF
> (`P` před/po, `K`, `M`, `C`, `Q`, `R`, innovace) a sestavoval ji jen legacy `EKFModel3`
> z ARBot2. V ARBot3 ji nikdo neposílal. Na dnešní asynchronní engine její tvar **nesedí**: popisovala jeden
> synchronní krok. Kdyby se vnitřek filtru měl zveřejňovat (zisk `K` odpovídá na „komu filtr
> věřil"), je to úkol na **nový tvar zprávy**, ne na oživení téhle.

## Stav / poznámky

- Testy: `ARBot.Common.Tests/Fusion` (predikce, jakobián, Q, konvergence, fúze v/ω,
  smyk, wrap, OOSM replay, prune, NIS/gating).
- **Adaptivní odhad R/Q z reziduí je vědomě odložen** — je stavový a konfliktní
  s bezstavovým replayem; zatím per-měření R z kvality senzoru + fyzikální Q + NIS gating.
- **Legacy EKF z ARBot2** (`Common/EKF.cs`, `EKFStep.cs`, `Models/EKFModel2/3*`, spolu se starým
  modelovým rámcem `IModel`/`SimpleModel`/`ModelState(History)` a osiřelým `ARBot.Common.Tests1`)
  byl **25. 9. 2026 smazán** — do té doby jen vyřazený z kompilace. Nahradila ho fúze v `Fusion/`
  (testy + provoz na robotu). Kdyby byla potřeba referenční matematika, je v historii gitu.
  Týž den šly pryč i `EKFStepMsg` + `IEKFStepInfo`: v ARBot3 je nikdo nevyráběl a v žádném
  záznamu nejsou. Zůstal `IModelState`, protože ho používá nová fúze i regulátory.
- **Zbývá** (příště, v projektu `ARBot`): `SensorAdapters` napojující reálné senzory na
  engine + řídicí smyčka; ladění σ a prahů gatingu na reálných datech.

### T265 (VIO): relativní yaw se používá jako ÚHLOVÁ RYCHLOST (2026-09-06)

**Dva nálezy naráz.** Za prvé: `hw.TrackingCamera` **nebyla vůbec napojená do pipeline** —
`BuildSensorSources` drátoval jen `hw.IMU`, `hw.GPS` a `hw.Motor`. T265 se přitom v `ARBotHW`
bezpodmínečně zakládala, běžela, otáčela librealsense pipeline a **její `MeasurementArived` nikdo
neodebíral**, takže její data neměla kam téct: ani do fúze, ani do streamu, ani do záznamu. Na
stránce se ukazovala jako senzor se stářím „—", což vypadalo jako porucha kamery. (Tím padá i
domněnka z DevLogu 3. 9. „T265 by přidala 200 Hz" — nepřidala by nic ani při plné funkčnosti.)

Za druhé, a proto to není jednořádková oprava: **T265 nemá magnetometr.** Její yaw je gravitačně
zarovnaný, ale otočený o **neznámou konstantu** proti severu (počátek = orientace při startu
pipeline). Naivní napojení by ho poslalo do fúze jako `HeadingMeasurement` a **vnutilo filtru
libovolně otočený svět** — tichá, těžko dohledatelná chyba.

**Jak se to řeší.** Zpráva teď nese `IMUState.HasAbsoluteHeading` (verze formátu **3**; starší
záznamy ho nemají a čtou se jako `true`, protože přesně tak se ta data tehdy chovala). VN100 ho má
`true`, T265 `false`. Pro relativní zdroj mapper:

- **neposílá kurz vůbec** (hlídá test),
- posílá **úhlovou rychlost z rozdílu yaw**: `(yaw₂ − yaw₁)/Δt`, protože v rozdílu se ta neznámá
  konstanta **odečte**. Je to rychlost z VIO, tedy dorovnaná obrazem, ne surový integrující gyroskop.
- **neposílá vlastní surový gyroskop** — byl by to týž fyzikální pohyb podruhé a filtr by si tu
  informaci započítal dvakrát.

**Okno místo derivace vzorek po vzorku.** T265 dává pózu 200 Hz; dělit šum yaw časem 5 ms znamená
zesílit ho dvěstěkrát. Na okně `RelYawWindowSec` (0,5 s) je z téhož šumu σ `√2·RelYawStd/okno`,
tedy o dva řády menší číslo. **Okna se nepřekrývají** (kotva se po každém měření posune na současný
vzorek) — překrývající se okna dávají korelovaná měření a filtr by si nadsadil informaci; tatáž past
a tatáž léčba jako u `MapCorrelator.MinPeriod`, viz
[map-correlation-localization.md](map-correlation-localization.md).

Při `Confidence = 0` (VIO ztraceno) se okno **zahodí a ukotví znovu** — po znovunalezení může yaw
skočit, takže rozdíl přes tu dobu je nesmysl.

⚠️ **Absolutní kurz z T265 takhle nevznikne, a to je záměr.** Její skutečná přednost je **nízký
drift** na minutách, a využít ji znamená přidat do stavu EKF **offset yaw** (`T265_yaw + offset =
ENU_yaw`) — což je přesně otevřený úkol „chyby senzorů jako stavy EKF" níž a je gatovaný měřením na
železe. Do té doby se do záznamu ukládají celá `IMUState` z T265, aby se ten offset dal změřit
offline.

⚠️ **`RelYawStd = 0,002 rad` je odhad, ne měření** — T265 hlásí kvalitu sledování, ne σ yaw.
Nastavit se to musí z dat ze zařízení. **Rychlost z T265 (`Velocity`) se zatím nepoužívá**: byl by
to slibný druhý zdroj dopředné rychlosti (lepší než odometrie na kluzku), ale bez měření proti
pravdě by to bylo ladění naslepo.

**Neověřeno na zařízení** — v simulaci T265 není, takže celá tahle cesta zatím běžela jen v testech.

### Kvalita GPS fixu: brána a sigma podle DOP (2026-09-06)

Do 6. 9. 2026 brala fúze **každý** fix, u kterého `GPSState.IsFixed` řekl „ano", a dala mu **vždy
tutéž** σ (`GpsPosStd`, 1,5 m). Počet družic a DOP přitom `GPSState` nese — jen se na ně nikdo
nedíval. Mise Robotour kritéria kvality má (`MinSatellites`, `MaxHdop` při armování depa), takže
fúze byla jediné místo, kde se fix bral bez otázek.

**Jak se to projevilo:** na robotu ujel odhad polohy **~570 m jedním směrem** rychlostí ~0,7 m/s,
zatímco robot **stál**. Že to netáhla predikce, bylo poznat z toho, že rychlost ve stavu byla nula —
`PredictState` posouvá polohu jen o `v·dt`, takže při `v = 0` ji hýbat nemůže. Zbývalo jediné:
poloha se táhla za měřeními, tedy za GPS.

**Léčba, dvě části:**

1. **σ se násobí DOP** (`sigma = GpsPosStd · max(1, DOP)`, parametr `gpsdopsigma=`). Tohle je ta
   podstatná: kvalita fixu je **spojitá** veličina a DOP je přesně ten násobek, o který geometrie
   družic zhoršuje přesnost — slabý fix tedy dostane malou váhu sám od sebe, místo aby se o něm
   rozhodovalo prahem ano/ne. `max(1, …)` proto, že DOP pod 1 by σ zmenšoval pod deklarovanou
   přesnost přijímače.
2. **Brána** na počet družic (`gpsminsat=`, výchozí 4) a DOP (`gpsmaxdop=`, výchozí 10). Má
   odstranit **nesmysl**, ne vybírat dobré fixy — zahodit GPS úplně je horší než jí dát malou váhu.

⚠️ **Neznámá hodnota není špatná hodnota:** přijímač, který počet družic nebo DOP nehlásí (nula),
branou **projde**. Jinak by stačil jeden nemluvný driver a robot by přišel o GPS docela.

Rozhodnutí dělá `DefaultMeasurementMapper.PositionRejectReason` / `PositionStd` — **veřejné
statické**, protože totéž potřebuje webový náhled, aby obsluha u robota viděla, že se GPS nebere
a proč. Dva kusy kódu, které si na tuhle otázku odpovídají samy, se dřív nebo později rozejdou.
Zahození se hlásí do `Trace` (tedy i do záznamu) s omezením na jednu hlášku za 10 s a hned při
změně důvodu — GPS chodí 5× za sekundu a zahozené fixy chodí v sériích.

⚠️ **Zároveň se opravilo mapování u-bloxu**, které tu bránu obcházelo úplně: `uBloxGps` jen
**přetypovával** `fixType` (UBX-NAV-PVT) na `FixQuality` (z NMEA GGA), ačkoli ty dva výčty spolu
nesouvisejí. Důsledek: **samotný mrtvý odhad** (`fixType = 1`, bez družic) se tvářil jako platný
`GpsFix` a fúze ho brala — a přesně takové řešení **ujíždí jedním směrem, i když robot stojí**.
Naopak **GNSS + mrtvý odhad** (`fixType = 4`, dobré řešení) se mapovalo na `Rtk`, které `IsFixed`
nepouští. Převod je teď výslovný, hlídá ho `UBloxFixQualityTests`. Pozor i na to, že u-blox plní
`Hdop` hodnotou **PDOP** (prostorový, vždy ≥ HDOP), takže proti němu je práh přísnější.

**Neověřeno na zařízení:** robot byl v době opravy vypnutý, takže se **neví, co ten přijímač
u těch 570 m vlastně hlásil**. Právě proto přibyla kvalita GPS do náhledu — příště to bude vidět
na stránce místo čtení kódu. Je možné, že fix hlásil dobré hodnoty a příčina je jinde.

### Odometrie teče i pod nouzovým zastavením (2026-08-27)

Do 27. 8. 2026 `DefaultMeasurementMapper` pod nouzovým zastavením odometrii **zahazoval**. Zrušeno
(argument autora): řídicí jednotka má pod stopem příkaz **stát** a motory jsou řízené pozičně ve
zpětné vazbě, takže kola nemohou hlásit nic než nulu — stop odometrii nezhoršuje. Odnesení robota je
navíc stejně možné bez stopu, takže se tím ty dva stavy nerozliší.

**Proč to bylo drahé:** pod drženým stopem neměl filtr **žádnou vazbu na rychlost** (stav má `v` i
`ω`), takže rychlost driftovala a polohu tahal šum GPS. Za desítky sekund stání se odhad rozešel
o metry — v misi Robotour, která stop drží celé servisní okno, to bylo vidět jako poskakující robot
na mapě. Pro srovnání: jízda s tekoucí odometrií má chybu pózy p50 0,164 m.

⚠️ **Zbývající děra:** chybová větev driveru vyrábí `MotorStateBase(true, 0, 0, …)`, takže po selhání
parsování dostane filtr „stojím", i když se robot může pohybovat. Rozlišovat se má „měření vs. zástupný
rámec po chybě", ne stop — viz [decisions.md](decisions.md), 27. 8. 2026.

### GPS kurz je druhá absolutní reference — a sám nestačí (2026-08-25)

**Co se přidalo:** `DefaultMeasurementMapper` dělá z GPS kurzu měření `GPS/heading`. Dva zdroje,
které nejsou totéž:

- `GPSState.Orientation` = skutečný **kurz vozidla** (dvouantenový přijímač, `uBlox HeadVeh`).
  Platí i při stání, σ konstantní (`GpsHeadingStd`). Má přednost.
- `GPSState.DynamicOrientation` = **kurz nad zemí** (course over ground) z vektoru rychlosti
  (`NmeaGps` z VTG, `uBloxGps` jako `atan2`). Jen nad `GpsMinSpeed`.

**σ se počítá, nezadává:** `σ = max(GpsHeadingStd, atan2(GpsCrossTrackStd, v))`. Kurz nad zemí není
měřená veličina, je to `atan2` z vektoru rychlosti, takže jeho nejistota **závisí na rychlosti** —
při 0,5 m/s je to 31°, při 3 m/s 5,7°. Konstantní σ by tu závislost zahodila a při pomalé jízdě by
filtr věřil něčemu skoro náhodnému. `GpsHeadingStd` je **podlaha** (fyzický strop přijímače).

**Jízda vzad je vyloučený stav:** kurz nad zemí je při ní o 180° jinde a rychlost z NMEA je bez
znaménka, takže to z fixu nejde poznat. Vyžaduje se kladná rychlost nad prahem — lepší žádné měření
než měření 180° vedle.

#### Proč to samo nestačí (naměřeno)

Motivace byla, že fúze měla **jedinou** absolutní referenci kurzu, takže bias kompasu neměla proti
čemu změřit: při `imubias=3` zůstala chyba kurzu na 3,0° a odhad seděl na IMU na **100 %** — kompas
kurz **definuje**, ne váží. GPS kurz je přitom nevychýlený (**+0,20°** proti pravdě při šumu 5,02°)
a rozpor `IMU − GPS` je vidět jako **+2,9°**, tedy na 3σ stačí ~30 vzorků = **6 s jízdy**.

Po zapojení ale **`GPS/heading` teče a nic nezmění** — 204 měření za běh, všechna přijatá, chyba
kurzu 2,98°, odhad na IMU pořád 100 %. Důvod je v poměru vah:

| | σ | kadence |
|---|---|---|
| `IMU/heading` | **0,017 rad** (1,0°) | 100 Hz |
| `GPS/heading` | 0,245 rad (14,0° při 1,2 m/s) | 5 Hz |

To je **208× na vzorek** × **20× v kadenci** ≈ **4 000:1**. A i při σ srovnané s naměřeným šumem
(5,0°, tedy `atan(0,1/1,2)`) zbývá **~520:1**.

> **Jádro je v tom, co σ kompasu popisuje.** 0,017 rad je jeho **krátkodobý šum**, ne jeho **bias**.
> Filtr proto věří kompasu na 1°, i když se ten kompas mýlí o 3° **trvale** — a žádné množství
> nevychýlené, ale hlučnější reference to nepřeváží. Sčítat víc absolutních referencí problém
> neřeší; musí se změnit, **co ta σ znamená**.

Souvislost s korelací mapy (tamtéž „korekce kurzu je ve fúzi bezmocná"):
[map-correlation-localization.md](map-correlation-localization.md). Měří to
`ARBot.Analyze heading`.

### Otevřený úkol (→ registr): chyby senzorů jako stavy EKF

Stav a data vede [registr úkolů](ukoly.md); tady je jen seznam, co se téhle oblasti týká.

- **[Chyby senzorů (bias kompasu a gyra) jako stavy EKF](ukoly.md#lok-bias-senzoru-jako-stav-ekf)** —
  místo aby se kompas a ostatní absolutní reference přehlasovaly, odhadovat chybu jednotlivých
  senzorů jako stav `x = [X, Y, θ, v, ω, b_kompas, b_gyro, …]`: kompas pak měří `θ + b_c`, gyro
  `ω + b_g`, oba biasy jako náhodná procházka s malým `Q`, a absolutní kurz pinuje `GPS/heading`.

  **Proč to není jen ladění σ.** Zvýšit `CompassHeadingStd` na řádově stupně je jednořádkové, ale je to
  fudge: filtr pak kompasu nevěří ani krátkodobě, kde je dobrý. Bias jako stav odděluje „krátkodobý
  šum" od „trvalé odchylky", což jsou dvě různé věci, které dnes popisuje jedno číslo.

  **Observabilita je změřená:** `b_gyro` je observabilní z jakékoli absolutní reference kurzu (stačí
  kompas), `b_kompas` z `GPS/heading` — v simulaci na 3σ za 6 s jízdy. Námitka, že by bias musela
  pinovat korelace s mapou (která má vlastní vadu, takže by stav pojedl chybu korelátoru), padla:
  GPS kurz je nezávislý na magnetometru i na mapě.

  **Gate: nejdřív potvrdit na reálném HW, jestli je to vůbec potřeba.** Všechno výše je změřené
  v simulaci, kde ten 3° bias kompasu vnutil člověk parametrem `imubias=3`. Jestli má skutečný VN100
  v téhle montáži bias, je empirická otázka o tom železe — a když ne, je to zbytečná složitost ve
  stavovém vektoru, na kterém visí všechno ostatní. Změřit to jde jízdou bez čehokoli nového:
  `dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- heading Records/<zaznam>.rec` — report
  umí běh bez ground truth a tiskne `IMU yaw − GPS kurz` (střední hodnotu, šum a kolik vzorků je
  potřeba na 3σ); stačí, že jsou to dvě nezávislé absolutní reference. Přístroj je ověřený proti
  známé odpovědi (`--nogt` nad simulačním záznamem, kde pravda existuje, ale zahodí se): ohlásil
  střední rozpor 2,78° proti vnucenému biasu 2,99°, tedy shoda do 0,2°, a odhadl potřebu 29 vzorků
  = 5,8 s jízdy.

  **Podmínky pořízení:** jízda nad prahem rychlosti (kurz nad zemí při stání neexistuje) a nejlépe
  smyčka nebo aspoň dva různé kurzy. Bias magnetometru je vázaný na tělo robota, takže se s kurzem
  otáčí; deklinace nebo chyba v převodu rámců je vázaná na svět, takže nerotuje — bez otočení se to
  nerozliší (tentýž rozlišovací znak používá [map-correlation-localization.md](map-correlation-localization.md)
  na „bias z montáže kamer vs. posun mapy"). Rozpor řádu stupňů, který rotuje s kurzem ⇒ bias
  kompasu je skutečný a úkol má smysl; rozpor pod ~0,5° ⇒ zavřít jako nepotřebné a `GPS/heading`
  nechat jen jako druhou referenci pro případ výpadku magnetometru.

  **První měření na zařízení gate nezavřelo:** nad `records/test/20260906-082403.rec` vyšlo
  `IMU yaw − GPS kurz` = −59,2° a `odhad − IMU yaw` = −0,01° ± 0,21° — na skutečném železe platí
  totéž co v simulaci, kompas kurz definuje, a poměr 4 000:1 je tím potvrzený na HW. Těch 59° ale
  není bias, který má pojmout stav, je to porucha: týž senzor měl o čtyři dny dřív rozpor
  −0,25° ± 4,3° a magnetické pole se mezitím nezměnilo (rozbor v [imu-and-frames.md](imu-and-frames.md)).
  Stav v EKF by takovou chybu schoval, ne opravil, a `GPS/heading` s σ 14° ji stejně nepřetáhne.
  Rozhodnout to může jen záznam se smyčkou a se zdravým kompasem; teprve na něm se ukáže, jestli má
  VN100 bias řádu stupňů, kvůli kterému by se stav vyplácel.

## GPS táhne stojícího robota (změřeno 2026-09-06)

Z pozorování autora: robot **stojí**, klesne DOP, začne se aktualizovat poloha — a protože je
occupancy grid kotvený ve světě, posouvá se pod robotem lokální mapa a vznikají v ní artefakty.
Měří to `ARBot.Analyze gps` (viz
[record-replay.md](record-replay.md#gps-proč-se-stojícímu-robotu-hýbe-poloha)).

**Data:** `records/20260902-222601.rec` — **390 s stání** (240 s + 150 s), rozpoznaného
z enkodérů.

> **Tři vady v samotném měřidle, které se našly až při čtení výstupu** (na dotaz autora „jak jsi
> spočítal sd na jednom vzorku?"): řádek `N = 1` v průměrovací křivce **není měření**, ale
> normalizační bod (blok o jednom vzorku je ten vzorek sám, takže činitel 1,00 vyjde z definice) —
> teď je tak i popsaný. Bloky a lagy se počítaly přes **slitý** seznam obou segmentů, takže
> překračovaly 32s mezeru mezi nimi; po opravě na počítání uvnitř segmentů se čísla pohnula
> málo (ρ@10 s 0,78 → 0,77, činitel 9,92 → 9,96×), ale správně to nebylo. A **sweep lagů končil
> na 25 s**, kde je ρ = 0,38 — těsně nad 1/e, takže report hlásil „dekorelační čas je delší než
> měřené okno" a odpověď ležela hned za koncem tabulky; po prodloužení na 80 s vyšlo `T_d ≈ 40 s`. *(Oba záznamy z 6. 9. mají stání nula — robot celou dobu jel; report to řekne sám
rozdělením posunu kol, nejtišší okno 0,65 m za 20 s.)*

### Co se potvrdilo

| | naměřeno |
|---|---|
| pohyb odhadu při **nehybných kolech** | **5,5 m/min** |
| hlášená σ polohy z fúze (`√P_xx`) | **0,074 m** |
| σ GPS, se kterou fúze počítala | 5,6 m |
| efektivní Kalmanovo zesílení `K` (regrese) | 0,0023 / 0,0013 (R² 0,81 / 0,75) |
| kadence oprav | **10 Hz** |
| **časová konstanta následování GPS `τ`** | **57 s** |

`τ = 57 s` je hluboko pod dobou stání (240 s), takže se odhad na bloudící fix **stihne
dotáhnout** — GPS má nad stojícím robotem autoritu. A `P` je přitom **nepoctivá o řády**: filtr
hlásí, že zná polohu na 7 cm, zatímco se odhad hýbe o 5,5 m za minutu.

⚠️ **Opravené kritérium.** Původně jsem si předregistroval práh „K ≥ 0,05". To bylo **špatně
škálované**: `K` je zesílení na **jednu opravu**, takže jeho význam závisí na kadenci. Naměřené
`K ≈ 0,002` by podle něj znamenalo „GPS netáhne", ačkoli odhad ujede metry. Rozhoduje `τ`, ne `K`.

### Proč: 10 Hz měření, z nichž je nezávislé zhruba jedno za 25 s

Nejsilnější nález je v časové korelaci. Odchylka fixu od průměru segmentu (tedy čistá chyba,
protože robot stál) má **p50 4,4 m** a max 10,2 m, a je **silně korelovaná v čase**:

| τ | 1 s | 5 s | 10 s | 15 s | 25 s | 40 s | 60 s |
|---|---|---|---|---|---|---|---|
| ρ | 0,99 | 0,91 | 0,77 | 0,63 | 0,38 | **0,12** | −0,12 |

**Dekorelační čas `T_d` ≈ 40 s.** Průměrovací křivka to říká ještě názorněji — **průměrování
nepomáhá vůbec**:

| N (doba) | sd průměru | kdyby byly nezávislé | činitel nadsazení |
|---|---|---|---|
| 1 (0,1 s) | 3,461 m | 3,461 m | 1,00× *(normalizace, ne měření)* |
| 10 (1 s) | 3,463 m | 1,095 m | 3,2× |
| 100 (10 s) | **3,448 m** | 0,346 m | **10,0×** |

Průměr ze sta fixů je stejně přesný jako jeden. Filtr přitom bere každý z nich jako nezávislé
měření se σ 5,6 m — tedy si za jeden dekorelační čas „nasčítá" informaci ze **~400 vzorků**,
které nesou informaci jednoho. Odtud ta σ 7 cm.

⚠️ **Dvě meze přesnosti, které to číslo nesmí přežít bez uvedení.** Odchylky se berou od
**průměru segmentu**, čímž se odečte stejnosměrná složka — autokorelace na dlouhých lagách se tím
uměle srazí, takže `T_d ≈ 40 s` je **spodní odhad** (a záporná ρ na 60–80 s je právě stopa po tom
odečtení, ne fyzika). A stojí to jen na **~10 nezávislých vzorcích** (390 s / 40 s). Pro závěr
„průměrování nepomáhá" to hraje ve prospěch opatrnosti, takže ho to nezeslabuje — ale přesnou
hodnotu `T_d` je potřeba potvrdit na delším záznamu ze stání.

**Je to táž past, jakou má projekt už jednou zaplacenou u `MapCorrelator`** (dekorelační čas
~3 s → `MinPeriod` 400 ms → 3 s, aby bylo každé měření nezávislé konstrukcí), jen s desetkrát
delší konstantou. Viz [map-correlation-localization.md](map-correlation-localization.md).

### Platí ten dekorelační čas i za jízdy? Z těchto dat se to změřit NEDÁ

Otázka autora: decimace na 40 s dává smysl při stání, ale co za jízdy? Multipath závisí na tom,
co je kolem antény, takže při pohybu se chyba nejspíš dekoreluje **rychleji** — a decimovat pak
na periodu naměřenou při stání by zahazovalo skutečnou informaci.

Měří to blok **A2b**: z enkodérů se sestaví mrtvý odhad (kurz z **rozdílu kol**, ne z fúze — ta
obsahuje kompas i GPS, tedy právě to, co se měří), **tuze se zarovná na dráhu z GPS** (2D
Procrustes) a autokorelace zbytku dá dekorelační čas.

**Výsledek: na tohle v záznamech nejsou data.** Nejdelší souvislý úsek jízdy napříč všemi
záznamy je **45 s / 17,5 m**; ostatní 34 s / 2,8 m a 32 s / 3,1 m. Robot jezdí stylem
popojeď‑stůj (FreeRun manévruje), takže se souvislé úseky rozpadají.

A hlavně — **kontrola ukázala, že by to číslo stejně nic neznamenalo.** Táž data ze stání,
prohnaná týmž měřidlem:

| stání, celé segmenty (240 s) | stání, nakrájené na 34s okna |
|---|---|
| `T_d ≈ 40 s` | **`T_d ≈ 10 s`** |

Krátké okno tedy zkrátí zdánlivý dekorelační čas **4×**, protože odečtení střední hodnoty
(u A2b navíc celkového natočení) smaže všechno pomalejší než okno samo. Jízda vydala 10 s
(45s okna) resp. 5 s (34s okno) — tedy **k nerozeznání od artefaktu**.

> **Poučení pro nástroj:** první verze verdiktu z toho vyvodila „zkrácené stání drží déle než
> jízda ⇒ rozdíl je skutečný", protože porovnávala 10 s proti 5 s. To bylo špatně — správně se
> musí porovnávat **zkrácené stání proti CELÝM segmentům téhož stání**, což teprve ukáže, jestli
> okno vůbec dovolí něco vidět. Opraveno.

**Co by bylo potřeba:** souvislá jízda **výrazně delší než očekávaný `T_d`**, tedy řádově
**5–10 minut bez zastavení**. Do té doby je perioda decimace za jízdy neznámá a nastavovat ji
podle hodnoty ze stání je střelba naslepo.

### Vedlejší, ale použitelný výsledek: GPS zná POSUN mnohem líp než POLOHU

Po tuhém zarovnání mrtvého odhadu na dráhu z GPS zbyde na 25 m jízdy odchylka **p50 0,52 m**
(max 2,4 m) — zatímco absolutní chyba polohy je p50 **4,4 m**. Rozdíl je právě ta společná
(pomalu bloudící) složka, kterou zarovnání pohltilo.

Prakticky: **na desítkách sekund zná GPS tvar trajektorie na půl metru, i když je o metry vedle.**
To je přesně ta informace, kterou **decimace zahazuje** a kterou by uměl využít **offset GPS jako
stav EKF** (Gauss–Markov s konstantou ~`T_d`): filtr by pak mohl brát všech 10 Hz, protože by
věděl, že se chyby opakují, a rozdíly by mu nesly pohyb. Je to táž třída úlohy jako otevřený
úkol „chyby senzorů jako stavy EKF" — jen místo biasu kompasu jde o offset polohy.

⚠️ Ten zbytek ale **míchá chybu GPS s driftem odometrie**, takže 0,52 m je horní mez obojího,
ne měření jednoho z nich. Oddělit je by chtěl delší úsek a nezávislou referenci.

### Co z toho plyne pro léčbu

1. **Decimace GPS polohy na dekorelační čas je nejsilnější páka** — je to faktor ~400
   v předpokládané informaci (10 Hz proti jednomu nezávislému vzorku za 40 s). Nebo ekvivalentně
   nafouknout σ o `√(T_d·f)`. ⚠️ **Perioda je ale změřená jen pro STÁNÍ**; za jízdy ji zatím
   nikdo nezná (viz výše), takže při jízdě může decimace zahazovat užitečnou informaci.
2. **`Q` úměrné rychlosti** (dnes je `ProcessNoise` na `v` nezávislé, viz `EKFModel`) je
   opodstatněné, ale samo o sobě to nestačí: i s malým `P` by 10 Hz korelovaných měření odhad
   přetáhlo.
3. **Utažení brány je nejhrubší nástroj** — v tomhle záznamu byl DOP 3–9, tedy pořád pod prahem,
   a přesto chyba p50 4,4 m. Brána tenhle případ nechytí.

### Prozatímní řešení: nafouknutá `gpsposstd` (2026-09-06)

Na návrh autora („není to ideální, ale rychlé"). Sigma polohy z GPS **je teď parametr**
`gpsposstd=` — do té doby to bylo natvrdo pole ve `FusionConfig`, ačkoli na ten klíč odkazoval
popis `gpsdopsigma`.

**Kolik nafouknout, není odhad.** Pro polohu s procesním šumem `Q` a měřením s rozptylem
`R = σ²` je ustálené Kalmanovo zesílení `K ≈ √(Q/R) ∝ 1/σ`, takže časová konstanta
`τ = 1/(K·f) ∝ σ`. Nafouknutí σ o `m` tedy **prodlouží `τ` m-krát** a stejným dílem zmenší drift.
A aby `N` korelovaných vzorků neslo informaci jednoho, má být `σ_eff = σ·√N`:

| | |
|---|---|
| korelovaných vzorků `N = f·T_d` | 10 Hz × 40 s = **400** |
| násobek `√N` | **20×** |
| `gpsposstd` 1,5 m → | **30 m** (dál se násobí DOP) |
| předpokládané `τ` 57 s → | **~19 min** |
| předpokládaný drift 5,5 m/min → | **~0,28 m/min** |

Nastaveno **jen v `config/pi-provoz.cfg`**, ne jako výchozí hodnota: virtuální GPS v simulaci má
šum bílý, takže tam by nafouknutí sigmy odhad jen zhoršilo.

**Vedlejší efekt, který je vlastně žádoucí:** ustálená `P` vzroste z 0,074 m na řádově metr, tedy
k hlášené nejistotě, která odpovídá skutečnosti.

⚠️ **Čtyři výhrady, se kterými se to musí brát:**

1. **Ten násobek je změřený jen pro STÁNÍ.** Za jízdy se `T_d` změřit nepodařilo, takže za jízdy
   může být 20× moc a GPS zbytečně slabá. Příznak, podle kterého to poznáš: globální navigace se
   začne citelně opožďovat za skutečnou polohou.
2. **Neřeší to příčinu**, jen sílu následku. Správně je buď decimovat fixy na `T_d`, nebo dát
   offset GPS do stavu EKF — teprve to umí využít, že GPS zná **posun** mnohem líp než polohu.
3. **`GpsPosStd` slouží dvěma věcem**: je to σ měření *a zároveň* počáteční nejistota polohy
   v `InitializePosition`. Nafouknutí zvětší i tu — což je ale spíš dobře (na startu robot
   opravdu neví, kde je) a velké `P` znamená velké zesílení, takže se první fixy prosadí rychle.
4. **Ověřit měřením, ne pocitem:** `ARBot.Analyze gps` nad novým záznamem ze stání. Drift a `τ`
   se mají posunout **právě o ten násobek**. Když ne, model neplatí a příčina je jinde.

> **Pro tenhle konkrétní problém je nafouknutí σ možná lepší než decimace.** Drží korekce
> **plynulé a drobné**, kdežto decimace by je aplikovala v periodických větších krocích — a
> world-kotvený occupancy grid snáší plíživý posun líp než skoky.

### Co se změřit nepodařilo a proč

**A/B „fix přijat vs. odmítnut" nevzniklo** — v tomhle záznamu prošla brána v každém okně stání
(DOP nikdy nepřelezl 10). Tím pádem **není potvrzené, že za artefakty v gridu může opravdu GPS**;
je potvrzené jen to, že GPS hýbe pózou. Na uzavření řetězu je potřeba **cílený záznam ze stání
v místě, kde DOP kolísá kolem prahu** (u budovy) — přesně to, co autor pozoroval 6. 9.

**Rychlost driftu je ale zdola omezená i bez toho:** póza se plíží, ne skáče — `PoseJumpDetector`
nehlásí **ani jeden** skok při žádném prahu až po 0,05 m, protože při 10 Hz je krok jen ~8 mm.
Grid se tedy **nikdy nezahazuje, jen rozmazává**, a nápad „posouvat origin místo `Clear()`" na
tenhle problém nemá vliv.

### Otevřený úkol (→ registr): Pitch/Roll patří do stavu EKF

Stav a data vede [registr úkolů](ukoly.md); tady je jen seznam, co se téhle oblasti týká.

- **[Náklon robota jde mimo fúzi a nezná svůj zdroj](ukoly.md#lok-ekf-pitch-roll-stav)** —
  `RobotState.Roll`/`Pitch` nejsou součástí stavu filtru, doplňuje je
  [`ControlLoop`](../Src/ARBot.Common/Runtime/ControlLoop.cs) z posledního IMU, které proteklo jeho
  `Consume` (`lastImu`).

  Dva problémy s tím:

  1. **Není poznat, které IMU vzorek poslalo.** [`IMUState`](../Src/ARBot.Common/Models/IMUState.cs) je
     `SensorStateBase`, ale **ne** `INamedMessage` — nenese žádnou identitu zdroje. Při více IMU tedy
     vyhrává prostě to, které dorazilo naposled, a Roll/Pitch mohou mezi tiky přeskakovat mezi čidly
     s jinou montáží i kvalitou. (Fúzní strana měření sice značkuje `Source` — `"IMU/heading"`,
     `"IMU/gyro"` — ale to jsou **konstanty**, takže ani tam se dvě IMU nerozliší.)
  2. **Obchází to fúzi.** Roll/Pitch jdou mimo EKF: bez gatingu (divoký vzorek se nezahodí), bez
     kovariance, bez korektního vzorkování v čase `t` (`GetStateAt` je nedopředikuje, jen se přilepí
     poslední hodnota). Zbytek `RobotState` je přitom fúzovaný a časově konzistentní — je to nekonzistence
     v jednom objektu.

  **Návrh:** přidat pitch/roll **do stavového vektoru EKF** (měření z IMU akcelerometru/YPR jako
  regulérní `IMeasurement` s vlastním σ a gatingem) a `RobotState.Roll`/`Pitch` plnit z filtru jako
  ostatní složky. Pak zmizí i `ControlLoop.lastImu` a smyčka nebude muset odebírat `IMUState`.

  **Kdo to používá** (kontrola dopadu): `RobotState.ToWorldTransform()` /
  `ToWorldTransformWithPosition()` (`Conversions.WorldToWorldTransform(Orientation, Pitch, Roll, …)`).
  Jako mezikrok (kdyby se stav EKF rozšiřovat nechtěl) by stačilo dát `IMUState` identitu zdroje a
  vybírat **konkrétní** IMU podle konfigurace — ale nekonzistenci s fúzí to neřeší.

### Otevřený úkol (→ registr): diagnostika EKF do streamu a záznamu

Stav a data vede [registr úkolů](ukoly.md); tady je jen seznam, co se téhle oblasti týká.

- **[Fúze zahazovala opožděné korekce a nebylo to vidět](ukoly.md#lok-korekce-zahozene-neviditelne)** —
  chování filtru nešlo zpětně prohlédnout ze záznamu: `AsyncFusionEngine.Diagnostics()` vracelo
  per-měření `Source / Time / Nis / Accepted`, ale nikam se to neemitovalo, takže když robot
  v simulaci „poskakoval", nedalo se odlišit, jestli je to šum GPS, nebo gating zahazující měření;
  zprávu [`MeasurementDiagMsg`](../Src/ARBot.Common/Logs/MeasurementDiagMsg.cs) (v katalogu
  `MessageCatalog`, takže se serializuje i přehraje ve View) dnes plní `FusionProcessor`
  za parametrem `measdiag=`.

  Detail, který určil, kde se emituje: NIS při `Enqueue` **ještě neexistuje** — měření se jen
  zařadí a buffer se označí za špinavý; `Nis`/`Accepted` plní až `EnsureValid()` a při doražení
  opožděného měření se uzly přepočítají, takže se NIS může zpětně změnit. Emitovat při vložení by
  zapisovalo hodnotu, která ještě není spočtená; proto se odebírají až usazené hodnoty, periodicky
  a bezpečně pod oknem historie. Objem ~155 měření/s ≈ 12 kB/s je proti obrazům z kamer
  (~1,8 GB/min) zanedbatelný; periodický souhrn po zdrojích by naopak neumožnil dohledat konkrétní
  zahozené měření. (`EKFStepMsg`, smazaná 25. 9. 2026, byla něco jiného — dump celých matic
  z předchozí generace, na průběžný záznam příliš těžký.)

### Zahození „příliš starého" měření: okno historie ≠ základ filtru

`AsyncFusionEngine.Enqueue` zahodí měření podle podmínky **`m.TimeStamp <= tBase`** — tedy podle
**základu filtru**, ne podle okna historie. Jsou to dvě různé věci a pletou se snadno:

- **Okno historie** (`FusionConfig.HistoryWindow`, 3 s) říká, jak hluboko do minulosti se filtr
  umí přepočítat. Na jeho konci `Prune` nejstarší uzel zapeče do základu.
- **`tBase`** je čas toho základu. Před něj se dostat nejde, protože tam žádný stav není —
  a to **bez ohledu na velikost okna**.

Hned po startu a po `InitializePosition` / `InitializeHeading` (které základ přerovnají na zadaný
čas a buffer vyprázdní) je historie krátká, takže i měření o pár milisekund starší propadne.
**Je to správné chování**, ne vada. Typicky se to stane hned po inicializaci u odometrie:
`SDC2160Ex` bere razítko na začátku čtení a pak čte čtyři řádky, takže jeho měření je o ~7–9 ms
starší než okamžik zařazení — proto hlášky chodí v párech `Odo/speed` + `Odo/rate`.

⚠️ **Hláška to do 1. 9. 2026 hlásila zavádějícím způsobem, a to hned dvakrát:**

1. Vinila **okno** („okno je 3000 ms"), přestože o zahození rozhoduje `tBase`. Nově obě situace
   rozlišuje: `starsi nez okno historie … okno je plne, takze merenie doslo POZDE` proti
   `starsi nez zaklad filtru … OKNO ZA TO NEMUZE`, a uvádí, jak daleko historie zatím sahá.
2. Říkala **„opozdeno o 7 ms"**, což tvrdí, že měření došlo pozdě. To platí jen v prvním případě.
   Ve druhém měření opožděné být vůbec nemuselo — **je jen starší než základ, protože základ byl
   postaven až za ním**. Svalovat to na doručení měření posílá hledat chybu na úplně špatné místo.
   Nově se píše „je o N ms starsi nez tBase … Merenie NEMUSELO byt opozdene".

Hlídá to `DroppedTooOldReasonTests`, včetně důkazu, že při okně 3 s i 60 s vyjde zahození stejně,
a včetně kontroly, že se v druhém případě slovo „opozdeno" **nepoužije**.

**Kdy je to naopak signál problému:** když hlášky chodí i po prvních sekundách běhu a hlásí
krátkou historii, něco filtr opakovaně reinicializuje. Pozor při čtení logu: `AsyncFusionEngine`
se zakládá **při každém Start**, takže panel *Debug output* může držet hlášky z víc běhů s různým
`tBase` — samo o sobě to reinicializace není.

## ✅ Podlaha σ kurzu z kompasu (`imuheadingstd=`, od 12. 9. 2026)

`DefaultMeasurementMapper` bral jako σ měření `IMU/heading` **přímo** `YprU` ze senzoru. To je
číslo, o které si senzor řekl sám — a je řádově vedle:

| | hodnota | zdroj |
|---|---|---|
| co senzor hlásí (`YprU`, p50) | **0,057–0,061°** | `ARBot.Analyze vn100`, tři záznamy 12. 9. 2026 |
| jaká je skutečná chyba (`IMU yaw − GPS kurz`) | **−4,90° / −2,79°** | `ARBot.Analyze heading`, dva jízdní záznamy |
| poměr | **~60–90× přesvědčenější, než jaký je** | |

**Proč to `YprU` nemůže vědět:** popisuje **krátkodobý šum** atitudového řešení, ne jeho **bias
vůči severu**. Ten je v **tělesovém rámci** (pootočení senzoru proti podvozku, zbytek magnetické
kalibrace, šikmé jetí robotu) a ani otáčením, ani časem nezmizí — změřeno, že půlrozdíl mezi dvěma
opačnými směry jízdy je jen ±1°, takže to není tvrdé železo. Viz [imu-and-frames.md](imu-and-frames.md).

**Řešení:** `FusionConfig.CompassHeadingStdFloor` (výchozí **5°**, kanonická hodnota je konstanta
`CompassHeadingStdFloorDeg`) se sklada s `YprU` **kvadraticky**:

```
σ = √(YprU² + podlaha²)
```

**Kvadraticky, ne maximem** — když `YprU` vyskočí (magnetická porucha, rozjetá VPE), σ má růst
dál; podlaha má tu informaci **doplnit, ne přebit**.

**Odkud 5°:** RMS změřeného biasu ze dvou běhů je **3,99°**, mezi běhy kolísá o ~2° a místní
porucha pole přidává jednotky stupňů (η² = 0,17–0,19 rozptylu vysvětlí **místo**). Zaokrouhleno
nahoru. Strážný test hlídá, aby default nikdo nesrazil **pod** změřený bias.

**Co to změní:**

| | před | po |
|---|---|---|
| σ měření `IMU/heading` | 0,059° | **5,0°** |
| informace kompas : GPS kurz | ~3,2 × 10⁶ : 1 | **~440 : 1** |

⚠️ **Kompas pořád vyhrává** — není to „skončilo přebírání", jen už ostatní reference něco váží.

⚠⚠ **A počtivý filtr z toho NEBUDE, jen méně nepočtivý.** Chyba kompasu je **časově korelovaná**
(je to bias), ale filtr ji bere jako **bílý šum**, takže si ji ze 100 vzorků za sekundu
„vyprůměruje". Ustálená σ kurzu ve filtru proto vyroste jen z ~0,06° na **~0,58°**, zatímco
skutečná chyba je 3–5° — tedy pořád **~8× přehnaně sebejistý**. Je to **táž past jako
u `gpsposstd`** (časově korelovaná chyba brana jako nezávislá) a jediná skutečná léčba je
**bias kompasu jako stav EKF** — otevřený úkol, pro který je tohle mezikrok, ne náhrada.

⚠️ **Parametr je ve STUPNÍCH** (`imuheadingstd=5`), převod na radiány je na okraji
(`ARBotRuntime.ApplyImuHeadingParams`). **0 = vypnuto**, tedy přesně staré chování pro A/B nad
záznamy. Parser **odmítá hodnoty v intervalu (0; 0,1)**: `imuheadingstd=0.087` (myšleno radiány)
by tiše nastavilo 0,087 **stupně**, což je méně než samo `YprU` — podlaha by se fakticky vypla
a nikdo by si toho nevšiml.

⚠️ **Na HW to neběželo** a dopad na jízdu změřený není. Další krok: záznam s `imuheadingstd=5`
a `imuheadingstd=0` nad týmž úsekem a porovnat `odhad − IMU yaw` a `odhad − GPS kurz`
(`ARBot.Analyze heading`). Kryje to 16 testů v `KompasSigmaTests`.

## ⚠️ Odkud se bere σ kurzu z GPS — a proč je správná ze špatného důvodu (12. 9. 2026)

Otazka autora: *„Proč je poměr kompas : GPS 440 : 1, kde se bere taková nejistota u kurzu GPS?"*

**Rozklad toho poměru.** Informace skalárního měření za sekundu je `f / σ²`:

| | σ | frekvence | informace/s |
|---|---|---|---|
| kompas (po podlaze 5°) | 0,0873 rad | 100 Hz | 13 123 |
| GPS kurz při 0,7 m/s | 0,410 rad (23,5°) | **9,9 Hz** | 58,9 |

Poměr **223 : 1** je tedy součin **22× z σ²** a **10× z frekvence**.

⚠️ **Frekvence fixu se MĚŘÍ, nepředpokládá.** Původně tu stalo 5 Hz — přijímač na robotu jede **9,9 Hz** (změřeno ze záznamů na pokyn autora), takže všechny přepočty byly **dvakrát vedle** a vypadaly přitom rozumně. `ARBot.Analyze heading` si ji teď počítá z mediánu rozestupů mezi fixy (`FixRateHz`) na všech třech místech, kde dřív byla natéčno.

**Odkud 23,5°.** `DefaultMeasurementMapper.FromGpsHeading` počítá
`σ = max(GpsHeadingStd, atan2(GpsCrossTrackStd, v))`, tedy při 0,7 m/s `atan2(0,3; 0,7)`.
To `0,3 m/s` je **předpoklad** — dokumentace u něj říká „stejné jako `GpsSpeedStd`, u přijímače
řešícího rychlost z Dopplera není důvod čekat, že příčná složka je jinak přesná" — a ověřený byl
jen **v simulaci**.

**Změřeno na skutečném přijímači** (`ARBot.Analyze heading`, blok *SUM KURZU Z GPS A JEHO
KORELACE*, oba jízdní záznamy 12. 9. 2026). Měří se změna kurzu z GPS proti změně kurzu
z **gyra** — nezávislého zdroje — za okno délky `lag`:

| lag [s] | σ FreeRun [°] | σ Track [°] |
|---|---|---|
| 0,2 | 1,09 | 0,86 |
| 1,0 | 3,46 | 2,05 |
| 2,0 | 5,29 | 3,02 |
| 5,0 | 7,73 | **4,11** |
| 10,0 | **8,32** | 3,87 |
| 20,0 | 7,72 | 4,09 |
| 40,0 | 9,07 | 4,25 |

⚠️ **Chyba NENI bílý šum** — kdyby byla, σ by na lagu nezávisela. Místo toho **roste a usadí
se** na `σ = 8,4° / 4,1°` při dekoračním čase **10 s / 5 s**.

**Proč nestačí měřit sample-to-sample:** rozdíl dvou sousedních fixů odečtením vyruší všechno,
co se mění pomalu — tedy právě tu korelovanou část. Krátkodobý šum vyšel **0,86–1,09°**, z čehož
by `GpsCrossTrackStd` vyšlo **0,007 m/s**, tedy **45× méně** než předpokládaných 0,3. Vzít tohle
číslo by znamenalo σ dramaticky **podstřelit**.

**Počtivá σ pro filtr**, který bere 5 Hz fixy jako nezávislé, je `σ_celková · √(τ·f)` — týž vzorec,
jímž se odvodilo [`gpsposstd`](configuration.md):

| | σ celková | τ | počtivá σ | model `atan2(0,3; v)` |
|---|---|---|---|---|
| FreeRun | 8,37° | 10 s | **83,1°** | 23,8° |
| Track | 4,08° | 5 s | **28,6°** | 22,9° |

✅ **Takže to číslo je ve správném pásmu (při 10 Hz je 1,3–3,5× optimistické) — ale ze špatného důvodu.** Není to příčný šum rychlosti
(ten je o řád menší); je to náhodou hodnota blízká informačně uškrcené σ. ⚠️ **A je to křehké:**
`atan2(0,3; v)` škáluje s rychlostí, což platí pro bílou složku, ale korelovaná část tu závislost
mít nemusí — při vyšší rychlosti by model σ srazil, aniž by k tomu byl důvod.

### ⚠️ Důsledek pro podlahu σ kompasu: 5° je pořád řádově málo

Táž úprava aplikovaná na kompas dopadá mnohem hůř:

- Chyba kompasu je **bias, který je přes celý běh prakticky konstantní** — `kurz z pole − yaw`
  drží po minutách 3,2–4,8° a půlrozdíl mezi dvěma opačnými směry jízdy je jen ∓1°. Tedy
  `τ ≳ délka záznamu` (600 s), zatímco vzorky chodí **100 Hz** — tedy **desetkrát častěji než GPS a s dekoračním časem o dva řády delším**.
- Počtivá σ by tedy byla `5° · √(600·100)` ≈ **1 200°** — což je jen jiný způsob, jak říct, že
  **konstantní bias nenese žádnou opakovatelnou absolutní informaci**.

⚠️ **Poměr 223 : 1 ve prospěch kompasu je proto artefakt** toho, že se kompasu časová korelace
ignoruje **mnohem víc** než GPS. Kdyby se uškrtily obě počtivě, **GPS kurz by kompas přebil**.

Tři cesty (na rozhodnutí autora, **žádná zatím neprovedena**):

1. **Zvednout podlahu na desítky stupňů** — hrubé, ale hned to obrátí pořadí a je to konzistentní
   s tím, co se udělalo u `gpsposstd` (30 m místo 1,5).
2. **Snížit frekvenci měření** `IMU/heading` (např. 1 Hz místo 100 Hz) — přesnější vyjádření téhož,
   a je to táž léčba jako `MinPeriod` u korelace s mapou.
3. **Bias kompasu jako stav EKF** — jediné správné řešení; kompas pak nese vynikající
   *relativní* informaci (změnu kurzu) a jeho absolutní část si filtr odhadne sám.

✅ **Autor rozhodl 12. 9. 2026: (3) je cíl, teď se dělá (2); hotovo** — `imuheadinghz=`,
výchozí **1 Hz** (`CompassHeadingMinPeriodSec = 1,0`), **0 = neomezeno** (staré chování pro A/B).

### Co se škrtí a co ne — mezi odečty kompasu nese kurz GYRO, ne odometrie

⚠️ `IMU/heading` a `IMU/gyro` jsou **dvě samostatná měření** ze též větve mapperu. Škrtí se
**jen to první**; úhlová rychlost jde dál v plné kadenci:

| zdroj úhlové rychlosti | σ | frekvence | informace/s | podíl |
|---|---|---|---|---|
| VN100 gyro | 0,02 rad/s | 100 Hz | 250 000 | **78 %** |
| T265 (rozdíl yaw na okně 0,5 s) | 0,0057 rad/s | 2 Hz | 62 500 | 19 % |
| odometrie | 0,10 rad/s | 91 Hz | 9 100 | **2,8 %** |

⚠️ U T265 ber ten podíl s rezervou — `RelYawStd = 0,002` je v konfiguraci výslovně označené jako
*„odhad, ne měření"*. Bez ní je to gyro 96,5 % / odometrie 3,5 %.

**Ta asymetrie je fyzikálně obhajitelná, ne libovolná:** chyba gyra je převážně **bílá** (angular
random walk), takže u něj je předpoklad nezávislosti zhruba počtivý — na rozdíl od kompasu, jehož
chyba je bias. **Drift mezi odečty je zanedbatelný:** naměřený klidový bias gyra 0,1–62,6 °/h dělá
za sekundu nanejvýš **0,017°** a za 10 s **0,17°**. Za 600 s už ale **10,4°** — a to je důvod, proč
kompas **nejde zahodit úplne**: bez absolutní kotvy kurz ujede.

**Bere se poslední vzorek, ne průměr intervalu.** Průměrování by srazilo bílý šum (0,059°), ale
bias ne — a ten je o dva řády větší.

**Kolik to udělá:**

| kadence | informace kompasu/s | kompas : GPS kurz |
|---|---|---|
| 100 Hz (dnes) | 13 123 | 223 : 1 |
| **1 Hz (nový default)** | 131 | **2,2 : 1** |
| 0,1 Hz | 13 | 0,22 : 1 (GPS vyhrává) |

Data argumentují spíš pro tu nižší — místně závislá část chyby kompasu se při 0,7 m/s obmění
za ~7 s — ale 1 Hz je konzervativní začátek, kde kompas zůstává kotvou. ⚠️ **Na HW to neběželo.**

⚠️ **Škrcení udělalo z mapperu STAVOVÝ objekt** (pamatuje si razítko posledního vydaného kurzu),
a to je přesně ta vlastnost, která umí rozbít přehrávání záznamu. Záruka, na které record/replay
stojí, je užší než dřív: stav je **čistou funkcí posloupnosti razítek**, takže dvě **čerstvé**
instance nad toutéž posloupností vydají totéž — sdílet **jednu** instanci mezi dvěma běhy už záruka
není. Produkce to nedělá (mapper vzniká jednou na `ARBotRuntime`, přehrávání zakládá nový), ale
`GoldenReplay_ReproducesControlLoopOutput` to dělal — a **spadl**, což bylo správně. Test má teď
mapper na každý průchod zvlášť a záruku hlídá `DvaMapperyNadToutezPosloupnosti_DajiTOTEZ`.

⚠️ **Škrcení se RESETUJE při skoku času vzad** (seek při přehrávání) — bez toho by se po skoku
dozadu přestal kurz vydávat, dokud by se čas nedotáhl zpátky, a to může být celá minuta ticha.
