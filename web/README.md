# Web arbot.cz (GitHub Pages)

Statický web projektu. **Tenhle adresář je to, co se publikuje** — GitHub Pages ho servíruje
tak, jak je, žádný build ani generátor se nepouští. Publikuje ho workflow
[.github/workflows/pages.yml](../.github/workflows/pages.yml) při každém pushi do `master`,
který se dotkne `web/`.

⚠️ **Jméno složky je `web/`, ne `docs/`** (přejmenováno 17. 9. 2026): vedle `doc/` s vývojovou
dokumentací byla `docs/` past. Cena: režim *Deploy from a branch* umí jen `/` nebo `/docs`,
proto se publikuje přes GitHub Actions, kde se složka zadá ve workflow.

```
web/
  index.html                 úvodní stránka  ->  arbot.cz/
  pages/*.html               podstránky      ->  arbot.cz/pages/<jméno>.html
  assets/site.css            sdílená sazba a barvy všech stránek
  assets/img/                obrázky webu
  .nojekyll                  vypíná Jekyll (servíruje se přesně to, co tu leží)
```

Dva soubory v repozitáři jsou **generované** a ručně se needitují (podrobnosti v sekcích níž):
`pages/historie.html` celá (`tools/ukoly.cs` z `doc/ukoly.yaml`) a **hlavička s menu ve všech
stránkách** (`tools/menu.cs`). Generují se ale **do repozitáře**, ne až při publikaci, takže
předchozí odstavec platí dál: co tu leží, to se publikuje.

Stránky jsou **obyčejné HTML soubory bez frameworku a bez JavaScriptu**; jediná externí věc jsou
fonty z Google Fonts. ⚠️ **Jediná výjimka je `pages/historie.html`** (od 17. 9. 2026, rozhodnutí
autora): inline skript bez knihoven pro filtr podle stavu a hledání v textu; bez JS stránka
funguje celá, jen bez filtru. Je generovaný (`tools/ukoly.cs`), nepiš ho ručně. Všechny odkazy jsou **relativní**, takže web funguje stejně na
`arbot.cz` i na `alesruda.github.io/ARBot3/` — díky tomu jde nová verze prohlédnout dřív,
než se přepne doména.

## Jak to zapnout

1. **GitHub → Settings → Pages → Build and deployment**: Source = *GitHub Actions* (ne
   *Deploy from a branch* — ten neumí složku `web/`). Workflow `pages.yml` pak při dalším pushi
   do `master` (nebo ručně přes *Actions → web (GitHub Pages) → Run workflow*) web nasadí na
   `https://alesruda.github.io/ARBot3/`.
2. **Napřed si to na té adrese prohlédni.** Teprve až bude vše v pořádku, přepni doménu (krok 3).
3. **Vlastní doména.** V *Settings → Pages → Custom domain* zadej `www.arbot.cz`
   (GitHub si tím sám založí soubor `web/CNAME`) a zaškrtni *Enforce HTTPS*. V DNS u registrátora:
   - `www` → `CNAME` → `alesruda.github.io.`
   - apex `arbot.cz` → čtyři `A` záznamy na IP GitHub Pages (aktuální seznam je
     v dokumentaci GitHubu, *Managing a custom domain for your GitHub Pages site*),
     případně `ALIAS`/`ANAME`, pokud to registrátor umí.

   ⚠️ **Soubor `CNAME` tu schválně zatím není.** Jakmile vznikne, GitHub přesměruje
   `alesruda.github.io/ARBot3/` na vlastní doménu — a dokud nebude hotové DNS, nebyl by web
   dostupný ani na jedné z těch adres. Proto až jako poslední krok.
4. **Až bude nový web živý, zrušit publikaci Google Sites**, ať nezůstane druhá verze obsahu.

## Úvodní stránka

Přepsaná 16. 9. 2026. Stavba shora dolů: **hero** (text + fotka robotu + pás čtyř čísel),
**Co ARBot je a kam míří**, **Co robot vidí** (segmentace z kamer, půdorys z náhledu),
**Sedm prvních míst**, **Rozcestník** na šest karet.

