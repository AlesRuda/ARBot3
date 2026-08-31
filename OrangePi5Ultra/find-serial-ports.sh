#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Hledani seriovych portu periferii ARBota na Orange Pi (Armbian / ARM64).
#
# Proc: v kodu (Src/ARBot/Robot/ARBotHW.cs, Init) je pro ARM64 jen ODHAD
#   PortAHRS = "/dev/ttyS0", motor a GPS nejsou vyplnene vubec. Na Windows jsou
#   porty COM5 (IMU) / COM9 (motor) / COM8 (GPS). Tenhle skript zjisti, ktere
#   zarizeni je na Pi na kterem /dev/tty* a vypise hotove parametry pro
#   prikazovou radku nebo konfiguracni profil (UartAHRS= / UartMotor= / UartGPS=).
#
# Jak to pozna: dvema nezavislymi zpusoby, oba bez zapisu do portu:
#   a) USB deskriptor (udev) - u-blox i Roboteq se hlasi vlastnim jmenem, takze
#      jsou poznat okamzite a spolehlive. Prevodnik CP2102/FTDI jmeno nema.
#   b) PASIVNI POSLUCH toho, co zarizeni samo vysila:
#     - VN100 IMU (VectorNav)      115200  binarni pakety zacinaji 0xFA, rozestup 80 B
#     - u-blox GPS                 (CDC)   UBX pakety 0xB5 0x62, pripadne NMEA "$GNxxx"
#     - SDC2160Ex (Roboteq) motor  (CDC)   ASCII radky "DI=", "C=", "V=", "A="
#
#   POZOR - PROC MOTOR PASIVNE MLCI: Roboteq zacne posilat telemetrii teprve,
#   az mu host posle prvni bajt; do te doby je port uplne ticho (0 B na vsech
#   rychlostech). Neni to zavada ani spatny port. Realny driver to nepozna,
#   protoze SDC2160Ex hned v konstruktoru posila "^ECHOF 1". Motor se proto
#   urcuje podle USB deskriptoru (bod a), pasivni posluch u nej nic nedokaze.
#   Overeno 31. 8. 2026: po dotazu "?FID" prisla odpoved i cela telemetrie.
#
#   Pozn.: u-blox i Roboteq jdou pres USB CDC-ACM, kde je nastavena rychlost
#   bezvyznamna - data tecou stejne na vsech. Skutecnou rychlost ma jen ttyUSB0
#   (CP2102), kde je za prevodnikem opravdovy UART do VN100.
#
# Pouziti:
#   sudo bash find-serial-ports.sh                 # projde vsechny kandidaty
#   sudo bash find-serial-ports.sh --dur 4         # delsi posluch (default 2 s)
#   sudo bash find-serial-ports.sh --dev /dev/ttyUSB0
#   sudo bash find-serial-ports.sh --bauds "115200 921600"
#
# Pozn.: spoustej pod sudo (pristup k /dev/tty*). Nic to nemeni ani nezapisuje.
#        ARBot pritom nesmi bezet — drzel by porty otevrene (sekce 1 to ohlasi).
# ---------------------------------------------------------------------------
set -u

DUR=2
BAUDS="115200 921600 9600 38400 57600 230400 460800"
ONLY_DEV=""

while [ $# -gt 0 ]; do
  case "$1" in
    --dur)   DUR="$2"; shift 2 ;;
    --bauds) BAUDS="$2"; shift 2 ;;
    --dev)   ONLY_DEV="$2"; shift 2 ;;
    -h|--help) sed -n '2,26p' "$0"; exit 0 ;;
    *) echo "Neznamy parametr: $1" >&2; exit 2 ;;
  esac
done

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

hr() { printf '%s\n' "-----------------------------------------------------------------"; }

# ===========================================================================
# 1) Inventura: co vubec v systemu je
# ===========================================================================
hr; echo "1) INVENTURA SERIOVYCH ZARIZENI"; hr

echo "== /dev/serial/by-id (stabilni jmena - TOHLE patri do konfigurace) =="
if [ -d /dev/serial/by-id ]; then
  ls -l /dev/serial/by-id/ 2>/dev/null | sed 's/^/  /'
else
  echo "  (neexistuje - zadny USB-serial prevodnik pripojeny)"
fi

echo
echo "== /dev/serial/by-path (podle fyzickeho USB portu) =="
if [ -d /dev/serial/by-path ]; then
  ls -l /dev/serial/by-path/ 2>/dev/null | sed 's/^/  /'
else
  echo "  (neexistuje)"
fi

echo
echo "== USB-serial a ACM zarizeni =="
ls -l /dev/ttyUSB* /dev/ttyACM* 2>/dev/null | sed 's/^/  /' || echo "  (zadne)"

