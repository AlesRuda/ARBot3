# Kalibrace magnetometru VN100 — rotační měření misí a zápis do senzoru

> **Pro agentní pracovníky:** plán se plní **task po tasku**, kroky mají checkboxy (`- [ ]`).
> Každý task končí zeleným buildem a testy pod `x64`. **Nekomitovat bez pokynu autora**
> (viz [CLAUDE.md](../CLAUDE.md)).

**Cíl:** Dát robotu schopnost **změřit si vlastní magnetickou kalibraci otáčením na místě**
a po lidském potvrzení si ji **zapsat do senzoru** — celé z telefonu, bez notebooku v poli.
Tím opravit kurz, který je dnes desítky stupňů vedle.

**Proč:** kurz z VN100 je naměřeně nepoužitelný a jde 1:1 do mapy i do mrkve. Na smyčce
projeté 7. 9. 2026 (`20260907-170728.rec`) je `IMU yaw − GPS kurz` p50 **−24,0°**, sd **18,6°**,
rozsah −58 … +29° — a chybuje **IMU**, ne GPS, protože `Doppler − směr posunu polohy` je
**0,31° ± 6,19°** (třetí nezávislá cesta). Fúze kurz **neváží, přebírá**
(`odhad − IMU yaw` = −0,01° ± 0,06°), a senzor si přitom hlásí `YprU` **0,151°**, tedy je
~120× přesvědčenější, než jaká je jeho chyba. Podrobnosti a historie nálezu:
[imu-and-frames.md](imu-and-frames.md).

Vada je v **poli vázaném na tělo robota**: 1. harmonická (tvrdé železo) **27,2°**,
2. harmonická (měkké) **25,2°**, `|B|` 0,363–0,511 G a sklon 46–83°, ačkoli obojí má být
konstanta. Gyro je čisté (klidový bias −4,6 °/h). **Kalibrace to spravit může, protože motory
to skoro nejsou**: pole závisí na proudu jen −0,00258 ± 0,00010 G/A a `|B|` jízda − stání je
−0,015 G, tedy desetina rozpětí 0,148 G. Statické rušení se odečíst dá; na proudu závislé by
se otáčením změřit nedalo.

## Dvě oddělené vady a fázování

1. **Pole vázané na tělo robota** (harmoniky výše) — **tohle řeší fáze 1**.
2. **VPE se táhne za vlastním polem 206 s** (`K = 0,00485 ± 0,00074 1/s`): `kurz z pole − yaw`
   jde po minutách +10 / +2 / −10 / +1,5 / **+30 / +46 / +37** / +5°, takže po zatáčce je yaw
   desítky stupňů vedle **i proti svému vlastnímu magnetometru**. Kalibrace to neopraví —
   **fáze 2**.

**Pořadí je kalibrace první**, protože vada 2 může být **následek** vady 1: adaptivní ladění VPE
(registr 36 `AdaptiveTuning (5,5,5)`, `AdaptiveFiltering (5,5; 5,5; 5,5)`) magnetometr **záměrně**
utlumí, když se měřené `|B|` a sklon rozejdou s referenčním vektorem — a to je přesně to, co
dnešní pole dělá. Fáze 2 proto **začíná přeměřením** bloku 3 v `ARBot.Analyze vn100`; když
zesílení `K` po kalibraci vyskočí samo, **fáze 2 se ruší jako neexistující vada** a zapíše se to.

## Rozhodnutí autora (8. 9. 2026)

