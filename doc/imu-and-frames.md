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

### ⚠️ Kurz z VN100 byl 6. 9. 2026 o **−59°** vedle (naměřeno)

Z pozorování autora „kurz robotu nesouhlasí s jeho reálným kurzem podle mapy“. Měřeno nad
`records/test/20260906-082403.rec` příkazem `ARBot.Analyze heading` (včetně bloků, které kvůli
tomu vznikly — viz [record-replay.md](record-replay.md#heading-nesedí-absolutní-reference-kurzu)).

**Co je změřeno:**

- **`IMU yaw − GPS kurz` = −59,2°** (688 vzorků nad 0,3 m/s, směrodatná odchylka jednoho
  vzorku 9,7°). Ve fyzikálním směru: kompas ukazuje o 59° **po směru hodinových ručiček**
  proti skutečnému směru jízdy.
- **Je to konstantní posun, ne otočené znaménko.** Model `yaw = +kurz + a` má zbytkový rozptyl
  14,6°, `yaw = −kurz + a` 27,0°. Robot jel tam a zpět: při kurzu ≈−157° vyšel rozpor −60,3°,
  při kurzu ≈+22° −58,3° — tedy **stejně před otáčkou i po ní**.
- **Chyba není v GPS.** Dopplerův kurz sedí na směr posunu polohy na +2,9° (33 oken po 1,5 m);
  to jsou dvě nezávislé větve řešení v přijímači.
- **Chyba není v našem kódu.** Mezi oběma záznamy se v cestě VN100 (driver, `IMUState`,
  `Conversions`) změnilo jen přidání `Name` (commit `2316de1`); yaw se počítá stejně a týž
  analyzátor čte oba záznamy.
- **Chyba není ani v gyru / sledování otáčení.** Kdyby yaw špatně sledoval rotaci, rozpor by se
  otáčkou o 180° změnil; nezměnil se. Špatně je **absolutní reference kurzu**.
- **Předtím přitom jel správně:** týž senzor v `records/test/20260902-230138.rec` (2. 9. 2026)
  měl `IMU yaw − GPS kurz` = **−0,25° ± 4,3°**. Není to tedy trvalá vlastnost montáže ani
  převodu rámců — mezi 2. a 6. 9. se něco změnilo **na senzoru nebo na robotu**.
- **Samotné magnetické pole se přitom nezměnilo:** `|B|` p50 0,446 → 0,436 G a sklon p50
  59,6° → 61,6° mezi oběma záznamy, tedy stejně. Kurz přepočtený **z pole** je proti GPS
  +12,7° (2. 9.) a −5,6° (6. 9.) — v obou případech řádově deklinace (~+5°), ne −59°.
  Rozdíl **kurz z pole − yaw ze senzoru** naopak vyskočil z +18,8° na +58,4°.
  → **Magnetometr měří skoro totéž co dřív, ale vlastní atitudové řešení VN100 se od něj
  odtáhlo o ~59°.**
- **Ten přepočet je ověřený proti zdravému senzoru:** v delším záznamu z téhož večera
  (`records/20260902-222601.rec`, **41 916 vzorků**) je `kurz z pole − yaw` **+4,25° ± 8,2°**,
  tedy řádově deklinace. Když senzor funguje, sedí jeho yaw na jeho vlastní pole — takže
  +58,4° ze 6. 9. není artefakt měřidla. *(Kurz z GPS je v tomhle záznamu nepoužitelný — robot
  se ploužil 0,33 m/s a sd rozporu je 131°.)*

**Co to znamená pro robota:** fúze kurz **neváží, přebírá ho** — `odhad − IMU yaw` je
**−0,01° ± 0,21°** (3 237 vzorků), zatímco `odhad − GPS kurz` je −59,1°. Chyba kompasu jde
tedy 1:1 do `RobotState.Theta`, tedy do mapového pohledu, do mrkve i do korelace s mapou. Sedí to
s poměrem „kompas přehlasuje GPS kurz ~4 000:1“ z [ekf-fusion.md](ekf-fusion.md).

**Co ještě není rozhodnuté** (a nesmí se to tvrdit):

- **Měkké železo není vyloučené.** Záznam pokrývá jen dva směry jízdy o 180°, a chyba
  z měkkého železa má **dvě** periody na otáčku — v protisměru tedy vypadá stejně jako
  konstantní posun. Vyloučené je **tvrdé** železo (jedna perioda → v protisměru opačné
  znaménko). Rozhodne **projetá smyčka** přes všech osm oktantů.
- **Proč se řešení VN odtáhlo od pole, není prokázáno.** Kandidáti k ověření **read-only**
  přes `VNRRG` (žádný zápis do flash): registr **35** (VPE Basic Control — heading mode
  Absolute / Relative / Indoor; v Relative je yaw vedený od zapnutí a má právě takový
  „náhodný“ konstantní offset), **44** (Magnetometer Calibration Control — HSI mode; adaptivní
  kalibrace může zkonvergovat špatně), **23** (magnetická kompenzace) a **26** (reference frame
  rotation). Porovnat s exportem `vn100-2026-7-8-nastavei z arbot2.sencfg` v rootu repa.
- **Vedlejší nález, který tím není vysvětlen:** medián `|a|` je **10,51 m/s²** v obou
  záznamech, tedy **o 7,2 % nad *g*** (9,807). Na kurz to přímo nemá vliv (svislice je směr,
  ne velikost), ale je to další ukazatel, že se na kalibraci senzoru nelze spolehnout.
  Podobně sklon pole 61–62° proti tabulkovým ~66° v ČR.

**Další krok:** projet **smyčku** (všech osm oktantů) a pustit `ARBot.Analyze heading` — report
pak vydá i harmonický rozklad a rozliší konstantní posun od železa. Do té doby nemá smysl
nic „opravovat“ softwarově: offsetem v kódu by se zabetonovala hodnota, o které se ví, že se
mezi 2. a 6. 9. sama změnila.

#### Co o sobě řekl sám senzor (`ARBot.Analyze vn100`, 6. 9. 2026)

Záznam nese i to, co senzor tvrdí o vlastní přesnosti, a jeho gyroskop — takže se dá zeptat
dál, pořád bez sáhnutí na železo. Popis metody:
[record-replay.md](record-replay.md#vn100-prověření-samotného-senzoru-ze-záznamu).

**1. Senzor si je jistý — a to je sám o sobě nález.** `YprU` (jeho vlastní 1σ pro yaw):

| záznam | `YprU` yaw p50 | skutečná chyba kurzu |
|---|---|---|
| `20260902-222601` | **0,053°** | ~0 (yaw sedí na pole na +4,2°) |
| `20260902-225743` | 0,106° | — |
| `20260902-230138` | 0,139° | −0,25° proti GPS |
| `20260906-082403` | **0,233°** (max 0,671°) | **−59,2° proti GPS** |

Senzor tedy zhoršení **zaznamenal** (4× větší σ), ale hlásí pořád **čtvrt stupně**, když je
o 59° vedle. A protože `DefaultMeasurementMapper` bere `imu.OrientationUncertainty?.X`
**přímo jako σ měření `IMU/heading`**, je to zároveň vysvětlení, proč fúze kurz přebírá
1:1: senzor si řekl o důvěru 0,23° a dostal ji.

**2. Zpětná vazba od magnetometru je slabá i tehdy, když je.** Zesílení `K` z regrese
`(Δyaw/Δt − ω_z)` na `(kurz z pole − yaw)`:

- `20260902-225743`: **K = +0,0021 ± 0,00044 1/s (4,8σ)**, tedy časová konstanta **~480 s**.
- `20260906-082403`: **K = +0,00018 ± 0,00035 1/s (0,5σ)**, tedy slučitelné s nulou.

⚠️ **Nesmí se z toho udělat „zpětná vazba se vypnula“** — ta dvě čísla se liší jen asi na
**2σ** a šum ve vysvětlující proměnné (kurz z pole je sám zašuměný) tlačí `K` **systematicky
k nule**. Co z toho **platí pevně**: i v tom lepším případě je časová konstanta **řádu minut**,
takže **kurz, se kterým senzor naběhne, ovládá celý pětiminutový běh**. To sedí s tím, co
`heading` vidí model-free: rozpor proti GPS byl po minutách −54 / −61 / −61 / −49 / −59 —
**neklesal**, i když měl 59° na to, aby ho pole stáhlo.

**3. Klidový bias gyra** (rychlost z GPS pod 5 cm/s): −35 až +25 °/h napříč záznamy.
Řádově desítky °/h — na 59° za 5 minut to nestačí ani zdaleka, takže **chyba není nabraná
za běhu, ale už při náběhu**. *(Číslo 270 °/h z `20260902-230138` je z 25 vzorků a neváží.)*

**Co z toho plyne pro další krok.** Chování odpovídá senzoru, který kurz vede převážně
integrací gyra od hodnoty při zapnutí a pole do něj míchá jen velmi pomalu — tedy přesně to,
co dělá VPE s nízkou šířkou pásma nebo heading mode jiný než *Absolute*. **Prokázat to ze
záznamu už nejde**; je na to potřeba přečíst registry na připojeném senzoru (read-only `VNRRG`,
registry **35**, **44**, **23**, **26**).

### ✅ Příčina nalezena na živém senzoru (6. 9. 2026): dva registry nesedí s exportem

Registry přečtené read-only na robotu (`deploy/vnprobe.sh`, služba `arbot` na tu chvíli
zastavená) proti referenčnímu exportu `vn100-2026-7-8-nastavei z arbot2.sencfg` v kořeni repa.
**Ze čtrnácti přečtených registrů se liší právě dva — a oba jsou kurzově kritické:**

| registr | export (známý dobrý stav) | senzor 6. 9. 2026 | |
|---|---|---|---|
| **35** VPE Basic Control | `1,` **`0 (Absolute)`** `,1,1` | `1,` **`1`** `,1,1` | ⛔ **Relative** |
| **23** Magnetometer Compensation | matice s diagonálou `1,222 / 1,175 / 1,081`, bias `(−0,274; −0,058; 0,076)` | jednotková matice, bias `(0;0;0)` | ⛔ **vymazána** |
| 26 Reference Frame Rotation | `diag(−1; 1; −1)` | `-1,0,0,0,1,0,0,0,-1` | ✅ sedí |
| 21 referenční vektory | `(0,234; 0; 0,4212) (0; 0; −9,79375)` | totéž | ✅ sedí |
| 36 VPE mag tuning | `(4;4;4)(5;5;5)(5,5;5,5;5,5)` | totéž | ✅ sedí |
| 44 Mag Calib Control | `0 (Off) 1 (NoOnboard) 5` | `0,1,5` | ✅ sedí |

**1. Heading mode je `Relative`, má být `Absolute`.** Hodnoty potvrzuje SDK v repu
(`vectornav-sdk-1-2-0/cpp/include/vectornav/Interface/Registers.hpp`:
`Absolute = 0, Relative = 1, Indoor = 2`) i sám export, který nulu popisuje jako `(Absolute)`.
V *Relative* není yaw kurz k magnetickému severu, ale k tomu, **kde senzor naběhl** — což přesně
vysvětluje všechno naměřené: **konstantní** posun (nezávislý na kurzu), který se **nemění během
jízdy**, ale **liší se mezi sezeními**, a nulová zpětná vazba od magnetometru.

**2. Kalibrace magnetometru (tvrdé/měkké železo) je pryč.** Export nesl matici s diagonálou
1,08–1,22 a bias `−0,274 G` — to není kosmetika, ten bias je **víc než polovina zemského pole**.
Teď je tam jednotková matice a nula. Potvrzuje to i sám senzor: registr **27** (kompenzované pole)
a **54** (syrové) hlásí **téměř totéž** — `(0,1263; 0,1425; 0,3520)` proti
`(0,1263; 0,1455; 0,3507)`, takže se **nekompenzuje nic**. A onboard HSI je vypnuté
(reg 44 `Off`), takže to nemá co nahradit. Odtud i to, že změřené `|B| = 0,400 G` je **17 %
pod** referencí, kterou má senzor v registru 21 (`0,482 G`), a odtud i nález ze záznamu, že
kurz přepočtený z pole má vlastní chybu závislou na kurzu (±~27°).

⚠️ **Jedna věc tomu na první pohled odporuje a patří sem, ne pod koberec:** *v tu chvíli*
yaw na pole seděl. Registr 27 dal yaw `−51,86°` a z jeho vlastního pole a zrychlení vychází
magnetický azimut `−51,9°` — shoda na 0,2°. V *Relative* módu ale VN kurz při náběhu
z magnetometru **inicializuje** a teprve pak ho vede gyrem; robot od zapnutí **stál**, takže
za tu dobu nemá kde nabrat rozdíl (klidový bias gyra je desítky °/h). Rozejde se to až
**otáčením** — a přesně to se stalo v tom záznamu. Nemá tedy smysl brát „teď to sedí" jako
protidůkaz; **rozhodne až měření po projeté smyčce.**

**Co tím padá:** domněnka, že se něco změnilo v magnetickém prostředí nebo v našem kódu.
Změnila se **konfigurace senzoru** — oba registry jsou na továrních hodnotách, zatímco zbytek
profilu (rámce, tuning, referenční vektory) je pořád ten z exportu. Vypadá to tedy na **částečný
factory reset**, ale **čím a kdy, se z toho určit nedá**; jen že mezi 2. a 6. 9.

**Vedlejší nález, který tím vysvětlený NENÍ:** `|a| = 10,519 m/s²` proti gravitační referenci
`−9,79375` z registru 21, tedy **+7,4 %**. Kompenzace zrychlení (reg 25) je jednotková
**i v exportu**, takže to není ztracená kalibrace, ale stav, který tu byl už předtím. Na kurz
přímo vliv nemá (svislice je směr, ne velikost), ale pitch/roll a detekce klidu na tom stojí.

#### Co s tím — a proč to diagnostika sama neopravila

Oprava je **zápis** do senzoru (`VNWRG,35` a obnova `VNWRG,23`) plus **uložení do flash**
(`VNWNV`), aby přežila vypnutí. To je **záměrně mimo `deploy/vnprobe.sh`**, který posílá jen
`VNRRG`: konfigurace železa se mění vědomě a ručně, ne vedlejším účinkem diagnostiky. Navíc:

- **Hodnoty z exportu jsou z ARBot2 a je jim rok** (8. 7. 2026, sériové číslo 100016133).
  Bias `−0,274 G` platí pro **tehdejší** železo kolem senzoru; na dnešním robotu může být jiný.
  Obnovit je je rozumný **první krok** (zjevně lepší než nula), ale správně se má kalibrace
  **změřit znovu** — otáčením robotu, tedy touž smyčkou, kterou stejně chce `heading`.
- **Dokud je heading mode `Relative`, nemá smysl měřit nic dalšího** — yaw není absolutní kurz,
  takže každé měření „biasu kompasu" musí vyjít jako náhodné číslo z posledního zapnutí.
  Gate v [ekf-fusion.md](ekf-fusion.md) („chyby senzorů jako stavy EKF") tím zůstává otevřený
  a je teď jasnější proč.
- **Až po opravení obou registrů** projet smyčku a pustit `ARBot.Analyze heading` a `vn100` —
  teprve tehdy říkají to, k čemu byly napsané.

#### ✅ Opraveno a zapsáno do flash (6. 9. 2026, na pokyn autora)

Autor doplnil, že **kalibraci magnetometru vymazal záměrně** — VN100 mimo robota hlásila špatný
směr, což sedí: kalibrace popisuje železo *kolem senzoru na robotu*, takže mimo něj je špatně.
Na heading mode si nevzpomněl. Obojí obnoveno skriptem `deploy/vnrestore.sh` (ten na rozdíl od
`vnprobe.sh` **zapisuje**) a uloženo do flash (`VNWNV`):

| | zapsáno | zpětné čtení |
|---|---|---|
| reg 35 | `1,0,1,1` (Absolute) | `$VNRRG,35,1,0,1,1` ✅ |
| reg 23 | matice + bias z exportu | `$VNRRG,23,1.222,0.005,…,0.076` ✅ |

**Že heading mode opravdu začal fungovat, je vidět na chování, ne jen na zpětném čtení:** kurz
se po zápisu **rozjel** z azimutu −51° přes −76 / −96 / −117 a **za ~100 s se usadil na −128,5°**,
kde zůstal i po restartu procesu. V *Relative* by se nehnul — VPE ho teď táhne k magnetickému poli.
Ta doba usazení je zároveň praktický důsledek: **po zapnutí (a po každé velké magnetické změně)
potřebuje kurz řádově dvě minuty, než se srovná** — sedí to s časovou konstantou „řádu minut"
naměřenou ze záznamu.

⚠️ **Co tím ale prokázané NENÍ: že je ta kalibrace správná.** Zapnutím se kurz otočil o ~78° a
stojící robot nemá proti čemu to rozhodnout. Čísla po kompenzaci: `|B| = 0,549 G` proti referenci
`0,482 G` v registru 21 (**+14 %**) a sklon **55,6°** proti 60,9°; bez kompenzace to bylo 0,400 G
(−17 %) a 63,3°. Ani jedno nesedí — jenže **měřeno uvnitř budovy**, kde pole deformuje sama stavba,
takže tady se to rozhodnout nedá. Kalibrace je navíc **rok stará a z ARBot2**.
**Rozhodne až venku:** projet smyčku, nahrát záznam a pustit `ARBot.Analyze heading` a `vn100`.
Kdyby kurz venku neseděl, je další krok **změřit kalibraci znovu** (otáčením robotu), ne vracet
tuhle.

⚠️ **Trvalost zápisu je ověřená jen zpětným čtením** — registr se čte z RAM, ne z flash. Skutečný
test je **vypnout a zapnout robota** a přečíst reg 35 znovu (`deploy/vnprobe.sh`).

⚠️ **`VNWNV` ukládá celou sadu registrů, jak je právě v RAM**, tedy i to, co do ní zapsal driver
při startu (`ADOR=0` v reg 06 a binární výstup 1 v reg 75). Je to neškodné — driver si je stejně
píše při každém startu — ale flash se tím v těchto položkách rozešla s exportem.

### ⚠️ Deklinace se NEZAPOČÍTÁVÁ nikde — kurz z VN100 je magnetický (2026-09-06)

Přečteno na živém senzoru (`deploy/vnprobe.sh`):

```
$VNRRG,21,0.234,0,0.4212,0,0,-9.79375     <- referenční vektor pole (NED)
$VNRRG,83,0,0,0,0,1000,0.000,+0,+0,+0     <- Reference Vector Configuration
```

- **Registr 21** je referenční pole, ke kterému VPE kurz zarovnává: `N = 0,234`,
  **`E = 0`**, `D = 0,4212` (z toho `|B| = 0,482 G`, sklon 60,9°). **Nulová východní složka
  znamená, že v referenci není deklinace** → hlášený yaw je azimut k **magnetickému** severu.
- **Registr 83**: `useMagModel = 0`, `useGravityModel = 0` — **model pole (WMM) je vypnutý**,
  takže registr 21 nikdo nepřepočítává a deklinace se tam nedostane ani dodatečně.

**A náš kód ji nepřidává taky:** `DefaultMeasurementMapper` bere `ypr.Yaw` tak, jak je, a
`Profile.DeklinaceDeg` je nula, která **se v ostrém kódu nepoužívá nikde** (jen ve starém
`SimpleModel` a v zakomentovaném `EKFModel2`) — tedy past: parametr, který vypadá, že něco dělá.

Skutečný azimut = magnetický + deklinace, u nás řádově **+5° na východ** (přesnou hodnotu pro
dané místo a datum dá WMM kalkulátor).

#### Připraveno: `IMagneticModel.SetModelParams(LLA)`

Zapnout model v senzoru umí obě verze driveru — `VN100IMU` i `VN100IMUBinary` implementují
`IMagneticModel` a sestavení příkazu (registr 83) je ve **společném `VnCommands`**, aby se ty
dva drivery nerozešly. Kryje to **9 testů** (`VnCommandsTest`), které porovnávají výsledný
řetězec znak po znaku — bez UARTu a bez hardwaru.

⚠️ **Původní `SetModelParams` v ASCII driveru NIKDY NEMOHL FUNGOVAT** a nikdo si toho nevšiml,
protože ho nikdo nevolal. Tři nezávislé chyby, každá má teď svůj test:

| chyba | důsledek |
|---|---|
| posílal `$VNRRG,83,…` | `VNRRG` je **čtecí** příkaz — nic nenastavil |
| formát `{0:N3}` | vkládá **oddělovač tisíců**, takže výška 1234,5 m → `1,234.500`, tedy čárka doprostřed čárkami odděleného příkazu |
| posílal `lla.Latitude` přímo | `LLA` je v **radiánech**, VN čeká **stupně** → poloha někde u rovníku |

**Nevolá to zatím nikdo, a je to záměr:** model pole potřebuje **polohu**, kterou driver při
startu nezná — smysl to má až po prvním kvalitním fixu GPS, takže volání si musí zařídit runtime.
A zápis do registru je **nestálý**; trvalé uložení (`VNWNV`) je vědomý ruční krok
(`deploy/vnrestore.sh`), ne něco, co má driver dělat při každém startu.

**Než se to zapne, změř smyčku.** Deklinace je vázaná na **svět**, zbytkové tvrdé železo na
**tělo** robota — a `ARBot.Analyze heading` je právě od toho, aby je rozlišil. Konstantní člen,
který vytiskne, **je deklinace plus zbytková chyba dohromady**; nastavovat model naslepo znamená
zabetonovat do senzoru číslo, které nikdo neproměřil.

#### ✅ Model pole se od 8. 9. 2026 nastavuje sám (`magmodel=`, výchozí `true`)

`MagModelInit` (`ARBot.Runtime`) zavolá `SetModelParams` **jednorázově po prvním fixu, který
projde branou kvality** — a to tímtéž verdiktem `DefaultMeasurementMapper.PositionRejectReason`,
jaký používá fúze a webový náhled (druhá brána by se s tou první rozešla).

⚠️ **Tím se otáčí rozhodnutí odstavce výše** („nenastavovat, dokud se nezměří smyčka").
Důvody, proč to je vědomé a ne přehlédnutí:

1. **Smyčka je projetá** (7. 9. 2026, `20260907-170728.rec`) a rozebraná: chyba kurzu je
   **27,2° / 25,2° harmonické z železa na těle**, tedy o řád víc než deklinace. Předpoklad toho
   odstavce je tedy splněný a jeho závěr byl „deklinace je malá ryba" — ne „nikdy ji nezapínat".
2. **Nezabetonovává se nic neproměřeného.** Model si deklinaci **dopočítá z WMM** podle polohy
   a data; není to číslo, které bychom vymysleli. A `VNWNV` se **záměrně neposílá**, takže zápis
   je nestálý a nastaví se při každém běhu podle aktuální polohy a data — pravidlo „uložení do
   flash je vědomý ruční krok" zůstává nedotčené.
3. **Přibyl druhý, nezávislý důvod, který ten odstavec neznal:** registr 21 znamená sklon
   **60,9°**, ačkoli pro ČR je ~**65,7°**, a **VPE porovnává měřený sklon proti té referenci**.
   Kalibrace magnetometru udělá `|B|` a sklon *konstantní*, ale nezmění, že ta konstanta je o 5°
   mimo — takže i po perfektní kalibraci může VPE magnetometr dál částečně dusit. To je
   podezření na vadu „VPE se táhne za vlastním polem 206 s" a je to důvod zapnout model
   **před** terénní kalibrací, ne po ní.

⚠️ **Bod 3 je hypotéza, ne zjištění.** Jak silně VPE reaguje na *konstantní* odchylku sklonu,
z dokumentace vyčíst nejde a změřit se to dá jedině na senzoru. **Celé to na HW neběželo**;
`magmodel=false` vrací chování do 8. 9. 2026, takže A/B je jeden přepínač.

⚠️ Po nastavení se kurz **skokem změní o deklinaci** a VPE se na novou referenci dotahuje
~100–170 s. Proto se to dělá při prvním dobrém fixu, ne až za jízdy.

**Co změřit na zařízení:** `IMU yaw − GPS kurz` se má zlepšit **přesně o deklinaci** (~+5°).
Když se zlepší o jiné číslo, byly ty dvě chyby smíchané — a právě to ten odstavec výše hlídal.

### ⚠️ Projetá smyčka venku (7. 9. 2026): kurz je pořád vedle — zbývá **železo na robotu**

Tohle je to měření, na které čekaly dva otevřené závěry z 6. 9.: „absolutní přesnost prokázaná
není" a „novou kalibraci změřit otáčením robotu". Záznam `records/test/20260907-170728.rec`
(452 s FreeRunu venku, `Absolute` + **vymazaná** kompenzace magnetometru, ujeto ~105 m se zatáčkami,
rozptyl kurzu 146° kruhové sd), nástroje `ARBot.Analyze heading --nogt` a `vn100`.

**Kurz je vedle, a GPS to není.** `IMU yaw − GPS kurz` má p50 **−24,0°**, střed −18,1°,
**sd 18,6°** a rozsah −57,7 … +28,6°. Že chybuje IMU a ne GPS, rozhoduje **třetí, nezávislá
cesta** — směr, kterým se skutečně posunula poloha:

| dvojice | rozdíl |
|---|---|
| GPS Doppler − směr posunu polohy | **0,31° ± 6,19°** ✅ |
| IMU yaw − směr posunu polohy | −13,53° ± 20,42° |
| odhad fúze − IMU yaw | **−0,01° ± 0,06°** |

Poslední řádek je ten podstatný pro chování robota: fúze kurz z kompasu **nevažuje, přebírá**,
takže ta chyba jde 1:1 do mapy i do mrkve. Senzor si přitom hlásí `YprU` (yaw 1σ) p50 **0,151°** —
tedy je **~120× přesvědčenější, než jaká je jeho skutečná chyba**, a `DefaultMeasurementMapper`
si to bere jako σ měření `IMU/heading`. O tu slepou důvěru si senzor řekl sám.

**Vada je v POLI, a je vázaná na tělo robota.** Rozpor závisí na kurzu, což je podpis železa:

| model / veličina | hodnota | mělo by být |
|---|---|---|
| 1. harmonická (**tvrdé železo**) | **27,2°** | 0 |
| 2. harmonická (**měkké železo**) | **25,2°** | 0 |
| `|B|` (p50 / rozsah) | 0,455 G / **0,363–0,511** | ~0,49 G a **konstantní** |
| sklon pole (p50 / rozsah) | 57,2° / **46–83°** | ~66° a konstantní |
| kurz z pole − kurz z GPS | −3,5° ± **29,6°** | ~+5° (deklinace) |

Gyro je v pořádku: klidový bias **−4,6 °/h**, tedy v katalogové in-run stabilitě. Takže je to
magnetometr, ne inerciálka.

✅ **A kalibrace to spravit může — protože motory to skoro nejsou.** To byla reálná obava:
rušení, které se mění s proudem, není v tělesovém rámci konstantní, takže ho otáčením robotu
nezměříš ani neodečteš. Změřeno (`ARBot.Analyze vn100`, blok 4) proti proudu motorů ze
`MotorStateBase`:

```
|B| na proudu:      −0,00258 ± 0,00010 G/A   (25,7 σ od nuly, rozsah proudu 13,1 A)
|B| jízda − stání:  −0,015 G                 (mediány 0,445 vs 0,460)
```

Závislost **je statisticky jistá, ale malá**: 0,015 G z celkového rozpětí `|B|` 0,148 G, tedy
řádově desetina. Zbytek je **statické** železo — a to je přesně to, co hard/soft-iron kalibrace
umí odečíst. **Další krok je tedy změřit novou kalibraci otáčením robotu**, ne stínění ani
přesun senzoru.

⚠️ **Druhá, oddělená vada: VPE se táhne za vlastním polem řádově minuty.** Zesílení zpětné vazby
`K = 0,00485 ± 0,00074 1/s`, tedy **časová konstanta 206 s**. Ve výsledku `kurz z pole − yaw`
kolísá po minutách +10 / +2 / −10 / +1,5 / **+30 / +46 / +37** / +5°, takže po každé zatáčce nebo
magnetické změně je yaw desítky stupňů vedle **i proti svému vlastnímu magnetometru**. To
kalibrace neopraví; sedí to s poznámkou „po zapnutí počítej s ~2 minutami" z 6. 9., jen to zjevně
platí i **za jízdy**. Dokud je konstanta takhle dlouhá, je krátkodobá σ 0,15° nesmysl dvakrát.

**Praktický důsledek pro řízení**: dokud je kurz vedle, nemá smysl ladit rychlostní obálku
lokálního plánovače — grid i mrkev se kreslí tímhle kurzem. Viz nález ze stejného záznamu
v [occupancy-and-local-planning.md](occupancy-and-local-planning.md).

### Konfigurace senzoru

Konfigurace VN100 (včetně reference frame rotation a binárního výstupu) je uložena
**ve flash senzoru** a přežívá vypnutí — v kódu se persistentně nezapisuje. Export
nastavení z VectorNav Control Center je v rootu repa
(`vn100-2026-7-8-nastavei z arbot2.sencfg`). Diagnostika jde dělat read-only přes
`VNRRG` (žádný zápis/flash) — např. registry 1 (model), 6/7 (async), 8/9/27 (YPR/qtn/YMR),
26 (reference frame rotation).
