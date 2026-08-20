using System;
using System.Globalization;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Simulation;
using ARBot.HAL;
using ARBot.Robot;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokument pro VIRTUÁLNÍ kameru: standardní náhled (zděděný z <see cref="CameraDocument"/>)
    /// plus panel pro vnucení <b>umělé chyby pózy</b> do renderovací cesty.
    ///
    /// <para><b>Proč to existuje.</b> Ve virtuálním HW renderuje kamera z odhadu fúze a occupancy
    /// grid se týmž odhadem ukotvuje, takže korelace s mapou vychází nulová <i>strukturálně</i> —
    /// i kdyby byl korelátor rozbitý. Když se ale renderuje z pózy posunuté o známou chybu, musí
    /// korelátor ohlásit právě ji. Panel proto ukazuje <b>očekávané</b> hodnoty vedle
    /// <b>naměřených</b> z <see cref="MapCorrelationMsg"/>. Viz doc/map-correlation-localization.md.</para>
    ///
    /// <para>Chyba je sdílená oběma kamerami (<see cref="ARBotHW.VirtualPoseError"/>) — kdyby měla
    /// levá jinou než pravá, fúzovaný grid by nedával smysl. Panel je tedy pro obě tentýž.</para>
    /// </summary>
    public partial class VirtualCameraDocument : CameraDocument, IMessageSink
    {
        public override Type ViewType => typeof(ARBot.Views.VirtualCameraDocumentView);

        /// <summary>Sdílená vnucená chyba; v design-time vlastní instance, aby návrhář nesahal na runtime.</summary>
        private readonly VirtualPoseError error;

        private IDisposable? feed;

        /// <summary>Poslední kurz robota [rad] — z <see cref="RobotStateMsg"/>, kvůli převodu FLU → ENU.</summary>
        private double lastTheta;

        // --- Vnucená chyba (v rámci robotu; vazba na UI) ---

        // POZOR na typ: Avalonia NumericUpDown.Value je decimal?, takže vlastnosti navázané na
        // něj musí být decimal — jinak vazba selže až za běhu. Stejný vzor jako
        // WorldViewDocument.DefaultRoadWidthMeters (a tamtéž přetypování na double).

        /// <summary>Posun vpřed [m] (FLU +X).</summary>
        [ObservableProperty] private decimal forwardM;

        /// <summary>Posun vlevo [m] (FLU +Y).</summary>
        [ObservableProperty] private decimal leftM;

        /// <summary>Chyba kurzu [°], matematicky (+CCW).</summary>
        [ObservableProperty] private decimal headingDeg;

        /// <summary>Je vnucená nějaká nenulová chyba? (Zvýraznění v panelu — snadno se zapomene vypnout.)</summary>
        [ObservableProperty] private bool isErrorActive;

        // --- Očekáváno vs. naměřeno ---

        [ObservableProperty] private string expectedText = "-";
        [ObservableProperty] private string measuredText = "-";
        [ObservableProperty] private string residualText = "-";

        /// <summary>Hlášení stavu (proč se nic neměří).</summary>
        [ObservableProperty] private string statusText = "Čeká se na zprávu korelátoru…";

        /// <summary>Konstruktor pro design-time / návrhář.</summary>
        public VirtualCameraDocument()
        {
            Id = "VirtualCamera";
            Title = "Virtuální kamera";
            error = new VirtualPoseError();
        }

        public VirtualCameraDocument(ICamera camera) : base(camera)
        {
            Title = "Virtuální kamera — " + (camera?.Name ?? "?");
            error = ARBotHW.Current?.VirtualPoseError ?? new VirtualPoseError();

            // Panel má ukázat, co je právě nastavené (dokument se dá zavřít a otevřít znovu,
            // chyba přitom žije dál na ARBotHW).
            forwardM = (decimal)error.ForwardM;
            leftM = (decimal)error.LeftM;
            headingDeg = (decimal)(error.HeadingRad * 180.0 / Math.PI);
            IsErrorActive = error.IsActive;

            // Odběr ze Streamu kvůli MapCorrelationMsg (naměřeno) a RobotStateMsg (kurz pro převod).
            try { feed = ARBotRuntime.Current?.Stream?.Connect(this); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            UpdateExpected();
        }

        // ============================ Vazba UI -> sdílená chyba ============================

        partial void OnForwardMChanged(decimal value) { error.ForwardM = (double)value; AfterErrorChanged(); }
        partial void OnLeftMChanged(decimal value) { error.LeftM = (double)value; AfterErrorChanged(); }
        partial void OnHeadingDegChanged(decimal value)
        {
            error.HeadingRad = (double)value * Math.PI / 180.0;
            AfterErrorChanged();
        }

        private void AfterErrorChanged()
        {
            IsErrorActive = error.IsActive;
            UpdateExpected();
        }

        /// <summary>Vynuluje vnucenou chybu (snadno se zapomene, proto tlačítko).</summary>
        [RelayCommand]
        private void ResetError()
        {
            ForwardM = 0m;
            LeftM = 0m;
            HeadingDeg = 0m;
        }

        // ============================ IMessageSink (vlákno producenta) ============================

        public void Post(Message msg)
        {
            switch (msg)
            {
                case RobotStateMsg rs:
                    // Jen si zapamatovat kurz; přepočet očekávané hodnoty jde na UI vlákno níž.
                    lastTheta = rs.Theta;
                    break;

                case MapCorrelationMsg mc:
                    // Kopie hodnot: zpráva může být dál recyklovaná producentem.
                    double dx = mc.Dx, dy = mc.Dy, phi = mc.Phi;
                    bool emitted = mc.Emitted;
                    byte reason = mc.Reason;
                    Dispatcher.UIThread.Post(() => ApplyMeasured(dx, dy, phi, emitted, reason),
                                             DispatcherPriority.Background);
                    break;
            }
        }

        /// <summary>Promítne naměřenou korelaci do panelu (UI vlákno).</summary>
        private void ApplyMeasured(double dx, double dy, double phi, bool emitted, byte reason)
        {
            var ci = CultureInfo.CurrentCulture;
            MeasuredText = string.Format(ci, "dx {0:F3} m   dy {1:F3} m   φ {2:F2}°",
                                         dx, dy, phi * 180.0 / Math.PI);

            var (ex, ey) = error.ExpectedWorldOffset(lastTheta);
            ResidualText = string.Format(ci, "Δ {0:+0.000;-0.000} m   {1:+0.000;-0.000} m",
                                         dx - ex, dy - ey);

            StatusText = string.Format(ci, "korelace: {0}{1}",
                                       (ARBot.Common.Localization.MapCorrelationReason)reason,
                                       emitted ? "" : " · do fúze se neposlalo");

            // Kurz mezitím mohl přijít nový -> ať očekávaná hodnota nezůstane pozadu.
            UpdateExpected();
        }

        /// <summary>Přepočte očekávaný světový posun z vnucené chyby a posledního kurzu.</summary>
        private void UpdateExpected()
        {
            var (ex, ey) = error.ExpectedWorldOffset(lastTheta);
            ExpectedText = string.Format(CultureInfo.CurrentCulture,
                                         "dx {0:F3} m   dy {1:F3} m   φ {2:F2}°",
                                         ex, ey, error.HeadingRad * 180.0 / Math.PI);
        }

        public override void Dispose()
        {
            try { feed?.Dispose(); } catch { /* Stream už mohl skončit */ }
            feed = null;
            base.Dispose();
        }
    }
}
