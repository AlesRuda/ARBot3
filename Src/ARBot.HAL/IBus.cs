using System;
using System.Collections.Generic;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// Represents bus
    /// </summary>
    public interface IBus
    {
        /// <summary>
        /// Value of the bus
        /// </summary>
        int Value { get; set; }
        /// <summary>
        /// Direction
        /// </summary>
        bool? IsOutput { get; set; }
        /// <summary>
        /// Edge sensitivity
        /// </summary>
        GPIOEdge? Edge { get; set; }
    }
}
