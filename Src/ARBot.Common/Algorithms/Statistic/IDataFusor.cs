using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Algorithms.Statistic
{
    /// <summary>
    /// Provadi fuzi dat. 
    /// </summary>
    public interface IDataFusor
    {
        /// <summary>
        /// N ruznych vzorku slouci do jedne hodnoty
        /// </summary>
        /// <param name="u"></param>
        /// <returns></returns>
        double Fusion(params double[] u);
    }
}
