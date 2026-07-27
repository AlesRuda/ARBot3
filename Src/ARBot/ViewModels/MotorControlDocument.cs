using System;
using System.Globalization;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.HAL;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovací dokument zobrazující stav řídicí jednotky motorů (<see cref="IMotorControl"/>).
    /// Obnova je řízená událostí <see cref="IMotorControl.MeasurementArived"/> (každé měření),
    /// takže data se zobrazují rovnoměrně, jak chodí z driveru. Jednotka je předána jako
    /// parametr a dokument ji NEvlastní (jen se odhlásí z události, nezavírá ji).
    /// </summary>
    public partial class MotorControlDocument : DocumentBase, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.MotorControlDocumentView);

        private readonly IMotorControl? motors;

        // Backpressure: nejnovější nezpracované měření (starší se zahazují), jednotně s ostatními
        // dokumenty - viz OnMeasurement/Flush.
        private readonly object pendingGate = new object();
        private IMotorState? pendingState;
        private volatile bool updateQueued;

        [ObservableProperty] private bool isEmergencyStop;
        /// <summary>Text nouzového stavu pro záhlaví.</summary>
        [ObservableProperty] private string emergencyText = "-";
        /// <summary>Barva indikátoru nouzového stavu.</summary>
        [ObservableProperty] private IBrush emergencyBrush = Brushes.Gray;

        [ObservableProperty] private string leftWheelSpeedText = "-";
        [ObservableProperty] private string rightWheelSpeedText = "-";
        [ObservableProperty] private string leftEncoderText = "-";
        [ObservableProperty] private string rightEncoderText = "-";
        [ObservableProperty] private string voltageText = "-";
        [ObservableProperty] private string leftCurrentText = "-";
        [ObservableProperty] private string rightCurrentText = "-";

        /// <summary>Podkladový senzor pro indikátor stavu (SensorStatusControl).</summary>
        public ISensor? Sensor { get; }

        /// <summary>Konstruktor pro design-time / návrhář.</summary>
        public MotorControlDocument()
        {
            Id = "Motors";
            Title = "Motory";
        }

        public MotorControlDocument(IMotorControl motors)
        {
            this.motors = motors;
            Sensor = motors;
            string name = (motors as ISensor)?.Name ?? "Motors";
            Id = "Motors:" + name;
            Title = "Motory — " + name;

            motors.MeasurementArived += OnMeasurement;

            // úvodní vykreslení z posledního známého měření (pokud je)
            Apply(motors.GetLastMeasurement());
        }

        // Běží na vlákně senzoru. Uloží jen nejnovější měření a koalescovaně naplánuje jednu
        // UI aktualizaci na Background prioritě (starší měření se zahodí).
        private void OnMeasurement(object? sender, IMotorState state)
        {
            if (state == null)
                return;

            lock (pendingGate)
                pendingState = state;

            if (updateQueued)
                return;
            updateQueued = true;
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        /// <summary>Promítne poslední nasbírané měření na UI vlákně (starší mezitím zahozená).</summary>
        private void Flush()
        {
            updateQueued = false;

            IMotorState? s;
            lock (pendingGate)
            {
                s = pendingState;
                pendingState = null;
            }

            if (s != null)
                Apply(s);
        }

        /// <summary>Promítne měření do vlastností (musí běžet na UI vlákně).</summary>
        private void Apply(IMotorState? s)
        {
            if (s == null)
                return;

            IsEmergencyStop = s.IsEmergencyStop;
            EmergencyText = s.IsEmergencyStop ? "NOUZOVÉ ZASTAVENÍ" : "v provozu";
            EmergencyBrush = s.IsEmergencyStop ? Brushes.OrangeRed : Brushes.LimeGreen;

            LeftWheelSpeedText = string.Format(CultureInfo.InvariantCulture, "{0,7:F3} m/s", s.LeftWheelSpeed);
            RightWheelSpeedText = string.Format(CultureInfo.InvariantCulture, "{0,7:F3} m/s", s.RightWheelSpeed);
            LeftEncoderText = string.Format(CultureInfo.InvariantCulture, "{0,10:F3} m", s.LeftEncoder);
            RightEncoderText = string.Format(CultureInfo.InvariantCulture, "{0,10:F3} m", s.RightEncoder);
            VoltageText = string.Format(CultureInfo.InvariantCulture, "{0:F2} V", s.Voltage);
            LeftCurrentText = string.Format(CultureInfo.InvariantCulture, "{0:F2} A", s.LeftMotorCurrent);
            RightCurrentText = string.Format(CultureInfo.InvariantCulture, "{0:F2} A", s.RightMotorCurrent);
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            if (motors != null)
                motors.MeasurementArived -= OnMeasurement;
        }
    }
}
