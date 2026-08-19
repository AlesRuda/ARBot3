# OSM navigace (`Maps/OsmNav`)

Globální navigace po silniční/pěší síti z **OpenStreetMap** — od `.osm` dat přes graf a pole
cost-to-goal až po volbu směru jízdy k cíli — plus **lokální predikce trajektorie a detekce
kolizí** (`Colider`). Modul je čistě algoritmický (bez HW), pracuje nad geografickými souřadnicemi
(WGS84) a v lokálním metrickém rámci robota.

Kód: `Src/ARBot.Common/Maps/OsmNav/`. Testy (NUnit): `Src/ARBot.Common.Tests/OsmNav.Tests/`.

**Autoritativní návrhový popis** (proč jsou věci tak, jak jsou) je v PDF — tento dokument je jeho
**doprovod z pohledu kódu**: mapa typů s odkazy do zdrojů, stav integrace do `ARBot.Common` a shrnutí
`Colideru`, který PDF nepokrývá. Deep detail architektury nedupluje — odkazuje:
- [OsmNav-popis.pdf](OsmNav-popis.pdf) — architektura routing/navigation strany (Verze 2, goal-rooted
  cost-to-goal, edge-based graf, LPA\*, runtime značky, MCL hypotézy, rozsah/omezení).
- [shrnuti-detekce-kolizi-a-navigace.pdf](shrnuti-detekce-kolizi-a-navigace.pdf) — detekce kolizí a navigace.

> **Stav (2026-08-04):** modul nakopírován z jiného projektu a zaintegrován do `ARBot.Common`
> (namespace `ARBot.Common.Maps.OsmNav.*`, testy převedeny na NUnit — 76 testů). **Ještě není napojen
> na řídicí smyčku** — to je otevřený úkol. Historie integrace v [devlog.md](devlog.md) (2026-08-04),
> rozhodnutí (sjednocení `Point2D`) v [decisions.md](decisions.md).

## Přehled vrstev a tok dat

```
.osm XML ──OsmXmlReader──▶ OsmData ──GraphBuilder(+TravelProfile)──▶ RoadNetwork
                                                                        │
                                        GoalField (LPA* cost-to-goal od cíle) ◀── cíl (LLA)
                                                     │           ▲
                              Router.Plan / Navigator.Update     └── SignApplier (runtime značky)
                                                     │
                                             NavigationFix (hrana, cílový uzel, arrived/no-route)

  (lokálně, paralelně) RobotState + ControlCommand ──TrajectoryPredictor──▶ PredictedTrajectory
                                                    ObstacleCollisionDetector ──▶ ObstacleThreat[]
```

Navigace (globální, geo) a Colider (lokální, metrický) jsou **oddělené podsystémy**: první říká
*kam* jet (směr k cíli po síti), druhý hlídá *jestli* aktuální řízení nevede do překážky.

## Osm — parsování a stavba grafu (`Osm/`)

- [`OsmXmlReader`](../Src/ARBot.Common/Maps/OsmNav/Osm/OsmXmlReader.cs) — streamované čtení `.osm` XML
  (Overpass/JOSM) → [`OsmData`](../Src/ARBot.Common/Maps/OsmNav/Osm/OsmXmlReader.cs) (`OsmNodeRaw`,
  `OsmWayRaw`, `TurnRestrictionRaw`). Podporuje jen **via-node** turn-restrikce.
- [`TravelProfile`](../Src/ARBot.Common/Maps/OsmNav/Osm/TravelProfile.cs) — strategie: které `highway`
  akceptovat, které `barrier`/`access` blokují, jak počítat cenu hrany a zda respektovat `oneway`.
  Hotové profily: `Car()`, `Pedestrian()`, `Bicycle()` (liší se povolenými cestami, `oneway`, rychlostí).
- [`GraphBuilder.BuildNetwork(OsmData, TravelProfile)`](../Src/ARBot.Common/Maps/OsmNav/Osm/GraphBuilder.cs)
  → `RoadNetwork`. Filtruje cesty profilem, dělí je na hrany mezi uzly, počítá délky
  (`GreatCircle.Distance`), zakládá odbočení a promítá turn-restrikce.

## Graph — edge-based síť (`Graph/`)

[`RoadNetwork`](../Src/ARBot.Common/Maps/OsmNav/Graph/RoadNetwork.cs) je **neměnná** (po `Builder.Build`
jen ke čtení, bezpečná ke sdílení mezi vlákny/hypotézami). Klíčové rozhodnutí: je **edge-based** —

- **uzel grafu = orientovaná hrana mapy** ([`Edge`](../Src/ARBot.Common/Maps/OsmNav/Graph/Edge.cs)),
- **přechod grafu = odbočení** (turn cost / turn restriction).