1. **`mission=magcal`, ne parametr `magcal=`.** Původní návrh měl booleovský přepínač vedle
   selektoru misí — přesně to, proti čemu je pravidlo v [CLAUDE.md](../CLAUDE.md) („mise se
   vylučují, takže se nevybírají booleovskými přepínači"). Jako mise to navíc **ubírá** práci:
   `IMissionStatus.PhaseText` **je** ten živý ukazatel pokrytí (stránka stav mise už kreslí),
   `IRegulatorHolder.Regulator = null` dá konstrukční záruku, že se robot nerozjede, a záznam
   se rozjede volbou mise, takže se nemění headless ani se nezavádí nový parametr.
2. **Celá procedura se automatizuje do stránky** — ⚠️ **a ohýbá tím vědomě nakreslenou čáru
   projektu.** Hlavičky `deploy/vnprobe.sh`, `deploy/vnrestore.sh` i `VnCommands` říkají, že
   „konfigurace senzoru se mění vědomě a ručně, ne vedlejším účinkem nějakého měření" a že
   `VNWNV` je „vědomý ruční krok". **Záměr té čáry ale byl „žádný zápis bez rozhodnutí člověka"
   a ten zůstává:** zápis se děje jen na ťuknutí na tlačítko **pod drženým nouzovým zastavením**,
   což je silnější gate než ssh session. Mění se mechanismus, ne pravidlo. Pojistka proti erozi:
   šev `IMagCalControl` je **úzký** — umí zapsat registry 23 a 44 a přečíst 21/23/44/47, volá ho
   **jen** `MagCalMission`, a **není** to obecné „zapiš jakýkoli registr". Obecné API by tu čáru
   smazalo. Viz [decisions.md](decisions.md).
   Důvod, proč to za tu cenu stojí: skriptová varianta znamená **notebook v poli**, a to je
   přesně ta věc, kvůli které se měření neudělá.
3. **Otáčí se rukou, ne vlastní silou.** Robot váží 10 kg. Rovnoměrnost rotace proložení nezajímá
   (jde jen o pokrytí azimutů), motory stojí, a nový kód v runtime by nepřinesl nic měřitelného.
4. **Náklony jsou součást měření, ne přepych** — viz „Proč nestačí rotace na rovině" níž.

## Globální omezení

- Jazyk: čeština (komentáře, dokumentace, výpisy).
- Build a testy **pod `x64`** (`dotnet test <proj> -p:Platform=x64`), na zařízení `OrangePI`.
- Diagnostika poruch do **`Trace`**, ne `Debug`.
- Čas přes **`TimeBase.Now`**.
- Doménový objekt si vyrábí zprávu **`ToLogMessage()`**; `Message` zůstává pasivní DTO.
- **Nemazat starou implementaci**, dokud novou nepotvrdí testy.

## Jádro: `MagCalFit`

`ARBot.Common/Calibration/MagCalFit.cs` — čisté proložení bez HW a bez streamu, testovatelné
proti syntetickým datům se **známou** odpovědí.

**Vstup:** pole z VN100 + `Acceleration` (směr gravitace → náklonové koše). Kurz ani GPS se do
proložení **nedávají** — a to je jeho síla: fit nepotřebuje znát pravdu o azimutu.

**⚠️ Azimutové koše se počítají z integrovaného gyra, ne z `yaw`.** `yaw` je právě ta vada, kterou
měříme, takže podle něj se pokrytí posuzovat nedá. Gyro je čisté (klidový bias **−4,6 °/h**, tedy
za dvě minuty otáčení ~0,15°) a na pokrytí stačí *relativní* úhel — nepotřebujeme vědět, kde je
sever, jen že jsme se otočili dokola. (Druhá možnost, vodorovný azimut samotného pole, je
použitelná taky, ale při silném tvrdém železu se koše rozdělí nerovnoměrně — právě proto, že to
pole je pokřivené.)

**⚠️ Kompenzované vs. surové pole.** Driver dnes bere `ImuGroupOptions.Mag`, tedy pole **po**
kompenzaci registrem 23. Kdyby se prokládala kompenzovaná data a výsledek se zapsal do registru
23, zapsala by se **korekce korekce** jako absolutní hodnota. **A z dat se to nepozná** — kdyby
v registru 23 stará kompenzace byla, vyšla by elipsoida přibližně vycentrovaná, což je od dobrého
železa nerozeznatelné. Řešení je vzít **nekompenzované** pole (`UncompMag` z binární IMU skupiny)
jako `IMUState.MagnetometerRaw`; pak je předpoklad odstraněn konstrukčně a offline re-fit jde
udělat z **kteréhokoli** budoucího záznamu bez znalosti stavu registru 23 při nahrávání.
Záložní plán, kdyby `UncompMag` v `vndotnetlib-0.4` nebyl: mise si registr 23 před měřením sama
vynuluje (funkčně ekvivalentní, jen bez toho trvalého zisku pro offline).

**Model.** Senzor kompenzuje jako `m_comp = C · (m_raw − b)`; hledá se `C` (3×3) a `b` (3×1),
tedy 12 parametrů registru 23. ⚠️ **Směr modelu potvrdit z ICD VN-100** — opačně vzatý dá
výsledek, který vypadá věrohodně a je špatný. Referenční export tomu odpovídá
(`B = (−0,274; −0,058; 0,076)`, vodorovně 0,280 G = hodnota z [imu-and-frames.md](imu-and-frames.md)).

Postup: nejmenší kvadráty na obecnou kvadriku `mᵀAm + vᵀm + c = 0` (lineární v 10 koeficientech),
pak rozklad na `(C, b)`.

**⚠️ Rozklad není jednoznačný.** `A = CᵀC` má nekonečně mnoho řešení lišících se rotací —
konstantní `|B|` splní i otočené řešení. Bere se **symetrická pozitivně definitní odmocnina**
`C = A^(1/2)`, protože měkké železo **je** symetrická deformace. Referenční export to potvrzuje:
mimo diagonálu má jednotky tisícin. Kdyby se to nechalo být, zbyl by po kalibraci **konstantní**
posun kurzu, který se nedá odlišit od deklinace.

**Měřítko se váže na registr 21, ne na průměr dat.** Korigované `|B|` se normuje na velikost
referenčního vektoru z registru 21 — `(0,234; 0; 0,4212)` → **0,4818 G**. Není to kosmetika: VPE
porovnává měřené `|B|` a sklon **proti tomuto vektoru** a při nesouhlasu magnetometr adaptivně
utlumí. Kalibrace, která udělá kouli o špatném poloměru, tedy VPE neuspokojí — a to je právě
symptom těch 206 s. **Tady se fáze 1 a 2 potkávají.**

**Ta hodnota se ČTE ze senzoru, nikde se nepíše natvrdo.** Mise si registr 21 přečte na začátku
a pošle ho v `MagCalMsg`; offline si ho `ARBot.Analyze magcal` vezme odtamtud, případně
z přepínače `--bref=`. Číslo 0,4818 G je jen dnešní hodnota, ne konstanta v kódu.

⚠️ **Důsledek pro fázi 2:** až se zapne model pole (registr 83), referenční vektor se přepočítá
podle polohy a data — tedy **změní se** a s ním i cíl normalizace. **Po zapnutí modelu se
kalibrace musí přeměřit** (samo proložení se nemění, mění se měřítko).

### Proč nestačí rotace na rovině

Rotace kolem svislé osy dá **kružnici**, ne kulovou plochu — poctivě se z ní řeší jen tvrdé
železo v `x, y` a měkké 2×2 ve vodorovné rovině. Složka `z` a její vazba zůstanou **nezměřené**.
Pro kurz na rovině to nevadí (kurz se počítá z vodorovné projekce), ale robot v terénu se
**naklání** a nezkalibrovaná `z` se náklonem promítne přímo do kurzu — chyba, která se pak
hledá jako „na kopci to nedrží". Proto se robot při měření **podkládá** (dva až tři náklony
20–30° stačí na to, aby soustava nebyla podurčená; kolik je opravdu potřeba, řekne až podmíněnost
naměřená v poli).

