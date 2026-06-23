using ARBot.Common.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Models
{
    /// <summary>
    /// Historie stavu modelu.
    /// Umoznuje interpolovat mezilehle stavy.
    /// </summary>
    public class ModelStateHistory:History<IModelState>
    {
        /// <summary>
        /// Konstruktor
        /// </summary>
        /// <param name="maxCount"></param>
        public ModelStateHistory(int maxCount):base(maxCount)
        {
        }

    }
}
