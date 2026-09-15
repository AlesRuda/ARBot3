using System;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Genericka realizace rozsireneho Kalmanova filtru (EKF). Provadi jen vypocet
    /// (predikce + korekce), nezna vyznam stavu ani senzory. Konkretni model robota
    /// je odvozen v <see cref="EKFModel"/>.
    ///
    /// Predikce: x = f(x, dt);  P = F P Fᵀ + Q(dt)
    /// Korekce:  S = H P Hᵀ + R;  K = P Hᵀ S⁻¹;  x += K (z - h(x));  P = Joseph form
    /// </summary>
    public abstract class Ekf
    {
        /// <summary>Stredni hodnota stavu (sloupcovy vektor delky Dim).</summary>
        public Vector<double> X;
        /// <summary>Kovariance stavu (Dim x Dim).</summary>
        public Matrix<double> P;

        public int Dim => X.Count;

        protected Ekf(Vector<double> x0, Matrix<double> p0)
        {
            X = x0;
            P = p0;
        }

        // --- haky konkretniho modelu ---

        /// <summary>Predikcni funkce f(x, dt).</summary>
        protected abstract Vector<double> PredictState(Vector<double> x, double dt);
        /// <summary>Jakobian predikcni funkce F = df/dx.</summary>
        protected abstract Matrix<double> JacobianF(Vector<double> x, double dt);
        /// <summary>Kovariance procesniho sumu Q(dt) (zavisi na stavu kvuli orientaci).</summary>
        protected abstract Matrix<double> ProcessNoise(Vector<double> x, double dt);
        /// <summary>Normalizace stavu po kroku (napr. zabaleni orientace do +-pi).</summary>
        protected virtual void NormalizeState(Vector<double> x) { }

        // --- verejne API pracujici nad instancnim stavem ---

        public void Predict(double dt)
        {
            var r = PredictStep(X, P, dt);
            X = r.X; P = r.P;
        }

        /// <summary>NIS posledniho volani <see cref="Update"/>.</summary>
        public double LastNis { get; private set; }
        /// <summary>Zda bylo posledni merenie prijato (neproslo gatingem = false).</summary>
        public bool LastAccepted { get; private set; }

        public void Update(IMeasurement m)
        {
            var r = UpdateStep(X, P, m);
            X = r.X; P = r.P;
            LastNis = r.Nis;
            LastAccepted = r.Accepted;
        }

        // --- ciste kroky nad libovolnym (x, P) - potrebne pro replay a prune v engine ---

        /// <summary>Predikcni krok nad zadanym (x, P). Nemeni instancni stav.</summary>
        public (Vector<double> X, Matrix<double> P) PredictStep(Vector<double> x, Matrix<double> P, double dt)
        {
            if (dt <= 0)
                return (x.Clone(), P.Clone());
            var F = JacobianF(x, dt);
            var xn = PredictState(x, dt);
            NormalizeState(xn);
            var Pn = F * P * F.Transpose() + ProcessNoise(x, dt);
            return (xn, Pn);
        }

        /// <summary>Vysledek korekcniho kroku vcetne NIS a priznaku prijeti (gating).</summary>
        public struct UpdateResult
        {
            public Vector<double> X;
            public Matrix<double> P;
            /// <summary>Normalized Innovation Squared: dᵀ S⁻¹ d.</summary>
            public double Nis;
            /// <summary>False, kdyz merenie neproslo gatingem (stav ponechan beze zmeny).</summary>
            public bool Accepted;
        }

        /// <summary>
        /// Korekcni krok nad zadanym (x, P). Nemeni instancni stav. Spocte NIS; pokud ma merenie
        /// nastaven <see cref="IMeasurement.GateThreshold"/> a NIS ho prekroci, merenie se zahodi
        /// (vrati puvodni x, P a Accepted=false).
        /// </summary>
        public UpdateResult UpdateStep(Vector<double> x, Matrix<double> P, IMeasurement m)
        {
            var H = m.Jacobian(x);
            var R = m.NoiseCovariance;
            var hx = m.Predict(x);
            var y = m.Residual(m.Value, hx);
            var Ht = H.Transpose();
            var HPHt = H * P * Ht;
            var S = HPHt + R;
            var Sinv = S.Inverse();

            double nis = y.DotProduct(Sinv * y);

            // efektivni kovariance sumu merenia (muze se pri Soft gatingu nafouknout)
            var Reff = R;
            if (m.GateThreshold.HasValue && nis > m.GateThreshold.Value)
            {
                if (m.GateMode == GateMode.Reject)
                    return new UpdateResult { X = x, P = P, Nis = nis, Accepted = false };

                // GateMode.Soft: nafoukni R umerne prekroceni prahu (robustni down-weight)
                double w = nis / m.GateThreshold.Value;   // > 1
                Reff = R * w;
                S = HPHt + Reff;
                Sinv = S.Inverse();
            }

            var K = P * Ht * Sinv;
            var xn = x + K * y;
            NormalizeState(xn);
            // Joseph form kvuli numericke stabilite a zachovani symetrie/PSD
            var I = Matrix<double>.Build.DenseIdentity(x.Count);
            var IKH = I - K * H;
            var Pn = IKH * P * IKH.Transpose() + K * Reff * K.Transpose();

            // ⚠️ POJISTKA NA KONECNOST VYSLEDKU. Hodnota i R muzou byt v poradku a krok presto
            // vyrobi NaN/∞ - typicky singularni S (nulove R pri imuheadingstd=0 a YprU=0 ze
            // senzoru; MathNet u singularni matice nehazi, vraci nekonecna cisla), ale taky
            // degenerovany jakobian. NaN se pak zapece do checkpointu fuze a zpet uz cesta
            // nevede: kazdy dalsi dotaz na polohu vrati NaN a ridici smycka podle nej pocita.
            // Gating to nechyti - "nis > prah" je pro NaN nepravdive. Zamitnuti je bezpecne:
            // stav zustane takovy, jaky byl pred merenim.
            if (!JeKonecne(xn) || !JeKonecne(Pn))
                return new UpdateResult { X = x, P = P, Nis = nis, Accepted = false };

            return new UpdateResult { X = xn, P = Pn, Nis = nis, Accepted = true };
        }

        /// <summary>Je vektor cely konecny (bez NaN a ±∞)?</summary>
        private static bool JeKonecne(Vector<double> v)
        {
            for (int i = 0; i < v.Count; i++)
                if (!double.IsFinite(v[i])) return false;
            return true;
        }

        /// <summary>Je matice cela konecna (bez NaN a ±∞)?</summary>
        private static bool JeKonecne(Matrix<double> m)
        {
            for (int r = 0; r < m.RowCount; r++)
                for (int c = 0; c < m.ColumnCount; c++)
                    if (!double.IsFinite(m[r, c])) return false;
            return true;
        }
    }
}
