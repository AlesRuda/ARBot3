namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Prijemce lokalniho cile - jedine pouto mezi globalni a lokalni navigacni vrstvou
    /// (viz doc/global-navigation-runtime.md). Zamerne lezi mimo <c>Occupancy</c> i <c>OsmNav</c>,
    /// aby na sobe ty dve vrstvy nezavisely: globalni vrstva nezna occupancy grid ani regulatory,
    /// lokalni nezna OSM. Diky tomu jde globalni vrstva testovat bez gridu i bez HW.
    /// </summary>
    public interface ILocalGoalSink
    {
        /// <summary>
        /// Nastavi cil lokalniho planovani [m, world ENU]. Volatelne z jineho vlakna.
        /// </summary>
        /// <param name="worldX">Cil na vychod [m].</param>
        /// <param name="worldY">Cil na sever [m].</param>
        /// <param name="corridorWidthM">
        /// Volitelna sirka koridoru cesty [m] v miste cile. Slouzi k testu "je cesta pres celou
        /// sirku prehrazena?" - ten musi probehnout na vlakne, ktere vlastni grid. 0 = neresit.
        /// </param>
        void SetGoal(double worldX, double worldY, double corridorWidthM = 0);

        /// <summary>Zrusi cil.</summary>
        void ClearGoal();
    }
}
