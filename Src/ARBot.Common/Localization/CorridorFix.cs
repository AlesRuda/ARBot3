using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Jeden cyklus hranove lokalizace: koridor z kamer, osa cesty z mapy a rozdil obou stran —
    /// tedy to, co jde (nebo nejde) do fuze. Viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class CorridorFix
    {
        /// <summary>Cas snimku, ke kteremu vysledek plati.</summary>
        public DateTime Time;

        /// <summary>Koridor videny kamerami (muze byt null, kdyz se nenasel).</summary>
        public RoadCorridor Corridor;

        /// <summary>Osa cesty podle mapy vztazena k poze.</summary>
        public RoadAxisMatch Axis;

        /// <summary>Kurz robotu v case snimku [rad] - potreba k prevodu relativniho sklonu na absolutni.</summary>
        public double PoseTheta;

        /// <summary>
        /// Poloha robotu v case snimku [m, world ENU] — doplnek k <see cref="PoseTheta"/>.
        ///
        /// <para><b>Nacpak to je:</b> aby se prolozene usecky
        /// (<see cref="RoadCorridor.LeftFrom"/> a spol.) daly nakreslit do mapy <b>touz pozou,
        /// se kterou se merilo</b>. Vrstva ve World pohledu driv promitala vsechno „posledni
        /// znamou" pozou, coz za jizdy posouvalo hranice o desitky centimetru; a parovat zpravu
        /// s pozou podle razitka nejde, protoze to neprezije seek v zaznamu (rekonstrukce stavu
        /// dodava jednu zpravu na klic).</para>
        /// </summary>
        public double PoseX, PoseY;

        /// <summary>
        /// Je poza (<see cref="PoseX"/>, <see cref="PoseY"/>, <see cref="PoseTheta"/>) vyplnena?
        /// <c>false</c> = fuze pozu k casu snimku neznala (mimo okno historie).
        /// </summary>
        public bool HasPose;

        /// <summary>Sirka cesty, se kterou se srovnavalo [m] (z filtru, jinak z mapy).</summary>
        public double MapWidthM;

        /// <summary>Sirka po zapracovani tohoto merenia [m].</summary>
        public double FilteredWidthM;

        /// <summary>
        /// Rozdil pricne polohy: <b>kamera minus mapa</b> [m]. To je vlastni chyba lokalizace,
        /// kterou merenie opravuje.
        /// </summary>
        public double LateralDisagreement;

        /// <summary>Rozdil sklonu cesty: kamera minus mapa [rad].</summary>
        public double HeadingDisagreementRad;

        /// <summary>
        /// Rozdil sirky: kamera minus <see cref="MapWidthM"/> [m] — tedy proti <b>filtru</b>,
        /// jakmile ten uz pro hranu odhad ma; proti mape jen u prvniho merenia na hrane.
        ///
        /// <para><b>Necti to jako „nesouhlas s mapou"</b> (naměřeno 23. 8. 2026). Na ceste, ktera
        /// se skutecne rozsiruje, filtr za rampou trvale zaostava o <c>Δ/α</c> a prave tenhle
        /// odstup se tu objevi — i kdyz kamera meri spravne. V testovaci mape to delalo p50
        /// 0,23 m, zatimco proti mape kamera souhlasila na centimetry. Viz
        /// <c>RoadWidthFilterTests.NaRozsirujiciSeCeste_filtrTrvaleZaostava</c>
        /// a doc/map-correlation-localization.md.</para>
        /// </summary>
        public double WidthDisagreement;

        /// <summary>Poslala se pricna korekce?</summary>
        public bool EmittedLateral;

        /// <summary>Poslala se korekce kurzu?</summary>
        public bool EmittedHeading;

        /// <summary>Proc se z koridoru (ne)stalo merenie.</summary>
        public CorridorFixReason Reason;

        /// <summary>
        /// Kolik merenii z hranove lokalizace uz fuze zahodila jako starsi nez okno historie
        /// (kumulativne). Zpetna vazba z fuze, ne vysledek merení - doplnuje ji stupen.
        /// </summary>
        public long DroppedByFusion;

        /// <summary>Vzniklo merenie?</summary>
        public bool Ok => Reason == CorridorFixReason.Ok;

        /// <summary>
        /// Snapshot pro telemetrii a zaznam. Konverzi vlastni domena - zprava zustava pasivni DTO
        /// (viz CLAUDE.md).
        /// </summary>
        public Logs.RoadCorridorMsg ToLogMessage()
        {
            var m = new Logs.RoadCorridorMsg
            {
                TimeStamp = Time,
                MapLateral = Axis.Lateral,
                MapHeadingRelRad = Axis.HeadingRelRad,
                MapWidth = MapWidthM,
                FilteredWidth = FilteredWidthM,
                WayId = Axis.WayId,
                EdgeDistance = Axis.DistanceM,
                LateralDisagreement = LateralDisagreement,
                HeadingDisagreementRad = HeadingDisagreementRad,
                WidthDisagreement = WidthDisagreement,
                EmittedLateral = EmittedLateral,
                EmittedHeading = EmittedHeading,
                FixReason = (byte)Reason,
                DroppedByFusion = DroppedByFusion,

                HasPose = HasPose,                  // verze 5
                PoseX = PoseX,
                PoseY = PoseY,
                PoseTheta = PoseTheta,
            };
            // Bez koridoru (chybela druha kamera) by vychozi 0 znamenala "Ok" - to by v telemetrii
            // lhalo, proto vlastni hodnota.
            m.CorridorReason = (byte)Localization.CorridorReason.NotComputed;
            if (Corridor != null)
            {
                m.Width = Corridor.Width;
                m.Lateral = Corridor.Lateral;
                m.DirectionRad = Corridor.DirectionRad;
                m.SigmaLateral = Corridor.SigmaLateral;
                m.SigmaDirectionRad = Corridor.SigmaDirectionRad;
                m.ResidualLeft = Corridor.ResidualLeft;
                m.ResidualRight = Corridor.ResidualRight;
                m.InliersLeft = Corridor.InliersLeft;
                m.InliersRight = Corridor.InliersRight;
                m.ParallelErrorRad = Corridor.ParallelErrorRad;
                m.DirectionLeftRad = Corridor.DirectionLeftRad;
                m.DirectionRightRad = Corridor.DirectionRightRad;

                // Usecky prolozeni - i kdyz se cyklus zamitl (prave tehdy jsou nejzajimavejsi).
                m.HasLeftLine = Corridor.HasLeftLine;
                m.LeftFromX = Corridor.LeftFrom.X; m.LeftFromY = Corridor.LeftFrom.Y;
                m.LeftToX = Corridor.LeftTo.X; m.LeftToY = Corridor.LeftTo.Y;
                m.HasRightLine = Corridor.HasRightLine;
                m.RightFromX = Corridor.RightFrom.X; m.RightFromY = Corridor.RightFrom.Y;
                m.RightToX = Corridor.RightTo.X; m.RightToY = Corridor.RightTo.Y;
                m.CorridorReason = (byte)Corridor.Reason;
            }
            return m;
        }
    }
}
