using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.ComputeUnit
{
    [StructLayout(LayoutKind.Sequential)]
    public struct AggregateItem
    {
        //SumX, SumY, SumZ, Count, SumZ2 poradi musi byt dodrzeno, je shodne s Point4D
        public float SumX, SumY, SumZ, Count, SumZ2;
        public float pad1, pad2, pad3;
//        public float Sum3, Sum4, Min, Max;
        public override string ToString()
        {
            return string.Format("X={4}{0}Y={5}{0}Avg={6}{0}Std={7}{0}Sum={1}{0}Sum2={2}{0}Count={3}{0}", Environment.NewLine, SumZ, SumZ2, Count, SumX / Count, SumY / Count, SumZ / Count, Math.Sqrt(SumZ2 / Count - Math.Pow(SumZ / Count, 2)));
//            return string.Format("X={8}{0}Y={9}{0}Avg={10}{0}Std={11}{0}Sum={1}{0}Sum2={2}{0}Sum3={3}{0}Sum4{4}{0}Count={5}{0}Min={6}{0}Max={7}{0}", Environment.NewLine, Sum, Sum2, Sum3, Sum4, Count, Min, Max, SumX / Count, SumY / Count, Sum / Count, Math.Sqrt(Sum2 / Count - Math.Pow(Sum / Count, 2)));
        }
        public Point4D ToPoint4D()
        {
            if (Count == 0)
                return Point4D.Invalid;
            return new Point4D() { X = SumX / Count, Y = SumY / Count, Z = SumZ / Count, A = 1 };
        }
    }
}
