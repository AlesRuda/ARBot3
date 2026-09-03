# World pohled (mapa)

Dokovatelný dokument **`WorldViewDocument`** — geografický (world) pohled na data ze
[`ARBotRuntime.Stream`](../Src/ARBot/Robot/ARBotRuntime.cs) nad **mapovým podkladem**. Analogie
[robot-centrického pohledu](traversability-grid.md) (`RobotCentricDocument`), ale v geografickém rámci
(WGS84 / Web Mercator EPSG:3857). Menu **Tools → World**.

> **Doplněno 2026-08-11:** přibyly vrstvy **Lokální mapa** (occupancy grid jako rastr) a **Lokální
> plán** (dráha + cíl); **Ctrl + klik** do mapy zadává cíl lokálnímu plánovači. Patří sem, a ne do
> robot-centrického pohledu, protože occupancy grid je world-kotvený a akumulovaný — v pohledu
> otáčeném s robotem by se s každou zatáčkou točil. Viz
> [occupancy-and-local-planning.md](occupancy-and-local-planning.md).

> **Pozor (2026-08-14): rastrové vrstvě je nutné nastavit `Style = new RasterStyle()`.**
> `MemoryLayer` má ve výchozím stavu `Style = VectorStyle`; vrstva **Lokální mapa** se ale plní
> `RasterFeature` (PNG). Bez explicitního `RasterStyle` Mapsui feature nevykreslí a jen zaloguje
> `VectorStyleRenderer can not render feature of type 'Mapsui.Layers.RasterFeature'` — vrstva
> vypadá prázdně, i když data v `OccupancyGridMsg` jsou v pořádku. Tohle nás stálo jedno ladění.

### Legenda vrstvy Lokální mapa

| stav buňky | vykreslení |
|---|---|
| `Blocked` | červená (alfa 0xB0) |
| `Free` | zelená (alfa 0x80) |
| `Unknown` | průhledné |

`Free` má vyšší alfu než původních `0x50` (2026-08-14): slabá zelená splývala se zeleným podkladem
OSM, takže nešlo poznat, co je potvrzená plocha a co jen podklad. Po zesílení je `Free` čitelná
a `Unknown` proto může zůstat průhledné — zkoušené zvýraznění `Unknown` šachovnicí se ukázalo jako
zbytečné a jen zašumilo obrázek.

> **Pozn. při ladění:** `Unknown` průhledné znamená, že skrz něj prosvítá podklad i fialová síť
> OsmNav (occupancy se kreslí *nad* ní — vypnutí podkladu s tím nehne). Souvisle vypadající plocha
> tedy ještě nemusí být potvrzená. Když jde o to, **proč robot jede pomalu**, čti radši čísla
> z Debug outputu (`LocalNavigator`: `koridor: free=… unknown=…` a rozpad rychlostní obálky) —
> brzdná obálka `VBrake` jede jen přes buňky `Free`.

Kód:
- [`Src/ARBot/ViewModels/WorldViewDocument.cs`](../Src/ARBot/ViewModels/WorldViewDocument.cs) — ViewModel
  (data, vrstvy, podklad, backpressure).
- [`Src/ARBot/Views/WorldViewDocumentView.axaml`](../Src/ARBot/Views/WorldViewDocumentView.axaml) (+ `.axaml.cs`) —
  View (ovládací panel + hostování Mapsui `MapControl`).

## Mapový engine — Mapsui

