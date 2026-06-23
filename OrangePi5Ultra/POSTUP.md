# Orange Pi 5 Ultra — postup nastavení (po reinstalaci)

Dokumentace ke konfiguraci provedené v červnu 2026. Slouží k rychlé obnově
Pi do funkčního stavu po reinstalaci Armbianu.

- **Deska:** Orange Pi 5 Ultra (RK3588, GPU Mali-G610, WiFi čip AP6275P/SYN43711)
- **OS:** Armbian (vendor kernel 6.1.x, base Ubuntu 26.04 arm64), KDE Plasma na Waylandu
- **Uživatel:** `ales`
- **Síť:** WiFi `VatNet`, podsíť `192.168.88.0/24`
- **Periferie:** Intel RealSense **D435** (depth) + **T265** (tracking)

> Automat: vše níže provede skript **`setup-orangepi.sh`** (ve stejné složce).
> Tento dokument popisuje, co skript dělá a proč — a slouží i jako ruční postup.

---

## ⚡ Rychlý postup (automatem)

1. Reinstaluj Armbian, nabootuj, přihlas se (konzole/monitor nebo ethernet).
2. Dostaň na Pi soubor `setup-orangepi.sh` (USB klíč, scp, nebo zkopíruj obsah).
3. Oprav konce řádků (kdyby přišel z Windows s CRLF):
   ```bash
   sed -i 's/\r$//' setup-orangepi.sh
   ```
4. Spusť **z konzole nebo přes ethernet** (NE přes WiFi, restartuje se síť):
   ```bash
   sudo bash setup-orangepi.sh
   ```
   Vyžádá si heslo k WiFi a heslo pro Samba účet.
   ⚠️ Krok 9 (RealSense SDK) kompiluje ze zdrojáků → **~15-20 min**.
5. Restartuj: `sudo reboot`

---

## 🔧 Jednotlivé kroky (a proč)

### 1. Overlaye: GPU (`panthor`) + USB3 OTG→host (`dwc3-host`) + SPI (`spi0-m2`, NeoPixel)
**GPU — problém:** bez akcelerace KDE renderuje softwarově (llvmpipe),
`kwin_wayland` žere ~2 jádra CPU. Mali-G610 (Valhall/CSF) potřebuje ovladač
**`panthor`** (ne `panfrost`, ten G610 neumí).

**USB3 OTG port — problém:** RK3588 má dva USB3-A porty: jeden „host" (jede hned)
a jeden **OTG**, který default běží v peripheral/device režimu → připojené
zařízení (kamera, flash) se **vůbec nezaregistruje** (nulová reakce v dmesg).
Overlay **`dwc3-host`** ten OTG port přepne natvrdo na **host**.

**SPI — pro NeoPixely (WS2812):** WS2812 se řídí přes **SPI MOSI** (přesné časování).
Default není vyvedené žádné `/dev/spidev`. Overlay **`spi0-m2-cs0-spidev`** zapne
SPI0 (mux M2) jako spidev na 40pin headeru.

**Řešení:** v `/boot/armbianEnv.txt`:
```bash
overlays=panthor-gpu dwc3-host spi0-m2-cs0-spidev
```
⚠️ **Háček s prefixem:** soubory `rk3588-dwc3-host.dtbo` a
`rk3588-spi0-m2-cs0-spidev.dtbo` mají prefix `rk3588-`, ale Armbian s
`overlay_prefix=rockchip-rk3588` hledá `rockchip-rk3588-*.dtbo` → **nenajde a tiše
přeskočí**. Nutno zkopírovat pod správný název:
```bash
cd /boot/dtb/rockchip/overlay
sudo cp rk3588-dwc3-host.dtbo            rockchip-rk3588-dwc3-host.dtbo
sudo cp rk3588-spi0-m2-cs0-spidev.dtbo   rockchip-rk3588-spi0-m2-cs0-spidev.dtbo
```
**Ověření po rebootu:**
- GPU: `glxinfo -B | grep Renderer` → `Mali-G610 MC4 (Panfrost)` (ne `llvmpipe`)
- OTG: `cat /proc/device-tree/usbdrd3_0/usb@fc000000/dr_mode` → `host`
- SPI: `ls /dev/spidev0.0`

**NeoPixel zapojení (SPI0-M2):** datový vstup **DIN → SPI0_MOSI = GPIO1_B1**,
**GND → GND**. (CLK=GPIO1_B3, MISO=GPIO1_B2, CS0=GPIO1_B4 — pro WS2812 netřeba.)
Zařízení `/dev/spidev0.0`, max 50 MHz. V Pythonu např. `Adafruit_CircuitPython_NeoPixel_SPI`.

