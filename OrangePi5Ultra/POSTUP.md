# Orange Pi 5 Ultra — postup nastavení (po reinstalaci)

Dokumentace ke konfiguraci provedené v červnu 2026. Slouží k rychlé obnově
Pi do funkčního stavu po reinstalaci Armbianu.

- **Deska:** Orange Pi 5 Ultra (RK3588, GPU Mali-G610, WiFi čip AP6275P/SYN43711)
- **OS:** Armbian (vendor kernel 6.1.x, base Ubuntu 26.04 arm64), KDE Plasma na Waylandu
- **Uživatel:** `ales`
- **Síť:** vlastní AP `arbot` (`192.168.7.1`) + ethernet; podrobně krok 3 a 4
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
   Vyžádá si heslo pro AP `arbot`, heslo k WiFi (jen pro záložní klientský profil,
   viz krok 3) a heslo pro Samba účet.
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

### 3. WiFi: AP jede na `hostapd`, ne přes NetworkManager

Deska umí **buď** klienta, **nebo** AP, a k AP vede jen jedna cesta. Všechny tři
kombinace byly na desce vyzkoušeny 29. 8. 2026 — tohle není odhad:

| Cesta | Klient (STA) | AP (hotspot) |
|---|---|---|
| NM + backend `iwd` | **funguje** | **neumí** — `nmcli con up` skončí na `net.connman.iwd.InvalidArguments: Argument type is wrong` (NM volá `AccessPoint.Start()` s argumenty, které iwd nebere) |
| NM + backend `wpa_supplicant` 2.11 | **nefunguje** — `wl_set_multi_akm: Failed to set join_pref` → `CTRL-EVENT-ASSOC-REJECT`, navenek klamavé „WRONG_KEY" | **nefunguje** — AP sice vysílá a je vidět, ale klient je odkopnut: `WLC_E_DEAUTH_IND(6) reason=17`. `wpa_supplicant` sám v logu varuje: *„nl80211 driver interface is not designed to be used with ap_scan=2; this can result in connection failures"* |
| **`hostapd` mimo NM** | — | **funguje** — klient projde 4-way handshake i DHCP |

Příčina u klienta je nekompatibilita `wpa_supplicant` 2.11 s Rockchip ovladačem
`bcmdhd` (FullMAC); u AP to, že AP režim ve `wpa_supplicant` je náhražka a pro
nl80211 je určený `hostapd` (což je i to, co s tímhle Broadcom čipem dělá Android).
**Robot proto jede na `hostapd`** (krok 4) a `wlan0` je z NetworkManageru vyjmuté.

> **Slepá ulička, do které nechoď znovu:** `nmcli con add ... 802-11-wireless.mode ap`
> vypadá funkčně — profil se založí, `iw dev wlan0 info` hlásí `type AP`, SSID je
> v seznamu sítí vidět. Teprve klient zjistí, že se nepřipojí. Vypnutí PMF
> (`wifi-sec.pmf 1`) odstraní z logu chybu `wl_cfg80211_external_auth`, ale
> `reason=17` zůstává — **nepomůže to**.

Návrat do role klienta (když se robot potřebuje připojit na cizí WiFi): vypnout
`hostapd` a `arbot-ap-net`, smazat `/etc/NetworkManager/conf.d/unmanaged-wlan0.conf`,
přepnout backend zpět na `iwd` a zapnout profil `VatNet`:
```bash
sudo systemctl disable --now hostapd arbot-ap-net
sudo rm /etc/NetworkManager/conf.d/unmanaged-wlan0.conf
printf '[device]\nwifi.backend=iwd\n' | sudo tee /etc/NetworkManager/conf.d/wifi_backend.conf
sudo systemctl disable --now wpa_supplicant; sudo systemctl enable --now iwd
sudo systemctl restart NetworkManager
sudo nmcli con modify VatNet connection.autoconnect yes && sudo nmcli con up VatNet
```
Heslo k VatNet musí být v `/var/lib/iwd/VatNet.psk` (viz krok 4).

**Autostart:** WiFi (SDIO) se registruje až ~8–12 s po startu, takže démon spuštěný
dřív nenajde `wlan0`. Drop-in, který na zařízení počká, má `hostapd` (krok 4)
i `iwd` (`/etc/systemd/system/iwd.service.d/wait-for-wlan.conf`).

### 4. Síťové profily (AP `arbot`, ethernet, klient VatNet)

