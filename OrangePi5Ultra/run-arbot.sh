#!/bin/bash
# Spousti ARBot na Orange Pi tak, aby po padu NECO ZUSTALO. Viz POSTUP.md krok 12.
#
#   - stdout i stderr jdou do logs/arbot-<datum>.log (neodchycena vyjimka .NET jinak
#     skonci na stderr terminalu a s nim zmizi),
#   - pri nativnim padu (SIGSEGV v librealsense apod.) .NET zapise minidump do logs/,
#   - na konci se zapise navratovy kod (139 = SIGSEGV, 134 = SIGABRT, 137 = SIGKILL/OOM).
#
# Pouziti:  ~/arbot/run-arbot.sh [parametry ARBot]      napr.  ~/arbot/run-arbot.sh config=config/pi-freerun.cfg
# Polozka v menu plochy (~/.local/share/applications/ARBot.desktop) ukazuje sem, ne primo na dotnet.
# Sledovani za behu:  tail -f ~/arbot/logs/arbot-*.log
#
# Skript lezi v repozitari (OrangePi5Ultra/run-arbot.sh) a na Pi se kopiruje do ~/arbot/ vedle ARBot.dll.

cd "$(dirname "$(readlink -f "$0")")" || exit 1
mkdir -p logs

# Minidump .NET pri nativnim padu. Typ 1 = Mini (zasobniky vsech vlaken, bez heapu; jednotky MB).
# %p = PID, %t = cas. Cte se pres `dotnet-dump analyze <soubor>` -> `clrstack -all`.
export DOTNET_DbgEnableMiniDump=1
export DOTNET_DbgMiniDumpType=1
export DOTNET_DbgMiniDumpName="$PWD/logs/core-%p-%t.dmp"

# Uklid: logy starsi 30 dni, dumpy starsi 14 dni (dump ma jednotky MB, eMMC neni bezedna).
find logs -maxdepth 1 -name 'arbot-*.log' -mtime +30 -delete 2>/dev/null
find logs -maxdepth 1 -name 'core-*.dmp'  -mtime +14 -delete 2>/dev/null

LOG="logs/arbot-$(date +%Y%m%d-%H%M%S).log"
{
    echo "== start $(date -Is)  args: $*"
    echo "== host $(hostname)  uptime: $(uptime -p)  dotnet: $(dotnet --version 2>/dev/null)"
} >> "$LOG"

/usr/bin/dotnet ARBot.dll "$@" >> "$LOG" 2>&1
rc=$?

echo "== konec $(date -Is)  exit=$rc" >> "$LOG"
exit $rc
