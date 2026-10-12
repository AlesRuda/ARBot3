# Plán: `StopHold` — držené zastavení řídicí smyčky

**Stav:** fáze 1–3 hotové (13. 9. 2026). Fáze 1 a 2 **na HW neběžely**, fáze 3 **ANO** — celá
choreografie je odzkoušená na robotu, ale **na zaseknuté kameře zatím ne** (viz níž).

## Proč

Dnes se robot zastavuje jediným způsobem: `IRegulatorHolder.Regulator = null`. Ta proměnná ale
nese **dvě různé věci najednou** — *kam jet* i *smím jet* — a vyhrává ten, kdo psal poslední.
Kdykoli bude potřeba robota dočasně podržet z jiného důvodu, než je cíl mise (restart kamer po
zaseknutí — viz [hardware.md](hardware.md), servisní okno, budoucí recovery manévr), začne se
o tu proměnnou přetahovat vyšší smyčka s tím, kdo drží stop.

`StopHold` to rozdělí: **regulátor zůstává „kam jet", hold je „smíš jet".** Vyšší smyčky nastavují
regulátor dál a nic o holdu nevědí.

## Tvar

```csharp
using (var hold = controlLoop.StopRequest("restart kamer"))
{
    while (!hold.IsStopped) { /* čekat, s VLASTNÍM timeoutem */ }
    // ... riskantní operace ...
}   // Dispose = uvolnění; robot se rozjede, až pustí VŠICHNI
```

- **Počítané držení.** Držitelů může být víc, robot stojí, dokud existuje aspoň jeden.
  Tím zmizí přetahování — nikdo nikomu nepřepisuje stav, každý mluví jen za sebe.
- **`IsStopped` = změřené stání**, ne „poslali jsme nulu".
- **`Dispose` je jediná cesta ven** a je idempotentní.

## Rozhodnutí (proč zrovna takhle)

1. **Každý hold nese důvod a je vidět.** Největší nová porucha, kterou tenhle mechanismus
   zavádí, je *„robot stojí a nikdo neví proč"*. Proto: `StopRequest(string duvod)`, `Trace` při
   prvním držení i posledním uvolnění, příznak v `DriveCommandMsg` (verze 3) a seznam důvodů pro
   stránku náhledu.
2. **Žádný finalizér, uvolnění jen vědomé.** Kdyby token pouštěl GC, robot by se rozjel proto, že
   někomu vypadla reference — to je ta horší strana selhání. Zapomenutý token naopak znamená
   stojícího robota, což je bezpečné, a bod 1 ho zviditelní.
3. **Neznámý stav motorů NENÍ stání.** Existující test stání ve větvi nouzového zastavení zní
   `mot == null || (LeftWheelSpeed == 0 && RightWheelSpeed == 0)` — tam je `mot == null` v pořádku
   (dopředná je už nulovaná), ale pro `IsStopped` by to byla lež: kdo čeká, aby směl udělat něco
   riskantního, nesmí dostat „stojí" od senzoru, který mlčí. Důsledek: **volající si musí nést
   vlastní timeout** (při `no_uart=true` by čekal navěky).
4. **Brzdí se řízeně, ne tvrdou nulou** — tatáž rampa jako u zastaralé dráhy (směr z regulátoru,
   dopředná `−MaxAcceleration·dt`; do 6. 10. 2026 `MaxDecceleration`). Je to mechanismus pro **plánované** události; nouzové
   zastavení zůstává vedle, nedotčené a tvrdé.
5. **Vlastní šev vedle `IRegulatorHolder`.** Mise i supervizor závisí na malém rozhraní
   (`IDriveHold`), ne na `ControlLoop` — jinak by se ztratila testovatelnost s fake objekty.

## Co se záměrně NEDĚLÁ

- **Mise se nemigrují.** `Regulator = null` na konci mise znamená „už nikam nejedu", ne „chvíli
  počkej". Splynutím by se ten rozdíl ztratil.
- **Hold nezahazuje dráhu.** `LocalNavigator` si aktivní dráhu ověřuje proti aktuální mapě každý
  cyklus, takže po rozjezdu se to zahojí samo. Míň pohyblivých částí.

