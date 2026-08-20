# Dokovatelné dokumenty a nástroje (Avalonia + Dock + MVVM)

Tento adresář obsahuje **prezentační `UserControl`y** (Views) pro dokovatelné dokumenty
a nástroje aplikace ARBot. Aplikace používá **Avalonia 12 + Dock.Avalonia +
CommunityToolkit.Mvvm** (žádné WPF).

## Princip

Každý dokovatelný prvek má oddělený **ViewModel** (ve `ViewModels/`) a **View**
(`UserControl` ve `Views/`). ViewModel zná typ svého View přes vlastnost `ViewType`:

- Dokumenty dědí z **`DocumentBase`**, nástroje z **`ToolBase`**
  (viz [`ViewModels/DockableBase.cs`](../ViewModels/DockableBase.cs)).
  Obě implementují `IViewProvider` s `Type ViewType`.
- [`ViewLocator`](../ViewLocator.cs) podle `ViewType` vytvoří instanci View. (Když prvek
  není `IViewProvider`, použije se fallback konvence názvu `...ViewModel` → `...View`.)
- **`App.axaml` NEobsahuje žádné inline `DataTemplate`y** pro dokumenty/nástroje —
  vše řeší `ViewLocator` + samostatné `UserControl`y. Díky tomu jde každý View
  **navrhovat s design-time náhledem**.

## Jak přidat nový dokument (nebo nástroj)

1. **ViewModel** ve `ViewModels/Xxx(Document|Tool).cs`:
   ```csharp
   public partial class XxxDocument : DocumentBase   // nebo : ToolBase
   {
       public override Type ViewType => typeof(ARBot.Views.XxxDocumentView);
       // ... [ObservableProperty] vlastnosti, konstruktor, ...
   }
   ```
2. **View** ve `Views/XxxDocumentView.axaml` (+ `.axaml.cs` s `InitializeComponent()`):
   ```xml
   <UserControl xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:vm="using:ARBot.ViewModels"
                x:Class="ARBot.Views.XxxDocumentView"
                x:DataType="vm:XxxDocument">
       <Design.DataContext><vm:XxxDocument/></Design.DataContext>
       <!-- obsah, bindingy proti vlastnostem XxxDocument -->
   </UserControl>
   ```
   `x:DataType` zapíná compiled bindings (kontrola názvů při buildu) a IntelliSense;
   `Design.DataContext` dává náhled v návrháři.

Žádná úprava `App.axaml` není potřeba.

## Design-time bezpečnost

`Design.DataContext` vytvoří ViewModel i v návrháři, proto konstruktory ViewModelů
**hlídají `Avalonia.Controls.Design.IsDesignMode`** a v návrhovém režimu nespouští
hardware/časovače/přístup k `ARBotHW` (např. `D435TestDocument` nezakládá kameru,
`SensorStatusTool` nespouští časovač, `DebugOutputTool` neregistruje Trace listener).
Nový ViewModel, který v konstruktoru dělá vedlejší efekty, musí totéž.

## Otevírání dokumentů z panelu Sensors

Panel **Sensors** ([`SensorStatusToolView`](SensorStatusToolView.axaml)) vypisuje
`ARBotHW.Current.Sensors`. Dvojklik na řádek vyvolá `SensorStatusTool.ActivateCommand`
→ event `SensorActivated(ISensor)` → `MainWindowViewModel.OpenSensorDocument`, který
podle typu senzoru vytvoří dokument ve **`CreateSensorDocument`** (switch) a přidá ho do
doku (deduplikace podle `Id`). Nový typ senzoru = přidat větev do `CreateSensorDocument`.

> **Pozor na pořadí větví.** `CreateSensorDocument` je `switch` *expression* — vyhrává **první**
> odpovídající vzor. Speciálnější typ proto musí být **výš** než obecné rozhraní: `VirtualCamera`
> stojí nad `ICamera`, jinak by se `VirtualCameraDocument` nikdy nevytvořil.

## Znovupoužitelné controly (`Views/Controls/`)

Vlastní vykreslované controly (Avalonia `Control` + `Render` + `StyledProperty` +
`AffectsRender`):

- `SensorStatusControl` — indikátor stavu `ISensor` (tečka + jméno + OK/CHYBA), poll
  `DispatcherTimer` 1 s (protože `ISensor.IsError` nenotifikuje). Dej do záhlaví každého
  dokumentu senzoru: `<ctl:SensorStatusControl Sensor="{Binding Sensor}"/>`.
