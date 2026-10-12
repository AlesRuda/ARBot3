using System;
using System.Threading;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;
using ARBot.Common.Fusion;
using ARBot.Common.Runtime;
using System.Threading.Tasks;
using ARBot.Common.Vision;
using ARBot.HAL;
using ARBot.Robot;
using NUnit.Framework;

namespace ARBot.Runtime.Tests
{
    /// <summary>
    /// Drzeni po zotaveni kamer trva do prvniho snimku kterekoli kamery
    /// (<see cref="PrvniSnimekKamery"/>, prov-zotaveni-kamer-slepy-po-uvolneni).
    /// </summary>
    public class PrvniSnimekKameryTests
    {
        /// <summary>Kamera, ktera umi jen vyvolat udalost snimku.</summary>
        private sealed class FalesnaKamera : ICamera
        {
            public FalesnaKamera(string jmeno) { Name = jmeno; }
            public event EventHandler<CameraFrame> MeasurementArived;
            public void Posli() => MeasurementArived?.Invoke(this, new CameraFrame { Name = Name });
            public int Odberatelu => MeasurementArived?.GetInvocationList().Length ?? 0;
            public string Name { get; }
            public bool IsError => false;
            public CameraSettings RGBSettings => null;
            public CameraSettings DepthSettings => null;
            public ICameraFrameProcessor FrameProcessor { get; set; }
            public Func<DateTime, RobotState> EstimatedPoseAt { get; set; }
            public bool Init(CameraSettings rgbSettings, CameraSettings depthSettings) => true;
            public CameraFrame GetLastMeasurement() => null;
            public ICameraProjection CreateProjector() => null;
            public IDepthCameraProjection CreateDepthProjector() => null;
        }

        [Test]
        public void SnimekPredOstrym_SeNepocita()
        {
            var leva = new FalesnaKamera("Left");
            using var cekani = new PrvniSnimekKamery(new ICamera[] { leva });

            leva.Posli();                                  // zdrava kamera pred uvolnenim (stary kontext)
            cekani.Ostre();

            Assert.That(cekani.Cekej(TimeSpan.FromMilliseconds(50)), Is.False);
            Assert.That(cekani.Kamera, Is.Null);
        }

        [Test]
        public void PrvniSnimekKterekoliKamery_UkonciCekani()
        {
            var leva = new FalesnaKamera("Left");
            var prava = new FalesnaKamera("Right");
            using var cekani = new PrvniSnimekKamery(new ICamera[] { leva, prava });
            cekani.Ostre();

            var vlakno = new Thread(() => { Thread.Sleep(50); prava.Posli(); });
            vlakno.Start();
            bool prisel = cekani.Cekej(TimeSpan.FromSeconds(5));
            vlakno.Join();

            Assert.That(prisel, Is.True);
            Assert.That(cekani.Kamera, Is.EqualTo("Right"), "staci kterakoli kamera");
        }

        [Test]
        public void BezSnimku_VyprsiTimeout()
        {
            var leva = new FalesnaKamera("Left");
            using var cekani = new PrvniSnimekKamery(new ICamera[] { leva });
            cekani.Ostre();

            Assert.That(cekani.Cekej(TimeSpan.FromMilliseconds(50)), Is.False);
        }

        [Test]
        public void ZruseniUkonciCekani()
        {
            var leva = new FalesnaKamera("Left");
            using var cekani = new PrvniSnimekKamery(new ICamera[] { leva });
            using var konec = new ManualResetEventSlim(true);
            cekani.Ostre();

            var t0 = DateTime.UtcNow;
            bool prisel = cekani.Cekej(TimeSpan.FromSeconds(30), konec.WaitHandle);

            Assert.That(prisel, Is.False);
            Assert.That(DateTime.UtcNow - t0, Is.LessThan(TimeSpan.FromSeconds(5)), "zruseni neceka na timeout");
        }

        [Test]
        public void BezKamer_NeniNaCoCekat()
        {
            using var cekani = new PrvniSnimekKamery(new ICamera[] { null });
            cekani.Ostre();

            Assert.That(cekani.PocetKamer, Is.EqualTo(0));
            Assert.That(cekani.Cekej(TimeSpan.FromSeconds(30)), Is.False);
        }

        // ---------------- poradi kroku supervizoru (CameraRecoverySupervisor.ZotavPodDrzenim) ----------------

