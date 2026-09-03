using ARBot.Common.Devices;
using ARBot.Common.Models;
using ARBot.HAL;
using Intel.RealSense;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;

namespace ARBot.HAL.Devices.Camera
{
    /// <summary>
    /// Ovladac sledovaci kamery Intel RealSense T265 (6DOF pose / IMU) pro Armbian/ARM64.
    /// Funkcne shodny s HALWindows variantou, ale linkuje managed wrapper Intel.RealSense 2.53
    /// (odpovida native librealsense2.so 2.53 na Orange Pi). Nacteni nativni knihovny resi
    /// <see cref="RealSenseNativeResolver"/>.
    /// Dedi ze SensorBase: po Start bezi na pozadi task ctouci pose snimky z pipeline;
    /// posledni stav je dostupny pres GetLastMeasurement a udalost MeasurementArived.
    /// <para>
    /// Ovladac je odolny proti odpojeni a pripojeni kamery za behu: pipeline se
    /// nezaklada v konstruktoru, ale az v pozadi smycce, jakmile je zarizeni k dispozici.
    /// Vypadek za behu (odpojeni) se detekuje pri cteni snimku, pipeline se zbouri a smycka
    /// se v intervalu <see cref="ReconnectPeriodMs"/> pokousi o opetovne pripojeni. Dokud
    /// kamera neni pripojena, <see cref="IsError"/> vraci true a smycka nezahlcuje CPU.
    /// </para>
    /// <para><b>Diagnostika jde do <see cref="System.Diagnostics.Trace"/>, ne do <c>Debug</c>.</b>
    /// <c>Debug.WriteLine</c> je <c>[Conditional("DEBUG")]</c>, takze v Release buildu - a prave
    /// ten bezi na zarizeni - nezustane po poruche ZADNA stopa. Stalo se to 2. 9. 2026: kamery
    /// se neohlasily a v panelu Debug output nebyl o nich ANI RADEK, takze se pricina hledala
    /// hodinu merenim zvenci, misto aby ji driver rovnou napsal (byla to nesplnitelna kombinace
    /// hloubky a barvy na USB 2.0). Tataz past uz jednou byla opravena u hlasky o zahozenem
    /// merenii ve fuzi - viz <c>AsyncFusionEngine.Enqueue</c>. Hlaseni jsou jednorazova
    /// (pripojeni, odpojeni) nebo throtlovana, takze proud nezaplavi.
    /// Hlida to <c>DiagnostikaSenzoruTests</c>.</para>
    /// <para><b>Sdileny kontext a boot (3. 9. 2026):</b> kontext je jeden pro vsechny RealSense
    /// drivery a dotazy na zarizeni jdou pod jednim zamkem (<see cref="RealSenseShared"/>).
    /// Konstruktor navic T265 <b>synchronne nabootuje</b> driv, nez <c>ARBotHW.SetRealHW</c>
    /// zalozi D435 - boot pod rukama streamujicich pipeline byl pricinou SIGSEGV v
    /// <c>tm_boot</c> i kamery bez pozy. Detaily a rozbor minidumpu: RealSenseShared,
    /// doc/devlog.md 3. 9. 2026.</para>
    /// </summary>
    public sealed class T265TrackingCamera : SensorBase<IMUState>, IIMU
    {
        /// <summary>Seriove cislo zarizeni; null = prvni dostupna T265.</summary>
        string sn;

        /// <summary>Interval opakovanych pokusu o pripojeni, kdyz kamera chybi [ms].</summary>
        private const int ReconnectPeriodMs = 1000;
        /// <summary>Timeout cekani na pose snimek [ms]. Kratky, aby smycka rychle reagovala
        /// na Stop() i na odpojeni kamery.</summary>
        private const uint FrameTimeoutMs = 1000;

        /// <summary>
        /// Po kolika po sobe jdoucich timeoutech bez pozy se pipeline zbourá a nastartuje znovu
        /// (stejny mechanismus jako u D435). Vyssi nez u D435: T265 po startu pipeline potrebuje
        /// par sekund, nez VIO rozjede a prijde prvni poza; prilis agresivni restart by se zacyklil.
        /// Drive se pri PRVNIM timeoutu volal QueryDevices a jeho selhani ("failed to set power
        /// state") se bralo jako odpojeni - odtud smycka "pipeline pripojena / kamera odpojena"
        /// v zaznamech z 3. 9. 2026, ve ktere poza nikdy neprisla.
        /// </summary>
        private const int StallTimeoutsBeforeRestart = 5;

