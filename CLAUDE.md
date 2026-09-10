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
  - ⚠️ **Výjimka: přihlašovací údaje do repozitáře NEPATŘÍ.** Klíče, tokeny a hesla se čtou
    z prostředí (Colab Secrets, proměnná prostředí, soubor mimo repo); do gitu jde jen **jméno**
    té proměnné a poznámka, kde ji nastavit. Bez téhle výjimky pravidlo „vše v repozitáři"
    doslovně vzato říká, že klíč do repa patří — a přesně tak se to 7. 9. 2026 stalo: v
    `Src/Colab/SemanticSegmentation.ipynb` byl natvrdo **LabelBox API klíč s platností do roku
    2042**. Zachránilo to jen to, že soubor ještě nebyl commitnutý, takže se obešlo přepisování
    historie; klíč se přesto musel zneplatnit a vydat nový (kratší platnost, oprávnění
    *Project lead*, ne Admin). Poučení k datům: klíč, který nikdy nepropadne, je horší než ten,
    jehož obnovu si musíš občas vynutit.
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
- **Čas měř přes `TimeBase.Now`, ne `DateTime.Now`/`UtcNow`.** `TimeBase` (`ARBot.Common/Common`) je
  čas startu aplikace plus monotonní `Stopwatch` a **záměrně nesleduje skoky systémových hodin (NTP)**,
  takže razítka jdou monotonně za sebou. Platí pro **razítka zpráv, měření dob, latencí a timeoutů** —
  jinak se míchají dvě základny: `DateTime.Now − TimeStamp` se po synchronizaci hodin skokově rozjede
  a proti `UtcNow` je navíc posunuté o offset zóny (u nás 1–2 h). Systémový čas zůstává jen tam, kde
  je opravdu potřeba **kalendářní datum pro člověka**: jména souborů (`records/yyyyMMdd-HHmmss.rec`,
  `logs/crash-*.log`), hlavička crash logu a seed generátoru. Sjednoceno 4. 9. 2026 na pokyn autora;
  našly se přitom čtyři případy míchání (latence v `CameraFrameProcessor`, `PerfMsg` z `PerfCollector`,
  `Info` z `TraceInfoBridge`, start mise v `RobotourMission`) — všechny šly do záznamu nebo do
  diagnostiky, takže tam posun o dvě hodiny nebyl vidět jako chyba, jen jako nesmyslné číslo.
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
  (`ARBot.Common/Configuration`, 60 klíčů s popisem a typem), profily `klíč=hodnota` (`config=cesta`)
  a panel *Tools → Konfigurace* s výpisem všech parametrů, jejich **původu** a uložením profilu.
  Precedence **default → soubor → příkazová řádka** (příkazová řádka přebíjí schválně, jinak by
  přestalo platit skriptované A/B měření). **Neznámý klíč nebo neplatná hodnota v profilu je chyba
  při startu**, ne tichý pád na default — to je hlavní zisk. Od 4. 9. 2026 se parametry čtou **typovanými
  odkazy** `ParamRegistry.NoUart.Value` (špatný klíč se nepřeloží, default jen v registru, i ten
  z `Profile`/konfiguračních tříd se čte, ne opisuje); `Program.GetParam*` neexistuje. Předtím si nechalo
  signaturu, takže se žádné z ~50 míst čtení neměnilo. ⚠️ **Do 5. 9. 2026 se přitom účinná
  konfigurace do záznamu nedostávala vůbec**, ačkoli se to tvrdilo tady i v komentářích: vypisuje se
  **před** startem runtime, kdy `TraceInfoBridge` ještě nestojí a nic nebufferuje. Dnes se po
  připojení mostu zopakuje spolu s verzí binárky (viz
  [record-replay.md](doc/record-replay.md#verze-binárky-a-konfigurace-v-záznamu-od-5-9-2026)). Změna platí **až po restartu** (panel ho
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
  (`Common ← HAL ← Runtime ← app`), kam patří fúze / adaptéry / řídicí smyčka.
- [doc/headless.md](doc/headless.md) — **runtime bez UI**: od 4. 9. 2026 je řídicí runtime
  (`ARBotRuntime`, `ARBotHW`, `CrashLog`, `RuntimeBootstrap`) ve vlastním projektu **`ARBot.Runtime`**
  (mezi HAL a aplikacemi, namespace `ARBot.Robot` beze změny, do `Common` nemůže kvůli směru
  závislostí) a konzolový **`ARBot.Headless`** ho spouští bez Avalonie. **Jen Run.** Návratové kódy
  0 / 2 (vadná konfigurace) / **3 (už běží jiná instance)** / pád.
  **Webový náhled** (`web=<port>`, výchozí 0 = vypnuto): půdorys, snímek kamery včetně **vrstvy
  „cesta z RGB"** (`?layer=prob`), senzory, stav, hlavička s verzí. Kreslení je
  v `ARBot.Common/Rendering` (vidí na to i `ARBot.Analyze`), HTTP v `ARBot.Runtime/Web`; **líný
  render** (bez publika se nekreslí ani nekopíruje snímek) a **vlastní server nad `TcpListener`**
  (`HttpListener` na Windows bez admin práv neumí jiný prefix než localhost).
  **Od 5. 9. 2026 (fáze 4) se to provozuje jako služba** — plán
  [doc/plan-headless-provoz.md](doc/plan-headless-provoz.md), nasazení
  [deploy/README.md](deploy/README.md):
  - **systemd jednotka `arbot`**, `enabled`, `Restart=always`. Původní zákaz („robot, který se sám
    rozjede, je horší než robot, který stojí") **platí dál**, jen ho drží dvoufázový běh: bez
    zadané mise runtime nastartuje, rozjede senzory a **stojí**, dokud mu člověk nevybere misi.
  - **Misi vybírá stránka**, ale jen při **drženém nouzovém zastavení** (gate i na serveru, 409) —
    web misi *nastaví*, rozjede ji až uvolnění stopu. Fáze čekání se **nenahrává** (~19 MB/s).
  - **`dataroot=`** (datový adresář) a **zámek jedné instance** kvůli nasazení **stínovou kopií**:
    binárky běží z kopie bokem, data zůstávají v původním adresáři, **restart služby = nasazení**.
  - **Verze** z `Src/Directory.Build.props` (razítkuje se jen s `-p:ArbotStamp=true`) je v hlavičce
    stránky, v crash logu i v záznamu.
  - **Od 6. 9. 2026** stránka ukazuje i **kvalitu GPS** (fix, družice, DOP, sigma, nebo důvod, proč
    se poloha nepoužívá) a nabízí **Power off** (`poweroffcmd=`) — vypnutí celé desky se zastavením
    runtime, aby šlo robotovi bezpečně odpojit napájení.
  - **Ověřeno na Orange Pi 5. 9. 2026**: služba, SIGTERM → `Stop()` 7 ms, zámek, náhled včetně textu
    měřítka a živého snímku z D435, CPU 6,2 % ve fázi čekání. **Neověřeno: celý průchod misí na
    zařízení a start po skutečném rebootu.** ⚠️ Jednou spadl na **SIGSEGV**, když byl na Pi zároveň
    otevřený *RealSense Viewer* (souvislost není prokázaná, jen časově sedí) — `CrashLog` nativní
    pád nezachytí.
- [doc/decisions.md](doc/decisions.md) — **deník rozhodnutí** (proč jsme co udělali); sem patří
  netriviální rozhodnutí, která se nedají vyčíst z kódu. Přidávej nová nahoru.
- [doc/devlog.md](doc/devlog.md) — **DevLog / deníček vývoje** (co se dělo den po dni);
  chronologický příběh projektu. Nejnovější nahoru; udržuj průběžně.
- [doc/build-and-platforms.md](doc/build-and-platforms.md) — platformy, HAL (Windows/Armbian),
  nativní knihovna, RealSense verze, externí (ne-NuGet) reference.
- [doc/ekf-fusion.md](doc/ekf-fusion.md) — EKF senzorická fúze (`ARBot.Common/Fusion`);
  hloubkově [doc/EKF_fuze_dokumentace.docx](doc/EKF_fuze_dokumentace.docx).
  ⚠️ **Od 6. 9. 2026 fúze posuzuje kvalitu GPS fixu** (`gpsminsat=`, `gpsmaxdop=`, `gpsdopsigma=`):
  do té doby brala **každý** fix s `IsFixed` a vždy s toutéž sigmou, ačkoli počet družic a DOP
  zpráva nese. Našlo se to tak, že na robotu **ujel odhad polohy ~570 m jedním směrem, zatímco robot
  stál** (rychlost ve stavu nula, takže polohu netáhla predikce, ale měření). Podstatné je
  **škálování sigmy podle DOP** (kvalita je spojitá veličina), brána má odstranit jen nesmysl —
  a **neznámá hodnota není špatná**: přijímač, který DOP nehlásí, projde. Zároveň se opravilo
  mapování u-bloxu, které bránu obcházelo: `fixType` se jen **přetypovával** na `FixQuality`, takže
  **samotný mrtvý odhad** (bez družic) se tvářil jako platný fix — a přesně takové řešení ujíždí,
  když robot stojí. **Příčina těch 570 m ale potvrzená není** (robot byl vypnutý), proto ta kvalita
  přibyla i do webového náhledu.
  ⚠️ **Od 6. 9. 2026 je napojená i T265** — do té doby `BuildSensorSources` drátoval jen IMU, GPS
  a motory, takže kamera běžela a **její data neměla kam téct** (v záznamu po ní není ani stopa;
  padá tím i dřívější domněnka „T265 by přidala 200 Hz"). Protože nemá magnetometr, jde její yaw do
  fúze **jen jako úhlová rychlost z rozdílu** (na nepřekrývajícím se okně 0,5 s) — jako kurz by
  vnutil filtru libovolně otočený svět. Rozlišuje to `IMUState.HasAbsoluteHeading` (verze zprávy 3).
  **Absolutní kurz z ní nevznikne**, dokud nebude offset yaw stavem EKF.
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
  ⚠️ **Kurz z VN100 byl 6. 9. 2026 o −59° vedle** (`20260906-082403.rec`), ačkoli 2. 9. seděl na
  −0,25° — změřeno proti GPS kurzu a proti směru posunu polohy. Chyba **není v GPS, v našem
  kódu ani v gyru**; magnetické pole je v obou záznamech stejné a kurz přepočtený **z pole** je
  proti GPS řádově deklinace, takže se od něj odtáhlo **atitudové řešení senzoru**. Fúze kurz
  **neváží, přebírá** (`odhad − IMU yaw` = −0,01° ± 0,21°), takže to jde 1:1 do mapy i mrkve.
  **Neopravovat softwarově** — nejdřív read-only `VNRRG` (registry 35 / 44 / 23 / 26) a **projet
  smyčku**: při dvou směrech o 180° je měkké železo od konstantního posunu nerozlišitelné.
  Senzor sám si přitom hlásí `YprU` (yaw 1σ) **0,23°** — a `DefaultMeasurementMapper` to bere
  **přímo jako σ měření `IMU/heading`**, takže si o tu slepou důvěru řekl sám. Prověření
  senzoru ze záznamu dělá **`ARBot.Analyze vn100`**.
  ✅ **Příčina nalezena na živém senzoru** (`deploy/vnprobe.sh`, read-only `VNRRG`): proti
  referenčnímu exportu `vn100-2026-7-8-nastavei z arbot2.sencfg` se liší **právě dva registry** —
  **35** má heading mode **`Relative` místo `Absolute`** (yaw tedy NENÍ kurz k severu, ale
  k tomu, kde senzor naběhl) a **23** má **vymazanou kalibraci magnetometru** (jednotková matice
  místo biasu −0,274 G). Rámce jsou v pořádku. **Zatím neopraveno** — zápis do senzoru a do jeho
  flash je vědomý ruční krok, ne vedlejší účinek diagnostiky. **Obojí opraveno a zapsáno do flash
  týž den** (`deploy/vnrestore.sh`); že Absolute zabral, je vidět na tom, že se kurz po zápisu
  za ~100 s sám přetočil na magnetické pole — **po zapnutí proto počítej s ~2 minutami, než se
  kurz srovná**. ⚠️ **Ta kalibrace je ale HORŠÍ NEŽ ŽÁDNÁ** — změřeno venku nad
  `20260906-153657.rec`: `IMU yaw − GPS kurz` má **sd 9,7°** bez ní (ranní záznam) proti
  **118,3°** s ní, a v modelech vyhrává „zamrzlý kompas". Důvod: její hard-iron bias je
  vodorovně **0,280 G**, tedy **větší než vodorovná složka zemského pole (~0,20 G)** — vnese
  do měření body-fixed vektor silnější než signál a kompas přestane reagovat na otáčení.
  **Správná konfigurace je `Absolute` + VYMAZANÁ kompenzace** (`deploy/vnrestore.sh
  --clearmag`); novou kalibraci změřit otáčením robotu. Oprava heading mode je tím
  nedotčená a prokázaná (`odhad − IMU yaw` = −0,02° ± 0,09°).
  ✅ **Vymazáno a uloženo do flash týž den**: z pole vychází azimut −2,2° (sever), kurz se na to
  za ~170 s dotáhl a usadil na −2 až −3° — souhlasí i s tím, kam robot fyzicky mířil.
  ⚠️ **Smyčka projetá 7. 9. 2026 (`20260907-170728.rec`) a kurz je POŘÁD vedle:**
  `IMU yaw − GPS kurz` p50 **−24,0°**, sd **18,6°**, rozsah −58 … +29° — a chybuje **IMU**, ne GPS,
  protože `Doppler − směr posunu polohy` = **0,31° ± 6,19°** (třetí nezávislá cesta). Fúze kurz
  **nevažuje, přebírá** (`odhad − IMU yaw` = −0,01° ± 0,06°), takže to jde 1:1 do mapy i do mrkve,
  a senzor si přitom hlásí `YprU` **0,151°**, tedy je **~120× přesvědčenější** než jaká je jeho
  chyba. Vada je v **poli, vázaném na tělo robota**: 1. harmonická (tvrdé železo) **27,2°**,
  2. harmonická (měkké) **25,2°**, `|B|` 0,363–0,511 G a sklon 46–83°, ačkoli obojí má být
  konstanta; gyro je čisté (klidový bias −4,6 °/h). ✅ **Kalibrace to spravit může, protože motory
  to skoro nejsou** — `ARBot.Analyze vn100` blok 4 páruje pole s proudem z `MotorStateBase`:
  **−0,00258 ± 0,00010 G/A** a `|B|` jízda − stání **−0,015 G**, tedy desetina rozpětí 0,148 G
  (na proudu závislé rušení by se otáčením změřit nedalo, statické jde odečíst). **Další krok je
  změřit novou kalibraci otáčením robotu**, ne stínění. ⚠️ **Druhá, oddělená vada: VPE se táhne za
  vlastním polem 206 s** (`K = 0,00485 ± 0,00074 1/s`) — `kurz z pole − yaw` jde po minutách
  +10 / +2 / −10 / +1,5 / **+30 / +46 / +37** / +5°, takže po zatáčce je yaw desítky stupňů vedle
  i proti svému vlastnímu magnetometru. To kalibrace neopraví.
  ✅ **Od 8. 9. 2026 se model pole nastavuje sám** (`magmodel=`, výchozí `true`): registr 83 se
  zapíše jednorázově po prvním kvalitním fixu, takže kurz je k **pravému** severu (deklinace
  z WMM) a referenční sklon sedí na naši polohu — registr 21 měl 60,9° proti ~65,7° pro ČR, a VPE
  proti té referenci porovnává měřený sklon. ⚠️ **Otáčí to dřívější rozhodnutí „nenastavovat,
  dokud se nezměří smyčka"** (vědomě, důvody v `imu-and-frames.md`) a **na HW to neběželo**;
  `magmodel=false` vrací staré chování. `IMU yaw − GPS kurz` se má zlepšit **přesně o deklinaci**.
  ✅ **Kalibraci si od 8. 9. 2026 robot změří sám: `mission=magcal`** — stojí, obsluha s ním otáčí
  rukou, stránka náhledu říká **co ještě chybí**, a po ťuknutí (jen pod **drženým** nouzovým
  zastavením) si kalibraci zapíše do registru 23 a do flash. Celé z telefonu, bez notebooku
  v poli. Plán a rozhodnutí: [doc/plan-vn100-kalibrace.md](doc/plan-vn100-kalibrace.md), kroky
  [doc/plan-vn100-kalibrace-kroky.md](doc/plan-vn100-kalibrace-kroky.md); offline rozbor
  `ARBot.Analyze magcal`. **Fáze 1 je hotová v kódu (1475 testů), ale NEBĚŽELA na skutečném
  senzoru** — chybí celé terénní měření.
  ⚠️ **První výjezd 10. 9. 2026 skončil bez výsledku a našel tři vady** (fáze 1b, opraveno
  v kódu, **na senzoru zase neběželo nic**): (a) **verdikt byl diagnóza, ne pokyn** — při
  kompletním pokrytí a podmíněnosti ~80 (práh 10⁴) stránka pořád radila „otáčej dál", tedy
  jedinou věc, která pomoct nemohla; (b) **koše se klíčovaly VELIKOSTÍ odklonu** po 10°, jenže
  ruční náklon ji neudrží, takže 22° a 34° daly dvě poloprázdné skupiny a hláška zmizela, aniž
  by se cokoli hnulo; (c) **obsluha neviděla, kam robota natočit**. Léčba: vedle elipsoidy se
  proloží i **samotná koule** (jen tvrdé železo), která rozliší „chybí náklon" od „**měnilo se
  pole**" — druhé neopraví žádné otáčení. Koule je o řád až dva lépe podmíněná (při náklonu
  2,9° 144 proti 1,89 × 10⁴), takže z ní jde **zapsat aspoň tvrdé železo** — ⚠️ ale je to půlka
  práce: odstraní 100 % bez měkkého železa, **80 %** při tom z referenčního exportu a jen
  **38 %** při patologickém. ⚠️ **Rovinná rotace přitom projde podmíněností (538) i zbytkem
  (0,0000) a vrátí bias vedle o 476 787 G** — chytí to teprve třetí brána na velikost měřítka.
  Místo půdorysu se kreslí **mapa pokrytí 24 × 5** (řádek = poloha robota, modrý rámeček = kde
  robot je); ⚠️ **Mercator přes celý směr pole by byl špatně** — při náklonech do 30° je
  dosažitelná jen ~pětina koule, takže druhá osa **není dalších 24 košů**. `MagCalMsg` je
  **verze 2**. ✅ Ranní záznam (`20260910-063617.rec`) hypotézu **potvrdil měřením**: vlastní čísla
  `A` [−0,634; 0,046; 0,178] při podmíněnosti 80; `TryFit` od té doby umí říct **důvod**
  (`out string duvod`). ⚠️ **Druhý výjezd týž den (`20260910-170809.rec`) doběhl s kompletním
  pokrytím a elipsoida se PROLOŽILA** (podmíněnost 135, `sd|B|` 2 mG, půlky 0,63°), ale verdikt
  shodil **rozptyl sklonu 2,09° proti prahu 0,5° — a ten měří akcelerometr, ne magnetometr**:
  roste s dynamikou otáčení rukou (1,1° v klidu → 4,2° při `|acc|` 5–10 % mimo klid), podlaha
  v úplném klidu je **0,42–0,46°**, a akcelerometr má **bias +0,27 m/s² v Z**, který sám natočí
  svislici o ~1° při náklonu 30–40°. Stránka tak nabídla jen tvrdé železo, tedy **horší** výsledek,
  než který odmítla. ✅ **Autor týž den rozhodl: sklon z brány VYŘAZEN, jen diagnostika**
  (`Usable` i verdikt bez něj, viz [doc/decisions.md](doc/decisions.md)); kalibrace z toho
  záznamu je podle nových pravidel POUZITELNÁ a **zapíše se ze záznamu bez dalšího výjezdu**
  (`ARBot.Analyze magcal --bref=0.4897` → `deploy/vnrestore.sh --magcal <12 čísel>`, zatím
  **nezapsáno**; na robotu s novým kódem neběželo). Slabým místem
  výsledku zůstává složka `z` (půlky: `b_z` 0,063 vs 0,018 G). Kalibrace **akcelerometru**
  (bias 0,27 m/s² v Z, registr 25) je otevřený samostatný úkol — prahem sklonu by ale kalibrace
  neprošla ani s ní.
  ✅ **Dokumentace VN** (TN002, TN004, ICD, manuál, datasheet) 10. 9. 2026 potvrdila konvenci
  registru 23 i tvar verdiktu — **našla ale dvě vady v zacházení
  s registrem 44**, obě opravené: (a) nedělal se **`Reset` před `Run`**, takže registr 47 nesl
  řešení z minulé mise a nebyl to nezávislý údaj; (b) **nedokončená mise nechala senzor v `Run`**,
  což TN002 kap. 5.2 uvádí přímo mezi příčinami ujíždějícího kurzu. Pozor: `ApplyCompensation = 1`
  **není „true", je to Disable** (ICD tab. 3.57: Disable = 1, Enable = 3). ⚠️ **Padlo tím i
  tvrzení „VPE 206 s je samostatná vada, kterou kalibrace neopraví"** — manuál kap. 3.3.5 říká
  opak (Absolute mode při dlouhodobé poruše kurz *slew*uje, a bez platné HSI kalibrace se režimy
  chovat nemusí) a registr 35 má **zapnuté** adaptivní filtrování i ladění, které samy zpožďují;
  přeměřit `K` až po kalibraci, viz [doc/imu-and-frames.md](doc/imu-and-frames.md). ⚠️ VN umí
  i **2D kalibraci** z pouhé rotace na rovině (platí do 5–10° náklonu) — **náš 3D fit to neumí**
  a jejich profil pro plnou 3D je šest otáček kolem různých os, psaný pro senzor v ruce; naše
  „rovina + dva protilehlé náklony" je tedy vědomě náhražka. ⚠️ **Ta PDF do repozitáře NEPATŘÍ**
  (jsou označená *Proprietary & Confidential* a tenhle repozitář je veřejný) — leží jen lokálně
  v `doc/Vectornav/`, proto jsou všechny závěry citované **s číslem kapitoly**. ⚠️ **Rotace na rovině NESTAČÍ a nestačí ani dva náklony
  na jednu stranu** (podmíněnost 2,0 × 10⁸ resp. 4,7 × 10⁷ proti 434 u páru +/−) — složka `z` je
  jinak nezměřená a promítne se náklonem přímo do kurzu. ⚠️ **Měřítko se váže na registr 21**
  (čte se ze senzoru), protože VPE proti němu porovnává `|B|` a sklon; je tedy možné, že tím
  zmizí i vada 206 s — přeměří se to a **je-li tak, fáze 2 se ruší**.
- [doc/hardware.md](doc/hardware.md) — senzory a připojení (per-zařízení, orientační).
- [doc/record-replay.md](doc/record-replay.md) — pipeline zpráv, záznam/přehrávání běhu,
  vize (BackProject), režimy Run/View/Simulace + otevřené úkoly.
- [doc/traversability-grid.md](doc/traversability-grid.md) — polární grid sjízdnosti z hloubkové
  kamery (depth → point cloud → polární grid, klasifikace + důvěra), robot-centrický, per-kamera.
- [doc/semantic-segmentation.md](doc/semantic-segmentation.md) — **sjízdnost z RGB neuronovou sítí**
  (`backproject=hist|nn`): druhá implementace `IBackProject` vedle histogramu barev, model
  **Model61.1** z ARBot2 (U-Net + MobileNetV2, 128×128, 112,5 MMAC) přes **ONNX Runtime**.
  Ten je zvolený proto, že jeden NuGet nese nativní knihovnu pro win-x64 **i** linux-arm64, takže
  v simulaci i na robotu běží **týž kód** (cena: publish 45 → 70 MB); TFLite runtime by znamenal
  vlastní nativní knihovnu na obě platformy. Model se převádí `models/tflite2onnx.py`, který
  **schová kvantizaci dovnitř modelu** (float na hranici, int8 uvnitř) — jinak by kvantizační
  konstanty musela znát C# strana a špatná hodnota by se projevila jako *tiše horší segmentace*,
  ne jako chyba. Předzpracování (RGB, `v/255`) je převzaté z ARBot2 `EdgeTPUDll/EdgeTPU.cpp`
  a **od 7. 9. 2026 potvrzené proti tréninku** (`Src/Colab/SemanticSegmentation.ipynb`);
  výstup se **normalizuje součtem
  kanálů**, aby práh 128 dal totéž rozhodnutí jako původní `out[0] < out[1]` (model končí sigmoidou,
  součet není 1). Měřidlo: `ARBot.Analyze backproject` — statistika **zvlášť za každou kameru**
  (míchat je je past: zamrzlá pravá D435 dělá průměr podezřele stabilním, proto report počítá
  i počet různých obrazů). Naměřeno na Windows (Release): v **simulaci** síť **11,3 ms** (int8) /
  **6,9 ms** (rozbalený do float — je to táž kvantovaná síť, jen s int8 vahami rozbalenými zpět, ne původní float model) proti **2,4 ms** histogramu, shoda 97,4 %; **venku ze zařízení**
  (`records/test/20260906-082403.rec`, levá kamera) shoda **89,6 %**, síť hlásí 70–75 % sjízdné
  plochy proti 80–85 % histogramu. **Rozdíl je vidět až na reálném asfaltu**
  ([obrázek](doc/media/backproject-nn-vs-hist-20260907.png)): histogram rozhoduje per-pixel podle
  barvy, takže **zrní** a hranice trávy je roztřepená; síť dá souvislou plochu. ⚠️ **Není to ale
  verdikt** — ground truth k záznamu není a na zarostlé ploše bez cesty je síť nerozhodná,
  zatímco histogram tvrdí 80 % sjízdné. ✅ **Změřeno na Orange Pi 7. 9. 2026 a je to únosné:**
  síť tam stojí **10,2 ms**, tedy prakticky totéž co na vývojovém PC — při dvou kamerách po 30 fps
  ~61 % **jednoho** jádra z osmi, asi 7,6 % celkového CPU. ⚠️ **Na ARM je pořadí variant OBRÁCENÉ
  než na x86**: int8 10,2 ms proti 15,0 (rozbalený) a 16,1 (float), kdežto na x86 int8 prohrával
  (11,3 : 6,9 : 8,0). Výchozí `int8` je tedy správně — ale kdyby se vybíralo podle čísel z PC,
  vybralo by se špatně. Přesnost je u všech tří variant stejná (88,16–88,23 %) a mezi Pi a Windows
  vychází na setinu procenta shodně. ✅ **A/B za skutečného běhu runtime na Pi** (7. 9. 2026,
  90 s na variantu, stojící robot): `compute_ms` celého zpracování snímku **8,7 → 15,7 ms**, tedy
  **+6,2 až +7,0 ms** — a to je **míň, než stojí samotná inference (10,2 ms)**, protože se ušetří
  histogram přes plný snímek a `PathEdges` běží nad **24× menším** obrazem. **Snímky se
  neztrácejí** (30 sn/s drží obě varianty), řídicí smyčka si nestěžovala, alokace dokonce klesly
  (70 proti 105 kB/snímek). Obě kamery stojí ~12 % CPU proti ~7 % u histogramu.
  ⚠️ **Nikdy to ale nejelo ani neřídilo** — všechna měření jsou ze stojícího robota.
  ✅ **NPU cesta hotová a změřená (7. 9. 2026): `backproject=npu`, `RknnBackProject` přes P/Invoke
  na `librknnrt.so`, převod `models/onnx2rknn.py`.** **3,3 ms proti 10,2 ms na CPU** (3,1×), za běhu
  runtime **jen +1,2 až +1,5 ms proti histogramu** (`compute_ms` 8,1 → 9,3), přesnost prakticky
  stejná (87,71 % / IoU 0,838). Každá kamera dostane vlastní jádro NPU. **Padlo tím dřívější
  doporučení psát C++ shim** — z `rknn_api` stačí pět volání a dvě malé struktury. Tři pasti, které
  z dokumentace RKNN nejsou vidět: (a) zdrojem **musí být float model** (u kvantovaného RKNN ignoruje
  mean/std a chce vstup už kvantovaný), (b) **u ONNX umí jen NCHW** a NHWC hlásí matoucím
  „len of mean_values … expect 128!", (c) **normalizaci dělá NPU**, takže se posílají syrové bajty
  0..255, ne 0..1 jako u ONNX. Driver je v jádře (`CONFIG_ROCKCHIP_RKNPU=y`, `/dev/dri/renderD129`) —
  **nehledej `/dev/rknpu*`**, to je DRM node, a `lsmod` nic nenajde. ✅ **Nasazení to řeší** (7. 9. 2026):
  `librknnrt.so` je v repu (`Src/ThirdParty/RKNN`) a `nasad.ps1` ji dá vedle binárek, modely
  (`*.onnx`/`*.rknn`, ne zdroje ani testset) do datového adresáře — výchozí cesty tedy sedí
  bez zadávání. Ověřeno celým řetězem včetně stínové kopie.
  ⚠️ Síť počítá ve **128×128** proti plnému snímku histogramu, takže mění hustotu dat pro
  occupancy grid i hranice cesty (dopad naměřený není) — a **zvětšit rozlišení není konfigurace,
  ale přetrénování**: squash 4:3 → 1:1 i nearest resize jsou **replika tréninku**, ne nedbalost,
  takže se **neopravují**. Kvalita modelu na dnešních datech je **neznámá**, ale úžeji, než se
  dřív psalo: model **měl** naměřeno 0,9546 per-pixel proti ~0,80 histogramu na pevné 50snímkové
  sadě — neznámé je, jak si to stojí na **D435 v roce 2026**. ✅ **Změřeno proti pravdě 7. 9. 2026**
  (`ARBot.Analyze backproject --truth=models/testset`, sada 50 snímků je v repu, vytáhl ji
  `Src/Colab/ExportTestSet.ipynb`): **síť 88,2 % / IoU 0,846** proti **histogramu 78,0 % / 0,752**,
  triviální „všechno je cesta" **68,7 %**. Rozhoduje ta správná chyba — histogram **vymýšlí cestu,
  kde není, 2,6× častěji** (FP 20,3 % proti 7,9 %) — ⚠️ ale **není to napříč sadou stejné**: na
  starších snímcích (`ck*`) síť 87,4 % proti 71,2 %, na novějších (`cl4*`, 2022) je **histogram
  nepatrně lepší**. ✅ **Mezera 88,2 vs 95,5 % je uzavřená (7. 9. 2026): trénovací sada se v čase
  měnila**, takže Model61.1 (únor 2021) byl trénován i testován na jiných datech než dnešní
  `models/testset` — čísla se neporovnávají. Sedí to s tím, že `fnTest` v notebooku obsahuje id
  z června 2022, a hlavně s tím, že **Model96.2 mezeru nemá** (0,9680 proti 0,9682) při průchodu
  toutéž cestou. Je to **vysvětlení, ne důkaz** (dohledatelné jen z časů v LabelBoxu, autor
  rozhodl nedohledávat); praktický důsledek platí dál: **u Model61.1 se nesmí tvrdit, že dává
  95 %** — na dnešní sadě dává 88,2 %. **Šest hypotéz je zamítnutých
  měřením** — kvantizace (float 88,16 = int8 88,23), jiný checkpoint, naše rekonstrukce sady
  (originál `ds_train` 88,15), pořadí kanálů (BGR 77,2), vzorkování při zmenšení (0,06 p. b.),
  „novější snímky jsou těžší". **Nezkoušej je znovu**, tabulka je v dokumentu. **Přesnost per-pixel sama nestačí**: „všechno je cesta"
  dá na téhle sadě 68,7 %, proto se tiskne i IoU a rozpad na cestu přidanou/zamlčenou.
  ⚠️ **Model61.1 nebyl nejlepší** — Model61.3 má 0,9599 při **stejné ceně**, Model96.2 0,9682,
  ale 34× dražší; ten slabší se vybral proto, že po kvantizaci musel běžet na **Google Coralu**,
  což dnes neplatí. ✅ **Model96.2 změřen na NPU 7. 9. 2026:** na CPU **637 ms (nepoužitelné)**,
  na NPU **44 ms** a **96,66 % / IoU 0,953** (proti 87,71 / 0,838 u 61.1), falešně přidaná cesta
  **7,9 % → 2,0 %**. ⚠️ Cena: za běhu runtime **půlí snímkovou frekvenci** (16,5 proti 29 sn/s) —
  a jestli to řízení vadí, se neví, nikdy s tím nejelo. ⚠️ **int8 ho ROZBIJE** (37,8 %, recall
  0,209; `optimization_level=2` nepomůže), takže **musí běžet fp16** — autorova varianta
  `_int16.tflite` to naznačovala předem. Zdrojem pro NPU musí být **Keras `.h5`**
  (`models/keras2onnx.py`), protože `.tflite` je dynamic-range kvantovaný a RKNN ho odmítne
  kvantizovat. **U Model96.2 mizí mezera „naměřeno vs. notebook"** (0,9680 proti 0,9682), kdežto
  u 61.1 zbývá 7,3 p. b. nevysvětlených. Další krok je **NPU** (RK3588, 3× ~2 TOPS) —
  postup je v dokumentu, ale první je změřit CPU cestu na Pi.
  ✅ **Rozbor modelu 9. 9. 2026 — polovina výpočtu je zbytečná:** `112,5 → 57,2 MMAC` u Model61.1
  a `3 837 → 2 125` u Model96.2 **při nezměněném rozhodnutí na všech 819 200 pixelech** sady
  (`models/onnxopt.py`, čas na Windows/ORT 3,29 → 2,09 ms resp. 62,7 → 38,0 ms). Dvě exaktní
  úpravy: Conv 1×1 **komutuje s nearest-`Resize`** (spočítá se před zvětšením) a dvě sousední
  Conv 1×1 bez nelinearity mezi nimi se slučují — vzniklo to tím, že `GenericModel20` staví
  MobileNetV2 blok **bez residuálu** a s `expansion=1`, takže lineární bottleneck spojí se
  `expand` dalšího bloku. **Měřidlem v repu prošlo** (`backproject --truth`): celý report je
  proti zdroji **shodný znak po znaku** včetně všech 50 snímků, čas −33 % resp. −39 %.
  ✅ **Od 9. 9. 2026 je to ve výchozí konfiguraci:** `nnmodel=models/Model61.1_int8_deq_opt.onnx`
  (**−54 % času** proti dřívějšímu `_int8`, 3,81 → 1,74 ms, přesnost 88,22 proti 88,23 %, tedy
  v šumu). Kryjí to dva testy — že výchozí model **existuje** (jinak by se zapomenutý soubor
  poznal teprve na robotu a vypadal jako porucha kamery) a že optimalizace **nemění rozhodnutí**
  proti zdrojovému modelu. ✅ **Na ARM přeměřeno 9. 9. 2026** — vyhrává taky, ale mnohem
  těsněji: **9,35 proti 10,18 ms** (−8 %, na x86 −54 %); extrapolace čekala ~7,7 ms a **byla
  mimo**. Staré chování vrátí `nnmodel=models/Model61.1_int8.onnx`.
  ✅ **Optimalizovaný model je od 9. 9. 2026 výchozí i pro NPU** (`npumodel=models/Model61.1_opt.rknn`,
  i v `config/pi-provoz.cfg`): na Orange Pi **3,27 → 2,72 ms (−17 %) při nezměněné přesnosti**
  (87,70 proti 87,71 % / IoU 0,838), kryto testem na existenci výchozího `.rknn`.
  ⚠️ **NPU využilo z −49 % ubraných násobení jen třetinu** — zisk z optimalizace se mezi CPU a NPU
  nepřenáší, proto se musel změřit. Alternativa `Model61.1_opt_fp16.rknn` stojí přesně tolik co
  starý model (3,33 ms) a dá přesnost CPU modelu (**88,19 %**, zamlčená cesta 4,19 místo 4,80 %),
  zaplatí se to o 0,13 p. b. horší falešně přidanou cestou — volba provozního bodu, autor 9. 9.
  zvolil rychlost. ⚠️ **A/B za běhu runtime rozdíl neukáže**: rozptyl `compute_ms` mezi běhy
  (±1,5 ms) je větší než celý zisk (0,55 ms), takže dokazuje jen, že model naběhne a nic
  neregreduje. ⚠️ **U Model96.2 je optimalizace NEVYUŽITELNÁ**, a je to zákonité: lepší checkpoint
  (`Model96.2.onnx`, 96,80 %) je **dynamic-range kvantovaný**, takže `onnxopt.py` na něm najde
  nulu; optimalizovat jde jen horší `.h5` větev, kde se za −12 % času (43,2 → 38,2 ms) platí
  **−1,31 p. b. přesnosti** — zůstává tedy beze změny. ✅ Při tom se zodpovědělo, **z čeho dnešní
  `Model96.2.rknn` vznikl**: z `.tflite` větve, ne z `_float.onnx`, jak se vedlo (fp16 převod
  `Model96.2.onnx` dá přesně jeho 96,66 % a soubor má na bajt tutéž velikost). ⚠️ **Nikdy s tím
  ale nejel** — všechna měření jsou ze stojícího robota. ⚠️ Proč je
  `_int8_deq_opt` o 27 % rychlejší než `_float_opt`, když mají **týž graf i týž počet násobení**,
  není vysvětlené (denormály vyvrácené měřením). ⚠️ Přitom se našlo, že
  `Model96.2.onnx` (z `.tflite`, **96,80 %**) a `Model96.2_float.onnx` (z `.h5`, **95,35 %**)
  jsou **jiné váhy** — lepší checkpoint v repu jako `.h5` není, a NPU varianta má naměřeno
  96,66 %, tedy **víc než její údajný zdroj**. **Mrtvé neurony ověřeny:**
  jen 0,70 % ReLU kanálů (28 ze 4 016), prořezání by dalo 1,7 % — zajímavé je, že v nejužších
  blocích dekodéru je mrtvých **3 z 16**, a že z 16 vstupů `final_conv` **stačí jeden** (kopie
  téhož signálu, 97,2 % rozptylu v 1. komponentě). **Flip-TTA i softmax jsou zamítnuté měřením**
  (+0,07 resp. +0,02 p. b.); práh 0,40 dá +0,45 p. b. přesnosti, ale **zhorší** falešně přidanou
  cestu ze 7,45 na 10,38 %. Chyby v notebooku (sigmoid + SCC, **validace = testovací sada**,
  dropout v každém bloku, `GenericModel25` definovaný dvakrát) jsou sepsané v dokumentu.
- [doc/world-view.md](doc/world-view.md) — world (geo) pohled: mapa (Mapsui) s přepínatelným podkladem
  (OSM online / MBTiles offline / žádný — offline-first na OrangePI) a vypínatelnými vrstvami dat ze
  streamu (poloha+kurz, trajektorie, trasa/graf, značky) + vrstva „Mapa (vize)" mimo stream
  (`visionmap=`, viz [doc/virtual-hw.md](doc/virtual-hw.md)).
- [doc/occupancy-and-local-planning.md](doc/occupancy-and-local-planning.md) — kartézský occupancy grid
  (fúze sjízdnosti z hloubky + z RGB, log-odds, kruhový buffer) a lokální plánování cesty nad ním
  (odstupy od překážek, rychlostní obálka, A\* → `RegulatorWayPoint[]`) + `LocalNavigator` jako vyšší
  řídicí smyčka. Hotové a napojené (`ARBot.Common/Occupancy`), **neověřeno na HW**.
  ⚠️ **Rozbor rychlostní obálky dotažen 7. 9. 2026 a hned něco našel** (`ARBot.Analyze envelope`
  nad `20260907-170728.rec`, FreeRun venku): robot se nezastavoval, **plazil se** — medián
  příkazované rychlosti **0,05 m/s** (podlaha `MinCostSpeed`) a v **53 %** plánů je na podlaze
  **už první uzel**. Vázal skoro vždy **`VAlong`** (odstup od překážky; 77 % plánů, 99,8 % těch na
  podlaze) při odstupu **0,403 m** = `SafeDist`, kdežto `VBrake` (hranice potvrzeného) nevázal
  téměř nikdy (p50 1,00 m/s) — **padla tím dosavadní hypotéza „plazí se skrz neověřený prostor"**,
  půdorys robota z 3. 9. svou práci dělá. Rozpad je **po uzlech**, ne minimum přes plán, a rozhodl
  to sloupec „vzdálenost vázajícího uzlu od robota" (p50 i p90 **0,00 m**): jedno číslo splácne
  „leze už od sebe" a „za dva metry se cesta zužuje". Nese ho **`LocalPlanMsg` verze 2**
  (`EnvClearanceM`, `EnvClosing`, `EnvFreeAheadM`, `EnvVClearance`, `EnvVBrake` na waypoint);
  starší záznamy si ho `envelope` **rekonstruuje** z gridu a kontroluje proti `MinClearanceM`
  (sedí do jedné buňky v 96,9 %, takže to není domněnka). Cesta je přitom **široká** (volný kanál
  p50 3,80 m, kamerový koridor 3,60 m) — robot jen jede 0,4 m od něčeho blokovaného, a ve **41,5 %**
  je to skvrna **do 4 buněk**, tedy šum v mapě. ✅ **Proč jede tak blízko, nalezeno 8. 9. 2026:
  může za to VYHLAZOVÁNÍ dráhy.** `StringPull` přijme zkratku podle **tvrdého** `d ≥ SafeDist`
  (`SegmentPassable` → `Passable()`, ne `VCost()`), takže zahodí odstup, který cena A\* koupila —
  změřeno na syntetické scéně: s dvoumetrovou rampou jde A\* **objížďkou 4,59 m** místo 2,80,
  a **výstupem je pořád táž přímka** s odstupem 0,450 m; na geometrii výsledné dráhy nemá cena
  vliv. Násobí to okno uzlu (strop uzlu platí podél celého, po vyhlazení mnohametrového úseku:
  skvrna 10×10 cm srazila celý 2,8m úsek z 1,00 na 0,33 m/s) a útes `VAlong` (nad 0,55 m plocho,
  pod ní spad na 15 cm). ✅ **Opraveno týž den (`smooth=`, výchozí `time`):** zkratka se přijme, jen
  když se pod obálku vejde **rampa, kterou regulátor odjede** (drží strop vjezdového uzlu, dobrzdí
  na strop výjezdového) **a** nezhorší to jízdní čas proti jemnému dělení. Druhá polovina té změny:
  **strop uzlu = obálka v uzlu**, ne minimum přes okno — bez ní by se sloučený úsek celý jel
  rychlostí nejhoršího místa. `PathResult` se měnit nemusel, rampa vzniká z `Speed` → `VLimit` →
  `Dist2Speed`; **vyměnil se ale bezpečnostní argument** („každý vzorek zastropuje aspoň jeden uzel"
  → „plánovač ověřil, že se rampa vejde pod obálku"), takže `smooth=passable` a únik drží obojí
  staré. Na realistické scéně (koridor 3,8 m, osm skvrn 10×10 cm) `MinClearance` **0,403 → 0,492 m**
  a rychlost **0,050 → 0,488 m/s**, plánování **1,84 → 1,52 ms**, uzlů ale **5 → 19**.
  ⚠️ **Tři pasti, které stály čas:** (a) čas zkratky jako `délka / min(v)` zakazuje jakoukoli změnu
  rychlosti po úseku (17 uzlů na 0,75 m, vede to na magickou toleranci — rampa ji odstraní);
  (b) zbývající dráhu v kontrole rampy měř **od středu buňky**, ne ze spojitého `t` (jinak se obálka
  a rampa rozejdou o 1–2 % a nesloučí se nic); (c) **`IMotionProfile.Dist2Speed` NENÍ průběh `v(s)`**,
  je to jeden krok regulátoru (perioda 0,1 s, činitel 0,9, v nule vrací nulu) a jako `v(s)` rozpadne
  i volnou plochu na 41 uzlů — na tohle je od 8. 9. 2026 v profilu **`Dist2MaxSpeed(dist, endSpeed)`**
  („nejvyšší rychlost `dist` před bodem, kde mám být na `endSpeed`"), kterou používá i zpětný průchod
  v `PathPlanner`u. Její invariant „příkaz nepřekročí strop" platí **jen když robot do místa vjíždí
  pod stropem** — jinak regulátor vrací nejlepší možné brzdění (0,252 proti stropu 0,141). Profil je
  **tatáž instance** jako v `PathPlanner`. ✅ Při tom se opravil i `Speed2Dist` — počítal
  `(v_s − v_e)²/(2a)` (dráhu rozjezdu z nuly na ROZDÍL rychlostí), správně je `|v_s² − v_e²|/(2a)`,
  resp. `|v_s² − v_e²|/a` u `SqrtMotionProfile`; je to teď přesná inverze `Dist2MaxSpeed`.
  ⚠️ **Nic z toho nejelo na HW** a **kolik z chování v terénu dělá vyhlazování a kolik rozmazání
  gridu chybou kurzu, změřené není** — takže **nejdřív kurz** (viz `imu-and-frames.md`),
  pak přeměřit.
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
  `envelope` / `dump` / `cameras` / `log` / `types`), viz
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
- [doc/track-mission.md](doc/track-mission.md) — **mise Track** (`TrackMission`): objezd míst ze
  souboru `*.track` (`mission=track track=<cesta>`). Řádek = `sirka,delka` ve **stupních** (soubor
  je okraj systému, dál se nese radián), poslední řádek `repeat` = jezdit dokola. Ke každému místu
  najde **nejbližší bod na síti cest** a jede na něj — a to není kosmetika, ale oprava vady:
  `Navigator` měří dojezd proti **surovému** cíli, takže při větším odsazení by `Arrived` nenastalo
  **nikdy** a mise by uvízla (past dohledaná u Robotouru). **Mezi body nezastavuje**, jen přepíná
  cíl. Bod dál od sítě než `trackoffroad=` (50 m) misi **přeruší** — tiché přeskočení by znamenalo,
  že robot objel jinou trasu, než člověk zadal. **Nesrozumitelný řádek je chyba, ne přeskočení**
  (mise se nezaloží). Volba mise robota **nerozjede**: automat čeká na stisk a uvolnění nouzového
  zastavení. ✅ **Projeto v simulaci** (36 testů; objela tři místa a po `repeat` začala druhé kolo), ⚠️ **na HW neběželo.** ⚠️ Příklad
  ze zadání (Praha, 50.0337/14.5257) **neleží v žádné mapě v repu** — 367–389 m od sítě
  `OSM/HajeRovne.osm`; funkční ukázka je `config/haje.track`.
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
  ✅ **Od 8. 9. 2026 i MAGNETOMETR s vnuceným železem** (`MagHardIronG`, `MagSoftIron` — 3 čísla
  = diagonála, 6 = plná symetrická matice), náklon a **otáčení robotem rukou**
  (`SimulatedRobot.HandSpinRadPerSec`). Do té doby `VirtualImu` neposílalo ani pole, ani
  zrychlení, takže `mission=magcal` v simulaci **nedělala vůbec nic**. Teď jde celá kalibrace
  proklikat a hlavně **automaticky ověřit, že mise vrátí právě to železo, které se do ní
  vložilo** — jediná kontrola, která chytí záměnu rámců nebo obrácenou inverzi. ⚠️ Rotaci
  **nelze vyvolat motory** (mise zahodí regulátor a smyčka posílá `Drive(0,0)` každý takt) a
  ⚠️ `virtualhw=true` **bez `map=`** nevytvoří žádný HW. Registry v simulaci drží
  `VirtualMagCalControl` v paměti; **neověřuje to železo skutečného robota**, jen náš řetěz.
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
