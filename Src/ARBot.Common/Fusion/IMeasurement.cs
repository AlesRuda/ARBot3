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

        /// <summary>Rezim gatingu (tvrde zahozeni vs. mekke down-weight). Vyznam jen kdyz je nastaven GateThreshold.</summary>
        GateMode GateMode { get; }

        /// <summary>
        /// Volitelny <b>limit kroku filtru</b> podel osy merenia (v jednotkach merenia: metry
        /// u polohy, radiany u kurzu). null nebo 0 = bez limitu.
        ///
        /// <para>Kdyz by krok <c>K·ν</c> limit prekrocil, filtr nafoukne R prave tak, aby krok
        /// vysel na limit - merenie se NEZAHODI (to delalo vysledek horsi nez nekorigovat, zmereno
        /// 25. 8. 2026), jen se velka inovace rozlozi na vic merenii. Filtr zustane konzistentni:
        /// P se zmensi jen umerne tomu, co skutecne prijal, a zbytek inovace ceka na dalsi merenie.
        /// Plati jen pro <b>skalarni</b> merenia (k = 1); u vektorovych se ignoruje.
        /// Viz doc/map-correlation-localization.md („Limit kroku korekce").</para>
        /// </summary>
        double? MaxStep { get; }

        /// <summary>Reziduum z - h(x) se spravnym zabalenim uhlu.</summary>
        Vector<double> Residual(Vector<double> z, Vector<double> hx);
    }
}
