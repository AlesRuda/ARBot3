# Prezentace pro Google Sites (arbot.cz) — podklad k nakopírování

Doména **arbot.cz** míří na Google Sites a stránka se má poskládat **z nativních bloků Sites**,
ne vložením cizího HTML rámečkem. Tenhle soubor je proto *přepis* stránky
[../web/pages/prezentace.html](../web/pages/prezentace.html) do podoby, kterou jde v editoru Sites nakopírovat blok po bloku.

**Jak s tím pracovat**

- Text pod čarou v každém bloku se kopíruje **tak, jak je**. Nadpisy jsou označené, ať je jasné,
  co v Sites nastavit jako *Nadpis* / *Podnadpis* / *Normální text*.
- **Sites neumí tabulky ani SVG.** Obě tabulky z původní stránky jsou tu proto přepsané na odrážky
  a všechna čtyři schémata jsou vyexportovaná jako PNG (1600 px široké, světlé pozadí):
  - `media/prezentace-schema-smycka.png` — řídicí smyčka
  - `media/prezentace-schema-obraz-mapa.png` — ze dvou obrázků jedna mapa
  - `media/prezentace-schema-fuze.png` — sloučení senzorů
  - `media/prezentace-schema-simulace.png` — skutečný vs. virtuální hardware
- **Vyexportováno z původního SVG**, ne vyfoceno — text ve schématech je ostrý i po zvětšení.
  Když se schéma změní v `prezentace.html`, PNG se musí vyrobit znovu (postup dole).
- Fotky a snímky obrazovky jsou v `doc/media/` pod jmény uvedenými u jednotlivých bloků.

**Co se přechodem do Sites ztratí** (vědomě): tmavé téma, sazba (Newsreader + Archivo) a barevné
akcenty — Sites si text obarví a osadí podle svého motivu. Obsah a schémata zůstávají.

---

## Doporučené rozvržení stránky

Jedna dlouhá stránka, shora dolů. V Sites odpovídá zhruba tomuhle sledu sekcí:

| # | Sekce | Blok v Sites |
|---|---|---|
| 1 | Úvodní banner | *Banner* nebo *Titulek* |
| 2 | Čtyři otázky + schéma smyčky | text + obrázek přes celou šířku |
| 3 | Oči robota | text + obrázek + odrážky s čísly |
| 4 | Ze dvou obrázků jedna mapa | obrázek (schéma) s popiskem |
| 5 | Kde jsem + schéma fúze | text + obrázek |
| 6 | Samokalibrace kompasu | text (klidně *Skládací skupina*) |
| 7 | Kudy dál | text + dva snímky obrazovky |
| 8 | Jak jet | text |
| 9 | Tři mise | *Rozvržení* se třemi sloupci |
| 10 | Ovládání z mobilu | text + snímek |
| 11 | Simulace | text + schéma + snímek |
| 12 | Pod kapotou | odrážky + fotka desky |
| 13 | Kde to dnes je | text |

---

## 1 — Úvodní banner

*Nadpis stránky:*

ARBot — robot, který si cestu najde sám

*Podnadpis:*

Dostane zeměpisné souřadnice cíle a dojede tam po chodnících a pěšinách. Bez řidiče, bez dálkového ovládání, bez čáry na zemi.

*Normální text pod banner:*

Je to malý robot na kolech (rozchod 41 cm) s počítačem velikosti krabičky od cigaret. Všechno, o čem je řeč dál, počítá on sám, na místě, bez internetu a bez cloudu.

*Čtyři čísla — v Sites jako rozvržení se čtyřmi sloupci, v každém tučné číslo a pod ním popisek:*

- **10×** — za sekundu se rozhodne, kam jet dál
- **2,7 ms** — mu trvá poznat v obraze cestu
- **5 cm** — velká je jedna buňka jeho mapy okolí
- **1 600+** — automatických testů hlídá, že se nic nerozbilo

---

## 2 — Čtyři otázky, které si robot pořád dokola odpovídá

*Nadpis:*

Čtyři otázky, které si robot pořád dokola odpovídá

*Text:*

Člověk projde po chodníku na konec ulice, aniž by o tom přemýšlel. Robot musí tu samou procházku rozložit na čtyři otázky — a zodpovědět je znovu a znovu, desetkrát za sekundu, celou dobu jízdy.

