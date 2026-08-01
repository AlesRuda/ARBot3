using System;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Klasifikace sjizdnosti bunky polarniho gridu.
    /// </summary>
    public enum TraversabilityClass : byte
    {
        /// <summary>Neznamo - malo vzorku (pod tvrdou podlahou) nebo nedostatek dat.
        /// POZOR: Unknown != Free. Do kartezskeho occupancy se NESMI zapsat jako sjizdne.</summary>
        Unknown = 0,
        /// <summary>Sjizdna plocha.</summary>
        Free = 1,
        /// <summary>Prekazka / nesjizdne (prilis daleko od plochy, drsne nebo strme).</summary>
        Obstacle = 2,
    }

    /// <summary>
    /// Hranice radialniho prstence: vzdalenost v metrech a zaroven RADEK hloubkoveho obrazu, kde se
    /// tato hranice „lame" (referencni stredni sloupec, model rovne zeme). Radek umoznuje vykreslit
    /// grid primo pres depth snimek (azimut = skupina sloupcu, radialne = pasmo radku) bez samostatneho
    /// obrazku. <see cref="Row"/> = -1 pokud radek nelze urcit.
    /// </summary>
    public struct RadialEdge
    {
        /// <summary>Vzdalenost hranice [m].</summary>
        public float Range;
        /// <summary>Radek depth obrazu odpovidajici teto vzdalenosti (ref. stredni sloupec); -1 = neznamo.</summary>
        public int Row;

        public RadialEdge(float range, int row) { Range = range; Row = row; }
    }

    /// <summary>
    /// Jedna bunka polarniho gridu sjizdnosti. Souradnice jsou robot-centricke
    /// (X roste na vychod, Y na sever, Z nahoru; pocatek v referencnim bode robotu).
    /// </summary>
    public struct PolarCell
    {
        /// <summary>Pocet platnych bodu, ze kterych bunka vznikla.</summary>
        public int Count;
        /// <summary>Teziste bodu bunky - X [m].</summary>
        public float MeanX;
        /// <summary>Teziste bodu bunky - Y [m].</summary>
        public float MeanY;
        /// <summary>Prumerna vyska bodu [m] (vuci referencnimu bodu robotu).</summary>
        public float MeanZ;
        /// <summary>Drsnost = smerodatna odchylka vysky v bunce [m].</summary>
        public float StdZ;
        /// <summary>Nejvyssi bod v bunce [m] - relevantni pro kolizi/prujezd.</summary>
        public float MaxZ;
        /// <summary>Sub-bunkova vzdalenost nejblizsiho bodu bunky [m] (nabezna hrana prekazky).</summary>
        public float EdgeRange;
        /// <summary>Duvera ve vykazane hodnoty 0..1 (pocet vzorku x dosah x drsnost).
        /// Slouzi jako vaha pri agregaci do kartezskeho occupancy.</summary>
        public float Confidence;
        /// <summary>Klasifikace sjizdnosti.</summary>
        public TraversabilityClass Class;
    }

    /// <summary>
    /// Polarni grid sjizdnosti spocteny z hloubkoveho obrazu jedne kamery
    /// (<see cref="CameraFrameProcessor"/>). Grid je robot-centricky a per-kamera (kvuli redundanci
    /// pri vypadku kamery). Azimut je delen po skupinach <see cref="ColumnsPerCell"/> sloupcu obrazu,
    /// radialne dle <see cref="RadialEdges"/> (Δr od 5 cm rostouci, aby bunka drzela cilovy pocet bodu).
    ///
    /// <para>Na rozdil od predchozi implementace NENI samostatna <see cref="ARBot.Common.Logs.Message"/> -
    /// grid je nyni soucasti <see cref="ARBot.Common.Devices.CameraFrame.Grid"/> a (de)serializuje se
    /// spolu s ramcem (viz <see cref="ARBot.Common.Devices.CameraFrame.ToData"/>). Jmeno kamery i cas
    /// porizeni nese samotny <see cref="ARBot.Common.Devices.CameraFrame"/>.</para>
    ///
    /// Vystup slouzi jako podklad pro aktualizaci kartezskeho occupancy gridu (planovani cesty);
    /// <see cref="PolarCell.Confidence"/> je vaha te aktualizace.
    /// </summary>
    public sealed class PolarTraversabilityGrid
    {
        /// <summary>Pocet azimutovych bunek (= pouzitelna sirka / <see cref="ColumnsPerCell"/>).</summary>
        public int AzimuthCount;

        /// <summary>Pocet sloupcu obrazu na jednu azimutovou bunku (N).</summary>
        public int ColumnsPerCell;

        /// <summary>Radialni hrany (vzdalenost + radek), rostouci; delka = <see cref="RadialCount"/> + 1.</summary>
        public RadialEdge[] RadialEdges;

        /// <summary>Bunky (row-major): index = azimut * <see cref="RadialCount"/> + radius.</summary>
        public PolarCell[] Cells;

        /// <summary>DIAGNOSTIKA (NEserializuje se): doba vypoctu gridu [ms] - k odliseni "compute" od
        /// cekani / GC pauz (celkove stari Δ mereno az pri zobrazeni). Pri replay je 0.</summary>
        public double ComputeMs;

        /// <summary>Pocet radialnich prstencu.</summary>
        public int RadialCount => RadialEdges == null ? 0 : Math.Max(0, RadialEdges.Length - 1);

        /// <summary>Pristup k bunce podle azimutoveho a radialniho indexu.</summary>
        public PolarCell this[int azimuth, int radius] => Cells[azimuth * RadialCount + radius];
    }
}
