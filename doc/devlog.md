# DevLog — deníček vývoje

Chronologický **záznam postupu vývoje den po dni** — stručné shrnutí, *co* se ten den dělalo,
*proč* a *v jakém stavu* to skončilo. Slouží jako souvislý příběh projektu napříč sezeními
i lidmi (viz [CLAUDE.md](../CLAUDE.md), pravidlo „vše v repozitáři").

**Vztah k [decisions.md](decisions.md):** deník rozhodnutí drží *proč* u netriviálních
rozhodnutí (trvalé odůvodnění). DevLog drží *časovou osu* — co se který den udělalo, na čem se
pokračuje, co je otevřené. **Neduplikuj** — když den přinesl zásadní rozhodnutí, shrň ho jednou
větou a **odkaž** do `decisions.md`; detaily domény odkaž do příslušného `doc/*.md`.

## Jak psát záznamy (pravidla)

- **Jeden nadpis `## RRRR-MM-DD` na den práce.** Absolutní datum (ne „včera"). **Nejnovější nahoru**
  (nad tuto sekci s pravidly to nepatří — přidávej pod čáru níže, na začátek seznamu dnů).
- **Zapisuj na konci pracovního sezení** (nebo když dokončíš smysluplný kus). Když už dnešní
  datum má nadpis, **připiš k němu** další odrážku, nezakládej druhý.
- **Formát dne:** krátký souhrn v odrážkách. Doporučené položky (vynech, co nedává smysl):
  - **Hotovo:** co se dokončilo a ověřilo (build/testy/na zařízení).
  - **Rozpracováno / další krok:** kde se pokračuje příště.
  - **Rozhodnutí:** jedna věta + odkaz do [decisions.md](decisions.md), pokud padlo zásadní rozhodnutí.
  - **Odkazy:** dotčené soubory, `doc/*.md`, commit (`git` hash), issue.
- **Stav ověření uváděj pravdivě** — co je odsimulované vs. co je nutné ověřit na HW (viz CLAUDE.md).
- **Obrázky: nový záznam = nový soubor.** Obrázek v `doc/media/`, na který se odkazuje starší záznam,
  se **nepřepisuje** (pokud o to není výslovně požádáno) — každý den/záznam má vlastní název souboru
  (např. `world-view-road-width.png`, ne znovu `world-view.png`). *Proč:* přepsáním se odkaz pod
  starším datem začne odkazovat na pozdější práci — stalo se, že pod 6. 8. byl `world-view.png`,
  který po úpravě 7. 8. ukazoval šířky cest, jež k 6. 8. neexistovaly. Když jeden obrázek sdílí víc
  záznamů, rozdělit na dva.
- **Čeština**, věcně a stručně (deník, ne esej). Nezapisuj to, co se dá vyčíst z gitu jedním
  příkazem — přidávej *kontext a záměr*, který v diffu není.

> **Pokyn pro Claude / asistenta:** DevLog průběžně **sám udržuj**. Na konci sezení, ve kterém
> vznikla smysluplná změna, přidej (nebo doplň) záznam pro dnešní den dřív, než skončíš. Starší
> sezení mohou být požádána o zpětné doplnění — chybějící dny klidně dorekonstruuj z git historie
> a označ je `(zpětně z gitu)`.

---

## 2026-08-20

- **„Při řídkém důkazu propustí hlídač volnou osu" — dohledáno a rozhodnuto NEOPRAVOVAT zvlášť.**
  Vypadalo to na samostatnou vadu; po změření je to **třetí stopa téhož problému** jako otevřený
  úkol č. 1. Beze změny kódu, jen měření a dokumentace.
  - **Co se změřilo:** profil skóre podél volné osy nad skutečnými snapshoty gridu ze záznamu
    (stejným kódem jako robot, nástroj mimo repozitář). Skóre podél volné osy **není ploché** —
    klesá vždy, ale u malého oblaku výrazně strměji: při posunu 2,5 m spadne na 0,58 (2 214 buněk)
    proti 0,82 (18 465 buněk).
  - **Není to o řídkosti.** Hustota je ve všech případech shodná (~230 buněk/m², ~57 % zaplnění)
    a podíl buněk mimo cestu taky (59–65 %). Rozhoduje **prostorový rozsah**, hlavně napříč:
    2,20 m proti 10,84 m — a 2,20 m je *méně než šířka cesty*. Pojmenování „řídký důkaz" bylo tedy
    od začátku špatné.
  - **Mechanismus:** u velkého oblaku leží většina buněk daleko od okraje cesty. Ty souhlasí
    u každého kandidáta, nic neurčují a jen **ředí procento** — posun s ním hne málo, konkurent
    zůstane u vrcholu, hlídač správně zhasne. Malý oblak žádnou nudnou buňku nemá, takže posun
    procentem hne hodně a hlídač propustí. Skóre se nezhoršilo proto, že by malý oblak věděl víc,
    ale proto, že nemá co ředit.
  - **Proč je to tentýž problém:** skóre je **normalizované**, takže o množství důkazu za sebou
    neví nic — a σ z jeho zakřivení (× konstantní `α`) to nemůže vědět taky. Proto vyjde pro malý
    oblak σ **menší** (0,1412 proti 0,23–0,29 m): větší jistota tam, kde je podkladu nejmíň. Jako
    s anketou — tři dotázaní se stoprocentní shodou vypadají lépe než tři tisíce s 94 %.
  - **Rozhodnutí (autor):** připsat k úkolu č. 1 a neřešit zvlášť. Až se σ naučí počítat, kolik
    informativního důkazu za ní stojí, případ zmizí sám (velká σ → strop σ podélnou osu potlačí →
    hlídač marže není potřeba). Opravovat to teď zvlášť by znamenalo přidat další ruční práh, což
    dokumentace projektu sama zakazuje. Naléhavost nízká: korekce vypnuté, nastane to jednou za pět
    běhů na jediný cyklus, hodnota byla správná — *ale* ve virtuálním HW je hodnota přišpendlená
    cirkularitou renderu, takže to není důkaz správnosti, jen absence protidůkazu.
  - **Vyzkoušeno a nefunguje:** poměrové přeformulování hlídače (marže volné osy proti marži určené
    osy na tomtéž oblaku, aby se ředění vykrátilo) — poměr vyjde 0,27 proti 0,10–0,13, takže rozumný
    práh problémový cyklus pustí taky. Zapsáno, ať se to nezkouší znovu.
  - **Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) →
    „Proč malý oblak obelže hlídač" (obrázky obou oblaků, tabulka, mechanismus, nejlevnější pojistka
    kdyby byla potřeba dřív). Bez commitu.

- **Latence korekce proti oknu historie EKF** (podnět autora: „v .rec jsem viděl, že korelace trvá
  cca 800 ms, vzhledem k 1s oknu u EKF je to dost na hraně"). Měl pravdu a to číslo situaci ještě
  podceňovalo. Beze změny kódu, jen měření a dokumentace.
  - **Naměřeno** z indexu záznamu (`ArrivalTicks − CaptureTicks`): celá latence korekce je
    v **Debugu p50 1 427 ms** (max 1 807), tedy **51 z 55 korekcí by EKF zahodil** jako starší než
    okno 1 s. V Release p50 194 ms (max 294), nic nad oknem — rezerva ~3,4×.
  - **⚠️ Zahození je neviditelné:** `Enqueue` starší měření zahodí a jen zaloguje `Debug.WriteLine`,
    což je `[Conditional("DEBUG")]` → v Release neprojde nikam a počítadlo neexistuje. Telemetrie
    přitom dál hlásí `Reason = Ok`, takže by to vypadalo, že funkce jede. Před měřením na OrangePI
    je potřeba to zviditelnit, jinak měření nic nerozliší.
  - **Proč je Debug/Release rozdíl tak velký:** izolovaně (tentýž snapshot, shodné skóre) trvá
    `CorrelationScorer.Scan` 523–583 ms v Debugu proti 118–131 ms v Release = **4,3×**. Je to
    vlastnost tvaru práce: horká smyčka udělá ~10–12 M iterací a v každé třikrát sáhne do pole přes
    property a zavolá `TryIsRoad`. Release to inlinuje a drží lokály v registrech, Debug má
    z každého přístupu skutečné volání (`DisableOptimizations`).
  - **Důležitější než ten poměr:** latence se zhorší **víc** než výpočet (7,4× proti 5,5×), protože
    v Debugu cyklus (696 ms) přeteče periodu snapshotu (500 ms) → využití 1,39 → fronta se zasytí.
    Ověřeno aritmeticky: 228 + 696 = 924 ms proti naměřeným 1 427, rozdíl ≈ jedna perioda čekání.
    **Návrhový důsledek:** cíl není „cyklus pod 1 s", ale „cyklus pohodlně pod 500 ms" — při
    přiblížení k periodě snapshotu se okno prolomí skokem, ne postupně.
  - **Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) → „Latence korekce
    proti oknu historie EKF". Bez commitu.

