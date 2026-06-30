using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ARBot.HAL
{
    /// <summary>
    /// Sonar abstraction
    /// </summary>
    public interface ISonar
    {
        /// <summary>
        /// Number of sonars
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Ping this sonar
        /// </summary>
        void Ping();

        /// <summary>
        /// Returns distance in meters.
        /// </summary>
        /// <param name="num">Sonar number</param>
        /// <returns></returns>
        double? Distance(int num);
    }
}
