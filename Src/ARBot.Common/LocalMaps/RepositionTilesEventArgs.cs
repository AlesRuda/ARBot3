using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ARBot.Common.Common;

namespace ARBot.Common.LocalMaps
{
    public class RepositionTilesEventArgs:EventArgs
    {
        public Point FirstTile;
        public int Width;
        public int Height;
    }
}
