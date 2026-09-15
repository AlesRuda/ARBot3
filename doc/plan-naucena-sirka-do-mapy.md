# Plán: naučená šířka cesty zpátky do mapy

**Stav:** ✅ **hotové v kódu 15. 9. 2026** (fáze 1–4, 19 nových testů), ověřené buildem, testy pod
`x64` a **během v simulaci**: se `roadwidthmap=true` nad `OSM/SyntetickyRovny.osm` se mapa
přestavěla jednou (9 uzlů), bez parametru se nezměnilo nic. ⚠️ **Na zařízení neběželo nic.**
⚠️ Prahy `RebuildThresholdM` / `MinRebuildPeriodSec` zůstávají **odhadnuté** — změřit z prvního
záznamu (jde to offline, viz rozhodnutí 6). Navazuje na
[map-correlation-localization.md](map-correlation-localization.md), sekci
„Šířka cesty: odhad s verdiktem kvality" — ten odhad dnes existuje, ale vidí ho **jen vlastní brány
koridoru**.

## Proč

`RoadWidthEstimator` umí od 15. 9. 2026 změřit skutečnou šířku cesty per OSM way a říct, kdy se
výsledku dá věřit. Ta hodnota ale zůstává uvnitř `CorridorLocalizer`u. Mapa mezitím tvrdí něco
jiného: `OSM/Hviezdoslavova.osm` nemá **ani jeden** tag `width`, takže celá síť má default
`roadwidth=3`.

Nejvíc to vadí **korelaci s mapou**. `MapCorrelator` porovnává sémantický kanál `LRoad` occupancy
gridu proti polygonu vozovky z `RoadScene`. Když je cesta ve skutečnosti 6 m a v mapě 3 m, koreluje
se proti špatnému tvaru — a to jde přímo do skóre i do σ, tedy do korekce polohy. Druhý důvod je
diagnostický: World pohled i webový půdorys kreslí cestu podle mapy, takže člověk nevidí, že se
odhad vůbec chytil.

## Co to dělá

Runtime drží **překryv šířek** (`nodeId → šířka`) spočítaný z důvěryhodných odhadů koridoru.
Překryv dostanou dva konzumenti:

| konzument | proč |
|---|---|
| `RoadScene` **korelátoru** | koreluje se proti skutečnému tvaru vozovky, ne proti defaultu |
| `MapMsg` (World pohled, webový půdorys) | člověk vidí, co robot naměřil |

**Graf sám se nemění.** `RoadNetwork`, `Node.Width`, navigace ani `RoadAxis` se nedotknou.

## Rozhodnutí (proč zrovna takhle)

### 1. Překryv, ne přepis grafu

Zvažovalo se přestavět `RoadNetwork` s novými `Node.Width`. **Zamítnuto:** `Node.Width` je
`get`-only a síť drží kromě korelátoru i `GlobalNavigator`, `RoadAxis`, `TrackMission` a navigační
pole. Výměna sítě za běhu je záměna identity toho, *podle čeho robot jede* — dosah změny je
nesrovnatelný s tím, co se získá.

Zvažovala se i **zpráva ve streamu** (koridor publikuje šířky, konzumenti se přestaví sami).
Zamítnuto: duplikuje stav ve dvou konzumentech a chce nový typ zprávy s verzováním, a to pro
hodnotu, která **nepřežívá běh** a v záznamu už je (`RoadCorridorMsg.Width`).

### 2. Šířka zůstává per UZEL (rozhodnutí autora)

Estimátor měří **per way**, `RoadScene` i `MapMsg` nesou šířku **per uzel**. Šířka uzlu =
**maximum přes cesty, které jím vedou a MAJÍ ODHAD**. Maximum je týž slučovací vzorec, jaký
používá `GraphBuilder` — ale jen nad **změřenými** cestami.

