using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Hranice cesty promitnute na rovinu co jede robot
    /// </summary>
    public class PathEdge2
    {
        /// <summary>
        /// <summary>
        /// Souradnice kraje v rovine kamery
        /// </summary>
        /// </summary>
        public Point Point { get; set; }
        private Point4D? wordPoint;
        /// <summary>
        /// Souradnice kraje v prostoru s pocatkem v miste robotu a svetove orientace.
        /// Ne kazdy bod musi lezet na rovine po ktere jede robot.
        /// Asi by bak mel byt oznacen jako Used=false, ale ne nutne
        /// </summary>
        public Point4D? WordPoint 
        { 
            get=>wordPoint;
            set
            {
                wordPoint2D = null;
                wordPoint = value;
            }
        }
        private Point2D? wordPoint2D;
        /// <summary>
        /// Souradnice kraje v rovine co jede robot
        /// </summary>
        public Point2D? WordPoint2D
        {
            get
            {
                if (wordPoint2D == null && wordPoint != null)
                    wordPoint2D = new Point2D(wordPoint.Value.X, wordPoint.Value.Y);
                return wordPoint2D;
            }
        }
        /// <summary>
        /// Jmeno kamery
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// Levy kraj 
        /// </summary>
        public bool Left { get; set; }
        /// <summary>
        /// Bod je pouzit pro vypocet linie cesty
        /// </summary>
        public bool Used { get; set; }
        /// <summary>
        /// Bod byl klasifikovan jako Inlier v ramci RASNAC
        /// </summary>
        public bool Inlier { get; set; }
        /// <summary>
        /// ID cesty z mapy, ke ktery byl bod prirazen
        /// </summary>
        public long? WayID { get; set; }
        /// <summary>
        /// Orientace kamery. Slouzi pro jasne rozliseni kde je vlevo a kde vpravo
        /// </summary>
        public double? Orientation { get; set; }
    }
}
