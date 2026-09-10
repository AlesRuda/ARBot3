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
        /// <b>Kolik vzorku smi vstoupit do samotneho prolozeni</b>; nad tim se rovnomerne redi
        /// (kazdy k-ty).
        ///
        /// <para><b>Nacpak.</b> Cena <c>Svd</c> nad matici <i>m</i>×10 roste v MathNetu
        /// <b>kvadraticke</b>, protoze se pocita plna matice <c>U</c> (<i>m</i>×<i>m</i>) —
        /// zmereno 8. 9. 2026: 1 200 vzorku 69 ms, 3 000 462 ms, 6 000 1 929 ms,
        /// 12 000 <b>7 729 ms</b>. A <see cref="MagCalCollector"/> proklada <b>1× za sekundu nad
        /// vsim, co dosud nasbiral</b>, pri 100–200 Hz z VN100 — takze by se mise po minute
        /// otaceni zadusila vlastnim prolozenim. Nad zaznamem venkovni jizdy (45 185 vzorku)
        /// prolozeni <b>nedobehlo vubec</b> (16 GB na <c>U</c>).</para>
        ///
        /// <para><b>Proc to nic nezkresli:</b> podminenost navrhove matice je na poctu vzorku
        /// <b>invariantni</b> — zmereno na tychz syntetickych datech 434,4 pro 60, 300, 1 200,
        /// 3 000, 6 000 i 12 000 vzorku (rozdil pod desetinu). Redi se tedy vec, ktera na poctu
        /// radku nezavisi. <b>Zbytky</b> (<see cref="MagCalResult.SdMagnitudeG"/>,
        /// <see cref="MagCalResult.SdInclinationDeg"/>) i <b>meritko</b> se pritom pocitaji dal
        /// nad <b>vsemi</b> vzorky — kvalita se ma posuzovat na vsech datech, redi se jen soustava.</para>
        ///
        /// <para>⚠️ Redit se musi <b>rovnomerne</b>, ne „prvnich N": pozdeji namerene naklony
        /// jsou prave ta cast dat, ktera soustavu urcuje.</para>
        /// </summary>
        public const int MaxFitSamples = 1500;

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
            => TryFit(mag, bRefG, out result, out condition, out _, acc, maxCondition);

        /// <summary>
        /// <see cref="TryFit(IReadOnlyList{Vector3}, double, out MagCalResult, out double, IReadOnlyList{Vector3}, double)"/>
        /// s <b>duvodem</b>, proc se neprolozilo — pro offline rozbor.
        ///
        /// <para>⚠️ Za podminenosti jsou jeste tri brany (nekladne vlastni cislo, tedy prolozena
        /// kvadrika neni elipsoida; nulove meritko) a z pouheho <c>false</c> se nepozna, ktera
        /// spadla. Presne to chybelo u ranniho vyjezdu 10. 9. 2026: podminenost 80 pod prahem,
        /// prolozeni pres to neurcene, a ze to bylo nekladne vlastni cislo, se jen odvozovalo.</para>
        /// </summary>
        /// <param name="duvod">Prazdny retezec pri uspechu, jinak ktera brana a s jakymi cisly.</param>
        public static bool TryFit(IReadOnlyList<Vector3> mag, double bRefG,
                                  out MagCalResult result, out double condition, out string duvod,
                                  IReadOnlyList<Vector3> acc = null,
                                  double maxCondition = MagCalThresholds.MaxCondition)
        {
            result = null;
            condition = double.PositiveInfinity;
            duvod = string.Empty;

            double s = Priprava(mag, bRefG, acc);

            // Rovnomerne redeni: krok tak, aby radku bylo nejvys MaxFitSamples. Viz jeho
            // dokumentace — cena Svd roste kvadraticky, kdezto podminenost je na poctu vzorku
            // invariantni, takze se redi vec, na ktere pocet radku nezalezi.
            int krok = mag.Count > MaxFitSamples ? (mag.Count + MaxFitSamples - 1) / MaxFitSamples : 1;
            int radku = (mag.Count + krok - 1) / krok;

            var d = Matrix<double>.Build.Dense(radku, 10);
            for (int r = 0; r < radku; r++)
            {
                int i = r * krok;
                double x = mag[i].X / s, y = mag[i].Y / s, z = mag[i].Z / s;
                d[r, 0] = x * x;      d[r, 1] = y * y;      d[r, 2] = z * z;
                d[r, 3] = 2 * x * y;  d[r, 4] = 2 * x * z;  d[r, 5] = 2 * y * z;
                d[r, 6] = 2 * x;      d[r, 7] = 2 * y;      d[r, 8] = 2 * z;
                d[r, 9] = 1;
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
            if (!(condition <= maxCondition))
            {
                duvod = $"podminenost {condition:G4} nad prahem {maxCondition:G4}";
                return false;
            }

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
            if (lam.Minimum() <= 0)
            {
                duvod = $"kvadrika neni elipsoida: vlastni cisla A [{lam[0]:G4}, {lam[1]:G4}, {lam[2]:G4}]"
                        + " (jedno nekladne -> paraboloid/hyperboloid; data v jednom smeru nezakrivuji)";
                return false;
            }

            var sqrtL = Matrix<double>.Build.DenseDiagonal(3, 3, i => Math.Sqrt(lam[i]));
            var C0 = eigenVectors * sqrtL * eigenVectors.Transpose();

            // Meritko: prumerna velikost po korekci ma byt bRefG.
            double k = mag.Average(m =>
            {
                var x = Vector<double>.Build.DenseOfArray(
                    new double[] { m.X / s - bn[0], m.Y / s - bn[1], m.Z / s - bn[2] });
                return (C0 * x).L2Norm();
            });
            if (!(k > 0)) { duvod = $"nulove meritko k = {k}"; return false; }

            // Zpet do G: prolozeni bezelo na m/s, takze C se deli s a bias nasobi s.
            var C = C0.Multiply(bRefG / k / s);
            var b = bn.Multiply(s);

            result = SeZbytky(C, b, condition, mag, acc);
            return true;
        }

        /// <summary>
        /// <b>Prolozeni samotne KOULE</b> — jen tvrde zelezo (posun stredu), mekke se
        /// nehleda. Ctyri nezname misto deseti.
        ///
        /// <para><b>Nacpak.</b> Odpovida na otazku, kterou <see cref="TryFit"/> polozit neumi:
        /// <i>lezi ta data vubec na NEJAKE kouli?</i> Kdyz koule sedi a elipsoida ne, je pole
        /// konzistentni a chybi jen naklon — pokyn zni „podloz robota vys". Kdyz nesedi ani
        /// koule, <b>menilo se behem mereni pole</b> a zadne otaceni to nespravi; obsluha musi
        /// robota postavit na jedno misto dal od kovu. Bez tehle rozlisovaci schopnosti posilal
        /// verdikt cloveka otacet i v pripade, kdy mu to nemohlo pomoct.</para>
        ///
        /// <para>Vysledek je <b>pouzitelny</b>, ne jen diagnosticky: <c>C</c> vyjde jako
        /// jednotkova matice krat meritko, takze jde zapsat do registru 23 jako kalibrace
        /// <b>jen tvrdeho zeleza</b>. Odstrani prvni harmonickou chyby kurzu, druhou (mekke
        /// zelezo) ne — u nasich dat ze 7. 9. 2026 tedy zhruba 27° z 52°.</para>
        ///
        /// <para>⚠️ <b>Ani koule se z rovinne rotace neurci</b> — na kruhu lezi nekonecne mnoho
        /// kouli a sloupec <c>x²+y²+z²</c> zdegeneruje na konstantu. Nejaky naklon je porad
        /// potreba, jen podstatne mensi nez u elipsoidy.</para>
        /// </summary>
        /// <param name="mag">Surova mereni pole [G].</param>
        /// <param name="bRefG">Referencni velikost pole [G] z registru 21.</param>
        /// <param name="result">Vysledek; <c>null</c>, kdyz je soustava neurcena.</param>
        /// <param name="condition">Podminenost navrhove matice — <b>vzdy vyplnena</b>.</param>
        /// <param name="acc">Gravitace ke kazdemu vzorku, kvuli rozptylu sklonu.</param>
        /// <param name="maxCondition">Nad touhle podminenosti je soustava neurcena.</param>
        public static bool TryFitSphere(IReadOnlyList<Vector3> mag, double bRefG,
                                        out MagCalResult result, out double condition,
                                        IReadOnlyList<Vector3> acc = null,
                                        double maxCondition = MagCalThresholds.MaxCondition)
        {
            result = null;
            condition = double.PositiveInfinity;

            double s = Priprava(mag, bRefG, acc);

            // Radky [ |m|², 2x, 2y, 2z, 1 ] — tataz normalizace i redeni jako u elipsoidy,
            // ze stejnych duvodu (jednotky a kvadraticka cena Svd).
            int krok = mag.Count > MaxFitSamples ? (mag.Count + MaxFitSamples - 1) / MaxFitSamples : 1;
            int radku = (mag.Count + krok - 1) / krok;

            var d = Matrix<double>.Build.Dense(radku, 5);
            for (int r = 0; r < radku; r++)
            {
                int i = r * krok;
                double x = mag[i].X / s, y = mag[i].Y / s, z = mag[i].Z / s;
                d[r, 0] = x * x + y * y + z * z;
                d[r, 1] = 2 * x;  d[r, 2] = 2 * y;  d[r, 3] = 2 * z;
                d[r, 4] = 1;
            }

            Svd<double> svd = d.Svd(true);
            var u = svd.VT.Row(4);

            // Jako u elipsoidy: podminenost pres NENULOVE smery (0..3); S[4] je ta, ktera MA
            // byt nulova.
            condition = svd.S[3] > 0 ? svd.S[0] / svd.S[3] : double.PositiveInfinity;
            if (!(condition <= maxCondition)) return false;

            // u[0] ≈ 0 znamena, ze prolozena kvadrika je ROVINA, ne koule — sloupec |m|² se
            // nepouzil. Deleni jim by dalo stred v nekonecnu.
            if (Math.Abs(u[0]) < 1e-12) return false;

            // Stred a polomer (v normalizovanych jednotkach) z |m − c|² = r².
            var cn = Vector<double>.Build.DenseOfArray(
                new[] { -u[1] / u[0], -u[2] / u[0], -u[3] / u[0] });
            double r2 = cn[0] * cn[0] + cn[1] * cn[1] + cn[2] * cn[2] - u[4] / u[0];
            if (!(r2 > 0)) return false;

            // Zpet do G: prolozeni bezelo na m/s, takze stred i polomer se nasobi s.
            double polomer = Math.Sqrt(r2) * s;
            if (!(polomer > 0)) return false;

            // Meritko na referenci z registru 21, ne na namereny polomer — stejny duvod jako
            // u elipsoidy: VPE porovnava |B| proti registru 21.
            double k = bRefG / polomer;

            // ⚠️ TRETI BRANA, bez ktere projde rovinna rotace. Podminenost ani zbytek ji
            // nechyti (zmereno: 538 a 0,0000 pri biasu vedle o 476 787 G) — prolozenim rovinne
            // elipsy je koule o poloměru v radu 10⁶, tedy skoro rovina, a normalizace tim
            // poloměrem srovna zbytek k nule. Viz MagCalThresholds.MaxSphereScale.
            if (!(k <= MagCalThresholds.MaxSphereScale) || !(k >= 1.0 / MagCalThresholds.MaxSphereScale))
                return false;

            var C = Matrix<double>.Build.DenseIdentity(3).Multiply(k);
            result = SeZbytky(C, cn.Multiply(s), condition, mag, acc);
            return true;
        }

        /// <summary>
        /// Spolecna kontrola vstupu obou prolozeni; vraci meritko normalizace.
        ///
        /// <para>⚠️ Normalizace je NUTNA. Bez ni jsou sloupce navrhove matice v jednotkach
        /// G², G a 1, tedy o rady jinde — a podminenost by pak merila volbu jednotek, ne
        /// geometrii dat, tedy presne to, co ma merit.</para>
        /// </summary>
        private static double Priprava(IReadOnlyList<Vector3> mag, double bRefG,
                                       IReadOnlyList<Vector3> acc)
        {
            if (mag == null) throw new ArgumentNullException(nameof(mag));
            if (mag.Count < MinSamples)
                throw new ArgumentException($"Malo vzorku ({mag.Count} < {MinSamples}).", nameof(mag));
            if (!(bRefG > 0)) throw new ArgumentOutOfRangeException(nameof(bRefG));
            if (acc != null && acc.Count != mag.Count)
                throw new ArgumentException("acc musi mit stejny pocet prvku jako mag.", nameof(acc));

            double s = mag.Average(v => v.Length());
            if (!(s > 0)) throw new ArgumentException("Nulove pole.", nameof(mag));
            return s;
        }

        /// <summary>
        /// Dopocita zbytky nad <b>vsemi</b> vzorky a slozi vysledek.
        ///
        /// <para>Zbytky se pocitaji uz zkalibrovanym vysledkem, takze staci pomocna instance.
        /// Nad <b>vsemi</b> vzorky zamerne, i kdyz se soustava redila: kvalita se ma posuzovat
        /// na vsech datech.</para>
        /// </summary>
        private static MagCalResult SeZbytky(Matrix<double> C, Vector<double> b, double condition,
                                             IReadOnlyList<Vector3> mag, IReadOnlyList<Vector3> acc)
        {
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
            return new MagCalResult(C, b, condition, Sd(velikosti), sdSklon, mag.Count);
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
