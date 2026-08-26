using ARBot.Common.Coordinates;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Prevod textu prectenoho z QR kodu na cil mise. Viz doc/robotour-mission.md.
    ///
    /// <para>Rozhrani existuje proto, aby sel format rozsirit, kdyby pravidla souteze zmenila podobu
    /// kodu — vychozi a jedina implementace je <see cref="GeoUriTargetParser"/>.</para>
    /// </summary>
    public interface IMissionTargetParser
    {
        /// <summary>
        /// Zkusi z textu odvodit cil. Vraci <c>null</c>, kdyz text cilem <b>neni</b> — nesrozumitelny
        /// ani nedekodovany kod nesmi nikdy posunout misi.
        /// </summary>
        LLA Parse(string text);
    }
}
