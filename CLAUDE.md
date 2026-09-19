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
  ⚠️ **Ten test ale kryje jen složky `Devices`** a jen vzorek `Debug.WriteLine($"{Name}: …`, takže
  externí audit 15. 9. 2026 našel porušení pravidla přesně tam, kam nevidí: **výjimka v `Consume`
  kteréhokoli stupně** (`MessageTarget` — fúze, navigace, mise), celý cyklus `LocalNavigator`
  i jeho hláška „NOUZOVE ZASTAVENI - kolize", a `Uart.ReportEx`, tedy **jediné místo, kde se hlásí,
  že port nejde otevřít**. Opraveno; hlídá to `DiagnostikaPoruchTests` (výčet souborů, rozšiřuj ho).
  ⚠️ **Do `Trace` se ale na horké cestě nesmí psát bez škrcení** — takt jede 10×/s, rámce VN100
  100×/s, snímky 30×/s, takže trvalá porucha zaplaví `Trace` i záznam, ve kterém se ta porucha
  hledá, a narazí na strop `TraceInfoBridge.MaxPerSecond` (200/s): ztratí se právě ta **první**,
  nejvíc vypovídající hláška. Na to je `ARBot.Common/Diagnostics/PoruchaHlasic.cs` (první výskyt
  celý, další po 5 s i s počtem potlačených, **jiný druh poruchy jde ven hned**).
- **Ověřuj změny buildem a testy** (`dotnet build` / `dotnet test` pod `x64`); u kódu
  s dopadem na HW napiš, co je odsimulované vs. co je nutné ověřit na zařízení.
- **Git: pracuje se přímo na `master`.** Commity jdou do masteru — **nezakládat feature branch**
  (ani „pro bezpečí"). Obecné pravidlo „na hlavní větvi nejdřív odboč" tady neplatí: je to
  jednouživatelské repo, celá historie je na masteru a odbočka znamená jen práci navíc.
  Existující `remotes/origin/*` větve jsou historie, ne aktuální konvence.
- **Commit jen na výslovný pokyn** — a **jeden pokyn = jeden commit** („commitni to" platí pro tu
  jednu žádost, ne pro zbytek sezení). Jinak změnu jen proveď, ověř buildem/testy a veď DevLog;
  na konci hotového celku ohlas hotovo a čekej. *(Autor chce mít commity pod kontrolou sám.)*
  ⚠️ **Platí i pro drobnou opravu hned po commitu:** 17. 9. 2026 se po „commitni to" (registr
  úkolů) opravil CSS osy a asistent ho commitnul sám, jako by pokyn trval — netrvá. Oprava po
  commitu = změna bez commitu, dokud autor neřekne znovu.
- **Průběžně veď DevLog** — na konci sezení se smysluplnou změnou přidej záznam dne do
  [doc/devlog.md](doc/devlog.md) (pravidla psaní jsou v hlavičce toho souboru).
