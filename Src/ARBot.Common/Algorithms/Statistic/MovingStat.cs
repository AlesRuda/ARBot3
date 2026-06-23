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
    public class MovingStat
    {
        List<double> vals = new List<double>();

        private double sum = 0.0;
        /// <summary>
        /// Maximalni pocet mereni. Po jeho dosaze dojde k automatickemu odstraneni prvniho.
        /// </summary>
        public int? MaxCount = null;

        /// <summary>
        /// Pridava novou hodnotu
        /// </summary>
        /// <param name="x"></param>
        public void Add(double x)
        {
            sum += x;
            vals.Add(x);
            if (MaxCount.HasValue && MaxCount < Count)
                RemoveFirst();
        }
        /// <summary>
        /// Odebira prvni vzorek
        /// </summary>
        public void RemoveFirst()
        {
            sum -= vals[0];
            vals.RemoveAt(0);
        }
        /// <summary>
        /// Pocet akumulovanych vzorku
        /// </summary>
        public double Count => vals.Count;

        /// <summary>
        /// Stredni hodnota
        /// </summary>
        public double Mean => Count == 0 ? 0 : (sum / Count);

        /// <summary>
        /// Rozptyl
        /// </summary>
        public double Variance
        {
            get
            {
                if (Count == 0)
                    return 0;
                double m = Mean;
                return vals.Sum(x => (x - m) * (x - m))/(Count-1);
            }
        }
        /// <summary>
        /// Smerodatna odchylka
        /// </summary>
        public double STD => Math.Sqrt(Variance);

        public override string ToString()
        {
            var r = 3 * STD;
            var num = Math.Max(0, ((int)-Math.Log10(r))) + 2;
            var sf = "{" + $"0:N{num}" + "}+-{" + $"1:N{num}" + "}";
            return string.Format(sf, Mean, r);
        }
    }
}