### Verdikt: čtyři nezávislá čísla

| Co | Role | Práh |
|---|---|---|
| podmíněnost návrhové matice | **určenost** — nutná podmínka, ⚠️ ale nad reálnými daty **neúčinná** (viz níž) | ≤ 10⁴ *(změřeno, viz níž)* |
| koše: 24× 15° azimutu, ≥ 3 náklonové skupiny (≥ 2 odklonem ≥ 15°), **náklony na obě strany** | co má člověk udělat **dál** | každý azimut ≥ 20 vzorků |
| `sd(|B|)`, `sd(sklonu)` po korekci | **kvalita**, ne určenost | ≤ 5 mG, ≤ 0,5° |
| shoda proložení z první a druhé poloviny dat | doplněk | ≤ 2° rozdílu v opravě kurzu |

⚠️ **Práh podmíněnosti byl původně odhadnut na 30 a bylo to o pět řádů mimo** — odmítal
i dokonalá data. Změřeno na syntetice 8. 9. 2026 (`MagCalFitTests`):

| pokrytí | podmíněnost |
|---|---|
| jen rovina (bez náklonů) | 2,0 × 10⁸ |
| dva náklony na **jednu** stranu (0, +0,35 rad) | 4,7 × 10⁷ |
| tři náklony (0, ±0,35 rad) | **434** |
| tři velké (0, ±0,6 rad) | **187** |
| pět náklonů | **209** |

✅ **Vypadl z toho kritérium, které v návrhu nebylo: náklony musí být na OBĚ strany.** Dva
náklony na tutéž stranu jsou skoro tak degenerované jako rovina — teprve pár +/− zlomí symetrii,
která drží složku `z` neurčenou. Hlídá to `MagCalCoverage.HasOppositeTilts` (koše se proto klíčují
velikostí odklonu **i jeho směrem**) a pokyn na stránce zní *„podlož robota na DRUHOU stranu"*.