Cílový stav pro soutěž: **robot vystavuje vlastní WiFi** (žádný router není),
a velká data se stahují kabelem. Každá cesta má vlastní podsíť, takže nikdy nejsou
dvě adresy v jedné podsíti (viz poznámka o ESET níže).

#### AP `arbot` — `hostapd` + vlastní `dnsmasq`

`/etc/hostapd/hostapd.conf` (**`chmod 600`**, obsahuje heslo — proto není v repu):
```
interface=wlan0
driver=nl80211
ssid=arbot
hw_mode=g
channel=6
ieee80211n=1
wmm_enabled=1
auth_algs=1
wpa=2
wpa_key_mgmt=WPA-PSK
rsn_pairwise=CCMP
country_code=CZ
ieee80211d=1
wpa_passphrase=HESLO
```
`wlan0` pryč z NetworkManageru — `/etc/NetworkManager/conf.d/unmanaged-wlan0.conf`:
```ini
[keyfile]
unmanaged-devices=interface-name:wlan0
```
Drop-in `/etc/systemd/system/hostapd.service.d/wait-for-wlan.conf` (počkat na SDIO
zařízení a restartovat při pádu):
```ini
[Unit]
After=sys-subsystem-net-devices-wlan0.device
Wants=sys-subsystem-net-devices-wlan0.device
[Service]
Restart=on-failure
RestartSec=5
```
Adresu, DHCP a NAT dělá `/etc/systemd/system/arbot-ap-net.service` — `hostapd` sám
žádné IP nenastavuje:
```ini
[Unit]
Description=ARBot AP: adresa, DHCP a NAT pro wlan0 (192.168.7.0/24)
Requires=hostapd.service
After=hostapd.service
[Service]
Type=simple
ExecStartPre=/sbin/ip addr replace 192.168.7.1/24 dev wlan0
ExecStartPre=/sbin/ip link set wlan0 up
ExecStartPre=/sbin/sysctl -qw net.ipv4.conf.wlan0.forwarding=1
ExecStartPre=/bin/sh -c '/sbin/iptables -t nat -C POSTROUTING -s 192.168.7.0/24 ! -d 192.168.7.0/24 -j MASQUERADE 2>/dev/null || /sbin/iptables -t nat -A POSTROUTING -s 192.168.7.0/24 ! -d 192.168.7.0/24 -j MASQUERADE'
ExecStart=/usr/sbin/dnsmasq --keep-in-foreground --interface=wlan0 --bind-interfaces \
  --listen-address=192.168.7.1 --dhcp-range=192.168.7.10,192.168.7.254,1h \
  --except-interface=lo --no-hosts --dhcp-authoritative --dhcp-leasefile=/var/lib/misc/arbot-ap.leases
Restart=on-failure
RestartSec=5
[Install]
WantedBy=multi-user.target
```
```bash
sudo systemctl unmask hostapd && sudo systemctl enable --now hostapd
sudo systemctl enable --now arbot-ap-net
```
`--except-interface=lo` tam **musí být** — jinak si dnsmasq vezme i `127.0.0.1:53`.
`country_code=CZ` ovladač přijal (`iw reg get` → `country CZ: DFS-ETSI`), takže se
tím zároveň řeší jinak výchozí world doména `country 00`.

#### Ethernet — dva profily, rozhoduje priorita
```bash
sudo nmcli con add type ethernet ifname enP3p49s0 con-name eth-dhcp \
  ipv4.method auto ipv4.dhcp-timeout 10 ipv6.method ignore \
  connection.autoconnect yes connection.autoconnect-priority 100 \
  connection.autoconnect-retries 1
sudo nmcli con add type ethernet ifname enP3p49s0 con-name eth-direct \
  ipv4.method shared ipv4.addresses 192.168.66.1/24 ipv6.method ignore \
  connection.autoconnect yes connection.autoconnect-priority 50
sudo nmcli con delete 'Wired connection 1'
```
- `eth-dhcp` (priorita 100) — v místní síti vezme adresu z DHCP a robot má internet.
- `eth-direct` (priorita 50) — když DHCP do 20 s nepřijde, NM spadne sem a **robot
  se sám stane DHCP serverem**. Na soutěži tedy stačí strčit kabel do notebooku,
  ten dostane `192.168.66.x` a robot je na `192.168.66.1`. Nic se nenastavuje ručně.
  **Ověřeno 29. 8. 2026** přepojením kabelu do notebooku.