echo
echo "== Onboard UARTy (ttyS*) a jejich skutecny driver =="
# Na RK3588 existuje /dev/ttyS0..ttyS9, ale VETSINA JE MRTVA - bez overlaye
# (uart1-m1 apod.) nema UART vyvedeny pin. Ziva je ta, ktera ma v
# /proc/tty/driver/serial neprazdny 'uart:'. Rozliseni je dulezite: cteni
# z mrtveho portu vrati nula bajtu, coz vypada uplne stejne jako "nic tam neni".
if [ -r /proc/tty/driver/serial ]; then
  echo "  /proc/tty/driver/serial:"
  cat /proc/tty/driver/serial 2>/dev/null | sed 's/^/    /'
else
  echo "  (/proc/tty/driver/serial nelze cist - spust pod sudo)"
fi
echo "  dmesg (probnute UARTy):"
dmesg 2>/dev/null | grep -iE "ttyS[0-9]|serial8250|dw-apb-uart|fiq_debugger" | tail -20 | sed 's/^/    /'

echo
echo "== Aktivni device-tree overlaye (UART na 40pin headeru je potrebuje) =="
grep -E "^overlays=|^overlay_prefix=" /boot/armbianEnv.txt 2>/dev/null | sed 's/^/  /' \
  || echo "  (/boot/armbianEnv.txt nedostupny)"

echo
echo "== lsusb (hledej FTDI / Silicon Labs CP210x / Prolific / CH340) =="
lsusb 2>/dev/null | sed 's/^/  /' || echo "  (lsusb neni nainstalovan)"

echo
echo "== Kdo drzi porty otevrene (getty na konzoli, bezici ARBot...) =="
for d in /dev/ttyUSB* /dev/ttyACM* /dev/ttyS*; do
  [ -e "$d" ] || continue
  holder="$(fuser "$d" 2>/dev/null | tr -d ' ')"
  [ -n "$holder" ] && echo "  $d  <- PID $holder ($(ps -o comm= -p $holder 2>/dev/null | tr '\n' ' '))"
done
systemctl list-units --type=service --state=running 2>/dev/null \
  | grep -oE "serial-getty@[a-zA-Z0-9]+" | sort -u | sed 's/^/  konzole: /'

# ===========================================================================
# 2) Kandidati k proslechnuti
# ===========================================================================
CANDS=""
if [ -n "$ONLY_DEV" ]; then
  CANDS="$ONLY_DEV"
else
  for d in /dev/ttyUSB* /dev/ttyACM*; do [ -e "$d" ] && CANDS="$CANDS $d"; done
  # ttyS* jen ty, ktere jsou v /proc/tty/driver/serial jako skutecny UART
  if [ -r /proc/tty/driver/serial ]; then
    for n in $(grep -E "uart:(16550A|DW-APB|SNPS)" /proc/tty/driver/serial 2>/dev/null \
               | cut -d: -f1 | tr -d ' '); do
      [ -e "/dev/ttyS$n" ] && CANDS="$CANDS /dev/ttyS$n"
    done
  fi
fi

hr; echo "2) PASIVNI POSLUCH (${DUR}s na kazdou rychlost, nic se nezapisuje)"; hr
if [ -z "${CANDS// /}" ]; then
  echo "Zadny kandidat. Bud nejsou periferie pripojene, nebo jsou na onboard UARTu"
  echo "bez overlaye (viz sekce 1) - pak je potreba pridat napr. 'uart1-m1' do"
  echo "overlays= v /boot/armbianEnv.txt a rebootovat."
  exit 1
fi
echo "Kandidati:$CANDS"
echo

# pocet vyskytu jednoho bajtu
cnt1() { od -An -tx1 -v "$1" | tr -s ' ' '\n' | grep -c "^$2$"; }
# pocet vyskytu dvojice bajtu ZA SEBOU (spravne zarovnane, ne v hex retezci)
cnt2() { od -An -tx1 -v "$1" | tr -s ' ' '\n' | grep -v '^$' \
         | awk -v a="$2" -v b="$3" '{if(p==a && $0==b) n++; p=$0} END{print n+0}'; }
# pocet vyskytu ASCII vzorku
cnta() { grep -aoF -- "$2" "$1" 2>/dev/null | wc -l; }
# pocet rozestupu mezi sousednimi 0xFA presne rovnych $2 bajtum
# (VN100 se pozna PERIODICITOU synchronizacniho bajtu, ne jeho cetnosti - viz nize)
strideFA() { od -An -tx1 -v "$1" | tr -s ' ' '\n' | grep -v '^$' \
             | awk -v w="$2" '{i++; if($0=="fa"){ if(p && i-p==w) n++; p=i }} END{print n+0}'; }

