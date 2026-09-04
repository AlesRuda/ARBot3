# Webový náhled headless runtime — návrh a implementační plán (fáze 3)

> **Pro agentní pracovníky:** plán se plní **task po tasku**, kroky mají checkboxy (`- [ ]`).
> Použij `superpowers:subagent-driven-development` nebo `superpowers:executing-plans`. Každý task
> končí zeleným buildem a testy pod `x64`. Nekomitovat bez pokynu autora (viz [CLAUDE.md](../CLAUDE.md)).

**Cíl:** Do procesu `ARBot.Headless` přidat malý HTTP server, který na mobilu nebo notebooku ukáže,
**co robot právě dělá** — snímek z kamery, půdorys s tím, co vidí lokální mapa, textový stav mise —
a nabídne jediný zásah: **zastavit**.

**Architektura:** Kreslení jde do `ARBot.Common/Rendering` (čistě ze zpráv, SkiaSharp tam už je kvůli
`ImageMsg`, takže na to uvidí i `ARBot.Analyze`). HTTP jde do `ARBot.Runtime/Web` jako odběratel
`ARBotRuntime.Stream` s politikou „latest-wins" a **líným renderem**: obrázek se kreslí teprve
v obsluze požadavku, takže když se nikdo nekouká, náhled nestojí nic. `ARBot.Headless` server zapne
a předá mu callback na ukončení procesu.

**Technologie:** .NET 10, `System.Net.Sockets.TcpListener` (vlastní GET-only HTTP/1.1), SkiaSharp
3.119 (už v `ARBot.Common`), NUnit 4. **Žádný nový NuGet.**

