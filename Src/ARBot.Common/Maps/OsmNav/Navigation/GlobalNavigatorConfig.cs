using System;

namespace ARBot.Common.Maps.OsmNav.Navigation
{
    /// <summary>
    /// Parametry globalni navigace (viz doc/global-navigation-runtime.md).
    /// Zadne konstanty v kodu - vse je tady.
    /// </summary>
    public sealed class GlobalNavigatorConfig
    {
        /// <summary>Jak casto se pocita globalni cyklus.</summary>
        public TimeSpan ReplanPeriod = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// Polovina hrany lokalni mapy [m] (occupancy grid 256 x 0,05 m = 12,8 m => 6,4).
        /// Predava se z konfigurace gridu, aby globalni vrstva nemusela znat occupancy.
        /// </summary>
        public double LocalMapHalfExtentM = 6.4;

        /// <summary>
        /// O kolik se lokalni mapa zmensi, nez se v ni hleda vystup trasy [m]. U kraje se bunky
        /// teprve "vsouvaji" pri precentrovani a EDT je tam orezana.
        /// </summary>
        public double CarrotMarginM = 0.5;

        /// <summary>
        /// Nad touto off-route vzdalenosti [m] prestava mit mrkev na hrane mapy smysl
        /// (mezi robotem a siti muze byt cokoli) - mrkev je pak nejblizsi bod trasy.
        /// </summary>
        public double OffRouteMaxM = 15.0;

        /// <summary>Perioda opakovaneho poslani geometrie trasy (<c>GraphNavigationMsg</c>).</summary>
        public TimeSpan RouteMessagePeriod = TimeSpan.FromSeconds(2);

        /// <summary>Polomer lokalni mapy zmenseny o okraj - v nem se hleda mrkev.</summary>
        public double CarrotHalfExtentM => Math.Max(0.1, LocalMapHalfExtentM - CarrotMarginM);
    }
}