⚠️ ~~**Na reálných datech se ta mezera zúží** — šum vyplní degenerovaný směr, takže rovinná rotace
bude mít podmíněnost menší než 10⁸.~~ ✅ **Změřeno 8. 9. 2026 a zúžila se o PĚT ŘÁDŮ, takže
podmíněnost jako brána nad reálnými daty NEFUNGUJE.** Nad 452 s venkovní jízdy
(`20260907-170728.rec`, 45 185 vzorků) je podmíněnost **352,2** — tedy hluboko pod prahem 10⁴,
ačkoli náklony v datech nejsou vůbec (0 z 2 odkloněných skupin) a výsledek je nesmysl:
`C[2,2] = 38,0` místo ~1,1, `sd(|B|)` po korekci **27× nad prahem**, `sd(sklonu)` **70×**.
Jízda po nerovném terénu tedy degenerovaný směr vyplní, ale **šumem** — soustava je numericky
řešitelná a statisticky pořád podurčená. **Verdikt zachránily koše pokrytí a zbytky**, ne
podmíněnost; primární pokyn pro obsluhu jsou proto **koše** (kritérium geometrické, na šumu
nezávislé) a **zbytky**. Práh se naostro nastaví až podle rotačního testu (Task 10) — snižovat
ho podle jednoho jízdního záznamu by bylo hádání.

⚠️ **Sklon se počítá SKLOPENÝ GRAVITACÍ, ne z pole v tělese.** Sklon je veličina **světová**;
z tělesového pole by při náklonech vyšel rozptyl v desítkách stupňů i u perfektní kalibrace
(naměřeno 11,2°, než se to opravilo). `MagCalFit.Fit` proto bere i akcelerometr a bez něj vrací
`SdInclinationDeg = NaN` — poctivěji než tiše vrátit číslo, které nic neznamená.

⚠️ **Rozpůlení dat samo nestačí** — dvě stejně degenerovaná data se v podurčeném směru shodnou
taky. Podmíněnost je nutná podmínka, rozpůlení jen doplněk; musí platit **obojí**.

Porovnává se **oprava kurzu ve stupních**, ne 12 parametrů: rozdíl parametrů se dá interpretovat
jen s jejich kovariancí, kdežto „o kolik jinak by mi vyšel kurz" je veličina, kterou zajímá
robota. Bere se maximum přes azimutové koše.

⚠️ Prahy jsou **odhad** a naostro se nastaví podle prvního skutečného měření — stejná zásada
jako u `perfwarn=70`.

### Co `MagCalFit` NEUMÍ (a musí to být v dokumentu)

- **Deklinaci.** Registr 83 je `UseMagModel = False` a východní složka registru 21 je 0, takže
  hlášený kurz je azimut k **magnetickému** severu (u nás ~+5°). Řeší se ve fázi 2.
- **Chybu `Reference Frame Rotation`.** Ta se s kurzem **neotáčí**, kdežto tvrdé železo ano —
  proto se musí při přeměření projet **smyčka**. Při dvou směrech o 180° je jinak měkké železo
  od konstantního posunu nerozlišitelné.
- **Osu `z`**, pokud se robot nenaklání.

## Zprávy a švy

- **`MagCalMission`** (`ARBot.Common/Missions`) — sourozenec `FreeRunMission`, ale nejjednodušší:
  nemá cíl, nemá mapu, neprodukuje mrkev. Jediné, co dělá s řízením, je **`Regulator = null`** po
  celou dobu mise. Automat `Collecting → Done`. Pozor na jména: **`StartMission()` / `CurrentStop`**,
  ne `Start()` / `Stop` — kolidovalo by se zděděnými metodami `MessageTarget`, které spouští vlákno
  stupně (past už zapsaná u Robotouru).
- **`MissionWait.MagCoverage = 7`** — nová hodnota **na konec** výčtu (jak si jeho dokumentace
  žádá): „čeká, až obsluha pokryje azimuty a náklony". Sedí na definici „něco, na co se čeká
  zvenčí (člověk…)".
- **`MagCalCollector` + `MagCalMsg`** — vzor `PerfCollector` → `PerfMsg`: 1× za sekundu, přes
  `ToLogMessage()`. Nese, co `PhaseText` nést nemůže: 12 parametrů, podmíněnost, koše pokrytí,
  zbytky, verdikt, a **stav registrů přečtený na začátku mise** (21, 23, 44). Tím je stav „před"
  v záznamu, ne v hlavě obsluhy.
