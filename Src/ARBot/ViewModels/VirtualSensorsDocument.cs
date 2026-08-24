using System;
using System.Globalization;
using ARBot.Common.Communication;
using ARBot.Common.Logs;
using ARBot.HAL.Devices;
using ARBot.Robot;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Dokument „Virtuální senzory": nastavení <b>šumu a systematických chyb</b> simulovaných
    /// senzorů (<see cref="ARBotHW.VirtualSensors"/>) a <b>živé měření skutečné chyby lokalizace</b>.
    ///
    /// <para><b>Proč to existuje.</b> Do 22. 8. 2026 měla simulace jen bílý šum GPS a IMU — nulová
    /// střední hodnota, takže chyba odhadu jen kolísala a nikam nerostla. Případ, který má hranová
    /// lokalizace léčit (pomalu rostoucí chyba polohy a kurzu), tak v simulaci vůbec nevznikl a
    /// musel se vnucovat ručně (<c>poseerror=</c>) — což je ale <i>známá odpověď</i>, ne skutečná
    /// úloha. Prokluz kol a bias gyra jsou systematické: neprůměrují se pryč. Viz
    /// doc/virtual-hw.md.</para>
    ///
    /// <para><b>Měření.</b> Panel páruje <see cref="GroundTruthMsg"/> (skutečnost) s
    /// <see cref="RobotStateMsg"/> (odhad) podle shodného časového razítka — obě zprávy emituje
    /// řídicí smyčka na témže tiku. Rozdíl je přímo chyba lokalizace; statistika (průměr, RMS,
    /// maximum) ukáže, jestli korekce konvergují, nebo jen šumí.</para>
    ///
    /// <para>Vlastnosti navázané na <c>NumericUpDown</c> jsou <b>decimal</b> (<c>Value</c> je
    /// <c>decimal?</c> — <c>double</c> by selhal až za běhu); stejný vzor jako
    /// <see cref="VirtualCameraDocument"/>.</para>
    /// </summary>
    public partial class VirtualSensorsDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.VirtualSensorsDocumentView);

        /// <summary>Sdílené nastavení; v design-time vlastní instance, aby návrhář nesahal na runtime.</summary>
        private readonly VirtualSensorOptions options;

        /// <summary>Parametry sceny (sum hloubky, trava) - sdilena instance z <see cref="ARBotHW"/>.</summary>
        private readonly ARBot.Common.Vision.Synthetic.SyntheticSceneOptions scene;

        private IDisposable feed;

        // --- Párování skutečnosti s odhadem (vlákno producenta) ---

        private RobotStateMsg lastEstimate;

        /// <summary>Latest-wins backpressure: než se předchozí přepočet vykreslí, další se zahazují.</summary>
        private volatile bool refreshPending;

        // Poslední spočtená chyba (vlákno producenta -> UI).
        private double errPosM, errHeadingRad;
        private bool hasError;

        // Statistika chyby polohy od posledního vynulování.
        private long statCount;
        private double statSum, statSumSq, statMax;

        // --- Šum (vazba na UI) ---

        [ObservableProperty] private decimal gpsPositionNoiseM;
        [ObservableProperty] private decimal gpsSpeedNoiseMps;
        [ObservableProperty] private decimal imuHeadingNoiseDeg;
        [ObservableProperty] private decimal imuGyroNoiseDegPerSec;

        // --- Systematické chyby (vazba na UI) ---

        [ObservableProperty] private decimal imuHeadingBiasDeg;
        [ObservableProperty] private decimal imuGyroBiasDegPerSec;
        [ObservableProperty] private decimal leftWheelSlip;
        [ObservableProperty] private decimal rightWheelSlip;

        /// <summary>Je nastavená nějaká systematická chyba? (Zvýraznění — snadno se zapomene vypnout.)</summary>
        [ObservableProperty] private bool isSystematicErrorActive;

        // --- Scena (sum hloubky, trava) - meni render virtualnich kamer, plati hned ---
        [ObservableProperty] private decimal depthNoiseM;
        [ObservableProperty] private decimal grassRoughnessM;
        [ObservableProperty] private decimal grassHeightM;

        /// <summary>
        /// Je scena dokonala rovina? Pak je zpetna projekce hranic exaktni a nakreslene hranice
        /// maji sednout na hranici v lokalni mape (zbyva jen casovani pozy). Ukazuje se v panelu,
        /// aby bylo poznat, ze bezi ten „mericí" rezim.
        /// </summary>
        [ObservableProperty] private bool isIdealPlane;

        // --- Naměřeno ---

        [ObservableProperty] private string truthText = "-";
        [ObservableProperty] private string estimateText = "-";
        [ObservableProperty] private string errorText = "-";
        [ObservableProperty] private string statsText = "-";
        [ObservableProperty] private string statusText = "Čeká se na ground truth (jen virtuální HW)…";

        /// <summary>
        /// Konstruktor. V design-time nesahá na <see cref="ARBotHW"/> ani na Stream (viz
        /// Views/README.md → „Design-time bezpečnost").
        /// </summary>
        public VirtualSensorsDocument()
        {
            Id = "VirtualSensors";
            Title = "Virtuální senzory";

            options = Avalonia.Controls.Design.IsDesignMode
                      ? new VirtualSensorOptions()
                      : (ARBotHW.Current?.VirtualSensors ?? new VirtualSensorOptions());
            scene = Avalonia.Controls.Design.IsDesignMode
                    ? new ARBot.Common.Vision.Synthetic.SyntheticSceneOptions()
                    : (ARBotHW.Current?.VirtualScene
                       ?? new ARBot.Common.Vision.Synthetic.SyntheticSceneOptions());

            LoadFromOptions();

            if (Avalonia.Controls.Design.IsDesignMode)
                return;

            try { feed = ARBotRuntime.Current?.Stream?.Connect(this); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        /// <summary>Načte hodnoty ze sdíleného nastavení (panel se dá zavřít a otevřít znovu,
        /// nastavení přitom žije dál na <see cref="ARBotHW"/>).</summary>
        private void LoadFromOptions()
        {
            // Zamerne pres generovane VLASTNOSTI, ne pres pole: setter zapise tutez hodnotu
            // zpatky do nastaveni (zadna zmena) a hlavne notifikuje UI.
            GpsPositionNoiseM = (decimal)options.GpsPositionNoiseM;
            GpsSpeedNoiseMps = (decimal)options.GpsSpeedNoiseMps;
            ImuHeadingNoiseDeg = (decimal)Rad2Deg(options.ImuHeadingNoiseRad);
            ImuGyroNoiseDegPerSec = (decimal)Rad2Deg(options.ImuGyroNoiseRad);

            ImuHeadingBiasDeg = (decimal)Rad2Deg(options.ImuHeadingBiasRad);
            ImuGyroBiasDegPerSec = (decimal)Rad2Deg(options.ImuGyroBiasRadPerSec);
            LeftWheelSlip = (decimal)options.LeftWheelSlip;
            RightWheelSlip = (decimal)options.RightWheelSlip;

            IsSystematicErrorActive = options.HasSystematicError;

            DepthNoiseM = (decimal)scene.DepthNoiseM;
            GrassRoughnessM = (decimal)scene.GrassRoughnessM;
            GrassHeightM = (decimal)scene.GrassHeightM;
            UpdateIdealPlane();
        }

        /// <summary>Renderer drzi TUTEZ instanci a cte ji pri kazdem pixelu, takze zmena plati hned -
        /// kamery se nemusi zakladat znovu.</summary>
        partial void OnDepthNoiseMChanged(decimal value)
        {
            if (value < 0m) return;
            scene.DepthNoiseM = (double)value;
            UpdateIdealPlane();
        }

        partial void OnGrassRoughnessMChanged(decimal value)
        {
            if (value < 0m) return;
            scene.GrassRoughnessM = (double)value;
            UpdateIdealPlane();
        }

        partial void OnGrassHeightMChanged(decimal value)
        {
            if (value < 0m) return;
            scene.GrassHeightM = (double)value;
        }

        private void UpdateIdealPlane()
            => IsIdealPlane = scene.DepthNoiseM <= 0 && scene.GrassRoughnessM <= 0;

        // ============================ Vazba UI -> sdílené nastavení ============================

        partial void OnGpsPositionNoiseMChanged(decimal value) => options.GpsPositionNoiseM = (double)value;
        partial void OnGpsSpeedNoiseMpsChanged(decimal value) => options.GpsSpeedNoiseMps = (double)value;
        partial void OnImuHeadingNoiseDegChanged(decimal value) => options.ImuHeadingNoiseRad = Deg2Rad((double)value);
        partial void OnImuGyroNoiseDegPerSecChanged(decimal value) => options.ImuGyroNoiseRad = Deg2Rad((double)value);

        partial void OnImuHeadingBiasDegChanged(decimal value)
        {
            options.ImuHeadingBiasRad = Deg2Rad((double)value);
            AfterSystematicChanged();
        }

        partial void OnImuGyroBiasDegPerSecChanged(decimal value)
        {
            options.ImuGyroBiasRadPerSec = Deg2Rad((double)value);
            AfterSystematicChanged();
        }

        partial void OnLeftWheelSlipChanged(decimal value)
        {
            // Nula ani záporná hodnota nedává smysl (kolo by jelo pozpátku vůči enkodéru) a
            // v NumericUpDown se dá snadno přejet — panel ji nepřevezme.
            if (value <= 0m) return;
            options.LeftWheelSlip = (double)value;
            AfterSystematicChanged();
        }

        partial void OnRightWheelSlipChanged(decimal value)
        {
            if (value <= 0m) return;
            options.RightWheelSlip = (double)value;
            AfterSystematicChanged();
        }

        /// <summary>Prokluz kol drží <c>SimulatedRobot</c>, ne nastavení — je nutné ho přenést.</summary>
        private void AfterSystematicChanged()
        {
            IsSystematicErrorActive = options.HasSystematicError;
            try { ARBotHW.Current?.ApplyVirtualSensorOptions(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        /// <summary>Vynuluje systematické chyby (šum zůstává) — snadno se zapomene, proto tlačítko.</summary>
        [RelayCommand]
        private void ResetSystematicError()
        {
            ImuHeadingBiasDeg = 0m;
            ImuGyroBiasDegPerSec = 0m;
            LeftWheelSlip = 1m;
            RightWheelSlip = 1m;
        }

        /// <summary>Vynuluje statistiku chyby — po zásahu do nastavení nemají stará čísla smysl.</summary>
        [RelayCommand]
        private void ResetStats()
        {
            statCount = 0;
            statSum = 0;
            statSumSq = 0;
            statMax = 0;
            StatsText = "-";
        }

        // ============================ IMessageSink (vlákno producenta) ============================

        public void Post(Message msg)
        {
            switch (msg)
            {
                case RobotStateMsg rs:
                    // Odhad přichází první; ground truth hned za ním se stejným razítkem.
                    lastEstimate = rs;
                    break;

                case GroundTruthMsg gt:
                    var est = lastEstimate;
                    if (est == null || est.TimeStamp != gt.TimeStamp) return;

                    double dx = gt.X - est.X, dy = gt.Y - est.Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    double dth = NormalizeAngle(gt.Theta - est.Theta);

                    statCount++;
                    statSum += dist;
                    statSumSq += dist * dist;
                    if (dist > statMax) statMax = dist;

                    errPosM = dist;
                    errHeadingRad = dth;
                    hasError = true;

                    double tx = gt.X, ty = gt.Y, tth = gt.Theta;
                    double ex = est.X, ey = est.Y, eth = est.Theta;

                    if (refreshPending) return;
                    refreshPending = true;
                    Dispatcher.UIThread.Post(() => ApplyMeasured(tx, ty, tth, ex, ey, eth),
                                             DispatcherPriority.Background);
                    break;
            }
        }

        /// <summary>Promítne skutečnost, odhad a jejich rozdíl do panelu (UI vlákno).</summary>
        private void ApplyMeasured(double tx, double ty, double tth, double ex, double ey, double eth)
        {
            refreshPending = false;
            if (!hasError) return;

            var ci = CultureInfo.CurrentCulture;
            TruthText = string.Format(ci, "X {0:F2} m   Y {1:F2} m   θ {2:F2}°", tx, ty, Rad2Deg(tth));
            EstimateText = string.Format(ci, "X {0:F2} m   Y {1:F2} m   θ {2:F2}°", ex, ey, Rad2Deg(eth));
            ErrorText = string.Format(ci, "{0:F3} m   {1:+0.00;-0.00}°", errPosM, Rad2Deg(errHeadingRad));

            long n = statCount;
            if (n > 0)
            {
                double mean = statSum / n;
                double rms = Math.Sqrt(statSumSq / n);
                StatsText = string.Format(ci, "n {0}   průměr {1:F3} m   RMS {2:F3} m   max {3:F3} m",
                                          n, mean, rms, statMax);
            }

            StatusText = "skutečnost vs. odhad (jen virtuální HW)";
        }

        private static double Rad2Deg(double rad) => rad * 180.0 / Math.PI;
        private static double Deg2Rad(double deg) => deg * Math.PI / 180.0;

        /// <summary>Úhel do intervalu (-pi, pi] — bez toho by rozdíl kurzů u ±180° skákal o 360°.</summary>
        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI) angle -= 2 * Math.PI;
            while (angle <= -Math.PI) angle += 2 * Math.PI;
            return angle;
        }

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            try { feed?.Dispose(); } catch { /* Stream už mohl skončit */ }
            feed = null;
        }
    }
}
