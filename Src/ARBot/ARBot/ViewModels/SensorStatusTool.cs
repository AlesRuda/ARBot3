using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Robot;
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
    public partial class SensorStatusTool : Tool
    {
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
                    Sensors.Add(new SensorRow(s));
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

        public SensorRow(ISensor sensor)
        {
            Sensor = sensor;
            Name = sensor.Name;
            IsError = sensor.IsError;
        }

        /// <summary>Textovy stav pro UI.</summary>
        public string StatusText => IsError ? "CHYBA" : "OK";

        /// <summary>Barva indikatoru dle stavu.</summary>
        public IBrush StatusBrush => IsError ? Brushes.OrangeRed : Brushes.LimeGreen;

        /// <summary>Nacte aktualni IsError z podkladoveho senzoru.</summary>
        public void Update() => IsError = Sensor.IsError;
    }
}
