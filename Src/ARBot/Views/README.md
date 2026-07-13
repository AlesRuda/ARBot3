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

Pozn.: dokumenty senzorů se obnovují **událostí `MeasurementArived`**, ne časovačem —
data se tak zobrazují rovnoměrně, jak chodí z driveru. Rozhraní `IIMU`/`IGPS`/`IMotorControl`/`ICamera`
proto událost vystavují (implementace ji dědí ze `SensorBase`).