        /// <summary>
        /// Drzeni trva PRES zotaveni az do prvniho snimku z noveho kontextu. Snimek, ktery zdrava kamera
        /// posle jeste behem zotaveni (stary kontext), drzeni pustit nesmi - kdyby se snimky pocitaly
        /// uz pred zotavenim, pustilo by se hned (nalez revize 12. 10. 2026: poradi nic nehlidalo).
        /// </summary>
        [Test]
        public void Supervizor_DrziDoPrvnihoSnimkuPoZotaveni_StarySnimekNepocita()
        {
            var drzeni = new DriveHoldRegistry();
            var leva = new FalesnaKamera("Left");
            var prava = new FalesnaKamera("Right");
            using var konec = new ManualResetEventSlim(false);
            using var zotaveno = new ManualResetEventSlim(false);

            var beh = Task.Run(() => CameraRecoverySupervisor.ZotavPodDrzenim(
                drzeni, "test", new ICamera[] { leva, prava },
                () => { leva.Posli(); zotaveno.Set(); },          // snimek ze STAREHO kontextu behem zotaveni
                konec.WaitHandle, TimeSpan.Zero, TimeSpan.FromSeconds(10), out _));

            Assert.That(zotaveno.Wait(TimeSpan.FromSeconds(5)), Is.True, "zotaveni probehlo");
            Thread.Sleep(150);
            Assert.That(beh.IsCompleted, Is.False, "bez snimku z noveho kontextu se drzi dal");
            Assert.That(drzeni.IsHeld, Is.True);

            prava.Posli();                                       // prvni snimek po zotaveni
            Assert.That(beh.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(beh.Result, Is.True);
            Assert.That(drzeni.IsHeld, Is.False, "po prvnim snimku se drzeni uvolni");
        }

        [Test]
        public void Supervizor_BezSnimku_PoTimeoutuPusti()
        {
            var drzeni = new DriveHoldRegistry();
            var leva = new FalesnaKamera("Left");
            using var konec = new ManualResetEventSlim(false);

            bool hotovo = CameraRecoverySupervisor.ZotavPodDrzenim(
                drzeni, "test", new ICamera[] { leva }, () => { },
                konec.WaitHandle, TimeSpan.Zero, TimeSpan.FromMilliseconds(100), out bool prisel);

            Assert.That(hotovo, Is.True);
            Assert.That(prisel, Is.False, "snimek neprisel");
            Assert.That(drzeni.IsHeld, Is.False);
        }

        /// <summary>
        /// Vyjimka ze zotaveni (novy kontext nejde zalozit) nesmi preskocit cekani na snimek - kamery
        /// se pripojuji dal a robot by byl slepy mimo drzeni (nalez revize 12. 10. 2026).
        /// </summary>
        [Test]
        public void Supervizor_VyjimkaZeZotaveni_PresToDrziDoSnimku()
        {
            var drzeni = new DriveHoldRegistry();
            var prava = new FalesnaKamera("Right");
            using var konec = new ManualResetEventSlim(false);
            using var zotaveno = new ManualResetEventSlim(false);
            bool prisel = false;

            var beh = Task.Run(() => CameraRecoverySupervisor.ZotavPodDrzenim(
                drzeni, "test", new ICamera[] { prava },
                () => { zotaveno.Set(); throw new InvalidOperationException("novy kontext nejde zalozit"); },
                konec.WaitHandle, TimeSpan.Zero, TimeSpan.FromSeconds(10), out prisel));

            Assert.That(zotaveno.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Thread.Sleep(150);
            Assert.That(beh.IsCompleted, Is.False, "i po vyjimce se drzi do snimku");
            Assert.That(drzeni.IsHeld, Is.True);

            prava.Posli();
            Assert.That(beh.Wait(TimeSpan.FromSeconds(5)), Is.True);
            Assert.That(beh.Result, Is.True);
            Assert.That(prisel, Is.True);
            Assert.That(drzeni.IsHeld, Is.False);
        }

        [Test]
        public void Supervizor_Konec_PreruseniUvolniDrzeni()
        {
            var drzeni = new DriveHoldRegistry();
            var leva = new FalesnaKamera("Left");
            using var konec = new ManualResetEventSlim(false);

            var beh = Task.Run(() => CameraRecoverySupervisor.ZotavPodDrzenim(
                drzeni, "test", new ICamera[] { leva }, () => { },
                konec.WaitHandle, TimeSpan.Zero, TimeSpan.FromSeconds(30), out _));
            Thread.Sleep(100);
            konec.Set();

            Assert.That(beh.Wait(TimeSpan.FromSeconds(5)), Is.True, "konec neceka na timeout");
            Assert.That(beh.Result, Is.False);
            Assert.That(drzeni.IsHeld, Is.False);
            Assert.That(leva.Odberatelu, Is.EqualTo(0), "odhlaseni od kamer i pri preruseni");
        }

        [Test]
        public void Dispose_OdhlasiOdKamer_ASnimekPakNevadi()
        {
            var leva = new FalesnaKamera("Left");
            var cekani = new PrvniSnimekKamery(new ICamera[] { leva });
            Assert.That(leva.Odberatelu, Is.EqualTo(1));

            cekani.Dispose();

            Assert.That(leva.Odberatelu, Is.EqualTo(0));
            Assert.DoesNotThrow(() => leva.Posli());
        }
    }
}
