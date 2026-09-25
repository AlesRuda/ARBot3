using System;

namespace ARBot.Common.Localization
{
    /// <summary>Proc koridor (ne)vznikl. Jde do telemetrie, aby bylo videt PROC se nemeri.</summary>
    public enum CorridorReason : byte
    {
        /// <summary>Koridor je pouzitelny.</summary>
        Ok = 0,

        /// <summary>Malo hranicnich bodu - kamera jeste nedodala dost semantiky.</summary>
        TooFewPoints = 1,

        /// <summary>Nasla se jen jedna hranice - sirka ani osa se z toho nedaji urcit.</summary>
        OneSideOnly = 2,

        /// <summary>Hranice nejsou rovnobezne - nejde o koridor (falesna hrana, odbocka, stin).</summary>
        NotParallel = 3,

        /// <summary>Sirka mimo rozumny rozsah - nejspis se prolozila spatna dvojice hranic.</summary>
        WidthOutOfRange = 4,

        /// <summary>RANSAC nenasel dost inlieru, aby primka nesla nahodu.</summary>
        TooFewInliers = 5,

        /// <summary>
        /// Koridor se vubec nepocital (chybela druha kamera). Vlastni hodnota proto, aby
        /// telemetrie nehlasila „Ok" u cyklu, kde zadny koridor nebyl.
        /// </summary>
        NotComputed = 6,
    }

    /// <summary>Ktera hranice nese merenie z JEDNE hrany (<see cref="RoadCorridor.SingleSide"/>).</summary>
    public enum CorridorSide : byte
    {
        /// <summary>Zadna - koridor je oboustranny, nebo nevznikl vubec.</summary>
        None = 0,

        /// <summary>Jen leva hranice.</summary>
        Left = 1,

        /// <summary>Jen prava hranice.</summary>
        Right = 2,
    }

