using System;
using System.Linq;

namespace ARBot.Common.Algorithms.Statistic
{

    /// <summary>
    /// Provadi fuzi data na zaklade jejich rozptylu.
    /// Dostane n mereni v jeden casovy okamzik. Tato mereni ovlivnuji stejnou velicinu. Prikladem muze byt pootoceni robotu z encoderu, z kompasu, z GPS, z vizualni odometrie, ze sorvnani obrazu cesty s mapou, ....
    /// Spocte stredni hodnotu (avg) a kvadraty odchylek od ni (du[i]^2). Kvadrat odchylky je pak merou (p[i]) spravnosti vzorku (u[i]). Cim vetsi hodnota kvadratu tim horsi vzorek.
    /// Suma p[i] musi splnovat podminku statistiky tj. =1. Tyto pozadavky plni vztah p[i]=(sum(du[j]^2)-du[i]^2) / ((n-1)*sum(du[j]^2))
    /// Vysledna hodnota y=sum(p[i]*u[i])
    /// </summary>
    public class StatisticDataFusor:IDataFusor
    {
        /// <summary>
        /// N ruznych vzorku slouci do jedne hodnoty podle rozptylu od prumeru.
        /// </summary>
        /// <param name="u"></param>
        /// <returns></returns>
        public double Fusion(params double[] u)
        {
            var n = u.Length;
            if (n == 0)
                throw new ArgumentException("Parametr u musi mit alespon jeden prvek.");
            if (u.Length == 1)
                return u[0];
            var avg = u.Sum() / n;
            var du = u.Select(v => v - avg).ToList();
            var c = du.Sum(v => v * v);
            if (c == 0)
                return avg;
            var nc = (n - 1) * c;
            double y = 0;
            for (int i = 0; i < n; i++)
                y += u[i] * (c - du[i] * du[i]) / nc;
            return y;
        }
    }
}