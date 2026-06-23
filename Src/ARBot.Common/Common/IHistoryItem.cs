using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Common
{
    /// <summary>
    /// Prvek historie stavu
    /// </summary>
    public interface IHistoryItem<Item>
    {
        /// <summary>
        /// Casova znacka
        /// </summary>
        DateTime TimeStamp { get; set; }
        /// <summary>
        /// Interpoluje stav mezi prev a next
        /// </summary>
        /// <param name="prev">Predchozi vzorek</param>
        /// <param name="next">Nasledujci vzorek</param>
        /// <param name="d">Linearni pozice mezi prev (0) a next (1)</param>
        /// <returns></returns>
        Item Interpolate(Item prev, Item next, float d);
    }
}
