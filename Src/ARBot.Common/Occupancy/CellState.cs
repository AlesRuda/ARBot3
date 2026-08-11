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
}