Podklad, zoom/pan a vrstvy řeší knihovna **Mapsui** (balíček `Mapsui.Avalonia12` pro Avalonia 12,
`Mapsui.Nts` pro čárové/geometrické útvary, `BruTile.MbTiles` pro offline podklad). ViewModel **vlastní**
Mapsui [`Map`](https://mapsui.com) model a všechny vrstvy; View mu ho v **code-behind** (mimo design-time)
přiřadí do `MapControl.Map`. Důvody code-behind místo XAML: vyhnout se xmlns 3rd-party controlu a pádu
návrháře; ovládací prvky (combobox podkladu, checkboxy vrstev) jsou v XAML a bindují na ViewModel.

> Rozhodnutí volby Mapsui (vs. vlastní tile control) je v [decisions.md](decisions.md) (2026-08-04).

## Vrstvy (každá samostatně vypínatelná)

Pořadí zdola nahoru: **podklad → mapa (síť) → mapa (vize) → mapa (náhled) → lokální mapa → surové GPS →
trajektorie → trasa/graf → lokální plán → značky → poloha**. Přepínače jsou `[ObservableProperty]` na ViewModelu; jejich změna
přestaví `Map.Layers` (`RebuildLayers`, běží na UI vlákně).

**Šířky a pořadí navigačních vrstev spolu souvisí.** Tři úrovně navigace (síť → globální trasa →
lokální plán) vedou po sobě, takže se v mapě překrývají; aby byly vidět všechny naráz, platí obě
pravidla dohromady: kreslí se **od nejširší po nejužší** a každá další je **výrazně užší** než ta
pod ní. Konkrétně (konstanty `PlanLineWidth` / `RouteLineWidth` / `RouteHighlightWidth`):

| Úroveň | Šířka | Pozn. |
|---|---|---|
| Mapa (síť) | metrická šířka cesty (pás) | úroveň 0 — pásu se poměr netýká, stačí že je úplně dole |
| Trasa / graf | 1,5× plán; zvýrazněná cesta 2× plán | zpod plánu kouká na obě strany |
| Lokální plán | 3 px (základ) | nejužší, kreslí se navrch |

*Proč:* dokud se plán kreslil **pod** trasou a byl užší, modrá čára plánu úplně zmizela pod zelenou
zvýrazněnou trasou (2026-08-17). Při změně šířky jedné vrstvy je proto potřeba zkontrolovat i ostatní.

| Vrstva | Zdroj (Message) | Rámec | Stav |
|---|---|---|---|
| **Podklad** | OSM online / MBTiles offline / žádný | Web Mercator | funkční |
| **Poloha + kurz** | [`RobotStateMsg`](../Src/ARBot.Common/Logs/RobotStateMsg.cs) (**fúzovaná póza**) | lokální ENU → LLA | živé v Run/View |
| **Trajektorie** | `RobotStateMsg` (akumulovaná fúzovaná póza) | lokální ENU → LLA | živé |
| **Surové GPS** | [`GPSState`](../Src/ARBot.Common/Devices/GPSState.cs) (fixy bez fúze) | WGS84 → Mercator | živé; **výchozí vypnuto** |
| **Mapa (síť)** | [`MapMsg`](../Src/ARBot.Common/Logs/MapMsg.cs) (síť z OsmNav, parametr `map=`) | WGS84 → Mercator | živé v Run (emituje runtime); fialová** |
| **Mapa (vize)** | `MapMsg` z `ARBotRuntime.VisionMapMessage` (parametr `visionmap=`) | WGS84 → Mercator | živé v Run; oranžová; **ne ze Streamu***** |
| **Mapa (náhled)** | `.osm` načtený tlačítkem v panelu (`SetPreviewMap`) | WGS84 → Mercator | zelená; **ne ze Streamu**; viz níž |
| **Trasa / graf** | [`GraphNavigationMsg`](../Src/ARBot.Common/Logs/GraphNavigationMsg.cs) (hrany) | lokální ENU → LLA | živé v Run |
| **Značky** | `GraphNavigationMsg` (start/cíl/výsledek) | lokální ENU → LLA | živé v Run |
| **Hranice cesty** | [`CameraFrame.PathEdges`](../Src/ARBot.Common/Devices/CameraFrame.cs) (body) + [`RoadCorridorMsg`](../Src/ARBot.Common/Logs/RoadCorridorMsg.cs) (úsečky) | rámec robotu → lokální ENU, **póza z každé zprávy** | živé; **výchozí vypnuto**, ladicí |

* Do 2026-08-13 byly tyto vrstvy *dormantní* — kód je uměl vykreslit, ale `GraphNavigationMsg`/`MapMsg`
se na `Stream` neemitovaly. Dnes je emituje runtime (`GlobalNavigator` trasu, `ARBotRuntime` mapu po
sestavení sítě), takže vrstvy žijí; ve View se přehrávají ze záznamu.

\*\* Vrstva **Mapa (síť)** se plní **jen ze streamu**. Ručně načtený `.osm` jde od 1. 9. 2026 do
samostatné vrstvy **Mapa (náhled)** a tuhle **nepřepisuje** — viz „Mapa (náhled)" níž.

\*\*\* Vrstva **Mapa (vize)** je mapa, ze které renderují **virtuální kamery** (`visionmap=`) —
záměrně **nechodí přes `Stream`** (a tedy ani do záznamu): druhá `MapMsg` ve streamu by přepsala tu
navigační a ve View by z ní vyšel jiný počátek lokální ENU roviny. Dokument si ji bere přímo z runtime
(`SetVisionMap`) při otevření a při změně sezení. Kreslí se **jen oranžovou konturou** (ne plochou —
Mapsui 5.1 výplň polygonu nevypne, viz [virtual-hw.md](virtual-hw.md)); rozestup od fialového pásu
navigační sítě *je* záměrně zavedená chyba mapy. Bez parametru je vrstva prázdná.

### Lokální plán obarvený rychlostí a rychlostní profil (3. 9. 2026)

Plán už není jedna modrá čára. **Každý úsek je vlastní featura obarvená stropem rychlosti**, který mu
plánovač předepsal (`RegulatorWayPoint.Speed` uzlu, ze kterého se odjíždí): **modrá** (původní
0x42A5F5) = plná rychlost, **oranžová** = brzdí, **červená** = stojí nebo se plazí na podlaze
0,05 m/s. Škála je normalizovaná na strop řízení (`Profile.MaxAllowedSpeed`, tedy `maxspeed=`);
když ji plán překročí, roztáhne se. Jedno místo pro barvy má
[`SpeedPalette`](../Src/ARBot/Views/Controls/SpeedPalette.cs). Proč konec škály zůstal modrý a ne
zelený: zelená je zvýrazněná globální trasa hned pod plánem.

**Rychlostní profil** je překryv **vlevo dole** (checkbox *Rychlostní profil plánu* v panelu vrstev,
výchozí zapnuto): graf **strop rychlosti [m/s] jako funkce vzdálenosti od robota po dráze [m]**,
úseky touž barvou jako v mapě, tečky v uzlech, čárkovaná čára stropu řízení a **žlutá značka
v nule = aktuální rychlost robota z fúze** (rozdíl proti prvnímu uzlu říká, o kolik robot za plánem
zaostává). Hlavička nese stav plánu, délku a nejmenší odstup. Když plán není, překryv se schová.
Jak to vypadá: [world-view-speed-profile.png](media/world-view-speed-profile.png) (self-test, FreeRun na
rovné mapě). Kreslí [`PlanSpeedProfileControl`](../Src/ARBot/Views/Controls/PlanSpeedProfileControl.cs) nad
modelem [`PlanSpeedProfile`](../Src/ARBot.Common/Occupancy/PlanSpeedProfile.cs) (čistý výpočet
v `Common`, má testy: kumulativní vzdálenost po dráze, ne přímo od robota).

*Proč vzdálenost a ne čas:* obálka je geometrická vlastnost dráhy (odstup od překážek, hranice
potvrzeně sjízdného), takže se čte „za kolik metrů mě co přibrzdí". Čas by závisel na tom, jak
rychle robot skutečně pojede.

*Proč překryv, ne tooltip:* tooltip nad úsekem plánu existuje dál (rychlost, tolerance polohy),
ale úseky se s každým plánem pohybují, takže se na ně myší míří špatně a obálka jako celek z nich
vidět není. **Rozpad obálky** (brzdí odstup, nebo hranice potvrzeného?) tu ale pořád **není** —
plánovač ho počítá (`MinVClear`/`MinVBrake`), jenže do `LocalPlanMsg` nejde; to je další krok.

### Jeden rámec pro všechna lokální data (2026-08-14)

**Poloha, trajektorie, trasa/graf, značky, lokální mapa i lokální plán se kreslí přes tentýž
`BuildGeoReference()`** — tedy z **fúzované pózy** v lokální ENU rovině. Je to podmínka toho, aby
spolu vrstvy seděly: plánovač počítá z fúzované pózy, takže cokoliv jiného pod značkou robota nutně
znamená, že plán „nevychází z robota".

Do 2026-08-14 se **poloha a trajektorie braly ze surového GPS**, zatímco plán a occupancy z fúzované
pózy. Projevy: (a) začátek plánu byl posunutý od značky robota přesně o aktuální chybu fixu, což
vypadalo, že plán vychází z „ideální pozice uprostřed cesty"; (b) trajektorie byla klubko šumu místo
dráhy — práh `MinTrackStepMeters` (0,5 m) propouští právě jen ty šumové výchylky; (c) značka míchala
dva zdroje, protože polohu brala z GPS, ale kurz z fúze.

Surové fixy se kreslí dál, ale jako **samostatná vypínatelná vrstva „Surové GPS"** (šedě, výchozí
vypnuto). Rozestup šedého bodu od žluté značky robota je přímo aktuální chyba GPS — užitečná
diagnostika kvality fixu a fúze.