⚠️ **Čísla v pásu nejsou dekorace — každé má zdroj** a při změně se musí přeměřit, ne odhadnout:

| Číslo | Co znamená | Odkud |
|---|---|---|
| `10×/s` | perioda řídicí smyčky | [perf-monitoring.md](../doc/perf-monitoring.md) |
| `2,7 ms` | inference sítě na NPU Orange Pi | [semantic-segmentation.md](../doc/semantic-segmentation.md), měřeno 9. 9. 2026 |
| `4,09 km` | nejdelší autonomní jízda | Robotour Marathon 2023 (robotika.cz), vítězná |
| `7×` | první místa v soutěžích | tabulka na *Umístění v soutěžích* |

⚠️ **U rekordů ověřuj poslední ročník.** První verze pásu nesla 1 595 m z Marathonu 2020 —
číslo bylo správně, jen o tři roky staré, a vypadalo by to, že robot od té doby nic nedokázal.

**Fotka v hero** (`assets/img/hero-robot.jpg`, 720 × 900) je od 26. 9. 2026 **verze PI na
Robotouru 2026** u Planetária ve Stromovce: výřez z originálu fotky `verze-pi-01.jpg`
(3060 × 4080 po otočení podle EXIF, celá šířka, výška 3825 od y = 128, pak zmenšení na 720 × 900;
uloženo bez metadat). Do té doby tu byl výřez z `verze-u2-2019-05.jpg` z obýváku, protože fotka
aktuálního robotu v terénu v repu nebyla.

⚠️ **Text úvodu tvrdil něco, co neplatí.** Do 16. 9. 2026 tu stálo, že cílem bylo „to co dokáže
mnohý hmyz" a že *„postupem času byl cíl přeformulován"* na soutěže. Podle autora se robot
**od začátku stavěl pro Robotour** — a potvrzuje to i stránka *Verze*, kde si soutěž vybírá
dřív, než sestaví seznam součástek. Nová verze proto začíná Robotourem a vysvětluje, že jde
o simulaci doručování (soudek piva, cíl z QR kódu).

## Menu je na jednom místě (od 18. 9. 2026)

Hlavní menu žije v **`tools/menu.cs`** a ten ho vypíše do bloku `<header class="sitehead">`
**ve všech** `web/**/*.html`:

```bash
dotnet run tools/menu.cs
```

⚠️ **Pořadí proti registru úkolů je povinné.** `tools/ukoly.cs` vyrobí `pages/historie.html`
s **prázdnou** hlavičkou a naplní ji až `menu.cs`; obráceně by `menu.cs` naplnil starou verzi
souboru a `ukoly.cs` by ji zase vyprázdnil. CI (`build-and-test.yml`, job `generovane-soubory`)
pouští obojí v tomhle pořadí a pak `git diff --exit-code -- doc/ukoly.md web/`, takže **ručně
upravená hlavička spadne**, stejně jako zapomenuté spuštění.

**Proč generátor a ne šablony.** Hlavička byla opsaná v každé stránce zvlášť — 21 souborů plus
kopie v `tools/ukoly.cs`, tedy 22 míst, a rostlo to s každou novou stránkou. Jekyll (`_layouts`)
ani skládání stránek až ve workflow by sice šlo, ale obojí by porušilo to hlavní, co o téhle
složce platí: **`web/` je přesně to, co se publikuje**, takže se dá prohlédnout lokálně a rozbitý
výstup se pozná před pushem, ne až na živém webu. Generátor tuhle vlastnost zachovává — v souborech
pořád leží hotové HTML, jen ho nepíše ruka.

**Co generátor drží navíc:** `class="on"`. Stránka, která v menu vlastní položku nemá, je zapsaná
ve **skupině** (články o soutěžích → *Umístění v soutěžích*, technické články → *Technické články*)
a generátor zvýrazní tu. Dřív se to udržovalo ručně a bylo to celé pravidlo uložené jen v hlavě.