- **`MissionMsg` se nemění** — je robotourovská (depo, QR, nakládka); FreeRun ji taky nepoužívá.
- **`IMagCalControl`** (`ARBot.Common/Missions/MissionSeams.cs`) — úzký šev: přečti registr
  21/23/44/47, zapiš 23/44, ulož do flash. Implementace v HAL nad driverem.
- **`IMUState` FormatVersion 3 → 4** — přidává `MagnetometerRaw` (nullable, plní jen VN100).

## Fáze 1 — kroky

**Podrobný postup krok za krokem** (testy, kód, příkazy, commity) je v
[plan-vn100-kalibrace-kroky.md](plan-vn100-kalibrace-kroky.md). Následující seznam je **osnova** —
tasky v plánu jsou jemnější (proložení a pokrytí zvlášť, mise a runtime zvlášť).

### Task 1 — `MagCalFit` a testy (bez HW)
- [ ] `ARBot.Common/Calibration/MagCalFit.cs`: proložení kvadriky, symetrický rozklad,
      normalizace na `|B|` z registru 21, podmíněnost, zbytky, koše pokrytí, verdikt.
- [ ] `MagCalFitTests`: syntetika se **známou** odpovědí (vyrobit pole, aplikovat známé `C, b`,
      parametry se musí vrátit).
- [ ] Test **degenerace**: jen yaw, bez náklonů → fit musí být **odmítnut podmíněností**, ne tiše
      odpovědět.
- [ ] Test **symetrie**: data vyrobená nesymetrickou (rotovanou) maticí → fit vrátí symetrickou.
- [ ] Test **normalizace**: korigované `|B|` sedí na 0,4818 G, ne na průměr dat.

### Task 2 — `ARBot.Analyze magcal` (bez HW)
- [ ] `Src/ARBot.Analyze/MagCalReport.cs` + `case "magcal"` v `Program.cs`.
- [ ] Tiskne: 12 čísel **ve tvaru ke zkopírování do `VNWRG,23,…`**, verdikt a všechna čtyři čísla,
      koše pokrytí.
- [ ] `MagCalMsg` ze záznamu **vedle** vlastního přepočtu — rozejdou-li se, je chyba v kódu, ne
      v senzoru.
- [ ] `--reg47=<12 čísel>` pro porovnání s tím, co spočítal sám senzor.
- [ ] **Pustit na existující záznam** (`records/20260903-*.rec`) a zapsat do DevLogu, jaká je
      podmíněnost nad běžnými jízdními daty. To je užitečné samo: řekne předem, jak moc je rotace
      potřeba, místo dohadu.

### Task 3 — surové pole
- [ ] **Nejdřív ověřit**, že `ImuGroupOptions` v `vndotnetlib-0.4/VectorNav.dll` má `UncompMag`
      (odpoví překladač, minuta práce).
- [ ] Když ano: přidat do `BinaryOutputConfig` v `VN100IMUBinary`, `IMUState.MagnetometerRaw`,
      `FormatVersion` 4, serializační test.
- [ ] Když ne: záložní plán — mise vynuluje registr 23 před měřením; zapsat do dokumentu, že
      offline re-fit pak vyžaduje znalost registru 23 z doby nahrávání.

### Task 4 — příkazy a čtecí cesta v driveru
- [ ] `VnCommands`: buildery pro registr 23 (`MagnetometerCompensation`), registr 44
      (`MagCalControl`, `HSIMode=Run` / `HSIOutput=NoOnboard`), `VNWNV`, a **parsování odpovědí**
      na `VNRRG` pro registry 21/23/44/47.
- [ ] ⚠️ **Čtecí cesta v driveru je netriviální část.** Driver jede **binárně**
      (`AsyncMode.SerialPort1`, ADOR=0), takže ASCII odpovědi přicházejí **utopené v binárním
      toku**. `deploy/vnprobe.sh` si na to naběhl a má to v hlavičce: musí se hledat rámce
      `$VN…*XX` **v bajtovém proudu, ne po řádcích**.
- [ ] Testy **proti zaznamenanému bajtovému proudu** (ASCII odpověď vložená mezi binární rámce),
      ne proti senzoru.
- [ ] `IMagCalControl` implementace v HAL.

### Task 5 — mise
- [ ] `MagCalMission`, `MagCalPhase`, `MagCalCollector`, `MagCalMsg`, `MissionWait.MagCoverage`.
- [ ] Napojení na selektor `mission=magcal` (`ParamRegistry`, `ConfigurationDocument`, seznam misí
      na stránce).
