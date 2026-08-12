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
- [doc/imu-and-frames.md](doc/imu-and-frames.md) — IMU, souřadnicové systémy, VN100
  (drivery, montáž, reference frame rotation).
- [doc/hardware.md](doc/hardware.md) — senzory a připojení (per-zařízení, orientační).
- [doc/record-replay.md](doc/record-replay.md) — pipeline zpráv, záznam/přehrávání běhu,
  vize (BackProject), režimy Run/View/Simulace + otevřené úkoly.
- [doc/traversability-grid.md](doc/traversability-grid.md) — polární grid sjízdnosti z hloubkové
  kamery (depth → point cloud → polární grid, klasifikace + důvěra), robot-centrický, per-kamera.
- [doc/world-view.md](doc/world-view.md) — world (geo) pohled: mapa (Mapsui) s přepínatelným podkladem
  (OSM online / MBTiles offline / žádný — offline-first na OrangePI) a vypínatelnými vrstvami dat ze
  streamu (poloha+kurz, trajektorie, trasa/graf, značky).
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
  detekce záseku/bloudění/přehrazené cesty a uzavírání hran. **Návrh, neimplementováno**; obsahuje
  blokující prerekvizitu (GPS + odometrie do EKF).
- [doc/robotour-mission.md](doc/robotour-mission.md) — **mise Robotour** (`MissionController`): stavový
  automat depo → nakládka → vykládka → depo, čtení QR kódů z pravé kamery. **Návrh, neimplementováno.**
- [Src/ARBot/Views/README.md](Src/ARBot/Views/README.md) — dokovatelné dokumenty a nástroje UI
  (DocumentBase/ToolBase + ViewType, design-time náhled, backpressure vzor aktualizací).
- [doc/virtual-hw.md](doc/virtual-hw.md) — virtuální HW (simulované senzory): `VirtualCamera` jako
  náhrada D435 — RGB + hloubka renderované z OsmNav mapy a pózy robota, šev `SetRealHW`/`SetVirtualHW`
  v `ARBotHW` (později i virtuální GPS/IMU). Hotové a otestované, **běh aplikace neověřen**.
- [doc/selftest.md](doc/selftest.md) — bezobslužný self-test (`selftest=true`): reprodukovatelné
  A/B měření výkonu vizuální cesty (otevře okna, Run, počká, souhrn z CSV, ukončí se).

Když vznikne nová netriviální doménová oblast, přidej k ní `doc/*.md` a odkaz sem.
