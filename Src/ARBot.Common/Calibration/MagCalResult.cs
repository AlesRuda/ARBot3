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
        /// <b>Prevod z naseho FLU ramce do ramce REGISTRU 23</b> — diagonala
        /// <c>diag(−1, −1, +1)</c>; je to involuce, takze plati i opacnym smerem.
        ///
        /// <para><b>Odkud:</b> registr 23 se aplikuje v ramci, ve kterem cidlo meri, tedy
        /// <b>pred</b> reference frame rotation (registr 26, u nas <c>diag(−1, 1, −1)</c>),
        /// kdezto nas fit bezi nad <c>IMUState.MagnetometerRaw</c>, ktere uz proslo registrem 26
        /// <b>i</b> prevodem FRD→FLU v driveru (<c>diag(1, −1, −1)</c>, viz
        /// <c>VN100IMUBinary.FrdToFlu</c>). Slozeni obou je <c>diag(−1, −1, +1)</c>.</para>
        ///
        /// <para>✅ <b>Zmereno na senzoru 11. 9. 2026</b>, ne jen odvozeno: cisty bias <c>+0,25</c>
        /// v jedne ose registru 23 posunul vystup o <b>+0,251 (X)</b>, <b>−0,250 (Y)</b>,
        /// <b>+0,249 (Z)</b>, krizove cleny pod 0,002 G. Mereni a odvozeni z kodu daly tutez
        /// diagonalu.</para>
        ///
        /// <para>⚠️ <b>Bez tehle transformace ma kalibrace obracene znamenko biasu v X a Y</b> —
        /// tedy hard-iron offset <b>pricita misto odecitani</b>, u prvni skutecne kalibrace
        /// o <b>0,22 G vodorovne</b>, coz je vic nez vodorovna slozka zemskeho pole (~0,20 G).
        /// Presne v tomhle stavu byla kalibrace nekolik hodin nasazena na robotu, nez se ramec
        /// zmeril. Viz doc/decisions.md, 11. 9. 2026.</para>
        ///
        /// <para>⚠️ <b>Plati pro NAS registr 26.</b> Kdyby se zmenila montaz cidla a s ni
        /// registr 26, musi se zmenit i tohle — proto to hlida <c>MagCalVnBiasTests</c> a proto
        /// to <b>neni skryte</b> uvnitr formatovani.</para>
        /// </summary>
        private static readonly double[] FluToSensor = { -1.0, -1.0, 1.0 };

        /// <summary>
        /// Dvanact cisel v poradi, v jakem je cte registr 23 (radky matice, pak bias) —
        /// ke zkopirovani za <c>VNWRG,23,</c>. <b>Prevedene do ramce cidla</b> pres
        /// <see cref="FluToSensor"/>: <c>C_s = T·C·T</c>, <c>b_s = T·B</c>.
        ///
        /// <para>✅ <b>Vzorec je ZMERENY na senzoru</b> (11. 9. 2026) <b>a zaroven dolozeny ICD</b>
        /// (VN100 ICD v3.1.0.0: <c>CalibratedMag = C · (MeasuredMag − B)</c>): VN aplikuje
        /// <c>C·(m − b)</c>, tedy <b>tentyz vzorec</b> jako <see cref="Apply"/>. Proto se
        /// <see cref="B"/> zapisuje <b>primo</b>. Mereni a dokument se shoduji.</para>
        ///
        /// <para>⚠️ <b>Merit se to musi pres VIC OS A s NEJEDNOTKOVOU matici</b>, jinak vyjde
        /// opak. Dve pasti, obe zazite tyz den:</para>
        /// <list type="number">
        /// <item><b>Jedna osa nestaci.</b> Cisty bias <c>(0,2; 0; 0)</c> posune vystup o
        ///   <b>+0,199 G</b>, ale <c>(0; 0,2; 0)</c> o <b>−0,202 G</b> — znamenko se lisi podle
        ///   osy, protoze mezi kompenzaci a vystupem lezi <b>reference frame rotation
        ///   (registr 26)</b>, u nas <c>diag(−1, 1, −1)</c>. Merena zmena je <c>−R·C·b</c>.
        ///   Z osy X samotne vyjde „VN bias pricita", coz je opak pravdy.</item>
        /// <item><b>Jednotkova matice nerozlisi <c>C·m − b</c> od <c>C·(m − b)</c></b> — pri
        ///   <c>C = I</c> jsou to tytez vzorce. Rozhodlo teprve <c>C[1,1] = 1,5</c> s
        ///   <c>b = (0; 0,2; 0)</c>: posun <b>−0,304 G</b> sedi na <c>−C·b</c> (−0,30), ne na
        ///   <c>−b</c> (−0,20).</item>
        /// </list>
        /// <para>Kazde mereni tri opakovani prolozena identitou, aby se vyrusil drift prostredi
        /// (~0,013 G za sekundy uvnitr budovy).</para>
        ///
        /// <para>⚠️ <b>Otevrene a NEZMERENE: v jakem RAMCI je <see cref="B"/>.</b> Registr 23 se
        /// aplikuje v ramci senzoru, tedy <b>pred</b> registrem 26, kdezto nas fit bezi nad
        /// <c>IMUState.MagnetometerRaw</c>, ktere uz proslo registrem 26 i prevodem FRD→FLU
        /// v driveru. Kdyby <c>UncompMag</c> bylo rotovane, musel by se bias (a mimodiagonalni
        /// cleny <c>C</c>) do senzoroveho ramce prevest. Pozna se to <b>jedine merenim kurzu
        /// proti GPS venku</b>; uvnitr budovy ne, tam se pole meni. Viz doc/decisions.md,
        /// 11. 9. 2026.</para>
        ///
        /// <para>⚠️ <b>Invariantni kultura je tu nutna, ne kosmeticka:</b> v ceskem prostredi by
        /// se desetinna CARKA dostala doprostred prikazu oddeleneho carkami a senzor by dostal
        /// dvakrat tolik parametru. Tataz past uz jednou kousla u registru 83
        /// (<c>{0:N3}</c> vkladalo oddelovac tisicu, viz VN100IMU.SetModelParams).</para>
        /// </summary>
        public string ToVnwrg23()
        {
            var t = FluToSensor;
            var c = new double[12];
            for (int i = 0; i < 3; i++)
            {
                // C_s = T·C·T: u diagonalni T staci soucin znamenek, tedy meni se prave ty cleny,
                // kde se indexy lisi ve znamenku (u nas sloupec/radek Z).
                for (int j = 0; j < 3; j++) c[i * 3 + j] = t[i] * C[i, j] * t[j];
                c[9 + i] = t[i] * B[i];
            }
            return string.Join(",", c.Select(v => v.ToString("F6", CultureInfo.InvariantCulture)));
        }

        public override string ToString()
            => string.Format(CultureInfo.InvariantCulture,
                "MagCal: podminenost {0:F1}, sd|B| {1:F5} G, sd sklonu {2:F2}°, vzorku {3}",
                Condition, SdMagnitudeG, SdInclinationDeg, Samples);
    }
}
