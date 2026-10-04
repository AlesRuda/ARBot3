using System;
using System.Collections.Generic;
using System.IO;
using ARBot.Common.Common;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;
using NUnit.Framework;

namespace ARBot.Common.Tests.Fusion
{
    /// <summary>
    /// Odometricka poza ve fuzi (<see cref="OdomPose"/>, faze 1 tematu
    /// <c>lp-grid-odometricka-soustava</c>): vedlejsi deterministicky integrator fuzovanych
    /// rychlosti v checkpointech <see cref="AsyncFusionEngine"/>, mimo kovarianci a mimo update.
    /// Testy hlidaji to, kvuli cemu existuje — korekce polohy a kurzu do ni NESKACOU — a ze se
    /// v okne historie chova stejne jako globalni stav (out-of-sequence, Prune, inicializace).
    /// </summary>
    [TestFixture]
    public class OdomPoseTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

        private const double V = 1.0;       // m/s
        private const double W = 0.2;       // rad/s
        private const double Dt = 0.05;     // s, 20 Hz rychlosti

        /// <summary>Rychlost z kol a gyro po 50 ms od <paramref name="from"/> do <paramref name="to"/> [s].</summary>
        private static IEnumerable<IMeasurement> Rychlosti(double from, double to)
        {
            for (int k = (int)Math.Round(from / Dt); k * Dt <= to + 1e-9; k++)
            {
                var t = T0.AddSeconds(k * Dt);
                yield return ScalarStateMeasurement.Velocity(V, 0.02, t, "Odo");
                yield return ScalarStateMeasurement.AngularRate(W, 0.01, t, "Gyro");
            }
        }

        private static AsyncFusionEngine Engine(double windowSec = 3.0)
            => new AsyncFusionEngine(new EKFModel(), TimeSpan.FromSeconds(windowSec));

        private static void Enqueue(AsyncFusionEngine e, IEnumerable<IMeasurement> ms)
        {
            foreach (var m in ms) e.Enqueue(m);
        }

