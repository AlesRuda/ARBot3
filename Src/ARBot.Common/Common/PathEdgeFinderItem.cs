using ARBot.Common.Coordinates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    public class PathEdgeFinderItem
    {
        /// <summary>
        /// Jmeno 
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Projekce color kamery
        /// </summary>
        public ICameraProjection CameraProjection { get; set; }
        /// <summary>
        /// Projekce depth kamery
        /// </summary>
        public IDepthCameraProjection DepthCameraProjection { get; set; }

        /// <summary>
        /// Zvetseni v ose X, ktere je nutne aplikovat na Probability, aby souradnice bodu odpovidaly projekci kamery
        /// </summary>
        public double ScaleX { get; set; }
        /// <summary>
        /// Zvetseni v ose Y, ktere je nutne aplikovat na Probability, aby souradnice bodu odpovidaly projekci kamery
        /// </summary>
        public double ScaleY { get; set; }

        /// <summary>
        /// Orientace kamery v matematickem smyslu.
        /// Slouzi pro urceni co je vlevo a co vpravo.
        /// </summary>
        public double Orientation { get; set; }
        /// <summary>
        /// Pravdepodobnosti obrazek sjizdnosti
        /// </summary>
        public Image<Gray> Probability { get; set; }
        /// <summary>
        /// Hloubkovy obrazek
        /// </summary>
        public Image<Gray16> Depth { get; set; }
        /// <summary>
        /// Nalezene hranice cesty v souradnicich kamery (probability  * (Scalex, ScaleY))
        /// </summary>
        public List<PathEdge> Edges { get; set; }
    }
}
