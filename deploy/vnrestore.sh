#!/bin/bash
# ---------------------------------------------------------------------------------
# Obnova konfigurace VN100 podle referencniho exportu + ULOZENI DO FLASH.
#
# ⚠️ TENHLE SKRIPT ZAPISUJE DO SENZORU. Diagnostika je jinde (vnprobe.sh, jen VNRRG).
# Delici cara je zamerna: konfigurace zeleza se meni vedome, ne vedlejsim ucinkem
# nejakeho mereni.
#
# NACPAK: 6. 9. 2026 se zjistilo, ze senzor ma heading mode RELATIVE misto ABSOLUTE
# (registr 35). V Relative neni yaw kurz k severu, ale k tomu, kde senzor nabehl -
# takze kurz robota byl o 59 stupnu vedle. Viz doc/imu-and-frames.md.
#
# POUZITI (sluzba musi byt zastavena, jinak port drzi ona):
#   sudo systemctl stop arbot
#   ./vnrestore.sh /dev/ttyUSB0            # jen heading mode -> Absolute
#   ./vnrestore.sh /dev/ttyUSB0 --mag      # navic obnovi kalibraci magnetometru z exportu
#   ./vnrestore.sh /dev/ttyUSB0 --clearmag # navic VYMAZE kalibraci magnetometru (jednotkova)
#   ./vnrestore.sh /dev/ttyUSB0 --magcal 1.12,0.007,...  # navic zapise ZADANOU kalibraci
#                                          # (12 cisel za VNWRG,23, presne jak je tiskne
#                                          #  `ARBot.Analyze magcal --bref=<|B| z registru 21>`)
#   sudo systemctl start arbot
#
# --magcal je cesta, jak zapsat kalibraci SPOCITANOU ZE ZAZNAMU bez dalsiho vyjezdu:
# mise magcal ji v poli odmitla (10. 9. 2026, rozptyl sklonu - brana pozdeji zrusena),
# ale surove pole v zaznamu je, takze report da tahz 12 cisel, ktera by zapsala mise.
# ⚠️ Normuj na |B| z registru 21 SENZORU (po magmodel= je to WMM, 10. 9. 2026 = 0,4897 G),
# ne na default reportu 0,4818 - jinak VPE porovnava |B| proti jine referenci.
#
# ⚠️ --mag ZAPISUJE ROK STAROU KALIBRACI z ARBot2 (export 8. 7. 2026). Plati pro
# tehdejsi zelezo kolem senzoru; na dnesnim robotu muze byt jina. Spravne se ma
# kalibrace ZMERIT ZNOVU otacenim robotu. Proto to NENI vychozi chovani.
#
# ⚠️ A NAMERENE 6. 9. 2026: na dnesnim robotu je ta stara kalibrace HORSI NEZ ZADNA.
# Jeji tvrde zelezo (-0,274 G vodorovne 0,280 G) je VETSI nez vodorovna slozka
# zemskeho pole (~0,20 G), takze do mereni vnese body-fixed vektor silnejsi nez
# signal - kompas pak prestane reagovat na otoceni. Zmereno nad zaznamy:
#   bez kalibrace  (20260906-082403): IMU yaw - GPS kurz  sd  9,7 deg
#   s kalibraci    (20260906-153657): IMU yaw - GPS kurz  sd 118,3 deg
# Proto je tu --clearmag: vrati registr 23 na jednotkovou matici a nulovy bias.
#
# ⚠️ VNWNV uklada CELOU sadu registru, jak je prave ted v RAM - tedy i to, co do ni
# zapsal driver pri startu (ADOR=0 v reg 06 a binarni vystup 1 v reg 75). Je to
# neskodne (driver si je stejne pise pri kazdem startu), ale flash se tim v techto
# polozkach rozejde s exportem.
#
# ⚠️ Zapis do flash jde overit jen ZPETNYM CTENIM - registr se cte z RAM, ne z flash.
# Skutecny test je az VYPNUTI A ZAPNUTI robota.
# ---------------------------------------------------------------------------------
set -u
DEV=${1:-/dev/ttyUSB0}
MAG=${2:-}
MAGCAL=${3:-}
OUT=/tmp/vnrestore.raw