        /// <summary>Pocet po sobe jdoucich timeoutu; 0 = posledni cteni snimek doslo.</summary>
        private int consecutiveTimeouts;

        /// <summary>Kolikrat se pipeline restartovala kvuli zaseknutemu streamu (diagnostika).</summary>
        public int StallRestarts { get; private set; }

        /// <summary>
        /// Po kolika restartech pipeline PO SOBE (bez jedine pozy mezi nimi) se kamere posle
        /// hardware reset. Restart pipeline neresi zaseknuty firmware; reset ano (3. 9. 2026).
        /// </summary>
        private const int StallRestartsBeforeHardwareReset = 3;

        /// <summary>
        /// Nejkratsi odstup dvou hardware resetu [s]. Kdyz poza nechodi z duvodu, ktery reset
        /// nespravi (tma - firmware hlasi SLAM_ERROR Vision), nesmi se kamera resetovat dokola:
        /// kazdy reset je ~5 s vypadku i pro gyro/accel a nahravani firmware.
        /// </summary>
        private const int HardwareResetMinIntervalSeconds = 60;

        /// <summary>Restarty pipeline po sobe bez pozy; nuluje se s kazdou prijatou pozou.</summary>
        private int consecutiveStallRestarts;

        private DateTime lastHardwareReset = DateTime.MinValue;

        /// <summary>Kolikrat se kamere poslal hardware reset (diagnostika).</summary>
        public int HardwareResets { get; private set; }

        /// <summary>Nejdelsi cekani na boot T265 v konstruktoru [s].</summary>
        private const int BootTimeoutSeconds = 10;

        private Pipeline pipeline;
        private PipelineProfile pipelineProfile;

        /// <summary>true = pipeline bezi a streamuje z pripojene kamery.</summary>
        private volatile bool connected = false;

        /// <summary>Prvni dostupna T265.</summary>
        public T265TrackingCamera() : this(null)
        {
        }

        /// <summary>
        /// Kamera dle serioveho cisla. Spusti snimaci smycku; ta se ke kamere pripoji,
        /// jakmile je k dispozici (kamera nemusi byt pripojena uz pri vytvoreni).
        /// </summary>
        /// <param name="sn">Seriove cislo zarizeni; null = prvni dostupne.</param>
        public T265TrackingCamera(string sn)
        {
            this.sn = sn;

            // Boot T265 JEDNOU a synchronne, jeste pred startem smycky - a protoze ARBotHW zaklada
            // T265 jako prvni kameru, i pred pipeline obou D435. Bez pripojene kamery se vrati
            // hned (jeden dotaz); s nenabootovanym Movidiem ceka, nez se prehlasi (typicky 3-5 s).
            RealSenseShared.BootT265(Name, TimeSpan.FromSeconds(BootTimeoutSeconds));

            Start();
        }

        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public override string Name => $"T265 {sn}";

