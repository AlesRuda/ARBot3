using System.Collections.Generic;
using ARBot.Common.Devices;

namespace ARBot.HAL.Devices.Bms
{
    /// <summary>Výsledek hledání rámce v přijatých bajtech.</summary>
    public enum JbdVysledek
    {
        /// <summary>Rámec ještě není celý - číst dál.</summary>
        MaloDat,
        /// <summary>Platný rámec (součet sedí, stav 0).</summary>
        Ramec,
        /// <summary>Vadný rámec (součet, chybový stav) - spotřebovaný, důvod v <c>chyba</c>.</summary>
        Chyba,
    }

    /// <summary>Platná odpověď BMS: registr a data.</summary>
    public readonly struct JbdRamec
    {
        public JbdRamec(byte reg, byte status, byte[] data) { Reg = reg; Status = status; Data = data; }
        public byte Reg { get; }
        public byte Status { get; }
        public byte[] Data { get; }
    }

    /// <summary>
    /// Protokol BMS JBD (Jiabaida) nad bajty, bez portu - testovatelný bez hardwaru
    /// (doc/plan-bms-jbd.md). Jen ČTENÍ: dotazy 0x03 (základní údaje) a 0x04 (napětí článků).
    ///
    /// <para>Rámec dotazu <c>DD A5 reg 00 chkH chkL 77</c>, odpovědi <c>DD reg stav délka data
    /// chkH chkL 77</c>; součet = <c>0x10000 − Σ</c> bajtů od pole za registrem (u dotazu
    /// <c>reg</c> a <c>00</c>, u odpovědi stav, délka a data). Rozložení dat je z veřejné
    /// dokumentace; na skutečném SP04S020 se ověří ve fázi 4.</para>
    /// </summary>
    public static class JbdProtocol
    {
        public const byte Start = 0xDD;
        public const byte End = 0x77;
        public const byte ReadCmd = 0xA5;
        public const byte RegBasic = 0x03;
        public const byte RegCells = 0x04;
        public const int BaudRate = 9600;
        public const int RequestLength = 7;
        public const int MaxDataLength = 64;
        public const int MaxCells = 32;
        public const int MaxSensors = 8;
        private const int BasicMinLength = 23;

        /// <summary>Dotaz na čtení registru <paramref name="reg"/>.</summary>
        public static byte[] ReadRequest(byte reg)
        {
            var b = new byte[] { Start, ReadCmd, reg, 0x00, 0, 0, End };
            ushort chk = Checksum(b, 2, 2);
            b[4] = (byte)(chk >> 8);
            b[5] = (byte)chk;
            return b;
        }

        /// <summary>Součet JBD: <c>0x10000 − Σ</c> bajtů <c>b[from .. from+count)</c>.</summary>
        public static ushort Checksum(IReadOnlyList<byte> b, int from, int count)
        {
            int sum = 0;
            for (int i = from; i < from + count; i++) sum += b[i];
            return (ushort)(0x10000 - sum);
        }

