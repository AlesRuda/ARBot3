using System;
using MathNet.Numerics.LinearAlgebra;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Jedno merenie senzoru pro EKF. Nese casovy okamzik poRIZENI (capture), namerenou
    /// hodnotu z, meRici model h(x), jeho jakobian H, kovarianci sumu R a vypocet rezidua
    /// (kvuli spravnemu zabaleni uhlu).
    /// </summary>
    public interface IMeasurement
    {
        /// <summary>Cas poRIZENI merenia (ne prichodu).</summary>
        DateTime TimeStamp { get; }

        /// <summary>Nazev zdroje pro logovani (napr. "GPS", "VN100/gyro").</summary>
        string Source { get; }

        /// <summary>Namerena hodnota z (sloupcovy vektor delky k).</summary>
        Vector<double> Value { get; }

        /// <summary>Merici model h(x) - ocekavane merenie pro stav x.</summary>
        Vector<double> Predict(Vector<double> x);

        /// <summary>Jakobian H = dh/dx (k x n).</summary>
        Matrix<double> Jacobian(Vector<double> x);

        /// <summary>Kovariance sumu merenia R (k x k).</summary>
        Matrix<double> NoiseCovariance { get; }

        /// <summary>
        /// Volitelny prah NIS pro gating. Kdyz je nastaven a NIS merenia ho prekroci,
        /// merenie se povazuje za odlehle a zahodi se (stav se nezmeni). null = bez gatingu.
        /// Prah lze ziskat z <see cref="Gating.ChiSquareThreshold"/>.
        /// </summary>
        double? GateThreshold { get; }

        /// <summary>Reziduum z - h(x) se spravnym zabalenim uhlu.</summary>
        Vector<double> Residual(Vector<double> z, Vector<double> hx);
    }
}