⚠️ **Konce řádků se přebírají ze souboru**, ne natvrdo LF: `.gitattributes` má `* text=auto`,
takže pracovní kopie je na Windows CRLF a na CI LF. Pevné `\n` by na Windows vyrobilo míchané
konce řádků uvnitř jinak CRLF souboru a `git diff` v CI by pak hlásil rozdíl, který v repu není.

⚠️ **Generátor nepřepisuje nic jiného než ten blok** — text stránek, `<title>`, MathJax
i konfigurace v hlavičce souboru zůstávají ruční práce.

## Technické články (od 18. 9. 2026)

`pages/technicke-clanky.html` je **rozcestník**: v hlavním menu je jedna položka a teprve za ní
je seznam článků jako karty (`.linkcards`, stejná stavba jako rozcestník na úvodu). Dnes jsou tam
tři — *Model diferenciálního podvozku*, *Detekce kraje vozovky*, *Regulátor sledování dráhy*.

**Proč rozcestník a ne rozbalovací podmenu v liště:** lišta by rostla donekonečna — po deseti
článcích by se na mobilu zalomila na tři řádky a hlavní členění webu by v ní zaniklo. Dropdown
by k tomu chtěl vlastní CSS a na dotykovém displeji se hover chová hůř, kdežto rozcestník funguje
všude stejně a bez JS. ⚠️ **Původní důvod byl ještě jiný** („menu je natvrdo ve 22 místech, takže
každý článek = 22 editací“) a **ten už neplatí** — menu má od téhož dne generátor (sekce výš).
Rozhodnutí na tom ale nestojí: platí i s generátorem.

⚠️ **`class="on"` se na článcích dává položce *Technické články***, ne článku samotnému — ten
v menu žádnou položku nemá. Bez toho by na článku nebylo zvýrazněné nic a vypadalo by to, že
návštěvník vypadl ze struktury webu. **Od 18. 9. 2026 to nikdo neudržuje ručně** — stránka je
zapsaná ve skupině v `tools/menu.cs` a zvýraznění vyrábí generátor.

Články mají společný tvar: `pagehead` (eyebrow / h1 / lead), text, číslované vzorce v `.mathblock`
(MathJax se načítá z CDN, konfigurace je v hlavičce každého článku), vysvětlivky symbolů
v `.legend`, schémata jako inline SVG a poznámky v `.note`.

## Šířka obsahu

Text i obrázky mají **jednu společnou šířku 1040 px** (sjednoceno 14. 9. 2026). Řídí to jediná
mřížka v `assets/site.css` u selektoru `main`; třída `.wide` v HTML zůstala kvůli existujícímu
značení, ale míří do téže dráhy. `.full` je jediná výjimka — táhne se přes celé okno (hlavička,
hero, patička).

⚠️ **Řádek textu má při té šířce ~104 znaků**, tedy skoro dvojnásobek typograficky pohodlných
55–65. Proto je písmo o stupeň větší (20 px) a proklad vyšší (1,68). Kdyby to bylo na čtení moc
dlouhé, jsou dva regulátory, obojí na jednom místě v `site.css`: **šířka** `minmax(0,1040px)`
v mřížce a **velikost písma** v `body`.

**Obrázky menší než sloupec** se nenatahují přes 2× své skutečné rozlišení, jinak změknou;
užší obrázek se ve sloupci **vystředí**. Strop patří na `<img>`, ne na `<figure>`
(na rámu by z něj udělal shrink-to-fit blok a obrázek by se scvrkl na svou vlastní šířku):

| Obrázek | Nativně | Strop | Proč |
|---|---|---|---|
| `podvozek-schema.png` | 211 × 424 | 420 px | 2× |
| `podvozek-varianty-pohybu.png` | 445 × 418 | 890 px | 2× |
| `podvozek-derivace.png` | 482 × 343 | 964 px | 2× |
| `detekce-okraj-cesty.png` | 319 × 240 | 638 px | 2× |
| `headless-web-nahled.png` | 600 × 1120 | 600 px | přes celý sloupec by byl 1,9 m vysoký telefon |
| `clanky/robotem-rovne-2010.png` | 320 × 256 | 640 px | 2× |
| `clanky/robotour-2013.jpg` | 220 × 472 | 440 px | 2× |

