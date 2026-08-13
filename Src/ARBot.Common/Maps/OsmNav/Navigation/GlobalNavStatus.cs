namespace ARBot.Common.Maps.OsmNav.Navigation
{
    /// <summary>
    /// Stav globalni navigace (viz doc/global-navigation-runtime.md).
    /// Detekce zaseku (StuckNoMotion / StuckNoProgress / RoadBlocked) je faze 4.
    /// </summary>
    public enum GlobalNavStatus
    {
        /// <summary>Neni zadany cil.</summary>
        NoGoal = 0,

        /// <summary>Jede se k cili po siti.</summary>
        Driving = 1,

        /// <summary>Cil uz je uvnitr lokalni mapy - mrkev je primo cil.</summary>
        GoalInMap = 2,

        /// <summary>Cil dosazen (vzdalenost pozy od cile pod prahem).</summary>
        Arrived = 3,

        /// <summary>Robot je prilis daleko od site - mrkev je nejblizsi bod trasy.</summary>
        OffRoute = 4,

        /// <summary>K cili nevede po siti zadna cesta.</summary>
        NoRoute = 5,
    }
}
