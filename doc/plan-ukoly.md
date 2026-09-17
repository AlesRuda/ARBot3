# Plán: registr úkolů projektu a stránka „Čím si projekt prošel"

**Stav:** ✅ **hotové 17. 9. 2026** — kostra, generátor, styly, menu, filtr (JS), CI krok, pravidla v `CLAUDE.md` a DevLogu, rekonstrukce od 23. 6. 2026 (180 témat), odkaz z úvodní stránky; sekce „Otevřené úkoly" v `doc/*.md` přepsané na odkazy. ⚠️ Stavy témat jsou rekonstrukce z DevLogu, ne nezávislé ověření; CI krok neběžel (nepushováno).
Vzniklo z dotazu autora: *„Máme někde přehledný soupis úkolů, které bylo a je potřeba vyřešit?
Checklist se stavem, kdy zjištěno, kdy vyřešeno, krátký popis a odkaz na dokumentaci."*
Odpověď byla **ne** — a tenhle plán říká, jak to napravit tak, aby z jednoho zdroje vznikl jak
pracovní checklist do `doc/`, tak stránka na web.

## Proč

Úkoly, nálezy a jejich stav dnes leží na pěti místech a žádné z nich nemá tvar seznamu:

| kde | co tam je | co chybí |
|---|---|---|
| sekce „Otevřené úkoly" v ~15 souborech `doc/*.md` | otevřené položky per doména | datum nálezu (většinou), datum vyřešení (nikdy — položka se smaže nebo dostane ✅) |
| „Rozpracováno / další krok" v [devlog.md](devlog.md) | nejúplnější zdroj, den po dni | je chronologický: co je *dnes* otevřené, se musí dohledat přes 8 700 řádků |
| checkboxy v `doc/plan-*.md` | kroky jedné akce | pohled napříč projektem |
| [decisions.md](decisions.md) | rozhodnutí, často končící „další krok je…" | není to seznam úkolů |
| ⚠️/✅ odrážky v `CLAUDE.md` | de facto stavový přehled | zapsaný jako próza v rozcestníku; nejde filtrovat a soubor tím bobtná |

Nejčastější stav v projektu je **„hotové v kódu, na zařízení neběželo"** — a právě ten není nikde
vidět jako seznam. Autor chce vědět, *kde jsme*; návštěvník webu má vidět, *čím si projekt prošel*.

## Co to dělá

Jeden **strukturovaný zdroj** `doc/ukoly.yaml`, který udržuje asistent v rámci dokumentování
(autor ho čte přes výstupy, ne přímo). Z něj **generátor** `tools/ukoly.cs` vyrobí dva výstupy,
oba commitované:

| výstup | pro koho | tvar |
|---|---|---|
| `doc/ukoly.md` | autor, práce v repu | checklist po oblastech, tabulka otevřených, odkazy do `doc/*.md` a do DevLogu |
| `web/pages/historie.html` | návštěvník webu | časová osa po měsících + strom po oblastech, sazba webu, filtr podle stavu a hledání (inline JS, rozhodnutí 10) |

