# Polární grid sjízdnosti z hloubkové kamery

Detekce sjízdnosti/překážek z hloubkového obrazu (`CameraFrame.ImageDepth`). Hloubkový obraz se
převede na point cloud, promítne do **polárního gridu** (robot-centrického, per-kamera) a v každé
buňce se vyhodnotí, zda plocha je sjízdná. Výstup slouží jako **podklad pro aktualizaci kartézského
occupancy gridu**, na kterém běží plánování cesty.

Kód: `Src/ARBot.Common/Vision/` — [`CameraFrameProcessor`](../Src/ARBot.Common/Vision/CameraFrameProcessor.cs),
[`PolarTraversabilityGrid`](../Src/ARBot.Common/Vision/PolarTraversabilityGrid.cs),
[`PolarGridConfig`](../Src/ARBot.Common/Vision/PolarGridConfig.cs).
Test: `Src/ARBot.Common.Tests/Vision/CameraFrameProcessorTest.cs`.

> **Aktualizace 2026-08-01 (krok 1–2 dle [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md)):**
> grid už **není samostatná zpráva** (`PolarTraversabilityGridMsg` zrušen). Je to
> `PolarTraversabilityGrid` **uvnitř `CameraFrame.Grid`** a počítá se **synchronně na vlákně kamery**
> přes `ICameraFrameProcessor`/`CameraFrameProcessor` (ne asynchronní `MessageProcessor` v grafu).
> Jádro výpočtu (`BuildGrid`, klasifikace, fit roviny, nativní/managed transform) je **beze změny** —
> níže popsaná geometrie a klasifikace platí dál, jen běží v `CameraFrameProcessor`.
>
> **Krok 3–4 hotové v kódu (2026-08-01, HW ověření pod zátěží čeká):** kamery **nejsou** v grafu; běží
> vlastním vláknem (grab + `Process` do **poolovaných** capture bufferů — `CaptureFramePool`) a `ControlLoop`
> je na tiku **pulluje** (`ICameraPullSource`) a **celý `CameraFrame`** (raw + grid) forwardne na `Stream`.
> Grid se **nekopíruje** (je per-snímek immutable → předává se referencí); velké image buffery si každý async
> odběratel (recorder/UI) kopíruje do vlastního poolu s release. Threading viz
> [plan-camera-vision-refactor.md](plan-camera-vision-refactor.md); geometrie/klasifikace níže platí beze změny.

## Výpočet gridu (dříve „pipeline stupeň")

`CameraFrameProcessor.Process(CameraFrame)` dopočte grid z `CameraFrame.ImageDepth` a uloží ho do
`CameraFrame.Grid` (per kamera; může být null, dokud projekce není k dispozici). Vzor výpočtu je stejný
jako u [`BackProjectProcessor`](../Src/ARBot.Common/Vision/BackProjectProcessor.cs) (probability), který
`CameraFrameProcessor` rovněž umí spočítat do `CameraFrame.ImageProbability`.

- **Robot-centrické** (rozhodnutí): používá **jen transformaci kamery vůči tělu robotu**
  (`Profile.LeftCameraTransform` / `RightCameraTransform`), **ne** světovou pózu. Detekce překážek
  tak nezávisí na kvalitě lokalizace. Projekce se předávají per kamera (klíč = `CameraFrame.Name`).

  > **Přesnost té transformace ale není jen kosmetika.** Pro detekci překážek stačí hrubá, ale grid
  > z ní jde dál do **korelace s mapou**, a tam se z chyby extrinsiky stane systematický bias, který
  > fúze **integruje** (je dokonale korelovaný napříč cykly, kdežto filtr měření bere jako nezávislá).
  > Chyba yaw 1° dá při dohledu 3–6 m příčný posun 5–10 cm, zatímco korelátor sám měří na 5 mm —
  > kalibrace je tedy pravděpodobně dominantní chybový člen. Podrobně a s čísly v
  > [map-correlation-localization.md](map-correlation-localization.md).
