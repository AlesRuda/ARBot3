using System;
using System.Collections.Generic;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// Edge sensitivity
    /// </summary>
    public enum GPIOEdge
    {
        /// <summary>
        /// Not edge sensitive
        /// </summary>
        None,
        /// <summary>
        /// Sensitive for rising edge
        /// </summary>
        Rising,
        /// <summary>
        /// Sensitive for falling edge
        /// </summary>
        Falling,
        /// <summary>
        /// Sensitive for rising and filling edge
        /// </summary>
        Both
    }
}