        /// <summary>
        /// Chybovy stav: krome chyby zpracovani (base) je za chybu povazovano i odpojeni
        /// kamery (dokud neni pipeline pripojena).
        /// </summary>
        public override bool IsError => !connected || base.IsError;

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro rotacni vektory.
        /// </summary>
        private Vector3 Angular2Vector3D(Intel.RealSense.Math.Vector v)
        {
            return new Vector3(-v.x, v.z, v.y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro pozicni vektory.
        /// </summary>
        private Vector3 Translation2Vector3D(Intel.RealSense.Math.Vector v)
        {
            return new Vector3(v.x, -v.z, v.y);
        }

        /// <summary>
        /// Otaci souradnicovy system (x roste na vychod, y roste na sever a z nahoru) pro rotaci.
        /// </summary>
        private Quaternion ToQuaternion(Intel.RealSense.Math.Quaternion v)
        {
            return new Quaternion(v.x, -v.z, v.y, v.w);
        }

        /// <summary>
        /// Prevede tracker_confidence (0-3) z T265 na normalizovanou duveru 0-1.
        /// </summary>
        private double ToConfidence(uint v)
        {
            if (v == 1)
                return 0.33;
            if (v == 2)
                return 0.66;
            if (v == 3)
                return 1;

            return 0;
        }

        /// <summary>
        /// Zjisti, zda je pozadovana kamera fyzicky pripojena. Pri sn==null hleda libovolnou
        /// T265 (podle nazvu), jinak podle serioveho cisla.
        /// </summary>
        /// <returns>true = je; false = dotaz prosel a T265 mezi zarizenimi neni; null = dotaz sam
        /// selhal (to neni dukaz odpojeni - viz <see cref="RealSenseShared.Query"/>).</returns>
        private bool? DevicePresent()
            => RealSenseShared.Query(Name, RealSenseShared.BySerialOrName(sn, "T265"));

        /// <summary>
        /// Zajisti pripojenou a bezici pipeline. Pokud kamera chybi nebo se start nezdari,
        /// vrati false (a pipeline necha zbouranou pro dalsi pokus).
        /// </summary>
        private bool EnsureConnected()
        {
            if (connected)
                return true;

            // != true: kdyz se pritomnost nepodarilo zjistit (null), radeji pockame na dalsi
            // pokus, nez abychom slepe startovali pipeline.
            if (DevicePresent() != true)
                return false;

            try
            {
                var cfg = new Config();
                if (sn != null)
                    cfg.EnableDevice(sn);
                cfg.EnableStream(Stream.Pose, Format.SixDOF);

                // Po odpojeni je pipeline zbourana (Teardown) - vzdy tvorime cerstvou instanci
                // ze sdileneho kontextu; znovupouzita pipeline se na nove zarizeni nenavaze.
                if (pipeline == null)
                    pipeline = new Pipeline(RealSenseShared.Context);

                pipelineProfile = pipeline.Start(cfg);
                connected = true;
                Trace.WriteLine($"{Name}: pipeline pripojena.");
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{Name}: pripojeni pipeline selhalo: {ex.Message}");
                Teardown();

                // "T265 is running!" / "Device is busy": kamera si mysli, ze streamuje - predchozi
                // klient ji opustil bez Stop (pad procesu). Ze stavu ji dostane jen hardware reset.
                string m = ex.Message ?? string.Empty;
                if (m.IndexOf("running", StringComparison.OrdinalIgnoreCase) >= 0
                    || m.IndexOf("busy", StringComparison.OrdinalIgnoreCase) >= 0)
                    TryHardwareReset("start pipeline selhal na zaseknutem zarizeni");
                return false;
            }
        }

        /// <summary>
        /// Posle kamere hardware reset (pres <see cref="RealSenseShared.HardwareResetT265"/>), ne
        /// casteji nez jednou za <see cref="HardwareResetMinIntervalSeconds"/>. Pipeline musi byt
        /// v tu chvili uz zbourana (reset zneplatni handle).
        /// </summary>
        private void TryHardwareReset(string reason)
        {
            var since = DateTime.UtcNow - lastHardwareReset;
            if (since.TotalSeconds < HardwareResetMinIntervalSeconds)
            {
                Trace.WriteLine($"{Name}: hardware reset by pomohl ({reason}), ale posledni byl pred "
                                + $"{since.TotalSeconds:F0} s - cekam (min. odstup {HardwareResetMinIntervalSeconds} s).");
                return;
            }
            lastHardwareReset = DateTime.UtcNow;
            HardwareResets++;
            consecutiveStallRestarts = 0;
            Trace.WriteLine($"{Name}: hardware reset #{HardwareResets} - {reason}.");
            bool back = RealSenseShared.HardwareResetT265(Name, TimeSpan.FromSeconds(BootTimeoutSeconds + 5));
            Trace.WriteLine(back ? $"{Name}: po resetu je kamera zpet." : $"{Name}: po resetu se kamera nevratila.");
        }

        /// <summary>
        /// Zastavi a UVOLNI pipeline a oznaci odpojeno. Pipeline se zahazuje (ne jen Stop),
        /// protoze po odpojeni USB se stara instance na znovupripojene zarizeni nenavaze -
        /// pri reconnectu se v EnsureConnected vytvori cerstva.
        /// </summary>
        private void Teardown()
        {
            connected = false;
            pipelineProfile = null;
            if (pipeline != null)
            {
                try
                {
                    pipeline.Stop();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"{Name}: Stop pipeline: {ex.Message}");
                }
                try
                {
                    pipeline.Dispose();
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"{Name}: Dispose pipeline: {ex.Message}");
                }
                pipeline = null;
            }
        }

