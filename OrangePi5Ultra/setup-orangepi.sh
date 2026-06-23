#!/bin/bash
# =====================================================================
#  setup-orangepi.sh -- obnova konfigurace Orange Pi 5 Ultra (Armbian+KDE)
#  po reinstalaci. Reprodukuje nastaveni z cervna 2026.
#
#  Obsahuje: GPU akcelerace (panthor), USB3 OTG->host, WiFi (iwd) + autostart,
#            WiFi pripojeni, nvtop, .NET 10 SDK, Samba share, RustDesk direct,
#            SSH klic, Intel RealSense SDK (librealsense 2.53.1, D435+T265).
#
#  SPUSTIT JAKO ROOT z KONZOLE nebo pres ETHERNET (NE pres WiFi, kterou
#  prave nastavujes - skript restartuje NetworkManager!):
#        sudo bash setup-orangepi.sh
#
#  Idempotentni - lze spustit opakovane. Hesla (WiFi, Samba) interaktivne.
#  POZOR: build RealSense (krok 9) trva ~15-20 min (kompilace ze zdrojaku).
#
#  POZOR pri kopirovani z Windows: soubor musi mit LF konce radku.
#  Pokud hlasi "bad interpreter" nebo "\r", spust nejdriv:
#        sed -i 's/\r$//' setup-orangepi.sh
# =====================================================================

# ----------------- KONFIGURACE (uprav podle potreby) -----------------
USERNAME="ales"
WIFI_SSID="VatNet"
SHARE_DIR="/home/ales/arbot"
LAN_SUBNET="192.168.88.0/24"
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
log "2) Instalace baliku (iwd, nvtop, samba, .NET 10 SDK)"
export DEBIAN_FRONTEND=noninteractive
apt-get update -o Acquire::ForceIPv4=true
apt-get install -y iwd nvtop samba dotnet-sdk-10.0
echo "  dotnet: $(dotnet --version 2>/dev/null || echo '?')"

# ----- 3) NetworkManager -> iwd backend + WiFi autostart fix -----
log "3) WiFi backend iwd + autostart drop-in"
mkdir -p /etc/NetworkManager/conf.d
printf '[device]\nwifi.backend=iwd\n' > /etc/NetworkManager/conf.d/wifi_backend.conf
mkdir -p /etc/systemd/system/iwd.service.d
cat > /etc/systemd/system/iwd.service.d/wait-for-wlan.conf <<'EOC'
[Unit]
# WiFi (bcmdhd) je na SDIO a registruje se az ~12s po startu.
# Pockej na existenci wlan0, nez se iwd spusti (jinak NEW_INTERFACE failed).
After=sys-subsystem-net-devices-wlan0.device
Wants=sys-subsystem-net-devices-wlan0.device
EOC
systemctl disable --now wpa_supplicant 2>/dev/null || true
systemctl enable --now iwd 2>/dev/null || true
systemctl daemon-reload
systemctl restart NetworkManager
sleep 3

# ----- 4) WiFi pripojeni -----
log "4) WiFi profil '$WIFI_SSID'"
if [ -n "$WIFI_SSID" ]; then
  read -rsp "  Zadej heslo k WiFi '$WIFI_SSID' (Enter = preskocit): " WPSK; echo
  if [ -n "$WPSK" ]; then
    nmcli connection delete "$WIFI_SSID" 2>/dev/null || true
    nmcli connection add type wifi con-name "$WIFI_SSID" ifname wlan0 ssid "$WIFI_SSID" \
      wifi-sec.key-mgmt wpa-psk wifi-sec.psk "$WPSK" connection.autoconnect yes >/dev/null 2>&1 \
      && echo "  NM profil vytvoren" || echo "  POZOR: nmcli add selhalo"
    # iwd vlastni ulozeni (autoconnect bez nutnosti secret agenta)
    mkdir -p /var/lib/iwd
    printf '[Security]\nPassphrase=%s\n' "$WPSK" > "/var/lib/iwd/${WIFI_SSID}.psk"
    chmod 600 "/var/lib/iwd/${WIFI_SSID}.psk"
    unset WPSK
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
  ufw allow from "$LAN_SUBNET" to any app Samba 2>/dev/null || true
  echo "  povoleno pro $LAN_SUBNET"
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
echo "  WiFi:      nmcli device status            (wlan0 connected)"
echo "  Samba:     z Windows  \\\\<ip-pi>\\$SHARE_NAME   (jmeno $USERNAME + samba heslo)"
echo "  .NET:      dotnet --info"
echo "  RealSense: rs-enumerate-devices -s        (D435 na USB3, T265 na USB2)"
echo "  SPI:       ls /dev/spidev0.0              (SPI0-M2 pro NeoPixel)"
echo
echo "Kamery: D435 -> USB3 port, T265 -> USB2 port. Oba USB3-A porty jsou po"
echo "tomto setupu funkcni (OTG port prepnut na host overlayem dwc3-host)."
echo "NeoPixel: datovy vstup (DIN) -> SPI0_MOSI = GPIO1_B1, GND -> GND. Zarizeni /dev/spidev0.0."