- **Co je kolem mě?** Kde končí cesta a začíná tráva, kde stojí lavička, kde je schod.
- **Kde jsem?** Ne „v Praze“, ale s přesností na decimetry a na pár stupňů natočení.
- **Kudy dál?** Jak se dostat k cíli po síti cest — a jak se přitom vyhnout tomu, co je právě teď v cestě.
- **Co mají udělat kola?** Jaké otáčky poslat motorům, aby robot projel po naplánované dráze a nepřevrátil se na rohu.

Jedno kolečko těch čtyř kroků trvá desetinu sekundy. Za tu dobu robot ujede pár centimetrů — a všechno se počítá znovu, protože svět už vypadá jinak.

*Obrázek přes celou šířku:* `media/prezentace-schema-smycka.png`

*Popisek obrázku:*

Řídicí smyčka. Vlevo to, co robot měří; vpravo to, co s tím udělá. Celé kolečko proběhne desetkrát za sekundu a nikdy se nezastaví — i stání je rozhodnutí, které se musí každou desetinu sekundy potvrdit.

---

## 3 — Oči robota

*Nadpis:*

Dvě oči: jedno měří vzdálenost, druhé rozumí tomu, co vidí

*Text:*

Robot má dvě kamery natočené mírně od sebe, aby dohromady pokryly široký výsek před ním. Každá z nich posílá třicetkrát za sekundu dva obrazy: normální barevnou fotku a takzvanou hloubkovou mapu — obrázek, ve kterém každý pixel neříká barvu, ale vzdálenost.

To jsou dvě různé informace a obě jsou potřeba. Hloubka pozná tvar: že je něco vyvýšené, že tam je schod, sloup, noha lavičky. Ale nepozná rozdíl mezi asfaltem a posekaným trávníkem — ten je z pohledu geometrie stejně rovný jako cesta. To je práce pro barvu.

*Podnadpis:*

Rozpoznat cestu v obraze

*Text:*

Původní řešení bylo jednoduché: naučit se, jakou barvu má cesta, a podle toho každý pixel zařadit. Funguje to překvapivě dobře — a přesně tak dlouho, dokud na cestu nedopadne stín, nespadne listí nebo nezačne pršet.

Dnes tu práci dělá neuronová síť. Je to stejný druh programu, jaký ve fotkách v telefonu pozná obličeje: dostala k vidění tisíce ručně obarvených snímků cest a naučila se z nich rozhodovat ne podle jednoho pixelu, ale podle celého okolí — podle tvaru, textury a souvislostí. Výsledek není seznam pixelů, ale souvislá plocha: tudy se dá jet.

*Obrázek přes celou šířku:* `media/segmentace-srovnani-20260907.png`

*Popisek obrázku:*

Co robot vidí a co si o tom myslí. Vlevo vstupní snímek, pak tři různé způsoby, jak v něm najít cestu — bílá je „projedu“, černá „neprojedu“. Barevný histogram (druhý sloupec) rozhoduje o každém pixelu zvlášť, a tak zrní a na okraji trávy se láme. Neuronové sítě (třetí a čtvrtý sloupec) dají souvislou plochu — a všimněte si třetího řádku: kolemjdoucího člověka poznají jako překážku, histogram ho z poloviny přehlédne.

*Text (původně tabulka — v Sites jako odrážky):*

Kolik to vydělá, je změřené na sadě padesáti snímků, u kterých člověk ručně vyznačil, kde cesta doopravdy je:

- **Barevný histogram** — 78,0 % správně určených pixelů
- **Neuronová síť** — 88,2 %, a hlavně 2,6× méně často si vymyslí cestu tam, kde žádná není
- **Větší síť** — 96,7 %, ale spotřebuje tolik výkonu, že klesne počet snímků za sekundu

Ten druhý bod je důležitější než první. Když robot cestu přehlédne, jede pomaleji a opatrněji. Když si ji vymyslí, sjede do trávy.

*Text na zvýraznění (v Sites se hodí Skládací skupina nebo jiné pozadí sekce):*

**Proč to stihne.** Počítač v robotovi má vedle běžného procesoru i malý čip určený právě na neuronové sítě. Na procesoru by síť běžela 9,4 ms, na tomhle čipu 2,7 ms — a obě kamery dohromady si tak vezmou jen kousek výkonu, který zbývá na všechno ostatní. Bez toho by robot musel volit mezi „vidí dobře“ a „stíhá řídit“.