- **Per-kamera** (rozhodnutí): každá kamera má vlastní grid a **vlastní fit referenční roviny**.
  Důvody: (1) redundance — při výpadku jedné kamery běží druhá; (2) mizí systematický z-offset mezi
  kamerami (různý pitch −20,2° vs −18,6°); (3) v překryvu dostane kartézská vrstva dva nezávislé hlasy.
- **Depth → point cloud**: dvě cesty (přepínač `PolarGridConfig.UseNativeTransform`):
  - **managed** (fallback, čistě testovatelné) — `Vector3.Transform` per pixel přes
    `IDepthCameraProjection.Camera2DToCamera3D` + `Transformation`;
  - **nativní** (runtime default) — `NativeComputeUnit.DepthTransform2Impl` (SIMD) se **znovupoužitým**
    `Point4D[]` bufferem (žádná alokace/snímek). Pozor: převádí **mm→m interně** a zapisuje výstup v
    **opačném pořadí** (`cloud[len-1-p]` = bod pixelu `p`) — ošetřeno indexem. **Ekvivalence obou cest je
    ověřena testem.** Nepoužívá nativní `Segment2` (padá na x64).

### Zapojení do runtime ([`ARBotRuntime`](../Src/ARBot.Runtime/Robot/ARBotRuntime.cs))

- **Run:** grid počítá `CameraFrameProcessor` **synchronně na vlákně kamery** (nastaven kamerám v
  `WireRun`; už **není** samostatný stupeň grafu). Projekce se sestavuje **líně z připojené kamery**
  (`ICamera.CreateDepthProjector()` vyžaduje připojenou pipeline; kamera se připojuje líně) a nastaví
  se jí robot-centrická orientace `Profile.Left/RightCameraTransform` podle `CameraFrame.Name`
  (`BuildDepthProjectionResolver`). Dokud kamera není připojená, resolver vrací `null` a grid se
  přeskočí (frame jde dál bez gridu). Grid je součástí `CameraFrame`, který na `Stream` (a do záznamu/UI)
  **forwardne `ControlLoop`** po pullu — zaznamená se to, co řízení reálně vzorkovalo (nestíhané snímky
  kamera zahodí bez alokace, pull vrací nejnovější). Vizualizace ukazují **stáří zprávy** (Δ = teď −
  `TimeStamp`) pro diagnostiku latence.
- **View:** grid se **nepřepočítává** (rozhodnutí) — jen se **přehraje zaznamenaný** grid, který je nyní
  **uvnitř `CameraFrame`** (`CameraFrame.Grid`, FormatVersion 2). Ladění algoritmu tedy vyžaduje novou
  jízdu/záznam. *(Alternativa přepočtu ze záznamu je odložena — potřebovala by intrinsics offline; viz
  Otevřené úkoly.)* Staré `.rec` (v1) grid neobsahují a přehrají se bez něj.

### Vizualizace (robot-centrický pohled)

Dokovatelný dokument [`RobotCentricDocument`](../Src/ARBot/ViewModels/RobotCentricDocument.cs) +
control [`RobotCentricControl`](../Src/ARBot/Views/Controls/RobotCentricControl.cs): ptačí pohled,
robot dole uprostřed, směr vpřed nahoru (X vpřed → nahoru, Y vlevo → vlevo). Dokument je **obecně
robot-centrický** — časem přibudou další robot-centrické vrstvy (sjízdnost z RGB, okraje vozovky…);
zatím je vrstvou polární grid sjízdnosti. Každá buňka se kreslí jako svůj **skutečný půdorys =
mezikruhová výseč** (radiální pásmo z `RadialEdges` × azimutový slot sloupce), barva dle třídy
(zelená sjízdné / červená překážka / šedá neznámé), průhlednost dle `Confidence`; dosahové kružnice po
metrech; robot je vykreslen v měřítku gridu. Výseče se díky **sdíleným hranicím dokonale skládají**
(žádný překryv ani mezery). *(Dřívější vykreslení jako čtverec u těžiště se u robota překrývalo — šířka
čtverce byla vázaná na radiální tloušťku ≥ 5 cm, což je u malých vzdáleností mnohem víc než azimutová
rozteč sousedních buněk.)* Azimutové hranice grid **neukládá** (jen `ColumnsPerCell`) — renderer je
**rekonstruuje z ložisek buněk** (průměrný směr obsazených buněk sloupce → hranice do půlky mezi
sousedy; prázdné sloupce lin. interpolací). Řešeno čistě v `RobotCentricControl` (bez změny datového
modelu / serializace; funguje i ve View/replay); při málo datech fallback na původní čtverce. Odebírá `Stream` (Run i View), backpressure „latest-wins"
([Views/README.md](../Src/ARBot/Views/README.md)). Menu **Tools → Robot-centric**, ve View automaticky
po startu. Více kamer se kreslí přes sebe.

