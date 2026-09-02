# ARBot3 — rozcestník a pravidla projektu

Autonomní mobilní robot. .NET 10, C#. Aplikace `ARBot` (Avalonia UI + Dock), sdílená
knihovna `ARBot.Common` (modely, fúze, algoritmy), hardwarová vrstva `ARBot.HAL`
(+ platformové `ARBot.HALWindows` / `ARBot.HALArmbian`). Zdroj je v `Src/`.

Tento soubor je rozcestník; **detailní doménová dokumentace je v `doc/`** a u konkrétních
komponent (viz odkazy níže). Při práci na dané oblasti si přečti příslušný dokument.

## Pravidla / konvence (dodržovat)

- **Vše v repozitáři** — všechny poznatky, poznámky a dokumentace musí být uloženy v rámci
  projektu (`doc/`, README, komentáře v kódu). **Žádné ukládání mimo repozitář** (žádná externí
  ani soukromá úložiště mimo git).
  - **Platí i pro poznámky asistenta o způsobu práce** (konvence, opravy, „zapamatuj si"):
    **nepatří do agentní paměti** (`~/.claude/.../memory/`) ani jinam mimo git — patří sem do
    `CLAUDE.md` (pravidla práce), do příslušného `doc/*.md` (doména) nebo do komentáře v kódu.
    Toto pravidlo **přebíjí** výchozí chování asistenta ukládat si poznámky do vlastní paměti.
- **Jazyk: čeština** — komunikace, komentáře v kódu i dokumentace jsou česky.
- **Build vždy pro konkrétní platformu — NE `AnyCPU`.** Windows/vývoj/testy = `x64`,
  cílové zařízení (Armbian/ARM64) = `OrangePI`. Např.
  `dotnet test <proj> -p:Platform=x64`. Podrobnosti: [doc/build-and-platforms.md](doc/build-and-platforms.md).
- **Při migracích/přepisech nemazat starou ani zakomentovanou implementaci, dokud
  novou nepotvrdí unit testy.**
- **Převod doménového stavu na zprávu:** doménové/algoritmické objekty si vyrábějí svou log-zprávu
  metodou **`ToLogMessage()`** (→ příslušný `*Msg`); konverzi vlastní doména, `Message` zůstává pasivní
  DTO (směr závislosti doména → `Logs`). Nezakládej `XxxMsg.FromDomain(...)`. Detail:
  [doc/architecture.md](doc/architecture.md).
- **Souřadnicové konvence:** world **ENU** + matematická orientace (0 = východ, +CCW),
  body **FLU** (X vpřed, Y vlevo, Z nahoru). Viz [doc/imu-and-frames.md](doc/imu-and-frames.md).
  **Zeměpisné souřadnice jsou VŠUDE v radiánech** — `LLA`, `GeoReference` i `GPSState` (ten od
  26. 8. 2026; dřív byl jediná výjimka se stupni a byla to tichá past, viz
  [doc/decisions.md](doc/decisions.md)). Převod na stupně patří jen na **okraje**: drivery při
  parsování a UI/telemetrie při zobrazení.
- **Diagnostika poruch jde do `Trace`, ne do `Debug`.** `Debug.WriteLine` je
  `[Conditional("DEBUG")]`, takže v **Release** buildu — a právě ten běží na zařízení — po poruše
  nezůstane **žádná stopa**. Platí pro všechno, co vysvětluje, *proč něco nejede*: stav senzorů,
  selhání připojení, zahozená měření. Vývojářské dumpy (výpis intrinsik apod.) v `Debug` zůstat
  mohou. Ta past kousla **dvakrát** — hláška o zahozeném měření ve fúzi (20. 8. 2026) a kamery
  (2. 9. 2026, kdy v panelu *Debug output* nebyl o nefunkčních kamerách ani řádek a příčina se
  hledala hodinu měřením zvenčí). Hlídá to `DiagnostikaSenzoruTests`.
- **Ověřuj změny buildem a testy** (`dotnet build` / `dotnet test` pod `x64`); u kódu
  s dopadem na HW napiš, co je odsimulované vs. co je nutné ověřit na zařízení.
- **Git: pracuje se přímo na `master`.** Commity jdou do masteru — **nezakládat feature branch**
  (ani „pro bezpečí"). Obecné pravidlo „na hlavní větvi nejdřív odboč" tady neplatí: je to
  jednouživatelské repo, celá historie je na masteru a odbočka znamená jen práci navíc.
  Existující `remotes/origin/*` větve jsou historie, ne aktuální konvence.
- **Commit jen na výslovný pokyn** — a **jeden pokyn = jeden commit** („commitni to" platí pro tu
  jednu žádost, ne pro zbytek sezení). Jinak změnu jen proveď, ověř buildem/testy a veď DevLog;
  na konci hotového celku ohlas hotovo a čekej. *(Autor chce mít commity pod kontrolou sám.)*
- **Průběžně veď DevLog** — na konci sezení se smysluplnou změnou přidej záznam dne do
  [doc/devlog.md](doc/devlog.md) (pravidla psaní jsou v hlavičce toho souboru).

## Doménová dokumentace

- [doc/configuration.md](doc/configuration.md) — **konfigurace aplikace**: registr parametrů
  (`ARBot.Common/Configuration`, 56 klíčů s popisem a typem), profily `klíč=hodnota` (`config=cesta`)
  a panel *Tools → Konfigurace* s výpisem všech parametrů, jejich **původu** a uložením profilu.
  Precedence **default → soubor → příkazová řádka** (příkazová řádka přebíjí schválně, jinak by
  přestalo platit skriptované A/B měření). **Neznámý klíč nebo neplatná hodnota v profilu je chyba
  při startu**, ne tichý pád na default — to je hlavní zisk. `Program.GetParam*` si nechalo
  signaturu, takže se žádné z ~50 míst čtení neměnilo. Změna platí **až po restartu** (panel ho
  umí). Hotové 31. 8. 2026; **panel je proklikaný celý** včetně *Uložit a restartovat* (1. 9. 2026),
  ale **na zařízení nic z toho neběželo** — a systemd jednotka aplikace neexistuje, takže restart
  se tam může chovat jinak.
- [doc/perf-monitoring.md](doc/perf-monitoring.md) — **měření výkonu řízení**: stíhá řídicí smyčka
  svou periodu? Obsazenost periody, zpoždění a **zameškané takty** ze `Scheduler`u, fronty
  a **zahozené zprávy** ze stupňů, CPU procesu — jednou za sekundu jako `PerfMsg` do streamu
  (tedy do UI i do záznamu) a panel *Tools → Výkon*. Zapíná `perf=` (výchozí true), práh varování
  `perfwarn=`. **Fáze 1 a 2 hotové 1. 9. 2026** (23 testů); panel autor proklikal („zdá se to být
  OK"), **na HW neověřeno**. Pozor: **na Windows je verdikt v panelu červený a je to správně** —
  plyne z nálezu níž. ⚠️ **První měření hned něco našlo:** na Windows v simulaci se **3–4 takty za
  sekundu nestihnou vydat včas** (scheduler je dohání) a zpoždění jde až na ~108 ms, **zatímco
  vlastní práce taktu trvá pod 1 ms** — brzdí tedy časovač, ne řídicí kód. Tím padla podmínka,
  kterou si spec kladla pro dva odložené nálezy (dohánění zameškaných taktů, krok rampy dobrzdění
  z periody): **akademické už nejsou.** Opravovat se ale pořád nemají — číslo je z Windows, kde
  hrubé rozlišení `System.Threading.Timer` samo stačí jako vysvětlení; **další krok je přeměřit
  to na OrangePi**. Fáze 3 (teplota, frekvence, CPU stroje) a 4 (`ARBot.Analyze perf`) zbývají.
- [doc/architecture.md](doc/architecture.md) — struktura projektů, směr závislostí
  (`Common ← HAL ← app`), kam patří fúze / adaptéry / řídicí smyčka.
- [doc/decisions.md](doc/decisions.md) — **deník rozhodnutí** (proč jsme co udělali); sem patří
  netriviální rozhodnutí, která se nedají vyčíst z kódu. Přidávej nová nahoru.
- [doc/devlog.md](doc/devlog.md) — **DevLog / deníček vývoje** (co se dělo den po dni);
  chronologický příběh projektu. Nejnovější nahoru; udržuj průběžně.
- [doc/build-and-platforms.md](doc/build-and-platforms.md) — platformy, HAL (Windows/Armbian),
  nativní knihovna, RealSense verze, externí (ne-NuGet) reference.
- [doc/ekf-fusion.md](doc/ekf-fusion.md) — EKF senzorická fúze (`ARBot.Common/Fusion`);
  hloubkově [doc/EKF_fuze_dokumentace.docx](doc/EKF_fuze_dokumentace.docx).
  **Od 25. 8. 2026 fúze bere i `GPS/heading`** (kurz nad zemí, `σ = max(podlaha, atan2(σ_příčné, v))`,
  práh na rychlost, jízda vzad vyloučená) — druhá absolutní reference kurzu vedle magnetometru.
  ⚠️ **Samo to ale nic nezmění a je to změřené:** kompas přehlasuje GPS kurz **~4 000:1** (σ 0,017 rad
  při 100 Hz proti 0,245 rad při 5 Hz), a i při σ srovnané s naměřeným šumem zbývá ~520:1. Příčina
  není v GPS: **σ kompasu popisuje jeho krátkodobý šum, ne jeho bias**, takže filtr věří na 1° něčemu,
  co se trvale mýlí o 3°. **Sčítat víc referencí to neřeší** — musí se změnit, co ta σ znamená.
  Odtud otevřený úkol **„chyby senzorů jako stavy EKF"** (bias kompasu a gyra), jehož předpokladem
  `GPS/heading` je — **ale je gatovaný potvrzením na reálném HW**: ten 3° bias vnutil v simulaci
  člověk, takže se teprve musí ukázat, jestli ho skutečný VN100 vůbec má. Měří to
  `ARBot.Analyze heading`, které tiskne „odhad sedí na IMU na N %" a **umí i běh bez ground truth**
  (rozpor `IMU − GPS kurz`), tedy jde pustit na záznam ze zařízení. Pořídit ho je potřeba **se
  smyčkou**: bias magnetometru se s kurzem otáčí, chyba rámců ne.
- [doc/imu-and-frames.md](doc/imu-and-frames.md) — IMU, souřadnicové systémy, VN100
  (drivery, montáž, reference frame rotation).
- [doc/hardware.md](doc/hardware.md) — senzory a připojení (per-zařízení, orientační).
- [doc/record-replay.md](doc/record-replay.md) — pipeline zpráv, záznam/přehrávání běhu,
  vize (BackProject), režimy Run/View/Simulace + otevřené úkoly.
- [doc/traversability-grid.md](doc/traversability-grid.md) — polární grid sjízdnosti z hloubkové
  kamery (depth → point cloud → polární grid, klasifikace + důvěra), robot-centrický, per-kamera.
- [doc/world-view.md](doc/world-view.md) — world (geo) pohled: mapa (Mapsui) s přepínatelným podkladem
  (OSM online / MBTiles offline / žádný — offline-first na OrangePI) a vypínatelnými vrstvami dat ze
  streamu (poloha+kurz, trajektorie, trasa/graf, značky) + vrstva „Mapa (vize)" mimo stream
  (`visionmap=`, viz [doc/virtual-hw.md](doc/virtual-hw.md)).
- [doc/occupancy-and-local-planning.md](doc/occupancy-and-local-planning.md) — kartézský occupancy grid
  (fúze sjízdnosti z hloubky + z RGB, log-odds, kruhový buffer) a lokální plánování cesty nad ním
  (odstupy od překážek, rychlostní obálka, A\* → `RegulatorWayPoint[]`) + `LocalNavigator` jako vyšší
  řídicí smyčka. Hotové a napojené (`ARBot.Common/Occupancy`), **neověřeno na HW**.
- [doc/path-following.md](doc/path-following.md) — regulátory pohybu (`IRegulator`: `PointRegulator` /
  `PathResult`, `IPathPlanner`, `IMotionProfile`): sledování dráhy z waypointů — plán = geometrie rohů +
  brzdná obálka, exekuce = feedforward + lookahead; analýza odchylky vs. vzdálenost cílového bodu.
- [doc/osm-nav.md](doc/osm-nav.md) — OSM navigace (`Maps/OsmNav`): globální navigace nad OpenStreetMap
  (edge-based graf, goal-rooted pole cost-to-goal / LPA\*, dopravní profily, runtime značky) + lokální
  predikce trajektorie a detekce kolizí (`Colider`). Mapa kódu + odkaz na návrhové PDF.
- [doc/global-navigation-runtime.md](doc/global-navigation-runtime.md) — **napojení OsmNav na runtime**
  (`GlobalNavigator`): LLA cíl → trasa po síti → „mrkev" pro `LocalNavigator`, metadata o postupu úseků,
  detekce záseku/bloudění/přehrazené cesty a uzavírání hran. **Fáze 0–4 hotové** (jízda k cíli po síti,
  trasa v mapě, detektory + uzavírání hran); zbývá recovery manévr, průřez koridorem a ověření na HW.
- [doc/map-correlation-localization.md](doc/map-correlation-localization.md) — **korelace occupancy gridu
  s mapou** (`MapCorrelator`): shoda semantického kanálu `LRoad` s OSM sítí (`RoadScene.IsRoad`) dá odhad
  chyby polohy a kurzu; 3-DOF `(dx, dy, φ)` s anizotropní kovariancí, do fúze jako dvě skalární osová
  měření. Léčba na „špatná lokalizace ⇒ špatná mrkev". **Fáze 1–3 hotové** (jádro, měření ve fúzi,
  zpráva + telemetrie, napojení na runtime), jádro má testy. **Ve výchozím stavu se ale vůbec
  nepočítá** (`mapcorr=false`, od 20. 8. 2026) — nic neřídí a stálo by čtvrt jádra; zapnout
  `mapcorr=true`. Korekce samotné posílat umí (`SendCorrections`, dřív `Enabled`), okno EKF je 3 s.
  **Tři podmínky, než korekce pustit naostro** (honestní σ, rychlostní limit, strop na nesouhlas
  s GPS) — viz [doc/decisions.md](doc/decisions.md); do jejich splnění nemá smysl ladit současné
  chování. **Honestní σ (podmínka 1) poprvé změřena a opravena 25. 8. 2026:** hlášená σ byla
  **1,43× optimističtější** než skutečný rozptyl a nejmenší oblak hlásil **největší** jistotu
  (0,0838 m při skutečné chybě 0,225 m). Léčba: `α` škálovat **vahou informativního důkazu** (buňky,
  které při posunu o krok derivace změní verdikt) → `σ ~ 1/√E_inf`; inverze pryč, optimističnost
  1,43× → 1,28×. **Od 25. 8. večer ZAPNUTO ve výchozím stavu** (`ReferenceInformativeEvidence = 37,5`,
  `mapcorrref=0` vrátí konstantní `α` pro A/B) — reference je teď **fyzikální veličina**
  (m²·log-odds, ne počet buněk), takže σ nezávisí ani na rozlišení gridu (surová váha se lišila 4×,
  σ 2×), ani na kroku derivace (dřív `σ ~ √h`; **tu past to odstranilo mimochodem** — krokem se proto
  schválně nedělí). Stará hodnota `15000` skončí výjimkou z `Validate()`. `MapCorrelationMsg` je
  verze 4 a hodnotu z verze 3 zahazuje (jiné jednotky).
  Měří to `ARBot.Analyze sigma` proti **tuze posunuté** mapě, tedy proti známé odpovědi.
  **Časová korelace mezi cykly změřena a vyřešena 25. 8. 2026 večer:** dekorelační čas **~3 s**
  (2,85/2,93/3,31 na třech bězích — a je to fyzikální konstanta, protože tytéž běhy měly periodu
  odlišnou o 42 %), činitel nadsazení informace 1,88–2,44. Léčba: **`MinPeriod` 400 ms → 3 s**, takže
  každé měření je nezávislé konstrukcí (po změně ρ(1) **záporná**, činitel 1,00). Druhý důvod pro
  tentýž odstup: cyklus stojí **1,31 s**, tedy **celé jádro** — starší údaj „~126 ms / čtvrt jádra"
  byl o řádek mimo; při odstupu 3 s je to ~40 %. Ta 400ms hranice byla v praxi mrtvá.
  ⚠️ **Přitom se našla past v samotném měřidle, která posunula všechna dosavadní čísla:** korelátor
  hlásí posun proti **odhadu** pózy, takže správná odpověď je „posun mapy **+ vlastní chyba fúze**".
  Ta druhá složka v měřidle chyběla (p50 0,105 m!), takže se **chyba fúze účtovala korelátoru**.
  Po jejím odečtení padají **dvě dosud vedené vady**: „σ optimistická 1,28–1,43×" (zbylo 1,03–1,17×,
  a přísnější test `sd(z) = 0,78–0,87` říká, že je σ naopak o ~15 % **konzervativní**) a
  „systematické vychýlení +0,10 m" (bylo to vychýlení **fúze**, hlášené správně; zbytek 0,007–0,023 m
  je pod krokem skenu 0,05 m). **Poctivost σ měř `sd(z)`, ne poměrem souhrnů** — σ se cyklus od cyklu
  mění 3× a velké chyby padají právě na cykly s velkou σ. Léčba té pasti: **póza, proti které se
  korelovalo, cestuje ve zprávě** (`MapCorrelationMsg` verze 5, `PoseX/PoseY/PoseTheta` + `HasPose`,
  stejná konvence jako `RoadCorridorMsg`) — dohledávat ji podle razítka nepřežije seek. Změřeno, že
  ta dřívější aproximace lhala jen o **0–4 mm** (max 35 mm), takže závěry platí.
  **Podmínka č. 1 („honestní σ") je splněná:** σ je přes pět běhů `sd(z) = 0,70–0,87` (~0,80), tedy
  asi **1,25× konzervativní** — a to se **vědomě neopravuje**, protože zmenšit σ = zvětšit autoritu
  korelátoru proti GPS, což je přesně to, co zbylé dvě podmínky gatují.
  **Korekce poprvé pustené naostro a změřené 25. 8. 2026** (`ARBot.Analyze corrections`) — tři nálezy:
  (a) **⚠️ tvrdý gate byl VADA:** korekce dělaly výsledek **horší, než když se nekorigovalo vůbec**
  (příčná chyba p50 0,674 → 0,847 m, zamítáno 42–46 %), protože `Reject` zahazuje podle velikosti
  innovace, tedy **právě ty velké korekce, které jsou potřeba**. Korelátor přitom hlásí správně
  (vlastní chyba 0,02–0,06 m). `GateMode.Soft` je teď výchozí (0,589 m; `mapcorrgate=reject` vrátí
  staré chování). (b) **podmínka 2 nemá naměřenou naléhavost** — přetok pózy p90 0,016 m a max 0,780 m
  je totožný s během bez korekcí (usazování po startu); netestuje to ale velké `P`, tedy běh bez GPS.
  (c) **podmínka 3 je naměřeně NUTNÁ:** GPS má σ **1,5 m** proti 0,088 m korelace, takže když se póza
  odtáhla o 0,37 m, GPS NIS se **vůbec nezměnilo** — nezávislá kontrola je slepá právě na škále, kde
  korelace pracuje. Se soft gatingem (0 % zamítnutých) váha té podmínky ještě vzrostla.
  **Strop je ale nízký, dokud se neopraví kurz:** zisk soft gatingu je 6–13 % a chyba kurzu zůstává
  na vnuceném biasu 3,0° — ten drift znovu vyrábí rychleji, než ho příčná korekce stahuje.
  „Pomohly korekce?" **nelze měřit nad posunutou mapou** (tam je správné odejít od pravdy o posun
  mapy) — musí být `visionmap` = `map` a skutečný drift.
  Další otevřené vady: `TightAxisAngle` vychýlená ~6,3°, **korekce kurzu je ve fúzi bezmocná**
  (IMU kompas ji přehlasuje ~200:1 a soft gating ji u velkých chyb udusí, naměřeno 22. 8. 2026).
  **Hranová lokalizace (`corridor=`) je k 23. 8. 2026 funkční, ale pořád vypnutá:** 178 měření
  za 40 s, chyba polohy 0,027 m, kurzu 0,18°. Zapnout ji naostro gatují tři podmínky výše.
  „Regrese šířkového nesouhlasu" **žádná regrese nebyla** — nesouhlas se měří proti *filtru*
  šířky, ne proti mapě, a jde o jeho zaostávání na cestě, která se skutečně rozšiřuje; proti mapě
  kamera souhlasí na centimetry. **Delší rovná testovací mapa hotová 24. 8. 2026**
  (`OSM/SyntetickyRovny.osm`, 160 m konstantní šířky 2 m): 921 měření za 70 s, z toho **prvních
  60 s 100 % `Ok`**, chyba šířky proti mapě p50 0,002 m, nerovnoběžnost p50 0,086° — proti staré
  mapě 5× víc dat a **bez selekčního efektu**. Dosavadní čísla (včetně `RegatePasses`) se měřila
  nad starou mapou, takže je má smysl přeměřit. **Pozor: robot startuje ve středu obálky uzlů**
  (`BuildOriginFromMap`), takže z mapy dlouhé *L* je ve směru jízdy jen *L/2* — na *N* s jízdy
  při *v* je potřeba `2·(N·v + 10 m)`. Stav a pořadí kroků:
  [doc/devlog.md](doc/devlog.md), záznam 24. 8. 2026, „Rozpracováno / další krok".
  **Estimátor proložení proměřen 24. 8. 2026:** ortogonální regrese a Huberova váha jsou
  **zamítnuté měřením**, ne názorem — nezkoušej je znovu bez přečtení té sekce. Totéž platí pro
  **přehradlování konsenzuální sady** (`RegatePasses`, vráceno na 0): je to no-op i nad hlučnými
  daty, a je znám důvod — práh inlieru `0,10 + 0,15·r` je **10× volnější než rezidua**, takže
  hradlování nemá co vyloučit (sada je vždy 266 z ~270 bodů). Zabralo by jen při hrubých outlierech
  nebo po utažení prahu. **Měř proti pravdě, ne proti `MapWidth`** — ten se z měření učí:
  `corridorfit --truewidth=2.0 --axisy=0`. Takhle se našlo, že **šířka má systematickou odchylku
  +18 mm**, kterou filtr schovával devítinásobně — a **dohledala se příčina**: odchylky hranových
  bodů mají zešikmené rozdělení (medián na okraji, dlouhý chvost ven), takže **nejmenší kvadráty
  sledují průměr**. Léčba je proložení, které cílí **medián**: `FitMode = OrthogonalL1` srazí
  vychýlení šířky na **1,4 mm** (−92 %) a **klesne i rozptyl** (−74 %), příčná poloha na 0,8 mm.
  Huber s MAD je slabší varianta téhož (6 mm), Tukey je srovnatelný s L1 ale dražší. **Naměřeno,
  zatím nezapnuto** — výchozí zůstává `LeastSquares`. **Rozhodnutí autora 27. 8. 2026: čeká se na
  měření na reálném HW**, protože to zešikmení je artefakt drsnosti trávy v simulaci (bez šumu je
  vychýlení −1,7 mm) a na skutečné kameře se ta chyba může ztratit v šumu. Zapínat léčbu vady,
  o které se neví, jestli na železe existuje, by znamenalo ladit simulaci.
  **Příčinou toho zešikmení je drsnost trávy** (změřeno sweepem 24. 8.): bez šumu je vychýlení
  −1,7 mm, při výchozí `grassrough=0,03` +17,0 mm a při 0,12 už **+54,2 mm**; šum hloubky na něj
  nemá vliv. Ono „+18 mm" je tedy **velikost artefaktu simulace**, ne předpověď pro HW — přenáší se
  mechanismus a léčba. Argument pro L1 je tím ale silnější: při drsnosti 0,12 dá 0,9 mm proti
  54,2 mm u LS. Drsnost trávy zároveň řídí rezidua (0,0093 → 0,0269 → 0,0856 m), takže **podlaha
  přesnosti koridoru je daná tvarem okraje trávy, ne hloubkovým senzorem**.
  Odchylky hranových bodů proti známému okraji měří `ARBot.Analyze edgebias`, grid ze záznamu
  (tedy co skutečně vyrobila běžící aplikace) `ARBot.Analyze grid`.
  Měření nad záznamy dělá `Src/ARBot.Analyze` (`corridor` / `corridorfit` / `edgebias` / `grid` /
  `dump` / `types`), viz
  [doc/record-replay.md](doc/record-replay.md#offline-analýza-záznamu-arbotanalyze) — a **měř
  každou variantu víckrát**: rozptyl mezi běhy téže konfigurace je větší, než se čeká. Pozor,
  **rezidua nejsou přesnost** a **méně přijatých při lepší geometrii není zlepšení** — obojí se
  tady už jednou spletlo.
- [doc/mission-freerun.md](doc/mission-freerun.md) — **mise FreeRun** (`FreeRunMission`): jízda
  v **pravé polovině** detekovaného koridoru, překážkám se vyhýbá lokální mapa, **bez mapové
  navigace**; když koridor není, drží kurz. Pro homologaci a přesun mezi stanovišti. Je to
  **producent mrkve** — sedí tam, kde jinak `GlobalNavigator`, a lokální vrstva se nemění.
  **Hotové a ověřené proti pravdě** (usadí se na −0,503 m proti požadovaným −0,500, dva běhy),
  **na HW neověřeno**. Zapíná se **selektorem `mission=none|freerun|robotour`** — mise se vylučují,
  takže se nevybírají booleovskými přepínači. Rozbor záznamu: `ARBot.Analyze freerun`.
- [doc/robotour-mission.md](doc/robotour-mission.md) — **mise Robotour** (`RobotourMission`,
  sourozenec `FreeRunMission`): stavový automat depo → nakládka → vykládka → depo, čtení QR kódů
  z pravé kamery, cíle zadává **globální** navigaci jako LLA. **Běží bez operátora** — je to
  simulace autonomního doručení, takže potvrzování cíle bylo zrušeno (26. 8. 2026) a jediné lidské
  vstupy jsou **QR kód a stop tlačítko**; uvolnění stopu je signál „hotovo". Viz
  [doc/decisions.md](doc/decisions.md). **Fáze 2–5 hotové 26. 8. 2026**
  (62 testů): `QrScanner` + `QrCodeMsg`, `geo:` parser, automat + `MissionMsg`, napojení
  `mission=robotour` a **UI panel** (*Tools → Mise Robotour*). **Zbývá jen ověření na HW (fáze 7)** —
  fáze 6 (přežití restartu) je **zrušená** (27. 8. 2026): mise restart přežít nemusí, stavový soubor
  nevznikne. Důsledek, se kterým se počítá: po restartu se jede od začátku a `ArmingAtDepot` postaví
  **nové** depo tam, kde robot stojí.
  **Od 27. 8. 2026 se cíl z QR kódu přichycuje na cestu** (`Probe` vrací `SnappedTarget` + `OffRoadM`)
  a cíl dál než `MaxTargetOffRoadM` (15 m) od sítě je **nedosažitelný**. Není to kosmetika: `Navigator`
  měří dojezd proti `GoalField.GoalPoint`, což je **surový** cíl, takže odsazení > 3 m by `Arrived`
  neohlásilo **nikdy** a mise by v jízdě uvízla (jízda nemá timeout). `MissionMsg` je **verze 6** a
  `AcceptedLatDeg/LonDeg` v ní znamenají **přichycený** cíl (ve verzích 2–5 surový). Depo a `goal=`
  z příkazové řádky se **nepřichycují**. Těch 15 m je z úsudku — odstup se měří do záznamu, aby šel
  nastavit z dat.
  **Vyzkoušet v simulaci:** panel mise („Start mise") + *Tools → Virtuální senzory*, kde je
  **červené tlačítko nouzového zastavení** — bez něj se servisní okno projít nedalo (virtuální motory
  hlásily stop natvrdo `false`), **náhled kamery** pro čtení a **QR kód do virtuální kamery**
  (svislá deska `SyntheticBillboard`, kreslí se jen do barvy — ne do hloubky, aby se nestala
  překážkou; viz [doc/virtual-hw.md](doc/virtual-hw.md)). **Celý průchod misí autor v simulaci
  proklikal 27. 8. 2026** a funguje; kód se staví na **1,0 m** (z 1,2 m se nepřečte) a stanoviště
  mají v panelu tlačítka s hotovými kódy.
  ⚠️ **`MaxSpreadM` v návrhu (1,0 m) by misi nikdy nezarmovalo** — je pod nominálním šumem GPS;
  je to teď **RMS** odchylka s prahem 2,5 m (maximum s rostoucím *n* roste, takže delší okno
  kritérium přitvrzovalo). Viz [doc/decisions.md](doc/decisions.md).
  **Dekodér je ZXing.Net, ne ZBar** (binding z ARBot2 nebyl k dispozici; ZXing je čistě managed,
  takže **fáze 1 „nativní libzbar na obě platformy" celá padla**) — viz
  [doc/decisions.md](doc/decisions.md), 26. 8. 2026. Úspěšnost čtení **není naměřená**: testy
  dokazují cestu (BGR32 → Y800 → dekodér), protože testovací obraz kóduje týž ZXing.
  Pozor na dvě jména: `StartMission()` a `CurrentStop` — `Start()`/`Stop` by kolidovaly se zděděnými
  metodami `MessageTarget`, které spouští **vlákno stupně**.
- [Src/ARBot/Views/README.md](Src/ARBot/Views/README.md) — dokovatelné dokumenty a nástroje UI
  (DocumentBase/ToolBase + ViewType, design-time náhled, backpressure vzor aktualizací).
- [doc/virtual-hw.md](doc/virtual-hw.md) — virtuální HW (simulované senzory): `VirtualCamera` jako
  náhrada D435 — RGB + hloubka renderované z OsmNav mapy a pózy robota, šev `SetRealHW`/`SetVirtualHW`
  v `ARBotHW` (později i virtuální GPS/IMU). Hotové a otestované; **běh aplikace ověřen** (19. 8. 2026).
  Umí i **umělou chybu pózy** (`poseerror=`, nástroj nad virtuální kamerou) — vnutí do renderu známý
  posun, takže korelace s mapou má proti čemu měřit. Od 22. 8. 2026 renderují kamery **ve výchozím
  stavu z ground truth** (`camerapose=truth`), takže chyba odhadu je měřitelná; simulace umí
  **systematické chyby** (prokluz kol `wheelslip=`, bias IMU `imubias=`), skutečná póza jde do
  záznamu jako `GroundTruthMsg` a mění se to i za běhu v panelu *Tools → Virtuální senzory*.
  Cíl jízdy jde zadat i z příkazové řádky (`goal=lat,lon`), takže bezobslužné běhy umí měřit
  i za jízdy — dřív vždy jen stály. Od 21. 8. 2026 i **dvě mapy** (`visionmap=`):
  kamery renderují z jiného `.osm` než podle kterého robot jede — vnucená chyba je v datech, ne
  v pozorovateli. Ve World pohledu je vidět jako vrstva „Mapa (vize)"; do streamu ani do záznamu nejde.
- [doc/telemetry-view.md](doc/telemetry-view.md) — **telemetrický pohled** (tabulka údajů v čase):
  stav robota, řídicí zásahy a údaje z dalších zpráv srovnané v čase (řádek = zpráva, sloupec = údaj,
  tučně = hodnota právě přišla), detail řádku, tooltipy s významem údajů, výběr sloupců a filtr
  řádků, obousměrné napojení na Replay a **graf vybraných údajů v čase** (schod/rampa, kurzor
  přehrávání, klik = skok). Staví na indexu záznamu (režim View). **Fáze 1 i 2 hotové**, jádro
  má testy; UI ověřeno za běhu jen zčásti. Zbývá režim Run a rychlostní diagnostika plánovače.
  Kroky: [doc/plan-telemetry-view.md](doc/plan-telemetry-view.md).
- [doc/selftest.md](doc/selftest.md) — bezobslužný self-test (`selftest=true`): reprodukovatelné
  A/B měření výkonu vizuální cesty (otevře okna, Run, počká, souhrn z CSV, ukončí se).
- [doc/screen-capture.md](doc/screen-capture.md) — toolbar pro **snímek obrazovky a videozáznam okna**
  (PNG / mp4 / GIF do `doc/media/`): surové snímky rourou do ffmpegu, fallback bez něj, limity záznamu.

Když vznikne nová netriviální doménová oblast, přidej k ní `doc/*.md` a odkaz sem.
