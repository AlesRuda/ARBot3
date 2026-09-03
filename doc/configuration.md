# Konfigurace aplikace — parametry, profily, panel

> **Stav 2026-09-01:** **hotové a otestované** (`ARBot.Common/Configuration`, panel *Tools →
> Konfigurace*). Jádro má **77 testů**, celá sada je zelená (1065). Registr obsahuje **57 parametrů**
> a strážný test hlídá, že se neroze­jde se zdrojovým kódem.
>
> **Ověřeno za běhu:** aplikace nastartuje s profilem (`config=`), bezobslužný self-test s ním
> proběhl celý; vadný profil aplikaci **zastaví před startem GUI** a vypíše **všechny** vady naráz
> i s tím, co se u každé čekalo:
> `'start=asd': cekam 'lat,lon[,kurzDeg]' ve stupnich, nebo 'gps'; 'mission=robotur': cekam jednu
> z hodnot: none | freerun | robotour; 'wheelslip=0,1': prvni cislo musi byt vetsi nez 0`.
>
> **Panel autor proklikal a potvrdil** (31. 8. 2026): tabulka, rozbalovací seznamy u výčtů,
> editace hodnot, *Načíst profil…* i *Uložit* (vzniklý profil má komentáře s popisy, nadpisy
> kategorií a jen hodnoty odlišné od výchozích), a hlášení chyb ve vstupních polích — červený
> rámeček a bublina při najetí myší i při zaostření.
>
> ***Uložit a restartovat* je funkční** (ověřil autor 1. 9. 2026) — tím je panel proklikaný celý.
>
> **Všechno výše je ověřené na Windows.** **Neověřeno:** nic z toho neběželo **na zařízení**
> (Armbian/OrangePI). U restartu na tom záleží víc než jinde: **systemd jednotka aplikace
> neexistuje** (`setup-orangepi.sh` řeší jen síť), takže větev „pod systemd jen skonči" tam pořád
> nemá jak nastat a chování na Pi může být jiné než tady.
>
> Postup implementace: [plan-configuration.md](plan-configuration.md).

**Výchozí stav (do 31. 8. 2026):** aplikace se konfigurovala **výhradně z příkazové řádky** přes
`Program.GetParam*` ([Program.cs](../Src/ARBot/Program.cs)) a klíč nikde neexistoval jako věc — byl
to string literál na místě čtení (`Program.GetParamBool("mapcorr", false)`), a těch míst je ~50,
hlavně v [ARBotRuntime.cs](../Src/ARBot/Robot/ARBotRuntime.cs),
[ARBotHW.cs](../Src/ARBot/Robot/ARBotHW.cs) a [SelfTest.cs](../Src/ARBot/Diagnostics/SelfTest.cs).

Z toho plynuly dvě bolesti, které tenhle návrh řeší:

1. **Dlouhá příkazová řádka.** Běh se deseti přepínači se na zařízení zadává přes SSH a nikde
   nezůstane. Řešením jsou **pojmenované profily** v souboru.
2. **Neobjevitelnost.** Jaké parametry vůbec existují a co znamenají, se dá zjistit jen grepem
   ve zdrojácích. Řešením je **registr parametrů** a panel, který ho vypíše.

Obojí stojí na téže věci — na registru. Bez něj nejde ani vypsat, co lze nastavit, ani ohlásit
překlep v klíči.

## Rozsah

**Uvnitř:** parametry, které se dnes čtou z příkazové řádky přes `Program.GetParam*`.

**Mimo rozsah** (každé z toho je samostatná úloha a ani jedno neplyne ze zadání):

