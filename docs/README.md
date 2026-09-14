# Web arbot.cz (GitHub Pages)

Statický web projektu. **Tenhle adresář je to, co se publikuje** — GitHub Pages ho servíruje
tak, jak je, žádný build ani generátor se nepouští.

```
docs/
  index.html                 úvodní stránka  ->  arbot.cz/
  pages/*.html               podstránky      ->  arbot.cz/pages/<jméno>.html
  assets/site.css            sdílená sazba a barvy všech stránek
  assets/img/                obrázky webu
  .nojekyll                  vypíná Jekyll (servíruje se přesně to, co tu leží)
```

Stránky jsou **obyčejné HTML soubory bez frameworku a bez JavaScriptu**; jediná externí věc jsou
fonty z Google Fonts. Všechny odkazy jsou **relativní**, takže web funguje stejně na
`arbot.cz` i na `alesruda.github.io/ARBot3/` — díky tomu jde nová verze prohlédnout dřív,
než se přepne doména.

## Jak to zapnout

1. **GitHub → Settings → Pages → Build and deployment**: Source = *Deploy from a branch*,
   Branch = `master`, složka = `/docs`. Po chvíli web běží na
   `https://alesruda.github.io/ARBot3/`.
2. **Napřed si to na té adrese prohlédni.** Teprve až bude vše v pořádku, přepni doménu (krok 3).
3. **Vlastní doména.** V *Settings → Pages → Custom domain* zadej `www.arbot.cz`
   (GitHub si tím sám založí soubor `docs/CNAME`) a zaškrtni *Enforce HTTPS*. V DNS u registrátora:
   - `www` → `CNAME` → `alesruda.github.io.`
   - apex `arbot.cz` → čtyři `A` záznamy na IP GitHub Pages (aktuální seznam je
     v dokumentaci GitHubu, *Managing a custom domain for your GitHub Pages site*),
     případně `ALIAS`/`ANAME`, pokud to registrátor umí.

   ⚠️ **Soubor `CNAME` tu schválně zatím není.** Jakmile vznikne, GitHub přesměruje
   `alesruda.github.io/ARBot3/` na vlastní doménu — a dokud nebude hotové DNS, nebyl by web
   dostupný ani na jedné z těch adres. Proto až jako poslední krok.
4. **Až bude nový web živý, zrušit publikaci Google Sites**, ať nezůstane druhá verze obsahu.

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
pohybu, sečna vs. tečna) jsou **ručně psané inline SVG přímo v HTML**, stejně jako schémata na
stránce *Jak to funguje*. Barvy berou z palety (`currentColor`, `var(--accent)`, `var(--plan)`),
takže se o tmavý podklad starat nemusí, jsou ostré v každém zvětšení a opraví se textovým editorem.

⚠️ **Předtím to byly překreslené rastry a nevypadalo to dobře** — proto se od převádění obrázků
u kreseb ustoupilo úplně. Světlé originály ze Sites zůstávají v `assets/img/podvozek-*.png` jako
předloha, ale web je nepoužívá.

Šířku schématu řídí třída na obalu: `.diagram.uzky` (560 px) nebo `.diagram.stredni` (760 px);
bez nich se schéma roztáhne přes celý sloupec.

### Obrázek, který se kvůli tmavému podkladu přebarvoval

Zbyl jediný: **`arbot-model-dark.gif`** (animovaný model robota na úvodní stránce). Invertovat ho
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

- **Text a obrázky**: uprav `.html` přímo. Vzorem je kterákoli stránka v `pages/` — hlavička
  (`<header class="sitehead">`) je v každém souboru zvlášť, takže **při přidání položky do menu
  se musí upravit všechny stránky**. Je jich sedm; kdyby jich mělo být výrazně víc, je čas
  na generátor.
- **Nová podstránka**: zkopíruj `pages/kontakt.html`, přepiš `<title>`, nadpis a obsah, a přidej
  odkaz do `<nav>` na všech stránkách. Pozor na `class="on"` — označuje právě zobrazenou položku.
- **Nový obrázek**: do `assets/img/`. Obrázky pro web jsou tu **záměrně zkopírované** z `doc/media/`
  (kde slouží vývojové dokumentaci) — GitHub Pages umí servírovat jen to, co leží uvnitř `docs/`.
  Když se obrázek ve `doc/media/` změní, je potřeba kopii obnovit.
- **Schémata** na stránce *Jak to funguje* jsou inline SVG přímo v `pages/prezentace.html`,
  takže se dají opravit textovým editorem. Jejich PNG varianty (`doc/media/prezentace-schema-*.png`)
  slouží jen jako podklad pro Google Sites, web je nepoužívá.

## Co se přeneslo z Google Sites

| Původní stránka na arbot.cz | Nový soubor | Stav |
|---|---|---|
| Domovská stránka | `index.html` | text převzat, doplněn rozcestník |
| — (nová) | `pages/prezentace.html` | popis fungování softwaru |
| Umístění v soutěžích | `pages/umisteni-v-soutezich.html` | popisy soutěží + tabulka výsledků 2009–2023 |
| ARBot → Verze | `pages/verze.html` | celý text + 4 fotky + **4 pásy fotek (30 snímků)** |
| ARBot → Model diferenciálního podvozku | `pages/model-diferencialniho-podvozku.html` | text + 3 schémata (SVG) + **vzorce (1)–(14)** |
| ARBot → Detekce kraje vozovky | `pages/detekce-kraje-vozovky.html` | text + obrázek + **vzorce (1)–(5)** |
| Kontakt | `pages/kontakt.html` | kontaktní a fakturační údaje |

Skupina *ARBot* z menu zanikla — na Sites neměla vlastní obsah, byla to jen rozbalovací položka.
Její tři podstránky jsou teď v menu přímo.

Obrázky převzaté ze Sites: `assets/img/arbot-logo.png` (logo, slouží i jako favicon),
`assets/img/arbot-model.gif` (animovaný model robota, 592 × 612, 72 snímků),
`verze-srv1-*.jpg` (4 fotky), `verze-z-*` / `verze-u2-*` (30 fotek ze čtyř pásů),
`podvozek-*.png` (3 schémata), `detekce-okraj-cesty.png`.

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
