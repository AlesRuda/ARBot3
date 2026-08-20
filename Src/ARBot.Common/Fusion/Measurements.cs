using System;
using ARBot.Common.Common;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Merenie polohy [X, Y] (GPS, kamera pose, lokalni mapa).
    /// </summary>
    public class PositionMeasurement : IMeasurement
    {
        private readonly Vector<double> z;
        private readonly Matrix<double> r;
        public DateTime TimeStamp { get; }
        public string Source { get; }
        public double? GateThreshold { get; set; }
        public GateMode GateMode { get; set; } = GateMode.Reject;

        public PositionMeasurement(double x, double y, double stdX, double stdY, DateTime t, string source)
        {
            z = Vector<double>.Build.DenseOfArray(new[] { x, y });
            r = Matrix<double>.Build.DenseOfDiagonalArray(new[] { stdX * stdX, stdY * stdY });
            TimeStamp = t;
            Source = source;
        }

        public Vector<double> Value => z;
        public Matrix<double> NoiseCovariance => r;

        public Vector<double> Predict(Vector<double> x)
            => Vector<double>.Build.DenseOfArray(new[] { x[EKFModel.IX], x[EKFModel.IY] });

        public Matrix<double> Jacobian(Vector<double> x)
        {
            var H = Matrix<double>.Build.Dense(2, x.Count);
            H[0, EKFModel.IX] = 1;
            H[1, EKFModel.IY] = 1;
            return H;
        }

        public Vector<double> Residual(Vector<double> z, Vector<double> hx) => z - hx;
    }

    /// <summary>
    /// Merenie orientace theta (kompas z VN100, GPS kurz, kamera). Reziduum je zabaleno do +-pi.
    /// </summary>
    public class HeadingMeasurement : IMeasurement
    {
        private readonly Vector<double> z;
        private readonly Matrix<double> r;
        public DateTime TimeStamp { get; }
        public string Source { get; }
        public double? GateThreshold { get; set; }
        public GateMode GateMode { get; set; } = GateMode.Reject;

        public HeadingMeasurement(double theta, double std, DateTime t, string source)
        {
            z = Vector<double>.Build.Dense(1, Conversions.NormalizeOrientation(theta));
            r = Matrix<double>.Build.Dense(1, 1, std * std);
            TimeStamp = t;
            Source = source;
        }

        public Vector<double> Value => z;
        public Matrix<double> NoiseCovariance => r;

        public Vector<double> Predict(Vector<double> x)
            => Vector<double>.Build.Dense(1, x[EKFModel.ITh]);

        public Matrix<double> Jacobian(Vector<double> x)
        {
            var H = Matrix<double>.Build.Dense(1, x.Count);
            H[0, EKFModel.ITh] = 1;
            return H;
        }

        public Vector<double> Residual(Vector<double> z, Vector<double> hx)
            => Vector<double>.Build.Dense(1, Conversions.NormalizeOrientation(z[0] - hx[0]));
    }

    /// <summary>
    /// Merenie jedne skalarni slozky stavu podle indexu (rychlost v, uhlova rychlost omega).
    /// Pouzij tovarni metody <see cref="Velocity"/> a <see cref="AngularRate"/>.
    /// </summary>
    public class ScalarStateMeasurement : IMeasurement
    {
        private readonly int idx;
        private readonly Vector<double> z;
        private readonly Matrix<double> r;
        public DateTime TimeStamp { get; }
        public string Source { get; }
        public double? GateThreshold { get; set; }
        public GateMode GateMode { get; set; } = GateMode.Reject;

        public ScalarStateMeasurement(int stateIndex, double value, double std, DateTime t, string source)
        {
            idx = stateIndex;
            z = Vector<double>.Build.Dense(1, value);
            r = Matrix<double>.Build.Dense(1, 1, std * std);
            TimeStamp = t;
            Source = source;
        }

        public Vector<double> Value => z;
        public Matrix<double> NoiseCovariance => r;

        public Vector<double> Predict(Vector<double> x) => Vector<double>.Build.Dense(1, x[idx]);

        public Matrix<double> Jacobian(Vector<double> x)
        {
            var H = Matrix<double>.Build.Dense(1, x.Count);
            H[0, idx] = 1;
            return H;
        }

        public Vector<double> Residual(Vector<double> z, Vector<double> hx) => z - hx;

        /// <summary>Merenie rychlosti v (odometrie, GPS speed, kamera).</summary>
        public static ScalarStateMeasurement Velocity(double v, double std, DateTime t, string source)
            => new ScalarStateMeasurement(EKFModel.IV, v, std, t, source);

        /// <summary>Merenie uhlove rychlosti omega (gyro, odometrie, kamera).</summary>
        public static ScalarStateMeasurement AngularRate(double w, double std, DateTime t, string source)
            => new ScalarStateMeasurement(EKFModel.IW, w, std, t, source);
    }

    /// <summary>
    /// Sdruzene merenie pozy [X, Y, theta] (napr. z kamery), zachovava korelaci mezi slozkami.
    /// Reziduum orientace je zabaleno do +-pi.
    /// </summary>
    public class PoseMeasurement : IMeasurement
    {
        private readonly Vector<double> z;
        private readonly Matrix<double> r;
        public DateTime TimeStamp { get; }
        public string Source { get; }
        public double? GateThreshold { get; set; }
        public GateMode GateMode { get; set; } = GateMode.Reject;

        public PoseMeasurement(double x, double y, double theta, double stdX, double stdY, double stdTheta, DateTime t, string source)
        {
            z = Vector<double>.Build.DenseOfArray(new[] { x, y, Conversions.NormalizeOrientation(theta) });
            r = Matrix<double>.Build.DenseOfDiagonalArray(new[] { stdX * stdX, stdY * stdY, stdTheta * stdTheta });
            TimeStamp = t;
            Source = source;
        }

        public Vector<double> Value => z;
        public Matrix<double> NoiseCovariance => r;

        public Vector<double> Predict(Vector<double> x)
            => Vector<double>.Build.DenseOfArray(new[] { x[EKFModel.IX], x[EKFModel.IY], x[EKFModel.ITh] });

        public Matrix<double> Jacobian(Vector<double> x)
        {
            var H = Matrix<double>.Build.Dense(3, x.Count);
            H[0, EKFModel.IX] = 1;
            H[1, EKFModel.IY] = 1;
            H[2, EKFModel.ITh] = 1;
            return H;
        }

        public Vector<double> Residual(Vector<double> z, Vector<double> hx)
            => Vector<double>.Build.DenseOfArray(new[]
            {
                z[0] - hx[0],
                z[1] - hx[1],
                Conversions.NormalizeOrientation(z[2] - hx[2])
            });
    }

    /// <summary>
    /// Merenie polohy PODEL JEDNE OSY: <c>h(x) = u . p</c>, kde <c>u</c> je jednotkovy vektor osy
    /// a <c>p = (X, Y)</c>. Viz doc/map-correlation-localization.md.
    ///
    /// <para><b>K cemu:</b> korelace s mapou zna polohu dobre v jednom smeru a spatne v kolmem
    /// (na prime ceste napric ano, podel ne). <see cref="PositionMeasurement"/> ma R jen
    /// DIAGONALNI v osach sveta, takze otocenou anizotropni kovarianci nepobere. Dve tato merenia
    /// po vlastnich osach kovariance to resi exaktne - a "podel nevim" je vyjadreno tim, ze se ta
    /// osa posle s velkou sigmou nebo vubec, ne trikem s nekonecnem.</para>
    /// </summary>
    public class AxisOffsetMeasurement : IMeasurement
    {
        private readonly Vector<double> z;
        private readonly Matrix<double> r;
        private readonly double ax;
        private readonly double ay;

        public DateTime TimeStamp { get; }
        public string Source { get; }
        public double? GateThreshold { get; set; }
        public GateMode GateMode { get; set; } = GateMode.Reject;

        /// <param name="axisX">Slozka osy na vychod (nemusi byt normovana).</param>
        /// <param name="axisY">Slozka osy na sever (nemusi byt normovana).</param>
        /// <param name="value">Namerena projekce polohy na osu [m].</param>
        /// <param name="std">Sigma merenia [m].</param>
        /// <param name="t">Cas porizeni.</param>
        /// <param name="source">Nazev zdroje pro logovani.</param>
        public AxisOffsetMeasurement(double axisX, double axisY, double value, double std,
                                     DateTime t, string source)
        {
            double len = Math.Sqrt(axisX * axisX + axisY * axisY);
            if (!(len > 0) || double.IsNaN(len) || double.IsInfinity(len))
                throw new ArgumentException("Osa merenia musi byt nenulovy konecny vektor.", nameof(axisX));

            ax = axisX / len;
            ay = axisY / len;
            z = Vector<double>.Build.Dense(1, value);
            r = Matrix<double>.Build.Dense(1, 1, std * std);
            TimeStamp = t;
            Source = source;
        }

        public Vector<double> Value => z;
        public Matrix<double> NoiseCovariance => r;

        public Vector<double> Predict(Vector<double> x)
            => Vector<double>.Build.Dense(1, ax * x[EKFModel.IX] + ay * x[EKFModel.IY]);

        public Matrix<double> Jacobian(Vector<double> x)
        {
            var H = Matrix<double>.Build.Dense(1, x.Count);
            H[0, EKFModel.IX] = ax;
            H[0, EKFModel.IY] = ay;
            return H;
        }

        public Vector<double> Residual(Vector<double> z, Vector<double> hx) => z - hx;
    }
}