        private static double Posun(RobotState a, RobotState b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        private static double PosunOdom(RobotState a, RobotState b)
            => Math.Sqrt((a.OdomX - b.OdomX) * (a.OdomX - b.OdomX) + (a.OdomY - b.OdomY) * (a.OdomY - b.OdomY));

        [Test]
        public void Integrate_JeTentyzVzorecJakoPredikceEkf()
        {
            // Bez korekci musi jit odometricka poza s globalni presne soubezne — jinak by se
            // transformace odom → svet menila i bez jedineho mereni polohy.
            var o = new OdomPose(1, 2, 0.3).Integrate(1.5, 0.4, 0.1);
            double b = 0.3 + 0.4 * 0.1 / 2;
            Assert.Multiple(() =>
            {
                Assert.That(o.X, Is.EqualTo(1 + 1.5 * Math.Cos(b) * 0.1).Within(1e-12));
                Assert.That(o.Y, Is.EqualTo(2 + 1.5 * Math.Sin(b) * 0.1).Within(1e-12));
                Assert.That(o.Theta, Is.EqualTo(0.34).Within(1e-12));
            });
        }

        [Test]
        public void PrvniMereni_ZacinaVPocatku()
        {
            var e = Engine();
            Enqueue(e, Rychlosti(0, 0));
            var s = e.GetStateAt(T0);
            Assert.That((s.OdomX, s.OdomY, s.OdomTheta), Is.EqualTo((0.0, 0.0, 0.0)));
        }

        [Test]
        public void BezKorekci_TransformaceOdomSvetStoji()
        {
            // Jen rychlosti: globalni i odometricka poza se pohybuji stejne, transformace mezi
            // nimi se po usazeni rychlosti nemeni. (V prvnich desetinach sekundy se meni: update
            // rychlosti posune pres korelaci P_xv i globalni polohu, odometrii ne — proto az od 2 s.)
            var e = Engine();
            Enqueue(e, Rychlosti(0, 10));

            var a = e.GetStateAt(T0.AddSeconds(8)).OdomToWorld();
            var b = e.GetStateAt(T0.AddSeconds(10)).OdomToWorld();
            var s = e.GetStateAt(T0.AddSeconds(10));

            Assert.Multiple(() =>
            {
                Assert.That(b.dX, Is.EqualTo(a.dX).Within(1e-3));
                Assert.That(b.dY, Is.EqualTo(a.dY).Within(1e-3));
                Assert.That(b.dTheta, Is.EqualTo(a.dTheta).Within(1e-4));
                Assert.That(s.OdomTheta, Is.EqualTo(Conversions.NormalizeOrientation(s.Theta)).Within(0.05),
                            "bez mereni kurzu se kurz globalni a odometricke pozy rozejde jen o usazovani omega");
            });
        }

        [Test]
        public void SkokPolohy_OdometriiNepohne()
        {
            // Dva behy nad tymiz rychlostmi; B dostane navic GPS fix 5 m vedle. Globalni poza
            // skoci, odometricka se smi lisit jen o to, co korekce protlaci pres rychlosti
            // (P_xv → v), a to dalsi mereni rychlosti hned stahne.
            var a = Engine();
            var b = Engine();
            Enqueue(a, Rychlosti(0, 6));
            Enqueue(b, Rychlosti(0, 6));

            var t = T0.AddSeconds(5.0);
            var pred = b.GetStateAt(t);
            b.Enqueue(new PositionMeasurement(pred.X + 5, pred.Y, 0.3, 0.3, t.AddMilliseconds(1), "GPS"));

            var tKonec = T0.AddSeconds(6);
            var sa = a.GetStateAt(tKonec);
            var sb = b.GetStateAt(tKonec);

            Assert.Multiple(() =>
            {
                Assert.That(Posun(sa, sb), Is.GreaterThan(2.0), "globalni poza ma korekci prevzit");
                Assert.That(PosunOdom(sa, sb), Is.LessThan(0.05), "odometricka poza do korekce skakat nesmi");
                Assert.That(Math.Abs(sb.OdomTheta - sa.OdomTheta), Is.LessThan(1e-3));
            });
        }

        [Test]
        public void SkokKurzu_OdometrickyKurzNepohne()
        {
            var a = Engine();
            var b = Engine();
            Enqueue(a, Rychlosti(0, 6));
            Enqueue(b, Rychlosti(0, 6));

            var t = T0.AddSeconds(5.0);
            var pred = b.GetStateAt(t);
            b.Enqueue(new HeadingMeasurement(pred.Theta + 0.5, 0.02, t.AddMilliseconds(1), "Compass"));

            var tKonec = T0.AddSeconds(6);
            var sa = a.GetStateAt(tKonec);
            var sb = b.GetStateAt(tKonec);

            Assert.Multiple(() =>
            {
                Assert.That(Math.Abs(Conversions.NormalizeOrientation(sb.Theta - sa.Theta)), Is.GreaterThan(0.3),
                            "globalni kurz ma korekci prevzit");
                Assert.That(Math.Abs(Conversions.NormalizeOrientation(sb.OdomTheta - sa.OdomTheta)), Is.LessThan(5e-3),
                            "odometricky kurz do korekce skakat nesmi (otocil by grid kolem robotu)");
                Assert.That(PosunOdom(sa, sb), Is.LessThan(0.05));
            });
        }

        [Test]
        public void InicializacePolohyAKurzu_OdometriiNepreruší()
        {
            // Rozhodnuti autora 4. 10. 2026: inicializace prepise jen globalni pozu, odometrie
            // bezi dal (grid v odometricke soustave se nemaze).
            var e = Engine();
            var r = Engine();
            Enqueue(e, Rychlosti(0, 5));
            Enqueue(r, Rychlosti(0, 10));   // reference bez inicializace

            var t = T0.AddSeconds(5);
            var pred = e.GetStateAt(t);
            e.InitializePosition(500, -300, 1.0, t);
            e.InitializeHeading(2.0, 0.1, t);
            var po = e.GetStateAt(t);

            Assert.Multiple(() =>
            {
                Assert.That(po.X, Is.EqualTo(500).Within(1e-9));
                Assert.That(po.Theta, Is.EqualTo(2.0).Within(1e-9));
                Assert.That(po.OdomX, Is.EqualTo(pred.OdomX).Within(1e-12), "odom X v okamziku inicializace");
                Assert.That(po.OdomY, Is.EqualTo(pred.OdomY).Within(1e-12));
                Assert.That(po.OdomTheta, Is.EqualTo(pred.OdomTheta).Within(1e-12));
            });

            // A pokracuje dal stejnymi rychlostmi jako reference (inicializace nenulovala v, omega).
            Enqueue(e, Rychlosti(5.05, 10));
            var se = e.GetStateAt(T0.AddSeconds(10));
            var sr = r.GetStateAt(T0.AddSeconds(10));
            Assert.That(PosunOdom(se, sr), Is.LessThan(0.02));
            Assert.That(Math.Abs(Conversions.NormalizeOrientation(se.OdomTheta - sr.OdomTheta)), Is.LessThan(1e-3));
        }

        [Test]
        public void InicializacePredPrvnimMerenim_OdometrieZacinaVPocatku()
        {
            var e = Engine();
            e.InitializePosition(100, 200, 1.0, T0.AddSeconds(-0.01));
            Enqueue(e, Rychlosti(0, 1));
            var s0 = e.GetStateAt(T0);
            Assert.That(s0.X, Is.EqualTo(100).Within(0.1));
            Assert.That(Math.Abs(s0.OdomX) + Math.Abs(s0.OdomY), Is.LessThan(0.01));
        }

        [Test]
        public void OutOfSequence_DaTotezJakoVPoradi()
        {
            var ms = new List<IMeasurement>(Rychlosti(0, 2));
            ms.Add(new PositionMeasurement(1.0, 0.5, 0.5, 0.5, T0.AddSeconds(1.02), "GPS"));
            ms.Sort((p, q) => p.TimeStamp.CompareTo(q.TimeStamp));

            var a = Engine();
            Enqueue(a, ms);

            // B: GPS dorazi az na konci (opozdene o ~1 s) a mezi tim se fuze ptala na stav,
            // takze checkpointy uz byly spoctene a musi se prepocitat.
            var b = Engine();
            foreach (var m in ms)
            {
                if (m.Source == "GPS") continue;
                b.Enqueue(m);
                b.GetStateAt(m.TimeStamp);
            }
            b.Enqueue(ms.Find(m => m.Source == "GPS"));

            var t = T0.AddSeconds(2);
            var sa = a.GetStateAt(t);
            var sb = b.GetStateAt(t);
            Assert.Multiple(() =>
            {
                Assert.That(sb.OdomX, Is.EqualTo(sa.OdomX).Within(1e-9));
                Assert.That(sb.OdomY, Is.EqualTo(sa.OdomY).Within(1e-9));
                Assert.That(sb.OdomTheta, Is.EqualTo(sa.OdomTheta).Within(1e-9));
                Assert.That(sb.X, Is.EqualTo(sa.X).Within(1e-9));
            });
        }

        [Test]
        public void Prune_ZapeceOdometriiDoBaze()
        {
            // Okno 0,2 s proti 60 s: s malym oknem projde vetsina uzlu Prune (oba jeho pripady —
            // uzel spocteny i nespocteny), s velkym zadny. Vysledek se lisit nesmi.
            var ms = new List<IMeasurement>(Rychlosti(0, 4));
            ms.Add(new PositionMeasurement(2.0, 0.8, 0.5, 0.5, T0.AddSeconds(2.01), "GPS"));
            ms.Sort((p, q) => p.TimeStamp.CompareTo(q.TimeStamp));

            var velke = Engine(60);
            Enqueue(velke, ms);

            var maleNespoctene = Engine(0.2);   // nikdo se nepta -> Prune zapeka nespoctene uzly
            Enqueue(maleNespoctene, ms);

            var maleSpoctene = Engine(0.2);     // dotaz po kazdem mereni -> Prune bere hotove checkpointy
            foreach (var m in ms)
            {
                maleSpoctene.Enqueue(m);
                maleSpoctene.GetStateAt(m.TimeStamp);
            }

            var t = T0.AddSeconds(4);
            var r = velke.GetStateAt(t);
            foreach (var (jmeno, e) in new[] { ("nespoctene", maleNespoctene), ("spoctene", maleSpoctene) })
            {
                var s = e.GetStateAt(t);
                Assert.Multiple(() =>
                {
                    Assert.That(e.BufferedCount, Is.LessThan(ms.Count / 10), jmeno + ": Prune musel bezet");
                    Assert.That(s.OdomX, Is.EqualTo(r.OdomX).Within(1e-9), jmeno);
                    Assert.That(s.OdomY, Is.EqualTo(r.OdomY).Within(1e-9), jmeno);
                    Assert.That(s.OdomTheta, Is.EqualTo(r.OdomTheta).Within(1e-9), jmeno);
                });
            }
        }

        [Test]
        public void GetStateAt_MeziUzlyDopredikujeOdometrii()
        {
            var e = Engine();
            Enqueue(e, Rychlosti(0, 3));
            var s1 = e.GetStateAt(T0.AddSeconds(3));
            var s2 = e.GetStateAt(T0.AddSeconds(3.5));   // za poslednim merenim
            Assert.That(PosunOdom(s1, s2), Is.EqualTo(V * 0.5).Within(0.01));
            Assert.That(PosunOdom(s1, s2), Is.EqualTo(Posun(s1, s2)).Within(1e-9),
                        "dopredikce bez mereni je pro obe pozy tataz");
        }

        [Test]
        public void OdomToWorld_PrevadiOdometrickouPozuNaGlobalni()
        {
            var s = new RobotState { X = 10, Y = 5, Theta = 1.0, OdomX = 3, OdomY = -2, OdomTheta = 0.4 };
            var (dx, dy, dth) = s.OdomToWorld();
            double c = Math.Cos(dth), sn = Math.Sin(dth);
            Assert.Multiple(() =>
            {
                Assert.That(dth, Is.EqualTo(0.6).Within(1e-12));
                Assert.That(c * s.OdomX - sn * s.OdomY + dx, Is.EqualTo(s.X).Within(1e-12));
                Assert.That(sn * s.OdomX + c * s.OdomY + dy, Is.EqualTo(s.Y).Within(1e-12));
            });
        }

        // --- RobotStateMsg verze 2 ---

        [Test]
        public void RobotStateMsg_VerzeFormatuJe2()
            => Assert.That(new RobotStateMsg().Verze, Is.EqualTo(2),
                           "pridani pole = zvednuta verze, jinak by se stare zaznamy cetly spatne");

        [Test]
        public void RobotStateMsg_RoundTripNeseOdometrii()
        {
            var msg = new RobotStateMsg(new RobotState
            {
                X = 1, Y = 2, Theta = 0.3, V = 0.8, Omega = 0.1, TimeStamp = T0,
                OdomX = -4.5, OdomY = 7.25, OdomTheta = -1.2,
            });

            var zpet = Kolecko(msg, 2);
            Assert.Multiple(() =>
            {
                Assert.That(zpet.HasOdom, Is.True);
                Assert.That(zpet.OdomX, Is.EqualTo(-4.5));
                Assert.That(zpet.OdomY, Is.EqualTo(7.25));
                Assert.That(zpet.OdomTheta, Is.EqualTo(-1.2));
                Assert.That(zpet.ToRobotState().OdomY, Is.EqualTo(7.25));
                Assert.That(zpet.X, Is.EqualTo(1));
            });
        }

        [Test]
        public void RobotStateMsg_Verze1_SeCteBezOdometrie()
        {
            var msg = new RobotStateMsg(new RobotState { X = 1, Y = 2, Theta = 0.3, TimeStamp = T0, OdomX = 9 })
            {
                Verze = 1,
            };

            var zpet = Kolecko(msg, 1);
            Assert.Multiple(() =>
            {
                Assert.That(zpet.HasOdom, Is.False, "verze 1 odometrii nenese");
                Assert.That(zpet.OdomX, Is.EqualTo(0));
                Assert.That(zpet.Y, Is.EqualTo(2));
            });
        }

        private static RobotStateMsg Kolecko(RobotStateMsg msg, int verze)
        {
            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                msg.ToData(bw);

            ms.Position = 0;
            var zpet = new RobotStateMsg { Verze = verze };
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                zpet.FromData(br);
            Assert.That(ms.Position, Is.EqualTo(ms.Length), "precteno vse, co se zapsalo");
            return zpet;
        }
    }
}