*Fallback:* bez načtené mapy nemá `BuildGeoReference()` pevný `MapOrigin` a odvozuje počátek z posledního
fixu a pózy — pak se lokální ENU rovina posouvá s každým fixem a všechno kreslené v ní poskakuje.
Bez pózy nebo bez rámce se značka robota vykreslí aspoň na surovém fixu, ať je robot vidět.

### Mapa (síť) z OsmNav

Silniční/pěší síť z OsmNav se přenáší zprávou [`MapMsg`](../Src/ARBot.Common/Logs/MapMsg.cs) — nese **uzly
v geografických souřadnicích** (LLA, stupně) + **šířku cesty v uzlu** [m] a **hrany** (indexy uzlů + WayId +
délka). Konverze ze sítě: `RoadNetwork.ToLogMessage()` (konvence `ToLogMessage` jako u ostatních domén;
obousměrné hrany se deduplikují na jednu úsečku). Protože jsou
souřadnice geografické, vrstva se kreslí **přímo** do Web Mercatoru (žádné zarovnávání lokálního rámce jako
u trasy/grafu). Celá síť je v **jedné** feature → přestavuje se jen při příchodu nové mapy (statická).

**Šířka cesty (proměnná).** Šířka je uložena v **uzlu** ([`Node.Width`](../Src/ARBot.Common/Maps/OsmNav/Graph/Node.cs)),
takže cesta může být na začátku a konci různě široká (interpolace podél hrany) a v křižovatce se hrany
**hladce napojí** (všechny sdílí šířku uzlu). Render: každá hrana = **vyplněný pás proměnné šířky**
(lichoběžník; poloviční šířka na obou koncích dle uzlu), každý uzel = **kotouč** (zaoblí konce a vyplní klín
v křižovatce). Vše se **sjednotí** (`MultiPolygon.Union()`) → uniformní průhlednost + jeden vnější obrys.
Metrická šířka se násobí `1/cos(lat)` (zkreslení Mercatoru) → reálná velikost, škáluje se se zoomem.