---

## 4 — Ze dvou obrázků jedna mapa

*Obrázek přes celou šířku:* `media/prezentace-schema-obraz-mapa.png`

*Popisek obrázku:*

Hloubka řekne, kde něco je; barva řekne, co to je. Robot je slévá do jedné mřížky kolem sebe — a buňku prohlásí za volnou, jen když se obě větve shodnou. Radši se jednou zbytečně zastaví, než aby jednou vjel do keře.

---

## 5 — Kde jsem

*Nadpis:*

Žádný senzor neříká pravdu. Tak je robot poslouchá všechny najednou

*Text:*

Tohle je nejméně vidět a nejvíc na tom záleží. Kdyby si robot myslel, že je o metr vedle, je mu jeho pěkná mapa okolí k ničemu — naplánuje si krásnou cestu do trávy.

Problém je, že každý senzor lže jinak:

- **GPS** se plete zhruba o metr a půl a mezi měřeními poskakuje. Pod stromy a mezi domy hůř.
- **Kompas** ukazuje směr, ale ovlivňuje ho železo v samotném robotu. Naměřená chyba: 24 stupňů.
- **Otáčky kol** řeknou přesně, kolik se kola otočila — ne, kolik robot ujel. Na mokré trávě se prokluz sčítá a chyba roste s každým metrem.
- **Gyroskop** velmi přesně měří, jak rychle se robot otáčí. Ale ne, kam míří: drobná chyba se integruje a po pár minutách je kurz úplně mimo.

Řešení se jmenuje Kalmanův filtr a myšlenka za ním je jednoduchá: každý senzor musí kromě naměřené hodnoty říct i to, jak moc si je jistý. Filtr pak neposlouchá jeden, ani většinu — váží je. Přesný údaj má velkou váhu, nejistý malou. Výsledkem je jediný odhad polohy a natočení, ke kterému si robot navíc nese, jak moc mu věří.

Druhá polovina triku je, že filtr umí předpovídat. Ví, jaké dal motorům povely, takže si dopředu spočítá, kde by měl za desetinu sekundy být. Když pak přijde měření z GPS, neptá se „kde jsem?“, ale „jak moc se to liší od toho, co jsem čekal?“. Díky tomu přežije i chvíli, kdy GPS úplně vypadne — třeba pod mostem.

*Obrázek přes celou šířku:* `media/prezentace-schema-fuze.png`

*Popisek obrázku:*

Čtyři nespolehliví svědci, jeden rozsudek. Váhy nejsou pevné — mění se za jízdy. Když GPS ohlásí slabý signál, filtr jí sám od sebe přestane věřit a víc se opře o kola a gyroskop.

---

## 6 — Samokalibrace kompasu

*Podnadpis:*

Robot, který si sám zkalibruje kompas

*Text:*

Kompas byl dlouho nejhorší článek. Měření ukázalo, že se plete o 24 stupňů, a co hůř, chyba se měnila podle toho, kterým směrem robot mířil — klasický projev železa a magnetického pole ve vlastní konstrukci. Filtr přitom kompasu věřil na desetinu stupně, protože senzor sám o sobě tvrdil, že je takhle přesný.

Kalibrace takového senzoru je jinak práce pro laboratoř. Tady ji dělá sám robot: spustí se mu kalibrační mise, obsluha ho vezme do rukou a pomalu s ním otáčí — a na stránce, kterou robot servíruje do telefonu, průběžně vidí, kterým směrem ho ještě musí natočit. Když má dost dat, robot si kalibraci spočítá a sám ji zapíše do paměti senzoru.

Výsledek změřený venku: chyba kurzu klesla z 24 ° na 3 ° a zkreslení od vlastního železa ze 27 ° na 3,6 °. Celé to trvá pár minut a jde to udělat v terénu, s telefonem v ruce, bez notebooku.

---

## 7 — Kudy dál

*Nadpis:*

Plánuje se na dvou úrovních: mapa města a nejbližších šest metrů

*Text:*

Kdyby robot plánoval jenom podle mapy, vjel by do každého odstaveného kola. Kdyby plánoval jenom podle toho, co vidí, dojel by na první křižovatku a nevěděl by, kam zabočit. Takže dělá obojí zároveň — a každé jinak rychle.

