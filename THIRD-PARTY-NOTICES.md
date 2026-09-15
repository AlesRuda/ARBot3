# Součásti třetích stran

Vlastní kód ARBot3 je pod licencí MIT (viz [LICENSE.txt](LICENSE.txt)). Repozitář ale nese
i **binárky, data a modely třetích stran**, které pod tu licenci **nespadají** — každá má svou.
Tenhle soubor je jejich soupis: co to je, odkud to je a pod čím to je.

> Vzniklo 15. 9. 2026 na podnět auditu. Do té doby byl `LICENSE.txt` **nevyplněná šablona**
> (`[year] [fullname]`) a soupis třetích stran neexistoval vůbec, ačkoli repozitář je veřejný
> a nese proprietární knihovny i data s povinnou atribucí.

⚠️ **Co je a co není ověřené.** Původ, verze a cesta jsou vyčtené přímo z repozitáře a z
dokumentace projektu — ty sedí. Licenční řádky jsou **z veřejných licencí těch projektů**;
u položek označených **(ověřit)** se licence z repozitáře vyčíst nedá (u binárky bez průvodního
souboru), takže je nutné je potvrdit u dodavatele dřív, než se z repozitáře udělá distribuce.

---

## Nativní knihovny v repozitáři

| Komponenta | Kde | Verze / původ | Licence |
|---|---|---|---|
| **Intel RealSense SDK 2.0** — `realsense2.dll`, `Intel.Realsense.dll` | `RealSense 2.0/x64/`, `RealSense 2.0/x86/` | Intel `librealsense2` | Apache License 2.0 |
| **Intel RealSense .NET wrapper** (zdroje) | `Src/ThirdParty/Intel.RealSense/` | Intel `librealsense2` | Apache License 2.0 |
| **Rockchip RKNPU runtime** — `librknnrt.so` | `Src/ThirdParty/RKNN/` | 2.3.2 (`429f97ae6b@2025-04-09`), [airockchip/rknn-toolkit2](https://github.com/airockchip/rknn-toolkit2) | proprietární, Rockchip — **(ověřit podmínky redistribuce)** |
| **VectorNav .NET Library** — `VectorNav.dll` + `.chm` | `vndotnetlib-0.4/` | VectorNav Technologies, v0.4 | proprietární, VectorNav — **(ověřit)** |
| **FTDI D2XX .NET wrapper** — `FTD2XX_NET.dll` | `Src/ARBot.HALWindows/` | Future Technology Devices International | proprietární, FTDI — **(ověřit)** |

⚠️ **Dokumentace VectorNav (TN002, TN004, ICD, manuál, datasheet) v repozitáři NENÍ a nesmí být** —
je označená *Proprietary & Confidential* a tenhle repozitář je veřejný. Leží jen lokálně
v `doc/Vectornav/`, proto jsou všechny závěry z ní citované **s číslem kapitoly**
(viz [doc/imu-and-frames.md](doc/imu-and-frames.md)).

⚠️ `Src/ThirdParty/Intel.RealSense` je **upravená kopie** wrapperu (viz
[doc/build-and-platforms.md](doc/build-and-platforms.md)). Apache 2.0 vyžaduje u upravených
souborů poznámku o změně — je proto potřeba ji v těch souborech mít, ne jen tady.

## Data

| Komponenta | Kde | Původ | Licence |
|---|---|---|---|
| **Mapová data OpenStreetMap** (`*.osm`) | `OSM/` | [OpenStreetMap](https://www.openstreetmap.org/) a přispěvatelé | ODbL 1.0 — **vyžaduje atribuci** |

Atribuce, kterou ODbL žádá: *„© přispěvatelé OpenStreetMap"*, s odkazem na
<https://www.openstreetmap.org/copyright>. ⚠️ Platí i pro **zobrazení** — mapový podklad
v aplikaci (Mapsui / OSM dlaždice) i na stránce náhledu.

⚠️ Syntetické mapy (`OSM/SyntetickyRovny.osm`) jsou **vlastní** — vznikly pro měření, ne
stažením z OSM. Jsou tedy pod MIT jako zbytek repozitáře.

## Modely neuronové sítě

| Komponenta | Kde | Původ | Licence |
|---|---|---|---|
| **Model61.1**, **Model96.2** a jejich varianty (`.tflite`, `.onnx`, `.rknn`, `.h5`) | `models/` | vlastní, převzaté z předchozí generace **ARBot2** (trénink 2021–2022) | MIT (vlastní dílo autora) |
| **Testovací sada** 50 snímků | `models/testset/` | vlastní záznamy robota | MIT (vlastní dílo autora) |

⚠️ Anotace trénovací sady vznikly v **LabelBoxu**; do repozitáře nejdou surová data ani klíč
(viz [CLAUDE.md](CLAUDE.md), pravidlo o přihlašovacích údajích).

## Balíčky NuGet

Nejsou v repozitáři — stahuje je `dotnet restore` a **do distribuce jdou až s publikovanou
aplikací**. Jejich licence je proto potřeba do NOTICE doplnit u vydané binárky, ne tady.
Hlavní z nich:

| Balíček | Licence |
|---|---|
| Avalonia, Avalonia.* | MIT |
| Dock.Avalonia, Dock.Model.Mvvm | MIT |
| CommunityToolkit.Mvvm | MIT |
| MathNet.Numerics | MIT |
| Microsoft.ML.OnnxRuntime | MIT |
| Mapsui, Mapsui.Nts | MIT |
| BruTile.MbTiles | Apache 2.0 |
| SkiaSharp, SkiaSharp.NativeAssets.* | MIT |
| SharpDX, SharpDX.* | MIT |
| System.IO.Ports, System.Device.Gpio | MIT |
| ZXing.Net | Apache 2.0 |
| NUnit, NUnit3TestAdapter, NUnit.Analyzers | MIT |
| coverlet.collector | MIT |

⚠️ `SkiaSharp 3.119.4-preview.1.1` je **preview** verze v produkční cestě — samostatný nález
auditu, nesouvisí s licencí.

---

## Co z toho zbývá udělat

1. Potvrdit podmínky redistribuce u tří proprietárních binárek **(ověřit)** výše — nebo je
   z veřejného repozitáře vyndat a stahovat je až při nasazení.
2. Doplnit poznámku o změně do upravených souborů `Src/ThirdParty/Intel.RealSense`
   (požadavek Apache 2.0, čl. 4b).
3. Zobrazit atribuci OSM tam, kde se mapa **ukazuje** — v aplikaci i na stránce náhledu.