    /// <summary>
    /// Koridor cesty videny z jednoho okamziku: <b>sirka</b>, <b>pricna poloha robotu</b>
    /// a <b>odchylka osy cesty</b> — v ramci robotu (X vpred, Y vlevo).
    ///
    /// <para><b>Co je to za merenie.</b> Nese vztah robotu k cestě, ne polohu ve svete: kamera
    /// meri "jak jsem posunuty a stoceny vuci koridoru". Do polohy se to prevede az porovnanim
    /// s mapou (tam vstupuje poza). Diky tomu je pozorovani <b>nezavisle na odhadu pozy</b>, coz
    /// z nej dela poctive merenie pro fuzi — na rozdil od plosne korelace, ktera koreluje grid
    /// ukotveny prave tim odhadem. Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    public sealed class RoadCorridor
    {
        /// <summary>Sirka koridoru [m] (odstup obou hranic).</summary>
        public double Width;

        /// <summary>
        /// Pricna poloha robotu vuci ose koridoru [m]; <b>kladne = robot je vlevo od osy</b>
        /// (FLU, +Y vlevo).
        /// </summary>
        public double Lateral;

        /// <summary>Smer cesty v ramci robotu [rad]; 0 = cesta vede rovne vpred.</summary>
        public double DirectionRad;

        /// <summary>Odchylka smeru obou hranic [rad] - kontrola, ze jde skutecne o koridor.</summary>
        public double ParallelErrorRad;

        /// <summary>
        /// Smery LEVE a PRAVE hranice zvlast [rad], normalizovane na +-90 stupnu. Diagnostika:
        /// pri <see cref="CorridorReason.NotParallel"/> rekne, ktera strana je vedle - prumer
        /// v <see cref="DirectionRad"/> se v tom pripade vubec nespocita.
        /// </summary>
        public double DirectionLeftRad, DirectionRightRad;

        /// <summary>
        /// Prolozene primky jako <b>usecky</b> v ramci robotu - koncove body dane rozsahem inlieru
        /// (krajni inliery promitnute na primku). Levá: <see cref="LeftFrom"/> → <see cref="LeftTo"/>.
        ///
        /// <para><b>Nacpak to je.</b> Cisla o nerovnobeznosti rikaji ZE je neco spatne, ne CO.
        /// Usecky jde nakreslit do mapy a rovnou videt, kudy ta prolozeni vedou - i u cyklu, ktere
        /// se zamitly. Plni se proto <b>hned po prolozeni</b>, jeste pred jakoukoli kontrolou.
        /// Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public Point2D LeftFrom, LeftTo, RightFrom, RightTo;

        /// <summary>Je usecka leve/prave hranice vyplnena? (Prolozeni mohlo selhat uplne.)</summary>
        public bool HasLeftLine, HasRightLine;

        /// <summary>Odhad sigma pricne polohy [m] z rozptylu reziduí a poctu inlieru.</summary>
        public double SigmaLateral;

        /// <summary>Odhad sigma smeru [rad].</summary>
        public double SigmaDirectionRad;

        /// <summary>RMS rezidua bodu od prolozene primky [m] - leva a prava hranice.</summary>
        public double ResidualLeft, ResidualRight;

        /// <summary>Kolik bodu RANSAC pouzil (inliery) na kazde strane.</summary>
        public int InliersLeft, InliersRight;

        /// <summary>Kolik hranicnich bodu vubec vstoupilo.</summary>
        public int PointsLeft, PointsRight;

        /// <summary>Proc koridor (ne)vznikl.</summary>
        public CorridorReason Reason;

        /// <summary>Je koridor pouzitelny?</summary>
        public bool Ok => Reason == CorridorReason.Ok;

        /// <summary>
        /// <b>Merenie z JEDNE hrany</b> (od 24. 9. 2026, <see cref="CorridorConfig.SingleEdge"/>):
        /// oboustranny koridor nevznikl (<see cref="Reason"/> nese proc), ale jedna strana se
        /// prolozila dost spolehlive. Pak plati <see cref="DirectionRad"/> /
        /// <see cref="SigmaDirectionRad"/> (smer TE hrany) a <see cref="EdgeOffset"/>;
        /// <see cref="Width"/> ani <see cref="Lateral"/> se z jedne hrany urcit nedaji.
        ///
        /// <para><b>Proc.</b> Na siroke ceste (cyklostezka v Modranech, 23. 9. 2026) nevzniklo
        /// ze 4 zaznamu ani jedno oboustranne merenie — kamera tam vzdalenejsi hranici vidi
        /// ridce. Kurz z jedne hrany na sirce nezavisi vubec, pricna poloha pres predpokladanou
        /// sirku ano. ARBot2 jednu hranu pouzival taky. Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public CorridorSide SingleSide;

        /// <summary>Je vyplnene merenie z jedne hrany?</summary>
        public bool HasSingleEdge => SingleSide != CorridorSide.None;

        /// <summary>
        /// Znamenkovy odstup JEDINE hranice od robotu [m] podel leve normaly jejiho smeru;
        /// <b>kladne = hranice je vlevo od robotu</b>. U leve hranice ma tedy byt kladny, u prave
        /// zaporny.
        /// </summary>
        public double EdgeOffset;

        /// <summary>Sigma <see cref="EdgeOffset"/> [m] - z reziduí, s podlahou jako u koridoru.</summary>
        public double EdgeSigma;

        /// <summary>
        /// Pricna poloha robotu vuci ose cesty z jedine hrany a PREDPOKLADANE sirky
        /// <paramref name="widthM"/>; stejna konvence jako <see cref="Lateral"/> (+ = vlevo).
        /// Chyba sirky se do vysledku prenasi <b>polovinou</b>.
        /// </summary>
        public double SingleEdgeLateral(double widthM)
            => SingleSide == CorridorSide.Left ? widthM / 2 - EdgeOffset
             : SingleSide == CorridorSide.Right ? -EdgeOffset - widthM / 2
             : double.NaN;

        /// <summary>Smer cesty ve stupnich (pro cteni v telemetrii).</summary>
        public double DirectionDeg => DirectionRad * 180.0 / Math.PI;
    }
}
