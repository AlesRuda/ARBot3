using System.Collections.Generic;
using ARBot.Common.Fusion;
using ARBot.Common.Logs;

namespace ARBot.Common.Runtime
{
    /// <summary>
    /// Prevadi surova senzorova mereni (zpravy) na fuzni mereni <see cref="IMeasurement"/>.
    /// Deje se az v konzumentu (runtime), ne ve zdroji - aby zivá i prehrana data prochazela
    /// identickou modelovaci cestou (deterministicky replay, re-tuning na logu).
    /// </summary>
    public interface IMeasurementMapper
    {
        /// <summary>Vrati fuzni mereni odvozena z jedne zpravy (mozno prazdne).</summary>
        IEnumerable<IMeasurement> ToMeasurements(Message msg);
    }
}