Šířka při načtení z OSM: [`GraphBuilder.BuildNetwork(..., defaultWidthMeters)`](../Src/ARBot.Common/Maps/OsmNav/Osm/GraphBuilder.cs)
přiřadí uzlu **max šířku z incidentních cest** — buď **default** (pole „Šířka cesty [m]" v panelu,
`DefaultRoadWidthMeters`), nebo **z OSM tagu** `width`/`est_width` (parsují se metry); uzel s vlastním
`width` tagem má přednost.

Naplnění vrstvy **Mapa (síť)**: **ze streamu** — runtime po sestavení sítě z `map=` pošle `MapMsg`
(cesta přes `Post`/`Flush`). Ve View se přehraje ze záznamu.

### Mapa (náhled) — „co robot dostane, dřív než to dostane"

Tlačítko **„Načíst OSM mapu…"** (`WorldViewDocument.LoadOsmMapAsync`) vybere `.osm`, na pozadí ho
zparsuje ([`OsmXmlReader`](../Src/ARBot.Common/Maps/OsmNav/Osm/OsmXmlReader.cs)), sestaví
[`RoadNetwork`](../Src/ARBot.Common/Maps/OsmNav/Graph/RoadNetwork.cs) (pěší profil,
[`GraphBuilder`](../Src/ARBot.Common/Maps/OsmNav/Osm/GraphBuilder.cs)), zkonvertuje na `MapMsg`
a vykreslí **zeleným obrysem** jako **samostatnou vrstvu** (`SetPreviewMap`). Tlačítko **„Smazat"**
ji zahodí (`ClearPreviewMap`).

**Náhled je oddělený od navigační sítě záměrně** (změněno 1. 9. 2026; dřív ji přepisoval). Smysl
tlačítka je ukázat, co robot dostane, **až** mu ten soubor dáte — to jde jedině tehdy, když je vidět
**vedle** toho, podle čeho jede teď. Načtení proto vrstvu *Mapa (síť)* nemění a *Smazat* se jí
netýká. Stejně jako *Mapa (vize)* náhled **nejde na `Stream` ani do záznamu**: záznam má popisovat,
co robot věděl, ne co si u toho někdo prohlížel.

⚠️ **Šířka cest bez tagu `width` se bere z parametru `roadwidth=`** — z téhož zdroje jako navigační
mapa (`ARBotRuntime.ReadNetwork`). **Do 1. 9. 2026 tu byly natvrdo 2 m proti 3 m u robota**, takže
týž soubor měl v náhledu jinak široké pásy než mapa, podle které se jelo — a náhled tím lhal právě
o tom, kvůli čemu existuje. Pole zůstává editovatelné (experimentovat se šířkou je legitimní),
a **použitá šířka je proto ve stavovém řádku**: když si ji přetočíte, je vidět, že náhled už
neodpovídá.

Náhled **nemá vlastní zaškrtávátko viditelnosti** — načtení a *Smazat* tu viditelnost jsou; třetí
stav „načteno, ale skryto" nemá zjevné použití. Při načtení (a bez GPS fixu) se mapa vycentruje na
rozsah sítě.

### Podklad a offline (OrangePI bez internetu)

Zdroj podkladu je přepínatelný: **OpenStreetMap (online) / Offline (MBTiles)**; vypíná se
**checkboxem „Zobrazit podklad"**. Když je podklad vypnutý, **žádná dlaždicová vrstva se nevytvoří**
→ **žádné pokusy o komunikaci po internetu**. To je záměr pro provoz na OrangePI, kde je proto
**výchozí stav `ShowBaseMap = false`** (`#if IsARM64`); na vývoji (x64) je zapnutý s OSM.
OSM vrstva posílá korektní `User-Agent` dle OSM tile usage policy.