if [ "$MAG" = "--magcal" ]; then
    # 12 cisel oddelenych carkou, desetinna TECKA (VN cte jen tu). Kontrola tvaru, ne hodnot:
    # o smyslu cisel rozhodl report (verdikt POUZITELNE), ne tenhle skript.
    if ! [[ "$MAGCAL" =~ ^-?[0-9]+(\.[0-9]+)?(,-?[0-9]+(\.[0-9]+)?){11}$ ]]; then
        echo "--magcal chce 12 cisel oddelenych carkou (desetinna tecka), dostal: '$MAGCAL'"
        exit 3
    fi
fi

if [ ! -e "$DEV" ]; then echo "NENI $DEV"; exit 1; fi
if fuser "$DEV" >/dev/null 2>&1; then
    echo "POZOR: port drzi jiny proces (zastav sluzbu: sudo systemctl stop arbot):"
    fuser -v "$DEV"
    exit 2
fi

stty -F "$DEV" 115200 raw -echo -echoe -echok -crtscts min 1 time 0

: > "$OUT"
timeout 25 cat "$DEV" > "$OUT" &
CATPID=$!
sleep 0.5

cks() { local s="$1" c=0 i n; for ((i=0;i<${#s};i++)); do printf -v n '%d' "'${s:$i:1}"; c=$(( c ^ n )); done; printf '%02X' "$c"; }
send() { local body="$1"; printf '$%s*%s\r\n' "$body" "$(cks "$body")" > "$DEV"; sleep "${2:-0.4}"; }

echo "=== PRED zapisem ==="
send "VNRRG,35"; send "VNRRG,23"; send "VNRRG,08"
sleep 0.3
grep -ao "\$VN[A-Z]\{3\},\(35\|23\|08\)[^*]*\*[0-9A-Fa-f]\{2\}" "$OUT" | sort -u

MARK=$(stat -c %s "$OUT")

# --- ZAPIS -----------------------------------------------------------------------
# Reg 35 VPE Basic Control: Enable=1, HeadingMode=0 (Absolute), FilteringMode=1, TuningMode=1.
send "VNWRG,35,1,0,1,1"

if [ "$MAG" = "--mag" ]; then
    # Reg 23 Magnetometer Compensation z exportu vn100-2026-7-8-nastavei z arbot2.sencfg:
    #   C = [(1.222; 0.005; 0.01)(0.002; 1.175; -0.012)(-0.004; -0.017; 1.081)]
    #   B = (-0.274; -0.058; 0.076)
    send "VNWRG,23,1.222,0.005,0.01,0.002,1.175,-0.012,-0.004,-0.017,1.081,-0.274,-0.058,0.076"
elif [ "$MAG" = "--clearmag" ]; then
    # Zadna kompenzace: jednotkova matice, nulovy bias. Tovarni stav - a podle mereni
    # z 6. 9. 2026 na tomhle robotu lepsi nez stara kalibrace z ARBot2 (viz hlavicka).
    send "VNWRG,23,1,0,0,0,1,0,0,0,1,0,0,0"
elif [ "$MAG" = "--magcal" ]; then
    # Kalibrace spocitana ze zaznamu (ARBot.Analyze magcal), stejny tvar jako pise mise:
    # radky C, pak bias; VN aplikuje C*(m - b). Viz doc/plan-vn100-kalibrace.md.
    send "VNWRG,23,$MAGCAL"
fi

# Ulozeni do flash. Trva ~1 s a senzor pritom neodpovida.
send "VNWNV" 3.0

echo "=== PO zapisu (zpetne cteni) ==="
send "VNRRG,35"; send "VNRRG,23"; send "VNRRG,08"; send "VNRRG,27"
sleep 0.5

kill $CATPID 2>/dev/null
wait $CATPID 2>/dev/null

tail -c +"$MARK" "$OUT" | grep -ao "\$VN[A-Z]\{3\}[^*]*\*[0-9A-Fa-f]\{2\}" | sort -u
echo
echo "Heading mode ma byt 35,1,0,1,1. Skutecny test trvalosti je az vypnuti a zapnuti robota."