        /// <summary>
        /// Najde v <paramref name="buf"/> první odpověď. Zahodí smetí před <c>0xDD</c>, ozvěnu
        /// vlastního dotazu (některé převodníky RS485) a „začátky" s nesmyslnou délkou (bajt
        /// <c>0xDD</c> uprostřed dat). Vrácený i vadný rámec z bufferu odebere.
        /// </summary>
        public static JbdVysledek TryExtract(List<byte> buf, out JbdRamec ramec, out string chyba)
        {
            ramec = default;
            chyba = null;
            while (true)
            {
                int start = buf.IndexOf(Start);
                if (start < 0) { buf.Clear(); return JbdVysledek.MaloDat; }
                if (start > 0) buf.RemoveRange(0, start);
                if (buf.Count < 4) return JbdVysledek.MaloDat;

                if (buf[1] == ReadCmd)
                {
                    // Ozvena dotazu (DD A5 reg 00 chk chk 77) - odpoved ma na tomhle miste registr.
                    if (buf.Count < RequestLength) return JbdVysledek.MaloDat;
                    buf.RemoveRange(0, RequestLength);
                    continue;
                }

                byte reg = buf[1];
                byte status = buf[2];
                int len = buf[3];
                if (len > MaxDataLength)
                {
                    buf.RemoveAt(0);   // 0xDD nebyl zacatek ramce
                    continue;
                }

                int celkem = 4 + len + 3;
                if (buf.Count < celkem) return JbdVysledek.MaloDat;
                if (buf[celkem - 1] != End)
                {
                    buf.RemoveAt(0);
                    continue;
                }

                ushort prijaty = (ushort)((buf[4 + len] << 8) | buf[5 + len]);
                ushort spocteny = Checksum(buf, 2, 2 + len);
                byte[] data = buf.GetRange(4, len).ToArray();
                buf.RemoveRange(0, celkem);

                if (prijaty != spocteny)
                {
                    chyba = $"spatny kontrolni soucet odpovedi (registr 0x{reg:X2})";
                    return JbdVysledek.Chyba;
                }
                if (status != 0)
                {
                    chyba = $"BMS vratila chybovy stav 0x{status:X2} (registr 0x{reg:X2})";
                    return JbdVysledek.Chyba;
                }
                ramec = new JbdRamec(reg, status, data);
                return JbdVysledek.Ramec;
            }
        }

        /// <summary>Rozebere data registru 0x03 do <paramref name="s"/> (bez napětí článků).</summary>
        public static bool TryParseBasic(byte[] d, BmsState s, out int pocetClanku, out string chyba)
        {
            pocetClanku = 0;
            chyba = null;
            if (d == null || d.Length < BasicMinLength)
            {
                chyba = $"zakladni udaje: kratka data ({d?.Length ?? 0} B)";
                return false;
            }
            int clanku = d[21];
            int cidel = d[22];
            if (clanku < 1 || clanku > MaxCells)
            {
                chyba = $"zakladni udaje: nesmyslny pocet clanku {clanku}";
                return false;
            }
            if (cidel > MaxSensors || d.Length < BasicMinLength + 2 * cidel)
            {
                chyba = $"zakladni udaje: {cidel} cidel v {d.Length} B";
                return false;
            }
            int soc = d[19];
            if (soc > 100)
            {
                chyba = $"zakladni udaje: stav nabiti {soc} %";
                return false;
            }

            s.PackVoltage = U16(d, 0) * 0.01;
            s.Current = (short)U16(d, 2) * 0.01;
            s.RemainingAh = U16(d, 4) * 0.01;
            s.NominalAh = U16(d, 6) * 0.01;
            s.Cycles = U16(d, 8);
            s.BalanceMask = (uint)U16(d, 12) | ((uint)U16(d, 14) << 16);
            s.Protection = (BmsProtection)(ushort)U16(d, 16);
            s.SocPercent = soc;
            s.ChargeFetOn = (d[20] & 0x01) != 0;
            s.DischargeFetOn = (d[20] & 0x02) != 0;
            var t = new double[cidel];
            for (int i = 0; i < cidel; i++) t[i] = (U16(d, BasicMinLength + 2 * i) - 2731) / 10.0;
            s.Temperatures = t;
            pocetClanku = clanku;
            return true;
        }

        /// <summary>Rozebere data registru 0x04: 2 B na článek v mV.</summary>
        public static bool TryParseCells(byte[] d, int pocetClanku, out double[] napeti, out string chyba)
        {
            napeti = null;
            chyba = null;
            if (d == null || pocetClanku < 1 || d.Length != 2 * pocetClanku)
            {
                chyba = $"napeti clanku: {d?.Length ?? 0} B pro {pocetClanku} clanku";
                return false;
            }
            napeti = new double[pocetClanku];
            for (int i = 0; i < pocetClanku; i++) napeti[i] = U16(d, 2 * i) * 0.001;
            return true;
        }

        private static int U16(byte[] d, int i) => (d[i] << 8) | d[i + 1];
    }
}