*Podnadpis:* Nahoře: trasa po mapě

*Text:*

Robot má v sobě mapu z OpenStreetMap — tu samou volně dostupnou mapu, ze které čerpá spousta navigací. Není to ale mapa pro auta: zajímají ho chodníky, pěšiny a parkové cesty. Z cíle si spočítá trasu po síti cest a z ní vytáhne jediné číslo, které zajímá spodní vrstvu: bod pár metrů před sebou, kterým se má trasa vydat. V projektu se mu říká „mrkev“ — robot za ní jde jako za mrkví na provázku.

Když se ukáže, že je cesta přehrazená, robot si tu hranu v mapě uzavře a trasa se přepočítá jinudy. Mapa tak není jen čtená, ale i opravovaná podle toho, co robot skutečně zažil.

*Podnadpis:* Dole: mřížka kolem robota

*Text:*

Ta mapa okolí je čtvercová síť buněk 5 × 5 cm, která robota obklopuje a jede s ním. V každé buňce není „ano/ne“, ale pravděpodobnost: čím víckrát tam kamera uvidí volno, tím je si robot jistější. Jeden šum v jednom snímku tak mapu nerozhodí, ale skutečná lavička, kterou vidí desetkrát po sobě, ano.

Nad touhle mřížkou běží hledání cesty (algoritmus A*, tentýž, který používají navigace i hry) — jenže nehledá jen nejkratší cestu, ale takovou, která si drží odstup od překážek. A na každý kousek dráhy se ještě spočítá, jak rychle se tudy smí: blíž u překážky pomaleji, do neprozkoumaného prostoru pomaleji, na volném prostranství naplno.

*Obrázek:* `media/robot-centric-grid.png`

*Popisek obrázku:*

Co robot „vidí“ po zpracování. Žlutá značka dole uprostřed je robot, kroužky jsou vzdálenosti po metrech. Zelené body jsou sjízdné, červené překážka — tady zrovna šikmá hrana obrubníku asi metr a půl před robotem.

*Obrázek:* `media/headless-plan-view-prehled-20260912.png`

*Popisek obrázku:*

Tentýž robot o dvě úrovně výš. Šedě síť cest z OpenStreetMap, fialově trasa k cíli, červeně robot, žlutě „mrkev“, za kterou právě jde. Tenhle obrázek si robot kreslí sám a posílá ho do telefonu.

*Text na zvýraznění:*

**Historka o tom, proč se všechno měří.** Robot se jednou při zkušební jízdě venku „plazil“ — jel pětinou rychlosti, kterou měl. Nikdo nevěděl proč; z chování to vypadalo na opatrnost kamer. Rozbor záznamu ukázal něco jiného: krok, který dráhu uhlazuje do plynulé křivky, zahazoval odstup od překážek, který mu plánovač pracně vykoupil. Robot tak jel 40 cm od věcí, které měl objet obloukem, a rychlostní pravidlo ho proto drželo dole. Po opravě: odstup 0,40 → 0,49 m, rychlost 0,05 → 0,49 m/s. Desetkrát rychleji, a bezpečněji zároveň.

---

## 8 — Jak jet

*Nadpis:*

Mezi plánem a koly

*Text:*

Naplánovaná dráha je čára na papíře. Poslední vrstva z ní dělá otáčky dvou kol — a je to jediné místo, kde chyba znamená ránu do obrubníku, ne špatné číslo v logu.

Robot si dráhu nerozdělí jen na „jeď tam“. Plán obsahuje i brzdnou obálku: pro každý bod dráhy nejvyšší rychlost, se kterou tam smí přijet, aby ještě stihl zastavit nebo projet zatáčku. Při jízdě k tomu přidá předstih — nemíří na bod, na kterém stojí, ale na bod kousek před sebou, přesně jako člověk, který se při řízení nedívá na kapotu.

*Podnadpis:* Tři nezávislé způsoby, jak robota zastavit

- **Nouzové tlačítko** na robotu. Je zapojené v hardwaru a software ho nemůže obejít. Nic dál v tomhle seznamu není jeho náhrada.
- **Držené zastavení.** Kterákoli část programu si může robota „podržet“ — a robot stojí, dokud ho drží kdokoli. Nebrzdí se přitom naplno, ale rampou, aby se náklad nepřevrátil.
- **Zastavení z telefonu.** Červené tlačítko na stránce, kterou robot servíruje.

