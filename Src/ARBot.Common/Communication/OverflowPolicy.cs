namespace ARBot.Common.Communication
{
    /// <summary>
    /// Politika cile pri zaplneni vstupni fronty.
    /// </summary>
    public enum OverflowPolicy
    {
        /// <summary>Producent ceka na misto ve fronte - BEZZTRATOVE (zaznam do souboru).</summary>
        Block,
        /// <summary>Zahodi nejstarsi zpravu (pro zivou telemetrii/monitoring).</summary>
        DropOldest,
        /// <summary>Zahodi novou zpravu.</summary>
        DropNewest
    }
}