        /// <summary>
        /// Pocka na dalsi pose snimek z pipeline a vrati ho jako IMUState (nebo null, kdyz
        /// snimek nedorazi / kamera je odpojena). Bookkeeping (FrameNum/periody) doplni SensorBase.
        /// </summary>
        /// <remarks>
        /// POZOR na frame: T265 nema magnetometr, takze Rotation/Translation/Velocity jsou ve
        /// vlastnim VIO framu - pocatek pri startu, gravitacne zarovnany (pitch/roll absolutni),
        /// ale yaw a poloha jen RELATIVNI (bez severu, NENI ENU). Viz remarks u IMUState.
        /// </remarks>
        protected override IMUState GetMeasurement()
        {
            // (Re)pripojeni: kdyz kamera chybi, nezahlcovat CPU a vratit null (bez udalosti).
            if (!EnsureConnected())
            {
                Thread.Sleep(ReconnectPeriodMs);
                return null;
            }

            try
            {
                if (pipeline.TryWaitForFrames(out var frames, FrameTimeoutMs))
                {
                    using (frames)
                    using (var pf = frames.PoseFrame)
                    {
                        consecutiveTimeouts = 0;
                        consecutiveStallRestarts = 0;
                        var f = pf.PoseData;
                        DateTime ts = D435Camera.CalcTimeStamp(pf.Timestamp);
                        return new IMUState()
                        {
                            Translation = Translation2Vector3D(f.translation),
                            Velocity = Translation2Vector3D(f.velocity),
                            Acceleration = Translation2Vector3D(f.acceleration),
                            Rotation = ToQuaternion(f.rotation),
                            AngularVelocity = Angular2Vector3D(f.angular_velocity),
                            AngularAcceleration = Angular2Vector3D(f.angular_acceleration),
                            TimeStamp = ts,
                            Confidence = ToConfidence(f.tracker_confidence)
                        };
                    }
                }

                // Timeout bez pozy: odpojeni se u T265 casto NEprojevi vyjimkou, jen prestanou
                // chodit snimky. Jeden timeout ale nic neznamena (VIO po startu nabiha par sekund)
                // a na sbernici se pri nem NEPTAME - dotaz nad bezicimi streamy umi selhat a jeho
                // selhani neni odpojeni. Az po prahu se pipeline zbourá (jinak by connected zustalo
                // true, IsError by hlasil OK a kamera by mlcela navzdy) a na sbernici se ptame jen
                // kvuli spravnemu hlaseni.
                if (++consecutiveTimeouts < StallTimeoutsBeforeRestart)
                    return null;

                bool? present = DevicePresent();
                Teardown();
                consecutiveTimeouts = 0;
                if (present == false)
                    Trace.WriteLine($"{Name}: kamera odpojena (bez pozy a zarizeni na sbernici chybi).");
                else
                {
                    StallRestarts++;
                    consecutiveStallRestarts++;
                    Trace.WriteLine($"{Name}: {StallTimeoutsBeforeRestart} s bez pozy, kamera "
                                    + (present == true ? "je" : "asi je") + " na sbernici - restart pipeline"
                                    + $" (celkem {StallRestarts}, po sobe {consecutiveStallRestarts}).");

                    // Restart pipeline zaseknuty firmware nespravi - po nekolika marnych po sobe
                    // se zkusi hardware reset. Kdyz poza nechodi kvuli tme (SLAM_ERROR Vision),
                    // reset nepomuze a rate limit drzi kameru v klidu.
                    if (consecutiveStallRestarts >= StallRestartsBeforeHardwareReset)
                        TryHardwareReset($"{consecutiveStallRestarts} restarty pipeline po sobe bez pozy");
                }
                return null;
            }
            catch (Exception ex)
            {
                // Tvrdy vypadek za behu (odpojeni kamery) -> zbourat pipeline, priste se zkusi reconnect.
                Trace.WriteLine($"{Name}: cteni snimku selhalo (odpojeno?): {ex.Message}");
                Teardown();
                return null;
            }
        }

        /// <summary>
        /// Zastavi pozadi task (base) a uvolni pipeline + kontext (nativni prostredky kamery).
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);   // zastavi a pocka na dokonceni pozadi smycky
            if (disposing)
            {
                Teardown();   // zastavi a uvolni pipeline (sdileny kontext se nedisposuje)
            }
        }
    }
}
