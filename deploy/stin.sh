#!/bin/sh
# Stinova kopie binarek pred startem sluzby (ExecStartPre v arbot.service).
#
# PROC: bezici .NET binarku nejde prepsat - assembly jsou memory-mapped, takze zapis
# skonci na ETXTBSY (scp), nebo se prepise novy inode a bezici proces si dal drzi ten
# stary (rsync). Aplikace proto bezi z KOPIE a nasazuje se do puvodniho adresare, ktery
# zustava zapisovatelny i za behu. Kazdy start sluzby kopii obnovi, takze:
#
#     restart sluzby = nasazeni nove verze.
#
# Data (records/, logs/, config/, OSM/) do kopie NEJDOU - jsou v datovem adresari,
# ktery aplikace dostane parametrem dataroot=. Kopie je tim mala (~16 MB).
#
# Viz doc/headless.md a doc/plan-headless-provoz.md, navrh H.
set -eu

ZDROJ="${1:-/home/ales/arbot-headless}"
CIL="${2:-/home/ales/arbot-headless-run}"

if [ ! -d "$ZDROJ" ]; then
    echo "stin.sh: zdrojovy adresar '$ZDROJ' neexistuje - neni co nasadit." >&2
    exit 1
fi

mkdir -p "$CIL"

# -u = kopirovat jen novejsi. Prvni start prekopiruje vse, dalsi uz jen to, co se
# nasazenim zmenilo; setri to zapisy na disk. Prazdny adresar (prvni spusteni) resi
# find, protoze 'cp $ZDROJ/*' by pri zadne shode predal doslovnou hvezdicku.
find "$ZDROJ" -maxdepth 1 -type f -exec cp -pu {} "$CIL"/ \;

# Nativni knihovny per-RID (SkiaSharp, System.IO.Ports) - jen kdyz je publish
# ma jako podadresar; framework-dependent publish je vetsinou dava do korene.
if [ -d "$ZDROJ/runtimes" ]; then
    cp -pru "$ZDROJ/runtimes" "$CIL"/
fi

# POZOR: soubory smazane ze zdroje tu ZUSTANOU (cp neumi mazat a rsync na Pi neni).
# Po prejmenovani nebo odebrani assembly je proto potreba kopii jednou smazat:
#     systemctl stop arbot && rm -rf /home/ales/arbot-headless-run
echo "stin.sh: binarky z '$ZDROJ' pripraveny v '$CIL'."
