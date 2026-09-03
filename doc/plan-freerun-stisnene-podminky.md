# FreeRun ve stísněných podmínkách — zadání průzkumu po prvním nárazu

> **Stav 2026-09-03: zadání, nic z toho není hotové.** Vzniklo z rozboru záznamu
> `records/20260902-222601.rec` (první jízda FreeRun na železe, 417 s, skončila nárazem do překážky).
> Rozbor a čísla níž jsou z `ARBot.Analyze localplan`, který k tomu vznikl. Kontext:
> [devlog.md](devlog.md) záznam 2. 9. 2026, [mission-freerun.md](mission-freerun.md),
> [occupancy-and-local-planning.md](occupancy-and-local-planning.md),
> [path-following.md](path-following.md).

**Cíl průzkumu:** zjistit, **proč robot najel do překážky, kterou lokální mapa znala**, a navrhnout
(a po schválení zavést) ochrany tak, aby ve stísněném prostoru **zastavil a hledal cestu**, místo aby
jel do zdi. Nejde o ladění mise na rovné cestě — na té funguje (viz mission-freerun.md, měřeno proti
pravdě). Jde o chování, když **koridor není a okolo je málo místa**.

**Pravidla projektu platí** (viz [CLAUDE.md](../CLAUDE.md)): čeština, build `x64`, commit jen na
pokyn, každou změnu chování **změřit nad záznamem** (`ARBot.Analyze`), starou cestu nemazat, dokud
novou nepotvrdí testy. Hypotézy níž jsou **z dat, ne dojmy**, ale pořadí oprav je názor asistenta —
autor ho ještě neschválil.

---

## Co se změřilo (záznam 20260902-222601, 7 639 plánů)

Příkaz: `dotnet run --project Src/ARBot.Analyze -p:Platform=x64 -- localplan records/20260902-222601.rec`
(`--from=225 --to=275` vypíše detail úseku, kdy byl uvolněný nouzový stop).

| ukazatel | hodnota | význam |
|---|---|---|
| mrkev z koridoru | **2 %** cyklů (180 z 7 672; šířka 0,5–1,0 m) | mise skoro celou dobu jela záložní cestou „drž kurz": mrkev **3 m přímo vpřed bez ohledu na terén** |
| mrkev nedosažitelná (`\|požadovaný − dosažený cíl\| > 0,3 m`) | **97 %** plánů, p50 0,89 m, p90 2,49 m | A\* cíl nenašel a vrátil fallback „nejbližší dosažitelná buňka" |
| délka plánu k mrkvi 3 m daleko | p50 **8,3 m**, p90 **22 m** (`HorizonM` = 25) | fallback vyrábí detoury skrz `Unknown`, aby se o 30 cm přiblížil |
| plány s odstupem **pod `SafeDist`** 0,4 m | **52 %**, minimum 0,00 m | eskapovací zóna (`EscapeRadius` 0,5 m) byla trvale aktivní |
| potvrzeně sjízdný dosah před robotem | **0 m ve 100 %** plánů | 1. uzel vždy na podlaze `MinCostSpeed` 0,05 m/s |
| poslední grid | `Free` 1,2 %, `Blocked` 15,1 % (téměř vše geometrie), `Unknown` 83,7 %; v čase `Free` 4–12 % | sémantický kanál z RGB skoro nic nepotvrdí jako cestu |
| příkazová rychlost při plánu 0,05 m/s | **0,10 m/s** (= celý `maxspeed`) | podlaha se **nevynucuje** mezi uzly, viz vada C |
| nouzový stop aktivní | 92 % času | robot skutečně jel jen 233–267 s |
| resety gridu / skoky pózy > 0,3 m | 0 / 0 | lokalizace za náraz nemůže; póza při stání jitteruje 9 mm / 0,1 s |

**Průběh nárazu (233–267 s):** mrkev 3 m vpřed byla nedosažitelná o ~2,5 m, plán byl **pahýl
0,5–0,6 m k nejbližší buňce u čela překážky**, minimální odstup 0,05 m, příkaz 0,10 m/s. Robot dojel
k překážce, periodicky se objevoval `EscapingBlocked` (stál v blokované buňce) a znovu `Partial`
s týmž pahýlem.

---

## Hypotézy vad (seřazené podle váhy, kterou jim dávají data)

### A. Fallback „jeď alespoň co nejblíž" mění chybný cíl v jízdu do překážky

