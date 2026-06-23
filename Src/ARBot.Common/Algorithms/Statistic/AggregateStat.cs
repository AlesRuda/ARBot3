using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Statistic
{
    /// <summary>
    /// Inkrementalne pocita prumer a rozptyl.
    /// Je mozne odstranovat hodnoty.
    /// </summary>
    public class AggregateStat
    {
        private long mN = 0L;
        private double mM = 0.0;
        private double mS = 0.0;

        /// <summary>
        /// Pridava novou hodnotu
        /// </summary>
        /// <param name="x"></param>
        public void Add(double x)
        {
            ++mN;
            double nextM = mM + (x - mM) / mN;
            mS += (x - mM) *(x - nextM);
            mM = nextM;
        }
        /// <summary>
        /// Odebira konkretni hodnotu
        /// </summary>
        /// <param name="x"></param>
        public void Remove(double x)
        {
            if (mN == 0)
            {
                throw new Exception();
            }
            else if (mN == 1)
            {
                mN = 0;
                mM = 0.0;
                mS = 0.0;
            }
            else
            {
                double mMOld = (mN * mM - x) / (mN - 1);
                mS -= (x - mM) * (x - mMOld);
                mM = mMOld;
                --mN;
            }
        }
        /// <summary>
        /// Pocet akumulovanych vzorku
        /// </summary>
        public double Count => mN;

        /// <summary>
        /// Stredni hodnota
        /// </summary>
        public double Mean => mM;

        /// <summary>
        /// Rozptyl
        /// </summary>
        public double Variance
        {
            get
            {
                return mN > 1 ? mS / (mN - 1) : 0.0;
            }
        }
        /// <summary>
        /// Smerodatna odchylka
        /// </summary>
        public double STD => Math.Sqrt(Variance);

        public override string ToString()
        {
            var r = 3 * STD;
            var num = ((int)-Math.Log10(r)) + 2;
            var sf = "{" + $"0:N{num}" +"}+-{"+ $"1:N{num}" + "}";
            return string.Format(sf, Mean, r);
        }
    }
}
