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

P/Invoke do `NativeLib.dll` (x64) / `libNativeLib.so` (ARM64) — kopíruje se do output
adresáře, výběr řeší `NativeLibraryResolver`. ARM64 asm má opravenou calling convention;
testování `libNativeLib.so` jde přes Docker/QEMU.

**Pozor:** `NativeComputeUnit.Segment`/`Segment2` padá na x64 (v produkci se nepoužívá,
třída není `IDisposable`).

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
- **Intel RealSense** — SDK ve složce `RealSense 2.0/` (kamery D435/T265); wrapper verze
  podle platformy (2.47 x64 / 2.53 ARM).