- [ ] Na začátku mise: přečíst registry 21/23/44 → do `MagCalMsg`; nastavit registr 44 na
      `Run`/`NoOnboard` (nezávislá kontrola). **Referenční `|B|` pro normalizaci se bere
      z přečteného registru 21**, ne z konstanty.
- [ ] **Jen v záložní variantě Tasku 3** (když `UncompMag` v DLL není): mise navíc **vynuluje
      registr 23** před sběrem a ohlásí to na stránce. S surovým polem tenhle krok **nesmí být** —
      mazal by aktivní kalibraci bez důvodu.
- [ ] `MagCalMissionTests`: automat, koše, a **`Regulator == null` po celou dobu mise** — to je
      **bezpečnostní test**, ne kosmetika.
- [ ] Serializační test `MagCalMsg` (vzor `PerfMsgSerializationTests`).

### Task 6 — stránka
- [ ] Blok „Kalibrace magnetometru": pokrytí, podmíněnost, zbytky, verdikt, 12 čísel, porovnání
      s registrem 47.
- [ ] Tlačítko **„Zapsat do senzoru"** — gatované **drženým nouzovým zastavením** (gate i na
      serveru, 409), týž mechanismus jako volba mise.
- [ ] Po zápisu ukázat **zpětné čtení** registru 23.

### Task 7 — terénní měření (na zařízení)
- [ ] Odjet runbook níž a zapsat naměřená čísla do [imu-and-frames.md](imu-and-frames.md)
      a [devlog.md](devlog.md).

### Task 8 — přeměření a rozhodnutí o fázi 2
- [ ] Projet **smyčku** venku, pustit `ARBot.Analyze vn100` a `heading`.
- [ ] Porovnat s akceptačními kritérii níž.
- [ ] Přeměřit blok 3 (`K`) → rozhodnout o fázi 2.
- [ ] **Vypnout a zapnout robota** a přeměřit — jediný skutečný test, že kalibrace přežila flash.

## Runbook v terénu

Všechno z telefonu; notebook nikde.

1. Na stránce vybrat `mission=magcal` **pod drženým nouzovým zastavením**, pak stop uvolnit.
2. **Otáčet robotem rukou**, dokud stránka nehlásí `HOTOVO` — průběžně říká, **co chybí**
   (které azimuty, který náklon). Doporučené pořadí: 2–3 pomalé obraty na rovině → podložit jednu
   stranu 20–30° → obrat → druhá strana.
3. **Stisknout nouzové zastavení.** Na stránce svítí 12 čísel, verdikt a porovnání s registrem 47.
4. Ťuknout **„Zapsat do senzoru"**. Mise zapíše registr 23, uloží (`VNWNV`), zpětně přečte
   a ukáže výsledek.
5. ⚠️ **Čekat ~2 minuty**, než se kurz srovná (senzor v režimu `Absolute` se na pole dotahuje
   ~100–170 s; zapsáno v [imu-and-frames.md](imu-and-frames.md) z 6. 9. 2026).

**Záchranná cesta**, když runtime nenaběhne: `deploy/vnprobe.sh` (read-only `VNRRG`)
a `deploy/vnrestore.sh` (zápis + flash). Oba zůstávají **beze změny**.

**U stolu, jako audit — ne krok procedury:** `ARBot.Analyze magcal <rec>`.

## Akceptační kritéria fáze 1

Výchozí stav je z `20260907-170728.rec`, ať je vidět, o kolik se to má zlepšit.

| Ukazatel | Dnes | Cíl |
|---|---|---|
| `IMU yaw − GPS kurz` p50 / sd | −24,0° / 18,6° | \|p50\| ≤ 5°, sd ≤ 5° |
| 1. harmonická (tvrdé železo) | 27,2° | ≤ 3° |
| 2. harmonická (měkké železo) | 25,2° | ≤ 3° |
| rozpětí `|B|` | 0,148 G | ≤ 0,01 G |
| rozpětí sklonu | 46–83° | ≤ 2° |

⚠️ **Zbytkový konstantní posun se do vady nepočítá**, dokud se nerozhodne o deklinaci (~+5°).
Odlišit ho od chyby `Reference Frame Rotation` umí **jedině smyčka**.

## Fáze 2 — VPE

Začíná **přeměřením** bloku 3 v `ARBot.Analyze vn100`:

- **`K` vyskočilo** → vada 2 byla **následek** vady 1. Fáze 2 se **ruší** a zapíše se to jako
  zamítnutá vada (i s číslem, aby to nikdo nezkoušel znovu).