`LocalPathPlanner.Search` vrací při nedosažitelném cíli `bestIdx` = buňka s nejmenší euklidovskou
vzdáleností k cíli. Když cíl leží ve zdi (nebo za ní), je ta buňka **z definice na hraně překážky**
a plán k ní vede. Dvě podoby téhož: pahýl k čelu (náraz), nebo 20 m detour skrz `Unknown`, aby se
robot dostal o pár desítek cm blíž. Stav se hlásí jako `Partial`, tedy stejně jako běžný „cíl za
horizontem", takže vyšší vrstva nedosažitelnost **nepozná**.

*Otázky:* má nedosažitelný cíl **uvnitř gridu** (bez ořezu) být samostatný stav (`Unreachable`)?
Má se pahýl kratší než X nebo detour delší než k·|robot−cíl| vůbec předávat regulátoru? Co má
dělat `FreeRunMission`, když dostane `Unreachable` — viz D.

### B. Eskapovací zóna ruší `SafeDist` pokaždé, když je překážka do 0,5 m

`Passable` připouští odstup < `SafeDist` pro buňky do `EscapeRadius` od startu. Navrženo pro „odjet
od zdi, u které jsem zaparkoval"; ve stísněném prostoru je robot **pořád** do 0,5 m od `Blocked`,
takže výjimka platí trvale a plán vede půdorysem robota skrz překážku (odstup 0,05 m). `PathCollides`
to nezachytí, protože běží jen, když nový plán nevznikl.

*Otázky:* má zóna platit jen tehdy, když **výchozí buňka sama** má odstup < `SafeDist`? Má odstup
v zóně klesat jen na aktuální odstup robota (nikdy pod něj), ne na nulu? Jak to interaguje
s `EscapingBlocked`?

### C. `Speed` waypointu je rychlost příjezdu do uzlu, ne strop úseku

`LocalPathPlanner.BuildWayPoints` píše `Speed` jako „min rychlost na následujícím úseku" (podlaha
`MinCostSpeed` za hranicí potvrzeného). `PathPlanner` z něj ale dělá `vNode[i]` = strop **v uzlu**
a `PathResult.Control` mezi uzly startuje z `profile.MaxSpeed` a brzdí jen tak, aby do dalšího uzlu
**dojel** na jeho strop. Naměřeno 0,05 → 0,10 m/s; při skutečném `MaxAllowedSpeed` 1,2 m/s by
robot „plouživý" úsek skrz neznámo projel skoro plnou rychlostí. Invariant *„nikdy nejeď rychleji,
než z čeho zastavíš na hranici potvrzeně průjezdného"* z occupancy-and-local-planning.md tedy mezi
uzly **neplatí** a dokument to tvrdí mylně.

*Otázky:* má se strop úseku vynucovat v `PathResult.Control` (min přes `VLimit[seg]` i
`VLimit[seg+1]`), nebo má `LocalPathPlanner` hustit uzly tak, aby na to stačil zpětný průchod?
Které existující testy `PathPlanner`/`PathResult` počítají s dnešní sémantikou? Ověřit
i v simulaci (`UNKNOWN` před robotem → rychlost skutečně klesá).

### D. Chybí vrstva „před robotem není sjízdno → zastav a hledej"

Když koridor není, mise klade mrkev vpřed a lokální vrstva ji **vždy** nějak obslouží (A, B). Nikdo
neřekne „tady se jet nedá". Návrh autora (3. bod v zadání): nedosažitelná mrkev nebo nulový sjízdný
dosah ⇒ **zastavit, pomalu rotovat jedním směrem**, dokud plán nemá sjízdný dosah, pak jet. VFH nebo
`Colider` až kdyby to nestačilo. Patří to do `FreeRunMission` (stavový krok), ne do plánovače.

*Otázky:* kterým směrem rotovat (k straně s větším podílem `Free`/`Unknown` v pásu před robotem?
vždy stejným?), jak dlouho, co když se otočí o 360° a nic — zastavit a hlásit. Jak to změřit nad
záznamem (`localplan` už umí epizody nedosažitelné mrkve).

### E. Nápady autora k mrkvi — a proč samy nestačí

- **Mrkev na okraji lokální mapy** (jako `GlobalNavigator`): vzdálenost nedosažitelnost neřeší
  (ve zdi je mrkev ve 3 i 6 m). Zisk je, že na okraji je většinou `Unknown`, ne `Blocked`, takže
  cesta existuje a `Partial` má jasný význam. Zvážit jako drobnost k `HorizonM`, ne jako lék.
