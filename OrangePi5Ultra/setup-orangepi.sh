#!/bin/bash
# =====================================================================
#  setup-orangepi.sh -- obnova konfigurace Orange Pi 5 Ultra (Armbian+KDE)
#  po reinstalaci. Reprodukuje nastaveni z cervna 2026.
#
#  Obsahuje: GPU akcelerace (panthor), USB3 OTG->host, vlastni WiFi AP "arbot"
#            (hostapd) + ethernet s padem na prime spojeni, nvtop, .NET 10 SDK,
#            Samba share, RustDesk direct,
#            SSH klic, Intel RealSense SDK (librealsense 2.53.1, D435+T265).
#
#  SPUSTIT JAKO ROOT z KONZOLE nebo pres ETHERNET (NE pres WiFi, kterou
#  prave nastavujes - skript restartuje NetworkManager!):
#        sudo bash setup-orangepi.sh
#
#  Idempotentni - lze spustit opakovane. Hesla (AP, WiFi, Samba) interaktivne.
#  POZOR: build RealSense (krok 9) trva ~15-20 min (kompilace ze zdrojaku).
#
#  POZOR pri kopirovani z Windows: soubor musi mit LF konce radku.
#  Pokud hlasi "bad interpreter" nebo "\r", spust nejdriv:
#        sed -i 's/\r$//' setup-orangepi.sh
# =====================================================================

# ----------------- KONFIGURACE (uprav podle potreby) -----------------
USERNAME="ales"
SHARE_DIR="/home/ales/arbot"

# WiFi AP, ktere robot vystavuje (heslo se zada interaktivne, neni v repu):
AP_SSID="arbot"
AP_ADDR="192.168.7.1"
AP_CHANNEL=6
AP_COUNTRY="CZ"

# Ethernet: prime spojeni s notebookem, kdyz v siti neni DHCP
ETH_DIRECT_ADDR="192.168.66.1"

# WiFi klient - jen zaloha, na desce se VYLUCUJE s AP (viz POSTUP.md krok 3).
# Skript profil jen pripravi, nezapina ho.
WIFI_SSID="VatNet"

# Odkud smi Samba (AP, primy kabel, mistni sit):
LAN_SUBNETS="192.168.7.0/24 192.168.66.0/24 192.168.88.0/24"

# Verejny SSH klic pro prihlaseni z Windows (prazdne = preskocit):
SSH_PUBKEY="ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIMrOyQDyZ2gYzUPmDzX1iyLKchoOHJBTEwk5AIHIURkT claude-orangepi-diag"
# ---------------------------------------------------------------------

log(){ echo; echo "============================================================"; echo "  $*"; echo "============================================================"; }
if [ "$(id -u)" -ne 0 ]; then echo "Spust jako root:  sudo bash $0"; exit 1; fi

# ----- 1) Overlaye: panthor (GPU) + dwc3-host (USB3 OTG->host) + spi0-m2 (SPI/NeoPixel) -----
log "1) Overlaye: panthor-gpu + dwc3-host (USB3 OTG->host) + spi0-m2-cs0-spidev (SPI pro NeoPixel)"
ENVF=/boot/armbianEnv.txt
DOVL=/boot/dtb/rockchip/overlay
# Nektere overlaye maji v dodavce prefix 'rk3588-', ale Armbian s
# overlay_prefix=rockchip-rk3588 hleda 'rockchip-rk3588-*.dtbo' -> zkopirujeme pod ten nazev,
# jinak je tise preskoci a overlay se neaplikuje.
for ov in dwc3-host spi0-m2-cs0-spidev; do
  if [ -f "$DOVL/rk3588-$ov.dtbo" ] && [ ! -f "$DOVL/rockchip-rk3588-$ov.dtbo" ]; then
    cp "$DOVL/rk3588-$ov.dtbo" "$DOVL/rockchip-rk3588-$ov.dtbo"
    echo "  zkopirovan overlay '$ov' pod spravny nazev"
  fi
