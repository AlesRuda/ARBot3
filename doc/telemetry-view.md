# Telemetrický pohled — tabulka údajů v čase

**Stav: fáze 1 implementována** (2026-08-17). Jádro (`ARBot.Common/Telemetry`) je pokryté testy
a ověřené i na skutečném záznamu; **UI zatím neověřeno za běhu** — aplikace se rozběhne, ale
tabulku nikdo neotevřel jinak než ručně. Implementační kroky: [plan-telemetry-view.md](plan-telemetry-view.md).
Datum návrhu: 2026-08-17.

Jeden pohled, ve kterém je vidět **stav robota, řídicí zásahy a údaje z dalších zpráv pohromadě
a srovnané v čase**: tabulka řazená podle času (sloupec = jeden údaj), detail vybraného řádku, a v
druhé fázi graf vybraných údajů v čase (víc řad v jednom grafu).

Motivace: dnes jde každý údaj hledat jen ve svém vlastním okně (World pohled, dokumenty senzorů,
Debug output). Nejde odpovědět na otázku typu „proč v 12:34:56 zpomalil" — to vyžaduje vidět
**vedle sebe** pózu, příkaz do motorů, stav plánu a stav globální navigace v tom okamžiku.

## Rozsah fáze 1

- **Jen režim View** (nad hotovým záznamem). Za běhu (Run) se do tabulky nikdo nekouká a index se
  v paměti nedrží; Run se dá přidat později jako „on-line ocas" právě zapisovaného záznamu, bez
  přepisu zbytku (viz [Fáze a kroky](#fáze-a-kroky)).
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
  long[] RowTicks        // čas řádku
  long[] RowSeq          // Seq zprávy, která řádek založila  -> seek
  string[] RowMsgName    // typ té zprávy
  TelemetryColumn[] Columns

TelemetryColumn
  ColumnSpec Spec
  double[] Value
  long[]   ValueTicks    // čas zprávy, ze které hodnota je; 0 = ještě nikdy nepřišla
```

**Řádek = jedna přijatá registrovaná zpráva.** Žádný „kotvicí typ" — každý příchod je vidět ve svém
pravém čase a řádek zároveň doslova odpovídá jednomu záznamu, což dělá detail řádku přímočarým.
Zprávy se **shodným časem řádku** (tentýž takt) se slévají do jednoho řádku, aby póza a řídicí
zásah z jednoho taktu nebyly na dvou řádcích. Slévají se jen **sousední** položky indexu a jen při
**přesné** shodě času; tolerance („sluč, co je do 5 ms") se dá přidat později, kdyby se časy
z jednoho taktu rozcházely o zlomky.

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
    string Format;                   // "F2"
    bool   Graphable;                // smí do grafu
    Func<Message, double?> Value;    // hodnota (null = tato zpráva sloupec neplní)
    Func<double, string> Text;       // volitelný převod čísla na text (enum, bool)
}
```

Přidat údaj = **jeden záznam v seznamu**. Nečíselné údaje jsou uvnitř vždy číslo (bool → 0/1,
enum → jeho hodnota) a `Text` je jen zobrazí (`Driving`, `STOP`) — takže i stav jde vykreslit do
grafu jako schod. Je-li `Text` zadaný, má přednost před `Format`; jinak se hodnota zobrazí přes
`Format`.

Sloupce ve fázi 1 (z toho, co záznamy nesou):

| Zpráva | Sloupce |
|---|---|
| [`RobotStateMsg`](../Src/ARBot.Common/Logs/RobotStateMsg.cs) | X, Y, Θ [°], v [m/s], ω [°/s] |
| [`DriveCommandMsg`](../Src/ARBot.Common/Logs/DriveCommandMsg.cs) | Speed, RotationSpeed, Forvard, Dif, EmergencyStop |
| [`LocalPlanMsg`](../Src/ARBot.Common/Logs/LocalPlanMsg.cs) | Status, délka, min. odstup, počet bodů, doba výpočtu, expandované buňky |
| [`GlobalNavMsg`](../Src/ARBot.Common/Logs/GlobalNavMsg.cs) | Status, zbývající trasa [m], počet hran, off-route, φ, počet uzavření |
| [`GPSState`](../Src/ARBot.Common/Devices/GPSState.cs) | lat, lon, kvalita fixu, satelity, HDOP |

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

Skener si otevírá **vlastní** read-only stream nad souborem záznamu (soubor je otevřený s
`FileShare.Read`), takže **nekoliduje s přehráváním** a nevyžaduje změnu `FileMessageSource`.
Index bere z `FileSource.Index`, pokud je k dispozici, jinak si přečte sidecar `*.idx` sám.

Běží na worker vlákně, hlásí postup a je zrušitelný (`CancellationToken`) — zavření dokumentu nebo
otevření jiného záznamu sken ukončí.

**Záznam bez sidecar indexu tabulku nepodpoří** (nejsou offsety ani časová osa). Hlásí se to
hláškou; teoretický fallback — sekvenční průchod celým souborem — by musel deserializovat i obrázky,
takže se do fáze 1 nedělá.

## UI — tabulka a detail

Nový dokument `TelemetryDocument` (menu **Tools**), tab jako World.

- **Tučně = hodnota právě přišla**, obyčejně = drží se z minula, prázdná buňka = ta zpráva zatím
  nepřišla. Na jeden pohled je tak vidět, co je nové a co stará hodnota.
- **Detail řádku** (panel u tabulky): všechny sloupce s hodnotou, časem a **stářím** vůči řádku
  („v = 1,20 m/s · 12:34:56.789 · o 0,8 s starší než řádek"), plus zpráva, která řádek založila
  (`Seq`, typ, T_in, T_out).
- **Napojení na Replay:** dvojklik na řádek zavolá `SeekTo(RowSeq)`; naopak kurzor přehrávání
  zvýrazní odpovídající řádek. Tabulka a [Replay panel](record-replay.md) tak spolupracují.
- **Výběr viditelných sloupců** — registr bude delší než obrazovka.
- **Filtr řádků podle typu zakládající zprávy** (nepovinné, ale levné — `RowMsgName` už v tabulce
  je): zapnutím jen `DriveCommandMsg` se z tabulky stane „jeden řádek = jeden takt řídicí smyčky",
  ostatní hodnoty se drží. Filtruje se **zobrazení**, sken i tabulka zůstávají celé.
- **Backpressure** se tady neřeší: data nepřicházejí ze `Stream`u, ale z jednorázového skenu.
  (Až přijde Run, platí povinný vzor z [Views/README.md](../Src/ARBot/Views/README.md).)

Vykreslení: **`Avalonia.Controls.DataGrid` 12.0.0** (virtualizace, měnitelné šířky sloupců).
Pozor na verzi — 12.0.1 a novější si vynucují Avalonia ≥ 12.0.5, kdežto projekt drží 12.0.3, takže
by build spadl na `NU1605`. Balíček potřebuje i `StyleInclude` svého tématu v `App.axaml`, jinak se
tabulka vykreslí jako prázdné místo. Sloupce se staví **v code-behind** (je jich desítky a jsou
datově řízené registrem) a jsou to `DataGridTemplateColumn`, protože textový sloupec neumí tučně
jen u některých buněk.

## Fáze 2 — grafy (jen připraveno)

Ikonka u číselného sloupce přidá jeho řadu do grafu. Graf je další dokument: osa X = čas, víc řad,
volitelně druhá osa Y. **Schod vs. rampa** se řídí příznakem *fresh*: držené hodnoty jako schod
(hodnota platí až do další zprávy), hustá data jako rampa; přepínač per řada.

Sloupcová pole jsou přesně to, co kreslení potřebuje, takže volba knihovny (vlastní
`Control.Render` — projekt už vlastní kreslené controly má — vs. externí balíček) zůstává na fázi 2
a tento návrh ji nepředjímá.

## Co ve zprávách chybí

Ze zadání jsou tři údaje, které dnes v žádné zprávě nejsou:

| Údaj | Stav | Co pro něj udělat |
|---|---|---|
| Vzdálenost do cíle | **je** — `GlobalNavMsg.RouteLengthM` (po síti; vzdušná ne) | jen sloupec v registru |
| Max. povolená rychlost z plánovače | **není ve zprávě** — `LocalPlanResult` ji zná (`MinVClear`, `MinVBrake`, `MinWayPointSpeed`, `SpeedLimitedBy`), ale `ToLogMessage`/`ToData` ji nepřenášejí | rozšířit `LocalPlanMsg` + verze +1 (samostatný krok **po** jádru) |
| Plánované odbočení | **neexistuje nikde** | musí ho začít počítat globální navigace (manévr na trase), pak nová položka v `GlobalNavMsg` |

Tabulka je proti tomu odolná: jakmile hodnota ve zprávě bude, je to jeden řádek v registru.

## Testy

Jádro je v `ARBot.Common` právě proto, aby šlo testovat — `ARBot` (UI) testovací projekt nemá.
`ARBot.Common.Tests/Telemetry/`: záznam se složí do `MemoryStream` přes `RecordingTarget`
(data + index), pak se přeskenuje. Ověřuje se:

- držení hodnot (sloupec z pomalé zprávy má na následujících řádcích tutéž hodnotu i čas),
- *fresh* na správných řádcích a **jen** na nich,
- prázdné buňky před první zprávou daného typu,
- čas řádku: `CaptureTicks`, a fallback na `ArrivalTicks` u zprávy bez `IHasCaptureTime`,
- slévání řádků při shodném čase,
- přeskočení neregistrovaných typů (ověřitelné tím, že se jejich rámce nečtou),
- zrušení skenu (`CancellationToken`) a strop na počet řádků.

## Zásahy do stávajícího kódu

Návrh je záměrně navržený tak, aby jich bylo minimum:

- **`ARBotRuntime`** — vystavit cestu k přehrávanému záznamu (`RecordPath`); dnes je jen lokální
  proměnná v `StartView`. Bez ní si skener nemá co otevřít.
- **`MainWindowViewModel`** — položka menu Tools → Telemetrie, otevření dokumentu.
- **`FileMessageSource`** — **žádná změna** (skener má vlastní stream, index je už public).
- **`CLAUDE.md`** — odkaz na tento dokument.

## Fáze a kroky

1. **Jádro + testy** — `TelemetryTable`, `ColumnSpec`, `TelemetryScanner` v `ARBot.Common/Telemetry`,
   testy v `ARBot.Common.Tests`. Bez UI.
2. **Registr sloupců** — `TelemetryColumns` v `ARBot` pro zprávy z tabulky výše.
3. **UI: tabulka** — dokument, virtualizovaná tabulka, tučné/obyčejné/prázdné buňky, výběr sloupců,
   sken na pozadí s postupem.
4. **UI: detail řádku + napojení na Replay** (dvojklik = seek, kurzor = zvýraznění).
5. **Rozšíření `LocalPlanMsg`** o rychlostní diagnostiku (+ verze) — až po jádru.
6. **Fáze 2: grafy** — volba kreslení, dokument grafu, přidávání řad z tabulky.
7. **Později: Run** — živé plnění (buď „ocas" zapisovaného záznamu, nebo odběr `Stream`u
   s povinným backpressure vzorem).

Po každém kroku build `-p:Platform=x64` a zelené testy (viz [CLAUDE.md](../CLAUDE.md)).

## Otevřené otázky a rizika

- **`Avalonia.Controls.DataGrid` pro Avalonia 12** — existuje? Neověřeno. Fallback je popsaný výše.
- **Rychlost skenu** — náhodné čtení desítek tisíc malých rámců na OrangePI (SD karta) může být
  pomalé. Změřit; kdyby to vadilo, přejít na sekvenční čtení s přeskakováním (`Seek` přes
  neregistrované rámce) nebo na plnění tabulky průběžně během skenu.
- **Časy z jednoho taktu** — slévání funguje na přesnou shodu. Jestli se v praxi časy z jednoho
  taktu rozcházejí, bude potřeba tolerance (a je pak otázka, jaká, aby neslila dva různé takty).
- **Strop řádků vs. dlouhý běh** — hodinový záznam na 65 zpráv/s je ~230 000 řádků; vejde se do
  stropu, ale je to ~110 MB. Případné řešení je rozsahový filtr (skenovat jen výsek času).