Hierarchie: **Oblast → Téma → Kroky**. Závislosti („čeká na") jen mezi tématy.

## Rozhodnutí (proč zrovna takhle)

### 1. YAML jako zdroj, ne Markdown

Zdroj píše a čte asistent; autor se dívá na výstupy. Markdown se strukturou v hlavičkách by se
musel parsovat a parser Markdownu s metadaty je křehký — YAML dá totéž bez křehkosti a chyba
ve zdroji je chyba při generování, ne tiše jiná stránka. Jediný soubor, ne soubor per téma:
témat bude řádově desítky až nízké stovky a jeden soubor se přehledně čte i diffuje.

### 2. Generované výstupy se commitují, web zůstává bez buildu

`web/README.md` drží pravidlo „publikuje se tak, jak leží". Autor navíc chce vidět checklist
v repu bez spouštění čehokoli. Proto generátor píše do repa a výstupy se commitují spolu se
zdrojem. Workflow `pages.yml` by je umělo vyrobit samo, ale pak by `doc/ukoly.md` v repu nebyl —
a ten je ten pracovní pohled. Aktuálnost hlídá CI krok (rozhodnutí 9).

### 3. Generátor jako jeden soubor C#, ne nový projekt

.NET 10 umí spustit jediný soubor (`dotnet run tools/ukoly.cs`) včetně balíčků přes direktivu
`#:package YamlDotNet@…`. Nový `csproj` v `ARBot.slnx` kvůli generátoru dokumentace by byl
ceremonie navíc; do `ARBot.Analyze` to nepatří (ten měří záznamy). Python na vývojovém stroji
není (zjištěno 17. 9. 2026), .NET je toolchain projektu.

### 4. Hierarchie Oblast → Téma → Kroky; závislosti jen mezi tématy

Z dokumentace vypadly tři druhy věcí, které se dnes vedou pomíchaně: **záměr** (postavit
headless provoz), **nález / vada** (kompas 60–90× sebejistější, než jaký je) a **ověření**
(běželo jen v simulaci). Téma je záměr nebo vada — jednotka, kterou má smysl ukázat na webu.
Ověření na HW je **krok** tématu, ne jeho vlastnost, protože je to samostatná práce s vlastním
datem. Závislosti na úrovni kroků by nesly víc údržby než užitku.

### 5. Stavy tématu

| stav | význam |
|---|---|
| `otevreno` | ví se o tom, neřeší se nebo se řeší |
| `v-kodu` | hotové v kódu, ověřené buildem/testy/simulací, **na zařízení neběželo** |
| `hotovo` | hotové a ověřené tam, kde to ověřit jde (u čistě SW věcí = testy) |
| `odlozeno` | vědomě odsunuté (např. „recovery manévr, priorita nízká") |
| `zamitnuto` | rozhodnuto nedělat, s odkazem do `decisions.md` |

`v-kodu` je záměrně samostatný stav a ne příznak — je to nejčastější stav v projektu a v seznamu
musí být vidět na první pohled. Krok má stavy `hotovo` / `otevreno` a datum.

### 6. Popis se píše pro čtenáře zvenčí

Jeden popis slouží oběma výstupům. Píše se tak, aby mu rozuměl člověk, který zná doménu, ale ne
kód: co bylo špatně, co se s tím udělalo, co zbývá. Čísla a detaily zůstávají v `doc/*.md`, kam
vede odkaz. Repo je veřejné, takže odkazy z webu na GitHub jsou v pořádku.

### 7. Rekonstrukce od začátku ARBot3 (23. 6. 2026)

Rozhodnutí autora. DevLog sahá až k prvnímu commitu (období před 9. 8. 2026 je „zpětně z gitu",
hrubší), takže zdrojem rekonstrukce je DevLog, git jen pro data. Starší verze robota (ARBot2 a
dřív) do registru **nepatří** — je to pracovní nástroj pro ARBot3; web má stránku *Verze*, na
kterou se historie může jen odkázat.

### 8. Sekce „Otevřené úkoly" v `doc/*.md` zůstávají, ale odkazují na id

Rušit je by znamenalo, že čtenář domény ztratí přehled, co je v jeho oblasti otevřené. Stav se
ale nesmí vést dvakrát: sekce má být seznam id s jednou větou a odkazem do `ukoly.md`, kde je
stav a data. Přepis těch sekcí je součást fáze 2, ne samostatná práce.

### 9. CI hlídá aktuálnost výstupů

Krok v `build-and-test.yml`: pustit generátor a `git diff --exit-code` nad `doc/ukoly.md`
a `web/pages/historie.html`. Chytí to zapomenuté spuštění generátoru i nevalidní YAML. Je to
zároveň jediný „test" generátoru, který je úměrný jeho velikosti (rozhodnutí 3): validace vstupu
+ idempotence výstupu.

### 10. Filtr podle stavu a hledání v textu — jediný JavaScript na webu

Původní návrh byl bez JS (web je jinak záměrně statický, `web/README.md`) a jen se dvěma fixními
projekcemi. **Autor 17. 9. 2026 rozhodl jinak:** se stovkou karet je stránka bez filtru
nepřehledná, a filtr podle stavu a hledání v textu jsou přesně ty dvě otázky, které se nad ní
kladou („co je otevřené", „kde je něco o kamerách"). Skript je **inline, bez knihoven, ~25 řádků**
a je to *progresivní vylepšení*: bez JS zůstane legenda s počty a celý obsah, se skriptem se
štítky stavů stanou zaškrtávátky, objeví se pole hledání a filtruje se osa i strom (prázdná
oblast nebo měsíc se schová, ukazuje se „N z M"). Hledání je bez ohledu na velikost písmen
a diakritiku (`normalize('NFD')`). Filtr podle **oblasti** se nedělá — oblasti jsou nadpisy
sekcí, stačí skrolovat.

## Schéma záznamu

```yaml
oblasti:                       # pořadí = pořadí na stránce
  - id: lokalizace
    nazev: Lokalizace a fúze senzorů
  - id: navigace
    nazev: Navigace po mapě
  # … lokalni-planovani, videni, mise, provoz, hw, nastroje, web

temata:
  - id: kompas-sigma-podlaha            # slug, jedinečný, stabilní (odkazují na něj závislosti a doc)
    oblast: lokalizace
    nazev: Kompas si věří 60–90× víc, než jaký je
    druh: vada                          # zamer | vada
    stav: v-kodu                        # viz rozhodnutí 5
    nalezeno: 2026-08-25
    vyreseno: 2026-09-12                # jen u v-kodu / hotovo; datum, kdy se stav změnil
    popis: >
      Senzor VN100 hlásí nejistotu kurzu 0,06°, ale proti GPS chybuje o 3–5°. Fúze mu proto
      věřila tisíckrát víc než GPS a druhá reference kurzu neměla šanci. Od 12. 9. má sigma
      kurzu z kompasu podlahu 5° a kompas se navíc škrtí na 1 Hz.
    kroky:
      - { co: Změřit poměr informace kompas : GPS kurz, stav: hotovo, kdy: 2026-08-25 }
      - { co: Podlaha sigmy `imuheadingstd=` a škrcení `imuheadinghz=`, stav: hotovo, kdy: 2026-09-12 }
      - { co: Záznam s `imuheadingstd=5` a `=0` nad týmž úsekem na zařízení, stav: otevreno }
    ceka_na: [vn100-kalibrace-magnetometru]   # id témat; cyklus nebo neznámé id = chyba generátoru
    odkazy:
      - { text: ekf-fusion.md, cesta: doc/ekf-fusion.md }
      - { text: rozhodnutí 12. 9., cesta: doc/decisions.md }
    devlog: [2026-08-25, 2026-09-12]     # dny → odkaz na kotvu `## RRRR-MM-DD` v devlog.md
```

Povinné: `id`, `oblast`, `nazev`, `druh`, `stav`, `nalezeno`, `popis`. Ostatní volitelné.
Validace (chyba generátoru, ne varování): duplicitní id, neznámá oblast, neznámé id v `ceka_na`,
cyklus v závislostech, `vyreseno` u stavu `otevreno`/`odlozeno`, `vyreseno` < `nalezeno`,
krok `hotovo` bez data.

## Výstupy

### `doc/ukoly.md`

1. Hlavička: **generováno, needitovat**, kdy, počty podle stavů.
2. **Otevřené a v kódu** — jedna tabulka napříč oblastmi (stav, oblast, téma, nalezeno, čeká na),
   seřazená stav → datum. To je ta odpověď na „kde jsme".
3. **Po oblastech** — každé téma jako blok: nadpis s id (kotva), stav a data, popis, kroky jako
   checklist `- [x]` / `- [ ]` s datem, závislosti jako odkazy na kotvy, odkazy do `doc/`,
   odkazy do DevLogu.
4. Uzavřená a zamítnutá témata jsou v bloku oblasti až za otevřenými.

### `web/pages/historie.html`

1. Hlavička a menu webu jako ostatní stránky (`sitehead`, položka *Historie* s `class="on"`).
2. **Osa po měsících**: pro každý měsíc od června 2026 seznam „nalezeno" a „vyřešeno" s odkazem
   do stromu níž. Z toho je vidět tempo a co který měsíc přinesl.
3. **Strom po oblastech**: karta tématu = název, štítek stavu (barva z palety webu:
   `--free` hotovo, `--plan` v kódu, `--accent` otevřeno, `--muted` odloženo/zamítnuto), data,
   popis, kroky, „čeká na". Odkazy do dokumentace vedou na `github.com/AlesRuda/ARBot3/blob/master/…`.
4. Styl: nové třídy v `site.css` (`.tema`, `.stav-*`, `.osa`), sazba a šířka jako zbytek webu,
   obrázky žádné.

## Tok

```
doc/ukoly.yaml  ──►  dotnet run tools/ukoly.cs  ──►  doc/ukoly.md
                          │                      └─►  web/pages/historie.html
                          └─ validace (chyba = nenulový návratový kód, nic se nepřepíše)
CI: generátor + git diff --exit-code nad oběma výstupy
```

Generátor se spouští z kořene repa, cesty má natvrdo relativně k němu (jediný účel, jediné
místo). Výstup zapisuje **až po úspěšné validaci celého vstupu**, a to oba soubory — nikdy jen
jeden.

## Chyby a okrajové stavy

- Nevalidní YAML nebo porušená validace → hlášení s id tématu a polem, návratový kód 1, výstupy
  nedotčené.
- Téma bez kroků je v pořádku (drobná vada s jedním datem).
- Téma `hotovo` bez `vyreseno` → chyba (datum je to, co autor chtěl vidět).
- Měsíc bez událostí se na ose vynechá (ne prázdný nadpis).
- Odkaz na `doc/*.md`, který neexistuje → chyba (odkazy z webu na GitHub by vedly na 404).

## Testování

Úměrně velikosti (rozhodnutí 3 a 9): žádný testovací projekt. Ověřuje se
(a) validací vstupu v generátoru, (b) idempotencí — druhý běh nezmění nic, (c) CI krokem, který
oba výstupy přegeneruje a porovná, (d) prohlédnutím stránky v prohlížeči nad `web/` (jako u
ostatních stránek webu, viz `web/README.md`). Kdyby generátor rostl, je čas na projekt s testy.

## Kroky

1. **Kostra** — `doc/ukoly.yaml` se seznamem oblastí a **třemi vzorovými tématy** různých stavů,
   `tools/ukoly.cs` s validací a oběma výstupy, nové třídy v `site.css`, položka *Historie*
   v menu všech 18 stránek (mechanicky, `sed`; generátor menu je mimo rozsah — viz níž).
   Ověření: generátor běží, oba výstupy vypadají správně, stránka prohlédnutá v prohlížeči.
2. **Rekonstrukce** — naplnit registr z DevLogu od 23. 6. 2026. Po obdobích (červen–červenec,
   srpen 1. půle, srpen 2. půle, září po týdnech), každé období samostatný průchod DevLogem
   se zápisem témat, kroků a dat; křížové závislosti až po všech obdobích. Zdroj pravdy pro
   *stav* jsou nejnovější záznamy a `CLAUDE.md`, ne nejstarší. Součástí je přepis sekcí
   „Otevřené úkoly" v `doc/*.md` na odkazy (rozhodnutí 8).
3. **CI a pravidla** — krok v `build-and-test.yml`; do `CLAUDE.md` pravidlo „nový nález nebo
   změna stavu = záznam v `ukoly.yaml` + přegenerovat" a odkaz na `ukoly.md`; do hlavičky
   DevLogu poznámka, že „Rozpracováno / další krok" má u tématu uvádět jeho id.
4. **Web** — odkaz z rozcestníku na úvodní stránce, poznámka v `web/README.md`, že stránka je
   generovaná a needituje se ručně.

Krok 2 je zdaleka největší; kroky 1 a 3 jsou hodiny, ne dny.

## Co se záměrně NEDĚLÁ

- **Generátor menu webu** — `web/README.md` říká „až se menu změní, je to 18 souborů, pak
  generátor". Menu se tímhle změní, ale generátor celého webu je jiná práce; položka se přidá
  mechanicky a dluh zůstane zapsaný.
- **Filtrování na stránce (JavaScript)** — rozhodnutí 10.
- **Historie ARBot2 a starších** — rozhodnutí 7.
- **Automatický sběr z DevLogu** — registr se plní ručně (asistentem); parsovat prózu DevLogu
  by dalo nespolehlivý výsledek a stejně by se musel kontrolovat.
- **Priority a odhady pracnosti** — nejsou v zadání; `odlozeno` s důvodem stačí.

## Otevřené

- **Granularita rekonstrukce.** Kolik témat vzejde z 8 700 řádků DevLogu, se ukáže až při
  práci; odhad je 60–120. Když to bude výrazně víc, je otázka, jestli slučovat, nebo přidat
  úroveň — rozhodne se po prvním období.
- **Jméno stránky** (`historie.html` / položka *Historie*) — pracovní, autor může změnit.
- **Zda vést i data „kdy se o tom rozhodlo"** (vazba na `decisions.md`) — zatím jen odkazem.

## Co se ukázalo při stavbě kostry (17. 9. 2026)

- **Id tématu má prefix oblasti** (`lok-`, `nav-`, `lp-`, `vid-`, `mise-`, `prov-`, `hw-`, `nast-`,
  `web-`), aby šlo v `ceka_na` a v DevLogu poznat, kam téma patří, bez hledání.
- **Kroky se píší blokově, ne `{ co: …, stav: … }`** — flow zápis rozbije každá čárka v textu
  kroku (první pokus: „(autor, při pushi)" se rozpadl na dvě pole). Schéma výš to ještě ukazuje
  ve flow tvaru kvůli stručnosti; ve zdroji se používá blok.
- **`hotovo` vyžaduje všechny kroky `hotovo`** (validace) — jinak by stav lhal, jako u prvního
  vzorového tématu webu, které mělo otevřený krok „přepnout Pages".
- **Výstupy nenesou časové razítko generování** — CI je porovnává s commitem a razítko by je
  rozhodilo při každém běhu. Hlavička říká jen „generováno z ukoly.yaml".
- **Menu webu je v generátoru natvrdo** (`HtmlVystup`), tedy devatenáctý výskyt téhož menu;
  zapsáno v `web/README.md`.
- **Rekonstrukce běží paralelně po devíti obdobích DevLogu** (agenti, každý vlastní fragment YAML
  ve scratchpadu, sloučení a křížové závislosti až nakonec). Pravidlo, kdo téma zakládá: to
  období, ve kterém se v DevLogu **objevilo poprvé**; stav a `vyreseno` se ale dohledávají
  **k dnešku**, ne ke konci období.
