using System;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.HAL;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokovací dokument zobrazující údaje z GPS přijímače (<see cref="IGPS"/>).
    /// Kurz (dynamický / z dvouantény) se vizualizuje kompasem, ostatní veličiny jsou
    /// číselné. Obnova je řízená událostí <see cref="IGPS.MeasurementArived"/> (každé měření),
    /// takže data se zobrazují rovnoměrně, jak chodí z driveru. GPS je předána jako parametr
    /// a dokument ji NEvlastní (jen se odhlásí z události, nezavírá ji).
    /// </summary>
    public partial class GpsDocument : DocumentBase, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.GpsDocumentView);

        private readonly IGPS? gps;

        /// <summary>Kurz pro kompas [°], 0 = sever, roste po směru hod. ručiček.</summary>
        [ObservableProperty] private double headingDeg;
        /// <summary>Zda je kurz k dispozici (jinak se kompas zbytečně netočí na 0).</summary>
        [ObservableProperty] private bool hasHeading;

        [ObservableProperty] private string positionText = "-";
        [ObservableProperty] private string altitudeText = "-";
        [ObservableProperty] private string qualityText = "-";
        [ObservableProperty] private string satellitesText = "-";
        [ObservableProperty] private string hdopText = "-";
        [ObservableProperty] private string orientationText = "-";
        [ObservableProperty] private string speedText = "-";
        [ObservableProperty] private string fixTimeText = "-";
        [ObservableProperty] private string frameText = "-";

        /// <summary>Podkladový senzor pro indikátor stavu (SensorStatusControl).</summary>
        public ISensor? Sensor { get; }

        /// <summary>Konstruktor pro design-time / návrhář.</summary>
        public GpsDocument()
        {
            Id = "GPS";
            Title = "GPS";
        }

        public GpsDocument(IGPS gps)
        {
            this.gps = gps;
            Sensor = gps;
            string name = (gps as ISensor)?.Name ?? "GPS";
            Id = "GPS:" + name;
            Title = "GPS — " + name;

            gps.MeasurementArived += OnMeasurement;

            // úvodní vykreslení z posledního známého měření (pokud je)
            Apply(gps.GetLastMeasurement());
        }

        private void OnMeasurement(object? sender, GPSState state)
        {
            if (state == null)
                return;
            Dispatcher.UIThread.Post(() => Apply(state));
        }

        /// <summary>Promítne měření do vlastností (musí běžet na UI vlákně).</summary>
        private void Apply(GPSState? s)
        {
            if (s == null)
                return;

            PositionText = string.Format(CultureInfo.InvariantCulture,
                "lat {0,11:F6}°   lon {1,11:F6}°", s.Latitude, s.Longitude);
            AltitudeText = string.Format(CultureInfo.InvariantCulture, "{0:F1} m", s.Altitude);
            QualityText = string.Format(CultureInfo.InvariantCulture,
                "{0} ({1})   fix {2}", (int)s.Quality, s.Quality, s.IsFixed ? "ano" : "ne");
            SatellitesText = s.NumberOfSatellites.ToString(CultureInfo.InvariantCulture);
            HdopText = s.Hdop.ToString("F1", CultureInfo.InvariantCulture);

            // Kurz: přednost má dvouanténní Orientation, jinak dynamický (z pohybu).
            // Obě jsou v matematické orientaci (0 = východ, +CCW) -> převod na azimut.
            double? orientRad = s.Orientation ?? s.DynamicOrientation;
            if (orientRad is double o)
            {
                double azimuthDeg = Conversions.Rad2Deg(Conversions.Orientation2Azimut(o));
                HeadingDeg = ((azimuthDeg % 360) + 360) % 360;
                HasHeading = true;
                string src = s.Orientation != null ? "2ant" : "dyn";
                OrientationText = string.Format(CultureInfo.InvariantCulture,
                    "{0:F1}°   ({1})", HeadingDeg, src);
            }
            else
            {
                HasHeading = false;
                OrientationText = "-";
            }

            double? speed = s.Speed ?? s.DynamicSpeed;
            SpeedText = speed is double v
                ? string.Format(CultureInfo.InvariantCulture, "{0:F2} m/s   ({1:F1} km/h)", v, v * 3.6)
                : "-";

            FixTimeText = s.FixTime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

            string hzText = s.FrameReceivePeriod.TotalSeconds > 0
                ? (1.0 / s.FrameReceivePeriod.TotalSeconds).ToString("0.0", CultureInfo.InvariantCulture)
                : "";
            FrameText = string.Format(CultureInfo.InvariantCulture,
                "#{0}   {1,6} Hz   {2:HH:mm:ss.fff}", s.FrameNum, hzText, s.TimeStamp);
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            if (gps != null)
                gps.MeasurementArived -= OnMeasurement;
        }
    }
}