done
if [ -f "$ENVF" ]; then
  cp -n "$ENVF" "$ENVF.bak.setup" 2>/dev/null || true
  cur=""
  grep -q '^overlays=' "$ENVF" && cur=$(grep '^overlays=' "$ENVF" | head -1 | cut -d= -f2)
  for ov in panthor-gpu dwc3-host spi0-m2-cs0-spidev; do
    echo " $cur " | grep -q " $ov " || cur="$cur $ov"
  done
  cur=$(echo $cur | xargs)   # trim mezer
  sed -i '/^overlays=/d' "$ENVF"
  echo "overlays=$cur" >> "$ENVF"
  grep '^overlays=' "$ENVF"
else
  echo "  $ENVF neexistuje - preskoceno (jina deska?)"
fi

# ----- 2) Instalace baliku -----
log "2) Instalace baliku (hostapd, iwd, nvtop, samba, .NET 10 SDK)"
export DEBIAN_FRONTEND=noninteractive
apt-get update -o Acquire::ForceIPv4=true
# hostapd = AP; iwd zustava kvuli zalozni roli klienta (POSTUP.md krok 3);
# dnsmasq-base pouziva jak NM (sdilene pripojeni), tak arbot-ap-net.service.
apt-get install -y hostapd iwd dnsmasq-base nvtop samba dotnet-sdk-10.0
echo "  dotnet: $(dotnet --version 2>/dev/null || echo '?')"

# ----- 3) WiFi AP "arbot" na hostapd -----
# POZOR: AP NEJDE postavit pres NetworkManager. Vyzkouseno 29. 8. 2026:
#   - backend iwd:            nmcli spadne na net.connman.iwd.InvalidArguments
#   - backend wpa_supplicant: AP vysila, ale klient je odkopnut (DEAUTH reason=17);
#                             wpa_supplicant sam varuje, ze jeho AP rezim neni
#                             pro nl80211 urceny ("ap_scan=2 ... connection failures")
# Funguje jedine hostapd mimo NM. Detaily: POSTUP.md kroky 3 a 4.
log "3) WiFi AP '$AP_SSID' (hostapd) - wlan0 mimo NetworkManager"

read -rsp "  Zadej heslo pro AP '$AP_SSID' (8-63 znaku, Enter = preskocit AP): " AP_PSK; echo
if [ -n "$AP_PSK" ]; then
  install -d -m 755 /etc/hostapd
  umask 077
  cat > /etc/hostapd/hostapd.conf <<EOC
# AP robota ARBot - generovano setup-orangepi.sh, viz POSTUP.md krok 4.
interface=wlan0
driver=nl80211
ssid=$AP_SSID
hw_mode=g
channel=$AP_CHANNEL
ieee80211n=1
wmm_enabled=1
auth_algs=1
wpa=2
wpa_key_mgmt=WPA-PSK
rsn_pairwise=CCMP
country_code=$AP_COUNTRY
ieee80211d=1
wpa_passphrase=$AP_PSK
EOC
  chmod 600 /etc/hostapd/hostapd.conf
  umask 022
  unset AP_PSK
  echo "  /etc/hostapd/hostapd.conf zapsan (chmod 600)"
else
  echo "  AP preskoceno - hostapd.conf nezmenen"
fi

# wlan0 patri hostapd, ne NetworkManageru
mkdir -p /etc/NetworkManager/conf.d
cat > /etc/NetworkManager/conf.d/unmanaged-wlan0.conf <<'EOC'
# wlan0 ridi hostapd (AP), ne NetworkManager - viz POSTUP.md krok 4.
[keyfile]
unmanaged-devices=interface-name:wlan0
EOC
# backend pro pripad, ze by NM na wifi presto sahnul (iwd zustava pro zalozni klienta)
printf '[device]\nwifi.backend=wpa_supplicant\n' > /etc/NetworkManager/conf.d/wifi_backend.conf
systemctl disable --now iwd 2>/dev/null || true

