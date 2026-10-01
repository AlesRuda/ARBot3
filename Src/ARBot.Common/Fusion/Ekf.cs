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

        /// <summary>
        /// Indexy polohy (x, y) ve stavu - pro <see cref="IMeasurement.MaxPositionStep"/>.
        /// <c>null</c> = model polohu nema a limit posunu polohy se ignoruje.
        /// </summary>
        protected virtual (int X, int Y)? PositionIndices => null;

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
        /// <summary>Nafouknuti R u posledniho merenia (Soft gate a/nebo limit kroku); 1 = beze zmeny.</summary>
        public double LastInflation { get; private set; } = 1;
        /// <summary>Zda posledni merenie narazilo na <see cref="IMeasurement.MaxStep"/>.</summary>
        public bool LastStepLimited { get; private set; }

        public void Update(IMeasurement m)
        {
            var r = UpdateStep(X, P, m);
            X = r.X; P = r.P;
            LastNis = r.Nis;
            LastAccepted = r.Accepted;
            LastInflation = r.Inflation;
            LastStepLimited = r.StepLimited;
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
            /// <summary>
            /// Kolikrat se R nafouklo proti tomu, co merenie hlasilo (Soft gate, limit kroku);
            /// 1 = beze zmeny. U vektoroveho merenia pomer prvnich diagonalnich prvku.
            /// </summary>
            public double Inflation;
            /// <summary>True, kdyz krok narazil na <see cref="IMeasurement.MaxStep"/> a R se kvuli nemu nafouklo.</summary>
            public bool StepLimited;
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
                    return new UpdateResult { X = x, P = P, Nis = nis, Accepted = false, Inflation = 1 };

                // GateMode.Soft: nafoukni R umerne prekroceni prahu (robustni down-weight)
                double w = nis / m.GateThreshold.Value;   // > 1
                Reff = R * w;
                S = HPHt + Reff;
                Sinv = S.Inverse();
            }

            // LIMIT KROKU (IMeasurement.MaxStep, 21. 9. 2026). Krok stavu podel osy skalarniho
            // merenia je s = P_h/(P_h + R)·ν, kde P_h = H·P·Hᵀ. Kdyz |s| > L, nafoukne se R prave
            // tak, aby |s| = L:  R' = P_h·(|ν|/L − 1). Je to tataz cesta jako Soft gate (nafouknuti
            // R, ne zahozeni), jen s jinym kriteriem: Soft se pta „jak moc je merenie odlehle",
            // limit „o kolik smi poza uhnout na jedno merenie". Sklada se s nim maximem, takze
            // plati prisnejsi z obou. Zbytek inovace filtr NEZTRATI - P se zmensi jen umerne
            // prijate casti (Josephova forma nize pocita s Reff), takze dalsi merenie tahne dal.
            //
            // Nacpak: na Robotouru 19. 9. 2026 stahl koridor nahromadeny drift 4-6 m v jednom kroku
            // a robot se skokem ocitl v blokovane casti gridu (doc/map-correlation-localization.md).
            // Jen pro k = 1 (koridor, kurz): uzavreny tvar; u vektoroveho merenia se limit
            // ignoruje, protoze „krok" by se musel definovat po osach.
            bool stepLimited = false;
            if (m.MaxStep is double lim && lim > 0 && y.Count == 1)
            {
                double ph = HPHt[0, 0];
                double nu = Math.Abs(y[0]);
                double krok = ph / (ph + Reff[0, 0]) * nu;
                if (krok > lim)
                {
                    double rMin = ph * (nu / lim - 1);
                    Reff = Matrix<double>.Build.Dense(1, 1, Math.Max(rMin, Reff[0, 0]));
                    S = HPHt + Reff;
                    Sinv = S.Inverse();
                    stepLimited = true;
                }
            }

            // LIMIT POSUNU POLOHY (IMeasurement.MaxPositionStep, 29. 9. 2026). Limit vyse hlida jen
            // krok podel osy merenia. Stav se ale meni o K·ν = P·Hᵀ·ν/(P_h + R) ve VSECH slozkach:
            // pricne merenie koridoru posune pres vazbu v P i polohu podel cesty, merenie kurzu
            // polohu vubec. Posun polohy je |g|·|ν|/(P_h + R), g = radky polohy z P·Hᵀ; kdyz
            // prekroci limit L, nafoukne se R na |g|·|ν|/L − P_h (tataz cesta jako vyse, sklada se
            // maximem). Nacpak: Track 27. 9. 2026 17:26:01 - jedno pricne merenie posunulo pozu
            // o 3,98 m podel hrany a o 0,18 m kolmo, ackoli limit podel osy byl 0,25 m.
            if (m.MaxPositionStep is double limPos && limPos > 0 && y.Count == 1
                && PositionIndices is (int ix, int iy))
            {
                var pht = P * Ht;
                double gx = pht[ix, 0], gy = pht[iy, 0];
                double g = Math.Sqrt(gx * gx + gy * gy);
                double ph = HPHt[0, 0];
                double nu = Math.Abs(y[0]);
                double posun = g * nu / (ph + Reff[0, 0]);
                if (posun > limPos)
                {
                    double rMin = g * nu / limPos - ph;
                    Reff = Matrix<double>.Build.Dense(1, 1, Math.Max(rMin, Reff[0, 0]));
                    S = HPHt + Reff;
                    Sinv = S.Inverse();
                    stepLimited = true;
                }
            }
            double inflation = R[0, 0] > 0 ? Reff[0, 0] / R[0, 0] : 1;

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
                return new UpdateResult { X = x, P = P, Nis = nis, Accepted = false, Inflation = 1 };

            return new UpdateResult { X = xn, P = Pn, Nis = nis, Accepted = true,
                                      Inflation = inflation, StepLimited = stepLimited };
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
