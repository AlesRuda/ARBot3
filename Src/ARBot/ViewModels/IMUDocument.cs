using System;
using System.Globalization;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.HAL;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovací dokument zobrazující údaje z inerciální jednotky (<see cref="IIMU"/>).
    /// Orientace z kvaternionu se vizualizuje kompasem (yaw) a umělým horizontem
    /// (pitch/roll), ostatní veličiny jsou číselné. IMU je předána jako parametr a
    /// dokument ji NEvlastní (jen se odhlásí z události, nezavírá ji).
    /// </summary>
    public partial class IMUDocument : DocumentBase, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.IMUDocumentView);

        private readonly IIMU? imu;

        // Backpressure: nejnovější nezpracované měření (starší se zahazují), aby se při ~100 Hz
        // nehromadila dispatcher fronta a UI zůstalo responzivní (viz OnMeasurement/Flush).
        private readonly object pendingGate = new object();
        private IMUState? pendingState;
        private volatile bool updateQueued;

        /// <summary>Kurz pro kompas [°], 0 = sever, roste po směru hod. ručiček.</summary>
        [ObservableProperty] private double headingDeg;
        /// <summary>Sklon pro umělý horizont [°].</summary>
        [ObservableProperty] private double pitchDeg;
        /// <summary>Náklon pro umělý horizont [°].</summary>
        [ObservableProperty] private double rollDeg;

        [ObservableProperty] private string orientationText = "-";
        [ObservableProperty] private string angularVelocityText = "-";
        [ObservableProperty] private string accelerationText = "-";
        [ObservableProperty] private string magnetometerText = "-";
        [ObservableProperty] private string quaternionText = "-";
        [ObservableProperty] private string confidenceText = "-";
        [ObservableProperty] private string uncertaintyText = "-";
        [ObservableProperty] private string frameText = "-";

        /// <summary>Podkladový senzor pro indikátor stavu (SensorStatusControl).</summary>
        public ISensor? Sensor { get; }

        /// <summary>Konstruktor pro design-time / návrhář.</summary>
        public IMUDocument()
        {
            Id = "IMU";
            Title = "IMU";
        }

        public IMUDocument(IIMU imu)
        {
            this.imu = imu;
            Sensor = imu;
            string name = (imu as ISensor)?.Name ?? "IMU";
            Id = "IMU:" + name;
            Title = "IMU — " + name;

            imu.MeasurementArived += OnMeasurement;

            // úvodní vykreslení z posledního známého měření (pokud je)
            var last = imu.GetLastMeasurement();
            if (last != null)
                Apply(last);
        }

        // Běží na vlákně senzoru. Uloží jen nejnovější měření a koalescovaně naplánuje jednu
        // UI aktualizaci na Background prioritě (starší měření se zahodí).
        private void OnMeasurement(object? sender, IMUState state)
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

            IMUState? s;
            lock (pendingGate)
            {
                s = pendingState;
                pendingState = null;
            }

            if (s != null)
                Apply(s);
        }

        /// <summary>Promítne měření do vlastností (musí běžet na UI vlákně).</summary>
        private void Apply(IMUState s)
        {
            var ypr = s.YPR();   // z kvaternionu; null když chybí Rotation
            if (ypr != null)
            {
                // ypr.Yaw je v matematické orientaci (0 = východ, +CCW); kompas chce azimut
                // (0 = sever, +CW) → převod Orientation2Azimut.
                double azimuthDeg = Conversions.Rad2Deg(Conversions.Orientation2Azimut(ypr.Yaw));
                HeadingDeg = ((azimuthDeg % 360) + 360) % 360;
                PitchDeg = Conversions.Rad2Deg(ypr.Pitch);
                RollDeg = Conversions.Rad2Deg(ypr.Roll);
                OrientationText = string.Format(CultureInfo.InvariantCulture,
                    "yaw {0:F1}°   pitch {1:F1}°   roll {2:F1}°", HeadingDeg, PitchDeg, RollDeg);
            }

            if (s.Rotation is Quaternion q)
                QuaternionText = string.Format(CultureInfo.InvariantCulture,
                    "x {0:F4}  y {1:F4}  z {2:F4}  w {3:F4}", q.X, q.Y, q.Z, q.W);

            AngularVelocityText = Vec(s.AngularVelocity, "rad/s");
            AccelerationText = Vec(s.Acceleration, "m/s²");
            MagnetometerText = Vec(s.Magnetometer, "");
            ConfidenceText = s.Confidence.ToString("F2", CultureInfo.InvariantCulture);

            if (s.OrientationUncertainty is Vector3 u)
                UncertaintyText = string.Format(CultureInfo.InvariantCulture,
                    "yaw {0:F2}°   pitch {1:F2}°   roll {2:F2}°   (1σ)",
                    Conversions.Rad2Deg(u.X), Conversions.Rad2Deg(u.Y), Conversions.Rad2Deg(u.Z));

            // Hz: 4 pevná místa před tečkou, doplněná mezerami (ne nulami); neplatná perioda = prázdné.
            string hzText = s.FrameReceivePeriod.TotalSeconds > 0
                ? (1.0 / s.FrameReceivePeriod.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)
                : "";
            FrameText = string.Format(CultureInfo.InvariantCulture,
                "#{0}   {1,6} Hz   conf {2:F2}   {3:HH:mm:ss.fff}", s.FrameNum, hzText, s.Confidence, s.TimeStamp);
        }

        private static string Vec(Vector3? v, string unit)
        {
            if (v == null)
                return "-";
            var val = v.Value;
            string u = string.IsNullOrEmpty(unit) ? "" : " " + unit;
            return string.Format(CultureInfo.InvariantCulture,
                "X {0,8:F3}   Y {1,8:F3}   Z {2,8:F3}{3}", val.X, val.Y, val.Z, u);
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            if (imu != null)
                imu.MeasurementArived -= OnMeasurement;
        }
    }
}