Ostatní obrázky sloupec vyplní.

⚠️ **Každý `<img>` musí mít `width` a `height`** — rozměry skutečného souboru. Bez nich obrázek
do načtení nezabere místo, rám se scvrkne a po doskrolování stránka poskočí; u pásů fotek se
dokonce složí do jednoho sloupce. Platí pro všechny obrázky na webu, ne jen pro náhledy v pásech.

## Tmavý podklad

Web je **záměrně jednotematický — tmavý**, ať ho divák otevře s jakýmkoli nastavením systému.
V `site.css` proto **není** `@media (prefers-color-scheme)` ani `[data-theme]`; celá paleta je
v jediném `:root` a `color-scheme:dark` přepne i posuvníky a formulářové prvky.

⚠️ **Kdyby se někdy přidávala světlá varianta, nestačí prohodit barvy** — musely by se s ní
překreslit i obrázky níže, jinak budou tmavé kresby na světlém pozadí nečitelné.

### Schémata jsou inline SVG, ne obrázky

Tři schémata na stránce *Model diferenciálního podvozku* (poloha v krocích k a k+1, varianty
pohybu, sečna vs. tečna) a tři na stránce *Regulátor sledování dráhy* (roh s vepsaným obloukem,
graf brzdné obálky, schéma zpětné vazby omezovače) jsou **ručně psané inline SVG přímo v HTML**,
stejně jako schémata na
stránce *Jak to funguje*. Barvy berou z palety (`currentColor`, `var(--accent)`, `var(--plan)`,
u regulátoru navíc `var(--block)` a `var(--free)` na rozlišení vady a léčby),
takže se o tmavý podklad starat nemusí, jsou ostré v každém zvětšení a opraví se textovým editorem.

⚠️ **Souřadnice ve schématech s grafem nebo geometrií nepiš od oka** — u regulátoru jsou dopočítané
skriptem z týchž vzorců, které stránka odvozuje (poloměr oblouku, body brzdné obálky), takže se
obrázek nemůže rozejít s textem. Pro ruční úpravu to znamená: měň popisky a čáry, ale ne čísla
v `points`/`d`, dokud si je nepřepočítáš.

⚠️ **Předtím to byly překreslené rastry a nevypadalo to dobře** — proto se od převádění obrázků
u kreseb ustoupilo úplně. Světlé originály ze Sites zůstávají v `assets/img/podvozek-*.png` jako
předloha, ale web je nepoužívá.

Šířku schématu řídí třída na obalu: `.diagram.uzky` (560 px) nebo `.diagram.stredni` (760 px);
bez nich se schéma roztáhne přes celý sloupec.

### Obrázek, který se kvůli tmavému podkladu přebarvoval

Zbyl jediný: **`arbot-model-dark.gif`** (animovaný model robota; do 16. 9. 2026 byl na úvodní
stránce, dnes ho web nepoužívá — model je podle autora zastaralý a čeká se na render
z Fusionu 360). Invertovat ho
nešlo — žlutý robot by zmodral — takže se jen **přepsala paleta**: položky do vzdálenosti 90 od
krémové `(255,255,229)` se posunuly na barvu stránky, robot zůstal beze změny. Přepisuje se přímo
v bajtech souboru, ne přeuložením PIL — to nafouklo 1,28 MB na 2,46 MB. Originál zůstává v repu.

⚠️ **Nestačí globální paleta: GIF má 72 lokálních palet**, jednu na snímek. Když se přepíše jen
globální, první snímek je správně a zbytek zůstane krémový. Kontrola, která to spolehlivě chytne:
po převodu **nesmí v celém souboru zbýt ani jedna krémová položka** (ověřeno: 0 z 12 288).
⚠️ **Snímky GIFu nekontroluj v PIL** — vykreslí i nedotčený originál jako barevnou změť, protože
neskládá dílčí snímky; rozhodčí je prohlížeč.

