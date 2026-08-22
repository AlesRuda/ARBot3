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

        /// <summary>Rozdil sirky: kamera minus mapa (nebo filtr) [m].</summary>
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
                m.CorridorReason = (byte)Corridor.Reason;
            }
            return m;
        }
    }
}
