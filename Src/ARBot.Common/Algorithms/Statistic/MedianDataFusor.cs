using System;
using System.Linq;

namespace ARBot.Common.Algorithms.Statistic
{
    /// <summary>
    /// Provadi fuzi data. Vysledkem je median predaneho souboru.
    /// </summary>
    public class MedianDataFusor : IDataFusor
    {
        /// <summary>
        /// Spocte median u.
        /// </summary>
        /// <param name="u"></param>
        /// <returns></returns>
        public double Fusion(params double[] u)
        {
            var c = u.Length;
            if (c == 0)
                throw new ArgumentException("Parametr u musi mit alespon jeden prvek.");
            if (c>2)
                u=u.OrderBy(i => i).ToArray();
            if (c % 2 == 1)
                return u[c/2];
            c /= 2;
            return (u[c-1]+ u[c ])/2;
        }
    }
}