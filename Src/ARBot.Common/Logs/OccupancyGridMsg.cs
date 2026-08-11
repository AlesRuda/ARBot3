using System;
using System.IO;
using ARBot.Common.Occupancy;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Odvozena zprava: snapshot kartezskeho occupancy gridu (<see cref="OccupancyGrid"/>) pro
    /// vizualizaci a zaznam. Viz doc/occupancy-and-local-planning.md.
    ///
    /// <para>Oba kanaly se posilaji jako <c>sbyte</c> pole v LOKALNIM poradi (index
    /// <c>i + j * Size</c>, kde <c>i = 0</c> odpovida <see cref="OriginX"/>) - prijemce tedy nemusi
    /// resit kruhovy buffer. Pri 256 x 256 je to 2 x 64 KB; proti ~1,8 GB/min obrazu zanedbatelne,
    /// presto se emituje ridsi frekvenci nez snimky (typicky 2 Hz).</para>
    /// </summary>
    [Serializable()]
    public class OccupancyGridMsg : Message, IHasCaptureTime
    {
        /// <summary>Pocet bunek na stranu.</summary>
        public int Size;
        /// <summary>Velikost bunky [m].</summary>
        public double Resolution;
        /// <summary>Absolutni index nejzapadnejsiho drzeneho sloupce.</summary>
        public int OriginX;
        /// <summary>Absolutni index nejjiznejsiho drzeneho radku.</summary>
        public int OriginY;
        /// <summary>Krok fixed-pointu log-odds (hodnota * Scale = log-odds).</summary>
        public float Scale;
        /// <summary>Prah, od ktereho je kanal "jiste neprujezdny" [log-odds].</summary>
        public float BlockedThreshold;
        /// <summary>Prah, do ktereho je kanal "jiste prujezdny" [log-odds].</summary>
        public float FreeThreshold;
        /// <summary>Kanal geometrie (log-odds neprujezdnosti z hloubky), lokalni poradi.</summary>
        public sbyte[] Occ;
        /// <summary>Kanal semantiky (log-odds neprujezdnosti z barvy), lokalni poradi.</summary>
        public sbyte[] Road;
        /// <summary>Cas, ke kteremu snapshot plati (cas pozy, ze ktere se naposledy zapisovalo).</summary>
        public DateTime TimeStamp;

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public OccupancyGridMsg() : base("OccupancyGridMsg", 1)
        {
        }

        /// <summary>Stav bunky z obou kanalu (stejna logika jako <see cref="OccupancyGrid.StateAt"/>).</summary>
        public CellState State(int i, int j)
        {
            if (Occ == null || (uint)i >= (uint)Size || (uint)j >= (uint)Size) return CellState.Unknown;
            int idx = i + j * Size;
            float o = Occ[idx] * Scale;
            float r = Road != null ? Road[idx] * Scale : 0f;
            if (o >= BlockedThreshold || r >= BlockedThreshold) return CellState.Blocked;
            if (o <= FreeThreshold && r <= FreeThreshold) return CellState.Free;
            return CellState.Unknown;
        }

        /// <summary>Svetova X souradnice stredu bunky [m].</summary>
        public double CenterX(int i) => (OriginX + i + 0.5) * Resolution;
        /// <summary>Svetova Y souradnice stredu bunky [m].</summary>
        public double CenterY(int j) => (OriginY + j + 0.5) * Resolution;

        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Size);
            bw.Write(Resolution);
            bw.Write(OriginX);
            bw.Write(OriginY);
            bw.Write(Scale);
            bw.Write(BlockedThreshold);
            bw.Write(FreeThreshold);
            Write(bw, TimeStamp);
            WriteChannel(bw, Occ);
            WriteChannel(bw, Road);
        }

        public override void FromData(BinaryReader br)
        {
            Size = br.ReadInt32();
            Resolution = br.ReadDouble();
            OriginX = br.ReadInt32();
            OriginY = br.ReadInt32();
            Scale = br.ReadSingle();
            BlockedThreshold = br.ReadSingle();
            FreeThreshold = br.ReadSingle();
            TimeStamp = ReadDateTime(br);
            Occ = ReadChannel(br);
            Road = ReadChannel(br);
        }

        private static void WriteChannel(BinaryWriter bw, sbyte[] data)
        {
            bw.Write(data != null);
            if (data == null) return;
            bw.Write(data.Length);
            for (int i = 0; i < data.Length; i++) bw.Write(data[i]);
        }

        private static sbyte[] ReadChannel(BinaryReader br)
        {
            if (!br.ReadBoolean()) return null;
            int n = br.ReadInt32();
            var data = new sbyte[n];
            for (int i = 0; i < n; i++) data[i] = br.ReadSByte();
            return data;
        }

        public override Message Build() => new OccupancyGridMsg();

        public override string ToString()
            => $"OccupancyGridMsg {Size}x{Size} res={Resolution:F3} origin=({OriginX},{OriginY})";
    }
}