> Pozn.: existuje ještě 3. USB3 řadič SoC (`usb@fcd00000`/`usbhost3_0`), který je
> v DT záměrně **disabled** — jeho combo-PHY (combphy2) je vyhrazena onboard 2.5G
> ethernetu (`pcie@fe180000`, RTL8125). To NENÍ ten OTG port; nech být.

### 2. Instalace balíků
```bash
sudo apt-get update
sudo apt-get install -y iwd nvtop samba dotnet-sdk-10.0
```
- **iwd** — WiFi backend (viz krok 3)
- **nvtop** — monitor využití GPU (KDE System Monitor to u Mali neukáže)
- **samba** — sdílení adresáře do Windows (krok 5)
- **dotnet-sdk-10.0** — .NET 10 LTS (aktuální v Ubuntu 26.04)

### 3. WiFi backend `iwd` + autostart
**Problém A — handshake:** s výchozím `wpa_supplicant` 2.11 selhává WPA2
handshake (`wl_set_multi_akm: Failed to set join_pref` → klamavé "WRONG_KEY").
Je to nekompatibilita wpa_supplicant 2.11 × Rockchip ovladač `bcmdhd` (FullMAC).
**Řešení A:** přepnout NetworkManager na backend **`iwd`** (dělá handshake softwarově):
```bash
sudo mkdir -p /etc/NetworkManager/conf.d
printf '[device]\nwifi.backend=iwd\n' | sudo tee /etc/NetworkManager/conf.d/wifi_backend.conf
sudo systemctl disable --now wpa_supplicant
sudo systemctl enable --now iwd
sudo systemctl restart NetworkManager
```
**Problém B — autostart:** WiFi (SDIO) se registruje až ~12 s po startu, iwd
naběhne dřív, nenajde `wlan0` (`NEW_INTERFACE failed`) a WiFi nenaskočí.
**Řešení B:** drop-in, který iwd počká na zařízení:
```bash
sudo mkdir -p /etc/systemd/system/iwd.service.d
sudo tee /etc/systemd/system/iwd.service.d/wait-for-wlan.conf <<'EOF'
[Unit]
After=sys-subsystem-net-devices-wlan0.device
Wants=sys-subsystem-net-devices-wlan0.device
EOF
sudo systemctl daemon-reload
```

### 4. WiFi připojení (VatNet)
```bash
sudo nmcli connection add type wifi con-name VatNet ifname wlan0 ssid VatNet \
  wifi-sec.key-mgmt wpa-psk wifi-sec.psk 'HESLO' connection.autoconnect yes
```
Pro spolehlivý autoconnect přes iwd lze navíc předvyplnit iwd úložiště:
```bash
printf '[Security]\nPassphrase=HESLO\n' | sudo tee /var/lib/iwd/VatNet.psk
sudo chmod 600 /var/lib/iwd/VatNet.psk
```
**Ověření po rebootu:** `nmcli device status` → `wlan0  connected  VatNet`.

### 5. Samba sdílení `/home/ales/arbot`
Pro nahrávání aplikace na Pi a čtení logů z Windows.
```bash
sudo mkdir -p /home/ales/arbot
sudo chown ales:ales /home/ales/arbot && sudo chmod 0775 /home/ales/arbot
```
Do `/etc/samba/smb.conf` přidat sekci:
```ini
[arbot]
   path = /home/ales/arbot
   browseable = yes
   writable = yes
   valid users = ales
   force user = ales
   force group = ales
   create mask = 0664
   directory mask = 0775
```
```bash
sudo systemctl restart smbd && sudo systemctl enable smbd
sudo smbpasswd -a ales        # nastaví heslo pro síťové sdílení (zadat 2×)
```
Pokud je aktivní firewall **ufw**, povolit Sambu v LAN:
```bash
sudo ufw allow from 192.168.88.0/24 to any app Samba
```
**Připojení z Windows (Průzkumník):** `\\192.168.88.24\arbot`,
přihlášení: jméno `ales` + heslo z `smbpasswd` (žádný registry tweak netřeba,
protože jdeme s heslem, ne guest).

### 6. ufw (řešeno v kroku 5)

### 7. RustDesk — přístup bez internetu (direct IP access, port 21118)
RustDesk je nutné nainstalovat ručně (.deb z rustdesk.com) — generuje vlastní
ID a heslo. Poté zapnout direct IP access v **obou** configech (jen uživatelský
se při startu přepíše z root configu):
```bash
sudo systemctl stop rustdesk
# do [options] v obou souborech přidat: direct-server = 'Y'
#   /home/ales/.config/rustdesk/RustDesk2.toml
#   /root/.config/rustdesk/RustDesk2.toml
sudo systemctl start rustdesk
```
**Připojení:** z klienta zadat IP Pi (`192.168.88.24` nebo `:21118`) + permanentní heslo.

