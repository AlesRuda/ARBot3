using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.Common.KDTree
{
    /// <summary>
    /// Rozhrani pro ziskani souradnic
    /// </summary>
    public interface IKDTreePoint
    {
        /// <summary>
        /// Souradnice 
        /// </summary>
        double[] Values { get; }
    }
}
