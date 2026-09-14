using System;
using System.Collections.Generic;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;
using ARBot.Common.Logs;
using ARBot.Common.Missions;
using ARBot.Common.Maps.OsmNav.Navigation;

namespace ARBot.Common.Rendering
{
    /// <summary>
    /// <b>Jedna zóna, která má být dosažena</b> — místo mise nebo cíl navigace i s dojezdovým
    /// poloměrem. Souřadnice jsou <b>zeměpisné v radiánech</b> (konvence projektu, viz CLAUDE.md),
    /// takže si je každý spotřebitel převede do své roviny sám: půdorys do lokální ENU, mapa
    /// do Web Mercatoru.
    /// </summary>
    public readonly struct GoalZone
    {
        /// <summary>Střed zóny [rad].</summary>
        public readonly double LatRad, LonRad;

        /// <summary>Dojezdový poloměr [m] — jak blízko se robot musí dostat, aby se ohlásil dojezd.</summary>
        public readonly double RadiusM;

        /// <summary>Popisek do obrázku (pořadí místa, „depo", „cil", …); může být <c>null</c>.</summary>
        public readonly string Label;

        /// <summary>Je to ta zóna, ke které se právě jede?</summary>
        public readonly bool Active;

        public GoalZone(double latRad, double lonRad, double radiusM, string label, bool active)
        {
            LatRad = latRad;
            LonRad = lonRad;
            RadiusM = radiusM;
            Label = label;
            Active = active;
        }
    }

    /// <summary>
    /// <b>Které zóny se mají kreslit</b> — společný výběr pro všechny pohledy.
    ///
    /// <para><b>Proč to je zvlášť (14. 9. 2026):</b> logika vznikla 12. 9. uvnitř <c>WebStatus</c>,
    /// tedy jen pro stránku náhledu, a ve World pohledu Avalonie proto zóny <b>vidět nebyly</b>.
    /// Opsat ji podruhé by znamenalo opsat i pravidla, která si na sebe už jednou došlápla — že se
    /// kreslí místo <b>přichycené</b> na síť, a že se dva zdroje <b>nemíchají</b>. Obojí se pozná
    /// až na robotu a v druhé kopii by se to opravovalo znovu.</para>
    ///
    /// <para>Výstup je záměrně v LLA, ne v metrech: půdorys má počátek lokální roviny, mapa ne,
    /// a převádět tam a zpátky přes cizí počátek je zbytečná příležitost k chybě.</para>
    /// </summary>
    public static class GoalZones
    {
        /// <summary>
        /// Sestaví zóny z posledních zpráv. Prázdné pole = není co kreslit.
        ///
        /// <para><b>Zdrojem je MISE, když nějakou hlásí:</b> <see cref="TrackMsg"/> nese celý seznam
        /// míst, <see cref="MissionMsg"/> depo / nakládku / vykládku. Teprve když mise žádná místa
        /// nemá (FreeRun, <c>goal=</c> z příkazové řádky, běh bez mise), kreslí se <b>cíl globální
        /// navigace</b>.</para>
        ///
        /// <para>⚠️ <b>Ty dva zdroje se schválně NEMÍCHAJÍ:</b> cíl navigace je totiž místo mise
        /// <b>přichycené na síť cest</b>, takže by vedle sebe vyšly dvě kružnice pár metrů od sebe
        /// a nikdo by nevěděl, která je ta, na které záleží.</para>
        /// </summary>
        /// <param name="track">Poslední <see cref="TrackMsg"/>, nebo <c>null</c>.</param>
        /// <param name="mission">Poslední <see cref="MissionMsg"/>, nebo <c>null</c>.</param>
        /// <param name="nav">Poslední <see cref="GlobalNavMsg"/>, nebo <c>null</c>.</param>
        public static IReadOnlyList<GoalZone> Select(TrackMsg track, MissionMsg mission, GlobalNavMsg nav)
        {
            // Polomer z dat, dokud nedosel, vychozi nastaveni navigatoru (aby se neopisovalo cislo).
            double r = nav != null && nav.GoalRadiusM > 0
                       ? nav.GoalRadiusM
                       : NavigatorOptions.DefaultArrivalRadiusMeters;

            var zony = new List<GoalZone>();

            if (track?.AllLatitudes != null && track.AllLongitudes != null
                && track.AllLatitudes.Length > 0)
            {
                int n = Math.Min(track.AllLatitudes.Length, track.AllLongitudes.Length);
                for (int i = 0; i < n; i++)
                {
                    // ⚠️ Kresli se misto PRICHYCENE na sit, ne surovy bod ze souboru: robot jede
                    // na prumet a proti nemu se meri dojezd, takze zona u surove souradnice by
                    // ukazovala jinam, nez kam se jede - a vypadalo to, ze se neprichycuje vubec
                    // (nalez autora 13. 9. 2026). Surove misto zustava ve zprave, takze posun cile
                    // je ze zaznamu porad dohledatelny.
                    bool mamPrichycene = track.SnappedLatitudes != null
                                         && track.SnappedLongitudes != null
                                         && i < track.SnappedLatitudes.Length
                                         && i < track.SnappedLongitudes.Length
                                         && track.SnappedLatitudes[i] != 0;
                    double lat = mamPrichycene ? track.SnappedLatitudes[i] : track.AllLatitudes[i];
                    double lon = mamPrichycene ? track.SnappedLongitudes[i] : track.AllLongitudes[i];
                    zony.Add(new GoalZone(lat, lon, r,
                                          (i + 1).ToString(CultureInfo.InvariantCulture),
                                          active: i == track.PointIndex));
                }
            }
            else if (mission != null)
            {
                // MissionMsg nese stupne (okraj systemu smerem k QR kodum), TrackMsg radiany -
                // proto ten prevod jen tady. Viz CLAUDE.md, "zemepisne souradnice jsou v radianech".
                int faze = mission.Phase;
                if (mission.HasDepot)
                    zony.Add(new GoalZone(Conversions.Deg2Rad(mission.DepotLatDeg),
                                          Conversions.Deg2Rad(mission.DepotLonDeg), r, "depo",
                                          faze == (int)RobotourPhase.DrivingToDepot));
                if (mission.HasPickup)
                    zony.Add(new GoalZone(Conversions.Deg2Rad(mission.PickupLatDeg),
                                          Conversions.Deg2Rad(mission.PickupLonDeg), r, "nakladka",
                                          faze == (int)RobotourPhase.DrivingToPickup));
                if (mission.HasDrop)
                    zony.Add(new GoalZone(Conversions.Deg2Rad(mission.DropLatDeg),
                                          Conversions.Deg2Rad(mission.DropLonDeg), r, "vykladka",
                                          faze == (int)RobotourPhase.DrivingToDrop));
            }

            if (zony.Count == 0 && nav != null && nav.HasGoal)
                zony.Add(new GoalZone(Conversions.Deg2Rad(nav.GoalLatDeg),
                                      Conversions.Deg2Rad(nav.GoalLonDeg), r, "cil", active: true));

            return zony;
        }
    }
}
