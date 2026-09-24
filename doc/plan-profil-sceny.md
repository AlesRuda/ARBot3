# Profil scény před robotem (nástroj na ladění detekce terénu)

Registr: `nast-profil-sceny`. Stav fáze 1: hotová v kódu (24. 9. 2026), ověřená testy, nad záznamem
a screenshotem z běžící aplikace ve View.

## Proč

Detekci terénu ([traversability-grid.md](traversability-grid.md)) šlo dosud kontrolovat jen
výsledkem: buňky obarvené třídou (robot-centrický pohled, vrstva `Traversability` v obrazech).
Nešlo ale vidět, **z jakých bodů buňka vznikla** a **proč má svou třídu**. Přesně tohle chybělo
u rozporu v `lp-drsnost-povrchu-rychlostni-strop`: náklon robotu ukazuje hrbol ~6 cm, ale grid
se na tom místě od běžné vozovky neliší. Je potřeba rozhodnout, jestli hrbol v datech není,
nebo jestli ho pohltí agregace buňky.

## Co nástroj ukazuje (fáze 1)

**Tools → Profil scény** (`open=profile`) ukazuje graf **z(r)** v jednom azimutu polárního gridu:

- **osa X** = vodorovná vzdálenost `√(x²+y²)` od počátku robotu. Podle téže veličiny řadí body
  do prstenců `BuildGrid`. Samotné `x` by u bočních azimutů lhalo.
- **surové body** hloubky ze sloupců obrazu, které tvoří azimutovou buňku (16 sloupců), obarvené
  třídou buňky, do které padly. Body mimo grid (blíž než `MinRangeM`, dál než poslední hrana) jsou šedé.
- **pozadí prstence** podle třídy buňky, sytost podle `Confidence`.
- **referenční rovina** pod těžištěm buňky (modrá čárkovaná) a pás **± `MaxHeightDev(r)`**.
- agregáty buňky: **`MeanZ`** (žlutá vodorovná), **`±StdZ`** (žlutá svislá v těžišti),
  **`MaxZ`** (oranžový trojúhelník, nad výřezem přitažený k okraji).
- **odečítací okno pod myší:** prstenec, počet bodů, agregáty a tři kritéria klasifikace proti
  prahům (odchylka od roviny, drsnost, stoupání k sousedům). Překročené kritérium je červeně.

Ovládání: výběr kamery, posuvník azimutu (0 = levý okraj obrazu), **Zmrazit** (drží snímek a nad
ním jde listovat azimuty), **1:1** (stejné měřítko os, skutečný tvar; jinak je výška zvětšená).
V grafu funguje kolečko jako lupa vzdálenosti, Ctrl+kolečko jako lupa výšky a tažení pravým
tlačítkem jako posun. Dvojklik vrátí automatický rozsah.

## Návrh

- **Jádro `ARBot.Common/Vision/SceneProfile.cs`**: `SceneProfile.Extract(depth, projection, grid,
  azimut, cfg)`. Body počítá **stejně jako managed cesta `BuildGrid`** (paprsek × hloubka →
  `Transformation`). Klasifikaci vysvětluje **tímtéž kódem** jako procesor: `FitReferencePlane`,
  `Deviation` a `MaxNeighborSlope` jsou kvůli tomu `internal static` v `CameraFrameProcessor`.
  Kopie by se časem rozešla a vysvětlení by lhalo.
- **Kontrola poctivosti:** `SceneProfile.Mismatches` počítá buňky, u kterých přepočet dává jinou
  třídu, než má grid. Může to způsobit jiná konfigurace při záznamu, protože nástroj bere výchozí
  `PolarGridConfig` stejně jako runtime. Hlavička to hlásí ⚠.
- **Data:** jen `CameraFrame` ze `Stream`, tedy Run i View beze změny formátu. Hloubka, grid
  i popis projekce (`Projection`, od CameraFrame v4) už jsou v záznamu.
- **Dokument** `SceneProfileDocument` **kopíruje hloubku**, protože buffery jsou z poolu. Kopie se
  dělá nejvýš 10× za sekundu času záznamu na kameru. Projekce se staví jednou na kameru.
  Control je `SceneProfileControl` a kreslí vlastním `Render` jako telemetrický graf.
- `profileshot=true` (+ `ts_rec=`) pořídí bezobslužný snímek do `doc/media/scene-profile*.png`.

## Ověření

- Unit testy `SceneProfileTest` na syntetické hloubce:
  - rovina dá `z = 0`,
  - počet bodů v každém prstenci = `Count` buňky (4 azimuty, zvlněný terén),
  - překážka je vysvětlená výškou/stoupáním, `Mismatches = 0`,
  - neplatné pixely se přeskočí,
  - chod bez gridu.
- **Nad reálným záznamem** (`records/20260915-123520.rec`, 200 snímků, 332 970 buněk; je to běh
  **simulace**, terénní záznamy na vývojovém stroji nejsou):
  - třída nesedí **v 0 buňkách**,
  - počet bodů nesedí v 16 buňkách, **vždy ±1 bod mezi sousedními prstenci** při shodném součtu
    za azimut. Je to zaokrouhlení nativní SIMD cesty (`UseNativeTransform = true` v runtime),
    kterou profil nepoužívá: bod přesně na hraně prstence padne jednou vlevo, jednou vpravo.
    Odečítací okno to u takové buňky ukáže („v profilu N!").
- Screenshot z běžící aplikace (View, `profileshot`):
  [scene-profile-20260924.png](media/scene-profile-20260924.png),
  [scene-profile-obstacle-20260924.png](media/scene-profile-obstacle-20260924.png).
  Odečítací okno pod myší na snímku není, protože se kreslí jen při pohybu myši. Ověřené je jen
  kódem.
- ⚠️ **Nad terénním záznamem z robota to zatím neběželo.**

## Fáze 2 (neudělané, samostatný návrh)

- **3D pohled na mračno** (rotace, posun, zoom). Jádro výpočtu bodů je sdílené. Rozhodnout mezi
  softwarovou projekcí v `Render` (bez závislosti, desítky tisíc bodů) a OpenGL
  (`OpenGlControlBase`, nová závislost, riziko na Armbianu).
- Profil ve webovém náhledu (`/profile.png?cam=&az=`) přes renderer v `ARBot.Common/Rendering`.
