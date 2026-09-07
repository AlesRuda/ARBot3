# Sémantická segmentace sjízdnosti (`backproject=nn`)

Převod **barva → pravděpodobnost sjízdnosti** je v ARBot3 za rozhraním
[`IBackProject`](../Src/ARBot.Common/Common/IBackProject.cs) a existují k němu dvě implementace,
které se přepínají parametrem `backproject=`:

| | `hist` (výchozí) | `nn` |
|---|---|---|
| třída | `BackProject` | `OnnxBackProject` |
| jak | zpětná projekce z histogramu barev (tabulka 4096 hodnot) | neuronová síť přes ONNX Runtime |
| rozlišení výstupu | plný snímek (640×480) | rozlišení modelu (128×128) |
| co potřebuje | nic | soubor `.onnx` (`nnmodel=`) |
| čas / snímek (Windows x64, Release) | **2,4 ms** | **11,3 ms** (int8) / **6,9 ms** (rozbalený, viz níž) |

Obojí vrací **spojitou** hodnotu 0..255, ne rozhodnutí ano/ne — occupancy fúze pracuje v log-odds
a mezilehlé hodnoty umí využít.

> ⚠️ **Na zařízení zatím neběželo.** Časy výše jsou z vývojového PC. Na Orange Pi se musí přeměřit
> (viz [Jak měřit](#jak-měřit)), protože poměr obou variant tam bude jiný — ORT má na ARM64
> vlastní int8 jádra. Jde přitom o **tutéž síť** ve dvou podobách: `_int8` počítá v int8,
> `_int8_deq` má tytéž váhy rozbalené do float (viz [Model](#model)).

## Model

V `models/` je model **Model61.1** přenesený z ARBot2 (únor 2021). Rozbor jeho TFLite souboru:

- **U-Net s MobileNetV2 bloky** (inverted residual: `expand` → `depthwise` → `project`),
  encoder 128 → 2 px, decoder zpět na 128 se skip-concat.
- 53× `Conv2D`, 24× `DepthwiseConv2D`, 6× `ResizeNearestNeighbor`, 4× `Concatenate`, `Logistic`.
- **112,5 MMAC** na snímek (MobileNetV2 @224² má pro srovnání ~300 MMAC).
- Vstup `[1,128,128,3]`, výstup `[1,128,128,2]`; **kanál 1 = sjízdno**.

Varianty souboru:

| soubor | k čemu |
|---|---|
| `Model61.1.tflite` | **dynamické tvary** (`Shape`→`StridedSlice`→`Mul`→`ResizeNN`) — deklarovaný výstup `[1,1,1,2]` je jen placeholder. Pro převod **nepoužitelný**. ⚠️ A **není to float**, ačkoli se to tu dřív psalo: `SaveTFLite` ho vyrábí s `optimizations=[Optimize.DEFAULT]` **bez** `representative_dataset`, tedy **dynamic-range kvantizací** — váhy int8, počítá se ve floatu. |
| `Model61.1_int8.tflite` | plně kvantovaný, statické tvary — **zdroj pro převod** |
| `Model61.1_int8.onnx` | převedený, int8 vnitřek (840 KB) — výchozí `nnmodel=` |
| `Model61.1_int8_deq.onnx` | **taky z `_int8.tflite`**, jen s `--dequantize`: kvantované váhy se rozbalí zpět do float (2,6 MB) — pro A/B |

⚠️ **`_deq` není původní float model.** Obě `.onnx` varianty pocházejí z **kvantovaného**
souboru; `--dequantize` jen rozbalí int8 váhy do float, takže **přesnost zůstává jako u int8**
a mění se jen aritmetika (a tím rychlost na dané platformě). **Referenční float model v repu
není žádný** — `Model61.1.tflite` má dynamické tvary *a* int8 váhy (viz tabulka výše).

### Odkud se berou ty dynamické tvary (a jak se jich zbavit)

Dekodér pětkrát zdvojnásobí rozlišení (`UpSampling2D(size=(2,2))`). `SaveTFLite` volá
`TFLiteConverter.from_keras_model(model)` **bez udání vstupního tvaru**, takže konvertor nemohl
spočítat vnitřní velikosti a místo *„zvětši na 8×8"* zapsal *„zjisti velikost vstupu, vynásob
dvěma a zvětši na to"* — odtud ten řetěz `Shape → StridedSlice → Mul → ResizeNN`, pětkrát.
Je to jako recept, který místo „forma 24 cm" říká „forma dvakrát širší než ta předchozí": upéct
se dá, ale **velikost výsledku z receptu nevyčteš**. Proto `tf2onnx` neprojde a proto soubor
deklaruje nesmyslný výstup 1×1 px.

**V tom souboru se to opravit nedá** (tvary tam jsou zapečené) — musí se **exportovat znovu
z Kerasu s pevným `tf.TensorSpec((1,128,128,3))`**, pak si exporter velikosti spočítá sám,
zapíše je jako konstanty a ten řetěz zmizí jako mrtvý kód. Hotové to má
[`Src/Colab/ExportFloatModel.ipynb`](../Src/Colab/ExportFloatModel.ipynb), který navíc **ověří,
že tvary jsou skutečně statické** (spočítá zbylé `Shape`/`Slice` uzly a pustí model na šumu),
místo aby to tvrdil.

EdgeTPU varianta (`*_edgetpu.tflite`) je kompilát pro Coral a **nekonvertuje se** — je to slepá
ulička, stejně jako celá třída `EdgeTPUSemanticSegmentation` (P/Invoke do `EdgeTPUDll.dll`,
jen `IsX64`), která v `ARBot.Common.csproj` zůstává vyřazená z překladu.

## Proč ONNX Runtime

Model je TFLite, takže nasnadě je TFLite runtime — ten by ale znamenal **vlastní nativní knihovnu
na obě platformy** (Windows x64 pro simulaci, ARM64 pro zařízení) a k tomu managed wrapper.
NuGet `Microsoft.ML.OnnxRuntime` nese nativní knihovnu pro `win-x64` i `linux-arm64` v jednom
balíčku, takže **v simulaci i na robotu běží týž kód** — a to je u téhle vrstvy podstatnější než
poslední milisekunda: vizuální cestu se ladí nad záznamy na Windows.

Cena: publish pro OrangePI naroste ze 45 MB na **70 MB** (`libonnxruntime.so` má 24,5 MB).

NPU Orange Pi (RK3588, 3× jádro po ~2 TOPS) je další krok, ne tenhle — viz
[Další krok: NPU](#další-krok-npu).

## Kontrakt modelu: float na hranici, int8 uvnitř

`OnnxBackProject` **vědomě nepodporuje kvantované I/O** a na model s `uint8` vstupem zahlásí chybu.
Důvod: kvantovaný vstup znamená, že volající musí znát `scale` a `zero_point` modelu a kvantizovat
sám — přesně to dělal ARBot2 ručně v C++ (`EdgeTPUDll/EdgeTPU.cpp`). Špatná konstanta by se
neprojevila jako chyba, ale jako **tiše horší segmentace**.

Skript `models/tflite2onnx.py` proto po převodu **zahodí úvodní `DequantizeLinear` a závěrečný
`QuantizeLinear`**: vnitřek modelu zůstane int8, ale na hranici se mluví v reálných jednotkách —
vstup 0..1, výstup pravděpodobnost. C# strana pak nemá žádné magické konstanty.

U tohoto modelu byla vstupní kvantizace `scale=0,00452162  zero_point=9`, tedy **ne** identita
0..255 → posílat syrové bajty by byl systematický posun (gain 1,15, offset −0,04).

### Převod

```bash
python -m venv .venv && .venv/bin/pip install tensorflow-cpu==2.15.1 tf2onnx==1.16.1 onnx==1.16.1 onnxruntime
.venv/bin/python models/tflite2onnx.py models/Model61.1_int8.tflite models/Model61.1_int8.onnx --check <obrazek.png>
```

Na Windows to nejde (tf2onnx potřebuje TensorFlow a Python 3.11) — dělá se to **ve WSL**,
stejně jako cross-compile nativní knihovny (viz [build-and-platforms.md](build-and-platforms.md)).

`--check` porovná výsledek proti původnímu TFLite. Naměřeno při zavedení:
**shoda rozhodnutí 99,1 %**, průměrný rozdíl pravděpodobnosti 0,011 (na reálném obrázku;
na náhodném šumu je rozdíl větší, ale tam je model nerozhodný a číslo nic neznamená).

## Předzpracování a postprocessing

Původně převzaté z ARBot2 (`EdgeTPUDll/EdgeTPU.cpp`, funkce `SemanticSegmentation`) jako jediné
reference, jak byl model **skutečně používán**. ✅ **Od 7. 9. 2026 je to potvrzené proti tréninku**
(viz [Jak model vznikl](#jak-model-vznikl-trénovací-notebook)) — ARBot2 to opsal správně:

- **Pořadí kanálů RGB.** Zdroj je `BGR32` (bajty B, G, R, X) a do tenzoru se plnilo
  `src[+2], src[+1], src[+0]`. Trénink čte obrázky `tf.image.decode_jpeg`, což dává **RGB** —
  takže tady už není co měřit a přepínač `nnchannels=rgb|bgr` zůstává jen jako pojistka pro
  jiný model. *(Dokud se to nevědělo, byl to reálný risk: na šedivé cestě a zelené trávě se obě
  pořadí liší jen o jednotky procent, protože R a B jsou u obojího nízké, takže **omyl by nebyl
  vidět jako chyba**.)*
- **Normalizace `v/255`**, kvantizaci si dělá model sám (viz výše). Trénink dělá
  `tf.image.convert_image_dtype(..., tf.float32)`, tedy rovněž 0..1.
- **Výstup se normalizuje součtem kanálů.** Není to kosmetika: ARBot2 rozhodovalo
  `out[0] < out[1]`, kdežto práh 128 nad surovým kanálem 1 by dal **jiný výsledek** — model končí
  sigmoidou, takže součet kanálů není přesně 1 (naměřený rozsah 0,85 až 1,18). Po normalizaci
  odpovídá práh 128 přesně původnímu rozhodnutí. Hlídá to test
  `FillProbability_PrahJeStejneRozhodnutiJakoArgmax`.

## Zapojení do pipeline

`ARBotRuntime.BuildBackProject()` vybere implementaci podle `backproject=` a **každá kamera
dostane vlastní instanci** — `Process` běží synchronně na vlákně své kamery a obě implementace
drží předalokované buffery, takže sdílená instance by si data přepisovala. `OnnxBackProject` je
`IDisposable` (drží nativní session) a uklízí se spolu s `CameraFrameProcessor`.

**Chybějící nebo vadný model je chyba při startu, ne tichý návrat k histogramu** — stejný důvod
jako u neznámého klíče v profilu ([configuration.md](configuration.md)): tichý fallback by
znamenal, že A/B měření sítě by nepozorovaně měřilo histogram.

`Process` v ustáleném stavu **nealokuje** (vstup i výstup jsou `OrtValue` nad vlastními poli);
hlídá to test `Process_NealokujeVUstalenemStavu` s limitem 1 kB na snímek.

### ⚠️ Síť mění rozlišení pravděpodobnostního obrazu

Histogram vrací plný snímek (640×480), síť **128×128**. `CameraFrameProcessor` to unese —
`Size()` řídí, jak velký obraz se alokuje, a `PathEdges(prob, sx, sy)` dostává škálování — ale
mění se tím **hustota dat** pro occupancy grid i pro hranice cesty. Jeden pixel výstupu sítě
odpovídá zhruba 5×4 px původního snímku. Co to udělá s přesností hranic koridoru
([map-correlation-localization.md](map-correlation-localization.md)), **naměřené není**.

Navíc se snímek 640×480 zmenšuje na čtvercových 128×128, tedy **s deformací poměru stran**.

⚠️ **Neopravovat — je to replika tréninku, ne nedbalost.** Notebook zmenšuje celý snímek
z kamery na `shape=(128, 128)`, tedy dělá **tentýž squash 4:3 → 1:1**. Vstup 160×128 „se
správným poměrem stran" by modelu vnutil geometrii, jakou nikdy neviděl. Ze stejného důvodu
není zdarma ani vyšší rozlišení (256×256): encoder zmenšuje 64× a byl trénovaný na tom, jak
velké jsou objekty ve 128×128 — změna rozlišení mění vztah receptivního pole k velikosti
objektů. Obojí je tedy **věc přetrénování, ne konfigurace**.

Souhlasí i **metoda zmenšení**: trénink používá `tf.image.resize(..., method='nearest')`
a [`Image.Resize`](../Src/ARBot.Common/Common/Image.cs) je nearest-neighbour se `floor`.
Přechod na plošné průměrování (area/bilinear) by byl odchylka od tréninku — mohl by pomoct,
ale musel by se změřit, ne předpokládat.

## Jak model vznikl (trénovací notebook)

`Src/Colab/SemanticSegmentation.ipynb` (do repa 7. 9. 2026) je **Colab notebook, ve kterém
Model61.1 vznikl** — historie ~40 architektur, trénink, kvantizace, export do TFLite. Do té doby
byl model černá skříňka a ARBot2 jediná stopa; teď se dá dohledat, co je záměr a co náhoda.

**Data:** LabelBox projekt `ck2iqtrixgi6z08113jpu9imr`, maska **bílá = sjízdno (1)**, ostatní 0.
Testovací sada je **pevný seznam 50 snímků** vyjmenovaný jmény přímo v notebooku (cela 13),
zbytek je trénink — takže je to **reprodukovatelné rozdělení**, ne náhodné při každém běhu.
Data jsou z let 2019–2022 (podle id), tedy z kamery ARBot2, ne z dnešní D435.

**Augmentace:** náhodný zoom-crop 0,5–1,0 z celého snímku, převrácení vlevo/vpravo, jasnost
±0,2, kontrast 0,8–1,0, saturace 0,8–1,2, gaussovský šum σ = 5/255. Žádná rotace. CLAHE je
v kódu, ale **zakomentované** — použilo se jen u Model96.1.

⚠️ **Klíč k LabelBoxu do notebooku nepatří** — čte se z Colab Secrets / proměnné prostředí
(`LABELBOX_API_KEY`). Proč, a co se stalo, je v [CLAUDE.md](../CLAUDE.md).

⚠️ **Data se tím notebookem už stáhnout nedají** (změřeno 7. 9. 2026): `DownloadLabelBox2` stojí
na `project.label_generator()` a ten dotaz **na serveru LabelBoxu neexistuje** — vrací
`GraphQL validation error`. Klíč ani oprávnění v tom nejsou, `get_project()` projde. Připnutí
starého SDK nepomůže, protože se změnila serverová strana. Dnešní cesta je
`project.export()` → `ExportTask` → `get_buffered_stream()` a hotovou ji má
[`Src/Colab/ExportTestSet.ipynb`](../Src/Colab/ExportTestSet.ipynb); odtud se dá převzít, až
bude potřeba stáhnout **trénovací** data (my zatím potřebujeme jen testovací sadu).
Praktický důsledek: **trénink se dnes nedá zopakovat jedním kliknutím** — ta cela se musí
nejdřív přepsat.

### Model61.1 nebyl nejlepší — byl nejlepší, co se vešlo do Coralu

Přesnosti jsou v názvech souborů vah (metrika `sparse_categorical_accuracy`, tedy **per-pixel
přesnost** na té 50snímkové sadě, ne IoU):

| model | přesnost | architektura |
|---|---|---|
| **Model61.1** (v `models/`) | **0,9546** | `GenericModel20`, 6 stupňů, kanály 8→256, kernel 3 |
| Model61.3 | 0,9599 | **táž architektura**, jiný běh |
| Model96.1 | 0,9657 | `GenericModel25`, 2 stupně, 128 kanálů, **kernel 9**, s CLAHE |
| **Model96.2** | **0,9682** | totéž bez CLAHE |

A je tam napsané, proč se do ARBot2 dostal ten slabší — u modelů s kernelem 9:

> „kernel 9x9 daval pekne vysledky, bohuzel po kvantizaci model nebezel na Google Coral"

**Ta podmínka dnes neplatí** (Coral je pryč, jedeme ONNX). Cena je ale jinde: Model96.2 zmenšuje
obraz jen 4× a drží 128 kanálů i na plném 128×128, takže **jedna** jeho 1×1 konvoluce 128→128
stojí ~270 MMAC — víc než **celý** Model61.1 (112,5 MMAC). Odhad celku je **řádově 2–3 GMAC,
tedy 20–30× dražší**; na CPU Orange Pi to je mimo hru a je to kandidát **až pro NPU**. Přesné
číslo dá profilovací cela notebooku (počítá FLOPs), to je práce pro Colab.

**Model61.3 je proti tomu zajímavý hned:** shodné volání `GenericModel20` se shodným encoderem
i decoderem, tedy **stejná cena**, o 0,5 p. b. lepší. Jeho váhy/TFLite jsou na Google Drive
autora, ne v repu — kdyby se pořídily, je to výměna souboru a `nnmodel=`.

### V notebooku je i měřidlo proti pravdě

Vedle sítí je tam **histogram jako Keras vrstva** (`BackProjectModel` / `BackProjectLayer`, LUT
16×16×16 nad 4bitovým RGB) a `ModelAccuracy()`. Tím se vyrobilo srovnání „síť ~96 % proti
histogramu ~80 %" a **tím se dá zavřít otevřená otázka kvality** — proti ground truth, ne jen
shodou dvou metod, jak to dnes umí `ARBot.Analyze backproject`.

⚠️ **To „80 %" ale není číslo našeho dnešního histogramu.** LUT v notebooku je **binární** (0/1),
kdežto ARBot3 jede na **odstupňované** tabulce `BackProject.RoadProbability` (hodnoty 0–255)
a s jiným pořadím osí indexu. Jsou to dva různé histogramy; než se to číslo použije jako
srovnávací základ, musí se přeměřit s naší tabulkou.

*(Pozor na jména: v `BackProject` jsou tabulky **dvě** a ta jménem `RoadProbabilityNew` je
**nepoužitá** — runtime i všechna měřidla berou `RoadProbability`, viz
`ARBotRuntime.BuildBackProject()`. „New" tedy neznamená „aktuální".)*

## Jak měřit

Dvě různé otázky, dvě různá měřidla — a **nesmí se zaměnit**: shoda s histogramem říká, jak moc
se metody rozcházejí, přesnost proti pravdě říká, **která se mýlí**.

**Proti pravdě** (`models/testset/`, od 7. 9. 2026):

```bash
ARBot.Analyze backproject --truth=models/testset [--png=<prefix>]
```

Záznam nepotřebuje. Vypíše pro **síť i histogram** přesnost per-pixel, **IoU**, precision a recall
— souhrnně i per snímek — a s `--png` uloží srovnání **nejhoršího** snímku (vstup | pravda |
histogram | síť); průměr totiž neřekne, *jak* se metoda mýlí, a to je to, co se opravuje.

Hlavní čísla jsou **na rozměru výstupu sítě (128×128)** s pravdou zmenšenou nejbližším sousedem —
tak to počítal notebook, jinak by nebyla srovnatelná s jeho 0,9546. Histogram se měří **i v plném
rozlišení**, protože to je jeho skutečný provozní bod; ty dvě sekce výstupu se nesmí míchat.

⚠️ **Přesnost per-pixel je u téhle úlohy zrádná sama pro sebe:** když je na snímku 80 % plochy
sjízdné, odpověď „všechno je cesta" má přesnost 0,80. Proto se tiskne i IoU a rozpad chyby na
**cestu přidanou, kde není** (nebezpečné) a **cestu zamlčenou** (jen opatrné) — to jsou různě
drahé chyby. Jádro měření je [`SegmentationMetrics`](../Src/ARBot.Common/Vision/SegmentationMetrics.cs)
v `Common`, ne v nástroji, **aby šlo testovat proti známým odpovědím** (9 testů): špatně počítaná
metrika se neprojeví jako chyba, ale jako výsledek, který vypadá rozumně.

Formát sady a jak ji pořídit: [`models/testset/README.md`](../models/testset/README.md).

### ✅ Naměřeno proti pravdě (7. 9. 2026, 50 snímků, na rozměru sítě)

| | přesnost | IoU | precision | recall | cesta přidaná (FP) | cesta zamlčená (FN) |
|---|---|---|---|---|---|---|
| **síť** (`Model61.1_int8`) | **88,2 %** | **0,846** | 0,891 | 0,943 | **7,9 %** | 3,9 % |
| histogram | 78,0 % | 0,752 | 0,767 | 0,975 | **20,3 %** | 1,7 % |
| „všechno je cesta" | 68,7 % | — | — | — | — | — |

**Síť vyhrává, a rozhoduje o tom ta správná chyba:** histogram **vymýšlí cestu, kde není,
2,6× častěji** (20,3 % proti 7,9 % plochy) — a to je u robota ta nebezpečná chyba. Jeho o málo
lepší recall (0,975) je jen důsledek toho, že říká „cesta" skoro všude: medián jeho predikce je
92,2 % plochy proti pravdě 83,2 %.

**Bez toho triviálního řádku by se to číslo přečetlo špatně.** Odpověď „všechno je cesta" má na
téhle sadě přesnost **68,7 %**, takže histogramových 78 % je nad nicneděláním jen o 9 p. b.,
zatímco síť o 19,5. Proto se přesnost per-pixel netiskne samotná.

**Histogram vyšel 78,0 % proti ~80 % z notebooku** — tak blízko, že to potvrzuje celý měřicí
řetěz (naše odstupňovaná LUT si vede skoro stejně jako binární z notebooku).

### ⚠️ Mezera 88,2 vs 95,5 % zůstává nevysvětlená — a pět cest je zamítnutých MĚŘENÍM

Číslo v názvu vah slibuje 95,5 %, na naší cestě model dává 88,2 %. **Do vyřešení se nesmí
tvrdit, že „model dává 95 %".** Nezkoušej znovu tohle, je to proměřené:

| hypotéza | verdikt |
|---|---|
| **kvantizace** (int8 proti float) | ❌ float model z `.h5` dá **88,16 %** proti int8 **88,23 %** — rozdíl 0,07 p. b., a float je o vlásek *horší* |
| **jiný checkpoint** v `.tflite` | ❌ měřeno přímo na `.h5`, jehož jméno to číslo nese |
| **naše rekonstrukce sady** | ❌ na **originálním `ds_train`** vyjde **88,15 %**, na našem exportu 88,16 % — export je věrný |
| **pořadí kanálů** | ❌ RGB 88,2 % proti BGR **77,2 %**; RGB je správně (dřív jen odvozeno z tréninku, teď změřeno) |
| **vzorkování při zmenšení** | ❌ střed bloku (jako `tf.image.resize`) proti rohu (jako `Image.Resize`) dá 88,22 vs 88,16 % — **0,06 p. b.** |
| **novější snímky v sadě jsou těžší** | ❌ naopak: na `cl4*` (2022) má síť 89,6 %, na `ck*` (2019–21) jen 87,4 % |

`--truththreshold=` (masky 0/1 v `ds_train` proti 0/255 v `models/testset`) a `--resizecenter`
zůstaly v nástroji právě proto, aby se ta měření dala zopakovat.

**Co zbývá:** změřit to **jejich kódem** — `ModelAccuracy(model, test_dataset)` v Colabu nad
načteným `.h5`. Když jejich metrika dá taky ~88 %, pak to číslo 0,9546 **nepatří k téhle sadě**
(seznam `fnTest` obsahuje id z **června 2022**, ačkoli model je z února 2021 — tehdejší testovací
sada byla nutně jiná a dnes ji nelze zrekonstruovat). Když dá 95 %, je rozdíl v naší cestě
a hledá se dál.

### ⚠️ Verdikt „síť vyhrává" není napříč sadou stejný

| podmnožina | síť | histogram |
|---|---|---|
| `ck*` — 32 snímků, 2019–2021 | **87,4 % / IoU 0,812** | 71,2 % / 0,659 |
| `cl4*` — 18 snímků, červen 2022 | 89,6 % / 0,886 | **90,3 % / 0,899** |

**Na novějších snímcích je histogram nepatrně lepší než síť.** Celkovou výhru sítě tedy táhne
starší, rozmanitější část sady; na novější části jsou obě metody vyrovnané. Než se z toho udělá
závěr o kvalitě, chybí popis těch dvou podmnožin — proč jsou pro barvu snadné.

**Histogram v plném rozlišení dá 78,2 % / IoU 0,754**, tedy proti 128×128 rozdíl v šumu — zmenšení
na rozměr sítě ho na téhle metrice **nestojí nic**. O přesnosti *hranic* to nevypovídá, ta se měří
jinde ([map-correlation-localization.md](map-correlation-localization.md)).

**Nejhorší snímek je poučný sám o sobě**
([obrázek](media/truth-ckhfbvg54001g3r63mnib1vhv.png)): listnatý lesní podklad, kde pravda říká
**nic není sjízdné** — a histogram tvrdí 93 %, protože suché listí mu padá do barev cesty. Síť dá
25 %. Je to zároveň důkaz, že ta nulová maska je **správná, ne rozbitá**.

**Nad záznamem** (shoda s histogramem, čas na tomto stroji):

```bash
ARBot.Analyze backproject <zaznam.rec> --limit=80 --png=<prefix>
```

Vypíše — **za každou kameru zvlášť** — čas obou převodů, **shodu rozhodnutí** při prahu 128
a podíl sjízdné plochy; `--png` uloží jeden obrázek se vstupem, histogramem a sítí vedle sebe.
`--model=` vybere jiný model, `--bgr` přehodí pořadí kanálů, `--skip` se podívá dál do záznamu
(prvních pár desítek snímků často pochází z jednoho místa).

Statistika je **zvlášť za každou kameru** — míchat je dohromady je past: 6. 9. 2026 měla pravá
D435 zamrzlý barevný stream (viz [hardware.md](hardware.md)), takže polovina snímků byla tentýž
obraz a průměr přes obě kamery vypadal podezřele stabilně. Report proto počítá i **kolik různých
obrazů** kamera dodala a na jediný obraz upozorní.

**Simulace** — `records/20260823-182213.rec` (80 snímků, Release, 6. 9. 2026):

| | čas p50 | shoda s histogramem | podíl sjízdné plochy |
|---|---|---|---|
| `Model61.1_int8.onnx` (int8) | 11,3 ms | 97,4 % | 53,3 % (histogram 55,0 %) |
| `Model61.1_int8_deq.onnx` (rozbalený do float) | 6,9 ms | 97,5 % | 53,2 % |
| histogram | 2,4 ms | — | 55,0 % |

**Venku, ze zařízení** — `records/test/20260906-082403.rec`, levá (funkční) kamera, 7. 9. 2026:

| | čas p50 | shoda s histogramem | podíl sjízdné plochy |
|---|---|---|---|
| síť (int8) | 10,5–14,8 ms | **89,6 %** (p50; min 24 %) | 70–75 % |
| histogram | 2,4 ms | — | 80–85 % |

![síť proti histogramu na venkovním snímku](media/backproject-nn-vs-hist-20260907.png)

*Vlevo vstup, uprostřed histogram, vpravo síť.* Tady je vidět, **v čem se liší**: histogram
rozhoduje per-pixel podle barvy, takže na skutečném asfaltu **zrní** (jednotlivé černé pixely
uprostřed cesty) a hranice trávy je roztřepená; síť má kontext, dá souvislou plochu a měkký
přechod. Na simulované scéně tenhle rozdíl vidět není — tam má histogram jednolitě šedou cestu.

⚠️ **Čísla nejsou verdikt „síť je lepší".** Ground truth k záznamu není, takže shoda 89,6 % říká
jen, jak moc se obě metody rozcházejí, ne kdo má pravdu. Na zarostlé ploše bez zjevné cesty
(`--skip=2000`) je síť **nejistá** (výstup kolem 0,5), zatímco histogram tvrdí 80 % sjízdné —
a která odpověď je správná, se z těch dat nepozná. Simulační shoda 97,4 % je hlavně **kontrola
implementace** (špatné pořadí kanálů nebo chybějící normalizace by ji srazily).

Záznam `20260902-222601.rec` je k porovnání nepoužitelný — je natočený **za tmy** (histogram tam
označí 92 % plochy za sjízdnou, shoda vyjde 41 %).

**Na robotu** (skutečný čas): pustit s `backproject=nn` a číst `traversability-timing-*.csv`
(sloupec `compute_ms`) nebo panel *Tools → Výkon*, viz [perf-monitoring.md](perf-monitoring.md).
`ARBot.Analyze` se na Pi nestaví, měří se tedy za běhu.

## Otevřené otázky

- **Neběželo to na zařízení.** Čas na Orange Pi je jediné číslo, které rozhoduje, jestli je tahle
  cesta použitelná: při dvou kamerách po 30 snímcích/s stojí síť na vývojovém PC 68 % jednoho
  jádra (int8) resp. 41 % (rozbalený do float).
- **Který model je na ARM rychlejší, se neví.** Na x86 vyhrává float 6,9 : 11,3 ms; na ARM64 to
  může být obráceně (dotprod / i8mm jádra pro int8). Výchozí je int8 (menší soubor, blíž k tomu,
  co by chtělo NPU).
- ~~**Pořadí kanálů** je převzaté z ARBot2, ne ověřené měřením.~~ **Zavřeno 7. 9. 2026** — trénink
  čte `decode_jpeg`, tedy RGB. Viz [Předzpracování](#předzpracování-a-postprocessing).
- **Kvalita modelu na dnešních datech** je neznámá — ale úžeji, než se dosud psalo. Model **měl**
  naměřenou výhodu: 0,9546 per-pixel proti ~0,80 histogramu na pevné 50snímkové sadě
  ([viz výše](#v-notebooku-je-i-měřidlo-proti-pravdě)). Neznámé je, jak si ta výhoda stojí na
  **datech z D435 v roce 2026** — jiná kamera, jiné scény, a trénovací data jsou z 2019–2022.
  Na venkovním záznamu dává síť zjevně čistší obraz cesty, na zarostlé ploše je nerozhodná;
  bez ground truth k **našim** záznamům je to pořád jen rozpor dvou metod.
- **Dopad menšího rozlišení** na hranice cesty a occupancy grid není naměřený. Pozor: **není to
  věc konfigurace** — vyšší rozlišení vstupu znamená přetrénování, viz
  [Síť mění rozlišení](#-síť-mění-rozlišení-pravděpodobnostního-obrazu).
- **Testovací sada s ground truth v repu ještě není** — měřidlo (`--truth=`) i exportní notebook
  ([`Src/Colab/ExportTestSet.ipynb`](../Src/Colab/ExportTestSet.ipynb)) hotové jsou a měřidlo je
  projeté na syntetickém páru se známou odpovědí, ale **na reálné sadě to zatím neběželo**: export
  z LabelBoxu musí pustit autor (klíč je jeho). Do té doby platí, že každá změna modelu je hádání.

## Další krok: NPU

Orange Pi 5 Ultra má **RK3588** s NPU (3 jádra, ~2 TOPS každé). Pro tenhle model by inference
klesla na jednotky ms **a hlavně by přestala brát CPU** řídicí smyčce. Kroky, kdyby na to došlo:

1. **Ověřit driver** — na tom celá cesta stojí: `ls /dev/rknpu*`, `dmesg | grep -i rknpu`,
   `cat /sys/kernel/debug/rknpu/version`. BSP kernel (rockchip 6.1) driver má, mainline ne.
2. Převést `rknn-toolkit2` na x86 Linux (WSL, Python 3.8–3.11) → `.rknn`. RKNN má **vlastní**
   kvantizační schéma, takže import už kvantovaného TFLite dá jiné výsledky než float model
   s kalibračním datasetem.
3. **Změřit dřív, než se píše C++**: `rknn_benchmark` nebo `rknn-toolkit-lite2` (Python přímo
   na Pi) dá skutečná čísla za deset minut.
4. Teprve pak integrace: **ne** P/Invoke na `rknn_api` přímo (velké verzované struktury), ale
   tři funkce ve stávající `libNativeLib.so` (`Src/NativeFuncs`, CMake) — `NnOpen` / `NnRun` /
   `NnClose`, uvnitř zero-copy (`rknn_create_mem` + `rknn_set_io_mem`) a `rknn_set_core_mask`
   podle indexu kamery, aby každá kamera měla vlastní NPU jádro. `librknnrt.so` načíst přes
   `dlopen` lazy, aby knihovna šla přeložit i bez SDK.

Hlavní argument pro NPU není FPS, ale **strop**: 128×128 je na sjízdnost hrubé a jakmile se vstup
zvětší, poroste čas kvadraticky.

## Kód

- [`Src/ARBot.Common/Vision/Nn/OnnxBackProject.cs`](../Src/ARBot.Common/Vision/Nn/OnnxBackProject.cs) — implementace
- [`Src/ARBot.Common.Tests/Vision/OnnxBackProjectTest.cs`](../Src/ARBot.Common.Tests/Vision/OnnxBackProjectTest.cs) — testy (integrační se přeskočí, když model chybí)
- [`Src/ARBot.Analyze/BackProjectReport.cs`](../Src/ARBot.Analyze/BackProjectReport.cs) — měřidlo nad záznamem
- [`Src/ARBot.Analyze/BackProjectReportTruth.cs`](../Src/ARBot.Analyze/BackProjectReportTruth.cs) — měřidlo proti pravdě (`--truth=`)
- [`Src/ARBot.Common/Vision/SegmentationMetrics.cs`](../Src/ARBot.Common/Vision/SegmentationMetrics.cs) — metriky (testované jádro)
- [`models/tflite2onnx.py`](../models/tflite2onnx.py) — převod modelu z TFLite (int8 cesta)
- [`Src/Colab/SemanticSegmentation.ipynb`](../Src/Colab/SemanticSegmentation.ipynb) — trénovací notebook (Colab), ve kterém model vznikl
- [`Src/Colab/ExportTestSet.ipynb`](../Src/Colab/ExportTestSet.ipynb) — export testovací sady s ground truth z LabelBoxu
- [`Src/Colab/ExportFloatModel.ipynb`](../Src/Colab/ExportFloatModel.ipynb) — export **float** modelu z `.h5` do ONNX s pevnými tvary
- `ARBotRuntime.BuildBackProject()` — výběr implementace
