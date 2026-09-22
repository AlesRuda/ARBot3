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
| **mp4** (H.264, crf 23, yuv420p) | 15 | 1280 px | **žádný** |
| **gif** (paletizovaný) | 8 | 800 px | 60 s |
| **gif bez ffmpegu** (vestavěný) | 8 | 800 px | ~37 s (300 snímků) |

U GIFu se po dosažení limitu záznam **sám zastaví** a uloží (`AutoStopRequested`); zbývající čas je
vidět v hlášce vedle tlačítek. **U mp4 limit od 22. 9. 2026 není** — do té doby tam bylo 10 minut
jako pojistka proti zapomenutému nahrávání, ale technický důvod to nemělo: snímky tečou přes
`FfmpegPipe` rovnou do kodéru, takže paměť je konstantní a roste jen soubor na disku (při 15 fps,
1280 px a crf 23 řádově stovky MB za hodinu). Zrušeno kvůli sestříhání 15minutového záznamu ze
soutěže a hodinového záznamu z maratonu. U GIFu limit **zůstat musí**: `palettegen` potřebuje celý
stream, takže si ho ffmpeg drží v paměti.

### ⚠️ Délka videa odpovídá ZÁZNAMU, ne tomu, jak dlouho ho aplikace přehrávala

ffmpeg dostává `-framerate 15` a **věří mu** — každý přijatý snímek považuje za 1/15 s. Snímkování
ale běží na `DispatcherPriority.Background`, takže se při vytížení UI tiky zpozdí; hotové video pak
bylo **kratší než skutečnost a jelo zrychleně**. Změřeno: 10 snímků za 3,26 s reálného času dalo
video **0,667 s** (= 10/15), tedy skoro pětinásobné zrychlení.

**Řídící veličinou je od 22. 9. 2026 časová osa, ne počet tiků.** `ScreenRecorder.Timeline` říká,
kde na ní zrovna jsme, a na sekundu té osy odejde přesně `fps` snímků:

- posunula se **míň** než o snímek → snímek se vůbec nepořizuje, jen se čeká;
- posunula se o **víc** → chybějící se doplní kopiemi (jiná data pro ten úsek osy nejsou).

**V režimu View je tou osou čas ZÁZNAMU** (`FileMessageSource.ReplayTime`), ne stopky. To není
detail: `ReplayPacing.RealTime` sice čeká na razítka záznamu, ale **když nestíhá, zpoždění
nedohání** — přehrávání je reálný čas *nebo pomalejší*. Podle stopek by tedy patnáctiminutový
záznam dal delší video. V režimu Run zdroj souboru neexistuje, `Timeline` zůstane `null` a měří se
stopkami, což je tam správně.

**Proč ne `-use_wallclock_as_timestamps`.** Nabízí se nechat razítka na ffmpegu (`-vsync vfr`)
a ověřeně to funguje — v témže pokusu vyšlo video 2,934 s místo 0,667 s při nezměněných 10 snímcích.
Jenže to odpovídá času **aplikace**, ne záznamu, takže to řeší jinou otázku. Surové video v rouře
žádná razítka nenese, takže vlastní čas snímku ffmpegu předat nejde — odtud převzorkování na naší
straně. ⚠️ **Novější ffmpeg s tím nepomůže**, není to otázka verze.

**Snímková frekvence se bere ze záznamu** (`FileMessageSource.FrameRate`, počítá se z indexu, tedy
bez čtení snímků): víc snímků za sekundu, než kolik jich záznam nese, není z čeho vzít a musely by
se duplikovat; míň by zahazovalo data. Nad `records/Robotour2026/Kolo2.rec` vyjde **8,5 sn/s**
(997 snímků / 2 kamery / 58,6 s), takže výchozích 15 fps tam vymýšlelo skoro polovinu snímků.
Frekvence se už jen **snižuje** pod výchozí hodnotu formátu — nad ni nemá smysl jít, tolik snímků
se stejně nepořídí. ⚠️ Počítá se **maximum přes kamery**, ne prostý počet `CameraFrame`: kamery jsou
dvě a každá posílá vlastní snímek, takže by frekvence vyšla **dvojnásobná** a video by běželo
dvakrát rychleji.

⚠️ Na jeden tik se doplní nejvýš 30 snímků (2 s): po delším zaseknutí by se jinak do fronty
o kapacitě 8 hrnuly stovky snímků, většina by se stejně zahodila a jen by to přidusilo kódování ve
chvíli, kdy je stroj beztak vytížený. ⚠️ Kopie musí jít do **nového bufferu** — `WriteFrame`
přebírá vlastnictví a po zápisu ho vrací do poolu.

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
