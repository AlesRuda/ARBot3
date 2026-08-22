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

        /// <summary>Smer cesty ve stupnich (pro cteni v telemetrii).</summary>
        public double DirectionDeg => DirectionRad * 180.0 / Math.PI;
    }
}
