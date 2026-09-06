# Nasazeni ARBot.Headless na Orange Pi (z Windows).
#
#   .\deploy\nasad.ps1                      # publish + kopie + restart sluzby
#   .\deploy\nasad.ps1 -RobotHost 192.168.7.1   # jina adresa (AP misto kabelu)
#   .\deploy\nasad.ps1 -NoRestart           # jen nahrat, nerestartovat
#   .\deploy\nasad.ps1 -NoData              # nenasazovat config/ a OSM/
#
# Nasazuje se do ~/arbot-headless; aplikace bezi ze stinove kopie ~/arbot-headless-run
# (obnovuje ji stin.sh pri kazdem startu sluzby), takze cil jde prepsat i za behu.
# Zaznamy a logy zustavaji v ~/arbot a NESAHA se na ne.
#
# PROFILY A MAPY (config/, OSM/) se nasazuji taky, ale do DATOVEHO ADRESARE ~/arbot -
# tedy tam, odkud je aplikace cte (dataroot=). Vedle binarek zamerne nejsou: dve kopie
# tychz map by matly, ktera se vlastne pouziva. Do 6. 9. 2026 se nenasazovaly vubec
# a musely se kopirovat rucne - a poznalo se to az tim, ze se zmena v profilu na robotu
# neprojevila, ackoli skript hlasil uspech.
#
# ⚠️ REPO VYHRAVA: soubor rucne upraveny na robotu se prepise. Skript proto pred kopii
# vypise, ktere soubory se LISI - aby rucni uprava nezmizela potichu.
#
# Verze se razitkuje (-p:ArbotStamp=true), takze kazde nasazeni ma vyssi cislo a je
# videt v hlavicce stranky i v crash logu. Viz doc/headless.md.

param(
    [string]$RobotHost = "192.168.66.1",
    [string]$User      = "ales",
    [string]$Dir       = "/home/ales/arbot-headless",
    [string]$DataDir   = "/home/ales/arbot",
    [switch]$NoRestart,
    [switch]$NoData
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

# Z PUBLISHE se config/ a OSM/ vyhazuji: aplikace je cte z datoveho adresare, takze
# dve kopie tychz map by jen matly, ktera se vlastne pouziva. Nasazuji se nize, ale
# rovnou do ~/arbot - viz sekce "profily a mapy".
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

# --------------------------------------------------------------------------------
# Profily a mapy do datoveho adresare
# --------------------------------------------------------------------------------
if (-not $NoData) {
    Write-Host "== profily a mapy do $cil`:$DataDir" -ForegroundColor Cyan

    # Nejdriv rict, co se prepise. Rucni uprava na robotu je legitimni vec (nekdo tam
    # neco doladil za tmy u robota) a nema zmizet bez varovani - jen se o ni ma vedet.
    $mistni = @{}
    foreach ($f in (Get-ChildItem -Path (Join-Path $repo "config"), (Join-Path $repo "OSM") -File -Recurse)) {
        $rel = $f.FullName.Substring($repo.Length + 1) -replace '\\', '/'
        $mistni[$rel] = (Get-FileHash $f.FullName -Algorithm MD5).Hash.ToLower()
    }
    $vzdalene = @{}
    $vystup = & ssh $cil "cd $DataDir 2>/dev/null && find config OSM -type f -exec md5sum {} + 2>/dev/null"
    foreach ($r in $vystup) {
        if ($r -match '^([0-9a-f]{32})\s+(.+)$') { $vzdalene[$Matches[2]] = $Matches[1] }
    }
    $lisi = @($mistni.Keys | Where-Object { $vzdalene.ContainsKey($_) -and $vzdalene[$_] -ne $mistni[$_] })
    $nove  = @($mistni.Keys | Where-Object { -not $vzdalene.ContainsKey($_) })
    if ($lisi.Count -gt 0) {
        Write-Warning "prepisuji soubory, ktere se na robotu LISI od repa (rucni upravy zmizi):"
        $lisi | Sort-Object | ForEach-Object { Write-Host "     $_" -ForegroundColor Yellow }
    }
    if ($nove.Count -gt 0) { Write-Host "   nove: $($nove.Count) souboru" -ForegroundColor DarkGray }
    if ($lisi.Count -eq 0 -and $nove.Count -eq 0) { Write-Host "   beze zmeny" -ForegroundColor DarkGray }

    # Baleni a prenos stejnou cestou jako binarky (scp, ne tar rourou - viz poznamka vyse).
    $dtgz = Join-Path $env:TEMP "arbot-data.tgz"
    & tar -czf $dtgz -C $repo config OSM
    if ($LASTEXITCODE -ne 0) { throw "zabaleni config/OSM selhalo" }
    & scp -q $dtgz "${cil}:/tmp/arbot-data.tgz"
    if ($LASTEXITCODE -ne 0) { throw "kopie config/OSM na Pi selhala" }
    # Rozbaluje se JEN do config/ a OSM/; records/ a logs/ v datovem adresari se netykaji.
    & ssh $cil "mkdir -p $DataDir && tar -xzf /tmp/arbot-data.tgz -C $DataDir && rm -f /tmp/arbot-data.tgz"
    if ($LASTEXITCODE -ne 0) { throw "rozbaleni config/OSM na Pi selhalo" }
    Remove-Item $dtgz -Force -ErrorAction SilentlyContinue
}
else {
    Write-Host "== -NoData: config/ a OSM/ se nenasazuji" -ForegroundColor DarkGray
}

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
