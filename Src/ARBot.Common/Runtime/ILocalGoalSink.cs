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
        /// <param name="goalRadiusM">
        /// Polomer cilove zony [m]: dojet kamkoli do ni znamena, ze je cil dosazen.
        /// <c>NaN</c> (vychozi) = nechat na lokalni vrstve, co povazuje za beznou velikost mrkve.
        ///
        /// <para>Je to udaj o CILI, ne nastaveni planovani, a proto ho posila globalni vrstva:
        /// bezna mrkev je bod, ale <b>pri dojezdu do cile</b> se pouzije dojezdovy polomer. Bez
        /// nej byl cilem A* jediny bod, takze mrkev v trave nebo tesne u prekazky byla nedosazitelna
        /// jako celek — robot dojel k nejblizsi bezpecne bunce a tam ZASTAVIL, ackoli jina cast
        /// cilove zony dosazitelna byla. Viz doc/occupancy-and-local-planning.md.</para>
        /// </param>
        void SetGoal(double worldX, double worldY, double corridorWidthM = 0,
                     double goalRadiusM = double.NaN);

        /// <summary>Zrusi cil.</summary>
        void ClearGoal();
    }
}
