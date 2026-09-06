#!/bin/bash
# ---------------------------------------------------------------------------------
# Read-only diagnostika VN100 na zarizeni (registry pres VNRRG).
#
# NACPAK: kurz z VN100 byl 6. 9. 2026 o 59 stupnu vedle (viz doc/imu-and-frames.md).
# Ze zaznamu se dalo zjistit, ze se atitudove reseni senzoru odtahlo od vlastniho
# magnetometru, ale NE PROC - konfiguracni registry v zaznamu nejsou. Tohle je ta
# chybejici cast: precte je na zivem senzoru a da je vedle referencniho exportu
# `vn100-2026-7-8-nastavei z arbot2.sencfg` v koreni repa.
#
# ⚠️ POSILA JEN VNRRG (cteni registru). ZADNY zapis (VNWRG) ani zapis do flash (VNWNV)
# tady byt NESMI - konfigurace senzoru se meni vedome a rucne, ne diagnostikou.
#
# POUZITI (sluzba musi byt zastavena, jinak port drzi ona):
#   sudo systemctl stop arbot
#   ./vnprobe.sh /dev/ttyUSB0
#   sudo systemctl start arbot
#
# ⚠️ PAST, na kterou se naslaplo: `stty ... min 0 time 0` udela cteni NEBLOKUJICI,
# takze `cat` dostane nulu bajtu = EOF a skonci hned. Prvni beh zachytil presne
# JEDEN bajt a vypadalo to, jako by senzor mlcel. Spravne je `min 1 time 0`.
#
# ⚠️ DRUHA PAST: po zastaveni sluzby zustava senzor v BINARNIM rezimu (driver mu
# zapsal ADOR=0 a binarni vystup 1), takze po lince tece ~9 kB/s binarnich dat
# a ASCII odpovedi jsou v nich utopene. Vytahovat je proto `grep -ao` na CELY
# vzorek, ne radkove - radkovy grep prvni pokus o cast odpovedi minul.
# ---------------------------------------------------------------------------------
set -u
DEV=${1:-/dev/ttyUSB0}
OUT=${2:-/tmp/vn.raw}

if [ ! -e "$DEV" ]; then echo "NENI $DEV"; exit 1; fi
if fuser "$DEV" >/dev/null 2>&1; then
    echo "POZOR: port drzi jiny proces (zastav sluzbu: sudo systemctl stop arbot):"
    fuser -v "$DEV"
    exit 2
fi

stty -F "$DEV" 115200 raw -echo -echoe -echok -crtscts min 1 time 0

: > "$OUT"
timeout 12 cat "$DEV" > "$OUT" &
CATPID=$!
sleep 0.5

cks() { local s="$1" c=0 i n; for ((i=0;i<${#s};i++)); do printf -v n '%d' "'${s:$i:1}"; c=$(( c ^ n )); done; printf '%02X' "$c"; }
send() { local body="$1"; printf '$%s*%s\r\n' "$body" "$(cks "$body")" > "$DEV"; sleep 0.35; }

# 01 model, 06/07 async vystup, 08 YPR, 21 referencni vektory pole a gravitace,
# 23 KOMPENZACE MAGNETOMETRU (tvrde/mekke zelezo), 25 kompenzace akcelerometru,
# 26 reference frame rotation, 27 YMR (zivy snimek yaw+pole+zrychleni+rychlosti),
# 35 VPE BASIC CONTROL (heading mode!), 36 VPE mag tuning, 44 HSI mode,
# 47 vypoctena kalibrace HSI, 54 SYROVA (nekompenzovana) mereni,
# 83 REFERENCE VECTOR CONFIGURATION (pouziva se model pole? kdyz ne, referencni vektor
#    v reg 21 je pevny a jeho VYCHODNI slozka rika, jestli je v nem DEKLINACE - pri E=0
#    je hlaseny kurz MAGNETICKY, ne k pravemu severu).
for r in 01 06 07 08 21 23 25 26 27 35 36 44 47 54 83; do send "VNRRG,$r"; done

sleep 1.0
kill $CATPID 2>/dev/null
wait $CATPID 2>/dev/null

echo "=== bajtu zachyceno: $(stat -c %s "$OUT") ==="
echo "=== odpovedi (porovnej s vn100-...sencfg v koreni repa) ==="
grep -ao "\$VN[A-Z]\{3\},[^*]*\*[0-9A-Fa-f]\{2\}" "$OUT" | sort -u
