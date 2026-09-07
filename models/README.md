# models/ — modely neuronových sítí

Modely pro sémantickou segmentaci sjízdnosti (`backproject=nn`). Podrobnosti, měření a rozhodnutí:
[doc/semantic-segmentation.md](../doc/semantic-segmentation.md).

| soubor | co to je |
|---|---|
| `Model61.1.tflite` | původní float model z ARBot2 (2021). **Má dynamické tvary**, takže se nedá převést — deklarovaný výstup `[1,1,1,2]` je jen placeholder. |
| `Model61.1_int8.tflite` | plně kvantovaná varianta téhož, statické tvary — **zdroj pro převod** |
| `Model61.1_int8.onnx` | převedený pro ONNX Runtime, int8 vnitřek (840 kB) — výchozí `nnmodel=` |
| `Model61.1_int8_deq.onnx` | **taky z `_int8.tflite`**, jen s `--dequantize`: kvantované váhy rozbalené do float (2,6 MB) — pro A/B měření. **Není** to původní float model, přesnost zůstává jako u int8. |
| `Model61.1.rknn` | pro **NPU** RK3588 (`backproject=npu`) — z float ONNX přes `onnx2rknn.py` |
| `tflite2onnx.py` | převodní nástroj (TFLite → ONNX + ověření proti TFLite) |
| `onnx2rknn.py` | převodní nástroj (float ONNX → RKNN pro NPU) |
| `keras2onnx.py` | převodní nástroj (Keras `.h5` → float ONNX s pevnými tvary) |

### Model96.2 — přesnější, ale 34× dražší

| soubor | co to je |
|---|---|
| `Model96.2_0.9643115401268005.h5` | Keras checkpoint — **jediný skutečně float zdroj** |
| `Model96.2.tflite` | dynamic-range kvantizovaný (váhy int8), float I/O, statické tvary |
| `Model96.2_int16.tflite` | int16 kvantizace — dodaná autorem; ⚠️ je to indicie, že model int8 nesnese, a měření to potvrdilo |
| `Model96.2.onnx` | z `.tflite` pro CPU cestu (na Pi 637 ms — **nepoužitelné**) |
| `Model96.2_float.onnx` | z `.h5` přes `keras2onnx.py` — zdroj pro RKNN |
| `Model96.2.rknn` | **fp16 na NPU: 44 ms, 96,66 %** — tahle varianta se používá |
| `Model96.2_int8.rknn` | ⚠️ 22 ms, ale přesnost spadne na **37,8 %** — nepoužitelné, drženo jako důkaz |

Model je U-Net s MobileNetV2 bloky: vstup `[1,128,128,3]`, výstup `[1,128,128,2]`,
**kanál 1 = sjízdno**, 112,5 MMAC na snímek.

## Převod

Potřebuje TensorFlow, tedy Linux/WSL a Python 3.11 (na Windows nejde):

```bash
python -m venv .venv
.venv/bin/pip install tensorflow-cpu==2.15.1 tf2onnx==1.16.1 onnx==1.16.1 onnxruntime
.venv/bin/python tflite2onnx.py Model61.1_int8.tflite Model61.1_int8.onnx --check ../doc/media/road-edges-image-20260823.png
```

`--check` porovná výsledek proti původnímu TFLite (shoda rozhodnutí, rozdíl pravděpodobností) —
bez toho se nedá poznat, že převod něco pokazil.

Skript navíc **přepíše I/O modelu na float32**: vnitřek zůstane int8, ale na hranici se mluví
v reálných jednotkách (vstup 0..1, výstup pravděpodobnost). Bez toho by C# strana musela znát
kvantizační konstanty modelu a špatná hodnota by se projevila jako tiše horší segmentace, ne jako
chyba. Proto `OnnxBackProject` kvantované I/O odmítá.

## Co s tím v gitu

**Modely jsou verzované** (rozhodnutí autora 7. 9. 2026) — dohromady ~5 MB. Bez nich by se
`backproject=nn` na čerstvé pracovní kopii nedalo ani spustit, ani přeměřit, a `.onnx` varianty
navíc nejsou surová data: vznikly převodem, který se dá zopakovat jen s TensorFlow ve WSL.
`.gitattributes` je značí jako `binary`, aby se jim nenormalizovaly konce řádků.

**Chybí `Model61.1_int8_edgetpu.tflite`**, který tu byl 6. 9. 2026 na začátku práce (kompilát pro
Coral). Pro ONNX cestu není k ničemu — kompilát pro Coral se nekonvertuje — a originál je
v ARBot2 (`Sources/dotnet/ARBot2/bin/x64/Debug/`), odkud sem byly modely zkopírované.
