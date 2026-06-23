using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Regulators
{
    /// <summary>
    /// Vysledek vypoctu regulatoru
    /// </summary>
    public class RegulatorResult
    {
        /// <summary>
        /// Dopredna rychlost v m/s
        /// </summary>
        public double Speed;
        /// <summary>
        /// Rychlost o taceni v rad/s a matematickem smyslu (proti smeru hodinovych rucicek)
        /// </summary>
        public double RotationSpeed;
        /// <summary>
        /// Vypoctena doba regulacniho zasahu
        /// </summary>
        public double RegulationTime;

        public override string ToString()
        {
            return string.Format("Speed={0}, RotationSpeed={1}, RegulationTime={2}", Speed, RotationSpeed, RegulationTime);
        }
    }
}
