@echo off
rem ---------------------------------------------------------------------------------
rem Vypne robota (celou desku Orange Pi) pres ssh.
rem
rem Nejdriv ZASTAVI SLUZBU arbot (runtime dojede fronty, uzavre zaznam, zastavi motory)
rem a teprve pak da systemu pokyn k vypnuti - tentyz postup jako tlacitko Power off
rem na strance nahledu (doc/headless.md). Vytazeni napajeni za behu znamena useknuty
rem zaznam a nedopsany souborovy system.
rem
rem sudo -n: kdyby sudo chtelo heslo, skonci chybou misto cekani na vstup.
rem Pouziti:  deploy\poweroff.bat              (192.168.66.1, kabel)
rem           deploy\poweroff.bat 192.168.7.1  (jina adresa, napr. AP)
rem ---------------------------------------------------------------------------------
setlocal
set "ROBOT=%~1"
if "%ROBOT%"=="" set "ROBOT=192.168.66.1"

rem 1) Zastavit sluzbu. Zaroven overi spojeni a sudo - kdyz tohle selze, nevypina se nic.
echo Zastavuji sluzbu arbot na ales@%ROBOT% ...
ssh -o ConnectTimeout=10 ales@%ROBOT% "sudo -n systemctl stop arbot"
if errorlevel 1 goto chyba
echo Sluzba zastavena (zaznam uzavren).

rem 2) Vypnout desku. Vypinani ukonci spojeni, takze ssh casto vrati 255 - to je uspech.
rem    Jina nenulova hodnota znamena, ze prikaz odmitl (napr. sudo).
echo Vypinam robota ...
ssh -o ConnectTimeout=10 ales@%ROBOT% "sudo -n /sbin/poweroff"
if errorlevel 255 goto ok
if errorlevel 1 goto chyba

:ok
echo.
echo Pokyn k vypnuti odeslan. Pockej ~15 s, nez deska dobehne, pak odpoj napajeni.
endlocal
exit /b 0

:chyba
echo.
echo SELHALO (kod %errorlevel%) - robot NENI vypnuty. Zkontroluj pripojeni a sudo.
endlocal
exit /b 1