`detekce-okraj-cesty.png` se nepřebarvoval: má černé pozadí už z podstaty (je to maska sjízdnosti).
Fotky a snímky obrazovky jsou tmavé samy o sobě.

⚠️ `doc/media/prezentace-schema-*.png` (schémata pro Google Sites) zůstala **světlá** schválně —
web je nepoužívá.

## Jak přidat nebo upravit stránku

- **Text a obrázky**: uprav `.html` přímo. Vzorem je kterákoli stránka v `pages/`. ⚠️ **Hlavičku
  `<header class="sitehead">` needituj** — v souboru sice fyzicky leží, ale od 18. 9. 2026 ji
  vyrábí `tools/menu.cs` (viz *Menu je na jednom místě* níž) a další běh generátoru ji přepíše.
  Články o soutěžích vznikly **jednorázovým skriptem**
  (v repu není — výstupem jsou ty `.html`, a kdyby tu skript zůstal, sváděl by k ruční úpravě,
  která by se při dalším běhu přepsala).
- **Nová podstránka**: zkopíruj `pages/kontakt.html`, přepiš `<title>`, nadpis a obsah, zapiš ji
  do `tools/menu.cs` (buď jako položku menu, nebo do některé skupiny) a pusť
  `dotnet run tools/menu.cs`. ⚠️ **Stránka, kterou `tools/menu.cs` nezná, je chyba**: generátor
  skončí kódem 1 a nepřepíše nic. Jinak by na ní nebylo zvýrazněné nic a vypadalo by to, že
  návštěvník vypadl ze struktury webu.
- ⚠️ **Nový technický článek do menu NEPATŘÍ.** Přidá se karta na `pages/technicke-clanky.html`
  a stránka se zapíše do skupiny *Technické články* v `tools/menu.cs`; zvýraznění položky
  obstará generátor. Viz sekce níž.
- ⚠️ **`pages/historie.html` je GENEROVANÁ** z registru úkolů `doc/ukoly.yaml` příkazem
  `dotnet run tools/ukoly.cs` (od 17. 9. 2026) — ruční úprava se přepíše dalším během a CI
  hlídá, že soubor odpovídá zdroji. ⚠️ **Po `ukoly.cs` se musí pustit i `menu.cs`**: registr
  vypíše jen prázdné `<header class="sitehead"></header>` a menu do něj doplní až druhý
  generátor. Styly stránky jsou v `site.css`, sekce „historie".
  Návrh a pravidla: [doc/plan-ukoly.md](../doc/plan-ukoly.md).
- **Nový obrázek**: do `assets/img/`. Obrázky pro web jsou tu **záměrně zkopírované** z `doc/media/`
  (kde slouží vývojové dokumentaci) — GitHub Pages umí servírovat jen to, co leží uvnitř `web/`.
  Když se obrázek ve `doc/media/` změní, je potřeba kopii obnovit.
- **Schémata** na stránce *Jak to funguje* jsou inline SVG přímo v `pages/prezentace.html`,
  takže se dají opravit textovým editorem. Jejich PNG varianty (`doc/media/prezentace-schema-*.png`)
  slouží jen jako podklad pro Google Sites, web je nepoužívá.

## Co se přeneslo z Google Sites

