using System;
using System.Collections.Generic;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// Represents single pin
    /// </summary>
    public interface IGPIO
    {
        /// <summary>
        /// Value of the pin
        /// </summary>
        bool Value { get; set; }
        /// <summary>
        /// Direction
        /// </summary>
        bool IsOutput { get; set; }
        /// <summary>
        /// Edge sensitivity
        /// </summary>
        GPIOEdge Edge { get; set; }
    }
}