### 8. SSH klíč pro přihlášení z Windows
Veřejný klíč (z `C:\Users\Ales\.ssh\id_ed25519.pub`) přidat do
`~/.ssh/authorized_keys` uživatele `ales`. Skript to udělá automaticky
(klíč je v něm v proměnné `SSH_PUBKEY`).

### 9. Intel RealSense SDK — librealsense **2.53.1** (D435 + T265)
**Proč právě 2.53.1:** podpora **T265** byla z `librealsense` odebrána ve
verzi 2.54.1 → poslední verze, kde fungují **obě** kamery, je **2.53.1**
(poslední Intelem validovaná pro T265 je 2.50.0). D435 je podporovaná dál.
Pro ARM64 nejsou oficiální balíčky → **build ze zdrojáků** (RSUSB backend,
bez patchování kernelu).
```bash
sudo apt-get install -y cmake build-essential git pkg-config libssl-dev \
  libusb-1.0-0-dev libudev-dev libx11-dev libxrandr-dev libxinerama-dev \
  libxcursor-dev libxi-dev libgl-dev libglu1-mesa-dev
git clone --depth 1 -b v2.53.1 https://github.com/IntelRealSense/librealsense.git ~/librealsense
cd ~/librealsense && mkdir build && cd build
cmake .. -DCMAKE_BUILD_TYPE=Release -DFORCE_RSUSB_BACKEND=ON \
  -DBUILD_EXAMPLES=ON -DBUILD_GRAPHICAL_EXAMPLES=ON -DBUILD_TOOLS=ON \
  -DBUILD_PYTHON_BINDINGS=OFF -DBUILD_UNIT_TESTS=OFF \
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5 -DCMAKE_CXX_FLAGS="-include cstdint -Wno-error"
make -j6 && sudo make install && sudo ldconfig
sudo cp ~/librealsense/config/99-realsense-libusb.rules /etc/udev/rules.d/
sudo udevadm control --reload-rules && sudo udevadm trigger
```
**Nutné build flagy a proč** (jinak to na téhle platformě nepřeloží):
- `FORCE_RSUSB_BACKEND=ON` — libusb backend, nepotřebuje patchovat kernel
- `CMAKE_POLICY_VERSION_MINIMUM=3.5` — CMake 4 jinak odmítne starý projekt
- `-include cstdint -Wno-error` — GCC 15 (chybějící includy / přísné warningy)

**Použití:**
```bash
rs-enumerate-devices -s     # seznam kamer (D435, T265)
realsense-viewer            # GUI (na desktopu) – depth, RGB, póza T265
```
**Zapojení kamer:** D435 → **USB3** port, T265 → **USB2** port (T265 stačí USB2).
Po kroku 1 (`dwc3-host`) jsou funkční **oba** USB3-A porty.

---

## ⚠️ Důležité poznámky

- **Dual-homing / ESET:** NIKDY nemít zapojený ethernet (`.25`) a WiFi (`.24`)
  zároveň na stejné podsíti! Jeden stroj se dvěma IP/MAC na jednom segmentu
  spustí na PC ESET „Útok ARP Cache Poisoning" a zablokuje komunikaci.
  → Provozovat **buď** WiFi (kabel vytažený), **nebo** ethernet.
  → Když musíš mít obojí: `sudo sysctl -w net.ipv4.conf.all.arp_announce=2`
    a `net.ipv4.conf.all.arp_ignore=1` (a uložit do `/etc/sysctl.d/`).
- **USB porty:** dva USB3-A porty (host + OTG→host po overlayi), dva USB2-A.
  D435 patří na USB3, T265 stačí USB2. (3. USB3 SoC řadič `fcd00000` je
  natrvalo disabled — sdílí PHY s onboard ethernetem, nech být.)
- **Konce řádků:** skript editovaný/uložený na Windows může mít CRLF →
  `sed -i 's/\r$//' setup-orangepi.sh`.
- **Vypnutí:** `sudo poweroff` (vzdáleně už NEZAPneš — jen fyzicky).
  Restart: `sudo reboot`.
- **IP adresy** jsou z DHCP (mohou se měnit). WiFi `192.168.88.24`,
  ethernet `192.168.88.25`. Hostname: `orangepi5-ultra`.
  Pro stálost zvážit rezervaci na routeru.
- **`sudo`** — pokud byl zapnut NOPASSWD (`/etc/sudoers.d/010-ales-nopasswd`),
  jde sudo bez hesla; jinak interaktivní příkazy z konzole nebo přes `ssh -t`.
- **Drobnost:** dhd načítá nvram `ap6611s` místo správného `AP6275P`
  (`/lib/firmware/ap6275p/`). Nevadí funkčnosti, ale je to nepřesné.