| Co | Proč ne |
|---|---|
| `Profile` a kalibrace robota | Jiná třída nastavení (mění se zřídka, patří k železu). Navíc má **odvozená statická pole** (`WheelPerimeter` z `WheelRadius`, `LeftCameraTransform` z `CameraYaw`) — načtení hodnot po statické inicializaci by je nechalo staré. Past, kterou tenhle návrh nemusí otevírat. **Dvě výjimky:** `maxspeed=` (1. 9. 2026) nastavuje `Profile.MaxAllowedSpeed` a `safedist=` (3. 9. 2026) `Profile.SafeDist` — jde to bezpečně, protože z těch polí **nic nederivuje** (viz [Strop rychlosti](#strop-rychlosti-maxspeed) a [Bezpečný odstup](#bezpečný-odstup-safedist)). Zbytek `Profile` mimo registr zůstává. |
| Živé přepínání za běhu | Vyloučené zadáním. Editor ukládá hodnoty **pro příští start**. |
| Skládání profilů (`include=`) | YAGNI — přepis z příkazové řádky zatím stačí. |
| Strukturovaný snímek konfigurace do záznamu | `GetParam` už dnes dělá `Debug.WriteLine("klíč=hodnota")`, což jde do záznamu jako [`Info`](../Src/ARBot.Common/Logs/Info.cs). Je to jen text a jen pro klíče, které se skutečně přečetly, ale stopa tam je — zisk ze strukturované zprávy je proto menší, než se na první pohled zdá. |
| Per-komponentní configy (`FusionConfig`, `MapCorrelatorConfig`, `CorridorConfig`, `VirtualHWOptions`) | Zůstávají, jak jsou. Runtime je plní z parametrů; ten kód se nemění. |

## Klíčové rozhodnutí: `GetParam*` si nechá signaturu

`Program.GetParam`, `GetParamDouble`, `GetParamBool` a `GetParamPath` **zůstanou beze změny
zvenčí**. Uvnitř jen přestanou sahat přímo na `Environment.GetCommandLineArgs()` a začnou se ptát
nové vrstvy.

Důsledek: **žádné z těch ~50 míst čtení se nemění.** Migrace je tím levná a nehrozí, že se při
přepisování něco tiše rozejde. `Profile` ani runtime se nedotýkáme vůbec.

## Rozvržení souborů

| Soubor | Odpovědnost |
|---|---|
| `ARBot.Common/Configuration/ParamDef.cs` | popis jednoho parametru: jméno, typ, default, popis, kategorie, výčet, rozbor |
| `ARBot.Common/Configuration/ParamParsers.cs` | rozbor složených hodnot — **týž kód používá registr i runtime** |
| `ARBot.Common/Configuration/ParamRegistry.cs` | statický seznam **všech** parametrů — jediné místo, kde parametr vzniká |
| `ARBot.Common/Configuration/ParamStore.cs` | účinné hodnoty **a jejich původ**; sestaví se jednou při startu |
| `ARBot.Common/Configuration/ParamFile.cs` | čtení a zápis souboru `klíč=hodnota` |
| `ARBot.Common/Configuration/RepoPaths.cs` | hledání kořene repa (přesun z `Program`, viz níž) |
| `ARBot/ViewModels/ConfigurationDocument.cs` | ViewModel panelu (+ `ParamRow` a jeho dva typy — viz níž) |
| `ARBot/Views/ConfigurationDocumentView.axaml` (+ `.cs`) | View panelu |
| `ARBot.Common.Tests/Configuration/*` | testy parseru, precedence, registru |

Registr žije v `ARBot.Common`, ne v `ARBot` — směr závislostí (`Common ← HAL ← app`) to dovoluje
a testovací projekt `ARBot.Common.Tests` na `ARBot` referenci **nemá**, takže jinak by registr
nešlo testovat.

### Přesun hledání kořene repa do `Common`

`Program.RepoRootOrBase()` (hledá složku s `.git` směrem nahoru od build outputu, fallback na
`AppContext.BaseDirectory`) se přesune do `ARBot.Common/Configuration/RepoPaths.cs` a `Program` na
něj bude delegovat. Není to kosmetika — potřebují ho dvě nové věci v `Common`:

- `ParamStore` musí umět rozřešit `config=` a všechny parametry typu *cesta* (dnes to dělá
  `GetParamPath` v `Program`);
- strážný test skenuje zdrojáky pod `Src/ARBot`, takže musí najít kořen repa, a na `Program` nevidí.

## Model parametru

```csharp
public sealed class ParamDef
{
    public string Name;            // "mapcorr" - porovnává se case-insensitive, jako dnes
    public ParamType Type;         // Bool | Double | String | Path
    public string Default;         // v textové podobě, jak by stálo v souboru
    public bool DefaultFromCode;   // default určuje kód za běhu (detekce portů) - viz níž
    public string Description;     // věta pro panel i pro komentář v souboru
    public string Category;        // "Fúze", "Mise", "Virtuální HW", "Self-test", ...
    public string[] AllowedValues; // úplný výčet (mission: none|freerun|robotour) — viz níž
    public Func<string, ParamParseResult> Parse;   // rozbor složené hodnoty i s důvodem odmítnutí
}
```

Default je v registru uložený **textově** — je to táž hodnota, jaká by stála v souboru, takže
zápis profilu i výpis v panelu používají jednu cestu a nemůžou se rozejít o formátování čísla.

### Dvojí zápis defaultu

Default bude nově na dvou místech: v registru a dál i v samotném volání
(`GetParamBool("mapcorr", false)`). Neshodu **ověří `ParamStore` za běhu**: v Debug buildu vyhodí
výjimku, v Release zaloguje varování. Defaulty z volání se dají později odstranit, ale ne v prvním
kroku — bylo by to ~50 změn a přesně to, čemu se zachováním signatury vyhýbáme.

## Precedence

```
default z registru  →  soubor (config=…)  →  příkazová řádka
```

Příkazová řádka přebíjí soubor **záměrně**: jinak by přestalo platit existující skriptované A/B
měření a vzniklá past („proč mi `mapcorr=true` nic nedělá") by byla tichá.

`ParamStore` si u každého klíče pamatuje **původ** (default / soubor / příkazová řádka). Panel ho
zobrazí — „proč to má tuhle hodnotu" je stejně častá otázka jako „co to vůbec je".

## Chování při chybě

Tohle je změna proti dnešku a je vědomá: konfigurace bude **upovídanější**.

| Situace | Dnes | Nově |
|---|---|---|
| Neznámý klíč v **souboru** | — | **Chyba při startu**, hláška a konec |
| Neznámý klíč na **příkazové řádce** | tiše ignorován | Hlasité varování, běh pokračuje |
| Neplatná hodnota (`mapcorr=ano`) | tiše default | **Chyba při startu** |
| Chybějící soubor z `config=` | — | **Chyba při startu** |

Vady profilu se hlásí **všechny naráz**, ne první nalezená — jinak by se profil opravoval po jedné
a mezi každou opravou startovalo. Pravidla platnosti drží `ParamRegistry.Validate()` jako **jediné
místo**: používá je start i načtení profilu v panelu, takže panel nemůže načíst profil, který by
aplikace při startu odmítla.

### Výčty a složené hodnoty

Typ (`Bool`/`Double`) odchytí jen část chyb — `start=asd` je pro `String` naprosto platná hodnota,
kterou runtime teprve zahodí. `ParamDef` proto umí dvě další věci:

- **`AllowedValues`** — úplný výčet (`mission`: none | freerun | robotour, `mapcorrgate`,
  `camerapose`). Vedle validace nese **informaci pro UI**: panel z něj dělá **rozbalovací seznam**
  místo textového pole. ⚠️ Výčet musí odpovídat tomu, co kód skutečně zná (u `mission` je to
  `switch` v `ARBotRuntime`) — automaticky to nikdo nehlídá.
- **`Parse`** — lambda pro složené hodnoty, která vrací i **důvod** odmítnutí, takže hláška řekne,
  co se čekalo, ne jen že je hodnota špatně.

**Ta lambda musí volat týž kód, jaký použije runtime při skutečném čtení** — jinak by jen přesunula
dvojí definici formátu jinam a panel by přijímal hodnoty, které runtime zahodí. Proto vzniklo
[`ParamParsers`](../Src/ARBot.Common/Configuration/ParamParsers.cs) v `Common` a
`ARBotRuntime.TryParsePair` i rozbor `start=` na něj **delegují**.

Ukázka toho, co to chytí (všechno naráz, jednou hláškou):

```
'start=asd': cekam 'lat,lon[,kurzDeg]' ve stupnich, nebo 'gps';
'mission=robotur': cekam jednu z hodnot: none | freerun | robotour;
'wheelslip=0,1': prvni cislo musi byt vetsi nez 0 (vlevo,vpravo).
```

> **Změna chování, se kterou se počítá:** hodnotu, kterou runtime dosud jen zahodil s hláškou
> (`wheelslip=0,1`), teď **zastaví start**. Je to záměr — tiše ignorovaná hodnota je tatáž past
> jako překlep v klíči. Runtime si své kontroly ponechal jako druhou obranu; po validaci ve
> `ParamStore` už selhat nemůžou.
>
> **Prázdná hodnota výčet ani rozbor nespouští** — `qrcamera=` znamená „všechny kamery" a je
> legitimní.

> ⚠️ **V buňce sloupce *Hodnota* musí být právě JEDEN prvek**, vybraný podle typu řádku
> (`ChoiceParamRow` → `ComboBox`, `TextParamRow` → `TextBox`, přes `UserControl.DataTemplates`).
> **Nevracet zpět na jednu šablonu se dvěma prvky přepínanými `IsVisible`** — přesně tak to bylo
> napsané poprvé a byla to vada, která **ztrácela data**: když `DataGrid` při virtualizaci
> recykloval kontejner z řádku *s* výčtem na řádek *bez* něj, dostal skrytý `ComboBox` prázdný
> `ItemsSource`, svou hodnotu v něm nenašel, nastavil `SelectedItem = null` — a obousměrný binding
> to zapsal **zpátky do `Value`**. Uložený profil pak tu hodnotu už neobsahoval.
>
> **Reprodukce:** *nemaximalizované* okno + scroll na dotčený řádek. V maximalizovaném okně se
> recyklace nekoná a vada se neprojeví — to bylo i vodítko, které ji odhalilo. Nalezeno
> 31. 8. 2026.
>
> Sloupec má `CellTemplate` i `CellEditingTemplate` (obě `ContentControl`): prvek uvnitř je sám
> interaktivní, takže editační režim `DataGrid`u nemá co přidat, ale když do něj přepne, musí mít
> co vykreslit.

**Kanonizace (`ParamDef.Canonical`) je podmínkou toho seznamu.** Validace výčtu je
case-insensitive, takže `mission=NONE` z profilu projde — ale rozbalovací seznam porovnává hodnoty
**přesně**, takže by nevybral žádnou, ukázal prázdno a při uložení by se hodnota **ztratila**.
Panel proto hodnotu při plnění tabulky převede na tvar zapsaný ve výčtu. Ověřeno i za běhu:
profil s `mission=FREERUN` a `camerapose=TRUTH` projde celým self-testem.

**Směr, který tím vzniká:** až se bude `Program.GetParam*` upravovat, dá se opustit úplně
a číst přes `ParamStore` — parsování, validace i prezentace by pak byly na jednom místě pro celou
aplikaci. Zatím zůstává jako tenká fasáda (viz „Klíčové rozhodnutí" výš).

Rozdíl mezi souborem a příkazovou řádkou u neznámého klíče není nedůslednost: mezi `args` jsou
i cizí argumenty (cesta k exe, argumenty Avalonie), takže tvrdá chyba by aplikaci znemožnila
spustit. V souboru žádné cizí klíče být nemají.

To, že `GetParamBool` dnes při neparsovatelné hodnotě tiše vrátí default, je stejná past jako
překlep v klíči — obojí registr odstraňuje.

## Formát souboru

Řádek na klíč, `klíč=hodnota`, `#` uvozuje komentář, prázdné řádky se ignorují. Tedy **přesně to,
co by se jinak napsalo na příkazovou řádku**, jen po řádcích. Jedna sémantika, žádné mapování,
edituje se v `nano` přes SSH a diff v gitu je čitelný.

Zápis z panelu bere pořadí a kategorie z registru a **ke každému klíči píše popis jako komentář**.
Profil je tím sám o sobě dokumentací parametrů — půlka objevitelnosti funguje i bez panelu:

```
# --- Fúze ---
# Zapina korelaci occupancy gridu s mapou (doc/map-correlation-localization.md).
mapcorr=true
# Posilat korekce do fuze, nebo jen merit.
mapcorrsend=true

# --- Mise ---
# Vyber mise: none | freerun | robotour.
mission=robotour
```

## Umístění profilů

Profily jsou v `config/` v kořeni repa a volají se `config=config/robotour.cfg`. Cesta se rozřeší
stejným pravidlem, jaké dnes používá `GetParamPath`: relativně proti kořeni repa, s fallbackem na
`AppContext.BaseDirectory` pro nasazení bez repa. Profily jdou do gitu, což sedí na pravidlo
„vše v repozitáři" z [CLAUDE.md](../CLAUDE.md).

⚠️ **Ten fallback sám ale nestačil a do 1. 9. 2026 profily na zařízení nefungovaly.** Na Pi se
nasazuje **jen build output**, kde není `.git` ani `config/` ani `OSM/` — `RootOrBase()` tedy
spadne na adresář aplikace a `config=config/pi-freerun.cfg` tam ukazuje na neexistující soubor,
což je **chyba při startu**. Ověřeno na zařízení: `~/arbot` žádné `config/` nemělo.

Léčba: `ARBot.csproj` kopíruje **`config/*.cfg` i `OSM/*.osm` do build outputu**
(`CopyToOutputDirectory="PreserveNewest"`, `LinkBase`), takže obojí cestuje s aplikací a totéž
zadání funguje na vývojovém stroji i na zařízení. OSM je ~30 MB v 17 souborech — kdyby to
u nasazení vadilo, zúžit výčet na mapy, na které se profily opravdu odkazují.

**Profily v repu hlídá test** (`ProfilyVRepuTests`): každý `config/*.cfg` musí projít registrem
(žádný neznámý klíč, žádná neplatná hodnota) a každá hodnota typu `Path` musí ukazovat na
existující soubor — registr kontroluje jen **tvar** cesty, takže `map=OSM/PreklepVeJmenu.osm`
by jinak prošlo a spadlo až na zařízení.

## Záznam běhu z profilu (`record=`)

`record=true` založí při startu režimu **Run** záznam `records/yyyyMMdd-HHmmss.rec` — tamtéž a pod
týmž jménem jako tlačítko *Run + záznam* v UI. Místo `true` jde zadat cestu k `.rec` souboru;
prázdná hodnota nebo `false` znamená bez záznamu.

**Nač to je:** na zařízení se aplikace pouští přes SSH profilem a k rozboru běhu
(`ARBot.Analyze`) je záznam potřeba — do té doby se dal zapnout jen ručně z UI.

**Pořadí:** cesta předaná volajícím (tlačítko *Run + záznam*) profil **přebíjí** — výslovná volba
člověka nad nastavením, stejně jako příkazová řádka nad profilem. Řeší to jedno místo
(`ARBotRuntime.Start`), takže na tom nezáleží, odkud se Run spustil.

## Strop rychlosti (`maxspeed=`)

`maxspeed=` [m/s] se při startu přenese do `Profile.MaxAllowedSpeed`, takže platí pro **celé
řízení naráz**: driver motoru (`SDC2160Ex`), rychlostní profil řídicí smyčky
(`TrapezoidMotionProfile`) i rychlostní obálku lokálního plánovače (`LocalPlannerConfig.MaxSpeed`).
Bez zadání platí hodnota z kódu (dnes 1,2 m/s).

**Proč se to děje už v `Program.Main`, ne až u čtenáře:** všechna tři místa hodnotu čtou při
**vzniku objektu** — dvě v konstruktoru, `LocalPlannerConfig.MaxSpeed` inicializátorem pole. Kdo by
vznikl dřív, než se hodnota nastaví, držel by starou a strop by platil jen zčásti; u bezpečnostního
omezení je to nejhorší možný výsledek.

**Past s odvozenými statickými poli** — kvůli které `Profile` v registru [záměrně není](#rozsah) —
se tady **neotevírá**: z `MaxAllowedSpeed` nic nederivuje (`MaxTheoreticalSpeed` se počítá z obvodu
kola a otáček motoru, ne z něj). Proto jde zpřístupnit tenhle jeden údaj, aniž by se otvíral celý
`Profile`.

Nekladnou hodnotu odmítne **registr** už při načtení profilu (`cekam cislo vetsi nez 0`). Hodnota
nad `Profile.MaxTheoreticalSpeed` se **ořízne s hláškou** — záměr „jeď naplno" je jednoznačný
a odmítnout kvůli tomu start robota v terénu by bylo horší než ho zpomalit.

## Bezpečný odstup (`safedist=`)

`safedist=` [m] se při startu přenese do `Profile.SafeDist`, odkud si ho při vzniku bere
`LocalPlannerConfig.SafeDist` — **tvrdý** minimální odstup od překážek lokálního plánovače (blíže je
neprůjezdno) a zároveň dolní mez rychlostní obálky `v_clear`. Bez zadání platí hodnota z kódu
(dnes 0,7 m). Přidáno 3. 9. 2026, když se odstup ladil při rozboru mrkve v nesjízdné oblasti a měnil
se přepisováním kódu.

Mechanismus je **stejný jako u `maxspeed=`** a platí i tytéž důvody: hodnota se čte při vzniku
objektu, proto se nastavuje v `Program.Main` před složením runtime; z `SafeDist` nic staticky
nederivuje, takže se past s odvozenými poli neotvírá.

**Vazba na `PrefDist`.** `LocalPlannerConfig.Validate()` vyžaduje `PrefDist > SafeDist` (mezi nimi se
lineárně snižuje rychlost). Kdyby `safedist=` skončil na `PrefDist` (0,8 m) nebo nad ním, plánovač by
při vzniku vyhodil výjimku a runtime by se nesložil. Proto `Program` v takovém případě **posune
`PrefDist` nad nový odstup se zachovaným rozestupem** (dnes 0,1 m) a zaloguje to do `Trace`. Je to
druhé pole `Profile`, kterého se parametr dotkne, a děje se to jen v tomto případě.

Nekladnou hodnotu odmítne **registr** už při načtení profilu (`cekam cislo vetsi nez 0`).

> ⚠️ Odstup nezaručí, že robot k překážce nedojede: úniková zóna kolem aktuální buňky
> (`EscapeRadius`) odstup slevuje a posouvá se s robotem, takže k okraji cesty se dá „doplížit"
> po buňce s libovolně velkým `safedist` — viz [devlog.md](devlog.md), 3. 9. 2026.

## Bezobslužný start (`autorun=`)

`autorun=true` spustí režim **Run** sám po startu aplikace — po připravení HW (`WaitReady`)
a krátkém ustálení, stejným postupem jako self-test. Doplňuje `mission=` a `record=`: profil tak
popíše celý běh od startu po záznam a v UI se nemusí nic klikat, což je na zařízení pouštěném
přes SSH podstatné.

> ⚠️ **Se zapnutou misí se robot rozjede sám**, bez dalšího pokynu; zastaví ho jen nouzové
> zastavení nebo *Stop* v UI. Prodleva před startem (~3 s) je na **ustálení**, ne bezpečnostní —
> skutečná pojistka je fyzické nouzové zastavení. Výchozí hodnota je `false`.

Při `selftest=true` se `autorun` **ignoruje** (a zapíše se proč): self-test si Run spouští sám
a druhý start by první zastavil.

Kód: [`MainWindowViewModel.AutoRun.cs`](../Src/ARBot/ViewModels/MainWindowViewModel.AutoRun.cs).

## Panel *Tools → Konfigurace*

`ToolBase` + `ViewType` podle konvence v [Views/README.md](../Src/ARBot/Views/README.md), tlačítka
přes třídy `btn` ze `Styles/Buttons.axaml`, konstruktor musí být bezpečný v design-time režimu
(`Design.IsDesignMode` — panel v návrháři nesmí číst soubory).

Obsah:

- **Tabulka** kategorie / klíč / popis / hodnota / **původ**.
- **Bublina nad celým řádkem** s popisem, **typem** a výchozí hodnotou. Sloupec s popisem je úzký
  a dlouhý popis se do něj nevejde; navíc ho člověk potřebuje vidět zrovna ve chvíli, kdy najede
  na sloupec *Hodnota*, ne na *Popis*. Typ se v tabulce vlastní sloupec nedostal — ubral by místo
  hodnotě — takže bublina je jediné místo, kde je vidět.
- **Fulltextový filtr** přes jméno i popis.
- **Editace hodnoty** s validací podle typu z registru (chyba se ukáže hned, ne až při startu).
- **Načíst profil…** — dialogem vybraný `.cfg` naplní tabulku. **Nic nespustí**: hodnoty začnou
  platit až po restartu. Co v profilu **není**, se vrátí na výchozí, aby tabulka ukazovala přesně
  to, jak by aplikace s tím profilem startovala — přimíchání k současnému stavu by dalo výsledek,
  který neodpovídá žádné skutečné konfiguraci. Vadný profil se **nenačte vůbec** (tabulka nezůstane
  napůl přepsaná) a stav vypíše všechny vady naráz. Po načtení se v *Původu* píše
  **„profil (načteno)"**, ne „profil" — běžící aplikace jede pořád se starou konfigurací a sloupec
  by jinak tiše lhal.
- **Uložit** — zapíše do souboru uvedeného v cestě, bez ptaní. Když žádný není (aplikace neběží
  s `config=` a nic se nenačetlo), zeptá se — tedy chová se jako *Uložit jako*, stejně jako každý
  editor.
- **Uložit jako…** — vždy se zeptá dialogem.
- **Uložit a restartovat.**

Pole s cestou je hlavně **informace, kam půjde *Uložit*** — bez ní by tlačítko zapisovalo neznámo
kam. Zůstává editovatelné záměrně: v prostředí bez správce souborů je to jediná cesta, jak profil
určit bez dialogu. Zavření dialogu ukládání **zruší**, nespadne na náhradní cestu — jinak by
„Zrušit" tiše někam zapsalo.

Do profilu se zapisují **jen hodnoty odlišné od defaultu** (s popisem v komentáři). Soubor tím
zůstane krátký a je z něj vidět, co se na tomhle běhu vlastně mění; úplný výčet je úlohou panelu,
ne profilu. Ukládají se přitom **účinné** hodnoty, tedy včetně těch, které přišly z příkazové
řádky — jinak by se právě to, kvůli čemu se profil zakládá, do souboru nedostalo.

Panel zobrazuje **celý registr**, ne jen klíče, které se v tomhle běhu přečetly — proto je registr
centrální deklarace, ne samoregistrace při čtení. Při `mission=robotour` musí být vidět i parametry
FreeRunu.

## Restart z panelu

`Process.Start(Environment.ProcessPath, args)` s `config=<uložený profil>`, pak `Shutdown()`.

**Předá se jen `config=`, původní argumenty se zahodí.** Kdyby se přenesly, přebily by podle
precedence právě uloženou hodnotu a tlačítko by nedělalo, co slibuje. Je to bezpečné právě proto,
že se do profilu ukládají účinné hodnoty (viz výš) — nic se ztratit nemůže.

⚠️ **Past se `systemd`:** pod službou s `Restart=always` by spuštění vlastní kopie vyrobilo **dvě
instance**. Detekce přes proměnnou prostředí `INVOCATION_ID`, kterou `systemd` službě nastavuje:
je-li přítomná, aplikace se jen ukončí a restart nechá na `systemd`; jinak nastartuje sama sebe.
> **Zjištěno při implementaci: žádná systemd jednotka aplikace zatím neexistuje.**
> `OrangePi5Ultra/setup-orangepi.sh` řeší jen síť (hostapd, AP, dnsmasq) — aplikace se na Pi
> spouští ručně. Ta větev tedy nikdy nenastane a je to obrana do budoucna. **Až jednotka vznikne,
> musí mít `Restart=always`** — jinak by tlačítko aplikaci vyplo a už ji nezaplo.

Potvrzovací dialog při běžícím Run nebo misi **zatím není** — tlačítko restartuje rovnou. Je to
vědomý dluh: dialog by chtěl vlastní okno a restart je akce, kterou člověk dělá záměrně.

## Strážný test

Test v `ARBot.Common.Tests` proskenuje `.cs` soubory pod `Src/ARBot` regulárním výrazem na volání
s literálem v prvním argumentu a porovná **obousměrně** s registrem:

- klíč čtený v kódu, který není v registru → test padne (někdo přidal parametr a zapomněl ho
  deklarovat);
- klíč v registru, který nikdo nečte → test padne (mrtvá deklarace).

Test čte zdrojáky jako soubory, takže nepotřebuje referenci na projekt `ARBot`. Kořen repa hledá
přes `RepoPaths`; když repo není (běh na zařízení), test se přeskočí.

**Skenuje se šest vzorů, ne jen `GetParam*`.** `ARBotRuntime` má dva vlastní pomocníky —
`ReadDouble(name, fallback)` a `TryReadMeters(name, out)`
([ARBotRuntime.cs:1096](../Src/ARBot/Robot/ARBotRuntime.cs:1096)) — které `Program.GetParam` volají
s **proměnnou**. Literál je až na místě volání toho pomocníka (`ReadDouble("mapcorrref", …)`,
`freerunlook`, `depotfix`, `depthnoise`, `grassrough`, `grassheight`). Test proto hledá
`GetParam(`, `GetParamBool(`, `GetParamDouble(`, `GetParamPath(`, `ReadDouble(` a `TryReadMeters(`.

Aby se nepřímost nerozšířila nepozorovaně, test navíc ověří, že mimo tělo těch dvou pomocníků
**neexistuje volání `Program.GetParam*` s ne-literálním prvním argumentem**. Kdyby vzniklo, test
padne a řešením je pomocníka přidat mezi skenované vzory — ne test vypnout.

### Dynamické defaulty

Ne každý default je konstanta. `Program.GetParam("UartAHRS", portAHRS)`
([ARBotHW.cs:351](../Src/ARBot/Robot/ARBotHW.cs:351)) má default z **detekce portů za běhu**, tedy
z proměnné. `ParamDef` proto rozlišuje default konstantní od defaultu určeného kódem
(`DefaultFromCode`): u těch druhých se v panelu místo hodnoty zobrazí popis („podle detekce portů"),
do profilu se nezapisují, dokud je někdo výslovně nenastaví, a kontrola shody defaultu se u nich
přeskočí.

## Testy

| Co | Kde |
|---|---|
| parser souboru: komentáře, mezery kolem `=`, prázdné řádky, duplicitní klíč, hodnota s `=` uvnitř | `ParamFileTests` |
| precedence default → soubor → příkazová řádka | `ParamStoreTests` |
| chybové stavy: neznámý klíč, neplatná hodnota, chybějící soubor | `ParamStoreTests` |
| round-trip: zapsat profil → přečíst → stejné hodnoty | `ParamFileTests` |
| shoda registru se zdrojovým kódem | `ParamRegistryGuardTests` |
| kontrola shody defaultu v registru a ve volání | `ParamStoreTests` |

Build i testy vždy pod konkrétní platformou (`-p:Platform=x64`), nikdy `AnyCPU`.

## Co se rozhodne až podle zkušenosti

- **Zda defaulty z volání `GetParam*` nakonec odstranit** a nechat je jen v registru. Zjednodušilo
  by to model, ale je to ~50 změn a nic to nerozbije, když se to udělá později.
- **Zda přidat skládání profilů** (`include=`). Až se ukáže, že se profily opisují.
- **Zda posílat strukturovaný snímek konfigurace do záznamu.** Až se ukáže, že textové `Info`
  hlášky na dohledání „s čím to běželo" nestačí.
