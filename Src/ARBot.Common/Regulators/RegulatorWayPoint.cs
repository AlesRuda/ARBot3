using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Regulators
{
    public class RegulatorWayPoint
    {
        public double X;
        public double Y;
        public double MaxPositionError=0.1;
        public double Speed;
        public double MaxSpeedError;
        public double? Orientation;
        public double MaxOrientationError =Math.PI/2;
    }
}