- `CompassControl`, `ArtificialHorizonControl` — kompas a umělý horizont pro IMU
  (kompas využívá i `GpsDocument` pro kurz/azimut).

## Hotové dokumenty senzorů

- `IMUDocument` (`IIMU`) — kompas (yaw) + umělý horizont (pitch/roll), orientace,
  úhlová rychlost, akcelerace, magnetometr, kvaternion, nejistota. Obnova událostí
  `MeasurementArived`.
- `GpsDocument` (`IGPS`) — kompas (kurz/azimut), poloha, výška, kvalita fixu, satelity,
  HDOP, rychlost. Obnova událostí `MeasurementArived` (jako IMU).
- `MotorControlDocument` (`IMotorControl`) — indikátor nouzového zastavení, rychlosti kol,
  enkodéry, proudy motorů, napětí. Obnova událostí `MeasurementArived`.
- `CameraDocument` (`ICamera`) — RGB / hloubkový stream (WriteableBitmap, Bgra8888) s přepínačem
  `ToggleSwitch` (RGB/Hloubka) + overlay s rozlišením a snímkem/Hz/časem. Hloubka (Gray16, ~mm)
  se normalizuje do grayscale (blízko světlé, daleko tmavé). Obnova událostí `MeasurementArived`.
  Kameru **NEvlastní** (je sdílená z `ARBotHW.Sensors`) — v `Dispose` se jen odhlásí. (Odlišné od
  `D435TestDocument`, který si vlastní kameru vytváří a zavírá — ten slouží jako samostatný test D435.)