Díky tomu jdou přirozeně modelovat **zákazy a ceny odbočení** (co by uzel-based graf neuměl).
[`Node`](../Src/ARBot.Common/Maps/OsmNav/Graph/Node.cs) = křižovatka/bod lomu se zeměpisnou polohou
(+ `Width` = šířka cesty v uzlu [m], pro vizualizaci proměnné šířky a hladké napojení; plní `GraphBuilder`).
`Edge.Index` je **hustý** index do polí plánovače. API: `Successors`/`Predecessors`, `BaseTraversalCost`,
`BaseTurnCost`/`BaseEdgeCost`, `FindReverse`, `NearestEdge` (mapmatching přes geo projekci).

## Routing — pole cost-to-goal (`Routing/`)

[`GoalField`](../Src/ARBot.Common/Maps/OsmNav/Routing/GoalField.cs) = **goal-rooted pole cost-to-goal**
nad `RoadNetwork`, počítané **LPA\*** od cíle (heuristika h=0) a **líně** (`EnsureSettled` dopočítá jen
potřebné uzly). Robot z libovolné hrany pak jen sestupuje gradientem (`NextEdge`, `CostToGoal`).

- **Split cíle:** cíl reálně **rozdělí** svou nejbližší hranu dočasným uzlem `T` na regulérní hrany
  (`A→T`, `T→B`, + reverzní u obousměrné); původní hranu zastíní. Tím robot na cílovém segmentu (i „slepý"
  cíl) dostane konečnou cost-to-goal **bez speciálního „phantom" případu** v Routeru/Navigatoru.
- **Overlay značek:** `SetTraversalCost`/`SetTurnCost` = globální overlay na permanentních uzlech/přechodech
  (přežívá `ClearGoal`). LPA\* pak **inkrementálně** přepočítá jen dotčenou část — levné přeplánování.
- [`Router.Plan(LLA from)`](../Src/ARBot.Common/Maps/OsmNav/Routing/Router.cs) — **bezstavová**
  extrakce celé trasy (seznam `Edge`) sestupem gradientu; volbu prvního směru dělá podle skutečných
  nákladů k cíli (field-aware), ne jen geometrie.

## Navigation — řízení k cíli (`Navigation/`)

- [`Navigator.Update(LLA)`](../Src/ARBot.Common/Maps/OsmNav/Navigation/Navigator.cs) — **tenký
  sledovač gradientu** nad sdíleným `GoalField`: mapmatchne polohu, vybere orientaci s nižší cenou do cíle
  (zohledňuje zbývající traversal aktuálního segmentu) a vrátí
  [`NavigationFix`](../Src/ARBot.Common/Maps/OsmNav/Navigation/NavigationFix.cs) = (aktuální hrana,
  cílový uzel, off-route vzdálenost, `Arrived`, `NoRoute`). Off-route se neřeší explicitně — jiná poloha
  jen přečte pole jinde. Práh dojezdu: `NavigatorOptions.ArrivalRadiusMeters` (default 12 m).
- [`SignApplier`](../Src/ARBot.Common/Maps/OsmNav/Navigation/SignApplier.cs) — promítá **runtime dopravní
  značky** do `GoalField` overlaye: `SpeedLimit`, `CloseRoad`, `NoTurn`, `OnlyTurn`.

## Geo — sdílený `Coordinates` stack

OsmNav **nemá vlastní geotyp** — používá systémový [`LLA`](../Src/ARBot.Common/Coordinates/LLA.cs)
(WGS84, radiány; stejný typ jako GPS/`ARBotState`/mapy). Konstrukce ze stupňů: `LLA.FromDegrees(lat, lon)`.
Vzdálenost: [`GreatCircle.Distance`](../Src/ARBot.Common/Coordinates/GreatCircle.cs) (haversine, R = 6371000).
Mapmatching (projekce bodu na úsek) pro `NearestEdge`/`NearestNode`:
[`LLA.ProjectOntoSegment`](../Src/ARBot.Common/Coordinates/LLA.cs) — instanční metoda, lokální rovinná
(equirectangular) projekce v `double`, t ořezané do [0,1]. (Dřívější OsmNav `GeoPoint`/`GeoMath` byly
sjednoceny do tohoto stacku — viz [decisions.md](decisions.md), 2026-08-04.)

## Colider — predikce trajektorie a detekce kolizí (`Colider/`)

Lokální metrický rámec, matematické úhly (CCW od +X). Nezávislé na geo/síti — pracuje s aktuálním
řízením a vnímanými překážkami.

- [`TrajectoryPredictor.Predict(RobotState, ControlCommand, PerceptionOptions)`](../Src/ARBot.Common/Maps/OsmNav/Colider/TrajectoryPredictor.cs)
  → [`PredictedTrajectory`](../Src/ARBot.Common/Maps/OsmNav/Colider/PredictedTrajectory.cs): unicycle model
  rozložený na **pár úseků** (`MotionArc`) — fáze jízdy pod aktuálním řízením + fáze brzdění do zastavení.
  Horizont sahá **za** bod posledního možného zabrzdění (reakční + brzdná dráha + rezerva), aby šla
  překážka detekovat včas.
