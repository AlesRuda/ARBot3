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

> **Zpráva `EKFStepMsg` existuje, ale nikdo ji neposílá.** Nese celý krok klasického EKF
> (`P` před/po, `K`, `M`, `C`, `Q`, `R`, innovace) a `EKFModel3.ToLogMessage()` ji umí sestavit —
> je to ale **pozůstatek z ARBot2**, registrovaný v katalogu kvůli čitelnosti starých záznamů,
> stejně jako zpráva `Module`. Na dnešní asynchronní engine její tvar **nesedí**: popisuje jeden
> synchronní krok. Kdyby se vnitřek filtru měl zveřejňovat (zisk `K` odpovídá na „komu filtr
> věřil"), je to úkol na **nový tvar zprávy**, ne na oživení téhle.

## Stav / poznámky

- Testy: `ARBot.Common.Tests/Fusion` (predikce, jakobián, Q, konvergence, fúze v/ω,
  smyk, wrap, OOSM replay, prune, NIS/gating).
- **Adaptivní odhad R/Q z reziduí je vědomě odložen** — je stavový a konfliktní
  s bezstavovým replayem; zatím per-měření R z kvality senzoru + fyzikální Q + NIS gating.
- **Legacy EKF** (`Common/EKF.cs`, `Models/EKFModel2/3*`) je vyřazen z kompilace
  (`<Compile Remove>` v `ARBot.Common.csproj`) — slouží jen jako referenční matematika.
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

### ⚠️ Otevřený úkol: chyby senzorů jako stavy EKF — ale nejdřív potvrdit na HW (2026-08-25)

**Návrh (autorův):** místo aby se kompas a ostatní absolutní referencie přehlasovaly, **odhadovat
chybu jednotlivých senzorů jako stav** — `x = [X, Y, θ, v, ω, b_kompas, b_gyro, …]`. Kompas pak měří
`θ + b_c`, gyro `ω + b_g`, oba biasy jako náhodná procházka s malým `Q`. Kompas tím **přestane mít
právo definovat absolutní kurz**; ten pinuje `GPS/heading`, které už hotové je.

**Proč to není jen ladění σ.** Zvýšit `CompassHeadingStd` na řádově stupně je jednořádkové, ale je to
fudge: filtr pak kompasu nevěří ani krátkodobě, kde je dobrý. Bias jako stav odděluje „krátkodobý
šum" od „trvalé odchylky", což jsou dvě různé věci, které dnes popisuje jedno číslo.

**Observabilita je vyřešená a změřená:** `b_gyro` je observabilní z jakékoli absolutní reference
kurzu (stačí kompas), `b_kompas` z `GPS/heading` — v simulaci na 3σ za 6 s jízdy. Původní námitka
(že by bias musela pinovat korelace s mapou, která má vlastní vadu, a stav by tak pojedl chybu
korelátoru) **padla**: GPS kurz je nezávislý na magnetometru i na mapě.

> **⛔ GATE: potvrdit na reálném HW, jestli je to vůbec potřeba.**
> Všechno výše je změřené v **simulaci**, kde ten 3° bias kompasu **vnutil člověk** parametrem
> `imubias=3`. Jestli má skutečný VN100 v téhle montáži bias, je empirická otázka o tom železe —
> a když ne, celý tenhle úkol je zbytečná složitost ve stavovém vektoru, na kterém visí všechno
> ostatní.
>
> **Jak to na zařízení změřit** (potřeba jen jízda, nic nového):
> ```bash
> dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- heading Records/<zaznam>.rec
> ```
> Report umí i **běh bez ground truth** a tiskne pak `IMU yaw − GPS kurz`: střední hodnotu, šum
> a kolik vzorků je potřeba na 3σ. Pravdu k tomu nikdo nepotřebuje — stačí, že jsou to dvě
> nezávislé absolutní referencie.
>
> **Podmínky pořízení:** jízda nad prahem rychlosti (kurz nad zemí při stání neexistuje), a nejlépe
> **smyčka nebo aspoň dva různé kurzy**. Bias magnetometru je vázaný na **tělo** robota, takže se
> s kurzem **otáčí**; deklinace nebo chyba v převodu rámců je vázaná na **svět**, takže nerotuje.
> Bez otočení se to nerozliší. *(Tentýž rozlišovací znak už doc/map-correlation-localization.md
> používá na „bias z montáže kamer vs. posun mapy".)*
>
> **Co s výsledkem:** rozpor řádu stupňů, který rotuje s kurzem ⇒ bias kompasu je skutečný a úkol má
> smysl. Rozpor pod ~0,5° ⇒ zavřít jako nepotřebné a `GPS/heading` nechat jen jako druhou referenci
> pro případ výpadku magnetometru.
>
> **Ten přístroj je ověřený proti známé odpovědi** (`--nogt` nad simulačním záznamem, kde pravda
> existuje, ale zahodí se): cesta pro HW ohlásila střední rozpor **2,78°** proti vnucenému biasu
> **2,99°**, tedy shoda do 0,2°, a odhadla potřebu 29 vzorků = 5,8 s jízdy. Bez toho by na zařízení
> běžel kód, který nikdy nikdo neproměřil.

### Otevřený úkol: Pitch/Roll patří do stavu EKF (2026-08-11)

`RobotState.Roll`/`Pitch` dnes **nejsou součástí stavu filtru** — doplňuje je
[`ControlLoop`](../Src/ARBot.Common/Runtime/ControlLoop.cs) z **posledního IMU**, které proteklo jeho
`Consume` (`lastImu`). Dva problémy s tím:

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

### Otevřený úkol: diagnostika EKF do streamu a záznamu (2026-08-13)

Chování filtru dnes nejde zpětně prohlédnout ze záznamu — `AsyncFusionEngine.Diagnostics()`
vrací per-měření `Source / Time / Nis / Accepted`, ale nikam se to neemituje, takže je to vidět
jen za běhu v debuggeru. Když robot v simulaci „poskakoval", nedalo se odlišit, jestli je to
šum GPS, nebo gating zahazující měření.

**Zpráva už existuje a je připravená:** [`MeasurementDiagMsg`](../Src/ARBot.Common/Logs/MeasurementDiagMsg.cs)
má přesně potřebná pole (`Source`, `Z`, `DiagR`, `Nis`, `Accepted`, `TimeStamp`) a je
**zaregistrovaná v katalogu** (`MessageCatalog`), takže by se rovnou serializovala i přehrála
ve View. Jen ji nikdo neplní. (`EKFStepMsg` vedle ní je něco jiného — dump celých matic
z předchozí generace, na průběžný záznam příliš těžký.)

**Pozor na jeden detail, který určuje, kde se emituje:** NIS při `Enqueue` **ještě neexistuje**.
Měření se jen zařadí a buffer se označí za špinavý; `Nis`/`Accepted` plní až `EnsureValid()`
a při doražení opožděného měření se uzly **přepočítají**, takže se NIS může zpětně změnit.
Emitovat při vložení by tedy zapisovalo hodnotu, která ještě není spočtená. Nabízí se odběr
až usazených hodnot — např. `FusionProcessor` si periodicky (~10 Hz, bezpečně pod oknem
historie 1 s) přečte `Diagnostics()` a pošle záznamy novější než poslední odeslaný.

Doplnit bude potřeba `Z` a `DiagR` do `AsyncFusionEngine.MeasurementInfo` (dnes nese jen
`Source/Time/Nis/Accepted`).

Objem: ~155 měření/s (IMU 100 Hz, odometrie 50 Hz, GPS 5 Hz) ≈ 12 kB/s — proti obrazům z kamer
(~1,8 GB/min) zanedbatelné. Alternativa je periodický souhrn po zdrojích (počet, podíl přijatých,
průměrný a maximální NIS), ale ten neumožní dohledat konkrétní zahozené měření.

K tomu patří i dokovatelný dokument, který to zobrazí. **Nerozhodnuto, neimplementováno.**

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