# WiFi (bcmdhd) je na SDIO a registruje se az ~8-12 s po startu -> pockat na zarizeni.
mkdir -p /etc/systemd/system/hostapd.service.d
cat > /etc/systemd/system/hostapd.service.d/wait-for-wlan.conf <<'EOC'
[Unit]
After=sys-subsystem-net-devices-wlan0.device
Wants=sys-subsystem-net-devices-wlan0.device
[Service]
Restart=on-failure
RestartSec=5
EOC
# tentyz drop-in pro iwd, kdyby se robot vracel do role klienta
mkdir -p /etc/systemd/system/iwd.service.d
cat > /etc/systemd/system/iwd.service.d/wait-for-wlan.conf <<'EOC'
[Unit]
After=sys-subsystem-net-devices-wlan0.device
Wants=sys-subsystem-net-devices-wlan0.device
EOC

# hostapd sam nenastavuje IP - adresu, DHCP a NAT dela tahle jednotka.
# --except-interface=lo tam MUSI byt, jinak si dnsmasq vezme i 127.0.0.1:53.
AP_NET="${AP_ADDR%.*}.0/24"
cat > /etc/systemd/system/arbot-ap-net.service <<EOC
[Unit]
Description=ARBot AP: adresa, DHCP a NAT pro wlan0 ($AP_NET)
Requires=hostapd.service
After=hostapd.service
[Service]
Type=simple
ExecStartPre=/sbin/ip addr replace $AP_ADDR/24 dev wlan0
ExecStartPre=/sbin/ip link set wlan0 up
ExecStartPre=/sbin/sysctl -qw net.ipv4.conf.wlan0.forwarding=1
ExecStartPre=/bin/sh -c '/sbin/iptables -t nat -C POSTROUTING -s $AP_NET ! -d $AP_NET -j MASQUERADE 2>/dev/null || /sbin/iptables -t nat -A POSTROUTING -s $AP_NET ! -d $AP_NET -j MASQUERADE'
ExecStart=/usr/sbin/dnsmasq --keep-in-foreground --interface=wlan0 --bind-interfaces --listen-address=$AP_ADDR --dhcp-range=${AP_ADDR%.*}.10,${AP_ADDR%.*}.254,1h --except-interface=lo --no-hosts --dhcp-authoritative --dhcp-leasefile=/var/lib/misc/arbot-ap.leases
Restart=on-failure
RestartSec=5
[Install]
WantedBy=multi-user.target
EOC

systemctl daemon-reload
systemctl restart NetworkManager
sleep 3
if [ -f /etc/hostapd/hostapd.conf ]; then
  systemctl unmask hostapd 2>/dev/null || true
  systemctl enable --now hostapd
  sleep 3
  # nektere ovladace odmitnou country_code -> zkusit bez nej, at AP vubec bezi
  if ! systemctl is-active --quiet hostapd; then
    echo "  hostapd nenastartoval s country_code=$AP_COUNTRY, zkousim bez nej"
    sed -i '/^country_code=/d; /^ieee80211d=/d' /etc/hostapd/hostapd.conf
    systemctl restart hostapd; sleep 3
  fi
  systemctl enable --now arbot-ap-net
  systemctl is-active --quiet hostapd && echo "  hostapd bezi" || echo "  POZOR: hostapd NEBEZI"
fi

# ----- 4) Sitove profily: ethernet (DHCP -> pad na prime spojeni) -----
log "4) Ethernet: eth-dhcp (DHCP) s padem na eth-direct ($ETH_DIRECT_ADDR)"
ETH_DEV="$(nmcli -t -f DEVICE,TYPE device status | awk -F: '$2=="ethernet"{print $1; exit}')"
if [ -z "$ETH_DEV" ]; then
  echo "  POZOR: zadne ethernetove zarizeni nenalezeno - preskoceno"