- **Sjízdný rámeček 1 px kolem mapy**: zaručí dosažitelnost, když robot není uzavřený, ale falšuje
  mapu a rámeček sousedí s `Blocked`, takže ho `SafeDist` stejně zakáže bez další výjimky. Totéž
  chování jde vyjádřit výslovně stavem `Unreachable` (A) a reakcí mise (D).
- **„Neroutovat přes nesjízdný terén"**: A\* přes `Blocked` už nejde; jde přes `Unknown` s cenou ×3,
  a bez toho by robot nikdy nevyjel (dopředu vidí 5 m, do stran nic). Otázka je spíš **kolik**
  `Unknown` smí plán obsahovat / jak dlouhý detour je ještě smysluplný (A).

### F. Proč není nic `Free` (vedlejší, ale rozhoduje o rychlosti)

`Free` vyžaduje **oba** kanály pod prahem. Sémantický kanál z RGB (`ImageProbability`) na této scéně
skoro nic nepotvrdil (simulace „hloubka = ideální rovina" dá `Free` jen 12,6 %). Zda podlahu drží
`VBrake` (nic `Free` po dráze) nebo `VClear` (při `maxspeed=0.1` dá rampa 0,4–0,8 m rychlost
≥ 0,05 až od odstupu 0,6 m), **z dat nejde říct**: `MinVClear`/`MinVBrake`/`MinFreeAheadM` jdou jen
do Debug outputu, ne do `LocalPlanMsg`.

*Úkol:* přidat je do `LocalPlanMsg` (nová verze zprávy, stará se čte dál) a do `localplan`. Bez toho
se F nedá rozhodnout. Zda je RGB klasifikátor na této scéně použitelný, je samostatná otázka
(prohlédnout snímky ve View).

---

## Navržené pořadí (názor asistenta, ke schválení)

1. **C** — bezpečnostní vada nezávislá na misi; opravit a pokrýt testem, který dnes chybí (rychlost
   **mezi** uzly proti stropu úseku). Opravit i tvrzení v occupancy-and-local-planning.md.
2. **B** — eskapovací zónu vázat na skutečný stav „stojím pod `SafeDist`", ne na poloměr kolem
   každého startu. Regresní test: robot u zdi (odstup 0,3 m) smí odjet; robot s odstupem 0,45 m
   nesmí dostat plán s odstupem 0,05 m.
3. **A + D** — `Unreachable` jako stav plánu; `FreeRunMission` na něj (a na nulový dosah) reaguje
   zastavením a rotací. Měřit `localplan` nad novým záznamem: epizody nedosažitelné mrkve mají
   končit rotací, ne pahýlem.
4. **F** — telemetrie obálky do zprávy, pak rozhodnout, zda je problém v RGB kanálu.
5. **E** — až nakonec, a jen pokud po 1–4 zbývá co ladit.

## Kritéria hotovo

- Nad záznamem 20260902-222601 (přehráním v simulaci nejde — je to záznam ze železa; použít
  syntetickou stísněnou scénu ve `virtualhw=true`, viz [virtual-hw.md](virtual-hw.md)) robot **do
  překážky nenajede**: žádný plán s odstupem < `SafeDist` mimo skutečný únik, žádná příkazová rychlost
  nad stropem úseku.
- `localplan` nad novým záznamem ze železa: podíl plánů „nedosažitelná mrkev" klesne řádově,
  epizody končí rotací/zastavením, minimální odstup ≥ `SafeDist` (kromě `EscapingBlocked`).
- Testy zelené (`dotnet test <proj> -p:Platform=x64`), DevLog zapsaný, rozhodnutí v decisions.md.

## Co je hotové a dá se použít

- `ARBot.Analyze localplan` ([LocalPlanReport.cs](../Src/ARBot.Analyze/LocalPlanReport.cs)) — stavy
  plánu v čase, dosažitelnost mrkve, délka plánu, dosah potvrzeného, rychlost 1. uzlu proti
  příkazové, podíl nouzového stopu, min. odstup proti `SafeDist`, resety gridu, skoky pózy,
  epizody, detail okna.
- `ARBot.Analyze freerun` (podíl cyklů s koridorem, důvody), `occupancy` (složení poslední mapy).
- Záznam `records/20260902-222601.rec` (14 GB, není v gitu; leží u autora).
