# Nasazení ARBota na Orange Pi (systemd + stínová kopie)

Řídicí runtime běží na zařízení jako služba `arbot`. Po zapnutí robota aplikace nastartuje,
rozjede senzory a **stojí**, dokud jí člověk nevybere misi na stránce náhledu — a to jde jen
při **drženém nouzovém zastavení**. Robot se tedy sám nikdy nerozjede, ani po restartu služby.

Podrobnosti a rozhodnutí: [doc/headless.md](../doc/headless.md),
[doc/plan-headless-provoz.md](../doc/plan-headless-provoz.md).

## Rozvržení na zařízení

| cesta | co to je |
|---|---|
| `~/arbot` | **datový adresář** (`dataroot=`): `config/`, `OSM/`, `records/`, `logs/`, `arbot.lock` |
| `~/arbot-headless` | **cíl nasazení** — binárky, `stin.sh`, `arbot.service`, `libNativeLib.so` |
| `~/arbot-headless-run` | **stínová kopie**, ze které se běží; obnovuje ji `stin.sh` při každém startu |

Proč stínová kopie: běžící .NET binárku nejde přepsat (assembly jsou memory-mapped → `ETXTBSY`),
takže se nasazuje do adresáře, který je zapisovatelný i za běhu, a aplikace běží z kopie.
**Restart služby = nasazení nové verze.**

## První instalace

```bash
# 1) binárky (z Windows, z kořene repa)
.\deploy\nasad.ps1 -NoRestart

# 2) jednotka (na Pi)
sudo cp ~/arbot-headless/arbot.service /etc/systemd/system/
sudo systemctl daemon-reload
sudo systemctl enable --now arbot
```

Profil `config/pi-provoz.cfg` musí být v datovém adresáři (`~/arbot/config/`). **Od 6. 9. 2026
ho tam dostane `nasad.ps1` sám** — viz níž.

## Běžné nasazení

```bash
.\deploy\nasad.ps1
```

Publikuje s razítkem verze, nahraje přes `tar` + `scp` (na Pi není rsync) a restartuje službu.
Verze je pak vidět v hlavičce stránky a v `logs/crash-*.log`, takže jde poznat, která binárka běží.

### Profily a mapy (od 6. 9. 2026)

Skript nasazuje i **`config/` a `OSM/`**, ale **do datového adresáře `~/arbot`** — tedy tam,
odkud je aplikace čte (`dataroot=`). Vedle binárek záměrně nejsou: dvě kopie týchž map by jen
matly, která se vlastně používá.

Do té doby se nenasazovaly vůbec a musely se kopírovat ručně — a poznalo se to až tím, že se
změna v profilu na robotu neprojevila, ačkoli skript hlásil úspěch. Přesně to se stalo
s `gpsposstd=30`.

⚠️ **Repo vyhrává:** soubor ručně upravený na robotu se přepíše. Skript proto před kopií
**vypíše, které soubory se liší**, aby ruční úprava nezmizela potichu:

```
prepisuji soubory, ktere se na robotu LISI od repa (rucni upravy zmizi):
     config/pi-provoz.cfg
```

Vypnout to jde `-NoData`; jiný cíl `-DataDir`. **Záznamy a logy v `~/arbot` se netýkají** —
rozbaluje se jen `config/`, `OSM/` a `models/`.

Kontrola, že se profil opravdu projevil: v účinné konfiguraci (v záznamu, `ARBot.Analyze log`)
musí být u klíče původ `(profil)`.

### Modely segmentace a runtime NPU (od 7. 9. 2026)

Aby šlo na robotu zapnout `backproject=nn` (síť na CPU) nebo `backproject=npu` (NPU), musí tam
být dvě věci navíc. Obojí nasazuje `nasad.ps1` sám:

| co | kam | proč tam |
|---|---|---|
| `models/*.onnx`, `models/*.rknn` | `~/arbot/models/` (datový adresář) | `nnmodel=` / `npumodel=` se řeší proti `dataroot=`, takže výchozí `models/Model61.1.rknn` sedí bez zadávání cesty |
| `librknnrt.so` | `~/arbot-headless/` (vedle binárek) | `DllImport` ji hledá u aplikace; do stínové kopie ji dostane `stin.sh` |

**Z `models/` se posílá jen to, co robot čte** — zdrojové `.tflite`, `.h5`, 50snímková testovací
sada a převodní skripty zůstávají v repu. **Všechny `.onnx`/`.rknn` varianty se posílají schválně**:
dá se pak A/B měřit přímo na robotu bez dalšího nasazování. Runtime knihovna NPU je binárka třetí
strany a v gitu **je** (`Src/ThirdParty/RKNN`), ze stejného důvodu jako RealSense DLL: bez ní
nejde nasadit funkční celek. Podrobnosti a verze:
[Src/ThirdParty/RKNN/README.md](../Src/ThirdParty/RKNN/README.md).