- **`K` nevyskočilo** → tři kandidáti, **každý jako jeden zápis + přeměření**, ne balík — jinak
  se nedozvíme, co zabralo:
  1. ✅ **HOTOVO 8. 9. 2026 a přesunuto PŘED terénní měření** (`magmodel=`, výchozí `true`,
     `MagModelInit`). Registr 83 se nastaví jednorázově po prvním kvalitním fixu, bez `VNWNV`.
     Důvod přesunu: registr 21 znamená sklon 60,9° proti ~65,7° pro ČR a **VPE porovnává měřený
     sklon proti té referenci**, takže i perfektní kalibrace může zůstat částečně udušená.
     ⚠️ Je to **hypotéza** a na HW to neběželo; `magmodel=false` vrací staré chování.
     Podrobně [imu-and-frames.md](imu-and-frames.md).
  1. ~~**Registr 83 `UseMagModel=true`** + rok + naše souřadnice.~~ Spraví referenci pro naši polohu
     **a přinese deklinaci**. ✅ **`VnCommands.ReferenceVectorConfig` je už napsaný, tvar příkazu
     ověřený proti odpovědi senzoru — a nikdo ho nevolá.** Chybí mu zavolání a `VNWNV`. Motivace
     navíc: registr 21 `(0,234; 0; 0,4212)` znamená sklon **60,9°** a `|B|` 0,482 G, ale pro ČR
     je sklon ~**65,7°** — **reference je sama o ~5° vedle** a VPE proti ní měřený sklon
     porovnává.
  2. **Registr 36** `BaseTuning` výš (dnes `(4,4,4)`).
  3. **Registry 37/38** měkčí adaptivní filtrování (dnes `AdaptiveTuning (5,5,5)`,
     `AdaptiveFiltering (5,5; 5,5; 5,5)`).
- [ ] Doplnit do `deploy/vnprobe.sh` čtení registrů **37 a 38**, ať je fáze 2 podložená daty
      (dnes čte 36 a 44).

### ⚠️ Levné ověření hypotézy „VPE utlumí magnetometr při nesouhlasu s registrem 21" — ZMĚŘENO 8. 9. 2026, hypotéza NEPODPOŘENA

Blok 2 v `ARBot.Analyze vn100` dával jedno `K` přes celý záznam (0,00485 ± 0,00074 1/s, tedy
206 s). Nový **blok 2b** ho rozpadá do kvantilových košů podle `| |B| − 0,4818 |`
a `|sklon − 60,9°|` (reference se bere z `--bref=` / `--incl=`, protože po zapnutí `magmodel=`
se registr 21 změní). Hypotéza předpovídala **monotónní pokles `K`** s rostoucí odchylkou.
Naměřeno nad `20260907-170728.rec` (449 oken po 1 s):

| koš | odchylka \|B\| [G] | `K` [1/s] | sd(chyby) [°] |
|---|---|---|---|
| 1 | 0,0001–0,0128 | −0,00115 ± 0,00234 | 16,8 |
| 2 | 0,0128–0,0262 | −0,00201 ± 0,00159 | 18,5 |
| 3 | 0,0262–0,0589 | +0,00270 ± 0,00086 | 23,8 |
| 4 | 0,0590–0,1115 | +0,00617 ± 0,00213 | 17,4 |

| koš | odchylka sklonu [°] | `K` [1/s] | sd(chyby) [°] |
|---|---|---|---|
| 1 | 0,0–2,6 | +0,00377 ± 0,00108 | 23,2 |
| 2 | 2,6–5,4 | +0,00684 ± 0,00142 | 27,3 |
| 3 | 5,4–8,2 | +0,00177 ± 0,00167 | 25,9 |
| 4 | 8,2–18,2 | −0,00248 ± 0,00217 | 12,7 |

**Výsledek: `K` s odchylkou `|B|` monotónně ROSTE, tedy přesně naopak, než hypotéza čekala**
(−0,0012 → −0,0020 → +0,0027 → +0,0062), a u odchylky sklonu **není monotónní vůbec** (roste,
pak padá; jen poslední koš by hypotézu podpořil). Rozptyl chyby proti poli je přitom v koších
`|B|` vyrovnaný (16,8–23,8°), takže rozdíl mezi nimi **nevysvětlí sám regresní útlum**.

