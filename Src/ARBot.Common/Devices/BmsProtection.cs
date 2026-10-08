using System.Collections.Generic;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Příznaky zásahu ochrany BMS. Hodnoty bitů jsou bity registru ochran JBD (základní údaje,
    /// bajty 16–17), takže driver je předá bez převodu; jiná BMS by si je mapovala sama.
    /// Viz doc/plan-bms-jbd.md.
    /// </summary>
    [System.Flags]
    public enum BmsProtection : ushort
    {
        None = 0,
        CellOvervoltage = 1 << 0,
        CellUndervoltage = 1 << 1,
        PackOvervoltage = 1 << 2,
        PackUndervoltage = 1 << 3,
        ChargeOvertemp = 1 << 4,
        ChargeUndertemp = 1 << 5,
        DischargeOvertemp = 1 << 6,
        DischargeUndertemp = 1 << 7,
        ChargeOvercurrent = 1 << 8,
        DischargeOvercurrent = 1 << 9,
        ShortCircuit = 1 << 10,
        FrontEndError = 1 << 11,
        MosLocked = 1 << 12,
    }

    /// <summary>Český popis příznaků pro stránku náhledu a <c>Trace</c>.</summary>
    public static class BmsProtectionText
    {
        private static readonly (BmsProtection Priznak, string Text)[] Texty =
        {
            (BmsProtection.CellOvervoltage, "přepětí článku"),
            (BmsProtection.CellUndervoltage, "podpětí článku"),
            (BmsProtection.PackOvervoltage, "přepětí baterie"),
            (BmsProtection.PackUndervoltage, "podpětí baterie"),
            (BmsProtection.ChargeOvertemp, "přehřátí při nabíjení"),
            (BmsProtection.ChargeUndertemp, "mráz při nabíjení"),
            (BmsProtection.DischargeOvertemp, "přehřátí při vybíjení"),
            (BmsProtection.DischargeUndertemp, "mráz při vybíjení"),
            (BmsProtection.ChargeOvercurrent, "nadproud nabíjení"),
            (BmsProtection.DischargeOvercurrent, "nadproud vybíjení"),
            (BmsProtection.ShortCircuit, "zkrat"),
            (BmsProtection.FrontEndError, "chyba měřicího obvodu"),
            (BmsProtection.MosLocked, "spínače zamčené"),
        };

        /// <summary>Popis všech nastavených příznaků; neznámé bity se vypíšou číslem, nezahodí.</summary>
        public static string Popis(BmsProtection p)
        {
            if (p == BmsProtection.None) return "žádná";
            var casti = new List<string>();
            int znamo = 0;
            foreach (var (priznak, text) in Texty)
            {
                znamo |= (int)priznak;
                if ((p & priznak) != 0) casti.Add(text);
            }
            int nezname = (int)p & ~znamo;
            if (nezname != 0) casti.Add($"neznámý příznak 0x{nezname:X4}");
            return string.Join(", ", casti);
        }
    }
}
