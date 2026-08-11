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

Pořadí zdola nahoru: **podklad → trajektorie → trasa/graf → značky → poloha**. Přepínače jsou
`[ObservableProperty]` na ViewModelu; jejich změna přestaví `Map.Layers`
(`RebuildLayers`, běží na UI vlákně).

| Vrstva | Zdroj (Message) | Rámec | Stav |
|---|---|---|---|
| **Podklad** | OSM online / MBTiles offline / žádný | Web Mercator | funkční |
| **Poloha + kurz** | [`GPSState`](../Src/ARBot.Common/Devices/GPSState.cs) (poloha) + [`RobotStateMsg`](../Src/ARBot.Common/Logs/RobotStateMsg.cs) (kurz) | WGS84 → Mercator | živé v Run/View |
| **Trajektorie** | `GPSState` (akumulované fixy) | WGS84 → Mercator | živé |
| **Mapa (síť)** | [`MapMsg`](../Src/ARBot.Common/Logs/MapMsg.cs) (síť z OsmNav) | WGS84 → Mercator | ruční načtení**; ze streamu dormantní* |
| **Trasa / graf** | [`GraphNavigationMsg`](../Src/ARBot.Common/Logs/GraphNavigationMsg.cs) (hrany) | lokální ENU → LLA | dormantní* |
| **Značky** | `GraphNavigationMsg` (start/cíl/výsledek) | lokální ENU → LLA | dormantní* |

\* *Dormantní* = kód vrstvu vykreslí, jakmile zpráva začne téct; `GraphNavigationMsg`/`MapMsg` se však zatím
na `Stream` **neemitují** (OsmNav není napojen na řídicí smyčku — viz [osm-nav.md](osm-nav.md),
Otevřené úkoly). Do té doby zůstávají tyto vrstvy (ze streamu) prázdné.

\*\* Vrstvu **Mapa (síť)** lze naplnit ručně tlačítkem **„Načíst OSM mapu…"** (viz níže) i bez runtime.

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

Naplnění vrstvy:
- **Ručně**: tlačítko **„Načíst OSM mapu…"** (`WorldViewDocument.LoadOsmMapAsync`) — vybere `.osm`, na pozadí
  ho zparsuje ([`OsmXmlReader`](../Src/ARBot.Common/Maps/OsmNav/Osm/OsmXmlReader.cs)), sestaví
  [`RoadNetwork`](../Src/ARBot.Common/Maps/OsmNav/Graph/RoadNetwork.cs) (pěší profil,
  [`GraphBuilder`](../Src/ARBot.Common/Maps/OsmNav/Osm/GraphBuilder.cs)), zkonvertuje na `MapMsg` a zobrazí.
  Při prvním načtení (a bez GPS fixu) mapu vycentruje na rozsah sítě.
- **Ze streamu**: až OsmNav začne `MapMsg` emitovat do runtime, vrstva se naplní automaticky (stejná cesta
  přes `Post`/`Flush`).

### Podklad a offline (OrangePI bez internetu)

Zdroj podkladu je přepínatelný: **Bez podkladu / OpenStreetMap (online) / Offline (MBTiles)**.
Klíčové: když je podklad vypnutý (checkbox „Zobrazit podklad") **nebo** je zdroj `None`, **žádná
dlaždicová vrstva se nevytvoří** → **žádné pokusy o komunikaci po internetu**. To je záměr pro provoz
na OrangePI. Proto je **výchozí podklad na ARM (`#if IsARM64`) `None`**, na vývoji (x64) OSM.
OSM vrstva posílá korektní `User-Agent` dle OSM tile usage policy.

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
- **Vrstvy**: checkboxy Poloha+kurz / Trajektorie / Trasa+graf / Značky.
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

## Otevřené úkoly / poznámky

- **ARM (OrangePI)**: Mapsui renderuje přes SkiaSharp — na ARM64 **ověřit nativní SkiaSharp assety**
  na zařízení (build ne­blokuje, jde o runtime závislost). Odsimulováno jen na x64.
- **Vyhledávání (geocoding)** zatím není (vyžadovalo by online službu, např. Nominatim) — možný
  další krok.
- **Trasa/graf/značky** ožijí po napojení OsmNav na řídicí smyčku a emitaci `GraphNavigationMsg` na
  `Stream` (viz [osm-nav.md](osm-nav.md)).
- Další podklady (Mapy.cz / Google) vyžadují API klíč a mají ToS omezení — neimplementováno.
