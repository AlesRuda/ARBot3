using System;
using System.Collections.Generic;
using ARBot.Common.Calibration;
using MathNet.Numerics.LinearAlgebra;
using NUnit.Framework;

// ⚠️ System.Numerics se ZAMERNE neimportuje celý: `Vector<>` v nem koliduje s MathNet
// `Vector<T>` a prekladac to hlasi jako CS0104. Alias to resi a zaroven necha `Vector<double>`
// jednoznacne mirit do MathNet.
using Vector3 = System.Numerics.Vector3;
using Quaternion = System.Numerics.Quaternion;

namespace ARBot.Common.Tests.Calibration
{
    /// <summary>
    /// Prolozeni elipsoidy magnetometru. Testy meri proti ZNAME odpovedi: vyrobi se idealni pole,
    /// pokrivi znamymi (C, b) a fit ma ty parametry vratit. Viz doc/plan-vn100-kalibrace.md.
    /// </summary>
    public class MagCalFitTests
    {
        /// <summary>Velikost pole [G] a sklon [rad] podle registru 21 dnesniho senzoru.</summary>
        private const double Bref = 0.4818;
        private const double SklonRad = 1.0638;   // 60,9 stupne

        /// <summary>
        /// Idealni pole v telese pri danem kurzu a naklonu [G].
        ///
        /// <para>Transformace se skladaji POSTUPNE, ne nasobenim kvaternionu — v System.Numerics
        /// je poradi u operatoru * matouci a tady na jednoznacnosti zalezi. Fyzikalni presnost
        /// konvence stejne neni podstatna: fit potrebuje jen to, aby vzorky lezely na kulove
        /// plose o polomeru Bref a pokryvaly prostor.</para>
        /// </summary>
        private static Vector3 Ideal(double yaw, double naklon)
        {
            double h = Bref * Math.Cos(SklonRad), v = -Bref * Math.Sin(SklonRad);
            var m = new Vector3((float)h, 0f, (float)v);
            m = Vector3.Transform(m, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, (float)-yaw));
            return Vector3.Transform(m, Quaternion.CreateFromAxisAngle(Vector3.UnitX, (float)-naklon));
        }

        /// <summary>Surova mereni pro zadane (C, b): m_raw = C^-1 * m_ideal + b.</summary>
        private static List<Vector3> Vzorky(Matrix<double> C, Vector<double> b,
                                            double[] naklony, int naAzimut = 60)
        {
            var Ci = C.Inverse();
            var res = new List<Vector3>();
            foreach (double n in naklony)
                for (int i = 0; i < naAzimut; i++)
                {
                    var id = Ideal(2 * Math.PI * i / naAzimut, n);
                    var x = Vector<double>.Build.DenseOfArray(new double[] { id.X, id.Y, id.Z });
                    var r = Ci * x + b;
                    res.Add(new Vector3((float)r[0], (float)r[1], (float)r[2]));
                }
            return res;
        }

        /// <summary>
        /// Gravitace ke kazdemu vzorku z <see cref="Vzorky"/> — akcelerometr v klidu meri −g.
        /// Poradi a delka musi souhlasit, protoze fit je paruje po indexu.
        /// </summary>
        private static List<Vector3> Gravitace(double[] naklony, int naAzimut = 60)
        {
            var res = new List<Vector3>();
            foreach (double n in naklony)
                for (int i = 0; i < naAzimut; i++)
                    res.Add(new Vector3(0f, (float)(-9.81 * Math.Sin(n)),
                                            (float)(-9.81 * Math.Cos(n))));
            return res;
        }

        private static Matrix<double> Mat(double xx, double yy, double zz,
                                          double xy, double xz, double yz)
            => Matrix<double>.Build.DenseOfArray(new[,] {
                   { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } });

        private static Vector<double> Vec(double x, double y, double z)
            => Vector<double>.Build.DenseOfArray(new[] { x, y, z });

        [Test]
        public void Prolozeni_VratiZnameParametry()
        {
            // Hodnoty z referencniho exportu vn100-2026-7-8-nastavei z arbot2.sencfg, tedy
            // realisticky rad: mekke zelezo ~1,1-1,2, tvrde ~0,27 G.
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);
            var vz = Vzorky(C, b, new[] { 0.0, 0.35, -0.35 });

            var r = MagCalFit.Fit(vz, Bref);

