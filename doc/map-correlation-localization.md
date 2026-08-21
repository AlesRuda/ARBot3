# Korelace occupancy gridu s mapou — odhad polohy

Robot ví, jak vypadá vozovka **kolem sebe** (occupancy grid, kanál `LRoad` ze semantiky RGB),
a zároveň ví, jak vypadá vozovka **podle mapy** (OSM síť, `RoadScene.IsRoad`). Když se ty dva obrazy
posunou proti sobě, ten posun **je** chyba lokalizace. Tenhle dokument popisuje, jak ji naměřit
a poslat do fúze.

Motivace: GPS na ±2 m je pro globální navigaci na hraně použitelnosti. Špatná lokalizace ⇒ špatná
„mrkev" z [globální navigace](global-navigation-runtime.md) ⇒ robot míří mimo cestu, i když lokální
plánovač pracuje správně. Korelace s mapou je na tuhle vadu nejsilnější léčba, protože mapu bereme
jako pravdu a chyba je celá v póze.

## Stav (2026-08-19)

Vznikl 19. 8. 2026 z otevřených úkolů v [occupancy-and-local-planning.md](occupancy-and-local-planning.md)
a [global-navigation-runtime.md](global-navigation-runtime.md). **Fáze 1–3 hotové** (jádro, měření
ve fúzi, zpráva + telemetrie, napojení na runtime) — implementováno a ověřeno buildem a celou
testovací sadou (`dotnet test -p:Platform=x64`). **Fáze 4–5 nejsou** (ladění nad záznamy, měření
na OrangePI) — viz [Fáze](#fáze) níže.

| Část | Stav |
|---|---|
| Jádro `ARBot.Common/Localization` (korelátor, konfigurace, výsledek) | **hotovo** — jednotkové testy |
| `AxisOffsetMeasurement` ve fúzi | **hotovo** — jednotkové testy |
| `MapCorrelationMsg` + telemetrie | **hotovo a ověřeno za běhu** — zpráva má testy; pád na `: byte` výčtu (viz níž) je opravený a ověřený nad reálným záznamem: všech 64 sloupců × 4 457 řádků (168 419 neprázdných buněk) se naformátovalo **bez jediné výjimky**, `korel duvod` vrací `Ok` |
| Napojení na `ARBotRuntime` | **hotovo, ověřeno během nad virtuálním HW** — korelátor běží v pipeline (vlastní vlákno nad snapshotem occupancy gridu) a produkuje výsledky (69 cyklů za 40 s); **na reálném HW se nespouštěl** |

**Od 20. 8. 2026 se korelace ve výchozím stavu VŮBEC NEPOČÍTÁ** — parametr příkazové řádky
`mapcorr` má default `false` a korelátor se v `ARBotRuntime.WireRun` ani nezaloží. Důvod: nic
neřídí (korekce jsou neúčinné, viz níž) a návrh je pod revizí, takže by jen spaloval ~126 ms na
cyklus, tedy čtvrt jádra na x64 a víc na ARM. Zapnout: **`mapcorr=true`**.

Kód **default nemění** — `false` zůstává. Zapnuto je to jen ve **spouštěcích profilech virtuálního
HW** ([launchSettings.json](../Src/ARBot/Properties/launchSettings.json), od 21. 8. 2026), protože
tam se korelátor právě testuje. Reálný HW i běh bez profilu tím nedotčen.

**Tři různé přepínače, nepleteme si je:**

| přepínač | co dělá | výchozí |
|---|---|---|
| `mapcorr=` (příkazová řádka) | zakládá se stupeň korelace **vůbec**? | **`false`** |
| `mapcorrsend=` (příkazová řádka) | posílají se měření **do fúze**? (= `SendCorrections`) | `true` |
| `measdiag=` (příkazová řádka) | publikují se **verdikty jednotlivých měření** (`MeasurementDiagMsg`)? | vypnuto |

`mapcorrsend=` je od 21. 8. 2026 a existuje pro jediné čisté měření skutečné autority korelace:
**A/B se stejnou zátěží** (výpočet běží v obou bězích, jen jeden neposílá). `measdiag=` bere
`true`/`*` = všechna měření, nebo seznam podřetězců zdroje oddělený čárkou — typicky
`measdiag=MapCorr`. Naplocho by to zaplavilo stream, měření chodí stovky za sekundu.

`SendCorrections` se dříve jmenovalo `Enabled` a byla to past: test je **až za celým výpočtem**
([MapCorrelator.cs](../Src/ARBot.Common/Localization/MapCorrelator.cs)), takže `false` neuspořilo nic
— sken, rastr, důkazní seznam i kovariance se spočítaly vždycky. Přejmenováno 20. 8. 2026. Při
`mapcorr=false` je `SendCorrections` bezpředmětné.

Korekce samotné jsou zapnuté (`SendCorrections = true`) a okno historie EKF je prodloužené na **3 s**
(`FusionConfig.HistoryWindow`) — fáze 1–3 běžely s posíláním vypnutým, 20. 8. je autor zapnul.

> **Otevřené vady tím nezmizely** — jen se rozhodlo ladit je za provozu místo nad záznamy. Pořád
> platí: σ je slepá k množství důkazu (viz [Otevřené úkoly](#otevřené-úkoly)), `TightAxisAngle` je
> vychýlená ~6,3° a **chybí tvrdý limit korekce za cyklus**. Ten poslední je při zapnutých
> korekcích nejakutnější: `MaxOffsetM` omezuje naměřený posun, ne aplikovaný krok, takže při malé σ
> proti velkému `P` může filtr aplikovat skoro dva metry v jednom updatu.

> **Prodloužení okna samo latenci neřeší.** Zahození se hlásí jako „opozdeno o N ms za nejnovejsim,
> okno je W ms" — když je N pořád nad W i po zvětšení okna, je problém v latenci, ne ve okně. Viz
> [Latence korekce proti oknu historie EKF](#latence-korekce-proti-oknu-historie-ekf); v Debug buildu
> je latence 1,4–1,8 s a s prodlouženým oknem roste přepočítávaný ocas, takže se to může i zhoršit.

### Co virtuální HW o funkčnosti korekcí ukázat NEMŮŽE

Naměřeno 20. 8. 2026 nad `20260820-122026.rec` (`SyntetickyKoridor.osm`, chyba pózy nastavená
z UI virtuální kamery, robot jede, korekce zapnuté):

| | |
|---|---|
| korelací, které poslaly korekci | **67 ze 67** (všechny tři složky) |
| z toho stav zareagoval ≥ 30 % tvrzeného posunu | **3** |
| součet tvrzených posunů / skutečných do 250 ms | 10,41 m / 1,83 m (17,6 %, většina z toho je jízda) |
| systematický posun stavu proti GPS | **0,141 m** při standardní chybě průměru **0,152 m** |

**První korekce se aplikovala** — stav uskočil v jednom 100 ms tiku přesně o tvrzených 0,800 m
(zesílení ≈ 1, protože `P` bylo po startu ještě volné). **Od druhé nula:** jemná stopa v 10 Hz
nemá v okamžiku korekce žádnou nespojitost.

**Filtr přitom zůstal na pravdě** (0,141 m proti šumu 0,152 m), takže se *nezamkl* mimo ni. Jede
prostě na GPS a korelaci ignoruje.

> **Ten experiment nemůže vyjít, a je to vlastnost zadání, ne vada.** Vnucená chyba je **fiktivní**:
> virtuální GPS měří simulovaného robota, tedy pravdu, a tu chybu popírá. Správně fungující filtr
> proto *má* dát přednost GPS. Ta jedna korekce, která prošla, odtlačila odhad 0,8 m **od** pravdy
> a GPS ho musela vytáhnout zpátky. Navíc se hlášený posun nemůže vynulovat ani principiálně:
> kamera renderuje z odhadu, takže posunutí odhadu posune i obraz — proto `Dx` stojí na 0,800.
>
> Chceš-li ověřit, že korekce **pracují**, musí být korelace jediná absolutní reference: **zhoršit
> nebo vypnout GPS** (což je i skutečný účel funkce — je na „špatná lokalizace ⇒ špatná mrkev"),
> nebo vnutit tutéž chybu i GPS, nebo měřit nad reálným záznamem se skutečnou chybou lokalizace.

#### Tři různé experimenty, tři různá místa vnucení chyby

Z toho výše plyne, že to nebyl jeden test, ale **tři** — a `poseerror` umí jen první:

| co se ověřuje | kam vnutit chybu | co má vyjít | stav |
|---|---|---|---|
| korelátor **najde** příčnou odchylku (znaménko, velikost) | póza kamery — `poseerror=` | `D` = vnucená chyba | **hotovo**, naměřeno na jednotky mm |
| korelace **opraví špatnou lokalizaci** | **GPS** (bias, šum) | póza se vrátí k pravdě | chybí |
| posun `d` **identifikuje posunutou mapu** | **mapa pro kameru** (dvě mapy, `visionmap=`) | `d` → posun mapy, póza zůstane na GPS | **sestava hotová** (21. 8. 2026), měření chybí |

**Vnucená chyba pózy je fyzikálně nesmysl** — „kamerina představa o tom, kde je" v realitě
neexistuje. Proto z ní vyšel ten kruh: posunutí odhadu posune i obraz. Naproti tomu **posunutá mapa
je reálný jev** (mis-georeferencovaná OSM), takže vnucení chyby tam měří skutečnou hypotézu.

**Klíčový rozdíl u dvou map:** hlášený posun zůstane konstantní — ale z *poctivého* důvodu, protože
posunutou mapu **nelze spravit posunutím robota**. Z toho posunu se tím stane **pravda pro `d`**
a jde ověřit falsifikovatelná předpověď: `d` má zkonvergovat k vnucenému posunu, zatímco póza má
zůstat na GPS. Sestava s `poseerror` to dát nemohla — tam se konstantní posun tvářil jako chyba
lokalizace, kterou má filtr opravit, a on ji „opravoval" do prázdna.

Mechanismus a past viz [virtual-hw.md](virtual-hw.md#dvě-mapy--vnucená-chyba-do-mapy-pro-kameru).

**Sestava pro třetí experiment je od 21. 8. 2026 k dispozici** — parametr `visionmap=<cesta.osm>`
řekne virtuálním kamerám, aby renderovaly z jiné mapy než z té navigační (`map=`). Vnucená chyba je
tak **v datech**, ne v parametru: [`OSM/SyntetickyKoridorPosunuty.osm`](../OSM/SyntetickyKoridorPosunuty.osm)
je kopie `SyntetickyKoridor.osm` s každým uzlem posunutým náhodně do 1 m a tabulkou posunů v hlavičce,
takže se dá z výsledku odečíst. Ve World pohledu je rozestup obou map vidět jako vrstva „Mapa (vize)".

```bash
ARBot.exe virtualhw=true mapcorr=true map=OSM/SyntetickyKoridor.osm visionmap=OSM/SyntetickyKoridorPosunuty.osm
```

> **Pozor u tohoto experimentu:** posun uzlů je **náhodný per uzel**, ne tuhá translace celé mapy.
> `MapCorrelator` hledá jedno 3-DOF `(dx, dy, φ)` na celý grid, takže při náhodných posunech nemá
> ke konvergenci jednu správnou odpověď — dostane vážený kompromis podle toho, které úseky má právě
> v gridu. Pro *tuhý* posun (kde se dá předpověď `d` → vnucený posun ověřit přímo) je potřeba mapa
> posunutá **jako celek**; posunuté uzly zkoušejí spíš robustnost proti deformaci mapy.

**Proč od druhé korekce nic:** **gating**. Korelátor hlásí σ ≈ 0,10 m; první korekce prošla, protože
`P` bylo velké, ale **tím ho sama stáhla** na ~σ². Od té chvíle je `S = P + R ≈ 2σ²` a tvrzený posun
0,28 m dá NIS ≈ 3,5–3,6 proti prahu χ²(1; 0,95) = 3,84. Je to strukturální past: **sebejistý
korelátor tvrdící velkou chybu si ji sám zamkne.** Čím větší chybu najde, tím spolehlivěji ji gate
zamítne.

#### Při `GateMode.Reject` se velký posun absorbovat NEDÁ — výpočet

Namítalo se (správně), že stav filtrovaný EKF nemusí skákat: začne na nule a poroste postupně, jak
se odchylka měří. Postupnost ale není vlastnost toho, že je to „v EKF" — je to vlastnost `P` na
startu, a ta dvě podmínky si **protiřečí**. Pro skutečný posun 0,8 m a σ korelace 0,105 m
(`R ≈ 0,011`):

| požadavek | podmínka | vyjde |
|---|---|---|
| neuskočit nad toleranci `PoseJumpDetector` 0,5 m | `K = P/(P+R) < 0,625` | **σ < 0,135 m** |
| projít gatingem (`NIS < 3,84`) | `S = P + R > 0,167` | **σ > 0,395 m** |

**Žádná hodnota nesplní obojí.** Posun 0,8 m se buď zamítne, nebo uskočí — postupně nikdy. To
vysvětluje naměřené „67 poslaných korekcí, stav zareagoval 3×" lépe než hypotéza o zamčení `P`:
není to nastavení, je to struktura.

> **Kandidát na řešení už v kódu je: `GateMode.Soft`** (`R' = R × NIS/prah`) — odlehlé měření se jen
> málo zváží, **nikdy nevypne**, a komentář u něj přímo říká „filtr se z dlouheho vypadku vzdy
> vzpamatuje". To je doslova ta postupná absorpce. Korelační měření jsou dnes na `Reject` (výchozí
> u `AxisOffsetMeasurement` i `HeadingMeasurement`).
>
> **Není to zdarma:** `Reject` byl vědomá volba a je v seznamu bezpečnostních pojistek („jeden
> výstřel robota neposune"). `Soft` pustí i špatné korelace, jen potlačené. Je to výměna rizik.

> **⚠️ Potvrdit to ze záznamu nelze** — chybí NIS a příznak přijetí. `MeasurementDiagMsg` přitom nese
> přesně `Source`, `Z`, `DiagR`, `Nis`, `Accepted`, ale **nikdo ji nepublikuje**: je to mrtvý DTO
> (`Diagnostics()` to drží jen v paměti). Dokud se nezapojí, zůstane „přijato vs. zamítnuto"
> u korekcí věcí odhadu. **Neopraveno.**

**Co je odsimulované / nutné ověřit na zařízení:**
- Napojení do `ARBotRuntime.WireRun` je **ověřené během nad virtuálním HW** (19. 8. 2026,
  `virtualhw=true map=OSM/HajeRovne.osm`, 40 s): korelátor se založil, běžel a poslal 69
  `MapCorrelationMsg`, všechny s `Reason = Ok`. **Na reálném HW se pořád nespouštěl.**
- **`Dx = Dy = 0` na správné póze nic nedokazuje** — pozor na to, snadno se to přečte jako důkaz.
  Ve virtuálním HW renderuje kamera z `engine.GetStateAt(t)` a occupancy grid se ukotvuje **touž**
  pózou, takže obsah gridu s mapou souhlasí vždycky. Nula je tedy **strukturální** a vyšla by
  i rozbitému korelátoru; ověřuje jen to, že sken najde vrchol uprostřed okna. Skutečný důkaz
  znamének a velikosti dá teprve **vnucená chyba pózy** (níž).
- **Pozor na cirkularitu simulace:** `korel skore` vychází ≈ 0,996, jenže virtuální kamera
  renderuje scénu **z téže OSM mapy**, proti které se koreluje (viz [virtual-hw.md](virtual-hw.md)).
  Vysoké skóre tedy potvrzuje geometrii a znaménka, **ne** kvalitu semantiky `LRoad` v reálu.
- **Bez cíle robot stojí.** Cíl jde zadat jen Ctrl+klikem ve world view, parametr příkazové řádky
  pro něj není — bezobslužný běh proto proměří jen statickou scénu. Pro fázi 4 (σ proti
  realizovanému rozptylu, rozdělení NIS, duty cycle `korel os+` na zakřivených cestách) je pohyb
  nutný, takže bude potřeba buď ruční jízda, nebo doplnit parametr typu `goal=lat,lon`.
- Telemetrické sloupce (`korel …`) autor **zkusil zobrazit 19. 8. 2026 a narazil na výjimku**:
  `MapCorrelationReason` je `: byte` (aby se do zprávy vešel na jeden bajt), ale sdílený helper
  `Enum<T,TEnum>` v `TelemetryColumns` předával `Enum.IsDefined` vždy `int`, což vyžaduje shodu
  s **podkladovým** typem výčtu → `ArgumentException`. Všechny starší výčtové sloupce
  (`GlobalNavStatus`, `LocalPlanStatus`, `GPSState.FixQuality`) mají standardní `int` podklad, takže
  ta past ležela v kódu nepovšimnutá a **žádná ze čtrnácti review ji nenašla** — odhalilo ji teprve
  spuštění. Opraveno přesunem převodu do `ARBot.Common/Telemetry/EnumPresentation.cs`, kde je
  pokrytý testy (`byte` i `int` výčet, neznámá hodnota, hodnota mimo podkladový typ). **Doplněno
  19. 8. 2026:** formátovací cesta je nově ověřená i nad reálnými daty — průchod skutečného registru
  `TelemetryColumns.All` přes celý záznam (64 sloupců × 4 457 řádků) proběhl bez výjimky. Ruční
  proklikání UI (řazení, filtr řádků, tooltipy) tím nahrazené **není**.
- Doba výpočtu jednoho cyklu korelace (`korel vypocet [ms]`) **změřena** na x64 — 126,5 ms průměr
  v Release, 696 ms v Debugu (tabulka „Naměřená doba cyklu" v sekci
  [Předpoklady a rizika](#předpoklady-a-rizika)). Na OrangePI měřená není. Reálný dopad korekce na
  jízdu měřený není; to je náplň fáze 4 a 5.

## Co to řeší a co ne

**Řeší:** systematickou chybu polohy a kurzu vůči mapě, dokud je robot na cestě, která v mapě je.

**Neřeší** (a nemá):
- Lokalizaci bez mapy (SLAM). Mapa je vstup, ne výstup.
- Situaci mimo mapovanou cestu. Tam korelátor **mlčí** (viz [Chování při nejistotě](#chování-při-nejistotě)).
- Chybu v mapě samotné. Když je OSM úsek posunutý nebo chybí, korelátor to pozná jen jako nízkou
  kvalitu shody — neumí rozhodnout, kdo z těch dvou se mýlí.
- Podélnou polohu na dlouhé přímé cestě bez odbočky. Tam je nepodmíněná a odhad to musí **přiznat**,
  ne si vymyslet (viz [Kovariance](#6-kovariance-z-hessiánu)).

## Klíčová rozhodnutí

Tři rozhodnutí, ze kterých vyplývá zbytek návrhu. Podrobněji v [decisions.md](decisions.md).

**Mapová pravda je celá síť, ne vybraná trasa.** Korelovat proti trase z `GlobalNavigator.Route` by
byla potvrzovací zaujatost: kdyby robot reálně odbočil jinam, korelace by ho k původní trase
přilepila — tedy přesně naopak, než k čemu má sloužit. Korelátor **trasu vůbec nevidí** a
`GlobalNavigator` na něm nezávisí ani obráceně.

**3-DOF `(dx, dy, φ)` ve světových osách, s anizotropní kovariancí.** Původní úvaha byla omezit se na
příčný posun a kurz vůči ose cesty, protože podélná složka je na přímé cestě degenerovaná. To ale
zahazuje informaci: **odbočky a křižovatky jsou v semantice vidět** a podélnou symetrii lámou.
Správná odpověď tedy není podélnou složku vynechat, ale odhadnout ji a nechat kovarianci říct, jak
moc jí věřit. Vedlejší přínos: bez rámu cesty nepotřebuje korelátor žádnou referenční osu, takže
z návrhu vypadl celý pojem „rám cesty" a s ním i závislost na vybrané hraně.

**Do fúze jdou dvě skalární osová měření, ne `PositionMeasurement`.** `PositionMeasurement` i
`PoseMeasurement` mají `R` jen diagonální (v osách světa), takže anizotropní kovarianci otočenou do
rámce cesty tam nedostaneš. Skalární měření podél vlastních os korelační plochy to řeší exaktně a
bez maticové `R`.

## Na čem to stojí (co už v repozitáři je)

| Co | Kde | K čemu |
|---|---|---|
| `OccupancyGrid`, kanál `LRoad` | [Occupancy/OccupancyGrid.cs](../Src/ARBot.Common/Occupancy/OccupancyGrid.cs) | robotí strana korelace; world-kotvený (ENU), takže se nemusí nic rotovat |
| `OccupancyGridMsg` (snapshot 500 ms) | `ARBot.Common/Logs` | vstup korelátoru; **je nahrávaný**, tedy ladění nad záznamy zdarma |
| `RoadScene.IsRoad(x, y)` | [Maps/OsmNav/Graph/RoadScene.cs](../Src/ARBot.Common/Maps/OsmNav/Graph/RoadScene.cs) | mapová strana: sjednocení kapslí kolem hran s interpolovanou šířkou, uniformní mřížka + CSR index |
| `RoadNetwork`, `Node.Width` | `Maps/OsmNav/Graph` | zdroj geometrie a šířek pro `RoadScene` |
| `AsyncFusionEngine.Enqueue` / `GetStateAt` / `Diagnostics` | [Fusion/AsyncFusionEngine.cs](../Src/ARBot.Common/Fusion/AsyncFusionEngine.cs) | šev do EKF, časově zarovnaná póza, NIS diagnostika |
| `HeadingMeasurement`, gating (`GateMode.Reject`) | [Fusion/Measurements.cs](../Src/ARBot.Common/Fusion/Measurements.cs), `Fusion/Gating.cs` | kurzová korekce a obrana proti výstřelům |
| `MessageProcessor` (`DropOldest`) | `ARBot.Common` | vlastní vlákno a fronta korelátoru |
| Graf řad v čase | [telemetry-view.md](telemetry-view.md) | `d`, `φ`, `S`, σ v čase bez nové práce v UI |

### Ze staré generace robotu

V repozitáři jsou dvě předchozí implementace téhož nápadu. Nová je nepoužívá, ale tvarovaly ji:

- [MapCorelator.cs](../Src/ARBot.Common/Navigations/MapCorelator.cs) — FFT fázová korelace
  rastr↔rastr s gaussovským okénkem, **jen translace**, a kovariance počítaná z korelační plochy.
  Ten poslední nápad nová verze přebírá; FFT ne, protože chceme i rotaci a hledáme v malém okně
  (přímé skenování je tam levnější a lépe se ladí).
- [PathMapCorelator.cs](../Src/ARBot.Common/Navigations/PathMapCorelator.cs) — point-to-line ICP
  (Kabsch) okrajů vozovky proti hranám mapy. Umí i rotaci, ale potřebuje extrahovat okrajové body
  a asociace a má lokální minima. Nová verze skóruje **shodu ploch**, což je z hustého gridu
  přirozenější a nepotřebuje asociace.

**Pozn. k pojmenování:** stará generace píše `Corelator` (překlep), nová je `Correlator`. Ať se to
nepletlo, staré soubory se nepřejmenovávají ani nemažou (pravidlo CLAUDE.md).

## Architektura

`MapCorrelator` je **vlastní `MessageProcessor`, který odebírá `OccupancyGridMsg`** — ne kód uvnitř
`LocalNavigator`.

```
CameraFrame ─► LocalNavigator ─► OccupancyGridMsg ─► MapCorrelator ─► AxisOffsetMeasurement ×2
                  (grid)          (snapshot 500 ms)      │            HeadingMeasurement
                                                         │                    │
                                                         ▼                    ▼
                                                  MapCorrelationMsg    AsyncFusionEngine
                                                  (telemetrie, UI)
```

Proč zvenčí, přes snapshot:

- **Nekrade čas plánovači.** Tik `LocalNavigator` smí trvat 15 ms; korelace by se do něj nevešla.
- **Žádné zamykání gridu**, žádné sdílené vlákno. Snapshot je už v lokálním pořadí, takže korelátor
  neřeší kruhový buffer.
- **Zpráva je nahrávaná** ⇒ korelátor jde ladit a měřit nad záznamy reálných jízd bez opakovaného
  běhu vize. Pro kalibraci `α`, prahů a σ je to rozhodující.
- 1–2 Hz je pro korekci lokalizace dost; snapshot se emituje každých 500 ms.

Zpoždění snapshotu nevadí: měření je timestampované a fúze má historii (`GetStateAt`,
`historyWindow`) — musí být delší než perioda snapshotu.

Nová složka `ARBot.Common/Localization/`:

| Typ | Odpovědnost |
|---|---|
| `MapCorrelator : MessageProcessor` | cyklus: póza → rastr → důkazy → skenování → měření + zpráva |
| `MapCorrelatorConfig`, `ScanLevel` | prahy, rozsahy, úrovně skenování, kalibrační konstanty (`Validate()`) |
| `RoadRaster` | rastr `IsRoad` zarovnaný s gridem (bitové pole + převod souřadnic) |
| `EvidenceCloud` | důkazní buňky `(x, y, w)` vytažené z kanálu `LRoad` |
| `CorrelationScorer`, `ScanResult` | skóre jednoho kandidáta a hrubě-jemný sken |
| `CorrelationCovariance` | σ ze zakřivení skóre; dvě větve podle definitnosti `−H` |
| `MapCorrelationResult`, `MapCorrelationReason` | `(dx, dy, φ)`, `S`, σ, pravidla poslat/mlčet, `ToLogMessage()` |

Mimo `Localization` vznikne ještě `Occupancy/PoseJumpDetector` (pojistka popsaná v
[Zpětná vazba na grid](#zpětná-vazba-na-grid)) a `Fusion/AxisOffsetMeasurement`.

Směr závislostí: `Localization` → `Occupancy` (jen `OccupancyGridMsg`), `Maps/OsmNav`, `Fusion`.
Doménově na `Localization` nezávisí nikdo; jedinou výjimkou je registr telemetrických sloupců
(`Src/ARBot/Telemetry/TelemetryColumns.cs`), který si z něj bere výčet `MapCorrelationReason`, aby
uměl vypsat jméno hodnoty — stejně jako to dělá u `GlobalNavStatus`.

### `RoadScene` a jeho místo

`RoadScene` vzniklo pro virtuální kameru a leží v `Vision/Synthetic`. Teď má druhého, nezávislého
konzumenta a „lokalizace závisí na `Vision.Synthetic`" je špatná zpráva o architektuře. Součástí
práce je proto **přesun** do `Maps/OsmNav/Graph/` (mechanická změna namespace; `RoadSceneTests` už
existují a přesun potvrdí). Nic dalšího se na něm nemění.

## Algoritmus

Značení: póza z fúze je `p̂ = (x̂, ŷ)`, `θ̂`. Kandidátní transformace `(dx, dy, φ)` otočí celý oblak
důkazů **kolem robota** a posune ho:

```
q' = R(φ)·(q − p̂) + p̂ + (dx, dy)
```

Výklad nalezeného maxima: skutečná poloha je `p̂ + (dx*, dy*)`, skutečný kurz `θ̂ + φ*`.

### 1. Póza

`engine.GetStateAt(msg.TimeStamp)`. Když vrátí `null` (snapshot starší než okno historie), snímek se
**zahodí** — korelovat proti špatné póze je horší než nekorelovat. Toto je jediný prior: hledá se
lokálně v okně kolem `p̂`.

### 2. Rastr mapy

`IsRoad` se jednou za cyklus vyhodnotí do bitového pole zarovnaného s gridem a rozšířeného o
`MapRasterMarginM` na každou stranu — ale ten je jen **dolní hranicí**. Skutečná marže se dopočítá
z geometrie gridu, protože kandidát cloud nejen posouvá, ale i **otáčí** kolem robotu, a kovariance
navíc sonduje o `HessianStepM` dál za maximem:

```
marže ≥ SearchRangeM + HessianStepM + polodiagonála · sin(polovina okna kurzu)
```

Rotační člen se dřív neuvažoval a chyběl (zjištěno finální review 2026-08-19): při 8° a polodiagonále
gridu ~9,05 m se rohová buňka posune o ~1,26 m, což s 2,4 m posunu dá 3,66 m proti tehdejší marži
3,0 m. Důkazy mimo rastr se zahazují z čitatele **i** jmenovatele, a u extrémních kandidátů jsou to
převážně buňky **nesouhlasné** — jejich zahození proto skóre takových kandidátů **nadhodnocuje**, tedy
chyba v nesprávném směru. Testovací konfigurace tomu unikala o pár centimetrů, proto to žádný test
nechytil.

Pro produkční grid (256 buněk po 5 cm, ±8°) vyjde marže 3,96 m, tedy rastr 416 × 416 ≈ 173 k dotazů.
Pak je každý dotaz kandidáta index do pole, ne prostorový dotaz.

**Pozor — ten vzorec je hranice pro úroveň 0, ne skutečně nejhorší případ** (zjištěno při re-review).
Úrovně se re-centrují na maximum té předchozí, takže maximální výchylka kandidáta je **součet**
polovin oken (2,4 + 0,4 + 0,1 = 2,9 m) a maximální |φ| je 8 + 2 + 0,5 = 10,5°, plus 2° sondy
Hessiánu. Skutečná potřeba je pro produkční grid asi **5,06 m** proti použitým 4,0 m. Původní
zkreslení je tím výrazně zmenšené, ne odstraněné — a je to vedeno
v [Otevřených úkolech](#otevřené-úkoly), ne opraveno na poslední chvíli.

Rastr **není** volitelná optimalizace: s 2D translací a rotací je kandidátů řádově stovky a bez
rastru by to bylo milióny prostorových dotazů na cyklus.

Bod `q'` obecně nepadne do středu buňky; bere se nejbližší (při 5 cm je to pod rozlišením gridu).

### 3. Důkazní seznam

Průchod gridem, buňky s `|LRoad| ≥ EvidenceThreshold` se uloží jako `(x, y, w)`, kde `w = LRoad`:
**kladné = „mimo cestu", záporné = „cesta"** (konvence gridu: log-odds neprůjezdnosti). Kanál `Occ`
se **neúčastní** — jsou v něm věci, které v mapě nejsou (parkující auta, chodci, stromy), a ty by
odhad systematicky tlačily stranou.

Typicky jednotky tisíc buněk místo 65 536, takže cena skenování klesne o řád.

### 4. Skenování `(dx, dy, φ)`

Hrubě → jemně, na hrubé úrovni s podvzorkovanými důkazy:

| úroveň | krok posunu | krok kurzu | okno posunu | okno kurzu | důkazy | kandidátů |
|---|---|---|---|---|---|---|
| 0 | 0,4 m | 4° | ±2,5 m | ±8° | každý 4. | ~845 |
| 1 | 0,1 m | 1° | ±0,4 m | ±2° | všechny | ~405 |
| 2 | 0,05 m | 0,5° | ±0,1 m | ±0,5° | všechny | ~75 |

Každá úroveň hledá v okně kolem maxima z předchozí. Okno úrovně 0 je ±2,5 m schválně **menší než
typický rozestup souběžných cest** — je to první obrana proti přeskočení na vedlejší cestu.

**Remíza se rozhoduje ve prospěch STŘEDU okna** (doplněno 2026-08-19 po integračním testu). Na ploché
části skóre je remíza běžná — posun **podél** přímé cesty nezmění nic, co robot vidí, takže desítky
kandidátů mají skóre přesně stejné. Naivní „první vyhrává" pak vrátilo **okraj okna**: maximum se
přilepilo na `dx = −2,4 m` a korelátor sám sebe zamítl jako `OffsetTooLarge`. Správné pravidlo je
vzít kandidáta **nejblíž středu okna** (vzdálenost se měří v *krocích*, tedy bez jednotek, aby se
posun a kurz daly porovnat): když data nedávají důvod jednu z remízových možností preferovat, správná
odpověď je „neopravuj" — priorem je současný odhad pózy. Protože každá úroveň se re-centruje na
vítěze předchozí, preference se přenáší tranzitivně k nulové korekci.

**Konkurent se vzorkuje jemným krokem, ale hrubým stride.** Krok se bere z **nejjemnější** úrovně:
konkurent je úzký vrchol široký asi jako cesta, takže hrubý krok ho umí přeskočit (naměřeno: při
kroku 0,4 m se vzorkovalo 1,0 / 1,4 / 1,8 m a rival na přesně 2,0 m se skóre 0,958 se minul, takže
se použilo 0,625). Stride ale **zůstává z nejhrubší** úrovně — musí odpovídat podvzorkování
skóre v maximu, proti kterému se konkurent porovnává, jinak jsou to nesouměřitelná čísla. **Referenční
hodnotou je `CoarseStrideScoreAtPeak`** — skóre s hrubým stride vyhodnocené v JEMNÉM maximu, ne
`CoarsePeakScore` (maximum na hrubé mřížce). Ta se kvantizací o 0,4 m měří v jiném bodě, takže práh
vycházel systematicky mírnější: naměřeno 0,8583 místo zamýšlených 0,9000. Opraveno po finální review.

### 5. Skóre

```
mᵢ = −1  když rastr říká „cesta",  +1  jinak
S(dx, dy, φ) = Σ wᵢ·mᵢ / Σ|wᵢ|            ∈ ⟨−1, 1⟩
```

Maximalizuje se `S`. Normalizace dělá skóre **porovnatelné mezi cykly**, takže `S` slouží zároveň
jako **metrika kvality** — druhá se nevymýšlí. `S = 1` je dokonalá shoda, `S ≈ 0` „grid a mapa
o sobě nic neříkají", `S < 0` „shoda naopak" (typicky robot mimo mapovanou cestu).

### 6. Kovariance z Hessiánu

Okolo nalezeného maxima se nafituje 3D paraboloid a

```
C = −α · H⁻¹        H = Hessián S v maximu (3×3)
```

`α` je kalibrační konstanta (`S` není log-věrohodnost, takže zakřivení má správný *tvar*, ale ne
absolutní škálu) — ladí se nad záznamy. Z `C`:

- **Translační blok 2×2 → vlastní rozklad** → dvě ortogonální osy `û₁, û₂` a jejich σ. Na přímé
  cestě vyjde jedna σ malá (napříč) a druhá obrovská (podél) **samo**; u odbočky se sevřou obě.
  Nic se nedetekuje ani nepřepíná — anizotropie je naměřená, ne dekretovaná.
- **Marginální σ pro `φ`** z odpovídajícího prvku `C`, takže vazba `φ` ↔ translace je zohledněná.

**Singulární `H` je na přímé cestě NORMÁLNÍ STAV, ne chyba** (zjištěno 2026-08-19 při implementaci).
Posun podél přímé cesty nemění nic, co robot vidí, takže podélná druhá derivace vyjde **přesně
nula** — `−H` je pak jen semidefinitní a nedá se invertovat. To nesmí zahodit celý výsledek: příčná
složka je určená dál a je to hlavní výstup celé funkce. Proto se počítá dvěma cestami:

| Cholesky `−H` | Postup |
|---|---|
| **projde** | `C = α·(−H)⁻¹`, vlastní rozklad translačního bloku `C` |
| **spadne** | vlastní rozklad translačního bloku `−H` **přímo**, `σ = √(α/λ)`; plochý směr → `+∞` |

**σ jsou MARGINÁLNÍ v obou cestách.** Naivní varianta brala σ přímo z bloků `−H`, tedy **podmíněné**
σ. Protože Schurův doplněk je `⪯ A_tt`, jsou podmíněné σ systematicky **menší** než marginální —
a příliš malá σ je nebezpečná: fúze by korelátoru věřila víc, než si zaslouží. Navíc by σ při
přepnutí větve skočila. Řeší se Schurovým doplňkem v obou směrech (u kurzu s **pseudo**inverzí, aby
se plochý směr vynechal místo dělení nulou).

Prahy „plocho" jsou dva, **každý ve svých jednotkách** — `α / SigmaCeilingM²` pro translaci
[skóre/m²] a `α / SigmaCeilingHeadingRad²` pro kurz [skóre/rad²]. Míchat je nelze: při výchozích
hodnotách se liší 3283×.

Na σ platí dolní hranice (`SigmaFloor*`). Nekonečná σ je **legitimní hodnota**, ne chyba: zahodí ji
strop `SigmaCeilingM` a `MapCorrelationMsg` ji přenese i do záznamu.

**σ závisí i na kroku derivace, ne jen na `α`.** Skóre je „tent" (viz [otevřený úkol](#otevřené-úkoly)),
takže zakřivení je `≈ 1/h` a `σ ≈ √h`. Absolutní škála je dána dvojicí (`α`, `HessianStepM`) a ladí
se **spolu**; změna kroku přepočítá všechny σ. Relativní anizotropie na kroku nezávisí.

`NoPeak` je vyhrazený pro **skutečnou** degeneraci translačního bloku: žádné zakřivení v žádném
směru, nebo zakřivení obrácené na špatnou stranu (sedlo, minimum). Plochý směr sám `NoPeak` nedává.
Špatné zakřivení kurzu zahodí jen korekci kurzu, ne celý výsledek.

### 7. Přijetí, nebo mlčení

Cyklus **neposílá nic**, když (v tomto pořadí — pořadí je součást kontraktu a je testované):

| # | podmínka | proč |
|---|---|---|
| 1 | důkazů méně než `MinEvidenceCells` | příliš málo dat |
| 2 | `S < MinScore` | robot pravděpodobně není na mapované cestě |
| 3 | `‖(dx*, dy*)‖ > MaxOffsetM` | tolik se póza mýlit nemá; hlásí se ztráta lokalizace, ale **neskáče se** |
| 4 | žádné použitelné maximum | plocha, sedlo, šum |
| 5 | konkurent **podél určené osy** výš než `CoarseStrideScoreAtPeak − AmbiguityMargin` | nejednoznačné (souběžná cesta) |

Pořadí odděluje „nemám data" od „mám data a nesouhlasí". Nejednoznačnost je **poslední schválně**:
konkurent se měří podél určené osy, a ta bez maxima neexistuje.

**Konkurent se měří PODÉL URČENÉ OSY, ne ve 2D** (opraveno 2026-08-19, našel integrační test).
Na přímé cestě je kandidát posunutý **podél** cesty skóre přesně stejný jako maximum — posun podél
přímé cesty nemění nic, co robot vidí. To ale **není nejednoznačnost**: je to tatáž odpověď posunutá
ve směru, který odhad už prohlásil za neznámý (nekonečná σ volné osy), a ta osa se do fúze beztak
neposílá. Původní 2D měření proto vyrábělo falešnou nejednoznačnost na **každé** přímé cestě
a potlačovalo i dobře určenou příčnou korekci — tedy hlavní výstup celé funkce, v její nejčastější
situaci. Konkurent posunutý podél **určené** osy je naopak nejednoznačnost skutečná: právě tak se
projeví souběžná cesta.


**Zbytková mezera, kterou tohle neřeší.** Hlídají se dva směry — určená osa a osa na ni kolmá.
Konkurent, který neleží ani v jednom (například jinak orientovaná cesta dosažitelná jen se
současnou změnou kurzu), zkontrolovaný **není**. Zbytek je ohraničený rozsahem hledání ±2,5 m,
takže nejde o tichou díru přes celou mapu, ale úplná odpověď to není — a před zapnutím korekcí
na robotu (fáze 3) by se to mělo aspoň proměřit nad záznamy.

Osa, jejíž σ přeroste `SigmaCeilingM`, se **vynechá samostatně** — typicky podélná na přímé cestě.
Stejně tak `φ`, když σ přeroste svůj strop. Cyklus tedy běžně pošle jen příčnou osu, a u odbočky
obě plus kurz.

Důvod mlčení jde do zprávy jako výčtový `Reason`, aby bylo v telemetrii vidět **proč** se
nekorigovalo. Které osy se poslaly, nesou tři samostatné příznaky — viz [Zpráva](#zpráva).

## Napojení na fúzi

Nové skalární měření v `Fusion/Measurements.cs`:

```
AxisOffsetMeasurement(û, value, σ, t, source)
    h(x) = û · p          H = [ûx, ûy, 0, 0, 0]          R = [σ²]
```

Korelátor pošle **dvě** — po vlastních osách translační kovariance, každou se svou σ. Je to exaktně
totéž jako otočená diagonální `R`, ale bez maticové `R` (kterou EKF dnes neumí) a bez „obrovské
sigmy" jako triku: podélná σ je velká **naměřeně**. Kurz jde přes existující `HeadingMeasurement`
(`θ̂ + φ*`), reziduum už umí zabalit do ±π.

`Source` = `"MapCorr"`, ať jsou v `Diagnostics()` a v telemetrii korekce rozpoznatelné.

### Autorita korelátoru

- `GateMode.Reject` + `GateThreshold` — jeden výstřel robota neposune a zahození je vidět jako NIS.
- σ nastavená tak, aby oprava tekla **postupně** (jednotky cm za cyklus), ne skokem.
- Tvrdý strop `MaxOffsetM`: nad ním se nekoriguje vůbec. Skákat o metry je horší než přiznat, že
  nevíš.
- Dva přepínače: `mapcorr=` (počítat vůbec, default `false`) a `SendCorrections` (posílat do fúze).

### Přiznaná aproximace

Měření `z = û·(p̂ + d)` je spočtené **z** `p̂`, tedy z filtrovaného stavu — formálně jde o relativní
měření podané jako absolutní a filtr ho bere jako nezávislé. Inovace tím nezkreslená není:
`û·(p̂ + d) − û·p̂ = û·d`, tedy `p̂` se vykrátí a inovace je přesně naměřený posun. Nepřesné je
**účtování kovariance** — filtr bude o něco sebejistější, než mu patří. Standardní kompromis
u scan-matchingu proti známé mapě; obranou jsou dolní hranice σ a pomalá korekce, ne exaktní
korelační účetnictví.


### Časová korelace mezi cykly (druhá přiznaná aproximace)

Grid drží ~2,5 s historie, korelátor jede na 2 Hz. Po korekci tedy **asi pět cyklů za sebou** čte
grid, v němž jsou pořád buňky zapsané ještě *neopravenou* pózou. Každý z těch cyklů znovu naměří
odeznívající zbytek chyby, kterou už jednou opravil, a pošle ho do fúze **jako nezávislé měření**.

Dva důsledky: mírný překmit (geometrická řada, konverguje — nejde o divergenci) a kovariance, která
se zužuje **rychleji, než informace opravňuje**. Filtr si tedy věří o něco víc, než by měl, a to nad
rámec aproximace popsané výše. Zjištěno finální review 2026-08-19.

### Chybná kalibrace kamer: bias, který systém integruje

**Nejhorší případ té časové korelace** (zjištěno 20. 8. 2026 při rozvaze nad dnešním měřením).
Chyba extrinsiky kamer (`Profile.Left/RightCameraTransform`) posune celý bodový oblak, tedy i grid,
proti skutečnosti. Korelátor to naměří jako chybu pózy — a protože ta chyba **není odeznívající, ale
dokonale korelovaná napříč všemi cykly**, zatímco filtr měření bere jako nezávislá, efektivní σ klesá
jako `σ/√N` a bias **vyhraje vahou počtu**. Výsledek není šum kolem pravdy, ale **posunutá póza
držená s falešnou jistotou**.

Kamery jsou v montáži 0,52 m nad zemí, yaw ±29°, pitch ~−20°. Dopad chyby 1° na **vodorovnou** polohu:

| chyba montáže | dopad |
|---|---|
| **yaw 1°** | bod ve vzdálenosti *L* se posune příčně o *L·ε* → při dohledu 3–6 m **5–10 cm** |
| **translace** (poloha kamery na robotu) | **1:1**, přímý konstantní bias |
| **pitch / roll 1°** | jen ~`h·δ` = **9 mm** — hloubka se *měří*, takže rotací skutečných 3D bodů se vodorovná složka posune málo |

Pitch a roll jsou tedy pro *polohu* méně kritické, než by se čekalo. Mění ale **klasifikaci**
(vzdálená zem se zdánlivě zvedne nebo klesne), takže posouvají zdánlivé okraje cesty — to se
kvantifikuje horší a tady kvantifikované není.

> **Rozhodující srovnání:** příčnou chybu korelátor nachází s přesností **5 mm** (naměřeno vnucenou
> chybou 19. 8. 2026). Yaw kalibrovaný na 1° zavádí **5–10 cm**, tedy **o řádek víc než vlastní šum
> korelátoru**. Ta pětimilimetrová přesnost je proto bezcenná, pokud extrinsika není dobrá na
> **desetiny stupně**. Kalibrace není nice-to-have, je to předpoklad s odvozeným požadavkem — a je to
> pravděpodobně **dominantní chybový člen celé úlohy**, ne šum korelace.

**Padá to do téhož koše** jako posunutá mapa a bias GPS: tři různí přispěvatelé, jeden pozorovatelný
jev, z jednoho měření neoddělitelní. Potvrzuje to zásadu „neatribuovat, ohraničit" — **strop na
nesouhlas s GPS** (podmínka 3 v [Otevřených úkolech](#otevřené-úkoly)) chytá i tohle, takže dělá
dvojí službu.

> **Rozlišovací znak, který jde změřit hned:** bias z montáže je vázaný na **tělo** robota, takže se
> s kurzem **otáčí**; posun mapy je vázaný na **svět**, takže se neotáčí. Stačí robota otočit nebo ho
> nechat projet smyčku a sledovat hlášený nesouhlas ve světových souřadnicích — rotuje-li s kurzem, je
> to kalibrace; stojí-li, je to mapa. Nepotřebuje to nic nového.

Je to nejsilnější jednotlivý argument pro to, aby se teď při zapnutých korekcích prioritně měřilo
rozdělení NIS pro `Source = "MapCorr"` — u konzistentního filtru s gatingem na 95 %
χ²(1) má být zamítnutých kolem 5 %; výrazně víc znamená příliš malou σ.

### „Jednotky cm za cyklus" je naděje, ne vynucený invariant

Výše uvedené „σ nastavená tak, aby oprava tekla postupně" popisuje **záměr**, ne mechanismus.
`MaxOffsetM` omezuje **naměřený** posun, ne **aplikovaný** krok: při malé σ proti velkému `P` může
filtr aplikovat téměř celé dva metry v jednom updatu. Tvrdý limit na velikost korekce za cyklus
v návrhu **není** — viz [Otevřené úkoly](#otevřené-úkoly).

### Cirkularita: proč to není argument v kruhu

`LRoad` se zapisuje pózou platnou pro **ten** snímek ([LocalNavigator](../Src/ARBot.Common/Occupancy/LocalNavigator.cs)
si ji bere přes `GetStateAt` zvlášť pro každý snímek). Konstantní chyba pózy tedy posune **všechny**
důkazy stejně a korelace ji poctivě najde. Chyba, která se během akumulačního okna *mění*, důkazy
rozmaže — a to se projeví poklesem `S`, tedy tou samou metrikou kvality, která už v návrhu je.

## Zpětná vazba na grid

Korekce jde přes EKF, takže obsah gridu zapsaný starou pózou je vůči nové póze posunutý.

**Neresampluje se nic.** Grid má clamp ±5 a krátkou paměť (~2,5 s při 10 Hz) — to je právě ta
zabudovaná obrana proti nepřesné lokalizaci. Při korekcích v jednotkách cm za cyklus se to samo
vypere; resampling by naopak rozmazával.

Pojistka proti **skoku** patří do `LocalNavigator`, ne do korelátoru: ten už si pro každý snímek
bere `GetStateAt`, takže může porovnat pózu s předchozí a **když skočí víc, než vysvětlí rychlost,
zahodit grid** (`Clear()`). Lokální pravidlo bez nového drátu — a chrání nejen před korelátorem,
ale i před znovuzachycením GPS, které umí skočit o metry.

**Hlídá se posun i rotace** (rotace doplněna 19. 8. 2026 —
`Check(x, y, theta, v, omega, t)` proti `ToleranceM` a `ToleranceRad`, default 5°).

> **Opraveno tvrzení.** Do 19. 8. 2026 tu stálo, že „korekce kurzu grid nijak nepoškodí: je
> world-kotvený, jeho obsah se nerotuje". První část je pravda, závěr z ní ale neplatí — a právě
> z toho vada vznikla. Že se obsah **nerotuje**, je zdroj problému, ne jeho vyloučení: buňky
> zapsané starým kurzem zůstanou ležet tam, kde jsou, takže vůči zápisům s novým kurzem jsou
> posunuté o `R · dTheta`. Při dohledu ~6 m stačí 5°, aby ten posun překročil translační toleranci.
> Rotace tedy grid poškodí **víc** než posun stejné velikosti, ne méně.

## Chování při nejistotě

Když robot podle mapy není na žádné cestě (tráva, průjezd mezi budovami, úsek chybějící v OSM),
korelátor **mlčí** — `S` spadne pod `MinScore` a neposílá se nic. Žádná zvláštní detekce; použije se
ta samá metrika kvality.

Cena tohoto rozhodnutí: dlouhý úsek bez korekce se pozná jen z telemetrie, robot sám o tom stavu
nijak neuvažuje. Eskalace do stavu globální navigace („lokalizace nepodložená mapou") je vědomě
odložená — viz [Otevřené úkoly](#otevřené-úkoly).

## Konfigurace

`MapCorrelatorConfig` (výchozí hodnoty jsou **odhad k naladění nad záznamy**, ne měřená pravda):

| Parametr | Default | Význam |
|---|---|---|
| `SendCorrections` | `true` | posílat měření do fúze; **výpočet tím nevypneš** — na to je `mapcorr=false`. Z příkazové řádky `mapcorrsend=` |
| `EvidenceThreshold` | 0,4 | absolutní hodnota `LRoad`, od které buňka vstupuje do korelace |
| `MinScore` | 0,25 | pod tím korelátor mlčí |
| `AmbiguityMargin` | 0,10 | o kolik musí být maximum lepší než konkurent |
| `AmbiguitySeparationM` | 1,0 | od jaké vzdálenosti od maxima se konkurent hledá (začátek sweepu podél osy) |
| `MinEvidenceCells` | 400 | méně důkazů = nekoreluje se |
| `Alpha` | 0,05 | škála σ ze zakřivení (viz níž) |
| `HessianStepM` | 0,20 | krok numerické druhé derivace pro posun — **spolu s `α` určuje absolutní škálu σ** |
| `HessianStepHeadingRad` | 2° | totéž pro kurz |
| `SigmaFloorM` | 0,05 | dolní hranice σ posunu (rozlišení gridu) |
| `SigmaFloorHeadingRad` | 0,5° | dolní hranice σ kurzu |
| `SigmaCeilingM` | 5,0 | nad tím se osa nepošle |
| `SigmaCeilingHeadingRad` | 5,0° | nad tím se kurz nepošle |
| `MaxOffsetM` | 2,0 | nad tím se nekoriguje a hlásí se ztráta lokalizace |
| `MapRasterMarginM` | 4,0 | rozšíření rastru za hranu gridu (dolní hranice; skutečná marže se dopočítá z geometrie — viz níž) |
| `MeasurementSource` | `"MapCorr"` | jméno zdroje v `Diagnostics()` a v telemetrii |
| `MinPeriod` | 400 ms | ochrana proti zahlcení, kdyby snapshoty chodily hustěji |
| `Levels` | 3 úrovně | rozsahy a kroky skenování; `SearchRangeM` je z nich **derivovaná** (`Levels[0].HalfRangeM`), ne samostatný parametr |

**K `Alpha`:** výchozí 0,05 je zvolená tak, aby zakřivení `∂²S/∂d² = −1 m⁻²` dalo σ ≈ 0,22 m — tedy
u mělkého maxima nedůvěřivá korekce v řádu decimetrů. Není to odvozená hodnota (`S` není
log-věrohodnost), je to **startovní bod pro fázi 4**; ladí se porovnáním rozptylu `Dx`/`Dy`
v telemetrii proti σ, kterou korelátor hlásí.

## Zpráva

`MapCorrelationMsg` (vyrábí `MapCorrelationResult.ToLogMessage()` — konvence CLAUDE.md, doména si
vyrábí svou zprávu):

`TimeStamp` (čas snapshotu) · `Dx`, `Dy`, `Phi` · `Score`, `SecondBestScore` · `SigmaTight`,
`SigmaLoose`, `TightAxisAngle`, `SigmaPhi` · `EvidenceCells`, `Candidates` · `Emitted`,
`EmitTightAxis`, `EmitLooseAxis`, `EmitHeading`, `Reason` · `ProcessingMs` · `DroppedByFusion`
(verze 2). Konkurent se nese dvakrát: `SecondBestScore` podél určené osy a `SecondBestScoreLoose`
podél volné. `DroppedByFusion` je **zpětná vazba z fúze**, ne výsledek korelace — doplňuje ji
korelátor po odeslání (viz [Přístroje](#přístroje-verdikt-měření-a-zpětná-vazba-o-zahození)).

**Per-osové příznaky nesou vlastní informaci, nestačí souhrnné `Emitted`** (doplněno 2026-08-19 po
review). Normální stav na přímé cestě je „poslala se příčná korekce, podélná se vynechala kvůli
stropu σ" — a to je při `Reason = Ok` a `Emitted = true` k nerozeznání od „poslalo se všechno",
pokud se každá osa nehlásí zvlášť. Přesně tohle je otázka, kterou se telemetrie ptá při ladění
stropů nejčastěji.

V [telemetrickém pohledu](telemetry-view.md) to znamená sloupce a řady bez nové práce v UI: `Dx`,
`Dy`, `Phi`, `Score`, σ, `Reason`, trojice příznaků, oba konkurenty a `TightAxisAngle` v čase,
srovnané s pózou a stavem globální
navigace. Sloupec `korel os+` (podélná osa) by měl na přímé cestě být **vypnutý**; když svítí
trvale, něco předstírá podélnou jistotu.

## Předpoklady a rizika

**Podélná lokalizace stojí na tom, že se odbočka v `LRoad` vůbec objeví.** Otevřený úkol
z [occupancy-and-local-planning.md](occupancy-and-local-planning.md) — *„není okluzní pravidlo
`InShadow` příliš přísné?"*, které v měření zahodilo ~5 200 z ~12 000 barevných vzorků — tím
přestává být kosmetika a stává se **přímým limitem téhle funkce**. Příčná korekce na něm závisí
mnohem méně (na okraje cesty vedle robota kamera vidí), podélná hodně.

Další:

| Riziko | Dopad | Co s ním |
|---|---|---|
| Šířky cest v OSM jsou často odhad | příčný odhad vychýlený o polovinu chyby šířky | chyba je symetrická, takže osu cesty to neposune; hlídat v telemetrii |
| Souběžná cesta blíž než `SearchRangeM` | přeskočení na vedlejší cestu | omezené okno + `AmbiguityMargin` + gating; ve výsledku detekovatelné jako skok |
| Výkon na ARM (OrangePI) | korelace nestíhá 2 Hz | `DropOldest` frontou to degraduje bezpečně; pyramida rastru je nevyužitá páka — **viz naměřené hodnoty níž** |
| `α` naladěné na jednom prostředí | přecenění nebo nedocenění korekce jinde | σ hranice a `GateMode.Reject` drží dopad omezený |
| Mapa posunutá vůči GNSS rámci | systematická „korekce" všude stejná | korelátor to nepozná; vyloučit porovnáním záznamu s ortofotem |
| **Chybná kalibrace kamer (extrinsika)** | systematický bias, který **systém integruje** — viz níž | strop na nesouhlas s GPS; rozlišit od posunu mapy otočením robota |

### Naměřená doba cyklu (2026-08-19, x64, virtuální HW, ~22 000 důkazních buněk, 1 325 kandidátů)

| Build | `korel vypocet` průměr | max | zpracováno snapshotů |
|---|---|---|---|
| **Release** | **126,5 ms** | 169,9 ms | 69 ze 70 |
| Debug | 696,3 ms | 828,7 ms | 55 ze 70 (21 % zahozeno) |

Dvě věci z toho plynou. Za prvé **měřit se smí jen Release** — Debug je 5,5× pomalejší a sám o sobě
přeteče periodu snapshotu (500 ms), takže `DropOldest` začne zahazovat. Za druhé komentář u fronty
v `ARBotRuntime` odhaduje „na ARM 100–200 ms": jenže 126 ms je hodnota **z desktopového x64**, ne
z OrangePI. Odhad v komentáři je proto nejspíš optimistický o celý řád velikosti stroje — změřit
na zařízení (fáze 5) dřív, než se korekce zapnou. Náklad roste lineárně s `korel bunek`
(1 932 buněk → 72 ms, 22 000 → 696 ms v Debugu), takže pyramida rastru i `Stride` jsou reálné páky.

### Latence korekce proti oknu historie EKF

> **Čísla v této podsekci jsou z 19. 8. 2026, kdy bylo okno historie 1 s.** Od 20. 8. 2026 je
> `FusionConfig.HistoryWindow` **3 s**, takže absolutní rezervy níž už neplatí — mechanismus
> a poměr Debug/Release ano. Nová měření jsou v [Naměřeno 21. 8. 2026](#naměřeno-21-8-2026-debug-vs-release-nad-dvěma-záznamy).

Korekce se stempluje **časem snapshotu gridu** (`r.TimeStamp`), ne časem zařazení — fúze pracuje
podle času pořízení, takže je to správně. Znamená to ale, že do EKF dorazí stará o celou dobu cesty,
a `FusionConfig.HistoryWindow` byl tehdy **1 s**. `Prune()` posouvá `tBase` na „nejnovější měření − okno";
IMU jede ~75 Hz, takže nejnovější měření je prakticky *teď* a uzávěrka je skutečně 1 s od snapshotu.

Naměřeno z indexu záznamu (`ArrivalTicks − CaptureTicks`):

| build | `MapCorrelationMsg` p50 / p95 / max | nad 1 s | využití fronty |
|---|---|---|---|
| **Debug** | **1 427** / 1 714 / 1 807 ms | **51 z 55** | 696/500 = **1,39** |
| **Release** | 194 / 252 / 294 ms | 0 z 69 | 126/500 = 0,25 |

Rozpad v Release (p50 → max): snapshot gridu na `Stream` 88 → 177 ms, plus čekání ve frontě
a výpočet 122 → 195 ms, celkem 194 → 294 ms. Rezerva k oknu tedy ~3,4×.

> **⚠️ Zahození je dnes NEVIDITELNÉ.** `AsyncFusionEngine.Enqueue` starší měření zahodí
> (`if (m.TimeStamp <= tBase)`) a jen zaloguje `Debug.WriteLine` — což je `[Conditional("DEBUG")]`,
> takže v **Release neprojde nikam** a žádné počítadlo neexistuje. Telemetrie přitom dál hlásí
> `Reason = Ok` a `korel os-` svítí, takže by to vypadalo, že funkce jede, i kdyby fúze zahazovala
> všechno. Před měřením na OrangePI je potřeba to zviditelnit, jinak měření nic nerozliší.

**Proč je rozdíl Debug/Release tak velký** (změřeno izolovaně, tentýž snapshot, shodné skóre):
samotné `CorrelationScorer.Scan` trvá 523–583 ms v Debugu proti 118–131 ms v Release, tedy **4,3×**.
Je to vlastnost tvaru téhle práce, ne obecný poměr: horká smyčka `Score` udělá ~10–12 milionů
iterací za cyklus a v každé sáhne třikrát do pole přes property (`cloud.X/Y/W`) a zavolá
`raster.TryIsRoad`. V Release se to všechno **inlinuje**, lokály zůstanou v registrech a část
kontrol rozsahu zmizí; v Debugu (`DebuggableAttribute(DisableOptimizations)`) je z každého přístupu
skutečné volání a lokály se odkládají na zásobník, aby je debugger uměl zobrazit. Kód, který čas
tráví v několika velkých voláních (I/O, dekomprese obrazu), by se takhle nelišil.

**Latence se ale zhorší víc než výpočet** (7,4× proti 5,5×) — a tohle je ta důležitá část:
v Debugu cyklus (696 ms) **přeteče periodu snapshotu** (500 ms), takže využití je 1,39, fronta se
zasytí a každý snapshot čeká na předchozí. Naměřený rozdíl to potvrzuje: 228 (latence gridu)
+ 696 (výpočet) = 924 ms, skutečnost 1 427 ms — chybějících ~500 ms je právě jedna perioda čekání.

> **Návrhový důsledek pro OrangePI:** cíl **není** „cyklus pod 1 s". Jakmile se cyklus přiblíží
> **periodě snapshotu (500 ms)**, přiskočí k latenci celá perioda čekání a okno 1 s se prolomí
> **skokem, ne postupně**. Bezpečný cíl je proto cyklus **pohodlně pod 500 ms**. Poměr 4,3× mezi
> Debugem a Release na ARM extrapolovat nelze — to je jiná osa (slabší jádra, menší cache).

### Naměřeno 21. 8. 2026 (Debug vs Release) nad dvěma záznamy

Podnět: autor pořídil záznam `records/20260821-085733.rec` s tím, že *„korelace se dle mého názoru
zahazuje kvůli velké latenci, odhad vlivu 1:400 nebude taky úplně reálný a korelace chodí velmi
řídce oproti GPS."* Změřeno nad indexem záznamu (`ArrivalTicks − CaptureTicks`) a dekódovanými
`MapCorrelationMsg` + `Info`; kontrolní běh v Release je `records/20260821-090853.rec`
(30 s, self-test, `virtualhw` + `visionmap`, tedy dvě mapy).

| | **Debug** (`…085733`, 28,5 s) | **Release** (`…090853`, 30,0 s) |
|---|---|---|
| cyklů korelace | 27 (**1,03 Hz**) | 53 (**1,74 Hz** = každý snapshot) |
| doba cyklu (`korel vypocet`) | 180 → **1 805 ms** (roste) | 62 → **104 ms** (plateau) |
| cena na důkazní buňku | **~36 µs** | **~5,3 µs** |
| důkazních buněk | 3 133 → **48 800** | 3 133 → **17 400** |
| latence korekce (p50 / max) | **1 756 / 3 320 ms** | **179 / 314 ms** |
| poslaných měření | 72 | 158 |
| **zahozeno fúzí jako starší okna** | **12 (17 %)** | **0** |

**Zahazování potvrzeno — ale je to Debug.** Ve sporném záznamu fúze zahodila 12 měření
z 5 posledních 6 cyklů, opoždění 3 031–3 225 ms proti oknu 3 000 ms (hlášky `[Fusion] zahozeno
mereni starsi nez okno historie` jsou v záznamu jako `Info`). Že jde o Debug build, se z toho
záznamu pozná: nese hlášku `Run + zaznam do:`, která jde z `Debug.WriteLine`, tedy
`[Conditional("DEBUG")]`. Skórovací smyčka je v Debugu ~6,8× dražší na buňku (36 vs 5,3 µs),
takže v Release stejná zátěž (48 800 buněk) vyjde na ~260 ms a do okna se vejde s velkou rezervou.
Platí tedy varování z předchozí podsekce: **měřit jen Release**.

**Co ale zůstává i v Release:** cena roste lineárně s počtem důkazních buněk a ten roste s ujetou
dráhou (grid je world-kotvený kruhový buffer, LRoad se z buňky bez opačného důkazu neztrácí).
17 400 buněk × 0,1 m = **174 m² důkazu** proti ~18 m², které kamera vidí *teď* — devět desetin
důkazu je historie zapsaná staršími pózami. Na ARM (~5–10× slabší jádro) je 17 000 buněk už
~0,5–1 s, tedy přesně v pásmu, kde podle předchozí podsekce latence přeskočí o celou periodu
snapshotu. **Strop na počet důkazních buněk (`Stride`, nebo okno kolem robotu) je tedy potřeba
bez ohledu na build.**

**Řídkost proti GPS — potvrzeno, číslo:** GPS jde **5,00 Hz**, korelace **1,74 Hz** v Release
(1,03 Hz v Debugu). Poměr měření na sekundu je tedy ~1 : 2,9 (Debug 1 : 4,9).

**Odhad „400:1" je nadsazený asi o řád.** Rozpad rozdílu proti
[decisions.md](decisions.md) (`(2,12/0,105)² ≈ 408`):

| krok | činitel | poměr |
|---|---|---|
| původní odhad | | **408 : 1** |
| σ GPS je **per osu 1,5 m**, ne 2,12 m (to je 2D radiální = 1,5·√2), zatímco osové měření korelace je 1D | ÷2 | 204 : 1 |
| σ korelace naměřená v tomto běhu je **0,150 m** (medián `sTight`), ne 0,105 | ×(0,105/0,150)² | 100 : 1 |
| **kadence**: 1,74 Hz proti 5,00 Hz | ×0,348 | **35 : 1** |
| volná osa (`sLoose` ≈ 0,21 m) místo těsné | | **~18 : 1** |
| Debug (5 z posledních 6 cyklů zahozeno) | | **0** v posledních 11 s |

A i těch 35:1 je **strop, ne skutečnost**: cykly nejsou nezávislé (viz
[Časová korelace mezi cykly](#časová-korelace-mezi-cykly-druhá-přiznaná-aproximace)) — sousední
cyklus koreluje z **téhož** nahromaděného oblaku, takže 53 měření za 30 s nenese 53 nezávislých
informací, ale fúze je tak bere. To je věcně [otevřený úkol č. 1 (honestní σ)](#otevřené-úkoly)
z druhé strany: chyba není jen v hodnotě σ, ale i v počtu měření, kterými se σ dělí.

**Pozorování, které stojí za vysvětlení:** v Release běhu bylo 158 měření přijato (0 zahozeno,
všech 53 cyklů `Ok`), a přesto **hlášený posun neklesá** — `dx` drží 0,35–0,50 m a `φ` 1,0–2,5°
po celých 30 s bez klesajícího trendu. Kandidáti na vysvětlení: (a) měření je z devíti desetin
historie, takže korekce pózy se do dalšího cyklu propíše jen málo — smyčka má velmi dlouhý chvost;
(b) gating měření zahazuje (NIS nad prahem); (c) GPS to táhne zpět (při 35:1 by ale musela být
skutečná chyba mapy ~7 m, což je vylučuje). **Rozhodnout mezi (a) a (b) z dnešního záznamu nelze**
— viz chybějící přístroje.

**Chybějící přístroje** — ~~bez nich se to dál ladit nedá~~ **doplněno 21. 8. 2026**, viz
[Přístroje](#přístroje-verdikt-měření-a-zpětná-vazba-o-zahození) a A/B měření níž:
- ~~`MeasurementDiagMsg` nikdo nepublikuje~~ → publikuje `FusionProcessor` za parametrem `measdiag=`,
  a nese navíc **verdikt** (`Accepted` / `GatedOut` / `TooOld`), protože samo „nepřijato" nerozliší
  „přišlo pozdě" od „zamítl gating".
- ~~`DroppedTooOld` nejde do telemetrie~~ → `MapCorrelationMsg.DroppedByFusion` (kumulativně, vždy,
  bez parametru) + sloupec „korel zahozeno fuzi" v telemetrickém pohledu.
- ~~`SendCorrections` nemá parametr~~ → `mapcorrsend=`.

### Přístroje: verdikt měření a zpětná vazba o zahození

**`MeasurementDiagMsg` (verze 2, publikuje se za `measdiag=`).** U každého měření, které projde
fúzí: zdroj, `z`, diagonála `R`, NIS a **verdikt**:

| verdikt | co znamená | co s tím |
|---|---|---|
| `Accepted` | měření se aplikovalo | — |
| `GatedOut` | přišlo včas, ale NIS přerostl práh gatingu | σ je moc optimistická, nebo model nesedí |
| `TooOld` | přišlo starší než okno historie, do filtru vůbec nevstoupilo | zkrátit výpočet, ne ladit σ |

Zdroj měření nese `INamedMessage.Name`, takže se řádky v indexu záznamu i v telemetrii rozliší
podle zdroje (jako „Left"/„Right" u kamer).

> **Verdikt chodí opožděně o okno historie** (3 s). Do té chvíle není konečný: kdykoli dorazí
> starší měření (out-of-sequence), NIS i přijetí se přepočítají. Hlásí se proto až ve chvíli, kdy
> uzel z okna **vypadává** — `TooOld` je jediná výjimka, ta je konečná hned (měření do bufferu
> nevstoupí). Důsledek: poslední okno běhu se v záznamu neobjeví.

**`MapCorrelationMsg.DroppedByFusion`** (verze 2) je kumulativní počet korekcí z korelace, které
fúze zahodila jako starší než okno. Je **vždy**, bez parametru — právě proto, že past byla
„`Reason = Ok` svítí, a do fúze nedojde nic".

> **Prahové překvapení k `TooOld`.** Měření se zahodí při `m.TimeStamp <= tBase`, a `tBase` je čas
> **posledního uzlu, který z okna vypadl** — ne „nejnovější mínus okno". Když měření dorazí hodně
> pozdě, ale `tBase` je ještě daleko vzadu (buffer se nestihl posunout), měření se **vloží
> a hned zapeče do báze** — tedy se použije, jen se z něj nestane trvalý uzel. Práh je proto
> volnější, než se z okna zdá; při hustém provozu (IMU 100 Hz) `tBase` dohání a rozdíl je malý.
> *(Napsáno až po tom, co na to spadl test — původní odhad prahu byl přísnější než skutečnost.)*

### A/B: skutečná autorita korekcí (21. 8. 2026, Release, dvě mapy)

Dva běhy 30 s, **stejná zátěž** (korelace počítá v obou), jediný rozdíl je `mapcorrsend=`:

| | A (`mapcorrsend=true`) | B (`mapcorrsend=false`) |
|---|---|---|
| korekcí do fúze (`MeasurementDiagMsg`) | 146 | 0 |
| z toho **`Accepted`** | **126 (86 %)** | — |
| z toho `GatedOut` | 20 (14 %), NIS max 11,3 | — |
| z toho `TooOld` | **0** | — |
| `DroppedByFusion` na konci | 0 | 0 |
| hlášené \|dx\| (0–10 / 10–20 / 20–30 s) | 0,411 → 0,383 → **0,376** | 0,414 → 0,400 → **0,400** |
| póza proti druhému běhu (t = 30 s) | — | **1,90 m** rozdíl |

**Co z toho plyne:**

1. **Gating není ta zácpa.** 86 % korekcí se aplikuje, `TooOld` je v Release nula. Hypotéza (b)
   z rozboru výš tedy padá.
2. **Korekce autoritu mají.** Trajektorie se mezi A a B rozejde o **1,6–1,9 m**, což je řádově
   víc než vnucený rozdíl map (~0,4 m) — póza se hýbe a robot podle ní jede jinudy. *(Pozor na
   interpretaci: robot jede autonomně, takže se malý posun pózy zesílí jinou volbou trasy. Číslo
   dokládá „korekce mají vliv", ne „vliv je takhle velký".)*
3. **A přesto hlášený posun neklesá** — za 30 s a 126 přijatých korekcí z 0,411 na 0,376 m, tedy
   **8 %**, kdežto bez korekcí drží 0,40 m. Zbývá tedy hypotéza (a): důkazní oblak je z devíti
   desetin **historie** zapsaná staršími pózami, takže korekce se do dalšího cyklu propíše jen
   málo a smyčka má velmi dlouhý chvost. Potvrdit se to dá zkrácením paměti důkazů (okno kolem
   robotu) — to je zároveň lék na rostoucí cenu cyklu, viz výš.
4. **14 % zamítnutých gatingem s NIS až 11,3** je nezávislý doklad, že σ korelace je moc
   optimistická — [otevřený úkol č. 1 (honestní σ)](#otevřené-úkoly) potřetí, tentokrát přímo
   z čísla NIS.

### Vnucená chyba pózy — měření se známou odpovědí

Aby šlo ověřit, že korelátor chybu **najde** (a ne jen že si ji nevymýšlí), umí virtuální HW
renderovat z pózy posunuté proti té, kterou je ukotvený grid: `poseerror=vpřed,vlevo[,stupně]`
na příkazové řádce, nebo za běhu nástroj nad virtuální kamerou. Obsah gridu se tím proti mapě
posune o známou hodnotu a korelátor **musí ohlásit právě ji**. Mechanismus a rámec:
[virtual-hw.md](virtual-hw.md#umělá-chyba-pózy-poseerror).

**Naměřeno 19. 8. 2026** (Release, `OSM/HajeRovne.osm`, robot stojí, ustálený stav od 6. cyklu).
Příčná složka se rozkládá podle **kurzu robotu**, ne podle `TightAxisAngle` — viz varování níž.

| vnuceno | příčně naměřeno / očekáváno | `Phi` naměř. / oček. | závěr |
|---|---|---|---|
| 0,5 m **vlevo** | 0,5050 / 0,5000 m (+5,0 mm) | — | ✔ najde |
| 0,5 m **vpravo** | −0,4972 / −0,5000 m (+2,8 mm) | — | ✔ znaménko symetrické |
| 0,5 m **vpřed** | 0,0000 / 0,0000 m | — | ✔ podélnou nehlásí ani nevymýšlí |
| **3° kurz** | 0,0034 / 0,0000 m | 3,00° / 3,00° | ✔ kurz přesně |

Tím je poprvé doložené, že příčný odhad i odhad kurzu mají **správné znaménko i velikost**
(chyba jednotky milimetrů). Podélný směr zůstává na přímé cestě neurčitelný — to je vlastnost
úlohy, ne vada.

> **⚠️ `TightAxisAngle` je vychýlená o −6,3°** proti skutečné kolmici na cestu (naměřeno shodně ve
> všech čtyřech bězích: −6,17 až −6,48°). Kdo podle ní rozkládá hlášený posun na složky, dostane
> u velké podélné nejednoznačnosti nesmysl: při `D = (−1,650; −0,800)` vyjde příčná složka 0,695 m
> místo skutečných 0,505 m, tedy **o 40 % víc**. Podélná neurčitost totiž posadí vrchol klidně
> 1,7 m podél cesty a těch 6,3° z toho do příčné složky prosákne. Je to další projev téhož
> rozbitého fitu Hessiánu jako u [otevřeného úkolu č. 1](#otevřené-úkoly).

## Testování

Jádro se testuje **bez HW a bez vize**: ruční `RoadNetwork` (přímá cesta, ohyb, T-křižovatka) →
`RoadScene`, grid naplněný programově ze **známé** pózy posunuté o zadané `(dx, dy, φ)`, a korelátor
to musí najít v toleranci.

Testy, které hlídají přímo tvrzení návrhu:

| Test | Co drží |
|---|---|
| návrat známého posunu a kurzu (přímá, ohyb, křižovatka) | základní správnost |
| **anizotropie**: přímá cesta ⇒ `σ_podél ≫ σ_napříč`; T-křižovatka v záběru ⇒ obě σ malé | jádro celého slibu |
| dvě souběžné cesty ⇒ nic se nepošle | `AmbiguityMargin` |
| pod robotem není cesta ⇒ `S < MinScore` ⇒ nic se nepošle | mlčení mimo mapu |
| `GetStateAt` vrací `null` ⇒ snímek zahozen | žádná korelace proti špatné póze |
| nad `SigmaCeiling` ⇒ osa vynechána | autorita (dolní hranice `SigmaFloor` **testem procvičená není** — naměřené hodnoty leží nad ní) |
| `AxisOffsetMeasurement`: `H`, `h(x)`, reziduum | šev do EKF |
| skok pózy ⇒ detektor ho pozná | pojistka zpětné vazby, **posun i rotace** (že na to `LocalNavigator` reaguje `Clear()`, **testem ověřené není** — detektor se testuje izolovaně) |
| rotace nevysvětlená `omega` ⇒ skok | regrese na první chybnou korelaci; k tomu že běžné zatáčení, šum kurzu (~0,7°/100 ms) ani přechod přes ±180° skok nehlásí |

Nad virtuálním HW: `VirtualHwOccupancyTest` už existuje — injektovat známou chybu pózy a ověřit, že
ji korelátor srazí. Nad záznamy reálných jízd: `MapCorrelationMsg` v telemetrii a graf `d`, `φ`, `S`,
σ v čase.

Build i testy vždy `-p:Platform=x64`; měření na cílovém HW pod `OrangePI`.

## Fáze

| # | Co | Řídí? |
|---|---|---|
| 1 | přesun `RoadScene`, jádro `Localization`, syntetické testy | ne |
| 2 | `MapCorrelationMsg`, telemetrie, běh nad záznamy | ne |
| 3 | `AxisOffsetMeasurement` + napojení na fúzi za přepínačem (default vypnuto) | ano |
| 4 | ladění `α`, prahů a σ nad záznamy a virtuálním HW | ano |
| 5 | měření na OrangePI, ověření na HW | ano |

Implementační kroky: [plan-map-correlation.md](plan-map-correlation.md).

## Otevřené úkoly

- **⚠️ Falešná podélná jistota na cestě pod úhlem k osám gridu** (naměřeno 2026-08-19, **neopraveno**).
  Na šikmé **přímé** cestě vychází `SigmaLoose` konečná — 0,1848 m — přesto že přímá cesta žádnou
  podélnou informaci nenese. Je to „jistěji" než skutečná T-křižovatka (0,2943 m) a hluboko pod
  stropem `SigmaCeilingM`. Reálné cesty nejsou zarovnané s osami gridu, takže to postihuje skoro
  každou jízdu.

  **Ale: v praxi to už z velké části hlídá kontrola konkurenta na volné ose** (zjištěno finální
  review 2026-08-19). Na šikmé přímé cestě je konkurent posunutý **podél** cesty skóre remízový, a
  protože se volná osa hlídá zvlášť (viz [Přijetí, nebo mlčení](#7-přijetí-nebo-mlčení)), podélná
  korekce se **nepošle** — mitigace tu vadu mimoděk neutralizuje. Je to ale **hlídač, ne důkaz**:
  na zakřivené cestě nebo u odbočky mimo mřížku může remíza konkurenta zmizet, zatímco σ zůstane
  chybně malá, a pak se korekce pošle. Před zapnutím korekcí (fáze 3) je proto pořád **nejzávažnější
  otevřený úkol celé funkce** — jen ne tak akutní, jak tvrdila původní verze tohoto odstavce.
  Sledovat se to dá sloupcem `korel os+`: na přímé cestě má být zhasnutý, a `korel konk+` řekne,
  jestli ho zhasl strop σ, nebo právě ten hlídač.

  Naměřená data (aby je nikdo nemusel zjišťovat znovu):

  | Měření | Hodnota |
  |---|---|
  | skóre **podél** šikmé cesty, sweep ±1,0 m | rozsah **přesně 0** (plocho) |
  | skóre **napříč** šikmé cesty, tentýž sweep | rozsah 0,410 |
  | `SigmaLoose` při `HessianStepM` = 0,2 / 0,4 / 0,8 / 1,6 m | 0,1848 / 0,2613 / 0,3695 / 0,5222 |
  | surové druhé diference (šikmá cesta) | `sxx = syy = −2,9297`, `sxy = +1,4648`, `sxp = syp = 0` |

  **Potvrzeno za běhu aplikace** (2026-08-19, virtuální HW nad `OSM/HajeRovne.osm`, robot stojí,
  40 s, Release i Debug shodně). Vada není omezená na výrazně šikmé cesty: určená osa vyšla
  `93,6°`, tedy cesta leží jen **~3,6° mimo osu gridu** — a `SigmaLoose` přesto vyšla **konečná ve
  všech 69 cyklech** (≈ 0,32 m), ani jednou `+∞`. Předpoklad „na přímé cestě `korel sig+` bývá
  `+∞`" tedy v praxi neplatí skoro nikdy; stačí několik stupňů natočení.

  **Hlídač konkurenta drží — kromě malého důkazního oblaku.** Podélná korekce se nepustila v 68
  z 69 cyklů (`korel os+` zhasnutý), přesně jak popisuje mitigace. Pustila se tam, kde je oblak
  malý. Je to **třetí naměřená stopa téže vady**, ne samostatný problém — proto je popsaná tady
  a ne jako vlastní úkol. Podrobně [Proč malý oblak obelže hlídač](#proč-malý-oblak-obelže-hlídač) níž.

  **Příčina prvního cyklu dohledána (19. 8. 2026) — není to řídký důkaz, je to znečištěný grid.**
  „Málo buněk" byla jen souběžná okolnost. Skutečný řetěz:

  1. `AsyncFusionEngine.InitializePosition` inicializovala **jen X a Y** — `Theta` ne. Kurz proto
     startoval na 0 a ke skutečné hodnotě došel až přes `HeadingMeasurement`; na `HajeRovne.osm`
     je to swing ~170°. *(Od 19. 8. 2026 už ne — viz `InitializeHeading` níž.)*
  2. `LocalNavigator` mezitím normálně fúzuje snímky do **world-ukotveného** gridu pózou v čase
     snímku. Se kurzem u nuly se obsah ukládá skoro obráceně.
  3. Pojistka na tohle existovala, ale byla **o jeden argument krátká**: `PoseJumpDetector.Check`
     dostával jen polohu a `v`. Rotace o 170° u stojícího robotu dá `moved ≈ 0`, takže skok
     nehlásila a grid se nezahodil.

  Měřená stopa v prvním snapshotu: důkaz leží **0,75–5 m za** robotem (od druhého snapshotu
  0,24–6,45 m **před** ním), a to jako zorný kužel s vrcholem u robota mířící dozadu — u robota
  úzký, s odstupem širší, tedy zápis proběhl s kurzem blízko 0°. Třída „cesta" přitom zabírá
  **5,00 m** napříč, zatímco od druhého snapshotu přesně **3,00 m** (= `roadwidth` mapy) —
  na tři metry široké cestě je 5 m důkazu geometricky nemožné, pokud jsou buňky umístěné
  konzistentně.

  Důsledek pro korelaci: `TightAxisAngle` vyjde v prvním cyklu o **−51° až −89°** mimo kolmici na
  cestu (v ustáleném stavu −6,3°), takže „lépe určená osa" míří prakticky **podél** cesty — do
  směru, který nese nejmíň informace.

  | vnuceno | 1. cyklus: příčně naměř. / oček. | odchylka osy | `korel os+` |
  |---|---|---|---|
  | 0,5 m vlevo | **−0,484** / +0,500 | −88,9° | svítí |
  | 0,5 m vpravo | **+0,394** / −0,500 | −51,4° | svítí |
  | 0,5 m vpřed | +0,095 / 0,000 | −77,1° | svítí |

  U obou příčných posunů má hlášená složka **opačné znaménko** než pravda a velikost skoro
  správnou — tedy přesně ten tvar chyby, který by fúzi odtlačil o ~1 m **špatným směrem**. Od
  druhého cyklu je všechno v pořádku.

  **✅ OPRAVENO** (19. 8. 2026), dvěma zásahy — druhý je ten podstatný:

  1. **`PoseJumpDetector` hlídá i rotaci** (viz [Zpětná vazba na grid](#zpětná-vazba-na-grid)).
     Zabírá demonstrovatelně — grid se zahodí a objeví se cyklus s `Reason = TooFewEvidence`, který
     neodešle nic. Samo o sobě to ale symptom **nesundalo**: pořád 2 ze 4 běhů chybné, protože
     detektor je *per-krok* — první volání zakládá referenci a skok hlásit nemůže, a plynulá
     konvergence kurzu se pod toleranci 5° za krok schová. Zásah zůstává správný a užitečný
     nezávisle: **abrupt** skok kurzu (korekce kurzu z korelace, znovuzachycení GPS) byl pro
     pojistku dřív neviditelný plošně, ne jen při startu.
  2. **Kurz se inicializuje** — `AsyncFusionEngine.InitializeHeading(theta, std, t)`, volaná
     z `ARBotRuntime.InitializeStartPose` místo dřívějšího `HeadingMeasurement`. Tím transient
     kurzu vůbec nevznikne, takže grid není co znečistit.

  **Naměřeno po opravě:** vnucená chyba −0,5 m napříč, čtyři běhy — první cyklus **−0,487 / −0,481 /
  −0,487 / −0,479 m** proti očekávaným −0,500 (chyba 1,3–2,1 cm), určená osa −6,3 až −6,8° (tedy
  správně, jako v ustáleném stavu), `korel os+` zhasnutý. Před opravou byl první cyklus chybný ve
  3 ze 3 běhů a hlásil opačné znaménko. Ustálený stav se nezměnil (0,5048 proti 0,5000 m).

  **Co z toho zbývá:** po zahození gridu se objeví cyklus s ~2 000 buňkami, kde `korel os+` svítí,
  i když hodnota `Dx/Dy` je správná. Není to regrese — a **není to samostatná vada**: je to táž
  slepota σ k množství důkazu, viz [Proč malý oblak obelže hlídač](#proč-malý-oblak-obelže-hlídač).

  **Příčina:** skóre **není lokálně kvadratické**. Pro cestu konstantní šířky je to „tent"
  `S ≈ 1 − k·|d|`, protože podíl nesouhlasných buněk roste s příčnou odchylkou **lineárně**. Druhá
  derivace tentu je nula všude a delta ve vrcholu, takže konečná diference vrátí `−2k/h` — odtud
  zakřivení `∝ 1/h` a `σ ∝ √h`, což je přesně to `√2` na každé zdvojnásobení kroku v tabulce výše.
  Kvadratická forma tent nepopíše: pro cestu pod 45° vyjde podélné vlastní číslo 2,07 místo nuly.
  Naměřený `sxy` je přesně **polovina** hodnoty, kterou by musel mít, aby podélný směr vyšel plochý.

  **Dvě opravy, které NEFUNGOVALY** (ať se nezkoušejí znovu):
  1. *Měřit σ směrově* místo fitu Hessiánu — 12 směrů po 15°, určená osa = směr s max informací,
     volná osa kolmá. Selhalo: `Info(θ)` je kvůli kvantizaci rastru schodovitá se širokými
     plošinami remíz (na přímé cestě je 60°–120° shodných), takže `argmax` bere první prvek plošiny
     a kolmý směr padne mimo skutečnou plochou osu. Zhoršilo to i dosud správnou přímou cestu.
  2. *Tie-break na střed plošiny* — přímou cestu opravil (90°, `SigmaLoose = ∞`), šikmou ne:
     kvantizace tam vyrábí **prohlubeň přesně na skutečné ose** (135° má 2,93 proti 4,39 u sousedů),
     takže se plošina rozpadne na dva oddělené běhy délky 2 a střed se netrefí.

  #### Proč malý oblak obelže hlídač

  Naměřeno 20. 8. 2026 nad `20260820-071801.rec` (po opravě inicializace kurzu). `#` = „tady je
  cesta", `.` = „tady cesta není", `R` = robot, vpravo = vpřed, znak = 0,5 m:

  ```
  snapshot 2 — 2 214 buněk            snapshot 7 — 18 465 buněk
                                        5,5 |                    ..
                                        4,0 |                  .......
                                        2,5 |               ............
    1,5 |                      ...       1,5 |             ..............
    0,5 |                 ........       0,5 |            ###############
    0,0 |              R #########       0,0 |            ##R############
   -0,5 |                ##.######      -0,5 |            ###############
                                        -1,5 |             #############
                                        -2,0 |              ............
                                        -4,0 |                ........
  ```

  | snapshot | buněk | podél | **napříč** | skóre při posunu 2,5 m | marže volné osy | `korel sig+` | `korel os+` |
  |---|---|---|---|---|---|---|---|
  | 2 | 2 214 | 4,41 m | **2,20 m** | 0,58 | 0,187 | **0,1412** | **svítí** |
  | 1 | 5 047 | 4,59 m | 4,68 m | 0,80 | 0,072 | 0,2309 | zhasnutý |
  | 7 | 18 465 | 7,06 m | 10,84 m | 0,82 | 0,054 | 0,2737 | zhasnutý |

  **Není to o řídkosti ani o podílu buněk mimo cestu.** Hustota je ve všech případech shodná
  (~230 buněk/m², tedy ~57 % zaplnění) a podíl buněk „mimo cestu" taky (59–65 %). Rozhoduje
  **prostorový rozsah** — a hlavně rozsah **napříč**: 2,20 m je *méně než šířka cesty* (3 m).

  Mechanismus: u velkého oblaku leží většina buněk **daleko od okraje cesty**. Ty souhlasí u
  **každého** kandidáta — trávník na trávníku sedí, ať posuneš kamkoli — takže nic neurčují a jen
  **ředí procento**. Posun o metr s procentem hne málo, konkurent zůstane blízko vrcholu a hlídač
  správně zhasne. Malý oblak žádnou takovou nudnou buňku nemá; každá jeho buňka je u okraje, tedy
  informativní, takže posun o metr hne procentem hodně → konkurent se odliší → hlídač propustí.
  Procento se ale nezhoršilo proto, že by malý oblak věděl víc, ale proto, že **nemá co ředit**.

  > **Jádro je jedno a společné s celým úkolem č. 1.** Je to jako s anketou: tři dotázaní se stoprocentní
  > shodou vypadají lépe než tři tisíce s 94 %, ale věřit se dá druhému číslu. Skóre je
  > **normalizované**, takže o množství důkazu za sebou neví nic — a σ z jeho zakřivení (× konstantní
  > `α`) to nemůže vědět taky. Proto vyjde pro malý oblak σ **menší** (0,1412 proti 0,23–0,29 m):
  > větší jistota tam, kde je podkladu nejmíň. Není to druhá vada, je to táž vada z druhé strany.

  **Rozhodnuto neopravovat zvlášť** (20. 8. 2026): až se σ naučí počítat, kolik informativního
  důkazu za ní stojí, tenhle případ zmizí sám — malý oblak dostane velkou σ, strop σ podélnou osu
  potlačí a hlídač marže nebude potřeba. Opravovat to teď zvlášť by znamenalo přidat další ruční
  práh, což je přesně to, co [Testování](#testování) zakazuje.

  Naléhavost byla nízká, dokud byly korekce vypnuté; **od 20. 8. 2026 už vypnuté nejsou**, takže
  tenhle cyklus teď do fúze skutečně pošle podélnou korekci s příliš malou σ. Po opravě kurzu
  nastane případ **jednou za pět běhů
  na jediný cyklus** (po zahození gridu) a hodnota, kterou v něm korelátor poslal, byla shodná
  s ustáleným stavem. *Ale:* ve virtuálním HW je hodnota přišpendlená tím, že kamera renderuje
  z téže mapy, takže to není důkaz správnosti — jen absence protidůkazu.

  **Co bylo vyzkoušeno a nefunguje:** přeformulovat hlídač **poměrově** (marže volné osy proti marži
  určené osy, měřené na tomtéž oblaku, takže ředění se vykrátí). Nepomůže: poměr vyjde 0,27 pro
  snapshot 2 proti 0,10–0,13 u ostatních, takže rozumný práh snapshot 2 pustí taky.

  **Nejlevnější pojistka, kdyby byla potřeba dřív** než oprava σ: nevěřit žádné ose, dokud důkaz
  nepokryje aspoň **místní šířku cesty** napříč. Hranice není laděná konstanta — bere se z mapy.
  Snapshot 2 (2,20 m proti 3 m) by neprošel, snapshoty 1 a 7 ano. Nekryje ale případ „dost široký,
  ale krátký oblak".

  **Kam se dívat dál.** Obě selhání ukazují na to, že jemným krokem (0,2 m = 4 buňky rastru) se
  směr měřit nedá. Kandidáti k prozkoušení: výrazně větší krok (1–2 m, tedy 20–40 buněk — pro tent
  je to bez ztráty, protože je lineární), bilineární čtení rastru místo nejbližšího souseda, nebo
  fit modelu `a(θ) = A·|sin(θ − θ₀)|` přes všech 12 směrů, který dá přímo směr cesty a je k
  plošinám i prohlubním robustní. Kvalifikovaný test už existuje —
  `SikmaCesta_PricnaSlozkaJeUrcena` stačí doplnit tvrzením o `SigmaLoose`.

  #### Korelace přes FFT — proč to není odpověď na rychlost, ale možná na tenhle úkol

  Otázka „nebylo by lepší použít FFT?" se bude vracet, tak sem patří obojí — proč ne a proč možná ano
  (rozvaha 20. 8. 2026; **čísla dole jsou odhady, ne měření**, na rozdíl od těch 126 ms).

  **Jako zrychlení téhož to nejspíš prohraje.** FFT spočítá korelaci pro *všechny* posuny, ať je
  chceš nebo ne — a dnešní sken je postavený přesně na tom, že je nechce: hierarchicky (1 325
  kandidátů proti 512² = 262 144 posunům), řídce (`Stride = 4` na nejhrubší úrovni, ~5 150 buněk
  z 20 600) a v malém okně (±2,5 m = ±50 buněk). Naměřeno ~14,2 M vyhodnocení buňky za cyklus,
  126 ms, tedy ~9 ns na jedno. K tomu:

  - **Rotace zůstane vnější smyčkou** — FFT umí translaci, ne rotaci. Mapová strana se transformuje
    jednou za cyklus, ale důkazní oblak se musí pro každé φ pootočit, rozsypat do 512² pole
    a transformovat znovu.
  - **Normalizace potřebuje druhou korelaci.** Pravidlo „buňka mimo rastr se přeskočí *včetně
    jmenovatele*" (viz [Skóre](#5-skóre)) se jako obyčejná korelace vyjádřit nedá — jmenovatel pro
    každý posun musí vzniknout korelací s maskou.
  - Kvůli cyklické konvoluci je potřeba **nulový padding** (rastr ~456² → 512²).

  Odhad: ~10 úhlů × 4 transformace 512² ≈ jednotky set ms s nativní FFT, se managed MathNet spíš
  sekundy. Proti 126 ms je to horší.

  **Kde by to ale vyhrálo výrazně: dát celou PLOCHU skóre** místo tří sond Hessiánu. Přímo je to
  nedostupné — plná plocha jednoho úhlu je 101×101 posunů × 20 600 buněk ≈ 210 M vyhodnocení, tedy
  odhadem ~1,9 s. FFT ji dá čtyřmi transformacemi, což je řádový rozdíl. A míří to na **tři největší
  otevřené vady naráz**:

  - **`TightAxisAngle` vychýlená o −6,3°** vzniká fitem kvadratické formy na „tent" ze tří sond;
    z plné plochy se směr hřebene **změří**.
  - **Hlídač nejednoznačnosti** je dnes heuristika („vzdálený konkurent + absolutní marže"); z plné
    plochy jde detekovat vícemodálnost pořádně.
  - A ten **jmenovatel pro každý posun**, který FFT přístup musí spočítat kvůli normalizaci, **je
    efektivní množství důkazu** — přesně ta veličina, ke které je σ dnes slepá
    (viz [Proč malý oblak obelže hlídač](#proč-malý-oblak-obelže-hlídač)).

  > **Nepřeprodávat:** FFT dá lepší **měření** plochy, ne lepší **model**. Že skóre není věrohodnost
  > a normalizace zahazuje počet buněk, je vada modelu, ne vzorkování. FFT jen dá do ruky správná
  > čísla, řešení úkolu č. 1 to samo není.

  **Cena:** `MathNet.Numerics` (už referencovaná, 5.0.0) má FFT managed a na tohle bude
  pravděpodobně moc pomalá → nativní knihovna, tedy **nová externí závislost i pro ARM64**, což
  v tomhle projektu není zdarma (viz [build-and-platforms.md](build-and-platforms.md)).

  **Fourier-Mellin na rotaci** (návrh autora, tentýž den). Platí, že FMT rotaci **odděluje**: log-polární
  přemapování magnitudového spektra udělá z rotace posun v úhlové souřadnici, takže ji fázová korelace
  najde jedním výstřelem. Cena odhadem ~7–8 transformací plus dvě log-polární interpolace proti
  ~40 (10 úhlů × 4), tedy asi **5× k dobru** — námitku „rotace zůstane vnější smyčkou" to opravdu boří.
  Pro **tento** problém tomu ale stojí v cestě čtyři věci:

  1. **Částečné překrytí.** FMT předpokládá dva obrazy s převážně stejným obsahem. Tady je důkazní
     oblak vějíř ~7 × 11 m jen před robotem, rastr 456² buněk (22,8 m napříč) s cestami jako tenkými
     pásy. Spektra budou dominovaná **nosiči** — tvarem vějíře proti hranici rastru — ne strukturou
     cesty. To je známý režim selhání FMT; chce to okenkování a horní propust a i tak je křehké.
  2. **Rotace a translace jsou tu skutečně provázané** — nejzávažnější bod. Malá rotace kolem
     vzdáleného bodu vypadá jako příčný posun. Ta vazba je vlastnost úlohy, ne artefakt, a stávající
     návrh ji záměrně marginalizuje (`TranslationAbsorbs`, odečet `mxx -= axp²/app`). Nezávislý odhad
     rotace by tu informaci zahodil.
  3. **Odhad by běžel na jiném kritériu.** Magnitudové spektrum zahazuje fázi (u rotačního kroku
     záměrně), takže rotace se najde maximalizací *něčeho jiného* než skóre, kterým se hledá
     translace. Pravidlo „přeskočit buňku mimo rastr včetně jmenovatele" ani normalizaci se v domény
     |F| vyjádřit nedají. Druhé nekonzistentní kritérium je v kódu, kde semantika remíz a
     nejednoznačnosti už nadělala potíže, riziko.
  4. **Úhlové rozlišení je na hraně.** Nejjemnější úroveň má krok 0,5° a `SigmaPhi` vychází ~1,6°;
     FMT se u dobře překrývajících obrazů dostane typicky na 0,5–1°, při částečném překrytí horší.
     Chtělo by to dojemnit, čímž se malá rotační smyčka vrací.

  **Kde FMT naopak sedí dobře: jako hrubý inicializátor pro široký záběr.** Dnešní záchytný rozsah je
  ±2,5 m a ±8° (`SearchRangeM`, `Levels[0]`) — když je lokalizace mimo víc, hierarchický sken tam
  **principiálně nedosáhne** a korelátor mlčí. FMT je právě na tohle dobrá: jeden výstřel, široký
  rozsah, nízká přesnost; hrubé `(dx, dy, φ)` by pak stávající sken dojemnil. Rozšířilo by to oblast
  přitažlivosti řádově a je to zároveň chybějící kus otevřeného úkolu „eskalace stavu *lokalizace
  nepodložená mapou*" — po delším výpadku GNSS nebo po přenesení robota je to ta schopnost, která tu
  není.

  **Kdy do toho jít:** ne dřív, než padne rozhodnutí o přestavbě (viz [decisions.md](decisions.md)).
  Když vyhraje varianta „příčný offset jen do lokální navigace", hledání se scvrkne skoro na
  **jednorozměrné** a FFT je zbytečná. Když vyhraje stavová varianta a bude potřeba honestní
  anizotropní kovariance, je FFT jako cesta k plné ploše nejspíš správná odpověď — a ten argument
  je silnější než rychlost.

  **Související mezera v pokrytí:** větev marginalizace vazby kurz↔translace (`TranslationAbsorbs`
  a odečet `mxx -= axp²/app`) není **žádným** testem procvičená. Šikmá cesta k tomu byla přidána
  právě proto, ale robot v ní stojí na její vlastní ose symetrie, takže `sxp = syp = 0` i tam.
  Scéna, která vazbu skutečně vyrobí, musí porušit soumístnost robotu se symetrií cesty — např.
  robot mimo osu blízko konce cesty, nebo cesta s měnící se šířkou.

- **UI vrstva nemá testovací projekt** (zjištěno 19. 8. 2026 skutečnou výjimkou za běhu).
  `Src/ARBot` (`TelemetryColumns`, `TelemetryChartControl`, view modely) není pokrytý žádným testem —
  na `ARBot.csproj` neukazuje ani jeden testovací projekt. Právě tam se schovala vada, kterou
  nenašla ani jedna ze čtrnácti review: sdílený helper předával `Enum.IsDefined` vždy `int`, což
  u `byte` výčtu padá. Opravou se převod přesunul do `ARBot.Common/Telemetry/EnumPresentation.cs`,
  ale je to obchvat, ne řešení — logika v `Src/ARBot` zůstává netestovatelná. Zvážit testovací
  projekt nad `ARBot`, nebo přesouvat formátovací logiku do `Common` systematicky (precedens:
  `AnglePresentation`, `EnumPresentation`).

- **Marže rastru pokrývá jen úroveň 0** (zjištěno při re-review 2026-08-19, **neopraveno**).
  `RequiredRasterMarginM` sčítá `SearchRangeM + HessianStepM + rotační člen`, ale úrovně se
  re-centrují, takže skutečná výchylka je součet polovin oken (2,9 m) a |φ| až 10,5° + 2° sondy.
  Potřeba je ~5,06 m proti použitým 4,0 m. Dopad: u nejextrémnějších kandidátů se pořád zahazují
  převážně nesouhlasné buňky, tedy jejich skóre je nadhodnocené. Oprava je triviální (sečíst úrovně),
  jen ji nechci dělat bez testu, který by ten rozdíl chytil.

- **Tvrdý limit korekce za cyklus** (doplněno finální review 2026-08-19, **neimplementováno**).
  `MaxOffsetM` omezuje naměřený posun, ne aplikovaný krok — při malé σ proti velkému `P` může filtr
  aplikovat téměř celé dva metry v jednom updatu. Slib „jednotky cm za cyklus" je dnes jen naděje
  o tom, jak vyjde `α`. Před zapnutím korekcí přidat limiter nezávislý na σ i na Kalmanově zesílení.

- **Bezobslužný běh neumí zadat cíl** (zjištěno 19. 8. 2026 při ověřování za běhu, **neimplementováno**).
  Cíl se zadává jen Ctrl+klikem ve world view (`WorldViewDocument.GoalRequested`), parametr
  příkazové řádky pro něj není. Bezobslužné běhy (`selftest=true`, `telemetryshot=true`) tedy vždy
  proměří jen **stojící robot** — což na kontrolu znamének stačí, ale všechna tři kritéria fáze 4
  potřebují pohyb. Doplnit `goal=lat,lon` vedle existujícího `start=lat,lon[,kurz]` (stejná cesta:
  `GlobalNavigator.SetGoal(LLA)`) je malá změna s velkým dopadem na měřitelnost.

- **Co proměřit nad záznamy před zapnutím** (fáze 4), v tomto pořadí:
  1. **σ proti realizované chybě** — reportovanou `korel sig-` proti skutečnému rozptylu `korel dx`/
     `korel dy` na známě dobrém úseku. Menší reportovaná σ než rozptyl = `α` je malé a filtr bude
     přesvědčenější, než smí.
  2. **Rozdělení NIS pro `Source = "MapCorr"`** z `AsyncFusionEngine.Diagnostics()`. U konzistentního
     filtru s gatingem na 95 % χ²(1) má být zamítnutých ~5 %; výrazně víc = σ moc malá, skoro nic =
     σ moc velká a korekce jsou neúčinné.
  3. **Kolik času svítí `korel os+`** na reálných šikmých cestách, se sloupcem `korel konk+` pro
     odlišení, jestli osu zhasl strop σ, nebo hlídač konkurenta. Když svítí, „falešná podélná jistota"
     protéká přes hlídač a zapínat se nesmí bez ohledu na body 1 a 2.

- **⚠️ Tři podmínky, než korekce pustit naostro** (20. 8. 2026, **návrh k rozhodnutí** — viz
  [decisions.md](decisions.md)). Rozvaha začala u toho, že kamera neměří polohu, ale **vztah k cestě**
  (GPS může lhát, mapa může být špatně nakreslená ve tvaru i v pozici), a mířila na nový stav filtru
  pro posun mapa↔GPS. **Závěr se ale otočil:** přímá korekce pózy stačí, protože mapový rámec *je*
  provozní rámec — mrkev, trasa i cíle misí jsou mapově relativní, a absolutní přesnost je stejně
  omezená chybou mapy. Stavová varianta se odkládá jako záloha.

  Přímý zásah ale **nepustit, dokud nebude splněné tohle** (všechno tři jsou body odsud):
  1. **Honestní σ** (úkol o slepotě σ k množství důkazu výš). Bez toho korelace přehlasuje GPS
     **~400:1** — poměr vah `(σ_GPS/σ_korel)² = (2,12/0,105)²` z naměřených hodnot — na základě
     jistoty, kterou si nezasloužila. To je skutečný problém, ne „přetahování" s GPS.
  2. **Rychlostní limit na aplikovanou korekci.** `MaxOffsetM` omezuje **naměřený** posun, ne
     aplikovaný krok, takže korekce mezi 0,5 a 2,0 m je současně „povolená" i „ničící grid"
     (`PoseJumpDetector`, tolerance 0,5 m). Bez limitu trhne gridem, mrkví i regulátorem.
  3. **Strop na nesouhlas s GPS.** Při 400:1 přestává být GPS nezávislou kontrolou — kdyby se
     korelace zachytila na souběžné cestě dva metry vedle (vedeno jako riziko výš), unese si pózu
     a nikdo to nezastaví. Jedna podmínka v `SendMeasurements` je levnější náhrada než celý stav.

  K tomu **`GateMode.Soft`** místo `Reject` — u přímé korekce je nesouhlas **přechodný** (póza se
  posune do mapového rámce a inovace klesne k nule), stačí projít tím přechodem.

  **Než jsou ty tři podmínky splněné, nemá smysl dolaďovat současné chování korekcí** — ladil by se
  mechanismus, který stojí na σ, jež si svou jistotu nezasloužila.

- **Eskalace stavu „lokalizace nepodložená mapou"** — dnes korelátor jen mlčí a stav si nikdo nečte.
  Chybí k tomu i **schopnost se znovu najít**: záchytný rozsah je jen ±2,5 m a ±8°, takže po delším
  výpadku GNSS nebo po přenesení robota hierarchický sken principiálně nedosáhne. Kandidát je
  Fourier-Mellin jako hrubý inicializátor — viz „Korelace přes FFT" v úkolu č. 1.
  Až bude jasné, jak dlouhé úseky bez shody v praxi vznikají, může `GlobalNavigator` ubrat nebo
  přestat věřit mrkvi.
- **Pyramida rastru mapy** — hrubá úroveň skenování by mohla dotazovat rastr s krokem 20 cm.
  Nevyužitá páka pro výkon na ARM; přidat, až měření řekne, že je potřeba.
- **Kanál `Occ` jako druhá evidence** — pomohl by u zdí a plotů, kde barva selhává, ale nese věci,
  které v mapě nejsou. Až bude příčný odhad z `LRoad` naladěný a bude s čím porovnávat.
- **Přežití restartu** — naladěná korekce se po restartu aplikace zahodí a filtr začíná od GPS.
  Stejná otázka jako uzavírání hran napříč běhy v [global-navigation-runtime.md](global-navigation-runtime.md).
