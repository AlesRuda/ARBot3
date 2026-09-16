using System;
using System.Globalization;
using System.Numerics;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using ARBot.Common.Models;
using ARBot.HAL;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        // Stopa magnetickeho pole pro mereni ruseni na robotu (kabely, zelezo). Plni se na
        // vlakne senzoru, tedy BEZ backpressure - statistika pres okno musi videt vsechny
        // vzorky, kdezto prekreslovani se dal skrti pres Flush(). Viz MagTrace.
        private readonly MagTrace magTrace = new MagTrace();

        /// <summary>Kurz pro kompas [°], 0 = sever, roste po směru hod. ručiček.</summary>
        [ObservableProperty] private double headingDeg;
        /// <summary>Sklon pro umělý horizont [°].</summary>
        [ObservableProperty] private double pitchDeg;
        /// <summary>Náklon pro umělý horizont [°].</summary>
        [ObservableProperty] private double rollDeg;

        // Každá skalární hodnota má VLASTNÍ vlastnost a ve view vlastní buňku pevné šířky.
        // Dřív byly slepené po třech i po čtyřech do jednoho TextBlocku ("yaw x pitch y roll z"),
        // takže změna šířky jednoho čísla posouvala všechna ostatní a údaje na obrazovce
        // poskakovaly (hlášeno z běhu na zařízení 31. 8. 2026). Monospace font sám nestačí —
        // mění se počet znaků (znaménko, řád), ne jen jejich šířka.
        [ObservableProperty] private string yawText = "-";
        [ObservableProperty] private string pitchText = "-";
        [ObservableProperty] private string rollText = "-";
        [ObservableProperty] private string angVelXText = "-";
        [ObservableProperty] private string angVelYText = "-";
        [ObservableProperty] private string angVelZText = "-";
        [ObservableProperty] private string accXText = "-";
        [ObservableProperty] private string accYText = "-";
        [ObservableProperty] private string accZText = "-";
        [ObservableProperty] private string magXText = "-";
        [ObservableProperty] private string magYText = "-";
        [ObservableProperty] private string magZText = "-";
        [ObservableProperty] private string quatXText = "-";
        [ObservableProperty] private string quatYText = "-";
        [ObservableProperty] private string quatZText = "-";
        [ObservableProperty] private string quatWText = "-";
        [ObservableProperty] private string uncYawText = "-";
        [ObservableProperty] private string uncPitchText = "-";
        [ObservableProperty] private string uncRollText = "-";
        [ObservableProperty] private string confidenceText = "-";

        /// <summary>Snimek stopy pro graf; nova instance = prekresleni.</summary>
        [ObservableProperty] private MagSnimek? magSnapshot;
        /// <summary>Velikost pole |B| [G] - zemske pole je konstanta, takze zmena = ruseni.</summary>
        [ObservableProperty] private string magAbsText = "-";
        /// <summary>Rozpeti |B| pres okno [G] - pres pomalou otocku je to primo mira tvrdeho zeleza.</summary>
        [ObservableProperty] private string magSpanText = "-";
        [ObservableProperty] private string magDeltaXText = "-";
        [ObservableProperty] private string magDeltaYText = "-";
        [ObservableProperty] private string magDeltaZText = "-";
        [ObservableProperty] private string magDeltaAbsText = "-";
        /// <summary>Stoji robot? Text pro obsluhu; <see cref="MagKlid"/> je na obarveni.</summary>
        [ObservableProperty] private string magKlidText = "-";
        [ObservableProperty] private bool magKlid;
        /// <summary>Je nastavena nula (rozdily se pocitaji proti ni, ne proti prumeru okna)?</summary>
        [ObservableProperty] private bool magNula;
        // Syrove hodnoty pro SensorFrameInfoControl - formatovani i pevne sloupce resi control.
        [ObservableProperty] private long frameNum;
        [ObservableProperty] private TimeSpan framePeriod;
        [ObservableProperty] private DateTime frameTime;

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

            // Stopa se plni ZDE, ne ve Flush(): Flush zahazuje mezilehla mereni (backpressure),
            // takze by statistika pres okno i rozpeti |B| pocitaly jen z toho, co stihlo UI.
            if (state.Magnetometer is Vector3 mag)
            {
                var ypr = state.YPR();
                magTrace.Pridej(state.TimeStamp, mag, state.AngularVelocity,
                                ypr != null ? ypr.Yaw : (double?)null);
            }

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
                YawText = Num(HeadingDeg, "F1");
                PitchText = Num(PitchDeg, "F1");
                RollText = Num(RollDeg, "F1");
            }

            if (s.Rotation is Quaternion q)
            {
                QuatXText = Num(q.X, "F4");
                QuatYText = Num(q.Y, "F4");
                QuatZText = Num(q.Z, "F4");
                QuatWText = Num(q.W, "F4");
            }

            SetVec(s.AngularVelocity, v => { AngVelXText = v.X; AngVelYText = v.Y; AngVelZText = v.Z; });
            SetVec(s.Acceleration, v => { AccXText = v.X; AccYText = v.Y; AccZText = v.Z; });
            SetVec(s.Magnetometer, v => { MagXText = v.X; MagYText = v.Y; MagZText = v.Z; });
            ConfidenceText = s.Confidence.ToString("F2", CultureInfo.InvariantCulture);

            if (s.OrientationUncertainty is Vector3 u)
            {
                UncYawText = Num(Conversions.Rad2Deg(u.X), "F2");
                UncPitchText = Num(Conversions.Rad2Deg(u.Y), "F2");
                UncRollText = Num(Conversions.Rad2Deg(u.Z), "F2");
            }

            ApplyMag();

            FrameNum = s.FrameNum;
            FramePeriod = s.FrameReceivePeriod;
            FrameTime = s.TimeStamp;
        }

        /// <summary>
        /// Prepocte panel magnetometru ze stopy. Jednotky: <b>G</b> pro absolutni hodnoty,
        /// <b>mG</b> pro rozdily — hledane ruseni je jednotky az desitky mG proti poli ~490 mG,
        /// takze v gaussech by se ztratilo v zaokrouhleni.
        /// </summary>
        private void ApplyMag()
        {
            var snap = magTrace.Snimek();
            MagSnapshot = snap;
            MagNula = snap.Nula.HasValue;

            if (snap.Vzorky.Count == 0)
            {
                MagAbsText = MagSpanText = "-";
                MagDeltaXText = MagDeltaYText = MagDeltaZText = MagDeltaAbsText = "-";
                MagKlidText = "-";
                MagKlid = false;
                return;
            }

            MagAbsText = Num(snap.VelikostG, "F4");
            MagSpanText = Num(snap.RozpetiG, "F4");
            MagDeltaXText = Num(snap.Rozdil.X * 1000, "F1");
            MagDeltaYText = Num(snap.Rozdil.Y * 1000, "F1");
            MagDeltaZText = Num(snap.Rozdil.Z * 1000, "F1");
            MagDeltaAbsText = Num(snap.RozdilVelikostG * 1000, "F1");

            // ⚠️ Bez teto hlasky by meridlo lhalo: pootoceni o 1 stupen udela ve vodorovne slozce
            // ~3,5 mG, tedy vic nez cely hledany efekt (kabely ke kameram: 6,4 mG).
            MagKlid = snap.Klid;
            string omega = double.IsNaN(snap.OmegaDegS)
                ? "|w| nezname"
                : string.Format(CultureInfo.InvariantCulture, "|w| {0:F1} °/s", snap.OmegaDegS);
            string otoceni = snap.YawOdNulyDeg.HasValue
                ? string.Format(CultureInfo.InvariantCulture, ", od nuly {0:F1}°", snap.YawOdNulyDeg.Value)
                : string.Empty;
            MagKlidText = omega + otoceni + (snap.Klid ? "  — platí" : "  — ROBOT SE HÝBE, NEPLATÍ");
        }

        /// <summary>Vezme současné pole za nulu — rozdíly se od teď počítají proti němu.</summary>
        [RelayCommand]
        private void MagVynulovat()
        {
            magTrace.Vynuluj();
            ApplyMag();
        }

        /// <summary>Zruší nulu (rozdíly se počítají proti průměru okna).</summary>
        [RelayCommand]
        private void MagZrusitNulu()
        {
            magTrace.ZrusNulu();
            ApplyMag();
        }

        /// <summary>Zahodí historii i nulu — na začátek dalšího pokusu.</summary>
        [RelayCommand]
        private void MagVymazat()
        {
            magTrace.Vymaz();
            ApplyMag();
        }

        /// <summary>Jedno číslo bez jednotky a bez odsazení - zarovnání řeší buňka ve view.</summary>
        private static string Num(double v, string format)
            => v.ToString(format, CultureInfo.InvariantCulture);

        /// <summary>
        /// Rozdělí volitelný vektor do tří textů. Chybějící vektor dá pomlčky, aby se ve view
        /// nemíchaly staré hodnoty s novými.
        /// </summary>
        private static void SetVec(Vector3? v, Action<(string X, string Y, string Z)> set)
        {
            set(v is Vector3 val
                ? (Num(val.X, "F3"), Num(val.Y, "F3"), Num(val.Z, "F3"))
                : ("-", "-", "-"));
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
