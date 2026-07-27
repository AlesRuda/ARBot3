# Deník rozhodnutí (decisions log)

Chronologický záznam **netriviálních rozhodnutí** na projektu — hlavně to, co by se jinak
„zahrabalo" a čeho se v kódu nedá vyčíst *proč*. Slouží jako sdílená paměť napříč sezeními
i lidmi (viz [CLAUDE.md](../CLAUDE.md), pravidlo „vše v repozitáři").

**Jak přispívat:** nové rozhodnutí přidej **nahoru** do sekce „Rozhodnutí" jako krátký blok:
*co* se rozhodlo, *proč* (kontext / alternativy), *důsledky* a *odkazy* (soubory, doc).
Absolutní datum (ne „minulý týden"). Detailní doménovou dokumentaci nech v příslušném
`doc/*.md`; sem patří jen rozhodnutí + odůvodnění + odkaz.

---

## Rozhodnutí

### 2026-07-25 — `Blob` → `ImageMsg`; obraz jako `Image`, bez `BlobType`/`Data`; komprese v serializaci
Původní `Blob` (BlobType + syrové `Data` + lazy JPEG) přejmenován na **`ImageMsg`** a přepracován:
nese přímo netypový **`Common.Image`** (pixel typ = identita, `PixelTypeName`), `Data` a `BlobType`
zrušeny. Serializaci obrazu řeší statické `ImageMsg.Write(bw, Image, Compression)` /
`ReadImage(bw)` (rekonstrukce přes `Image.Create` z uloženého názvu typu), komprese
`None/Deflate/Jpeg/Png` je per-zpráva ve vlastnosti `Comp`. Vizuální „druh" (`LayerKind`
Color/Probability/Depth) se v `MessageImageLayers` odvozuje z pixel typu (BGR32/RGB/BGR→Color,
Gray→Probability, Gray16→Depth) místo dřívějšího `BlobType`.
- **Proč:** čistší model (obraz je obraz, ne generický blob dat), self-popisný záznam a
  volitelná komprese na jednom místě; odstranění duplicitní identity (BlobType vs pixel typ).
- **Enablery:** netypový base `Common.Image` (z něj dědí `Image<T>`) + `Image.Create(name,w,h)`.
- **Rozsah:** aktivní cesta (`BackProjectProcessor`, `MessageImageLayers`, `ImageDocument`,
  katalog, recording limit `"ImageMsg"`, `ARBot.Record`) převedena; legacy `ToLogMessage`
  (LocalMap/GridNavigation…) převedeny na `Image<Gray>`; mrtvé/nekompilované ARBot2 soubory
  (Driver, MessageQueue komentář) ponechány. Testy převedeny, build 0 chyb, Common 200 / HAL 12.

### 2026-07-25 — Verzování zpráv: `Message.Verze` + větvení `FromData` podle uložené verze
Každá `Message` nese verzi formátu, ve kterém vznikla (`Message(name, verze)`). Rámec záznamu
verzi ukládá (`MessageWriter`: `MsgName:délka:Verze`) a `MessageReader` ji před `FromData` nastaví
na uloženou hodnotu. Pravidlo: `ToData` píše vždy aktuální layout; `FromData` větví podle
`this.Verze` a starší formát namigruje do aktuálního modelu; **při každé změně obsahu zprávy se
verzní konstanta zvedne** a přidá se čtecí větev pro předchozí verzi.
- **Vynuceno typem:** `SensorStateBase(int verze)` verzi **vyžaduje** (nemá bezparametrický ctor),
  takže každý senzorový stav musí předat svou konstantu (konvence `public const int FormatVersion`).
- **Proč:** dopředná kompatibilita — starý `.rec` musí jít přehrát i po změně zpráv.
- **Důsledek:** princip a I/O tok rozepsány v [record-replay.md → Verzování zpráv](record-replay.md).
  Dle tohoto principu je od 2026-07-25 hotová i serializace `CameraFrame` (`FormatVersion`,
  `FromData` větví podle `Verze`); surové framy se ale defaultně nezaznamenávají (limit 0, RGB je v
  záznamu jako JPEG `Blob`).

### 2026-07-25 — Run rozdělen na „Run without log" / „Run and log"; jméno záznamu `yyyyMMdd-HHmmss.rec`
Menu **Runtime** má dvě varianty spuštění: bez záznamu a se záznamem. „Run and log" pojmenuje
výstup automaticky `yyyyMMdd-HHmmss.rec` ve složce **`records/` v kořeni repa** (sidecar index
`.rec.idx` řeší runtime; složka se vytvoří). Kořen se hledá směrem nahoru přes marker `.git`
(`MainWindowViewModel.RepoRootOrBase`), fallback = `AppContext.BaseDirectory` (nasazení bez repa,
např. na Pi). `records/` je v `.gitignore` (velké binární logy se necommitují).
- **Proč:** dřívější „Run" volal `Start(Mode.Run)` bez cesty → runtime nenahrával. Uživatel chce
  vědomou volbu a bezklikové logování s časovým razítkem; záznamy mít na stabilním místě (ne pod
  `bin`, které se maže při Clean).
- **Důsledek:** `MainWindowViewModel.RunAndLog` + `RepoRootOrBase`, menu **Runtime → Run and log**;
  cesta se vypíše do Debug output. Přehrání přes **Runtime → View…**.

### 2026-07-25 — Paměť/poznatky výhradně v repu, žádná externí paměť
Poznatky, poznámky a rozhodnutí se ukládají jen do repa (`doc/*.md`, README, komentáře v kódu).
Externí „memory" úložiště harnessu (`~/.claude/…`) se **nepoužívá** — je mimo git a nejde sdílet
s týmem. Tento soubor vznikl jako „catch-all" na rozhodnutí, která nezapadají do konkrétního
doménového docu.
- **Proč:** potenciální spolupráce více lidí; CLAUDE.md se navíc čte na začátku každého sezení,
  takže repo je zároveň paměť napříč sezeními.
- **Důsledek:** CLAUDE.md = rozcestník „vždy v kontextu"; detaily v `doc/` (načítají se při práci
  v dané oblasti). Viz [CLAUDE.md](../CLAUDE.md).

