using System;
using ARBot.Common.Regulators;
using NUnit.Framework;

namespace ARBot.Common.Tests.Regulators
{
    /// <summary>
    /// Numerický guard kinematických profilů. <see cref="TrapezoidMotionProfile"/> hlídají golden hodnoty
    /// (zachycené z původního <c>Regulator</c> před jeho smazáním — parita byla dokázána v <see cref="PointRegulatorTests"/>).
    /// <see cref="SqrtMotionProfile"/> se ověřuje proti closed-form odmocninovému zákonu (nezávislý oracle).
    /// </summary>
    public class MotionProfileParityTests
    {
        private const double VMax = 0.8;
        private const double WMax = Math.PI / 6;
        private const double Accel = 0.20;
        private const double Rozchod = 0.41;
        private const double Tol = 1e-9;

        // Golden hodnoty TrapezoidMotionProfile.Dist2Speed (VMax=0.8, a=0.20).
        private static readonly (double dist, double vs, double ve, double speed, double time)[] TrapezoidGolden =
        {
            (0.05, 0.0, 0.0, 0.07200000000000002, 0.9600000000000002),
            (0.3,  0.0, 0.0, 0.19800000000000004, 2.29),
            (1.0,  0.2, 0.3, 0.45,                2.45),
            (2.0,  0.5, 0.0, 0.6300000000000001,  4.3500000000000005),
            (10.0, 0.0, 0.0, 0.8,                 16.599999999999998),
            (-6.1, 0.0, 0.0, -0.8,                11.725000000000001),
        };

        [Test]
        public void Trapezoid_Dist2Speed_MatchesGolden()
        {
            var p = new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod);
            foreach (var g in TrapezoidGolden)
            {
                var r = p.Dist2Speed(g.dist, g.vs, g.ve);
                Assert.That(r.Speed, Is.EqualTo(g.speed).Within(Tol), $"Speed @ d={g.dist} vs={g.vs} ve={g.ve}");
                Assert.That(r.RegulationTime, Is.EqualTo(g.time).Within(Tol), $"Time @ d={g.dist} vs={g.vs} ve={g.ve}");
            }
        }