> **Do 1. 9. 2026 byla v nabídce i položka „Bez podkladu"** a dělala **přesně totéž** jako odškrtnutý
> checkbox — dvě ovládání pro jeden stav. Vyrábělo to matoucí stav „v combu je OSM, ale nic nevidím".
> Zrušila se **položka v nabídce**, ne checkbox: vypnout a zapnout podklad je častější než změnit
> jeho druh, a přes checkbox to jde jedním klikem **bez ztráty volby**. Hodnota `BaseMap.None`
> v enumu zůstává jako bezpečná náhrada v `GetBaseLayer`.

**Panel se řídí volbou v nabídce:** cesta k `.mbtiles` je vidět jen u offline podkladu, tlačítko
*Uložit výřez jako MBTiles* jen u online — dlaždice se totiž **vždycky stahují z OpenStreetMap**,
takže je to akce „připrav si offline z online" a nad offline podkladem nedává smysl. Po dokončení
exportu se cesta předvyplní, takže stačí přepnout podklad na *Offline*.

Offline podklad je [MBTiles](https://github.com/mapbox/mbtiles-spec) soubor (SQLite s dlaždicemi);
cesta se zadá v panelu (tlačítko „…" otevře výběr souboru). Vrstva se staví jen když soubor existuje.
Podporujeme **rastrové** MBTiles (PNG/JPG dlaždice) ve Web Mercator; vektorové MBTiles (MVT/`.pbf`) ne
(Mapsui `TileLayer` je rastrový renderer).

### Vestavěný export výřezu do MBTiles

Tlačítko **„⬇ Uložit výřez jako MBTiles"** stáhne dlaždice **aktuálního viditelného výřezu** z OpenStreetMap
pro rozsah **z13–19** a zapíše je do `.mbtiles` (schéma MBTiles, TMS y-flip; zápis přes `sqlite-net`, který
přišel s `BruTile.MbTiles`). Po dokončení se cesta rovnou nastaví do `MbTilesPath` — stačí přepnout podklad
na *Offline (MBTiles)*. Určeno pro rychlé pořízení podkladu okolí trasy bez externích nástrojů; výsledný
soubor se zkopíruje na OrangePI.

Ochrany (kvůli [OSM tile usage policy](https://operations.osmfoundation.org/policies/tiles/) a velikosti):
- **Tvrdý strop** počtu dlaždic (`MaxExportTiles`, default 5000) — při překročení export odmítne a vyzve
  k přiblížení/zmenšení výřezu (spočítá se předem, nic se nestahuje).
- **Šetrné tempo** (prodleva `ExportThrottleMs` mezi requesty) + korektní `User-Agent`.
- Export běží na pozadí (tlačítko je po dobu běhu zakázané, stav se ukazuje v panelu), ruší se při zavření
  dokumentu.

Pro větší oblasti / produkci je vhodnější vlastní ortofoto nebo zdroj povolující offline balení (GDAL/QGIS
z georeferencovaného rastru) — hromadné stahování z OSM není pro velké rozsahy v souladu s jejich policy.

## Souřadnice a zarovnání rámců

- **Poloha/trajektorie**: `GPSState.Latitude/Longitude` jsou **desetinné stupně** (NMEA i uBlox) →
  přímo `SphericalMercator.FromLonLat(lon, lat)`. Robustní, globální — nevyžaduje žádný počátek.
- **Kurz robota**: preferuje se `RobotStateMsg.Theta` (fúzovaný, matematický úhel: 0 = východ, +CCW),
  fallback `GPSState.DynamicOrientation`.
- **Tvar robota**: robot se kreslí jako svůj **skutečný půdorys** — sdílený obrys
  [`RobotGlyph.OutlineMeters`](../Src/ARBot/Views/Controls/RobotGlyph.cs) (v metrech) orotovaný o kurz a
  převedený do Web Mercatoru jako **metrický polygon** (ne symbol). Škáluje se se zoomem = reálná velikost.
  Mercator zkresluje měřítko o `1/cos(lat)`, proto se obrys tímto faktorem násobí (robot má reálný rozměr).
  Pozn.: robot je ~0,5 m, takže při běžném mapovém zoomu je **subpixelový** — viditelný až po přiblížení
  (viz hluboký zoom níže).
- **Trasa/graf + značky** jsou v **lokálních ENU metrech** (rámec `RobotStateMsg`). Pro geo-umístění
  se sestaví [`GeoReference`](../Src/ARBot.Common/Coordinates/GeoReference.cs) **zarovnaný na GPS**:
  počátek lokální roviny se posune tak, aby aktuální lokální poloha robotu `(X, Y)` odpovídala jeho
  GPS poloze (`origin = GeoReference(gpsLLA).ToLLA(−X, −Y)`). Zarovnání je **aproximativní** (drifuje
  s GPS šumem), pro vizualizaci dostatečné. Bez GPS fixu i lokální pózy se tyto vrstvy nekreslí.

## Backpressure (povinný vzor)

`IMessageSink.Post` běží na vlákně producenta → drží se jen **nejnovější** zpráva per typ a plánuje
se jeden koalescovaný `Flush` na UI vlákně (`DispatcherPriority.Background`) — vzor „latest-wins",
viz [Views/README.md](../Src/ARBot/Views/README.md). `Flush` přepočte featury vrstev, zavolá
`DataHasChanged()` a (při „Sledovat robota") vycentruje mapu. Trajektorie je capovaná (max 5000 bodů,
minimální krok 0,5 m).

## Ovládání

- **Sbalení panelu**: přepínacím tlačítkem („☰ Mapa a vrstvy") vlevo nahoře lze ovládací panel sbalit,
  aby nebránil pohledu na mapu (sbalený zabírá jen tlačítko). Stav drží `PanelExpanded` na ViewModelu.
- **Podklad**: combobox zdroje + checkbox zapnutí + cesta k MBTiles.
- **Vrstvy**: checkboxy Poloha+kurz / Trajektorie / Surové GPS / Mapa (síť) / Trasa+graf / Značky /
  Lokální mapa / Lokální plán.
- **Tooltipy v mapě** (`FindMarkerTip`): barevné puntíky ani čáry samy o sobě nic neříkají, proto
  k nim jde najet myší. Popisy se drží v **oddělených seznamech** podle vrstvy — `markerTips`
  a `planTips` (body), `routeSegTips` a `planSegTips` (úsečky); jeden společný by si vrstvy
  přepisovaly, protože se přestavují nezávisle. Hledá se **jen ve viditelných vrstvách** (popisek
  k něčemu, co není vidět, mate). Pořadí: nejdřív **body**, a teprve když žádný netrefil, **čáry** —
  body leží NA čárách, takže kruh kolem kurzoru chytí úsečku vždycky, kdežto bod jen když na něj
  uživatel opravdu míří. Mezi čarami rozhoduje vzdálenost, při shodě vyhrává plán (kreslí se
  nad trasou — pořadí hledání kopíruje pořadí vykreslení).
  - **Modrá „mrkev"** (Značky, z `GraphNavigationMsg.ResultX/Y`) a **žlutý cíl** (Lokální plán,
    z `LocalPlanMsg.RequestedGoalX/Y`) jsou v ustáleném stavu **tentýž bod** — globální vrstva mrkev
    spočítá a předá ji přes `SetGoal()` lokální. Kreslí se dvakrát schválně: rozestup mezi nimi ukáže,
    že se globální a lokální vrstva rozešly (mrkev se přepočítává průběžně, žlutá je cíl posledního
    hotového plánu).
  - **Hrany trasy/grafu** (`routeSegTips` + `BuildEdgeTip`): hrany se od sebe liší jen barvou
    a tloušťkou. Tooltip řekne, **která** cesta to je a **čím** je: `Hrana <OSM WayId> · vybraná
    trasa / trasa / graf sítě / uzavřená a penalizovaná`, délka, azimut, přímá vzdálenost koncových
    uzlů, šířka cesty (průměr obou uzlů), ID uzlů a — pokud je spočtená — vzdálenost uzlů k cíli
    (`Final` = hodnota už je v Dijkstrovi uzavřená, jinak „předběžně").
    **Pozor na producenta zprávy:** `GlobalNavigator` plní `ID` = OSM `WayId`, `Length` = metrickou
    délku hrany a `Distance` vrcholů nechává nespočtenou; starší cesta přes `Map` plní `Length`
    *váhou* hrany a `Distance` metrickou vzdáleností uzlu k cíli. Proto se `Distance` ukazuje jen
    při `DistanceCalculated`.
  - **Stav globální navigace** (`globalNavTip` + `BuildGlobalNavTip`, z `GlobalNavMsg`): tahle zpráva
    **nemá vlastní geometrii** (cíl i mrkev už kreslí Značky), takže se přidává jako **hlavička**
    tooltipu ke všemu, co globální navigace vyrobila — ke značkám a k hranám trasy. Obsahuje
    `GlobalNavStatus`, cíl (lat/lon), vzdálenost od sítě, zbývající trasu (m / počet hran),
    potenciál postupu φ, počet uzavřených hran, mrkev v ENU a čas cyklu. Text se skládá **při
    příjmu zprávy** (chodí každý cyklus), hledání tooltipu pak jen porovnává vzdálenosti.
  - **Úseky lokálního plánu** (`planSegTips` + `BuildPlanSegmentTips`): plán je jedna modrá čára bez
    čísel, takže parametry, které ji určily, nejsou v mapě vidět vůbec. Najetím **kamkoli na čáru**
    se ukáže tooltip úseku `k → k+1`: hlavička plánu (stav, počet bodů, délka, cena, min. odstup,
    doba výpočtu), délka úseku a kumulativní vzdálenost od robota, směr (ENU), předepsaná rychlost
    a tolerance polohy v obou koncích (`Orientation` / `MaxSpeedError` jen když jsou zadané —
    plánovač je nechává na výchozích hodnotách). Délky a směry se počítají z lokálních metrických
    souřadnic (ENU), ne z Web Mercatoru (ten je v metrech jen přibližně).
  - **Cesty sítě OsmNav** (`mapSegHits` + `FindMapEdgeTip`, z `MapMsg`): název sítě, OSM `WayId`,
    délka hrany, šířka (průměr + obě koncové hodnoty) a ID uzlů. Dvě odlišnosti proti ostatním
    vrstvám: (1) **trefou je pas cesty**, ne pevný okruh kolem kurzoru — cesty se kreslí v metrické
    šířce, takže uživatel míří na to, co vidí (tolerance z viewportu slouží jen jako minimum, aby
    šla trefit i úzká cesta při odzoomování); (2) **text se nepředpočítává** — síť má i desetitisíce
    hran, řetězec ke každé by byly zbytečné megabajty, takže se skládá až při trefě z `lastMap`
    (hit-test drží jen úsečku, poloviční šířku a index hrany, s levným odřezem podle obálky).
    Síť se hledá **úplně nakonec**: kreslí se pode vším a jako široký pás, takže by jinak přebila
    trasu i plán, které po ní vedou.
- **Sledovat robota**: centruje mapu na polohu při každé aktualizaci (první fix navíc nastaví zoom).
- Zoom/pan/rotace: standardní gesta Mapsui (kolečko, tažení).
- **Hluboký zoom**: povoleno přiblížení hluboko nad rámec zdroje dlaždic (`OverrideZoomBounds`, min.
  rozlišení ~zoom 23, tj. ~10× a víc nad z19), aby šel vidět metrický tvar robota. Nad maximem dlaždic se
  podklad jen zvětší (overzoom, rozmazaný), ale datové vrstvy (robot, trasa) se kreslí dál ostře. Rozsah
  je konstanta v `ApplyZoomBounds` (`WorldViewDocument`).

## Reprodukovatelný screenshot (`worldshot`)

Spuštění s parametrem `worldshot=true` bezobslužně otevře World, nakrmí ho syntetickou trajektorií +
polohou (Praha), počká na dlaždice OSM, hluboko přiblíží na robota, uloží `doc/media/world-view.png`
a ukončí se (obdoba self-testu, ale bez HW/Run). Kód: `MainWindowViewModel.WorldShot.cs`. Slouží
k pořízení obrázku featury do [devlog.md](devlog.md) bez ruční obsluhy.

## Vrstva „Hranice cesty": póza z každého snímku (23. 8. 2026)

Vrstva promítá **každou sadu bodů pózou z jejího vlastního snímku** a proložené úsečky pózou z jejich
zprávy. Do 23. 8. 2026 se všechno promítalo **jednou** „poslední známou" pózou, a to bylo špatně
o měřitelnou hodnotu.

**Proč to vadilo.** Kamery nejsou fázově svázané a jejich snímky jsou až `MaxCameraSkewMs` = 400 ms
od sebe, takže starší sada byla nakreslená pózou novějšího snímku. Naměřeno na záznamu z 23. 8.
(pózy dohledané z `RobotStateMsg`, tedy **podhodnoceně**): posun mezi pózami obou kamer p50 0,037 m,
ale rozdíl **kurzu** p90 3,2° a max 12,3°. A kurz se s dálkou násobí — na dosahu proložení 8 m dělá
celková chyba kreslení **p50 0,15 m, p90 0,61 m, max 2,03 m**. Přeměřit jde
`ARBot.Analyze poses <záznam>`.

**Póza cestuje ve zprávě** ([`CameraFrame.PoseAtCaptureX`](../Src/ARBot.Common/Devices/CameraFrame.cs),
[`RoadCorridorMsg.PoseX`](../Src/ARBot.Common/Logs/RoadCorridorMsg.cs)), ne v historii ve view —
**kvůli seeku**. Rekonstrukce stavu dodá poslední zprávu pro každý klíč `(MsgName, Name)`: dva snímky
s různými časy, ale jen jednu `RoadCorridorMsg` a jeden `RobotStateMsg`. Párovat podle razítka ani
interpolovat v historii tedy po seeku nelze. Detail:
[record-replay.md](record-replay.md#seek-určuje-kde-smí-póza-být-23-8-2026).

**Výchozí je odhad z fúze, ne ground truth.** Do 23. 8. 2026 ground truth *vyhrávala*, kdykoli byla
k dispozici. Dvě věci na tom byly špatně: virtuální a reálný běh se chovaly jinak už z principu
(na reálném robotu ground truth není), a hlavně se hranice kreslily **jinou pózou**, než jakou je
ukotvená vrstva **Lokální mapa** (occupancy grid se plní odhadem z fúze). Ty dvě vrstvy se pak
nemohly krýt ani principiálně. Ground truth zůstává jako **volitelný** přepínač „Hranice ze skutečné
pózy" — odděluje chybu detektoru od chyby lokalizace, ale pro srovnání s lokální mapou má být vypnutý.

> **Zbytkový rozdíl proti lokální mapě není nula.** Hraniční body jdou zpětnou projekcí přes
> **měřenou hloubku**, semantický kanál gridu dopředu na **rovinu země**. Se `depthnoise=0`
> a `grassrough=0` obojí splyne — viz [virtual-hw.md](virtual-hw.md).

Rámeček vpravo dole hlásí, který režim běží (`poza: z kazdeho snimku` / `ground truth` / kolik kamer
vlastní pózu nemá).

## ⚠️ Vrstva „Hranice cesty" občas shodí Mapsui (23. 8. 2026, neuzavřeno)

Při přehrávání se zapnutou vrstvou hranic vyskočí `NullReferenceException` **uvnitř Mapsui**:

```
Mapsui.Extensions.FeatureExtensions.GetExtent(IEnumerable<IFeature>)
Mapsui.Layers.MemoryLayer.set_Features(IEnumerable<IFeature>)
WorldViewDocument.BuildEdgesFeatures(...)
```

**Co je vyloučeno** (v tomto pořadí se to zjišťovalo):

- **Obsah featur.** Diagnostika v okamžiku pádu: 395 featur, z toho **0 null, 0 bez extentu,
  0 s nekonečnou souřadnicí**.
- **Data v záznamu.** 38 841 hraničních bodů, 143 úseček a 716 póz v `20260823-154828.rec` — žádné
  NaN ani nekonečno.
- **Data jako taková.** Tytéž featury z téhož záznamu (stejná póza, stejný `GeoReference`) prohnané
  **skutečným Mapsui 5.1.0** v konzolovém programu: **322 cyklů, nula pádů**.
- **Malformované vstupy.** Mapsui 5.1.0 snese null prvek v seznamu, featuru bez geometrie, NaN,
  Infinity, prázdný `LineString` i prázdný seznam — ověřeno samostatným testem.

**Co říká IL.** `GetExtent` má 74 bajtů a **jediné nechráněné dereferencování je `ldarg.0`** —
argument. Prvek i jeho `Extent` mají null-check (`en.Current?.Extent`, `if (e == null) continue`),
větev `extent == null ? new MRect(e) : extent.Join(e)` taky. Argument přichází z
`MemoryLayer._localFeatures`, do kterého se zapisuje **jedině** výsledek `ToArray()` a nikdy null
(proskenovány všechny metody `MemoryLayer`, které do těch polí píší).

**Chování.** Není deterministické: nastane i nenastane na témž místě záznamu a objevilo se i na
jiných místech. Se schovanou záložkou World nenastalo — ale ani to není průkazné, protože
s otevřenou taky proběhlo bez chyby.

**Závěr:** vypadá to na **souběh nad toutéž instancí vrstvy** na straně Mapsui, ne na naše data.
Neuzavřeno, odloženo.

**Zatím platí pojistka:** celá přestavba vrstvy je v `try/catch`; při chybě se vrstva **vypne**,
do rámečku se napíše `Hranice: VRSTVA VYPNUTA po chybe (…)` a do Debug outputu jde řádek
s počtem featur, nulových, bez extentu, nekonečných, počtem kamer a pózou. Ladicí vrstva nemá
právo shodit běh.

## Otevřené úkoly / poznámky

- **ARM (OrangePI)**: Mapsui renderuje přes SkiaSharp — na ARM64 **ověřit nativní SkiaSharp assety**
  na zařízení (build ne­blokuje, jde o runtime závislost). Odsimulováno jen na x64.
- **Vyhledávání (geocoding)** zatím není (vyžadovalo by online službu, např. Nominatim) — možný
  další krok.
- ~~**Trasa/graf/značky** ožijí po napojení OsmNav~~ — **ožily**: `GlobalNavigator` emituje
  `GraphNavigationMsg` na `Stream`, vrstva se kreslí včetně tooltipů na hranách i značkách.
- Další podklady (Mapy.cz / Google) vyžadují API klíč a mají ToS omezení — neimplementováno.
