using System;
using System.Collections.Generic;
using System.Text;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Obecne rozhrani pro seonsor
    /// </summary>
    public interface ISensor
    {
        /// <summary>
        /// Jmeno sensoru, ktere se zobrazuje v logu a GUI
        /// </summary>
        public string Name { get; }
        /// <summary>
        /// Zda je senzor v chybovem stavu
        /// </summary>
        public bool IsError { get; }
    }
}
