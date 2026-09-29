#!/bin/bash
# ---------------------------------------------------------------------------------
# Premereni SPI0 (LED pasek WS2812) MULTIMETREM na 40pinove liste Orange Pi.
#
# NACPAK: 28. 9. 2026 runtime na /dev/spidev0.0 posilal 20 snimku/s (statistika spi0
# bez chyb), ale pasek zustal tmavy. Za provozu je linka aktivni jen ~2 % casu
# (864 B za ~1 ms kazdych 50 ms), takze multimetr na ni ukaze nulu. Tenhle skript
# posila vzor NEPRETRZITE, a pak je na pinech videt stejnosmerna uroven:
#
#   vzor  high  (0xFF)  -> MOSI ~3,3 V, CLK ~1,65 V (strida 50 %), MISO/CS nic
#   vzor  low   (0x00)  -> MOSI ~0 V,   CLK ~1,65 V
#   vzor  half  (0xF0)  -> MOSI ~1,65 V
#
# Tim se rozhodne, na kterem fyzickem pinu je MOSI (podle pinoutu RK3588 GPIO1_B2 =
# pin 19; OrangePi5Ultra/POSTUP.md do 28. 9. uvadel GPIO1_B1) a jaka je uroven
# (WS2812 napajeny 5 V chce na DIN aspon ~3,5 V - 3,3 V z Pi je na hrane).
#
# POUZITI (sluzba musi stat, jinak do SPI pise i ona):
#   sudo systemctl stop arbot
#   ./spitest.sh high      # Ctrl+C = konec
#   ./spitest.sh low
#   sudo systemctl start arbot
#
# Zapisuje JEN do /dev/spidev0.0 (write() = poloduplexni prenos rychlosti z DT).
# Pasek pri vzoru high dostane "same jednicky" = bila na plny jas na VSECH LED,
# pokud je zapojeny spravne - pozor na odber (36 LED x 60 mA ~ 2,2 A).
# ---------------------------------------------------------------------------------
set -euo pipefail   # konec pri prvni chybe (i uprostred roury) - DeploySkriptyTests
DEV=/dev/spidev0.0
case "${1:-}" in
    high) B='\377' ;;
    low)  B='\000' ;;
    half) B='\360' ;;
    *) echo "pouziti: $0 high|low|half"; exit 1 ;;
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
