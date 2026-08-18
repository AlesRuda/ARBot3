# Telemetrický pohled — tabulka údajů v čase

Jeden pohled, ve kterém je vidět **stav robota, řídicí zásahy a údaje z dalších zpráv pohromadě
a srovnané v čase**: tabulka v pořadí záznamu (sloupec = jeden údaj), detail vybraného řádku
a graf vybraných údajů v čase (víc řad v jednom grafu).

Motivace: dnes jde každý údaj hledat jen ve svém vlastním okně (World pohled, dokumenty senzorů,
Debug output). Nejde odpovědět na otázku typu „proč v 12:34:56 zpomalil" — to vyžaduje vidět
**vedle sebe** pózu, příkaz do motorů, stav plánu a stav globální navigace v tom okamžiku.

## Stav (2026-08-17)

Návrh vznikl 17. 8. 2026 a **fáze 1 i fáze 2 jsou téhož dne hotové**; tabulka běžela nad reálným
záznamem. Implementační kroky: [plan-telemetry-view.md](plan-telemetry-view.md).

| Část | Stav |
|---|---|
| Jádro `ARBot.Common/Telemetry` (tabulka, builder, skener, řady) | **hotové**, 21 testů, ověřené i na skutečném záznamu |
| Registr sloupců (47 údajů ze 7 typů zpráv, s popisy) | **hotové** |
| UI: tabulka, detail řádku, tooltipy, sken na pozadí | **hotové a viděné za běhu** |
| Napojení na Replay: kurzor → řádek | **hotové a viděné za běhu** (viz snímek níže) |
| Napojení na Replay: dvojklik = seek | hotové, **za běhu neověřené** |
| Výběr sloupců, filtr řádků, přehazování sloupců | hotové, **za běhu neověřené** |
| Fáze 2: graf řad v čase — kreslení, legenda, kurzor | **hotové a viděné za běhu** |
| Graf: ovládání myší (lupy, tažení, odečítátko, klik = seek) | hotové, **za běhu neověřené** |
| Rozšíření `LocalPlanMsg` o rychlostní diagnostiku | **není** — viz [Co zbývá](#co-zbývá) |
| Režim Run (živé plnění) | **není** (záměrně mimo fázi 1) |

**Ověřeno buildem `-p:Platform=x64`, testy a bezobslužným během** (`telemetryshot=true`, viz níže) —
ten projde celou cestu View → sken → tabulka → graf a pořídí obrázky do deníčku. **Co se ovládá
myší, tím ověřit nejde** a na cílovém HW (OrangePI) to neběželo.

## Rozsah fáze 1

- **Jen režim View** (nad hotovým záznamem). Za běhu (Run) se do tabulky nikdo nekouká a index se
  v paměti nedrží; Run se dá přidat později jako „on-line ocas" právě zapisovaného záznamu, bez
  přepisu zbytku (viz [Co zbývá](#co-zbývá)).
- **Tabulka + detail řádku.** Grafy jsou fáze 2 — návrh je připravuje, ale nestaví.
- **Údaje, které v záznamech opravdu jsou.** Tři z původně jmenovaných tam nejsou; viz
  [Co ve zprávách chybí](#co-ve-zprávách-chybí).

## Na čem to stojí (co už v repozitáři je)

| Co | Kde | K čemu |
|---|---|---|
| Sidecar index záznamu | [`MessageIndex`, `IndexEntry`](../Src/ARBot.Common/Communication/MessageIndex.cs) | časová osa **všech** zpráv + `Offset`/`Length`/`MsgName`/`Name` |
| Index už načtený v paměti | [`FileMessageSource.Index`](../Src/ARBot.Common/Communication/FileMessageSource.cs) | `ARBotRuntime` ho čte při `Start(Mode.View, …)` |
| Náhodné čtení rámce | tamtéž (`ReadFrameAt`, privátní) | vzor, jak přečíst jednu zprávu z `Offset` |
| Seek v záznamu | `FileMessageSource.SeekTo(seq)` + [`ReplayNavTool`](../Src/ARBot/ViewModels/ReplayNavTool.cs) | napojení tabulky na přehrávání |
| Soubor otevřený s `FileShare.Read` | [`ARBotRuntime`](../Src/ARBot/Robot/ARBotRuntime.cs) (`StartView`) | skener si smí otevřít **vlastní** stream |

Klíčová vlastnost indexu: `ArrivalTicks` (T_out) stampuje `RecordingTarget` **každé** zprávě,
kdežto `CaptureTicks` (T_in) je 0 u zpráv bez `IHasCaptureTime` (např. `GraphNavigationMsg`).
Index je proto jediné místo, kde je úplná časová osa záznamu. Viz
[record-replay.md](record-replay.md).

## Datový model (`ARBot.Common/Telemetry/`)

Tabulka je **sloupcová** — sloupec je pole hodnot, řádky jsou indexy:

```
TelemetryTable
  int RowCount
  long[] RowTicks          // čas řádku (T_in, jinak T_out)
  long[] RowArrivalTicks   // T_out zakládající zprávy (detail ukazuje oba časy)
  long[] RowSeq            // Seq zprávy, která řádek založila  -> seek
  string[] RowMsgName      // typ té zprávy
  TelemetryColumn[] Columns
  bool Truncated           // narazilo se na strop řádků

TelemetryColumn
  ColumnSpec Spec
  double[] Value
  long[]   ValueTicks    // čas zprávy, ze které hodnota je; 0 = ještě nikdy nepřišla
```

Tabulku skládá [`TelemetryTableBuilder`](../Src/ARBot.Common/Telemetry/TelemetryTableBuilder.cs)
(volatelný i samostatně — testy ho plní přímo, bez souboru), plní ji `TelemetryScanner`.

**Řádek = jedna přijatá registrovaná zpráva.** Žádný „kotvicí typ" — každý příchod je vidět ve svém
pravém čase a řádek zároveň doslova odpovídá jednomu záznamu, což dělá detail řádku přímočarým.
Zprávy se **shodným časem řádku** (tentýž takt) se slévají do jednoho řádku, aby póza a řídicí
zásah z jednoho taktu nebyly na dvou řádcích. Slévají se jen **sousední** položky indexu a jen při
**přesné** shodě času; tolerance („sluč, co je do 5 ms") se dá přidat později, kdyby se časy
z jednoho taktu rozcházely o zlomky.

U slitého řádku platí `RowSeq` a `RowMsgName` **první** zprávy taktu — ta řádek založila. Seek
z tabulky tedy míří na začátek taktu, ne doprostřed; hodnoty ostatních zpráv téhož taktu už v řádku
jsou, takže se ničemu nezmešká.

> **Pozor: čas řádku není monotónní.** Řádky jdou v pořadí **záznamu** (`Seq`, tedy T_out), ale čas
> řádku je čas **pořízení** (T_in) — a každá zpráva putuje pipeline jinak dlouho, některé navíc
> nesou čas svých vstupních dat. V reálném záznamu se to opravdu děje: dvě sousední `LocalPlanMsg`
> s klesajícím T_in, a `GPSState` prokládající `RobotStateMsg` mimo pořadí. Pro tabulku to nevadí
> (řádek = jedna zpráva, sloupec `Čas` říká, kdy vznikla), pro **graf ano** — proto se řada před
> kreslením třídí, viz [Fáze 2](#fáze-2--graf-řad-v-čase).

**Čas řádku** = `CaptureTicks`, a když je 0, pak `ArrivalTicks`. Detail řádku ukazuje **oba** časy —
rozdíl T_in/T_out je sám o sobě diagnostika (jak dlouho měření putovalo pipeline).

**„Přišlo právě teď" se neukládá.** Vyplyne z dat: buňka je *fresh*, když
`ValueTicks[r] != ValueTicks[r-1]` (na prvním řádku když `ValueTicks[0] != 0`), a *prázdná*, když
`ValueTicks[r] == 0` (do toho okamžiku ta zpráva ještě nepřišla). Šetří to pole a hlavně to nemůže
s hodnotami rozejít. Známé omezení: přijdou-li dvě zprávy se **stejným** časem i hodnotou, druhá se
jako fresh nepozná — pro diagnostiku bezvýznamné.

**Paměť:** 16 B na buňku (hodnota + čas). Při ~65 zprávách/s je 10 minut ≈ 39 000 řádků, tedy
30 sloupců ≈ 19 MB. Strop na počet řádků je konfigurovatelný (default 500 000); při odříznutí to
pohled **nahlásí**, aby se nezdálo, že záznam končí dřív. (Kdyby paměť tlačila, čas jde uložit jako
`int` milisekundy od začátku záznamu — 12 B/buňku. Zbytečná optimalizace, dokud nebude potřeba.)

## Registr sloupců (`ARBot/Telemetry/TelemetryColumns.cs`)

Sloupce se definují **explicitně v UI vrstvě** — jednotky, formát a „co má smysl kreslit" jsou
prezentační věc, takže se kvůli nim nesahá do `ARBot.Common` a doména nedostane starosti UI.

```csharp
sealed class ColumnSpec
{
    string MsgName;                  // ze které zprávy
    string Name;                     // volitelně i která instance (INamedMessage, např. kamera)
    string Header;                   // "v [m/s]"
    string Description;              // vysvětlení do tooltipu (záhlaví je jen zkratka)
    string Format;                   // "F2"
    bool   Graphable;                // smí do grafu
    AngleKind Angle;                 // Heading / Rate / None — řídí převod konvence při zobrazení
    Func<Message, double?> Value;    // hodnota (null = tato zpráva sloupec neplní)
    Func<double, string> Text;       // volitelný převod čísla na text (enum, bool)
}
```

Přidat údaj = **jeden záznam v seznamu**. Nečíselné údaje jsou uvnitř vždy číslo (bool → 0/1,
enum → jeho hodnota) a `Text` je jen zobrazí (`Driving`, `STOP`) — takže i stav jde vykreslit do
grafu jako schod. Je-li `Text` zadaný, má přednost před `Format`; jinak se hodnota zobrazí přes
`Format`. Registr má na to tři tovární metody: `Num` (číslo), `Flag` (logická hodnota → zkratka
nebo `-`) a `Enum` (výčet → jméno hodnoty). `MsgName` si berou z **prototypu** (`new T().MsgName`),
aby se název typu nepsal jako řetězec a nerozešel se při přejmenování.

**`Description` je povinná část definice** — záhlaví musí být zkratka (šířka sloupce), takže význam
údaje nese popis a nový sloupec s ním přijde rovnou. Zobrazuje se jako tooltip (viz [UI](#ui--tabulka-a-detail)).

Sloupce — 47 údajů ze sedmi typů zpráv (z toho, co záznamy opravdu nesou):

| Zpráva | Sloupce |
|---|---|
| [`RobotStateMsg`](../Src/ARBot.Common/Logs/RobotStateMsg.cs) | X [m], Y [m], theta [°], v [m/s], omega [°/s] |
| [`DriveCommandMsg`](../Src/ARBot.Common/Logs/DriveCommandMsg.cs) | cmd v [m/s], cmd omega [°/s], cmd dif [m/s], STOP |
| [`LocalPlanMsg`](../Src/ARBot.Common/Logs/LocalPlanMsg.cs) | plan stav, délka [m], odstup [m], počet bodů, výpočet [ms] |
| [`GlobalNavMsg`](../Src/ARBot.Common/Logs/GlobalNavMsg.cs) | nav stav, do cíle [m], hran trasy, od sítě [m], fi [s], uzavřeno hran |
| [`GPSState`](../Src/ARBot.Common/Devices/GPSState.cs) | lat [°], lon [°], fix, satelitů, HDOP, alt [m], v [m/s], kurz [°] |
| [`MotorStateBase`](../Src/ARBot.Common/Devices/MotorStateBase.cs) | kolo L/R [m/s], odo v [m/s], odo omega [°/s], enc L/R [m], bat [V], I L/R [A], HW STOP, mot drop |
| [`IMUState`](../Src/ARBot.Common/Models/IMUState.cs) | IMU yaw/pitch/roll [°], gyro z [°/s], acc x/z [m/s²], IMU conf, IMU drop |

Poznámky ke konkrétním sloupcům:

- **Senzorové zprávy dají tabulce smysl „příkaz → skutečnost", ale nafouknou počet řádků.**
  `MotorStateBase` a `IMUState` chodí mnohem častěji než řídicí smyčka (v testovacím záznamu 6 761
  a 13 475 zpráv proti 1 351 taktům), takže řádků je 21 556 místo 2 806 a sken trvá 71 ms místo 29.
  Zaplatí se to tím, že jde srovnat `cmd omega` (co chtěla smyčka) → `gyro z` (co naměřilo IMU) →
  `omega` (co z toho udělala fúze), nebo `cmd v` → `kolo L`/`kolo R`. Když je řádků moc, filtr
  **Řádky ▾** nechá zakládat řádky jen vybraným typům a hodnoty ostatních se dál drží z minula.
- **`HW STOP` vs. `STOP`** jsou dva různé údaje: `HW STOP` hlásí hardware motorů, `STOP` je to, co si
  o nouzovém zastavení myslela řídicí smyčka. Rozdíl mezi nimi je diagnostika sama pro sebe.
- **`acc x`/`acc z` mohou zůstat prázdné.** V testovacím záznamu IMU dodává jen orientaci, úhlovou
  rychlost a důvěru — `Acceleration` je `null` (serializace ho přenáší, ale driver ho neplní).
  Prázdná buňka správně znamená „tato hodnota nikdy nepřišla", ne nulu.
- **`enc L`/`enc R` jsou kumulativní** (od verze 2 zprávy `MotorStateBase`): roste to od startu,
  není to přírůstek za takt.

- **`theta`/`omega`** jsou ve **stupních** (zprávy nesou radiány) — v tabulce se čísla čtou očima,
  ne dosazují do vzorců. Kurz je matematická orientace v ENU (0° = východ, +CCW), **ne azimut**;
  proto to má i v tooltipu.
- **`fi`** (cost-to-goal) má **3 desetinná místa**: mezi takty se mění o zlomky sekundy a na jednom
  desetinném místě vypadalo zamrzle.
- **`HDOP` je ve starších záznamech ze simulace nula** — `VirtualGps` ho do 17. 8. 2026 nevyplňoval
  (viz [virtual-hw.md](virtual-hw.md)). Na reálném HW je namapovaný (uBlox `PVT.pDOP`, NMEA `GGA[7]`).
- **`Graphable`** dnes nikdo nečte (všechny sloupce ho mají implicitně `true`) — je to příprava
  na fázi 2.
- **`Name`** (rozlišení instance, např. levá/pravá kamera) zatím **žádný sloupec nepoužívá**; kód
  pro něj je v builderu hotový.


### Konvence úhlů: uloženo matematicky, přepíná se zobrazení

Směrové údaje přicházejí z různých zdrojů v různých konvencích — fúze a IMU hlásí **matematickou
orientaci** (0° = východ, kladně proti hodinovým ručičkám), GPS přijímač naopak **azimut**
(0° = sever, po směru hodinových ručiček). Dokud si každý sloupec převáděl po svém, míchala tabulka
obojí: `theta` a `IMU yaw` matematicky, `GPS azimut` kompasově.

Nově platí jedno pravidlo: **uloženo je vždy matematicky ve stupních**, a převod dělá až zobrazení.
Sloupec k tomu nese druh úhlové veličiny ([`AngleKind`](../Src/ARBot.Common/Telemetry/AnglePresentation.cs)):

| `AngleKind` | Které sloupce | Světové zobrazení |
|---|---|---|
| `Heading` | `theta`, `IMU yaw`, `GPS kurz` | azimut = `90° − hodnota`, do [0, 360) |
| `Rate` | `omega`, `cmd omega`, `gyro z`, `odo omega` | obrácené znaménko (kladně = doprava) |
| `None` | vše ostatní **včetně `IMU pitch`/`roll`** | beze změny (náklony nejsou kurzy) |

Přepíná se tlačítkem **Azimut** — je v liště tabulky **i grafu** a platí **pro celou tabulku
najednou**; jinak by polovina mluvila jiným jazykem než druhá (kompasový kurz vedle zatáčení
„doleva kladně" je ta samá past, jen posunutá). Výchozí je matematická konvence: jsou to tatáž
čísla, jaká jsou ve zprávách a v debuggeru, takže tabulka při ladění nelže; azimut je vědomé
přepnutí, když chceš srovnat s mapou nebo kompasem.

Převod je na jednom místě (`AnglePresentation.Present`) a aplikuje se v `TelemetryColumn.ValueAt`
a `TextAt` — tedy i v grafu, protože řady se táhnou přes `ValueAt`. **Data se nemění**, jen se jinak
čtou; surová hodnota zůstává dostupná přes `RawValueAt`. Klasifikace sloupců je v registru
pohromadě (`TelemetryColumns.Mark`) a **neznámé záhlaví je chyba** — po přejmenování sloupce se
příznak nemůže tiše ztratit.

Přepínač v grafu **data nevlastní**: jen požádá tabulku (`TelemetryChartDocument.WorldAnglesRequested`),
ta přepne režim, přepočítá řady a pošle je zpátky (`SetSeries(series, worldAngles)`). Obě okna tak
nikdy neukazují jinou konvenci a graf zůstává čistým kreslítkem nad hotovými řadami.

## Skener (`ARBot.Common/Telemetry/TelemetryScanner`)

Jeden průchod indexem (v pořadí `Seq`):

1. Položka indexu, jejíž `MsgName` (a případně `Name`) **není** v registru, se **přeskočí bez
   čtení**. Tím se přeskočí `CameraFrame` a `Blob`, tedy drtivá většina objemu záznamu — projít
   celý běh znamená přečíst jednotky MB, ne gigabajty.
2. Registrovaná položka se přečte na `Offset` (náhodné čtení jednoho rámce), její sloupce se
   uloží do „aktuálního stavu".
3. Založí se nový řádek — nebo se aktualizuje ten předchozí, má-li shodný čas (slévání výše).
   Do řádku se zapíše celý aktuální stav, takže neaktualizované sloupce nesou hodnotu i čas
   z minula (odtud „drží se z minula").

Skener bere `Stream` (ne cestu) — volající si soubor otevře sám. `TelemetryDocument` mu otevře
**vlastní** read-only stream nad souborem záznamu (ten je otevřený s `FileShare.Read`), takže sken
**nekoliduje s přehráváním** a nevyžaduje změnu `FileMessageSource`; testy místo toho podstrčí
`MemoryStream`. Index bere hotový z `FileSource.Index` — **sidecar si sám nečte**: v režimu View ho
runtime už načetl, a bez runtime (dávkové použití) si ho volající přečte přes `MessageIndex.Read`
a předá.

Běží na worker vlákně, hlásí postup (0..1, ~100 hlášení na celý sken) a je zrušitelný
(`CancellationToken`) — zavření dokumentu sken ukončí.

**Poškozený rámec sken nezastaví** — přeskočí se a jede se dál (jinak by jedna vadná zpráva
zahodila celý zbytek záznamu).

**Strop řádků** (`maxRows`, default 500 000) se hlásí jako `Truncated` jen tehdy, když za ním
opravdu ještě nějaká sledovaná zpráva je — záznam, který skončí přesně na stropu, o nic nepřišel
a varování by bylo lživé.

**Záznam bez sidecar indexu tabulku nepodpoří** (nejsou offsety ani časová osa). Dokument to hlásí
hláškou „Záznam nemá sidecar index (*.idx)"; teoretický fallback — sekvenční průchod celým souborem —
by musel deserializovat i obrázky, takže se nedělal.

**Skener filtruje jen podle `MsgName`, ne podle `Name`.** Zprávu jiné instance (jiná kamera) tedy
přečte a zahodí ji až builder. Dokud je `Name` nevyužité, nestojí to nic; kdyby přibyl sloupec
vázaný na jednu instanci, je to místo, kde se ušetří čtení.

## UI — tabulka a detail

Dokument [`TelemetryDocument`](../Src/ARBot/ViewModels/TelemetryDocument.cs) (menu **Tools →
Telemetrie**) s view [`TelemetryDocumentView`](../Src/ARBot/Views/TelemetryDocumentView.axaml),
tab jako World. Nad tabulkou je stavový řádek (počet řádků, časový rozsah, případné varování
o oříznutí) a během skenu ukazatel postupu.

![Telemetrická tabulka](media/telemetry-view.png)

- **Tučně = hodnota právě přišla**, obyčejně = drží se z minula, prázdná buňka = ta zpráva zatím
  nepřišla. Na jeden pohled je tak vidět, co je nové a co stará hodnota.
- **Detail řádku** (panel u tabulky): všechny sloupce s hodnotou, časem a **stářím** vůči řádku
  („v = 1,20 m/s · 12:34:56.789 · o 0,8 s starší než řádek"), plus zpráva, která řádek založila
  (`Seq`, typ, T_in, T_out).
- **Tooltip s významem údaje.** Záhlaví sloupce musí být zkratka (šířka sloupce), takže vysvětlení
  — co to je, odkud se to bere, jak to číst — nese `ColumnSpec.Description` a ukáže se najetím myší
  **na záhlaví v tabulce i na řádek v detailu**. Je to součást definice sloupce, takže nový údaj
  přijde s popisem rovnou (jeden záznam v registru, ne zvláštní tabulka textů).
- **Napojení na Replay** obousměrné:
  - dvojklik na řádek (nebo tlačítko v detailu) zavolá `Pause()` + `SeekTo(RowSeq)`;
  - naopak **kurzor přehrávání vybírá řádek** — dokument polluje `FileMessageSource.Cursor`
    (100 ms, stejně jako [`ReplayNavTool`](../Src/ARBot/ViewModels/ReplayNavTool.cs); zdroj o postupu
    událost nemá) a vybere poslední řádek s `RowSeq ≤ Cursor-1`, tedy poslední **už přehranou**
    zprávu. Vybraný řádek View odscrolluje do viditelné části — ale **jen** když ho vybralo
    přehrávání (událost `PlaybackRowChanged`), aby tabulka uživateli neskákala pod rukama.
  - Přestavuje se jen při **změně** kurzoru: když přehrávání stojí, uživatelův výběr v tabulce
    zůstane, kde je. Skrytý tab synchronizaci zastavuje (`OnActiveChanged`).
  - `Cursor` je `Seq` **následující** zprávy (a `SeekTo(pos)` ho nastaví na `pos+1`) — proto to
    `-1`. Ze stejného důvodu se o jednu opravila i pozice v `ReplayNavTool`, jinak by slider po
    každém skoku z tabulky ujel o řádek. Jeho časovač nově běží pořád (ne jen během Play), protože
    kurzorem hýbe i tabulka.
- **Výběr viditelných sloupců** (tlačítko *Sloupce ▾*): registr je 25 údajů, což je víc, než se
  vejde na obrazovku. Zaškrtávátko skrývá `DataGridColumn` — **data ani sken se nemění**, jen
  zobrazení. Vedle každého je přepínač *graf*, který ten údaj přidá do grafu (fáze 2 níže).
  Tlačítka *Vše* / *Nic* jsou tam proto, že odklikat 25 položek ručně nikdo nechce.
- **Přehazování sloupců myší** (`CanUserReorderColumns`) — pořadí v registru je dané tím, jak
  údaje spolu souvisejí, ale při konkrétním hledání chce mít člověk vedle sebe jiné dvojice.
  Mapa sloupců v code-behind drží **reference** na `DataGridColumn`, ne pozice, takže přeházení
  pořadí nerozbije skrývání ani ikonu grafu.
- **Ikona grafu přímo v záhlaví sloupce** — tentýž přepínač jako ve flyoutu (obousměrně svázané,
  aby se ovladače nerozešly), ale na místě, kam se člověk zrovna dívá; přes flyout by musel hledat,
  který řádek seznamu odpovídá sloupci pod kurzorem. Ikona je **nakreslená geometrie**, ne znak:
  symboly grafu (`∿`, `📈`) nemusí být v použitém fontu a vysypal by se prázdný obdélník.
- **Filtr řádků podle typu zakládající zprávy** (tlačítko *Řádky ▾*): nabízí typy, které v tomhle
  záznamu opravdu jsou, i s počtem řádků. Necháte-li jen `DriveCommandMsg`, je **jeden řádek =
  jeden takt řídicí smyčky** a ostatní hodnoty se drží z minula jako vždycky. Filtruje se zobrazení,
  tabulka zůstává celá; stavový řádek pak hlásí „X z Y řádků (filtr)".
  - Filtrovaná kolekce se **vyměňuje celá** (ne položka po položce) — u desetitisíc řádků by
    jednotlivé notifikace tabulku na vteřiny zastavily. Výběr řádku se po výměně obnoví (nebo
    padne na nejbližší předchozí viditelný), aby detail nebliknul na prázdno.
- **Tabulka se drží aktuálního záznamu.** Týž časovač (100 ms) hlídá i to, jestli tabulka pořád
  patří k tomu, co se přehrává — porovnává `RecordPath` **a referenci** na `FileMessageSource`
  (tentýž soubor otevřený znovu je nový zdroj s novým indexem, takže i ten se přeskenuje). Když se
  záznam změní, staré řádky se zahodí hned a spustí se nový sken; výsledek zastaralého skenu se
  zahazuje podle jeho `CancellationTokenSource`, aby nepřepsal tabulku toho nového. Ze stejného
  důvodu časovač běží **od otevření dokumentu**, ne až po prvním úspěšném skenu: telemetrii jde
  otevřít i **dřív než záznam** a naplní se, jakmile záznam přijde.
- **Backpressure** se tady neřeší: data nepřicházejí ze `Stream`u, ale z jednorázového skenu.
  (Až přijde Run, platí povinný vzor z [Views/README.md](../Src/ARBot/Views/README.md).)

Čitelnost (dolazeno 17. 8. podle první zkušenosti s reálným záznamem — vypadá to jako drobnosti,
ale tabulka se bez nich četla špatně):

- Sloupec času musí být tak široký, aby se vešlo **`HH:mm:ss.fff` včetně milisekund** (130 px) —
  u telemetrie jsou zrovna milisekundy to podstatné. Typ zprávy 155 px.
- **Záhlaví je větším písmem (14) než data (12)** a řádek záhlaví vyšší, aby se v desítkách úzkých
  sloupců dalo orientovat.
- **Hodnoty v buňkách se svisle centrují** stejně jako čas — jinak „plavou" nahoře a řádek se
  nečte jako jeden celek.
- Detail řádku má písmo 13/14 (stáří 12); panel je 380 px široký.

Vykreslení: **`Avalonia.Controls.DataGrid` 12.0.0** (virtualizace, měnitelné šířky sloupců) —
existuje a funguje, riziko z návrhu je vyřešené. Pozor na verzi: 12.0.1 a novější si vynucují
Avalonia ≥ 12.0.5, kdežto projekt drží 12.0.3, takže by build spadl na `NU1605`. Balíček potřebuje
i `StyleInclude` svého tématu v `App.axaml` (`avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml`),
jinak se tabulka vykreslí jako prázdné místo. Sloupce se staví **v code-behind** (je jich desítky
a jsou datově řízené registrem) a jsou to `DataGridTemplateColumn`, protože textový sloupec neumí
tučně jen u některých buněk. Buňky se **nebindují** — řádek je hotový a už se nemění, takže se text
čte přímo při vykreslení (desetitisíce řádků × desítky sloupců by jinak znamenaly desetitisíce
notifikací).

## Co zbývá

Obě fáze stojí; tohle zbývá:

1. **Proklikat myší to, co bezobslužný běh neověří:** flyouty (výběr sloupců, filtr řádků),
   přehazování sloupců, dvojklik = seek a v grafu lupy, tažení, odečítátko a klik = skok.
2. **Rozšíření `LocalPlanMsg`** o rychlostní diagnostiku (+ verze) — viz
   [Co ve zprávách chybí](#co-ve-zprávách-chybí). Pak je to jeden řádek v registru a údaj je i v grafu.
3. **Režim Run** — živé plnění, buď „ocas" právě zapisovaného záznamu, nebo odběr `Stream`u
   s povinným backpressure vzorem z [Views/README.md](../Src/ARBot/Views/README.md).
4. **Změřit sken na OrangePI** (SD karta, náhodné čtení desetitisíc rámců).

## Fáze 2 — graf řad v čase

![Graf telemetrie](media/telemetry-chart.png)

Přepínač *graf* u sloupce (v záhlaví sloupce nebo ve flyoutu *Sloupce ▾*) přidá jeho řadu do
dokumentu **Graf telemetrie** a rovnou ho otevře. Dokument [`TelemetryChartDocument`](../Src/ARBot/ViewModels/TelemetryChartDocument.cs)
drží řady a legendu, kreslí
[`TelemetryChartControl`](../Src/ARBot/Views/Controls/TelemetryChartControl.cs).

**Řada = jen skutečné příchody.** [`TelemetrySeries`](../Src/ARBot.Common/Telemetry/TelemetrySeries.cs)
vytáhne ze sloupce dvojice (čas, hodnota) pro buňky, které jsou *fresh*; držené hodnoty jsou jen
opakování té předchozí a v grafu by z nich byla hustší řada bez jediné nové informace.
**Řada se třídí podle času** (jen když to je potřeba — obvykle ne): čas řádku není monotónní, viz
poznámka v [datovém modelu](#datový-model-arbotcommontelemetry), a osa X grafu monotónní být musí,
jinak by křivka dělala klikyháky a půlení v `ValueAtTime` vracelo nesmysly. Ze samotných
příchodů jde nakreslit obojí — **schod** (hodnota platí až do dalšího příchodu) i **rampa**
(interpolace mezi příchody), přepínač je per řada. Výchozí je schod u výčtů a logických hodnot
(mezi `Driving` a `Blocked` se nic neinterpoluje) a rampa u čísel.

**Každá řada má vlastní měřítko osy Y** (autoscale na svoje min/max). Návrh počítal s „volitelně
druhou osou Y", ale to problém neřeší: v jednom grafu jsou metry, stupně za sekundu i stav výčtu —
dvě osy by stačily na dvě řady. Rozsah každé řady je proto vidět v legendě a osa Y s čísly se kreslí,
jen když je zapnutá **právě jedna** řada (tam je jednoznačná).

**Lupa na ose Y** (Ctrl+kolečko) zoomuje v *normalizované* ose — 0 = spodek rozsahu řady, 1 = vrch —
takže roztáhne všechny řady zároveň a jejich vzájemné porovnání zůstane platné. Kdyby se zoomovalo
v hodnotách, byla by u každé řady jiná a graf by přestal dávat smysl. Popisky osy Y procházejí týmž
přepočtem, takže po přiblížení nelžou.

**Odečítátko hodnot pod myší** (jako „tracker" v OxyPlotu): svislá čára v místě kurzoru, tečka na
každé křivce a rámeček s časem a hodnotou **každé** viditelné řady — ne jen té nejbližší, protože
smysl grafu je porovnávat je mezi sebou. Hodnota se čte tak, jak je řada nakreslená: u schodu
poslední příchod (`ValueAtTime`), u rampy interpolace mezi sousedními příchody (`InterpolatedAt`).
Rámeček se u pravého okraje sklopí na druhou stranu čáry, aby nevylezl z plochy.

**Kreslí se vlastním `Control.Render`**, ne grafovou knihovnou. OxyPlot by byl přirozená volba, ale
oficiální `OxyPlot.Avalonia` cílí na Avalonii 11 a pro dvanáctku existuje jen neoficiální fork se
162 staženími — viz [decisions.md](decisions.md#2026-08-17--graf-telemetrie-se-kreslí-vlastním-controlem-ne-oxyplotem--rozhodnutohotovo).
Data jsou navíc už v poli a projekt kreslené controly má (kompas, umělý horizont, robot-centrický
pohled). **Až OxyPlot vydá podporu Avalonie 12, stojí za to se k tomu vrátit** — cena přechodu je
jeden control, `TelemetrySeries` je na kreslení nezávislá. Co dnes proti knihovně chybí: anotace,
výběr obdélníkem, export obrázku a legenda v ploše grafu.

Ovládání: kolečko = lupa času **kolem času pod myší** (jinak by ujíždělo místo, na které se člověk
dívá), Ctrl+kolečko = lupa hodnot (taky kolem bodu pod myší), pravé tlačítko táhne oběma směry,
dvojklik = celý rozsah i výchozí měřítko, **levý klik = skok v přehrávání** na ten okamžik (čas se
půlením přeloží na `Seq` v indexu). Kurzor přehrávání je svislá čára a legenda u každé řady ukazuje
její hodnotu v tom místě — čtenou jako schod, tedy tutéž hodnotu, jakou má v tom okamžiku tabulka.

Při hustých datech (víc bodů než pixelů) se kreslí **obálka min/max po pixelech** místo lomené čáry
přes všechny body: desetitisíce úseček by se stejně slily, jen by zdržely, a obálka navíc ukazuje
rozptyl, ne náhodně vybraný vzorek.

Výřez se drží i při přidání další řady (zoom se nezahodí), ale **resetuje se, když se s daty vůbec
neprotíná** — to nastane po přepnutí na jiný záznam, kde by graf jinak zůstal prázdný, aniž by bylo
poznat proč.

## Reprodukovatelný screenshot (`telemetryshot`)

Spuštění s parametrem `telemetryshot=true` bezobslužně otevře **poslední záznam se sidecar indexem**
ve `records/` (nebo ten zadaný v `ts_rec=<cesta>`), počká na sken, posune přehrávání doprostřed
záznamu, uloží `doc/media/telemetry-view.png`, pak zapne tři údaje do grafu (`v`, `cmd v`, `omega`),
uloží `doc/media/telemetry-chart.png` a aplikaci ukončí. Obrázky výše vznikly takhle.

Stejný vzor jako `worldshot=true` ve [world-view.md](world-view.md#reprodukovatelný-screenshot-worldshot);
kód je v [MainWindowViewModel.TelemetryShot.cs](../Src/ARBot/ViewModels/MainWindowViewModel.TelemetryShot.cs).
Smysl: obrázek featury jde kdykoli pořídit znovu, bez ručního proklikávání — a při té příležitosti
se celá cesta (View → sken → tabulka → graf) projde za běhu, což testy neumí.

## Co ve zprávách chybí

Ze zadání jsou tři údaje, které dnes v žádné zprávě nejsou:

| Údaj | Stav | Co pro něj udělat |
|---|---|---|
| Vzdálenost do cíle | **je** — `GlobalNavMsg.RouteLengthM` (po síti; vzdušná ne) | jen sloupec v registru |
| Max. povolená rychlost z plánovače | **není ve zprávě** — `LocalPlanResult` ji zná (`MinVClear`, `MinVBrake`, `MinWayPointSpeed`, `SpeedLimitedBy`), ale `ToLogMessage`/`ToData` ji nepřenášejí | rozšířit `LocalPlanMsg` + verze +1 (samostatný krok **po** jádru) |
| Plánované odbočení | **neexistuje nikde** | musí ho začít počítat globální navigace (manévr na trase), pak nová položka v `GlobalNavMsg` |

Tabulka je proti tomu odolná: jakmile hodnota ve zprávě bude, je to jeden řádek v registru.

## Testy

Jádro je v `ARBot.Common` právě proto, aby šlo testovat — `ARBot` (UI) testovací projekt nemá,
takže **UI se ověřuje jen spuštěním** (bezobslužně přes `telemetryshot=true`, viz níže).
`ARBot.Common.Tests/Telemetry/`, 21 testů ve třech souborech:

[`TelemetryTableBuilderTests`](../Src/ARBot.Common.Tests/Telemetry/TelemetryTableBuilderTests.cs)
plní builder přímo zprávami (bez souboru) a ověřuje pravidla tabulky:

- držení hodnot (sloupec z pomalé zprávy má na následujících řádcích tutéž hodnotu i čas),
- *fresh* na správných řádcích a **jen** na nich,
- prázdné buňky před první zprávou daného typu,
- čas řádku: `CaptureTicks`, fallback na `ArrivalTicks` u zprávy bez `IHasCaptureTime`, a že si
  řádek drží **oba** časy,
- slévání řádků při shodném čase,
- strop řádků (přestane přidávat a ohlásí `Truncated`),
- `Text` má přednost před `Format`.

[`TelemetryScannerTests`](../Src/ARBot.Common.Tests/Telemetry/TelemetryScannerTests.cs) skládají
záznam do `MemoryStream` přes `RecordingTarget` (data + index) a skenují ho:

- řádky vzniknou **jen** z registrovaných typů,
- časy řádků jdou v pořadí záznamu,
- **náhodné čtení opravdu funguje**: mezi sledované zprávy se vloží velké přeskakované rámce
  a řádky přesto vyjdou úplné (kdyby se četlo sekvenčně, rozpadlo by se to),
- strop řádků hlásí `Truncated`,
- zrušení skenu (`CancellationToken`),
- sken bez indexu vyhodí výjimku.

[`TelemetrySeriesTests`](../Src/ARBot.Common.Tests/Telemetry/TelemetrySeriesTests.cs) ověřují
vytažení řady pro graf:

- řada bere **jen příchody**, ne držené hodnoty (5 řádků tabulky → 2 body pomalé zprávy),
- zná svůj rozsah a krajní časy,
- `ValueAtTime` se čte jako **schod** a je `null` před prvním příchodem,
- `InterpolatedAt` **interpoluje** mezi příchody (rampa) a mimo rozsah drží krajní hodnotu,
- řada se **setřídí podle času**, i když v záznamu jdou zprávy s klesajícím T_in (a hodnoty se
  přesunou spolu s časy) — reálný případ ze záznamu, viz poznámka v datovém modelu,
- sloupec, který v záznamu nikdy nepřišel, dá prázdnou řadu (a ne výjimku),
- text hodnoty respektuje `Text` z definice sloupce (výčet jménem).

## Zásahy do stávajícího kódu

Návrh byl záměrně dělaný tak, aby jich bylo minimum. Co se nakonec doopravdy změnilo:

- **`ARBotRuntime`** — přibylo `RecordPath` (cesta k přehrávanému záznamu); do té doby to byla jen
  lokální proměnná v `StartView` a skener neměl co otevřít.
- **`MainWindowViewModel` + `MainWindow.axaml`** — položka menu Tools → Telemetrie a zakládání
  dokumentu grafu (`ShowTelemetryChart`). Tabulka o docích nic neví — jen vyvolá událost
  s řadami, dokument grafu založí a aktivuje hlavní okno.
- **`ARBot.csproj` + `App.axaml`** — balíček `Avalonia.Controls.DataGrid` 12.0.0 a `StyleInclude`
  jeho tématu.
- **`ReplayNavTool`** — pozice opravená o jednu (`Cursor-1`) a časovač běží pořád; vynutilo si to
  napojení tabulky na přehrávání (viz [UI](#ui--tabulka-a-detail)).
- **`FileMessageSource`** — **žádná změna**, jak návrh sliboval (skener má vlastní stream, index
  i `Cursor` jsou už public).
- **`CLAUDE.md`** — odkaz na tento dokument.

## Otevřené otázky a rizika

- **Rychlost skenu** — náhodné čtení desítek tisíc malých rámců na OrangePI (SD karta) může být
  pomalé. Na vývojovém stroji je to bez problému (`records/20260814-132817.rec`: index 27 541 zpráv
  → **29 ms**, 2806 řádků), na cílovém HW **neměřeno**. Kdyby to vadilo, přejít na sekvenční čtení
  s přeskakováním (`Seek` přes neregistrované rámce) nebo na plnění tabulky průběžně během skenu.
- **Časy z jednoho taktu** — slévání funguje na přesnou shodu. Jestli se v praxi časy z jednoho
  taktu rozcházejí, bude potřeba tolerance (a je pak otázka, jaká, aby neslila dva různé takty).
  Ze záznamu, na kterém tabulka běžela, se to zatím nedá říct — je vidět, že se **některé** takty
  slily a jiné ne.
- **Strop řádků vs. dlouhý běh** — hodinový záznam na 65 zpráv/s je ~230 000 řádků; vejde se do
  stropu, ale je to ~110 MB. Případné řešení je rozsahový filtr (skenovat jen výsek času).
