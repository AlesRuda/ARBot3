using ARBot.Common.Models;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Regulátor pohybu robota k cíli. Cíl (jeden bod nebo celá dráha) drží regulátor <b>uvnitř</b>;
    /// každý tik řídicí smyčky se zavolá <see cref="Control"/>, který robota z aktuální pózy dovede k cíli.
    /// Sjednocené rozhraní pro bodovou regulaci (<see cref="PointRegulator"/>) i sledování dráhy
    /// (<see cref="PathResult"/>) — nižší řídicí smyčka (<c>ControlLoop.Regulator</c>) je používá
    /// transparentně. Viz <c>doc/path-following.md</c>.
    /// </summary>
    /// <remarks>
    /// Instance je typicky <b>stavová</b> (drží cíl, případně progres na trase) a určená pro jednoho
    /// konzumenta (nižší smyčku); není thread-safe. Změna cíle = výměna instance (atomicky přes
    /// <c>ControlLoop.Regulator</c>). Kinematiku (accel-limitované zásahy) řeší <see cref="IMotionProfile"/>.
    /// </remarks>
    public interface IRegulator
    {
        /// <summary>
        /// Spočte řídicí zásah (dopredná a rotační rychlost) pro daný stav robota.
        /// </summary>
        /// <param name="state">Aktuální stav robota (póza + rychlosti), typicky z EKF.</param>
        RegulatorResult Control(IModelState state);

        /// <summary>True, pokud robot dosáhl cíle (v rámci tolerance).</summary>
        bool IsFinished { get; }
    }
}
