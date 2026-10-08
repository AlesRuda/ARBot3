using System;
using System.IO;
using System.Linq;
using ARBot.Common.Logs;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Stav baterie z chytré BMS (doc/plan-bms-jbd.md). Pasivní DTO — z bajtů ho plní
    /// <c>JbdProtocol</c> v HAL. Proud je <b>kladný při nabíjení</b> (konvence JBD).
    ///
    /// <para><see cref="HasMeasurement"/> = false je zástupná zpráva po chybě komunikace (vzor
    /// <see cref="MotorStateBase.HasMeasurement"/>): ostatní pole v ní nic neznamenají.</para>
    /// </summary>
    public sealed class BmsState : SensorStateBase
    {
        /// <summary>Verze formátu serializace (viz doc/record-replay.md → Verzování zpráv).</summary>
        public const int FormatVersion = 1;

        /// <summary>Strop počtu článků/čidel při čtení — ochrana proti poškozenému záznamu.</summary>
        private const int MaxPolozek = 64;

        /// <summary>Bezparametrický ctor (nutný pro Build/reflexi prototypů zpráv).</summary>
        public BmsState() : base(FormatVersion) { }

        /// <summary>Nese zpráva skutečné měření? <c>false</c> = zástupná zpráva po chybě komunikace.</summary>
        public bool HasMeasurement { get; set; } = true;
        /// <summary>Napětí baterie [V].</summary>
        public double PackVoltage { get; set; }
        /// <summary>Proud [A], kladný = nabíjení.</summary>
        public double Current { get; set; }
        /// <summary>Zbývající kapacita [Ah] podle počítání náboje v BMS.</summary>
        public double RemainingAh { get; set; }
        /// <summary>Jmenovitá kapacita [Ah] nastavená v BMS.</summary>
        public double NominalAh { get; set; }
        /// <summary>Stav nabití [%] 0–100.</summary>
        public int SocPercent { get; set; }
        /// <summary>Počet cyklů podle BMS.</summary>
        public int Cycles { get; set; }
        /// <summary>Napětí článků [V] v pořadí od B−.</summary>
        public double[] CellVoltages { get; set; } = Array.Empty<double>();
        /// <summary>Teploty čidel [°C].</summary>
        public double[] Temperatures { get; set; } = Array.Empty<double>();
        /// <summary>Maska právě vyvažovaných článků (bit 0 = článek 1).</summary>
        public uint BalanceMask { get; set; }
        /// <summary>Příznaky zásahu ochrany.</summary>
        public BmsProtection Protection { get; set; }
        /// <summary>Nabíjecí spínač sepnutý.</summary>
        public bool ChargeFetOn { get; set; }
        /// <summary>Vybíjecí spínač sepnutý.</summary>
        public bool DischargeFetOn { get; set; }

        /// <summary>Nejnižší napětí článku [V], <c>NaN</c> bez článků.</summary>
        public double CellMinV => CellVoltages.Length > 0 ? CellVoltages.Min() : double.NaN;
        /// <summary>Nejvyšší napětí článku [V], <c>NaN</c> bez článků.</summary>
        public double CellMaxV => CellVoltages.Length > 0 ? CellVoltages.Max() : double.NaN;
        /// <summary>Nejvyšší teplota [°C], <c>NaN</c> bez čidel.</summary>
        public double TempMaxC => Temperatures.Length > 0 ? Temperatures.Max() : double.NaN;

        /// <summary>Zástupná zpráva po chybě komunikace.</summary>
        public static BmsState NoMeasurement(DateTime t) => new BmsState { HasMeasurement = false, TimeStamp = t };

        /// <inheritdoc/>
        public override Message Build() => new BmsState();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            WriteMeta(bw);
            bw.Write(HasMeasurement);
            bw.Write(PackVoltage);
            bw.Write(Current);
            bw.Write(RemainingAh);
            bw.Write(NominalAh);
            bw.Write(SocPercent);
            bw.Write(Cycles);
            bw.Write(CellVoltages.Length);
            foreach (var v in CellVoltages) bw.Write(v);
            bw.Write(Temperatures.Length);
            foreach (var v in Temperatures) bw.Write(v);
            bw.Write(BalanceMask);
            bw.Write((ushort)Protection);
            bw.Write(ChargeFetOn);
            bw.Write(DischargeFetOn);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            ReadMeta(br);
            HasMeasurement = br.ReadBoolean();
            PackVoltage = br.ReadDouble();
            Current = br.ReadDouble();
            RemainingAh = br.ReadDouble();
            NominalAh = br.ReadDouble();
            SocPercent = br.ReadInt32();
            Cycles = br.ReadInt32();
            CellVoltages = CtiPole(br);
            Temperatures = CtiPole(br);
            BalanceMask = br.ReadUInt32();
            Protection = (BmsProtection)br.ReadUInt16();
            ChargeFetOn = br.ReadBoolean();
            DischargeFetOn = br.ReadBoolean();
        }

        private static double[] CtiPole(BinaryReader br)
        {
            int n = br.ReadInt32();
            if (n < 0 || n > MaxPolozek)
                throw new InvalidDataException($"BmsState: nesmyslny pocet polozek {n}");
            var a = new double[n];
            for (int i = 0; i < n; i++) a[i] = br.ReadDouble();
            return a;
        }
    }
}
