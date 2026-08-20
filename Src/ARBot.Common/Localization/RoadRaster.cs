using System;
using ARBot.Common.Maps.OsmNav.Graph;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Vozovka podle mapy predpocitana do bitoveho pole se STEJNYM rozlisenim a zarovnanim jako
    /// occupancy grid, rozsirena o marzi (aby kandidat skenovani nesahal mimo).
    /// Viz doc/map-correlation-localization.md.
    ///
    /// <para>Duvod existence: jeden cyklus korelace vyhodnoti stovky kandidatu a kazdy se pta na
    /// tisice bunek. Prostorovy dotaz <see cref="RoadScene.IsRoad"/> se proto zaplati JEDNOU za
    /// cyklus a dal se uz jen indexuje do pole.</para>
    ///
    /// <para><b>Mimo rastr neznamena "neni cesta".</b> <see cref="TryIsRoad"/> vraci <c>false</c>
    /// a volajici takovy dukaz PRESKOCI - jinak by okraj rastru systematicky tlacil odhad dovnitr.</para>
    /// </summary>
    public sealed class RoadRaster
    {
        private readonly byte[] bits;

        /// <summary>Pocet bunek na stranu.</summary>
        public int Size { get; }

        /// <summary>Velikost bunky [m] (stejna jako u gridu).</summary>
        public double Resolution { get; }

        /// <summary>Absolutni index nejzapadnejsiho sloupce rastru.</summary>
        public int OriginX { get; }

        /// <summary>Absolutni index nejjiznejsiho radku rastru.</summary>
        public int OriginY { get; }

        private RoadRaster(byte[] bits, int size, double resolution, int originX, int originY)
        {
            this.bits = bits;
            Size = size;
            Resolution = resolution;
            OriginX = originX;
            OriginY = originY;
        }

        /// <summary>
        /// Vyhodnoti <see cref="RoadScene.IsRoad"/> ve stredech bunek na oblasti gridu rozsirene
        /// o <paramref name="marginM"/> na kazdou stranu.
        /// </summary>
        /// <param name="scene">Mapova pravda.</param>
        /// <param name="gridOriginX">Absolutni index nejzapadnejsiho sloupce GRIDU.</param>
        /// <param name="gridOriginY">Absolutni index nejjiznejsiho radku GRIDU.</param>
        /// <param name="gridSize">Pocet bunek gridu na stranu.</param>
        /// <param name="resolution">Velikost bunky [m].</param>
        /// <param name="marginM">Marze za hranu gridu [m]; musi byt >= max. posun kandidata.</param>
        public static RoadRaster Build(RoadScene scene, int gridOriginX, int gridOriginY,
                                       int gridSize, double resolution, double marginM)
        {
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (gridSize <= 0) throw new ArgumentException("gridSize musi byt > 0.", nameof(gridSize));
            if (resolution <= 0) throw new ArgumentException("resolution musi byt > 0.", nameof(resolution));
            if (marginM < 0) throw new ArgumentException("marginM nesmi byt zaporna.", nameof(marginM));

            int margin = (int)Math.Ceiling(marginM / resolution);
            int size = gridSize + 2 * margin;
            int originX = gridOriginX - margin;
            int originY = gridOriginY - margin;

            var bits = new byte[(size * size + 7) / 8];
            for (int j = 0; j < size; j++)
            {
                double y = (originY + j + 0.5) * resolution;
                int rowBase = j * size;
                for (int i = 0; i < size; i++)
                {
                    double x = (originX + i + 0.5) * resolution;
                    if (!scene.IsRoad(x, y)) continue;
                    int bit = rowBase + i;
                    bits[bit >> 3] |= (byte)(1 << (bit & 7));
                }
            }
            return new RoadRaster(bits, size, resolution, originX, originY);
        }

        /// <summary>
        /// Rika mapa v tomto svetovem bode "cesta"? Vraci <c>false</c>, kdyz bod lezi MIMO rastr -
        /// pak je <paramref name="isRoad"/> bezvyznamny a dukaz se ma preskocit.
        /// </summary>
        public bool TryIsRoad(double worldX, double worldY, out bool isRoad)
        {
            int i = (int)Math.Floor(worldX / Resolution) - OriginX;
            int j = (int)Math.Floor(worldY / Resolution) - OriginY;
            if ((uint)i >= (uint)Size || (uint)j >= (uint)Size)
            {
                isRoad = false;
                return false;
            }
            int bit = i + j * Size;
            isRoad = (bits[bit >> 3] & (1 << (bit & 7))) != 0;
            return true;
        }
    }
}
