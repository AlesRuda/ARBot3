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
    /// Ovladac sledovaci kamery Intel RealSense T265 (6DOF pose / IMU).
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

        /// <summary>Kontext pro zjisteni pritomnosti zarizeni (detekce (od|při)pojeni).</summary>
        private readonly Context ctx = new Context();

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
        private bool DevicePresent()
        {
            try
            {
                using (var devices = ctx.QueryDevices())
                {
                    foreach (var d in devices)
                    {
                        using (d)
                        {
                            if (sn != null)
                            {
                                if (d.Info[CameraInfo.SerialNumber] == sn)
                                    return true;
                            }
                            else
                            {
                                var name = d.Info[CameraInfo.Name];
                                if (name != null && name.IndexOf("T265", StringComparison.OrdinalIgnoreCase) >= 0)
                                    return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{Name}: QueryDevices selhalo: {ex.Message}");
            }
            return false;
        }

        /// <summary>
        /// Zajisti pripojenou a bezici pipeline. Pokud kamera chybi nebo se start nezdari,
        /// vrati false (a pipeline necha zbouranou pro dalsi pokus).
        /// </summary>
        private bool EnsureConnected()
        {
            if (connected)
                return true;

            if (!DevicePresent())
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
                    pipeline = new Pipeline(ctx);

                pipelineProfile = pipeline.Start(cfg);
                connected = true;
                Trace.WriteLine($"{Name}: pipeline pripojena.");
                return true;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"{Name}: pripojeni pipeline selhalo: {ex.Message}");
                Teardown();
                return false;
            }
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
                        var f = pf.PoseData;
                        DateTime ts = D435Camera.CalcTimeStamp(pf.Timestamp);
                        return new IMUState()
                        {
                            Name = Name,   // puvodce mereni - v robotovi muze byt IMU vic (viz IMUState.Name)
                            // T265 nema magnetometr: yaw je relativni k orientaci pri startu
                            // pipeline, tedy o neznamou konstantu vedle. Bez tohohle priznaku by ho
                            // fuze vzala jako KURZ a vnutila si libovolne otoceny svet.
                            HasAbsoluteHeading = false,
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

                // Timeout bez snimku: odpojeni se u T265 casto NEprojevi vyjimkou, jen prestanou
                // chodit snimky. Overit fyzickou pritomnost - kdyz kamera zmizela, zbourat pipeline
                // (jinak by connected zustalo true, IsError by hlasil OK a reconnect by se nespustil).
                if (!DevicePresent())
                {
                    Trace.WriteLine($"{Name}: kamera odpojena (timeout + zarizeni chybi).");
                    Teardown();
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

        /* STARA IMPLEMENTACE - ponechana do overeni na zarizeni (viz CLAUDE.md:
           "pri prepisech nemazat starou implementaci, dokud novou nepotvrdi testy").
           Puvodni Init konfiguroval a spoustel pipeline synchronne v konstruktoru a
           nezvladal odpojeni/pripojeni za behu (pri odpojeni WaitForFrames vyhazoval
           vyjimku v tesne smycce a pipeline se uz neobnovila).

        private void Init()
        {
            if (IsRunning)
                Stop();

            var cfg = new Config();
            if (sn != null)
                cfg.EnableDevice(sn);
            cfg.EnableStream(Stream.Pose, Format.SixDOF);

            if (pipeline == null)
                pipeline = new Pipeline();
            else
                pipeline.Stop();

            pipelineProfile = pipeline.Start(cfg);

            Start();
        }
        */

        /// <summary>
        /// Zastavi pozadi task (base) a uvolni pipeline + kontext (nativni prostredky kamery).
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);   // zastavi a pocka na dokonceni pozadi smycky
            if (disposing)
            {
                Teardown();   // zastavi a uvolni pipeline
                ctx?.Dispose();
            }
        }
    }
}
