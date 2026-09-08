using System;
using System.Globalization;
using System.Linq;
using MathNet.Numerics.LinearAlgebra;

// ⚠️ System.Numerics se ZAMERNE neimportuje celý: `Vector<>` v nem koliduje s MathNet
// `Vector<T>` (CS0104). Alias to resi a `Vector<double>` tim jednoznacne miri do MathNet.
using Vector3 = System.Numerics.Vector3;

namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Vysledek prolozeni</b> — kompenzace magnetometru ve tvaru, v jakem ji ceka registr 23
    /// VN100: <c>m_comp = C · (m_raw − B)</c>.
    ///
    /// <para><see cref="C"/> je <b>symetricka</b> zamerne (viz <see cref="MagCalFit"/>): rozklad
    /// elipsoidy je jednoznacny jen na rotaci a fyzikalni volba je symetricke reseni. Kdyby
    /// symetricka nebyla, zbyl by po kalibraci konstantni posun kurzu, ktery se neda odlisit
    /// od deklinace.</para>
    ///
    /// <para>Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public sealed class MagCalResult
    {
        public MagCalResult(Matrix<double> c, Vector<double> b, double condition,
                            double sdMagnitudeG, double sdInclinationDeg, int samples)
        {
            C = c ?? throw new ArgumentNullException(nameof(c));
            B = b ?? throw new ArgumentNullException(nameof(b));
            if (c.RowCount != 3 || c.ColumnCount != 3)
                throw new ArgumentException("C musi byt 3x3.", nameof(c));
            if (b.Count != 3) throw new ArgumentException("B musi byt 3x1.", nameof(b));
            Condition = condition;
            SdMagnitudeG = sdMagnitudeG;
            SdInclinationDeg = sdInclinationDeg;
            Samples = samples;
        }

        /// <summary>Matice mekkeho zeleza 3×3 (symetricka, bezrozmerna).</summary>
        public Matrix<double> C { get; }

        /// <summary>Bias tvrdeho zeleza [G] — odecita se od suroveho mereni.</summary>
        public Vector<double> B { get; }

        /// <summary>
        /// Podminenost navrhove matice: <b>urcenost</b> soustavy, nutna podminka pouzitelnosti.
        /// Rotace na rovine (bez naklonu) da vysokou hodnotu — a je to spravne, protoze slozka
        /// <c>z</c> pak neni merena.
        /// </summary>
        public double Condition { get; }

        /// <summary>Rozptyl <c>|B|</c> po korekci [G] — <b>kvalita</b>, ne urcenost.</summary>
        public double SdMagnitudeG { get; }

        /// <summary>
        /// Rozptyl sklonu po korekci [deg]; <see cref="double.NaN"/>, kdyz se prokladalo
        /// <b>bez akcelerometru</b>.
        ///
        /// <para>⚠️ <b>Sklon je velicina SVETOVA, ne telesova</b> — pocita se sklopeny gravitaci.
        /// Z pole v ramci telesa by to byla chyba: kdyz se robot nakloni, „sklon" v telese se
        /// legitimne meni a rozptyl vyjde v desitkach stupnu i u perfektni kalibrace. (Takhle to
        /// bylo napsane a odhalil to teprve synteticky test s naklony.)</para>
        /// </summary>
        public double SdInclinationDeg { get; }

        /// <summary>Kolik vzorku se prokladalo.</summary>
        public int Samples { get; }

        /// <summary>Kompenzace jednoho mereni: <c>C · (m − B)</c>.</summary>
        public Vector3 Apply(Vector3 raw)
        {
            double x = raw.X - B[0], y = raw.Y - B[1], z = raw.Z - B[2];
            return new Vector3(
                (float)(C[0, 0] * x + C[0, 1] * y + C[0, 2] * z),
                (float)(C[1, 0] * x + C[1, 1] * y + C[1, 2] * z),
                (float)(C[2, 0] * x + C[2, 1] * y + C[2, 2] * z));
        }

        /// <summary>
        /// Dvanact cisel v poradi, v jakem je cte registr 23 (radky <see cref="C"/>, pak
        /// <see cref="B"/>) — ke zkopirovani za <c>VNWRG,23,</c>.
        ///
        /// <para>⚠️ <b>Invariantni kultura je tu nutna, ne kosmeticka:</b> v ceskem prostredi by
        /// se desetinna CARKA dostala doprostred prikazu oddeleneho carkami a senzor by dostal
        /// dvakrat tolik parametru. Tataz past uz jednou kousla u registru 83
        /// (<c>{0:N3}</c> vkladalo oddelovac tisicu, viz VN100IMU.SetModelParams).</para>
        /// </summary>
        public string ToVnwrg23()
        {
            var c = new[] { C[0, 0], C[0, 1], C[0, 2], C[1, 0], C[1, 1], C[1, 2],
                            C[2, 0], C[2, 1], C[2, 2], B[0], B[1], B[2] };
            return string.Join(",", c.Select(v => v.ToString("F6", CultureInfo.InvariantCulture)));
        }

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture,
                "MagCal: podminenost {0:F1}, sd|B| {1:F5} G, sd sklonu {2:F2}°, vzorku {3}",
                Condition, SdMagnitudeG, SdInclinationDeg, Samples);
    }
}