# Ktere rychlosti tenhle stty vubec umi (na Armbianu odmita napr. 460800).
# Zjisti se jednou dopredu, aby to pak nehlasil u kazdeho zarizeni zvlast.
probe_dev=""
for d in $CANDS; do [ -e "$d" ] && { probe_dev="$d"; break; }; done
OKBAUDS=""
for b in $BAUDS; do
  if timeout 3 stty -F "$probe_dev" "$b" >/dev/null 2>&1; then OKBAUDS="$OKBAUDS $b"
  else echo "Rychlost $b tenhle stty neumi - preskakuji."; fi
done
BAUDS="$OKBAUDS"

RESULT="$TMP/result.txt"
: > "$RESULT"

for dev in $CANDS; do
  echo "### $dev"
  # udev identifikace (VID/PID prevodniku) - rekne aspon typ cipu, kdyz mlci
  if command -v udevadm >/dev/null 2>&1; then
    udevadm info -q property -n "$dev" 2>/dev/null \
      | grep -E "^(ID_VENDOR|ID_VENDOR_ID|ID_MODEL|ID_MODEL_ID|ID_SERIAL_SHORT|ID_USB_DRIVER|ID_PATH)=" \
      | sed 's/^/    /'
  fi

  # --- a) identifikace podle USB deskriptoru -------------------------------
  # Nejsilnejsi dukaz, kdyz je k dispozici: u-blox i Roboteq maji vlastni VID
  # a rikaji sve jmeno samy. Prevodniky (CP210x, FTDI) jmeno zarizeni za sebou
  # neznaji, takze u nich rozhoduje az posluch.
  vid="$(udevadm info -q property -n "$dev" 2>/dev/null | sed -n 's/^ID_VENDOR_ID=//p')"
  pid="$(udevadm info -q property -n "$dev" 2>/dev/null | sed -n 's/^ID_MODEL_ID=//p')"
  drv="$(udevadm info -q property -n "$dev" 2>/dev/null | sed -n 's/^ID_USB_DRIVER=//p')"
  usb_id=""
  case "$vid:$pid" in
    1546:*)     usb_id="GPS (u-blox)" ;;
    20d2:*)     usb_id="MOTOR SDC2160Ex (Roboteq)" ;;
    10c4:ea60)  echo "    (CP210x prevodnik - jmeno zarizeni za nim nezna, rozhodne posluch)" ;;
    0403:*)     echo "    (FTDI prevodnik - jmeno zarizeni za nim nezna, rozhodne posluch)" ;;
  esac

  best_id="?"; best_baud=""; best_note=""; gotdata=0
  if [ -n "$usb_id" ]; then
    echo "    -> podle USB deskriptoru: $usb_id"
    best_id="$usb_id"; best_note="USB deskriptor $vid:$pid"
    # U CDC-ACM je nastavena rychlost bezvyznamna, data tecou stejne na vsech.
    [ "$drv" = "cdc_acm" ] && best_baud="(CDC)"
  fi
  for baud in $BAUDS; do
    cap="$TMP/$(basename "$dev").$baud.bin"
    # raw, bez rizeni toku a bez echa; clocal => nezajima nas DCD (jinak open() visi).
    # POZOR: rychlost se zadava HOLYM CISLEM. Slovo 'speed' je v stty DOTAZ na
    # rychlost, ne jeji nastaveni - s nim skonci cely prikaz na "invalid argument"
    # a vsechny porty se tvari jako mrtve (stalo to jeden beh mereni, 31. 8. 2026).
    err="$(timeout 3 stty -F "$dev" "$baud" raw cs8 -parenb -cstopb \
        -echo -echoe -echok -echoctl -echoke -ixon -ixoff -crtscts clocal cread \
        min 1 time 0 2>&1 >/dev/null)" \
      || { echo "    ${baud}: stty selhalo: ${err:-(bez hlasky, port asi obsazeny)}"; continue; }
    timeout "$DUR" cat "$dev" > "$cap" 2>/dev/null
    n=$(stat -c%s "$cap" 2>/dev/null || echo 0)
    [ "$n" -eq 0 ] && { echo "    ${baud}: 0 B"; continue; }
    gotdata=1

    fa=$(cnt1 "$cap" fa)
    fa80=$(strideFA "$cap" 80)
    ubx=$(cnt2 "$cap" b5 62)
    vnascii=$(cnta "$cap" '$VN')
    nmea=$(( $(cnta "$cap" '$GN') + $(cnta "$cap" '$GP') + $(cnta "$cap" '$GA') ))
    di=$(cnta "$cap" 'DI=')
    # pomer tisknutelnych znaku - pozna spravnou rychlost u ASCII zarizeni
    pr=$(tr -dc '\11\12\15\40-\176' < "$cap" | wc -c)
    prpct=$(( 100 * pr / n ))

    id="?"; note="tisknutelnych ${prpct}% (spatna rychlost?)"
    if [ "$ubx" -ge 2 ] || [ "$nmea" -ge 2 ]; then
      id="GPS (u-blox)"; note="UBX=$ubx NMEA=$nmea"
    elif [ "$vnascii" -ge 2 ]; then
      id="IMU VN100 (ASCII)"; note="\$VN=$vnascii"
    elif [ "$di" -ge 1 ]; then
      id="MOTOR SDC2160Ex (Roboteq)"; note="DI= ${di}x"
    elif [ "$fa80" -ge 20 ]; then
      # VN100 se pozna PERIODICITOU synchronizacniho bajtu, ne jeho cetnosti.
      # Cetnost k nicemu neni ze dvou stran: v nahodnem smeti (= spatna rychlost)
      # je 0xFA jeden z 256 bajtu, a naopak v platnem streamu se 0xFA nahodne
      # vyskytuje i uvnitr floatu. Rozestup 80 B je dany konfiguraci driveru
      # (VN100IMUBinary.Configure): mag+accel+gyro 36 B + ypr+yprU+yprRate 36 B
      # + 8 B hlavicka a CRC = 80 B, pri ~100 Hz tedy 8000 B/s.
      # Puvodni prah "aspon jedna 0xFA z 60 bajtu" tenhle stream ZAMITAL
      # (80 > 60) - stalo to jedno mereni, 31. 8. 2026.
      id="IMU VN100 (binarni)"; note="0xFA rozestup 80 B: ${fa80}x (z $fa vyskytu), tisknutelnych ${prpct}%"
    elif [ "$prpct" -ge 90 ]; then
      id="neznamy ASCII"; note="tisknutelnych ${prpct}%"
    fi

    printf "    %-8s %7s B  -> %-28s %s\n" "${baud}:" "$n" "$id" "$note"
    echo "      ukazka: $(head -c 96 "$cap" | tr -dc '\40-\176' | head -c 80)"
    echo "      hex   : $(od -An -tx1 -v "$cap" | head -1 | cut -c1-72)"

    case "$id" in
      "?"|"neznamy ASCII") ;;
      *) if [ "$best_id" = "?" ]; then best_id="$id"; best_baud="$baud"; best_note="$note"; fi ;;
    esac
  done

  if [ "$gotdata" -eq 0 ]; then
    case "$best_id" in
      MOTOR*) echo "    Pasivne ticho - u Roboteqa NORMALNI stav, zacne vysilat teprve po"
              echo "    prvnim prijatem bajtu. Port je spravny, viz hlavicka skriptu." ;;
      *)      echo "    Pasivne ticho na vsech rychlostech - zarizeni nevysila samo, nebo"
              echo "    je port obsazeny / nezapojeny (viz 'Kdo drzi porty' v sekci 1)." ;;
    esac
  fi

  # stabilni jmeno pro tento uzel, pokud existuje
  byid=""
  if [ -d /dev/serial/by-id ]; then
    for l in /dev/serial/by-id/*; do
      [ -e "$l" ] || continue
      if [ "$(readlink -f "$l")" = "$(readlink -f "$dev")" ]; then byid="$l"; break; fi
    done
  fi
  echo "$dev|$best_id|$best_baud|${byid:-$dev}|$best_note" >> "$RESULT"
  echo
done

# ===========================================================================
# 3) Vysledek + hotove parametry
# ===========================================================================
hr; echo "3) VYSLEDEK"; hr
printf "%-16s %-28s %-9s %s\n" "UZEL" "ROZPOZNANO" "RYCHLOST" "STABILNI JMENO"
while IFS='|' read -r dev id baud byid note; do
  printf "%-16s %-28s %-9s %s\n" "$dev" "$id" "${baud:--}" "$byid"
done < "$RESULT"

echo
echo "Parametry pro ARBota (prikazova radka nebo profil, viz doc/configuration.md):"
while IFS='|' read -r dev id baud byid note; do
  case "$id" in
    IMU*)   echo "  UartAHRS=$byid" ;;
    GPS*)   echo "  UartGPS=$byid" ;;
    MOTOR*) echo "  UartMotor=$byid" ;;
  esac
done < "$RESULT"
echo
echo "Pozn.: pouzij jmeno z /dev/serial/by-id/, kdyz existuje - /dev/ttyUSB0..2 se"
echo "       mezi restarty prehazuje podle poradi enumerace USB, by-id ne."
echo "Pozn.: rychlosti jsou v kodu pevne (IMU 115200, motor 115200, GPS 921600);"
echo "       skript je jen overuje. Kdyz vyjde jina, patri oprava do driveru."
