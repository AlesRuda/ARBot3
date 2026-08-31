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

        // Backpressure: nejnovější nezpracované měření (starší se zahazují), jednotně s ostatními
        // dokumenty - viz OnMeasurement/Flush.
        private readonly object pendingGate = new object();
        private GPSState? pendingState;
        private volatile bool updateQueued;

        /// <summary>Kurz pro kompas [°], 0 = sever, roste po směru hod. ručiček.</summary>
        [ObservableProperty] private double headingDeg;
        /// <summary>Zda je kurz k dispozici (jinak se kompas zbytečně netočí na 0).</summary>
        [ObservableProperty] private bool hasHeading;

        // Každá hodnota má VLASTNÍ vlastnost a ve view vlastní buňku pevné šířky - dřív byly
        // slepené do jednoho TextBlocku ("lat X°   lon Y°"), takže změna délky jednoho čísla
        // posouvala sousední údaje a text na obrazovce poskakoval (hlášeno z běhu 31. 8. 2026).
        [ObservableProperty] private string latitudeText = "-";
        [ObservableProperty] private string longitudeText = "-";
        [ObservableProperty] private string altitudeText = "-";
        [ObservableProperty] private string qualityCodeText = "-";
        [ObservableProperty] private string qualityNameText = "-";
        [ObservableProperty] private string fixText = "-";
        [ObservableProperty] private string satellitesText = "-";
        [ObservableProperty] private string hdopText = "-";
        [ObservableProperty] private string headingText = "-";
        [ObservableProperty] private string headingSourceText = "-";
        [ObservableProperty] private string speedMsText = "-";
        [ObservableProperty] private string speedKmhText = "-";
        [ObservableProperty] private string fixTimeText = "-";
        // Syrove hodnoty pro SensorFrameInfoControl - formatovani i pevne sloupce resi control.
        [ObservableProperty] private long frameNum;
        [ObservableProperty] private TimeSpan framePeriod;
        [ObservableProperty] private DateTime frameTime;

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

        // Běží na vlákně senzoru. Uloží jen nejnovější měření a koalescovaně naplánuje jednu
        // UI aktualizaci na Background prioritě (starší měření se zahodí).
        private void OnMeasurement(object? sender, GPSState state)
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

            GPSState? s;
            lock (pendingGate)
            {
                s = pendingState;
                pendingState = null;
            }

            if (s != null)
                Apply(s);
        }

        /// <summary>Promítne měření do vlastností (musí běžet na UI vlákně).</summary>
        private void Apply(GPSState? s)
        {
            if (s == null)
                return;

            // GPSState drzi RADIANY (viz GPSState.Latitude), zobrazuje se ve STUPNICH.
            LatitudeText = ARBot.Common.Common.Conversions.Rad2Deg(s.Latitude)
                .ToString("F6", CultureInfo.InvariantCulture);
            LongitudeText = ARBot.Common.Common.Conversions.Rad2Deg(s.Longitude)
                .ToString("F6", CultureInfo.InvariantCulture);
            AltitudeText = s.Altitude.ToString("F1", CultureInfo.InvariantCulture);
            QualityCodeText = ((int)s.Quality).ToString(CultureInfo.InvariantCulture);
            QualityNameText = s.Quality.ToString();
            FixText = s.IsFixed ? "ano" : "ne";
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
                HeadingText = HeadingDeg.ToString("F1", CultureInfo.InvariantCulture);
                HeadingSourceText = s.Orientation != null ? "2ant" : "dyn";
            }
            else
            {
                HasHeading = false;
                HeadingText = "-";
                HeadingSourceText = "";
            }

            double? speed = s.Speed ?? s.DynamicSpeed;
            SpeedMsText = speed is double v ? v.ToString("F2", CultureInfo.InvariantCulture) : "-";
            SpeedKmhText = speed is double v2 ? (v2 * 3.6).ToString("F1", CultureInfo.InvariantCulture) : "-";

            FixTimeText = s.FixTime.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);

            FrameNum = s.FrameNum;
            FramePeriod = s.FrameReceivePeriod;
            FrameTime = s.TimeStamp;
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
