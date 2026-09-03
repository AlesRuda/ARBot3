# Mise FreeRun (`FreeRunMission`) — jízda v koridoru bez mapy

**Stav: hotové a ověřené v simulaci proti pravdě, na HW neověřeno** (2026-08-25).

Kód: [`FreeRunMission`](../Src/ARBot.Common/Missions/FreeRunMission.cs),
[`FreeRunConfig`](../Src/ARBot.Common/Missions/FreeRunConfig.cs),
[`CorridorSource`](../Src/ARBot.Common/Localization/CorridorSource.cs),
zpráva [`FreeRunMsg`](../Src/ARBot.Common/Logs/FreeRunMsg.cs), rozbor `ARBot.Analyze freerun`.

Nejjednodušší mise: robot se drží v **pravé polovině detekovaného koridoru** a překážkám se vyhýbá
podle lokální mapy. **Nepotřebuje mapovou navigaci** — žádnou `.osm`, žádnou trasu, žádný cíl.
Když koridor není, jede rovně.

Použití: **homologace** a **přesun mezi stanovišti**.

**Hotové profily:** [`config/pi-freerun.cfg`](../config/pi-freerun.cfg) pro **skutečné HW na
Orange Pi** (`./ARBot config=config/pi-freerun.cfg` — zapíná misi, **bezobslužný start Run**
a záznam běhu, takže se v UI neklikne nic; ⚠️ **robot se rozjede sám**, viz
[configuration.md](configuration.md#bezobslužný-start-autorun)),
[`config/simulace-freerun.cfg`](../config/simulace-freerun.cfg) pro virtuální HW nad syntetickou
mapou. Hlavní obsah pi profilu jsou komentáře o tom, co se **záměrně nenastavuje**: `map=`,
`corridor=` ani `mapcorr=` mise nepotřebuje (`corridor=` je hranová lokalizace **proti mapě**,
mise má vlastní `CorridorSource`). Viz [configuration.md](configuration.md).

Sourozencem je [`RobotourMission`](robotour-mission.md) — soutěžní mise s QR kódy a servisními
okny (jádro hotové 26. 8. 2026). **FreeRun se dělal dřív**, protože
z ní nepotřebuje nic: žádná servisní okna, žádné QR, žádné potvrzení obsluhou, žádný stavový automat.
Postavit velký automat jen proto, aby v něm mohl být triviální stav, by bylo obrácené pořadí.

> **Společnou abstrakci misí zavádět nebudeme**, dokud existuje jedna. Až vznikne `RobotourMission`,
> teprve se ukáže, co je opravdu společné — vymýšlet to předem znamená hádat.
>
> **Vyhodnoceno 26. 8. 2026, kdy `RobotourMission` vznikla: abstrakce se nezavedla a je vidět proč.**
> Společné mají jen to, že obě produkují cíl — ale na **jinou vrstvu** (FreeRun mrkev do
> `ILocalGoalSink`, Robotour LLA do `IGlobalGoalSink`) a z jiných vstupů. Zbytek se nepotkává:
> FreeRun je bezstavový přepočet snímku na mrkev, Robotour stavový automat s člověkem v cyklu.
> Nadřazený typ by nesl jedinou metodu a nic by nezjednodušil.

## Kam to zapadá

FreeRun je **producent mrkve**. Sedí přesně tam, kde dnes [`GlobalNavigator`](osm-nav.md), a šev už
existuje: `ILocalGoalSink.SetGoal(worldX, worldY, corridorWidthM)`.

```
CameraFrame ─▶ CorridorSource ─▶ RoadCorridor + póza ─▶ FreeRunMission ─▶ SetGoal(mrkev)
                                                                              │
                                          LocalNavigator (occupancy grid, A*, obálka) ◀┘
                                                                              │
                                                                     RegulatorWayPoint[]
```

**Lokální vrstva se nemění.** Occupancy grid, `ClearanceField`, A\*, odstupy od překážek i rychlostní
obálka se použijí nezměněné — FreeRun jen posouvá cíl. To je důvod, proč je ta mise malá: nevzniká
nový řídicí řetěz.

Vedlejší efekt: `SetGoal` má parametr `corridorWidthM`, na který dnes **nikdo není zdrojem** (je to
příprava na test průřezu koridorem, fáze 4b v [global-navigation-runtime.md](global-navigation-runtime.md)).
FreeRun ho má přirozeně z `RoadCorridor.Width`.

## Chování

| situace | co robot dělá |
|---|---|
| koridor je | mrkev v **pravé polovině**, odsazení **`Width/4` od osy** |
| koridor není | mrkev **přímo vpřed** od aktuální pózy (drží aktuální kurz) |
| překážka v pravé polovině | **překážka vyhraje** — A\* ji objede kudy může, i přes osu nebo mimo koridor, a pak se robot vrátí vpravo |
| není průjezd vůbec | zastavit a ohlásit (recovery manévr neexistuje — viz [Otevřené](#co-zůstává-otevřené)) |
| ukončení | **jen zastavením obsluhou** (nouzové zastavení / UI) |

**Koridor je preference, ne omezení** (rozhodnutí autora). Do plánovače se nesahá: kdyby měl koridor
být měkkým omezením, znamenalo by to cenu v `LocalPathPlanner` — to je vedený otevřený úkol
„koridor trasy jako cena v lokálním A\*" a řeší se samostatně, ne v téhle misi.

## Matematika mrkve

[`RoadCorridor`](../Src/ARBot.Common/Localization/RoadCorridor.cs) je **v rámci robota (FLU) a bez
mapy** — přesně to, co FreeRun potřebuje:

- `Width` — šířka koridoru [m]
- `Lateral` — příčná poloha robotu vůči ose; **kladné = robot je vlevo od osy** (+Y vlevo)
- `DirectionRad` — směr cesty v rámci robotu; 0 = cesta vede rovně vpřed
- `Ok` / `Reason` — jestli je koridor použitelný

Požadovaná příčná poloha je `−Width/4` (tedy vpravo od osy o čtvrtinu šířky). Se `φ = DirectionRad`,
směrovým vektorem `d = (cos φ, sin φ)` a levým normálem `n = (−sin φ, cos φ)`:

```
mrkev_body = L·d + (−Lateral − Width/4)·n
```

**Kontroly znaménka** (bez nich se to splete):

| stav | očekávání | vyjde |
|---|---|---|
| robot na ose, `Width = 2` | mrkev 0,5 m **vpravo** | `−0,5·n`, a `n` je vlevo ✓ |
| robot už na požadované čáře (`Lateral = −0,5`, `Width = 2`) | mrkev přímo po směru cesty | `0·n + L·d` ✓ |
| robot vpravo od požadované čáry (`Lateral = −1,5`, `Width = 2`) | mrkev **vlevo** | `+1,0·n` ✓ |

Do světa se to převede **pózou v čase pořízení**, ne „poslední známou":
`mrkev_world = póza ⊕ R(PoseTheta)·mrkev_body`. Póza cestuje spolu s koridorem — tatáž konvence, jakou
už má `RoadCorridorMsg.PoseX/PoseY/PoseTheta` a `MapCorrelationMsg` (párovat podle razítka nepřežije
seek; viz [record-replay.md](record-replay.md)).

**Proč odsazení proporcionální a ne pevný odstup od hrany** (rozhodnutí autora): `Width/4` degraduje
rozumně na obou koncích — na 2m cestě 0,5 m, na 4m 1,0 m, na 1m 0,25 m — a nepřidává konstantu,
protože šířka už z koridoru je. Pevných „0,5 m od pravé hrany" by na 1m cestě poslalo robota **vlevo
od osy**.

**Jediná nová konstanta je lookahead `L`.** To je i jediné, co se bude ladit.

## Návrhové rozhodnutí: vytáhnout `CorridorSource`

[`CorridorLocalizer`](../Src/ARBot.Common/Localization/CorridorLocalizer.cs) **mapu vyžaduje** —
hodí výjimku na null `RoadNetwork` i `GeoReference`, protože srovnává koridor s mapovou osou. FreeRun
mapu nemá. Ale mapově nezávislá polovina toho stupně je právě to, co potřebuje:

| část | mapa | kdo to potřebuje |
|---|---|---|
| `TryPair` — spárovat snímky obou kamer v okně `MaxCameraSkewMs` | ne | oba |
| `MetricPoints` — z `PathEdge` metrické body vlevo/vpravo | ne | oba |
| `Reproject` — kompenzace pohybu mezi snímky | ne | oba |
| `CorridorFinder.Find` — RANSAC dvou přímek → `RoadCorridor` | ne | oba |
| srovnání s `RoadAxis`, měření do fúze | **ano** | jen lokalizátor |

**Návrh:** vytáhnout mapově nezávislou část do `CorridorSource` (z `CameraFrame` → `RoadCorridor` +
póza pořízení; odhadem ~100 řádků z 361). Nad ním pak stojí obojí — `CorridorLocalizer` si přidá
mapové porovnání a měření do fúze, `FreeRunMission` mrkev.

Duplikovat párování by bylo špatně: je to ta nejchytřejší část toho kódu (párovací okno, směr
hledání, kompenzace pohybu) a stála nejvíc měření — viz
[map-correlation-localization.md](map-correlation-localization.md), sekce o `NoPair`.

> ⚠️ **Riziko, které tím bereme:** `CorridorLocalizer` má **naměřené chování** (178 měření za 40 s,
> chyba polohy 0,027 m, kurzu 0,18°) a sahá se do něj. Pojistkou jsou jeho existující testy a
> pravidlo projektu: **starou cestu nesmazat, dokud novou nepotvrdí testy.** Po refaktoru se ověří,
> že `corridor` report nad týmž záznamem dává tatáž čísla.

## Konfigurace

| parametr | význam | výchozí (návrh) |
|---|---|---|
| `LookaheadM` | jak daleko před robota se klade mrkev | `1,5` m (do 3. 9. 2026 `3,0`; zkráceno na polovinu na pokyn autora, mj. kvůli krátkým úsekům jako `OSM/SyntetickyRovny2m.osm`) — **jediná skutečná ladicí konstanta**; z příkazové řádky `freerunlook=`. Měření níže v tomto dokumentu jsou ještě s 3,0 m |
| `RightOffsetFraction` | podíl šířky od osy vpravo | `0,25` (= `Width/4`); `Validate()` odmítne ≥ 0,5, protože to už je na hranici koridoru |

> **Strop rychlosti mise tu není a nebyl ani dřív, i když to tak vypadalo.** Do 1. 9. 2026 měl
> `FreeRunConfig` pole `MaxSpeedMps` s popisem „strop nad existující obálkou" — jenže ho **nikdo
> nečetl** a číst ho ani nešlo: šev do lokální vrstvy je `SetGoal(worldX, worldY, corridorWidthM)`
> a kanál pro rychlost tam není. Nastavit ho tedy nic nedělalo. Pole je odstraněné; rychlost se
> omezuje parametrem **`maxspeed=`**, který nastaví `Profile.MaxAllowedSpeed`, takže platí pro
> celé řízení (motor, rychlostní profil i obálku plánovače) — viz
> [configuration.md](configuration.md#strop-rychlosti-maxspeed).

### Která mise běží: `mission=`

Mise se **vylučují**, takže se nevybírají booleovskými přepínači (jako `mapcorr=`, `corridor=`), ale
jedním selektorem:

| `mission=` | co běží | poznámka |
|---|---|---|
| **`none`** | žádná mise | **výchozí** — cíl zadává obsluha klikem v mapě, jako dnes |
| `freerun` | `FreeRunMission` | homologace, přesun mezi stanovišti |
| `robotour` | `RobotourMission` | soutěžní mise; jádro hotové 26. 8. 2026, ale **čeká na „Start" z UI, které ještě není** — viz [robotour-mission.md](robotour-mission.md). Bez mapy se nezaloží (zadává LLA cíle globální navigaci). |

Neznámá hodnota **skončí hlášením a `none`**, ne tichým ignorováním: „mise neběží, i když si ji
někdo přál" je přesně ten druh chyby, který se pak hledá na soutěži.

**Proč selektor a ne `freerun=true`:** dvě mise zapnuté zároveň by si přepisovaly mrkev a nešlo by
poznat, která vyhrála. Selektor to vylučuje konstrukcí a zároveň nezavádí abstrakci misí, dokud je
jen jedna.

## Telemetrie a záznam

`FreeRunMission` publikuje vlastní zprávu (mrkev, `Lateral`, `Width`, `DirectionRad`, důvod
koridoru, jestli se drží kurz). **Bez zprávy je mise v záznamu neviditelná** a nedá se změřit, jak
jela — a tenhle projekt měří všechno nad záznamem (viz
[record-replay.md](record-replay.md#offline-analýza-záznamu-arbotanalyze)).

## Testování a co je naměřeno

Bez HW, celé v simulaci a jednotkových testech. Testy:
[`FreeRunMissionTests`](../Src/ARBot.Common.Tests/Missions/FreeRunMissionTests.cs).

- **Mrkev — geometrie:** kontroly znaménka z tabulky výše jako samostatné testy proti syntetickému
  `RoadCorridor` (na ose → vpravo, na požadované čáře → přímo vpřed, příliš vpravo → táhne vlevo),
  plus proporcionalita odsazení a zatáčející cesta. To je jádro a splete se to nejsnáz.
- **Bez koridoru:** mrkev je `L` vpřed od aktuální pózy, žádné příčné uhnutí.
- **Převod do světa:** pózou **pořízení**, ne poslední známou.
- **Zpráva:** obousměrná serializace a registrace v katalogu (bez ní index zprávu ukáže, ale `Read`
  vrátí `null` — a tváří se to jako chybějící stupeň; přesně to se týž den stalo u `GPSState`).
- **Refaktor `CorridorSource`:** existující testy koridoru prošly bez úpravy chování.

### Naměřeno za běhu (25. 8. 2026)

`mission=freerun virtualhw=true map=OSM/SyntetickyRovny.osm`, 45 s. Osa cesty je `y = 0`, šířka 2 m,
takže **pravda říká: robot má skončit na `y = −0,5 m`**.

| | naměřeno |
|---|---|
| cyklů s koridorem | **618 z 619 (100 %)**, jeden `NoPair` |
| hlášená příčná poloha p50 | −0,503 m (požadováno −0,500) |
| odchylka od vlastního cíle p50 | **0,001 m** |
| **skutečná příčná poloha p50 (ground truth)** | **−0,502 m** |
| **skutečná odchylka p50** | **−0,002 m** |
| poslední čtvrtina běhu | −0,503 m (min −0,518, max −0,494) |

**Druhý běh reprodukuje:** 578 z 579 cyklů, skutečná příčná poloha p50 −0,503 m, poslední čtvrtina
−0,505 m. Robot se tedy usadil **na 2 cm** v pravé polovině a drží to. Kadence mise je ~13 Hz
(na snímek, ne na dvojici).

### Těžší scéna: `SyntetickyKoridor.osm` (nálevka + křižovatka)

Rovná mapa je nejlepší případ. Na koridoru s nálevkou a křižovatkou vyjde:

| | naměřeno |
|---|---|
| cyklů s koridorem | **281 z 534 (53 %)** — zbytek jel rovně |
| důvody | `Ok` 281, `NoCorridor` 240, `OutsideCorridor` 12, `NoPair` 1 |
| šířka koridoru | p50 3,02 m, **min 0,78 — max 3,45 m** |
| odchylka od požadované čáry | p50 −0,057 m, **p90 0,440 m**, max 1,063 m |

**Záložní cesta „drž kurz" tedy běží skoro v polovině cyklů** — na tuhle scénu je ten profil právě
proto. A přesnost je o řád horší než na rovné mapě (p90 0,44 m proti 0,001 m u p50), což je
očekávané: šířka se mění čtyřnásobně, takže se s ní hýbe i požadovaná čára, a koridor vypadává.

To je zároveň **jediné místo, kde má smysl ladit lookahead** (`freerunlook=`) — na rovné mapě je
už teď regulační odchylka pod centimetrem, takže tam není co zlepšovat.

> `OutsideCorridor` 12× (2 %) stojí za pozdější pohled: na 3m cestě by k tomu dojít nemělo. Nejspíš
> se u křižovatky proložila jiná dvojice hranic. Není to blokující — mise ten cyklus prostě jede
> rovně.

### Jak to pustit

Profily v `Src/ARBot/Properties/launchSettings.json`:

| profil | k čemu |
|---|---|
| *mise FreeRun, rovna mapa* | měření proti pravdě (osa `y = 0`, šířka 2 m) |
| *mise FreeRun, koridor s nalevkou a krizovatkou* | těžší scéna — protestuje i jízdu bez koridoru |

Bezobslužně se záznamem:

```bash
ARBot.exe selftest=true st_seconds=45 st_record=true no_uart=true virtualhw=true mission=freerun map=OSM/SyntetickyRovny.osm
```

```bash
dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- freerun Records/<zaznam>.rec --axisy=0 --truewidth=2.0
```

> **`map=` je potřeba i bez mapové navigace** — mise podle mapy nejede, ale **virtuální kamera z ní
> renderuje cestu**, takže bez mapy není co detekovat. Na reálném HW `map=` pro FreeRun potřeba není.
> `goal=` se naopak nezadává vůbec: mrkev vyrábí mise.

> **Rozjezd je v průměru zahrnutý a kazí ho:** robot startuje na ose, takže první sekundy se teprve
> srovnává (odtud `p90 0,138 m` a `max 0,501 m` u odchylky). Zajímá **p50 a konec běhu**, ne průměr —
> `ARBot.Analyze freerun` proto tiskne i poslední čtvrtinu zvlášť a na ten transient upozorňuje.

## Co zůstává otevřené

- ⚠️ **První jízda na železe ve stísněných podmínkách (2. 9. 2026) skončila nárazem.** Koridor byl
  jen ve 2 % cyklů, mrkev „drž kurz" byla v 97 % plánů nedosažitelná a lokální vrstva z toho
  vyrobila pahýl k čelu překážky s odstupem 0,05 m. Rozbor a **zadání průzkumu** (fallback
  plánovače, eskapovací zóna, sémantika `Speed` waypointu, vrstva „zastav a rotuj"):
  [plan-freerun-stisnene-podminky.md](plan-freerun-stisnene-podminky.md).
- **Zaseknutí:** robot zastaví a ohlásí, protože **recovery manévr neexistuje** (couvnutí ani otočka
  na místě) — vedený otevřený úkol v [global-navigation-runtime.md](global-navigation-runtime.md).
  Do té doby je zastavení jediná možná odpověď.
- **Kdyby „hned držet kurz" v praxi cukalo:** známá léčba je podržet poslední koridor N sekund a
  teprve pak přejít na kurz. Vědomě se to **nestaví** — autor volil jednodušší a předvídatelnější
  variantu. Krátké výpadky koridoru jsou přitom časté, takže je to reálný kandidát na první úpravu
  po prvním běhu.
- **Dostupnost koridoru je scénově závislá.** Na rovné testovací mapě je to **100 % `Ok` po prvních
  60 s** (921 měření za 70 s, 24. 8. 2026), ale na nálevce a u křižovatky se rozpadl. Starší číslo
  „8 % za jízdy" je z 22. 8., tedy **před** opravou párování kamer — neplatí.
- **Koridor jako cena v A\*** (aby objezd překážky volil nejmenší vybočení) je samostatný otevřený
  úkol, ne součást téhle mise.
