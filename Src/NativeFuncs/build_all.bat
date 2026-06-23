@echo off
echo ==================================================
echo   KOMPILACE PRO WINDOWS (x64)
echo ==================================================

if exist out\win rmdir /s /q out\win

cmake -S . -B out/win -G "Visual Studio 17 2022" -A x64
cmake --build out/win --config Release

echo.
echo ==================================================
echo   KOMPILACE PRO LINUX ARM64 (p?es WSL - Ubuntu)
echo ==================================================

:: 1. Vy?išt?ní a p?íprava build adresá?e uvnit? ?istého Linux filesystemu
wsl -d Ubuntu rm -rf /tmp/native_build
wsl -d Ubuntu mkdir -p /tmp/native_build

:: 2. Konfigurace projektu uvnit? Linuxu
wsl -d Ubuntu cmake -S . -B /tmp/native_build -G "Ninja" -DCMAKE_BUILD_TYPE=Release -DCMAKE_SYSTEM_NAME=Linux -DCMAKE_SYSTEM_PROCESSOR=aarch64 -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++ -DCMAKE_ASM_COMPILER=aarch64-linux-gnu-gcc

:: 3. Pouhý BUILD (bez instalace na disk C:), což prob?hne 100% korektn? v /tmp/
wsl -d Ubuntu cmake --build /tmp/native_build

:: 4. BEZPE?NÉ KOPÍROVÁNÍ POMOCÍ WINDOWS
:: Windows si sám vytvo?í složku bin a vytáhne si hotové .so z WSL
if not exist bin mkdir bin
copy /Y \\wsl.localhost\Ubuntu\tmp\native_build\libNativeLib.so bin\libNativeLib.so

echo.
echo ==================================================
echo   HOTOVO! Zkontrolujte složku /bin.
echo ==================================================
pause