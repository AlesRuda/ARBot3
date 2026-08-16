# Snímek obrazovky a videozáznam okna (toolbar)

Toolbar pod hlavním menu s tlačítky **Snímek**, **● MP4**, **● GIF** a ikonou **složky** — ruční
pořízení PNG snímku hlavního okna a spuštění/zastavení videozáznamu. Slouží k doložení chování UI
(deníček, popis chyby, ukázka jízdy ve World view) bez externího nahrávacího nástroje.

Po uložení se vedle hlášky objeví **jméno souboru jako odkaz** — kliknutí ho otevře v přidružené
aplikaci (prohlížeč obrázků / přehrávač), celá cesta je v tooltipu. **Ikona složky** otevře
`doc/media/` ve správci souborů a poslední soubor v ní rovnou označí.

Doplňuje bezobslužnou cestu ze [selftest.md](selftest.md) — ta umí totéž, ale jen z příkazové řádky
(`selftest=true st_shot=true st_video=true`) a s ukončením aplikace na konci. Toolbar dává tytéž
schopnosti interaktivně, za běhu, kdykoli.

## Kde to je v kódu

| Soubor | Role |
|---|---|
| [Views/MainWindow.axaml](../Src/ARBot/Views/MainWindow.axaml) | toolbar (`Border` + `StackPanel` pod `Menu`; Avalonia nemá `ToolBar` control) |
| [ViewModels/MainWindowViewModel.Capture.cs](../Src/ARBot/ViewModels/MainWindowViewModel.Capture.cs) | příkazy, stav tlačítek, hláška o průběhu |
| [Diagnostics/ScreenRecorder.cs](../Src/ARBot/Diagnostics/ScreenRecorder.cs) | vlastní záznam: časovač, snímkování, volba kodéru |
| [Diagnostics/FfmpegPipe.cs](../Src/ARBot/Diagnostics/FfmpegPipe.cs) | běžící ffmpeg, do jehož stdin tečou surové BGRA snímky |
| [Diagnostics/ScreenCapture.cs](../Src/ARBot/Diagnostics/ScreenCapture.cs) | render vizuálu do bitmapy / PNG (sdílené se self-testem) |
| [Diagnostics/ShellOpen.cs](../Src/ARBot/Diagnostics/ShellOpen.cs) | otevření souboru / složky v OS (Windows `explorer /select`, Linux `xdg-open`) |
| [Diagnostics/GifWriter.cs](../Src/ARBot/Diagnostics/GifWriter.cs) | vestavěný GIF zapisovač — fallback, když není ffmpeg |

## Kam se ukládá

Do `doc/media/` (stejná složka jako self-test, `SelfTest.MediaDir()`), s časovým razítkem:

- `shot-RRRRMMDD-HHMMSS.png`
- `rec-RRRRMMDD-HHMMSS.mp4` / `.gif`

Tyto názvy jsou v [doc/media/.gitignore](media/.gitignore) — jde o pracovní výstupy. **Co má zůstat
v deníčku, přejmenuj na popisný název** (pravidlo „nový záznam = nový soubor" v hlavičce
[devlog.md](devlog.md)). Cesta k uloženému souboru se vypíše vedle tlačítek i do Debug output.

## Parametry záznamu

Nejsou v UI (toolbar má být jednoduchý), jsou to konstanty v `ScreenRecorder`:

| | fps | max. šířka | limit délky |
|---|---|---|---|
| **mp4** (H.264, crf 23, yuv420p) | 15 | 1280 px | 10 min |
| **gif** (paletizovaný) | 8 | 800 px | 60 s |
| **gif bez ffmpegu** (vestavěný) | 8 | 800 px | ~37 s (300 snímků) |

Po dosažení limitu se záznam **sám zastaví** a uloží (`AutoStopRequested`) — aby zapomenuté nahrávání
nezaplnilo disk ani paměť. Zbývající čas je vidět v hlášce vedle tlačítek.

Proč je GIF omezenější: `palettegen` potřebuje celý stream, takže si ho ffmpeg drží v paměti; a GIF
je i tak řádově větší soubor než H.264. **Pro delší záznamy používej mp4.**

## Jak to funguje (a proč zrovna takhle)

**Snímkuje se na UI vlákně** — Avalonia vizuál se jinde renderovat nedá. Časovač běží
na `DispatcherPriority.Background`, aby snímkování nepředbíhalo vlastní vykreslování a vstup.

**Surové snímky tečou rovnou do ffmpegu** (`FfmpegPipe`, `-f rawvideo -pixel_format bgra` na stdin),
ne přes PNG soubory v dočasné složce jako dávkové `Ffmpeg.EncodeMp4/EncodeGif` v self-testu. Rozdíl je
podstatný: záznam může běžet libovolně dlouho, paměť i disk zůstávají konstantní a na UI vlákně zbyde
jen kopie pixelů (žádné PNG kódování). Zmenšení a barevný převod dělá ffmpeg.

**Zápis do roury má vlastní vlákno a frontu s pevnou kapacitou** (8 snímků). Když ffmpeg nestíhá,
snímek se **zahodí** (počítá se v hlášce jako „zahozeno") — UI se nikdy nezablokuje. Buffery se
recyklují přes pool; bez toho by šlo o megabajty alokací na snímek (LOH).

**Rozměr se zafixuje při startu** a zarovná na sudý (vyžaduje `yuv420p`) — kodér neumí měnit rozměr
za běhu. Zvětší-li se okno během záznamu, video zůstane na původním výřezu. Ve fallbacku bez ffmpegu
se snímky s jiným rozměrem zahazují (GIF vyžaduje shodné snímky).

**ffmpeg není závislost projektu** — hledá se za běhu (`Ffmpeg.Find()`: `ARBOT_FFMPEG` → PATH →
Shotcut / winget / `C:\ffmpeg\bin`). Bez něj:

- **GIF** funguje přes vestavěný `GifWriter` (snímky se drží v paměti → kratší limit, horší komprese);
- **MP4** nejde vůbec — tlačítko to řekne v hlášce.

## Stav ověření

Ověřeno **na Windows/x64 za běhu aplikace** (tlačítka odkliknuta přes UI Automation): PNG snímek,
GIF i MP4 záznam včetně přepínání popisků tlačítek, zamykání druhého formátu během záznamu
a průběžné hlášky. Výsledný mp4 zkontrolován ffmpegem (1280x642, h264, 15 fps). Ověřen i odkaz na
soubor (otevřel se prohlížeč obrázků) a tlačítko složky (otevřel se explorer).

**Neověřeno:** běh na Armbianu/OrangePI (jiná cesta k ffmpegu — použij `ARBOT_FFMPEG`; `Ffmpeg.Find()`
navíc hledá jen `ffmpeg.exe`, takže na Linuxu je proměnná nutná; a `ShellOpen` tam potřebuje
`xdg-open`) a fallback bez ffmpegu.
