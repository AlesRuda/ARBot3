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

        // --- Detektor A: nehybu se ---

        /// <summary>Doba, po kterou se robot nesmi hnout, aby se to povazovalo za zasek.</summary>
        public TimeSpan NoMotionSec = TimeSpan.FromSeconds(10);

        /// <summary>Draha, pod kterou se za tu dobu povazuje robot za stojici [m].</summary>
        public double MinMotionM = 0.5;

        /// <summary>Cekani, nez se zasek zacne eskalovat (lokalni vrstva mozna prave dobrzduje).</summary>
        public TimeSpan EscalateSec = TimeSpan.FromSeconds(5);

        /// <summary>Kolikrat se zkusi zotaveni, nez se s hranou zachazi jako u prehrazeni.</summary>
        public int MaxRecoveries = 2;

        // --- Detektor B: nepostupuju k cili ---

        /// <summary>Delka okna postupu v ujete draze [m].</summary>
        public double ProgressWindowM = 20.0;

        /// <summary>
        /// Jaky zlomek ujete drahy se musi promitnout do priblizeni k cili.
        /// 0,3 = "staci, kdyz jsme se priblizili aspon tretinou toho, co jsme ujeli".
        /// </summary>
        public double ProgressGain = 0.3;

        /// <summary>Profilova rychlost [m/s] - prevadi pozadovany postup z metru na sekundy potencialu.</summary>
        public double ProfileSpeedMps = 1.0;

        /// <summary>Nasobek ceny hrany pri soft penalizaci - hrana se nezakaze, jen zdrazi.</summary>
        public double PenaltyFactor = 5.0;

        // --- Detektor C: cesta je prehrazena ---

        /// <summary>Kolik po sobe jdoucich neuspechu lokalniho planovani znamena prehrazeni.</summary>
        public int BlockedPlanCount = 20;

        // --- Zapominani uzavreni ---

        /// <summary>Po teto dobe se uzavreni meni na soft penalizaci (kdo vi, jestli tam prekazka je).</summary>
        public TimeSpan ClosureTtl = TimeSpan.FromSeconds(300);

        /// <summary>Po tolika potvrzenich je uzavreni trvale pro celou misi.</summary>
        public int MaxClosures = 2;

        /// <summary>Pozadovany pokles potencialu pres cele okno [s].</summary>
        public double RequiredPhiDrop => ProgressGain * ProgressWindowM / Math.Max(0.01, ProfileSpeedMps);

        /// <summary>Polomer lokalni mapy zmenseny o okraj - v nem se hleda mrkev.</summary>
        public double CarrotHalfExtentM => Math.Max(0.1, LocalMapHalfExtentM - CarrotMarginM);
    }
}
