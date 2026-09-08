using System;
using System.Collections.Generic;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;

// ⚠️ System.Numerics se ZAMERNE neimportuje celý: `Vector<>` v nem koliduje s MathNet
// `Vector<T>` (CS0104). Alias to resi a `Vector<double>` tim jednoznacne miri do MathNet.
using Vector3 = System.Numerics.Vector3;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Prolozeni elipsoidy</b> mereni magnetometru → kompenzace registru 23 VN100.
    ///
    /// <para><b>Princip.</b> Neporusene pole ma konstantni velikost, takze surova mereni maji
    /// pri otaceni telesa lezet na <b>kulove plose</b>. Tvrde zelezo ji posune (stred mimo
    /// pocatek), mekke ji zdeformuje na elipsoidu. Hleda se tedy elipsoida a z ni transformace,
    /// ktera ji vrati na kouli.</para>
    ///
    /// <para>⚠️ <b>Rozklad NENI jednoznacny.</b> <c>A = CᵀC</c> ma nekonecne mnoho reseni
    /// lisicich se rotaci — konstantni <c>|B|</c> splni i otocene reseni. Bere se <b>symetricka
    /// pozitivne definitni odmocnina</b>, protoze mekke zelezo <i>je</i> symetricka deformace.
    /// Referencni export senzoru to potvrzuje: mimo diagonalu ma jednotky tisicin. Kdyby se to
    /// nechalo byt, zbyl by po kalibraci KONSTANTNI posun kurzu, ktery se neda odlisit od
    /// deklinace.</para>
    ///
    /// <para>⚠️ <b>Meritko se vaze na registr 21</b> (<c>bRefG</c>), ne na prumer dat: VPE
    /// porovnava merene <c>|B|</c> a sklon proti referencnimu vektoru a pri nesouhlasu
    /// magnetometr adaptivne utlumi. Koule o spatnem polomeru tedy VPE neuspokoji — a prave to
    /// je podezreni na druhou vadu (VPE se tahne za vlastnim polem 206 s).</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public static class MagCalFit
    {
        /// <summary>Pod timhle poctem vzorku se neproklada — 10 neznamych a sum.</summary>
        public const int MinSamples = 50;

        /// <summary>
        /// Prolozi elipsoidu a vrati kompenzaci registru 23; <b>vyhodi vyjimku</b>, kdyz je
        /// soustava neurcena. Pro sber za behu pouzij <see cref="TryFit"/> — tam je neurcenost
        /// normalni stav, ne vyjimka.
        /// </summary>
        public static MagCalResult Fit(IReadOnlyList<Vector3> mag, double bRefG,
                                       IReadOnlyList<Vector3> acc = null)
        {
            if (!TryFit(mag, bRefG, out var result, out double condition, acc))
                throw new ArgumentException(
                    $"Soustava neni urcena (podminenost {condition:F1}) — data nepokryvaji "
                    + "dost smeru. Otacej robotem dal a pridej naklon.", nameof(mag));
            return result;
        }

        /// <summary>
        /// <b>Prolozeni, ktere neurcenost hlasi navratovou hodnotou, ne vyjimkou.</b>
        ///
        /// <para>Podminenost se vraci <b>vzdy</b>, i kdyz se prolozit nepodarilo — je to cislo,
        /// ktere obsluze rika, jak daleko od hotova je. „Prolozeni selhalo" by ji neposlouzilo;
        /// „podminenost 84, otacej dal" ano. Behem sberu je neurcenost <b>normalni stav</b>,
        /// takze vyjimka by tu byla rizeni toku vyjimkami.</para>
        /// </summary>
        /// <param name="mag">Surova mereni pole [G].</param>
        /// <param name="bRefG">Referencni velikost pole [G] z registru 21.</param>
        /// <param name="result">Vysledek; <c>null</c>, kdyz je soustava neurcena.</param>
        /// <param name="condition">Podminenost navrhove matice — <b>vzdy vyplnena</b>.</param>
        /// <param name="acc">
        /// Mereni akcelerometru ve stejnem poradi jako <paramref name="mag"/>, kvuli
        /// <see cref="MagCalResult.SdInclinationDeg"/>. Bez nich zustane sklon
        /// <see cref="double.NaN"/> — viz jeho dokumentace.
        /// </param>
        /// <param name="maxCondition">Nad touhle podminenosti se povazuje soustava za neurcenou.</param>
        public static bool TryFit(IReadOnlyList<Vector3> mag, double bRefG,
                                  out MagCalResult result, out double condition,
                                  IReadOnlyList<Vector3> acc = null,
                                  double maxCondition = MagCalThresholds.MaxCondition)
        {
            result = null;
            condition = double.PositiveInfinity;

            if (mag == null) throw new ArgumentNullException(nameof(mag));
            if (mag.Count < MinSamples)
                throw new ArgumentException($"Malo vzorku ({mag.Count} < {MinSamples}).", nameof(mag));
            if (!(bRefG > 0)) throw new ArgumentOutOfRangeException(nameof(bRefG));
            if (acc != null && acc.Count != mag.Count)
                throw new ArgumentException("acc musi mit stejny pocet prvku jako mag.", nameof(acc));

            // ⚠️ Normalizace vstupu je NUTNA. Bez ni jsou sloupce navrhove matice v jednotkach
            // G², G a 1, tedy o rady jinde — a podminenost by pak merila volbu jednotek, ne
            // geometrii dat, tedy presne to, co ma merit.
            double s = mag.Average(v => v.Length());
            if (!(s > 0)) throw new ArgumentException("Nulove pole.", nameof(mag));

            var d = Matrix<double>.Build.Dense(mag.Count, 10);
            for (int i = 0; i < mag.Count; i++)
            {
                double x = mag[i].X / s, y = mag[i].Y / s, z = mag[i].Z / s;
                d[i, 0] = x * x;      d[i, 1] = y * y;      d[i, 2] = z * z;
                d[i, 3] = 2 * x * y;  d[i, 4] = 2 * x * z;  d[i, 5] = 2 * y * z;
                d[i, 6] = 2 * x;      d[i, 7] = 2 * y;      d[i, 8] = 2 * z;
                d[i, 9] = 1;
            }

            // Homogenni soustava D·u = 0 → nejmensi singularni vektor.
            Svd<double> svd = d.Svd(true);
            var u = svd.VT.Row(9);

            // Podminenost pres NENULOVE smery (0..8). S[9] je ta, ktera MA byt nulova — kdyby
            // se delilo ji, vyslo by "spatne" i u perfektnich dat. Kdyz je male i S[8], soustava
            // neni urcena, a prave to chceme videt.
            condition = svd.S[8] > 0 ? svd.S[0] / svd.S[8] : double.PositiveInfinity;

            // ⚠️ Rozhodnout PODLE podminenosti, a to JESTE PRED rozkladem. Puvodne se
            // podminenost pocitala a nepouzila, takze rovinna rotace spadla az na nekladnem
            // vlastnim cisle — tedy vyjimkou misto cislem, a obsluha by na strance videla
            // "prolozeni selhalo" misto "podminenost 84, otacej dal".
            if (!(condition <= maxCondition)) return false;

            var A = Matrix<double>.Build.DenseOfArray(new[,] {
                { u[0], u[3], u[4] },
                { u[3], u[1], u[5] },
                { u[4], u[5], u[2] } });
            var v = Vector<double>.Build.DenseOfArray(new[] { u[6], u[7], u[8] });

            // Znak u je libovolny; pro odmocninu je potreba pozitivne definitni A.
            if (A.Evd(Symmetricity.Symmetric).EigenValues.Real().Minimum() < 0)
            {
                A = A.Multiply(-1.0);
                v = v.Multiply(-1.0);
            }

            // Stred elipsoidy z ∂/∂m [mᵀAm + 2vᵀm + c] = 0  →  A·b = −v
            // (v NORMALIZOVANYCH jednotkach, prepocet zpet do G je niz).
            var bn = A.Solve(v.Multiply(-1.0));

            // Symetricka pozitivne definitni odmocnina: C0 = V·diag(√λ)·Vᵀ
            var evd = A.Evd(Symmetricity.Symmetric);
            var lam = evd.EigenValues.Real();
            var eigenVectors = evd.EigenVectors;

            // Zaloha za podminenosti: prolozena kvadrika neni elipsoida (nekladna vlastni cisla),
            // tedy data nejsou rotace pole. Po kontrole podminenosti by se to stat nemelo, ale
            // odmocnina z nekladneho cisla je horsi nez "neurceno".
            if (lam.Minimum() <= 0) return false;

            var sqrtL = Matrix<double>.Build.DenseDiagonal(3, 3, i => Math.Sqrt(lam[i]));
            var C0 = eigenVectors * sqrtL * eigenVectors.Transpose();

            // Meritko: prumerna velikost po korekci ma byt bRefG.
            double k = mag.Average(m =>
            {
                var x = Vector<double>.Build.DenseOfArray(
                    new double[] { m.X / s - bn[0], m.Y / s - bn[1], m.Z / s - bn[2] });
                return (C0 * x).L2Norm();
            });
            if (!(k > 0)) return false;

            // Zpet do G: prolozeni bezelo na m/s, takze C se deli s a bias nasobi s.
            var C = C0.Multiply(bRefG / k / s);
            var b = bn.Multiply(s);

            // Zbytky se pocitaji uz zkalibrovanym vysledkem, takze staci pomocna instance.
            var pomocna = new MagCalResult(C, b, condition, 0, double.NaN, mag.Count);
            var velikosti = new List<double>(mag.Count);
            var sklony = acc == null ? null : new List<double>(mag.Count);
            for (int i = 0; i < mag.Count; i++)
            {
                var c = pomocna.Apply(mag[i]);
                velikosti.Add(c.Length());
                if (sklony == null) continue;

                // ⚠️ SKLON JE VELICINA SVETOVA, ne telesova. Pocitat ho z pole v ramci telesa
                // je chyba: kdyz se robot nakloni, "sklon" v telese se legitimne meni a rozptyl
                // vyjde v desitkach stupnu i u perfektni kalibrace. Proto se sklopi gravitaci:
                // sin(sklon) = m̂ · ĝ_dolu, kde ĝ_dolu = −acc/|acc| (akcelerometr v klidu meri −g).
                double an = acc[i].Length(), mn = c.Length();
                if (!(an > 0) || !(mn > 0)) { sklony.Add(double.NaN); continue; }
                double dot = -(c.X * acc[i].X + c.Y * acc[i].Y + c.Z * acc[i].Z) / (an * mn);
                sklony.Add(Math.Asin(Math.Clamp(dot, -1.0, 1.0)) * 180.0 / Math.PI);
            }

            double sdSklon = sklony == null ? double.NaN
                                            : Sd(sklony.Where(x => !double.IsNaN(x)).ToList());
            result = new MagCalResult(C, b, condition, Sd(velikosti), sdSklon, mag.Count);
            return true;
        }

        /// <summary>
        /// <b>O kolik stupnu se dve kalibrace lisi v OPRAVE KURZU</b> — maximum pres azimuty.
        ///
        /// <para><b>Proc ne rozdil dvanacti parametru:</b> ten se da vylozit jen s jejich
        /// kovarianci, kdezto „o kolik jinak by mi vysel kurz" je velicina, ktera zajima robota.
        /// Pouziva se na kontrolu shody prvni a druhe poloviny dat.</para>
        ///
        /// <para>⚠️ Tahle kontrola SAMA NESTACI — dve stejne degenerovana data se v podurcenem
        /// smeru shodnou taky. Musi platit i <see cref="MagCalResult.Condition"/>.</para>
        /// </summary>
        public static double HeadingDiffDeg(MagCalResult a, MagCalResult b, int bins = 24)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            if (bins < 1) throw new ArgumentOutOfRangeException(nameof(bins));

            double max = 0;
            for (int i = 0; i < bins; i++)
            {
                // Zkusebni surove mereni: kruh o polomeru 1 G ve vodorovne rovine.
                double f = 2 * Math.PI * i / bins;
                var m = new Vector3((float)Math.Cos(f), (float)Math.Sin(f), 0f);
                var ca = a.Apply(m);
                var cb = b.Apply(m);
                double rozdil = Math.Atan2(ca.Y, ca.X) - Math.Atan2(cb.Y, cb.X);
                while (rozdil > Math.PI) rozdil -= 2 * Math.PI;
                while (rozdil < -Math.PI) rozdil += 2 * Math.PI;
                max = Math.Max(max, Math.Abs(rozdil) * 180.0 / Math.PI);
            }
            return max;
        }

        private static double Sd(List<double> x)
        {
            if (x.Count < 2) return 0;
            double m = x.Average();
            return Math.Sqrt(x.Sum(v => (v - m) * (v - m)) / (x.Count - 1));
        }
    }
}
