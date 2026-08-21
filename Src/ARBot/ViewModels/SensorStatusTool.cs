using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Robot;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovaci nastroj zobrazujici seznam senzoru z <see cref="ARBotHW.Current"/>
    /// (jmeno + chybovy stav). Stav se periodicky obnovuje - <see cref="ISensor.IsError"/>
    /// se muze menit za behu a sam zmenu nehlasi.
    /// </summary>
    public partial class SensorStatusTool : ToolBase
    {
        public override Type ViewType => typeof(ARBot.Views.SensorStatusToolView);

        private readonly DispatcherTimer timer;

        /// <summary>Radky se senzory (jmeno + IsError).</summary>
        public ObservableCollection<SensorRow> Sensors { get; } = new();

        /// <summary>Stavova/chybova hlaska (napr. kdyz ARBotHW neni dostupny nebo je prazdno).</summary>
        [ObservableProperty]
        private string status = string.Empty;

        public SensorStatusTool()
        {
            Id = "SensorStatus";
            Title = "Sensors";

            // V design-time nahledu nepristupovat k ARBotHW ani nespoustet casovac.
            if (Design.IsDesignMode)
                return;

            // ISensor.IsError se meni za behu bez notifikace -> periodicky refresh na UI vlakne.
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (_, _) => Refresh();
            timer.Start();
            Refresh();
        }

        /// <summary>Znovu nacte seznam senzoru z ARBotHW.Current a aktualizuje chybove stavy.</summary>
        [RelayCommand]
        private void Refresh()
        {
            List<ISensor> live;
            try
            {
                live = ARBotHW.Current?.Sensors?.ToList() ?? new List<ISensor>();
                Status = string.Empty;
            }
            catch (Exception ex)
            {
                Status = "ARBotHW nedostupny: " + ex.Message;
                return;
            }

            // Odebrat radky senzoru, ktere uz v kolekci nejsou.
            for (int i = Sensors.Count - 1; i >= 0; i--)
                if (!live.Any(s => ReferenceEquals(s, Sensors[i].Sensor)))
                    Sensors.RemoveAt(i);

            // Pridat nove senzory a aktualizovat chybovy stav stavajicich.
            foreach (var s in live)
            {
                var row = Sensors.FirstOrDefault(r => ReferenceEquals(r.Sensor, s));
                if (row == null)
                    Sensors.Add(new SensorRow(s, msg => Status = msg));
                else
                    row.Update();
            }

            if (Sensors.Count == 0 && Status.Length == 0)
                Status = "Zadne senzory";
        }

        /// <summary>
        /// Vyvoláno při aktivaci senzoru (dvojklik). Naslouchá např. MainWindowViewModel,
        /// který otevře odpovídající detailní dokument.
        /// </summary>
        public event Action<ISensor>? SensorActivated;

        /// <summary>Aktivuje senzor daného řádku (otevře jeho dokument).</summary>
        [RelayCommand]
        private void Activate(SensorRow? row)
        {
            if (row?.Sensor != null)
                SensorActivated?.Invoke(row.Sensor);
        }

    }

    /// <summary>Jeden radek panelu senzoru - obal nad <see cref="ISensor"/> s obnovitelnym IsError.</summary>
    public partial class SensorRow : ObservableObject
    {
        /// <summary>Podkladovy senzor.</summary>
        public ISensor Sensor { get; }

        /// <summary>Jmeno senzoru.</summary>
        public string Name { get; }

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(StatusBrush))]
        private bool isError;

        /// <summary>Bezi smycka mereni senzoru? U neovladatelnych senzoru zustava <c>true</c>.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(StatusText))]
        [NotifyPropertyChangedFor(nameof(StatusBrush))]
        [NotifyPropertyChangedFor(nameof(ToggleText))]
        private bool isRunning = true;

        // Kam ohlasit chybu ovladani (stavova radka panelu). null = nikam.
        private readonly Action<string>? report;

        public SensorRow(ISensor sensor, Action<string>? report = null)
        {
            Sensor = sensor;
            Name = sensor.Name;
            this.report = report;
            IsError = sensor.IsError;
            Update();
        }

        /// <summary>
        /// Zastaví běžící senzor, nebo znovu spustí zastavený.
        ///
        /// <para><b>Příkaz je na řádku, ne na panelu</b> — šablona pak binduje
        /// <c>{Binding ToggleCommand}</c> bez hledání předka. Cesta přes
        /// <c>$parent[ItemsControl].DataContext</c> by při přejmenování selhala <b>tiše</b>
        /// (view má <c>CompileBindings=False</c> a chyby oblasti Binding jsou v logu odfiltrované).</para>
        ///
        /// <para><b>Vypnutí nepřežije Run.</b> Řídicí pipeline si senzory spouští sama
        /// (<c>SensorMessageSource(controlSensor: true)</c>), takže start runtime zastavený senzor
        /// zapne zpátky — vypínat se má až za běhu. Vědomé rozhodnutí (21. 8. 2026): zámek, který
        /// by Run přebil, by byl další skrytý stav.</para>
        ///
        /// <para><b>U motorů to nezastaví kola.</b> Zastaví se jen jejich smyčka měření, tedy
        /// odometrie; poslední příkaz jízdy platí v řídicí jednotce dál. Proto se před zastavením
        /// posílá <c>Drive(0,0)</c> — ale když běží řídicí smyčka, ta si za svůj tik pošle vlastní
        /// příkaz a nulu přebije. Zastavení motorů proto <b>není</b> bezpečnostní funkce.</para>
        /// </summary>
        [RelayCommand]
        private void Toggle()
        {
            if (Sensor is not IControllableSensor ctl)
                return;

            try
            {
                if (ctl.IsRunning)
                {
                    // Motory: nejdriv nulova rychlost, at robot nezustane jezdit na posledni prikaz.
                    if (Sensor is IMotorControl motors)
                        motors.Drive(0, 0);
                    ctl.Stop();
                }
                else
                {
                    ctl.Start();
                }
                report?.Invoke(string.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SensorStatus: {Name} Stop/Start selhalo. {ex}");
                report?.Invoke($"{Name}: {ex.Message}");
            }

            Update();       // stav prekreslit hned, necekat na sekundovy refresh
        }

        /// <summary>
        /// Da se tenhle senzor spustit/zastavit? <c>false</c> u senzoru bez vlastni smycky
        /// (MD23 po I2C, fiktivni motory) - tam se tlacitko neukazuje, viz
        /// <see cref="IControllableSensor"/>.
        /// </summary>
        public bool CanControl => Sensor is IControllableSensor;

        /// <summary>Textovy stav pro UI.</summary>
        public string StatusText => IsError ? "CHYBA" : (IsRunning ? "OK" : "STOP");

        /// <summary>Barva indikatoru dle stavu (zastaveny senzor je seda, ne zelena).</summary>
        public IBrush StatusBrush => IsError ? Brushes.OrangeRed
                                             : (IsRunning ? Brushes.LimeGreen : Brushes.Gray);

        /// <summary>Popis tlacitka - co se stane po kliknuti.</summary>
        public string ToggleText => IsRunning ? "Stop" : "Start";

        /// <summary>Nacte aktualni stav z podkladoveho senzoru (IsError + bezi/nebezi).</summary>
        public void Update()
        {
            IsError = Sensor.IsError;
            IsRunning = Sensor is IControllableSensor ctl ? ctl.IsRunning : true;
        }
    }
}
