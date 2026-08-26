# Build a platformy

## Platformy

Řešení se buildí pro konkrétní platformu — **NE `AnyCPU`**. Na `AnyCPU` padá běh kvůli
nativním závislostem `Intel.RealSense`. Používej:

- **`x64`** — Windows (vývoj, testy).
- **`OrangePI`** — Armbian / ARM64 (cílové zařízení, Orange Pi 5 Ultra).

Příklady:
```
dotnet build ARBot\ARBot\ARBot.csproj -p:Platform=x64
dotnet test  ARBot.Common.Tests\ARBot.Common.Tests.csproj -p:Platform=x64
```

`.csproj` mají `<Platforms>AnyCPU;x64;OrangePI</Platforms>` a podle platformy definují
symboly **`IsX64` / `IsX86` / `IsARM64`** (pro platform-specific `#if`).

## Platformově dedikovaný HAL

Aplikace `ARBot` referencuje HAL podle platformy (viz `ARBot/ARBot/ARBot.csproj`):

- `OrangePI` → **`ARBot.HALArmbian`** (Intel.RealSense wrapper **2.53**).
- ostatní (x64/x86/AnyCPU) → **`ARBot.HALWindows`** (Intel.RealSense wrapper **2.47**).

Platformově specifické třídy (např. `D435Camera`) žijí v obou HAL vrstvách ve stejném
namespace (`ARBot.HAL.Devices.Camera`), takže aplikační kód je nevidí rozdílně.

## Nativní knihovna (NativeFuncs)

P/Invoke do `NativeLib.dll` (x64) / `libNativeLib.so` (ARM64). Zdroj je `Src/NativeFuncs`
(C++ `native_funcs.cpp` + asm `asm_win_x64.asm` / `asm_linux_arm64.S`), **staví se přes CMake**,
NE přes `dotnet` — proto `NativeFuncs/bin/NativeLib.dll` (a `libNativeLib.so`) **nejsou v gitu**
a nesmí se smazat spolu s `bin`/`obj` (`ARBot.Common.csproj` je pro `x64` kopíruje do output bez
`Exists` guardu → jinak build padá na `MSB3030`).

Rebuild nativní knihovny:
```
# Windows x64 (vcvars64 + Ninja preset):
"C:\Program Files\Microsoft Visual Studio\18\Insiders\VC\Auxiliary\Build\vcvars64.bat"
cd Src\NativeFuncs && cmake --preset windows-x64 && cmake --build out/build/windows-x64
#   -> linkuje rovnou do Src\NativeFuncs\bin\NativeLib.dll
# ARM64 .so (cross-compile ve WSL) + Windows kopie: Src\NativeFuncs\build_all.bat
```

Výběr správného binárního souboru za běhu řeší `DllImportResolver` (`NativeLibraryResolver`
v `ARBot.Common.Tests`, resp. `NativeLibResolver` v HAL) — mapuje `NativeLib.dll` →
`libNativeLib.so` na ne-Windows, takže jeden managed kód běží na x64 i ARM.

### Testování ARM (aarch64) verze

- **QEMU v Dockeru** (rychlá iterace na Windows): Docker Desktop + `tonistiigi/binfmt --install arm64`,
  image `mcr.microsoft.com/dotnet/sdk:10.0` s `--platform linux/arm64`; repo mount do `/src`,
  `NUGET_PACKAGES=/src/.nuget-arm` pro rychlý restore. Spouštěj ARM testy **per-test ve smyčce**
  (crash jednoho jinak shodí celý test host).
- **Reálný HW** (Orange Pi, Armbian 26.8, .NET SDK 10): přenos zdrojů z Windows přes
  `tar czf - --exclude=bin --exclude=obj … | ssh …` (lokálně není rsync) nebo Samba share;
  zachovat relativní layout `../NativeFuncs/bin`. Pak `dotnet test --filter …NativeComputeUnit`.
- Po zásahu do `asm_linux_arm64.S` je nutný rebuild (`build_all.bat`) — asm musí dodržet
  **ARM64 AAPCS64** calling convention (float args v `v0-v7`, `Point4D` by-value = HFA `v1-v4`,
  8. int arg v registru; `uais` offset je už bajtový, nenásobit). Historicky zde byly 4 funkce
  psané s x86/x64 konvencí (opraveno 2026-07-03).

### NativeComputeUnit — známé problémy

`NativeComputeUnit` (`ARBot.Common/Algorithms/ComputeUnit`) staví/testuje se jen jako **x64**
(`-p:Platform=x64`, kvůli `IsX64`).

- **`Segment`/`Segment2` padá na x64** — `DepthTransformImpl` zápis do `ci->WorldPoints` způsobí
  AccessViolation na reálných hloubkových datech (x64 nativní cesta je historicky nedodělaná).
  Tyto testy jsou `[Ignore]`. Fungují: `DepthTransform2Impl` (`SegmentNew3`), `FindPathEdge`,
  `BackProjectBGR32`, `CopyRGB24ToBGR32`/`CopyBGR24ToBGR32`.
- **Třída se v produkci nepoužívá** (nikde `new NativeComputeUnit`, jen testy) a **není
  `IDisposable`** — nativní paměť uvolní až finalizer, takže v testech nutné `GC.KeepAlive(unit)`
  než přečteš `WorldPoints`/`ComputeInfo` (jinak use-after-free).