> ⚠️ **Oprava 15. 9. 2026:** původně přispívala i cesta **bez** odhadu, a to svou **mapovou**
> hodnotou. Naučené **zúžení** se tím na každém sdíleném uzlu přehlasilo — a protože půlšířky
> segmentu se berou z jeho dvou **koncových uzlů**, zůstala celá naučená cesta široká všude, kde
> se dotýká jiné cesty, tedy prakticky na celé síti. Navenek to vypadalo, že se šířka
> neaktualizuje vůbec; našlo se to na dvoumapovém rigu (vizuální mapa 2 m proti jízdní 3 m).
> Mapová šířka nezměřené cesty je **default, ne důkaz** — a default nesmí přehlasovat měření.
> Drží to `RoadWidthOverridesTests.UzsiOdhadNaSdilenemUzlu_seNEZTRATI`.

Alternativa „šířka per hrana" by uměla rozlišit vozovku od chodníku ve společném uzlu, ale znamenala
by `RoadScene.Segment` i **`MapMsg` verze 2** a práci v kreslení. Autor zvolil per uzel.

> ⚠️ **Přiznaná cena.** Tam, kde se chodník dotýká vozovky, podědí chodník její šířku — korelace
> pak bude tvrdit cestu i přes trávník mezi nimi. Je to **totéž pravidlo, které mapa používá už
> dnes**, ale s naučenými šířkami (2 m vs. 6 m) je jeho dopad větší než s uniformním defaultem 3 m.
> Je to **známá mez, ne vada**; kdyby se v datech ukázala jako podstatná, léčbou je varianta
> „per hrana", ne úprava pravidla.

### 3. Oddělení od virtuální kamery je konstrukcí, ne kázní

`RoadScene` se staví **dvakrát** — pro korelátor ([ARBotRuntime.cs:616](../Src/ARBot.Runtime/Robot/ARBotRuntime.cs:616))
a pro **virtuální kameru** ([ARBotHW.cs:570](../Src/ARBot.Runtime/Robot/ARBotHW.cs:570)). Překryv
dostane **jen ta první**; scéna rendereru se staví bez něj a nikdo jí ho nepředá.

Kdyby ho dostala i kamera, simulace by renderovala cestu podle odhadu a koridor by měřil **sám
sebe** — táž past jako `camerapose=fusion`, kterou projekt už jednou našel a zdokumentoval
22. 8. 2026. Proto to nesmí být věc opatrnosti při psaní kódu, ale věc toho, že ta cesta
neexistuje. **Hlídá to test.**

### 4. Výchozí vypnuto

Nový parametr `roadwidthmap=` (bool, **výchozí `false`**). Stejně jako `mapcorr`, `corridor`
i všechny tři odtlumovací parametry: bez toho by se změřený odhad začal propisovat do korelace
dřív, než kdokoli viděl jediný záznam ze zařízení.

### 5. Výměna scény je atomická záměna reference

`RoadScene` je **neměnná**, takže stačí publikovat hotovou novou instanci a přepsat referenci.
Korelátor si ji na svém vlákně přečte buď starou, nebo novou — nikdy rozpracovanou.
`MapCorrelator.scene` přestane být `readonly` a stane se z něj vlastnost se zápisem.

⚠️ Uvnitř **jednoho** cyklu korelace se scéna číst dvakrát nesmí (rastr a skórování musí vidět
totéž) — `Process` si ji proto vyzvedne **jednou na začátku** do lokální proměnné. Tatáž vada se
v tomhle projektu už jednou stala u pózy v `CorridorLocalizer.Process` (23. 8. 2026).

### 6. Kdy se přestavuje

Přestavba není zadarmo: `RoadScene` je O(hrany + mřížka) a `MapMsg` má na Hviezdoslavově ~400 uzlů,
takže publikovat ji často by nafouklo záznam (a `MapMsg` se dnes posílá **jednou za běh**).

Přestaví se, když jsou **obě** podmínky splněné:

