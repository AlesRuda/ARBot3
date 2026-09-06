# Nasazeni ARBot.Headless na Orange Pi (z Windows).
#
#   .\deploy\nasad.ps1                      # publish + kopie + restart sluzby
#   .\deploy\nasad.ps1 -RobotHost 192.168.7.1   # jina adresa (AP misto kabelu)
#   .\deploy\nasad.ps1 -NoRestart           # jen nahrat, nerestartovat
#
# Nasazuje se do ~/arbot-headless; aplikace bezi ze stinove kopie ~/arbot-headless-run
# (obnovuje ji stin.sh pri kazdem startu sluzby), takze cil jde prepsat i za behu.
# Data (records/, logs/, config/, OSM/) zustavaji v ~/arbot - viz dataroot= v jednotce.
#
# Verze se razitkuje (-p:ArbotStamp=true), takze kazde nasazeni ma vyssi cislo a je
# videt v hlavicce stranky i v crash logu. Viz doc/headless.md.

param(
    [string]$RobotHost = "192.168.66.1",
    [string]$User      = "ales",
    [string]$Dir       = "/home/ales/arbot-headless",
    [switch]$NoRestart
)

$ErrorActionPreference = "Stop"
$repo = Split-Path $PSScriptRoot -Parent
$pub  = Join-Path $env:TEMP "arbot-headless-publish"
$cil  = "$User@$RobotHost"

Write-Host "== publish (OrangePI / linux-arm64, s razitkem verze)" -ForegroundColor Cyan
if (Test-Path $pub) { Remove-Item $pub -Recurse -Force }
dotnet publish (Join-Path $repo "Src\ARBot.Headless\ARBot.Headless.csproj") `
    -p:Platform=OrangePI -r linux-arm64 --self-contained false -p:ArbotStamp=true `
    -o $pub -v:q --nologo
if ($LASTEXITCODE -ne 0) { throw "publish selhal" }

# config/ a OSM/ se NEKOPIRUJI: aplikace je cte z datoveho adresare (~/arbot), kde uz
# jsou. Dve kopie tychz map by jen matly, ktera se vlastne pouziva.
Remove-Item (Join-Path $pub "config") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $pub "OSM")    -Recurse -Force -ErrorAction SilentlyContinue

# Skript stinove kopie a jednotka patri vedle binarek (ExecStartPre na ne ukazuje).
Copy-Item (Join-Path $PSScriptRoot "stin.sh")       $pub -Force
Copy-Item (Join-Path $PSScriptRoot "arbot.service") $pub -Force
Copy-Item (Join-Path $PSScriptRoot "README.md")     $pub -Force

$verze = (Get-Content (Join-Path $pub "ARBot.Headless.deps.json") -Raw) -match '"ARBot.Headless/([^"]+)"' `
         | Out-Null; $verze = $Matches[1]
Write-Host "== nasazuji verzi $verze na $cil`:$Dir" -ForegroundColor Cyan

# Balik + scp, protoze na Pi neni rsync (viz doc/build-and-platforms.md).
# POZOR: tar rourou do ssh tudy NEJDE - PowerShell pipeline vede text, ne bajty,
# a archiv se cestou rozbije ("This does not look like a tar archive", naslapnuto
# 5. 9. 2026). scp posila soubor tak, jak je.
$tgz = Join-Path $env:TEMP "arbot-headless.tgz"
& tar -czf $tgz -C $pub .
if ($LASTEXITCODE -ne 0) { throw "zabaleni selhalo" }
& scp -q $tgz "${cil}:/tmp/arbot-headless.tgz"
if ($LASTEXITCODE -ne 0) { throw "kopie na Pi selhala" }
# stin.sh se do repa uklada s LF, takze se konce radku neresi - kdyby se tam nekdy dostal
# CRLF, shell na Pi hlasi 'bad interpreter'.
& ssh $cil "mkdir -p $Dir && tar -xzf /tmp/arbot-headless.tgz -C $Dir && rm -f /tmp/arbot-headless.tgz && chmod +x $Dir/stin.sh"
if ($LASTEXITCODE -ne 0) { throw "rozbaleni na Pi selhalo" }
Remove-Item $tgz -Force -ErrorAction SilentlyContinue

# libNativeLib.so se cross-kompiluje ve WSL a publish ji NENESE. Bez ni Run spadne hned
# pri startu (DllNotFoundException v NativeComputeUnit) - overeno na Pi 5. 9. 2026. Bere se
# z datoveho adresare, kam ji autor dodava rucne.
$so = & ssh $cil "test -f $Dir/libNativeLib.so && echo mam || (test -f /home/ales/arbot/libNativeLib.so && cp /home/ales/arbot/libNativeLib.so $Dir/ && echo zkopirovano || echo chybi)"
if ($so -eq "chybi") {
    Write-Warning "libNativeLib.so neni ani v $Dir, ani v /home/ales/arbot - Run spadne na DllNotFoundException. Viz doc/build-and-platforms.md."
} else {
    Write-Host "   libNativeLib.so: $so" -ForegroundColor DarkGray
}

if ($NoRestart) {
    Write-Host "== hotovo (bez restartu). Nova verze se pouzije az pri pristim startu sluzby." -ForegroundColor Green
    exit 0
}

Write-Host "== restart sluzby (stin.sh prekopiruje binarky)" -ForegroundColor Cyan
& ssh $cil "sudo systemctl restart arbot && sleep 3 && systemctl is-active arbot"
if ($LASTEXITCODE -ne 0) { throw "sluzba nenabehla - podivej se na 'journalctl -u arbot -n 50'" }

Write-Host "== hotovo. Nahled: http://$RobotHost`:8080/" -ForegroundColor Green