else
  echo "  zarizeni: $ETH_DEV"
  ETH_NET="${ETH_DIRECT_ADDR%.*}.0/24"
  nmcli con delete eth-dhcp 2>/dev/null || true
  nmcli con delete eth-direct 2>/dev/null || true
  # ipv6.method ignore NENI kosmetika: s IPv6 ceka aktivace na RA (~30 s) a pad
  # na eth-direct trva 32 s misto 12 s (zmereno 29. 8. 2026).
  nmcli con add type ethernet ifname "$ETH_DEV" con-name eth-dhcp \
    ipv4.method auto ipv4.dhcp-timeout 10 ipv6.method ignore \
    connection.autoconnect yes connection.autoconnect-priority 100 \
    connection.autoconnect-retries 1 >/dev/null
  nmcli con add type ethernet ifname "$ETH_DEV" con-name eth-direct \
    ipv4.method shared ipv4.addresses "$ETH_DIRECT_ADDR/24" ipv6.method ignore \
    connection.autoconnect yes connection.autoconnect-priority 50 >/dev/null
  nmcli con delete 'Wired connection 1' 2>/dev/null || true

  # Netplan past: system je rizeny netplanem a ten umi pri zapisu zahodit
  # ipv4.method=shared (keyfile se generuje do /run). Zkontrolovat a doplnit.
  for f in /etc/netplan/*.yaml; do
    grep -q "$ETH_DIRECT_ADDR/24" "$f" 2>/dev/null || continue
    if ! grep -q 'ipv4.method: "shared"' "$f"; then
      echo "  netplan zahodil ipv4.method=shared v $f - doplnuji"
      sed -i "s|^\(\s*\)ipv4.address1: \"$ETH_DIRECT_ADDR/24\"|\1ipv4.address1: \"$ETH_DIRECT_ADDR/24\"\n\1ipv4.method: \"shared\"|" "$f"
    fi
  done
  netplan generate 2>/dev/null || true
  nmcli con reload
  # Restart NM vyse nechal viset stary dnsmasq, ktery drzi adresu sdileneho
  # pripojeni -> nova instance se tam nedostane a eth-direct cykli.
  pkill -f 'dnsmasq.*--clear-on-reload' 2>/dev/null || true
  nmcli device reapply "$ETH_DEV" >/dev/null 2>&1 || true
fi

# Zalozni profil klienta WiFi. NEZAPINA se - na desce se vylucuje s AP.
if [ -n "$WIFI_SSID" ]; then
  read -rsp "  Zadej heslo k WiFi '$WIFI_SSID' pro zalozni klientsky profil (Enter = preskocit): " WPSK; echo
  if [ -n "$WPSK" ]; then
    nmcli con delete "$WIFI_SSID" 2>/dev/null || true
    nmcli con add type wifi con-name "$WIFI_SSID" ifname wlan0 ssid "$WIFI_SSID" \
      wifi-sec.key-mgmt wpa-psk wifi-sec.psk "$WPSK" connection.autoconnect no >/dev/null 2>&1 \
      && echo "  profil '$WIFI_SSID' pripraven (autoconnect NE)" || echo "  POZOR: nmcli add selhalo"
    mkdir -p /var/lib/iwd
    printf '[Security]\nPassphrase=%s\n' "$WPSK" > "/var/lib/iwd/${WIFI_SSID}.psk"
    chmod 600 "/var/lib/iwd/${WIFI_SSID}.psk"
    unset WPSK
    echo "  prepnuti do role klienta: POSTUP.md krok 3"
  else
    echo "  preskoceno"
  fi
fi

# ----- 5) Samba share -----
SHARE_NAME="$(basename "$SHARE_DIR")"
log "5) Samba share [$SHARE_NAME] -> $SHARE_DIR"
mkdir -p "$SHARE_DIR"; chown "$USERNAME:$USERNAME" "$SHARE_DIR"; chmod 0775 "$SHARE_DIR"
cp -n /etc/samba/smb.conf /etc/samba/smb.conf.orig 2>/dev/null || true
awk -v s="[$SHARE_NAME]" '$0==s{inb=1;next} /^\[/&&inb{inb=0} !inb{print}' /etc/samba/smb.conf > /tmp/smb.new && mv /tmp/smb.new /etc/samba/smb.conf
sed -i '/^[[:space:]]*map to guest[[:space:]]*=/d' /etc/samba/smb.conf
cat >> /etc/samba/smb.conf <<EOC

[$SHARE_NAME]
   path = $SHARE_DIR
   browseable = yes
   writable = yes
   valid users = $USERNAME
   force user = $USERNAME
   force group = $USERNAME
   create mask = 0664
   directory mask = 0775
EOC
systemctl restart smbd 2>/dev/null; systemctl enable smbd 2>/dev/null || true
echo "  Nastav heslo pro Samba ucet '$USERNAME' (zadej 2x):"
smbpasswd -a "$USERNAME"

# ----- 6) ufw: Samba v LAN (pokud aktivni) -----
log "6) Firewall (ufw) - Samba v LAN"
if systemctl is-active --quiet ufw; then
  for net in $LAN_SUBNETS; do
    ufw allow from "$net" to any app Samba 2>/dev/null || true
    echo "  povoleno pro $net"
  done
else
  echo "  ufw neaktivni - preskoceno"
fi

# ----- 7) RustDesk direct IP access (jen pokud je nainstalovan) -----
log "7) RustDesk direct IP access (port 21118)"
rd_set(){
  local f="$1"
  [ -f "$f" ] || return
  grep -q '^direct-server' "$f" && { echo "  uz nastaveno: $f"; return; }
  if grep -q '^\[options\]' "$f"; then
    sed -i "/^\[options\]/a direct-server = 'Y'" "$f"
  else
    { echo; echo "[options]"; echo "direct-server = 'Y'"; } >> "$f"
  fi
  echo "  -> $f"
}
if command -v rustdesk >/dev/null 2>&1 || systemctl list-unit-files 2>/dev/null | grep -q '^rustdesk'; then
  systemctl stop rustdesk 2>/dev/null || true
  pkill -f 'rustdesk --server' 2>/dev/null || true; sleep 1
  rd_set "/home/$USERNAME/.config/rustdesk/RustDesk2.toml"
  rd_set "/root/.config/rustdesk/RustDesk2.toml"
  systemctl start rustdesk 2>/dev/null || true
else
  echo "  RustDesk neni nainstalovan - nainstaluj rucne (.deb z rustdesk.com),"
  echo "  pak spust skript znovu (jen doplni direct-server)."
fi

# ----- 8) SSH klic pro prihlaseni z Windows -----
log "8) SSH klic (authorized_keys pro $USERNAME)"
if [ -n "$SSH_PUBKEY" ]; then
  HOMEDIR="$(getent passwd "$USERNAME" | cut -d: -f6)"
  install -d -m 700 -o "$USERNAME" -g "$USERNAME" "$HOMEDIR/.ssh"
  touch "$HOMEDIR/.ssh/authorized_keys"
  grep -qF "$SSH_PUBKEY" "$HOMEDIR/.ssh/authorized_keys" || echo "$SSH_PUBKEY" >> "$HOMEDIR/.ssh/authorized_keys"
  chown -R "$USERNAME:$USERNAME" "$HOMEDIR/.ssh"; chmod 600 "$HOMEDIR/.ssh/authorized_keys"
  echo "  klic pridan"
fi

# ----- 9) Intel RealSense SDK (librealsense 2.53.1 - posledni s D435 i T265) -----
log "9) Intel RealSense SDK (librealsense 2.53.1) - POZOR ~15-20 min kompilace"
if command -v rs-enumerate-devices >/dev/null 2>&1; then
  echo "  librealsense uz je nainstalovan - preskakuji build"
else
  echo "  Instalace build zavislosti (vc. X11/GL pro viewer)..."
  apt-get install -y cmake build-essential git pkg-config libssl-dev libusb-1.0-0-dev libudev-dev \
    libx11-dev libxrandr-dev libxinerama-dev libxcursor-dev libxi-dev libgl-dev libglu1-mesa-dev
  RS_SRC="/home/$USERNAME/librealsense"
  rm -rf "$RS_SRC"
  git clone --depth 1 -b v2.53.1 https://github.com/IntelRealSense/librealsense.git "$RS_SRC"
  mkdir -p "$RS_SRC/build" && cd "$RS_SRC/build"
  # Flagy nutne na ARM64 / GCC 15 / CMake 4:
  #  FORCE_RSUSB_BACKEND  = libusb backend, nepatchuje kernel
  #  CMAKE_POLICY_VERSION_MINIMUM=3.5 = CMake 4 jinak odmita stary projekt
  #  -include cstdint -Wno-error = GCC 15 chybejici includy / prisne warningy
  cmake .. \
    -DCMAKE_BUILD_TYPE=Release \
    -DFORCE_RSUSB_BACKEND=ON \
    -DBUILD_EXAMPLES=ON \
    -DBUILD_GRAPHICAL_EXAMPLES=ON \
    -DBUILD_TOOLS=ON \
    -DBUILD_PYTHON_BINDINGS=OFF \
    -DBUILD_UNIT_TESTS=OFF \
    -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
    -DCMAKE_CXX_FLAGS="-include cstdint -Wno-error"
  echo "  Kompiluji (-j6)..."
  make -j6
  make install
  ldconfig
  cp "$RS_SRC/config/99-realsense-libusb.rules" /etc/udev/rules.d/ 2>/dev/null || true
  udevadm control --reload-rules 2>/dev/null; udevadm trigger 2>/dev/null
  chown -R "$USERNAME:$USERNAME" "$RS_SRC" 2>/dev/null || true
  echo "  librealsense nainstalovan ($(rs-enumerate-devices --version 2>&1 | grep -i version | head -1))"
fi
# Zastupce realsense-viewer na plochu
DESKDIR="$(getent passwd "$USERNAME" | cut -d: -f6)/Desktop"
mkdir -p "$DESKDIR"
cat > "$DESKDIR/realsense-viewer.desktop" <<'EOC'
[Desktop Entry]
Type=Application
Name=RealSense Viewer
Comment=Intel RealSense D435 / T265 viewer
Exec=/usr/local/bin/realsense-viewer
Icon=camera-video
Terminal=false
Categories=Graphics;
EOC
chmod +x "$DESKDIR/realsense-viewer.desktop"
chown "$USERNAME:$USERNAME" "$DESKDIR/realsense-viewer.desktop" 2>/dev/null || true

log "HOTOVO - doporucen reboot:  sudo reboot"
echo "Po rebootu overit:"
echo "  GPU:       glxinfo -B | grep Renderer   (Mali-G610, ne llvmpipe)  /  nvtop"
echo "  USB3 OTG:  cat /proc/device-tree/usbdrd3_0/usb@fc000000/dr_mode  (= host)"
echo "  AP:        systemctl is-active hostapd arbot-ap-net   (active active; wlan0 = unmanaged)"
echo "  Ethernet:  nmcli device status            (eth-dhcp v siti, jinak eth-direct)"
echo "  Samba:     z Windows  \\\\<ip-pi>\\$SHARE_NAME   (jmeno $USERNAME + samba heslo)"
echo "  .NET:      dotnet --info"
echo "  RealSense: rs-enumerate-devices -s        (D435 na USB3, T265 na USB2)"
echo "  SPI:       ls /dev/spidev0.0              (SPI0-M2 pro NeoPixel)"
echo
echo "Kamery: D435 -> USB3 port, T265 -> USB2 port. Oba USB3-A porty jsou po"
echo "tomto setupu funkcni (OTG port prepnut na host overlayem dwc3-host)."
echo "NeoPixel: datovy vstup (DIN) -> SPI0_MOSI = GPIO1_B1, GND -> GND. Zarizeni /dev/spidev0.0."