### 2026-07-25 — Backpressure UI dokumentů: „latest-wins + Background flush" (povinný vzor)
Dokumenty přijímající data z `MeasurementArived` / `IMessageSink.Post` nesmí postovat na UI
vlákno každou zprávu — jen uloží nejnovější (starší zahodí) a koalescovaně naplánují jeden
`Flush` na `DispatcherPriority.Background`.
- **Proč:** producent (kamera ~30 Hz, IMU/motor ~100 Hz, backproject) přetékal dispatcher frontu
  → UI zamrzalo a zpracovávalo staré framy („stall → dávka stovek Hz → zpět"). `RelaySource`
  fan-out běží na vlákně producenta a nemá frontu, takže odběratel musí být neblokující.
- **Důsledek:** aplikováno v `CameraDocument`, `D435TestDocument`, `IMUDocument`, `GpsDocument`,
  `MotorControlDocument`, `ImageDocument` (dict pending per zdroj); `DebugOutputTool` obdobně.
  Vzor a šablona kódu: [Src/ARBot/Views/README.md](../Src/ARBot/Views/README.md).

### 2026-07-25 — DebugOutputTool: virtualizovaný list řádků místo jednoho `string`
Debug/Trace výstup drží `ObservableCollection<string>` zobrazenou virtualizovaným `ListBox`em
(dřív jeden velký `string` v `TextBox`).
- **Proč:** velký `TextBox` se při každé aktualizaci celý přeskládával (`BidiData` na UI vlákně)
  a s délkou logu ztrácel responzivitu.
- **Důsledek:** koalescované dávkové přidávání + ořez s hysterezí (`MaxLines`); render jen
  viditelných řádků. Soubor `Src/ARBot/ViewModels/DebugOutputTool.cs`.

### 2026-07-25 — Řídicí smyčka + UART odolné vůči nedostupným portům
Časovač `ControlLoop.Pump` má reentrancy guard (`Interlocked`), `Uart.ReOpen` je neblokující
(timestamp backoff místo `Thread.Sleep`), blokující čtení jde přerušit přes `IUart.CancelRead`
a `SensorBase.Process` má idle-backoff.
- **Proč:** při nedostupných COM portech blokoval `Drive()` ~3 s v `ReOpen` a `System.Threading.Timer`
  callbacky se překrývaly → exploze vláken (~180) a zamrznutí UI; blokující `Read` navíc věsel
  `SensorBase.Stop()` (`task.Wait()`).
- **Důsledek:** soubory `Uart.cs`, `UartSensorBase.cs`, `SensorBase.cs`, `ARBotRuntime.cs`,
  `SDC2160Ex.cs`/`SDC2160.cs`; test `ARBot.HAL.Tests/UartCancelReadTests.cs`. `Stop()` senzoru
  nejdřív nastaví `stopRequired`, pak `CancelRead()` (pořadí kvůli race).

### 2026-07-25 — Znovuotevírání dokovacích nástrojů přes sdílený `ReopenTool`
Nástroje (Sensors overview, Debug output) mají v `DockFactory` stabilní referenci a v menu
příkaz, který je znovuotevře přes společný `MainWindowViewModel.ReopenTool` (ošetřuje stavy
pinned/hidden/odpojený).
- **Proč:** `DebugOutputTool` se po zavření nedal znovu otevřít (nikde nedržená reference).
- **Důsledek:** `DockFactory.DebugOutput`, menu **Tools → Debug output**.

### 2026-07-25 — Nativní knihovna se staví CMakem a NENÍ v gitu
`NativeFuncs/bin/NativeLib.dll` (a `libNativeLib.so`) jsou build artefakty CMake, ne git.
Nesmí se mazat spolu s `bin`/`obj` — `ARBot.Common.csproj` je pro x64 kopíruje bez `Exists`
guardu, takže jinak build padá (`MSB3030`).
- **Proč:** zjištěno při čištění `bin/obj` (omylem smazána `NativeLib.dll`).
- **Důsledek:** postup rebuildu (vcvars + `cmake --preset windows-x64`) v
  [doc/build-and-platforms.md](build-and-platforms.md).

---

## Dříve učiněná rozhodnutí (kanonicky v doc/ nebo CLAUDE.md)

Rozhodnutí z dřívějška, jejichž odůvodnění je už rozepsané jinde — zde jen jako rozcestník
(přesná data viz git historie):

- **Build jen pod konkrétní platformou (x64 / OrangePI), ne AnyCPU** — kvůli nativním
  závislostem (Intel.RealSense). → [build-and-platforms.md](build-and-platforms.md), [CLAUDE.md](../CLAUDE.md)
- **Vlastní MSBuild platforma `OrangePI`** (ne `ARM64` = Windows-on-ARM, ne RID) a solution
  `.slnx` místo `.sln`. → [build-and-platforms.md](build-and-platforms.md)
- **Platformově dedikovaný HAL** (`HALWindows` 2.47 / `HALArmbian` 2.53, stejný namespace). →
  [architecture.md](architecture.md), [build-and-platforms.md](build-and-platforms.md)
- **Souřadnicové konvence:** world ENU + matematická orientace, body FLU. →
  [imu-and-frames.md](imu-and-frames.md)
- **EKF senzorická fúze** (přepis na generický `Ekf` → `EKFModel`, async replay). →
  [ekf-fusion.md](ekf-fusion.md)
- **Pipeline zpráv pro záznam/přehrávání** (`MessageSource`/`Target`, role, taps). →
  [record-replay.md](record-replay.md)
- **Při migracích nemazat starou/zakomentovanou implementaci, dokud ji nepotvrdí testy.** →
  [CLAUDE.md](../CLAUDE.md)
- **Jazyk: čeština** (komunikace, komentáře, dokumentace). → [CLAUDE.md](../CLAUDE.md)