⚠️ **Verze `librknnrt.so` musí odpovídat toolkitu, kterým se `.rknn` model převedl** (obojí
2.3.2). Mění se proto naráz.

Ověřeno 7. 9. 2026 celým řetězem: po `nasad.ps1` a restartu služby najde runtime model na výchozí
cestě a obě kamery nastartují na NPU (v logu `jadro NPU maska 1` / `2`).

⚠️ **Po `-NoRestart` je ve stínové kopii ještě STARÁ binárka.** `stin.sh` ji obnovuje až při
**startu služby**, takže kdo po nasazení spustí runtime ručně z `~/arbot-headless-run`, měří něco
jiného, než co právě nasadil. Naběhnuto 7. 9. 2026 při testu `camerafps=` — parametr se „neprojevil",
ačkoli kód byl správně. Buď restartuj službu, nebo pusť `~/arbot-headless/stin.sh` ručně.

## Provoz

```bash
systemctl status arbot            # běží?
journalctl -u arbot -f            # co říká
sudo systemctl stop arbot         # zastavit a nechat stát
sudo systemctl start arbot        # znovu (a nasadit, co leží v ~/arbot-headless)
```

Stránka: `http://<ip>:8080/` — stav, snímek kamery, půdorys, výběr mise, zastavení.
Adresy robota: AP `arbot` → `192.168.7.1`, kabel napřímo → `192.168.66.1`.

## Diagnostika VN100 (`vnprobe.sh`)

Read-only výpis registrů IMU z živého senzoru — pro porovnání s referenčním exportem
`vn100-2026-7-8-nastavei z arbot2.sencfg` v kořeni repa. Vzniklo 6. 9. 2026, když se ukázalo,
že kurz robota je o 59° vedle a ze záznamu už nešlo zjistit proč (registry v něm nejsou).

```bash
scp deploy/vnprobe.sh ales@192.168.66.1:/tmp/ && ssh ales@192.168.66.1 'chmod +x /tmp/vnprobe.sh'
ssh ales@192.168.66.1 'sudo systemctl stop arbot; /tmp/vnprobe.sh /dev/ttyUSB0; sudo systemctl start arbot'
```

⚠️ **Službu je nutné zastavit** — jinak port drží ona (skript to pozná přes `fuser` a odmítne
běžet). ⚠️ Skript posílá **jen `VNRRG` (čtení)**; žádný zápis (`VNWRG`) ani uložení do flash
(`VNWNV`) v něm záměrně není — konfigurace železa se mění vědomě a ručně.

Co našel: [doc/imu-and-frames.md](../doc/imu-and-frames.md) — heading mode `Relative` místo
`Absolute` a vymazaná kalibrace magnetometru.

Obnovu dělá **`vnrestore.sh`** — ten na rozdíl od `vnprobe.sh` **zapisuje** a ukládá do flash
(`VNWNV`), takže změna přežije vypnutí. Dělící čára mezi těmi dvěma skripty je záměrná:
konfigurace železa se mění vědomě, ne vedlejším účinkem diagnostiky.

```bash
ssh ales@192.168.66.1 'sudo systemctl stop arbot; /tmp/vnrestore.sh /dev/ttyUSB0 --mag; sudo systemctl start arbot'
```

Bez `--mag` obnoví jen heading mode (reg 35 → `Absolute`); s `--mag` i kalibraci magnetometru
(reg 23) z exportu. ⚠️ Ta kalibrace je **z ARBot2 a rok stará** — ber ji jako provizorium,
dokud se nezměří nová otáčením robotu.

## Na co narazit

- **`libNativeLib.so` není v publishi** (kříží se ve WSL). Skript ji doplní z datového adresáře;
  když chybí i tam, Run spadne hned při startu na `DllNotFoundException`.
- **Návratové kódy**: `0` řádné ukončení, `2` vadná konfigurace, `3` už běží jiná instance,
  jinak pád (viz `~/arbot/logs/crash-*.log`). Kódy 2 a 3 jednotka **nerestartuje** —
  opakovat je do nekonečna by jen zaplavilo journal.
- **Smazané soubory zůstanou ve stínové kopii** (`cp` neumí mazat, rsync na Pi není). Po
  přejmenování nebo odebrání assembly:
  ```bash
  sudo systemctl stop arbot && rm -rf ~/arbot-headless-run && sudo systemctl start arbot
  ```
- **Ruční spuštění vedle běžící služby** skončí kódem 3 — drží to zámek `~/arbot/arbot.lock`.
  Nejdřív `sudo systemctl stop arbot`.
- **Tlačítko „Zastavit robota" na stránce ukončí proces**, ale služba ho za 5 s vrátí (a mezitím
  obnoví stínovou kopii). Kdo chce robota nechat stát, dá `systemctl stop arbot`.