Tvar robotu je ve sdílené [`RobotGlyph`](../Src/ARBot/Views/Controls/RobotGlyph.cs) (tělo + 4 kola,
v metrech) s parametrem orientace (robot-centric volá „vpřed = nahoru"; world view zavolá s reálnou
orientací a pozicí robotu).

**Overlay přes depth ([`ImageDocument`](../Src/ARBot/ViewModels/ImageDocument.cs)):** grid se navíc
nabízí jako vrstva `"<kamera>/Traversability"`, rasterizovaná do velikosti depth snímku (per-pixel alfa,
prázdno/`Unknown` = průhledné) a zarovnaná přes `ColumnsPerCell` × `RadialEdge.Row`. Vybere se do overlay
slotu nad `"<kamera>/Depth"` a mísí se stávajícím posuvníkem průhlednosti — bez samostatného obrázku
tříd v záznamu (kreslí se z `CameraFrame.Grid`).

## Geometrie gridu

Střed = referenční bod robotu (osa rotace × zem). Souřadnice robot-rel. ENU: **X východ, Y sever,
Z nahoru**, směr vpřed = θ 0.

### Azimut — konstantní počet sloupců

Azimutová buňka = skupina **N sloupců obrazu** (`ColumnsPerCell`, default **16 → 30 buněk** při
depth 480×270). Volba „konstantní počet sloupců" (ne konstantní Δθ) je záměr: použitelná šířka musí
být beze zbytku dělitelná N, mapování obraz→buňka je celočíselné, bez aliasingu. Úhlová šířka buňky
mírně kolísá (pinhole: konstantní *tangens* na pixel, ne úhel) — to nevadí, protože reálné úhly bereme
z tabulky paprsků.

*(Pozn.: při ořezu krajních sloupců kvůli distorzi — `EdgeColumnTrim` — musí být dělitelná ta oříznutá
šířka.)*

### Radiálně — Δr od 5 cm rostoucí

Pravidlo: **Δr = max(5 cm, tolik, aby buňka držela cílový počet bodů)**. Blízko robotu je bodů
nadbytek → drží se podlaha 5 cm (kvůli návaznosti na kartézský occupancy ~5 cm). S dálkou klesá
hustota (řádků obrazu na buňku ubývá) → Δr roste. Hrany se **počítají při initu z geometrie kamery**
(`PolarGridConfig.BuildRadialEdges`): model = průsečík paprsku s rovinou země z=0, vzorkuje se střední
azimutová buňka, hrany se kladou tak, aby prstenec spanoval ≥ 5 cm a zároveň ≥ cíl bodů
(`TargetPointsPerCell / AssumedValidFraction`).

Orientačně (výška 0,52 m, sklon ~20°, VFOV ~58°): 5 cm zóna ~0,45 → ~2,8 m, dál Δr roste ~6 → ~40 cm,
za ~5,1 m už buňka nenasbírá 8 bodů → `Unknown`. Řádek→vzdálenost a odvození parametrů je v historii
návrhu (viz `doc/decisions.md`).

