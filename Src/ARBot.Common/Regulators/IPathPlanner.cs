namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Plánovač dráhy: z posloupnosti waypointů předpočítá geometrii rohů (kruhový oblouk z tolerance)
    /// a brzdnou obálku rychlosti (zpětný průchod) a vrátí <see cref="IRegulator"/> k řízení.
    /// Viz <c>doc/path-following.md</c>.
    /// </summary>
    public interface IPathPlanner
    {
        /// <summary>
        /// Naplánuje dráhu z waypointů. <see cref="RegulatorWayPoint.MaxPositionError"/> je tolerance
        /// průjezdu (ε), <see cref="RegulatorWayPoint.Speed"/> volitelný strop rychlosti v uzlu
        /// (u posledního waypointu požadovaná koncová rychlost, typicky 0 = zastavení).
        /// </summary>
        /// <param name="waypoints">Posloupnost waypointů (min. 2 body).</param>
        IRegulator Plan(RegulatorWayPoint[] waypoints);
    }
}
