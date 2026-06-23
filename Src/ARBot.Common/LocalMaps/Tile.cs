using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.LocalMaps
{
    public class Tile
    {
        public const int Width = 32;
        public const int Height = 32;

        public Point Position;

        private BayesPixel[,] points;

        public Tile(int x, int y):this(new Point(x, y))
        {
        }

        public Tile(Point position)
        {
            Position = position;
            points = new BayesPixel[Width, Height];
            for (int i = 0; i < Width; i++)
            {
                for (int j = 0; j < Height; j++)
                {
                    points[i, j] = new BayesPixel();
                }
            }
        }

        public BayesPixel this[int x, int y]
        {
            get
            {
                return points[x, y];
            }
            set
            {
                points[x, y]=value;
            }
        }
    }
}