| Původní stránka na arbot.cz | Nový soubor | Stav |
|---|---|---|
| Domovská stránka | `index.html` | text přepsaný (16. 9. 2026), pás čísel, snímky z jízdy, rozcestník |
| — (nová) | `pages/prezentace.html` | popis fungování softwaru |
| Umístění v soutěžích | `pages/umisteni-v-soutezich.html` | popisy soutěží + tabulka výsledků 2009–2025 **včetně odkazů** |
| ARBot → Verze | `pages/verze.html` | celý text + 4 fotky + **4 pásy fotek (30 snímků)**; 26. 9. 2026 přibyla sekce **Verze PI** s pátým pásem (3 snímky) |
| ARBot → Model diferenciálního podvozku | `pages/model-diferencialniho-podvozku.html` | text + 3 schémata (SVG) + **vzorce (1)–(14)** |
| ARBot → Detekce kraje vozovky | `pages/detekce-kraje-vozovky.html` | text + obrázek + **vzorce (1)–(5)** |
| Kontakt | `pages/kontakt.html` | kontaktní a fakturační údaje |
| Umístění → 11 článků o soutěžích (2009–2017) | `pages/robotour-2009.html` a dalších 10 | text, 6 obrázků, 6 videí; znění beze změn |

Skupina *ARBot* z menu zanikla — na Sites neměla vlastní obsah, byla to jen rozbalovací položka.
Její tři podstránky šly nejdřív do menu přímo. ⚠️ **18. 9. 2026 se skupina vrátila**, ale jinak:
jako *Technické články*, tedy **stránka s obsahem** (rozcestník), ne rozbalovací položka — přibyl
totiž třetí takový článek (*Regulátor sledování dráhy*) a bylo jasné, že další budou.

Stránky, které na Sites nebyly a vznikly až tady: `pages/prezentace.html`,
`pages/historie.html`, `pages/technicke-clanky.html` a `pages/regulator-sledovani-drahy.html`.

**Fotky z telefonu** (`verze-pi-*.jpg`, 26. 9. 2026) se před uložením otáčejí podle EXIF
(telefon ukládá fotku na šířku a otočení jen zapíše do metadat) a **ukládají se bez metadat** —
fotka z telefonu nese i GPS polohu místa, kde vznikla. Plná velikost 1536 × 2048, náhled 345 × 460.

Obrázky převzaté ze Sites: `assets/img/arbot-logo.png` (logo, slouží i jako favicon),
`assets/img/arbot-model.gif` (animovaný model robota, 592 × 612, 72 snímků),
`verze-srv1-*.jpg` (4 fotky), `verze-z-*` / `verze-u2-*` (30 fotek ze čtyř pásů),
`podvozek-*.png` (3 schémata), `detekce-okraj-cesty.png`, `clanky/*` (6 obrázků k článkům).

### Odkazy v tabulce výsledků a články o soutěžích

Tabulka na Sites byla vložený blok HTML a Sites ho vykresluje v **cizím rámečku**, takže se při
prvním převodu odkazy nevytáhly. Ve skutečnosti ležely **escapované přímo ve zdroji hostitelské
stránky** (`&lt;table class="dataTable"&gt;…`) — rámeček je až výsledek. Doplněné 16. 9. 2026.

Odkazy u ročníků **2018 a novějších** vedou na výsledkové listiny pořadatelů (robotika.cz,
ok1kpi.cz). Ročníky **2009–2017** vedly na články na Sites; ty jsou od 16. 9. 2026 **přenesené
sem** jako jedenáct stránek v `pages/` (`robotour-2009.html`, `robotem-rovne-2010.html`, …)
a odkazy v tabulce míří na ně. Zrušení publikace Sites (krok 4 výše) tedy web už nerozbije.

- **Znění článků je beze změn** včetně překlepů — je to archiv, ne redakce. Upravená je jen
  typografie (uvozovky, pomlčky) a struktura (odstavce, seznamy, mezinadpisy).
- **Obrázky** (6) leží v `assets/img/clanky/`. Fotky jsou překlopené na JPEG (ze Sites chodí
  i fotky jako PNG — u fotky robota z roku 2009 to bylo 981 kB proti 185 kB v JPEG), mapy
  a snímky obrazovky zůstaly PNG kvůli textu v nich. **Stahovat je nešlo dávkově**: Sites
  servíruje obrázky přes podepsané `googleusercontent.com` adresy, které po chvíli vrací **403**,
  takže se adresa musí vytáhnout a hned použít.
