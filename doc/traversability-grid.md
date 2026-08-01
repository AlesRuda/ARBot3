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

### Zapojení do runtime ([`ARBotRuntime`](../Src/ARBot/Robot/ARBotRuntime.cs))

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
zatím je vrstvou polární grid sjízdnosti. Každá buňka = vyplněný čtverec u těžiště, barva dle třídy
(zelená sjízdné / červená překážka / šedá neznámé), průhlednost dle `Confidence`; dosahové kružnice po
metrech; robot je vykreslen v měřítku gridu. Odebírá `Stream` (Run i View), backpressure „latest-wins"
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

## Otevřené úkoly

- **Ladění prahů a šumového modelu** (`RoughRef`, `MaxSlope`, škálování `MaxHeightDev`) na reálných
  datech — kamera zatím není namontovaná; geometrie a klasifikátor ověřeny syntetickým testem.
- **Radiální hrany** lze zpřesnit z reálného podílu platných pixelů (teď `AssumedValidFraction`).
- **Referenční plocha** — per-azimut profil místo jedné roviny, pokud zvlněný terén nestačí.
- **Přepočet ve View** ze záznamu (odloženo) — vyžadoval by projekci offline (živé intrinsics se
  nezaznamenávají): buď nominální intrinsics D435 480×270 v `Profile`, nebo zaznamenat intrinsics/tabulku
  paprsků do `.rec`. Zvoleno zatím: View jen přehrává grid zaznamenaný v Run.
- **Agregace do kartézského occupancy** + distance transform pro plánovač (další stupeň).
- **Výkon na ARM** — managed per-pixel `Transform`; případně přesměrovat na `NativeComputeUnit`
  (`DepthTransform2Impl` funguje na x64/ARM).