Každá radiální hrana je `RadialEdge { Range, Row }` — vzdálenost v metrech **a řádek depth obrazu**, kde
se hranice láme (referenční střední sloupec, model rovné země). Řádek umožňuje vykreslit grid **přímo
přes depth snímek** (azimut = skupina sloupců `ColumnsPerCell`, radiálně = pásmo řádků `[Row(r+1)…Row(r)]`)
bez samostatného obrázku tříd. To je využito v overlayi přes depth v `ImageDocument` (viz Vizualizace).

## Model buňky (`PolarCell`)

`Count`, `MeanX/MeanY` (těžiště [m]), `MeanZ` (výška), `StdZ` (drsnost), `MaxZ` (nejvyšší bod —
relevantní pro kolizi/průjezd), `EdgeRange` (sub-buňková vzdálenost nejbližšího bodu = náběžná hrana),
`Confidence` (0..1) a `Class`.

### Klasifikace (`TraversabilityClass`)

- `Unknown` — `Count` pod tvrdou podlahou `MinPointsPerCell` (8). **`Unknown` ≠ `Free`** — do
  kartézského occupancy se **nesmí** zapsat jako sjízdné (jinak si robot „prosvítí" díru za dohledem).
- `Obstacle` — příliš daleko od referenční roviny **nebo** drsné (`StdZ`) **nebo** strmé vůči sousedům.
  Prahy se škálují vzdáleností (šum depth roste s r).
- `Free` — jinak.

Referenční plocha: robustní **fit jedné roviny** z blízkých nízkých buněk (`PlaneParams`, managed).
Odchylka buňky = `centroid · plane.v` (viz `PlaneParams`). *Budoucí zlepšení:* per-azimut radiální
profil pro zvlněný terén.

### Důvěra (`Confidence`)

Váha pro agregaci do kartézského occupancy. `confidence = f_count · f_range · f_rough`:
- **f_count** — od podlahy (malé kladné) k cíli (1). `confidence == 0` ⇔ `Unknown`.
- **f_range** — klesá s r² (šum senzoru).
- **f_rough** — klesá s `StdZ` vůči očekávanému šumu `RoughRef(r)`.

Důvěra = důvěra ve **vykázanou hodnotu** (`MeanZ`), oddělená od `Class` (vysoká `StdZ` je pro *Obstacle*
naopak pozitivní signál).

## Vztah k TSDF / „vejde se robot"

Uvažovaná 2D (ptačí perspektiva) verze TSDF = **kartézský occupancy + distance transform** (inflace
o poloměr robotu) pro „vejde se robot", plus **per-azimut přesná náběžná hrana** (`EdgeRange`) pro jemnou
vzdálenost k překážce. Plný 3D TSDF je pro čistě přízemní sjízdnost overkill (a drahý na ARM); pro
převisy/podjezdy stačí 2,5D (`MaxZ` per buňka).

## Ladění: profil scény (od 24. 9. 2026)

**Tools → Profil scény** (`open=profile`) ukazuje graf výšky podle vodorovné vzdálenosti v jednom
azimutu gridu. Vidět jsou **surové body hloubky**, ze kterých buňky vznikly, a přes ně buňky:
třída, rovina ± tolerance, `MeanZ`/`StdZ`/`MaxZ`. Pod myší nástroj ukáže, **které kritérium
klasifikace** buňka překročila. Vysvětlení počítá tentýž kód jako `CameraFrameProcessor`, takže
nemůže lhát, a nesoulad s gridem hlásí. Běží v Run i ve View. Body se ve View přepočítávají
z hloubky a z popisu projekce, který je v záznamu od CameraFrame v4. Poznámka níž o tom, že se
intrinsiky nezaznamenávají, je tím zastaralá. Detail: [plan-profil-sceny.md](plan-profil-sceny.md).

## Prahy a šum změřené nad jízdami (10. 10. 2026)

`ARBot.Analyze prahy` (`vid-grid-prahy-realna-data`) běžel nad 46 záznamy, z toho 33 s jízdou;
každý 5. snímek dává 50 410 snímků jedné kamery s budoucí dráhou. Replika klasifikace sedí na
třídu v záznamu ve všech buňkách.

**Pravdou je projetá dráha.** Buňka je projetá, když její těžiště leží do 0,2 m od dráhy, kterou
robot vzápětí ujel. Dráha se integruje z fúzovaných `v`, `ω` od času expozice hloubky.
Z projetých se vyřazují buňky, v jejichž okolí 3×3 je něco nepřejetelného (vrchol nad 0,25 m):
to jsou lidé jdoucí před robotem. Měří se jen **falešné překážky** — přehlédnutou překážku tahle
pravda neodhalí. Plánovač se navíc soustavným falešným překážkám vyhýbá, takže ty jsou
podhodnocené.

- **Falešné překážky na projeté zemi: 0,28 %** klasifikovaných buněk.
  - Podle vzdálenosti: 0,17 % (0,5–1 m), 0,15 % (1–2 m), 0,46 % (2–3,5 m), 1,21 % (3,5–5,5 m).
  - Medián po jízdách: 0,13 / 0,26 / 0,66 % (do 2 / 2–4 / nad 4 m).
  - Mezi jízdami je velký rozptyl: ve 2–4 m od 0,03 do 4,9 %. Nejhorší jsou parky a Robotour;
    část z toho jsou skutečné nerovnosti.
  - Na jeden snímek kamery vychází ~0,6 falešné buňky na projeté dráze.
- **Stoupání** dělá 0,15 % z 0,28 %, a to vždy k *nízkému* sousedovi (výškový rozdíl jednotek cm).
  - U robotu jsou sousedé 4–6 cm od sebe (prstenec 5 cm), takže práh 0,35 spustí už **schod
    1,5–2 cm**. Ten je pod prahem odchylky 4–5 cm a ~100× nad chybou průměru buňky; je to tedy
    skutečný mikroreliéf nebo strukturovaná chyba hloubky, ne náhodný šum.
  - Spodní mez vzdálenosti sousedů 0,1 m by do 1 m odstranila 70 % falešných překážek
    (0,17 → 0,05 %). Ve všech buňkách by tam ubrala 4,9 → 2,9 % překážek a nevíme, kolik z nich
    jsou skutečné obrubníky. Do 1 m jsou všichni sousedé blíž než 0,1 m, takže je to tam totéž
    co `MaxSlope` ×2. Malá vzdálenost sama vysvětlí jen asi čtvrtinu tamních selhání, zbytek
    by selhal i v obvyklé vzdálenosti.
  - Za 2 m mají buňky, které stoupáním padnou, sousedy blíž než ostatní buňky (medián 0,66–0,80×).
    Tam spodní mez působí opravdu jako mez, ne jako vyšší práh.
- **Drsnost (`StdZ`) skoro nerozhoduje.**
  - Na projeté zemi je p99 2,5 mm + 0,9 mm·r², práh `2,5 × RoughRef` = 25 mm + 10 mm·r², tedy
    8–12× výš.
  - Samotná drsnost shodí 0,00 % projetých a nejvýš 0,02 % všech buněk. I práh ×0,2 přidá na
    projeté zemi jen +0,05 p. b.
  - `StdZ` je rozptyl uvnitř buňky (včetně sklonu × délky buňky), ne šum jednoho bodu. Korelovaný
    šum hloubky se projeví v průměru buňky, tedy v odchylce.
  - `RoughRef` určuje i váhu důvěry `fRough`, takže jeho změna mění i váhy v occupancy gridu.
- **Odchylka od roviny.**
  - p99 je do 1,6 m plochá (~2,5 cm) a od 2 m roste až na 12,8 cm v 5,4 m. Práh 3 cm + 2 cm/m je
    do 3 m 1,5–2,5× nad p99, ve 4–5,4 m už jen ~1,1–1,2×.
  - Zlom ve 2 m je mez proložení roviny (`PlaneFitMaxRangeM`). **Rovina proložená do 3,5 m** sníží
    p99 ve 3–5 m o 20–30 % (4,9 m: 11,8 → 8,8 cm), falešné překážky tam o 20–30 % a překážky ve
    všech buňkách o ~3 p. b. U robotu ale p99 stoupne 2,4 → 3,0 cm.
  - Zbytek roste zhruba lineárně, což odpovídá šumu hloubky: `σ_Z ∝ Z²` se u země pozorované pod
    nízkým úhlem promítne do výšky ∝ r. Velká část růstu za 2 m je ale tvar terénu, který jedna
    rovina neopíše.
- **Podíl platných pixelů** na projeté zemi je p50 ≈ 1,0 proti `AssumedValidFraction` 0,6.
  - Zúžit prstence ale není zadarmo. Do ~2,3 m rozhoduje podlaha 5 cm a nic se nezmění.
  - Dál mají prstence 2 řádky obrazu a šly by na 1 (o 50 % užší). p10 počtu bodů ve 4,4–4,9 m
    (16) by pak spadl na ~8 = `MinPointsPerCell` a přibylo by `Unknown`.
- **`Unknown` u robotu.** V 0,5–0,75 m je 29 % projetých buněk `Unknown`:
  - ~22 p. b. je geometrie: okraj zorného pole, buňka s méně než 8 pixely.
  - ~8 % jsou buňky s dost pixely bez platné hloubky. Soustředí se 0,1–0,2 m bokem od dráhy
    a jsou skoro stejné ve všech jízdách — nejspíš neplatný pruh levé kamery D435 nebo zákryt
    vlastním tělem, ne náhodný výpadek. Ověřit.

## Otevřené úkoly (→ registr)

Stav a data vede [registr úkolů](ukoly.md); tady je jen seznam, co se téhle oblasti týká.

- **[Prahy klasifikace a šumový model gridu sjízdnosti nejsou laděné na reálných datech](ukoly.md#vid-grid-prahy-realna-data)** —
  ladění prahů a šumového modelu (`RoughRef`, `MaxSlope`, škálování `MaxHeightDev`) nad záznamem
  z terénu; geometrie a klasifikátor jsou ověřené syntetickým testem. Patří k tomu i **radiální
  hrany**, které lze zpřesnit z reálného podílu platných pixelů (teď `AssumedValidFraction`).
- (bez tématu v registru) **Referenční plocha** — per-azimut profil místo jedné roviny, pokud zvlněný terén nestačí.
- **[Obtížně sjízdný povrch (hrbol, prasklina) jako rychlostní strop v lokální mapě](ukoly.md#lp-drsnost-povrchu-rychlostni-strop)** —
  `StdZ` a odchylka od roviny pod prahem `MaxHeightDev` dnes zůstávají v polární buňce a dál nejdou;
  záměr je přenést je jako spojitou drsnost do kartézského gridu a odvodit z ní rychlostní strop.
- **[Polární grid sjízdnosti z hloubkové kamery s robot-centrickým pohledem](ukoly.md#vid-polarni-grid-sjizdnosti)** —
  přepočet ve View ze záznamu je odložený: vyžadoval by projekci offline (živé intrinsics se
  nezaznamenávají) — buď nominální intrinsics D435 480×270 v `Profile`, nebo zaznamenat
  intrinsics/tabulku paprsků do `.rec`; View jen přehrává grid zaznamenaný v Run.
- **[Occupancy grid a lokální plánování nad ním](ukoly.md#lp-occupancy-grid-lokalni-planovani)** —
  agregace do kartézského occupancy + distance transform pro plánovač, viz
  [occupancy-and-local-planning.md](occupancy-and-local-planning.md). Pozn.: zápis do occupancy hledá
  azimutovou buňku **projekcí bodu země do obrazu** (sloupec), protože u sklopené kamery není sloupec
  konstantním azimutem — proto se azimutové hranice do gridu neukládají a renderer si je dál
  rekonstruuje z těžišť (a jinak to ani nejde).
- **[Polární grid sjízdnosti z hloubkové kamery s robot-centrickým pohledem](ukoly.md#vid-polarni-grid-sjizdnosti)** —
  výkon na ARM: managed per-pixel `Transform` se přesměroval na `NativeComputeUnit`
  (`DepthTransform2Impl` funguje na x64/ARM).
