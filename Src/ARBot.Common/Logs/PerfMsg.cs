using System;
using System.Collections.Generic;
using System.IO;

namespace ARBot.Common.Logs
{
    /// <summary>Verdikt o tom, jestli rizeni stiha.</summary>
    public enum PerfVerdict { Ok = 0, Warning = 1, Error = 2 }

    /// <summary>
    /// Vykon rizeni za jeden interval sberu (~1 s): stiha ridici smycka svou periodu, ktera cast
    /// ji brzdi a jak je na tom stroj.
    ///
    /// <para><b>Proc jedna zprava a ne CSV.</b> Ve streamu jde soucasne do UI (zivy ukazatel) i do
    /// zaznamu (rozbor po jizde), takze obe pouziti maji tataz data a nic se nemusi parovat.</para>
    ///
    /// <para><b>Proc nese i MAXIMUM, nejen prumer.</b> Nestihani je typicky spickove - ojedinely
    /// dlouhy takt by se v prumeru za sekundu ztratil. <see cref="WorstTickTime"/> je kotva, podle
    /// ktere se v ostatnich zpravach dohleda, co robot v tu chvili delal.</para>
    ///
    /// <para>Viz doc/perf-monitoring.md.</para>
    /// </summary>
    [Serializable()]
    public class PerfMsg : Message
    {
        /// <summary>Takty na jednom jadru - kvuli nestejnym jadrum RK3588.</summary>
        public struct CoreEntry
        {
            public int ProcessorId;
            public int TickCount;
            public double AvgMs;
        }

        /// <summary>Stav a vykon jednoho stupne pipeline.</summary>
        public struct StageEntry
        {
            public string Name;
            public int QueueLength;
            public long Processed;
            public long Dropped;
            public double AvgMs;
            public double MaxMs;
        }

        /// <summary>Zacatek intervalu.</summary>
        public DateTime From;
        /// <summary>Konec intervalu; delka nemusi byt presne 1 s, kdyz se sberac opozdi.</summary>
        public DateTime To;

        public int TickCount;
        /// <summary>Takty, ktere se nestihly vydat vcas (dnes se dohaneji).</summary>
        public int MissedTicks;

        /// <summary>Prumerna obsazenost periody [%]. HLAVNI CISLO.</summary>
        public double OccupancyAvgPct;
        /// <summary>Nejvetsi obsazenost periody [%] v intervalu.</summary>
        public double OccupancyMaxPct;

        public double DelayAvgMs;
        public double DelayMaxMs;

        /// <summary>Cas nejdelsiho taktu - kotva pro dohledani v ostatnich zpravach.</summary>
        public DateTime WorstTickTime;
        /// <summary>Jadro, na kterem nejdelsi takt bezel.</summary>
        public int WorstProcessorId;

        /// <summary>Vytizeni procesu [%] z CELEHO stroje (ne z jednoho jadra); -1 = neznamo.</summary>
        public double ProcessCpuPct = -1;
        /// <summary>Vytizeni stroje [%]; -1 = neznamo (faze 3).</summary>
        public double MachineCpuPct = -1;

        public PerfVerdict Verdict;

        public List<CoreEntry> Cores = new List<CoreEntry>();
        public List<StageEntry> Stages = new List<StageEntry>();

        public PerfMsg() : base("PerfMsg", 1)
        {
        }

        public override void ToData(BinaryWriter bw)
        {
            Write(bw, From);
            Write(bw, To);
            bw.Write(TickCount);
            bw.Write(MissedTicks);
            bw.Write(OccupancyAvgPct);
            bw.Write(OccupancyMaxPct);
            bw.Write(DelayAvgMs);
            bw.Write(DelayMaxMs);
            Write(bw, WorstTickTime);
            bw.Write(WorstProcessorId);
            bw.Write(ProcessCpuPct);
            bw.Write(MachineCpuPct);
            bw.Write((int)Verdict);

            bw.Write(Cores?.Count ?? 0);
            foreach (var c in Cores ?? new List<CoreEntry>())
            {
                bw.Write(c.ProcessorId);
                bw.Write(c.TickCount);
                bw.Write(c.AvgMs);
            }

            bw.Write(Stages?.Count ?? 0);
            foreach (var s in Stages ?? new List<StageEntry>())
            {
                bw.Write(s.Name ?? string.Empty);
                bw.Write(s.QueueLength);
                bw.Write(s.Processed);
                bw.Write(s.Dropped);
                bw.Write(s.AvgMs);
                bw.Write(s.MaxMs);
            }
        }

        public override void FromData(BinaryReader br)
        {
            From = ReadDateTime(br);
            To = ReadDateTime(br);
            TickCount = br.ReadInt32();
            MissedTicks = br.ReadInt32();
            OccupancyAvgPct = br.ReadDouble();
            OccupancyMaxPct = br.ReadDouble();
            DelayAvgMs = br.ReadDouble();
            DelayMaxMs = br.ReadDouble();
            WorstTickTime = ReadDateTime(br);
            WorstProcessorId = br.ReadInt32();
            ProcessCpuPct = br.ReadDouble();
            MachineCpuPct = br.ReadDouble();
            Verdict = (PerfVerdict)br.ReadInt32();

            int coreCount = br.ReadInt32();
            Cores = new List<CoreEntry>(coreCount);
            for (int i = 0; i < coreCount; i++)
                Cores.Add(new CoreEntry
                {
                    ProcessorId = br.ReadInt32(),
                    TickCount = br.ReadInt32(),
                    AvgMs = br.ReadDouble(),
                });

            int stageCount = br.ReadInt32();
            Stages = new List<StageEntry>(stageCount);
            for (int i = 0; i < stageCount; i++)
                Stages.Add(new StageEntry
                {
                    Name = br.ReadString(),
                    QueueLength = br.ReadInt32(),
                    Processed = br.ReadInt64(),
                    Dropped = br.ReadInt64(),
                    AvgMs = br.ReadDouble(),
                    MaxMs = br.ReadDouble(),
                });
        }

        public override Message Build() => new PerfMsg();

        public override string ToString()
            => string.Format("PerfMsg obsazenost {0:F0}/{1:F0}% takty={2} zameskane={3} {4}",
                             OccupancyAvgPct, OccupancyMaxPct, TickCount, MissedTicks, Verdict);
    }
}