- **TODO (barevné převody):** RGB/BGR→BGR32 v `Src/ARBot.Common/Vision/MessageImageLayers.cs`
  se dělá **dočasně** managed (`Image<T>.ConvertTo`, per-pixel v C#). Cíl: směrovat přes
  akcelerovaný `NativeComputeUnit` (SIMD/HW) — doplnit `byte[]→byte[]` varianty
  `CopyRGB24ToBGR32`/`CopyBGR24ToBGR32` (zatím jen nad typovými poli / `IntPtr`). Zavádí to
  závislost na native lib do dané cesty — zvážit vs. dostupnost na cílové platformě.

## Kamery D435 / T265 na ARM (Orange Pi)

Platformově dedikovaný HAL (viz výše): `D435Camera` i `T265TrackingCamera` existují v
`ARBot.HALWindows` (RealSense **2.47**) i `ARBot.HALArmbian` (**2.53**), ve stejném namespace
`ARBot.HAL.Devices.Camera`, takže `ARBotHW` je referencuje bez `#if`.

- Managed wrapper **musí verzí odpovídat** native lib (interface-kompatibilní jen na patch-level).
  Wrapper 2.53 je zkompilovaný ze zdrojů `librealsense/wrappers/csharp` do projektu
  `Src/ThirdParty/Intel.RealSense` (cmake C# bindings jsou VS-only — nepoužívat).
- **T265 byl v librealsense odebrán ve 2.50+** — jestli ho 2.53 native lib na Pi reálně obslouží,
  je nutné ověřit na zařízení (managed typy `Pose`/`PoseFrame` existují, kód se přeloží).
- `D435CameraProjection.TransformBack` na ARM vyhazuje `NotSupported` (nativní `ColorPixel23D`
  není v žádné `libNativeLib`). Grab RGB+Depth funguje (ověřeno 2026-07-03, RGB 640×480 + Depth).
- Resolvery native lib: `RealSenseNativeResolver` (`realsense2` → `librealsense2.so`),
  `NativeLibResolver` (`NativeLib.dll` → `libNativeLib.so`).

## Solution `.slnx` a platforma OrangePI

Řešení je `Src/ARBot/ARBot.slnx` (**ne `.sln`**). Platforma **`OrangePI`** (Armbian/ARM64,
`DefineConstants += IsARM64`) je definovaná v `ARBot`, `ARBot.HAL`, `ARBot.HALArmbian`,
`ARBot.Common`, `Intel.RealSense` (HALWindows platformu OrangePI NEMÁ). V `.slnx` je řetězec
`*|OrangePI → OrangePI` a `HALWindows`/`HALZBoard`/`ARBot.Common.Tests` jsou z OrangePI buildu
vyloučené. `Platform=ARM64` je Windows-on-ARM, `RID linux-arm64` je RID-specific — proto vlastní
`OrangePI`. App se na Pi deployuje framework-dependent (managed výstup zůstává portable IL).

Pozn.: `VectorNav.dll` (VN100) je MSIL/AnyCPU managed (.NET FW 4.0), NE x64/Windows binárka →
referuje se na všech platformách vč. OrangePI (VN100 soubory se nevylučují). Reálné načtení net40
assembly na ARM64 pod .NET 10 ověřit na zařízení.

## Externí závislosti (mimo NuGet)

Některé reference nejsou z NuGetu, ale z lokálních cest / sourozeneckých složek repa —
bez nich se nesestaví:

- **`VectorNav.dll`** (VN100 IMU) — `vndotnetlib-0.4/VectorNav.dll`, referováno
  z `ARBot.HAL.csproj` (HintPath `..\..\vndotnetlib-0.4\VectorNav.dll`). SDK i
  `vectornav-sdk-1-2-0/` jsou v rootu repa.
- **`FTD2XX_NET.dll`** (FTDI USB-UART) — `ARBot.HALWindows/FTD2XX_NET.dll`, referováno
  z `ARBot.csproj` (jen `x64`).
- **`NativeLib` / `libNativeLib.so`** — vlastní nativní knihovna (`NativeFuncs`),
  viz výše.
> **Čtení QR kódů tady vědomě NENÍ.** Návrh mise Robotour původně počítal se ZBarem, tedy
> s bindingem zkopírovaným do `Src/ThirdParty/ZBar/` a nativní `libzbar` na obou platformách
> (`libzbar.dll` pro x64, `DllImportResolver` pro `libzbar.so.0` na Armbianu) — a s zápisem do této
> sekce. Skutečný dekodér je **`ZXing.Net` z NuGetu** (`ARBot.Common.csproj`), který je **čistě
> managed a žádné nativní assety nemá**, takže pro OrangePI není potřeba nic navíc; ověřeno buildem
> `-p:Platform=OrangePI`. Viz [decisions.md](decisions.md), 26. 8. 2026.

- **Intel RealSense** — SDK ve složce `RealSense 2.0/` (kamery D435/T265); wrapper verze
  podle platformy (2.47 x64 / 2.53 ARM). **V gitu jsou jen DLL** (`Intel.Realsense.dll`
  + `realsense2.dll`, x64 i x86, ~56 MB) — `.gitignore` je pro tyhle dva podadresáře
  vyjímá z build-output pravidel `x64/` / `x86/`. Doprovodné `*.pdb` (~200 MB, z toho jeden
  soubor 104 MB → nad limitem GitHubu) v gitu **nejsou**; k buildu ani běhu nejsou potřeba.
