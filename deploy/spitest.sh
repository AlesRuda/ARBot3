#!/bin/bash
# ---------------------------------------------------------------------------------
# Premereni SPI pro LED pasek WS2812 na 40pinove liste Orange Pi (multimetr / osciloskop).
#
# NACPAK: 28. 9. 2026 runtime na /dev/spidev0.0 posilal 20 snimku/s (statistika spi0
# bez chyb), ale pasek zustal tmavy. Za provozu je linka aktivni jen ~2 % casu,
# takze multimetr na ni ukaze nulu. Tenhle skript posila vzor NEPRETRZITE:
#
#   vzor  high  (0xFF)  -> MOSI trvale v 1 (na osciloskopu rovna cara!), CLK kmita
#   vzor  low   (0x00)  -> MOSI ~0 V,   CLK kmita
#   vzor  half  (0xF0)  -> MOSI obdelnik (na osciloskopu nejlepe videt)
#
# ✅ 11. 10. 2026 rozhodnuto osciloskopem: signal byl na pinu 19 (SPI0_MOSI_M2 = GPIO1_B2),
# ale pasek je zapojeny na PIN 13 (GPIO1_A1 = SPI4_MOSI_M2). Overlay se proto prepnul na
# spi4-m2-cs0-spidev a zarizeni je /dev/spidev4.0 (DEV niz; jine se zada druhym argumentem).
# Uroven na DIN u pasku: log. 1 = 4,3 V, log. 0 = 0,3 V. Viz OrangePi5Ultra/POSTUP.md.
#
# ⚠️ Pri spravnem zapojeni vzor high i half rozsviti VSECHNY LED bile na plny jas
# (kazda nenulova davka sub-bitu se cte jako jednicka) - odber 36 x 60 mA ~ 2,2 A.
#
# POUZITI (sluzba musi stat, jinak do SPI pise i ona):
#   sudo systemctl stop arbot
#   ./spitest.sh high      # Ctrl+C = konec
#   ./spitest.sh low
#   ./spitest.sh half /dev/spidev0.0   # jine SPI zarizeni
#   sudo systemctl start arbot
#
# Zapisuje JEN do zadaneho spidev (write() = poloduplexni prenos rychlosti, kterou
# naposledy nastavil nekdo pres ioctl, jinak z DT).
# ---------------------------------------------------------------------------------
set -euo pipefail   # konec pri prvni chybe (i uprostred roury) - DeploySkriptyTests
DEV=${2:-/dev/spidev4.0}
case "${1:-}" in
    high) B='\377' ;;
    low)  B='\000' ;;
    half) B='\360' ;;
    *) echo "pouziti: $0 high|low|half [/dev/spidevX.Y]"; exit 1 ;;
esac
if fuser "$DEV" >/dev/null 2>&1; then
    echo "POZOR: $DEV drzi jiny proces (sudo systemctl stop arbot):"; fuser -v "$DEV"; exit 2
fi
echo "posilam vzor '$1' na $DEV nepretrzite, Ctrl+C = konec"
BUF=$(mktemp)
head -c 4096 /dev/zero | tr '\000' "$B" > "$BUF"
trap 'rm -f "$BUF"' EXIT                    # uklid i pri konci chybou (set -e)
trap 'echo; echo konec; exit 0' INT TERM
while :; do cat "$BUF" > "$DEV"; done