- ⚠️ **Sdílené připojení nesmí klientovi vnucovat výchozí bránu ani DNS.** `ipv4.method=shared`
  posílá v DHCP nabídce i volby 3 (router) a 6 (DNS) — notebook pak dostane **druhou výchozí
  trasu** přes robota, Windows si ji vybere (drátový adaptér má nižší metriku než WiFi)
  a **přijde o internet**, protože v režimu `eth-direct` robot žádný uplink nemá.
  Léčba je `/etc/NetworkManager/dnsmasq-shared.d/no-default-route.conf`:
  ```
  dhcp-option=3
  dhcp-option=6
  ```
  Prázdná hodnota znamená „neposílat". NM ten adresář předává sdílenému `dnsmasq`
  jako `--conf-dir`. Platí pro všechna sdílená připojení NM; AP `arbot` se to netýká
  (má vlastní `dnsmasq` v `arbot-ap-net.service`, a tam brána smysl dává — přes robota
  jde mobil na internet, když má robot uplink kabelem).
- **Jak dlouho ten pád trvá — a proč tak vypadají ty parametry.** Původně (`dhcp-timeout 20`,
  `retries 2`, `ipv6.method auto`) trval **74 s** od startu, než byl ethernet použitelný.
  Zkrácení `dhcp-timeout` samo nestačilo: profil čeká i na **IPv6 RA (~30 s)**, který ten
  IPv4 timeout přebije, takže pokus stál 32 s. Teprve `ipv6.method ignore` to srazilo na
  **12 s** (změřeno 29. 8. 2026). IPv6 tu k ničemu není, SSH i apt jedou po IPv4.
- **Cena za to zkrácení:** v síti s pomalým DHCP (STP na managed switchi apod.) může robot
  spadnout na `eth-direct`, i když měl dostat adresu — pak nemá internet a NM se sám zpátky
  nevrátí. Léčba je `sudo nmcli con up eth-dhcp`.

#### Klient VatNet (jen pro backend `iwd`, viz krok 3)
```bash
sudo nmcli con add type wifi con-name VatNet ifname wlan0 ssid VatNet \
  wifi-sec.key-mgmt wpa-psk wifi-sec.psk 'HESLO' connection.autoconnect no
printf '[Security]\nPassphrase=HESLO\n' | sudo tee /var/lib/iwd/VatNet.psk
sudo chmod 600 /var/lib/iwd/VatNet.psk
```

> ⚠️ **Past: systém je řízený netplanem, ne čistým NM.** `nmcli con add` zapíše
> YAML do `/etc/netplan/` a NM keyfile se z něj generuje do `/run` (názvy
> `netplan-NM-…`). U profilů v režimu AP přitom netplan zahazoval `ipv4.method: shared`
> při **každém** zápisu (`con add` i `con modify`; u ethernetu ho zachová) — AP pak
> vysílalo, ale klient nedostal adresu. Od přechodu na `hostapd` se to AP netýká,
> ale u ethernetových profilů kontroluj po každé změně
> `sudo grep ipv4.method /etc/netplan/90-NM-*.yaml` a případně doplň do `passthrough:`
> plus `sudo netplan generate`.

