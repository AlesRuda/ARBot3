using System;

namespace ARBot.Common.Occupancy
{
    /// <summary>
    /// Stav bunky occupancy gridu odvozeny z OBOU kanalu (<c>LOcc</c> geometrie, <c>LRoad</c> semantika).
    /// Kanaly jsou rovnocenne - neprujezdnost od kterehokoli z nich staci.
    /// Viz doc/occupancy-and-local-planning.md.
    /// </summary>
    public enum CellState : byte
    {
        /// <summary>Ani jeden kanal si neni dost jisty (vcetne "o ceste nic nevim").
        /// PLANOVAT se skrz smi (s penalizaci), VJET se do ni nesmi - o to se stara rychlostni
        /// obalka (nejed rychleji, nez z ceho zastavis na hranici potvrzene prujezdneho).</summary>
        Unknown = 0,

        /// <summary>OBA kanaly jiste prujezdne = potvrzene sjizdna bunka.</summary>
        Free = 1,

        /// <summary>KTERYKOLI kanal jiste neprujezdny. Tvrda prekazka pro planovani i jizdu.</summary>
        Blocked = 2,
    }

    /// <summary>
    /// Cim je bunka blokovana. Oba kanaly vyrabeji tentyz <see cref="CellState.Blocked"/>, ale
    /// NEZNAMENAJI totez: geometrie z hloubky rika "fyzicky se tam neprojede" (zed, dira),
    /// semantika z barvy rika "tohle neni cesta" (trava, obrubnik).
    ///
    /// <para>Pro bezne planovani je rozdil nepodstatny - obojimu se vyhyba. Rozhoduje ve chvili,
    /// kdy robot v blokovane bunce UZ STOJI: ven se smi pres semanticky blokovane bunky (z travy
    /// zpatky na cestu), pres geometricky blokovane NIKDY (do zdi se nejede).
    /// Viz doc/occupancy-and-local-planning.md.</para>
    /// </summary>
    [Flags]
    public enum CellBlockReason : byte
    {
        /// <summary>Bunka neni blokovana.</summary>
        None = 0,

        /// <summary>Kanal hloubky hlasi jiste neprujezdno - tvrda fyzicka prekazka.</summary>
        Geometry = 1,

        /// <summary>Kanal barvy hlasi jiste "mimo cestu" - projezdne, ale nezadouci.</summary>
        Semantics = 2,
    }
}