- **důvěryhodná** šířka nějaké cesty se liší od té, se kterou se stavělo naposled, o víc než
  `RebuildThresholdM` (návrh 0,25 m), **a**
- od poslední přestavby uplynulo aspoň `MinRebuildPeriodSec` (návrh 10 s).

Práh je proti tomu, aby přestavbu vyvolávalo kolísání odhadu v řádu centimetrů; odstup proti tomu,
aby ji vyvolala řada cest, které se usadí těsně po sobě.

> ⚠️ **Obě hodnoty jsou odhad, ne měření** — stejně jako prahy kvality estimátoru. Skutečný počet
> přestaveb za jízdu jde spočítat **offline z posloupnosti `Width`** v `RoadCorridorMsg`, protože
> celý mechanismus je její čistá funkce. Nastavit je z prvního záznamu, ne je ladit odhadem.

## Komponenty

| kus | odpovědnost |
|---|---|
| `RoadWidthOverrides` (nový, `ARBot.Common/Maps/OsmNav/Graph`) | neměnná mapa `nodeId → šířka`; statická továrna složí ji ze sítě + per-way odhadů pravidlem maxima |
| `RoadScene` (změna) | volitelný překryv v konstruktoru; bez něj se chová přesně jako dnes |
| `RoadNetwork.ToLogMessage` (změna) | volitelný překryv pro `WidthMeters` uzlu |
| `MapCorrelator` (změna) | `scene` jde vyměnit; `Process` si ji bere jednou na začátku |
| `RoadWidthMapUpdater` (nový, `ARBot.Runtime`) | drží poslední použité šířky, rozhoduje o přestavbě (práh + odstup), staví novou scénu a `MapMsg`, předá je korelátoru a do streamu |

**Updater je stupeň pipeline** (`MessageProcessor`, fronta `DropOldest`) napojený na stream,
takže má vlastní vlákno a přestavba nezdržuje ani koridor, ani řídicí smyčku.

**Tik a data jsou schválně dvě různé věci:**

- **tikem** je `RoadCorridorMsg` ze streamu — chodí právě tehdy, kdy se odhad mohl změnit
  (emituje se **i u zamítnutých cyklů**), takže updater nepotřebuje vlastní časovač;
- **daty** je `CorridorLocalizer.Widths`, tedy přímo `RoadWidthEstimator` — `TryGetWidth` je
  přesně ta otázka, kterou updater potřebuje, a zpráva ji nenese (nese jednu naměřenou šířku,
  ne verdikt kvality per hrana). Updater proto dostane odkaz na stupeň v konstruktoru.

V koridoru se nemění nic.