> ⚠️ **Restart NetworkManageru nechá viset jeho `dnsmasq`.** Osiřelý proces drží
> `192.168.66.1:53`, nová instance se tam nedostane a `eth-direct` pak cyklí
> („getting IP configuration"). Léčba: `sudo pkill -f 'dnsmasq.*--clear-on-reload'`
> a `sudo nmcli con up eth-direct`.

**Ověření po rebootu:** `nmcli device status` → `wlan0 unmanaged`
a `enP3p49s0 connected` (podle prostředí `eth-dhcp` nebo `eth-direct`);
`systemctl is-active hostapd arbot-ap-net` → `active active`.
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
**Zapojení kamer — D435 na USB3, T265 na USB2, a POZOR na huby.** Po kroku 1
(`dwc3-host`) jsou funkční **oba** USB3-A porty. D435 smí být buď přímo v desce, nebo
za **jedním napájeným** hubem; co ji rozbije, je **řetěz dvou hubů za sebou**.

> ⚠️ **Řetěz dvou hubů v cestě D435 kamery rozbije, a projeví se to matoucími příznaky.**
> Změřeno 30. 8. 2026, když obě D435 visely za řetězem dvou USB3 hubů (druhý byl napájený):
> viewer hlásil u D435 „no frames received" a **T265 nezobrazil vůbec**, což svádělo na
> chybějící podporu T265. Ve skutečnosti se `rs-enumerate-devices` zaseklo
> (`futex_wait_queue`) dřív, než se na T265 dostalo. V logu byly
> `failed to claim usb interface, RS2_USB_STATUS_NO_DEVICE/IO` → `acquire_power failed`,
> v `dmesg` opakované `USB disconnect` obou kamer plus `reset SuperSpeed` hubu — **bez
> jediné chybové hlášky kernelu** (žádný nadproud, žádné `-71`/`-110`).
> Selhávala pokaždé **jiná** kamera, takže to nebyl vadný kus, ale souboj o zdroj při
> současné inicializaci. Jednotlivá kamera přitom streamovala v pořádku
> (`rs-hello-realsense` vracel vzdálenost) — to je dobrý test, když se zdá, že „nic nejede".
>
> **Měření (`rs-bench`, 5 běhů `rs-enumerate-devices`, počítá se výpis všech tří kamer
> a návratový kód):**
>
> | Zapojení | Úplných běhů |
> |---|---|
> | obě D435 za řetězem dvou hubů | **0 / 5** |
> | jedna přímo, druhá za řetězem | **0 / 5** |
> | obě přímo na kořenových portech | **10 / 10** |
> | **obě za jedním napájeným hubem** | **10 / 10** |
>
> V obou funkčních zapojeních navíc **nula** událostí odpojení/připojení v `dmesg`
> a nula chyb kernelu.
>
> **Závěr: nevadí hub, vadí dva huby za sebou.** Robot dnes jede na jednom napájeném
> hubu, protože přímé zapojení do desky se do konstrukce nevešlo.
> Obraz ve vieweru na tomhle zapojení funguje (potvrzeno 30. 8. 2026). `rs-bench` ale testuje
> jen otevření kamer, **ne propustnost** souběžného streamu přes sdílenou linku hubu.
> Diagnostický skript zůstal na desce jako `/usr/local/sbin/rs-bench`.
>
> ⚠️ **Nové (1. 9. 2026): jeden port hubu měl vadné spojení — a poznalo se to podle `-71`.**
> Pravá D435 (USB sériové číslo `828313020236`) na **portu 4** hubu nepřežila USB reset:
> ```
> usb 2-1.4: device not accepting address 4, error -71
> usb 2-1.4: Device not responding to setup address.    (a tak dál pro adresy 5, 6, 7, 8)
> usb 2-1.4: USB disconnect, device number 4
> ```
> `-71` je `EPROTO`. Kamera nedokázala ani přijmout adresu, kernel to zkusil pětkrát a vzdal to;
> rebind hubu skončil `unable to enumerate USB device`. Softwarově se vrátit **nedala**.
> Po fyzickém přepojení (**táž kamera do jiného portu hubu**) se vyčetla na plných 5 Gbps a
> obě kamery pak streamovaly 40 s na 30 fps bez jediné chyby. **Vada tedy jde za portem 4
> nebo za kabelem, který v něm byl — ne za kamerou.**
>
> **Jak to odlišit od dřívějšího problému s huby:** ten se projevoval **bez jediné chybové
> hlášky kernelu** (viz odstavec výše — žádné `-71`/`-110`). Když v `dmesg` je `-71`, je to
> **fyzická vrstva** (kabel, konektor, port), a hledat to v librealsense nebo v propustnosti
> je ztráta času.
>
> **Co bylo naopak změřeno jako zdravé** (1. 9. 2026, mimo aplikaci, `rs`-úrovní i skutečným
> driverem): dvě D435 na jednom hubu **120 s i 375 s na 30/30 fps, nula timeoutů**. Přidání
> **T265** je měřitelně zhorší, ale nezasekne: `CLEAR_HALT` vyskočí z 1 na ~72 a prvních
> ~100 s kolísají na 20–30 fps. Zásek pozorovaný v aplikaci po ~75 minutách běhu se **takto
> reprodukovat nepodařilo** — na jeho přežití je v driveru záchrana, viz
> [decisions.md](../doc/decisions.md), 1. 9. 2026.
>
> **Pozor při diagnostice: USB reset kamery není nevinný.** Na zdravém zařízení je rutina
> (zůstane vyčtené), tady vyhodil kameru ze sběrnice natrvalo a následný unbind/rebind hubu
> dostal do nefunkčního stavu i druhou kameru. Z toho se dostane jen fyzickým přepojením.
>
> ⚠️ **Nové (2. 9. 2026): kamery mohou po bootu naskočit jen na USB 2.0 — a pak nejedou vůbec.**
> Po restartu se obě D435 vyčetly jako `new high-speed USB device` (`speed=480`,
> `Usb Type Descriptor: 2.1`), přestože předchozí den jely na 5 Gbps a **s kabely se nemanipulovalo**.
> Hub sám byl na USB3 sběrnici vyčtený na 5000M, takže linka deska↔hub byla v pořádku — nenaskočily
> SuperSpeed linky **hub↔kamera**. Léčba: **fyzické odpojení a připojení kamer** (`new SuperSpeed
> USB device`, `speed=5000`), pak obě streamují 30/30 fps.
>
> **Proč to není vidět jako chyba:** kernel si ani jednou nestěžoval — žádné „Cannot enable. Maybe
> the USB cable is bad?", žádné `-71`, žádný nadproud. Hub SuperSpeed zařízení **vůbec nedetekoval**.
> Je to tedy jiná signatura než vada portu z 1. 9. (tam `-71`).
>
> **Proč to bolí až takhle:** na USB 2.0 **nejde hloubka a barva zároveň**. Změřeno na jedné kameře:
> Z16 480×270@30 **+** barva 640×480@30 se nepodaří vyřešit ani jako RGB8, ani jako YUYV; jen barva
> jede (23,8 fps), jen hloubka se otevře ale dodá 0 snímků. Aplikace proto neohlásí **ani jednu**
> kameru — vypadá to jako porucha obou, ne jako rychlost linky.
>
> **Pozor na dvě mylná vysvětlení, na která se dá naletět:**
> 1. *„Nedovřený konektor"* — SS kontakty leží v zásuvce hlouběji, takže by to obraz vysvětlovalo.
>    **Vyvráceno:** konektory byly zasunuté nadoraz a nikdo s nimi nehýbal.
> 2. *„Nestačí propustnost na dvě kamery"* — **vyvráceno:** selže i jedna kamera samotná.
>
> **Ještě jedna zavádějící hláška:** librealsense vrátí `failed to set power state`, když je na
> zařízení zapnutý USB **autosuspend** (`power/control=auto`, senzor `suspended`). Po
> `echo on > .../power/control` zmizí a objeví se ta skutečná chyba (nesplnitelná kombinace
> formátů). Instalovaná realsense udev pravidla autosuspend **neřeší**.
>
> **Otevřené:** jak často to nastává, není změřeno (jeden boot dal 5 Gbps, jiný ne; žurnál
> historii bootů nedrží).
>
> ⚠️ **Při měření rozlišuj TEPLÝ RESTART od STUDENÉHO STARTU** (postřeh autora, 2. 9. 2026).
> Při `reboot` zůstane hub i kamery **napájené** a USB3 PHY se inicializuje jinak než při zapnutí
> ze studena, kdy se rozbíhá celý napájecí a resetovací sled. Je dost možné, že se jev váže právě
> na **zapnutí**, ne na reboot — takže série teplých restartů, které projdou, **nedokazuje nic**.
> Měřit se to musí obojím způsobem a výsledky držet zvlášť.
>
> Kdyby se to potvrdilo, léčba je **softwarová obdoba přepojení**: hub hlásí „Per-port power
> switching" a jeho porty mají v sysfs `disable`
> (`/sys/bus/usb/devices/1-1:1.0/1-1-port2/disable`), takže port jde odpojit a zapnout **bez
> instalace čehokoli**, ještě než se spustí aplikace. Vyzkoušet to ale nejdřív ručně — než se to
> pověsí na start, musí být jisté, že se kamera vždycky vrátí.

> **Slepé uličky, do kterých nechoď znovu:** není to napájení (kernel nehlásil nadproud
> a smyčka běžela jen za běhu librealsense) ani `uvcvideo` (odpojení jeho rozhraní,
> a dokonce i celého zařízení od `usb` ovladače, dalo jen 1 úspěšný běh z 5 — náhoda,
> ne oprava). `uvcvideo` je v tomhle kernelu `builtin`, takže ho stejně nelze odebrat.

### 10. Managed wrapper Intel.RealSense (2.53) pro .NET aplikaci/testy
Krok 9 buildí jen **nativní** `librealsense2.so`. Managed C# wrapper
(`Intel.RealSense.dll`) se **NEbuildí přes cmake** — `BUILD_CSHARP_BINDINGS` je
Visual-Studio-only (`VS_DOTNET_TARGET_FRAMEWORK_VERSION`). Místo toho je v solution
projekt **`Src/ThirdParty/Intel.RealSense`** (zdroje wrapperu **v2.53.1** z
`librealsense/wrappers/csharp`, kompilované rovnou do `net10.0`), aby verze managed
wrapperu **přesně odpovídala** nativní `librealsense2.so`.

> **Proč shoda verzí:** `rs.h` říká, že interface-compatible jsou jen rozdíly v
> *patch* úrovni. Wrapper 2.47 (Windows) proti native 2.53 (Pi) je rozdíl v *minor*
> → mimo garantovanou zónu (riziko „api version mismatch" nebo tichého ABI driftu
> struktur `rs2_intrinsics`/`extrinsics`). Proto **platform-dedikovaný HAL**:
> `ARBot.HALArmbian.D435Camera` s wrapperem **2.53**, `ARBot.HALWindows.D435Camera`
> zůstává na **2.47** (odpovídá Windows native).

Nativní naming řeší `ARBot.HALArmbian/RealSenseNativeResolver.cs` (mapuje
`realsense2`/`realsense2d` → `librealsense2.so`).

**POZOR:** při upgradu nativní `librealsense` na Pi je nutné aktualizovat i zdroje
wrapperu v `Src/ThirdParty/Intel.RealSense`, aby verze zůstaly shodné.

**Spuštění D435 integračního testu na Pi** (kamera na USB3):
```bash
cd ~/arbot/ARBot.HAL.Tests
dotnet test -c Debug --filter Category=Hardware
```
Bez připojené kamery se test gracefully přeskočí (`Assert.Ignore`).

---

## ⚠️ Důležité poznámky

- **Dual-homing / ESET (historické, od 29. 8. 2026 neaktuální):** dokud byl robot
  klientem VatNet, měl ethernet `.25` i WiFi `.24` v jedné podsíti — jeden stroj se
  dvěma IP/MAC na jednom segmentu spustí na PC ESET „Útok ARP Cache Poisoning" a
  zablokuje komunikaci. Současné rozdělení podsítí (AP `192.168.7.x`, kabel napřímo
  `192.168.66.x`, místní síť z DHCP) to vylučuje konstrukcí. Kdyby se robot vracel
  do role klienta: provozovat **buď** WiFi, **nebo** ethernet, případně
  `sudo sysctl -w net.ipv4.conf.all.arp_announce=2` a
  `net.ipv4.conf.all.arp_ignore=1` (a uložit do `/etc/sysctl.d/`).
- **USB porty:** dva USB3-A porty (host + OTG→host po overlayi), dva USB2-A.
  D435 patří na USB3, T265 stačí USB2. (3. USB3 SoC řadič `fcd00000` je
  natrvalo disabled — sdílí PHY s onboard ethernetem, nech být.)
- **Konce řádků:** skript editovaný/uložený na Windows může mít CRLF →
  `sed -i 's/\r$//' setup-orangepi.sh`.
- **Vypnutí:** `sudo poweroff` (vzdáleně už NEZAPneš — jen fyzicky).
  Restart: `sudo reboot`.
- **IP adresy** (od 29. 8. 2026, viz krok 4). Hostname: `orangepi5-ultra`
  (mDNS `orangepi5-ultra.local`, avahi běží).
  - **AP `arbot`** — robot `192.168.7.1`, klienti `192.168.7.10–254`. Pevné.
  - **Ethernet v místní síti** — z DHCP, tedy proměnlivé (naposledy `192.168.88.25`);
    tady se vyplatí mířit na hostname, ne na adresu. Pro stálost zvážit rezervaci na routeru.
  - **Ethernet napřímo do notebooku** — robot `192.168.66.1`, notebook `192.168.66.x` z DHCP. Pevné.
- **`sudo`** — pokud byl zapnut NOPASSWD (`/etc/sudoers.d/010-ales-nopasswd`),
  jde sudo bez hesla; jinak interaktivní příkazy z konzole nebo přes `ssh -t`.
- **Drobnost:** dhd načítá nvram `ap6611s` místo správného `AP6275P`
  (`/lib/firmware/ap6275p/`). Nevadí funkčnosti, ale je to nepřesné.