- [`MotionArc`](../Src/ARBot.Common/Maps/OsmNav/Colider/MotionArc.cs) — úsek konstantní křivosti (rovný =
  kapsle / oblouk = výsek mezikruží) s **analytickým O(1) promítnutím bodu** (`Project` → `ArcProjection`
  s laterálním odstupem, ujetou vzdáleností, časem a nejistotou σ).
- [`ObstacleCollisionDetector.Detect(...)`](../Src/ARBot.Common/Maps/OsmNav/Colider/ObstacleCollisionDetector.cs)
  → seznam [`ObstacleThreat`](../Src/ARBot.Common/Maps/OsmNav/Colider/ObstacleThreat.cs) setříděný dle
  závažnosti a času: promítne střed každé překážky na úseky a porovná kolmý odstup s nafouknutým
  polokoridorem `w = obal robota + velikost překážky + k·σ`. Závažnost
  ([`ThreatSeverity`](../Src/ARBot.Common/Maps/OsmNav/Colider/ThreatSeverity.cs)): `Watch` / `Imminent` /
  `Unavoidable` (podle toho, zda kolize leží za bodem zastavení).
- Datové typy: [`RobotState`](../Src/ARBot.Common/Maps/OsmNav/Colider/RobotState.cs) (poloha/heading/
  speed/yaw + [`PoseCovariance`](../Src/ARBot.Common/Maps/OsmNav/Colider/PoseCovariance.cs) z EKF),
  [`RobotFootprint`](../Src/ARBot.Common/Maps/OsmNav/Colider/RobotFootprint.cs) (kapsle),
  [`Obstacle`](../Src/ARBot.Common/Maps/OsmNav/Colider/Obstacle.cs) (kruh),
  [`ControlCommand`](../Src/ARBot.Common/Maps/OsmNav/Colider/ControlCommand.cs),
  [`PerceptionOptions`](../Src/ARBot.Common/Maps/OsmNav/Colider/PerceptionOptions.cs).

### Souřadnice a přesnost

Pozice v Colideru jsou sdílený `ARBot.Common.Point2D` (**float**), posuny/vektory `Vector2D` (**double**);
`MotionArc` drží mezivýpočty (rotace, vzdálenosti) v lokálních `double` a je bez alokací. Float u pozic je
vědomý kompromis (u téměř rovných oblouků ~mm, funkčně neškodný proti bezpečnostní rezervě) — detaily
a odůvodnění sjednocení `Point2D`/`Point2DF` viz [decisions.md](decisions.md) (2026-08-04). Konvence úhlů
a rámců je konzistentní s [imu-and-frames.md](imu-and-frames.md).

## Návrhová rozhodnutí a omezení (shrnutí, detail v [PDF](OsmNav-popis.pdf))

- **Edge-based graf** proto, že zákazy/příkazy odbočení a jednosměrky nejdou zapsat do uzel-based grafu
  (v uzlu není známo, odkud vozidlo přijelo).
- **Goal-rooted pole nezávisí na startu** → jedno sdílené `GoalField` obslouží **libovolný počet hypotéz
  o poloze** (částice Monte Carlo lokalizace) — každá jen čte svou lokální hodnotu a gradient; neměnná síť
  i pole jsou v paměti jen jednou.
- **Runtime značky jsou globální** (jeden model světa) → promítají se do sdíleného pole inkrementálně
  (LPA\* opraví jen dotčenou část, ne celý graf).
- **Omezení:** vstup jen `.osm` XML (menší výřezy, malá mapa v paměti); turn-restrikce jen **via-node**
  (vzácné via-way se přeskočí); výpočet pole je **jednovláknový**; heuristické zaostření hledání (koridor
  místo „koule") je připravené jako budoucí volitelný režim.

## Otevřené úkoly

- ~~**Napojení na řídicí smyčku**~~ — **hotové** (`GlobalNavigator`, fáze 0–4; robot jede k cíli po
  síti, trasa je vidět v mapě a vrstva si uzavírá neprůchozí hrany). Zbývá recovery manévr, průřez
  koridorem a ověření na HW — vede se to v [global-navigation-runtime.md](global-navigation-runtime.md).
- Zdroj `.osm` dat a životní cyklus `RoadNetwork`/`GoalField` (kdy stavět, kdy přeplánovat) —
  rozhodnutí je v návrhu výše (síť vlastní runtime, jedno `GoalField` na misi).
- Zdroj `Obstacle` seznamu (z vize / polárního gridu — [traversability-grid.md](traversability-grid.md)).
