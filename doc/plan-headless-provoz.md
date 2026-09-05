# Headless v provozu — verze, výběr mise z webu, systemd a nasazení (fáze 4)

> **Pro agentní pracovníky:** plán se plní **task po tasku**, kroky mají checkboxy (`- [ ]`).
> Každý task končí zeleným buildem a testy pod `x64`. **Nekomitovat bez pokynu autora**
> (viz [CLAUDE.md](../CLAUDE.md)).

**Cíl:** Z headless runtime udělat něco, co se dá na zařízení skutečně **provozovat**: robot se
zapne, sám nastartuje aplikaci, **stojí a čeká, až mu člověk z mobilu vybere misi**, a ze stránky
je na první pohled poznat, **jaká verze aplikace to je** a **na co se právě čeká**.

**Proč:** dnešní stav ([headless.md](headless.md)) umí jen „ssh + příkaz s `mission=` na příkazové
řádce". Na soutěži to znamená, že u každého startu musí být člověk s notebookem a ssh; a když se
podíváš na stránku, nepoznáš, jestli na Pi běží ta binárka, kterou jsi před chvílí nasadil, nebo ta
předchozí.

Předchozí fáze: [plan-runtime-headless.md](plan-runtime-headless.md) (1 a 2),
[plan-headless-web.md](plan-headless-web.md) (3).

## Rozhodnutí autora (5. 9. 2026)

Tahle fáze **otáčí dvě dřívější rozhodnutí** ze 4. 9. 2026. Obojí je vědomé a důvod je zapsaný:

1. **systemd ano** — jednotka se `enable`, aplikace startuje po bootu. Zapnu robota a z webu mu dám
   příkaz, bez dalšího. Restart se povoluje (`Restart=always`), **protože restartovaný proces se
   sám nerozjede**: skončí ve fázi čekání na misi. Původní důvod zákazu („robot, který se sám znovu
   rozjede, je horší než robot, který stojí") tím zůstává splněný — jen ho nově drží dvoufázový
   běh místo neexistence jednotky.
2. **Misi lze vybrat z webu**, ačkoli původní rozhodnutí říkalo „rozjet robota z webu nikdy nepůjde,
   pak by bylo nutné heslo". Přístup **chrání heslo k WiFi** a navíc platí pojistka: **misi jde
   vybrat jen při stisknutém nouzovém zastavení**, takže rozjede robota vždy až člověk stojící
   u něj tím, že stop uvolní. Web tedy misi *nastaví*, nespustí.

Dál platí:

- **Bez mise se nejezdí** — fáze čekání nabízí jen výběr mise, ne „jeď bez mise".
- Stránka vybírá zatím **misi**, ne konfigurační profil. Zbytek je v profilu na příkazové řádce
  jednotky. (Výběr profilu je zvažovaná budoucnost, viz [Co se sem vědomě nedělá](#co-se-sem-vědomě-nedělá).)
- **Verze musí být v crash logu** a musí se zvedat s každým nasazením.
- Nasazení přes **stínovou kopii**: skript odkopíruje binárky bokem a spustí je odtamtud, takže
  původní adresář zůstane přepisovatelný i za běhu. **`records/`, `logs/` a `config/` se čtou
  a píšou v původním adresáři**, ne ve stínové kopii.
- Aplikace se dělá **první**, nasazování až nakonec.

## Globální omezení

- **Build pro konkrétní platformu, NE `AnyCPU`**: `x64` (Windows, testy), `OrangePI` (Armbian/ARM64).
- **Jazyk: čeština** — kód, komentáře, dokumentace, jména testů.
- **Žádný nový NuGet balík.** Verzování se řeší MSBuildem, ne `MinVer`/`Nerdbank.GitVersioning`.
- `ARBot.Runtime` nesmí získat referenci na Avalonii (hlídá test).
- **Diagnostika poruch do `Trace`, ne do `Debug`.**
- **Čas přes `TimeBase.Now`.** Jediná výjimka v téhle fázi je **systémový čas v hlavičce stránky**
  a datum buildu — to je kalendářní údaj pro člověka, tedy přesně ten případ, který CLAUDE.md
  povoluje. Doba běhu se naopak počítá z `TimeBase`.
- **Náhled nesmí zabránit robotovi jet** ani ho rozjet bez pojistky.

## Než začneš

**Výchozí stav:** poslední commit `2316de1` (fáze 3), pracovní strom čistý. Baseline testů pod `x64`:
`ARBot.Common.Tests` **1 155**, `ARBot.Runtime.Tests` **36**.

**Přečti napřed:** [CLAUDE.md](../CLAUDE.md), [headless.md](headless.md),
[configuration.md](configuration.md) (registr parametrů, `ParamStore`, precedence),
[record-replay.md](record-replay.md) (životní cyklus `Start/Stop`, verzování zpráv),
[robotour-mission.md](robotour-mission.md) a [mission-freerun.md](mission-freerun.md).

### Co dnes platí (zjištěno čtením kódu 5. 9. 2026)

- **Mise se vybírá uvnitř `ARBotRuntime.Start`** — `ARBotRuntime.cs:607`, `switch` nad
  `ParamRegistry.Mission.Value` při skládání grafu. Mimo Start se mise založit nedá.
- **Před Runem není o hardwaru známo vůbec nic.** Skutečné senzory zakládá `ARBotHW.SetRealHW`,
  které volá až `ARBotRuntime.Start`, a zdroje se rozbíhají na `ARBotRuntime.cs:778`
  (`foreach (var s in sources) s.Start()`). `ARBotHW.Init` jen **zjistí porty a kamery**.
  ⚠️ **Z toho plyne celý návrh dvoufázového běhu níž:** stav nouzového zastavení chodí jako
  `MotorStateBase.IsEmergencyStop`, tedy zprávou ze stupně, který před Runem neběží. Bez Runu se
  gate na e-stop postavit nedá.
- **`Start` je opakovatelný:** `Start(Mode, file)` na začátku volá `Stop()`, když už běží, a zvedá
  `SessionId` (odběratelé si podle něj zahodí obsah). UI to tak dělá běžně (Run → Stop → Run).
- **Parametry se čtou živě** z `ParamStore.Current` (`Param.Raw`), setter `Current` je privátní
  (`ParamStore.cs:45`, nastavuje se v `Build`). `ParamOrigin` má tři hodnoty: `Default`, `File`,
  `CommandLine`.
- **Seznam misí je v registru:** `ParamRegistry.Mission.Def.AllowedValues` = `none, freerun, robotour`
  (`ParamRegistry.cs:120`), s komentářem „když přibude mise, patří i sem". Stránka z toho může
  seznam vzít, takže nevznikne druhý zdroj pravdy.
- **Verze dnes žádná není.** `Directory.Build.props` neexistuje, `ARBot.Common.csproj` má natvrdo
  `AssemblyVersion 1.0.0.0`, repo **nemá gitové tagy** (`git describe` selže).
- **`CrashLog` píše do `AppContext.BaseDirectory/logs`** (`CrashLog.cs:72`) a `CrashLog.Install()`
  běží **před** `RuntimeBootstrap.TryConfigure`, tedy dřív, než jsou známé parametry.
- **`RepoPaths` má malý dopad** — mimo testy jen `Program.cs:31`, `ConfigurationDocument`
  (5 míst, UI), `Param.cs:107` (`PathParam.Value`), `ParamStore.cs:72,191`
  a `ARBotRuntime.cs:193,195` (cesta záznamu). Na zařízení není `.git`, takže základem je adresář
  aplikace.
- **Žádný zámek jedné instance neexistuje** (grep na `Mutex`/pidfile nic nenajde).
- ⚠️ **`mission=robotour` se v headless nikdy nerozjede** (nalezeno 5. 9. 2026 díky řádku stavu):
  `RobotourMission.StartMission()` volá **jediné místo v celém repu** — tlačítko „Start mise"
  v UI panelu (`RobotourMissionDocument.cs:228`). V headless tedy mise vznikne, zůstane v `Idle`
  a robot stojí navěky — zatímco úvodní řádek hlásí „POZOR: mise je zapnuta - robot se rozjede bez
  dalsiho pokynu", což pro Robotour **není pravda** (pro FreeRun ano, ta se rozjede sama).
  Řeší to Task 4: výběr mise z webu musí u Robotour `StartMission()` zavolat.
- **Mise nemají společného předka** a `RobotourMission.cs:22` to výslovně odůvodňuje. Ten komentář
  mluví o **řídicí** ose („obě produkují cíl, ale každá na jinou vrstvu") — tenhle plán zavádí
  **rozhraní pro hlášení stavu**, což je jiná osa a s tím rozhodnutím není v rozporu. Komentář se
  ale musí upřesnit, aby se příště nečetl jako zákaz.
- **`WebStatus`** drží poslední zprávu každého druhu, kreslí až na požadavek a na `ARBotHW` sahá jen
  přes `HasCurrent`. Stránka je jeden `const string` v `WebStatus.cs` a obnovuje se každou sekundu
  z `/status.json`.
- **Záznam je velký:** 476 MB za 25 s běhu (měřeno 4. 9. 2026), tedy ~19 MB/s.

### Pasti, na které se tady narazí

1. **Nahrávat fázi čekání na misi je vyloučené.** Při ~19 MB/s by deset minut čekání znamenalo
   ~11 GB. Záznam proto začíná **až misí** (viz návrh) — a je to i logičtější: jeden `.rec` = jedna
   mise.
2. **Verze v assembly atributech = rebuild celého řešení při každém buildu.** Kdyby se číslo
   odvozovalo z času vždy, přepisoval by se `AssemblyInfo` a překládalo by se všechno pořád dokola
   (a testovací smyčka by se zpomalila). Proto se **razítkuje jen na pokyn** (`-p:ArbotStamp=true`),
   jinak je verze `0.0.0.0-dev`.
3. **Datum buildu nebrat z časového razítka PE hlavičky** — moderní SDK staví deterministicky
   a razítko je hash, ne čas. Musí se vložit explicitně jako text.
4. **`CrashLog` neví, kam psát, dokud se nepřečte konfigurace.** Pořadí `Install()` → `TryConfigure`
   je záměrné a nemění se; adresář logu se proto **dováže dodatečně**.
5. **`.NET` binárku běžícího procesu nejde přepsat** (assembly jsou memory-mapped → `ETXTBSY`).
   Proto stínová kopie; `scp` přímo na běžící soubory selže, `rsync` sice projde (píše přes dočasný
   soubor a přejmenuje), ale běžící proces si dál drží starý inode.
6. **Dvě instance nikdo nehlídá.** Až poběží jednotka a ty se přes ssh připojíš a pustíš aplikaci
   ručně, sáhne druhá instance na tytéž UARTy a kamery. Port náhledu se ošetří sám („bez nahledu"
   a jede se dál) — a **to je právě ta zákeřná varianta**: stránka ukazuje první instanci, zatímco
   ovládat můžeš druhou.
7. **Virtuální nouzové zastavení není v headless čím stisknout.** Panel *Tools → Virtuální senzory*
   je v UI; v headless by se gate na e-stop nedal na Windows vůbec vyzkoušet.
8. **`mission=` se musí dostat do záznamu pravdivě.** Účinná konfigurace se vypisuje jednou po
   složení `ParamStore`; kdyby se mise předala Startu „bokem", zůstalo by v záznamu `mission=none`.

## Návrh

### A. Dvoufázový běh (jádro celé fáze)

```
proces start
   ├─ bootstrap konfigurace, zámek instance, web server
   ├─ WaitReady() + HwSettleMs
   ├─ FÁZE A: Start(Run) s mission=none, BEZ ZÁZNAMU
   │     robot stojí (žádný producent mrkve → žádná mrkev → LocalNavigator nemá kam jet)
   │     stránka: senzory, kamera, půdorys, a VÝBĚR MISE
   │     výběr povolen jen když MotorStateBase.IsEmergencyStop == true a zpráva je čerstvá
   ├─ POST /mission {mission=freerun|robotour}
   │     → override v ParamStore (origin Runtime) → Trace s účinnou konfigurací
   │     → Start(Run, cesta záznamu) — Start si sám udělá Stop() a postaví graf s misí
   └─ FÁZE B: mise běží; robot se rozjede až po uvolnění nouzového zastavení
         POST /stop → Stop() → konec procesu (systemd ho vrátí do fáze A s novými binárkami)
```

**Proč fáze A vůbec běží (a nečeká se jen tak):** bez Runu není znám stav nouzového zastavení
(viz zjištění výše), takže by nebylo na čem gate postavit. Navíc je to funkce, ne cena — před
vypuštěním robota je stránka se **senzory a snímkem kamery** přesně to, co člověk chce vidět.

**Záznam** začíná až fází B (past 1). Na zařízení se v jednotce používá `record=true`, tedy
`records/yyyyMMdd-HHmmss.rec` — s pevnou cestou by každý restart přepsal předchozí záznam.

**Mise nejde vybrat dvakrát:** další `POST /mission` ve fázi B se odmítne (409) s hláškou; změna mise
znamená zastavit a vybrat znovu.

### B. Gate na nouzové zastavení

Výběr mise je povolen, když poslední `MotorStateBase` **hlásí stisknutý stop** a **není starší 3 s**
(týž práh jako u „ticha" senzorů na stránce). Stránka tlačítka nedisabluje potichu — píše důvod:

| stav | co stránka řekne |
|---|---|
| e-stop stisknutý, zpráva čerstvá | výběr mise povolen |
| e-stop uvolněný | „nejdřív stiskni nouzové zastavení" |
| žádná / stará zpráva od motorů | „motory nehlásí stav — misi nelze vybrat" |

Gate se vyhodnocuje **i na serveru**, ne jen v JavaScriptu: `POST /mission` bez stisknutého stopu
vrátí 409. Klientská kontrola je pohodlí, serverová je ta pojistka.

**Pro zkoušku na Windows** dostane stránka **při `virtualhw=true`** přepínač virtuálního nouzového
zastavení (obdoba červeného tlačítka v panelu *Tools → Virtuální senzory*). Se skutečným HW se
nezobrazí a endpoint ho odmítne — jinak by to byl přesně ten dálkový rozjezd, který tu nesmí být.

### C. Výběr mise a `ParamStore`

Nový původ hodnoty **`ParamOrigin.Runtime`** („zvoleno za běhu") a řízený zápis
`ParamStore.SetRuntimeOverride(klíč, hodnota)`, povolený **jen pro `mission`** (bílá listina —
mutovatelná globální konfigurace je jinak přesně to, čemu se registr vyhýbal).

Proč přes store a ne parametrem do `Start`: `mission` čte `ARBotRuntime.Start` z registru a účinná
konfigurace jde do záznamu. Kdyby se hodnota předala bokem, záznam by lhal (past 8). Po přepsání se
do `Trace` vypíše řádek „mise zvolena z webu: freerun" — a přes `TraceInfoBridge` se to dostane do
záznamu jako `Info`.

### D. Stav mise („na co se čeká")

Rozhraní v `ARBot.Common/Missions`:

```csharp
public interface IMissionStatus
{
    string MissionName { get; }     // "freerun" | "robotour" — týž tvar jako mission=
    string PhaseText { get; }       // krátký název fáze pro člověka
    MissionWait WaitingFor { get; } // na co se čeká; None = na nic
    TimeSpan Elapsed { get; }       // od startu mise, z TimeBase
}
```

`MissionWait` je **výčet** (ne text), aby přežil v záznamu a dal se filtrovat offline:
`None, GpsFix, EmergencyStopPressed, QrCode, EmergencyStopReleased, Route, Arrival`. Text pro
člověka dělá **jedna** převodní funkce, aby se stejná hláška nepsala dvakrát.

⚠️ **Do zprávy to nakonec nešlo** (rozhodnuto při psaní Tasku 2, návrh chtěl `MissionMsg` verze 7):
u Robotour je „na co se čeká" **čistá funkce fáze**, a tu `MissionMsg.Phase` už nese. Uložit vedle
ní odvozenou hodnotu = dva zdroje pravdy v záznamu, a platilo by to jen pro nové nahrávky. Převod
proto dělá `MissionStatusText` **u čtenáře** a bere `int`, takže se stejně volá nad živou misí i nad
přečtenou zprávou — a „na co se čekalo" jde dopočítat i pro **starší** záznamy. Formát zpráv se
nemění. Stránce dovolí ukázat misi a fázi i **dřív, než přijde první zpráva**,
`ARBotRuntime.CurrentMission`.

⚠️ Komentář v `RobotourMission.cs:22` („společná abstrakce misí se záměrně nezavádí") se **upraví**,
ne smaže: dál platí pro řídicí osu, nově se vymezí proti ose hlášení stavu.

### E. Verze a datum buildu

Nový `Src/Directory.Build.props`:

- `VersionPrefix` = `1.0` (ruční, mění člověk při velké změně).
- Při `-p:ArbotStamp=true`: `Version = 1.0.<dní od 2026-01-01>.<sekund od půlnoci / 2>`, tedy
  **standardní čtyřdílná .NET verze, která monotónně roste s každým buildem** (je to totéž, co
  kdysi dělalo `1.0.*`). Bez razítka `0.0.0.0` + `-dev` (past 2).
- `AssemblyInformationalVersion` = `1.0.248.31234+<krátký git hash>[-dirty] (2026-09-05 14:03 UTC)`.
  **Příznak `dirty` je tu podstatný** — nasazuje se běžně z rozpracované kopie a číslo commitu by
  jinak lhalo.

Čtení za běhu: `BuildInfo` v `ARBot.Runtime` (rozparsuje informational version jednou, dá
`Version`, `GitHash`, `IsDirty`, `BuildTimeUtc`, `Popis()`).

Kam to jde: **hlavička crash logu** (výslovný požadavek), **úvodní řádek headless**
(→ přes `TraceInfoBridge` i do záznamu), **`/status.json`** a hlavička stránky.

### F. Datový adresář (stínová kopie)

Nový parametr **`dataroot=<cesta>`** (typ cesta, výchozí prázdno = dnešní chování). Když je zadaný,
řeší se proti němu **všechny relativní cesty** — záznam, `logs/`, profily, mapy — místo
`RepoPaths.RootOrBase()`. Stínová kopie tak má jen binárky a data zůstanou v původním adresáři.

Fallback na adresář aplikace se **nedělá**: původní adresář je výstup `publish`, takže `config/`
i `OSM/` v něm jsou. Jeden zdroj, žádné hádání, kterou kopii mapy zrovna čteme.

`CrashLog` dostane `CrashLog.LogDirectory` (nastaví `RuntimeBootstrap` po `TryConfigure`); pád
**před** načtením konfigurace zůstane v adresáři aplikace, což je stínová kopie — to je přijatelné
a bude to v dokumentaci (past 4).

### G. Zámek jedné instance

Soubor `<dataroot>/arbot.lock` otevřený s `FileShare.None` po celý běh (na Unixu to .NET mapuje na
`flock`, takže po pádu procesu zámek padá s ním — žádné mrtvé pidfily). Když se nepovede zamknout:
hláška „už běží jiná instance (systemd? `systemctl status arbot`)" a **návratový kód 3**.

Zámek se bere **až po `TryConfigure`** (potřebuje `dataroot`) a **před** `ARBotHW`, aby druhá
instance nesáhla na porty.

### H. systemd jednotka a nasazení

```ini
[Unit]
Description=ARBot headless runtime
After=network-online.target
StartLimitIntervalSec=300
StartLimitBurst=5

[Service]
Type=simple
User=ales
WorkingDirectory=/home/ales/arbot
ExecStartPre=/home/ales/arbot/stin.sh
ExecStart=/usr/bin/dotnet /home/ales/arbot-run/ARBot.Headless.dll config=config/orangepi.cfg dataroot=/home/ales/arbot web=8080 record=true
Restart=always
RestartSec=5
RestartPreventExitStatus=2 3
TimeoutStopSec=30
KillSignal=SIGTERM

[Install]
WantedBy=multi-user.target
```

- `ExecStartPre=stin.sh` udělá **stínovou kopii** `~/arbot` → `~/arbot-run` (smaže a zkopíruje
  binárky). Tím platí: **každý restart procesu = nasazení nové verze**, a původní `~/arbot` jde
  přepisovat za běhu.
- `RestartPreventExitStatus=2 3` — vadná konfigurace (2) ani druhá instance (3) se restartem
  nespraví, restartovat je do nekonečna je jen zaplavený journal.
- `Restart=always` je bezpečné **jen díky fázi A**. Kdyby někdo někdy fázi A zrušil, musí zrušit
  i tohle.
- Milý vedlejší efekt: **„Zastavit robota" na stránce = nasazení nové verze** (proces skončí
  s kódem 0, systemd ho za 5 s vrátí a `ExecStartPre` mezitím překopíruje binárky). Kdo chce robota
  nechat stát, dá `systemctl stop arbot`.
- Logy: stdout už jde přes `ConsoleTraceListener`, takže `journalctl -u arbot -f` funguje bez další
  práce. **Ověřit vyprazdňování bufferu**, když stdout není terminál, ale roura do journalu.

Nasazovací skript z Windows (`nasad.ps1`): `dotnet publish -p:Platform=OrangePI -r linux-arm64
-p:ArbotStamp=true` → `rsync`/`scp` do `~/arbot` → `systemctl restart arbot`. `libNativeLib.so` se
**nekopíruje** (cross-kompiluje se ve WSL, viz [build-and-platforms.md](build-and-platforms.md)) —
skript ji jen zkontroluje a hlásí, když v cíli chybí.

## Tasky

### Task 1: Verze a datum buildu — **HOTOVO 5. 9. 2026**

- [x] `Src/Directory.Build.props`: `VersionPrefix`, razítkování pod `ArbotStamp` (viz návrh E),
      `AssemblyInformationalVersion` s git hashem, příznakem `dirty` a časem buildu.
      Git se volá přes `Exec` s `ContinueOnError` — **build musí projít i bez gitu** (na zařízení,
      v archivu bez `.git`), tehdy hash chybí a je to vidět.
- [x] `BuildInfo` v `ARBot.Runtime`: rozparsuje informational version vstupní assembly, vlastnosti
      `Version`, `GitHash`, `IsDirty`, `BuildTimeUtc`, `Popis()`; **nikdy nevyhodí výjimku** (na
      chybějící nebo divný atribut vrací „neznámá verze").
- [x] `CrashLog`: verze a datum buildu do hlavičky logu (dosavadní `Version()` deleguje na `BuildInfo`).
- [x] Verze do úvodního výpisu **obou** aplikací. Nakonec **jedním** řádkem v
      `RuntimeBootstrap.TryConfigure`, ne dvakrát v `Program.Main`: obě aplikace tudy stejně
      procházejí a řádek jde toutéž cestou jako konfigurace. Vypíše se **i při vadné konfiguraci** —
      je na profilu nezávislý a u hlášení „nenastartovalo to" je to první, co člověk potřebuje.
- [x] Testy (`ARBot.Runtime.Tests`, 52 celkem): rozbor informational version (s hashem, bez hashe,
      dirty, prázdný, nesmyslný, nečitelný čas), `Popis()`, verze v hlavičce crash logu.
      Dva dosavadní testy `RuntimeBootstrapTests` počítaly řádky výpisu — upraveny na nový počet.
- [x] Ověřeno: bez razítka `0.0.0.0-dev`, s `-p:ArbotStamp=true` `1.0.247.11684+2316de12-dirty
      (2026-09-05 06:29 UTC)`; dva razítkované buildy za sebou dají různá čísla; **dva buildy bez
      razítka nepřepíšou `AssemblyInfo.cs`**, tedy se nepřekládá celé řešení znovu (past 2).
      Build `x64` i `OrangePI` celého `ARBot.slnx`, `ARBot.Common.Tests` 1 174 zelených.
- [x] **⚠️ Nález navíc: účinná konfigurace se do záznamu nikdy nedostávala.**
      `RuntimeBootstrap.TryConfigure` ji vypisuje **před** startem runtime, ale `TraceInfoBridge`
      se připojuje až v `ARBotRuntime.Start` a **nic nebufferuje** — takže v `.rec` nebyl ani jeden
      z těch ~60 řádků, přestože to tvrdí [CLAUDE.md](../CLAUDE.md) i komentáře u výpisu. Léčba:
      verze a `ParamStore.Current.DescribeAll()` se **zopakují hned po `traceBridge.Attach()`**.
      Ověřeno nad skutečným záznamem z headless běhu (řetězce `ARBot verze:` i `virtualhw=true`
      jsou v `.rec`). Text v CLAUDE.md je na to potřeba upravit — je v Tasku 7.

### Task 2: Stav mise — `IMissionStatus` a „na co se čeká" — **HOTOVO 5. 9. 2026**

- [x] `IMissionStatus` a výčet `MissionWait` v `ARBot.Common/Missions` (návrh D).
- [x] Implementace na `RobotourMission`: mapování fází na `MissionWait`
      (`ArmingAtDepot` → `GpsFix`, servisní okno → `EmergencyStopPressed` / `QrCode` /
      `EmergencyStopReleased`, jízda → `Arrival`). **Žádná fáze nezůstala bez odpovědi** — hlídá to
      test nad všemi hodnotami výčtu. Hodnota `Route` z návrhu **nevznikla**: automat „hledání
      trasy" jako fázi nemá, trasu řeší vrstva pod misí.
- [x] Implementace na `FreeRunMission`. **`WaitingFor` je tam vždy `None`** a stav nese `PhaseText`
      (jede v koridoru / bez koridoru drží kurz / čeká na pózu). FreeRun totiž nemá stanoviště, kód
      ani operátora — kdyby se tam vecpal umělý „čeká na koridor", přestal by ten řádek na stránce
      znamenat „bez zásahu člověka se nic nestane", což je u Robotour jeho jediný smysl.
- [x] **⚠️ Odchylka od návrhu: `MissionMsg` ani `FreeRunMsg` se neverzovaly.** Návrh chtěl
      `WaitingFor` uložit do zprávy, aby „to skončilo v záznamu". Při psaní se ukázalo, že
      **u Robotour je to čistá funkce fáze**, a tu `MissionMsg.Phase` už nese — uložit vedle ní
      odvozenou hodnotu by znamenalo dva zdroje pravdy v `.rec` a platilo by to jen pro nové
      nahrávky. Převod proto dělá `MissionStatusText` (bere `int`, takže jde volat nad živou misí
      i nad přečtenou zprávou) a **„na co se čekalo" jde dopočítat i pro všechny starší záznamy**.
      Žádná změna formátu, tedy ani riziko pro čtení starých `.rec`.
- [x] `ARBotRuntime.CurrentMission` — **počítaná** vlastnost nad oběma typovanými (mise se vylučují),
      takže není co zapomenout vynulovat. Typované `FreeRunMission` / `RobotourMission` zůstaly (UI).
- [x] **Nález navíc:** `Start` mise **nenuloval**, takže po `Stop` + `Start` s jinou misí by zůstala
      viset předchozí a `CurrentMission` by hlásila obě. Doteď to nevadilo (druhý `Start` s jinou
      misí se nikde nedělal), s výběrem mise z webu (Task 4) by to vadilo hned. Opraveno.
- [x] Upřesněn komentář v `RobotourMission` (řídicí osa × osa hlášení stavu).
- [x] Testy (+21, `ARBot.Common.Tests` 1 195 zelených): mapování **všech** fází, shoda převodu
      z živé mise a z čísla ve zprávě, neznámá fáze z novějšího záznamu, průchod celou misí
      s kontrolou `WaitingFor` v každém kroku, „u výkladky se nečeká na kód", doba běhu z hodin dat.

### Task 3: Hlavička stránky a stav mise — **HOTOVO 5. 9. 2026**

- [x] `/status.json` má blok **`head`**: `version`, `git` (hash + `-dirty`), `build`, `uptime`,
      `now`, `mission`, `phase`, `missionElapsed`, `waiting`. Vlastní blok (a ne ploché klíče)
      proto, že tabulka na stránce vypisuje **všechno ostatní** z JSON automaticky — hlavičková
      pole by v ní jinak vyskočila podruhé.
- [x] `TimeBase.Uptime` a `TimeBase.Started` — doba běhu z monotonní základny, tedy bez skoku při
      synchronizaci hodin. `now` je naopak `DateTime.Now`: kalendářní čas pro člověka.
- [x] Hlavička nad lištou (verze, build, běží, čas) a pod ní **řádek stavu mise** s oranžovým
      „čeká se na: …". Bez mise píše „mise: žádná".
- [x] Mise se čte ze **živého** `ARBotRuntime.CurrentMission`, ne ze zprávy — stav je vidět hned
      a platí stejně pro obě mise (FreeRun žádnou `MissionMsg` neposílá). Na `ARBotRuntime.Current`
      se sahá jen přes `HasCurrent`, jinak by čtení runtime založilo.
- [x] `missionPhase` (číslo fáze) a `missionElapsed` **zmizely z tabulky** — fáze je teď v hlavičce
      jako text. Číslo obsluze nic neříká a dvě místa s týmž údajem se rozejdou.
- [x] Testy (+4, `ARBot.Runtime.Tests` 56): pole hlavičky, doba běhu roste, bez mise je jméno
      prázdné a **runtime se nezaloží**, fáze už není v tabulce jako číslo.
- [x] Ověřeno v prohlížeči na desktopu i v mobilním rozměru (375×812): hlavička se zalomí na tři
      řádky, nic nepřetéká. FreeRun ukázal „mise: freerun — jede v koridoru (0:00:19)",
      Robotour „čeká se na: pokyn ke startu mise".

### Task 4: Dvoufázový běh a výběr mise z webu — **HOTOVO 5. 9. 2026**

- [x] `ParamOrigin.Runtime` + `ParamStore.SetRuntimeOverride` s bílou listinou klíčů (`mission`),
      včetně kanonizace hodnoty a validace přes registr.
- [x] `ARBot.Headless/Program.cs`: fáze A — `Start(Mode.Run, ARBotRuntime.NoRecord)`, čekání na
      volbu mise **nebo** na signál (`WaitAny`), pak `Start(Mode.Run)` už se záznamem.
      `NoRecord` je nová konstanta: `null` dál znamená „vezmi `record=`", prázdný řetězec
      „nenahrávej ani podle parametru".
- [x] Fáze A se zapne **jen když mise zadaná nebyla a náhled opravdu běží**. S `mission=` na
      příkazové řádce se chová jako dosud (jeden Start, hned se záznamem) — dokumentované spuštění
      na zařízení se tím nemění. Když se bind náhledu nepovede, do `Trace` jde „není čím vybrat
      misi", protože jinak by proces čekal navždy.
- [x] `WebStatus`: drží poslední `MotorStateBase`, `MissionBlockedReason()` (null = lze vybrat),
      pole `estop`, `pick`, `pickBlocked`, `virtualhw`. Seznam misí jde z registru
      (`Mission.Def.AllowedValues` bez `none`) — žádný druhý seznam.
- [x] `POST /mission?m=…`: gate na e-stop **na serveru** (409), `none` a prázdná hodnota 400,
      neznámá mise 400 (odmítne ji `ParamStore`, tedy jeden zdroj pravdy), druhá volba 409,
      `GET` 405. Hodnota jde query stringem, ne tělem — `HttpMini` čte jen hlavičku.
- [x] **Robotour se rozjíždí sám** (pokyn autora 5. 9. 2026) — `StartMission()` volá `ARBotRuntime`
      hned po založení mise, takže to platí pro UI, příkazovou řádku i výběr z webu. Bezpečné to je
      proto, že auto-start robota **nerozjede**: `Idle → ArmingAtDepot → AwaitingEStop`, tedy pořád
      se čeká na člověka, který stop stiskne a uvolní. Úvodní výstraha v headless opravena.
- [x] Stránka: panel výběru (jen ve fázi A), tlačítka zešedlá s **důvodem**, potvrzovací dialog,
      **virtuální e-stop** při `virtualhw=true` (server ho se skutečným HW odmítá 404).
- [x] Testy (+11, `ARBot.Runtime.Tests` 67): všechny odmítavé cesty výše + že se volba zapíše do
      účinné konfigurace s původem `Runtime`.
- [x] **Zkouška na Windows proklikaná celá**: start bez mise → stránka nabídla výběr, tlačítka
      zešedlá s důvodem „nejdriv stiskni nouzove zastaveni", **žádný `.rec` nevznikl** → stisk
      virtuálního stopu → tlačítka zelená → volba FreeRun → runtime se přestavěl a začal záznam →
      uvolnění stopu → robot jede 0,80 m/s v pravé polovině (y = −0,467 proti požadovaným −0,5) →
      druhá volba mise odmítnuta 409 → Stop ze stránky, `Stop()` 2 ms, záznam čitelný
      v `ARBot.Analyze` (13 828 zpráv). Totéž pro Robotour: auto-start → „ukotvuje depo / čeká se
      na: kvalitní fix GPS" → „servisní okno / čeká se na: QR kód".
- [x] **⚠️ Dva nálezy ze zkoušky, které testy chytit nemohly:**
      (a) **JavaScript spadl na `SyntaxError`** a stránka zůstala na „spojuji se…" — nefungovalo
      vůbec nic, ani obrázek. Příčina: skládání `onclick` do řetězce znamená apostrofy v apostrofech
      uvnitř C# verbatim řetězce a escape se cestou ztratil. Léčba: jméno mise jde do `data-mise`
      a obsluha se navěsí až po `innerHTML`. **Endpointové testy tohle nikdy nechytí** — server
      odpovídal správně, rozbitá byla stránka.
      (b) **Účinná konfigurace hlásila `mission=freerun (default)`** — `DescribeAll` měla ve switchi
      `_ => "default"`, takže nový původ `Runtime` propadl. Zrovna tenhle výpis je to, co v záznamu
      má o konfiguraci říkat pravdu. Opraveno i v panelu *Konfigurace* v UI.

### Task 5: Datový adresář a zámek instance — **HOTOVO 5. 9. 2026**

- [x] `dataroot=` v registru; `RepoPaths.SetDataRoot` / `DataRootOrBase()`, přes které jde
      `Resolve`. Prázdná hodnota = dnešní chování, **žádná změna na Windows**.
- [x] Čte se **jen z příkazové řádky** a **dřív než `config=`** (proti datovému adresáři se hledá
      i profil). `dataroot` v profilu je proto **chyba při startu** s vysvětlením, ne tiché
      ignorování — přišel by pozdě a cesty by mířily jinam, než člověk napsal.
- [x] `CrashLog.LogDirectory` nastavuje `RuntimeBootstrap` po `TryConfigure`. Pád **před** načtením
      konfigurace zůstává vedle aplikace (tedy ve stínové kopii) — vědomý ústupek, protože prohodit
      pořadí by znamenalo, že pád při čtení konfigurace nezanechá stopu žádnou.
- [x] `SingleInstanceLock`: `<dataroot>/arbot.lock`, `FileShare.None`, kód **3**, hláška odkazující
      na `systemctl status`. Bere se **po konfiguraci** (potřebuje `dataroot`) a **před hardwarem**.
      Zámek souboru, ne pidfile: padá s procesem, takže po tvrdém zabití nezůstane viset.
- [x] Testy (+9, `ARBot.Common.Tests` 1 200, `ARBot.Runtime.Tests` 72): řešení cest s `dataroot`
      i bez, `PathParam` přes nový základ, `dataroot` v profilu = chyba, druhý zámek neprojde,
      po uvolnění jde vzít znovu, `CrashLog` píše do zadaného adresáře.
- [x] Ověřeno na Windows: druhá instance skončila **kódem 3** a hláškou, a to **ještě před
      inicializací HW** (v logu není ani „Cekam na inicializaci HW"); záznam z `record=true`
      skončil v `<dataroot>/records/`, v repu nevznikl žádný.
- [x] **⚠️ Nález ze zkoušky: `record=true` psal do repa i s `dataroot=`.** `RecordPathFromParams`
      si větev „true" skládala z `RootOrBase()` sama, místo aby šla přes `Resolve`. Unit testy to
      neodhalily — pokrývaly `Resolve`, ne tuhle jedinou cestu okolo. Opraveno.
- [x] **Nález navíc:** `MainWindowViewModel` měl **vlastní kopii** hledání kořene repa, takže
      tlačítko *Run + záznam* a snímek telemetrie by o datovém adresáři nevěděly. Teď deleguje na
      `RepoPaths` — jedno místo.
- [x] **Nález navíc:** strážný test „každý parametr se někde čte" spadl, protože `dataroot` se
      z principu čte řetězcovým klíčem v `ParamStore.Build` (dřív, než store existuje). Neřešeno
      výjimkou v testu, ale tím, že ho `RuntimeBootstrap` **skutečně čte typovaným odkazem** při
      výpisu — takže stráž zůstala přísná.

### Task 6: systemd a nasazení — **HOTOVO A OVĚŘENO NA ZAŘÍZENÍ 5. 9. 2026**

- [x] `deploy/arbot.service`, `deploy/stin.sh`, `deploy/nasad.ps1`, `deploy/README.md`
      a profil `config/pi-provoz.cfg` (**bez mise** — jednotka nesmí použít `pi-freerun.cfg`,
      ten má `mission=freerun` + `autorun=true`, tedy rozjezd po bootu).
- [x] **Rozvržení na zařízení** (odchylka od návrhu H): nasazuje se do `~/arbot-headless`, běží se
      z `~/arbot-headless-run`, ale **datový adresář je `~/arbot`** — tam už jsou `config/`, `OSM/`,
      `records/` (9,6 GB) a `logs/`. Nasazovat headless přímo do `~/arbot` by přepsalo sdílené
      knihovny pod tamní **UI aplikací z 3. 9.** a ta by běžela s novým `Common` a starým `ARBot.dll`.
      Data tím zůstávají na jednom místě, což byl smysl požadavku.
- [x] Ověřeno na Orange Pi (Armbian, .NET 10.0.9, aarch64):
      **verze v hlavičce stránky i v crash logu** (`1.0.247.19186 (2316de12-dirty, build …)`);
      **crash log v datovém adresáři**; **zámek instance** (ruční běh vedle služby → kód 3);
      **`systemctl stop` → SIGTERM → řádné ukončení za 7 ms** a „Deactivated successfully"
      (`PosixSignalRegistration` na ARM tedy funguje — otevřená otázka z fáze 2);
      **stínová kopie** (17 MB, `ExecStartPre` prošel); **journal** (`journalctl -u arbot -f`);
      **jednotka `enabled`**, tedy start po bootu; **fáze A** čeká na misi s drženým stopem
      (`estop:true`, `pick:["freerun","robotour"]`, bez `pickBlocked`), robot stojí a nenahrává;
      **CPU 6,2 %** ve fázi A s otevřenou stránkou.
- [x] **Náhled na ARM ověřen celý:** `world.png` se nakreslil **včetně textu měřítka** (fonty
      SkiaSharpu na ARM byly označené za nejnejistější místo náhledu) a `camera.jpg` vrátil živý
      snímek z D435 (640×480, 17 kB). Senzory hlásí VN100, SDC2160Ex, uBloxGps, obě D435 se stářím
      pod 0,05 s; **T265 hlásí chybu** („5 s bez pózy, restart pipeline") — to je stav HW, ne tahle
      práce, a je vidět právě proto, že se `IsError` a stáří zobrazují.
- [x] **⚠️ Čtyři nálezy, které se daly najít jen na zařízení:**
      (a) **`libNativeLib.so` není v publishi** (kříží se ve WSL) → Run spadl hned při startu na
      `DllNotFoundException` v `NativeComputeUnit`. Skript ji teď doplní z datového adresáře
      a hlásí, když chybí i tam.
      (b) **V journalu byl každý řádek dvakrát** — `_TRANSPORT=stdout` i `=syslog`. Na Linuxu píše
      výchozí `DefaultTraceListener` do **syslogu**, náš `ConsoleTraceListener` na stdout a systemd
      sbírá obojí. Na Windows to vidět není (tam týž listener píše do debuggeru). Léčba:
      `Trace.Listeners.Clear()` před přidáním vlastního.
      (c) **Crash log hlásil na ARM64 architekturu „x64"** (`Environment.Is64BitProcess`), takže
      podle hlavičky nešlo poznat, jestli pád přišel ze zařízení nebo z vývojového stroje. Teď
      `RuntimeInformation.ProcessArchitecture`.
      (d) **`tar | ssh` v PowerShellu rozbíjí archiv** („This does not look like a tar archive") —
      pipeline vede text, ne bajty. Skript proto balí a posílá `scp`.
- [ ] **Zbývá autorovi:** projít celou misi na zařízení (stisk stopu → výběr mise → uvolnění →
      jízda) a ověřit start po **skutečném bootu**. Sluzba je `enabled`, ale reboot se nezkoušel.

### Task 7: Dokumentace — **HOTOVO 5. 9. 2026**

- [x] [headless.md](headless.md): dvoufázový běh, výběr mise, hlavička, `dataroot=`, zámek, systemd,
      nasazení; upravit sekci „Žádný systemd" a seznam „co ověřit na zařízení".
- [x] [decisions.md](decisions.md) — nakonec **šest** záznamů nahoru: (1) systemd a `Restart=always`
      jištěný fází A (mění rozhodnutí ze 4. 9.), (2) výběr mise z webu proti stisknutému e-stopu
      (mění rozhodnutí ze 4. 9.), (3) verzování razítkem na pokyn.
- [x] [configuration.md](configuration.md): `dataroot=`, `ParamOrigin.Runtime` — včetně pasti, kdy
      nový původ propadl ve `switch`i a výpis ho hlásil jako výchozí hodnotu.
- [x] [robotour-mission.md](robotour-mission.md) a [mission-freerun.md](mission-freerun.md):
      `IMissionStatus`, „na co se čeká", auto-start Robotour a proč má FreeRun vždy `None`.
- [x] [record-replay.md](record-replay.md): **verze binárky a účinná konfigurace v záznamu**
      (a proč tam do 5. 9. 2026 nebyly). `MissionMsg` se neverzovala — viz Task 2.
- [x] [CLAUDE.md](../CLAUDE.md): odkaz na tenhle plán u headless odstavce **a oprava tvrzení
      o konfiguraci v záznamu** — do 5. 9. 2026 tam nebyla vůbec (nález v Tasku 1); dnes ano,
      protože se vypíše znovu po připojení `TraceInfoBridge`.
- [x] Nově i [deploy/README.md](../deploy/README.md) — rozvržení adresářů na zařízení, první
      instalace, běžné nasazení, návratové kódy a pasti.
- [x] [devlog.md](devlog.md): záznam dne.
- [x] Ohlas hotovo, **nekomituj**.

## Co se sem vědomě nedělá

- **Výběr konfiguračního profilu ze stránky.** Zatím stačí mise; profil je v jednotce. Až bude
  potřeba, seznam se vezme z `config/*.cfg` v datovém adresáři a mechanismus je tentýž jako
  u mise (override + `Start`).
- **Zadávání cíle z webu.** Robotour bere cíl z QR kódu, to se nemění.
- **HTTPS a heslo.** Přístup chrání heslo k WiFi; self-signed certifikát by znamenal celostránkovou
  výstrahu prohlížeče, tedy **horší** dostupnost tlačítka Zastavit. Kdyby se to někdy měnilo, jde
  o `SslStream` v `HttpMini` a certifikát s IP v SAN.
- **Přežití mise přes restart.** Zrušeno 27. 8. 2026 a platí to dál: po restartu se jede od začátku
  a `ArmingAtDepot` postaví nové depo tam, kde robot stojí.

## Co se tímhle plánem NEOVĚŘÍ

- **Nic z toho neběželo na OrangePi** — jako celá fáze 3. Zvlášť: chování `ExecStartPre` a stínové
  kopie, vyprazdňování stdout do journalu, `flock` přes `FileShare.None` na Armbianu, čas
  `WaitReady()` se skutečnými kamerami ve fázi A a to, jestli `MotorStateBase` chodí dost brzy na
  to, aby se gate na e-stop dal použít hned po startu.
- **Čtení skutečného nouzového zastavení** — na Windows se zkouší virtuální; že `MD23`/`SDC2160`
  hlásí `IsEmergencyStop` včas a spolehlivě, ukáže až zařízení.
- **Kolik stojí fáze A** (senzory a fúze běží, robot stojí) na CPU Pi.
