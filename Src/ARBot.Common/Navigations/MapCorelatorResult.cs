using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Navigations
{
    /// <summary>
    /// Vysledek korelace lokalni mapy s mapou
    /// </summary>
    public class MapCorelatorResult
    {
        public double Maximum { get; set; }
        public double MaximumAtX { get; set; }
        public double MaximumAtY { get; set; }
        public double AvgX { get; set; }
        public double AvgY { get; set; }
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double VariaceX { get; set; }
        public double VariaceY { get; set; }
        public double Covariance { get; set; }
        public TimeSpan ProcessingTime { get; set; }

        public override string ToString()
        {
            return string.Format(@"Maximum={0}
MaximumAtX={1}
MaximumAtY={2}
AvgX={3}
AvgY={4}
OffsetX={5}
OffsetY={6}
VariaceX={7}
VariaceY={8}
Covariance={9}
ProcessingTime={10}", Maximum, MaximumAtX, MaximumAtY, AvgX, AvgY, OffsetX, OffsetY, VariaceX, VariaceY, Covariance, ProcessingTime);
        }
    }
}
