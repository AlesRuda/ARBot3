# librknnrt.so — runtime NPU pro RK3588

Userspace knihovna Rockchipu, přes kterou jede `backproject=npu`
([`RknnBackProject`](../../ARBot.Common/Vision/Nn/RknnBackProject.cs)). Bez ní se NPU použít nedá:
**driver je v jádře, ale sám o sobě nestačí** — `CONFIG_ROCKCHIP_RKNPU=y` a `/dev/dri/renderD129`
existují i na čistém Armbianu, kdežto tahle knihovna v systému **není**.

| | |
|---|---|
| verze | **2.3.2** (`429f97ae6b@2025-04-09`) |
| platforma | pouze **linux-arm64** (RK3588) |
| původ | [airockchip/rknn-toolkit2](https://github.com/airockchip/rknn-toolkit2), `rknpu2/runtime/Linux/librknn_api/aarch64/librknnrt.so` |

⚠️ **Verze musí odpovídat toolkitu, kterým se model převedl.** Model `models/Model61.1.rknn`
vznikl s `rknn-toolkit2==2.3.2`, tedy s toutéž verzí. Při nesouladu `rknn_init` selže (nebo
hůř: projde a počítá nesmysly), proto se obojí mění naráz — viz
[doc/semantic-segmentation.md](../../../doc/semantic-segmentation.md#převod-modelu).

## Proč je binárka v gitu

Stejný důvod jako u RealSense DLL (`Src/ThirdParty/Intel.RealSense`, viz
[build-and-platforms.md](../../../doc/build-and-platforms.md)): bez ní nejde na robota nasadit
funkční celek. `nasad.ps1` ji kopíruje vedle binárek, odkud si ji vezme i stínová kopie
(`stin.sh` bere všechny soubory z kořene). Na Windows se nepoužije a nic nezdrží — `DllImport`
se řeší až při prvním volání, tedy jen když někdo zapne `backproject=npu`.

## Aktualizace

```bash
curl -L -o Src/ThirdParty/RKNN/librknnrt.so \
  https://raw.githubusercontent.com/airockchip/rknn-toolkit2/master/rknpu2/runtime/Linux/librknn_api/aarch64/librknnrt.so
grep -a -o "librknnrt version[ -~]\{0,50\}" Src/ThirdParty/RKNN/librknnrt.so   # kontrola verze
```

Po aktualizaci **znovu převeď model** toutéž verzí toolkitu (`models/onnx2rknn.py`) a přeměř —
kvantizace se mezi verzemi mění.
