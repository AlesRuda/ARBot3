using ARBot.Common.Logs;
//using ARBot.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Models
{
    /// <summary>
    /// Rozhrani pro model robotu, na zaklade ridicich zasahu a mereni odhaduje stav robotu.
    /// </summary>
    public interface IModel
    {
        /// <summary>
        /// Aktualni stav
        /// </summary>
        IModelState CurrentState { get; }
        /// <summary>
        /// Odhadovany budouci stav
        /// </summary>
        IModelState PredictedState { get; }
        /// <summary>
        /// Aktualizace stavu
        /// </summary>
        void Update(StateBase s);
        /// <summary>
        /// Aktualizace stavu
        /// </summary>
        void Update(ARBotState s);
        /// <summary>
        /// Vytvari instanci stavu modelu
        /// </summary>
        /// <returns></returns>
        IModelState CreateState();

        /// <summary>
        /// Nastavuje pozici robata
        /// </summary>
        /// <param name="orientation"></param>
        /// <param name="x"></param>
        /// <param name="y"></param>
        void SetOrietantionPosition(double orientation, double x, double y);
        /// <summary>
        /// Prevod na message pro zapis do logu
        /// </summary>
        /// <returns></returns>
        EKFStepMsg ToLogMessage();
    }
}
