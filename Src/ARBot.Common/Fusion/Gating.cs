using MathNet.Numerics.Distributions;

namespace ARBot.Common.Fusion
{
    /// <summary>
    /// Rezim gatingu odlehlych merenia.
    /// </summary>
    public enum GateMode
    {
        /// <summary>NIS &gt; prah -&gt; merenie se zahodi (stav se nezmeni).</summary>
        Reject = 0,
        /// <summary>
        /// NIS &gt; prah -&gt; merenie se nezahodi, ale nafoukne se mu R umerne prekroceni
        /// (R' = R * NIS/prah). Odlehle merenie se jen malo zvazi, nikdy nevypne -&gt;
        /// filtr se z dlouheho vypadku vzdy vzpamatuje. Bezstavove (slada se s replayem).
        /// </summary>
        Soft = 1
    }

    /// <summary>
    /// Pomocnik pro gating merenia pres NIS (Normalized Innovation Squared).
    /// NIS = dᵀ S⁻¹ d ma pri konzistentnim filtru chi-kvadrat rozdeleni se stupni volnosti
    /// rovnymi dimenzi mereni. Prah se voli jako kvantil tohoto rozdeleni.
    /// </summary>
    public static class Gating
    {
        /// <summary>
        /// Prah NIS = kvantil chi-kvadrat rozdeleni. Merenie s NIS nad prahem je odlehle.
        /// </summary>
        /// <param name="degreesOfFreedom">Dimenze merenia (napr. 1 pro rychlost, 2 pro polohu).</param>
        /// <param name="probability">Pravdepodobnost pokryti (napr. 0.95 nebo 0.99).</param>
        public static double ChiSquareThreshold(int degreesOfFreedom, double probability = 0.95)
        {
            return new ChiSquared(degreesOfFreedom).InverseCumulativeDistribution(probability);
        }
    }
}
