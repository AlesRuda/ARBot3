# Měření výkonu: stíhá řídicí algoritmus?

> **Stav 2026-09-01:** **fáze 1 a 2 hotové** (smyčka + zpráva + panel + stupně), **23 nových
> testů** (celkem 1040 zelených). Fáze 3 (CPU stroje, teplota, frekvence, CPU čas taktu — vše
> platformní přes HAL) a fáze 4 (`ARBot.Analyze perf`) zbývají.
>
> ⚠️ **Na zařízení to neběželo.** Všechno ověření je na Windows v simulaci; hodnoty obsazenosti
> na RK3588 budou jiné a **práh `perfwarn` je pořád odhad**. Rozpad po jádrech nemá jak se ověřit
> na vývojovém stroji (big.LITTLE je vlastnost cílového HW) — test pokrývá jen správnost agregace.
> **Cena měření** (že `perf=true` sama nezhorší obsazenost) je taky neověřená; pozná se až A/B
> během na zařízení.
>
> **Panel *Tools → Výkon* autor proklikal 1. 9. 2026** a hlásí „zdá se to být OK". Automatem
> ověřený není — projekt nemá UI testy, takže bez člověka šel ověřit jen build se statickou
> kontrolou bindingů (všechny šablony a sloupce mají `x:DataType`, překlep by chytil `AVLN2000`)
> a testy jádra.
>
> ⚠️ **Na Windows je verdikt v panelu červený (`NESTÍHÁ`), a je to správně** — plyne z 3–4
> zameškaných taktů za sekundu (viz „První měření"). Není to vada panelu ani měření; zelený verdikt
> tady čekat nelze, dokud se nevysvětlí to zpoždění časovače.
>
> **Ověřeno za běhu je toto:** bezobslužný běh (`selftest=true st_seconds=10 st_record=true`)
> zapsal do záznamu **10 `PerfMsg` za 10 s** a zprávy se ze záznamu načtou zpátky se smysluplnými
> hodnotami (viz „První měření" níž).

Otázka, na kterou to má odpovídat: **stíhá řízení počítat, nebo je stroj přetížený?** A když
nestíhá — **která část to brzdí** a je viník uvnitř aplikace, nebo mimo ni?

Do 1. 9. 2026 se to zjistit nedalo. Existovalo měření **vizuální cesty**
(`traversability-timing-*.csv`: `compute avg/p50/p95/max`, `wait_avg`) a jeho souhrn v self-testu,
plus obecná pomůcka [`Performance`](../Src/ARBot.Common/Common/Performance.cs) (agregace
Sum/Sum2/Cnt). **O samotné řídicí smyčce se nevědělo nic.**

## Rozsah

| Co | Měří se |
|---|---|
| **Periodická řídicí smyčka** | obsazenost periody, zpoždění proti mřížce, zameškané takty |
| **Asynchronní stupně** | délka fronty, doba zpracování zprávy, počet **zahozených** zpráv |
| **Systém** | CPU procesu a stroje, počet jader, **teplota a throttling** |

**Mimo rozsah:** latence od čidla k zásahu (měří důsledek místo příčiny; má smysl, až tohle
nebude stačit) a jakákoli **reakce** na přetížení (snížení zátěže, nouzové zastavení) — nejdřív
měřit, pak teprve uvažovat o řízení podle toho.

## Kde se metriky berou

**Scheduler je pro smyčku správné místo.** Jako jediný zná plánovaný čas taktu i skutečný, takže
zpoždění spočítá zadarmo; zároveň callback volá, takže na témže místě změří i **dobu práce**.
`Timer` v `ARBotRuntime`, který ho pumpuje, o svém zpoždění neví nic.

| Metrika | Kde | Jak |
|---|---|---|
| zpoždění taktu, zameškané takty, doba tiku | [`Scheduler`](../Src/ARBot.Common/Runtime/Scheduler.cs) | volitelný odběratel metrik; bez něj nestojí nic |
| fronta, zahozené, doba zpracování | [`MessageTarget`](../Src/ARBot.Common/Communication/MessageTarget.cs) | `Interlocked` počítadla |
| CPU procesu | `ARBot.Common` | `Process.TotalProcessorTime` — přenositelné |
| CPU stroje, teplota, throttling | **`ARBot.HAL*`** | stejnojmenná třída v obou HAL; Armbian čte `/sys/class/thermal`, Windows vrací „neznámo" |

Platformní rozdělení kopíruje existující vzor: `ARBot.csproj` vybírá `HALArmbian` (Platform
`OrangePI`) nebo `HALWindows` **při buildu**, takže se nic nevětví za běhu.

## Zpráva a sběr

**Jedna zpráva `PerfMsg` jednou za sekundu**, do streamu — tím jde současně do UI (živý ukazatel)
i do záznamu (rozbor po jízdě). Žádná zvláštní cesta pro každý účel.

Nese za interval **průměr, maximum a počet překročení prahu**, plus **čas nejhoršího taktu**.
Průměr sám by ojedinělý dlouhý tik rozmazal — a přitom právě ten je typicky ten problém; maximum
ho ukáže a čas umožní dohledat, co robot v tu chvíli dělal.

⚠️ **Sběrač musí mít vlastní časovač, ne řídicí mřížku.** Kdyby visel na `Scheduler`u, přestal by
posílat právě ve chvíli, kdy se nestíhá — tedy když je nejvíc potřeba. Nezávislý časovač navíc
zachytí i případ, kdy řízení stojí úplně.

**Sekundová perioda je i rozhodnutí o ceně měření.** Odečet CPU a délek front není zadarmo; na
1 Hz je to šum, na 10 Hz už by měření samo bylo součástí problému, který má měřit.

### Obsah zprávy

| Skupina | Údaj | Poznámka |
|---|---|---|
| interval | od, do | aby šlo počítat na sekundu i při zpožděném sběru |
| smyčka | počet taktů | kolik jich za interval proběhlo |
| | **obsazenost** avg, max [%] | `doba tiku / perioda`; 100 % = tik trvá celou periodu |
| | zpoždění avg, max [ms] | skutečný start taktu proti plánovanému času na mřížce |
| | **zameškané takty** | kolikrát se takt nestihl; dnes se místo toho dohání (viz níž) |
| | čas nejhoršího taktu | kotva pro dohledání v ostatních datech |
| | **jádro nejhoršího taktu** | `Thread.GetCurrentProcessorId()` — viz „Nestejná jádra" |
| | **rozpad po jádrech** | pro každé jádro: počet taktů a průměrná doba |
| | CPU čas taktu (fáze 3) | proti wall-clock rozliší „počítáme" od „čekali jsme" |
| stupně | pole záznamů | jméno, délka fronty, zpracováno, **zahozeno**, doba zpracování avg/max |
| systém | CPU procesu, CPU stroje [%] | **0–100 % celého stroje**, ne jednoho jádra; stroj jen tam, kde ho HAL umí |
| | teplota [°C], throttling | jen Armbian; jinde „neznámo" |
| verdikt | OK / varování / chyba | viz prahy níž |

**Obsazenost je hlavní číslo** — je bez jednotek, srovnatelná mezi stroji a přímo odpovídá na
položenou otázku. Zpoždění a zameškané takty říkají, jestli se nestíhání už projevuje na mřížce.

⚠️ **CPU je jen kontext, ne odpověď.** Řídicí smyčka je **jedno vlákno**, takže může být úplně
saturovaná, zatímco stroj ukazuje 15 %. Kdo by sledoval jen CPU, uzavře „je klid" — a přitom se
nestíhá. CPU slouží k odlišení „brzdí nás vlastní kód" od „bere nám to někdo jiný", nic víc.

**Počet jader se neposílá.** Procenta jsou už normalizovaná na celý stroj, takže k jejich
interpretaci není potřeba — jádra potřebuje jen **výpočet** uvnitř
(`Process.TotalProcessorTime` je součet přes všechna jádra, takže se dělí
`uplynulý čas × počet jader`).

### Proč ne stávající zpráva `Module`

[`Module`](../Src/ARBot.Common/Logs/Module.cs) má pole `Name`, `Enabled`, `CPU` a vypadá jako
hotové místo — ale je to **mrtvá zpráva z ARBot2**: registrovaná v katalogu kvůli čitelnosti
starých záznamů, jinak jen v zakomentovaném kódu, nikdo ji neposílá ani nečte. Její tvar navíc
nestačí (chybí fronta, zahozené, doba zpracování) a je ve starých záznamech jako verze 1.
Zůstane, jak je.

## Nestejná jádra: proč se sleduje, kde takt běžel

Cílové zařízení má **RK3588** ([POSTUP.md](../OrangePi5Ultra/POSTUP.md)), tedy čtyři výkonná
a čtyři úsporná jádra. Táž práce tam trvá různě dlouho podle toho, kde běží — a k tomu **DVFS**
mění frekvenci i na tomtéž jádru podle zátěže a teploty. **Doba taktu proto kolísá i bez zjevné
příčiny** a kdo to neví, hledá vinu v kódu.

Vlákno navíc nemá afinitu a **řídicí smyčka běží při každém taktu na jiném vlákně ThreadPoolu**
(`System.Threading.Timer`, viz [ARBotRuntime.cs:665](../Src/ARBot/Robot/ARBotRuntime.cs:665)) —
takže se stěhuje mezi jádry volně.

⚠️ **Porovnání wall-clock a CPU času samo o sobě nestačí.** Nabízí se pravidlo „wall 90 ms,
CPU 88 ms ⇒ počítá náš kód; wall 90 ms, CPU 20 ms ⇒ čekali jsme" — jenže na úsporném jádru je
delší **i CPU čas**, takže „náš kód je pomalý" a „běželi jsme na malém jádru" vypadají stejně.
Proto se vedle toho sleduje jádro.

**Rozpad po jádrech (počet taktů a průměrná doba) je tam schválně místo jediného „jádro nejhoršího
taktu"** — samotné „nejhorší takt byl na jádru 2" je anekdota, kdežto z rozpadu se bimodalita
pozná na první pohled.

**Do kódu se nezabuduje domněnka, která jádra jsou výkonná.** Číslování na RK3588 tu nikdo
neověřil a hádat se nebude: čtyři jádra s výrazně delší průměrnou dobou taktu jsou ta úsporná,
a to z dat vyjde samo.

**Afinita (připnutí smyčky na výkonná jádra) je mimo rozsah.** Je to **odpověď, ne měření**,
a riskantní: když jsou výkonná jádra zaneprázdněná něčím jiným, připnutí situaci zhorší. Má smysl
teprve tehdy, když měření ukáže, že smyčka na úsporných jádrech reálně sedí.

## Verdikt a prahy

Samotná čísla nikdo nesleduje, takže zpráva ponese i **verdikt**:

- **obsazenost periody nad prahem** → varování,
- **zameškaný takt** → chyba.

Prahy jdou z [registru parametrů](configuration.md), takže se dají změnit bez překladu — až se
ukáže, jaké hodnoty jsou na Pi normální. Nastavovat je teď od stolu by znamenalo hádat.

## Panel a rozbor záznamu

**Panel *Tools → Výkon*** ukáže poslední zprávu: obsazenost s verdiktem (barva), zpoždění,
zameškané takty, tabulku stupňů a systémové údaje. Protože čte ze streamu, **funguje i při
přehrávání záznamu** — ve View se prostě přehrají zprávy z běhu.

**`ARBot.Analyze perf`** projde záznam a vypíše rozdělení obsazenosti (p50/p90/max), počet
zameškaných taktů a časy, kdy k nim došlo — tedy to, co je pro rozbor po jízdě potřeba a co
v panelu nejde vidět, protože ten ukazuje jen aktuální sekundu.

## Kde to v kódu je

| Soubor | Odpovědnost |
|---|---|
| [`Diagnostics/TickStats.cs`](../Src/ARBot.Common/Diagnostics/TickStats.cs) | akumulátor statistik taktů + `TickSnapshot` / `CoreSnapshot` |
| [`Diagnostics/ISchedulerMetrics.cs`](../Src/ARBot.Common/Diagnostics/ISchedulerMetrics.cs) | rozhraní, kterým `Scheduler` hlásí takty |
| [`Diagnostics/StageStats.cs`](../Src/ARBot.Common/Diagnostics/StageStats.cs) | `StageSnapshot` — stav a výkon jednoho stupně |
| [`Diagnostics/PerfCollector.cs`](../Src/ARBot.Common/Diagnostics/PerfCollector.cs) | vlastní časovač, sestaví a pošle `PerfMsg` |
| [`Runtime/Scheduler.cs`](../Src/ARBot.Common/Runtime/Scheduler.cs) | měření taktů (`Metrics`; `null` = neměří se) |
| [`Communication/MessageTarget.cs`](../Src/ARBot.Common/Communication/MessageTarget.cs) | počítadla fronty (`TakeStageSnapshot`) |
| [`Logs/PerfMsg.cs`](../Src/ARBot.Common/Logs/PerfMsg.cs) | zpráva (verze 1) |
| [`Robot/ARBotRuntime.cs`](../Src/ARBot/Robot/ARBotRuntime.cs) | napojení sběrače (parametr `perf=`) |
| [`ViewModels/PerformanceDocument.cs`](../Src/ARBot/ViewModels/PerformanceDocument.cs) | panel *Tools → Výkon* |

**Práh `perfwarn` se čte v `ARBotRuntime` a předává sběrači konstruktorem**, ne uvnitř
`ARBot.Common`. Je to konvence projektu (konfigurace se čte výhradně přes `Program.GetParam*`) a
zároveň nutnost: strážný test `ParamRegistryGuardTests` skenuje jen `Src/ARBot`, takže klíč čtený
z `Common` by hlásil jako mrtvý. Viz [configuration.md](configuration.md).

### Zahozené zprávy se dopočítávají, nepočítají

U politik `DropOldest` / `DropNewest` vrací `Channel.Writer.TryWrite` **`true` i tehdy, když se
něco zahodilo** — kanál zahodí *jinou* zprávu, ne tu právě zapisovanou, a volajícímu o tom nic
neřekne. Počet zahozených proto z návratové hodnoty zjistit nejde a odvozuje se z bilance
**`zapsané − vyzvednuté − délka fronty`** (délka z `ChannelReader.Count`). Pořadí těch tří odečtů
je záměrné: při souběhu může rozdíl vyjít jen *menší*, nikdy větší, takže měření zahození nikdy
nevymyslí.

## První měření (1. 9. 2026, Windows, simulace)

Deset sekund `mission=freerun` nad `SyntetickyKoridor.osm`, perioda 100 ms. Za sekundu:

| Údaj | Hodnota |
|---|---|
| takty | 9–11 |
| **zameškané takty** | **3–4** |
| obsazenost | avg 0 %, max 1 % |
| zpoždění taktu | avg 65–86 ms, **max ~108 ms** |
| CPU procesu | 15–18 % |
| stupně | fronty 0–1, **zahozeno 0**, nejpomalejší `LocalNavigator` (avg ~5 ms, max ~36 ms) |

⚠️ **Nález, kvůli kterému se to stavělo, padl hned:** takt se **nestíhá vydat včas ve ~30 %
případů** a scheduler ho dohání. Přitom **vlastní práce taktu trvá pod 1 ms** — brzdí tedy
*časovač*, ne řídicí kód. Verdikt je proto `Error` v každé sekundě.

To ruší podmínku, kterou si spec sama položila u obou odložených nálezů níž („pokud fáze 1 naměří
nula zameškaných taktů, je otázka akademická"): **nula to není.** Než se z toho ale začne cokoli
opravovat, patří vědět, jestli je to vlastnost cílového zařízení, nebo jen Windows: `System.Threading.Timer`
má na Windows hrubé rozlišení a běh sdílel stroj s renderem oken. **Změřit totéž na OrangePi je
proto první krok**, ne změna politiky dohánění.

**Jedna hodnota v tabulce je zatím nedůvěryhodná: obsazenost 0/1 %.** Doba taktu se měří kolem
`Scheduler.PumpDue` → callbacku, jenže vizuální cesta běží synchronně na vlákně kamery, ne v taktu.
Obsazenost tedy měří *řídicí* práci, ne celou zátěž — a číslo pod 1 % je tak spíš zpráva o tom,
že to nejdražší se počítá jinde.

## Fáze

1. **Smyčka + zpráva + panel** — ✅ hotovo 1. 9. 2026.
2. **Asynchronní stupně** — ✅ hotovo 1. 9. 2026 (počítadla v `MessageTarget`).
3. **Systém, teplota, throttling, frekvence jader, CPU čas taktu** — jediná platformně závislá
   část, zásah do obou HAL. Frekvence (`/sys/devices/system/cpu/cpu*/cpufreq/scaling_cur_freq`)
   patří k teplotě: teprve spolu odliší „běželi jsme na úsporném jádru" od „frekvence spadla kvůli
   teplotě". CPU čas taktu je taky platformní (`QueryThreadCycleTime` / `clock_gettime` s
   `CLOCK_THREAD_CPUTIME_ID`) — .NET pro CPU čas *aktuálního vlákna* přenositelné API nemá.
4. **`ARBot.Analyze perf`** — rozbor ze záznamu.

## Dva nálezy, které měření odhalí — a které se zatím NEOPRAVUJÍ

Obojí je zásah do **řízení**, ne do diagnostiky, a obojí se projeví jen tehdy, když je takt
nepravidelný. Jestli k tomu vůbec dochází, je přesně to, co fáze 1 změří. **Opravovat to naslepo
by znamenalo měnit brzdné chování robota na základě domněnky.**

> **A fáze 1 to změřila: takt JE nepravidelný** (3–4 zameškané za sekundu, zpoždění až ~108 ms —
> viz „První měření" výš). Obě otázky níž tedy přestaly být hypotetické. **Pořád se ale neopravují**,
> a to z jiného důvodu než dřív: měřilo se na **Windows**, kde je hrubé rozlišení
> `System.Threading.Timer` samo o sobě dostatečné vysvětlení. Rozhodovat o politice dohánění podle
> čísla z vývojového stroje by byla táž chyba jako rozhodovat podle domněnky — **další krok je
> přeměřit to na OrangePi**.

### 1. Zameškané takty se dohánějí

> **Není to nedopatření, je to vědomá kompenzace** — a to je při čtení kódu potřeba vědět.
> Časovač v [ARBotRuntime.cs:660](../Src/ARBot/Robot/ARBotRuntime.cs:660) má **reentranční guard**:
> když předchozí `Pump()` ještě běží, další callback se **zahodí** (jinak by se překrývaly a zahltily
> ThreadPool). Dohánění ve scheduleru je odpověď právě na to — komentář u guardu říká „zameškané
> takty dožene Scheduler při příštím tiku". Otázka tedy nezní „proč to tam je", ale **jestli je
> dohnat správnější než zahodit**.

[`Scheduler.cs:54`](../Src/ARBot.Common/Runtime/Scheduler.cs:54) má `while`, ne `if`:

```csharp
while (now >= r.NextTick)
{
    due.Add((r.OnTick, r.NextTick));
    r.NextTick = r.NextTick + r.Interval;
}
```

Když `PumpDue` přijde o 300 ms pozdě, vygeneruje **tři takty hned po sobě**. Regulátor spočítá tři
kroky nad prakticky stejným stavem a všechny pošle do motorů. Do `OnTick` jde `NextTick`, tedy
**plánovaný** čas — proto `tk - lastRegulatorTick` vidí hezké násobky periody i tehdy, když
ve skutečnosti uplynulo mnohem víc.

**V reálném čase je zameškaný takt promarněná příležitost — patří zahodit, ne dohnat.** Catch-up
dává smysl jen tam, kde čas řídí data, ne hodiny.

> **Souvislost s replay a simulací, na kterou se nabízí myslet, dnes NEEXISTUJE:**
> ve View se řídicí smyčka **vůbec nezakládá** (kořen je `FileMessageSource` → `Stream`,
> viz [ARBotRuntime.cs:125](../Src/ARBot/Robot/ARBotRuntime.cs:125)) a **simulace je normální Run**
> — jen s virtuálními senzory, pořád na `SystemClock` a v reálném čase.
> [`VirtualClock`](../Src/ARBot.Common/Runtime/VirtualClock.cs) používají **jen testy**;
> `FusionProcessor` ho umí posouvat časy zpráv, což vypadá jako příprava na rychlý přepočet
> záznamu, který ale neexistuje. **Dohánění je tedy čistě chování při Run.**

Až se to bude opravovat, patří to jako **volba politiky** (reálný čas → zahodit, virtuální čas →
dohánět), aby budoucí rychlý replay měl co potřebuje.

### 2. Krok rampy dobrzdění je z periody, ne ze skutečného odstupu

[`ControlLoop.cs:190`](../Src/ARBot.Common/Runtime/ControlLoop.cs:190) — dobrzdění při **zastaralé
dráze** (path controller nedostal novou trasu déle než `PathControlTimeOut`, tedy 500 ms):

```csharp
double decel = Profile.MaxDecceleration * period.TotalSeconds;
forvard = Math.Max(0, lastForward - decel);
```

Je to **výpočet příkazu do budoucna** — vydaná hodnota platí do příštího taktu. `decel` je ale
krok diskrétní rampy `v(k) = v(k−1) − a·Δt`, a aby ta rampa měla skutečně strmost
`MaxDecceleration` [m/s²], musí být `Δt` **odstup mezi zásahy**. V ustáleném stavu je `Δt = period`
a je to totožné — proto to nikdy nevadilo.

Rozejde se to jen při nepravidelném taktu: při odstupu 200 ms robot jel těch 200 ms `lastForward`,
ale rychlost se srazí jen o `a·100 ms` — dobrzďuje tedy **poloviční strmostí, než zamýšlí**.
Chyba je konzervativní vůči trhnutí, ale **nekonzervativní vůči „zastavit včas"**, což je to,
k čemu ta rampa je.

Není to poplach: skutečné brzdění a bezpečnost řeší řadič motorové jednotky vlastním skriptem
(`SDC2160Ex`), tohle je konzistence softwaru. **Podmínka „pokud fáze 1 naměří nula zameškaných
taktů, je otázka akademická" se ale nesplnila** — na Windows jich je 3–4 za sekundu a odstup mezi
zásahy tedy skutečně kolísá. Zůstává otevřená; rozhodne měření na zařízení.

## Rizika

- **Měření stojí čas.** Proto 1 Hz a proto `Interlocked` místo zámků. Až bude panel hotový, patří
  ověřit, že zapnutí měření samo nezhorší obsazenost periody.
- **CPU stroje se na Windows a Linuxu čte úplně jinak** — proto je v HAL, ne v `Common`. CPU
  procesu je naopak přenositelné.
- **Na Pi je hlavní podezřelý teplotní throttling**, který vypadá jako „kód se zpomalil". Bez
  teploty by se ta záměna nedala vyloučit — proto je ve fázi 3, ne mimo rozsah.
- **Prahy verdiktu jsou zatím odhad.** Naostro se dají nastavit až podle prvního měření na
  zařízení; do té doby je verdikt orientační.