*Text:*

To „držené zastavení“ zní jako drobnost, ale je za ním zajímavá vlastnost: robot umí sám sebe zastavit, opravit se a jet dál. Když zamrzl obraz z jedné kamery, dohlížecí část programu si robota podržela, zbourala a znovu postavila spojení s kamerami a hold pustila. Od poruchy k plné funkci to trvalo 29 sekund. Předtím ta samá porucha znamenala mrtvou kameru na 22 minut, než si toho někdo všiml a restartoval program.

---

## 9 — Tři mise

*Nadpis:*

Tři mise nad stejným základem

*Text:*

Všechno předchozí je motor. Mise je to, co mu řekne, kam. Jsou tři a liší se jen tím, odkud berou cíl — zbytek programu se pro ně nemění.

*V Sites: rozvržení se třemi sloupci, v každém malý nadpis a odstavec.*

**Volná jízda** (bez mapy)
Robot si najde cestu před sebou a jede její pravou polovinou. Překážkám se vyhne podle toho, co vidí. Když cesta zmizí, drží směr. Hodí se na přesuny a na soutěžní homologaci.

**Objezd míst** (podle seznamu)
Textový soubor se souřadnicemi, řádek na místo. Robot je objede v pořadí a případně dokola. Každé místo si předem přichytí na nejbližší cestu — a když některé leží moc daleko od jakékoli cesty, řekne to hned na startu, ne až u něj.

**Doručení zásilky** (soutěžní)
Depo → nakládka → vykládka → zpět. Cíl si robot přečte z QR kódu, který mu obsluha ukáže před kamerou. Jede bez operátora; jediné lidské vstupy jsou ten papír s kódem a stop tlačítko.

---

## 10 — Ovládání z mobilu

*Nadpis:*

Celý robot se ovládá z mobilu, přes prohlížeč

*Text:*

Na robotovi neběží žádná aplikace s okny — je to služba, která nastartuje sama s počítačem. Místo obrazovky servíruje obyčejnou webovou stránku: půdorys s trasou, živý snímek z kamery, stav senzorů, rychlost, poloha, zatížení procesoru. Stačí se připojit na stejnou síť a otevřít ji v telefonu. Nic se neinstaluje.

Jedna věc je na té stránce schválně nepohodlná. Misi jde vybrat jen ve chvíli, kdy je stisknuté nouzové zastavení — a robot se rozjede teprve tehdy, když ho člověk uvolní. Stránka tedy misi jen připraví; rozjet ji musí ruka u robota. Robot, který se rozjede sám od sebe, protože si někdo na druhém konci města ťukl do telefonu, je horší než robot, který stojí.

*Obrázek:* `media/headless-web-nahled.png`

*Popisek obrázku:*

Stránka, kterou robot sám servíruje. Nahoře přepínače mezi půdorysem, kamerou a pohledem na cestu, pod tím živý obraz a všechna čísla, která se jinak dají číst jen z logu. Vpravo nahoře červené Zastavit robota. Když se na stránku nikdo nedívá, robot ji ani nekreslí — aby tím netrávil výkon potřebný na řízení.

---

## 11 — Simulace

*Nadpis:*

Robot, který jezdí uvnitř počítače

*Text:*

Kdyby se každá změna musela zkoušet venku na trávě, byl by vývoj nekonečný. Proto má robot druhé tělo: úplně stejný program, ale místo kamer, GPS a motorů má simulaci.

Není to hra ani animace. Virtuální kamera bere mapu z OpenStreetMap a renderuje z ní to, co by robot ze své polohy skutečně viděl — barevný obraz i hloubku. Virtuální kola se otáčejí podle stejných povelů, které by dostal motorový regulátor. Řídicí program o tom vůbec neví: dostává data ze stejných rozhraní jako na železe.

Tím se ale otevře něco, co venku nikdy nejde: v simulaci se ví, kde robot doopravdy je. Můžete tedy změřit, o kolik se jeho vlastní odhad plete — a to je jediný způsob, jak zjistit, jestli byla změna zlepšení, nebo zhoršení. Navíc se dají poruchy zadat: prokluz kol, vychýlený kompas, chyba polohy o zvolený kus. Jde dokonce nechat kamery kreslit podle jiné mapy, než podle které robot jede — takže chyba je prokazatelně v datech, ne v pozorovateli.

