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
| `Model61.1.tflite` | float, **dynamické tvary** (`Shape`→`StridedSlice`→`Mul`→`ResizeNN`) — deklarovaný výstup `[1,1,1,2]` je jen placeholder. Pro převod **nepoužitelný**. |
| `Model61.1_int8.tflite` | plně kvantovaný, statické tvary — **zdroj pro převod** |
| `Model61.1_int8.onnx` | převedený, int8 vnitřek (840 KB) — výchozí `nnmodel=` |
| `Model61.1_int8_deq.onnx` | **taky z `_int8.tflite`**, jen s `--dequantize`: kvantované váhy se rozbalí zpět do float (2,6 MB) — pro A/B |

⚠️ **`_deq` není původní float model.** Obě `.onnx` varianty pocházejí z **kvantovaného**
souboru; `--dequantize` jen rozbalí int8 váhy do float, takže **přesnost zůstává jako u int8**
a mění se jen aritmetika (a tím rychlost na dané platformě). Původní float váhy z
`Model61.1.tflite` použít nejdou — má dynamické tvary. Kdyby se někdy hodily, musel by se
reexportovat z Keras/TF zdroje s pevnými tvary.

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

Obojí je převzaté z ARBot2 (`EdgeTPUDll/EdgeTPU.cpp`, funkce `SemanticSegmentation`), protože
to je jediná reference, jak byl model **skutečně používán**:

- **Pořadí kanálů RGB.** Zdroj je `BGR32` (bajty B, G, R, X) a do tenzoru se plnilo
  `src[+2], src[+1], src[+0]`. Přepínač `nnchannels=rgb|bgr` je tu na A/B: na šedivé cestě
  a zelené trávě se obě pořadí liší jen o jednotky procent (R a B jsou u obojího nízké),
  takže **omyl by nebyl vidět jako chyba** — musí se změřit na barevnější scéně.
- **Normalizace `v/255`**, kvantizaci si dělá model sám (viz výše).
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
Je to schválně: ARBot2 dělal totéž (`sx` a `sy` nezávisle), takže model to takhle vidí odjakživa.

## Jak měřit

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
- **Pořadí kanálů** je převzaté z ARBot2, ne ověřené měřením na barevné scéně.
- **Kvalita modelu na dnešních datech** je neznámá — je z roku 2021 a trénovací data k němu
  nejsou. Že běží, neznamená, že je lepší než histogram: na venkovním záznamu dává **zjevně
  čistší obraz cesty**, ale ground truth k tomu není a na zarostlé ploše je nerozhodný.
- **Dopad menšího rozlišení** na hranice cesty a occupancy grid není naměřený.

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
- [`models/tflite2onnx.py`](../models/tflite2onnx.py) — převod modelu
- `ARBotRuntime.BuildBackProject()` — výběr implementace