            for (int i = 0; i < 3; i++)
            {
                Assert.That(r.B[i], Is.EqualTo(b[i]).Within(0.002), $"bias slozka {i}");
                for (int j = 0; j < 3; j++)
                    Assert.That(r.C[i, j], Is.EqualTo(C[i, j]).Within(0.01), $"C[{i},{j}]");
            }
        }

        [Test]
        public void JenYaw_BezNaklonu_JeSpatnePodminene()
        {
            // NEJDULEZITEJSI test tehle sady: bez nej by fit nad rovinnou rotaci TISE odpovedel
            // a nikdo by nevedel, ze slozka z je vymyslena.
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);

            bool okRovina = MagCalFit.TryFit(Vzorky(C, b, new[] { 0.0 }, 200), Bref,
                                             out var rovina, out double condRovina);
            bool okNaklony = MagCalFit.TryFit(Vzorky(C, b, new[] { 0.0, 0.35, -0.35 }), Bref,
                                              out var snaklony, out double condNaklony);

            Assert.That(okRovina, Is.False,
                "rotace na rovine se NESMI prolozit - slozka z neni merena");
            Assert.That(rovina, Is.Null);
            Assert.That(condRovina, Is.GreaterThan(MagCalThresholds.MaxCondition),
                "a podminenost musi byt vyplnena, aby obsluha vedela, jak daleko od hotova je");

            Assert.That(okNaklony, Is.True, "s naklony ma byt soustava urcena");
            Assert.That(condNaklony, Is.LessThan(MagCalThresholds.MaxCondition));
            Assert.That(snaklony, Is.Not.Null);
        }

        [Test]
        public void NesymetrickyVstup_VratiSymetrickouMatici()
        {
            // Data vyrobena ROTOVANOU (tedy nesymetrickou) matici R·C_sym. Fit musi vratit
            // symetricke reseni, jinak by zbyl konstantni posun kurzu nerozlisitelny od deklinace.
            var sym = Mat(1.20, 1.15, 1.08, 0.0, 0.0, 0.0);
            var rot = Matrix<double>.Build.DenseOfArray(new[,] {
                { 0.9962, -0.0872, 0.0 }, { 0.0872, 0.9962, 0.0 }, { 0.0, 0.0, 1.0 } });
            var b = Vec(-0.10, 0.05, -0.02);

            var r = MagCalFit.Fit(Vzorky(rot * sym, b, new[] { 0.0, 0.35, -0.35 }), Bref);

            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                    Assert.That(r.C[i, j], Is.EqualTo(r.C[j, i]).Within(1e-9),
                        $"C musi byt symetricka: [{i},{j}] vs [{j},{i}]");
        }

        [Test]
        public void PoKorekci_SediVelikostNaReferenci_NeNaPrumerDat()
        {
            // Mekke zelezo se stredni hodnotou VYRAZNE nad 1 -> prumer surovych dat je jiny nez
            // Bref, takze test odlisi normalizaci na referenci od normalizace na data. Na tom
            // zalezi: VPE porovnava |B| proti registru 21, ne proti nasim datum.
            var C = Mat(1.60, 1.55, 1.50, 0.0, 0.0, 0.0);
            var naklony = new[] { 0.0, 0.35, -0.35 };
            var vz = Vzorky(C, Vec(-0.20, 0.10, 0.05), naklony);

            var r = MagCalFit.Fit(vz, Bref, Gravitace(naklony));

            double prumer = 0;
            foreach (var m in vz) prumer += r.Apply(m).Length();
            prumer /= vz.Count;

            Assert.That(prumer, Is.EqualTo(Bref).Within(0.001));
            Assert.That(r.SdMagnitudeG, Is.LessThan(MagCalThresholds.MaxSdMagnitudeG));
            Assert.That(r.SdInclinationDeg, Is.LessThan(MagCalThresholds.MaxSdInclinationDeg),
                "sklon se pocita SKLOPENY gravitaci, takze pri naklonech musi zustat konstantni");
        }

        [Test]
        public void BezAkcelerometru_JeSklonNaN_NeSmyslnaHodnota()
        {
            // Poctivejsi nez tise vratit telesovy sklon, ktery by pri naklonech nic neznamenal.
            var vz = Vzorky(Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012),
                            Vec(-0.274, -0.058, 0.076), new[] { 0.0, 0.35, -0.35 });

            var r = MagCalFit.Fit(vz, Bref);

            Assert.That(double.IsNaN(r.SdInclinationDeg), Is.True);
            Assert.That(r.SdMagnitudeG, Is.LessThan(MagCalThresholds.MaxSdMagnitudeG),
                "velikost se da spocitat i bez gravitace");
        }

        [Test]
        public void ToVnwrg23_MaDvanactCisel_SDesetinnouTeckou()
        {
            var r = new MagCalResult(Mat(1.2, 1.1, 1.0, 0.01, 0.02, 0.03),
                                     Vec(-0.274, -0.058, 0.076), 12.0, 0.001, 0.1, 500);

            string s = r.ToVnwrg23();

            Assert.That(s.Split(',').Length, Is.EqualTo(12));
            Assert.That(s, Does.Contain("."), "desetinna TECKA - carka by rozbila prikaz");
            Assert.That(s, Does.StartWith("1.200000"));
            Assert.That(s, Does.EndWith("0.076000"));
        }

        [Test]
        public void MaloVzorku_JeChyba_NeTichaOdpoved()
        {
            var vz = Vzorky(Mat(1.0, 1.0, 1.0, 0, 0, 0), Vec(0, 0, 0), new[] { 0.0 }, 10);

            Assert.That(() => MagCalFit.Fit(vz, Bref), Throws.ArgumentException);
        }

        [Test]
        public void HeadingDiffDeg_ShodneKalibrace_DaNulu()
        {
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);
            var vz = Vzorky(C, b, new[] { 0.0, 0.35, -0.35 });

            var a = MagCalFit.Fit(vz, Bref);
            var d = MagCalFit.Fit(vz, Bref);

            Assert.That(MagCalFit.HeadingDiffDeg(a, d), Is.LessThan(1e-6));
        }

        [Test]
        public void Podminenost_JeInvariantni_NaPoctuVzorku()
        {
            // TOHLE JE OPRAVNENI REDENI (MagCalFit.MaxFitSamples). Cena Svd roste s poctem
            // vzorku kvadraticky (12 000 vzorku = 7,7 s, 45 000 uz nedobehne), takze se nad
            // stropem redi — a smi se to jen proto, ze podminenost na poctu radku NEZAVISI.
            // Kdyby zavisela, redenim by se menil verdikt „urceno / neurceno".
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);

            MagCalFit.TryFit(Vzorky(C, b, new[] { 0.0, 0.35, -0.35 }, 100), Bref,
                             out _, out double condMalo);
            MagCalFit.TryFit(Vzorky(C, b, new[] { 0.0, 0.35, -0.35 }, 4000), Bref,
                             out _, out double condMnoho);

            Assert.That(condMalo, Is.EqualTo(condMnoho).Within(1.0),
                "podminenost se s poctem vzorku nesmi hnout - jinak redit nelze");
        }

        [Test]
        public void NadStropem_SeRedi_AVysledekZustane()
        {
            // Redenim se nesmi zmenit ANI parametry, ne jen verdikt. Vzorku je 12x nad stropem.
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);
            var naklony = new[] { 0.0, 0.35, -0.35 };
            int naAzimut = 6 * MagCalFit.MaxFitSamples / naklony.Length;
            var vz = Vzorky(C, b, naklony, naAzimut);
            Assert.That(vz.Count, Is.GreaterThanOrEqualTo(6 * MagCalFit.MaxFitSamples),
                        "predpoklad testu");

            var r = MagCalFit.Fit(vz, Bref, Gravitace(naklony, naAzimut));

            for (int i = 0; i < 3; i++)
            {
                Assert.That(r.B[i], Is.EqualTo(b[i]).Within(0.002), $"bias slozka {i}");
                for (int j = 0; j < 3; j++)
                    Assert.That(r.C[i, j], Is.EqualTo(C[i, j]).Within(0.01), $"C[{i},{j}]");
            }
            Assert.That(r.Samples, Is.EqualTo(vz.Count),
                "Samples hlasi, kolik vzorku do vysledku vstoupilo - zbytky se pocitaji ze VSECH");
        }

        [Test]
        public void Koule_ZUzkehoPasma_DaTvrdeZelezo_KdyzElipsoidaJesteNe()
        {
            // JADRO cele te zmeny: kdyz obsluha nenaklonila robota dost, elipsoida se urcit
            // neda - ale TVRDE ZELEZO ano. Bez toho konci mise hlaskou "otacej dal" a obsluha
            // si z pole neveze nic.
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var b = Vec(-0.274, -0.058, 0.076);
            var uzke = new[] { 0.0, 0.05, -0.05 };     // ~2,9 stupne, hluboko pod MinTiltDeg
            var vz = Vzorky(C, b, uzke, 200);

            bool elipsoida = MagCalFit.TryFit(vz, Bref, out _, out double ce);
            bool koule = MagCalFit.TryFitSphere(vz, Bref, out var r, out double ck);

            Assert.That(elipsoida, Is.False,
                $"predpoklad testu: elipsoida z uzkeho pasma nejde (cond {ce:G4})");
            Assert.That(koule, Is.True, $"koule ma z tehoz pasma vyjit (cond {ck:G4})");
            Assert.That(ck, Is.LessThan(ce / 100),
                "koule ma byt o rady lepe podminena - to je cely duvod, proc tu je");

            // ⚠️ Odhad tvrdeho zeleza je SAM VYCHYLENY neopravenym mekkym zelezem - zmereno
            // 0,055-0,059 G proti skutecnym 0,274 G, tedy odstrani se ~80 %, ne vsechno.
            // Tolerance je proto 0,07 G a NENI to nedbalost: je to naměřená mez metody.
            double chyba = Math.Sqrt(Math.Pow(r.B[0] - b[0], 2) + Math.Pow(r.B[1] - b[1], 2)
                                   + Math.Pow(r.B[2] - b[2], 2));
            Assert.That(chyba, Is.LessThan(0.07), "tvrde zelezo ma vyjit radove spravne");
            Assert.That(chyba, Is.LessThan(b.L2Norm() / 3),
                "a hlavne podstatne min, nez kdyz se nekoriguje vubec");
        }

        [Test]
        public void Koule_NaRovine_JeNeurcena_IKdyzPodminenostAZbytekMlci()
        {
            // ⚠️ NEJDULEZITEJSI test tehle sady. Rovinna rotace s mekkym zelezem projde
            // podminenosti (538) I zbytkem (0,0000) - a pritom je bias vedle o 476 787 G,
            // protoze prolozenim rovinne elipsy je koule o poloměru v radu 10⁶. Chyti to
            // teprve brana na meritko. Bez tohohle testu by mise nabidla zapsat nesmysl.
            var C = Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012);
            var vz = Vzorky(C, Vec(-0.274, -0.058, 0.076), new[] { 0.0 }, 200);

            bool ok = MagCalFit.TryFitSphere(vz, Bref, out var r, out double cond);

            Assert.That(ok, Is.False, "rotace na rovine NEURCUJE ani kouli");
            Assert.That(r, Is.Null);
            Assert.That(cond, Is.LessThan(MagCalThresholds.MaxCondition),
                "a podminenost to opravdu NECHYTI - proto ta brana na meritko existuje");
        }

        [Test]
        public void Koule_KdyzSeVPulceMereniZmeniloPole_MaVelkyZbytek()
        {
            // Tohle je ten stav, ktery dnes nikdo nevidi: obsluha s robotem popojela a meri
            // pokazde v jinem poli. Zadne otaceni to nespravi - a prave to ma verdikt poznat.
            var naklony = new[] { 0.0, 0.35, -0.35 };
            var prvni = Vzorky(Mat(1, 1, 1, 0, 0, 0), Vec(-0.274, -0.058, 0.076), naklony);
            var druha = Vzorky(Mat(1, 1, 1, 0, 0, 0), Vec(-0.174, 0.042, 0.176), naklony);
            var smes = new List<Vector3>(prvni); smes.AddRange(druha);

            MagCalFit.TryFitSphere(prvni, Bref, out var cista, out _);
            bool ok = MagCalFit.TryFitSphere(smes, Bref, out var spatna, out _);

            Assert.That(cista.SdMagnitudeG, Is.LessThan(MagCalThresholds.MaxSphereSdMagnitudeG),
                "predpoklad testu: nad konzistentnim polem je zbytek maly");
            Assert.That(ok, Is.True, "soustava je porad resitelna - vada je ve zbytku, ne v urcenosti");
            Assert.That(spatna.SdMagnitudeG, Is.GreaterThan(MagCalThresholds.MaxSphereSdMagnitudeG),
                "posun pole uprostred mereni musi byt videt na zbytku koule");
        }

        [Test]
        public void Koule_PriKonzistentnimPoli_NehlasiZmenuPole_AniPriPatologickemMekkemZeleze()
        {
            // Druha strana teze mince: prah "pole se menilo" nesmi vyskocit tam, kde je
            // kalibrace nejvic potreba, tedy u silneho mekkeho zeleza.
            foreach (var C in new[] { Mat(1.222, 1.175, 1.081, 0.005, 0.010, -0.012),
                                      Mat(1.5, 1.0, 0.8, 0.05, 0.05, -0.05) })
            {
                var vz = Vzorky(C, Vec(-0.274, -0.058, 0.076), new[] { 0.0, 0.35, -0.35 });
                MagCalFit.TryFitSphere(vz, Bref, out var r, out _);
                Assert.That(r.SdMagnitudeG, Is.LessThan(MagCalThresholds.MaxSphereSdMagnitudeG),
                    $"mekke zelezo {C[0, 0]:F2} nesmi vypadat jako menici se pole");
            }
        }

    }
}