⚠️ **Nedělá to z toho ale vyvrácení — měření nemá rozlišovací schopnost, a je to teď změřené:**
podíl rozptylu odchylky `|B|`, který vysvětlí kurz, je **η² = 0,910**. Odchylka `|B|` je tedy
z 91 % funkcí kurzu (dělá ji tvrdé železo na těle) a „`K` klesá s odchylkou" a „`K` závisí na
kurzu" **jsou skoro totéž měření**. Kontrolní rozpad podle kurzu to potvrzuje: `K` tam kolísá
−0,0054 … +0,0219 1/s, tedy **v širším rozpětí než po odchylce**, a sd chyby se mezi koši
kurzu mění 5,0–31,9°, takže se u něj regresní útlum liší silně.

**Co z toho platí pro `magmodel=`:** třetí důvod pro zapnutí modelu pole („registr 21 udává
sklon 60,9° místo ~65,7°, takže VPE dusí magnetometr") **tuhle oporu nedostal** — a byl označen
jako hypotéza, takže se tím nic nekácí. Zůstává v platnosti první a druhý důvod (deklinace se
dopočítá z WMM, nic neproměřeného se nebetonuje) a `magmodel=false` je pořád jeden přepínač
A/B. **Přeměřit tenhle blok po kalibraci** (fáze 2) má smysl dál: až tvrdé železo zmizí, spadne
i η² a rozpad začne rozlišovat.

## Co se sem vědomě nedělá

- **Windows SW (VN100 Control Center) jako hlavní cesta.** Kalibrovat se musí **v robotu** (tvrdé
  i měkké železo je vlastnost šasi, ne senzoru), takže by to znamenalo notebook na jezdícím robotu
  a černou skříňku, kterou nelze skriptovat ani přeměřit. Zůstává jako **třetí** záloha.
- **Rotace vlastní silou** (nová rutina v runtime). Viz rozhodnutí 3.
- **Obecné „zapiš jakýkoli registr" API.** Viz rozhodnutí 2 — smazalo by tu čáru.
- **Skládání kalibrací** (`C_abs = C_new · C_pre`). Algebra je jednoduchá
  (`b_abs = b_pre + C_pre⁻¹ · b_new`), ale součin symetrických matic symetrický není, takže by to
  zaneslo právě tu nejednoznačnost, kterou symetrický rozklad odstraňuje. Řeší se surovým polem
  (Task 3).
- **Nová kalibrace ze staré z ARBot2.** ⚠️ Ta je **horší než žádná** — hard-iron bias vodorovně
  0,280 G je **větší než vodorovná složka zemského pole (~0,20 G)**, takže kompas přestane
  reagovat na otáčení: `sd(IMU yaw − GPS kurz)` **9,7°** bez ní proti **118,3°** s ní. Změřeno
  6. 9. 2026, zapsáno v [imu-and-frames.md](imu-and-frames.md).
- **Automatický zápis do senzoru bez člověka.** Nikdy.

## Otevřené otázky a co zůstane neověřené

- **Směr modelu registru 23** (`C·(m−b)` vs. `C·m − b`) — potvrdit z ICD VN-100 (Task 4).
- ✅ **`UncompMag` v `vndotnetlib-0.4` JE** (ověřeno překladem 8. 9. 2026; v enumu jsou
  i `UncompAccel`/`UncompGyro`). Surové pole je proto v `IMUState.MagnetometerRaw`, formát 4,
  a předpoklad „registr 23 = identita" **padl konstrukčně** — mise registr 23 mazat nesmí.
- **Sémantika polí registrů 37/38** — potvrdit z ICD, dnes se zná jen tvar z exportu.
- **Stačí 2–3 náklony?** Řekne až podmíněnost naměřená v poli, ne návrh.
- **Přežije kalibrace vypnutí a zapnutí?** Jediný skutečný test flash (Task 8).
- **Celá fáze 2** do přeměření.
- **`IMUState` FormatVersion 4 se dotkne všech záznamů** — starší se musí dál čítat
  (`FromData` podle verze).

## Odkazy

- [imu-and-frames.md](imu-and-frames.md) — historie nálezu, čísla, `deploy/vnprobe.sh`,
  `deploy/vnrestore.sh`
- [decisions.md](decisions.md) — rozhodnutí 1 a 2 výše
- [ekf-fusion.md](ekf-fusion.md) — proč fúze kurz přebírá a ne váží
- [robotour-mission.md](robotour-mission.md) — vzor mise (`StartMission()` / `CurrentStop`)
- [perf-monitoring.md](perf-monitoring.md) — vzor `PerfCollector` → `PerfMsg`
- `vn100-2026-7-8-nastavei z arbot2.sencfg` (v korenu repa) — referenční export registrů
