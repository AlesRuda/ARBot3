@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

:: ============================================================================
::  Build nativni knihovny NativeLib pro obe platformy.
::   - Windows x64  -> bin\NativeLib.dll     (MSVC + MASM, preset windows-x64)
::   - Linux ARM64  -> bin\libNativeLib.so   (cross-compile ve WSL, pro Orange Pi)
::
::  Skript si sam najde Visual Studio a zavola vcvars64.bat - cmake/cl/ml64
::  NEJSOU v systemove PATH, prichazeji az s nim. Viz doc/build-and-platforms.md.
::
::  ARM64 cast se PRESKOCI, kdyz neni WSL distro (na Windows-only vyvoji
::  ji nepotrebujes). Jinou distribuci lze zadat: build_all.bat MojeDistro
:: ============================================================================

set "WSL_DISTRO=%~1"
if "%WSL_DISTRO%"=="" set "WSL_DISTRO=Ubuntu"

set "WIN_OK=0"
set "ARM_OK=0"

:: --- Najdi Visual Studio a nastav prostredi prekladace ---------------------
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" (
    echo [CHYBA] Nenalezen vswhere.exe - neni nainstalovane Visual Studio?
    goto :summary
)

set "VSPATH="
for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSPATH=%%i"

if not defined VSPATH (
    echo [CHYBA] Visual Studio nema nainstalovane C++ nastroje.
    echo         Doinstaluj komponenty:
    echo           Microsoft.VisualStudio.Component.VC.Tools.x86.x64
    echo           Microsoft.VisualStudio.Component.Windows11SDK.26100
    echo           Microsoft.VisualStudio.Component.VC.CMake.Project
    goto :summary
)

set "VCVARS=%VSPATH%\VC\Auxiliary\Build\vcvars64.bat"
if not exist "%VCVARS%" (
    echo [CHYBA] Nenalezen vcvars64.bat v "%VSPATH%".
    goto :summary
)

:: vcvars64.bat sam vola vswhere - bez tohoto radku hlasi "not recognized" (neskodne, ale matouci).
set "PATH=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer;%PATH%"
call "%VCVARS%" >nul

where cmake >nul 2>&1
if !errorlevel! neq 0 (
    echo [CHYBA] cmake nenalezen ani po vcvars64.
    echo         Chybi komponenta "C++ CMake tools for Windows".
    goto :summary
)

:: --- Windows x64 ----------------------------------------------------------
echo ==================================================
echo   KOMPILACE PRO WINDOWS (x64)
echo ==================================================

cmake --preset windows-x64
if !errorlevel! neq 0 goto :win_failed

cmake --build out/build/windows-x64
if !errorlevel! neq 0 goto :win_failed

set "WIN_OK=1"
goto :arm

:win_failed
echo [CHYBA] Build pro Windows selhal.

:: --- Linux ARM64 pres WSL --------------------------------------------------
:arm
echo.
echo ==================================================
echo   KOMPILACE PRO LINUX ARM64 (pres WSL - %WSL_DISTRO%)
echo ==================================================

:: POZOR: wsl vraci u chybejici distribuce -1, a "if errorlevel 1" znamena ">= 1",
:: takze zaporny kod by propadl. Proto vsude porovnani s nulou.
wsl -d %WSL_DISTRO% -e true >nul 2>&1
if !errorlevel! neq 0 (
    echo [PRESKOCENO] WSL distro "%WSL_DISTRO%" neni k dispozici.
    echo              Potrebne jen pro Orange Pi ^(ARM64^); pro Windows vyvoj ne.
    echo              Instalace:  wsl --install -d Ubuntu
    echo              a v ni:     sudo apt install cmake ninja-build gcc-aarch64-linux-gnu g++-aarch64-linux-gnu
    goto :summary
)

wsl -d %WSL_DISTRO% rm -rf /tmp/native_build
wsl -d %WSL_DISTRO% mkdir -p /tmp/native_build

:: Konfigurace i build bezi v ciste Linux ceste (/tmp), ne na namapovanem disku.
wsl -d %WSL_DISTRO% cmake -S . -B /tmp/native_build -G "Ninja" -DCMAKE_BUILD_TYPE=Release -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_SYSTEM_PROCESSOR=aarch64 -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++ -DCMAKE_ASM_COMPILER=aarch64-linux-gnu-gcc
if !errorlevel! neq 0 goto :arm_failed

wsl -d %WSL_DISTRO% cmake --build /tmp/native_build
if !errorlevel! neq 0 goto :arm_failed

:: Hotovy .so si vytahne Windows (kopirovani z WSL na Windows disk je spolehlivejsi nez opacne).
if not exist bin mkdir bin
copy /Y \\wsl.localhost\%WSL_DISTRO%\tmp\native_build\libNativeLib.so bin\libNativeLib.so
if !errorlevel! neq 0 goto :arm_failed

set "ARM_OK=1"
goto :summary

:arm_failed
echo [CHYBA] Build pro ARM64 selhal.

:: --- Souhrn ----------------------------------------------------------------
:summary
echo.
echo ==================================================
echo   VYSLEDEK
echo ==================================================

if "%WIN_OK%"=="1" (echo   [OK]    bin\NativeLib.dll) else (echo   [CHYBI] bin\NativeLib.dll)
if "%ARM_OK%"=="1" (echo   [OK]    bin\libNativeLib.so) else (echo   [CHYBI] bin\libNativeLib.so)

echo.
if "%WIN_OK%"=="1" goto :ok
echo Windows build NEPROSEL - aplikace pro x64 se bez NativeLib.dll nesestavi.
endlocal
pause
exit /b 1

:ok
endlocal
pause
exit /b 0