*(Alternativa „událost z `CorridorLocalizer`u" by byla těsnější vazba mezi stupni; alternativa
„vlastní vlákno s časovačem" by přidala vlákno kvůli události, která chodí sama.)*

⚠️ **Stupeň je tím stavový a závislý na pořadí** — táž poznámka jako u `DefaultMeasurementMapper`
po škrcení kompasu (12. 9. 2026): záruka record/replay platí pro **čerstvou** instanci, ne pro
sdílenou. Při `Start` se zakládá nový graf, takže to sedí; test to má pokrýt.

## Tok dat

```
CorridorLocalizer ──RoadCorridorMsg──►  RoadWidthMapUpdater
   │  (Widths: RoadWidthEstimator)          │  práh + odstup
   │                                        ├─► nová RoadScene ─► MapCorrelator.Scene (záměna reference)
   └────────────────────────────────────────┴─► nový MapMsg    ─► Stream (World pohled, web)

ARBotHW / VirtualCamera ──► vlastní RoadScene BEZ překryvu  (nikdy se nemění)
```

## Chyby a okrajové stavy

| situace | chování |
|---|---|
| `corridor=false` | updater se nezaloží (není zdroj) — `Trace` říká proč |
| `mapcorr=false` | updater běží a překresluje **mapu**, ale korekce stejně nikdo nepočítá; je to legitimní diagnostický režim |
| žádná cesta nemá důvěryhodnou šířku | žádná přestavba, mapa zůstane původní |
| odhad se vrátí k mapové hodnotě | přestavba zpátky; překryv **není jednosměrná ráčna** |
| `map=` chybí | koridor se nezaloží už dnes, takže se to nestane |

## Testování

1. `RoadWidthOverrides` použije **maximum** přes cesty uzlem, a cesta bez důvěryhodného odhadu
   přispěje **mapovou** hodnotou (přímý test pravidla z rozhodnutí 2).
2. `RoadScene` **bez** překryvu dá přesně tytéž odpovědi `IsRoad` jako dnes (pojistka, že se
   nezměnilo chování ve výchozím stavu).
3. `RoadScene` **s** překryvem: bod, který leží 2,5 m od osy, je `IsRoad` teprve při naučené
   šířce 6 m, ne při mapových 3 m.
4. Updater přestaví, až když rozdíl přesáhne práh **a** uplynul odstup; pod prahem nepřestaví ani
   po odstupu.
5. **Scéna virtuální kamery překryv nedostane** — hlídá rozhodnutí 3. Test staví HW i korelátor
   a ověřuje, že po přestavbě renderer odpovídá pořád podle mapy.
6. `roadwidthmap=false` (výchozí): scéna korelátoru ani `MapMsg` se nezmění, i když má koridor
   důvěryhodné odhady.
7. Korelátor vidí uvnitř jednoho cyklu **jednu** scénu (rozhodnutí 5).
8. Dvě **čerstvé** instance updateru nad týmž sledem `RoadCorridorMsg` dají tytéž přestavby
   (record/replay — viz poznámka o stavovosti výš).

## Kroky

- **Fáze 1** — `RoadWidthOverrides` + volitelný překryv v `RoadScene` a `ToLogMessage` (testy 1–3).
  Nic se ještě nikam nenapojuje, výchozí chování beze změny.
- **Fáze 2** — vyměnitelná scéna v `MapCorrelator`u (testy 5, 7).
- **Fáze 3** — `RoadWidthMapUpdater`, parametr `roadwidthmap=`, napojení v runtime, republikace
  `MapMsg` (testy 4, 6).
- **Fáze 4** — ověření v simulaci: dvě mapy, koridor zapnutý, kontrola, že se půdorys po usazení
  odhadu překreslí a že se renderer nezměnil.

## Co se záměrně NEDĚLÁ

- **Perzistence** (rozhodnutí autora 15. 9. 2026). Naučená šířka **nepřežije restart**. Kdyby
  přežívala, přežil by i špatný odhad — a už by ho nikdo nepřepsal měřením, protože šířková brána
  ho brání. Cena je rozjezdová fáze na začátku každého běhu; ta je v řádu sekund na hranu.
- **Šířka per hrana** a s ní `MapMsg` verze 2 (rozhodnutí 2).
- **Zápis `width` tagů do `.osm`.** Mapa je vstup, ne výstup; přepisovat ji za běhu by znamenalo,
  že dva běhy nad „toutéž" mapou nejsou srovnatelné.
- **Změna plánovače.** Šířku z mapy dnes nečte — `GlobalNavigator` ji jen přeposílá do telemetrie.

## Otevřené

- **Prahy přestavby (0,25 m / 10 s) nejsou změřené.** Nastavit z prvního záznamu; jde to offline.
- **Jestli korelaci naučená šířka skutečně pomůže, se tímhle plánem nedozvíme** — `mapcorr` je
  ve výchozím stavu vypnutý a jeho tři podmínky pro ostré nasazení platí dál (viz
  [decisions.md](decisions.md)). Tenhle plán dodává **předpoklad**, ne výsledek.
- **Pravidlo maxima u styku chodník/vozovka** (rozhodnutí 2) — změřit dopad, až budou data.