- **Videa** (6) jsou `<iframe>` na `youtube-nocookie.com`, popisek pod videem je zároveň odkaz
  na YouTube. ⚠️ **Nedávej jim `referrerpolicy="no-referrer"`** — YouTube pak přehrávač odmítne
  s *Chyba 153*, což vypadá jako zrušené video. ⚠️ **Táž chyba přijde i při otevření stránky
  z disku** (`file://`): stránka nemá origin, takže ji YouTube odmítne bez ohledu na značkování.
  **Web si prohlížej přes http(s)**, ne dvojklikem na soubor. Titulky u videí jsou skutečné názvy
  z YouTube (oEmbed), ne vymyšlené.
- ⏳ **Otevřené:** u slova „MD23" v článku *Robotour 2013* vedl odkaz na starý blogový článek
  `arbot.cz/post/2012/04/02/Tuhnuti-MD23` (2. 4. 2012). Text se hledá v záloze; až se najde,
  přidat jako další stránku a odkaz vrátit.
- **Tři odkazy uvnitř článků byly už na Sites mrtvé** (kufr.cz, vosrk.cz, starý blog
  `arbot.cz/post/…` — všechny 404): text zůstal, odkaz ne, a v patičce článku je o tom věta.

## Pásy fotek

Na Sites byly na stránce *Verze* čtyři **karusely**. V DOM je vidět jen aktuální snímek a zbytek
je `background-image` na skrytých divech — proto se při prvním průchodu přehlédly.

Na novém webu jsou to **vodorovné pásy** (`<figure class="strip">`): žádný JavaScript, jen
`overflow-x:auto` se `scroll-snap`. Klik na fotku otevře plnou velikost v nové záložce.

- **náhled** `assets/img/nahledy/<jméno>.jpg` — výška 460 px (dvojnásobek zobrazených 230 px),
  JPEG q82, dohromady 1,4 MB za všech 30
- **plná velikost** `assets/img/<jméno>.jpg` — originál 2048 × 1536, ~300–500 kB za kus

⚠️ **Náhledy musí mít v HTML `width` a `height`.** Bez nich `loading="lazy"` obrázky do načtení
nezaberou žádné místo, pás se srolovaný složí do jednoho sloupce a po doskrolování poskočí.
Nové náhledy se generují Pillow skriptem (viz DevLog 14. 9. 2026) a rozměry se doplní do atributů.

## Vzorce

Na Sites byly vzorce v **blocích „vložený kód“**, které Google servíruje v cizím rámečku
(`googleusercontent.com`) — zdroj z nich vytáhnout nejde a v rámečku byly navíc **vodorovně
oříznuté**, takže část nešla přečíst ani ze stránky. **Zdrojový LaTeX dodal autor** a vzorce jsou
teď přepsané přímo v HTML.

Sází je **MathJax** z cdnjs (`tex-mml-chtml`), takže zdroj zůstává čitelný LaTeX v souboru:

- blokový vzorec: `$$egin{align} ... \end{align}$$`, každý řádek ukončený `\`,
  číslo vzorce jako `	ag{9}` — **čísluje tedy MathJax podle značek**, ne CSS, a čísla proto
  zůstávají stejná jako na původním webu
- vzorec v textu: `\( ... \)`

⚠️ **Dvě věci, na kterých se to dá snadno rozbít:**

1. V konfiguraci MathJaxu v hlavičce musí být `inlineMath:[['\(','\)']]` — tedy
   **dvě** zpětná lomítka. S jedním JavaScript escape sekvenci spolkne, delimiter je pak `(`
   a vzorce v textu se vypíšou jako zdroj (`\(\omega\)`), zatímco blokové se sázet budou.
   Vypadá to jako chyba v obsahu, ne v konfiguraci.
2. Zalomení řádku uvnitř `align` je `\`. Když se při generování ztratí jedno lomítko, řádky
   se slijí do jednoho a `	ag` se rozhodí.

Široké vzorce (např. (9) a (13) u podvozku) se **vodorovně posouvají uvnitř `.mathblock`** —
stránka samotná se vodorovně neroluje.