*Obrázek přes celou šířku:* `media/prezentace-schema-simulace.png`

*Popisek obrázku:*

Jeden šev, dvě těla. Výměna proběhne na jediné vrstvě rozhraní — nad ní je program v obou případech bit po bitu stejný. To je podmínka, aby výsledky ze simulace vůbec něco znamenaly.

*Obrázek:* `media/road-edges-image-20260823.png`

*Popisek obrázku:*

Pohled virtuální kamery. Zjednodušený svět — šedá cesta, zelená tráva — ale vyrobený z opravdové mapy a z opravdové polohy robota, a poslaný do programu úplně stejnou cestou jako obraz ze skutečné kamery.

*Podnadpis:* A všechno se nahrává

*Text:*

Každá jízda se zapisuje: všechny zprávy ze všech senzorů, všechna rozhodnutí, s časovými razítky. Když se venku stane něco divného, nemusí se to zkoušet znovu — záznam se u stolu přehraje a je vidět přesně to, co v tu chvíli viděl robot. Nad tím vším hlídá přes 1 600 automatických testů, které proběhnou při každé změně. Většina z nich nekontroluje, jestli program spadne — ale jestli pořád vrací tatáž čísla jako dřív.

---

## 12 — Pod kapotou

*Nadpis:*

Co v tom vlastně běží

*Text (původně tabulka — v Sites jako odrážky):*

- **Jazyk** — C# / .NET 10, jeden zdrojový kód pro vývojový počítač s Windows i pro robota s Linuxem na ARM
- **Počítač robota** — Orange Pi 5 Ultra: osm jader a zvláštní čip na neuronové sítě, spotřeba řádu wattů
- **Kamery** — dvě hloubkové kamery Intel RealSense, k tomu sledovací kamera pro odhad vlastního pohybu
- **Ostatní senzory** — inerciální jednotka VN100 (kompas + gyroskop + akcelerometr), GPS u-blox, motorový regulátor Roboteq
- **Mapy** — OpenStreetMap, i offline; robot nepotřebuje internet
- **Vývojová aplikace** — desktopová aplikace s dokovatelnými panely: mapa, obraz z kamer, telemetrie, přehrávání záznamů

*Obrázek:* `media/OrangePiUltra_Top.jpg`

*Popisek obrázku:*

Celý mozek. Deska o velikosti dlaně pod měděným chladičem. Běží na ní vnímání, fúze, plánování, řízení, webová stránka i neuronová síť — a ve chvílích, kdy robot jen čeká na povel, zatěžuje procesor asi 6 %.

---

## 13 — Kde to dnes je

*Nadpis:*

Kde to dnes je

*Text:*

Jádro běží. Robot venku jezdí, vidí, lokalizuje se, kreslí si mapu okolí, drží se cesty a servíruje svou stránku do telefonu; služba startuje sama, přežije vypnutí i výpadek kamery. Kompas je zkalibrovaný a ověřený třemi nezávislými měřeními.

Část toho, co je výš popsané, je ale zatím ověřená hlavně v simulaci a měřením nad záznamy — celý průchod soutěžní misí na skutečném robotu teprve čeká. Je to průběžně vedený projekt, ne hotový produkt.

---

## Jak znovu vyrobit PNG ze schémat

Schémata jsou v [../web/pages/prezentace.html](../web/pages/prezentace.html) jako inline SVG. Export dělá headless Chrome
(žádná další knihovna není potřeba):

1. Z `prezentace.html` se vytáhne každý `<svg viewBox="0 0 W H">…</svg>` do samostatného
   dočasného HTML s vynucenou **světlou** paletou (`--surface`, `--accent`, `--free`, `--block`,
   `--plan`) a bílým pozadím; SVG se nastaví `width: 1600px`.
2. `chrome.exe --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=1
   --virtual-time-budget=6000 --window-size=1600,<H·1600/W> --screenshot=<out.png> file:///<tmp.html>`

`--virtual-time-budget` tam je proto, aby se stihl načíst font Archivo z Google Fonts — bez něj
se schéma vysází náhradním písmem. Hotová PNG patří do `doc/media/` pod jméno
`prezentace-schema-*.png`; **nepřepisuj jimi jiné obrázky** (pravidlo z hlavičky
[devlog.md](devlog.md)).