- **Zviditelnění zahazovaných měření ve fúzi** (rozhodnutí autora po předchozí odrážce). Sada
  **666 prošlo, 4 skipy, 0 selhalo**, build 0 chyb.
  - **Hotovo:** `AsyncFusionEngine.DroppedTooOld` a `DroppedTooOldBySource()` — počítadlo měření
    zahozených jako starší než okno historie, rozpadlé podle `Source` (aby šlo odlišit podezřelé
    „MapCorr" od běžného opozdělého GPS fixu). `Diagnostics()` to ukázat nemůže: zahozené měření do
    bufferu nikdy nevstoupí. Hláška o zahození jde nově přes **`Trace.WriteLine`**, takže dorazí do
    záznamu i v Release. +5 testů na počítadlo, +1 integrační (zahození → `Info` v proudu).
  - **`ARBotRuntime.Log` NEvznikla** — autor to zamítl správně: nepřidávala by mechanismus, jen
    jméno, a `ARBot.Common` na aplikační vrstvu sahat nesmí, takže právě tam, kde to bylo potřeba,
    by se použít nedala. Platí jedno pravidlo pro obě vrstvy: `Trace.WriteLine`. Zapsáno do
    [record-replay.md](record-replay.md) i s tabulkou „kdy Trace a kdy Debug".
  - **Ověřeno mimochodem:** `TRACE` je definované i v Release (doplňuje ho SDK), přestože
    `ARBot.Common.csproj` ho explicitně uvádí jen v Debug konfiguraci — zkontrolováno na příkazové
    řádce překladače (`/define:TRACE;IsX64;RELEASE;...`).
  - **⚠️ Nedořešeno:** hlášky ze **startu** (načtení mapy, `poseerror=`, vložení počáteční pózy) do
    záznamu pořád nedorazí — most se připojuje až na konci `WireRun`, o ~170 řádků wiringu později.
    Převedl jsem je na `Trace.WriteLine`, ale bez přesunu zapojení mostu (nebo pufrování) jdou jen
    do debug outputu. Popsáno v [record-replay.md](record-replay.md).
  - **Opravena latentní vada testu**, kterou to odhalilo: `AsyncFusionEngineConcurrencyTests`
    tvrdil uvnitř spotřebitele `Assert.That(rs, Is.Not.Null)`, tedy že spotřebitel vždy stihne
    zůstat v okně 1 s, zatímco producent žene 25 s modelového času co nejrychleji. Při zatížení
    stroje producent vyhraje, staré časy se prořežou a `GetStateAt` **správně** vrátí `null` — test
    pak padal podle vytížení CPU (2 ze 3 běhů celé sady; izolovaně vždy prošel). Deklarovaný záměr
    testu je „bez výjimky a bez deadlocku", takže tvrzení bylo mimo jeho vlastní kontrakt. Nově se
    null toleruje a místo toho se hlídá `answered > 0`, aby test nezhloupl na „všechno null, nic se
    neověřilo". Sada 5× po sobě zelená. *Poznámka: na pomalejším stroji (OrangePI, CI) by to padalo
    i beze mne.*


- **Korekce zapnuté, okno EKF na 3 s — a hláška o zahození doplněná** (autor zapnul
  `MapCorrelatorConfig.Enabled = true` a `FusionConfig.HistoryWindow = 3 s`, v logu ale dál viděl
  zahazování `MapCorr`). Sada **667 prošlo, 4 skipy, 0 selhalo**, build 0 chyb.
  - **Z logu šlo hned vyloučit okno jako příčinu:** `MapCorr @ 11:59:21.196 (tBase=11:59:21.628)` je
    proti `tBase` staré jen **432 ms**, ne 3 s. Protože `tBase ≈ nejnovější měření − okno`, znamená
    to, že korekce byla opožděná o ~3,4 s — tedy latence, ne velikost okna. Prodloužení okna proto
    nemohlo pomoct a při větším okně navíc roste přepočítávaný ocas.
  - **Hláška teď nese typ, hodnotu a hlavně O KOLIK bylo pozdě** (žádost autora „bylo by pěkné říct
    i jaké měření bylo zahozeno"):
    `[Fusion] zahozeno merenie starsi nez okno historie: AxisOffsetMeasurement 'MapCorr' @ 11:59:21.196
    z=[12.345] - opozdeno o 3804 ms za nejnovejsim (11:59:25.000), okno je 3000 ms (tBase=11:59:21.900)`.
    Typ je podstatný: z korelace chodí **tři různá** měření (dvě osová + kurz). „Opozdeno o N ms
    proti oknu W ms" je akční číslo — řekne, jestli pomůže větší okno nebo rychlejší výpočet.
  - **Test odhalil skutečnou vadu v mém kódu:** hláška sahala na `nodes[Count-1]`, ale `Initialize*`
    uzly promaže, takže buffer je prázdný i u inicializovaného filtru → index −1. V provozu by to
    shodilo první opožděné měření po inicializaci. Opraveno (`nodes.Count > 0 ? … : tBase`) a
    pokryto testy pro obě větve (prázdný i neprázdný buffer).
  - **Aktualizován test i dokumentace:** `Vychozi_JeVypnuty` → `Vychozi_MaZapnuteKorekce` (ten test
    je tam schválně, aby stav přepínače byl vědomé rozhodnutí; teď nese datum a důvod). Srovnána
    čtyři místa v `CLAUDE.md` a specifikaci, která ještě tvrdila „korekce jsou vypnuté".
  - **Nejakutnější otevřená vada při zapnutých korekcích:** chybí **tvrdý limit korekce za cyklus** —
    `MaxOffsetM` omezuje naměřený posun, ne aplikovaný krok, takže při malé σ proti velkému `P` může
    filtr aplikovat skoro dva metry v jednom updatu. Vyzdviženo ve specifikaci.
  - **Odkazy:** [AsyncFusionEngine.cs](../Src/ARBot.Common/Fusion/AsyncFusionEngine.cs),
    [map-correlation-localization.md](map-correlation-localization.md). Bez commitu.

- **Ovlivňují korekce polohu robota? — změřeno nad `20260820-122026.rec`** (pozorování autora:
  „v Release to nezahazuje, ale nezdá se mi, že by korelace ovlivňovaly pozici robota"). Měl pravdu,
  a příčina je v zadání experimentu, ne v korelátoru. Beze změny kódu, jen měření a dokumentace.
  - **Naměřeno:** korekci poslalo **67 ze 67** cyklů (všechny tři složky), ale stav zareagoval aspoň
    30 % tvrzeného posunu jen **3×**. Součet tvrzených posunů 10,41 m proti 1,83 m skutečných do
    250 ms (17,6 %, a většina z toho je jízda, ne odezva).
  - **První korekce se aplikovala** — stav uskočil v jednom 100 ms tiku přesně o tvrzených 0,800 m
    (zesílení ≈ 1, `P` bylo po startu volné). Od druhé už jemná stopa v 10 Hz nemá v okamžiku
    korekce **žádnou nespojitost**.
  - **Vyvrácena moje vlastní obava:** vyslovil jsem hypotézu, že ta jedna přijatá korekce mohla filtr
    zamknout mimo pravdu. Neplatí — systematický posun stavu proti GPS je **0,141 m** při standardní
    chybě průměru **0,152 m** (šum virtuální GPS σ = 2,12 m, 194 fixů). Tedy v šumu; filtr jede na
    GPS a zůstává na pravdě.
  - **Ten experiment vyjít nemůže, a je to vlastnost zadání.** Chyba pózy byla vnucená z UI virtuální
    kamery, ale je **fiktivní** — virtuální GPS měří simulovaného robota, tedy pravdu, a tu chybu
    popírá. Správně fungující filtr *má* dát přednost GPS. Ta jedna korekce, co prošla, odtlačila
    odhad 0,8 m **od** pravdy a GPS ho vytáhla zpět. Navíc se hlášený posun nemůže vynulovat ani
    principiálně: kamera renderuje z odhadu, takže posunutí odhadu posune i obraz — proto `Dx` stojí
    konstantně na 0,800. Na ověření, že korekce pracují, musí být korelace jediná absolutní
    reference: zhoršit/vypnout GPS (což je i skutečný účel funkce), nebo vnutit tutéž chybu i GPS,
    nebo měřit nad reálným záznamem.
  - **Proč od druhé nic — hypotéza, ne fakt:** gating. σ ≈ 0,10 m, první korekce prošla při velkém
    `P`, ale **tím ho sama stáhla** na ~σ²; pak `S = P + R ≈ 2σ²` a posun 0,28 m dá NIS ≈ 3,5–3,6
    proti prahu 3,84. Strukturální past: **sebejistý korelátor tvrdící velkou chybu si ji sám
    zamkne** — čím větší chybu najde, tím spolehlivěji ji gate zamítne.
  - **⚠️ Potvrdit to nelze** — v záznamu není NIS ani příznak přijetí. `MeasurementDiagMsg` přitom
    nese přesně `Source`, `Z`, `DiagR`, `Nis`, `Accepted`, ale **nikdo ji nepublikuje**; je to mrtvý
    DTO. Zapojit ji je doporučený další krok.
  - **Mimochodem:** `MapMsg` drží souřadnice ve **stupních** (`LatDeg`), zatímco `RoadNetwork` uzly
    v **radiánech** (`LLA.FromDegrees` v `GraphBuilder`) — na tom si při analýze záznamu snadno
    naběhnout (mně se to stalo). `GPSState.Latitude/Longitude` jsou stupně.
  - **Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) → „Co virtuální HW
    o funkčnosti korekcí ukázat nemůže". Bez commitu.

- **Návrhová rozvaha: korelace jako odhad posunu mapa↔GPS** (úvaha autora: „GPS může kecat, mapa může
  být špatně nakreslená do tvaru i polohy — to pozorování kamerou je vlastně to nejpřesnější, co se
  týče pozice robota vůči cestě"). **Zapsáno jako návrh k rozhodnutí, kód nedotčen** — viz
  [decisions.md](decisions.md).
  - **Jádro:** kamera neměří polohu, měří **vztah k cestě**. Podporují to naměřená čísla z 19. 8.:
    příčná chyba nalezena s přesností jednotek **milimetrů**, podélná na přímé cestě **vůbec**.
    Dnešní `(Dx, Dy)` slévá tři věci: polohu robota napříč cestou, tvar cesty v mapě a umístění celé
    mapy vůči GNSS rámci.
  - **Návrh autora:** nový stav filtru — aditivní posun `d` mezi rámcem GPS a rámcem mapy, krmený
    korelací. Obě role, které autor chce, padnou na dvě složky s různou observovatelností: napříč
    pořád, podél jen na struktuře (odbočka, ohyb) — tedy „odbočení zpřesní pozici" je doslova
    observovatelnost podélné složky.
  - **Co jsem k tomu přidal:** atribuce (mapa vs. GPS) není potřeba ani možná — `d` je prostě
    transformace, která srovná GPS s mapou. Aplikovat ho **na mapu, ne na robota**, aby póza
    a ukotvení gridu neskákaly (jinak se vrátí kolo „skok pózy → zahodit grid → divná σ"). A do EKF
    místo vedle něj, protože rozdělení „chyba je v GPS" vs. „v mapě" pak vypadne z kovariancí samo
    místo ručního pravidla. Rozhodující konstanta je **procesní šum na `d`**.
  - **Rozpouští to gating** naměřený téhož dne: filtr zamítá to, co neumí vysvětlit; jak nesouhlas
    dostane stav, přestane být odlehlý a stane se z něj informace.
  - **Námitka autora a její vyřešení:** „když posuneš mapu, musím dostat novou mapovou zprávu".
    Platí na naivní čtení, ale `MapMsg` má **jediného konzumenta** — `WorldViewDocument` (kreslení).
    Řídicí cesta bere in-process `RoadNetwork`. Graf se tedy nepřeposílá nikdy; `d` jsou **dva
    doubly** v `RobotStateMsg` (verze +1) a konzumenti si posun přičtou sami.
  - **Dvě námitky autora, obě věcné, obě zapsané:** (1) *posun ovlivní naplánovanou trasu i lokální
    plán* — míří na slabinu mého triku „aplikovat na mapu": ten zachrání grid, ale **mrkev se posune
    tak jako tak**, a mrkev robota řídí. Přeprodal jsem to. Trasa jako posloupnost hran se ale nemění
    (topologie); mění se „na které hraně jsem", což je právě to, co má korelace spravit. Léčba je
    rychlostní limit na Δ`d` — a to je ten „tvrdý limit korekce za cyklus" z otevřených úkolů, jen
    aplikovaný na posun, kde sedí lépe. (2) *bude se to blbě prezentovat* — UI je řešitelné a zlepší
    to (kreslit **použitou** mapu; zbylá mezera proti podkladu OSM je přímo ten posun, dnes nevidět),
    ale **pojmová cena je trvalá**: dva rámce, a každé číslo musí říct, ve kterém je.
  - **Zaostřené rozhodnutí:** ty námitky jsou cena stavového řešení. Jednodušší varianta „příčný
    offset jen do lokální navigace, EKF obejít" je **neplatí** — proto je doporučená jako **první
    krok** a stavová až po měření nesouhlasu GPS↔mapa na **reálném** záznamu. Zatím nevíme, jestli
    stavové řešení řeší problém, který v praxi máme, nebo problém, který si umíme představit.
  - **Důsledek pro plán:** otevřený úkol č. 1 (σ slepá k množství důkazu) tímhle nabývá na
    důležitosti — rozdělení mezi pózou a `d` řídí poměr rozptylů. A dokud se o návrhu nerozhodne,
    nemá smysl dolaďovat současné chování korekcí.

- **Korelace se ve výchozím stavu vůbec nepočítá** (`mapcorr=false`) **a `Enabled` → `SendCorrections`**
  (podnět autora: „vzhledem k tomu, že se to zatím nepoužívá, mi přijde zbytečné i korelaci počítat —
  lze to nějak jednoduše vypnout?"). Odpověď byla: **nešlo**, a přepínač, který tak zní, to nedělal.
  - **Nález:** `MapCorrelatorConfig.Enabled` se testuje **až za celým výpočtem** — přeskočí jen
    `SendMeasurements`. Sken, rastr, důkazní seznam i kovariance se spočítaly vždycky, takže
    `false` neuspořilo **nic**. Sám jsem na tu záměnu naletěl: psal jsem „korekce jsou vypnuté" ve
    chvíli, kdy korelátor spaloval 126 ms na cyklus (~čtvrt jádra na x64, na ARM víc).
  - **Hotovo:** parametr `mapcorr=true/false` (default **false**) rozhoduje, jestli se ten stupeň
    v `WireRun` vůbec založí — při `false` nevznikne ani vlákno, ani fronta, ani rastr. A přejmenování
    `Enabled` → `SendCorrections`, aby dva různé přepínače měly dvě různá jména; u obou je v kódu
    i v dokumentaci napsané, že „posílat" není totéž jako „počítat".
  - **Ověřeno za běhu:** bez parametru **0** `MapCorrelationMsg` v záznamu (grid se publikuje dál,
    21 zpráv), s `mapcorr=true` jich je 21. Build 0 chyb, sada **667 prošlo, 4 skipy, 0 selhalo**.
  - **Proč default false:** korelátor dnes nic neřídí (korekce jsou neúčinné — viz odrážky výš),
    telemetrii nikdo nečte a návrh je pod revizí, takže jediné, co produkuje, je zátěž. Kdo se
    k tomu vrátí, zapne si to parametrem; stav je navíc z každého záznamu poznat na první pohled
    (přítomnost `MapCorrelationMsg`).
  - **Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) (tabulka „dva
    přepínače"), [CLAUDE.md](../CLAUDE.md). Bez commitu.

- **Rozvaha: korelace přes FFT?** (dotaz autora). Zapsáno do specifikace k otevřenému úkolu č. 1,
  kód nedotčen. Závěr: **jako zrychlení téhož nejspíš prohraje, jako jiný estimátor by vyhrála.**
  - **Proč ne rychlost:** FFT dá korelaci pro *všechny* posuny, ale dnešní sken je postavený na tom,
    že je nechce — hierarchicky (1 325 kandidátů proti 512² = 262 144), řídce (`Stride = 4`, ~5 150
    z 20 600 buněk) a v okně ±50 buněk. Rotace navíc zůstane vnější smyčkou (FFT umí translaci),
    normalizace „buňka mimo rastr se přeskočí včetně jmenovatele" potřebuje **druhou** korelaci
    s maskou, a kvůli cyklické konvoluci je potřeba padding 456² → 512².
  - **Proč možná ano:** dala by celou **plochu skóre** místo tří sond Hessiánu. Přímo je to
    nedostupné (~210 M vyhodnocení, odhadem ~1,9 s na jeden úhel), FFT to zvládne čtyřmi
    transformacemi. Míří to na tři největší otevřené vady naráz: vychýlenou `TightAxisAngle` (změřit
    směr hřebene místo fitu kvadratiky na „tent"), heuristický hlídač nejednoznačnosti (detekovat
    vícemodálnost pořádně) — a hlavně ten **jmenovatel pro každý posun**, který FFT musí spočítat
    kvůli normalizaci, **je efektivní množství důkazu**, tedy přesně ta veličina, ke které je σ dnes
    slepá.
  - **Nepřeprodávat:** FFT dá lepší *měření* plochy, ne lepší *model*. Že skóre není věrohodnost, je
    vada modelu, ne vzorkování.
  - **Cena:** `MathNet.Numerics` je už referencovaná, ale její FFT je managed a na tohle
    pravděpodobně moc pomalá → nativní knihovna, tedy nová externí závislost i pro ARM64.
  - **Kdy:** ne dřív, než padne rozhodnutí o přestavbě. Když vyhraje „příčný offset do lokální
    navigace", hledání se scvrkne skoro na jednorozměrné a FFT je zbytečná.
  - **Fourier-Mellin (návrh autora):** rotaci opravdu **odděluje** a je asi 5× lacinější než FFT na
    každý úhel — námitku „rotace zůstane vnější smyčkou" to boří. Pro tenhle problém tomu ale stojí
    v cestě čtyři věci: (1) **částečné překrytí** — spektra budou dominovaná nosiči (vějíř proti
    hranici rastru), ne strukturou cesty, což je známý režim selhání FMT; (2) **rotace a translace
    jsou tu skutečně provázané** (malá rotace kolem vzdáleného bodu vypadá jako příčný posun) a
    stávající návrh tu vazbu záměrně marginalizuje — nezávislý odhad by ji zahodil; (3) odhad by
    běžel na **jiném kritériu** (magnituda zahazuje fázi; normalizaci ani „přeskoč buňku mimo rastr"
    v |F| vyjádřit nelze); (4) **úhlové rozlišení na hraně** (potřeba 0,5°, FMT typicky 0,5–1°
    a při částečném překrytí horší).
  - **Kde FMT naopak sedí:** jako **hrubý inicializátor pro široký záběr**. Dnešní záchytný rozsah je
    jen ±2,5 m a ±8°, takže po delším výpadku GNSS nebo po přenesení robota hierarchický sken
    principiálně nedosáhne a korelátor mlčí. Jeden výstřel, široký rozsah, nízká přesnost — a sken to
    dojemní. Je to zároveň chybějící kus otevřeného úkolu „eskalace stavu *lokalizace nepodložená
    mapou*", kam jsem to připsal jako kandidáta.
  - **Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) → „Korelace přes
    FFT" (včetně FMT) a otevřený úkol o eskalaci. Bez commitu.

- **Oprava návrhu testovací sestavy: dvě mapy** (úvaha autora: „problém je tedy v systematické chybě,
  kterou zavádí posunutá kamera — správnější by bylo mít dvě mapy, jednu na které naviguje robot
  a druhou posunutou/zdeformovanou, co vidí kamera"). **Návrh, neimplementováno**; zapsáno.
  - **Proč je to lepší:** `poseerror` vnucuje chybu do „kameriny představy o tom, kde je", což
    fyzikálně **neexistuje** — a protože kamera renderuje z odhadu, posunutí odhadu posune i obraz.
    Odtud ten kruh (`Dx` stálo celý běh na 0,800). Posunutá mapa je naproti tomu **reálný jev**
    (mis-georeferencovaná OSM), takže vnucení chyby tam měří skutečnou hypotézu.
  - **Klíčový důsledek:** hlášený posun zůstane konstantní, ale z *poctivého* důvodu — posunutou mapu
    nelze spravit posunutím robota. Stane se z něj **pravda pro `d`** a jde ověřit falsifikovatelná
    předpověď: `d` zkonverguje k vnucenému posunu, póza zůstane na GPS.
  - **Cena:** jeden řádek. Scéna pro kamery vzniká v `ARBotHW.SetVirtualHW` jako
    `new RoadScene(options.Network, options.Origin)`; stačí posunutý počátek
    (`new GeoReference(origin.ToLLA(-dx, -dy))`). Levnější než `VirtualPoseError`, který jsem dnes
    napsal. Rotace taky levná, obecná deformace chce klonovat síť.
  - **Past:** posun držet **pod polovinou šířky cesty** — grid sleduje kameru, mrkev pravou mapu,
    a při velkém posunu se dostanou do konfliktu (mrkev tahá tam, kde grid říká „mimo cestu").
  - **Tři experimenty, tři místa vnucení** (zapsáno jako tabulka): póza kamery → korelátor odchylku
    *najde* (hotovo, mm); **GPS** → korekce *opraví lokalizaci* (chybí); **mapa pro kameru** → `d`
    *identifikuje posunutou mapu* (chybí). `poseerror` tedy nebyl zbytečný, umí jen první řádek.
  - **A k tomu výpočet nemožnosti + `GateMode.Soft`:** při `Reject` se velký posun absorbovat **nedá**
    — pro 0,8 m a σ 0,105 m vychází „neuskočit" jako σ < 0,135 m a „projít gatingem" jako σ > 0,395 m,
    tedy protiřečící si podmínky. Vysvětluje to naměřené „67 poslaných, 3 zareagovaly" lépe než moje
    hypotéza o zamčení `P`: není to nastavení, je to struktura. Kandidát `GateMode.Soft`
    (`R' = R × NIS/prah`) v kódu **už je** a jeho komentář slibuje přesně tu postupnou absorpci;
    korelační měření jsou dnes na `Reject`. Výměna rizik, ne výhra.
  - **Odkazy:** [virtual-hw.md](virtual-hw.md) → „Dvě mapy",
    [map-correlation-localization.md](map-correlation-localization.md) → tabulka tří experimentů
    a výpočet nemožnosti, [decisions.md](decisions.md).

- **Revize návrhu: přímá korekce pózy stačí, stav pro posun se odkládá** (otázka autora: „je potřeba
  vůbec odhadovat pomocí EKF ten posun? nestačilo by se vrátit k přímému zásahu? ano, bude se
  přetahovat GPS s kamerou, vadí to?"). Vadí to méně, než jsem tvrdil, a stavová varianta stojí víc,
  než jsem přiznával — **závěr v `decisions.md` jsem otočil**. Dokumentace, kód nedotčen.
  - **„Přetahování" není kmitání** — jsou to dvě měření téže veličiny a filtr je zváží podle σ. Poměr
    vah z naměřených hodnot `(2,12/0,105)² ≈ 400`, takže korelace přehlasuje GPS ~400:1 a póza sedne
    na mapu. Nic neosciluje.
  - **A to je pro jízdu žádoucí:** mrkev, trasa i cíle misí jsou mapově relativní, takže póza
    v mapovém rámci dává správnou mrkev vůči cestě. K tomu argument, který jsem dřív nedomyslel —
    **absolutní přesnost je stejně omezená chybou mapy**, takže oddělovat rámce se vyplatí jen
    s použitím pro absolutní polohu, které nejde přes mapu. U tohohle robota takové není.
  - **Jediná vážná ztráta:** při 400:1 přestává být GPS nezávislou kontrolou, takže záchyt na souběžné
    cestě dva metry vedle si unese pózu a nikdo to nezastaví. Jde to ale koupit zpět **levněji než
    stavem** — explicitní strop na nesouhlas s GPS, jedna podmínka v `SendMeasurements`.
  - **Tři podmínky, než to pustit naostro:** honestní σ (jinak přehlasuje GPS na základě jistoty,
    kterou si nezasloužila — to je skutečný problém, ne přetahování), rychlostní limit na aplikovanou
    korekci, a ten strop na nesouhlas s GPS. Plus `GateMode.Soft` místo `Reject`: u přímé korekce je
    nesouhlas **přechodný** (póza se posune do mapového rámce a inovace klesne k nule), takže stačí
    projít tím přechodem — stav k tomu potřeba není.
  - **Co jsem přeceňoval:** (1) „paměť přes výpadky dá jen stav" — po dopočtu slabé, po korekci je `P`
    utažené, jeden GPS fix má zesílení ~0,0025 a korekce odtéká na škále **desítek sekund**;
    (2) výhoda „aplikovat posun na mapu" — zachrání grid, ale mrkev se posune tak jako tak;
    (3) prezentovatelnost — u přímého zásahu problém vůbec nevzniká, takže autorova námitka nakonec
    argumentuje **pro** jednodušší variantu.
  - **Co platí dál:** observovatelnost dvou složek (napříč pořád, podél jen na struktuře), že atribuce
    mapa vs. GPS není potřeba ani možná, ověření dvěma mapami, a výpočet nemožnosti u `Reject`.
  - **Kdy by stav byl potřeba:** až bude použití pro absolutní polohu nezávislou na mapě (návrat do
    depa podle GNSS, hlášení polohy mimo mapový rámec, fúze s jiným zdrojem mapy).
  - **Odkazy:** [decisions.md](decisions.md) (přepsaný zápis, revize označená),
    [map-correlation-localization.md](map-correlation-localization.md) (otevřený úkol přepsán na „tři
    podmínky"), [CLAUDE.md](../CLAUDE.md).

- **Chybná kalibrace kamer je bias, který systém integruje** (závěr autora z dnešního zkoumání).
  Zapsáno; kód nedotčen. Je to **nejhorší případ** už vedené aproximace „časová korelace mezi cykly".
  - **Mechanismus:** chyba extrinsiky posune celý bodový oblak, tedy i grid. Korelátor to naměří jako
    chybu pózy — a protože ta chyba **není odeznívající, ale dokonale korelovaná napříč všemi cykly**,
    zatímco filtr měření bere jako nezávislá, efektivní σ klesá jako `σ/√N` a bias **vyhraje vahou
    počtu**. Výsledek není šum kolem pravdy, ale **posunutá póza držená s falešnou jistotou**.
  - **Naměřený poměr, který mění pohled na celou úlohu:** korelátor nachází příčnou chybu na **5 mm**,
    ale yaw kalibrovaný na 1° zavádí při dohledu 3–6 m **5–10 cm**. Tedy **o řádek víc než vlastní šum
    korelace** — kalibrace je pravděpodobně **dominantní chybový člen**, ne korelátor. Ta
    pětimilimetrová přesnost je bezcenná, pokud extrinsika není dobrá na desetiny stupně.
  - **Rozpad podle složky:** yaw 1° → *L·ε*, tedy 5–10 cm; chyba translace → **1:1**; pitch/roll 1° →
    jen ~`h·δ` = 9 mm, protože hloubka se *měří* a rotací skutečných 3D bodů se vodorovná složka
    posune málo (mění to ale klasifikaci, a tedy zdánlivé okraje cesty — nekvantifikováno).
  - **Padá to do téhož koše** jako posunutá mapa a bias GPS: tři přispěvatelé, jeden pozorovatelný jev,
    z jednoho měření neoddělitelní. Potvrzuje to „neatribuovat, ohraničit" — strop na nesouhlas s GPS
    (podmínka 3) chytá i tohle, takže dělá dvojí službu.
  - **Rozlišovací znak, který jde změřit hned:** bias z montáže je vázaný na **tělo**, takže se
    s kurzem **otáčí**; posun mapy je vázaný na **svět**, takže ne. Stačí robota otočit nebo nechat
    projet smyčku a sledovat hlášený nesouhlas ve světových souřadnicích. Nepotřebuje to nic nového.
  - **Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) → „Chybná kalibrace
    kamer" + řádek v tabulce rizik, [traversability-grid.md](traversability-grid.md) (varování u
    robot-centrické transformace — pro detekci překážek stačí hrubá, pro korelaci ne).

## 2026-08-19

- **Korelace occupancy gridu s mapou — zapojení do runtime a telemetrie** (poslední díl dvanáctidílného
  plánu [plan-map-correlation.md](plan-map-correlation.md), viz [map-correlation-localization.md](map-correlation-localization.md)).
  - **Hotovo:** `ARBotRuntime.MapCorrelator` — nový korelátor vedle `GlobalNavigator` v `WireRun`,
    stejný vzor (guard `RoadNetwork != null && fusionConfig.GeoReference != null`, vlastní vlákno nad
    snapshotem occupancy gridu z `LocalNavigator.Output`, ne nad celým `Stream`). Do
    `TelemetryColumns` přibyla sekce `korel …` (17 sloupců: posun, kurz, skóre, oba konkurenty,
    sigmy, směr určené osy, počet buněk, příznaky odeslání korekcí, důvod, doba výpočtu). Ověřeno
    buildem celého řešení (`dotnet build Src/ARBot.slnx -p:Platform=x64`, 0 chyb) a celou sadou
    (`dotnet test Src/ARBot.Common.Tests -p:Platform=x64` — **631 passed, 4 skipped, 0 failed**).
  - **Finální whole-branch review a její oprava (téhož dne):** review nad celou prací našla šest
    nálezů, které per-task review vidět nemohly, protože každá viděla jen svůj výřez. Podstatné:
    fronta korelátoru (`capacity 2`) nesla i `LocalPlanMsg` při 10–30 Hz, takže při delším cyklu
    tiše vytlačila snapshot gridu; práh nejednoznačnosti porovnával skóre měřená ve **dvou různých
    bodech** (naměřeno 0,8583 místo zamýšlených 0,9000); `SigmaLoose = +∞`, což je na přímé cestě
    **normální** hodnota, rozbíjelo autoscale grafu telemetrie — tedy právě té metody, na které
    stojí ladění ve fázi 4; a marže rastru neuvažovala **rotaci** kandidáta, takže u extrémních
    kandidátů se zahazovaly převážně nesouhlasné buňky a jejich skóre se nadhodnocovalo.
    Přidáno osm testů, mezi nimi tři, které dosud chyběly: že korekce **posune pózu ke skutečnosti**
    (naměřeno Y 0,0000 → 0,6679 proti pravdě 0,7 — obrácení znaménka by dosud prošlo celou sadou),
    že remíza na přímé cestě vyhrává **středem okna** (`Dx` = 0,000, dřív okraj −2,4 m), a že šikmá
    cesta podélnou osu **nepošle**.
  - **Runtime nález (autor si telemetrii zkusil zobrazit):** řada s korelacemi spadla na
    `ArgumentException`. Příčina byla v mém kódu: `MapCorrelationReason` je `: byte` (aby se do
    zprávy vešel na jeden bajt), ale sdílený helper `Enum<T,TEnum>` v `TelemetryColumns` předával
    `Enum.IsDefined` vždy `int` — a to vyžaduje shodu s **podkladovým** typem výčtu. Všechny starší
    výčtové sloupce (`GlobalNavStatus`, `LocalPlanStatus`, `GPSState.FixQuality`) mají standardní
    `int`, takže ta past ležela v `TelemetryColumns` nepovšimnutá a odhalilo ji teprve spuštění.
    Opraven **helper**, ne můj výčet — převod se přesunul do
    `ARBot.Common/Telemetry/EnumPresentation.cs` (vedle `AnglePresentation`), kde ho jde pokrýt
    testy: `byte` i `int` výčet, celý výčet po hodnotách, neznámá hodnota, hodnota mimo podkladový
    typ. Sada 640 testů, 636 prošlo. Poučení: čtrnáct review to nenašlo, protože **UI vrstva nemá
    testovací projekt** — `TelemetryColumns` žije v `Src/ARBot`, na který žádný test neukazuje.
  - **Odsimulované / zbývá ověřit:** samotný běh aplikace (Run, virtuální i reálný HW) s mapou a
    zapnutým korelátorem se nespouštěl — jen kompilace a testy nad `ARBot.Common`. Telemetrické
    sloupce se v běžící aplikaci nezobrazily.
  - **Záměrně beze změny:** `MapCorrelatorConfig.Enabled` zůstává `false` — korelátor počítá a hlásí,
    ale nic neřídí, dokud se nevyřeší otevřená vada „falešná podélná jistota na cestě pod úhlem
    k osám gridu" (viz `map-correlation-localization.md` → Otevřené úkoly).
  - **Rozpracováno / další krok:** fáze 4 (ladění `α`, prahů a σ nad záznamy a virtuálním HW) a
    fáze 5 (měření na OrangePI) nejsou implementační kroky plánu, jsou to měřicí úkoly nad hotovým
    základem.

- **Korelace s mapou — první skutečné spuštění aplikace** (navazuje na předchozí odrážku; **beze
  změny kódu**, jen měření a dokumentace). Dvakrát 40 s s virtuálním HW nad `OSM/HajeRovne.osm`
  (`selftest=true st_record=true virtualhw=true`), Debug i Release; výsledky vytaženy ze záznamu
  diagnostickým nástrojem mimo repozitář.
  - **Korelátor běží a hlásí:** 69 `MapCorrelationMsg` za 40 s, všechny `Reason = Ok`. Proti známé
    pravdě vyšlo `Dx = Dy = 0,000 m`, `Phi = 0,00°` — na správné póze si tedy chybu nevymýšlí.
    Pozor na cirkularitu: `korel skore` ≈ 0,996 je vysoké i proto, že virtuální kamera renderuje
    z **téže** mapy, proti které se koreluje.
  - **Pád na `: byte` výčtu je ověřeně opravený.** Průchod skutečného registru `TelemetryColumns.All`
    přes celý záznam — 64 sloupců × 4 457 řádků, 168 419 neprázdných buněk — proběhl **bez jediné
    výjimky**, `korel duvod` vrací `Ok`. To je právě ta cesta, která se minule sesypala; formátování
    výčtu se volá až při zobrazení (`TelemetryColumn.TextAt`), takže stavba řádků ji sama nepokryje.
  - **Vada „falešná podélná jistota" potvrzena za běhu a zpřesněna.** `SigmaLoose` vyšla konečná
    (≈ 0,32 m) ve **všech 69 cyklech**, ani jednou `+∞` — a to při určené ose 93,6°, tedy cestě jen
    ~3,6° mimo osu gridu. Předpoklad „na přímé cestě bývá `korel sig+` = `+∞`" tedy v praxi neplatí
    skoro nikdy. Hlídač konkurenta podélnou korekci zadržel v 68 z 69 cyklů, ale **v prvním cyklu
    selhal** — a to je nový spouštěč: při řídkém důkazu (5 195 buněk) je konkurent ještě
    rozlišitelný, odstup 0,1456 projde prahem 0,10 a pošle se podélná korekce s **nejmenší σ
    z celého běhu** (0,2481 m). Selhává tedy tam, kde je odhad nejmíň podložený; `MinEvidenceCells`
    proti tomu nechrání. Deterministické, Debug i Release shodně.
  - **Doba cyklu změřena:** Release 126,5 ms průměr / 169,9 max (69 ze 70 snapshotů), Debug 696 ms /
    829 max (55 ze 70, 21 % zahodil `DropOldest`). Poučení pro další měření: **Debug číslo je
    bezcenné**, je 5,5× pomalejší a sám přeteče periodu snapshotu. Odhad „na ARM 100–200 ms"
    v komentáři u fronty v `ARBotRuntime` je nejspíš optimistický — 126 ms je z desktopového x64.
  - **Nový otevřený úkol:** bezobslužný běh **neumí zadat cíl** (jen Ctrl+klik v mapě), takže robot
    vždy stojí a všechna tři měřitelná kritéria fáze 4 zůstávají nedosažitelná bez ruční jízdy.
  - **Odkazy:** [map-correlation-localization.md](map-correlation-localization.md) — doplněn Stav,
    naměřená doba cyklu v sekci rizik, rozšířený otevřený úkol č. 1 a nový úkol o cíli.
    Bez commitu (pravidlo CLAUDE.md).

- **Umělá chyba pózy pro virtuální kameru — korelace poprvé měřená proti známé pravdě**
  (návrh autora: „pro virtuální kameru udělejme speciální nástroj, který umožní nastavit umělou
  chybu; tu by pak měl reportovat korelátor").
  - **Proč to bylo potřeba:** při ověřování za běhu (odrážka výš) se ukázalo, že `Dx = Dy = 0`
    ve virtuálním HW **nic nedokazuje** — kamera renderuje z `engine.GetStateAt(t)` a occupancy
    grid se ukotvuje touž pózou, takže nula je strukturální a vyšla by i rozbitému korelátoru.
    Předchozí formulace v dokumentaci ji četla jako „end-to-end test znamének"; opraveno.
  - **Mechanismus:** do renderovací cesty se vlepí známý posun (`PoseAt = t =>
    hw.VirtualPoseError.Apply(engine.GetStateAt(t))`). Obsah gridu se proti mapě posune o `−e`, což
    je totéž, jako by robot stál na `odhad + e` — a protože korelátor hlásí „skutečná = odhad + D",
    musí vyjít `D = e`. `VirtualCamera` se **nemění vůbec**; GPS a IMU dál měří pravdu, aby známá
    odpověď nezmizela.
  - **Hotovo:** [`VirtualPoseError`](../Src/ARBot.Common/Simulation/VirtualPoseError.cs) v `Common`
    (čistá funkce, 14 testů — znaménka FLU→ENU, neměnnost vstupního stavu, parsování nezávislé na
    národním prostředí), sdílená instance na `ARBotHW`, parametr `poseerror=vpřed,vlevo[,stupně]`,
    a `VirtualCameraDocument` dědící z `CameraDocument` (panel vedle náhledu, očekávané vedle
    naměřených z `MapCorrelationMsg`). Sada **650 prošlo, 4 skipy, 0 selhalo**.
  - **Naměřeno — korelátor obstál:** příčná chyba 0,5 m vlevo → hlášeno 0,5050 m; 0,5 m vpravo →
    −0,4972 m; kurz 3° → `Phi` 3,00°; čistě podélná chyba 0,5 m → příčná složka 0,0000 m (podélnou
    na přímé cestě najít nelze a korelátor si ji **nevymýšlí**). Chyba jednotky milimetrů — poprvé
    je doložené správné znaménko i velikost.
  - **Dva nové nálezy, oba z téhož rozbitého fitu Hessiánu:**
    1. `TightAxisAngle` je soustavně vychýlená o **−6,3°** proti kolmici na cestu. Kdo podle ní
       rozkládá hlášený posun, dostane u velké podélné nejednoznačnosti o 40 % víc (0,695 m místo
       0,505 m) — na tohle jsem sám naletěl, než jsem rozklad převedl na kurz robotu.
    2. **První cyklus je chybný soustavně a přitom se odesílá:** osa je odchýlená o −51° až −89°,
       takže „lépe určená osa" míří skoro podél cesty, a hlášená příčná složka má u obou posunů
       **opačné znaménko** než pravda (−0,484 místo +0,500). Od druhého cyklu (≥ 7 000 buněk) je
       vše v pořádku. `MinEvidenceCells = 400` je proti tomu bezcenné.
  - **Ověření UI:** `Src/ARBot` nemá testovací projekt, tak jsem view nechal projít layoutem mimo
    okno (`AppBuilder…SetupWithoutStarting`) — vazby drží obousměrně. Při té příležitosti se chytla
    past: `NumericUpDown.Value` je `decimal?`, takže vlastnosti musí být `decimal` (jinak by to
    selhalo až za běhu); `WorldViewDocument` to tak dělá už dávno.
  - **Odkazy:** [virtual-hw.md](virtual-hw.md#umělá-chyba-pózy-poseerror) (mechanismus, parametr),
    [map-correlation-localization.md](map-correlation-localization.md) (tabulka měření, varování
    u `TightAxisAngle`, zpřesněný úkol č. 1), [Views/README.md](../Src/ARBot/Views/README.md).
    Bez commitu.

- **Proč je první korelace chybná — příčina dohledána, pojistka rozšířena o rotaci**
  (dotaz autora: „není mi jasné, proč je první korelace chybná, to přece musí mít nějaký důvod").
  Měl pravdu — „řídký důkaz" z předchozí odrážky byla souběžná okolnost, ne příčina.
  - **Příčina:** grid je znečištěný. `InitializePosition` inicializuje jen X/Y, **kurz ne**, takže
    startuje na 0 a ke skutečným −170° dojde až přes `HeadingMeasurement`. `LocalNavigator` mezitím
    fúzuje snímky do world-ukotveného gridu — s kurzem u nuly se ukládají skoro obráceně. A pojistka
    na přesně tento případ (`PoseJumpDetector`, v komentáři „obsah gridu je na spatnem miste")
    byla **o jeden argument krátká**: dostávala jen polohu a `v`, takže rotace o 170° u stojícího
    robotu dala `moved ≈ 0` a skok nehlásila.
  - **Jak se to dokázalo:** vykreslení důkazního seznamu z prvního snapshotu (nástroj mimo
    repozitář). Leží **za** robotem (0,75–5 m) jako zorný kužel s vrcholem u robota mířící dozadu —
    u robota úzký, dál širší, tedy zápis proběhl s kurzem blízko 0°. Od druhého snapshotu je kužel
    normálně vpřed. Třída „cesta" v prvním snapshotu zabírá **5,00 m** napříč proti pozdějším
    přesně **3,00 m** (= `roadwidth`), což je na tři metry široké cestě nemožné, pokud jsou buňky
    umístěné konzistentně. Posun to vysvětlit neumí (odhad se hýbe o ~1,4 m); kužel otočí jen rotace.
  - **Hotovo:** `PoseJumpDetector.Check(x, y, theta, v, omega, t)` — hlídá i rotaci proti novému
    `ToleranceRad` (default 5°, zvoleno tak, aby při dohledu ~6 m odpovídalo `ToleranceM` = 0,5 m).
    Úmyslně **změna podpisu, ne přetížení**: že šla zavolat verze slepá ke kurzu, byla ta vada.
    Úhel se normalizuje přes `Conversions.NormalizeOrientation` — bez toho by přechod přes ±180°
    hlásil skok pokaždé, když robot míří na západ (a tam na `HajeRovne` míří). +6 testů, sada
    **656 prošlo, 4 skipy, 0 selhalo**.
  - **Sama pojistka symptom NESUNDALA:** první cyklus byl pořád chybný ve **2 ze 4 běhů** (týž
    příkaz dvakrát dal jednou −0,492 správně, jednou +0,394 s opačným znaménkem). Souběh, protože
    detektor je *per-krok*: první volání zakládá referenci a skok hlásit nemůže, a plynulá
    konvergence kurzu se pod toleranci 5° za krok schová. Zásah zůstává správný nezávisle na tom —
    abrupt skok kurzu byl pro pojistku slepý plošně, ne jen při startu.

  - **Opraveno chybné tvrzení v dokumentaci:** ve „Zpětná vazba na grid" stálo, že „korekce kurzu
    grid nijak nepoškodí: je world-kotvený, jeho obsah se nerotuje". První část platí, závěr ne —
    a vada plyne přesně z toho. Že se obsah **nerotuje**, je zdroj problému: buňky zapsané starým
    kurzem zůstanou ležet, takže vůči novým zápisům jsou posunuté o `R · dTheta`. Rotace grid
    poškodí **víc** než posun stejné velikosti, ne méně.
  - **Dvě poznámky pro další měření:** v **Release** buildu jsou `Debug.WriteLine` kompilačně
    odstraněné, takže záznam neobsahuje žádné `Info` — logy jdou vytáhnout jen z Debug běhu.
    A `MapMsg` v záznamu **je**, jen se `MsgName` jmenuje `Map` (dřívější poznámka o jeho absenci
    byla omyl ve způsobu hledání).
  - **Odkazy:** [PoseJumpDetector.cs](../Src/ARBot.Common/Occupancy/PoseJumpDetector.cs),
    [LocalNavigator.cs](../Src/ARBot.Common/Occupancy/LocalNavigator.cs),
    [map-correlation-localization.md](map-correlation-localization.md). Bez commitu.

- **Kurz se v EKF inicializuje — první korelace je poprvé správná** (rozhodnutí autora ze tří
  navržených cest: „pokud znám kurz, tak proč ho neinicializovat; ARBotRuntime o inicializaci hned
  posílá měření směru, aby se to srovnalo — takhle to bude umět rovnou EKF"). Viz
  [decisions.md](decisions.md).
  - **Hotovo:** `AsyncFusionEngine.InitializeHeading(theta, std, t)` jako obdoba
    `InitializePosition`; sdílené jádro obou v jednom privátním `InitializeAxesLocked`, aby se
    nemohly rozejít. `ARBotRuntime.InitializeStartPose` ji volá místo `HeadingMeasurement`.
    Kdo kurz nezná (GPS fix ho nenese), posílá ho dál jako měření — ta cesta zůstává.
    +5 testů, sada **661 prošlo, 4 skipy, 0 selhalo**.
  - **Test, který to odůvodňuje:** při `P0 = I` je σ kurzu 1 rad (57°), takže startovní měření
    o 170° vedle má NIS ~8,7 proti χ²(1; 0,95) = 3,84 — se zapnutým gatingem se **zahodí**. Tatáž
    latentní past, jakou u polohy popisuje `FarAwayFix_WithGating_WouldBeRejected`. Do teď to
    nebylo vidět jen proto, že prahy gatingu nikdo nenastavuje.
  - **Naměřeno:** vnucená chyba −0,5 m napříč, čtyři běhy → první cyklus −0,487 / −0,481 / −0,487 /
    −0,479 m (chyba 1,3–2,1 cm), určená osa −6,3 až −6,8° místo −51 až −89°, `korel os+` zhasnutý.
    Před opravou chybný ve 3 ze 3. Ustálený stav nedotčen (0,5048 proti 0,5000 m).
  - **Zbývá:** po zahození gridu se objeví cyklus s ~2 000 buňkami, kde `korel os+` svítí, byť
    hodnota je správná. Není to regrese — a po doměření (viz další odrážka) se ukázalo, že to není
    ani samostatná vada.


## 2026-08-18

- **Srovnání dokumentace se skutečností** (podnět autora: „máme někde seznam věcí k řešení?").
  Seznam úkolů je záměrně u domén (`Otevřené úkoly` v devíti docs), souhrn nikde — při sbírání
  vyplavaly **čtyři zastaralé položky**, opraveno:
  - `osm-nav.md`: „napojení na řídicí smyčku — zatím neimplementováno" → hotové (fáze 0–4).
  - `world-view.md`: vrstvy Trasa/graf/Značky/Mapa už nejsou *dormantní* — runtime zprávy emituje;
    opravena i tabulka vrstev a poznámka pod ní.
  - `global-navigation-runtime.md`: „`OSM/` je mimo git" → je verzované; otevřené zůstává jen to,
    odkud se výřezy berou.
  - `record-replay.md`: „duplicitní rozchod 0,5 vs 0,41" → **vyřešeno** už dříve,
    `FusionConfig.WheelBase = Profile.Rozchod` (ověřeno v kódu).
- **Ruční proklikání telemetrického UI** (autor): flyouty, filtr řádků, přehazování sloupců,
  dvojklik = seek i ovládání grafu myší — chová se to podle očekávání. Stavová tabulka
  v [telemetry-view.md](telemetry-view.md) srovnána, položka „proklikat myší" z „Co zbývá" vypadla.

- **World pohled: Shift + klik přesune simulovaného robota** (žádost autora) — vývojářská pomůcka
  na zkoušení scénářů bez restartu běhu, vedle existujícího Ctrl + klik = cíl. Detail:
  [virtual-hw.md](virtual-hw.md).
  - Podstatné je, že se **nemění jen poloha**: srovnat se musí naráz ground truth simulace, **fúze**
    (`InitializePosition` — jinak by EKF držel starou polohu a přetahoval se) a **rozjetá dráha**
    (vede odjinud; regulátor se nuluje hned, dráhu zahodí navigátor na svém vlákně přes nový
    `LocalNavigator.RequestPathReset`). Na tu poslední část je test.
  - **Kurz zůstává** a **occupancy grid se nečistí** (rozhodnutí autora) — integrátor ho na novou
    pózu vycentruje sám při dalším snímku. Trajektorie se při skoku > 2 m začne kreslit znovu.
  - Platí jen v Run s virtuálním HW; jinak runtime vrátí `false` a napíše důvod do Debug outputu.
  - **Ověřeno:** build `x64`, 535 testů. **Za běhu neověřeno** — neklikal jsem.

- **Lokální plánovač: únik z blokované buňky** (myšlenka autora — robot uvázne, když se na `Blocked`
  buňku dostane). Návrh v [occupancy-and-local-planning.md](occupancy-and-local-planning.md).
  - **Rozbor záznamu rozhodl, kudy do toho.** V `20260818-093903.rec` robot 5 s (47 plánů) hlásil
    `RobotBlocked` až do konce záznamu. Buňka pod ním: `LOcc = −4,85` (hloubka na **záporném** dorazu
    = jistě volno), `LRoad = +5,00` (barva na **kladném** dorazu = jistě mimo cestu). Nejbližší
    nezablokovaná buňka **0,05 m**. Nebyla to překážka, ale okraj cesty.
  - **Relaxaci gridu jsme zamítli**: `LRoad` sedí na clampu, robot stojí, a buňku pod sebou dopředu
    hledící kamera nikdy neuvidí — evidence-based zapomínání se nemá o co opřít. Časový decay by
    nechal vyblednout i skutečné překážky.
  - **Dělicí čára je kanál, ne vzdálenost:** ven se smí přes semanticky blokované buňky (z trávy na
    cestu), přes geometricky blokované nikdy. Výchozí buňka je výjimka — robot na ní stojí.
  - Cílem úniku není cíl mise, ale nejbližší buňka průjezdná běžným pravidlem; hledá se uniformní
    cenou do `EscapeMaxLength` (1,5 m), jinak `RobotBlocked` (bloudit mimo cestu je horší než stát).
    Rychlost srazí brzdná obálka sama — únik je popojetí krokem.
  - Dvě návaznosti: `PathCollides` posuzuje únikovou dráhu **jen podle geometrie** (jinak by ji hned
    zahodil), a `EscapingBlocked` v `GlobalNavigator` **není selhání plánu** (jinak by uváznutí
    nakonec zavřelo hranu, která je v pořádku) — na obojí je test.
  - **Ověřeno:** 6 nových testů, celkem 534 zelených, build `x64`. Regresní test drží, že běžné
    plánování přes `Blocked` dál nevede. **Za běhu neověřeno** — v aplikaci to neběželo.
  - **Odloženo (krok 3 z návrhu):** zapisovat pod půdorysem robotu důkaz „volno" do kanálu hloubky.
    Do semantického kanálu psát nelze — robot by se naučil, že cesta je všude, kam zabloudí.

- **Sjednocené směrové údaje v telemetrii** (pozorování autora: „jednou mají nulu na severu,
  podruhé v matematickém smyslu"). Nově platí jedno pravidlo: **uloženo je vždy matematicky ve
  stupních**, převod na azimut dělá až zobrazení. Sloupec k tomu nese `AngleKind`
  (`Heading` / `Rate` / `None`) a v liště je přepínač **Azimut** pro celou tabulku najednou.
  - **Proč i `Rate`:** kdyby se přepínaly jen kurzy, byl by ve světovém režimu kompasový kurz vedle
    zatáčení „doleva kladně" — tatáž past, jen posunutá. Ve světové konvenci se proto úhlovým
    rychlostem obrací znaménko. `IMU pitch`/`roll` jsou náklony, ne kurzy — těch se to netýká.
  - **Co to opravilo:** `GPS azimut` se převáděl na azimut už při čtení ze zprávy, kdežto `theta`
    a `IMU yaw` zůstávaly matematické — tabulka tedy míchala dvě konvence. Sloupec se jmenuje
    `GPS kurz` a vrací matematickou orientaci jako ostatní.
  - Převod je na jednom místě (`AnglePresentation.Present`) a sedí v `TelemetryColumn.ValueAt`
    a `TextAt`, takže ho podědí i graf (řady se táhnou přes `ValueAt`). Data se nemění, surová
    hodnota zůstává na `RawValueAt`.
  - Klasifikace je v registru pohromadě (`TelemetryColumns.Mark`) a **neznámé záhlaví hodí výjimku** —
    po přejmenování sloupce se příznak nemůže tiše ztratit.
  - Přepínač je v liště **tabulky i grafu**; graf data nevlastní, jen o přepnutí požádá tabulku
    (`WorldAnglesRequested`), ta přepočítá řady a pošle je zpátky — obě okna tedy nikdy neukazují
    jinou konvenci a graf zůstává kreslítkem nad hotovými řadami.
  - **Ověřeno:** 13 nových testů (převodní tabulka, obě konvence v tabulce, řada grafu jde za
    tabulkou), celkem 528 zelených, build `x64`. **Za běhu neověřeno** — přepínač jsem neklikal.

- **Pojistka na zrychlení motorů — na hostovi, ne ve skriptu** (diskuse s autorem: „`acceleration<=0`
  je technicky nesmysl"). Souhlas, ale s ostřejším důvodem: i kdyby pojistka ve skriptu zafungovala,
  robota **nezastaví** — při nulovém zrychlení je rampa mrtvá, takže `reqSpeed=0` nemá čím zabrat
  a vynulování rotace jen sebere zatáčení. Zpřesnění k nule: nebezpečná není nula od začátku
  (to se robot nerozjede), ale nula, která přijde **za jízdy**. U záporné hodnoty má autor pravdu
  bez výhrad — rampa diverguje od cíle až na saturaci, tedy plná rychlost opačným směrem.
  - Skutečná díra byla na hostovi: `SetAcceleration` v obou driverech posílal hodnotu bez kontroly
    (záporná prošla, malá se zaokrouhlila na nulu — `v = 1182·a`, takže pod 0,00043 m/s² nula).
    Nově společný [`MotorAcceleration.ToUnits`](../Src/ARBot.HAL/Devices/MotorDriver/MotorAcceleration.cs):
    bere velikost, nikdy neposílá nulu (minimum 1) a clampování hlásí do Debug outputu. 5 testů.
  - Z komentáře u skriptu (a tím i z `.mbs`) vypadl slib pojistky, která tam není a nepomohla by;
    místo něj je tam napsaný **předpoklad** `acceleration > 0` a odkaz, kdo ho hlídá.

- **Opravena dynamika virtuálního robotu** (navazuje na rozbor výše). `SimulatedRobot` rampoval
  každé kolo zvlášť; nově drží stav v `(dopředná, rozdíl)`, každá složka má svou rampu a při
  saturaci **ustupuje dopředná rychlost, rotace se drží** — přesně jako skutečný řadič
  (`RizeniDiffPodvozku.mbs`, totéž v `SDC2160.Drive`). Simulace navíc poprvé má strop rychlosti kola
  (`VirtualHWOptions.MaxWheelSpeed`, default `Profile.MaxTheoreticalSpeed`), který dosud neměla vůbec.
  - **TDD doloženo:** regresní test na starém modelu vrací 0,0 rad/s místo 0,428 (rozdíl kol zmražen)
    a saturační test pustil kolo na 1,3 m/s při stropu 1,0. Po opravě 6/6 zelených, celkem 515 + 30.
  - **Rozhodnutí nedělat 50/50:** referencí je skutečný řadič a ten dává rotaci absolutní přednost;
    kompromis by simulaci rozešel s robotem jiným způsobem. Detail: [virtual-hw.md](virtual-hw.md).
- **`RizeniDiffPodvozku.mbs` dosynchronizován** ze skriptu v komentáři `SDC2160Ex.cs` — ten byl
  novější (změna nouzového zastavení z 11. 8.: rotace se nuluje až při `curSpeed = 0`, rozlišení
  watchdogu od e-stopu, oprava „tisicanach" → „tisicinach"). Výpočetní jádro bylo shodné.
  **Do jednotky se to musí nahrát a ověřit na zařízení** — soubor sám chování robota nezmění.
  - **Nález při synchronizaci:** komentář slibuje pojistku `acceleration<=0` v cestě nouzového
    zastavení, ale v kódu skriptu není. Neopravováno — je to firmware a bezpečnostní cesta.

- **Rozbor dynamiky virtuálního robotu** (pozorování autora: při požadavku −30 a +30 °/s nejsou
  směrnice `theta` symetrické; záznam `20260818-093903.rec`). **Kořenová příčina nalezena:**
  `SimulatedRobot.Step` omezuje zrychlení **per kolo**, takže když jsou saturovaná obě, rozdíl
  rychlostí kol (a tím `ω`) se zmrazí. Detail v [virtual-hw.md](virtual-hw.md).
  - Data z obou oken: fúze, odometrie i sklon `theta` **spolu souhlasí** (vpravo −30,0 / −30,0 /
    −30,0 °/s; vlevo +5,8 / +5,8 / +5,8) — chyba tedy není ve fúzi ani v měření, ale v tom, že kola
    nevykonala příkaz.
  - Nešlo o asymetrii vlevo/vpravo: při zatáčce doleva smyčka **současně** poručila skok rychlosti
    1,20 → 0,17 m/s; obě kola šla na dorazovou deceleraci 0,5 m/s² a rozdíl zůstal 2 s zmražený na
    0,041 m/s. Při zatáčce doprava byl rozdíl ustavený dřív, než kola narazila do limitu.
  - **Neopraveno záměrně** — oprava je rozhodnutí o modelu (rampovat zvlášť `v` a rozdíl kol) nebo
    o řídicí smyčce, a napřed je potřeba vědět, jak rampuje skutečný driver.
  - **Vedlejší nález:** XML dokumentace `DriveCommandMsg.Dif` tvrdí `dif = RotationSpeed * Rozchod`,
    ale v záznamu i v `IMotorControl` platí `dif = omega * rozchod / 2`. Dokumentace zprávy lže.
- **Telemetrie: odometrická rychlost a rychlost otáčení** (`odo v`, `odo omega` z `MotorStateBase`) —
  právě ta dvojice, která šla srovnat s `cmd v`/`cmd omega` a s `v`/`omega` z fúze. Registr má 47 sloupců.

- **Telemetrie: doplněny senzorové zprávy** (žádost autora) — `MotorStateBase` (rychlosti kol,
  kumulativní enkodéry, napětí baterie, proudy motorů, `HW STOP`, zahozené vzorky), `IMUState`
  (yaw/pitch/roll z kvaternionu, `gyro z`, `acc x`/`acc z`, důvěra, zahozené vzorky) a rozšířená
  `GPSState` (výška, rychlost, azimut). Registr má teď 45 sloupců ze 7 typů zpráv.
- **Proč to stálo za to:** teprve s těmito sloupci jde v jedné tabulce srovnat řetěz
  *příkaz → skutečnost*: `cmd omega` (co chtěla smyčka) → `gyro z` (co naměřilo IMU) → `omega`
  (co z toho udělala fúze), nebo `cmd v` → `kolo L`/`kolo R`.
- **Jedna úprava jádra registru:** `Num<T>` teď bere `Func<T, double?>` místo `Func<T, double>` —
  senzorová pole jsou nullable (`Vector3?`, `Quaternion?`, `double?`) a chybějící hodnota musí
  zůstat chybějící, ne nula. Existující sloupce se nezměnily (`double` se na `double?` převede sám).
- **Cena, kterou je dobré znát:** senzory chodí mnohem častěji než řídicí smyčka, takže na testovacím
  záznamu vyrostl počet řádků z 2 806 na 21 556 a sken z 29 na 71 ms. Filtr **Řádky ▾** to řeší —
  nechá zakládat řádky jen vybraným typům, hodnoty ostatních se dál drží z minula.
- **Zjištění ze záznamu:** IMU v tom běhu dodává jen orientaci, úhlovou rychlost a důvěru —
  `Acceleration` je `null` (serializace ho přenáší, driver ho neplní), takže `acc x`/`acc z` zůstávají
  prázdné. Prázdná buňka správně znamená „nikdy nepřišlo", ne nulu.
- **Ověřeno:** build `x64`, testy (512 zelených) a sken skutečného záznamu — motory i IMU plní
  hodnoty (enkodér 117,1 m, baterie 24,0 V, yaw 115,6°, gyro z 0,92 °/s). **Neověřeno za běhu**
  v UI (nová jsou jen data v registru, mechanismus tabulky je týž).

## 2026-08-17

- **Telemetrický pohled: čitelnost, tooltipy a chybějící synchronizace s přehráváním** (zpětná vazba
  autora z prvního spuštění nad reálným záznamem). Sloupec času byl užší než `HH:mm:ss.fff`, takže
  ořezával **milisekundy** — u telemetrie zrovna to podstatné (130 px, typ zprávy 155 px). Záhlaví
  je větším písmem (14) a hodnoty v buňkách se svisle centrují jako čas, aby se řádek četl jako
  jeden celek; detail řádku je z 11/12 na 13/14. Nově má každý sloupec v registru **`Description`** —
  vysvětlení údaje, které se ukáže jako tooltip na záhlaví i na řádku detailu (záhlaví musí zůstat
  zkratka, význam patří jinam). **Synchronizace kurzor přehrávání → řádek** přitom nebyla vůbec
  implementovaná, jen opačný směr (dvojklik = seek): dokument teď polluje `FileMessageSource.Cursor`
  a vybírá poslední už přehraný řádek (scrolluje jen když výběr udělalo přehrávání). Vedlejší
  oprava: `Cursor` je `Seq` **následující** zprávy, takže se pozice v `ReplayNavTool` opravila o
  jednu a jeho časovač běží pořád — jinak by slider po skoku z tabulky ujel o řádek a o skoku odjinud
  by se vůbec nedozvěděl. Ověřeno buildem (`x64`) a testy (516 zelených); **UI za běhu neověřeno**.
  Viz [telemetry-view.md](telemetry-view.md).
- **Navazující drobnosti z téhož pohledu:** `fi` (cost-to-goal) se zobrazuje na **3 desetinná
  místa** — na `F1` vypadalo zamrzle, protože se mezi takty mění o zlomky sekundy. A **HDOP byl
  v celém záznamu nula**: není to chyba telemetrie, ale `VirtualGps`, který ho vůbec nenastavoval
  (že jde o simulaci, prozradí i konstantních 12 družic a stále `GpsFix`). Doplněn `GpsHdop = 0,9`
  do `VirtualSensorOptions` — stejná kosmetika jako počet družic, simulace geometrii družic
  nemodeluje. **Starší záznamy ze simulace mají v HDOP dál nulu.** Na reálném HW je hodnota
  namapovaná správně (uBlox `PVT.pDOP`, NMEA `GGA[7]`). Viz [virtual-hw.md](virtual-hw.md).
- **[telemetry-view.md](telemetry-view.md) srovnán se skutečností** — byl pořád psaný jako návrh
  v budoucím čase, i když fáze 1 stojí. Nová tabulka **Stavu** a sekce **Co zbývá** oddělují hotové
  od slíbeného; opraveno několik tvrzení, která v kódu neplatila: skener si sidecar `*.idx` sám
  **nečte** (bere hotový index z runtime), seznam sloupců neodpovídal registru (uváděl `Forvard`
  a „expandované buňky", které nikdy nevznikly), a **výběr sloupců ani filtr řádků nejsou
  implementované**, přestože je dokument popisoval jako součást UI. Doplněno chování, na které
  se přišlo až při čtení kódu: u slitého řádku platí `Seq` **první** zprávy taktu (seek míří na
  začátek taktu), `Truncated` se nehlásí, když za stropem už nic sledovaného není, poškozený rámec
  sken nezastaví, a skener filtruje jen podle `MsgName` (`Name` řeší až builder). Testů je **14**,
  ne 15, jak tvrdil plán.
- **Oprava nalezená tou revizí: tabulka se držela prvního záznamu.** Sken běžel jen v konstruktoru
  dokumentu, takže po otevření **jiného** záznamu zůstaly v tabulce staré řádky (a `Seq` z nich
  ukazovaly do cizího souboru — dvojklik by skákal jinam, než uživatel čte). Stejná díra platila
  obráceně: telemetrie otevřená **dřív než záznam** už se nikdy nenaplnila. Nově týž časovač, který
  hlídá kurzor přehrávání, porovnává i `RecordPath` + referenci na `FileMessageSource` a při změně
  přeskenuje; výsledek zastaralého skenu se zahodí podle jeho `CancellationTokenSource`, aby
  nepřepsal tabulku nového záznamu. Ověřeno buildem a testy (516), **za běhu neověřeno**.
- **Dotažena fáze 1 telemetrie + udělána fáze 2 (grafy).** Výběr viditelných sloupců a filtr řádků
  podle typu zakládající zprávy (obojí návrh sliboval a chybělo) jsou dvě tlačítka s flyoutem ve
  stavovém řádku; filtruje se **jen zobrazení**, data zůstávají celá. Filtrovaná kolekce se vyměňuje
  celá — u desetitisíc řádků by jednotlivé notifikace tabulku na vteřiny zastavily.
- **Graf telemetrických řad** (`TelemetryChartDocument` + `TelemetryChartControl`): řada se vytahuje
  z tabulky jako **jen skutečné příchody** (`TelemetrySeries` v `ARBot.Common`, 5 testů) — držené
  hodnoty jsou opakování bez informace a schod/rampa se dá nakreslit i tak. Dvě rozhodnutí proti
  původnímu návrhu: (a) místo „volitelně druhé osy Y" má **každá řada vlastní měřítko**, protože dvě
  osy stačí na dvě řady a v grafu jsou metry, °/s i stav výčtu; osa Y s čísly se kreslí, jen když je
  zapnutá jedna řada. (b) Kreslí se **vlastním `Control.Render`**, ne knihovnou — data už jsou v poli,
  projekt kreslené controly má a balíček by přinesl další nároky na verzi Avalonie (přesně ten
  problém, co má `Avalonia.Controls.DataGrid`). Hustá data se kreslí jako obálka min/max po pixelech.
  Klik do grafu skáče v přehrávání, kurzor přehrávání je svislá čára a legenda ukazuje hodnotu v tom
  místě. Build `x64`, testy 521 zelených; **kreslení ani ovládání myší za běhu neověřeno**.
  Drobnost pro příště: emoji mimo BMP (📈) rozbije build C# souboru bez BOM — BMP znaky (▶, ⏸, ▾)
  fungují.
- **Zpětná vazba k telemetrii a grafu (4 body autora), všechny hotové:** (1) sloupce tabulky jde
  **přehazovat myší** (mapa sloupců drží reference, ne pozice, takže to nic nerozbije); (2) přidání
  údaje do grafu je i **ikonou přímo v záhlaví sloupce** — svázanou obousměrně s přepínačem ve
  flyoutu, aby se ovladače nerozešly; ikona je nakreslená geometrie, protože symboly grafu nemusí
  být v použitém fontu; (3) graf umí **lupu na ose Y** (Ctrl+kolečko) — zoomuje se v normalizované
  ose společné všem řadám, aby jejich vzájemné porovnání zůstalo platné, a popisky osy procházejí
  týmž přepočtem, takže po přiblížení nelžou; (4) **odečítátko hodnot pod myší** (obdoba trackeru
  z OxyPlotu): svislá čára, tečka na každé křivce a rámeček s hodnotou **každé** viditelné řady —
  u schodu poslední příchod, u rampy interpolace (`InterpolatedAt`, +1 test).
- **K OxyPlotu** (autor ho historicky používal a chválí): zůstáváme u vlastního kreslení, protože
  oficiální `OxyPlot.Avalonia` cílí na Avalonii 11 a pro dvanáctku je jen neoficiální fork se 162
  staženími — na produkční závislost robota málo. Zdůvodnění a podmínky přehodnocení jsou
  v [decisions.md](decisions.md). Build `x64`, testy 522 zelených; **UI za běhu neověřeno**.
- **Snímky telemetrie a grafu do deníčku** — a s nimi první běh celé věci. Přibyl parametr
  `telemetryshot=true` (obdoba `worldshot=true`): otevře poslední záznam se sidecar indexem ve
  `records/`, počká na sken, posune přehrávání doprostřed, uloží snímek tabulky, pak zapne tři
  údaje do grafu a uloží snímek grafu. Bezobslužně a reprodukovatelně — snímky featury se tím dají
  kdykoli pořídit znovu ([MainWindowViewModel.TelemetryShot.cs](../Src/ARBot/ViewModels/MainWindowViewModel.TelemetryShot.cs)).

  ![Telemetrická tabulka](media/telemetry-view.png)

  Tabulka nad reálným záznamem: milisekundy se vejdou, záhlaví je čitelné a nese ikonu grafu,
  hodnoty jsou svisle na střed, vpravo detail se stářím údajů. Vybraný řádek (22:45:35.949)
  odpovídá kurzoru přehrávání v Replay panelu (`Seq` 3065) — **synchronizace funguje**.

  ![Graf telemetrie](media/telemetry-chart.png)

  Graf tří řad (`v`, `cmd v`, `omega`) s kurzorem přehrávání a legendou, která u každé řady ukazuje
  hodnotu v místě kurzoru a rozsah řady.
- **Nález ze snímku: čas řádku není monotónní.** Na obrázku tabulky jsou dvě sousední `LocalPlanMsg`
  s klesajícím T_in (35.243 → 35.195) a `GPSState` proložené mimo pořadí. Je to logické — řádky jdou
  v pořadí **záznamu** (T_out), ale čas řádku je čas **pořízení** (T_in) a každá zpráva putuje
  pipeline jinak dlouho. Tabulce to nevadí, ale **řada v grafu je osa X** a musí být rostoucí, jinak
  by křivka dělala klikyháky a půlení v `ValueAtTime` vracelo nesmysly. `TelemetrySeries.From` proto
  řadu setřídí (jen když je potřeba), +1 test. Opraveno dřív, než to stihlo někoho zmást — v grafu
  na snímku je to vidět nebylo, protože `LocalPlanMsg` v něm zapnutý není.
- **Replay panel se dokuje k Debug outputu, ne mezi dokumenty** (žádost autora). Dosud vznikal jako
  další záložka v `DocumentDock`, takže při jeho aktivaci zmizel obrazový dokument — a přitom se na
  replay kroky člověk dívá právě kvůli obrázkům. Nově jde do téhož (spodního) doku jako Debug output;
  když tam Debug output není (zavřený / připnutý / vytažený do plovoucího okna), nadokuje se dolů
  samostatně stejnou cestou jako `ReopenTool(..., Alignment.Bottom)`. Vedlejší oprava: při otevření
  **dalšího** záznamu se starý panel (navázaný na už zavřený `FileMessageSource`) zahodí a vytvoří
  nový — dřív se jen aktivoval ten starý s neplatným zdrojem. Viz [record-replay.md](record-replay.md).
- **Tooltip na úseky lokálního plánu ve World pohledu** (žádost autora). Plán byl jen modrá čára —
  parametry, které ji určily, nešly v mapě zjistit vůbec. Najetím kamkoli na čáru se teď ukáže popis
  úseku `k → k+1`: hlavička plánu (stav, počet bodů, délka, cena, min. odstup, doba výpočtu), délka
  úseku, kumulativní vzdálenost od robota, směr v ENU a předepsaná rychlost + tolerance polohy v obou
  koncích. Hit-test je na **úsečku** (waypointy se nekreslí, míří se na čáru) a běží až po bodových
  značkách, aby jim popis úseku nepřebil jejich vlastní tooltip. Viz [world-view.md](world-view.md).
- **Tooltipy i pro `GraphNavigationMsg` a `GlobalNavMsg`** (navazující žádost). Hrany trasy/grafu
  dostaly vlastní popis (OSM `WayId`, druh hrany včetně **uzavřených/penalizovaných**, délka, azimut,
  šířka cesty, uzly, vzdálenost k cíli když je spočtená). `GlobalNavMsg` žádnou geometrii nemá — cíl
  i mrkev už kreslí Značky — takže se z něj skládá **hlavička** připojená ke všemu, co globální
  navigace vyrobila (značky + hrany trasy): stav, cíl, vzdálenost od sítě, zbývající trasa, φ, počet
  uzavření, mrkev, čas cyklu. World pohled si tím poprvé bere `GlobalNavMsg` ze streamu.
- **Doplněna i síť OsmNav** (`MapMsg`, vrstva „Mapa"). Tím mají tooltip všechny tři úrovně
  navigace: síť → globální trasa (`GraphNavigationMsg`) → lokální plán (`LocalPlanMsg`).
  U sítě je trefou **pás cesty** (kreslí se v metrické šířce, tak se na ni i míří) a text se skládá
  až při trefě — desetitisíce hran, předpočítané řetězce by byly megabajty. Hledá se poslední:
  pás leží pode vším, jinak by přebil trasu i plán, které po něm vedou.
- **Zdokumentován `GraphNavigationMsg`** (žádost autora) — XML komentáře u třídy, vrcholu, hrany
  i konstruktorů + sekce „Zprávy s víc producenty" v [record-replay.md](record-replay.md).
  Jádro věci: je to **obecný kontejner**, který plní čtyři různí producenti a **každý jinak** —
  souřadnice jsou jednou lokální ENU (`GlobalNavigator`), jindy složky ECEF (starší `Maps.Map`);
  `Edge.Length` je jednou metr, jindy váha; `Vertex.Distance` platí jen při `DistanceCalculated`.
  Verze 1 vs 2 se liší jediným polem (`HightLight`).
- **Šířky a pořadí navigačních vrstev ve World pohledu** (z fotky autora: modrý plán byl schovaný
  pod zelenou trasou). Plán se kreslil **pod** trasou a byl užší → zmizel. Nově se kreslí od nejširší
  po nejužší (síť → trasa → plán) a šířky jsou odvozené z jedné konstanty: plán 3 px, hrana trasy
  1,5× plán, zvýrazněná cesta 2× plán. Pořadí hledání tooltipu kopíruje pořadí vykreslení (při shodě
  vyhrává plán).
- **Při tom vyšlo najevo:** uzavřené/penalizované hrany se kreslí **šedě jako zbytek grafu** —
  `GlobalNavigator` je posílá s `Collision=true`, ale vykreslování rozlišuje jen `HightLight`/`Path`
  (komentář v `BuildRouteMessage` slibuje odlišenou barvu). Zatím to řeší jen tooltip; barvu jsem
  neměnil, protože o to nikdo nežádal.
- **Ověřeno:** build `x64`. **Neověřeno:** vzhled a chování za běhu (vše je čistě UI, bez testů —
  aplikace nemá testovací projekt).
- **Nový telemetrický pohled — fáze 1** (žádost autora: vidět stav robota, řídicí zásahy a údaje
  z dalších zpráv *pohromadě* a srovnané v čase). Návrh v [telemetry-view.md](telemetry-view.md),
  kroky v [plan-telemetry-view.md](plan-telemetry-view.md).
  - **Stojí na indexu záznamu, ne na odběru `Stream`u.** Index má čas u *každé* zprávy (`ArrivalTicks`
    stampuje `RecordingTarget`, `CaptureTicks` je 0 u zpráv bez `IHasCaptureTime`), takže je to jediné
    místo s úplnou časovou osou. Navíc dovolí **přeskočit obrázky bez čtení** — sken čte jen typy,
    které mají sloupec.
  - **Řádek = jedna zpráva**, zprávy se shodným časem se slévají. Na skutečném záznamu se 4833
    registrovaných zpráv slilo do 2806 řádků, tedy póza a řídicí zásah z jednoho taktu skutečně
    padnou na jeden řádek. **Kotvicí typ zprávy se ukázal jako zbytečný** (autorova otázka „proč to
    potřebuju" byla oprávněná) — zjednodušeno.
  - **„Právě přišlo" se neukládá**, plyne ze změny času hodnoty (`ValueTicks[r] != ValueTicks[r-1]`) —
    nemůže se to s daty rozejít. V tabulce je to tučně, v detailu jako stáří v ms.
  - **Jádro v `ARBot.Common/Telemetry`** (15 testů, celá sada 505 zelená), registr sloupců v UI
    vrstvě — jednotky a formát nepatří do domény. Přidat údaj = jeden řádek v registru.
  - **Ověřeno i na reálném záznamu**: 27 541 zpráv v indexu, sken **29 ms**, 2806 řádků.
  - **Neověřeno:** UI za běhu — aplikace se rozběhne, ale tabulku jsem neotevřel (vyžaduje kliknutí
    Runtime → View… a Tools → Telemetrie). Nová závislost `Avalonia.Controls.DataGrid` **12.0.0**;
    novější verze vynucují Avalonia ≥ 12.0.5, což projekt nemá.
  - Chybí ve zprávách: *max. povolená rychlost z plánovače* (`LocalPlanResult` ji zná, `LocalPlanMsg`
    ji nepřenáší) a *plánované odbočení* (neexistuje nikde) — obojí je samostatný krok.

## 2026-08-16

- **Toolbar pro snímek obrazovky a videozáznam okna** (žádost autora). Pod menu přibyl pruh
  s tlačítky **Snímek** / **● MP4** / **● GIF**; výstup jde do `doc/media/` jako `shot-*.png`
  a `rec-*.mp4|gif` (nové vzory v `doc/media/.gitignore` — je to pracovní výstup; co má zůstat
  v deníčku, se přejmenuje na popisný název).
- **Většina schopností už v repu byla** — `ScreenCapture`, `Ffmpeg`, `GifWriter` z self-testu.
  Chyběla jen interaktivní cesta k nim: dosud šly použít výhradně parametry z příkazové řádky
  (`selftest=true st_shot=…`), tedy s ukončením aplikace na konci.
- **Nové je průběžné kódování:** self-test ukládá každý snímek jako PNG do dočasné složky a kóduje
  až nakonec — pro záznam bez předem známé délky se to nehodí. `FfmpegPipe` proto drží běžící ffmpeg
  a posílá mu surové BGRA na stdin (`-f rawvideo`): konstantní paměť i disk, na UI vlákně zbyde jen
  kopie pixelů. Zápis do roury má vlastní vlákno a frontu na 8 snímků; při nestíhání se snímek zahodí,
  aby se UI nikdy nezablokovalo. Buffery se recyklují (jinak megabajty alokací na snímek).
- **Limity a auto-stop:** mp4 15 fps/1280 px/10 min, GIF 8 fps/800 px/60 s (GIF je omezenější,
  protože `palettegen` si drží celý stream v paměti). Po limitu se záznam sám uloží — zapomenuté
  nahrávání nemá zaplnit disk. Bez ffmpegu funguje GIF přes vestavěný zapisovač, mp4 to odmítne
  s hláškou.
- **Ověřeno za běhu na Windows/x64** (tlačítka odkliknuta přes UI Automation): PNG, GIF i mp4 vzniknou,
  popisky tlačítek se přepínají na Stop, druhý formát je během záznamu zamčený, hláška ukazuje délku,
  počet snímků a zbývající čas. Výsledný mp4 zkontrolován ffmpegem (1280x642, h264, 15 fps).
  **Neověřeno:** Armbian/OrangePI (`Ffmpeg.Find()` hledá jen `ffmpeg.exe` → tam bude nutné
  `ARBOT_FFMPEG`) a fallback bez ffmpegu.
- **Doplněno na žádost autora:** jméno uloženého souboru je v toolbaru **odkaz** (otevře ho
  v přidružené aplikaci) a přibyla **ikona složky** (otevře `doc/media/` a soubor v ní označí).
  Kvůli tomu se cesta oddělila od textu hlášky (`RecordingResult.Path` vs. `Message`).
  Otevírání je v `ShellOpen` (Windows `explorer /select`, Linux `xdg-open`) a ikona je vektorová
  (`PathIcon`), ne emoji — na Armbianu nemusí být emoji font.
- **První ostré použití** (autor) — simulované odbočení ve World pohledu na virtuálním HW.
  Záznam je pořízený právě novým tlačítkem **● GIF**, takže je v něm vidět i toolbar sám:
  vlevo nahoře přepnuté **■ Stop GIF** a průběžná hláška `● REC gif · … · zbývá … s`.

  ![Simulované odbočení: World pohled s OSM podkladem, virtuální HW (VirtualMotors/GPS/IMU), robot
  projíždí zatáčku po trase; nahoře toolbar v režimu záznamu](media/SimulovaneOdboceni.gif)

- **Odkazy:** [doc/screen-capture.md](screen-capture.md) (nový),
  `Src/ARBot/Diagnostics/{ScreenRecorder,FfmpegPipe,ShellOpen}.cs`,
  `Src/ARBot/ViewModels/MainWindowViewModel.Capture.cs`, `Src/ARBot/Views/MainWindow.axaml`,
  ukázka [media/SimulovaneOdboceni.gif](media/SimulovaneOdboceni.gif).

- **`GreatCircle` bere `Ellipsoid`; vzdálenosti sjednoceny na WGS84** (žádost autora, navazuje na
  zjištění u syntetické mapy). Místo haversinu na pevné kouli R = 6 371 000 m počítá geodetiku
  (Vincentyho inverzní úloha) na zvoleném modelu, výchozí je `Wgs84` — tedy tentýž, se kterým
  pracuje `GeoReference` i fúze. Pro `a == b` se vzorec sám degeneruje na obyčejný great-circle,
  takže koule zůstává dostupná (`Ellipsoid.Sphere`, nebo `new Ellipsoid(r, r)` pro přesně původní
  chování). `LLA.Distance(Ellipsoid, …)` na `GreatCircle` deleguje — dřív měla vlastní haversine
  s poloměrem `SemiMajorAxis`, takže „WGS84" tam znamenalo kouli o rovníkovém poloměru.
- **Efekt:** délky hran v grafu teď sedí na metrický svět. Na syntetickém koridoru vychází
  `graf 9.800 / ENU 9.800` a `10.000 / 10.000` (dřív `9.770` a `9.969`). Test konzistence
  `GeoReferenceTests.LocalDistance_MatchesGreatCircle` šlo zpřísnit z „do 0,1 %" na 1 mm.
  Nových 10 testů v `GreatCircleEllipsoidTests`; celá sada `ARBot.Common.Tests` (506) zelená.
- **Kam se nešlo a proč:** `LLA.ProjectOntoSegment` dál počítá na střední kouli. Měřítko se při
  projekci na úsečku vykrátí, takže přesnější poloměry nic nezpřesní — jen posunou poslední bity
  vráceného bodu, a na těch visí degenerovaný split cílové hrany (cíl přesně v uzlu). Pokus to
  „taky sjednotit" shodil regresní test `GoalFieldSplitTests.DeadEndGoal_RobotOnGoalSegment_FiniteCost`,
  tak jsem to vrátil a nechal u toho poznámku v kódu. Podrobně v [decisions.md](decisions.md).
- **`Ellipsoid.Eccentricity` → `Flattening`.** Ta vlastnost počítá `1 − b/a`, což je **zploštění**
  (WGS84 ≈ 0,00335), ne excentricita (`e = sqrt(1 − b²/a²)` ≈ 0,0818) — jméno lhalo o řád.
  Nikde se nepoužívala, takže přejmenování bylo bez rizika; `EccentricitySquared` (`e² = 1 − b²/a²`)
  je naopak správně a zůstává. `GreatCircle` teď bere `f` odtud místo vlastního přepočtu.

- **Replay (`ReplayNavTool`): kompaktnější ovládání, víc místa pro grid.** Tři řádky nad sebou
  (textová pozice / posuvník / tlačítka) se složily do **jednoho**: `poradi/celkem`, roztahovací
  posuvník a tlačítka vpravo. **Play a Pauza je jedno přepínací tlačítko** (`TogglePlay`,
  popisek z `PlayPauseText`) — třetí stav neexistuje, takže dvě tlačítka byla zbytečná.
  Textová pozice ukazuje **jen `poradi/celkem`**; typ zprávy a čas z ní zmizely, protože jsou
  vidět na vybraném řádku gridu a jen braly místo. V gridu je **čas jako druhý sloupec** hned za
  `Seq` (pořadí sloupců: Seq, Čas, Typ, Jméno). Ušetřily se dva řádky výšky.
- **Replay: skok na předchozí/následující zprávu téhož proudu** (`◀◀ Stejná` / `Stejná ▶▶`).
  „Stejná" znamená shodnou dvojici **`(MsgName, Name)`** — tedy tutéž identitu proudu, jakou
  používá `FileMessageSource.SeekTo` při rekonstrukci stavu. Díky tomu krokování drží *jednu*
  kameru a nepřepíná se mezi levou a pravou. Když už tím směrem žádná taková zpráva není,
  tlačítko nedělá nic (zůstane na místě). **Ověřeno nad záznamem `20260816-134136.rec`**
  (27 246 zpráv): z `5428 MotorStateBase` dvě stisknutí → `5432` → `5434`, obojí `MotorStateBase`,
  s přeskočením proložených `IMUState` / `RobotStateMsg` / `DriveCommandMsg`; zpět na `5432`.
  Při ověření se ukázalo, že sloupec Čas byl úzký a `HH:mm:ss.fff` přetékal do typu — rozšířen.

- **ImageDocument: hodnota pixelu pod kurzorem.** Při ukazování myší do panelu se ve spodním
  informačním boxu objeví třetí řádek `[x,y] <podklad> | <overlay>` — tedy hodnota ze **stejného
  místa v obou vrstvách naráz** (RGB proti pravděpodobnosti sjízdnosti, hloubka v mm). Přepočet
  pozice na pixel dělá View, protože závisí na `Stretch="Uniform"` (obraz je vycentrovaný
  a olemovaný prázdnem); ViewModel jen čte z `registry`, tedy z téhož zdroje, ze kterého se panel
  kreslí. Grid sjízdnosti se rasterizuje zvlášť a v registry není, u něj se hodnota nehlásí.
  **Ověřeno:** vozovka `RGB 131,125,132 | p 251`, tráva `RGB 61,138,62 | p 0` — sedí na čísla
  z diagnostického testu (`probability vozovky = 254`, `travy = 0`). Hodí se rovnou na ladění
  sémantického kanálu occupancy gridu (viz níže).

- **Tři drobnosti v UI** (hlášené autorem, všechny ověřené za běhu nad virtuálním HW):
  - **Overlay nad pravou kamerou ukazoval `left/probability`.** `AutoSelect` dával *tutéž*
    probability vrstvu do obou overlayů (bral první, co dorazila). Nově se overlay páruje
    s **kamerou svého panelu** (`EnsureDefaultOverlays` + `FindOverlayFor`) — a protože pořadí
    příchodu vrstev není zaručené, dělá se to až po zpracování snímku, ne při objevení vrstvy.
    Při ověření se ukázala i druhá polovina problému: **panely samotné se přiřazovaly podle pořadí
    příchodu**, takže vlevo klidně byla pravá kamera. Teď rozhoduje jméno kamery (`AssignBaseLayer`).
  - **Panel senzorů je po startu sbalený** do auto-hide proužku na levé hraně (`PinDockable`
    v konstruktoru `MainWindowViewModel`, až po `InitLayout` — pinování pracuje se živým stromem)
    a má **pevnou šířku 300 px**. Šířku řídí dvě různé věci podle stavu: připnutý panel
    `SetPinnedBounds` (v pixelech), rozbalený `Proportion` doku — a ta se navíc normalizuje mezi
    sourozenci, takže není podílem šířky okna (změřeno na okně 1424 px: 0,35 → 370 px,
    0,50 → 475 px). Proporce je zkalibrovaná tak, aby rozbalený panel vyšel stejně široký jako
    vysunutý proužek, jinak by při odepnutí poskočil. Obě hodnoty jsou konstanty v `DockFactory`;
    při té příležitosti zmizelo natvrdo psané `0.25` v `ReopenTool`, kvůli kterému měl panel po
    znovuotevření z menu jinou šířku než po startu.
  - **World view otevřený před Startem poskakoval.** Příčina: Mapsui navigační volání
    (`CenterOnAndZoomTo`, `ZoomToBox`) při nepřipraveném viewportu **nezahodí ani nevyhodí výjimku,
    ale odloží si ho** a přehraje později (`Executing postponed call …`). Zařadila se tak dvě
    odložená volání za sebou a po připojení viewportu se provedla hned po sobě; `initialCentered`
    se navíc nastavil už při odložení, takže pojistka „centruj jen jednou" neplatila. Nově se
    necentruje, dokud `ViewportReady()` nehlásí nenulový viewport, a centrování na mapu se zkouší
    i v dalších cyklech (mapa přijde jen jednou). **Ověřeno:** v Debug outputu je po opravě
    `Executing postponed call` **0×** (dřív 2×: `ZoomToBox` + `CenterOn`).

- **Syntetická testovací mapa `OSM/SyntetickyKoridor.osm`** (zadání autora): koridor 10 m na západ
  (šířka 3 m, úsek A) → pravoúhlý zlom na sever 2,5 m (šířka 2 m, B) → zlom na západ 3 m
  (šířka 1 m, C) → 10 m, ve kterých se rozšíří zpět na 3 m (D). Z konce úseku B pokračuje ještě
  10 m na sever, šířka 2 m (E) — z uzlu 5 je tedy **křižovatka tvaru T**. Slouží k testu průjezdu
  zúžením, pravoúhlými rohy a odbočení z průchozího koridoru do úzké boční větve.
  *(Zadání vznikalo ve třech krocích — severní úsek nejdřív 1 m, pak 2,5 m, nakonec přibyla
  větev E; mapa se pokaždé přegenerovala, ne ručně přepsala.)*
- **Co si vyžádalo pozornost:** šířka v OsmNav **není vlastností úseku, ale uzlu** — uzel dostane
  maximum ze šířek cest, které jím vedou, a mezi uzly se lineárně interpoluje
  (`GraphBuilder.cs`, `RoadScene.cs`). Naivní mapa se 4 uzly by proto 1m zúžení vůbec neměla:
  rohové uzly by převzaly širší hodnotu a koridor by se jen plynule přeléval. Řešeno tagem
  `width` **na každém uzlu** (ten má přednost před cestami, takže mapa vypadá stejně bez ohledu
  na výchozí šířku, která se navíc liší podle způsobu načtení — 2,0 m ve World view vs. 3,0 m
  u `map=`) a uzly 0,2 m před rohy, aby si úseky udržely šířku po celé délce.
- **Na křižovatce (uzel 5) to platí obráceně** než v rohu: nese šířku **průchozího** koridoru
  B→E (2 m), ne nejužší větve. S 1 m podle odbočky C by robot jedoucí z B na sever do E našel
  na křižovatce falešné zúžení, které tam fyzicky není. Zúžení na 1 m proto začíná až uzlem 8,
  0,2 m západně od křižovatky.
- **Rozhodnutí u poslední části:** „rozšíří se na 3 m ve délce 10 m" je čteno jako **nálevka** —
  šířka roste rovnoměrně po celých 10 m (uzel 1 m → uzel 3 m). Kdyby šlo o skokové rozšíření
  a rovný 10m úsek, stačí změnit `width` uzlu 6 na 3 a přidat uzel 0,2 m za ním — popsáno
  v komentáři přímo v souboru.
- **Ověřeno:** souřadnice vyrobeny přímo přes `GeoReference.ToLLA` (přesná inverze převodu, který
  aplikace používá) a mapa načtena zpět reálným `OsmXmlReader` + `GraphBuilder`: v lokálním ENU
  vycházejí vzdálenosti 9,800 / 0,200 / 2,300 / 0,200 / 3,000 / 10,000 m a šířky 3/3/2/2/1/1/3 m
  přesně. Mapa načtena i v aplikaci (`virtualhw=true map=…`) — World view koridor vykreslil.
- **Vedlejší zjištění:** `GreatCircle` počítal s koulí R = 6371000 m, zatímco `GeoReference`
  s elipsoidem WGS84, takže délky hran v grafu vycházely ve směru východ–západ na naší šířce
  asi o 0,3 % kratší (10,000 m v ENU = 9,969 m v grafu). Týkalo se to všech map stejně, včetně
  reálných OSM dat. **Opraveno tentýž den — viz níže.**

## 2026-08-14

- **Volba hardwaru v menu + čistý šev `ARBotHW`.** Návrh autora: po startu aplikace neběží žádný HW,
  v menu jde přepnout Reálný/Virtuální. Vyšlo to z pozorování, že po přechodu Run → View zůstaly
  viset virtuální kamery.
- **Nalezená příčina toho pozorování:** šev byl **jednosměrný**. `SetRealHW` zakládal kamery jen
  `if (LeftCamera == null)`, ale po `SetVirtualHW` ta pole null nejsou — skutečné kamery se tedy už
  nikdy nevrátily a virtuální běžely dál (renderovaly na pozadí i ve View).
- **Hotovo:** `HwMode` (None/Real/Virtual) + `SetNoHW()`, který uvolní kamery **i UART porty**
  (bez toho by je `SetRealHW` nemohl znovu otevřít). `SetRealHW`/`SetVirtualHW` ho volají na začátku,
  takže přepnutí je čisté v obou směrech. `Init()` už HW nezakládá — jen zjistí porty. Založení řídí
  `ARBotRuntime.RequestedHwMode` (výchozí `Real`, s `virtualhw=true` pak `Virtual`) až v `Start(Run)`;
  `Start(View)` HW uvolní. Menu `Runtime → Hardware` (radio, aktivní jen se zastaveným runtime).
- **Rozhodnutí:** samostatný parametr `hw=` se nezavádí — `virtualhw=true` stačí a „žádný HW" je
  stav po startu, ne volba parametru. A **bez mapy virtuální HW nespadne na reálný**, ale zůstane
  `None`: při žádosti o simulaci se nesmí nečekaně rozjet skutečné kamery.
- **Hotovo (World view):** (a) ve View se počátek ENU dopočítá ze **zaznamenané `MapMsg`** stejným
  pravidlem jako `BuildOriginFromMap`. Bez toho `MapOrigin == null` → záložní varianta, ve které
  platí `origin + póza == GPS`, takže kreslená poloha degenerovala na surový fix a stopa se rozskákala
  (projevilo se jen na čerstvě puštěné aplikaci — po Run tam `MapOrigin` z předchozího běhu zůstal).
  (b) `ARBotRuntime.SessionId` se zvyšuje při každém `Start` a `WorldViewDocument` podle něj zahodí
  akumulovaný stav — jinak se záznam kreslil přes stopu z předchozího běhu.
- **Ověřeno:** `ARBot.Common.Tests` 481 ✓, `ARBot.HAL.Tests` 30 ✓, aplikace se přeloží pro `x64`
  i `OrangePI`. **Neověřeno:** cokoliv se skutečnými kamerami (na vývojovém stroji nejsou) — tedy
  `SetRealHW` po `SetNoHW`, znovuotevření UART portů a chování menu za běhu.
- **Odkazy:** `Src/ARBot/Robot/{ARBotHW,ARBotRuntime}.cs`,
  `Src/ARBot/ViewModels/{MainWindowViewModel,WorldViewDocument}.cs`,
  `Src/ARBot/Views/MainWindow.axaml`, [doc/virtual-hw.md](virtual-hw.md).
- **Debugovací výstup teče do záznamu (`Trace` → zpráva `Info`).** Návrh autora: napojit
  `Trace.Listeners` na existující zprávu `Info`, aby šlo pustit reálnou aplikaci (i na HW), a pak si
  debug hlášky přečíst z nahrávky — místo posílání výpisů z okna Debug output ručně. Zkracuje to
  ladicí cyklus na jeden běh.
- **Hotovo:** `Info` povýšena na **verzi 2** (přibyl čas, oblast a úroveň; `FromData` se větví podle
  `Verze`, takže staré záznamy se čtou dál). Nový `TraceInfoBridge` (`MessageProcessor` s frontou
  `DropOldest` a vlastním vláknem) + `TraceLogContext` (thread-static obálka, kterou
  `FilteredTraceLogSink` doplní oblast/úroveň Avalonie). Zapojení v `ARBotRuntime` (Start/Stop).
  Čtení bez GUI: `[Explicit]` nástroj `RecordingDumpTest` (cesta v `ARBOT_RECORD`).
- **Filtruje se až při čtení, ne na vstupu** — do proudu jde všechno včetně hlášek Avalonie/Mapsui,
  aby se nic neztratilo; oblast a úroveň slouží k filtrování nad záznamem.
- **Dvě věci, které test odhalil a bez kterých by to nefungovalo:**
  - **Smyčka log → `Info` → odběratel → log.** Thread-static potlačení pokryje jen synchronní
    odběratele; ten s vlastní frontou loguje z jiného vlákna. Bez tvrdého stropu `MaxPerSecond`
    vyrobil test **přes 24 000 zpráv za 200 ms**. Strop navrhované chování „nic nezahazovat" trochu
    porušuje, ale bez něj se to nedá pustit.
  - **`.gitignore` má `[Ll]ogs/`**, takže nová složka `Src/ARBot.Common.Tests/Logs/` byla tiše
    ignorovaná a testy by se nikdy nedostaly do commitu (existující `Src/ARBot.Common/Logs/` přežívá
    jen díky už sledovaným souborům). Testy přesunuty do `.../Diagnostics/`.
- **Vedlejší nález:** `LocalNavigatorTest.PrekazkaNaRozjeteDraze_ZpusobiNouzoveZastaveni` padal —
  **není to regrese téhle práce**, ale důsledek změny `Profile.MaxDecceleration` 0,30 → 0,50
  z předchozího commitu (ta, co v commit message figuruje jako „nevznikla v tomto sezení a není
  pokryta testy"). `PathCollides` kontroluje dráhu jen na vzdálenost závazku
  `v²/(2·a) + v·Ts + rozlišení`; při v = 0,5 m/s to je 0,52 m pro deceleraci 0,30, ale jen 0,35 m
  pro 0,50 — a zeď měl test natvrdo v 0,8 m, kde odstup klesá pod `SafeDist` až od 0,4 m. Chování je
  správné (silnější brzda = kratší závazek = kratší dohled), brittle byl test; zeď přesunuta na 0,5 m,
  aby seděla pro obě hodnoty, s vysvětlením v komentáři.
- **Ověřeno:** `ARBot.Common.Tests` 481 ✓ (9 nových), `ARBot.HAL.Tests` 30 ✓, aplikace se přeloží.
  **Neověřeno:** běh v aplikaci a skutečné čtení záznamu z reálného běhu.
- **Odkazy:** `Src/ARBot.Common/Logs/{Info,TraceInfoBridge,TraceLogContext}.cs`,
  `Src/ARBot/{FilteredTraceLogSink.cs,Robot/ARBotRuntime.cs}`,
  `Src/ARBot.Common.Tests/Diagnostics/*`, [doc/record-replay.md](record-replay.md).
- **Vyšetřeno: „occupancy grid přichází prázdný".** Hlášení znělo, že v `OccupancyGridMsg` jsou
  pole `Occ` a `Road` samé nuly, i když kamery evidentně cestu vidí. Postup byl shora dolů po
  řetězu, ne hádáním:
  1. `logs/traversability-timing-*.csv` z běhu ukázal `cells=1680` u každého snímku → **polární
     grid se počítá**, vstup do agregace existuje.
  2. Napsán offline test celého runtime řetězu (viz níže) → **agregace i převod na zprávu jsou
     v pořádku**, chyba tedy nebyla v `ARBot.Common/Occupancy`.
  3. Diagnostika přidaná do `OccupancyIntegrator`/`LocalNavigator` z běžící aplikace potvrdila,
     že se grid **plní i za běhu** (`touched=9470`, `occ=8619`, `road=3771`, `drop=0`).
- **Dvě skutečné příčiny (obě opraveny / vysvětleny):**
  - **Vrstva „Lokální mapa" se nevykreslovala.** `occupancyLayer` je `MemoryLayer`, který má
    ve výchozím stavu `Style = VectorStyle`, ale plní se `RasterFeature` (PNG rastr). Mapsui to
    jen zalogovalo (`VectorStyleRenderer can not render feature of type 'Mapsui.Layers.RasterFeature'`
    — ten WARN byl v Debug outputu celou dobu vidět) a vrstva zůstala neviditelná. Opraveno
    explicitním `Style = new RasterStyle()`. *(Pozn.: `RasterStyle` v Mapsui 5.1.0 existuje, jen
    není v XML dokumentaci balíčku — proto se hůř hledal.)*
  - **„Samé nuly" v debuggeru byl klamný pohled.** Zapsané buňky leží v kuželu **před** robotem,
    tj. uprostřed pole; první stovky prvků `Occ`/`Road` jsou jihozápadní roh gridu, kam kamera
    nevidí. V měření je první nenulový prvek až na indexu **5239 z 65536** — pohled na začátek
    pole v debuggeru tedy ukáže nuly zcela právem. Zadokumentováno testem, ať to příště nikoho
    nesvede.
- **Hotovo:** diagnostika `OccupancyIntegrator.IntegrateStats` (`LastStats`) — počitadla podél celé
  cesty zápisu (chybějící projekce → buňky mimo zorné pole → azimut/prstenec → samé `Unknown` →
  stín → zápis), vypisovaná z `LocalNavigator` do Debug outputu jednou za 2 s
  (`IntegrateStatsLogPeriod`). Když bude grid někdy opravdu prázdný, první nula v tom řádku rovnou
  řekne, který článek selhal.
- **Hotovo:** nový test `VirtualHwOccupancyTest` — celý řetěz `VirtualCamera` → `CameraFrameProcessor`
  → `OccupancyIntegrator` → `OccupancyGrid` → `ToLogMessage()` **bez GUI**, se stejnými montážními
  transformacemi z `Profile` a stejným přetypováním projekcí jako v `ARBotRuntime`; varianty
  managed/nativní transform a robot v počátku i desítky metrů od něj se záporným kurzem. Dosavadní
  `OccupancyIntegratorTest` používaly umělou projekci a robota v počátku, takže tuhle kombinaci
  (a hlavně *složení* dílů) nepokrývaly.
- **Ověřeno:** buildy x64 zelené; `ARBot.Common.Tests` 469 ✓, `ARBot.HAL.Tests` 28 ✓.
  **Neověřeno:** vykreslení vrstvy v běžící aplikaci po opravě stylu (nutné potvrdit okem) a
  celé chování na reálném HW.
- **Navazující nález (tentýž den): `CameraProjection.Transform` počítal posunutí kamery dvakrát.**
  Po zviditelnění vrstvy bylo vidět, že plocha **mimo cestu se neoznačuje jako nesjízdná**, i když
  probability je počítaná správně. Postup měření (vše offline, nad `RoadScene.IsRoad` jako ground truth):
  1. LUT `BackProject.RoadProbability` rozlišuje barvy scény správně (vozovka 128,128,128 → **254**,
     tráva 60,140,60 → **0**) — chyba tedy nebyla v klasifikaci barvy.
  2. Příčný profil: barva se překlápí na trávu až v `y ≈ 3,9 m`, ačkoli scéna má okraj v `y = 2,0 m`,
     a to **nezávisle na vzdálenosti** → translace, ne chyba perspektivy.
  3. Round-trip `pixel → zem → pixel` (renderer vs. `Transform`): chyba **~95 px**, blízké body
     `Transform` dokonce zahodil jako „mimo obraz".
- **Příčina:** `rotationWorld2Cam` je inverze **celé** transformace včetně translace (viz `SetOrientation`,
  kde se `M41..M43` před inverzí vrací zpět), a `Vector3.Transform` translaci matice uplatňuje. Ruční
  `x - offset.X, y - offset.Y, -offset.Z` ji tedy započítalo **podruhé**. Chyba je úměrná posunutí kamery
  (`Profile.LeftCameraOff`, výška 0,52 m) — proto se projevila hlavně v řádku obrazu.
  **Platí i pro reálný HW**, ne jen pro simulaci: `OccupancyIntegrator` přes `Transform` vzorkuje *oba*
  kanály a `PathEdgeFinder` jím promítá body cesty.
- **Proč to testy nechytly:** `PolarGridLookupTest` `CameraProjection.Transform` vůbec nevolá — má vlastní
  referenční `GroundToPixel`, a to pro kameru **bez postranního posunutí**, kde je dvojí odečet neškodný.
  Reálná metoda tak nikdy nebyla pokrytá s nenulovým offsetem.
- **Hotovo:** oprava v `CameraProjection.Transform` (původní řádky ponechány zakomentované do ověření
  na HW, viz CLAUDE.md) + dva regresní testy: `ProjekceTamZpet_JeInverzniKRenderu` (round-trip < 0,5 px;
  před opravou ~95 px) a `MimoCestu_JeZeSemantikyNesjizdne` (věcné očekávání nad ground truth scény).
  Měřeno: mimo cestu **647 správně / 0 špatně** (před opravou 741 vzorků, z toho 4 správně).
- **Třetí nález téhož dne: World view kreslil polohu ve dvou různých rámcích.** Z obrázku vypadalo, že
  lokální plán nevychází z robota, ale „z ideální pozice uprostřed cesty". Ve skutečnosti plán vycházel
  správně — z **fúzované pózy** (přes `BuildGeoReference()`, při načtené mapě pevný `MapOrigin`), zatímco
  **značka robota a trajektorie se kreslily ze surového GPS**. Rozestup byl přesně aktuální chyba fixu.
  Oranžové „klubko" v mapě nebyla dráha robota, ale stopa surových fixů — práh `MinTrackStepMeters`
  (0,5 m) propouští právě jen šumové výchylky. Značka navíc míchala zdroje: poloha z GPS, kurz z fúze.
  **Opraveno:** poloha i stopa jdou z fúzované pózy přes tentýž `GeoReference` jako plán a occupancy;
  surové fixy zůstávají jako samostatná vypínatelná vrstva **„Surové GPS"** (výchozí vypnuto) — rozestup
  od značky robota je teď čitelná diagnostika kvality fixu. Detail:
  [world-view.md → Jeden rámec pro všechna lokální data](world-view.md).
  **Neověřeno:** vzhled v běžící aplikaci (nutné potvrdit okem).
- **Drobnost k tomu:** žlutý cíl lokálního plánu neměl tooltip (modrá „mrkev" ve Značkách ho měla).
  Doplněn — popisy se nově drží ve dvou seznamech (`markerTips` pro Značky, `planTips` pro Lokální
  plán), protože se obě vrstvy přestavují nezávisle a jeden společný by si přepisovaly. `FindMarkerTip`
  navíc hledá **jen ve viditelných vrstvách** (dřív by popisek vyskočil i nad vypnutou vrstvou).
- **Čtvrtý nález: robot jede 0,1 m/s, i když je povoleno 1,2 m/s.** Příčinu se podařilo najít až
  měřením v běžící aplikaci — dvě mé hypotézy předtím padly (viz níže), obě proto, že jsem je stavěl
  na offline testu s jednou kamerou a stojícím robotem místo na reálném běhu.
  Postup byl „shora dolů" po stupních, každý stupeň s vlastním číslem z logu:
  1. occupancy grid: `free=3955 unknown=534 blocked=70` — mapa v pořádku;
  2. rychlostní obálka plánovače: `v=1,20 VClear=1,20 VBrake=1,20 freeAhead=5,3 m` — **nesráží**;
  3. `PathPlanner`: `vLimit[0]=1,20`, dráha rovná 6 m, `nejostrejsiRoh=0°` — **nesráží**;
  4. regulátor: `vCmd=0,95 -> v=0,05 m/s, beta=-11,9°, Trot=0,786 s, lookahead=0,15 m` — **zde**.
- **Příčina:** `IMotionProfile.SpeedLimit` váže dopřednou rychlost na dobu dorovnání rotace:
  `v ≤ d / (stability · T_rot)`, kde `stability = 4` a `d` je vzdálenost k lookahead bodu. Dosazeno:
  `0,15 / (4 · 0,786) = 0,048 m/s` — sedí na setinu.
  **Strukturální problém:** `d = max(LookaheadMin, LookaheadTime · v)` se počítá z AKTUÁLNÍ rychlosti,
  takže omezovač závisí na vlastním výstupu. Při nízké rychlosti je `d` zaražené na podlaze 0,15 m →
  strop zůstává nízký → rychlost nízká. Je to západka, ze které se soustava sama nedostane; aby při
  `T_rot = 0,786 s` vyšlo 1,2 m/s, musel by být lookahead 3,8 m.
  Druhý faktor je `MaxAllowedRotationSpeed = π/6` (jen **30°/s**) — proto trvá dorovnání pouhých 12°
  celých 0,79 s.
- **Opraveno (návrh autora):** cílem řízení je nově **nejbližší UZEL DRÁHY před robotem** — z něj se
  bere směr i vzdálenost, do které se váže dopředná rychlost. Dřív se mířilo na *virtuální* bod na
  ideální trase ve vzdálenosti `max(LookaheadMin, LookaheadTime·v)`; ten drží menší boční odchylku,
  ale počítá se z aktuální rychlosti, takže omezovač závisel na vlastním výstupu → západka.
  Autorova formulace: *„musím mířit na bod dráhy před sebou a zároveň uvažovat vzdálenost k němu;
  virtuální bod na ideální trase sice zmenší odchylku, ale blokuje rychlost — kde potřebuju přesný
  průjezd, nasekám waypointy blízko sebe."* Sedí to i s tím, že se dráha přeplánovává každý snímek
  z aktuální pózy, takže se boční odchylka vynuluje sama.
  **Nutný detail:** uzel blíž než `L_d` se musí přeskakovat — jinak je azimut k bodu „pod robotem"
  špatně podmíněný a vzdálenost jde k nule, což by robota zastavilo na každém uzlu.
  Zbývající délka celé dráhy by nešla: na dojezd už je brzdná obálka o krok dřív, byla by to duplicita.
  *(`distToNext` se v `Control` počítalo už předtím a nikde se nepoužívalo — původní návrh tedy
  nejspíš mířil sem.)* Původní varianta ponechána zakomentovaná do ověření na HW.
  Hlídají dva testy, oba ověřené tak, že bez opravy padají: `Straight_ReachesFullSpeed`
  (bez opravy **0,19 z 0,80 m/s**) a `ManyCollinearWaypoints_DoesNotStallAtEach` (bez přeskakování
  uzlů **0,62 z 0,80 m/s** a padají i rohové testy).
  **Neověřeno na HW ani v aplikaci.** Detail a co zbývá viz [path-following.md](path-following.md).
- **Hotovo (diagnostika, zůstává v repu):** `LocalNavigator` vypisuje do Debug outputu čtyři řádky —
  rozpad zápisu snímku (`IntegrateStats`), stavy buněk v koridoru **v rámci robota**, rozpad rychlostní
  obálky (`LocalPlanResult.MinFreeAheadM/MinVClear/MinVBrake` + `SpeedLimitedBy`) a rozpad posledního
  zásahu regulátoru (`PathResult.LastVCmd/LastSpeed/LastBeta/LastRotTime/LastLookahead`). Bez těchto
  čísel se problém hádal třikrát špatně; s nimi byl nalezený na jeden běh.
- **Poučení:** diagnostiku regulátoru je nutné číst z **odcházející** instance při výměně — ta nová
  ještě neřídila (napoprvé z toho byly samé nuly). A minimum přes uzly dráhy musí vynechat poslední
  uzel (tam je zastavení z definice) a inicializovat se z `PositiveInfinity`, ne z `MaxValue`
  (`+Inf < MaxValue` neplatí → vypsalo se `1,8e308`).
- **Otevřené (obojí zapsáno mezi úkoly, neřešeno — mimo rozsah tohoto ladění):**
  - `CameraProjection.TransformBack` vypadá na stejnou třídu chyby (v měření vracel `false` pro
    většinu pixelů a nesmyslné souřadnice pro zbytek); používá ho `TargetPoly` →
    [imu-and-frames.md → Otevřený úkol: ověřit `TransformBack`](imu-and-frames.md).
  - Okluzní pravidlo `InShadow` zahazuje většinu barevných vzorků (`shadow ≈ 5 200` z ~12 000) →
    [occupancy-and-local-planning.md → Otevřené úkoly](occupancy-and-local-planning.md).
- **Odkazy:** `Src/ARBot.Common/Occupancy/{OccupancyIntegrator,LocalNavigator}.cs`,
  `Src/ARBot.Common/Coordinates/CameraProjection.cs`, `Src/ARBot/ViewModels/WorldViewDocument.cs`,
  `Src/ARBot.HAL.Tests/VirtualHwOccupancyTest.cs`,
  [doc/occupancy-and-local-planning.md](occupancy-and-local-planning.md), [doc/world-view.md](world-view.md),
  [doc/imu-and-frames.md](imu-and-frames.md).

## 2026-08-13

- **Globální navigace, fáze 2 a 3** podle [global-navigation-runtime.md](global-navigation-runtime.md).
  Robot teď jede k cíli po OSM síti a trasa je vidět v mapě.
  - **`RouteCarrot`** — jádro celé vrstvy: mrkev je *poslední bod trasy uvnitř lokální mapy*,
    počítáno od průmětu robota k **prvnímu** výstupu. První výstup a ne poslední proto, že kdyby se
    trasa z mapy vynořila a zase vrátila, byl by pozdější kus s robotem nespojený a lokální plánovač
    by cíl na něm neobsloužil. Čistá funkce, 4 testy.
  - **`GlobalNavigator`** (`MessageProcessor`, odebírá jen `RobotStateMsg`, ne celý Stream) —
    póza → LLA → `Navigator.Update` → mrkev → `ILocalGoalSink.SetGoal`. Stavy `NoGoal`/`Driving`/
    `GoalInMap`/`Arrived`/`OffRoute`/`NoRoute`, nová `GlobalNavMsg` (v katalogu → do záznamu a do View).
    8 testů nad syntetickou sítí, bez gridu i bez HW.
  - **Trasa do `GraphNavigationMsg`** při změně trasy nebo jednou za `RouteMessagePeriod` — rozsvítí
    se vrstva „Trasa / graf". Ctrl+klik nově míří do globální vrstvy jako LLA (kus fáze 5 předtažený,
    jinak by nešlo fáze 2–3 vyzkoušet).
  - **`ILocalGoalSink`** dán do `ARBot.Common/Runtime`, ne do `Occupancy` — aby `OsmNav`
    a `Occupancy` na sobě nezávisely, jak návrh vyžaduje.
  - **Změny parametrů podle návrhu:** `LocalPlannerConfig.HorizonM` 6 → **25 m** (není to radius, ale
    délka dráhy; mrkev na okraji mapy je vzdušně 5,9 m, ale cesta k ní přes bludiště klidně 30 m),
    `NavigatorOptions.ArrivalRadiusMeters` 12 → **3 m**.
  - Past při psaní testů: síť je **edge-based**, takže přechody jsou odbočení a musí se registrovat
    `AddTurn` — bez nich trasa neexistuje. Testovací síť to zpočátku neměla a hlásila `NoRoute`.
- **Fáze 4 — detektory záseku a uzavírání hran.** Vrstva si teď sama zavírá cesty, které se ukážou
  jako neprůchozí.
  - **Potenciál φ** = `(1−t)·cena zbytku hrany + cost-to-goal`. Klesá i když robot překážku
    **objíždí** (pole je goal-rooted) — proti vzdušné vzdálenosti, která při objíždění roste,
    je to poctivá míra postupu.
  - **`ProgressWindow`** běží proti **ujeté dráze**, ne času: když robot stojí, okno se neposouvá
    a detektor bloudění se vůbec neuplatní (od stání je detektor A).
  - **A** (nehýbu se) je vypnutý pod `EmergencyStop` a bez platného plánu — jinak by každé zmáčknutí
    stopu za jízdy po 10 s vyrobilo falešný zásek a robot by začal zavírat hrany kvůli tomu, že u něj
    někdo stál. `DriveCommandMsg` nese `EmergencyStop` a chodí po `loop.Output`, takže to nechtělo
    žádné nové drátování.
  - **B** (bloudím) → soft penalizace (hrana jen zdraží), při opakování na téže hraně uzavření.
  - **C** (přehrazeno) → `CloseRoad` hrany **i reverzní** — fyzická zábrana blokuje oba směry.
  - Seznam uzavření je klíčovaný `(WayId, From, To)`, tedy **trvalou identitou** — `Edge.Index`
    platí jen pro jednu instanci sítě. TTL vrací uzavření na soft penalizaci, po `MaxClosures`
    je trvalé. Uzavřené hrany jdou do mapy jako `Collision`.
- **Neověřeno:** běh v aplikaci. Zbývá recovery manévr (couvnutí/otočka — neexistuje, takže A umí
  jen počkat a pak zavřít), průřez koridorem (4b) a ověření na HW (fáze 6).
- **Rebuild na čistém klonu + oprava `build_all.bat`.** Po smazání a novém klonu chyběla
  `NativeLib.dll`; postavena. Skript sám padal na `'cmake' is not recognized` — CMake není
  v systémové `PATH` (je jen ten z VS a přidá ho až `vcvars64.bat`, který skript nevolal) a druhá
  půlka hlásila chyby WSL, protože distro Ubuntu na stroji není. Nejhorší bylo, že skript nakonec
  vypsal „HOTOVO! Zkontrolujte složku /bin." i když nepostavil nic. Nově si VS najde přes `vswhere`,
  ARM64 část se s vysvětlením přeskočí a souhrn říká pravdu (`[OK]`/`[CHYBÍ]` + nenulový exit).
  Past při psaní: `wsl` u chybějící distribuce vrací **-1**, ale `if errorlevel 1` znamená „≥ 1",
  takže záporný kód propadne jako úspěch — všechny kontroly teď porovnávají s nulou.
  - **`RealSense 2.0/` je nově v gitu** → aplikace jde poprvé přeložit i pro `x64`.

- **Mapa se zobrazuje ve world view.** `WorldViewDocument` `MapMsg` uměl už dřív, chyběl druhý
  konec: runtime svou načtenou síť do Streamu neposílal. `ARBotRuntime.MapMessage` se publikuje
  na konci `WireRun` (kdy je připojený i záznam → mapa se přehraje ve View) a pohled otevřený
  až za běhu si ji vyzvedne z runtime (Stream zprávy nepřehrává).

- **Virtuální motory, GPS a IMU — uzavřená simulační smyčka.** Nad ground-truth modelem
  `SimulatedRobot` (`ARBot.Common/Simulation`) přibyly `VirtualMotors`, `VirtualGps` a `VirtualImu`
  v `ARBot.HAL`. Model je ideální + rampa zrychlení; motory jsou **přesná inverze odometrie**
  v `DefaultMeasurementMapper`, takže `omega`, které fúze spočítá, je to, které chtěl regulátor.
  Zapnutí `virtualhw=true` teď vymění i motory/GPS/IMU, start řeší `start=lat,lon[,kurz]`
  s fallbackem na přichycení k nejbližší hraně sítě. Detail: [virtual-hw.md](virtual-hw.md).
  - **Integrace pohybu:** koncová rychlost dělala z rampy dvojnásobnou dráhu, lichoběžník zase
    při velkém kroku rozmazal rychlou rampu přes celý interval → integruje se po 5 ms krocích.
  - **Ověřeno testem uzavřené smyčky** přes skutečný `AsyncFusionEngine`: po jízdě rovně
    i v oblouku je chyba odhadu polohy ~0,2 m a kurzu ~0,01 rad.

- **Opraveno: `FusionConfig.WheelBase` bylo natvrdo 0,5 m** proti profilovým `Profile.Rozchod`
  = 0,41 m a nikde se nesesouhlasovalo (přiřazení existovalo jen ve třech testech). Odometrická
  úhlová rychlost tím byla systematicky podhodnocená o 18 % — **i na reálném robotu**. Nově se
  bere z profilu, regresi hlídá `FusionConfigDefaultsTests`.

- **Opravený závod ve `VirtualMotors`:** baseline enkodéru držené v poli přepisoval
  `GetMeasurement` dřív, než báze stav zveřejnila, takže vyzvednutí v tom okně spárovalo přírůstek
  s časem jiného vzorku (projevovalo se jako občas padající test v plné sadě). Nově se baseline
  posouvá o přírůstek nesený přímo vyzvednutým stavem. **Stejnou strukturu má i `SDC2160Ex`.**

- **Opraveno: odometrie hlásila nulovou rychlost, když ji nikdo nevyzvedával.**
  `MotorStateBase.LeftWheelSpeed` se počítala jako `LeftEncoder / FramePickupPeriod`, jenže
  `FramePickupPeriod` závisí na `GetLastMeasurement()` — a ten v runtime nevolá nikdo,
  `MotorSource` jen odebírá událost. Bez otevřeného okna motorů tak do EKF teklo trvale
  `Velocity(0)` a `AngularRate(0)`; s otevřeným se rychlost počítala přes interval překreslování
  UI. **Týkalo se to i reálného robota.**
  - **Řešení (`MotorStateBase` verze 2):** rychlost kol je vlastní pole, které plní driver ze
    svého vzorkovacího intervalu, a enkodéry jsou kumulativní. Rychlost je tak vlastnost měření
    v jeho čase, ne vlastnost odběru, a mezi oběma cestami odběru nezůstal žádný sdílený stav.
    Upraveny `SDC2160Ex` i `SDC2160`. Zpětně: záznamy verze 1 se načtou, ale rychlosti v nich
    nejsou (enkodér je tam přírůstek a doba vyzvednutí se neserializovala).
  - Zajímavost: přesně takhle to dělal původní zakomentovaný driver `MD23` — odchýlil se až
    `SDC2160Ex`.

- **`start=` inicializuje EKF, ne jen simulaci.** Známá počáteční poloha jde přes
  `AsyncFusionEngine.InitializePosition` rovnou do filtru (kurz jako `HeadingMeasurement`) —
  **platí i pro reálný HW**, kde vím, kam jsem robota postavil. `start=gps` je výslovná volba
  „počkej na fix" (dosud tichý fallback), v simulaci nemá smysl a spadne zpět na přichycení
  k cestě. Bez zadání se na reálném HW nic nemění.

- **První běh se simulovaným HW** (screenshot uživatele): senzory hlásí OK, robot jede po mapě.
  Tři nálezy:
  - **Robot se kreslil otočený o 180°** — `RobotGlyph.Draw` převádí obrys z původní WPF konvence
    (osa Y dolů) přes `lym = -ly`, ale world view si ten výpočet rozkopíroval a převod v něm chyběl,
    přestože komentář tvrdil „shodne s RobotGlyph". Opraveno zavedením společné `RobotGlyph.ToWorld()`,
    kterou teď volají oba pohledy — duplikace byla příčinou, proč se to mohlo rozejít.
    **Netestováno** (žádný testovací projekt nereferencuje `ARBot`).
  - **„Trasa / graf" zůstává prázdná** — čeká na `GraphNavigationMsg`, kterou nikdo neemituje;
    globální navigace zatím neexistuje. Ctrl+klik nastavuje cíl *lokálního* plánovače, ten se kreslí
    do vrstvy „Lokální plán".
  - **Poskakování polohy** je očekávané: virtuální GPS sype σ = 1,5 m a `FusionConfig.GpsPosStd`
    je taky 1,5, takže filtr nepřehání důvěru — jen 1,5 m při 5 Hz je vidět, když robot skoro stojí.

- **Poznamenáno k dořešení:** diagnostika EKF do streamu a záznamu — viz
  [ekf-fusion.md](ekf-fusion.md) → „Otevřený úkol: diagnostika EKF do streamu a záznamu".
  Zpráva `MeasurementDiagMsg` k tomu už existuje i je v katalogu, jen ji nikdo neplní.

- **Ověřeno:** `ARBot.Common.Tests` 447/4, `ARBot.HAL.Tests` 23/1, aplikace se sestaví pro `x64`
  i `OrangePI`. **Neověřeno:** běh aplikace se simulovaným HW po opravě natočení robota.

## 2026-08-12

- **Zprovoznění repa na čistém počítači.** `NativeLib.dll` nešla vybuildit — `build_all.bat` hlásil
  „cmake is not recognized". Příčina nebyla v projektu: VS 2022 Community bylo nainstalované jen
  s .NET workloady, takže chyběl Windows SDK, `vcvarsall.bat` i C++ CMake tools (MSVC toolset
  14.44 tam paradoxně byl jako závislost, ale bez SDK je k ničemu). Po doinstalování komponent
  postavena `Src/NativeFuncs/bin/NativeLib.dll` dokumentovaným postupem (`vcvars64` →
  `cmake --preset windows-x64`). Druhá půlka `build_all.bat` (ARM64 `.so` přes WSL) padá — WSL
  distro Ubuntu na stroji není; pro běh na Windows není potřeba.
  - **Stále chybí složka `RealSense 2.0/`** v rootu repa (není v gitu, jako NativeLib). Bez ní se
    nesestaví `ARBot.HALWindows` → ani `ARBot` pro `x64`. Doplní se z jiného počítače.
  - Repo bylo vlastněné `BUILTIN\Administrators` (klon z elevated shellu) → `git` hlásil *dubious
    ownership*; srovnáno přes `safe.directory`.

- **Virtuální HW: `VirtualCamera` jako náhrada D435.** Nová simulace, která místo snímání renderuje
  RGB + hloubku z načtené OsmNav mapy a pózy robota — účel je vývoj vizuální cesty bez hardwaru
  a reprodukovatelné testy. Návrh i popis: [virtual-hw.md](virtual-hw.md).
  - **Rozvrstvení:** `RoadScene` + `SyntheticFrameRenderer` (čistá geometrie/rasterizace) v
    `ARBot.Common/Vision/Synthetic`, `VirtualCamera` (slupka `ICamera`) v **`ARBot.HAL`** — bez
    platformní závislosti a **bez Intel.RealSense**, takže jde postavit i tam, kde `HALWindows` ne.
  - **Klíčové rozhodnutí:** kamera si vyrobí syntetické pinhole intrinsics a **tutéž instanci
    `CameraProjection` použije k renderování i vrátí z `CreateProjector()`**. Rasterizace je psaná
    jako přesná inverze rozbalení ve `CameraFrameProcessor` (`Vector3.Transform(ray*d, Transformation)`),
    takže neshoda v hloubkové cestě je skutečná chyba, ne artefakt simulace. Hlídá to round-trip test.
  - **Model světa:** dvě vodorovné roviny (vozovka `z=0`, tráva `z=GrassHeight`), na pixel jeden
    paprsek, vyhrává bližší platný zásah — z toho vypadne i správná okluze hrany vozovky. Šum je
    čistá funkce `(seed, snímek, pixel, kanál)`, ne sekvence `Random` → snímek je bitově
    reprodukovatelný nezávisle na počtu vláken.
  - **Šev v `ARBotHW`:** `SetRealHW()` (default, volá se z `Init`) / `SetVirtualHW(VirtualHWOptions)`.
    `ARBotRuntime` ho volá **až za** vytvořením `AsyncFusionEngine`, takže `PoseAt = t =>
    engine.GetStateAt(t)` jde předat rovnou v opcích. Zapíná se `virtualhw=true` + `map=<cesta.osm>`;
    best-effort (chybějící mapa simulaci nezapne, nikdy neshodí start). Runtime nově drží síť
    v `RoadNetwork`/`MapOrigin` — první krok k otevřenému úkolu z [osm-nav.md](osm-nav.md).
  - **`GeoReference` si kamera nezakládá** — dostane ji hotovou. Při té příležitosti zjištěno, že
    `FusionConfig.GeoReference` je dnes **deklarované, ale nezadrátované** (nikdo ho nečte ani
    nenastavuje; komentář slibuje GPS adapter, který neexistuje). Runtime ho při zapnutí simulace
    naplní, aby mapa i fúze počítaly od stejného počátku.
- **Nález k ladění vize:** klasifikátor polárního gridu povoluje `MaxHeightDev(r) = 0,03 + 0,02·r`,
  takže **výchozí tráva 0,10 m je překážkou jen do ~3,5 m**; při 0,25 m je nesjízdná v celém dosahu
  gridu. Není to chyba geometrie (round-trip sedí) — je to vlastnost klasifikátoru, viz
  [virtual-hw.md](virtual-hw.md).
- **Ověřeno:** `ARBot.Common.Tests` 422 prošlo / 4 přeskočeno, `ARBot.HAL.Tests` 15 / 1, obojí `x64`.
  Celá aplikace se sestaví pro `OrangePI` (tj. i drátování se překládá). **Neověřeno:** běh aplikace
  se simulovanými kamerami a `x64` build — čeká na `RealSense 2.0/`.
- **Rozpracováno / další krok:** sjednotit mapu s `WorldViewDocument` (UI si ji pořád načítá vlastní
  cestou); drsnost trávy hashovat podle světové polohy místo pixelu (dnes mezi snímky „bliká");
  virtuální GPS a IMU do stejného ševu.

- **`RealSense 2.0/` doplněna do gitu** (řeší „stále chybí složka" výše — na čistém stroji už není
  co dohledávat z jiného počítače). Složka nebyla ignorovaná omylem: build-output pravidla `x64/` /
  `x86/` v `.gitignore` chytala i její podadresáře. Doplněny negace `!RealSense 2.0/x64/` a `x86/`.
  - **Do gitu jen DLL** (`Intel.Realsense.dll` + `realsense2.dll` pro obě platformy, ~56 MB) — to je
    vše, co `ARBot.HALWindows.csproj` referencuje. Doprovodné `*.pdb` (~200 MB) zůstávají mimo git:
    jsou to debug symboly nativní knihovny, k buildu ani běhu nepotřebné, a `x64/realsense2.pdb`
    má 104 MB, tedy nad 100MB limitem GitHubu — přes LFS by se to protlačit dalo, ale za cenu
    čtvrtiny LFS kvóty a povinného `git-lfs` při klonování (i na OrangePI). Vyloučí je pravidlo
    `*.pdb`, které v `.gitignore` už bylo.
  - Odkazy: [build-and-platforms.md](build-and-platforms.md) (sekce *Externí závislosti*), `.gitignore`.

- **Implementována fáze 0 mise: nouzové zastavení v `ControlLoop`** (zadání:
  [robotour-mission.md](robotour-mission.md)). Smyčka nově odebírá i `IMotorState` (`MotorStateBase` je
  `IPrimaryMessage`, takže do `Consume` už tekla — jen se zahazovala) a při `IsEmergencyStop` posílá
  `Drive(0, stojí ? 0 : rotace)` podle pravidla „rotaci nuluj až ve stoje". `DriveCommandMsg` dostal
  příznak `EmergencyStop` (**FormatVersion 1 → 2**; v1 záznamy se čtou dál, příznak zůstane `false`),
  aby v záznamu bylo vidět *proč* byla nula; přidána diagnostická property `ControlLoop.LastMotorState`.
  Odometrie se pod stopem do fúze **nepouští**.
- **Implementována fáze 0 globální navigace: GPS a odometrie do EKF** (zadání:
  [global-navigation-runtime.md](global-navigation-runtime.md)).
  - `AsyncFusionEngine.InitializePosition(x, y, std, t)` + `IsPositionInitialized` + vystavená
    `GeoReference`. Inicializace navíc **vynuluje korelace polohy se zbytkem stavu** a **zahodí měření
    starší než `t`** (novější přepočítá z nového základu).
  - `DefaultMeasurementMapper`: `GPSState` → `PositionMeasurement` (+ `GPS/speed` nad `GpsMinSpeed`),
    odometrie → `Odo/speed` a `Odo/rate` (`v = (vL+vR)/2`, `ω = (vR−vL)/rozchod`). **První použitelný
    fix polohu inicializuje**, další už jen korigují.
  - Načtení mapy **osamostatněno od `virtualhw`**: `map=<cesta.osm>` nastaví `RoadNetwork` + `MapOrigin`
    a založí z něj `FusionConfig.GeoReference` — počátek daný mapou potřebuje i reálný běh, ne jen
    simulace (a je stejný napříč běhy i záznamy). `virtualhw=true` už jen vymění kamery. Mapper i model
    dostávají **tutéž** instanci `FusionConfig`, jinak by se referenční bod rozešel.
- **Dvě věci našly testy, ne úvaha:**
  1. **`GPSState` je ve STUPNÍCH** (u-blox posílá `1e-7 deg`), `LLA` v radiánech. První verze mapperu
     stupně předávala jako radiány — přesně ta tichá fatální chyba, o jaké si píšeme u
     `InvariantCulture`. Hlídá to test `Gps_IsInterpretedAsDegrees_NotRadians` (fix v počátku roviny
     musí dát lokální `[0,0]`).
  2. **Odůvodnění inicializace polohy v návrhu bylo nepřesné.** Tvrdilo, že vzdálený první fix „gating
     zahodí" — gating se ale uplatní **jen když má měření nastavený `GateThreshold`**, což dnes nikdo
     nedělá. Skutečnost: dnes se fix *přijme*, ale `K = P/(P+R) ≈ 0,31`, takže se stav k pravdě plazí
     sekundy a mezitím se do gridu zapisují pózy stovky metrů mimo; **a jakmile se prahy zapnou, fix se
     opravdu zahodí a filtr robota nenajde nikdy.** Inicializace je potřeba v obou světech. Oba scénáře
     mají po testu a dokument je opravený.
- **Zapsán otevřený úkol: znaménko rotace ověřit na zařízení** — [path-following.md](path-following.md)
  („Převod ω → `dif`"), s odkazem z komentáře v `ControlLoop.OnTick`. Nesrovnalost je jen papírová:
  `rotationSpeed` je +CCW (vlevo), ale `Drive` dokumentuje `dif>0` jako pravé otáčení a `SDC2160Ex`
  ještě posílá `−CalcSpeed(dif)`; výsledek závisí i na tom, které kolo je motor 1. Z kódu se to
  rozhodnout **nedá**. **Autorův odhad: komentář `dif>0 = vpravo` je správný a nesrovnalost je jen
  zdánlivá** (předchozí generace jela s `+ω·Rozchod/2` bez přehození a fungovala). Zkouška je jedna a
  rozhodne obojí: zadat malé `+ω` při nulové rychlosti, vidět kam se robot otočí, a týmž pokusem
  porovnat odometrické `ω` proti `IMUState.AngularVelocity.Z`. Naslepo se to opravovat nemá — je to
  příkazová cesta a otočené znaménko znamená zatáčení od dráhy místo k ní.
- **Opraven faktor 2 v převodu ω → `dif`** (`ControlLoop`). Bylo `dif = rotationSpeed * wheelBase`,
  správně je **`/ 2`**: `dif` je **offset na kolo**, ne rozdíl rychlostí kol, takže
  `vR − vL = ω·rozchod = 2·dif`. Robot by tedy zatáčel **dvakrát rychleji, než regulátor chce**.
  Shodují se na tom tři nezávislé zdroje: (a) předchozí generace
  (`Drive(ReqSpeed, ReqRotationSpeed * Rozchod / 2)`), (b) `TrapezoidMotionProfile`, který používá
  `rozchod2 = rozchod/2` jako rameno pro převod ω ↔ rychlost kola, (c) MicroBasic skript driveru
  (`motor1 = −(curSpeed+curRotSpeed)`, `motor2 = curSpeed−curRotSpeed` — dif se k jednomu kolu přičte
  a od druhého odečte). `ControlLoop` je jediné místo v repu, kde se ω na `dif` převádí. Přidán test
  `RotationSpeed_ToDif_IsHalfWheelBase`; dva existující testy ten starý faktor **kódovaly**, takže
  byly upraveny — a doplněn komentář do `IMotorControl.Drive`, že `difSpeed` je offset na kolo.
  *(Nesahal jsem na **znaménko** — to je zvlášť a patří k ověření na zařízení.)*
- **Znaménko odometrického ω potvrzeno předchozí generací:** `(RightWheelSpeed − LeftWheelSpeed) / rozchod`,
  tedy přesně to, co je implementované (`OdoOmegaSign = +1`). Přepínač zůstává jen jako pojistka pro
  případ jiné polarity enkodérů; formulace v kódu opravena z „NEOVĚŘENO" na „shodné s předchozí generací".
- **Ověřeno:** `ARBot.Common.Tests` **454/458** pod x64 (4 přeskočené jsou původní; nově +16 fúzních
  testů, +4 testy nouzového zastavení, +1 test převodu ω→dif), `ARBot.HAL.Tests` 15/16, build `ARBot`
  (x64) i `ARBot.HALArmbian` (OrangePI) zeleno. **Na zařízení neověřeno nic** — GPS ani motory v běhu
  nebyly a upravený MicroBasic skript není nahraný.

## 2026-08-11

- **Occupancy + lokální plánování dotaženo do runtime.** Přidán `LocalNavigator` (vyšší řídicí smyčka
  jako `MessageProcessor` na vlastním vlákně): odebírá `CameraFrame` z `ControlLoop.Output`, pro **každý
  snímek zvlášť** si vyžádá pózu z EKF v čase jeho pořízení, zapíše do gridu, přepočte EDT, naplánuje
  a hotový `IRegulator` atomicky předá do `ControlLoop.Regulator`. Fronta `DropOldest` — když plánovač
  nestíhá, zpracuje se nejnovější snímek. Zapojeno v `ARBotRuntime.WireRun` (+ `BuildColorProjectionResolver`
  pro semantický kanál, `ARBotRuntime.Navigator` pro UI).
- **`AsyncFusionEngine.GetStateAt` vrací `null` mimo okno historie** místo tiché „nejlepší snahy"
  (bazový, až o sekundu starý stav). `ControlLoop` na `null` zastaví (bezpečný stav), `LocalNavigator`
  snímek zahodí. Hranice okna sama je uvnitř — dotaz přesně na `tBase` platí, jinak by první tik
  zbytečně zastavil (odhalil to existující test `OnTick_CallsDrive_AndEmitsDerivedMessages`).
- **Zprávy + vizualizace:** `OccupancyGridMsg` (oba kanály v lokálním pořadí, 2 Hz) a `LocalPlanMsg`
  (cíl, waypointy, stav, odstup, doba výpočtu) — obojí do záznamu, takže ve View jde zpětně vidět, co
  robot věděl a kudy chtěl jet. Vrstvy jsou ve **world pohledu** (viz revize níže), occupancy jako
  rastr (PNG → `MRaster`), plán jako čára + cíl; cíl se zadává **Ctrl + klikem** do mapy.
- **Revize po review — dvě opravy:** (a) vrstvy původně šly do robot-centrického pohledu, kde by se
  world-kotvená akumulovaná mapa s každou zatáčkou **otáčela**; přesunuty do world pohledu, kde leží
  pevně a sedí na podklad. (b) „plán bez dráhy regulátor nepřepisuje" byla **bezpečnostní díra** —
  mapa se mezitím změnila a na rozjeté trase už mohla být překážka, přičemž watchdog dobrzdí až za
  500 ms (+ ~1 m brzdné dráhy). Nově se rozjetá dráha každý cyklus ověřuje proti aktuální mapě a při
  kolizi v dosahu brzdné dráhy se řízení zahodí okamžitě (`AbortedCollision`). Rozhodnutí:
  [decisions.md 2026-08-11](decisions.md).
- **Testy chytily samy sebe:** tři testy navigátoru procházely **naprázdno** — pomocná metoda volala
  `MessageTarget.Stop()`, který frontu trvale uzavře (`TryComplete`), takže druhý „pump" v testu tiše
  nedělal nic a asserty typu „nic se nezměnilo" platily triviálně. Přepsáno na `Session` (start jednou,
  stop až na konci) + počítadla `ProcessedFrames`/`DroppedFrames`, na která se dá deterministicky čekat.
  Ověřeno i opačně: s vypnutou kolizní kontrolou test skutečně padá.
- **Zaznamenán otevřený úkol: Pitch/Roll patří do stavu EKF.** `ControlLoop.Consume` bere Roll/Pitch
  z „posledního došlého" `IMUState`, který **nenese identitu zdroje** (`IMUState` není `INamedMessage`),
  takže při dvou IMU (VN100 + T265) není poznat od kterého vzorek je a mezi tiky to může přeskakovat
  mezi čidly s jinou montáží a kvalitou. Navíc to obchází fúzi — bez gatingu, bez kovariance a bez
  dopředikování do času tiku, zatímco zbytek `RobotState` fúzovaný je. Návrh řešení + kontrola dopadu
  v [ekf-fusion.md](ekf-fusion.md); odkazy doplněny i do [imu-and-frames.md](imu-and-frames.md) a do
  XML komentářů u `ControlLoop.Consume` a `RobotState.Pitch`. **Neopraveno** (je to zásah do stavového
  vektoru filtru).
- **Ověřeno:** `ARBot.Common.Tests` **415/415** pod x64, build ARBot (x64) i HALArmbian (OrangePI) zeleno,
  self-test (`selftest=true st_seconds=8 no_uart=true`) potvrdil, že runtime s novým uzlem čistě
  nastartuje, vykresluje (59 renderů) a skončí. **Kamery nejsou namontované** → celý řetěz je
  odsimulovaný nad syntetickou kamerou; výkon na OrangePI zbývá změřit.
- **Globální oprava typu `Word` → `World`.** Historický překlep se táhl přes celou vizuální/geometrickou
  cestu (`WordPoints`, `WordPointsCount`, `WordObstaclePoints*`, `WordPoint`/`WordPoint2D`,
  `CameraToWordTransform`, `WordToWordTransform`, `rotationWord2Cam`) — 300 výskytů ve 28 souborech
  včetně `NativeFuncs` (`.cpp`/`.hpp`, komentáře v `.asm`/`.S`) a dvou `doc/*.md`. Přejmenováno jen
  tam, kde `Word` znamenalo *svět*; **nedotčeno** zůstalo 16bitové „word" v `MMR`/`IMMR`,
  `dword`/`word ptr` v assembleru, „odvození ve wordu" v `SimpleModel` (Word dokument) a `ThirdParty`.
  Jde o čistě jmenné přejmenování — `ComputeInfo` je `LayoutKind.Sequential` (jména polí ABI neovlivní)
  a binární formáty zpráv (`PathEdgeMsg`) se nemění. Stejným tahem opraven i překlep
  `Trasnformation` → `Transformation` (`IModelState`, `RobotState`, `ModelState`, `EKFModel3State`).
  **Ověřeno:** `ARBot.Common.Tests` 419/419 pod x64, build `ARBot.Common` i `ARBot` (x64) zeleno.
- **Návrh dalšího kroku: OsmNav v runtime + mise Robotour (zatím jen na papíře, žádný kód).** Rozdělen
  na dvě vrstvy nad `LocalNavigatorem` a zapsán do dvou nových dokumentů:
  [global-navigation-runtime.md](global-navigation-runtime.md) (`GlobalNavigator` — LLA cíl, trasa po
  OSM síti, metadata o postupu úseků, detekce záseku/bloudění/přehrazené cesty, uzavírání hran) a
  [robotour-mission.md](robotour-mission.md) (`MissionController` — depo → nakládka → vykládka → depo,
  čtení QR z pravé kamery). Klíčová rozhodnutí návrhu: **globální vrstva předává dolů „mrkev" na trase
  (~5 m), ne cílový bod** (vzdálený cíl si `LocalPathPlanner` promítá po přímce na hranici gridu, což by
  mířilo přes domy); **postup se měří jediným potenciálem** φ = zbytek hrany + cost-to-goal ze `GoalFieldu`
  (klesá i při objíždění, na rozdíl od vzdušné vzdálenosti); **tři oddělené detektory** (nehýbu se /
  nepostupuju / přehrazeno) s eskalací soft-penalizace → `CloseRoad` obou směrů + TTL; **overlay uzavření
  přežije změnu cíle**, takže návrat do depa nezajede do téže slepé uličky. Znovupoužívá se `MapMsg`
  a `GraphNavigationMsg` (world pohled je už kreslí).
- **Nález při analýze: GPS ani odometrie nejsou napojené na fúzi.** `DefaultMeasurementMapper` mapuje jen
  `IMUState` (kurz + gyro), `PositionMeasurement` nikdo nevyrábí a `FusionConfig.GeoReference` nikdo
  nenastavuje ani nečte (world pohled si ji sestavuje ad hoc z posledního fixu a `RobotStateMsg`).
  Stav EKF `[X, Y, θ, v, ω]` tedy nemá **žádné** měření polohy ani rychlosti — pro globální navigaci je
  to **blokující prerekvizita** (fáze 0 v novém dokumentu). Doplněno i do
  [ekf-fusion.md](ekf-fusion.md) souvislosti („zbývá: SensorAdapters").
- **Revize návrhu po diskusi (šest změn, všechny věcné):**
  1. **Mrkev jde až na okraj lokální mapy**, ne 5 m dopředu (poslední bod trasy uvnitř gridu, po
     *prvním* výstupu z něj) — jinak je lokální plánovač krátkozraký a v bludišti vjede do slepé
     odbočky, o které occupancy grid **už ví**. Důsledek: `LocalPlannerConfig.HorizonM` je **limit
     délky dráhy** (ne radius), takže z 6 m na **25 m** — cesta k bodu 6 m daleko může mít v bludišti
     30 m a plán by se utínal. Parametr `FinalApproachM` tím zanikl: „cíl uvnitř gridu ⇒ mrkev = cíl".
  2. **`RoadNetwork` je property `ARBotRuntime`** (+ `GlobalNavigator`); mapa se načítá **parametrem
     příkazové řádky `osm=`** (soutěž musí startovat bez klikání) nebo z UI, které o stavbu požádá
     runtime a jen odebírá `MapMsg`.
  3. **Návrat do depa je normální `SetGoal(depo)`**, nikoli `ClearGoal` — v textu se to pletlo
     s interním `GoalField.ClearGoal()`. `Cancel()` teď znamená výhradně „přestaň jezdit".
  4. **Počátek ENU roviny se bere ze OSM mapy** (střed bboxu), ne z prvního GPS fixu: je znám před
     fixem, je stejný napříč běhy i záznamy a nemůže se za běhu posunout. Důsledek pro EKF: první
     `PositionMeasurement` musí stav **inicializovat**, ne jen korigovat (jinak by grid skočil).
  5. **Čtení QR = handshake s nouzovým zastavením.** Robot dojede a stojí; obsluha zmáčkne
     nouzové zastavení, **teprve pak** se zapne scanner; přečtený cíl obsluha v UI **potvrdí**
     (s odvozenými souřadnicemi, vzdáleností a délkou trasy) a pak stop uvolní. Jeden opakovaně
     použitý podautomat „servisní okno" pro všechna tři zastavení. `IMotorState.IsEmergencyStop`
     **už existuje** a teče ve `MotorStateBase`, takže je to jen odběr zprávy. Zametání otočkou za
     kódem **zamítnuto** — pod nouzovým zastavením se robot nesmí hýbat a u něj stojí člověk. Nouzové
     zastavení za jízdy → `Paused` a po uvolnění se pokračuje k témuž cíli (ne `Aborted`).
  6. **Dekodér QR = ZBar** (binding `zbar-sharp` z ARBot2 → `Src/ThirdParty/ZBar/`), ne ZXing.Net:
     v předchozí generaci se osvědčil. Dvě povinné odchylky od původního kódu: **nepoužívat
     `System.Drawing`** (`ToBitmap()`/`Scan(System.Drawing.Image)` je na Armbianu nedostupné — místo
     toho surové **Y800** přes `ZBar.Image.Data`, což je i rychlejší) a zajistit `libzbar` na obou
     platformách (`DllImportResolver` kvůli `libzbar.so.0`). Formát kódu je `geo:` a parser se portuje
     1:1 včetně sufixů `n/s/e/w` a **`InvariantCulture`** (pod `cs-CZ` by `49.2103` → 492103; má na to
     test).
- **Druhá revize (upřesnění zadání):**
  1. **`GeoReference` se zakládá v rámci načtení mapy** (`ARBotRuntime` vyplní `FusionConfig.GeoReference`),
     takže první `PositionMeasurement` už ji má; fallback „z prvního fixu" zůstává pro běh bez mapy —
     přesně jak to `FusionConfig` už dnes popisuje.
  2. **Nalezen konkrétní důsledek počátku uprostřed mapy:** `EKFModel` startuje s `P0 = DenseIdentity(5)`,
     tedy σ = 1 m pro polohu, a gating je χ² (2 DOF, 0,95 → ≈ 6,0). První fix stovky metrů od nuly dá
     `NIS ≈ 2,7·10⁴` → `Reject` ho **zahodí** a filtr robota nikdy nenajde. Fáze 0 proto musí obsahovat
     **inicializaci stavu z prvního přijatého měření polohy** (nastavit `X, Y` a blok `P` na `R`,
     gating pro toto jedno měření vynechat). Latentní chyba už dnes — neprojeví se jen proto, že GPS
     do filtru nevstupuje vůbec.
  3. **Nouzové zastavení řeší `ControlLoop`, ne stavový automat mise:** drží si poslední
     `IsEmergencyStop` (`MotorStateBase` už do `Consume` teče, jen se zahazuje) a nuluje rychlost;
     **všechny ostatní smyčky běží dál**, takže watchdog nevyprší, plán je pořád aktuální a po uvolnění
     regulátor plynule pokračuje. Stav `Paused` z předchozí verze návrhu tím **zanikl** (a mapa se
     mezitím dál plní). Dopad: detektor záseku „nehýbu se" musí být pod stopem **vypnutý**, jinak by
     stání u nakládky vyrobilo falešný zásek a zavíralo hrany v mapě. Přidána fáze 0 mise — je to malý
     samostatný kus, užitečný i bez mise.
  4. **Čtení kódů potvrzeno:** dvě čtení — v depu (místo nakládky) a na místě nakládky (místo
     vykládky); na vykládce už se nečte nic a jede se na zapamatované depo.
- **Třetí revize (dotažení tří detailů):**
  1. **Inicializace polohy je funkce fúze, volaná misí:** `AsyncFusionEngine.InitializePosition(x, y, std, t)`
     + `IsPositionInitialized` místo „první měření se chová jinak". `MissionController` ve stavu
     `ArmingAtDepot` čeká na nepřerušeně kvalitní fix (`IsFixed`/`NumberOfSatellites`/`Hdop`) po
     `DepotFixSec`, fixy z okna **zprůměruje** (robot stojí) a **rozptyl** okna použije jako `std` i jako
     kontrolu kvality. Depo je tím nejpřesněji zaměřený bod mise — a je to jediný cíl, který nepřijde
     z QR kódu. Pro běh bez mise má `ARBotRuntime` hloupější fallback (první vyhovující fix).
  2. **`Arrived` stojí na póze z EKF a na toleranci `NavigatorOptions.ArrivalRadiusMeters`**, která se
     nastavuje podle toho, že **stanoviště je větší než chyba dojezdu** (12 → **3 m**). Zrušena
     podmínka „a musí stát" i ruční tlačítko „jsem na místě" z předchozí verze — obojí bylo řešení
     problému, který neexistuje. Zastavení na stanovišti je dvoufázové: `Cancel()` (řízené dobrzdění
     existující cestou přes watchdog, dráha se přitom pořád hlídá proti mapě) a `Regulator = null`
     teprve až robot stojí; tvrdá varianta zůstala pro `Aborted`.
  3. **Vzorec pro stop:** `Drive(0, aktuální rychlost == 0 ? 0 : požadovaná rotace)` — jedno pravidlo
     pro obě situace: dokud se kola točí, zůstává rotace (dobrzdění je **řízené**), a jak robot stojí,
     je poslední odeslaný příkaz `(0, 0)`, takže po uvolnění stopu není žádný transient. Tím padla moje
     otázka „nulovat i rotaci?" — odpověď je „ano, ale až ve stoje". Rychlost se bere z motorů
     ne z fúze; chybějící stav motorů = počítá se jako stojící.
  4. **Bez epsilonu — porovnává se na přesnou nulu.** `MotorStateBase.LeftWheelSpeed` je
     `LeftEncoder / FramePickupPeriod`, kde `LeftEncoder` je **přírůstek** enkodéru za rámec
     (`SDC2160Ex` posílá `leftEnc - lastLeftEnc`) — není to filtrovaná ani derivovaná hodnota, takže
     když se nepohnul ani tik, je to přesně 0,0. Motory jsou řízené pozičně ve zpětné vazbě, takže
     „nepohnul se ani tik" znamená „stojí". Parametr `EStopSpeedEps` tím zanikl.
  5. **Nález: řadič si nouzové zastavení ošetřuje sám** — MicroBasic skript v SDC2160 (v hlavičce
     `SDC2160Ex.cs`) při `di3=0` nuloval `reqSpeed` **i** `reqRotSpeed`; `curSpeed`/`curRotSpeed`
     k nule dojedou přes svou `acceleration`, tedy pozvolna (varianta s okamžitým `curSpeed=0` je
     záměrně zakomentovaná). Rotaci ale nuloval hned, takže dobrzdění bylo vždy „rovně" a hostitelské
     pravidlo by se k motorům nedostalo. Řadič je ten, kdo skutečně brzdí — hostitelská změna
     v `ControlLoop` drží konzistenci softwaru s realitou (`DriveCommandMsg`, žádný zastaralý příkaz,
     „stání není zásek") a funguje i pro `DummyMotors`/simulaci.
- **Změna firmwaru motorové jednotky (jediná dnešní změna kódu).** Skript v `SDC2160Ex.cs` upraven na
  totéž pravidlo jako host: `if di3=0 then reqSpeed=0; if curSpeed=0 or acceleration<=0 then reqRotSpeed=0`.
  `curSpeed=0` je dosažitelné **přesně** (celočíselná rampa končí clampem `curSpeed=1000*reqSpeed`);
  pojistka `acceleration<=0` uzavírá jedinou cestu k nekonečné otočce na místě (nenastavená `VAR 1` ⇒
  rampa nepostupuje ⇒ `curSpeed` nuly nikdy nedosáhne). **Watchdog (500 ms) zůstal záměrně jiný** —
  nuluje obě složky hned, protože u mrtvého hosta je poslední rotační příkaz zastaralý a slepé zatočení
  při dojezdu je horší než dojezd rovně. Předchozí varianta je ve skriptu zachovaná zakomentovaná
  (pravidlo „nemazat starou implementaci, dokud novou nepotvrdí ověření").
  **Ověřeno: build `ARBot.HAL` (x64) zeleno — nic víc ověřit nešlo.** Skript je *zdroj*, ne kompilovaný
  kód: do jednotky se nahrává zvlášť (Roborun+ / MicroBasic), takže **dokud se nenahraje, chování robota
  se nemění**. Na zařízení je nutné ověřit: stop rovně → zastaví rovně; stop v zatáčce → dotočí,
  zastaví a **netočí se na místě**; uvolnění → plynule pokračuje; zabitý host → obě složky na nulu.
- **Rozpracováno / další krok:** fáze 0 mise (nouzové zastavení v `ControlLoop` — malý samostatný kus)
  a fáze 0 globální navigace (GPS + odometrie do EKF, `GeoReference` z mapy, `InitializePosition`).

## 2026-08-10

- **Návrh occupancy gridu a lokálního plánování (zatím jen na papíře).** Probrán celý řetěz od
  `CameraFrame` po `RegulatorWayPoint[]`: kartézský grid 5 cm kotvený ve světě (kruhový buffer,
  posun bez rotace), **dva rovnocenné kanály** `LOcc` (z hloubky) + `LRoad` (z RGB) jako log-odds
  ve `sbyte`, distance transform pro odstupy, A\* s cenou = jízdní čas + čas otočení, string-pulling
  na waypointy s `MaxPositionError` = skutečná volná rezerva. Klíčové: „skrz neznámo se smí plánovat,
  ale nesmí se do něj vjet" se neřeší zvláštním pravidlem, ale invariantem *nejeď rychleji, než z čeho
  zastavíš na hranici potvrzeně průjezdného*. Hystereze plánu zamítnuta (držet plán nad starší mapou =
  riziko kolize); stabilita se řeší započtením otočení do ceny. Zadání implementace v novém
  [occupancy-and-local-planning.md](occupancy-and-local-planning.md) (9 fází), rozhodnutí v
  [decisions.md 2026-08-10](decisions.md). **Kód zatím žádný** — příští krok je fáze 1 (`OccupancyGrid`).
- **Implementováno algoritmické jádro occupancy + lokálního plánování** (`Src/ARBot.Common/Occupancy/`):
  `OccupancyGrid` (kruhový buffer, dva log-odds kanály v `sbyte`), `OccupancyIntegrator` (gather zápis
  obou kanálů z `CameraFrame`), `ClearanceField` (exaktní EDT Felzenszwalb–Huttenlocher),
  `LocalPathPlanner` (A\*, string-pulling → `RegulatorWayPoint[]`) + tři konfigurace. `Profile.PrefDist`
  = 0,8 m. `CameraFrame` **FormatVersion 3 → 4** (serializovaný popis projekce `CameraProjectionInfo`).
  Ověřeno: `ARBot.Common.Tests` **399/399** pod x64, build ARBot (x64) i HALArmbian (OrangePI) zeleno.
  Zbývá napojení na runtime (`LocalNavigator`, zprávy, vizualizace, cíl z UI) a **ověření výkonu na HW**.
- **Návrh se při implementaci opravil: azimutové hranice gridu jsou geometricky neproveditelné.**
  U sklopené kamery není sloupec obrazu konstantním azimutem — azimut pozemního bodu se na jednom
  sloupci mění s řádkem skoro o celou šířku buňky, takže jediná hodnota na hranici je systematicky
  špatná. Odhalil to test, který hranice měl ověřit. Místo nich se azimut hledá **projekcí bodu země
  do obrazu a odečtením sloupce**, což mapování z `BuildGrid` invertuje přesně (stejný vzor už
  používá `PathEdgeFinder`). Rozhodnutí: [decisions.md 2026-08-10](decisions.md).
- **Dvě chyby nalezené při implementaci (opraveny):** (a) `CameraProjection.Transform` promítal i body
  **za** kamerou — chybí kontrola `Z > 0`, perspektivní dělení záporným Z převrátí znaménka, takže bod
  4 m za robotem vyšel jako pixel před ním (latentní i pro `PathEdgeFinder`); (b) `CameraFramePool.CopyInto`
  nekopíroval nově přidané pole rámce, takže `Projection` se do záznamu nedostala — pool je na přidávání
  polí do `CameraFrame` systematicky náchylný, stojí za pozornost při každém dalším poli.
- **Oprava `CameraProjection` — záměna přetížení `ToDistort(int,int)` / `(float,float)`.** Dvě chyby
  najednou: (a) konstruktor plnil `toDistortCache` přes int přetížení, které četlo *právě plněnou,
  ještě prázdnou* cache → cache zůstala **celá nulová** a `UnDistort<T>(Image<T>)` vracel konstantní
  obraz; (b) větev „mimo rozsah" volala sama sebe (nekonečná rekurze) a navíc škálovala `Fx/PPx`
  podruhé. Bez živého dopadu — `UnDistort` nikdo mimo `CameraProjection` nevolá a hloubková cesta
  (`camera2DToCamera3DCache`) se plní přímým výpočtem. Nalezeno při přípravě serializace projekce do
  `CameraFrame`. Ověřeno: nové testy proti původnímu kódu selžou, s opravou projdou; `ARBot.Common.Tests`
  pod x64 **328/328**. Odkazy: `Src/ARBot.Common/Coordinates/CameraProjection.cs`,
  `Src/ARBot.Common.Tests/Common/CameraProjectionDistortTest.cs`.

## 2026-08-09

- **PathEdges do `CameraFrame` (oprava zahazovaného výpočtu).** Revize odvozených entit snímku odhalila,
  že `cu.PathEdges(...)` v `D435Camera` výsledek odjakživa zahazovalo a `PathEdgeFinder` (bez call-situ
  v runtime) si hrany počítal duplicitně sám. Výpočet přesunut do `CameraFrameProcessor` (volitelný
  `IComputeUnit`, **bez fallbacku**), výsledek nově v `CameraFrame.PathEdges` a serializuje se s rámcem
  (**FormatVersion 3**, čtecí větve v1/v2 zachovány). `PathEdgeFinder.Process` bere předem spočtené
  `Items[].Edges`; pooly (`CaptureFramePool`/`CameraFramePool`) hrany nulují/předávají referencí jako
  `Grid`. `ARBotRuntime` dává procesoru per-kamera `NativeComputeUnit`. Rozhodnutí + odůvodnění:
  [decisions.md 2026-08-09](decisions.md). Ověřeno: build x64 + OrangePI (HALArmbian) zeleno, testy
  `ARBot.Common.Tests` 326/326 (nové: roundtrip v3 s hranami, čtení v2 bez hran, procesor s fake
  `IComputeUnit` vč. škálování do RGB). **Na HW neověřeno** (výkon nativního `FindPathEdge` na vlákně
  kamery per snímek — sledovat `compute_ms` v traversability CSV).
- **Úklid `D435Camera`: odstraněna kamerová `BackProject` větev** (obě HAL). Mrtvý dev kód — probability
  i hrany počítá `CameraFrameProcessor`; s větví odešla i property `BackProject`, `resizedColorImage`
  a zakomentovaný zbytek `cu.PathEdges` (nová cesta už potvrzena testy). Následně odstraněno i pole `cu`
  a konstruktory s `IComputeUnit` (žádný volající je nepředával; hlavní konstruktor je nyní
  `D435Camera(string sn, CameraSettings rgb)`). Build x64 (ARBot, Record, HAL.Tests) + OrangePI zeleno.

## 2026-08-07

- **Šířka cesty v mapě (proměnná, přes šířku v uzlu).** Do `OsmNav.Graph.Node` přidána `Width` [m] —
  cesta tak může být na začátku/konci různě široká (interpolace podél hrany) a v křižovatce se hrany
  **hladce napojí** (sdílí šířku uzlu). `GraphBuilder.BuildNetwork(..., defaultWidthMeters=2.0)`: šířku
  uzlu spočítá jako **max přes incidentní cesty** (default, nebo z OSM tagu `width`/`est_width` — parsuje
  metry; uzel s vlastním `width` tagem má přednost). Přeneseno do `MapMsg.MapNode.WidthMeters`. Render ve
  World: cesty jako **vyplněné pásy proměnné šířky** (lichoběžník na hranu + kotouč v uzlu), vše
  **sjednoceno** (`MultiPolygon.Union()`) → uniformní průhlednost + jeden vnější obrys; šířka [m] × `1/cos(lat)`
  (Mercator). V panelu pole **„Šířka cesty [m]"** (`DefaultRoadWidthMeters`, `NumericUpDown`) se uplatní
  při načtení. Ověřeno: OsmNav testy 76/76, screenshot (vzorová `.osm`: obvod `width=6`, diagonály default 2
  → viditelný taper + hladké křižovatky). Build x64 zeleno.

  ![Mapa z OsmNav se šířkami cest: obvod (OSM width=6 m) široký, diagonály se zužují k prostřednímu uzlu (default 2 m), hladké křižovatky](media/world-view-road-width.png)

- **Fix UI: panel „Mapa a vrstvy" přetékal.** Tělo panelu (`ScrollViewer`) mělo pevný `MaxHeight=440`,
  takže na nižším okně obsah přetékal pod mapu bez scrollbaru. Nově se `MaxHeight` odvíjí od výšky mapy
  (`$parent[Grid].Bounds.Height` − chrome, přes `SubtractConverter`) → scrollbar naskočí přesně, když se
  obsah nevejde; jinak se panel roztáhne dle obsahu. Ověřeno screenshotem na zmenšeném okně.
- **Sjednocení převodu domény→zpráva na konvenci `ToLogMessage()`.** `MapMsg` (viz 2026-08-06) původně
  konvertoval statickou tovární metodou `MapMsg.FromRoadNetwork(RoadNetwork)`. Přepsáno na projektovou
  konvenci: konverzi vlastní **doména** — `RoadNetwork.ToLogMessage()` → `MapMsg`; `MapMsg` je zpět čisté
  pasivní DTO (nezná `RoadNetwork`, odpadla závislost `Logs → Graph`). Konvenci jsem zaznamenal do
  [architecture.md](architecture.md) (sekce „Převod doménového stavu na zprávu") a [CLAUDE.md](../CLAUDE.md).
  Build x64 zeleno.

## 2026-08-06

- **World (geo) pohled — nový dokovatelný dokument `WorldViewDocument`.** Analogie robot-centrického
  pohledu, ale v geografickém rámci nad mapovým podkladem. Menu **Tools → World**.

  ![World pohled: OSM podklad, robot jako metrický tvar, trajektorie, sbalovací panel vrstev](media/world-view.png)

  *(Screenshot pořízen bezobslužně režimem `worldshot=true` — otevře World, nakrmí syntetickou
  trajektorií + polohou nad Prahou, hluboko přiblíží na robota a uloží `doc/media/world-view.png`.)*
  - **Hotovo (odsimulováno x64, build zeleno):**
    - Mapový engine **Mapsui** (`Mapsui.Avalonia12` 5.1.0 + `Mapsui.Nts` + `BruTile.MbTiles`) — ověřena
      kompatibilita s Avalonia 12.0.3 (dedikovaný balíček `Mapsui.Avalonia12`).
    - Přepínatelný **podklad**: OSM online / offline MBTiles / žádný. Vypnutí podkladu (nebo zdroj `None`)
      **nevytvoří žádnou dlaždicovou vrstvu** ⇒ na OrangePI žádné pokusy o internet; na ARM (`#if IsARM64`)
      je i výchozí podklad `None`.
    - **Vrstvy** (vypínatelné): poloha+kurz (`GPSState`+`RobotStateMsg`), trajektorie (GPS stopa),
      trasa/graf a značky (`GraphNavigationMsg`). Backpressure „latest-wins" jako u ostatních dokumentů.
    - View hostuje Mapsui `MapControl` v code-behind (mimo design-time), ovládací panel + info v XAML
      (compiled bindings ověřeny buildem).
  - **Poznámka k datům:** `GraphNavigationMsg` se zatím na `Stream` neemituje (OsmNav není napojen na
    řídicí smyčku) → vrstvy trasa/graf/značky jsou připravené, ale prázdné, dokud data nepotečou.
    Poloha + trajektorie fungují živě (GPS + `RobotStateMsg` přes `ControlLoop.EmitDerived`).
  - **Nutno ověřit na zařízení:** Mapsui renderuje přes SkiaSharp — na ARM64 ověřit nativní SkiaSharp assety.
  - **Rozhodnutí:** volba Mapsui (vs. vlastní tile control) — viz [decisions.md](decisions.md).
  - **Odkazy:** `Src/ARBot/ViewModels/WorldViewDocument.cs`, `Src/ARBot/Views/WorldViewDocumentView.axaml(.cs)`,
    menu v `MainWindowViewModel`/`MainWindow.axaml`, [doc/world-view.md](world-view.md).
  - **Doplněk:** ovládací panel je **sbalovací** (přepínač „☰ Mapa a vrstvy", stav `PanelExpanded`), aby
    nebránil pohledu na mapu. Přidán **vestavěný export výřezu do MBTiles** (tlačítko „⬇ Uložit výřez jako
    MBTiles"): stáhne dlaždice OSM aktuálního výřezu z13–19 a zapíše `.mbtiles` (`sqlite-net`), s tvrdým
    stropem počtu dlaždic (5000), throttlingem a User-Agent (OSM tile usage policy). Slouží k rychlému
    pořízení offline podkladu bez externích nástrojů. Odsimulováno x64 (build zeleno); reálné stahování
    a chování okna neověřeno tady (GUI).
  - **Fix (runtime):** `sqlite-net` (z `BruTile.MbTiles`) padal při prvním `SQLiteConnection` na
    `You need to call SQLitePCL.raw.SetProvider()` — bundle balíček se transitivně nepřitáhl. Řešeno
    jednorázovou explicitní inicializací `SQLitePCL.raw.SetProvider(new SQLite3Provider_e_sqlite3())`
    (`EnsureSqliteProvider`) před zápisem exportu i před čtením offline MBTiles. Ověřeno koncově izolovaným
    konzolovým testem s týmiž verzemi balíčků (SQLitePCLRaw 3.0.2). Pozn.: na ARM64 ověřit nativní
    `e_sqlite3` assety na zařízení.
  - **Vizualizace mapy z OsmNav (`MapMsg`):** nová zpráva `MapMsg` (uzly v LLA stupních + hrany) +
    konverze ze sítě (obousměrné hrany se deduplikují na jednu úsečku). Registrována v
    `MessageCatalog`. World dokument má vrstvu **„Mapa (síť)"** (jedna `MultiLineString` featura, efektivní
    i pro velkou síť; přestavuje se jen při nové mapě) a tlačítko **„Načíst OSM mapu…"** (`LoadOsmMapAsync`:
    `.osm` → `OsmXmlReader` → `GraphBuilder` pěší profil → `RoadNetwork` → `MapMsg` → vrstva, parsování na
    pozadí). Souřadnice jsou geografické → kreslí se přímo (bez zarovnávání lokálního rámce). Ověřeno
    screenshotem (worldshot načte malou vzorovou `.osm` síť). OsmNav zatím na Stream `MapMsg` neemituje —
    vrstva ožije i z runtime, až se OsmNav napojí; teď se plní ručním načtením. Panel zvětšen → tělo je
    rolovatelné (ScrollViewer).
  - **Robot jako tvar + hluboký zoom:** robot se v mapě kreslí jako **metrický polygon** ze sdíleného
    `RobotGlyph.OutlineMeters` (místo trojúhelníku) — orotovaný o kurz, převedený do Mercatoru s korekcí
    `1/cos(lat)` (reálná velikost, škáluje se se zoomem). Povoleno **hlubší přiblížení** nad rámec dlaždic
    (`OverrideZoomBounds`, ~z23), protože ~0,5 m robot je při běžném zoomu subpixelový. `RobotGlyph` dostal
    veřejný `OutlineMeters` (jediný zdroj tvaru). Build x64 zeleno, `WorldViewDocument` bez varování.

## 2026-08-04

- **Integrace `Maps/OsmNav` do `ARBot.Common` + testy.** Do projektu nakopírován modul OSM navigace
  z jiného projektu (`Maps/OsmNav/…`: Geo, Graph, Osm, Routing, Navigation, Colider) a jeho testy.
  Zaintegrováno do stávajících projektů (žádný samostatný `.csproj` — SDK globbing).
  - **Hotovo:**
    - Přemapování namespaců podle konvence odvozené od cesty: `OsmNav.Core.Geo/Graph`, `OsmNav.Osm/Routing/
      Navigation` a odchylný `Colider` → `ARBot.Common.Maps.OsmNav.{Geo,Graph,Osm,Routing,Navigation,Colider}`.
    - Zdroj neměl `ImplicitUsings` (cílový projekt je taky nemá) → doplněny explicitní `using` (System,
      Collections.Generic, Linq; +System.IO u `OsmXmlReader`) a `#nullable enable` do souborů s nullable anotacemi.
    - Testy převedeny z **xUnit → NUnit** (74 testů, 22 souborů): `[Fact]→[Test]`, `Assert.Equal(…,N)→
      Assert.That(…, Is.EqualTo(…).Within(1e-N))`, `Assert.Single`, `Contains/DoesNotContain`, `False/Empty/
      InRange/Same/…`. Konverzní část paralelizována přes subagenty s jednotnou převodní tabulkou.
    - **Kolize jmen `Point2D`:** existuje `ARBot.Common.Point2D` v kořenovém namespace; ten podle pravidel
      C# přebíjí `using`-importovaný `…OsmNav.Colider.Point2D` (file-scoped namespace testů je vnořen pod
      `ARBot.Common`). Dočasně vyřešeno přesunem `using …OsmNav.Colider;` **pod** `namespace …;` (viz níže,
      poté zrušeno sloučením typů).
    - Odstraněny přenesené build artefakty (`obj/`, `bin/`).
  - **Ověřeno:** `dotnet build` + `dotnet test` pod `x64` — OsmNav 74/74 prošlo; celá sada 316 passed /
    4 skipped / 0 failed. Čistě algoritmické (bez HW), na zařízení netřeba ověřovat.

- **Sjednocení `Point2D` (float) — provedeno.** Místo dvou planárních bodových typů ponechán sdílený
  `ARBot.Common.Point2D` (float) a OsmNav double-verze smazána. Vyžádalo si to přepis `MotionArc` do
  algebry bod/vektor (`Point2D` pozice + `Vector2D` posun), a to **bez alokací** — `Vector2D` je class,
  tak se posuny/rotace/vzdálenosti počítají v lokálních `double` (helpery `Offset`/`Rotate`/`Hypot`).
  Přesnostní kompromis (float u téměř rovných oblouků, ~mm) je vědomý a funkčně neškodný. Detaily a *proč*
  viz [decisions.md](decisions.md) (záznam 2026-08-04). Kolize jmen tím definitivně zmizela (workaround v testech vrácen).
  `Point2DTests` přepsán na sdílenou algebru. **Ověřeno x64:** OsmNav 76/76, celá sada 318 / 4 skip / 0 fail.

- **Sjednocení `Point2DF` → `Point2D` — provedeno.** Odstraněn druhý float bodový typ (`Point2DF`),
  který sloužil jen jako blittable nosič pro nativní interop (`Depth2XYZ`/`DepthTransform*`/`Segment2`)
  a tabulku `Camera2DToCamera3D`. Bezpečné: identický nativní layout (ABI beze změny), žádné operátory
  ani `.Distance` se nepoužívaly, přesnost stejná (oba float). Upraveny `ICameraProjection`/`CameraProjection`,
  `NativeComputeUnit`, `HALWindows/D435CameraProjection`; `HALArmbian` dědí → beze změny (ARM build netřeba).
  Detaily viz [decisions.md](decisions.md) (2026-08-04). **Ověřeno x64:** Common + HALWindows build zeleno,
  testy 318 / 4 skip / 0 fail (nativní interop testy s `Point2D[]` prošly).

- **Doménová dokumentace OsmNav — hotová.** Přidán [osm-nav.md](osm-nav.md) (mapa kódu + stav integrace +
  shrnutí `Colideru`, který PDF nepokrývá) a odkaz z [CLAUDE.md](../CLAUDE.md) rozcestníku. Dokument
  nedupluje autoritativní [OsmNav-popis.pdf](OsmNav-popis.pdf) (návrh routing/navigation strany) — odkazuje
  na něj a doplňuje pohled z kódu (klikací odkazy na typy, konvence, otevřené úkoly).
  - **Další krok:** začlenění do řídicí smyčky (`ControlLoop`): zdroj polohy → `Navigator` → regulátor
    ([path-following.md](path-following.md)) + `Colider` jako brzda; zdroj `.osm` dat a `Obstacle` seznamu.
  - **Odkazy:** `Src/ARBot.Common/Maps/OsmNav/`, `Src/ARBot.Common.Tests/OsmNav.Tests/`, `doc/osm-nav.md`.

- **Sjednocení geo: OsmNav `GeoPoint`/`GeoMath` → `LLA` — provedeno.** Odstraněn vlastní geotyp OsmNav
  (stupně, value struct) ve prospěch systémového `LLA` (radiány), který produkuje lokalizace (GPS/EKF) a
  používají mapy/`ARBotState` — čistý šev pro pozdější napojení na řídicí smyčku. Haversine → `GreatCircle.Distance`
  (numericky identické), projekce na úsek → **double** equirectangular (`GeoReference.ToLocal` vrací float
  `Point2D` → shazovalo oracle testy, proto vlastní double; finálně jako `LLA.ProjectOntoSegment`).
  Přidán `LLA.FromDegrees`. Dotčeno 6 zdrojů + testy. Detaily viz [decisions.md](decisions.md) (2026-08-04).
  **Ověřeno x64:** OsmNav 76/76, celá sada 321 / 4 skip / 0 fail; HALWindows build zeleno.

- **`ProjectOntoSegment` přesunut do `LLA` + názvosloví geometrie sjednoceno.** Na přání „věci na jednom
  místě" projekce přesunuta z `GeoSegment` (smazán) do `LLA` (instanční, vedle `Distance`). Při té
  příležitosti opraven matoucí název `Intersect` = *projekce* (ne průsečík): `MapWay.Intersect` a
  `NavigationBase.Intersect` → `ProjectOntoLine`; `Intersection` (Line2D/LineSegment2D = skutečný průsečík)
  a `Graph.Intersect` (jiná doména) beze změny. Konvence: `ProjectOntoLine`/`ProjectOntoSegment` vs
  `Intersection`. Detaily [decisions.md](decisions.md) (2026-08-04). **Ověřeno x64:** 321 / 4 skip / 0 fail, `ARBot` build zeleno.

- **Doplněn skalární operátor `*` do `ARBot.Common.Point2D`** (float i double, komutativně — parity
  s existujícím `/`; byl i v původním OsmNav `Point2D`). `MotionArc` **záměrně zůstává** na `double`
  mezivýpočtech (jedno zaokrouhlení na float až při uložení pozice → přesnější u `d − Radius`), nový
  operátor přes něj tedy nevede; je k dispozici pro obecné použití. Testy v `Common/Point2D.cs` /
  `Tests/Point2DTest.cs`. **Ověřeno x64:** 321 / 4 skip / 0 fail.

## 2026-08-02

- **Nový regulátor sledování dráhy (Fáze 1–5 z 6).** Návrh přediskutován do detailu, pak realizace.
  Cíl: vést robota **dráhou z waypointů**, projet každý uzel v rámci `ε` (`MaxPositionError`) **max.
  rychlostí** (bez zastavování v uzlech). Architektura: plán (geometrie rohů + brzdná obálka) + exekuce
  (feedforward + přeplánování z `IModelState`), **žádná proporcionální steering smyčka** (ta v tomto
  setupu kmitá).
  - **Hotovo (build + 237 testů zeleno pod x64):**
    - Fáze 1 — `IRegulator.Control` narovnán na jeden waypoint (pryč `MaxWayPoints`/pole), přesná
      dokumentace dnešního chování. Staré regulátory beze změny.
    - Fáze 2 — `IMotionProfile` + `TrapezoidMotionProfile` (matematika z `Regulator`), 5 **paritních** testů.
    - Fáze 3 — `IPathPlanner.Plan` → `PathResult`: rohy kruhovým obloukem `R=ε·cos(θ/2)/(1−cos(θ/2))`
      (osekání na ½ úseku), vrcholové stropy, **zpětná brzdná obálka** (dopředný průchod se nepočítá).
      9 testů vč. příkladu 2 m + 10 cm.
    - Fáze 4 — `PathResult.Control`: lokalizace + lookahead `L_d=τ_look·v` + zásah přes profil. 4 **simulační**
      testy (přímka, 90° roh, start natočený pryč, S-dráha) — průjezd v ε, dojezd/stop, nekmitá.
    - Fáze 5 — `ControlLoop.Path` jako settable property (atomická výměna vyšší smyčkou), watchdog
      (`Profile.PathControlTimeOut` → dobrzdění po poslední trase), `null` = stání. `ARBotRuntime` + testy
      přepnuty; +2 testy (stání, watchdog).
  - **Rozhodnutí:** feedforward místo pure-pursuit, oblouk místo klotoidy, jen zpětná obálka, `τ_look≈3·Ts`,
    property-based path swap — viz [decisions.md](decisions.md) (2026-08-02).
  - **Další krok:** Fáze 6 (tento zápis + decisions + odkaz z CLAUDE.md — hotovo). **Otevřené / čeká na HW:**
    ověření dynamiky motorů, sweep `τ_look ∈ {0,2;0,3;0,5 s}` na record/replay + selftestu, a **vyšší smyčka
    (plánovač trasy z mapy/OSM)**, která bude `ControlLoop.Path` reálně plnit — zatím robot stojí.
  - **Odkazy:** [path-following.md](path-following.md), `Src/ARBot.Common/Regulators/*`,
    `Src/ARBot.Common/Runtime/ControlLoop.cs`, `Src/ARBot.Common/Configuration/Profile.cs`.

- **Sjednocení regulátorů (navazuje).** `IRegulator` splynul s `IPathController` (jedno rozhraní
  `Control(IModelState)+IsFinished`, cíl drží regulátor uvnitř). `PointRegulator` (bodový, přes
  `IMotionProfile`) **nahradil** `Regulator` i `SimplRegulator` — ty **smazány** po důkazu parity;
  vznikl `SqrtMotionProfile` (odmocninový zákon konzistentně). `ControlLoop.Path` → `ControlLoop.Regulator`.
  Nižší smyčka teď transparentně reguluje na bod (`PointRegulator`) i na dráhu (`PathResult`). Build + 242
  testů zeleno (parita překlopena na golden/closed-form). Viz [decisions.md](decisions.md) (2026-08-02, sjednocení).

## 2026-08-01

- **Robot-centrický pohled: konec překrývajících se buněk.** Buňky polárního gridu se v
  [`RobotCentricControl`](../Src/ARBot/Views/Controls/RobotCentricControl.cs) kreslily jako **čtverec
  u těžiště** se stranou = radiální tloušťka prstence → u robota se překrývaly (šířka čtverce ≥ 5 cm
  je u malých vzdáleností mnohem víc než azimutová rozteč sousedních buněk) a přes vnitřní hranici
  do předchozího prstence (těžiště je posunuté k bližší hraně). **Datový překryv to nebyl** — grid je
  čistá partice. **Hotovo:** buňka se teď kreslí jako svůj skutečný půdorys = **mezikruhová výseč**
  (radiální pásmo z `RadialEdges` × azimutový slot), výseče se díky sdíleným hranicím dokonale skládají.
  Azimutové úhly grid neukládá → renderer je **rekonstruuje z ložisek buněk** (bez změny datového modelu
  / serializace, funguje i ve View/replay); fallback na čtverce při málo datech. Ověřeno buildem (x64,
  clean compile); vizuální kontrola na živém běhu čeká (app v době úpravy běžela). Detail v
  [traversability-grid.md](traversability-grid.md#vizualizace-robot-centrický-pohled). Rozhodnutí
  renderer-only rekonstrukce vs. ukládat `AzimuthEdges` do gridu — zvolena rekonstrukce (menší zásah).
- **Analýza + rozhodnutí (bez kódu):** root-cause GC pauz (200–455 ms, ~13 % snímků) — porovnáním se
  starým **ARBot2** (WPF/.NET 4.8) zjištěno, že nejde o framework ani recyklaci bufferů (starý app taky
  `new`oval per snímek), ale o **architekturu**: starý byl pull + synchronní + jeden živý frame + málo
  vláken (nízký churn), nový je push async fan-out + frame v mnoha frontách + hodně vláken (vysoký
  dlouho žijící LOH churn).
- **Rozhodnutí (návrh):** přejít na **synchronní vlákno-per-kamera** vizuální cestu — `ICameraFrameProcessor`
  volaný v kameře, grid v `CameraFrame`, kamery pullované `ControlLoop`em, **poolované buffery + kopie
  s release** (klíč: GC tlak ≠ memcpy — zisk je v recyklaci, ne ve vyhýbání se kopiím; robot vždy
  nahrává + UI = dva async odběratelé, každý má vlastní pool kopií). → [decisions.md 2026-08-01](decisions.md).
- **Další krok:** implementace inkrementálně dle sekvence v rozhodnutí (1: `ICameraFrameProcessor` + grid
  v `CameraFrame`; 2: konzumenti na `CameraFrame.Grid`; 3: pull přes `ControlLoop`; 4: pooling + kopie).
- **Kroky 1–2 hotové** (čistý agent) — `ICameraFrameProcessor`/`CameraFrameProcessor`, grid v `CameraFrame`
  (v2), konzumenti přepnutí. Změřeno na HW: **`wait` avg 37→13 ms** (mizí fronta), `compute` teď měří
  BackProject+grid dohromady (~51 ms p50), **GC špičky trvají** (max ~494 ms) — cíl kroku 4 (pooling).
  Otevřeno: udělat **BackProject volitelný**, pokud je jen pro viz (~25 ms/snímek). Detaily v
  [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md).
- **Hotovo (krok 1+2 dle [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md)):** grid je nyní
  součástí `CameraFrame` (`PolarTraversabilityGrid`, bez dědičnosti `Message`); `CameraFrame` FormatVersion
  **1→2** (grid serializován uvnitř rámce, `FromData` větví v1=bez gridu / v2=s gridem — staré `.rec` čitelné).
  Nový `ICameraFrameProcessor`/`CameraFrameProcessor` počítá **synchronně na vlákně kamery** probability
  (`ImageProbability`) i polární grid — jádro `BuildGrid` přeneseno 1:1 z `DepthTraversabilityProcessor`
  (včetně nativní SIMD cesty). `D435Camera` (obě platformy) dostala `FrameProcessor` a volá ho v
  `GetMeasurement`. `WireRun`: staré async stupně `BackProjectProcessor` + `DepthTraversabilityProcessor`
  **vyřazeny z grafu**, procesor nastaven kamerám; konzumenti (`RobotCentricDocument`/`Control`,
  `ImageDocument`) čtou `frame.Grid`. `PolarTraversabilityGridMsg` + `DepthTraversabilityProcessor`
  **smazány** (logika ověřena přenesenými testy `CameraFrameProcessorTest` vč. `NativeTransform_MatchesManaged`),
  odebrán z `MessageCatalog`. `BackProjectProcessor` **ponechán** (používá `ARBot.Record`). Diagnostický CSV
  přesunut do procesoru, per-kamera (`traversability-timing-<kamera>.csv`).
- **Ověřeno:** build x64 (app + oba testovací projekty) i OrangePI (HALArmbian) zelený; testy **222** zelených
  (Common 210/4 skip, HAL 12/1 skip). **Neověřeno na HW** (agent nemá kameru) — vizuální shoda gridu/overlaye
  v UI a latence v `logs/*.csv` je **HW brána** dle plánu (kroky 3–4 = pull + pooling ještě nezačaty).
- **Rozhodnutí (od člověka):** **BackProject (probability) je vstup pro řízení robota** → počítá se vždy,
  nedělá se z něj volitelný/on-demand výpočet (uzavírá otevřenou otázku „jen viz vs. řízení"). →
  [decisions.md 2026-08-01](decisions.md).
- **Krok 3 hotový** (dle [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md)): kamery **vyňaty**
  z grafu (`SensorMessageSource`); `ControlLoop` je na tiku **pulluje** přes nové rozhraní `ICameraPullSource`
  (Common, drží směr závislostí — implementaci `HwCameraPullSource` naplní `ARBotRuntime` čtením
  `ARBotHW.Current` za běhu) a **celý `CameraFrame`** (raw + grid) forwardne na `Stream` pro záznam/UI.
  Pull vrací null, když není nový snímek. Nový test `ControlLoopTests.OnTick_PullsCameras_AndForwardsFrameToOutput`.
- **Krok 4 hotový** (kontrakt potvrzen člověkem — varianta „per-consumer pool + release"): `CaptureFramePool`
  (triple-buffer v obou `D435Camera` — recyklované RGB/Depth buffery místo `new` per grab; `CameraFrameProcessor`
  recykluje probability i resize buffer, prob je tak per-slot triple-bufferovaná). Každý async odběratel
  (`RecordingTarget`, `ImageDocument`) má **vlastní `CameraFramePool`**: v `Post()` synchronně memcpy do
  poolované kopie (grid předán referencí — je per-snímek immutable), po zpracování `Release`; vyschnutí poolu =
  best-effort drop. `RobotCentricDocument` kopii nedělá (čte jen grid). Unit testy `CameraFramePoolTest`.
- **Ověřeno:** build **x64 i OrangePI** (app) zelený; testy **217** zelených (Common + 6 nových pool/pull testů).
  **HW ověření pod zátěží stále čeká** (agent nemá kameru) — **klíčová brána kroku 4**: `logs/traversability-
  timing-*.csv` bez periodických 200–455 ms špiček (churn ~0) a integrita záznamu ve View (obraz i grid bez
  tearingu). Dokud neproběhne, je krok 4 „hotový v kódu", ne „ověřený".
- **Root-cause 200 ms záseků NALEZEN na HW** (uživatel pustil sw, špičky v `compute_ms` přetrvaly: max 345 ms,
  ~11 % snímků >100 ms; `wait_ms` malý ~13 ms → pull funguje). Pooling kamerových bufferů (krok 4) nepomohl,
  protože **churn byl jinde**: **`MessageWriter.Write` serializoval KAŽDOU zprávu přes novou `MemoryStream` +
  `ms.ToArray()`** — u `CameraFrame` (~1,8 MB nekomprimované) to bylo několik **LOH** alokací na snímek
  (~90 MB/s při 16 fps) na vlákně recorderu → periodická blokující gen2 GC pauzující i vlákno kamery uprostřed
  `Process`. **Fix:** `MessageWriter` serializuje do **jedné znovupoužité `MemoryStream`** a zapisuje přímo
  z `GetBuffer()` (0 alokací/zprávu; wire formát beze změny — 29 record/replay testů zelených). Navíc
  doplněno **poolování transientů v `BuildGrid`** (`acc` ~79 KB, `dev`, `List` bodů roviny) — sekundární
  churn na vlákně kamery. **Ponaučení:** pooling image bufferů je zbytečný, dokud serializace re-alokuje
  celý snímek; **serializace byla dominantní zdroj (40×).** → [decisions.md 2026-08-01](decisions.md).
- **Ověřeno po fixu:** build x64 i OrangePI zelený, testy **217** zelené. **Znovu ověřit na HW pod zátěží**
  (`logs/*.csv`): očekáváme zmizení periodických >100 ms špiček v `compute_ms`.
- **DEFINITIVNÍ DIAGNÓZA na HW (GC instrumentace).** Do diagnostického CSV doplněny sloupce
  `cam_alloc_kb` (alokace vlákna kamery v `Process`), `proc_alloc_kb` (všechna vlákna) a `gen2`
  (proběhla-li gen2 GC). Čistý běh (1665 snímků, UART senzory vypnuté, robot-centric otevřené, bez
  záznamu, bez interakce): **`compute_avg` 40,5 ms, gen2 = 0 na VŠECH snímcích, jen 0,24 % >100 ms
  (vše warmup seq 0).** `cam_alloc_kb` po warmupu **58 KB/snímek** (jen `grid.Cells`, gen0) — pooling
  probability potvrzen (358→58 KB od seq 3). **Ustálená cesta kamera→procesor je čistá.**
- **Root-cause záseků = dva tranzientní přispěvatelé, ne ustálený churn:** (1) **odpojené UART senzory**
  (IMU/GPS/motor) házely výjimky v těsné smyčce — `Debug.WriteLine(ex.ToString())` alokoval stack-trace
  string na jejich vláknech → periodický gen2 (uživatel potvrdil: vypnutí zkrátilo záseky, `wait` 14→1,2 ms);
  (2) vyšší alokační tlak před poolingem (acc 79 KB + prob 300 KB/snímek) tlačil gen2 — teď poolnuto.
- **Zpevnění (produkce):** `SensorBase` error smyčka — exponenciální backoff (mrtvý senzor polluje ~1×/s
  místo 50×/s) + throttlované logování `ex.Message` (ne `ex.ToString()`) → odpojený/spadlý senzor za běhu
  už nealokuje ani nežere CPU. `RobotCentricControl` cachuje štětce (bylo až 1650 `new SolidColorBrush`/
  render) + pera. Build+testy zelené.
- **Stav:** ustálený churn cíleně vyřešen (gen2=0). Zbývá 58 KB/snímek (`grid.Cells`, gen0, žádná gen2) —
  ponecháno; poolování gridu by vyžadovalo kopii u konzumentů za marginální zisk.
- **Self-test harness** (návrh uživatele) — bezobslužné, reprodukovatelné měření výkonu: parametr
  `selftest=true` → aplikace sama otevře zadaná okna, pustí Run, po `st_seconds` zastaví, zapíše souhrn
  z CSV do `logs/selftest-result.txt` a ukončí se. Parametrizovatelné varianty (`st_record`, `st_images`,
  `st_robot`, `no_uart`) → A/B měření bez ruční obsluhy. Kód: `Src/ARBot/Diagnostics/SelfTest.cs`,
  `MainWindowViewModel.SelfTest.cs`; přepínač `no_uart` v `ARBotHW`. Návod + doporučené varianty:
  [selftest.md](selftest.md). Build x64 i OrangePI zelený.
- **Diagnostiku lze vypnout** parametrem `diag=false` (soutěž) — vypne CSV i GC měření na vlákně kamery
  (`ARBotRuntime` nepředá cestu CSV; `CameraFrameProcessor` pak neměří `DateTime.Now`/GC).
- **Změřeno agentem (self-test, 5 variant à 20 s, `no_uart` kde uvedeno):** `compute avg` 19–23 ms,
  **>100 ms = 0 % ve VŠECH variantách**, max ≤ 48 ms — periodické 200–1098 ms záseky **pryč**. `gen2`
  během Process: baseline/record/uart = **1** (⇒ fix `MessageWriter` i zpevnění `SensorBase` potvrzeny),
  **images = 12, full = 52** — jediný zbývající churn je `WriteableBitmap` per snímek v `ImageDocument`
  (zvýší gen2 při otevřeném okně Images, ale zatím **bez** >100 ms špičky). Další optimalizace (nepovinná):
  recyklace `WriteableBitmap` (TODO ve `Views/README.md`).
- **Ověřeno počítadly (ne jen analýza):** doplněna diag počítadla UI (`ImageDocument.DiagFramesIngested/
  DiagBitmapsCreated`, `RobotCentricControl.DiagRenders`) do souhrnu self-testu. Potvrzeno: v `images`
  variantě `ImageDocument` reálně tvořil ~397 `WriteableBitmap`/15 s → to je zdroj gen2. Zároveň odhaleno,
  že churnoval **i jako neviditelný tab na pozadí** (VM `Ingest` neběží přes Avalonia `Control.Render`,
  takže není frameworkem gatován viditelností).
- **Self-test umí screenshot + video** (návrh uživatele) — pro ilustrace do deníčku. `st_shot=true`
  vyrenderuje hlavní okno do PNG (`ScreenCapture` přes Avalonia `RenderTargetBitmap`); `st_video=true`
  nahraje krátké video. **Video pipeline:** je-li k dispozici **ffmpeg** (auto-detekce PATH → Shotcut →
  winget → override), vytvoří se **komprimovaný GIF** (palettegen, ~117 KB) nebo **mp4**
  (`st_video_format=mp4`, ~34 KB) — obojí ověřeno vytažením snímku zpět. Bez ffmpeg fallback na
  **vestavěný `GifWriter`** (nekomprimovaný LZW s periodickým Clear kódem — korektní, jen velký;
  ověřeno přes GDI+). Ukázka (robot-centrický grid s živými daty z levé kamery):

  ![Robot-centrický grid sjízdnosti (self-test screenshot)](media/robot-centric-grid.png)

- **#1 hotové — gate renderu na viditelnost tabu.** `DocumentBase.IsActive` (nastavuje `DockFactory`
  z `ActiveDockableChanged` = aktivní tab `DocumentDock`); `ImageDocument` si **nejnovější vždy pamatuje**
  (`pending`, poolovaná kopie, 0 GC), ale **renderuje jen když je aktivní**; při zviditelnění
  (`OnActiveChanged`) hned vyrenderuje zapamatovaný snímek. **Změřeno:** Images na pozadí = **0 bitmap,
  gen2=1** (dřív 397/23); Images aktivní = renderuje (387 bitmap) živě i on-show. `RobotCentric` gate
  nepotřebuje (jeho render běží přes `Control.Render`, Avalonia ho gatuje sama). Self-test má nový přepínač
  `st_images_active`. Build x64 i OrangePI + 217 testů zelené.

## 2026-07-30

- **Hotovo:** polární grid sjízdnosti **zapojen do runtime pipeline** — v **Run** ho počítá graf
  (`DepthTraversabilityProcessor`; projekce se sestavuje **líně z připojené kamery** +
  `Profile.Left/RightCameraTransform`), ve **View** se grid jen **přehrává** ze záznamu (nepřepočítává).
  Build + testy zelené (x64, 206/4 skip).
- **Hotovo:** robot-centrická **vizualizace** — dokument přejmenován z `Traversability*` na obecný
  `RobotCentricDocument`/`RobotCentricControl` (plátno pro budoucí robot-centrické vrstvy — RGB
  sjízdnost, okraje vozovky); tvar robotu vytažen do sdíleného `RobotGlyph` (parametr orientace →
  použitelný i pro world view). Ptačí pohled: robot dole, vpřed nahoru, buňky dle třídy + důvěra.
- **Hotovo:** **overlay gridu přes depth** v `ImageDocument` jako vrstva `"<kamera>/Traversability"`
  (rasterizace z `PolarTraversabilityGridMsg` do velikosti depth, per-pixel alfa) — bez samostatného
  obrázku tříd v záznamu, zarovnání přes `ColumnsPerCell` × `RadialEdge.Row`.
- **Hotovo:** `RadialEdges` rozšířeny z `float` na `RadialEdge {Range, Row}` (řádek depth obrazu, kde
  se hranice láme) → umožní kreslit overlay bez zpětné projekce. Drobná optimalizace: `RadialBin`
  půlením intervalu místo lineárně (volá se na každý pixel).
- **Ověřeno na živé kameře** (kamera připojená): overlay i robot-centrický pohled už zobrazují data;
  prahy klasifikace, šumový model a přesnost zarovnání řádků se teprve doladí.
- **Ladění dle zpětné vazby z HW:** oprava orientace robotu v `RobotGlyph` (WPF Y↓ → math Y↑, byl
  vzhůru nohama); vizualizace ukazují **stáří zprávy** (Δ = teď − `TimeStamp`) pro diagnostiku latence;
  vstup `DepthTraversabilityProcessor` přepnut na **`DropOldest`/kap. 2** (neomezená fronta rostla a
  grid se zpožďoval za realtime — best-effort viz stupeň má počítat vždy nejnovější snímek).
- **Ladění #2:** robot je dozadu delší než dopředu (počátek = osa otáčení) → `RobotGlyph` publikuje
  `Forward/Rear/SideExtentMeters`, `RobotCentricControl` nechá dole místo na zadní dosah (nebyl vidět
  celý). Přidána diagnostika latence: `PolarTraversabilityGridMsg.ComputeMs` (NEserializovaná doba
  `BuildGrid`) zobrazená vedle Δ → k odlišení vlastního výpočtu od čekání ve frontě / GC pauz.
  Bimodální Δ (30↔500 ms) ukazuje na **GC pauzy** z alokací per-snímek, ne na TPL (stupně běží na
  dedikovaných `LongRunning` vláknech, ne na threadpoolu) — potvrdit `ComputeMs` a pak řešit alokace.
  Čísla na obrazovce skáčou a nejdou spárovat → `DepthTraversabilityProcessor` navíc píše **CSV log**
  `logs/traversability-timing.csv` (sloupce `seq;capture;camera;wait_ms;compute_ms;cells`; `wait` =
  pořízení→start výpočtu, tj. fronta/hopy/GC) — `logs/` je už v `.gitignore`.
- **Diagnóza z CSV (448 vzorků, 1 kamera):** `compute` podlaha ~16–50 ms (75 % < 50 ms), ale ~8 %
  snímků špička 200–455 ms s NÍZKÝM `wait` → pauza padá dovnitř výpočtu; špičky periodické ~každých 5 s
  = **Gen2/LOH GC** z per-snímek alokací (image buffery kamer ~1,5 MB/snímek → LOH). Není to TPL.
- **Experiment Server GC — ZAMÍTNUT:** zkusil jsem `ServerGarbageCollection=true` (x64). Data z CSV to
  **zhoršila** (compute avg 50→66 ms, >100 ms z 8 % na 20 %, bimodální rozdělení se rozmazalo do
  spojitého ocasu 0–500 ms) — background GC vlákna při mnoha stupních pipeline přesytila CPU, výpočetní
  vlákno je častěji odscheduleno (wall-clock compute roste). Vráceno na **Workstation + Concurrent**
  (default). Trvalé řešení špiček je **snížit alokace** (pooling image bufferů v driveru kamery), ne
  měnit GC režim; pro headroom na 2 kamery navíc nativní depth→pointcloud (`DepthTransform2Impl`).
- **Krok A — nativní depth→pointcloud (hotovo):** `PolarGridConfig.UseNativeTransform` = `DepthTransform2Impl`
  (SIMD) se **znovupoužitým** `Point4D[]` bufferem místo managed per-pixel transformu (žádná alokace/snímek).
  Z asm ověřeno: **mm→m interně** + výstup v **opačném pořadí** (`cloud[len-1-p]`) — ošetřeno indexem.
  **Ekvivalence managed↔native** ověřena testem (`BuildGrid_NativeTransform_MatchesManaged`); v runtime
  zapnuto, managed cesta ponechána jako fallback. Zbývá: pooling bufferů kamer (krok B) na GC špičky.
- **Dopad A (změřeno z CSV):** CPU cena výpočtu klesla ~3× — `compute_ms` **min 16→5 ms**, mode <30 ms,
  205 snímků <10 ms (managed nikdy pod 16). Ale **wall-clock avg/ocas beze změny** (~52 ms avg, ~13 %
  >100 ms, max 483) — dominuje **GC** (špičky mají vysoký `compute`, NÍZKÝ `wait` → pauza padá dovnitř
  výpočtu). A tedy dal headroom (2 kamery), latenční ocas spraví až krok B (alokace bufferů kamer).
- **Rozhodnutí:** View = jen přehrávání gridu (přepočet ze záznamu odložen — živé intrinsics se
  nezaznamenávají), Run = živé intrinsics. → [decisions.md 2026-07-29](decisions.md) (doplněno 2026-07-30).
- **Zavedení DevLogu** — tenhle deníček + pravidla + odkaz z CLAUDE.md.
- **Odkazy:** `Src/ARBot/Robot/ARBotRuntime.cs`, `Src/ARBot/ViewModels/{RobotCentricDocument,ImageDocument}.cs`,
  `Src/ARBot/Views/Controls/{RobotCentricControl,RobotGlyph}.cs`, `Src/ARBot/Views/RobotCentricDocumentView.axaml*`,
  `Src/ARBot.Common/Vision/{PolarTraversabilityGridMsg,PolarGridConfig,DepthTraversabilityProcessor}.cs`,
  [doc/traversability-grid.md](traversability-grid.md).

- **Hotovo (zapojení prezentace senzorů):** dokumenty GPS/motory/kamera zapojeny do panelu Sensors
  přes `CreateSensorDocument` (`MainWindowViewModel`), aktualizován
  [Views/README.md](../Src/ARBot/Views/README.md). (Jediné dnešní kusy dané senzorové práce —
  `MainWindowViewModel`+`README` mají mtime 07-30; zbytek vznikl dřív, viz níže.)
- **Oprava datace DevLogu:** senzorová/HAL práce byla nejdřív omylem zapsaná celá pod 07-30;
  **zpětně rozdělena k reálným datům podle časů změn souborů** — 07-12/13 (Armbian porty + prezentace
  senzorů, commit `16490f9`), 07-21 (D435 hot-plug), 07-24 (senzory v `ARBotHW`), 07-25 (dokumenty).
- **Ověření (celé senzorové práce):** buildy x64 i OrangePI zelené; na HW zbývá ověřit hot-plug,
  načtení net40 `VectorNav.dll` na ARM64, podporu T265 v nativní librealsense 2.53 (T265 v 2.50+
  odebrán — riziko), rovnoměrnost GPS a `System.IO.Ports` na Armbianu.

## 2026-07-29 _(zpětně z gitu)_

- **Hotovo:** návrh a implementace **polárního gridu sjízdnosti** z hloubkové kamery
  (`DepthTraversabilityProcessor`: depth → point cloud → polární grid → `PolarTraversabilityGridMsg`),
  robot-centrický a per-kamera; geometrie + klasifikace ověřeny syntetickým testem.
- **Rozhodnutí:** klíčová návrhová rozhodnutí (robot-centrický, per-kamera, azimut = konstantní
  počet sloupců, radiální Δr, confidence + edge range). → [decisions.md 2026-07-29](decisions.md).
- **Odkazy:** `Src/ARBot.Common/Vision/{DepthTraversabilityProcessor,PolarTraversabilityGridMsg,PolarGridConfig}.cs`,
  `Src/ARBot.Common.Tests/Vision/`, [doc/traversability-grid.md](traversability-grid.md).

## 2026-07-28 _(odhad — práce v pracovním stromu, bez vlastního commitu; HEAD na `4c69ea8`)_

- **Hotovo (record/replay runtime dotažen do Run+View):** `ARBotRuntime` (jeden veřejný `Stream`,
  režimy Run/View, `Start/Stop`), `RoleRouter`+`RelaySource` (router přes `IPrimaryMessage`), přesun
  `IMotorControl` → `ARBot.Common.Devices` + `DummyMotors`, **thread-safe `AsyncFusionEngine`**
  (fúze i řízení běží paralelně, žádná umělá serializace), `RobotState : IModelState`,
  **best-effort záznam** (per-typ drop v `Post`, `T_out`/`Name` v `MessageIndex`), `IScheduler`+
  `ControlLoop` (řízení jako periodický uzel; fúze přestala tikat — `PumpTicks` odstraněn),
  `FileMessageSource.SeekTo` (index-aware) + migrace `ImageDocument` na odběr `Stream`u.
  Build x64 + testy zelené. Detail v [doc/record-replay.md](record-replay.md).
- **Rozhodnutí:** řízení běží **nezávisle na měřeních** (periodická smyčka nad schedulerem vzorkuje
  odhad EKF přes `GetStateAt(t_k)`, funguje i při výpadku měření); fúze jen agreguje/predikuje.
- **Hotovo (dokumentace):** [doc/record-replay.md](record-replay.md) rozšířen o **„Implementační
  kontrakt"** (skeletony API, rozhodnutí, gotchas, pořadí kroků, výchozí volby); rozsah **Run+View**,
  **Simulate odložen** s hookem `T_in`+`T_out` v záznamu. Návrh prověřen opakovaným review z čistého kontextu.
- **Oprava repa:** `.gitignore` omylem ignoroval **zdrojový** `Src/ARBot.Common/Logs/` (VS pravidlo
  `[Ll]ogs/`) → celý message model (`Message`, `Blob`, `RobotStateMsg`, …) nebyl trackovaný; přidána
  negace `!Src/ARBot.Common/Logs/` a soubory přidány do gitu (build z čistého klonu teď projde).
- **Hotovo (record/replay UI dotažení):** `ImageDocument` — **oddělený overlay pro levou a pravou
  kameru** (`LeftOverlay*`/`RightOverlay*`, vlastní výběr vrstvy i info; průhlednost sdílená).
- **Hotovo:** **ReplayNavTool** — během Play se aktualizuje slider i textová pozice (poll `Cursor`
  `DispatcherTimer`em + napojen `Completed`); přidán **virtualizovaný grid** záznamů z indexu
  (Seq / typ / jméno / čas), klik na řádek = seek, výběr synchronní se sliderem (`ScrollIntoView`
  sleduje přehrávání). `IndexEntry` pole → **auto-properties** (binding přímo do gridu bez VM řádků).
- **Hotovo:** stavově podmíněné příkazy **Run/View/Stop** (Run/View jen v klidu, Stop jen za běhu).
- **Rozhodnutí:** `CameraFrame` se zaznamenává **bez komprese** (`None`) — šetří CPU; ~1,8 GB/min
  (2× D435 @10 Hz) se na NVMe vejde na hodiny (dost pro testy i soutěžní jízdu). Komprese zůstává
  připravená (`ImageMsg.Compression` Jpeg/Png/Deflate) k zapnutí, když bude potřeba šetřit místo.
- **Ověřeno:** build x64 0 chyb; testy Common 201 / HAL 12 zelené (živě v aplikaci zatím neověřeno).
- **Odkazy:** `Src/ARBot/ViewModels/{ImageDocument,ReplayNavTool,MainWindowViewModel}.cs`,
  `Src/ARBot/Views/{ImageDocumentView,ReplayNavToolView}.axaml*`,
  `Src/ARBot.Common/Communication/MessageIndex.cs`, `Src/ARBot.Common/Devices/CameraFrame.cs`.

## 2026-07-27 _(zpětně z gitu)_

- **Hotovo:** record/replay — serializace `ImageMsg` + `CameraFrame`, responzivita UI
  (backpressure „latest-wins + Background flush"), stavové příkazy Run/View. Commit `4c69ea8`.
- **Rozhodnutí:** `Blob` → `ImageMsg`; verzování zpráv (`Message.Verze`); Run rozdělen na
  „Run without log / Run and log". → [decisions.md 2026-07-25](decisions.md).
- **Odkazy:** [doc/record-replay.md](record-replay.md).

## 2026-07-25 _(zpětně, dle časů změn souborů)_

- **Hotovo:** dokumenty **prezentace senzorů** — `GpsDocument`, `MotorControlDocument`,
  `CameraDocument` (ViewModely, mirror `IMUDocument`). Kamerový dokument s přepínačem **RGB/hloubka**
  (Gray16 → grayscale). Obnova **událostí `MeasurementArived`** (ne pollováním `DispatcherTimer`em)
  → rovnoměrné zobrazení, jak data chodí z driveru. (Views náčrtem už ~07-11/12, ViewModely zde.)
- **Odkazy:** `Src/ARBot/ViewModels/{GpsDocument,MotorControlDocument,CameraDocument}.cs`.

## 2026-07-24 _(zpětně z gitu)_

- **Hotovo:** vytvoření **řídicí smyčky** a **zárodku systému zpráv** (pipeline
  `MessageSource`/`Target`, role, taps) s podporou vizualizace. Commity `50b5660`, `b714d9a`.
- **Rozhodnutí:** řídicí smyčka + UART odolné vůči nedostupným portům. → [decisions.md 2026-07-25](decisions.md).
- **Odkazy:** [doc/record-replay.md](record-replay.md), [doc/architecture.md](architecture.md).
- **Hotovo (senzory v `ARBotHW`):** sjednocené wiring VN100/GPS/motorů (port přes parametr) + větev pro
  **OrangePI** (VN100 přes sdílený `Uart` — System.IO.Ports jede i na Linux/ARM, ne `UartNative`);
  kamerové vlastnosti přetypovány na rozhraní `ICamera`/`IIMU` (příprava překladu app pro ARM).
  → `Src/ARBot/Robot/ARBotHW.cs`.

## 2026-07-21 _(zpětně, dle časů změn souborů)_

- **Hotovo:** **D435 odolná proti hot-plugu** (obě platformy) — lazy (re)connect ve smyčce, detekce
  odpojení přes `Context.QueryDevices` + timeout `TryWaitForFrames`, `Teardown` pipeline zahodí a
  znovu vytvoří, `IsError` = `!connected`, bez busy-loopu (stejný vzor jako T265 z 07-12/13).
- **Odkazy:** `Src/ARBot.{HALWindows,HALArmbian}/Devices/Camera/D435Camera.cs`.

## 2026-07-13 _(zpětně z gitu)_

- **Hotovo:** dotažení portů na Armbianu a prezentace senzorů v UI. Commit `16490f9`.
- **Hotovo (VN100 na OrangePI):** `VectorNav.dll` je **MSIL/AnyCPU managed** (ne x64/Windows) →
  reference povolena i pro `OrangePI`, VN100 soubory se přestaly z ARM buildu vylučovat (`ARBot.HAL.csproj`).
- **Hotovo (T265 na Armbian):** `T265TrackingCamera` zduplikován do HALArmbian (jako D435, wrapper
  RealSense 2.53). ⚠ T265 byl v librealsense 2.50+ odebrán — reálnou podporu v 2.53 nutno ověřit na HW.
- **Hotovo (HAL kamery/senzory):** **T265 odolná proti hot-plugu** (lazy reconnect; odpojení se u T265
  projeví jen timeoutem, ne výjimkou → kontrola přítomnosti + zahození pipeline); **GPS „blokové" čtení**
  opraveno (`UBXMessage.Parse` četl payload částečně `Read(buf,0,len)` → desync; nově blokující
  `u.Read(len)`, plynulých ~10 Hz); rozhraní `IGPS`/`IMotorControl` dostaly `MeasurementArived`,
  `ICamera : ISensor`.
- **Odkazy:** `Src/ARBot.HAL/ARBot.HAL.csproj`, `Src/ARBot.{HALWindows,HALArmbian}/Devices/Camera/T265TrackingCamera.cs`,
  `Src/ARBot.HAL/Devices/GPS/uBlox/UBXMessage.cs`, `Src/ARBot.HAL/{IGPS,IMotorControl,ICamera}.cs`.

## 2026-07-11 _(zpětně z gitu)_

- **Hotovo:** restrukturalizace projektu. Commit `960f97c`.

## 2026-07-10

- **Hotovo (VN100 / IMU):** druhý driver `VN100IMUBinary` (binární výstup VN-100) — navíc čte
  **attitude uncertainty (YprU)** → `IMUState.OrientationUncertainty` (zdroj kovariance R pro
  orientaci ve fúzi). Vyjasněny **souřadnicové framy**: body **FLU**, world **ENU** + matematická
  orientace. Montáž VN100 (X vzad) + uložená **reference frame rotation `diag(-1,1,-1)`** → výstup
  robotem zarovnaný FRD/NED; driver převádí (yaw azimut→ENU math, gyro/accel/mag FRD→FLU). Decode
  ověřen syntetickým paketem, framy read-only diagnostikou (`VNRRG`, COM5@115200, reg 26 ve flash).
- **Hotovo (UI):** `IMUDocument` — kompas + umělý horizont kreslené z kvaternionu, ostatní číselně;
  vlastní controly `CompassControl`/`ArtificialHorizonControl` + sdílený `SensorStatusControl`
  (indikátor `ISensor`). Otevírání detailu **dvojklikem** v panelu Sensors (mapování senzor→dokument,
  rozšiřitelné).
- **Hotovo (refaktor UI):** `DocumentBase`/`ToolBase` s `ViewType` (`IViewProvider`); `ViewLocator`
  tvoří view podle typu → **inline DataTemplaty z `App.axaml` nahrazeny samostatnými `UserControl`y**
  ve `Views/` (design-time náhled přes `Design.DataContext`, `Design.IsDesignMode` guardy).
- **Hotovo (dokumentace):** `CLAUDE.md` rozcestník + doménové `doc/*.md` + `Views/README.md`.
- **Rozhodnutí:** IMU frame se řeší **na senzoru** (reference frame rotation), ne SW offsetem;
  `SensorAdapters` + řídicí smyčka patří do aplikace `ARBot` (potřebují Common i HAL).
- **Ověření:** build x64 + Fusion/HAL testy zelené; VN100 čtení ověřeno na HW (heading OK po factory
  resetu — dřívější „180° na severu" byla stará konfigurace senzoru, ne chyba kódu).
- **Odkazy:** commity `b36c698`, `76c8b89`, `6491ddd`; [doc/imu-and-frames.md](imu-and-frames.md),
  [Src/ARBot/Views/README.md](../Src/ARBot/Views/README.md).

## 2026-07-08

- **Hotovo:** do EKF přidán **NIS + gating** odlehlých měření; klíčový je **měkký režim (Soft)** —
  místo tvrdého zahození se odlehlému měření nafoukne `R` (`R'=R·NIS/práh`), takže se filtr z dlouhého
  výpadku (např. GPS) **vždy zotaví** (tvrdý `Reject` umí trvale zaseknout — „lockout"). Bezstavové →
  skládá se s replayem. Testy (lockout recovery) zelené. Commit `069071c`.
- **Odkazy:** `Src/ARBot.Common/Fusion/{Gating,Ekf}.cs`, [doc/ekf-fusion.md](ekf-fusion.md).

## 2026-07-07

- **Hotovo:** **EKF senzorická fúze** napsaná od nuly (`ARBot.Common/Fusion`) — generický `Ekf`
  (predikce/korekce, Joseph form, čisté `PredictStep`/`UpdateStep` pro replay) → `EKFModel`
  `[X,Y,θ,v,ω]` (near-constant-velocity, `Q(dt)` škáluje s časem; **v,ω jako stavy** → robustní vůči
  smyku), měřicí modely per senzor, `AsyncFusionEngine` (zpracování dle **času pořízení**, checkpointy
  + líný přepočet, out-of-sequence přes replay, okno ~1 s, `GetStateAt` do budoucna i rekonstrukce
  minulosti), `SlipDetector`, `GeoReference` (LLA→ENU). MathNet, čistě managed (ARM-safe). Testy zelené (x64).
- **Kontext:** legacy EKF nekompiloval (chybějící `Matrix`, vazby na WPF) — přepis hlavně kvůli
  **lepšímu asynchronnímu zpracování** senzorů s různými kmitočty a latencí (kamery) místo pevného 10 Hz taktu.
- **Rozhodnutí:** `v`/`ω` jsou stavy (ne vstup) kvůli smyku; adaptivní odhad R/Q z reziduí **odložen**
  (je stavový, konfliktní s bezstavovým replayem → zatím jen NIS gating).
- **Hotovo (souběžně — HAL reorg + UI diagnostika + Armbian NeoPixel; commit `ee60186`):** větší dávka
  napříč HAL a UI (zpětně dohledáno pickaxe — celé sezení skončilo v jednom commitu, navazující sezení
  soubory dál upravovala 07-10/11/13/27):
  - **HAL reorganizace:** kamery/joystick/NeoPixel přesunuty do `Devices/{Camera,Joystick,NeoPixel}`,
    **namespace sjednoceny s adresářovou strukturou** (RootNamespace `ARBot.HAL`; `RealSenseNativeResolver`
    → `ARBot.HAL`), opraveni konzumenti (`D435TestDocument`, `D435CameraTest`).
  - **Panel Debug output:** dokovací nástroj `DebugOutputTool` + `RelayTraceListener` napojený na
    `Trace.Listeners`; v `Program.cs` `.LogToTrace()` nahrazeno `FilteredTraceLogSink`, který přeposílá logy
    Avalonie do Trace, ale **filtruje oblast `Binding`** — jinak by panel zaplavily neškodné binding-warningy
    z Dock.Avalonia themy (bindí na volitelné, běžně null `DockCapabilityPolicy`/`OriginalOwner`). Diagnostika
    D435 na Armbianu: `DiagCam` (log do souboru) → `Debug.WriteLine` (jde teď i do panelu).
  - **Panel Sensors + menu Tools:** `SensorStatusTool` zobrazuje `ARBotHW.Current.Sensors` (jméno + `IsError`,
    barevný indikátor, periodický refresh); přidáno menu **Tools → Sensors overview** (`OpenSensors`).
  - **Dock UX:** robustní (znovu)otevírání panelu ve všech stavech (viditelný / pinnutý / skrytý / v plovoucím
    okně / zavřený) **bez duplikátů** — dokování vůči stabilnímu `DocumentDock` přes `SplitToDock`. Zprovozněna
    **plovoucí okna** (`DefaultHostWindowLocator = () => new HostWindow()` v `DockFactory.InitLayout`) — bez toho
    se vytažený nástroj „ztratil". Chování Docku (collapse rozpouští proporcionální dok, hide vs. remove, pinned
    kolekce) dohledáno dekompilací Dock `FactoryBase`.
  - **Armbian NeoPixel (WS2812):** doimplementován `ArmbianSpiNeoPixelDriver` — přes `/dev/spidev0.0`;
    `System.Device.Spi.SpiDevice` **vstupuje jako parametr** (vlastník = volající), `WriteData` pošle buffer
    sub-bitů jedním zápisem; balíček `System.Device.Gpio`. SPI na Pi viz
    [OrangePi5Ultra/POSTUP.md](../OrangePi5Ultra/POSTUP.md) (overlay `spi0-m2-cs0-spidev`, DIN→SPI0_MOSI).
  - **Komentáře:** doplněny do `NeoPixelProcessor` (animační smyčka LED — blinkry, KnightRider, Alert).
  - **Ověření:** buildy zelené (ARBot x64, HALArmbian OrangePI, HAL.Tests). Na HW/později zbývá: NeoPixel
    časování (`PulseConfig` vs. SPI clock) a reálný běh na Pi; v samotné appce je `IsX64` nedefinováno (blok
    v `ARBotHW.Init()` se nepřekládá) → panel Sensors je na vývojovém stroji bez HW prázdný / „CHYBA".
  - **Odkazy:** `Src/ARBot/ViewModels/{DebugOutputTool,RelayTraceListener,SensorStatusTool,DockFactory,MainWindowViewModel}.cs`,
    `Src/ARBot/FilteredTraceLogSink.cs`, `Src/ARBot/{Program.cs,App.axaml}`, `Src/ARBot/Views/MainWindow.axaml`,
    `Src/ARBot/Robot/NeoPixelProcessor.cs`, `Src/ARBot.HALArmbian/Devices/NeoPixel/ArmbianSpiNeoPixelDriver.cs`,
    `Src/ARBot.{HALWindows,HALArmbian}/Devices/{Camera,Joystick}/*` (namespace).
- **Odkazy:** `Src/ARBot.Common/Fusion/*`, `Src/ARBot.Common.Tests/Fusion/`, commity `fc3eb47` (EKF),
  `ee60186` (HAL+UI), [doc/ekf-fusion.md](ekf-fusion.md).

## 2026-07-05 _(doplněno zpětně)_

- **Hotovo:** **D435 kamera běží v ARBot aplikaci na reálném Orange Pi** — živý RGB stream, ověřeno
  na HW (`ed02517`). Funkční celý řetězec `OrangePI` → `HALArmbian` → wrapper 2.53 →
  `librealsense2.so` + `libNativeLib.so`.
- **Platforma `OrangePI`** (Armbian/ARM64) + přepínač `IsARM64` (stejný styl jako `IsX64`/`IsX86`)
  napříč `ARBot`, `ARBot.HAL`, `ARBot.HALArmbian`, `ARBot.Common`, `ThirdParty/Intel.RealSense`
  (v `ARBot.slnx` vč. vyloučení Windows-only projektů). Platforma **nemění managed výstup** (portable
  IL) — jen přepíná define + výběr HAL; app se na Pi nasazuje framework-dependent (`dotnet ARBot.dll`).
- **Platform-dedikovaný HAL:** `OrangePI` → `HALArmbian` (RealSense **2.53**), jinak `HALWindows`
  (**2.47**). *Proč dvě verze wrapperu:* managed `Intel.RealSense.dll` musí verzí odpovídat native
  `librealsense2.so` — dle `rs.h` je zaručeně ABI-kompatibilní jen rozdíl v *patch*; 2.47↔2.53 je
  *minor* (riziko „api version mismatch" / tichého driftu struktur `rs2_intrinsics`/`extrinsics`).
  ARM wrapper 2.53 proto zkompilován ze zdrojů `librealsense v2.53.1` do net10.0
  (`ThirdParty/Intel.RealSense`) — cmake C# bindings jsou Visual-Studio-only.
- **D435Camera** portována do `HALArmbian`; `RealSenseNativeResolver` mapuje P/Invoke jména na Linuxu
  (`realsense2`→`librealsense2.so`, `NativeLib.dll`→`libNativeLib.so`).
- **Ladění GUI:** D435 dokument nejdřív nezobrazoval obraz → `DllNotFoundException('NativeLib.dll')`
  při zpracování snímku (aplikaci chyběl resolver — měly ho jen testy) → resolver doplněn do
  `HALArmbian`. Přidán i overlay s číslem snímku pro rychlou vizuální diagnostiku.
- **Build fixy pro ARM:** `FTDISpi.handle` pod `#if IsX64`, `VectorNav`/`VN100IMU` jen mimo `OrangePI`
  (Windows-only HW s knihovnou mimo repo).
- **HW test:** nový `ARBot.HAL.Tests` — headless integrační test D435 (`[Category("Hardware")]`,
  graceful skip bez kamery).
- **Rozhodnutí:** platform-dedikovaný HAL + dvě verze RealSense wrapperu podle cílové platformy.
- **Odkazy:** `ed02517`, `Src/ARBot.HALArmbian/*`, `Src/ThirdParty/Intel.RealSense/`,
  `Src/ARBot/ARBot/{App.axaml,ViewModels/D435TestDocument.cs}`, `OrangePi5Ultra/POSTUP.md`,
  [doc/build-and-platforms.md](build-and-platforms.md).

## 2026-07-03 _(doplněno zpětně)_

- **Hotovo:** **NUnit testy pro `NativeComputeUnit`** (P/Invoke vrstva nad `NativeLib`) + oprava
  warningu CA1060 (P/Invoke deklarace přesunuty do vnořené `NativeMethods`) (`92449d7`).
- **Oprava nativní knihovny** (odhaleno testy):
  - **x64:** doplněn chybějící `EXPORT` u `TransformPoint4DImpl`, `Depth2XYZImpl`, `XYZ2PlaneImpl`,
    `ClearAggregateImpl` v `asm_win_x64.asm` — nebyly v exportech DLL (runtime `EntryPointNotFound`;
    latentní bug i v `D435CameraProjection`).
  - `CalcPlaneParams` v `native_funcs.cpp` — ochrana proti singularitě (`d==0`), shoda s managed
    `PlaneParams.Calc()`.
  - **ARM (`asm_linux_arm64.S`):** hlavně **špatná calling convention** — psáno x86-stylem místo
    AAPCS64 (float/double v jiných registrech, HFA `Point4D`, `float r` v `s0`) u `XYZ2PlaneImpl`
    a `AggregateObstaclesImpl`; + `×32` offset bug v `ClearAggregateImpl`/`ExtractObstaclesImpl`.
- **Ověřeno:** x64 Windows, ARM přes **Docker/QEMU** i **reálný Orange Pi** (32 passed, 4 skip).
- **Nález:** `NativeComputeUnit.Segment` cesta padá na x64 (`AccessViolation`), třída se v produkci
  nepoužívá a není `IDisposable` → Segment testy ponechány `[Ignore]`.
- **Odkazy:** `92449d7`, `Src/ARBot.Common.Tests/NativeComputeUnitTest.cs`, `Src/NativeFuncs/*`.

## 2026-07-02 _(doplněno zpětně)_

- **Hotovo (D435 Test v UI):** menu **Test → „D435 Test"** otevře **Dock dokument** napojený na
  `D435Camera(sn=null)` a zobrazuje **RGB stream** (`CameraFrame.ImageRGB` BGR32 → Avalonia
  `WriteableBitmap`, aktualizace na UI vlákně přes `Dispatcher.UIThread`). App `ARBot` nově referencuje
  `ARBot.HALWindows` a **zůstává `net10.0`** (cross-platform buildable — HALWindows je taky `net10.0`).
  `e094112`. Výchozí bod pro následné portování kamery na ARM (viz 07-05).
- **Hotovo (nasazení nativní knihovny):** `NativeLib.dll` (z `NativeFuncs/bin`) se **kopíruje do
  outputu** (transitivně do app/HALWindows/testů) — jinak P/Invoke za běhu hlásil `DllNotFoundException`.
  Řešeno `<None CopyToOutputDirectory>` v `ARBot.Common.csproj`.
- **Hotovo (sjednocení kamer na `SensorBase`):** `ICamera` sjednoceno se `SensorBase` — `ImageGrabed`
  → **`MeasurementArived`** (`EventHandler<CameraFrame>`); `D435Camera : SensorBase<CameraFrame>,
  ICamera`, `T265TrackingCamera : SensorBase<IMUState>, IIMU`, `T265TrackingCameraNative` **samostatně**
  `SensorBase<IMUState>` (blokující `GetMeasurement` nad `T265Grab`, už nedědí z managed T265). Odpadl
  duplikovaný task/loop/`GetLastMeasurement`/`Dispose`; smazán `ImageGrabedEventArgs`. Kamery tak mají
  bookkeeping (`FrameNum`/periody) i `MeasurementArived` zdarma z base.
- **Ověření:** build + testy zelené (x64); **na reálné kameře zde neověřeno** (živý stream ověřen až
  v navazujícím sezení při ARM portu, `ed02517`).
- **Odkazy:** `Src/ARBot/ARBot/ViewModels/{D435TestDocument,DockFactory,MainWindowViewModel}.cs`,
  `Src/ARBot.HAL/ICamera.cs`, `Src/ARBot.HALWindows/{D435Camera,T265TrackingCamera,T265TrackingCameraNative}.cs`,
  `Src/ARBot.Common/ARBot.Common.csproj`, [doc/build-and-platforms.md](build-and-platforms.md).

## 2026-06-30 _(doplněno zpětně)_

- **Hotovo (zprovoznění HAL vrstvy):** projekty `HAL`/`HALWindows`/`HALZBoard`/`HALArmbian` zařazeny do
  buildu (`ARBot.slnx`); doplněny `<Platforms>AnyCPU;x64;x86</Platforms>` — bez nich VS projekty
  **přeskakoval** („Skipped": solution x64 → projekt neměl x64 konfiguraci) a nevznikaly DLL. `3833705`.
- **Hotovo (přesuny do HAL):** NMEA zprávy `Common → HAL`; `Uart` do **sdíleného HAL** na
  **`System.IO.Ports`** (cross-platform → netřeba rozhazovat per-platforma); `using HAL` →
  `using ARBot.HAL` (sjednocení namespace). Reference FTD2XX_NET, Intel.RealSense (+ kopie nativní
  `realsense2.dll`), vndotnetlib.
- **Hotovo (WPF → System.Numerics):** kamery (D435, T265), `VN100IMU` a `CameraProjection` přepsány
  z WPF `System.Windows.Media.Media3D` (`Matrix3D`/`Vector3D`/`Quaternion`) na `System.Numerics`
  (`Matrix4x4`/`Vector3`/`Quaternion`) — app i HAL tak netáhnou WPF.
- **Hotovo (`MeasurementArived`):** událost přidána do `IIMU`; `SensorBase` dostal hook `OnMeasurement`,
  který ji vyvolává (VN100IMU a další senzory ji tím mají zadarmo).
- **Hotovo (UI scaffolding):** `MainWindow` menu (File/Utils/Test) + **dokovací engine Dock 12**
  (`DockFactory`, `DockControl`, `DockFluentTheme`).
- **Hotovo (HALWindows štíhlejší):** `net10.0-windows` → **`net10.0`** (WPF už netřeba) — aby app mohla
  HALWindows referencovat a **zůstat cross-platform**; odebrány nepoužívané reference (AForge,
  Microsoft.CSharp, System.Net.Http, System.Data.DataSetExtensions, Bytecode, UsbWrapper). `521db4b`.
- **Ověření:** build celého solution x64 zelený. RealSense/SharpDX/FTDI jsou Windows-only až za běhu.
- **Pozn.:** tohle byl **první** bring-up HAL; hlubší reorganizace (kamery/joystick/NeoPixel do
  `Devices/*`, další sjednocení namespace) proběhla v navazujícím sezení (`ee60186`, 07-07).
- **Odkazy:** `Src/ARBot/ARBot.slnx`, `Src/ARBot.HAL*/*`,
  `Src/ARBot/ARBot/{App.axaml,Views/MainWindow.axaml,ViewModels/*}`, [doc/architecture.md](architecture.md).

## 2026-06-24 _(doplněno zpětně)_

- **Hotovo (odstranění vlastní `Common.Matrix`):** smazána home-grown třída `ARBot.Common.Common.Matrix`
  (~2000 ř. netestovaného kódu); živí uživatelé přepsáni na **MathNet.Numerics** / **System.Numerics** —
  `ECEF` (už nededí z Matrix, čistý 3-vektor), `Transformation` (3×3/3×1 → `Matrix<double>`), `ICP`,
  `Intrinsics`, `MapWay`, `IEKFStepInfo` + `EKFStepMsg` (`Matrix<double>`, wire-format logu zachován).
  Mrtvý generický EKF framework (`EKF.cs`/`EKFStep.cs`) vyloučen z buildu. `d772350`, `a379776`.
- **Hotovo (migrace testů na NUnit):** staré VS/MSTest testy (`Point2DTest`, `Line2DTest`,
  `ConversionsTest`) převedeny na **NUnit** (constraint model `Assert.That`), přejmenovány do schématu
  `Metoda_Scenar_Ocekavani` a okomentovány; nový `Vector2DTest`; do `Point2D` doplněna value-rovnost
  (`IEquatable`, `==`/`!=`) + explicitní konverze na `Matrix<double>`. Charakterizační testy
  `Transformation`/`ECEF` přeneseny z legacy `Tests1` jako síť před migrací.
- **Rozhodnutí:** vlastní `Matrix` → **MathNet** (mikro-benchmark: MathNet ~2,5× rychlejší i na malých
  EKF maticích 6/13, prověřené dekompozice, méně údržby); pro fixní 3D geometrii zůstává System.Numerics.
- **Ověření:** build x64 + testy zelené (86 na tomto stavu).
- **Odkazy:** `Src/ARBot.Common/Coordinates/{ECEF,Transformation,Intrinsics}.cs`,
  `Src/ARBot.Common/SLAM/ICP.cs`, `Src/ARBot.Common/Logs/{EKFStepMsg,Message}.cs`,
  `Src/ARBot.Common.Tests/*Test.cs`.

## 2026-06-23 _(zpětně z gitu)_

- **Hotovo:** úvodní commit a **založení repa** (`.gitattributes`/`.gitignore`/README/LICENSE).
  `98d6c14`, `9b1be0e`.
- **Hotovo (OS/device bring-up Orange Pi 5 Ultra — Armbian/KDE; ops, mimo repo):** zprovozněna cílová
  HW platforma pro ARBot.

  ![OrangePi 5 Ultra, 8GB, Cooler](media/OrangePiUltra_Top.jpg)
  ![OrangePi 5 Ultra, NVME Disk 512 GB](media/OrangePiUltra_Bottom.jpg)
  
  Práce běžela napříč **~06-17→06-23** (rekonstrukce z časů souborů a Pi logů;
  v gitu jen anchor 06-23 — `setup-orangepi.sh` mtime + úvodní commit). V repu je z toho jen
  `OrangePi5Ultra/setup-orangepi.sh` + `POSTUP.md` — **idempotentní recovery celého setupu** pro případ
  reinstalace.
  - **RealSense SDK:** `librealsense` **2.53.1** zkompilován ze zdrojů (poslední verze s D435 **i** T265 —
    T265 odebrán v 2.54.1), RSUSB backend, flagy pro **GCC 15 / CMake 4** (`-DCMAKE_POLICY_VERSION_MINIMUM=3.5`,
    `-include cstdint`). **D435 i T265 ověřené na reálném HW** (`rs-enumerate-devices`). → podklad pro
    app-side 07-05 (D435 v appce) a 07-13 (T265 na Armbianu).
  - **USB:** „mrtvý" USB3-A port = OTG řadič (`fc000000`) default v peripheral režimu → overlay
    **`dwc3-host`** (OTG→host), port oživen a ověřen kamerou na USB3. 3. USB3 řadič `fcd00000` je natrvalo
    disabled (sdílí combo-PHY s onboard 2.5G ethernetem, `pcie@fe180000`) — ne vada, nechán být.
  - **SPI pro NeoPixel:** overlay `spi0-m2-cs0-spidev` → `/dev/spidev0.0` (WS2812 přes MOSI = GPIO1_B1).
    → podklad pro app-side `ArmbianSpiNeoPixelDriver` (07-08).
  - **GPU:** overlay `panthor-gpu` (Mali-G610/Valhall; panfrost ho neumí) → konec softwarového renderingu
    (`kwin_wayland` ~198 %→~11 % CPU).
  - **Síť/služby:** WiFi na backend **`iwd`** (wpa_supplicant 2.11 × Rockchip `bcmdhd` FullMAC = selhání
    WPA2 handshake) + systemd drop-in „počkej na `wlan0`" (SDIO race při bootu); Samba share (nahrávání
    appky / čtení logů); RustDesk direct-IP access (offline, port 21118); .NET 10 SDK; `nvtop`.
    WiFi-only kvůli ESET falešnému „ARP poisoning" při dual-homingu (ethernet+WiFi na jedné podsíti).
  - **Overlay háček:** overlaye `rk3588-*.dtbo` (dwc3-host, spi0) × `overlay_prefix=rockchip-rk3588` →
    nutno zkopírovat pod `rockchip-rk3588-*.dtbo`, jinak je Armbian tiše přeskočí (setup skript to řeší).
  - **Pozn.:** `POSTUP.md` byl později doplněn jinou session (mtime 07-03); `setup-orangepi.sh` zůstal
    z tohoto bring-upu (mtime 06-23).
- **Odkazy:** `OrangePi5Ultra/setup-orangepi.sh`, `OrangePi5Ultra/POSTUP.md`; návazné app-side dny
  07-05 (D435 v appce), 07-08 (NeoPixel driver), 07-13 (T265/VN100 na Armbianu).
