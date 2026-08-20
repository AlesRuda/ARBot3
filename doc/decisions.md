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
