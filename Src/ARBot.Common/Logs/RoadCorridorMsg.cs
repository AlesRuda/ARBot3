using System;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava hranove lokalizace: koridor videny kamerami, osa cesty z mapy a rozdil obou
    /// stran — tedy to, co jde (nebo nejde) do fuze.
    ///
    /// <para><b>K cemu to je:</b> aby v telemetrii a v zaznamu bylo videt PROC se (ne)korigovalo,
    /// bez zapinani diagnostiky merenii. Past, kterou to ma vylucit, je stejna jako u plosne
    /// korelace: „duvod = Ok" svitilo i ve chvilich, kdy do fuze nedoslo nic.
    /// Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    [Serializable()]
    public class RoadCorridorMsg : Message, IHasCaptureTime
    {
        /// <summary>Cas snimku, ke kteremu vysledek plati.</summary>
        public DateTime TimeStamp;

        // --- koridor z kamer ---

        /// <summary>Merena sirka koridoru [m].</summary>
        public double Width;

        /// <summary>Merena pricna poloha robotu vuci ose koridoru [m]; kladne = vlevo.</summary>
        public double Lateral;

        /// <summary>Merený smer cesty v ramci robotu [rad].</summary>
        public double DirectionRad;

        /// <summary>Sigma pricne polohy [m] z rozptylu reziduí.</summary>
        public double SigmaLateral;

        /// <summary>Sigma smeru [rad].</summary>
        public double SigmaDirectionRad;

        /// <summary>RMS rezidua bodu od prolozene primky [m] - leva a prava hranice.</summary>
        public double ResidualLeft, ResidualRight;

        /// <summary>Kolik bodu RANSAC pouzil na kazde strane.</summary>
        public int InliersLeft, InliersRight;

        /// <summary>Proc koridor (ne)vznikl (<c>CorridorReason</c> jako byte).</summary>
        public byte CorridorReason;

        // --- mapa a rozdil ---

        /// <summary>Odstup pozy od osy hrany podle mapy [m]; kladne = vlevo.</summary>
        public double MapLateral;

        /// <summary>Sklon hrany vuci kurzu robotu podle mapy [rad].</summary>
        public double MapHeadingRelRad;

        /// <summary>Sirka, se kterou se srovnavalo [m] (z filtru, jinak z mapy).</summary>
        public double MapWidth;

        /// <summary>Sirka po zapracovani tohoto merenia [m].</summary>
        public double FilteredWidth;

        /// <summary>Id cesty (OSM way), ke ktere se merilo.</summary>
        public long WayId;

        /// <summary>Vzdalenost pozy od hrany [m].</summary>
        public double EdgeDistance;

        /// <summary><b>Kamera minus mapa</b> pricne [m] - vlastni chyba lokalizace.</summary>
        public double LateralDisagreement;

        /// <summary>Kamera minus mapa ve sklonu [rad].</summary>
        public double HeadingDisagreementRad;

        /// <summary>Kamera minus mapa v sirce [m].</summary>
        public double WidthDisagreement;

        /// <summary>Poslala se pricna korekce?</summary>
        public bool EmittedLateral;

        /// <summary>Poslala se korekce kurzu?</summary>
        public bool EmittedHeading;

        /// <summary>Proc se z koridoru (ne)stalo merenie (<c>CorridorFixReason</c> jako byte).</summary>
        public byte FixReason;

        /// <summary>
        /// Kolik merenii z hranove lokalizace uz fuze zahodila jako <b>starsi nez okno historie</b>
        /// (kumulativne za beh).
        ///
        /// <para><b>Proc to tu je:</b> <see cref="EmittedLateral"/> rika „poslali jsme", ne
        /// „doslo to". Presne na tuhle past se doplo pocitadlo u plosne korelace
        /// (<see cref="MapCorrelationMsg.DroppedByFusion"/>): telemetrie hlasila „duvod = Ok"
        /// i ve chvili, kdy do fuze nedochazelo nic. Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public long DroppedByFusion;

        /// <summary>
        /// O kolik se smery obou hranic lisi [rad] - kontrola „je to koridor". Nad
        /// <c>CorridorConfig.MaxParallelErrorRad</c> (10 stupnu) se cyklus zahodi jako
        /// <see cref="CorridorReason.NotParallel"/>.
        ///
        /// <para><b>Proc to je ve zprave</b> (doplneno 22. 8. 2026, verze 2): za jizdy propadalo
        /// <c>NotParallel</c> 132 z 174 sparovanych cyklu, zatimco u stojiciho robota ani jeden -
        /// a ze zaznamu neslo zjistit, jestli je odchylka tesne nad prahem, nebo o rad vedle.
        /// Bez teto hodnoty se ta otazka nedala zodpovedet jinak nez novym behem s ladicim vypisem.
        /// Viz doc/map-correlation-localization.md.</para>
        ///
        /// <para>Ve zpravach verze 1 chybi - nacte se jako 0.</para>
        /// </summary>
        public double ParallelErrorRad;

        /// <summary>
        /// Smery LEVE a PRAVE hranice zvlast [rad] - diagnostika k <see cref="ParallelErrorRad"/>:
        /// rekne, ktera strana je vedle. Ve zpravach verze &lt; 3 chybi (nactou se jako 0).
        /// </summary>
        public double DirectionLeftRad, DirectionRightRad;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public RoadCorridorMsg() : base("RoadCorridorMsg", 3)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            Write(bw, TimeStamp);
            bw.Write(Width);
            bw.Write(Lateral);
            bw.Write(DirectionRad);
            bw.Write(SigmaLateral);
            bw.Write(SigmaDirectionRad);
            bw.Write(ResidualLeft);
            bw.Write(ResidualRight);
            bw.Write(InliersLeft);
            bw.Write(InliersRight);
            bw.Write(CorridorReason);
            bw.Write(MapLateral);
            bw.Write(MapHeadingRelRad);
            bw.Write(MapWidth);
            bw.Write(FilteredWidth);
            bw.Write(WayId);
            bw.Write(EdgeDistance);
            bw.Write(LateralDisagreement);
            bw.Write(HeadingDisagreementRad);
            bw.Write(WidthDisagreement);
            bw.Write(EmittedLateral);
            bw.Write(EmittedHeading);
            bw.Write(FixReason);
            bw.Write(DroppedByFusion);
            bw.Write(ParallelErrorRad);     // verze 2
            bw.Write(DirectionLeftRad);     // verze 3
            bw.Write(DirectionRightRad);
        }

        public override void FromData(BinaryReader br)
        {
            TimeStamp = ReadDateTime(br);
            Width = br.ReadDouble();
            Lateral = br.ReadDouble();
            DirectionRad = br.ReadDouble();
            SigmaLateral = br.ReadDouble();
            SigmaDirectionRad = br.ReadDouble();
            ResidualLeft = br.ReadDouble();
            ResidualRight = br.ReadDouble();
            InliersLeft = br.ReadInt32();
            InliersRight = br.ReadInt32();
            CorridorReason = br.ReadByte();
            MapLateral = br.ReadDouble();
            MapHeadingRelRad = br.ReadDouble();
            MapWidth = br.ReadDouble();
            FilteredWidth = br.ReadDouble();
            WayId = br.ReadInt64();
            EdgeDistance = br.ReadDouble();
            LateralDisagreement = br.ReadDouble();
            HeadingDisagreementRad = br.ReadDouble();
            WidthDisagreement = br.ReadDouble();
            EmittedLateral = br.ReadBoolean();
            EmittedHeading = br.ReadBoolean();
            FixReason = br.ReadByte();
            DroppedByFusion = br.ReadInt64();

            // Verze 1 tenhle udaj nemela - starsi zaznamy se nactou s nulou.
            if (Verze >= 2)
                ParallelErrorRad = br.ReadDouble();

            if (Verze >= 3)
            {
                DirectionLeftRad = br.ReadDouble();
                DirectionRightRad = br.ReadDouble();
            }
        }

        public override Message Build() => new RoadCorridorMsg();
    }
}
