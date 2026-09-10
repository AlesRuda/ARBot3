using System;
using System.Collections.Generic;
using System.IO;
using ARBot.Common.Calibration;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

using Vector3 = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// Sberac kalibrace: integruje gyro, plni kose, obcas prolozi a vyrobi <see cref="MagCalMsg"/>.
    /// Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalCollectorTests
    {
        private const double Bref = 0.4818;
        private const double SklonRad = 1.0638;
        private static readonly DateTime T0 = new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc);

        private static readonly Matrix<double> C = Matrix<double>.Build.DenseOfArray(new[,] {
            { 1.222, 0.005, 0.010 }, { 0.005, 1.175, -0.012 }, { 0.010, -0.012, 1.081 } });
        private static readonly Vector<double> Bias =
            Vector<double>.Build.DenseOfArray(new[] { -0.274, -0.058, 0.076 });

        /// <summary>
        /// Pole i gravitace pro danou pozu robota — <b>obojí z JEDNÉ rotace</b>.
        ///
        /// <para>⚠️ Je to zamer, ne styl. Kdyz se pole a gravitace vyrabeji dvema nezavislymi
        /// vzorci, snadno se rozejdou (naklon v jednom a ne v druhem, nebo obraceny znak) a fit
        /// pak spravne hlasi rozptyl sklonu v desitkach stupnu — chyba je ale v testu. Presne
        /// tohle se tady jednou stalo (sd(sklonu) 18,7° pri perfektni kalibraci). Jedna rotace
        /// aplikovana na oba svetove vektory tu tridu chyby odstranuje.</para>
        /// </summary>
        private static (Vector3 Mag, Vector3 Acc) Poza(double yaw, double naklon, double smerRad)
        {
            // Svetove vektory: pole se sklonem SklonRad k severu, gravitace dolu.
            var mWorld = new Vector3((float)(Bref * Math.Cos(SklonRad)), 0f,
                                     (float)(-Bref * Math.Sin(SklonRad)));
            var gWorld = new Vector3(0f, 0f, -9.81f);

            // Svet -> teleso: nejdriv kurz kolem svislice, pak naklon kolem vodorovne osy.
            var osa = new Vector3((float)Math.Cos(smerRad + Math.PI / 2),
                                  (float)Math.Sin(smerRad + Math.PI / 2), 0f);
            var qYaw = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)-yaw);
            var qTilt = Quaternion.CreateFromAxisAngle(osa, (float)-naklon);

            Vector3 DoTelesa(Vector3 v) => Vector3.Transform(Vector3.Transform(v, qYaw), qTilt);

            var m = DoTelesa(mWorld);
            var x = Vector<double>.Build.DenseOfArray(new double[] { m.X, m.Y, m.Z });
            var r = C.Inverse() * x + Bias;
            return (new Vector3((float)r[0], (float)r[1], (float)r[2]), DoTelesa(gWorld));
        }

        private static IMUState Vzorek(double t, double omega, Vector3 mag, Vector3 acc)
            => new IMUState
            {
                Name = "VN100 IMU",
                HasAbsoluteHeading = true,
                MagnetometerRaw = mag,
                Acceleration = acc,
                AngularVelocity = new Vector3(0f, 0f, (float)omega),
                TimeStamp = T0.AddSeconds(t),
            };

        /// <summary>Jeden obrat o 360° pri danem naklonu; 100 Hz, 0,5 rad/s (tedy ~12,6 s).</summary>
        private static void Obrat(MagCalCollector c, double naklon, double smerRad, ref double t)
        {
            const double omega = 0.5, dt = 0.01;
            int n = (int)(2 * Math.PI / omega / dt);
            for (int i = 0; i < n; i++)
            {
                double yaw = omega * dt * i;
                var p = Poza(yaw, naklon, smerRad);
                c.Add(Vzorek(t, omega, p.Mag, p.Acc));
                t += dt;
            }
        }

        [Test]
        public void JenObratNaRovine_NeniPouzitelne()
        {
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, 0, ref t);

            Assert.Multiple(() =>
            {
                Assert.That(c.Coverage.Complete, Is.False);
                Assert.That(c.Usable, Is.False,
                            "bez naklonu je slozka z vymyslena - nesmi to byt pouzitelne");
                Assert.That(c.Verdict, Does.Contain("naklon"));
            });
        }

        [Test]
        public void TriObratySProtilehlymiNaklony_JePouzitelne_ASediParametry()
        {
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, 0, ref t);
            Obrat(c, 0.40, 0, ref t);
            Obrat(c, 0.40, Math.PI, ref t);

            Assert.That(c.Coverage.Complete, Is.True, "pokryti: " + c.Coverage.MissingText());
            Assert.That(c.LastResult, Is.Not.Null);
            Assert.That(c.LastResult.Condition, Is.LessThan(MagCalThresholds.MaxCondition));
            Assert.That(c.Usable, Is.True, c.Verdict);
            Assert.That(c.Verdict, Is.EqualTo("HOTOVO"));
            for (int i = 0; i < 3; i++)
                Assert.That(c.LastResult.B[i], Is.EqualTo(Bias[i]).Within(0.01),
                            $"bias slozka {i}");
        }

        [Test]
        public void IMUBezAbsolutnihoKurzu_SeIgnoruje()
        {
            // V robotu je IMU vic a T265 posila RELATIVNI yaw. Michat dve ruzne nuly by dalo
            // nesmysl - stejny duvod jako ve Vn100Report.
            var c = new MagCalCollector(Bref);
            var p0 = Poza(0, 0, 0);
            var t265 = Vzorek(0, 0, p0.Mag, p0.Acc);
            t265.Name = "T265";
            t265.HasAbsoluteHeading = false;

            c.Add(t265);

            Assert.That(c.Coverage.Mag.Count, Is.EqualTo(0));
        }

        [Test]
        public void MezeraVDatech_SeNeintegruje()
        {
            // Po dlouhe mezere se uhlova rychlost nesmi integrovat - nasbiralo by se otoceni,
            // ktere se nestalo, a kose by se naplnily vedle.
            var c = new MagCalCollector(Bref);
            var p = Poza(0, 0, 0);

            c.Add(Vzorek(0, 1.0, p.Mag, p.Acc));
            c.Add(Vzorek(30, 1.0, p.Mag, p.Acc));   // mezera 30 s

            Assert.That(c.Coverage.AzimuthCounts[0], Is.EqualTo(2),
                        "oba vzorky do TEHOZ kose - yaw se nesmel posunout");
            Assert.That(c.IntegratedYawRad, Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void BezSurovehoPole_SpadneNaKompenzovane()
        {
            // Zaznamy formatu < 4 surove pole nenesou; sberac musi jet dal, jen s vyhradou,
            // kterou hlasi report.
            var c = new MagCalCollector(Bref);
            var p = Poza(0, 0, 0);
            var v = Vzorek(0, 0, p.Mag, p.Acc);
            v.MagnetometerRaw = null;
            v.Magnetometer = p.Mag;

            c.Add(v);

            Assert.That(c.Coverage.Mag.Count, Is.EqualTo(1));
        }

        [Test]
        public void ToLogMessage_NeseVerdiktPokrytiIMeritko()
        {
            var c = new MagCalCollector(Bref) { Reg23Before = "1,0,0,0,1,0,0,0,1,0,0,0" };
            double t = 0;
            Obrat(c, 0.0, 0, ref t);
            Obrat(c, 0.40, 0, ref t);
            Obrat(c, 0.40, Math.PI, ref t);

            var m = c.ToLogMessage();

            Assert.Multiple(() =>
            {
                Assert.That(m.Vnwrg23.Split(',').Length, Is.EqualTo(12));
                Assert.That(m.FilledAzimuthBins, Is.EqualTo(MagCalThresholds.AzimuthBins));
                Assert.That(m.TiltedGroups,
                            Is.GreaterThanOrEqualTo(MagCalThresholds.MinTiltedGroups));
                Assert.That(m.HasOppositeTilts, Is.True);
                Assert.That(m.BRefG, Is.EqualTo(Bref).Within(1e-9),
                            "meritko musi cestovat se zpravou, jinak se vysledek neda porovnat");
                Assert.That(m.Reg23Before, Is.EqualTo("1,0,0,0,1,0,0,0,1,0,0,0"));
                Assert.That(m.Verdict, Is.EqualTo("HOTOVO"));
            });
        }

        [Test]
        public void ToLogMessage_PredProlozenim_NeseAsponPodminenost()
        {
            // "Prolozeni selhalo" obsluze neposlouzi; "podminenost 8e7, otacej dal" ano.
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, 0, ref t);

            var m = c.ToLogMessage();

            Assert.That(m.Vnwrg23, Is.Empty);
            Assert.That(m.Condition, Is.GreaterThan(MagCalThresholds.MaxCondition));
            Assert.That(m.Verdict, Does.StartWith("POKRACUJ"));
        }

        [Test]
        public void MagCalMsg_ProjdeSerializaci()
        {
            var a = new MagCalMsg
            {
                Phase = 2, Condition = 412.5, SdMagnitudeG = 0.0012, SdInclinationDeg = 0.21,
                Vnwrg23 = "1.2,0.0,0.0,0.0,1.1,0.0,0.0,0.0,1.0,-0.274,-0.058,0.076",
                Verdict = "HOTOVO", MissingText = string.Empty,
                FilledAzimuthBins = 24, TiltGroups = 3, TiltedGroups = 2, HasOppositeTilts = true,
                Samples = 3770, BRefG = 0.4818, Reg23Before = "1,0,0,0,1,0,0,0,1,0,0,0",
                TimeStamp = new DateTime(2026, 9, 8, 12, 5, 0, DateTimeKind.Utc),
            };

            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                a.ToData(bw);
            ms.Position = 0;
            var b = new MagCalMsg();
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                b.FromData(br);

            Assert.Multiple(() =>
            {
                Assert.That(b.Phase, Is.EqualTo(2));
                Assert.That(b.Condition, Is.EqualTo(412.5).Within(1e-9));
                Assert.That(b.Vnwrg23, Is.EqualTo(a.Vnwrg23));
                Assert.That(b.Verdict, Is.EqualTo("HOTOVO"));
                Assert.That(b.HasOppositeTilts, Is.True);
                Assertic(b);
            });
        }

        private static void Assertic(MagCalMsg b)
        {
            Assert.That(b.BRefG, Is.EqualTo(0.4818).Within(1e-9));
            Assert.That(b.Reg23Before, Is.EqualTo("1,0,0,0,1,0,0,0,1,0,0,0"));
            Assert.That(b.TimeStamp, Is.EqualTo(new DateTime(2026, 9, 8, 12, 5, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void MagCalMsg_JeVReplayKatalogu()
        {
            // Katalog mapuje jmeno zpravy na prototyp; bez toho by se zaznam neprehral.
            var katalog = ARBot.Common.Communication.MessageCatalog.RecordDefaults();
            Assert.That(katalog.Contains(nameof(MagCalMsg)), Is.True,
                        "bez zapisu v katalogu by MagCalMsg v zaznamu byla necitelna");
        }

        /// <summary>Obrat, pri kterem je pole posunute o zadany vektor — „popojel jsem s robotem".</summary>
        private static void ObratSPosunemPole(MagCalCollector c, double naklon, double smerRad,
                                              Vector3 posun, ref double t)
        {
            const double omega = 0.5, dt = 0.01;
            int n = (int)(2 * Math.PI / omega / dt);
            for (int i = 0; i < n; i++)
            {
                var p = Poza(omega * dt * i, naklon, smerRad);
                c.Add(Vzorek(t, omega, p.Mag + posun, p.Acc));
                t += dt;
            }
        }

        [Test]
        public void KdyzSeBehemMereniZmeniloPole_VerdiktNeposilaObsluhuOtacet()
        {
            // Obsluha s robotem popojela a meri pokazde v jinem poli. ZADNE otaceni to
            // nespravi - a presne to musi verdikt rict, jinak posila cloveka delat neco,
            // co mu pomoct nemuze.
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, 0, ref t);
            ObratSPosunemPole(c, 0.30, 0, new Vector3(0.10f, 0.10f, 0.10f), ref t);
            ObratSPosunemPole(c, 0.30, Math.PI, new Vector3(0.10f, 0.10f, 0.10f), ref t);

            Assert.That(c.Usable, Is.False, "z nekonzistentniho pole se zapisovat nesmi");
            Assert.That(c.Verdict, Does.Contain("pole"),
                "verdikt ma pojmenovat PRICINU - menici se pole");
            Assert.That(c.Verdict.ToLowerInvariant(), Does.Not.Contain("otacej"),
                "a hlavne NESMI radit otaceni, ktere tady nepomuze");
        }

        [Test]
        public void PriMalemNaklonu_JeTvrdeZelezoUzZmerene_IKdyzElipsoidaNe()
        {
            // Obsluha otocila robota dokola a trochu ho naklonila. Na elipsoidu to nestaci,
            // ale tvrde zelezo uz zmerene je - a to je to, co si ma moct odvezt z pole.
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, 0, ref t);
            Obrat(c, 0.05, 0, ref t);
            Obrat(c, 0.05, Math.PI, ref t);

            Assert.That(c.LastResult, Is.Null, "predpoklad testu: elipsoida jeste ne");
            Assert.That(c.HardIronOnly, Is.Not.Null, "ale tvrde zelezo ano");
            Assert.That(c.CanWriteHardIron, Is.True);
            Assert.That(c.Usable, Is.False, "plna kalibrace porad ne");

            // ⚠️ Odhad tvrdeho zeleza je SAM VYCHYLENY neopravenym mekkym zelezem, a nejde
            // o nepresnost implementace, ale o vlastnost metody: koule prolozena povrchem
            // elipsoidy ma stred posunuty. Zmereno 10. 9. 2026, kolik tvrdeho zeleza se
            // odstrani podle sily mekkeho: bez nej 100 %, pri 1,05/1,00/0,97 92 %, pri
            // referencnim 1,222/1,175/1,081 kolem 80 %, pri patologickem 1,5/1,0/0,8 uz jen
            // 38 %. Tolerance je odtud, ne z pohodli.
            double chyba = 0;
            for (int i = 0; i < 3; i++)
                chyba += Math.Pow(c.HardIronOnly.B[i] - Bias[i], 2);
            Assert.That(Math.Sqrt(chyba), Is.LessThan(Bias.L2Norm() / 3),
                "castecna kalibrace ma odstranit aspon dve tretiny tvrdeho zeleza");
        }

        [Test]
        public void NaRovine_NeniCoZapsat_AniJakoTvrdeZelezo()
        {
            // Bez naklonu neni urcena ani koule (a rovinna elipsa dava nesmyslny stred, ktery
            // chyti az brana na meritko). Nesmi se tedy nabizet ani castecny zapis.
            var c = new MagCalCollector(Bref);
            double t = 0;
            Obrat(c, 0.0, 0, ref t);
            Obrat(c, 0.0, 0, ref t);

            Assert.That(c.HardIronOnly, Is.Null);
            Assert.That(c.CanWriteHardIron, Is.False);
        }

        [Test]
        public void MagCalMsg_NeseMrizkuPokryti_AProjdeSerializaci()
        {
            // Mrizka musi projit i do ZAZNAMU, ne jen na stranku - jinak by se pozdeji nedalo
            // dohledat, co obsluha v poli videla, a ARBot.Analyze by to nemel z ceho postavit.
            var a = new MagCalMsg
            {
                Verdict = "POKRACUJ", MissingText = "chybi naklon", Vnwrg23 = string.Empty,
                Reg23Before = string.Empty,
                Grid = new[] { new[] { 20, 21, 22 }, new[] { 0, 5, 0 } },
                CurrentRow = 1, CurrentAzimuthBin = 2, CurrentTiltDeg = 23.5,
                SphereSdMagnitudeG = 0.0021, SphereVnwrg23 = "1,0,0,0,1,0,0,0,1,-0.27,0,0",
                CanWriteHardIron = true,
            };

            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                a.ToData(bw);
            ms.Position = 0;
            var b = new MagCalMsg();
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                b.FromData(br);

            Assert.Multiple(() =>
            {
                Assert.That(b.Grid.Length, Is.EqualTo(2));
                Assert.That(b.Grid[0], Is.EqualTo(new[] { 20, 21, 22 }));
                Assert.That(b.Grid[1], Is.EqualTo(new[] { 0, 5, 0 }));
                Assert.That(b.CurrentRow, Is.EqualTo(1));
                Assert.That(b.CurrentAzimuthBin, Is.EqualTo(2));
                Assert.That(b.CurrentTiltDeg, Is.EqualTo(23.5).Within(1e-9));
                Assert.That(b.SphereSdMagnitudeG, Is.EqualTo(0.0021).Within(1e-9));
                Assert.That(b.SphereVnwrg23, Is.EqualTo(a.SphereVnwrg23));
                Assert.That(b.CanWriteHardIron, Is.True);
            });
        }

        [Test]
        public void MagCalMsg_VerzeJedna_SeJesteDaPrecist()
        {
            // Zaznamy z 8.-10. 9. 2026 maji verzi 1. Bez vetve ve FromData by se cely .rec
            // rozsypal - binarni stream by se posunul o nove polozky.
            var v1 = new MagCalMsg
            {
                Phase = 1, Condition = 80.0, SdMagnitudeG = 0.002, SdInclinationDeg = 0.3,
                Vnwrg23 = string.Empty, Verdict = "POKRACUJ", MissingText = string.Empty,
                FilledAzimuthBins = 24, TiltGroups = 3, TiltedGroups = 2, HasOppositeTilts = true,
                Samples = 1000, BRefG = 0.4818, Reg23Before = string.Empty,
                TimeStamp = new DateTime(2026, 9, 10, 8, 0, 0, DateTimeKind.Utc),
            };

            var ms = new MemoryStream();
            using (var bw = new BinaryWriter(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                ZapisVerzi1(v1, bw);
            ms.Position = 0;
            var b = new MagCalMsg();
            b.Verze = 1;                       // presne to dela MessageReader podle hlavicky ramce
            using (var br = new BinaryReader(ms, System.Text.Encoding.UTF8, leaveOpen: true))
                b.FromData(br);

            Assert.Multiple(() =>
            {
                Assert.That(b.Condition, Is.EqualTo(80.0).Within(1e-9));
                Assert.That(b.Samples, Is.EqualTo(1000));
                Assert.That(b.TimeStamp, Is.EqualTo(v1.TimeStamp));
                Assert.That(b.Grid, Is.Empty, "verze 1 mrizku nenesla - prazdna, ne vymyslena");
                Assert.That(b.CurrentRow, Is.EqualTo(-1), "a aktualni bunka se nezna");
                Assert.That(b.CanWriteHardIron, Is.False);
            });
        }

        /// <summary>Presne ten layout, ktery zapisovala verze 1 — kopie, at se test neveze na kod.</summary>
        private static void ZapisVerzi1(MagCalMsg m, BinaryWriter bw)
        {
            bw.Write(m.Phase); bw.Write(m.Condition); bw.Write(m.SdMagnitudeG);
            bw.Write(m.SdInclinationDeg); bw.Write(m.Vnwrg23); bw.Write(m.Verdict);
            bw.Write(m.MissingText); bw.Write(m.FilledAzimuthBins); bw.Write(m.TiltGroups);
            bw.Write(m.TiltedGroups); bw.Write(m.HasOppositeTilts); bw.Write(m.Samples);
            bw.Write(m.BRefG); bw.Write(m.Reg23Before);
            bw.Write(m.TimeStamp.ToBinary());
        }
    }
}