        [Test]
        public void Trapezoid_Rot2RotSpeed_MatchesGolden()
        {
            var p = new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod);
            var r1 = p.Rot2RotSpeed(1.0, 0, 0);
            Assert.That(r1.RotationSpeed, Is.EqualTo(0.5235987755982988).Within(Tol));   // clamp na WMax
            Assert.That(r1.RegulationTime, Is.EqualTo(2.5465480620910004).Within(Tol));
            var r2 = p.Rot2RotSpeed(0.1, 0, 0);
            Assert.That(r2.RotationSpeed, Is.EqualTo(0.17560975609756105).Within(Tol));
            Assert.That(r2.RegulationTime, Is.EqualTo(0.5800000000000001).Within(Tol));
        }

        [Test]
        public void Sqrt_Dist2Speed_MatchesClosedForm()
        {
            var p = new SqrtMotionProfile(VMax, WMax, Accel, Rozchod);
            foreach (var dist in new[] { -6.1, -0.1, 0.05, 0.3, 2.0, 10.0 })
            {
                double expSpeed = Math.Sign(dist) * Math.Min(Math.Sqrt(4 * Accel * Math.Abs(dist)) / 2, VMax);
                double expTime = Math.Sqrt(Math.Abs(dist) / (4 * Accel));
                var r = p.Dist2Speed(dist, 0, 0);
                Assert.That(r.Speed, Is.EqualTo(expSpeed).Within(Tol), $"Speed @ {dist}");
                Assert.That(r.RegulationTime, Is.EqualTo(expTime).Within(Tol), $"Time @ {dist}");
            }
        }

        /// <summary>
        /// <c>Speed2Dist</c> proti closed-form ODMOCNINOVEHO zakona profilu: z <c>v = √(a·d)</c> plyne
        /// <c>d = |v_s² − v_e²| / a</c>.
        /// <para><b>Zmeneno 8. 9. 2026:</b> do te doby test pinnul <c>(v_s − v_e)²/(2a)</c>, coz je
        /// draha rozjezdu z nuly na ROZDIL rychlosti - neodpovidalo to popisu metody ("vzdalenost, na
        /// ktere robot zrychli/zpomali z v_s na v_e pri Acceleration") ani vlastnimu zakonu profilu
        /// (pri v_s = 0,5 a v_e = 0 vracelo 0,625 misto 1,25). Produkcni volani metoda nemela.</para>
        /// </summary>
        [Test]
        public void Sqrt_Speed2Dist_MatchesClosedForm()
        {
            var p = new SqrtMotionProfile(VMax, WMax, Accel, Rozchod);
            foreach (var vs in new[] { 0.0, 0.2, 0.5 })
                foreach (var ve in new[] { 0.0, 0.1 })
                    Assert.That(p.Speed2Dist(vs, ve),
                                Is.EqualTo(Math.Abs(vs * vs - ve * ve) / Accel).Within(Tol));
        }

        [Test]
        public void SpeedLimit_CouplesToRotationTime()
        {
            var p = new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod);
            // rt=0 -> bez omezeni; rt>0 -> min(speed, d/(stability*rt)), stability=4.
            Assert.That(p.SpeedLimit(0.8, 1.0, new RegulatorResult { RegulationTime = 0 }), Is.EqualTo(0.8).Within(Tol));
            Assert.That(p.SpeedLimit(0.8, 1.0, new RegulatorResult { RegulationTime = 1.0 }),
                        Is.EqualTo(Math.Min(0.8, 1.0 / (4 * 1.0))).Within(Tol));
        }

        // ---------------- Dist2MaxSpeed: brzdna obalka jako soucast profilu ----------------

        private static IMotionProfile[] Profily() => new IMotionProfile[]
        {
            new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod),
            new SqrtMotionProfile(VMax, WMax, Accel, Rozchod),
        };

        /// <summary>V bode, kde uz ma byt endSpeed, je stropem prave endSpeed - jinak by obalka lhala.</summary>
        [Test]
        public void Dist2MaxSpeed_VNuloveVzdalenosti_JeToEndSpeed()
        {
            foreach (var p in Profily())
                foreach (double ve in new[] { 0.0, 0.05, 0.3, VMax })
                    Assert.That(p.Dist2MaxSpeed(0, ve), Is.EqualTo(ve).Within(Tol), p.GetType().Name);
        }

        /// <summary>Strop roste se vzdalenosti a nikdy nepresahne MaxSpeed.</summary>
        [Test]
        public void Dist2MaxSpeed_RosteSeVzdalenosti_ANeprekrociMaxSpeed()
        {
            foreach (var p in Profily())
            {
                double prev = -1;
                foreach (double d in new[] { 0.0, 0.05, 0.2, 0.5, 1.0, 2.0, 5.0, 20.0 })
                {
                    double v = p.Dist2MaxSpeed(d, 0.1);
                    Assert.That(v, Is.GreaterThanOrEqualTo(prev - Tol), $"{p.GetType().Name} @ d={d}");
                    Assert.That(v, Is.LessThanOrEqualTo(p.MaxSpeed + Tol), $"{p.GetType().Name} @ d={d}");
                    prev = v;
                }
            }
        }

        /// <summary>
        /// <b>Invariant, na kterem stoji vyhlazovani drahy</b> (<c>LocalPathPlanner</c>): dokud robot do
        /// mista vjizdi POD stropem, prikaz regulatoru ten strop neprekroci. Diky tomu smi planovac
        /// predpovidat rampu stropem misto toho, aby volal <see cref="IMotionProfile.Dist2Speed"/>
        /// po vzorcich (to je JEDEN KROK regulatoru, ne prubeh rychlosti po draze - v nule vraci nulu).
        /// Viz doc/occupancy-and-local-planning.md, lecba 8. 9. 2026.
        ///
        /// <para><b>Predpoklad `vs &lt;= strop` je podstatny, ne formalita:</b> kdyz uz robot jede rychleji,
        /// nez obalka dovoluje (nemel by - drzi to zpetny pruchod plus strop useku), regulator vraci
        /// nejlepsi mozne brzdeni, ne nesplnitelny strop. Zmereno: <c>Dist2Speed(0,05, vs=0,4, ve=0)</c>
        /// = 0,252 proti stropu 0,141 - z 0,4 m/s se na peti centimetrech zastavit neda a prikaz to
        /// nepredstira.</para>
        /// </summary>
        [Test]
        public void Dist2MaxSpeed_JeHorniMeziPrikazuDist2Speed_KdyzSeDoMistaVjizdiPodStropem()
        {
            int overeno = 0;
            foreach (var p in Profily())
                foreach (double ve in new[] { 0.0, 0.05, 0.2, 0.5, VMax })
                    foreach (double d in new[] { 0.0, 0.05, 0.1, 0.25, 0.5, 1.0, 2.0, 5.0 })
                    {
                        double cap = p.Dist2MaxSpeed(d, ve);
                        foreach (double vs in new[] { 0.0, 0.1, 0.4, VMax })
                        {
                            if (vs > cap + Tol) continue;           // uz je nad obalkou - viz shrnuti
                            Assert.That(p.Dist2Speed(d, vs, ve).Speed, Is.LessThanOrEqualTo(cap + Tol),
                                        $"{p.GetType().Name} d={d} vs={vs} ve={ve}");
                            overeno++;
                        }
                    }
            Assert.That(overeno, Is.GreaterThan(100), "predpoklad nesmi vyradit skoro vsechno");
        }

        /// <summary>
        /// <see cref="IMotionProfile.Speed2Dist"/> je PRESNA INVERZE <see cref="IMotionProfile.Dist2MaxSpeed"/>:
        /// vzdalenost, na ktere se rychlost zmeni z <c>vs</c> na <c>ve</c>, je prave ta, ve ktere brzdna
        /// obalka konciciho na <c>ve</c> dava <c>vs</c>. Jsou to dva pohledy na jednu krivku, takze kdyz
        /// se rozejdou, jeden z nich lze.
        ///
        /// <para><b>Naslo to dve vady</b> (8. 9. 2026): <c>Speed2Dist</c> pocital <c>(vs-ve)²/(2a)</c>
        /// misto <c>(vs²-ve²)/(2a)</c> - sedelo to jen pro <c>ve = 0</c> (pri vs=0,8, ve=0,3 vyslo 0,25
        /// misto 0,55), a u <see cref="SqrtMotionProfile"/> ani to, protoze jeho zakon je <c>v = √(a·d)</c>,
        /// tedy <c>d = v²/a</c>. Diskretni simulace lichobezniku pritom na fyzikalni vzorec sedi
        /// presne, takze vzorkovani v tom nehraje roli.</para>
        /// </summary>
        [Test]
        public void Speed2Dist_JePresnouInverziDist2MaxSpeed()
        {
            foreach (var p in Profily())
                foreach (double vs in new[] { 0.2, 0.5, VMax })
                    foreach (double ve in new[] { 0.0, 0.1, 0.3 })
                    {
                        if (ve >= vs) continue;
                        double d = p.Speed2Dist(vs, ve);
                        Assert.That(p.Dist2MaxSpeed(d, ve), Is.EqualTo(vs).Within(1e-9),
                                    $"{p.GetType().Name} vs={vs} ve={ve} (Speed2Dist={d})");
                    }
        }

        /// <summary>
        /// <c>Speed2Dist</c> proti KINEMATICE z prvnich principu, ne proti uzavrenemu tvaru:
        /// <c>t = |Δv|/a</c> a <c>s = v_pomalejsi·t + ½·a·t²</c>. Je to nezavisla derivace teze veci -
        /// rozvinuti da <c>Δ(2v_s + Δ)/(2a) = |v_s² − v_e²|/(2a)</c>, takze kdyby se uzavreny tvar
        /// v implementaci zapsal spatne, tenhle test to chyti.
        ///
        /// <para>Plati jen pro <see cref="TrapezoidMotionProfile"/> - <see cref="SqrtMotionProfile"/>
        /// nema konstantni zrychleni, jeho zakon je <c>v = √(a·d)</c>.</para>
        ///
        /// <para><b>Pozor na znamenka:</b> Δ musi byt <c>v_e − v_s</c>. Zapsano s <c>v_s − v_e</c>
        /// vychazi pri zrychlovani zaporna draha (0,3 → 0,8 pri a = 0,5 da −0,05 misto 0,55).</para>
        /// </summary>
        [Test]
        public void Trapezoid_Speed2Dist_OdpovidaKinematiceZPrvnichPrincipu()
        {
            var p = new TrapezoidMotionProfile(VMax, WMax, Accel, Rozchod);
            foreach (double vs in new[] { 0.0, 0.3, 0.5, VMax })
                foreach (double ve in new[] { 0.0, 0.3, 0.5, VMax })
                {
                    if (Math.Abs(vs - ve) < 1e-12) continue;
                    double t = Math.Abs(ve - vs) / Accel;
                    double s = Math.Min(vs, ve) * t + 0.5 * Accel * t * t;
                    Assert.That(p.Speed2Dist(vs, ve), Is.EqualTo(s).Within(Tol), $"vs={vs} ve={ve}");
                }
        }
}
}