- **Veď registr úkolů** [doc/ukoly.yaml](doc/ukoly.yaml) (od 17. 9. 2026): nový nález nebo
  záměr = nové téma, změna stavu (hotovo v kódu / ověřeno na HW / odloženo) = úprava tématu, a po
  každé změně **přegenerovat** `dotnet run tools/ukoly.cs` **a hned za ním `dotnet run tools/menu.cs`**
  (výstupy [doc/ukoly.md](doc/ukoly.md) a `web/pages/historie.html` jsou commitované, CI hlídá,
  že sedí se zdrojem). ⚠️ **Pořadí je povinné a druhý krok se nesmí vynechat:** `ukoly.cs` vyrobí
  `historie.html` s **prázdnou** hlavičkou a teprve `menu.cs` do ní doplní společné menu webu;
  19. 9. 2026 se commitnul výstup jen z prvního kroku a CI (`generovane-soubory`) spadlo na
  `git diff --exit-code`. Stav
  **`v-kodu`** („hotové v kódu, na zařízení neběželo") je schválně samostatný — je to nejčastější
  stav v projektu a v seznamu musí být vidět. Sekce „Otevřené úkoly" v `doc/*.md` stav **nevedou**,
  jen odkazují na id v registru. Pravidla a schéma: [doc/plan-ukoly.md](doc/plan-ukoly.md).

## Doménová dokumentace

- [doc/configuration.md](doc/configuration.md) — **konfigurace aplikace**: registr parametrů
  (`ARBot.Common/Configuration`, 85 klíčů s popisem a typem), profily `klíč=hodnota` (`config=cesta`)
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
  ale **na zařízení nic z toho neběželo**, takže restart se tam může chovat jinak. ⚠️ Stálo tu,
  že „systemd jednotka aplikace neexistuje" — **to neplatí od 5. 9. 2026** (jednotka `arbot` je
  popsaná o pár odrážek níž, takže si tenhle soubor odporoval sám se sebou; našel to audit
  15. 9. 2026). Restart pod službou je tím pádem **živá cesta**, ne hypotéza.
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
  - **Od 12. 9. 2026** půdorys kreslí **zóny, které mají být dosaženy** — místa mise (seznam
    z `TrackMsg` — od 13. 9. 2026 **na PŘICHYCENÝCH místech**, protože na surových zóna ležela
    vedle cesty a vypadalo to, že se nepřichycuje (`TrackMsg` verze 3); depo/nakládka/vykládka
    z `MissionMsg`, jinak cíl navigace) jako kružnice
    o **dojezdovém poloměru**, aktivní plnou čarou a zbytek čárkovaně; kvůli tomu je
    **`GlobalNavMsg` verze 2** (`GoalRadiusM` — bez něj byl poloměr jen v konfiguraci, tedy mimo
    data) a **`TrackMsg` verze 2** (celý seznam míst). Legenda zároveň přestala vynechávat
    **ujetou dráhu**. ⚠️ **Na zařízení neběželo**; ověřeno testy a simulací.
  - **Od 6. 9. 2026** stránka ukazuje i **kvalitu GPS** (fix, družice, DOP, sigma, nebo důvod, proč
    se poloha nepoužívá) a nabízí **Power off** (`poweroffcmd=`) — vypnutí celé desky se zastavením
    runtime, aby šlo robotovi bezpečně odpojit napájení.
  - **Ověřeno na Orange Pi 5. 9. 2026**: služba, SIGTERM → `Stop()` 7 ms, zámek, náhled včetně textu
    měřítka a živého snímku z D435, CPU 6,2 % ve fázi čekání. **Neověřeno: start po skutečném rebootu** (`prov-start-po-rebootu`; misi ze stránky robot
    14. 9. odjel, celý seznam Tracku ale neobjel). ⚠️ Jednou spadl na **SIGSEGV**, když byl na Pi zároveň
    otevřený *RealSense Viewer* (souvislost není prokázaná, jen časově sedí) — `CrashLog` nativní
    pád nezachytí.
  - **Od 14. 9. 2026 hlídač zatuhnutí** (`hangwatch=`, výchozí 20 s): `HangWatchdog` je sourozenec
    `CrashLog` — ten chytá pád, tenhle operaci, která **neskončila vůbec**. `Start(Mode.Run)` je jím
    obalený a po vypršení jde do `Trace` hlášení a vedle něj **minidump** do `logs/hang-*.dmp` se
    zásobníky všech vláken. Vzniklo z toho, že 14. 9. runtime při volbě mise ze stránky **zatuhl
    uvnitř `Start()`** (v journalu doběhlo `corridor=false`, `mission=track` už ne, při běžných 5 ms
    mezi nimi), proces žil dál, ale stránka umlkla — a **dohledávat to odkazem na stránce nejde**,
    protože ta je právě to, co chybí; v terénu je u robota jen mobil, takže si důkaz musí pořídit
    robot sám. Arm je **před zámkem** (zatuhnout jde i na čekání na `gate`) a **jen pro `Run`**
    (`WireView` staví index nad gigabajty, tam je dlouhý start legitimní). ⚠️ **Neléčí to nic**, jen
    zapisuje důkaz; ⚠️ **na zařízení neběželo**. `hangwatch=0` vrací staré chování.
  - ⚠️ **Služba se po PĚTI restartech v pěti minutách vzdá** (systemd `StartLimitBurst=5` /
    `StartLimitIntervalUSec=5min`): šestý start skončí `start-limit-hit`, jednotka zůstane `failed`
    a **`Restart=always` ji už nevrátí** — robot je mrtvý do ručního `systemctl reset-failed`. Našlo
    se to 14. 9. 2026 na reprodukčním testu, ale v provozu je to horší: crash loop (a dvě SIGSEGV
    při `Stop()` téhož dne říkají, že to není hypotéza) odstaví robota v terénu **trvale**.
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
  ✅ **Od 12. 9. 2026 má σ kurzu z kompasu PODLAHU** (`imuheadingstd=`, výchozí **5°**,
  `CompassHeadingStdFloor`): sklada se **kvadraticky** s `YprU` ze senzoru, protože VN100 hlásí
  `YprU` **0,059°**, ale jeho změřená chyba proti GPS je **−4,90° / −2,79°** — tedy je
  ~60–90× přesvědčenější, než jaký je. Těch 5° je RMS změřeného biasu (3,99°) zaokrouhlené
  nahoru; `imuheadingstd=0` vrací přesně staré chování pro A/B. Poměr informace kompas : GPS kurz
  tím spadl z **~3,2 × 10⁶ : 1** na **~440 : 1**. ⚠️ **Počtivý filtr z toho ale není, jen méně
  nepočtivý:** bias je časově korelovaný a filtr ho bere jako bílý šum, takže ustálená σ kurzu
  ve filtru vyroste jen z ~0,06° na **~0,58°** proti skutečné chybě 3–5° (pořád ~8× přehnaně
  sebejistý) — táž past jako u `gpsposstd`. ⚠️ **Na HW to neběželo**; další krok je záznam
  s `imuheadingstd=5` a `=0` nad týmž úsekem. ⚠️ **A těch 5° je nejspíš pořád řádově málo:**
  změřeno (`ARBot.Analyze heading`, blok o šumu GPS kurzu), že chyba kurzu z GPS je časově
  **korelovaná** (σ 8,4° / 4,1° při τ 10 s / 5 s), takže počtivá σ pro filtr je `σ·√(τ·f)`
  = **83° / 29°** — a model `atan2(0,3; v)` = 23° to trefuje **náhodou, ne konstrukcí** (příčný
  šum rychlosti je ve skutečnosti 0,007 m/s, ne 0,3). ⚠️ **GPS je 10 Hz, ne 5** — to bylo
  v reportu natéčno a všechny přepočty tak byly dvakrát vedle; teď se frekvence **měří**
  (`FixRateHz`). U kompasu je `τ ≳ 600 s` při 100 Hz, tedy počtivá σ ≈ 1 200°.
  ✅ **Od 12. 9. 2026 se proto kurz z kompasu ŠKRTÍ na `imuheadinghz=` (výchozí 1 Hz,
  0 = neomezeno)** — poměr kompas : GPS spadl z 223 : 1 na **2,2 : 1**. ⚠️ **Škrtí se jen
  absolutní kurz, ne gyro:** `IMU/gyro` jde dál v plné kadenci a mezi odečty kompasu nese kurz
  právě ono (78 % informace o úhlové rychlosti, odometrie jen 2,8 %). ⚠️ **Mapper je tím
  STAVOVÝ** — záruka record/replay platí pro dvě **čerstvé** instance, ne pro jednu sdílenou
  (zlomil se na tom golden replay test, opraven). Cílem zůstává bias jako stav EKF.
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
  záznamu je podle nových pravidel POUZITELNÁ a **11. 9. 2026 se zapsala do senzoru i do flash**
  (`ARBot.Analyze magcal --bref=0.4897` → `deploy/vnrestore.sh --magcal <12 čísel>`).
  ⚠️ ~~Zapsaná, ale NEOVĚŘENÁ.~~ ✅ **Ověřena venku 12. 9. 2026 a SEDÍ** (tři záznamy
  `records/test/20260912-*`, z toho jeden statické otáčení robotem): zbytkové tvrdé železo
  **0,0023 G** (0,46 % `|B|`), zbytková matice **I ± 0,004**, rozpětí `|B|` přes otočku
  **0,148 → 0,019 G**; rušení od motorů je v šumu (−0,0003 proti −0,0026 G/A). Kurz `IMU − GPS`
  **p50 −24,0° → −3,6 / −3,1°**, harmonické 27,2/25,2° → **3,6/2,9°**, a **VPE se přestala táhnout
  minuty** (`K` 0,00485 → **0,0186 1/s**, tedy 206 → **53 s**) — potvrzuje to domněnku z 10. 9.,
  že dlouhá konstanta byla z velké části důsledek nezkalibrovaného železa.
  ⚠️ **Zbylých −3,7° je KONSTANTA, ne železo** (půlrozdíl mezi opačnými směry jen ∓1° a mezi běhy
  mění znaménko; místo vysvětlí η² 0,17–0,19) — a tímhle měřením **nejde rozložit** na pootočení
  senzoru / zbytek kalibrace / šikmé jetí; na to je potřeba průjezd téhož úseku s robotem otočeným
  o 180°. ⚠️ **Deklinace ten zbytek NEVYSVĚTLÍ** — v konvenci projektu se magnetický kurz na pravý
  převádí **odečtením** `D`, takže −4,9° jde na **−10,3°**, ne k nule (že je GPS kurz k pravému
  severu, plyne z `Doppler − směr posunu polohy` = 0,12°, kde se směr posunu počítá ze souřadnic).
  ✅ **Změřeno na živém senzoru týž den** (`deploy/vnprobe.sh`, read-only `VNRRG`): registr 83 má
  `UseMagModel=1` a registr 21 je `(0,199158; **0,0119037**; 0,447158)`, tedy `|B|` **0,4896 G**,
  sklon **65,95°** a deklinace **3,42°** — deklinace se **aplikuje**. ⚠️ **Ale vestavěný model VN
  je zastaralý o ~1,9°** (WMM pro Prahu 2026 dává ~5,3°; rozdíl odpovídá epoše ~2015), ačkoli
  senzor dostal rok 2026,693. Yaw tedy čte o 1,9° výš než pravda a dopočítání zbytku deklinace
  rozpor **zhorší** (−4,90 → −6,78°), takže zbytek v tělesovém rámci je o 1,9° **větší**,
  než se zdálo. ⚠️ **Znaménko té korekce vypadá obráceně, než je**: v azimutu se chybějící 1,9°
  přičítá, ale do záznamu jdou obě veličiny v **matematické** orientaci (`90 − A`), což to překlopí.
  Rozhodčí jsou data, ne úvaha: `kurz z pole − yaw` = **+3,74 / +3,91°** proti předpovědi
  **+3,42°** (= `D_s`), kdežto opačné znaménko dává −3,42°. ⚠️ **Registr 83 je přitom ve FLASH, ačkoli ho `MagModelInit` záměrně zapisuje bez
  `VNWNV`** — perzistoval ho `vnrestore.sh --magcal` z 11. 9. (rok v registru je 2026,693 = 11. 9.
  a přežil reboot). ✅ **A padá tím výhrada u sklonu:** reference je 65,95°, ne starých 60,9°,
  takže neshoda je skutečná — a **akcelerometr ji potvrdil nezávisle** (registr 27 dá `|acc|`
  10,524 m/s² = **+7,3 %**, registr 25 je jednotkový s nulovým biasem). ✅ Registr 54 „surové"
  pole **taky není** — liší se od registru 27 jen o 0,0025 G, kdežto registrem 23 by se lišil
  v X 4×; třetí nezávislé potvrzení nálezu o `UncompMag`. ⚠️ **Sklon pole je 62,3–63,2° proti WMM ~65,9°** (`|B|` sedí), podezřelý je
  **akcelerometr**: `|a|` v klidu 10,487 m/s² proti g = 9,807 (**+6,9 %**), střed koule `z`
  +0,264 m/s², mezi `−acc` a „dolů" z atitudy 1,89° — `magmodel` tuhle neshodu **zvětšil**
  (registr 21 měl dřív 60,9°). Čísla a tabulky: [doc/imu-and-frames.md](doc/imu-and-frames.md),
  sekce „Po kalibraci (12. 9. 2026)". ✅ Při nasazení se **změřila
  konvence registru 23**: VN aplikuje `C·(m − b)`, tedy tentýž vzorec jako náš `Apply`, a `B` se
  zapisuje přímo (kód se nezměnil, drží to `MagCalVnBiasTests`). ⚠️ **Měřit se to musí přes víc os
  a s nejednotkovou maticí** — z osy X samotné vyjde pravý opak, protože mezi kompenzací a výstupem
  leží registr 26; a `C = I` nerozliší `C·m − b` od `C·(m − b)`. ✅ **Změřen i RÁMEC — a byla to
  vada:** registr 23 se aplikuje **před** registrem 26, fit běží až za ním a za převodem FRD→FLU,
  mezi nimi je `diag(−1, −1, +1)`. Bez převodu měl bias v X a Y **obrácené znaménko**, tedy offset
  se **přičítal** (0,22 G vodorovně, víc než vodorovná složka pole); `ToVnwrg23()` teď počítá
  `C_s = T·C·T`, `b_s = T·B`. ⚠️ **Dvanáctka v dokumentaci se tím změnila** — starší zápisy mají
  obrácená znaménka. ⚠️ **Registr 54 není surové pole** (ačkoli ho tak ICD uvádí — změřeno, že se
  mění podle registru 23 stejně jako registr **20 „Compensated IMU"**).
  ✅ **12. 9. 2026 se zavřela i otázka `UncompMag`: v binárním výstupu je taky KOMPENZOVANÝ** —
  `MagnetometerRaw` a `Magnetometer` jsou v záznamu **bit po bitu shodné** (22 445 vzorků,
  `max |raw − comp| = 0` G). ⚠️ **A je to past, na které příští kalibrace stojí:**
  `MagCalCollector` sbírá právě tohle pole a `MagCalMission.WriteToSensor()` zapisuje výsledek
  do registru 23 **přímo**, bez složení s `Reg23Before` — druhé spuštění `mission=magcal` by tedy
  dobrou kalibraci **přepsalo maticí blízkou jednotkové**. ✅ **Opraveno týž den:** mise si registr
  23 před sběrem **sama vymaže** (jen do RAM, takže výpadek napájení vrátí kalibraci z flash)
  a při ukončení **bez zápisu ho vrátí**; když ho nejde přečíst ani vymazat, mise **NEZAČNE**
  (`MagCalPhase.NotCleared`) — měřila by ze zkompenzovaného pole. Skládání `C₁·C₀` se zamítlo:
  potřebuje znát rámcovou transformaci mezi fitem a registrem, a **ta už jednou kousla**.
  Opravená je i hláška `MagCalReport` „výsledek je ABSOLUTNÍ kalibrace" — nad záznamem s nenulovým
  registrem 23 je to **reziduum** (a jako měřidlo zbytkového železa je to přesně to, co bylo
  potřeba). ⚠️ **Na senzoru to neběželo** — ověřeno buildem, testy (1406 / 105 / 104) a simulací.
  Viz [doc/decisions.md](doc/decisions.md).
  Slabým místem
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
  zmizí i vada 206 s — ✅ **přeměřeno 12. 9. 2026: 206 s → 53 s**, tedy zmizela ze čtyř pětin,
  ne úplně.
  ⚠️ **Ta kalibrace ale 14. 9. 2026 UŽ NEÚČINKUJE** (nalezeno 15. 9. v `20260914-170945.rec`
  a `20260914-170611.rec`): `∣B∣` při stání **0,614 G** proti referenčním 0,4897 a proti 0,498 G
  z 12. 9., rozpětí přes záznam **0,177 G** (12. 9. 0,019 G; **před** kalibrací 0,148 G), zbytkové
  tvrdé železo **0,481 G** proti 0,0023 G, harmonické 3,6/2,9° → **12,8/8,1°**,
  `IMU yaw − GPS kurz` p50 **−15,6°** (sd 13,0°). Mezi 12. 9. 13:14 a 14. 9. 17:06 tedy na robotu
  **přibylo železo**. ✅ **Zdroj je zužený měřením: KABELY ke kamerám** (13. 9. se prohodily
  ony, ne kamery) — a **není to jejich proud**: nahrávání začíná dřív než D435, a při rozsvícení
  obou kamer v 6,5 s se pole posune jen o **6,4 mG** při stojícím robotu (yaw 0,3°, náklon 0,0°),
  tedy **4 %** vodorovného offsetu ~160 mG. **Je to jejich železo** (stínění, konektory, feritové
  jádro): rušení je **konstantní vektor v tělese** (`sd(|B|)` po odečtení 0,0040 / 0,0203 G)
  a jeho vodorovná složka je v obou bězích téhož dne shodná — `(0,059; −0,157)` a
  `(0,036; −0,154) G`, tedy ~0,16 G proti zemským 0,199 G (chyba kurzu až ±53°). Měří to
  **blok 5 `ARBot.Analyze vn100`** (`--camwin=`, `--camdead=`). ⚠️ **Širsi okno číslo NEZPŘESNÍ,
  ale zkazí** (při `camwin=5,5` vyjde 22,7 mG, protože se do něj dostane 6° otočení robotu — blok
  takový řádek sám označí). ⚠️ **Stínit ani stěhovat senzor netreba** — to je léčba na rušení
  závislé na proudu, a to jsou změřeně 4 % problému; cena za kalibraci je, že její platnost je
  od teď vázaná na **polohu kabelů**. ✅ **Odtud i „směr se pomalu ustaloval":**
  VPE v senzoru se táhne za polem s `K` = 0,0029 1/s, tedy **τ = 345 s** proti řádově 0,2 1/s
  12. 9. — je to **v senzoru**, `imuheadingstd=` / `imuheadinghz=` na to nesahají a fúze kurz
  pořád **přebírá** (`odhad − IMU yaw` 2,15° ± 8,60°). ⚠️ **Dvanáctka z té jízdy je ale
  NEPOUŽITELNÁ** — běžná jízda neměří složku `z` a podmíněnost 117 sama nestačí; a ⚠️
  **závislost `∣B∣` na proudu je záměna s kurzem** (+0,0054 G/A v jednom běhu, **−0,0152 v druhém**
  týž den; η² = 0,927 vysvětlí kurz) — tedy těleso, ne motory, a kalibrovatelné. Další krok je
  **dát kabely do polohy, ve které mají zůstat, ověřit to minutovým záznamem s jednou otočkou
  na místě** (rozpětí `|B|` přes otočku: 12. 9. **0,019 G**, teď **0,177 G**) **a teprve pak
  `mission=magcal`, s náklony na obě strany**. Měří to nový blok
  `ARBot.Analyze heading --bin=` (*VYVOJ ROZPORU V CASE*). ⚠️ **Platí to zpětně i pro ostatní
  měření nad tím záznamem** (`localplan` ze 14. 9. je měřený při rozbitém kurzu).
  ✅ **Nová kalibrace 17. 9. 2026 (`mission=magcal`, zapsána do senzoru) a 18. 9. PRVNÍ JÍZDY
  s ní** (`20260918-154028.rec`, `-155329.rec`): zbytkové vodorovné železo **11–18 mG** (proti
  158–169 mG 14./17. 9. a 1,3 mG při otáčení na místě 12. 9.), `|B|` 0,484–0,490 G na referenci,
  VPE `K` 0,019–0,036 1/s (**τ 28–53 s**, jako 12. 9., proti 345 s), skok pole při zapnutí kamer
  jen 1–6 mG (kabely statické). `IMU yaw − GPS kurz` p50 **−2,5° / −1,6°**, sd 3,9–4,6°, na
  protilehlých kurzech stejný — **konstanta, ne železo**, ale mezi běhy 13 min po sobě se liší
  (−3,4 proti −1,1°) → argument pro bias kurzu jako stav EKF. Akcelerometr (+6,9 %) a sklon pole
  (63° proti 66°) nezměněné. Tabulka přes všechny stavy senzoru: [doc/imu-and-frames.md](doc/imu-and-frames.md),
  „Po NOVÉ kalibraci". Úkoly, které na to čekaly, přeměřeny (registr, 18. 9.).
- [doc/hardware.md](doc/hardware.md) — senzory a připojení (per-zařízení, orientační).
  ⚠️ **Výpadky D435 za provozu jsou cizí, Intelem NEVYŘEŠENÝ problém** (rešerše 11. 9. 2026) —
  naše léčba (detekce + zbourání pipeline + reconnect) je to, k čemu ve vláknech všichni dojdou.
  Propustnost (13–18 % USB3) ani VN100/GPS na témž hubu to **nejsou** — spočítáno. Padly přitom
  dva omyly: „T265 odebrán ve 2.50+" (je až ve **2.54.1**, 2.50 je poslední *validovaná*) a
  „z RSUSB nemůžeme kvůli T265" (**není UVC zařízení**, jde přes `src/tm2` nad libusb v obou
  backendech).
  ❌ **Hypotéza „`CLEAR_HALT` 1 → ~72 kvůli T265" je 14. 9. 2026 VYVRÁCENÁ na zařízení**, a s ní
  padl i plán sahat kvůli ní na backend nebo verzi SDK: bez T265 je `CLEAR_HALT` **stejný**
  (7,39 → 7,10–7,85 za minutu) a **zamrzání streamu D435 taky** (3,4 → 3,6–4,2 za hodinu).
  ⚠️ **`CLEAR_HALT` navíc není podpis poruchy, ale šum** — teče 3–15 za minutu v *každé* minutě,
  kdežto porucha přijde jednou za ~17 minut; jako měřidlo je mrtvý. *(Poučení: klidovou hodnotu
  měř dřív, než podle čísla začneš rozhodovat.)*
  ✅ **Podezřelým je teď fyzická větev `2-1.3`** — **12 tvrdých záseků z 12** na tom portu, ať na
  něm visí kterákoli kamera (kamery se 13. 9. schválně prohodily). Další krok je kabel/port, ne
  software. ⚠️ Runtime přitom odpojenou T265 **hledá dál ~1×/s** (7 829 chybových řádků za
  168 min, přes sdílený zámek RealSense) — neškodí, ale zahlcuje journal.
  Podrobnosti a tabulky: [hardware.md](doc/hardware.md), [decisions.md](doc/decisions.md).
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
  ✅ **Od 12. 9. 2026 s ní robot jezdí** (provozní profil `backproject=npu`, jízdy 12./14./16. 9.); ⚠️ dopad na řízení proti histogramu **změřený není**.
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
  `Model96.2.onnx` dá přesně jeho 96,66 % a soubor má na bajt tutéž velikost). ⚠️ **S Model96.2 robot
  nikdy nejel** — provozní profil má Model61.1; měření 96.2 jsou ze stojícího robota. ⚠️ Proč je
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
  ✅ **Od 14. 9. 2026 i vrstva „Zóny"** — místa mise (jinak cíl navigace) jako kružnice o **dojezdovém
  poloměru**. Půdorys stránky náhledu je kreslil od 12. 9., ale v Avalonii vidět nebyly, protože ta
  logika seděla uvnitř `WebStatus`; teď je v `ARBot.Common/Rendering/GoalZones.cs` a oba pohledy ji
  berou odtud (opsat ji podruhé by znamenalo opsat i pravidla „kreslí se PŘICHYCENÉ místo" a „zdroje
  se nemíchají", která si na sebe už došlápla). ⚠️ **Poloměr se přepočítává na Web Mercator**
  (`1/cos(lat)`, +55 %), jinak by kružnice byla o třetinu menší než zóna. ⚠️ **Ke kružnici patří
  značka ve středu**: kružnice je v metrech, takže při běžném zoomu (1,5 m/px) má poloměr 3 m
  **3 pixely** a ztratí se — první pokus vypadal, jako by se zóny nekreslily vůbec. Ověřeno
  v běžící aplikaci v simulaci.
- [doc/occupancy-and-local-planning.md](doc/occupancy-and-local-planning.md) — kartézský occupancy grid
  (fúze sjízdnosti z hloubky + z RGB, log-odds, kruhový buffer) a lokální plánování cesty nad ním
  (odstupy od překážek, rychlostní obálka, A\* → `RegulatorWayPoint[]`) + `LocalNavigator` jako vyšší
  řídicí smyčka. Hotové a napojené (`ARBot.Common/Occupancy`); **robot s tou vrstvou venku jel** (7., 12., 14. 9. 2026) a co se přitom ukázalo, jsou samostatná témata v registru (`lp-*`).
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
  ⚠️ **Od 12. 9. 2026 se doplňuje KLÍN mezi zornými poli barvy** (`wedgefill=`, výchozí 6°):
  barva D435 má při 640×480 jen **55°** (katalogových 69 platí pro 16:9) a kamery jsou pootočené
  o ±29,3°, takže přímo před robotem zbývá **mezera 3,7°** (0,19 m ve 3 m), kde hloubka vidí, ale
  barva ne — buňka tam zůstává `Unknown`, protože `Free` žádá **oba** kanály. Doplňuje se
  **interpolací z buněk příčně vlevo a vpravo**, ne konstantou „sjízdné": ta by do mapy zapsala
  cestu i tam, kde je tráva. Čtyři pojistky (geometrie se nedoplňuje nikdy, měření se nepřepisuje,
  nutná podpora z obou stran, doplněním nevznikne překážka) hlídají testy.
  ⚠️ **Kolik to stojí, se mezi záznamy liší ČTYŘNÁSOBNĚ a první měření bylo vedle** (změřeno
  `ARBot.Analyze wedge`): nad `20260907-170728.rec` byl klín příčinou 20,0 % zastavení paprsku,
  ale nad `20260912-125851.rec` **72,6 %** — a tam je `freeAhead` p50 jen **1,52 m** proti 4,58 m
  ze samotné geometrie, takže **robot je pod 0,6 m/s ve 40,5 % vzorků proti 3,3 % bez semantiky**.
  Léčba z toho vrátí asi **pětinu** (40,5 → 33,9 %, `freeAhead` p50 1,52 → 2,22 m).
  ⚠️ **Průměr `VBrake` je na tuhle otázku ŠPATNÁ veličina** — většinu času je na stropu, takže
  rozdíl se v něm rozpustí (vycházelo „+0,8 %"); ptát se musí „jak často robot leze", a měřit
  na **víc záznamech**.
  ⚠️ **Léčba se přitom pětkrát vypnula vlastní opatrností a pokaždé to našlo až měření**: doplňovat
  jen buňky bez vzorku (mají slabý, ne žádný), žádat rozhodnutého souseda (je jen v 17,3 %),
  poloviční důvěra (sousedé jsou sami těsně pod prahem), práh 0,5 m (16–20 % zastavení je blíž)
  a čistě úhlový klín (ve 0,35 m je užší než buňka). Každou opravu hlídá test.
  ✅ **Od 14. 9. 2026 je cíl A\* ZÓNA, ne bod** (`carrotradius=`, výchozí 0 = průjezdní mrkev je
  bod). Cílem byla jediná buňka, takže mrkev v trávě nebo těsně u překážky byla nedosažitelná
  **jako celek**: plán skončil na nejbližší bezpečné buňce, stav `GoalBlocked` a robot tam
  **zastavil a čekal**, ačkoli jiná část cílové zóny dosažitelná byla. Naměřeno nad
  `20260914-170945.rec`: `GoalBlocked` **24 %** a `GoalUnsafe` **19 %** plánů, mrkev nedosažitelná
  v **52 %** plánů (p90 rozdílu **2,52 m**) — a kde rozdíl vyskočil, robot ujel **0,1–0,6 m za 10 s**
  místo 8–9 m; k prvnímu bodu trasy tak jel 44 m **9,5 minuty**. A\* vrací **první vytaženou** buňku
  zóny, tedy tu **nejlevnější na dojetí** (podle svého kritéria — času), ne geometricky nejbližší:
  ta může ležet **za** překážkou, kvůli které je střed nedosažitelný. ⚠️ **Heuristika se proto musí
  měřit k OKRAJI zóny** (`max(0, d − R)`) — jinak je `h > 0` i na cílových buňkách, pořadí vytahování
  přestane odpovídat ceně a vrátí se dražší bod, což vypadá jako *tiše horší dráha*, ne jako chyba.
  **Poloměr je vlastnost CÍLE, ne plánovače**: průjezdní mrkev je bod, ale **při dojezdu** se použije
  **dojezdový poloměr** zmenšený o rezervu `ArrivalZoneMarginM` (0,5 m) a o odstup mrkve od cíle —
  jinak by robot zastavil uvnitř zóny mrkve, ale **vně** zóny dojezdu a `Arrived` by nenastalo nikdy
  (táž past jako u nepřichycených bodů Tracku). ⚠️ Neprůjezdná **celá** zóna zůstává `GoalBlocked`,
  aby mrkev ve zdi nevypadala jako dojezd. ⚠️ **Není to lék na špatnou mapu** — `Blocked` buněk bylo
  v témže záznamu p50 27,7 % (max 51,0 %) při rozbitém kurzu, takže část „nedosažitelnosti" může být
  chyba gridu, kterou zóna zakryje. ⚠️ **Na HW neběželo** (12 testů).
  ⚠️ **Nic z toho nejelo na HW** a **kolik z chování v terénu dělá vyhlazování a kolik rozmazání
  gridu chybou kurzu, změřené není** — takže **nejdřív kurz** (viz `imu-and-frames.md`),
  pak přeměřit.
- [doc/path-following.md](doc/path-following.md) — regulátory pohybu (`IRegulator`: `PointRegulator` /
  `PathResult`, `IPathPlanner`, `IMotionProfile`): sledování dráhy z waypointů — plán = geometrie rohů +
  brzdná obálka, exekuce = feedforward + lookahead; analýza odchylky vs. vzdálenost cílového bodu.
  ✅ **Od 13. 9. 2026 má smyčka DRŽENÉ ZASTAVENÍ** (`StopHold`, `controlLoop.StopRequest("důvod")`):
  druhý, nezávislý vstup „smím jet" vedle regulátoru „kam jet". Držitelů může být víc a robot stojí,
  dokud drží kdokoli — tím zmizí přetahování o `Regulator = null`, které dnes nese obojí najednou
  a vyhrává ten, kdo psal poslední. Brzdí **rampou** (ne tvrdou nulou), nouzové zastavení zůstává
  vedle; do záznamu jde `DriveCommandMsg.Held` (**verze 3**), důvody do `Trace`. ⚠️ **`IsStopped` je
  měřené stání a neznámý stav motorů je `false`** — volající si musí nést vlastní timeout, jinak by
  bez připojených motorů čekal navždy. Fáze 1–3 hotové (mechanismus, odzbrojení detektoru záseku
  v `GlobalNavigator`, řádek „zastaveno: …" na stránce náhledu, **supervizor zotavení kamer**;
  15 testů). ✅ **Zotavení kamer ověřeno na robotu 13. 9. 2026 i na SKUTEČNÉ poruše**: zamrzla barva →
  15× `failed to set power state` → supervizor vzal hold a vyměnil RealSense kontext → **obě D435
  zpátky, od zamrznutí do obnovy 29 s** (dřív táž porucha znamenala mrtvou kameru 343 s a 22 min,
  než přišel restart služby). ⚠️ **Jedna epizoda a robot u toho STÁL** — koordinace s bržděním
  za jízdy je zatím jen z testů.
  ⚠️ **Past, kterou našlo až zařízení:** bourat pipeline z cizího vlákna **zatuhne**
  (`pipeline.Stop()` proti běžícímu `TryWaitForFrames`), `StopHold` pak zůstal držený a robot
  stál do restartu služby — proto je `IRecoverableCamera` **žádost a potvrzení**. ⚠️ **Bez odzbrojení detektoru by plánované stání
  po 10 s vypadalo jako zásek** a robot by začal zavírat hranu kvůli tomu, že čekal na opravu
  kamery. Plán a rozhodnutí: [doc/plan-drive-hold.md](doc/plan-drive-hold.md).
- [doc/osm-nav.md](doc/osm-nav.md) — OSM navigace (`Maps/OsmNav`): globální navigace nad OpenStreetMap
  (edge-based graf, goal-rooted pole cost-to-goal / LPA\*, dopravní profily, runtime značky) + lokální
  predikce trajektorie a detekce kolizí (`Colider`). Mapa kódu + odkaz na návrhové PDF.
  ⚠️ **Mapa uložená z JOSM nese i objekty, které v ní autor SMAZAL** (`action='delete'`, JOSM je
  zahodí až po uploadu nebo *Purge*) — v soutěžní `OSM/Robotour2026-ver1.osm` je to 57 z 95 cest
  s `highway`. Do 18. 9. 2026 je `OsmXmlReader` bral jako živou síť; od té doby je přeskakuje
  (test `Read_SkipsJosmDeletedObjects`). Našlo se večer před Robotourem při přepnutí profilu na
  soutěžní mapu; ⚠️ **na zařízení neběželo**, nasazení profilu potřebuje i novou binárku.
- [doc/global-navigation-runtime.md](doc/global-navigation-runtime.md) — **napojení OsmNav na runtime**
  (`GlobalNavigator`): LLA cíl → trasa po síti → „mrkev" pro `LocalNavigator`, metadata o postupu úseků,
  detekce záseku/bloudění/přehrazené cesty a uzavírání hran. **Fáze 0–4 hotové** (jízda k cíli po síti,
  trasa v mapě, detektory + uzavírání hran); zbývá recovery manévr, průřez koridorem a ověření na HW.
- [doc/map-correlation-localization.md](doc/map-correlation-localization.md) — **korelace occupancy gridu
  s mapou** (`MapCorrelator`): shoda semantického kanálu `LRoad` s OSM sítí (`RoadScene.IsRoad`) dá odhad
  chyby polohy a kurzu; 3-DOF `(dx, dy, φ)` s anizotropní kovariancí, do fúze jako dvě skalární osová
  měření. Léčba na „špatná lokalizace ⇒ špatná mrkev". **Fáze 1–3 hotové** (jádro, měření ve fúzi,
  zpráva + telemetrie, napojení na runtime), jádro má testy. **Ve výchozím stavu se ale vůbec
  nepočítá** (`mapcorr=false`, od 20. 8. 2026) — nic neřídí a stálo by celé jádro (1,31 s na cyklus, při odstupu 3 s ~40 %); zapnout
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
  Další otevřené vady: `TightAxisAngle` vychýlená ~6,3°.
  ⚠️ ~~**korekce kurzu je ve fúzi bezmocná** (IMU kompas ji přehlasuje ~200:1)~~ — **to už
  NEPLATÍ** (15. 9. 2026). Bylo to naměřené 22. 8. 2026, tedy **před** `imuheadingstd=5`
  a `imuheadinghz=1` z 12. 9.; poměr informace se tím překlopil z **kompas 220 : 1 nad koridorem**
  na **koridor ~150–1000 : 1 nad kompasem**, tedy o pět řádů. Koridor je jediná reference kurzu
  **bez magnetického biasu**, ale zamyká kurz na **azimut OSM hrany** — chyba mapy jde 1:1 do kurzu
  robotu. *(Je to výpočet z dokumentovaných σ a kadencí, ne měření; reprodukuje ale obě dřívější
  publikovaná čísla.)*
  **Hranová lokalizace (`corridor=`) je k 23. 8. 2026 funkční, ale pořád vypnutá:** 178 měření
  za 40 s, chyba polohy 0,027 m, kurzu 0,18°. Zapnout ji naostro gatují tři podmínky výše.
  ✅ **Od 16. 9. 2026 vybírá hranu sítě PŘIŘAZENÍ přes χ²** (`assoc=`, výchozí true; `EdgeAssociator`),
  ne prostá nejbližší hrana — nad `20260916-164926.rec` se totiž **polovina cyklů (1 114 z 2 256)
  párovala na PŘÍČNOU ulici**: při chybě pózy 3–4 m vyhraje u křižovatky jiná cesta a šířková brána
  ji nechytí, protože v té mapě nemá žádná cesta tag `width`. Skóre je **Mahalanobisova vzdálenost**
  (příčná odchylka + azimut, každá dělená svou σ), ne lineární kombinace — váhy tím zmizí a práh má
  známé rozdělení (5,99 / 9,21). ⚠️ **Obě σ musí mít PODLAHU** (`assocfloorlat=3` m,
  `assocfloorhdg=10°`): filtr hlásí σ kurzu 1,10°, ale skutečná chyba je 15–20°, takže bez podlahy
  vyjde χ² kurzu **p50 220 i na správné hraně** a zamítlo by se všechno — počtvrté táž past jako
  `YprU`, `gpsposstd` a `Reject`. Po opravě magnetometru **snížit na ~3°**. K tomu **tvrdé veto na
  azimut** (`assocveto=45°`, kolmá ulice není „trochu mimo") a **odstup od druhého kandidáta**
  (`assocmargin=4`) — při nejednoznačnosti se **neposílá nic** (`AmbiguousEdge`), protože vybrat tu
  o chlup lepší by znamenalo hádat. ⚠️ **Dvě pasti, které stály čas:** obousměrná cesta jsou dvě
  hrany se shodnou geometrií, a hlavně **kolineární sousední segment téže OSM cesty dá přesně tutéž
  osu** (`Relate` počítá z přímky, ne z úsečky), takže bez slučování na **hypotézy** by test
  nejednoznačnosti zamítl **každou rovnou cestu**. `RoadCorridorMsg` je **verze 6** (skóre vítěze
  i druhého — prahy jdou proladit jen offline). ✅ **Opravena přitom živá vada v `Send()`:** rozdíl
  směrů dvou **přímek** se neskládal na ±90° a smysl se nerozhodoval podle kurzu, takže u cesty
  kolmé na kurz šel do fúze **kurz otočený** — týkalo by se to **40 ze 424 přijatých cyklů (9,4 %)**
  a `GateMode.Soft` takové měření nezahodí, jen odtlumí. ⚠️ **Cena:** koridor tím už nikdy neřekne
  „jsi otočený o 180°"; na převrácení musí hlídat **kurz z GPS**. Ověřeno testy (1 584 / 140 / 115)
  a **během v simulaci** (845/855 cyklů `Ok`, žádný `AmbiguousEdge`); ⚠️ **na zařízení neběželo**
  a nad tím záznamem to **přeměřené není**.
  ✅ **Od 15. 9. 2026 jde odtlumit jako GPS a kompas** (`corridorstd=` [m], `corridorheadingstd=`
  [°], `corridorhz=` [Hz]; sigmy **kvadraticky**, škrtí se **jen posílání**, ne výpočet — zpráva
  chodí dál, aby šly prahy proladit offline). **Výchozí 0 = dnešní chování schválně:** u
  `imuheadingstd` je default 5°, protože ten bias byl změřený, kdežto **dekorelační čas koridoru
  změřený není**.
  ✅ **A odemkl se rozjezd odhadu šířky.** Šířková brána se ptala na **mapovou** šířku dřív, než se
  filtr měl z čeho naučit — na cestě širší než `roadwidth ± 1,5 m` se první měření nepřijalo
  **nikdy** a hrana zůstala **němá navždy**. `OSM/Hviezdoslavova.osm` přitom nemá **ani jeden** tag
  `width`, takže celá síť má 3 m: na vozovce by koridor nezměřil nic a v reportu by to vypadalo
  jako porucha detektoru. Léčba: `RoadWidthEstimator` (okno + **medián** + verdikt kvality z **MAD**)
  běží **bez brány** a sám řekne „nevím"; brána i posílání platí **až od kvality**
  (`CorridorFixReason.WidthNotTrusted`). ⚠️ Padlo tím i zdůvodnění u `WidthUpdateMaxDisagreementM`:
  **šířka na póze nezávisí vůbec** (`Width = cL − cR` v rámci robotu), takže kvalitu měř **shodou
  měření mezi sebou**, ne shodou s mapou — a podmiňovat učení pózou by vyrobilo týž zámek.
  `RoadWidthFilter` zůstává, dokud se nová cesta neprověří na datech. ⚠️ **Na HW neběželo nic.**
  ✅ **A od 15. 9. 2026 jde naučená šířka DÁL DO MAPY** (`roadwidthmap=`, výchozí **false**): dostane
  ji **korelace** (`RoadScene`) i **kreslení** (`MapMsg` → World pohled, webový půdorys). Je to
  **neměnný překryv `nodeId → šířka`** předaný konzumentům — **graf sítě se nemění vůbec**, protože
  ten drží i navigace a jeho výměna za běhu by byla záměna identity toho, podle čeho robot jede.
  Přestavuje `RoadWidthMapUpdater` (tik = `RoadCorridorMsg`, data = přímo estimátor) za prahem
  0,25 m **a** odstupem 10 s; scéna korelátoru se **atomicky zamění**. ⚠️ **Scénu virtuální kamery
  to nedostane** — jinak by simulace renderovala podle odhadu a koridor by měřil **sám sebe** (táž
  past jako `camerapose=fusion`); hlídá to test. ⚠️ **Šířku nese uzel, ne cesta**, takže chodník
  u vozovky podědí její šířku — známá mez, ne vada.
  ⚠️ **Šířka uzlu je maximum jen přes cesty, které MAJÍ ODHAD** — a to je oprava vady nalezené týž
  den na dvoumapovém rigu (vizuální mapa 2 m proti jízdní 3 m), kdy se navenek **neaktualizovalo
  nic**: původně přispívala i cesta **bez** odhadu svou **mapovou** hodnotou, takže naučené
  **zúžení** se na každém sdíleném uzlu přehlasilo — a protože půlšířky segmentu se berou z jeho
  dvou **koncových uzlů**, zůstala celá naučená cesta široká všude, kde se dotýká jiné cesty, tedy
  prakticky na celé síti. Mapová šířka nezměřené cesty je **default, ne důkaz**. ⚠️ **Perzistence vědomě není:** kdyby naučená
  šířka přežila restart, přežil by i špatný odhad a už by ho nikdo nepřepsal. Plán:
  [doc/plan-naucena-sirka-do-mapy.md](doc/plan-naucena-sirka-do-mapy.md). ⚠️ **Na HW neběželo**,
  ověřeno simulací; prahy jsou odhad a jdou doladit offline.
  ~~**Provozní profil `pi-provoz.cfg` je od 15. 9. 2026 v MĚŘICÍM režimu** (`corridorsend=false`)~~
  — ⚠️ **to nikdy neplatilo** (řádek v profilu chyběl, default je `true`; nález 17. 9.) a
  **od 17. 9. 2026 je `corridorsend=true` v profilu zapsané vědomě** (autor, commit `a44b4f4`,
  po kalibraci kompasu 12. 9. a přiřazení hrany 16. 9.). ✅ **První jízdy s korekcemi naostro
  18. 9. 2026** (`records/test/20260918-154028.rec`, `-155329.rec`): za jízdy dá koridor měření
  v **~52 % cyklů** (rezidua 7 cm, ~43 inlierů na stranu, dosah ~7 m), zbytek `NotParallel` 15–17 %
  (proložení na jinou hranu, brána zahazuje správně), `TooFewInliers` 14–18 %, `OneSideOnly`
  9–11 %; **ve stání koridor nevzniká** (0,6 %), takže celkové procento je číslo o stání, ne o
  koridoru. **Fúze se podle něj řídí**: `odhad − IMU yaw` z −0,01 ± 0,06° na **+0,39 ± 1,88°** a
  odhad je ke GPS kurzu blíž než kompas (první změření korekce kurzu z koridoru na zařízení).
  ⚠️ **Přesnost pózy tím ověřená není** — s korekcemi naostro je příčný nesouhlas (p50 0,06 m)
  self-konzistence; potřeba A/B s `corridorsend=false` po téže trati nebo pravda. **Každá D435
  vidí jen SVOU hranu** (levá levou, pravá pravou; nový blok v `corridorfit`), takže výpadek jedné
  kamery vypne koridor celý. Estimátor proložení: na reálných datech jsou **všechny varianty
  (LS/L1/Huber/Tukey) nerozlišitelné**. Detail: [map-correlation-localization.md](doc/map-correlation-localization.md),
  sekce „První jízda s korekcemi z koridoru NAOSTRO". Původní důvod pro měřicí režim byl
  spočítaný — s `gpsposstd=30` by příčná autorita koridoru byla řádu **10⁵–10⁶ : 1**.
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
  **venku projetá 7. a 12. 9. 2026** (`20260907-170728.rec`). Zapíná se **selektorem `mission=none|freerun|robotour`** — mise se vylučují,
  takže se nevybírají booleovskými přepínači. Rozbor záznamu: `ARBot.Analyze freerun`.
- [doc/track-mission.md](doc/track-mission.md) — **mise Track** (`TrackMission`): objezd míst ze
  souboru `*.track` (`mission=track track=<cesta>`). Řádek = `sirka,delka` ve **stupních** (soubor
  je okraj systému, dál se nese radián), poslední řádek `repeat` = jezdit dokola. Ke každému místu
  najde **nejbližší bod na síti cest** a jede na něj — a to není kosmetika, ale oprava vady:
  `Navigator` měří dojezd proti **surovému** cíli, takže při větším odsazení by `Arrived` nenastalo
  **nikdy** a mise by uvízla (past dohledaná u Robotouru). **Mezi body nezastavuje**, jen přepíná
  cíl. Bod dál od sítě než `trackoffroad=` (50 m) misi **přeruší** — tiché přeskočení by znamenalo,
  že robot objel jinou trasu, než člověk zadal. **Nesrozumitelný řádek je chyba, ne přeskočení**
  (mise se nezaloží). **Od 13. 9. 2026 se všechna místa přichycují PŘEDEM, při odjezdu** — dřív až
  když na bod přišla řada, takže robot se seznamem, jehož páté místo leží mimo síť, objel čtyři
  a teprve pak misi přerušil daleko od člověka. Přichycení je čistá geometrie (na póze nezávisí),
  kdežto dosažitelnost se dál zkouší až u konkrétního bodu. ⚠️ **Nejde to udělat už při volbě
  mise:** `IRouteProbe.Probe` počítá i dosažitelnost, takže bez pózy vrací nuly a kontrola tiše
  projde („nejvetsi odstup 0,0 m" i pro bod 370 m od cesty) — chyceno při ověřování v simulaci.
  Volba mise robota **nerozjede**: automat čeká na stisk a uvolnění nouzového
  zastavení. ✅ **Projeto v simulaci** (36 testů; objela tři místa a po `repeat` začala druhé kolo), ⚠️ **na zařízení odjela 12. a 14. 9. 2026, ale celý seznam neobjela** (k prvnímu bodu 44 m za 9,5 min, `NoRoute`) — stav vede registr (`mise-track`).
  **Seznamy `*.track` leží od 12. 9. 2026 u map v `OSM/`, ne v `config/`** — seznam patří
  **ke konkrétní mapě** (jeho body musí ležet na její síti), kdežto v `config/` vypadal jako
  nastavení běhu, které jde libovolně kombinovat s jakoukoli mapou. ⚠️ **A právě tak se to jednou stalo:**
  soubor pojmenovaný po jedné mapě nesl body z druhé a mise se přerušila hláškou
  „*lezi 272 m od site cest*", což se čte jako porucha navigace, ne jako záměna souboru.
  Funkční ukázka je `OSM/Hviezdoslavova.track` + `OSM/Hviezdoslavova.osm` (body **3,7–7,0 m**
  od sítě, ověřeno jízdou v simulaci 12. 9. 2026); tytéž body jsou od `OSM/HajeRovne.osm`
  367–389 m, tedy nad limitem `trackoffroad=50`.
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
  ⚠️ **Na soutěži 19. 9. 2026 skener NEDOSTAL JEDINÝ SNÍMEK** (`20260919-092933.rec`: 239 s
  v `Servicing`, kód v obraze v 725 snímcích, `QrCodeMsg` žádná): skutečná D435 se jmenuje
  **`Right 740112071021`**, ne `Right`, a skener porovnával celé jméno — virtuální kamera vrací holý
  název, takže simulace i všech 19 testů procházely. Od té doby se jméno bere jako **první slovo**
  (`QrScanner.CameraMatches`) a **stránka náhledu kreslí tu kameru, ze které se čte QR** (do té doby
  první ve slovníku = levou, takže obsluha ukazovala kód špatné kameře). Rozbor `ARBot.Analyze mission`;
  nouzové obejití bez binárky `qrcamera=` (prázdné = všechny). ⚠️ **Na zařízení neběželo**
  (`mise-qr-jmeno-kamery`). Poučení: jméno senzoru, se kterým se porovnává, musí být v testu
  **to z driveru**, ne to ze simulace.
  ⚠️ **Týž den další dva nálezy z depa** (`ARBot.Analyze mission`, bloky 1b/1c): (a) mise se **158 s
  nezarmovala** — kritérium je HDOP ≤ 2,0 + ≥ 6 družic nepřerušeně 5 s, a mezi budovami byl HDOP
  2,0–2,95 (vyhovovalo 4,7 % fixů); „sigma 60–70 m" na stránce je `gpsposstd × HDOP`, **ne** kritérium.
  Práh je teď **`depothdop=`** (default 2,0, `pi-provoz.cfg` má 3,0). (b) kód se **četl a mise ho
  zamítala** „nevede trasa (je mimo mapu?)", ačkoli cíl je uzel mapy: robot stál na **náměstí**
  (`highway=pedestrian` + `area=yes`, way 956523901), které je se sítí spojené **jen schody** —
  profil Robot je nepouští, síť má **2 komponenty**. Stránka důvod zamítnutí **neukazovala** (teď
  řádky „QR kódy" a „kód ZAMÍTNUT"). **Ostrov je skutečný** (robot tam nevyjede, GPS ho tam jen
  posadila), takže se **neopravuje mapa, ale načtení**: `mapprune=` (výchozí true, `NetworkIslands`)
  zahodí všechny komponenty kromě té s **největší délkou cest** — póza se pak přichytí na chodník
  2,4 m vedle a cíl je dosažitelný. Rozbor `ARBot.Analyze route` (`--noprune` = síť jako v souboru).
  Obojí ⚠️ **na zařízení neběželo** (`mise-robotour-depothdop`, `mise-robotour-mapa-ostrov`).
  ✅ **Změna pravidel Robotour 2026 (19. 9. 2026): po vykládce smí následovat DALŠÍ nakládka**
  místo jízdy do depa, a tak dokola. V automatu: servisní okno u vykládky má **zapnutý skener**,
  přečtený kód je místo další nakládky (`nextPickupChosen`), **uvolnění stopu bez kódu = do depa**;
  rozlišuje se `CodeExpected` (kód se přijímá všude) a `CodeRequired` (bez něj se neodjede — depo,
  nakládka). Stránka i UI u vykládky hlásí „vyloženo: QR kód DALŠÍ nakládky, nebo uvolnění stopu
  bez kódu = jízda do depa" (`MissionWait.QrCodeOrRelease`). `MissionMsg` **verze 7** (`Deliveries`,
  `NextPickupChosen`). ⚠️ **Na zařízení neběželo** (`mise-robotour-dalsi-nakladka`; testy 1 601 / 140).
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