**Spec:** návrhová část je v tomto dokumentu, sekce [Návrh](#návrh) — plán z ní argumentuje, čtou se
spolu.

## Globální omezení

- **Build pro konkrétní platformu, NE `AnyCPU`**: `x64` (Windows, testy), `OrangePI` (Armbian/ARM64).
- **Jazyk: čeština** — kód, komentáře, dokumentace, jména testů.
- **Žádný nový NuGet balík** v `ARBot.Runtime` ani `ARBot.Headless`; `ARBot.Runtime` navíc nesmí mít
  `PackageReference` na Avalonia/Dock/Mapsui ani `using Avalonia` (viz [architecture.md](architecture.md)).
- **Diagnostika poruch do `Trace`, ne do `Debug`** — v Release na zařízení `Debug.WriteLine` mlčí.
- **Náhled nesmí zabránit robotovi jet.** Selhání serveru (obsazený port, výjimka v obsluze) je
  hláška do `Trace` a jede se dál bez náhledu — stejná zásada, jakou má dnes záznam.
- **Nemazat starou implementaci, dokud novou nepotvrdí testy.**
- Výchozí stav je **vypnuto** (`web=0`).

## Rozhodnutí autora (4. 9. 2026)

- **Náhled + Stop.** Web umí čtení stavu a jedno tlačítko, které zavolá `Stop()` a ukončí proces
  (jako Ctrl+C). Zadávání cíle ani start mise na webu **nebude** — to by znamenalo, že robota lze
  z webu rozjet.
- **Bez hesla, na všech rozhraních.** Robot je na uzavřené síti. Důsledek, se kterým se počítá:
  kdokoli v té síti může robota **zastavit**. Rozjet ne. To je vědomě ta bezpečnější strana.
- **Půdorys nese occupancy grid** (to, co robot skutečně vidí) **pod sítí cest z mapy**, plus póza,
  mrkev a trajektorie. Jeden obrázek, ne dva.
- **Vlastní HTTP nad `TcpListener`**, ne `HttpListener` — viz past 1 níž.
- **Kreslení do `ARBot.Common/Rendering`**, HTTP do `ARBot.Runtime/Web`.
- Gate „až po ověření fáze 2 na zařízení" z [plan-runtime-headless.md](plan-runtime-headless.md)
  se **vědomě přeskakuje** (na HW se to teď nedá vyzkoušet). Důsledek: **nevíme, kolik CPU zbývá**,
  proto je návrh postavený tak, aby náhled bez zájmu nestál nic a šel vypnout.

## Než začneš

**Výchozí stav:** commit `df8b3af` (fáze 1 a 2 hotové), pracovní strom čistý.
`ARBot.Common.Tests` pod `x64` **1 131 zelených** (4 přeskočené), `ARBot.Runtime.Tests` **4 zelené** —
to je baseline, proti které se porovnává.

**Přečti napřed:** [CLAUDE.md](../CLAUDE.md), [doc/headless.md](headless.md) (co headless je a není),
[doc/architecture.md](architecture.md) (vrstvy), [doc/occupancy-and-local-planning.md](occupancy-and-local-planning.md)
(co znamenají stavy buněk), [Src/ARBot/Views/README.md](../Src/ARBot/Views/README.md) (vzor
„latest-wins", který se tu opakuje mimo UI).

### Co dnes platí (zjištěno 4. 9. 2026)

Tohle jsou **naměřená fakta**, ne domněnky. Zkontroluj, jen když se něco nechová podle plánu.

- **`ImageMsg` do streamu při Run NECHODÍ a nemá kdo by ho vyrobil.** `BackProjectProcessor` (jediný
  jeho výrobce) se v `ARBotRuntime` **nekonstruuje** — `grep "new BackProjectProcessor" Src` najde jen
  `ARBot.HAL.Tests`. Potvrzeno záznamem z headless: `CameraFrame` 258, `ImageMsg` **0**.
  **Je to mrtvý kód** (potvrdil autor 4. 9. 2026): jeho práci dnes dělá `CameraFrameProcessor` a její
  výsledek nese **`CameraFrame` sám** (`ImageProbability`, `Grid`, `PathEdges`, `Projection`), takže
  se obraz nikam nepřeposílá jako samostatná zpráva. Komentář o limitu `["ImageMsg"] = 2` v `WireRun`
  tedy nepopisuje dnešek. **Do tohohle plánu to nepatří, ale stojí za úklid:** `BackProjectProcessor`
  je kandidát na smazání, jeho náhradu už testy potvrzují (`CameraFrameProcessorTest`).
  ⇒ `/camera.jpg` bere **`CameraFrame.ImageRGB`** (`Image<BGR32>`, `Step` 4).
- **`CameraFrame.ImageProbability`** (`Image<Gray>`, `Step` 1) je **pravděpodobnost cesty z RGB** —
  plní ji `CameraFrameProcessor.ComputeProbability` v každém snímku a čte ji `OccupancyIntegrator`
  jako barevný kanál occupancy gridu. Je to tedy přímo to, **co robot považuje za cestu**, ještě před
  fúzí do mapy. Pro náhled je to nejcennější druhý pohled po samotném RGB, a `SkiaColorType(1)`
  = `Gray8`, takže do JPEG jde bez převodu. ⇒ `/camera.jpg?layer=prob`.
- ⚠️ **`CameraFrame` nese poolované capture buffery kamery** a kamera je recykluje. Držet si na něj
  referenci mimo `Post` je chyba. `ImageDocument` v UI si proto bere **stabilní kopii** z
  **`CameraFramePool`** (`ARBot.Common/Devices/CameraFramePool.cs`, veřejná, thread-safe, používá ji
  i `RecordingTarget` v Common): `Acquire(src)` zkopíruje do slotu a vrátí kopii (nebo `null`, když
  je pool vyčerpaný = drop), `Release(frame)` slot vrátí. **Grid uvnitř snímku se předává referencí
  a je immutable** (viz hlavička té třídy).
- **`OccupancyGridMsg` naopak alokuje svá pole** (`OccupancyGrid.ToLogMessage`:
  `Occ = new sbyte[Size*Size]`, `Road = ...`), takže je to vlastní kopie a **referenci držet lze**.
  Má `Size`, `Resolution`, `OriginX/Y` (v buňkách), `State(i,j)` → `CellState` a
  `CenterX(i)`/`CenterY(j)` v ENU metrech.
- **`WorldViewDocument.EncodeOccupancyPng` už existuje a je čistě SkiaSharp, bez Avalonie**
  (`Src/ARBot/ViewModels/WorldViewDocument.cs`, kolem ř. 964) — blokované červeně, potvrzeně volné
  zeleně, neznámé průhledně, řádek 0 obrazu je severní hrana. Nepsat podruhé: **přesunout** do Common.
  ⚠️ Má v sobě vadu: `handle.Free()` je až za `image.Encode(...)` a **není v `finally`**, takže při
  výjimce v kódování unikne připnutý `GCHandle`. Při přesunu opravit (`ImageMsg.EncodeSkia` to má
  správně — `try`/`finally`).
- **Kódování obrazu do JPEG v Common už je**, ale privátně: `ImageMsg.EncodeSkia(step, w, h, pixels,
  fmt)` + `SkiaColorType(step)` (podporuje step 1 → `Gray8` a step 4 → `Bgra8888`),
  `ImageMsg.JpegQuality` = 90. ⇒ zveřejnit tenkou obálku, ne psát druhý kodér.
- **Uzly mapy jsou v LLA, ne v ENU.** `Node.Location` je `LLA`, `Node.Width` je šířka cesty v uzlu
  [m] (0 = neurčeno), `Edge.From`/`To` jsou `Node`. Převod dělá
  `GeoReference.ToLocal(LLA) → Point2D` (`ARBot.Common/Coordinates/GeoReference.cs`).
  `RoadScene` sice ENU geometrii má, ale její `Segment` i pole `segments` jsou **private**, takže se
  z ní kreslit nedá — renderer si hrany převede sám.
- **Runtime vystavuje, co je potřeba:** `ARBotRuntime.Current.Stream` (`MessageSource`,
  `Connect(IMessageSink)` → `IDisposable`), `RoadNetwork`, `MapOrigin` (`GeoReference`),
  `HwSettleMs`, `IsRunning`, `Stop()`.
- **Zprávy pro stav:** `RobotStateMsg` (`X`, `Y`, `Theta`, `V`, `Omega`), `GlobalNavMsg` (`Status`,
  `HasGoal`, `GoalLatDeg/LonDeg`, `CarrotX/Y`, `HasCarrot`, `OffRouteDist`, `RouteLengthM`,
  `ClosureCount`), `MissionMsg` (`Phase`, `Stop`, `ElapsedSec`, `HasDepot`, `AcceptedCodeText`,
  `AbortReason`, …), `FreeRunMsg` (`GoalX/Y`, `FromCorridor`, `Width`, `Lateral`, `DirectionRad`),
  `LocalPlanMsg` (`Status`, `LengthM`, `MinClearanceM`, `ComputeMs`), `PerfMsg` (`ProcessCpuPct`,
  `MissedTicks`, `OccupancyAvgPct`, `DelayMaxMs`).
- **Parametry:** `ParamRegistry.Num(name, def, category, description, parse)` zakládá číslo,
  `parse` je volitelné omezení rozsahu (vzory v `ParamParsers`: `Kladne`, `Nezaporne`, `LatLon`…).
  Číslo je vždy `double`, celočíselné parametry se castují u čtení (jako `st_seconds`).
- **`ARBot.Runtime.Tests` existuje** (NUnit 4, jen `x64`, referuje `ARBot.Runtime`), vzor testu se
  statickým stavem je `RuntimeBootstrapTests` (`[NonParallelizable]`, `TearDown` uklidí).

### Pasti, na které se tady naráží

1. ⚠️ **`HttpListener` na Windows bez admin práv nezvládne jiný prefix než `localhost`.** Naměřeno
   4. 9. 2026: `http://+:port/` i `http://*:port/` skončí `HttpListenerException: Přístup byl
   odepřen`, `http://localhost:port/` projde. Na Linuxu by fungoval, ale ladil by se pak jiný stav,
   než jaký běží na Pi. **Proto vlastní `TcpListener`** — chová se na obou platformách stejně
   a nepotřebuje URL ACL.
2. **Query string v cestě.** Stránka si obrázky vynucuje přes `?t=<časové razítko>` (jinak je
   prohlížeč cachuje), takže router musí `?` a všechno za ním odstřihnout.
3. **`Stop()` je idempotentní, ale `Start()` podruhé nejdřív volá `Stop()`.** Server smí `Stop()`
   zavolat, `Start()` nikdy.
4. **Port v testech nezapisovat pevně.** `TcpListener` s portem 0 si nechá přidělit volný od OS;
   server proto po startu **musí vystavit skutečný port** (`Port`), jinak jsou testy křehké.
5. **Build hlásí `MSB3027`/`MSB3021` na zamčené `ARBot.exe`, když aplikace běží.** Zavřít aplikaci.
6. **Pole `Occ`/`Road` jsou indexovaná `[j * Size + i]`** a řádek 0 obrázku je **severní** hrana,
   tedy nejvyšší `j` — kdo to splete, dostane půdorys vzhůru nohama.

---

## Návrh

### Vrstvy a soubory

```
ARBot.Common/Rendering/        ← kreslení ze zpráv (bez UI, bez HAL); vidí na to i ARBot.Analyze
  OccupancyPng.cs              ← přesunuto z WorldViewDocument (+ oprava GCHandle)
  PlanViewRenderer.cs          ← půdorys: grid + síť cest + póza + mrkev + trajektorie
ARBot.Common/Logs/ImageMsg.cs  ← + public EncodeJpeg / EncodePng (obálka nad privátním EncodeSkia)

ARBot.Runtime/Web/             ← HTTP a skládání stránky (potřebuje ARBotRuntime.Stream)
  HttpMini.cs                  ← parser požadavku + skládání odpovědi (bez socketu → testovatelné)
  WebStatus.cs                 ← latest-wins snímek stavu, JSON a HTML stránka
  WebPreviewServer.cs          ← TcpListener, accept loop, routování, líný render

ARBot.Headless/Program.cs      ← zapnutí serveru, callback na ukončení
ARBot.Common/Configuration/    ← parametr web= (+ validátor portu v ParamParsers)
```

Dělení je podle odpovědnosti, ne podle vrstvy: `HttpMini` neví nic o robotovi (jde otestovat na
řetězcích), `WebStatus` neví nic o socketech (jde otestovat na zprávách), `WebPreviewServer` je
lepidlo mezi nimi a `ARBotRuntime`.

### Cesty

| cesta | odpověď | pozn. |
|---|---|---|
| `GET /` | `text/html` | stránka s oběma obrázky, textem stavu a tlačítkem Stop |
| `GET /camera.jpg` | `image/jpeg` | poslední snímek; `?cam=<jméno>` vybere kameru, `?layer=prob` pravděpodobnost cesty místo RGB |
| `GET /world.png` | `image/png` | půdorys |
| `GET /status.json` | `application/json` | tentýž stav jako text (obnovení bez reloadu, skriptovaný dohled) |
| `POST /stop` | `text/plain` | zastaví runtime a ukončí proces |
| cokoli jiného | 404 | |

`POST` u zastavení je záměrný: `GET` by mohl vyvolat prefetch prohlížeče nebo náhled odkazu.
Odpovědi nesou `Cache-Control: no-store` a `Connection: close`.

### Latest-wins a líný render

Server je `IMessageSink` připojený na `ARBotRuntime.Current.Stream`. `Post` běží na vlákně
producenta, takže **nesmí blokovat ani alokovat víc než musí**:

- `OccupancyGridMsg`, `RobotStateMsg`, `GlobalNavMsg`, `MissionMsg`, `FreeRunMsg`, `LocalPlanMsg`,
  `PerfMsg` → uloží se **reference** na poslední zprávu daného druhu pod krátkým zámkem. Tyhle
  zprávy jsou vyrobené pro stream a nikdo je nerecykluje (viz „Co dnes platí").
- `CameraFrame` → **jen když o snímek někdo v poslední době stál** (poslední požadavek na
  `/camera.jpg` není starší než `CameraInterestSec` = 10 s) se pořídí kopie z `CameraFramePool`
  (kapacita 2) a předchozí se vrátí do poolu. Bez zájmu se snímek **zahodí bez kopírování**.
- Z `RobotStateMsg` se navíc plní **trajektorie**: kruhový buffer `TrailPoints` = 600 bodů,
  nový bod se zapíše jen když je dál než 0,1 m od posledního.

**Renderuje se teprve v obsluze požadavku.** Když se nikdo nekouká, náhled nekreslí, nekóduje
a u kamery ani nekopíruje — jediná režie je uložení referencí.

### Vlákna a limity

Jedno vlákno s `IsBackground = true` a `ThreadPriority.BelowNormal` (náhled nesmí soupeřit
s řízením) přijímá spojení a **obsluhuje je po jednom**. Dva obrázky, o které si stránka řekne
současně, se tedy serializují — to je v pořádku a drží to paměť i CPU pod kontrolou.

| limit | hodnota | proč |
|---|---|---|
| hlavička požadavku | 8 kB | delší = odmítnout (413), ochrana proti záplavě |
| čtení a zápis | 5 s | zaseknuté spojení nesmí držet server |
| půdorys | 512 × 512 px, výřez 40 m | čitelné na mobilu, levné na kreslení |
| kamera | JPEG kvalita 90 (`ImageMsg.JpegQuality`) | tentýž kodér, jaký má záznam |
| interval obnovení stránky | 1 s | v JavaScriptu stránky |

Interval, rozlišení a výřez zůstávají **pevně v kódu**. Parametry se z nich udělají, až se ukáže,
že je potřeba je měnit — dnes by to bylo šest klíčů v registru bez důvodu.

### Parametr

Jediný nový klíč, kategorie *Diagnostika*:

```
web=<port>     0 = vypnuto (výchozí), jinak 1024–65535
```

Validaci dělá nový `ParamParsers.WebPort`, takže `web=80` (privilegovaný port) i `web=99999`
skončí **chybou při startu**, ne tichým pádem na default.

### Chyby

- **Bind selže** (obsazený port, chybějící právo): hláška do `Trace`, `Start` vrátí `false`,
  **robot jede dál bez náhledu**.
- **Výjimka v obsluze požadavku**: 500, spojení se zavře, server žije dál, hláška do `Trace`
  (jen první výskyt a pak jednou za minutu, aby se konzole nezaplavila).
- **Data ještě nejsou** (grid nepřišel, kamera neběží): obrázek s textem „čekám na data" nebo
  `204 No Content` u kamery — **ne 500**. Stránka to snese a je z ní vidět, že server žije.
- **Po `Stop()`** stránka dál odpovídá a v textu je „zastaveno" — proces se ukončuje až po dojetí
  `Stop()`, takže poslední pohled na stav zůstane.

### Stránka

Jeden soubor HTML skládaný v `WebStatus` (žádné externí zdroje — Pi je offline): tmavé pozadí,
nad sebou půdorys a snímek kamery, pod nimi tabulka stavu a červené tlačítko **Zastavit** s
potvrzením. Nad snímkem jsou dvě tlačítka, která přepínají vrstvu mezi **kamerou** a
**pravděpodobností cesty z RGB** — to druhé je odpověď na „vidí robot vůbec cestu?", protože je to
týž kanál, jaký jde do occupancy gridu. JavaScript každou sekundu přehodí `src` obou obrázků
s novým `?t=` a stáhne `/status.json`. Zastavení posílá `fetch('/stop', {method:'POST'})`.

---

## Tasky

### Task 1: Kreslení do Common — `OccupancyPng` a JPEG

Přesun existujícího kódu do `ARBot.Common/Rendering` s opravou unikajícího `GCHandle` a zveřejnění
kodéru JPEG. Po tomto tasku se **nic nechová jinak**, jen to leží jinde a má testy.

**Soubory:**
- Create: `Src/ARBot.Common/Rendering/OccupancyPng.cs`
- Modify: `Src/ARBot.Common/Logs/ImageMsg.cs` (přidat `EncodeJpeg`/`EncodePng`)
- Modify: `Src/ARBot/ViewModels/WorldViewDocument.cs` (smazat privátní kodér, volat Common)
- Test: `Src/ARBot.Common.Tests/Rendering/OccupancyPngTests.cs`

**Rozhraní:**
- Produces: `ARBot.Common.Rendering.OccupancyPng.Encode(OccupancyGridMsg og) → byte[]?`
  (null při chybě nebo prázdném gridu); `ARBot.Common.Logs.ImageMsg.EncodeJpeg(Common.Image img) → byte[]`,
  `ImageMsg.EncodePng(Common.Image img) → byte[]`.

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Rendering/OccupancyPngTests.cs`:

```csharp
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using ARBot.Common.Rendering;

namespace ARBot.Common.Tests.Rendering
{
    /// <summary>
    /// Kodovani occupancy gridu do PNG (presunuto z WorldViewDocument 4. 9. 2026, aby na nej
    /// videl i webovy nahled headless a ARBot.Analyze). Viz doc/plan-headless-web.md.
    /// </summary>
    public class OccupancyPngTests
    {
        /// <summary>Grid 4x4, kde bunka (1,2) je neprujezdna a (0,0) potvrzene volna.</summary>
        private static OccupancyGridMsg Grid()
        {
            var og = new OccupancyGridMsg
            {
                Size = 4, Resolution = 0.05, OriginX = 0, OriginY = 0,
                Scale = 1f, BlockedThreshold = 0.5f, FreeThreshold = -0.5f,
                Occ = new sbyte[16], Road = new sbyte[16],
            };
            // State() cte oba kanaly; hodnoty nad prahem = neprujezdno, pod = volno.
            og.Occ[2 * 4 + 1] = 100; og.Road[2 * 4 + 1] = 100;
            og.Occ[0] = -100; og.Road[0] = -100;
            return og;
        }

        [Test]
        public void PrazdnyNeboNulovyGrid_VratiNull()
        {
            Assert.Multiple(() =>
            {
                Assert.That(OccupancyPng.Encode(null), Is.Null);
                Assert.That(OccupancyPng.Encode(new OccupancyGridMsg { Size = 0 }), Is.Null);
                Assert.That(OccupancyPng.Encode(new OccupancyGridMsg { Size = 4, Occ = null }), Is.Null);
            });
        }

        [Test]
        public void Grid_ZakodujeSeJakoPng_SpravnychRozmeru()
        {
            byte[] png = OccupancyPng.Encode(Grid());

            Assert.That(png, Is.Not.Null);
            // Magicke bajty PNG: 89 50 4E 47 0D 0A 1A 0A
            Assert.That(png[0], Is.EqualTo(0x89));
            Assert.That(png[1], Is.EqualTo((byte)'P'));
            Assert.That(png[2], Is.EqualTo((byte)'N'));
            Assert.That(png[3], Is.EqualTo((byte)'G'));

            using var bmp = SkiaSharp.SKBitmap.Decode(png);
            Assert.That(bmp.Width, Is.EqualTo(4));
            Assert.That(bmp.Height, Is.EqualTo(4));
        }

        [Test]
        public void SeverJeNahore_ANeprujezdnaBunkaJeCervena()
        {
            // Radek 0 obrazu je SEVERNI hrana = nejvyssi j. Bunka (i=1, j=2) je tedy
            // na radku (Size-1-j) = 1.
            using var bmp = SkiaSharp.SKBitmap.Decode(OccupancyPng.Encode(Grid()));

            var blocked = bmp.GetPixel(1, 4 - 1 - 2);
            var free = bmp.GetPixel(0, 4 - 1 - 0);
            var unknown = bmp.GetPixel(3, 3);

            Assert.Multiple(() =>
            {
                Assert.That(blocked.Red, Is.GreaterThan(blocked.Green), "neprujezdna je cervena");
                Assert.That(blocked.Alpha, Is.GreaterThan(0));
                Assert.That(free.Green, Is.GreaterThan(free.Red), "potvrzene volna je zelena");
                Assert.That(unknown.Alpha, Is.EqualTo(0), "nezname je pruhledne");
            });
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~OccupancyPngTests"
```

Očekávej chybu překladu: `OccupancyPng` neexistuje.

- [x] **Krok 3: Vytvoř `Src/ARBot.Common/Rendering/OccupancyPng.cs`**

Tělo vezmi z `Src/ARBot/ViewModels/WorldViewDocument.cs` (metoda `EncodeOccupancyPng` a konstanty
`OccBlockedBgra`, `OccFreeBgra`, `PremulBgra`) a **oprav `GCHandle`**: `handle.Free()` musí být
ve `finally`, jinak při výjimce v `Encode` unikne připnuté pole.

```csharp
using System;
using System.Runtime.InteropServices;
using ARBot.Common.Logs;
using ARBot.Common.Occupancy;
using SkiaSharp;

namespace ARBot.Common.Rendering
{
    /// <summary>
    /// Occupancy grid jako PNG: neprujezdne cervene, potvrzene volne zelene, nezname pruhledne.
    /// <b>Radek 0 obrazu je SEVER</b> (nejvyssi <c>j</c>) - rastr se kresli shora dolu.
    ///
    /// <para>Presunuto 4. 9. 2026 z <c>WorldViewDocument</c> (UI) sem, aby na nej videl i webovy
    /// nahled headless runtime a <c>ARBot.Analyze</c>; UI to vola odtud. Kod je jinak tentyz.</para>
    ///
    /// <para><b>Pozn. k ladeni:</b> <see cref="CellState.Unknown"/> je pruhledne, takze v mape nejde
    /// odlisit od plochy, o ktere grid nic nevi. Pri otazce „proc robot leze" to muze svest - brzdna
    /// obalka jede jen pres bunky <see cref="CellState.Free"/>, takze souvisle vypadajici plocha
    /// jeste neznamena potvrzenou.</para>
    /// </summary>
    public static class OccupancyPng
    {
        /// <summary>Zakoduje grid do PNG (BGRA premultiplied). Vraci null pri chybe nebo prazdnem gridu.</summary>
        public static byte[] Encode(OccupancyGridMsg og)
        {
            if (og == null || og.Occ == null || og.Size <= 0) return null;

            int n = og.Size;
            GCHandle handle = default;
            try
            {
                using var bmp = new SKBitmap(new SKImageInfo(n, n, SKColorType.Bgra8888, SKAlphaType.Premul));
                var pixels = new uint[n * n];
                for (int j = 0; j < n; j++)
                {
                    int row = (n - 1 - j) * n;   // otoceni: sever nahoru
                    for (int i = 0; i < n; i++)
                    {
                        pixels[row + i] = og.State(i, j) switch
                        {
                            CellState.Blocked => BlockedBgra,
                            CellState.Free => FreeBgra,
                            _ => 0u,             // Unknown = pruhledne
                        };
                    }
                }

                handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                bmp.InstallPixels(bmp.Info, handle.AddrOfPinnedObject(), bmp.Info.RowBytes);

                using var image = SKImage.FromBitmap(bmp);
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch (Exception ex)
            {
                // Trace, ne Debug: v Release na zarizeni by Debug mlcel (viz CLAUDE.md).
                System.Diagnostics.Trace.WriteLine($"OccupancyPng: kodovani selhalo: {ex.Message}");
                return null;
            }
            finally
            {
                // MUSI byt tady: kdyz Encode vyhodi, drive unikal pripnuty GCHandle.
                if (handle.IsAllocated) handle.Free();
            }
        }

        // Barvy rastru (BGRA premultiplied, jako u SKColorType.Bgra8888).
        private static readonly uint BlockedBgra = PremulBgra(0xE5, 0x39, 0x35, 0xB0);
        // Free ma vyssi alfu: pri prekryvu se zelenym podkladem OSM se slaba zelena nedala rozeznat.
        private static readonly uint FreeBgra = PremulBgra(0x4C, 0xAF, 0x50, 0x80);

        private static uint PremulBgra(byte r, byte g, byte b, byte a)
        {
            uint rr = (uint)(r * a / 255), gg = (uint)(g * a / 255), bb = (uint)(b * a / 255);
            return ((uint)a << 24) | (rr << 16) | (gg << 8) | bb;
        }
    }
}
```

> Tělo `PremulBgra` je doslova to, co má dnes `WorldViewDocument.cs:1008-1012` — skládání kanálů
> `((uint)a << 24) | (rr << 16) | (gg << 8) | bb` je ověřené proti originálu, nemění se.

- [x] **Krok 4: Spusť test a ověř, že prochází**

```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~OccupancyPngTests"
```

Očekávej 3 zelené. Kdyby test na barvu padl, porovnej `PremulBgra` s originálem.

- [x] **Krok 5: Přidej veřejné kódování obrazu do `ImageMsg`**

Do `Src/ARBot.Common/Logs/ImageMsg.cs`, k ostatním pomocným metodám (privátní `EncodeSkia` zůstává,
tohle je jen obálka — jeden kodér, dvě jména):

```csharp
        /// <summary>
        /// Zakoduje obraz do JPEG (kvalita <see cref="JpegQuality"/>). Podporuje jen 8bit pixely
        /// (step 1 = Gray8, step 4 = BGRA/BGR32); jinak vyhodi <see cref="NotSupportedException"/>.
        ///
        /// <para>Verejne kvuli webovemu nahledu headless runtime (doc/plan-headless-web.md), ktery
        /// posila snimek kamery do prohlizece. Kodovani samo je tentyz <c>EncodeSkia</c>, jaky
        /// pouziva zaznam - druhy kodek by se rozesel.</para>
        /// </summary>
        public static byte[] EncodeJpeg(Common.Image img)
        {
            if (img == null) throw new ArgumentNullException(nameof(img));
            return EncodeSkia(img.Step, img.Width, img.Height, img.Data, SKEncodedImageFormat.Jpeg);
        }

        /// <summary>Zakoduje obraz do PNG (bezztratove). Omezeni jako u <see cref="EncodeJpeg"/>.</summary>
        public static byte[] EncodePng(Common.Image img)
        {
            if (img == null) throw new ArgumentNullException(nameof(img));
            return EncodeSkia(img.Step, img.Width, img.Height, img.Data, SKEncodedImageFormat.Png);
        }
```

- [x] **Krok 6: Napiš a spusť test kodéru**

Do `Src/ARBot.Common.Tests/Rendering/OccupancyPngTests.cs` přidej druhou testovací třídu:

```csharp
    /// <summary>Verejne kodovani obrazu (obalka nad privatnim EncodeSkia) - viz ImageMsg.EncodeJpeg.</summary>
    public class ImageEncodeTests
    {
        [Test]
        public void Bgr32_SeZakodujeDoJpeg()
        {
            var img = new ARBot.Common.Common.Image<ARBot.Common.Common.BGR32>(8, 4);
            byte[] jpeg = ImageMsg.EncodeJpeg(img);

            Assert.That(jpeg, Is.Not.Null.And.Length.GreaterThan(4));
            // Magicke bajty JPEG: FF D8 FF
            Assert.That(jpeg[0], Is.EqualTo(0xFF));
            Assert.That(jpeg[1], Is.EqualTo(0xD8));

            using var back = SkiaSharp.SKBitmap.Decode(jpeg);
            Assert.That(back.Width, Is.EqualTo(8));
            Assert.That(back.Height, Is.EqualTo(4));
        }

        [Test]
        public void NullObraz_Vyhodi()
            => Assert.Throws<System.ArgumentNullException>(() => ImageMsg.EncodeJpeg(null));
    }
```

```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~Rendering"
```

Očekávej 5 zelených. Konstruktor `Image<T>(int width, int height)` existuje
(`Src/ARBot.Common/Common/Image.cs:70`), `Step` si dopočítá z typu pixelu.

- [x] **Krok 7: Přepoj UI na Common a smaž duplikát**

Ve `Src/ARBot/ViewModels/WorldViewDocument.cs`: v `UpdateOccupancyFeature` zaměň
`EncodeOccupancyPng(og)` za `ARBot.Common.Rendering.OccupancyPng.Encode(og)` a **smaž** privátní
`EncodeOccupancyPng`, `OccBlockedBgra`, `OccFreeBgra` i `PremulBgra`. Komentář o `Unknown`
a o ladění se přesunul do nové třídy, takže se nemaže, jen nezůstává na dvou místech.

- [x] **Krok 8: Ověř build a celou sadu**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
dotnet build Src/ARBot.slnx -p:Platform=OrangePI
dotnet test Src/ARBot.Common.Tests -p:Platform=x64
```

Očekávej 0 chyb a **1 136 zelených** (baseline 1 131 + 5 nových).

---

### Task 2: `PlanViewRenderer` — půdorys ze zpráv

**Soubory:**
- Create: `Src/ARBot.Common/Rendering/PlanViewRenderer.cs`
- Test: `Src/ARBot.Common.Tests/Rendering/PlanViewRendererTests.cs`

**Rozhraní:**
- Consumes: `OccupancyPng` z Tasku 1 se **nepoužívá** (půdorys kreslí buňky sám, aby je dostal
  do jednoho obrázku se sítí a pózou).
- Produces:
  ```csharp
  public sealed class PlanViewInput {
      public OccupancyGridMsg Grid;
      public RoadNetwork Network;      // uzly v LLA
      public GeoReference Origin;      // pocatek lokalni ENU roviny
      public bool HasPose; public double PoseX, PoseY, PoseTheta;
      public bool HasCarrot; public double CarrotX, CarrotY;
      public IReadOnlyList<PlanViewPoint> Trail;
  }
  public readonly struct PlanViewPoint { public readonly double X, Y; public PlanViewPoint(double x, double y); }
  public sealed class PlanViewOptions { public int SizePx = 512; public double SpanM = 40; }
  public static class PlanViewRenderer {
      public static byte[] Render(PlanViewInput input, PlanViewOptions options = null);
  }
  ```

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Common.Tests/Rendering/PlanViewRendererTests.cs`:

```csharp
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Rendering;

namespace ARBot.Common.Tests.Rendering
{
    /// <summary>
    /// Pudorys pro webovy nahled headless runtime: occupancy grid + sit cest + poza + mrkev.
    /// Vsechno v lokalni ENU rovine, sever nahoru, robot ve stredu. Viz doc/plan-headless-web.md.
    /// </summary>
    public class PlanViewRendererTests
    {
        private static PlanViewOptions Opt() => new PlanViewOptions { SizePx = 128, SpanM = 20 };

        [Test]
        public void PrazdnyVstup_NespadneAVratiObrazek()
        {
            byte[] png = PlanViewRenderer.Render(new PlanViewInput(), Opt());

            Assert.That(png, Is.Not.Null, "bez dat se kresli prazdna scena, ne null");
            using var bmp = SkiaSharp.SKBitmap.Decode(png);
            Assert.Multiple(() =>
            {
                Assert.That(bmp.Width, Is.EqualTo(128));
                Assert.That(bmp.Height, Is.EqualTo(128));
            });
        }

        [Test]
        public void NullVstup_Vyhodi()
            => Assert.Throws<System.ArgumentNullException>(() => PlanViewRenderer.Render(null, Opt()));

        [Test]
        public void NeprujezdnaBunkaSeObjeviCervene()
        {
            // Grid 8x8 po 1 m se stredem v pocatku; bunka na severu od robota je neprujezdna.
            var og = new OccupancyGridMsg
            {
                Size = 8, Resolution = 1.0, OriginX = -4, OriginY = -4,
                Scale = 1f, BlockedThreshold = 0.5f, FreeThreshold = -0.5f,
                Occ = new sbyte[64], Road = new sbyte[64],
            };
            // (i=4, j=6) => stred (0.5, 2.5) m, tedy 2,5 m severne od robota v pocatku.
            og.Occ[6 * 8 + 4] = 100; og.Road[6 * 8 + 4] = 100;

            var input = new PlanViewInput { Grid = og, HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0 };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            // 20 m na 128 px => 6,4 px/m. Bod (0.5, 2.5) je 0,5 m vpravo a 2,5 m nahoru od stredu.
            int px = (int)(64 + 0.5 * 6.4);
            int py = (int)(64 - 2.5 * 6.4);
            var c = bmp.GetPixel(px, py);

            Assert.That(c.Red, Is.GreaterThan(c.Green).And.GreaterThan(c.Blue),
                        "neprujezdna bunka severne od robota ma byt cervena nad stredem obrazku");
        }

        [Test]
        public void RobotJeVeStredu_AKurzMeniTvar()
        {
            var input = new PlanViewInput { HasPose = true, PoseX = 12, PoseY = -7, PoseTheta = 0 };
            using var a = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            // Stred obrazku patri robotovi bez ohledu na jeho svetovou pozici (vyrez ho sleduje).
            var stred = a.GetPixel(64, 64);
            Assert.That(stred.Alpha, Is.GreaterThan(0));

            // Otoceni o 90 stupnu musi obrazek zmenit (trojuhelnik miri jinam).
            input.PoseTheta = System.Math.PI / 2;
            using var b = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));
            bool nejakyRozdil = false;
            for (int y = 54; y < 74 && !nejakyRozdil; y++)
                for (int x = 54; x < 74; x++)
                    if (a.GetPixel(x, y) != b.GetPixel(x, y)) { nejakyRozdil = true; break; }

            Assert.That(nejakyRozdil, Is.True, "kurz robota se ma na pudorysu poznat");
        }

        [Test]
        public void SitCestSeVykresliZUzluVLLA()
        {
            // Dva uzly 10 m od sebe podel osy vychod-zapad; kresli se pruh siroky podle Node.Width.
            // RoadNetwork ma privatni konstruktor - stavi se Builderem (jako CorrelationTestScenes).
            var origin = GeoReference.FromDegrees(50.029, 14.52);
            var a = new Node(1, origin.ToLLA(-5, 0), 2.0);
            var b = new Node(2, origin.ToLLA(5, 0), 2.0);
            var builder = new RoadNetwork.Builder();
            builder.AddEdge(a, b, 10, wayId: 100, traversalCost: 10);
            var net = builder.Build();

            var input = new PlanViewInput
            {
                Network = net, Origin = origin,
                HasPose = true, PoseX = 0, PoseY = 0, PoseTheta = 0,
            };
            using var bmp = SkiaSharp.SKBitmap.Decode(PlanViewRenderer.Render(input, Opt()));

            // Bod 2 m vychodne od robota lezi na ose cesty -> nesmi byt cista barva pozadi.
            var naCeste = bmp.GetPixel((int)(64 + 2 * 6.4), 64);
            var mimoCestu = bmp.GetPixel(64, (int)(64 - 8 * 6.4));   // 8 m severne, mimo pruh
            Assert.That(naCeste, Is.Not.EqualTo(mimoCestu), "pruh cesty ma byt videt");
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~PlanViewRendererTests"
```

Očekávej chybu překladu: `PlanViewRenderer` neexistuje.

- [x] **Krok 3: Napiš `PlanViewRenderer`**

`Src/ARBot.Common/Rendering/PlanViewRenderer.cs`. Pořadí kreslení je zdola nahoru: pozadí, pruhy
cest, buňky gridu, trajektorie, mrkev, robot, měřítko.

```csharp
using System;
using System.Collections.Generic;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Maps.OsmNav.Graph;
using ARBot.Common.Occupancy;
using SkiaSharp;

namespace ARBot.Common.Rendering
{
    /// <summary>Bod trajektorie v lokalni ENU rovine [m].</summary>
    public readonly struct PlanViewPoint
    {
        public readonly double X, Y;
        public PlanViewPoint(double x, double y) { X = x; Y = y; }
    }

    /// <summary>Co se ma na pudorys nakreslit. Vsechno v lokalni ENU rovine (krome uzlu mapy, ty jsou LLA).</summary>
    public sealed class PlanViewInput
    {
        /// <summary>Lokalni mapa (to, co robot vidi). Null = nekresli se.</summary>
        public OccupancyGridMsg Grid;
        /// <summary>Sit cest z mapy (uzly v LLA). Null = nekresli se.</summary>
        public RoadNetwork Network;
        /// <summary>Pocatek lokalni ENU roviny - bez nej se sit nakreslit neda.</summary>
        public GeoReference Origin;

        public bool HasPose;
        public double PoseX, PoseY, PoseTheta;

        public bool HasCarrot;
        public double CarrotX, CarrotY;

        /// <summary>Ujeta draha (nejstarsi prvni). Null nebo prazdne = nekresli se.</summary>
        public IReadOnlyList<PlanViewPoint> Trail;
    }

    /// <summary>Rozmery vykresu.</summary>
    public sealed class PlanViewOptions
    {
        /// <summary>Strana obrazku [px].</summary>
        public int SizePx = 512;
        /// <summary>Sirka vyrezu [m] - kolik metru se vejde na stranu obrazku.</summary>
        public double SpanM = 40;
    }

    /// <summary>
    /// <b>Pudorys okoli robota</b> do PNG: occupancy grid nad sit cest, plus poza, mrkev
    /// a ujeta draha. Sever nahoru, robot ve stredu vyrezu; kdyz poza neni, stred je pocatek
    /// lokalni roviny.
    ///
    /// <para><b>Nacpak to je:</b> webovy nahled headless runtime (doc/headless.md) - jeden obrazek,
    /// ze ktereho se pozna, jestli robot vidi cestu, kam mu ukazuje mrkev a proc pripadne stoji.
    /// Kresli se <b>ze zprav</b>, takze na to vidi i <c>ARBot.Analyze</c> nad zaznamem.</para>
    ///
    /// <para>Bez UI a bez HAL (SkiaSharp je v Common kvuli <see cref="ImageMsg"/>).</para>
    /// </summary>
    public static class PlanViewRenderer
    {
        public static byte[] Render(PlanViewInput input, PlanViewOptions options = null)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var opt = options ?? new PlanViewOptions();
            int n = Math.Max(32, opt.SizePx);
            double span = opt.SpanM > 0 ? opt.SpanM : 40;
            double pxPerM = n / span;

            // Stred vyrezu: robot, nebo pocatek lokalni roviny, kdyz poza jeste neni.
            double cx = input.HasPose ? input.PoseX : 0;
            double cy = input.HasPose ? input.PoseY : 0;

            // ENU -> pixely. Sever nahoru, takze y je obracene.
            float PX(double x) => (float)(n / 2.0 + (x - cx) * pxPerM);
            float PY(double y) => (float)(n / 2.0 - (y - cy) * pxPerM);

            try
            {
                using var surface = SKSurface.Create(new SKImageInfo(n, n, SKColorType.Bgra8888, SKAlphaType.Premul));
                var c = surface.Canvas;
                c.Clear(new SKColor(0x14, 0x18, 0x1C));

                DrawNetwork(c, input, PX, PY, pxPerM);
                DrawGrid(c, input.Grid, PX, PY, pxPerM);
                DrawTrail(c, input.Trail, PX, PY);
                DrawCarrot(c, input, PX, PY, pxPerM);
                DrawRobot(c, input, PX, PY, pxPerM);
                DrawScale(c, n, span);

                using var image = surface.Snapshot();
                using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                return data?.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"PlanViewRenderer: kresleni selhalo: {ex.Message}");
                return null;
            }
        }

        /// <summary>Pruhy cest ze site: uzly jsou v LLA, prevod dela GeoReference.</summary>
        private static void DrawNetwork(SKCanvas c, PlanViewInput input,
                                        Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (input.Network?.Edges == null || input.Origin == null) return;

            using var paint = new SKPaint { Color = new SKColor(0x55, 0x5A, 0x60), IsAntialias = true,
                                            Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            using var axis = new SKPaint { Color = new SKColor(0x8A, 0x90, 0x98), IsAntialias = true,
                                           Style = SKPaintStyle.Stroke, StrokeWidth = 1 };

            foreach (var e in input.Network.Edges)
            {
                var a = input.Origin.ToLocal(e.From.Location);
                var b = input.Origin.ToLocal(e.To.Location);

                // Sirka cesty: uzly ji nesou kazdy svou (0 = neurceno) - vezmeme vetsi z nich.
                double w = Math.Max(e.From.Width, e.To.Width);
                paint.StrokeWidth = (float)(Math.Max(w, 0.5) * pxPerM);

                c.DrawLine(PX(a.X), PY(a.Y), PX(b.X), PY(b.Y), paint);
                c.DrawLine(PX(a.X), PY(a.Y), PX(b.X), PY(b.Y), axis);
            }
        }

        /// <summary>Bunky lokalni mapy: neprujezdne cervene, potvrzene volne zelene, nezname nic.</summary>
        private static void DrawGrid(SKCanvas c, OccupancyGridMsg og,
                                     Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (og?.Occ == null || og.Size <= 0) return;

            using var blocked = new SKPaint { Color = new SKColor(0xE5, 0x39, 0x35, 0xB0) };
            using var free = new SKPaint { Color = new SKColor(0x4C, 0xAF, 0x50, 0x70) };

            float side = (float)(og.Resolution * pxPerM) + 1f;   // +1 px, aby mezi bunkami nebyly spary
            for (int j = 0; j < og.Size; j++)
            {
                for (int i = 0; i < og.Size; i++)
                {
                    var st = og.State(i, j);
                    if (st == CellState.Unknown) continue;

                    float x = PX(og.CenterX(i)), y = PY(og.CenterY(j));
                    if (x < -side || y < -side) continue;   // hruby vyrez, at se nekresli mimo

                    var rect = new SKRect(x - side / 2, y - side / 2, x + side / 2, y + side / 2);
                    c.DrawRect(rect, st == CellState.Blocked ? blocked : free);
                }
            }
        }

        private static void DrawTrail(SKCanvas c, IReadOnlyList<PlanViewPoint> trail,
                                      Func<double, float> PX, Func<double, float> PY)
        {
            if (trail == null || trail.Count < 2) return;

            using var paint = new SKPaint { Color = new SKColor(0x42, 0xA5, 0xF5), IsAntialias = true,
                                            Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            using var path = new SKPath();
            path.MoveTo(PX(trail[0].X), PY(trail[0].Y));
            for (int k = 1; k < trail.Count; k++) path.LineTo(PX(trail[k].X), PY(trail[k].Y));
            c.DrawPath(path, paint);
        }

        /// <summary>Mrkev (cil lokalni vrstvy) jako kruzek a spojnice od robota.</summary>
        private static void DrawCarrot(SKCanvas c, PlanViewInput input,
                                       Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (!input.HasCarrot) return;

            using var paint = new SKPaint { Color = new SKColor(0xFF, 0xC1, 0x07), IsAntialias = true,
                                            Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            float x = PX(input.CarrotX), y = PY(input.CarrotY);
            c.DrawCircle(x, y, (float)Math.Max(4, 0.3 * pxPerM), paint);
            if (input.HasPose)
                c.DrawLine(PX(input.PoseX), PY(input.PoseY), x, y, paint);
        }

        /// <summary>Robot jako trojuhelnik miric po kurzu (0 = vychod, +CCW - viz doc/imu-and-frames.md).</summary>
        private static void DrawRobot(SKCanvas c, PlanViewInput input,
                                      Func<double, float> PX, Func<double, float> PY, double pxPerM)
        {
            if (!input.HasPose) return;

            float x = PX(input.PoseX), y = PY(input.PoseY);
            float r = (float)Math.Max(6, 0.5 * pxPerM);
            double th = input.PoseTheta;

            using var body = new SKPaint { Color = new SKColor(0xFF, 0xFF, 0xFF), IsAntialias = true };
            using var path = new SKPath();
            // Vrchol ve smeru kurzu, dve zadni rohy o +-140 stupnu. Pozor na obraceny smysl y v pixelech.
            path.MoveTo(x + (float)(r * Math.Cos(th)), y - (float)(r * Math.Sin(th)));
            path.LineTo(x + (float)(r * 0.7 * Math.Cos(th + 2.44)), y - (float)(r * 0.7 * Math.Sin(th + 2.44)));
            path.LineTo(x + (float)(r * 0.7 * Math.Cos(th - 2.44)), y - (float)(r * 0.7 * Math.Sin(th - 2.44)));
            path.Close();
            c.DrawPath(path, body);
        }

        /// <summary>Meritko v levem dolnim rohu - bez nej se z obrazku nepozna vzdalenost.</summary>
        private static void DrawScale(SKCanvas c, int n, double span)
        {
            using var paint = new SKPaint { Color = new SKColor(0xB0, 0xB6, 0xBC), IsAntialias = true,
                                            Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            using var font = new SKFont { Size = 12 };
            using var text = new SKPaint { Color = new SKColor(0xB0, 0xB6, 0xBC), IsAntialias = true };

            double metry = span >= 40 ? 10 : span >= 20 ? 5 : 1;
            float len = (float)(metry * n / span);
            float y = n - 14, x0 = 12;
            c.DrawLine(x0, y, x0 + len, y, paint);
            c.DrawText($"{metry:0} m", x0, y - 6, SKTextAlign.Left, font, text);
        }
    }
}
```

> Přetížení `DrawText(string, float, float, SKTextAlign, SKFont, SKPaint)` je ověřené spuštěním
> proti nainstalované SkiaSharp 3.119 — nehádej, funguje.

- [x] **Krok 4: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~Rendering"
```

Očekávej 10 zelených (5 z Tasku 1 + 5 nových). Kdyby test na červenou buňku padl o pixel, zkontroluj
znaménko v `PY` a indexaci `og.State(i, j)`.

- [x] **Krok 5: Ověř build obou platforem**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
dotnet build Src/ARBot.slnx -p:Platform=OrangePI
```

---

### Task 3: `HttpMini` — parser požadavku a odpověď

Nejmenší GET/POST server, jaký stačí pro prohlížeč, oddělený od socketu, aby šel testovat na
řetězcích a bajtech.

**Soubory:**
- Create: `Src/ARBot.Runtime/Web/HttpMini.cs`
- Test: `Src/ARBot.Runtime.Tests/Web/HttpMiniTests.cs`

**Rozhraní:**
- Produces:
  ```csharp
  namespace ARBot.Robot.Web;
  public readonly struct HttpRequestLine {
      public readonly bool Ok; public readonly string Method, Path, Query;
  }
  public static class HttpMini {
      public const int MaxHeaderBytes = 8 * 1024;
      public static HttpRequestLine ParseRequestLine(string firstLine);
      public static string ReadHeader(System.IO.Stream s, int maxBytes = MaxHeaderBytes);  // null = prekroceno/zavreno
      public static void WriteResponse(System.IO.Stream s, int status, string contentType, byte[] body);
      public static void WriteText(System.IO.Stream s, int status, string text);
  }
  ```

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Runtime.Tests/Web/HttpMiniTests.cs`:

```csharp
using System.IO;
using System.Text;
using ARBot.Robot.Web;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// Minimalni HTTP nad TcpListener (HttpListener na Windows bez admin prav neumi jiny prefix
    /// nez localhost - naměřeno 4. 9. 2026). Viz doc/plan-headless-web.md.
    /// </summary>
    public class HttpMiniTests
    {
        [Test]
        public void RozeberePozadavekAOdstrihneQueryString()
        {
            var r = HttpMini.ParseRequestLine("GET /world.png?t=12345 HTTP/1.1");

            Assert.Multiple(() =>
            {
                Assert.That(r.Ok, Is.True);
                Assert.That(r.Method, Is.EqualTo("GET"));
                Assert.That(r.Path, Is.EqualTo("/world.png"), "query string do routovani nepatri");
                Assert.That(r.Query, Is.EqualTo("t=12345"));
            });
        }

        [Test]
        public void PozadavekBezQuery_MaPrazdnyQuery()
        {
            var r = HttpMini.ParseRequestLine("POST /stop HTTP/1.1");
            Assert.Multiple(() =>
            {
                Assert.That(r.Ok, Is.True);
                Assert.That(r.Method, Is.EqualTo("POST"));
                Assert.That(r.Path, Is.EqualTo("/stop"));
                Assert.That(r.Query, Is.Empty);
            });
        }

        [TestCase("")]
        [TestCase("GET")]
        [TestCase("blabla")]
        public void NesmyslnyRadek_NeniOk(string radek)
            => Assert.That(HttpMini.ParseRequestLine(radek).Ok, Is.False);

        [Test]
        public void PrecteHlavickuAzPoPrazdnyRadek()
        {
            var vstup = new MemoryStream(Encoding.ASCII.GetBytes(
                "GET / HTTP/1.1\r\nHost: pi:8080\r\n\r\nTELO"));

            string hlavicka = HttpMini.ReadHeader(vstup);

            Assert.That(hlavicka, Does.StartWith("GET / HTTP/1.1"));
            Assert.That(hlavicka, Does.Contain("Host: pi:8080"));
            Assert.That(hlavicka, Does.Not.Contain("TELO"), "telo do hlavicky nepatri");
        }

        [Test]
        public void PrilisDlouhaHlavicka_VratiNull()
        {
            var dlouha = "GET / HTTP/1.1\r\nX: " + new string('a', HttpMini.MaxHeaderBytes + 10);
            var vstup = new MemoryStream(Encoding.ASCII.GetBytes(dlouha));

            Assert.That(HttpMini.ReadHeader(vstup, HttpMini.MaxHeaderBytes), Is.Null);
        }

        [Test]
        public void OdpovedMaStavovyRadekDelkuANoStore()
        {
            var vystup = new MemoryStream();
            HttpMini.WriteResponse(vystup, 200, "image/png", new byte[] { 1, 2, 3 });

            string s = Encoding.ASCII.GetString(vystup.ToArray());
            Assert.Multiple(() =>
            {
                Assert.That(s, Does.StartWith("HTTP/1.1 200 OK\r\n"));
                Assert.That(s, Does.Contain("Content-Type: image/png"));
                Assert.That(s, Does.Contain("Content-Length: 3"));
                Assert.That(s, Does.Contain("Cache-Control: no-store"));
                Assert.That(s, Does.Contain("Connection: close"));
                Assert.That(vystup.ToArray()[^3..], Is.EqualTo(new byte[] { 1, 2, 3 }), "telo na konci");
            });
        }

        [Test]
        public void ChybovyStav_MaSpravnyText()
        {
            var vystup = new MemoryStream();
            HttpMini.WriteText(vystup, 404, "nenalezeno");

            string s = Encoding.ASCII.GetString(vystup.ToArray());
            Assert.That(s, Does.StartWith("HTTP/1.1 404 Not Found\r\n"));
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

```bash
dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~HttpMiniTests"
```

Očekávej chybu překladu: `ARBot.Robot.Web` neexistuje.

- [x] **Krok 3: Napiš `HttpMini`**

`Src/ARBot.Runtime/Web/HttpMini.cs`:

```csharp
using System;
using System.IO;
using System.Text;

namespace ARBot.Robot.Web
{
    /// <summary>Rozebrany prvni radek pozadavku.</summary>
    public readonly struct HttpRequestLine
    {
        public readonly bool Ok;
        public readonly string Method;
        /// <summary>Cesta BEZ query stringu (ten se pri routovani zahazuje).</summary>
        public readonly string Path;
        /// <summary>Query string bez uvodniho '?' (prazdny, kdyz nebyl).</summary>
        public readonly string Query;

        public HttpRequestLine(bool ok, string method, string path, string query)
        {
            Ok = ok; Method = method; Path = path; Query = query;
        }
    }

    /// <summary>
    /// <b>Nejmensi HTTP, jake staci prohlizeci.</b> Jen prvni radek + hlavicky do prazdneho radku,
    /// odpoved vzdy s <c>Content-Length</c> a <c>Connection: close</c>. Zadny keep-alive, zadny
    /// chunked, zadne cteni tela (POST /stop ho nepotrebuje).
    ///
    /// <para><b>Proc vlastni a ne <c>HttpListener</c>:</b> ten na Windows bez administratorskych prav
    /// neprijme jiny prefix nez <c>localhost</c> (<c>http://+:port/</c> i <c>http://*:port/</c> skonci
    /// „Pristup byl odepren" - naměřeno 4. 9. 2026). Ladil by se tedy jiny stav, nez jaky bezi na Pi.
    /// Tenhle kod se chova na obou platformach stejne a nepotrebuje URL ACL. Viz doc/headless.md.</para>
    ///
    /// <para>Bez socketu - pracuje nad <see cref="Stream"/>, takze jde otestovat nad <c>MemoryStream</c>.</para>
    /// </summary>
    public static class HttpMini
    {
        /// <summary>Strop na hlavicku pozadavku; delsi se odmitne (413).</summary>
        public const int MaxHeaderBytes = 8 * 1024;

        /// <summary>Rozebere „GET /cesta?dotaz HTTP/1.1". Pri nesmyslu vraci <c>Ok = false</c>.</summary>
        public static HttpRequestLine ParseRequestLine(string firstLine)
        {
            if (string.IsNullOrWhiteSpace(firstLine)) return default;

            var parts = firstLine.Split(' ');
            if (parts.Length < 3) return default;

            string method = parts[0];
            string target = parts[1];
            if (method.Length == 0 || target.Length == 0 || target[0] != '/') return default;

            int q = target.IndexOf('?');
            string path = q >= 0 ? target.Substring(0, q) : target;
            string query = q >= 0 ? target.Substring(q + 1) : string.Empty;
            return new HttpRequestLine(true, method, path, query);
        }

        /// <summary>
        /// Precte hlavicku az po prazdny radek (CRLFCRLF nebo LFLF). Vraci <c>null</c>, kdyz spojeni
        /// skoncilo driv nebo hlavicka prekrocila <paramref name="maxBytes"/>. Cte po bajtech -
        /// hlavicka je male desitky bajtu, takze na tom nezalezi, a nesmi se precist telo.
        /// </summary>
        public static string ReadHeader(Stream s, int maxBytes = MaxHeaderBytes)
        {
            if (s == null) return null;

            var sb = new StringBuilder(256);
            int konec = 0;   // kolik znaku ukoncovaci sekvence uz sedi
            for (int n = 0; n < maxBytes; n++)
            {
                int b = s.ReadByte();
                if (b < 0) return null;          // spojeni zavreno pred koncem hlavicky

                char ch = (char)b;
                sb.Append(ch);

                // Prazdny radek = konec hlavicky. Snesem CRLFCRLF i LFLF (curl, telnet).
                if (ch == '\n')
                {
                    if (konec == 1) return sb.ToString();
                    konec = 1;
                }
                else if (ch != '\r')
                {
                    konec = 0;
                }
            }
            return null;   // prekroceno
        }

        /// <summary>Odesle odpoved s telem. Hlavicky jsou zamerne minimalni.</summary>
        public static void WriteResponse(Stream s, int status, string contentType, byte[] body)
        {
            body ??= Array.Empty<byte>();
            var head = new StringBuilder(160);
            head.Append("HTTP/1.1 ").Append(status).Append(' ').Append(Reason(status)).Append("\r\n");
            if (!string.IsNullOrEmpty(contentType))
                head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            // Nahled je ziva data - cache by ukazovala minulost.
            head.Append("Cache-Control: no-store\r\n");
            head.Append("Connection: close\r\n\r\n");

            var bytes = Encoding.ASCII.GetBytes(head.ToString());
            s.Write(bytes, 0, bytes.Length);
            if (body.Length > 0) s.Write(body, 0, body.Length);
            s.Flush();
        }

        /// <summary>Odesle textovou odpoved (UTF-8).</summary>
        public static void WriteText(Stream s, int status, string text)
            => WriteResponse(s, status, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(text ?? string.Empty));

        private static string Reason(int status) => status switch
        {
            200 => "OK",
            204 => "No Content",
            404 => "Not Found",
            405 => "Method Not Allowed",
            413 => "Payload Too Large",
            500 => "Internal Server Error",
            503 => "Service Unavailable",
            _ => "OK",
        };
    }
}
```

- [x] **Krok 4: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~HttpMiniTests"
```

Očekávej 9 zelených (7 testů, z toho jeden `[TestCase]` třikrát).

---

### Task 4: `WebStatus` a `WebPreviewServer`

**Soubory:**
- Create: `Src/ARBot.Runtime/Web/WebStatus.cs`
- Create: `Src/ARBot.Runtime/Web/WebPreviewServer.cs`
- Test: `Src/ARBot.Runtime.Tests/Web/WebPreviewServerTests.cs` (dvě třídy: server přes `HttpClient`
  a `WebStatus` sám, kvůli línému renderu)

**Rozhraní:**
- Consumes: `HttpMini`, `HttpRequestLine` (Task 3); `PlanViewRenderer`, `PlanViewInput`,
  `PlanViewOptions`, `PlanViewPoint` (Task 2); `ImageMsg.EncodeJpeg` (Task 1).
- Produces:
  ```csharp
  namespace ARBot.Robot.Web;
  public sealed class WebStatus : ARBot.Common.Communication.IMessageSink {
      public WebStatus();
      public void Post(ARBot.Common.Logs.Message msg);
      public void NoteCameraInterest();            // volá server při /camera.jpg
      public byte[] RenderPlanView();              // null = nešlo nakreslit
      public byte[] RenderCameraJpeg(string cam, string layer);  // layer: null/"rgb" | "prob"; null = žádný snímek
      public string ToJson(bool running);
      public string ToHtml();
      public string[] CameraNames { get; }
  }
  public sealed class WebPreviewServer : System.IDisposable {
      public WebPreviewServer(WebStatus status, System.Action onStop);
      public bool Start(int port);   // false = bind selhal (jede se dál bez náhledu)
      public int Port { get; }       // skutečný port po Start (u portu 0 přidělený OS)
      public void Dispose();
  }
  ```

- [x] **Krok 1: Napiš padající test**

`Src/ARBot.Runtime.Tests/Web/WebPreviewServerTests.cs`:

```csharp
using System.Net.Http;
using ARBot.Common.Logs;
using ARBot.Robot.Web;

namespace ARBot.Runtime.Tests.Web
{
    /// <summary>
    /// Webovy nahled: server odpovida na ctyri cesty, POST /stop zavola callback a port 0
    /// si necha pridelit od OS (pevny port by testy rozbil pri soubehu). Viz doc/plan-headless-web.md.
    /// </summary>
    [NonParallelizable]
    public class WebPreviewServerTests
    {
        private WebStatus status;
        private WebPreviewServer server;
        private HttpClient klient;
        private int stopu;

        [SetUp]
        public void Start()
        {
            stopu = 0;
            status = new WebStatus();
            server = new WebPreviewServer(status, () => stopu++);
            Assert.That(server.Start(0), Is.True, "server se ma nastartovat na portu pridelenem OS");
            klient = new HttpClient { BaseAddress = new System.Uri($"http://127.0.0.1:{server.Port}/") };
        }

        [TearDown]
        public void Konec()
        {
            klient?.Dispose();
            server?.Dispose();
        }

        [Test]
        public async Task Koren_VratiHtmlStranku()
        {
            var r = await klient.GetAsync("/");
            string telo = await r.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(200));
                Assert.That(r.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/html"));
                Assert.That(telo, Does.Contain("/world.png"), "stranka ma nacitat pudorys");
                Assert.That(telo, Does.Contain("/camera.jpg"), "stranka ma nacitat kameru");
                Assert.That(telo, Does.Contain("/stop"), "stranka ma mit tlacitko zastaveni");
            });
        }

        [Test]
        public async Task Pudorys_JePngIBezDat()
        {
            var r = await klient.GetAsync("/world.png?t=1");
            byte[] telo = await r.Content.ReadAsByteArrayAsync();

            Assert.Multiple(() =>
            {
                Assert.That((int)r.StatusCode, Is.EqualTo(200));
                Assert.That(r.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
                Assert.That(telo[0], Is.EqualTo(0x89), "magicke bajty PNG");
                Assert.That(telo[1], Is.EqualTo((byte)'P'));
            });
        }

        [Test]
        public async Task Kamera_BezSnimku_Vrati204()
        {
            var r = await klient.GetAsync("/camera.jpg");
            Assert.That((int)r.StatusCode, Is.EqualTo(204), "bez snimku se vraci No Content, ne chyba");
        }

        [Test]
        public async Task Kamera_PosleRgbIPravdepodobnostCesty()
        {
            // Snimek se kopiruje jen pri zajmu - o ten se prvni pozadavek postara.
            await klient.GetAsync("/camera.jpg");

            var frame = new ARBot.Common.Devices.CameraFrame
            {
                Name = "Left",
                ImageRGB = new ARBot.Common.Common.Image<ARBot.Common.Common.BGR32>(8, 4),
                ImageProbability = new ARBot.Common.Common.Image<ARBot.Common.Common.Gray>(8, 4),
            };
            status.Post(frame);

            var rgb = await klient.GetAsync("/camera.jpg?cam=Left");
            var prob = await klient.GetAsync("/camera.jpg?cam=Left&layer=prob");
            byte[] rgbTelo = await rgb.Content.ReadAsByteArrayAsync();
            byte[] probTelo = await prob.Content.ReadAsByteArrayAsync();

            Assert.Multiple(() =>
            {
                Assert.That((int)rgb.StatusCode, Is.EqualTo(200));
                Assert.That(rgb.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/jpeg"));
                Assert.That(rgbTelo[0], Is.EqualTo(0xFF), "magicke bajty JPEG");
                Assert.That(rgbTelo[1], Is.EqualTo(0xD8));
                Assert.That((int)prob.StatusCode, Is.EqualTo(200), "layer=prob posila ImageProbability");
                Assert.That(probTelo[0], Is.EqualTo(0xFF));
            });
            Assert.That(status.CameraNames, Does.Contain("Left"));
        }

        [Test]
        public async Task Status_JeJsonSeStavemMise()
        {
            status.Post(new RobotStateMsg { X = 1.5, Y = -2.5, Theta = 0.25, V = 0.8 });

            var r = await klient.GetAsync("/status.json");
            string json = await r.Content.ReadAsStringAsync();

            Assert.Multiple(() =>
            {
                Assert.That(r.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
                Assert.That(json, Does.Contain("\"v\""), "rychlost patri do stavu");
                Assert.That(json, Does.Contain("0.8"));
            });
        }

        [Test]
        public async Task NeznamaCesta_Vrati404()
        {
            var r = await klient.GetAsync("/neexistuje");
            Assert.That((int)r.StatusCode, Is.EqualTo(404));
        }

        [Test]
        public async Task StopJenPostem()
        {
            var get = await klient.GetAsync("/stop");
            Assert.Multiple(() =>
            {
                Assert.That((int)get.StatusCode, Is.EqualTo(405), "GET nesmi zastavit robota");
                Assert.That(stopu, Is.EqualTo(0));
            });

            var post = await klient.PostAsync("/stop", new StringContent(string.Empty));
            Assert.Multiple(() =>
            {
                Assert.That((int)post.StatusCode, Is.EqualTo(200));
                Assert.That(stopu, Is.EqualTo(1), "POST /stop ma zavolat callback presne jednou");
            });
        }

        [Test]
        public async Task PudorysUkazeNeprujezdnouBunku()
        {
            var og = new OccupancyGridMsg
            {
                Size = 8, Resolution = 1.0, OriginX = -4, OriginY = -4,
                Scale = 1f, BlockedThreshold = 0.5f, FreeThreshold = -0.5f,
                Occ = new sbyte[64], Road = new sbyte[64],
            };
            og.Occ[6 * 8 + 4] = 100; og.Road[6 * 8 + 4] = 100;
            status.Post(og);
            status.Post(new RobotStateMsg { X = 0, Y = 0, Theta = 0 });

            byte[] png = await klient.GetByteArrayAsync("/world.png");
            using var bmp = SkiaSharp.SKBitmap.Decode(png);

            bool nasel = false;
            for (int y = 0; y < bmp.Height && !nasel; y++)
                for (int x = 0; x < bmp.Width; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.Red > 150 && c.Green < 100) { nasel = true; break; }
                }

            Assert.That(nasel, Is.True, "neprujezdna bunka ma byt na pudorysu cervene videt");
        }
    }

    /// <summary>
    /// Sam <see cref="WebStatus"/> bez serveru - hlavne <b>lizny render</b>: bez zajmu se snimek
    /// kamery vubec nekopiruje, takze nahled bez publika nestoji ani memcpy. To je jadro navrhu,
    /// protoze rozpocet CPU na Pi neznama - viz doc/plan-headless-web.md.
    /// </summary>
    public class WebStatusTests
    {
        private static ARBot.Common.Devices.CameraFrame Snimek() => new ARBot.Common.Devices.CameraFrame
        {
            Name = "Left",
            ImageRGB = new ARBot.Common.Common.Image<ARBot.Common.Common.BGR32>(8, 4),
        };

        [Test]
        public void BezZajmu_SeSnimekNezkopiruje()
        {
            var status = new WebStatus();

            status.Post(Snimek());

            Assert.Multiple(() =>
            {
                Assert.That(status.CameraNames, Is.Empty, "bez zajmu se snimek zahodi bez kopirovani");
                Assert.That(status.RenderCameraJpeg(null, null), Is.Null);
            });
        }

        [Test]
        public void PoOhlaseniZajmu_SeSnimekZkopirujeAZakoduje()
        {
            var status = new WebStatus();

            status.NoteCameraInterest();
            status.Post(Snimek());

            Assert.Multiple(() =>
            {
                Assert.That(status.CameraNames, Does.Contain("Left"));
                Assert.That(status.RenderCameraJpeg(null, null), Is.Not.Null);
            });
        }

        [Test]
        public void UjetaDrahaSeSbiraAzOdUrciteVzdalenosti()
        {
            var status = new WebStatus();

            status.Post(new RobotStateMsg { X = 0, Y = 0 });
            status.Post(new RobotStateMsg { X = 0.01, Y = 0 });   // pod prahem 0,1 m -> nezapise se
            status.Post(new RobotStateMsg { X = 1.0, Y = 0 });    // nad prahem -> zapise se

            // Draha neni verejna; overi se pres to, ze pudorys jde nakreslit a stav nese posledni pozici.
            string json = status.ToJson(running: true);
            Assert.Multiple(() =>
            {
                Assert.That(json, Does.Contain("\"x\":1"));
                Assert.That(status.RenderPlanView(), Is.Not.Null);
            });
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

```bash
dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64 --filter "FullyQualifiedName~WebPreviewServerTests"
```

Očekávej chybu překladu: `WebStatus` a `WebPreviewServer` neexistují.

- [x] **Krok 3: Napiš `WebStatus`**

`Src/ARBot.Runtime/Web/WebStatus.cs`. Klíčové vlastnosti: `Post` běží na vlákně producenta, takže
jen ukládá; kamera se kopíruje **jen při zájmu**; render se dělá až v obsluze požadavku.

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Rendering;

namespace ARBot.Robot.Web
{
    /// <summary>
    /// <b>Stav pro webovy nahled</b> - odberatel <see cref="ARBotRuntime.Stream"/> s politikou
    /// „latest-wins" a <b>liznym renderem</b>: <see cref="Post"/> jen ulozi posledni zpravu daneho
    /// druhu, obrazky se kresli teprve v obsluze pozadavku. Kdyz se nikdo nekouka, nahled nestoji nic.
    ///
    /// <para><b>Kamera je vyjimka.</b> <see cref="CameraFrame"/> nese <b>poolovane</b> capture buffery,
    /// ktere kamera recykluje, takze si na nej nejde drzet referenci - musi se poridit kopie z
    /// <see cref="CameraFramePool"/>. Kopie se ale dela <b>jen kdyz o snimek nekdo v poslednich
    /// <see cref="CameraInterestSec"/> sekundach stal</b> (<see cref="NoteCameraInterest"/>); jinak se
    /// snimek zahodi bez kopirovani. Tim nahled bez publika nestoji ani memcpy.</para>
    ///
    /// <para>Ostatni zpravy (<see cref="OccupancyGridMsg"/>, <see cref="RobotStateMsg"/>, …) si svoje
    /// pole alokuji samy (viz <c>OccupancyGrid.ToLogMessage</c>), takze u nich staci reference.</para>
    /// </summary>
    public sealed class WebStatus : IMessageSink
    {
        /// <summary>Jak dlouho po pozadavku na snimek se snimky jeste kopiruji [s].</summary>
        public const double CameraInterestSec = 10;
        /// <summary>Kolik bodu ujete drahy se pamatuje.</summary>
        private const int TrailCapacity = 600;
        /// <summary>Kratsi posun se do drahy nezapisuje [m].</summary>
        private const double TrailMinStepM = 0.1;

        private readonly object gate = new object();
        private readonly CameraFramePool framePool = new CameraFramePool(2);
        private readonly Dictionary<string, CameraFrame> cameras = new Dictionary<string, CameraFrame>();
        private readonly List<PlanViewPoint> trail = new List<PlanViewPoint>(TrailCapacity);

        private OccupancyGridMsg grid;
        private RobotStateMsg state;
        private GlobalNavMsg nav;
        private MissionMsg mission;
        private FreeRunMsg freeRun;
        private LocalPlanMsg plan;
        private PerfMsg perf;
        private DateTime cameraInterest = DateTime.MinValue;

        /// <summary>Jmena kamer, ze kterych uz snimek prisel.</summary>
        public string[] CameraNames
        {
            get { lock (gate) { var k = new string[cameras.Count]; cameras.Keys.CopyTo(k, 0); return k; } }
        }

        /// <summary>Rekni, ze o snimky kamery ma nekdo zajem (vola server pri /camera.jpg).</summary>
        public void NoteCameraInterest()
        {
            lock (gate) cameraInterest = DateTime.UtcNow;
        }

        // --- IMessageSink: bezi na vlakne producenta, MUSI byt neblokujici a skoupe na alokace. ---
        public void Post(Message msg)
        {
            if (msg == null) return;

            switch (msg)
            {
                case CameraFrame cf:
                    PostCamera(cf);
                    return;
                case OccupancyGridMsg og:
                    lock (gate) grid = og;
                    return;
                case RobotStateMsg rs:
                    PostState(rs);
                    return;
                case GlobalNavMsg gn:
                    lock (gate) nav = gn;
                    return;
                case MissionMsg mm:
                    lock (gate) mission = mm;
                    return;
                case FreeRunMsg fr:
                    lock (gate) freeRun = fr;
                    return;
                case LocalPlanMsg lp:
                    lock (gate) plan = lp;
                    return;
                case PerfMsg pm:
                    lock (gate) perf = pm;
                    return;
            }
        }

        private void PostCamera(CameraFrame cf)
        {
            // Bez zajmu se snimek ani nekopiruje - to je cely trik, jak nahled bez publika nic nestoji.
            lock (gate)
            {
                if ((DateTime.UtcNow - cameraInterest).TotalSeconds > CameraInterestSec) return;
            }

            var copy = framePool.Acquire(cf);
            if (copy == null) return;   // pool vycerpan -> drop (nech stary snimek)

            string key = cf.Name ?? string.Empty;
            CameraFrame old = null;
            lock (gate)
            {
                cameras.TryGetValue(key, out old);
                cameras[key] = copy;
            }
            if (old != null) framePool.Release(old);   // vraceni mimo zamek
        }

        private void PostState(RobotStateMsg rs)
        {
            lock (gate)
            {
                state = rs;
                if (trail.Count == 0)
                {
                    trail.Add(new PlanViewPoint(rs.X, rs.Y));
                    return;
                }
                var last = trail[trail.Count - 1];
                double dx = rs.X - last.X, dy = rs.Y - last.Y;
                if (dx * dx + dy * dy < TrailMinStepM * TrailMinStepM) return;

                if (trail.Count >= TrailCapacity) trail.RemoveAt(0);
                trail.Add(new PlanViewPoint(rs.X, rs.Y));
            }
        }

        /// <summary>Nakresli pudorys z posledniho stavu. Null = nepodarilo se.</summary>
        public byte[] RenderPlanView()
        {
            PlanViewInput input;
            lock (gate)
            {
                input = new PlanViewInput
                {
                    Grid = grid,
                    Network = ARBotRuntime.HasCurrent ? ARBotRuntime.Current.RoadNetwork : null,
                    Origin = ARBotRuntime.HasCurrent ? ARBotRuntime.Current.MapOrigin : null,
                    HasPose = state != null,
                    PoseX = state?.X ?? 0,
                    PoseY = state?.Y ?? 0,
                    PoseTheta = state?.Theta ?? 0,
                    Trail = trail.ToArray(),
                };

                // Mrkev: globalni navigace ji ma jako CarrotX/Y, mise FreeRun jako GoalX/Y.
                if (nav != null && nav.HasCarrot) { input.HasCarrot = true; input.CarrotX = nav.CarrotX; input.CarrotY = nav.CarrotY; }
                else if (freeRun != null) { input.HasCarrot = true; input.CarrotX = freeRun.GoalX; input.CarrotY = freeRun.GoalY; }
            }
            return PlanViewRenderer.Render(input);
        }

        /// <summary>
        /// Zakoduje posledni snimek dane kamery do JPEG. <paramref name="cam"/> null nebo prazdne =
        /// prvni, ktera je k dispozici. Null = zadny snimek (server vrati 204).
        ///
        /// <para><paramref name="layer"/> = <c>"prob"</c> posle misto RGB
        /// <see cref="CameraFrame.ImageProbability"/>, tedy <b>pravdepodobnost cesty z RGB</b> - to,
        /// co robot povazuje za cestu jeste pred fuzi do mapy (plni <c>CameraFrameProcessor</c>, cte
        /// <c>OccupancyIntegrator</c>). Je to <c>Image&lt;Gray&gt;</c> (step 1), takze do JPEG jde
        /// stejnym kodekem bez prevodu. Cokoliv jineho = RGB.</para>
        /// </summary>
        public byte[] RenderCameraJpeg(string cam, string layer)
        {
            CameraFrame frame = null;
            lock (gate)
            {
                if (!string.IsNullOrEmpty(cam)) cameras.TryGetValue(cam, out frame);
                else foreach (var kv in cameras) { frame = kv.Value; break; }
            }
            if (frame == null) return null;

            bool prob = string.Equals(layer, "prob", StringComparison.OrdinalIgnoreCase);
            ARBot.Common.Common.Image img = prob ? frame.ImageProbability : frame.ImageRGB;
            if (img == null) return null;

            try { return ImageMsg.EncodeJpeg(img); }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine($"WebStatus: kodovani snimku selhalo: {ex.Message}");
                return null;
            }
        }

        /// <summary>Stav jako JSON - tentyz obsah, jaky ma tabulka na strance.</summary>
        public string ToJson(bool running)
        {
            var sb = new StringBuilder(512);
            lock (gate)
            {
                sb.Append('{');
                sb.Append("\"running\":").Append(running ? "true" : "false");
                Num(sb, "x", state?.X); Num(sb, "y", state?.Y); Num(sb, "theta", state?.Theta);
                Num(sb, "v", state?.V); Num(sb, "omega", state?.Omega);
                Num(sb, "planLength", plan?.LengthM); Num(sb, "clearance", plan?.MinClearanceM);
                Num(sb, "offRoute", nav?.OffRouteDist); Num(sb, "routeLength", nav?.RouteLengthM);
                Num(sb, "cpu", perf?.ProcessCpuPct);
                if (perf != null) sb.Append(",\"missedTicks\":").Append(perf.MissedTicks);
                if (mission != null)
                {
                    sb.Append(",\"missionPhase\":").Append(mission.Phase);
                    sb.Append(",\"missionElapsed\":").Append(Fmt(mission.ElapsedSec));
                    Str(sb, "missionCode", mission.AcceptedCodeText);
                    Str(sb, "missionAbort", mission.AbortReason);
                }
                if (freeRun != null)
                {
                    sb.Append(",\"corridor\":").Append(freeRun.FromCorridor ? "true" : "false");
                    Num(sb, "corridorWidth", freeRun.Width);
                    Num(sb, "lateral", freeRun.Lateral);
                }
                // Jmena kamer pod tymz zamkem - property CameraNames by brala zamek znovu.
                if (cameras.Count > 0)
                {
                    var jmena = new string[cameras.Count];
                    cameras.Keys.CopyTo(jmena, 0);
                    Str(sb, "cameras", string.Join(",", jmena));
                }
                sb.Append('}');
            }
            return sb.ToString();

            void Num(StringBuilder b, string name, double? v)
            {
                if (v.HasValue) b.Append(",\"").Append(name).Append("\":").Append(Fmt(v.Value));
            }
            void Str(StringBuilder b, string name, string v)
            {
                if (!string.IsNullOrEmpty(v))
                    b.Append(",\"").Append(name).Append("\":\"").Append(v.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            }
        }

        private static string Fmt(double v)
            => double.IsFinite(v) ? v.ToString("0.###", CultureInfo.InvariantCulture) : "null";

        /// <summary>Stranka nahledu. Zadne externi zdroje - Pi je offline.</summary>
        public string ToHtml() => Html;

        private const string Html = @"<!doctype html>
<html lang=""cs""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>ARBot - nahled</title>
<style>
 body{background:#14181c;color:#e6e9ec;font:14px system-ui,sans-serif;margin:0;padding:12px}
 h1{font-size:16px;margin:0 0 10px}
 img{width:100%;max-width:520px;display:block;background:#0b0e11;border:1px solid #2a2f35;margin-bottom:10px}
 table{border-collapse:collapse;font-variant-numeric:tabular-nums}
 td{padding:2px 10px 2px 0}
 td:first-child{color:#9aa0a6}
 button{background:#c62828;color:#fff;border:0;padding:12px 18px;font-size:15px;border-radius:4px;margin-top:12px}
 button.prep{background:#37474f;padding:6px 12px;font-size:13px;margin:0 6px 8px 0}
 #stav{color:#9aa0a6;margin-top:8px}
</style></head><body>
<h1>ARBot - nahled</h1>
<img id=""world"" alt=""pudorys"">
<div>
 <button class=""prep"" onclick=""vrstva('rgb')"">kamera</button>
 <button class=""prep"" onclick=""vrstva('prob')"">cesta z RGB</button>
</div>
<img id=""cam"" alt=""kamera"">
<table id=""tab""></table>
<button onclick=""zastavit()"">Zastavit robota</button>
<div id=""stav"">spojuji se...</div>
<script>
var popisky={running:'bezi',x:'X [m]',y:'Y [m]',theta:'kurz [rad]',v:'rychlost [m/s]',omega:'omega [rad/s]',
 planLength:'plan [m]',clearance:'odstup [m]',offRoute:'mimo trasu [m]',routeLength:'trasa [m]',
 cpu:'CPU procesu [%]',missedTicks:'zameskane takty',missionPhase:'faze mise',missionElapsed:'mise [s]',
 missionCode:'kod',missionAbort:'preruseno',corridor:'koridor',corridorWidth:'sirka koridoru [m]',
 lateral:'odchylka [m]',cameras:'kamery'};
var vrstvaKamery='rgb';
function vrstva(v){ vrstvaKamery=v; tik(); }
function tik(){
 var t=Date.now();
 document.getElementById('world').src='/world.png?t='+t;
 document.getElementById('cam').src='/camera.jpg?layer='+vrstvaKamery+'&t='+t;
 fetch('/status.json').then(function(r){return r.json()}).then(function(d){
  var h='';
  for(var k in d){ h+='<tr><td>'+(popisky[k]||k)+'</td><td>'+d[k]+'</td></tr>'; }
  document.getElementById('tab').innerHTML=h;
  document.getElementById('stav').textContent=d.running?'runtime bezi':'runtime zastaven';
 }).catch(function(){ document.getElementById('stav').textContent='server neodpovida'; });
}
function zastavit(){
 if(!confirm('Zastavit robota a ukoncit proces?'))return;
 fetch('/stop',{method:'POST'}).then(function(){
  document.getElementById('stav').textContent='zastaveno';
 });
}
tik(); setInterval(tik,1000);
</script></body></html>";
    }
}
```

- [x] **Krok 4: Napiš `WebPreviewServer`**

`Src/ARBot.Runtime/Web/WebPreviewServer.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace ARBot.Robot.Web
{
    /// <summary>
    /// <b>Webovy nahled headless runtime.</b> Jedno vlakno prijima spojeni a obsluhuje je po jednom;
    /// odpovida na <c>/</c>, <c>/world.png</c>, <c>/camera.jpg</c>, <c>/status.json</c> a
    /// <c>POST /stop</c>. Viz doc/headless.md.
    ///
    /// <para><b>Nesmi ublizit rizeni:</b> vlakno je <c>IsBackground</c> a ma
    /// <see cref="ThreadPriority.BelowNormal"/>, spojeni se serializuji a obrazky se kresli teprve
    /// na pozadavek (<see cref="WebStatus"/>). Kdyz bind selze, <see cref="Start"/> vrati
    /// <c>false</c> a <b>robot jede dal bez nahledu</b> - stejna zasada, jakou ma zaznam.</para>
    ///
    /// <para><b>Bez autentizace, na vsech rozhranich</b> (rozhodnuti autora 4. 9. 2026): robot je na
    /// uzavrene siti a jediny zasah je zastaveni, tedy ta bezpecnejsi strana. Rozjet robota z webu
    /// nejde a nikdy nesmi jit.</para>
    /// </summary>
    public sealed class WebPreviewServer : IDisposable
    {
        private const int IoTimeoutMs = 5000;

        private readonly WebStatus status;
        private readonly Action onStop;
        private TcpListener listener;
        private Thread thread;
        private volatile bool running;
        private DateTime lastErrorLog = DateTime.MinValue;

        /// <param name="status">Odberatel streamu, ze ktereho se cte stav a kresli obrazky.</param>
        /// <param name="onStop">Co udelat na <c>POST /stop</c> - v headless nastavi udalost ukonceni.
        /// Server sam <c>ARBotRuntime.Stop()</c> nevola: o ukonceni procesu rozhoduje aplikace.</param>
        public WebPreviewServer(WebStatus status, Action onStop)
        {
            this.status = status ?? throw new ArgumentNullException(nameof(status));
            this.onStop = onStop;
        }

        /// <summary>Skutecny port, na kterem server posloucha (u portu 0 ten pridelený OS).</summary>
        public int Port { get; private set; }

        /// <summary>
        /// Nastartuje server. <c>false</c> = bind selhal (obsazeny port, chybejici pravo) a jede se
        /// dal bez nahledu; duvod je v <see cref="Trace"/>.
        /// </summary>
        public bool Start(int port)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Start();
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"web={port}: nahled se nepodarilo nastartovat ({ex.Message}) -> bez nahledu.");
                listener = null;
                return false;
            }

            running = true;
            thread = new Thread(Loop)
            {
                IsBackground = true,
                Name = "ARBot web",
                // Nahled nikdy nesmi soupent s ridici smyckou.
                Priority = ThreadPriority.BelowNormal,
            };
            thread.Start();
            Trace.WriteLine($"web={Port}: nahled bezi na http://<ip>:{Port}/ (bez hesla; /stop zastavi robota).");
            return true;
        }

        private void Loop()
        {
            while (running)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    client.ReceiveTimeout = IoTimeoutMs;
                    client.SendTimeout = IoTimeoutMs;
                    Handle(client);
                }
                catch (Exception ex)
                {
                    if (!running) break;   // Dispose zavrel listener - normalni konec
                    LogRateLimited($"web: obsluha spojeni selhala: {ex.Message}");
                }
                finally
                {
                    try { client?.Close(); } catch { }
                }
            }
        }

        private void Handle(TcpClient client)
        {
            using var s = client.GetStream();

            string header = HttpMini.ReadHeader(s);
            if (header == null) { HttpMini.WriteText(s, 413, "hlavicka je prilis dlouha nebo spojeni skoncilo"); return; }

            int nl = header.IndexOf('\n');
            var req = HttpMini.ParseRequestLine(nl > 0 ? header.Substring(0, nl).TrimEnd('\r') : header);
            if (!req.Ok) { HttpMini.WriteText(s, 400, "nesmyslny pozadavek"); return; }

            switch (req.Path)
            {
                case "/":
                case "/index.html":
                    HttpMini.WriteResponse(s, 200, "text/html; charset=utf-8",
                                           Encoding.UTF8.GetBytes(status.ToHtml()));
                    return;

                case "/world.png":
                {
                    var png = status.RenderPlanView();
                    if (png == null) { HttpMini.WriteText(s, 503, "pudorys se nepodarilo nakreslit"); return; }
                    HttpMini.WriteResponse(s, 200, "image/png", png);
                    return;
                }

                case "/camera.jpg":
                {
                    // Zajem se hlasi VZDY, i kdyz snimek jeste neni - jinak by se prvni snimek
                    // nikdy nezkopiroval a kamera by zustala prazdna nadobro.
                    status.NoteCameraInterest();
                    var jpeg = status.RenderCameraJpeg(QueryValue(req.Query, "cam"),
                                                       QueryValue(req.Query, "layer"));
                    if (jpeg == null) { HttpMini.WriteResponse(s, 204, null, null); return; }
                    HttpMini.WriteResponse(s, 200, "image/jpeg", jpeg);
                    return;
                }

                case "/status.json":
                {
                    bool run = ARBotRuntime.HasCurrent && ARBotRuntime.Current.IsRunning;
                    HttpMini.WriteResponse(s, 200, "application/json; charset=utf-8",
                                           Encoding.UTF8.GetBytes(status.ToJson(run)));
                    return;
                }

                case "/stop":
                    if (!string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase))
                    {
                        // GET by mohl vyvolat prefetch prohlizece nebo nahled odkazu.
                        HttpMini.WriteText(s, 405, "zastaveni jde jen pres POST");
                        return;
                    }
                    Trace.WriteLine("web: prislo POST /stop -> ukoncuji.");
                    HttpMini.WriteText(s, 200, "zastavuji");
                    try { onStop?.Invoke(); } catch (Exception ex) { Trace.WriteLine("web: stop selhal: " + ex.Message); }
                    return;

                default:
                    HttpMini.WriteText(s, 404, "nenalezeno");
                    return;
            }
        }

        /// <summary>Vytahne hodnotu klice z query stringu (<c>cam</c>, <c>layer</c>); null = nebyl.</summary>
        private static string QueryValue(string query, string key)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(key)) return null;
            string prefix = key + "=";
            foreach (var part in query.Split('&'))
            {
                if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(part.Substring(prefix.Length));
            }
            return null;
        }

        /// <summary>Hlaska nejvys jednou za minutu - zaplavena konzole je horsi nez zadna.</summary>
        private void LogRateLimited(string text)
        {
            var now = DateTime.UtcNow;
            if ((now - lastErrorLog).TotalSeconds < 60) return;
            lastErrorLog = now;
            Trace.WriteLine(text);
        }

        public void Dispose()
        {
            running = false;
            try { listener?.Stop(); } catch { }
            listener = null;
            try { thread?.Join(1000); } catch { }
            thread = null;
        }
    }
}
```

- [x] **Krok 5: Spusť testy a ověř, že procházejí**

```bash
dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64
```

Očekávej **24 zelených** (4 bootstrap + 9 HttpMini + 8 serveru + 3 `WebStatus`). Kdyby `Task`
v testech nešel přeložit, přidej `using System.Threading.Tasks;`.

- [x] **Krok 6: Ověř build obou platforem a pravidlo „runtime nezná UI"**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
dotnet build Src/ARBot.slnx -p:Platform=OrangePI
grep -rn "using Avalonia" Src/ARBot.Runtime --include=*.cs
```

Poslední příkaz musí být prázdný.

---

### Task 5: Napojení v headless a parametr `web=`

**Soubory:**
- Modify: `Src/ARBot.Common/Configuration/ParamRegistry.cs` (nový klíč `web`)
- Modify: `Src/ARBot.Common/Configuration/ParamParsers.cs` (validátor portu)
- Modify: `Src/ARBot.Headless/Program.cs` (zapnutí serveru)
- Test: `Src/ARBot.Common.Tests/Configuration/WebPortParamTests.cs`

**Rozhraní:**
- Consumes: `WebPreviewServer`, `WebStatus` (Task 4).
- Produces: `ParamRegistry.Web` (`DoubleParam`), `ParamParsers.WebPort`.

- [x] **Krok 1: Napiš padající test parametru**

`Src/ARBot.Common.Tests/Configuration/WebPortParamTests.cs`:

```csharp
using ARBot.Common.Configuration;

namespace ARBot.Common.Tests.Configuration
{
    /// <summary>
    /// Parametr web= (port webovehu nahledu headless). Neplatny port ma byt chyba pri startu,
    /// ne tichy pad na default. Viz doc/plan-headless-web.md a doc/configuration.md.
    /// </summary>
    [NonParallelizable]
    public class WebPortParamTests
    {
        [TearDown]
        public void Uklid() => ParamStore.Build(new string[0]);

        [Test]
        public void VychoziJeVypnuto()
        {
            ParamStore.Build(new string[0]);
            Assert.That(ParamRegistry.Web.Value, Is.EqualTo(0), "nahled je ve vychozim stavu vypnuty");
        }

        [TestCase("8080")]
        [TestCase("1024")]
        [TestCase("65535")]
        [TestCase("0")]
        public void PlatnyPortProjde(string hodnota)
        {
            Assert.DoesNotThrow(() => ParamStore.Build(new[] { "web=" + hodnota }));
            Assert.That(ParamRegistry.Web.Value, Is.EqualTo(double.Parse(hodnota)));
        }

        [TestCase("80", "privilegovany port")]
        [TestCase("1023", "privilegovany port")]
        [TestCase("65536", "nad rozsahem")]
        [TestCase("-1", "zaporny")]
        [TestCase("8080.5", "necele cislo")]
        public void NeplatnyPortJeChybaPriStartu(string hodnota, string proc)
        {
            var ex = Assert.Throws<ParamFileException>(() => ParamStore.Build(new[] { "web=" + hodnota }),
                                                       $"{hodnota}: {proc}");
            Assert.That(ex.Message, Does.Contain("web"));
        }
    }
}
```

- [x] **Krok 2: Spusť test a ověř, že padá**

```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~WebPortParamTests"
```

Očekávej chybu překladu: `ParamRegistry.Web` neexistuje.

- [x] **Krok 3: Přidej validátor portu**

Do `Src/ARBot.Common/Configuration/ParamParsers.cs`, k ostatním validátorům:

```csharp
        /// <summary>
        /// Port weboveho nahledu: 0 (vypnuto) nebo cele cislo 1024-65535. Privilegovane porty
        /// (pod 1024) odmitame zamerne - proces bezi jako bezny uzivatel a bind by selhal az za behu.
        /// </summary>
        public static ParamParseResult WebPort(string text)
        {
            if (!double.TryParse(text, System.Globalization.NumberStyles.Float,
                                 System.Globalization.CultureInfo.InvariantCulture, out double v))
                return ParamParseResult.Invalid("cekam cislo");
            if (v != System.Math.Floor(v))
                return ParamParseResult.Invalid("cekam cele cislo");
            if (v == 0) return ParamParseResult.Valid();
            if (v < 1024 || v > 65535)
                return ParamParseResult.Invalid("cekam 0 (vypnuto) nebo port 1024-65535");
            return ParamParseResult.Valid();
        }
```

Tvar `ParamParseResult.Valid()` / `Invalid("cekam …")` odpovídá sousedním validátorům
(`Kladne`, `Nezaporne`) v témž souboru.

- [x] **Krok 4: Přidej klíč do registru**

Do `Src/ARBot.Common/Configuration/ParamRegistry.cs`, do kategorie diagnostiky (vedle `perf`):

```csharp
        public static readonly DoubleParam Web = Num("web", "0", K_DIAG,
              "Port webovehu nahledu v ARBot.Headless (0 = vypnuto). Stranka ukaze snimek kamery, "
            + "pudorys s lokalni mapou a stav mise a nabidne zastaveni robota. Posloucha na vsech "
            + "rozhranich BEZ HESLA - kdokoli v siti muze robota zastavit (rozjet ne). "
            + "V UI aplikaci se ignoruje. Viz doc/headless.md.", ParamParsers.WebPort);
```

- [x] **Krok 5: Spusť testy parametru**

```bash
dotnet test Src/ARBot.Common.Tests -p:Platform=x64 --filter "FullyQualifiedName~WebPortParamTests"
```

Očekávej 10 zelených. Strážní test `KazdyParametrSeVAplikaciNekdeCte` teď **padne** — `web` se
ještě nikde nečte. To je správně, spraví to krok 6.

- [x] **Krok 6: Zapni server v headless**

V `Src/ARBot.Headless/Program.cs`, mezi bod 4 (výpis, co se chystá) a bod 5 (čekání na HW).
Server startuje **před** Run schválně: kdyby se HW nerozjelo, stránka je jediné, co o tom poví.

```csharp
            // 4b) Webovy nahled (web=<port>, 0 = vypnuto). Startuje PRED Run: kdyby se HW nerozjelo,
            //     je stranka jedine, co o tom rekne. Selhani bindu nahled vypne, ale beh nezastavi.
            int webPort = (int)ParamRegistry.Web.Value;
            WebStatus webStatus = null;
            WebPreviewServer web = null;
            IDisposable webConnection = null;
            if (webPort > 0)
            {
                webStatus = new WebStatus();
                web = new WebPreviewServer(webStatus, () =>
                {
                    duvod ??= "web /stop";
                    konec.Set();
                });
                if (web.Start(webPort))
                    webConnection = ARBotRuntime.Current.Stream.Connect(webStatus);
                else
                    { web.Dispose(); web = null; webStatus = null; }
            }
```

Deklarace `konec` a `duvod` musí být **nad** tímto blokem — dnes jsou v bodě 7, takže se ta část
posune výš (registrace signálů může zůstat, kde je). Na konci `Main`, po `Stop()`:

```csharp
            // Nahled drzi posledni stav i po Stop(), ale proces uz konci - odpojit a zavrit.
            try { webConnection?.Dispose(); } catch { }
            try { web?.Dispose(); } catch { }
```

A do úvodního výpisu (bod 4) přidej, na čem náhled běží:

```csharp
                + $" nahled: {(webPort > 0 ? "http://<ip>:" + webPort + "/" : "vypnuty (web=0)")}.");
```

Přidej `using ARBot.Robot.Web;` a `using System;` (pokud tam není).

- [x] **Krok 7: Ověř build a celé sady**

```bash
dotnet build Src/ARBot.slnx -p:Platform=x64
dotnet build Src/ARBot.slnx -p:Platform=OrangePI
dotnet test Src/ARBot.Common.Tests -p:Platform=x64
dotnet test Src/ARBot.Runtime.Tests -p:Platform=x64
```

Očekávej 0 chyb, **1 151 zelených** v Common (baseline 1 131 + 5 z Tasku 1 + 5 z Tasku 2
+ 10 parametr) a **24** v Runtime. Strážní test parametrů musí být zelený.

- [x] **Krok 8: Ověř za běhu na Windows**

Spusť headless se simulovaným HW a náhledem:

```bash
dotnet Src/ARBot.Headless/bin/x64/Debug/net10.0/ARBot.Headless.dll virtualhw=true mission=freerun map=OSM/SyntetickyRovny.osm web=8080
```

Pak v prohlížeči otevři `http://localhost:8080/` a zkontroluj **všechno tohle**:

1. Půdorys se kreslí a je na něm vidět síť cesty, robot ve středu a jak jede.
2. Snímek kamery se objeví (první požadavek jen ohlásí zájem, obraz přijde do sekundy).
3. Tlačítko **cesta z RGB** přepne snímek na pravděpodobnost cesty a je na ní poznat vozovka.
4. Tabulka stavu se obnovuje, rychlost je nenulová, jakmile se robot rozjede.
5. `http://localhost:8080/neexistuje` vrátí 404, `GET /stop` vrátí 405.
6. Tlačítko **Zastavit robota** proces ukončí s kódem 0 a v konzoli je `web: prislo POST /stop`.

Zapiš do plánu, co z toho vyšlo, a **kolik CPU** proces bral s otevřenou stránkou proti běhu bez ní
(údaj `cpu` na stránce nebo `ProcessCpuPct` z `PerfMsg` — to je to číslo, na které se v `doc/headless.md`
čeká).

- [x] **Krok 9: Ověř, že vadný port zastaví start**

```bash
dotnet Src/ARBot.Headless/bin/x64/Debug/net10.0/ARBot.Headless.dll web=80
```

Očekávej kód 2 a hlášku o neplatné hodnotě na stderr. Pak ověř, že obsazený port běh **nezastaví**:
spusť dvě instance s `web=8080 virtualhw=true` a druhá musí hlásit „bez nahledu" a jet dál.

---

### Task 6: Dokumentace

**Soubory:**
- Modify: `doc/headless.md` (sekce o náhledu, nahradit „Fáze 3 — rámec")
- Modify: `doc/configuration.md` (parametr `web=`)
- Modify: `doc/architecture.md` (`ARBot.Common/Rendering`, `ARBot.Runtime/Web`)
- Modify: `doc/decisions.md` (dvě rozhodnutí, nahoru)
- Modify: `doc/devlog.md` (záznam dne)
- Modify: `doc/plan-runtime-headless.md` (fáze 3 má vlastní dokument)
- Modify: `CLAUDE.md` (rozšířit řádek o headless)

- [x] **Krok 1: `doc/headless.md`**

Sekci „Fáze 3 — webový náhled (jen rámec)" nahraď popisem hotového stavu: cesty, co je na stránce,
parametr `web=`, že je **bez hesla a kdokoli v síti může zastavit**, líný render a proč (CPU),
a naměřená čísla z Tasku 5 krok 8. Do „Stav ověření" přidej, co se ověřilo na Windows a že **na
OrangePi náhled neběžel**.

- [x] **Krok 2: `doc/configuration.md`**

Do tabulky parametrů přidej `web=` (kategorie Diagnostika, výchozí 0) a krátkou sekci o něm
s odkazem na `headless.md`. Uprav počet klíčů registru (dnes 59 → 60) i v `CLAUDE.md`.

- [x] **Krok 3: `doc/architecture.md`**

K `ARBot.Common` přidej `Rendering/` (kreslení ze zpráv, bez UI — vidí na to i `ARBot.Analyze`),
k `ARBot.Runtime` přidej `Web/`. Do „Kam co patří" řádek: kreslení náhledu patří do Common,
protože je to funkce zpráv, ne UI; HTTP patří do Runtime, protože potřebuje `Stream`.

- [x] **Krok 4: `doc/decisions.md`** (nahoru, nad záznam o runtime)

Dva bloky: **„Náhled headless je vlastní HTTP nad `TcpListener`, ne `HttpListener`"** (s naměřeným
důvodem: Windows bez admin práv nepřijme jiný prefix než localhost, takže by se ladil jiný stav než
na Pi) a **„Webový náhled umí jen čtení a zastavení, bez hesla"** (robot je na uzavřené síti,
zastavení je bezpečnější strana než rozjezd; rozjet robota z webu nesmí jít nikdy).

- [x] **Krok 5: `doc/devlog.md`**

Záznam dne: co se udělalo, čím se to ověřilo, **naměřený CPU rozdíl** a pravdivý stav — Windows ano,
OrangePi ne. Odkaz na tento plán a na `headless.md`.

- [x] **Krok 6: `doc/plan-runtime-headless.md`**

V hlavičce a v sekci o fázi 3 doplň, že fáze 3 má vlastní dokument
[plan-headless-web.md](plan-headless-web.md), a že gate „až po ověření na zařízení" autor
4. 9. 2026 vědomě přeskočil (na HW to nešlo vyzkoušet).

- [ ] **Krok 7: Ohlas hotovo, nekomituj**

Sepiš, co má autor ověřit na OrangePi: že se stránka otevře z mobilu na `http://<ip>:<port>/`,
že se snímek kamery a půdorys kreslí i tam, kolik CPU to bere proti běhu bez náhledu a jestli
tlačítko zastavení uzavře záznam.

---

## Co se tímto plánem NEOVĚŘÍ

- **Nic z toho neproběhne na OrangePi.** Náhled se ověří jen na Windows se simulovaným HW.
  Otevřené otázky pro zařízení: kolik CPU kreslení a kódování skutečně bere na ARM, jestli
  `SkiaSharp` na Armbianu nakreslí text měřítka (fonty!) a jak se náhled chová přes WiFi
  s několika prohlížeči.
- **Rychlost čtení na mobilu** — snímek kamery v plné velikosti může být na slabém spoji pomalý;
  zmenšování se schválně neimplementuje, dokud se neukáže, že je potřeba.
- **Souběh víc prohlížečů** se testuje jen tím, že se spojení serializují; nikdo neměřil, co dělá
  pět otevřených stránek naráz.