## Kroky

- **Fáze 1 — mechanismus** (hotovo 13. 9. 2026): `IDriveHold`, `StopHold`, `DriveHoldRegistry`,
  napojení v `ControlLoop`, `DriveCommandMsg` verze 3. Testy: dva zdroje a pořadí uvolnění,
  idempotentní `Dispose`, stání bez motorů, rampa místo skoku, nouzové zastavení nedotčené.
- **Fáze 2 — konzumenti** (hotovo 13. 9. 2026): detektor záseku v `GlobalNavigator` se odzbrojí
  i pod drženým stopem — `OnDriveCommand(emergencyStop, held)`, pole se jmenuje `legitimniStani`,
  protože obojí znamená totéž („robot stojí právem"). Bez toho by plánované zastavení po 10 s
  vypadalo jako zásek a robot by začal **zavírat hranu** kvůli tomu, že čekal na opravu kamery.
  Na stránce náhledu je řádek **„zastaveno: …"** s důvody; ty se čtou ze **živé** smyčky
  (`WebStatus.HoldReasonsSource`, šev kvůli testům), protože v `DriveCommandMsg` je jen příznak.
- **Fáze 3 — supervizor kamer** (hotovo 13. 9. 2026): `CameraRecoverySupervisor` (vlastní vlákno,
  perioda 1 s) po 15 selhaných dotazech vezme hold, počká na stání, zavolá `ARBotHW.RecoverCameras`
  a pustí — **od 12. 10. 2026 až po prvním snímku kterékoli kamery** (`PrvniSnimekKamery`, nejdéle
  30 s; do té doby hned po recyklaci, viz „Proč po zotavení kamer nevzniká plán"). Spouští se i **ručně** tlačítkem *Zotavit kamery* (`POST /camerarecover`) — bez něj by
  se hypotéza dala ověřit jedině čekáním, až porucha přijde sama. Odstup dvou zotavení nejméně
  60 s: kdyby recyklace nepomáhala, zastavovat robota každou vteřinu je horší než porucha sama.

  ✅ **Ověřeno na robotu** (13. 9. 2026, 16:12): žádost → hold → *„robot stojí"* → obě D435 uvolnily
  handle za 2 s → kontext zahozen a založen nový → hold uvolněn → **obě kamery připojené zpátky
  za 10 s** od žádosti. Proces nespadl.

  ✅ **A pak i na SKUTEČNÉ poruše** (16:49:55 zamrzla barva → 15× selhaný dotaz → 16:50:14
  supervizor → 16:50:24 **obě kamery zpátky**). Od zamrznutí do obnovy **29 s** proti 343 s
  a 22 minutám v dřívějších epizodách, kde to skončilo restartem služby. Hold držel 2 s.
  ⚠️ Je to **jedna epizoda** a mechanismus zásek pořád nevysvětluje — jen ho léčí.

## ⚠️ Past, kterou našlo až zařízení: teardown z cizího vlákna zatuhne

První verze fáze 3 bourala pipeline **přímo z vlákna supervizora**. Na robotu to **zatuhlo**:
vlákno kamery sedělo v `TryWaitForFrames` a souběžný `pipeline.Stop()` z cizího vlákna se už
nevrátil. `RecoverCameras` nedoběhla, takže se **neuvolnil `StopHold`** — robot zůstal stát
a spravil to až restart služby. Pipeline librealsense není bezpečná pro souběžný `Stop` a `Wait`.

Léčba: `IRecoverableCamera` je **žádost a potvrzení** (`RequestRelease` → `Released` →
`ResumeAfterRecovery`). Kamera si pipeline zbourá sama ve své smyčce a **zaparkuje se**, takže
během výměny kontextu nikdo nevolá `QueryDevices`. Supervizor čeká na potvrzení od *všech* kamer
(5 s); **když se nedočká, kontextem nehne** — nativní pád je horší než mrtvá kamera.

✅ **Dvě věci na tom stojí za zapsání.** Za prvé: chování při selhání bylo **bezpečné** — robot
zůstal stát, stránka u toho psala *„zastaveno: zotavení kamer"*, takže bylo hned vidět proč.
Přesně kvůli tomuhle je důvod povinný. Za druhé: v tom pokusu se **zdravá kamera po teardownu
okamžitě zasekla** do známého „failed to set power state" a už se nevzpamatovala — kdežto při
opravené verzi (teardown **a** recyklace kontextu) se obě vrátily za 10 s. Je to nepřímé, ale
je to zatím nejsilnější doklad, že recyklace kontextu tu poruchu skutečně léčí.

## ⚠️ Druhá past: lék, který oslepí všechny, se nesmí opakovat donekonečna

Do zotavení byla 13. 9. 2026 večer zapojena i **T265** (`IRecoverableCamera`), aby se před výměnou
kontextu uvolnila — bez toho jí zůstaly zaseknuté handle a zotavení jí nepomohlo.

Jenže její porucha („připojí se, ale nedává pózu" po zpackaném bootu) **recyklací kontextu
vyléčit nejde**. Výsledek: hlásila si o zotavení pořád dokola a supervizor kvůli jedné trvale
vadné kameře **bral každých 60 s dolů i obě zdravé D435** a pokaždé zastavil robota.

Léčba: **odstup se po neúspěšném zotavení zdvojnásobuje** (60 s → 2 → 4 … nejvýš 15 min);
za neúspěch se bere další žádost do 3 minut od minulého zotavení. Do logu jde hláška, která rovnou
říká, co s tím (fyzicky odpojit a zapojit kameru). Ruční žádost ze stránky se do toho nepočítá —
za tu odpovídá člověk. ⚠️ **Tahle podoba v kódu nezůstala:** týž den ji nahradilo třetí kritérium — vzdání po třech zotaveních za 15 min (`MaxZotaveniVOkne`), protože si zdvojnásobený odstup sám natahoval nad práh; odstup je stálých 60 s (`MinOdstup`, od 12. 10. 2026 měřený od konce zotavení).

Původní pojistka „odstup aspoň 60 s" byla napsaná přesně proti tomuhle a **nestačila**.

⚠️ **A nestačil ani ten backoff.** T265 se po replugu (19:04) za hodinu a půl zasekla znovu a
zotavení jí zase nepomáhalo — takže i se stropem 15 min by kvůli ní šly **každých 15 minut dolů
i obě zdravé D435** a robot by pokaždé zastavil. Proto se to po **třech marných zotaveních
u dané kamery vzdá**: robot jede dál bez ní a do logu jde hláška, že to chce fyzicky přepojit.

Tři vlastnosti, na kterých to stojí:

- **Vzdává se to per kamera**, ne globálně — když se pak zasekne jiná, zotaveni proběhne normálně.
- **Samo se to zruší**, jakmile se vzdaná kamera znovu chytne.
- **Hláška jmenuje kameru**, která si o zotavení říká. Do té doby v ní bylo natvrdo „typicky T265",
  což je hádání zabudované do diagnostiky — zrovna tady to náhodou sedělo, ale příště by to poslalo
  hledat na špatné místo.

## Otevřené úkoly (→ registr)

Stav a data vede [registr úkolů](ukoly.md); tady je jen seznam, co se téhle oblasti týká.

- **[Po uvolnění holdu chodí plán až za 9–13 s a zastaralý regulátor mezitím točí robotem na místě](ukoly.md#lp-zastaraly-regulator-toci-na-miste)** —
  viz „Ověřeno ze záznamů" níž (ve všech čtyřech záznamech 8,7–16,9 s).
- **[Po zotavení kamer je robot 8–17 s slepý mimo držení](ukoly.md#prov-zotaveni-kamer-slepy-po-uvolneni)** —
  proč plán nechodí, viz „Proč po zotavení kamer nevzniká plán" níž; od 12. 10. 2026 se drží do
  prvního snímku (v kódu, na zařízení neběželo).
- **[Rampa motorové jednotky je 2,6× strmější, než říká `Profile.MaxAcceleration`](ukoly.md#hw-motor-rampa-jednotky)** —
  nalezeno při rozboru rozjezdu, rozhoduje autor.

## ✅ Ověřeno ze záznamů (5. 10. 2026)

`ARBot.Analyze hold` vezme epizody z `DriveCommandMsg.Held` a vypíše brzdění, rozjezd a časovou osu
(příkaz, rotace, fúze, kola, gyro, stáří plánu). V záznamech od 13. 9. jsou čtyři epizody (16., 18.,
23. a 29. 9.), všechny zotavení pravé D435, 1,6–6,2 s; **dvě padly do jízdy**:

| | 23. 9. `20260923-143515` | 29. 9. `20260929-151634` |
|---|---|---|
| před holdem | fúze 0,47 m/s, kola 0,60 | fúze 0,13 m/s, kola 0,12 |
| krok příkazu pod holdem | 0,050 m/s za takt (tehdy `MaxDecceleration` 0,50) | 0,040 m/s za takt |
| kola stojí za / dráha | 0,35 s / 5 cm | 0,27 s / 3 cm |
| první plán po uvolnění | za 13,1 s | za 8,7 s |
| rozjezd s prvním plánem | příkaz 0 → 0,57 m/s za takt, za 1 s 1,53 | 0 → 0,51 m/s za takt, za 2 s 1,70 |

- **Brzdění pod holdem za jízdy funguje, jak má:** rampa bez skoku, kola sledují příkaz se zpožděním
  ~0,1 s.
- **Rozjezd rampou není.** Po uvolnění chybí plán (regulátor je zastaralý), dopředný příkaz zůstává
  nula — ale **rotace ze zastaralého regulátoru jde do motorů**: 29. 9. se robot točil na místě
  −0,5 rad/s a obsluha zmáčkla nouzové zastavení. S prvním plánem pak příkaz skočí bez rampy; profil
  pohybu ji nedělá, zrychlení omezí až motorová jednotka (kola ~1,9 m/s²). Obojí vedou nová témata výše.

**Rozhodnutí autora týž den** ([decisions.md](decisions.md)): (1) u zastaralé dráhy stojící robot
nerotuje — `ControlLoop` nuluje rotaci, jakmile dopředný příkaz dobrzdil a kola stojí (v kódu, 5 testů,
⚠️ na zařízení neběželo); (2) skok příkazu je v pořádku, rozjezd omezí motorová jednotka.

### Proč po zotavení kamer nevzniká plán (změřeno 12. 10. 2026)

`ARBot.Analyze zotaveni` sestaví kolem každého držení časovou osu od uvolnění (t = 0): snímky po
kamerách z indexu (čas příchodu do streamu), zpracování `LocalNavigator` z `PerfMsg`, grid, **všechny**
plány i bez dráhy, cíl navigace a hlášky; `--kontext` jen z hlášek měří, za kolik sekund od recyklace
kontextu RealSense se která kamera připojila. V záznamech (91 souborů nad 100 MB; neprošly
`20260918-155329.rec` a `Robotour2026/Kolo2.rec`, které se analyzátorem neotvírají) jsou čtyři
zotavení, všechna pravé D435; zotavení 13. 9. jsou popsaná v [hardware.md](hardware.md), 28. 9. jen
v journalu:

| | 16. 9. | 18. 9. | 23. 9. | 29. 9. |
|---|---|---|---|---|
| držení | 6,3 s | 1,8 s | 1,7 s | 2,5 s |
| nový kontext → „pipeline připojena" (L / P) | 16,22 / 16,26 s | 12,34 / 12,57 s | 12,33 / 12,42 s | 7,98 / 8,08 s |
| uvolnění → první snímek (L / P) | 16,80 / 16,90 s | 12,81 / 13,11 s | 13,00 / 13,00 s | 8,60 / 8,70 s |
| první snímek → grid a plán | +0,06 s | +0,03 s | +0,04 s | +0,06 s |
| pollerů na sdíleném zámku | 3 (L, P, T265) | 3 | 3 | 2 (bez T265) |
| ve slepé době (uvolnění → první snímek) rotace na místě, binárky před 5. 10. | 169 ze 169 taktů, do 0,52 rad/s | 0 (lokální minimum, bez dráhy) | 131 ze 131, do 0,15 rad/s | 5 taktů, od 0,5 s nouzové zastavení, po jeho uvolnění dalších 35 (do 0,52 rad/s) |

- **Celá mezera je znovupřipojení kamer.** Od uvolnění do prvního snímku nedojde do streamu ani do
  navigátoru jediný snímek z žádné kamery (`LocalNavigator` zpracuje 20 zpráv/s, tedy jen
  `RobotStateMsg` a `DriveCommandMsg` ze smyčky, zahozeno 0), a s prvním snímkem vznikne grid i plán
  do 0,06 s. **Grid, plánovač ani globální navigace mezeru nedělají.** (18. 9. byl robot už před
  zotavením v lokálním minimu — `AlreadyAtGoal` bez dráhy — takže tam plán s dráhou nepřišel vůbec;
  plány samotné naběhly s prvním snímkem.)
- **Držení se (do 12. 10. 2026) uvolnilo dřív, než kamery vidí — záměrně.** `CameraRecoverySupervisor` držel
  `StopHold` jen přes `ARBotHW.RecoverCameras`, a ten se vrátí hned po recyklaci kontextu
  a `ResumeAfterRecovery`, které jen pustí smyčky driverů zpátky k připojování („Kamery se připojí
  samy z nového kontextu"). Návrh počítal s oslepením „na pár sekund"; změřeno **8–17 s**.
- **Kam se těch 8–16 s ztratí, ze záznamu poznat nejde.** Dotaz, který projde a zařízení nenajde,
  driver nezapisuje, a hlídač nativních volání hlásí až od 20 s (v binárce z 23. 9. ještě nebyl).
  Data sedí na tři vysvětlení: (A) jedno dlouhé volání, (B) rychlé tiché dotazy „není tu", dokud se
  zařízení po recyklaci neobjeví, (C) několik serializovaných `QueryDevices` pod sdíleným zámkem
  `RealSenseShared`. Pro A/C mírně mluví jen to, že se obě D435 připojí vždy do 0,23 s od sebe.
  Doba sedí s počtem vláken, která se na zařízení ptají přes sdílený zámek: 16.–23. 9. tři (obě
  D435 a hledání odpojené T265) → 12–16 s, 29. 9. i 13. 9. dvě → ~8 s (13. 9. byla T265 připojená
  a dávala pózu, takže se neptala, a do zotavení ještě zapojená nebyla — [hardware.md](hardware.md)).
  **Prokázané to není** a diagnostiku doby dotazů autor nechce; od 26. 9. se T265 nezakládá, takže
  dnes se dá čekat ~8 s.
- **Dnešní kód by v mezeře stál, ne točil:** zastaralá dráha se u stojícího robotu nuluje (5. 10.)
  a zamkne jako dojetá (11. 10., `ControlLoop.IsDriving`). ⚠️ Mimo držení je ale **ozbrojený detektor
  záseku A** globální navigace: `legitimniStani` platí jen pod držením nebo stopem a `localPlanValid`
  si nese stav posledního plánu z doby před držením. Podmínky splnil jen 23. 9. (jízda s cílem,
  poslední plán `Ok`; 16. 9. byl poslední plán `EscapingBlocked`, tedy neplatný, a 29. 9. FreeRun bez
  cíle) — a tam robot ujel 0,5 m asi 14,3 s po uvolnění proti prahu 15 s, rezerva ~0,7–1,0 s (ze
  záznamu; s dnešní rampou 0,40 m/s² by byla menší). Pokus se navíc započítával na hranu natrvalo
  (čítač se nenuloval,
  [`nav-detektor-a-pokusy-bez-nulovani`](ukoly.md#nav-detektor-a-pokusy-bez-nulovani)).

✅ **Rozhodnutí autora 12. 10. 2026, v kódu:**
- **Držet do prvního snímku kterékoli kamery.** Zotavení spouští jedna kamera, ale recyklace kontextu
  odpojí všechny, takže po ní vždy chybí obě D435. `CameraRecoverySupervisor` se před zotavením
  přihlásí k událostem snímků obou kamer (`PrvniSnimekKamery`) a po recyklaci drží `StopHold`, dokud
  některá nepošle snímek — nejdéle `PrvniSnimekTimeout` (30 s). Slepá doba je tím legitimní stání:
  stránka ukazuje „zastaveno: zotavení kamer", detektor záseku A je odzbrojený. Do `Trace` jde
  „první snímek po zotavení za N s (kamera)". Snímky před recyklací (zdravá kamera do svého
  uvolnění) se nepočítají; za první se počítá jakýkoli snímek, i ten, jehož zpracování selhalo.
  ⚠️ **Po vypršení 30 s se pustí** a pak platí totéž co dřív: robot bez snímků stojí, ale už ne pod
  držením — stránka nejdřív ukazuje „jede", od 20 s stání „stojí N s při jízdě" a od 60 s „UVÁZL"
  (`StuckMonitor`), a jede-li navigace s cílem po hraně a poslední plán z doby před zotavením byl
  platný, ozbrojí se detektor A a po 45 s souvislého stání může hranu uzavřít. Vzácný případ (kamery
  se nevrátí), vědomě ponechaný: supervizor zaseknutý v čekání by nemohl zkusit další zotavení.
  Výjimka ze zotavení (nový kontext nejde založit) čekání na snímek nepřeskočí.
  Pořadí kroků (přihlásit se ke snímkům před zotavením, počítat až po něm) je v testovatelné
  `CameraRecoverySupervisor.ZotavPodDrzenim`; 10 testů (6 na čekání, 4 na pořadí a výjimku, mutace
  chycené). K tomu z revize: `MinOdstup` (60 s) se měří od **konce** zotavení, ruční „Zotavit kamery"
  zmáčknuté během zotavení, které zabralo, se ignoruje (dřív hned strhlo právě připojené kamery;
  když se kamery nevrátily, klik platí), texty na stránce slibují 10–25 s místo „pár sekund"
  (zastavení 2–6 s + připojení 9–17 s), a `Dispose` supervizoru neruší událost, když vlákno
  nedoběhlo (stará vada: `Stop` během zotavení mohl shodit proces).
- **Diagnostiku doby dotazů na zařízení autor nechce** — kam se 8–16 s ztratí, zůstává nezměřené.
- **Čítač pokusů detektoru A se nuluje pohybem** o `MinMotionM` od posledního pokusu, ať je
  detektor ozbrojený, nebo ne (i únik `EscapingBlocked`), pokud navigace jede s cílem po hraně —
  pokusy se počítají v jednom souvislém stání, viz
  [global-navigation-runtime.md](global-navigation-runtime.md).

⚠️ Na zařízení neběželo.

**Srovnání s nouzovým zastavením** (`ARBot.Analyze hold --estop`, 9 jízd 18.–29. 9., 16 epizod, z toho 11 s rozjezdem v záznamu):
po uvolnění stopu přijde plán za **0,05–0,4 s** (po holdu 9–13 s; jediná výjimka 3,6 s je stop hned po
holdu 29. 9.), takže zastaralá dráha tam nenastává. Rozjezd je jinak stejný: příkaz skočí za jeden
takt až o 1,5–1,6 m/s, kola zrychlují plynule ~1,0–1,3 m/s² — rampou motorové jednotky, která je
ovšem **2,6× strmější** než nastavených 0,40 m/s² (převod jednotek, `hw-motor-rampa-jednotky`).
Převod je 5. 10. 2026 opravený (autor) a rampy jsou zvlášť (skript 2.2): běžná jízda jednou rampou
0,40 m/s² (rozjezd i brzdění), pod nouzovým zastavením a watchdogem 1,0 m/s² — viz [hardware.md](hardware.md).
- **[Zaseknuté kamery D435 si runtime zotaví sám za ~29 s](ukoly.md#prov-zotaveni-kamer-supervizor)** —
  kolik trvá recyklace kontextu, a tedy jak dlouhý hold to bude: změřeno na skutečné poruše,
  obnova 28–29 s.