- `VirtualCameraDocument` (`VirtualCamera`) — **dědí z `CameraDocument`** (celý stream i backpressure
  se znovupoužívá; view vkládá `CameraDocumentView` a přidá panel vedle něj) a doplňuje **umělou
  chybu pózy**: vpřed / vlevo / kurz v rámci robotu, vynulování, a **očekávané hodnoty vedle
  naměřených** z `MapCorrelationMsg` (odběr ze `Stream`). Slouží k ověření korelace occupancy gridu
  s mapou — viz [doc/virtual-hw.md](../../../doc/virtual-hw.md#umělá-chyba-pózy-poseerror).
  Chyba je sdílená oběma kamerami (`ARBotHW.VirtualPoseError`), takže panel je pro Left i Right tentýž.
  Vlastnosti navázané na `NumericUpDown` jsou **`decimal`** (`NumericUpDown.Value` je `decimal?` —
  `double` by selhal až za běhu); stejný vzor jako `WorldViewDocument.DefaultRoadWidthMeters`.

## Dokumenty nad `Stream` (ne senzory)

- `ImageDocument` — obrazové vrstvy (ImageMsg/CameraFrame), odběr `ARBotRuntime.Stream`. Umí i overlay
  gridu sjízdnosti jako vrstvu `"<kamera>/Traversability"` (rasterizace z `PolarTraversabilityGridMsg`
  do velikosti depth snímku, per-pixel alfa) — vybere se do overlay slotu nad `"<kamera>/Depth"`.
- `RobotCentricDocument` (`RobotCentricControl`) — robot-centrický (ptačí) pohled na robot-centrická
  měření. Robot dole, vpřed nahoru; sdílené scaffolding (dosahové kružnice, osa, tvar robotu v měřítku)
  + vrstvy. Zatím vrstva: polární grid sjízdnosti (`PolarTraversabilityGridMsg`) — buňky obarvené dle
  třídy, průhlednost dle důvěry. Výhledově další vrstvy (RGB sjízdnost, okraje vozovky…). Odběr `Stream`
  (Run i View), backpressure „latest-wins". Menu **Tools → Robot-centric**.
  Viz [doc/traversability-grid.md](../../../doc/traversability-grid.md).
- `WorldViewDocument` (Mapsui `MapControl`) — world (geo) pohled: mapa s přepínatelným podkladem
  (OSM online / MBTiles offline / žádný) a vypínatelnými vrstvami dat ze `Stream` (poloha+kurz z `GPSState`/
  `RobotStateMsg`, trajektorie z GPS, trasa/graf a značky z `GraphNavigationMsg`). Podklad lze úplně vypnout ⇒
  **na OrangePI žádné pokusy o internet** (na ARM je i výchozí podklad `None`). ViewModel vlastní Mapsui `Map`,
  View mu ho přiřadí do `MapControl.Map` v code-behind (mimo design-time). Odběr `Stream` (Run i View),
  backpressure „latest-wins". Menu **Tools → World**. Viz [doc/world-view.md](../../../doc/world-view.md).

Pozn.: dokumenty senzorů se obnovují **událostí `MeasurementArived`**, ne časovačem —
data se tak zobrazují rovnoměrně, jak chodí z driveru. Rozhraní `IIMU`/`IGPS`/`IMotorControl`/`ICamera`
proto událost vystavují (implementace ji dědí ze `SensorBase`).

## Backpressure: aktualizace z `MeasurementArived` / `Post` (POVINNÝ vzor)

`MeasurementArived` (i `IMessageSink.Post` u `ImageDocument`) běží **na vlákně producenta**
(`SensorBase.Process`, resp. `RelaySource` fan-out — ten nemá vlastní frontu). Při vysoké
frekvenci (kamera ~30 Hz, IMU/motor ~100 Hz, backproject) by naivní
`Dispatcher.UIThread.Post(() => Apply(x))` na **každou** zprávu zahltil dispatcher frontu →
UI ztratí responzivitu a zpracovává staré framy (typicky „stall → dávka stovek Hz → zpět").

Všechny dokumenty přijímající data proto MUSÍ použít jednotný vzor **„latest-wins + Background
flush"**:

1. Handler (na vlákně producenta) je **neblokující**: pod zámkem uloží jen **nejnovější**
   payload (starší zahodí) a když `!updateQueued`, naplánuje jednu aktualizaci:
   `Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background)`.
2. `Flush` (na UI vlákně) pod zámkem vyzvedne + vynuluje pending a zavolá `Apply`/render.

```csharp
private readonly object pendingGate = new();
private TState? pendingState;              // nebo Dictionary<klíč,zpráva> pro víc zdrojů
private volatile bool updateQueued;

private void OnMeasurement(object? sender, TState state)
{
    if (state == null) return;
    lock (pendingGate) pendingState = state;       // nejnovější vyhrává (drop stale)
    if (updateQueued) return;
    updateQueued = true;
    Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);  // vstup/render mají přednost
}

private void Flush()
{
    updateQueued = false;
    TState? s; lock (pendingGate) { s = pendingState; pendingState = null; }
    if (s != null) Apply(s);
}
```

Aplikováno v: `CameraDocument`, `D435TestDocument`, `IMUDocument`, `GpsDocument`,
`MotorControlDocument`, `ImageDocument` (ten drží `Dictionary` pending per zdroj —
`C:<kamera>` / `B:<blob>` — aby se nestarval žádný slot). `DebugOutputTool` má obdobnou
koalescenci nad řádky. **Každý nový dokument aktualizovaný z eventu/streamu musí tenhle vzor
dodržet.**

## Gate renderu na viditelnost tabu (DocumentBase.IsActive)

`DocumentBase.IsActive` (nastavuje `DockFactory` z `ActiveDockableChanged` = aktivní tab
`DocumentDock`) umožňuje dokumentu **gatovat drahý render, když není vidět**. Nutné pro
vizualizace, jejichž render **neběží přes Avalonia `Control.Render`** (ten framework gatuje
viditelností sám) — typicky tvorba `WriteableBitmap` ve ViewModelu. `ImageDocument`: nejnovější
zprávu si **vždy pamatuje** (`pending`, poolovaná kopie), ale renderuje **jen když je aktivní**;
při zviditelnění (`OnActiveChanged`) hned vyrenderuje zapamatovaný snímek. Bez toho skrytý tab
chrlí `WriteableBitmap` (GC gen2) na pozadí — viz [devlog 2026-08-01](../../../doc/devlog.md).
`RobotCentricControl` gate nepotřebuje (renderuje přes `Control.Render`).

Možné další optimalizace obrazových dokumentů (zatím neuděláno): recyklace `WriteableBitmap`
místo alokace na každý frame **když je dokument viditelný** (skrytý už díky IsActive nerenderuje);
přesun `MessageImageLayers.Extract` (JPEG dekód / barevný převod) na worker vlákno mimo UI — viz
TODO v [build-and-platforms.md](../../../doc/build-and-platforms.md).
