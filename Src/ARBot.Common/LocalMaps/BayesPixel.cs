using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.LocalMaps
{
    public class BayesPixel
    {
        public double Value;
        public BayesPixel()
        {
            Value = 0.5;
        }
        public void Update(double value)
        {
            double v1 = Value * value;
            Value = Math.Min(0.999, Math.Max(0.001, v1 / (1 + 2 * v1 - Value - value)));
        }
    }
}
