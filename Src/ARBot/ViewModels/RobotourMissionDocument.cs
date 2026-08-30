using System;
using System.Globalization;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.Robot;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ARBot.ViewModels
{
    /// <summary>
    /// Panel <b>mise Robotour</b>: fáze automatu, stav nouzového zastavení, přečtený kód
    /// s odvozeným cílem a tlačítka Start / Potvrdit / Přerušit. Viz doc/robotour-mission.md.
    ///
    /// <para><b>Proč to existuje.</b> Mise čeká na „Start mise" od obsluhy a <b>sama se nerozjede</b>
    /// — bez tohoto panelu se tedy `mission=robotour` jen založí a zůstane v <c>Idle</c>. Zároveň je
    /// tenhle panel místem druhé pojistky přijetí cíle: <b>stroj kontroluje, člověk potvrzuje</b>,
    /// a aby to potvrzení nebylo prázdné gesto, musí obsluha vidět <i>konkrétní, zkontrolovatelný</i>
    /// cíl — přečtený text, z něj odvozené souřadnice, vzdálenost od depa a délku nalezené trasy.</para>
    ///
    /// <para><b>Stav se čte ze <see cref="MissionMsg"/> na Streamu</b>, ne z instance mise. Panel tím
    /// funguje i při <b>přehrávání záznamu</b> (režim View), kde žádná mise neběží — celá soutěžní
    /// jízda se dá přehrát fázi po fázi. Příkazy naopak potřebují živou misi
    /// (<see cref="ARBotRuntime.RobotourMission"/>); když neběží, panel to <b>říká přímo v UI</b>
    /// místo aby tlačítka tiše nic nedělala.</para>
    ///
    /// <para>Aktualizace drží povinný vzor „latest-wins + Background flush" (viz Views/README.md):
    /// <see cref="MissionMsg"/> chodí 1× za sekundu, ale při každé změně fáze i mimo periodu, a
    /// <see cref="Post"/> běží na vlákně producenta.</para>
    /// </summary>
    public partial class RobotourMissionDocument : DocumentBase, IMessageSink, IDisposable
    {
        public override Type ViewType => typeof(ARBot.Views.RobotourMissionDocumentView);

        private IDisposable feed;

        /// <summary>
        /// Živá mise pro příkazy; <c>null</c> ve View, při <c>mission=none</c> a <b>než se spustí
        /// Runtime</b>.
        ///
        /// <para><b>Hledá se ZNOVU při každém použití, ne jednou v konstruktoru.</b>
        /// <see cref="ARBotRuntime.Current"/> (a s ním <c>Stream</c>) vzniká už při prvním přístupu,
        /// ale <see cref="ARBotRuntime.RobotourMission"/> se zakládá teprve při <b>Run</b>. Panel
        /// otevřený dřív než Run si tedy uložil <c>null</c> natrvalo: zprávy ze Streamu chodily
        /// (fáze se ukazovala správně), ale tlačítka zůstala zakázaná a panel tvrdil „mise neběží“,
        /// i když běžela. Nalezeno v běžící aplikaci 26. 8. 2026.</para>
        /// </summary>
        private static RobotourMission Mission
        {
            get
            {
                try { return ARBotRuntime.Current?.RobotourMission; }
                catch { return null; }
            }
        }

        // --- latest-wins backpressure ---
        private readonly object pendingGate = new();
        private MissionMsg pending;
        private volatile bool updateQueued;

        // --- Stav automatu ---

        [ObservableProperty] private string phaseText = "-";
        [ObservableProperty] private string waitingForText = "-";
        [ObservableProperty] private string stopText = "-";
        [ObservableProperty] private string elapsedText = "-";

        /// <summary>Je právě drženo nouzové zastavení? Řídí barvu indikátoru.</summary>
        [ObservableProperty] private bool isEmergencyStop;
        [ObservableProperty] private string emergencyText = "-";

        /// <summary>Hlásí mise „kód nevidím"? Řešením je obsluha, ne robot — proto to musí být vidět.</summary>
        [ObservableProperty] private bool isCodeNotSeen;

        [ObservableProperty] private bool isAborted;
        [ObservableProperty] private string abortReasonText = string.Empty;

        /// <summary>
        /// Kvalita fixu v depu — <b>proč</b> se (ne)pokračuje z `ArmingAtDepot`. Ukazuje se jen
        /// v té fázi; bez toho mise stojí a z panelu nejde poznat, co jí chybí.
        /// </summary>
        [ObservableProperty] private bool showFixQuality;
        [ObservableProperty] private string fixQualityText = "-";
        [ObservableProperty] private bool isFixBlocking;

        // --- Nabídnutý cíl (to, co obsluha potvrzuje) ---

        [ObservableProperty] private bool hasAcceptedTarget;
        [ObservableProperty] private string acceptedCodeText = "-";
        [ObservableProperty] private string acceptedTargetText = "-";
        [ObservableProperty] private string acceptedDistanceText = "-";
        [ObservableProperty] private string acceptedRouteText = "-";

        /// <summary>
        /// O kolik se cíl posunul přichycením na cestu. Vypisuje se proto, aby šel limit
        /// <c>MaxTargetOffRoadM</c> (dnes zvolený úsudkem) nastavit z naměřených běhů.
        /// </summary>
        [ObservableProperty] private string acceptedOffRoadText = "-";

        // --- Zapamatované cíle ---

        [ObservableProperty] private string depotText = "-";
        [ObservableProperty] private string pickupText = "-";
        [ObservableProperty] private string dropText = "-";
        [ObservableProperty] private string countersText = "-";

        // --- Náhled kamery, ze které se čte kód ---

        /// <summary>
        /// Snímek z kamery, ze které scanner čte. Vidět ho je ve chvíli míření kódem na robota
        /// zásadní — bez obrazu obsluha netuší, jestli je kód vůbec ve výhledu (podnět autora).
        /// </summary>
        [ObservableProperty] private WriteableBitmap previewImage;

        /// <summary>Ukazuje se náhled? Jen v servisním okně, kde se čte — jinde je to zbytečná režie.</summary>
        [ObservableProperty] private bool showPreview;

        [ObservableProperty] private string previewCameraText = "-";

        /// <summary>
        /// Co se s obrazem právě děje — jestli se z něj skenuje, nebo se jen míří. Bez toho není
        /// poznat, proč se přečtený kód (ještě) neobjevil.
        /// </summary>
        [ObservableProperty] private string previewHintText = string.Empty;

        /// <summary>Nejnovější snímek z té správné kamery; renderuje se až ve <c>Flush</c>.</summary>
        private CameraFrame pendingFrame;

        // --- QR kód v simulaci (testovací pomůcka) ---

        /// <summary>Je k dispozici virtuální scéna? Bez ní se QR do obrazu položit nedá.</summary>
        [ObservableProperty] private bool canPlaceQr;

        /// <summary>Text kódu, který se má položit před kameru (předplní se cílem u depa).</summary>
        [ObservableProperty] private string qrText = string.Empty;

        /// <summary>
        /// Kam kód postavit: jak daleko <b>před kameru</b> (podél jejího směru pohledu) a v jaké
        /// výšce [m]. Není to „vpravo od robota" — kamera je stočená, viz
        /// <see cref="ARBot.Common.Vision.Qr.QrBillboard.InFrontOfCamera"/>.
        ///
        /// <para><b>1,0 m je naměřená hodnota, ne odhad</b> (autor, 27. 8. 2026): z původních 1,2 m
        /// se kód v simulaci <b>nepřečetl</b>. Vzdálenost řídí, kolik pixelů na modul zbyde po
        /// projekci a podvzorkování scanneru — dál = menší modul = dekodér neuspěje.</para>
        /// </summary>
        [ObservableProperty] private decimal qrDistanceM = 1.0m;
        [ObservableProperty] private decimal qrHeightM = 0.35m;

        /// <summary>
        /// Hotové kódy stanovišť pro <b>současnou testovací mapu</b> — tlačítka vedle textu kódu.
        /// Vypisovat je pokaždé ručně je jediná zdlouhavá část průchodu misí v simulaci.
        ///
        /// <para>⚠️ Jsou vázané na tu mapu: leží <b>na cestě</b> východně od depa (nakládka ~50 m,
        /// vykládka ~100 m). Nad jinou mapou dají cíl mimo síť a mise je zamítne — to je správně,
        /// jen to znamená napsat si vlastní. Proto zůstává textové pole plně editovatelné.</para>
        /// </summary>
        private const string PickupPreset = "geo:50.029,14.5208";
        private const string DropPreset = "geo:50.029,14.5214";

        [ObservableProperty] private string qrPlacedText = string.Empty;

        /// <summary>
        /// Proč se poslední kód <b>zamítl</b>. Bez toho vypadá zamítnutí jako „kód se nepřečetl" —
        /// a tři důvody (nesrozumitelný / příliš daleko / bez trasy) znamenají úplně jiné řešení.
        /// </summary>
        [ObservableProperty] private bool hasRejected;
        [ObservableProperty] private string rejectedText = string.Empty;

        /// <summary>Postavená deska, aby se dala odebrat (a aby se nepokládalo víc kódů přes sebe).</summary>
        private ARBot.Common.Vision.Synthetic.SyntheticBillboard placedBoard;

        /// <summary>
        /// Text kódu na postavené desce — podle něj se pozná, že se přečetl <b>ten</b> kód.
        ///
        /// <para><b>Bez toho se deska mazala hned:</b> první verze odebírala desku, kdykoli
        /// <i>existoval</i> nějaký nabídnutý cíl. Jeden nepotvrzený cíl tedy znamenal, že další kód
        /// zmizel do jedné sekundy (perioda `MissionMsg`) — zelený text pod tlačítkem probliknul a
        /// kód se v obraze mihl. Nahlásil autor 26. 8. 2026.</para>
        /// </summary>
        private string placedCodeText = string.Empty;

        /// <summary>Poslední póza — kód se staví relativně k robotu, takže je potřeba.</summary>
        private RobotStateMsg lastPose;

        /// <summary>Byl už text kódu předplněn? (Aby se nepřepisoval, co obsluha napsala.)</summary>
        private bool qrTextPrefilled;

        // --- Ovládání ---

        [ObservableProperty] private bool canStart;
        [ObservableProperty] private bool canAbort;

        /// <summary>Vysvětlení, proč nejde ovládat (žádná živá mise). Prázdné = mise běží.</summary>
        [ObservableProperty] private string noMissionText = string.Empty;

        /// <summary>
        /// Konstruktor. V design-time nesahá na runtime ani na Stream (viz Views/README.md →
        /// „Design-time bezpečnost").
        /// </summary>
        public RobotourMissionDocument()
        {
            Id = "RobotourMission";
            Title = "Mise Robotour";

            if (Avalonia.Controls.Design.IsDesignMode)
                return;

            try { feed = ARBotRuntime.Current?.Stream?.Connect(this); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            // Nez prijde prvni zprava, ukaz aspon to, co uz mise vi (typicky Idle).
            var m = Mission?.LastMessage;
            if (m != null) Apply(m);
            else UpdateCommandAvailability(null);
        }

        // ============================ Příkazy obsluhy ============================

        /// <summary>„Start mise" — přechod <c>Idle</c> → <c>ArmingAtDepot</c>.</summary>
        [RelayCommand]
        private void StartMission() => Mission?.StartMission();

        /// <summary>Přerušení mise — zastavuje tvrdě, z každého stavu.</summary>
        [RelayCommand]
        private void Abort() => Mission?.Abort("přerušeno obsluhou z panelu mise");

        // ============================ QR kód v simulaci ============================

        /// <summary>
        /// Postaví <b>QR kód</b> do scény virtuální kamery — svislou desku vpravo od robota, čelem
        /// k němu. Viz doc/virtual-hw.md.
        ///
        /// <para><b>Proč to je tady a ne v misi:</b> je to <i>testovací pomůcka pro simulaci</i>,
        /// takže nesmí být součástí řídicí logiky. Panel je UI, takže smí znát obojí — stav mise
        /// i virtuální scénu; mise o virtuálních kamerách nadále neví nic.</para>
        ///
        /// <para>Kód se staví <b>vpravo</b>, protože se čte z pravé kamery (<c>QrCameraName</c>).
        /// Normála desky míří zpět k robotu, aby na ni koukal zpředu.</para>
        /// </summary>
        [RelayCommand]
        private void PlaceQr()
        {
            var scene = Scene;
            if (scene == null) { QrPlacedText = "Virtuální scéna není — jde to jen s virtualhw=true."; return; }

            string text = (QrText ?? string.Empty).Trim();
            if (text.Length == 0) { QrPlacedText = "Zadej text kódu (např. geo:50.03,14.52)."; return; }

            var pose = lastPose;
            if (pose == null) { QrPlacedText = "Neznám pózu robota — počkej na první RobotStateMsg."; return; }

            RemoveQr();

            // Směr si bere z MONTÁŽNÍ MATICE kamery, ne z domněnky „vpravo je vpravo": pravá kamera
            // je stočená o 29° vpravo, takže deska postavená 90° vpravo je mimo její výhled (a když
            // se do obrazu dostane, je zkosená). Nalezeno v aplikaci 26. 8. 2026.
            var board = ARBot.Common.Vision.Qr.QrBillboard.InFrontOfCamera(
                text, QrCameraMount, pose.X, pose.Y, pose.Theta,
                (double)QrDistanceM, (double)QrHeightM);

            lock (scene.Billboards) scene.Billboards.Add(board);
            placedBoard = board;
            placedCodeText = text;

            QrPlacedText = string.Format(CultureInfo.CurrentCulture,
                "Kód stojí {0:F2} m před kamerou {1} ve výšce {2:F2} m, kolmo na ni. "
                + "Zmizí sám, až se přečte.",
                (double)QrDistanceM,
                string.IsNullOrWhiteSpace(QrCameraName) ? "Right" : QrCameraName,
                (double)QrHeightM);
        }

        /// <summary>Odebere postavený kód ze scény. Volá se i automaticky, až se kód přečte.</summary>
        [RelayCommand]
        private void RemoveQr()
        {
            var scene = Scene;
            var board = placedBoard;
            placedBoard = null;
            placedCodeText = string.Empty;
            if (scene == null || board == null) return;

            lock (scene.Billboards) scene.Billboards.Remove(board);
            QrPlacedText = string.Empty;
        }

        /// <summary>
        /// Předvyplní kód nakládky. Kód se <b>jen zapíše do pole</b>, nestaví se — postavení je
        /// pořád vědomý krok („Postavit QR kód"), protože jinak by tlačítko dělalo dvě věci naráz
        /// a překlep by se nedal opravit.
        /// </summary>
        [RelayCommand]
        private void UsePickupPreset() => SetPreset(PickupPreset);

        /// <summary>Předvyplní kód vykládky; jinak totéž co <see cref="UsePickupPreset"/>.</summary>
        [RelayCommand]
        private void UseDropPreset() => SetPreset(DropPreset);

        private void SetPreset(string code)
        {
            QrText = code;
            // Preset je vědomá volba obsluhy, takže automatické předvyplnění už nemá co dělat -
            // jinak by při dalším MissionMsg text přepsalo zpátky.
            qrTextPrefilled = true;
            QrPlacedText = string.Empty;
        }

        /// <summary>Virtuální scéna, nebo <c>null</c> (reálné HW / před startem Runtime).</summary>
        private static ARBot.Common.Vision.Synthetic.SyntheticSceneOptions Scene
        {
            get
            {
                // ActiveVirtualScene, ne VirtualScene: kamery mohou renderovat z jine instance a
                // psani do te druhe je TICHÁ vada (viz ARBotHW.ActiveVirtualScene).
                try { return ARBotHW.Current?.ActiveVirtualScene; }
                catch { return null; }
            }
        }

        // ============================ IMessageSink (vlákno producenta) ============================

        public void Post(Message msg)
        {
            // Poza se pamatuje bez preplanovani UI - potrebuje ji jen prikaz "postav QR kod",
            // takze by kazdy tik smycky zbytecne budil dispatcher.
            if (msg is RobotStateMsg rs) { lastPose = rs; return; }

            if (msg is CameraFrame frame)
            {
                // Snimky chodi ~30 Hz z obou kamer. Zahazuji se HNED, kdyz nahled neni videt -
                // jinak by skryty panel choval WriteableBitmap na pozadi (viz Views/README.md,
                // gate na IsActive).
                if (!showPreview || !IsActive || !IsPreviewCamera(frame.Name)) return;
                if (frame.ImageRGB == null) return;

                lock (pendingGate) pendingFrame = frame;
                ScheduleFlush();
                return;
            }

            if (!(msg is MissionMsg m)) return;

            lock (pendingGate) pending = m;          // nejnovejsi vyhrava
            ScheduleFlush();
        }

        /// <summary>Naplanuje jednu aktualizaci UI (latest-wins; viz Views/README.md).</summary>
        private void ScheduleFlush()
        {
            if (updateQueued) return;
            updateQueued = true;
            Dispatcher.UIThread.Post(Flush, DispatcherPriority.Background);
        }

        private void Flush()
        {
            updateQueued = false;

            MissionMsg m;
            CameraFrame f;
            lock (pendingGate)
            {
                m = pending; pending = null;
                f = pendingFrame; pendingFrame = null;
            }

            if (m != null) Apply(m);
            if (f?.ImageRGB != null && ShowPreview && IsActive) UpdatePreview(f.ImageRGB);
        }

        /// <summary>Je to kamera, ze ktere scanner cte? Prazdne jmeno v konfiguraci = kterakoli.</summary>
        private static bool IsPreviewCamera(string frameName)
        {
            string want = QrCameraName;
            return string.IsNullOrWhiteSpace(want)
                   || string.Equals(frameName, want, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Montazni matice kamery, ze ktere se cte kod. Prazdne jmeno kamery (= skenuji se vsechny)
        /// bere <b>pravou</b>, protoze tam ma kod podle navrhu mise byt.
        /// </summary>
        private static System.Numerics.Matrix4x4 QrCameraMount
            => string.Equals(QrCameraName, "Left", StringComparison.OrdinalIgnoreCase)
               ? ARBot.Common.Configuration.Profile.LeftCameraTransform
               : ARBot.Common.Configuration.Profile.RightCameraTransform;

        /// <summary>Kamera, ze ktere scanner cte (z jeho konfigurace, ne z domnenky panelu).</summary>
        private static string QrCameraName
        {
            get
            {
                try { return ARBotRuntime.Current?.QrScanner?.Config?.CameraName ?? "Right"; }
                catch { return "Right"; }
            }
        }

        /// <summary>Zkopiruje BGR32 snimek do <see cref="WriteableBitmap"/> (tyz vzor jako CameraDocument).</summary>
        private void UpdatePreview(ARBot.Common.Common.Image<ARBot.Common.Common.BGR32> rgb)
        {
            int w = rgb.Width, h = rgb.Height;
            if (w <= 0 || h <= 0) return;

            var bmp = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                                          PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var fb = bmp.Lock())
            {
                int rowBytes = w * 4;
                var data = rgb.Data;
                for (int y = 0; y < h; y++)
                    System.Runtime.InteropServices.Marshal.Copy(
                        data, y * rowBytes, fb.Address + y * fb.RowBytes, rowBytes);
            }

            PreviewImage = bmp;
            PreviewCameraText = string.Format(CultureInfo.CurrentCulture, "{0}  {1}x{2}",
                                              string.IsNullOrWhiteSpace(QrCameraName) ? "kamera" : QrCameraName,
                                              w, h);
        }

        /// <summary>Promítne stav mise do panelu (UI vlákno).</summary>
        private void Apply(MissionMsg m)
        {
            var ci = CultureInfo.CurrentCulture;
            var phase = (RobotourPhase)m.Phase;
            var stop = (RobotourStop)m.Stop;

            PhaseText = PhaseName(phase);
            WaitingForText = WaitingFor(phase, stop, m);
            StopText = IsServiceWindow(phase) ? StopName(stop) : "—";
            // V Idle mise jeste nezacala, takze „0 s" by tvrdilo, ze zacala prave nyni.
            ElapsedText = phase == RobotourPhase.Idle
                ? "—" : string.Format(ci, "{0:F0} s", m.ElapsedSec);

            IsEmergencyStop = m.EmergencyStop;
            EmergencyText = m.EmergencyStop ? "DRŽENO" : "uvolněno";

            IsCodeNotSeen = m.CodeNotSeen;
            IsAborted = phase == RobotourPhase.Aborted;
            AbortReasonText = m.AbortReason ?? string.Empty;

            ShowFixQuality = phase == RobotourPhase.ArmingAtDepot;
            ApplyFixQuality(m, ci);

            // Deska zmizí, až se přečte TEN JEJÍ kód — ať už se přijal (nabídnutý cíl), nebo zamítl.
            // Zamítnutý se odebírá taky: jinak by ho scanner čtl a zamítal dál a čítač by rostl,
            // zatímco obsluha by opravovala text.
            //
            // Porovnává se TEXT, ne jen „existuje nabídnutý cíl": s tou podmínkou se každý nově
            // postavený kód smazal do sekundy, dokud byl nějaký cíl nepotvrzený.
            if (placedBoard != null && placedCodeText.Length > 0
                && (string.Equals(m.AcceptedCodeText, placedCodeText, StringComparison.Ordinal)
                    || string.Equals(m.RejectedCodeText, placedCodeText, StringComparison.Ordinal)))
            {
                RemoveQr();
            }

            // Nahled jen tam, kde se cte kod - jinde by to byla jen rezie.
            // Náhled běží po CELOU dobu servisního okna tam, kde se čte kód — ne jen ve
            // `Servicing`.
            //
            // Původně byl vázaný jen na `Servicing`, tedy na držený stop. To přestalo stačit ve dvou
            // krocích: (a) mířit kódem se musí UŽ PŘED stisknutím stopu, a přesně tehdy je fáze
            // `AwaitingEStop`, takže panel neukazoval nic; (b) po zrušení potvrzování se `Servicing`
            // opouští hned po přečtení kódu, takže se okno viditelnosti zkrátilo na okamžik.
            // Nahlásil autor 27. 8. 2026.
            //
            // Skenování to NEROZŠIŘUJE: `QrScanner.Enabled` řídí mise a zapíná ho výhradně pod
            // drženým stopem. Ukázat obraz není totéž jako z něj číst.
            ShowPreview = CodeExpected(stop)
                          && (phase == RobotourPhase.AwaitingEStop
                              || phase == RobotourPhase.Servicing
                              || phase == RobotourPhase.AwaitingEStopRelease);

            // Aby bylo poznat, proč se z obrazu (ještě) nečte.
            PreviewHintText = phase switch
            {
                RobotourPhase.AwaitingEStop =>
                    "Zaměř kód na tuhle kameru a pak zmáčkni nouzové zastavení — skenuje se až pod ním.",
                RobotourPhase.Servicing => "Skenuje se. Drž stop, dokud se kód nepřečte.",
                RobotourPhase.AwaitingEStopRelease =>
                    "Kód je přečtený a přijatý. Uvolni nouzové zastavení a robot vyrazí.",
                _ => string.Empty,
            };
            if (!ShowPreview) PreviewImage = null;

            CanPlaceQr = Scene != null;
            PrefillQrText(m, ci);

            // Zamitnuti se ukazuje, dokud neprijde prijaty kod (ten duvod v misi smaze).
            HasRejected = !string.IsNullOrEmpty(m.RejectReason);
            RejectedText = HasRejected
                ? string.Format(ci, "„{0}“ — {1}",
                                string.IsNullOrEmpty(m.RejectedCodeText) ? "?" : m.RejectedCodeText,
                                m.RejectReason)
                : string.Empty;

            HasAcceptedTarget = m.HasAcceptedCode;
            AcceptedCodeText = string.IsNullOrEmpty(m.AcceptedCodeText) ? "-" : m.AcceptedCodeText;
            AcceptedTargetText = m.HasAcceptedCode
                ? string.Format(ci, "{0:F6}° , {1:F6}°", m.AcceptedLatDeg, m.AcceptedLonDeg) : "-";
            AcceptedDistanceText = m.HasAcceptedCode
                ? string.Format(ci, "{0:F0} m od depa", m.AcceptedDistanceFromDepotM) : "-";
            // Nula znamena "zkouska neprobehla", ne "trasa je nulova" - to je rozdil, ktery obsluha
            // pred potvrzenim potrebuje znat.
            AcceptedRouteText = !m.HasAcceptedCode ? "-"
                : m.AcceptedRouteLengthM > 0
                    ? string.Format(ci, "trasa {0:F0} m", m.AcceptedRouteLengthM)
                    : "délka trasy neznámá (dosažitelnost se neověřovala)";
            // Souradnice vyse uz jsou PRICHYCENE na cestu, takze bez tohohle radku nejde poznat,
            // ze se cil vubec posunul - a o kolik.
            AcceptedOffRoadText = !m.HasAcceptedCode ? "-"
                : string.Format(ci, "{0:F1} m od cesty (přichyceno na síť)", m.AcceptedOffRoadM);

            DepotText = Position(m.HasDepot, m.DepotLatDeg, m.DepotLonDeg, ci);
            PickupText = Target(m.HasPickup, m.PickupLatDeg, m.PickupLonDeg, m.PickupCodeText, ci);
            DropText = Target(m.HasDrop, m.DropLatDeg, m.DropLonDeg, m.DropCodeText, ci);

            CountersText = string.Format(ci, "kódů přečteno {0}, zamítnuto {1}, timeoutů {2}",
                                         m.CodesRead, m.CodesRejected, m.Timeouts);

            UpdateCommandAvailability(m);
        }

        /// <summary>
        /// Předplní text kódu <b>kódem nakládky</b> (<see cref="PickupPreset"/>) — tedy prvním
        /// stanovištěm, které obsluha stejně potřebuje.
        ///
        /// <para>Prázdné pole by obsluhu nutilo souřadnice vymýšlet, a špatně zvolený cíl mise
        /// (mimo síť, moc daleko) zamítne — což pak vypadá jako vada čtení, ne jako špatné zadání.
        /// Předplní se <b>jednou</b>, aby se nepřepsalo, co obsluha napsala.</para>
        ///
        /// <para><b>Dřív se počítal cíl 50 m severně od depa</b> — bod nezávislý na mapě, ale na té
        /// rovné testovací mapě leží <b>50 m od cesty</b>, takže by ho od 27. 8. 2026 zamítl limit
        /// <c>MaxTargetOffRoadM</c> (15 m) pokaždé. Předvyplnění, které vždycky selže, je horší než
        /// hodnota vázaná na mapu.</para>
        /// </summary>
        private void PrefillQrText(MissionMsg m, CultureInfo ci)
        {
            if (qrTextPrefilled || !m.HasDepot) return;
            if (!string.IsNullOrWhiteSpace(QrText)) { qrTextPrefilled = true; return; }

            QrText = PickupPreset;
            qrTextPrefilled = true;
        }

        /// <summary>
        /// Proč armování (ne)pokračuje. Skládá se to na tři části, protože mise se může zaseknout
        /// ze <b>tří různých důvodů</b> a jen jeden z nich je „počkej ještě":
        /// nedorazil fix vůbec / fix nesplňuje kritéria (družice, HDOP) / fixy jsou příliš rozházené.
        /// </summary>
        private void ApplyFixQuality(MissionMsg m, CultureInfo ci)
        {
            if (!m.HasFixInfo)
            {
                FixQualityText = "Žádný fix z GPS ještě nedorazil.";
                IsFixBlocking = true;
                return;
            }

            if (!m.FixQualityOk)
            {
                FixQualityText = string.Format(
                    ci, "Fix nesplňuje kritéria: družic {0}, HDOP {1:F1}. Okno se počítá od začátku.",
                    m.FixSatellites, m.FixHdop);
                IsFixBlocking = true;
                return;
            }

            // Rozptyl přes limit = „čekej dál"; pod limitem se jen sbírá okno.
            bool spreadOver = m.FixSpreadM > m.FixSpreadLimitM;
            FixQualityText = string.Format(
                ci, "Družic {0}, HDOP {1:F1}. Rozptyl {2:F2} m z {3} vzorků (limit {4:F2} m){5}",
                m.FixSatellites, m.FixHdop, m.FixSpreadM, m.FixSamples, m.FixSpreadLimitM,
                spreadOver ? " — PŘES LIMIT, okno se zahazuje a začíná znovu." : ".");
            IsFixBlocking = spreadOver;
        }

        /// <summary>
        /// Co smí obsluha zmáčknout. Bez živé mise <b>nic</b> — tlačítko, které tiše nic nedělá, je
        /// horší než zakázané.
        /// </summary>
        private void UpdateCommandAvailability(MissionMsg m)
        {
            // Mise se hleda ZNOVU pri kazde aktualizaci - vznika teprve pri Run, takze panel
            // otevreny driv ji na zacatku nema (viz Mission).
            if (Mission == null)
            {
                CanStart = CanAbort = false;
                NoMissionText =
                    "Misi zatim nejde ovládat. Buď ještě neběží Runtime (spusť Run), nebo ji "
                    + "aplikace nemá založenou (chybí mission=robotour, nebo mapa map=). "
                    + "Při přehrávání záznamu je to normální: panel ukazuje zaznamenaný průběh, "
                    + "ovládat se nedá.";
                return;
            }

            NoMissionText = string.Empty;

            var phase = m == null ? RobotourPhase.Idle : (RobotourPhase)m.Phase;
            bool finished = phase == RobotourPhase.Finished || phase == RobotourPhase.Aborted;

            // Start je jediny prikaz, ktery misi POSOUVA. Potvrzovani zaniklo — robot ma ukol
            // vykonat bez zasahu operatora a jedine lidske vstupy jsou QR kod a stop tlacitko
            // (viz RobotourMission.AcceptTarget). Prerusit zustava jako bezpecnostni zasah.
            CanStart = phase == RobotourPhase.Idle;
            CanAbort = !finished && phase != RobotourPhase.Idle;
        }

        private static bool CodeExpected(RobotourStop s)
            => s == RobotourStop.Depot || s == RobotourStop.Pickup;

        private static bool IsServiceWindow(RobotourPhase p)
            => p == RobotourPhase.AwaitingEStop
               || p == RobotourPhase.Servicing
               || p == RobotourPhase.AwaitingEStopRelease;

        private static string PhaseName(RobotourPhase p) => p switch
        {
            RobotourPhase.Idle => "Nečinná",
            RobotourPhase.ArmingAtDepot => "Armování v depu",
            RobotourPhase.AwaitingEStop => "Čeká na nouzové zastavení",
            RobotourPhase.Servicing => "Servisní okno",
            RobotourPhase.AwaitingEStopRelease => "Čeká na uvolnění stopu",
            RobotourPhase.DrivingToPickup => "Jede na nakládku",
            RobotourPhase.DrivingToDrop => "Jede na vykládku",
            RobotourPhase.DrivingToDepot => "Vrací se do depa",
            RobotourPhase.Finished => "Hotovo",
            RobotourPhase.Aborted => "PŘERUŠENO",
            _ => p.ToString(),
        };

        private static string StopName(RobotourStop s) => s switch
        {
            RobotourStop.Depot => "depo (čte se kód nakládky)",
            RobotourStop.Pickup => "nakládka (čte se kód vykládky)",
            RobotourStop.Drop => "vykládka (kód se nečte)",
            _ => s.ToString(),
        };

        /// <summary>
        /// <b>Na co mise čeká.</b> Musí to být vidět, protože nouzové zastavení je signál mise jen
        /// ve stavech, které na něj čekají — obsluha, která ho zmáčkne za jízdy, by jinak čekala,
        /// že tím něco odemkla (viz doc/robotour-mission.md).
        /// </summary>
        private static string WaitingFor(RobotourPhase p, RobotourStop stop, MissionMsg m) => p switch
        {
            RobotourPhase.Idle => "na „Start mise“ z tohoto panelu",
            RobotourPhase.ArmingAtDepot => "na kvalitní fix GPS (pak se jím inicializuje fúze a zapamatuje depo)",
            RobotourPhase.AwaitingEStop => "až obsluha ZMÁČKNE nouzové zastavení",
            RobotourPhase.Servicing =>
                CodeExpected(stop)
                    ? (m.HasAcceptedCode ? "na potvrzení přečteného cíle" : "na přečtení QR kódu (skenuje se)")
                    : "na potvrzení, že je vyloženo",
            RobotourPhase.AwaitingEStopRelease => "až obsluha UVOLNÍ nouzové zastavení",
            RobotourPhase.DrivingToPickup or RobotourPhase.DrivingToDrop or RobotourPhase.DrivingToDepot
                => "na dojezd k cíli (nouzové zastavení za jízdy misi neposune)",
            RobotourPhase.Finished => "nic, mise je hotová",
            RobotourPhase.Aborted => "na zásah obsluhy",
            _ => "-",
        };

        private static string Position(bool has, double latDeg, double lonDeg, CultureInfo ci)
            => has ? string.Format(ci, "{0:F6}° , {1:F6}°", latDeg, lonDeg) : "-";

        private static string Target(bool has, double latDeg, double lonDeg, string code, CultureInfo ci)
            => has
               ? string.Format(ci, "{0:F6}° , {1:F6}°   ({2})", latDeg, lonDeg,
                               string.IsNullOrEmpty(code) ? "bez kódu" : code)
               : "-";

        public override bool OnClose()
        {
            Dispose();
            return base.OnClose();
        }

        public void Dispose()
        {
            // Postavenou desku je potreba uklidit - jinak by QR kod zustal ve scene i po zavreni
            // panelu a nikdo by uz nemel jak ho odebrat.
            try { RemoveQr(); } catch { /* scena uz mohla zmizet */ }

            try { feed?.Dispose(); } catch { /* Stream uz mohl skoncit */ }
            feed = null;
        }
    }
}
