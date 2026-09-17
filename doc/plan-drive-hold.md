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
   dopředná `−MaxDecceleration·dt`). Je to mechanismus pro **plánované** události; nouzové
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
  a pustí. Spouští se i **ručně** tlačítkem *Zotavit kamery* (`POST /camerarecover`) — bez něj by
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
za tu odpovídá člověk.

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

- **[Řídicí smyčka umí držené zastavení (StopHold)](ukoly.md#lp-drzene-zastaveni-stophold)** — víc
  epizod zotavení, a hlavně **za jízdy**: dosud robot stál bez mise, takže hold neměl co brzdit
  (držel 2 s), a koordinace s bržděním je jen z testů.
- **[Řídicí smyčka umí držené zastavení (StopHold)](ukoly.md#lp-drzene-zastaveni-stophold)** —
  rozjezd po uvolnění holdu ověřit měřením: má to být rampa, ne skok na příkazovanou rychlost;
  rampu má dělat regulátor sám (profil pohybu), ale otestované to není.
- **[Zaseknuté kamery D435 si runtime zotaví sám za ~29 s](ukoly.md#prov-zotaveni-kamer-supervizor)** —
  kolik trvá recyklace kontextu, a tedy jak dlouhý hold to bude: změřeno na skutečné poruše,
  obnova 28–29 s.
