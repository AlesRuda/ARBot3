using System;
using System.IO;
using ARBot.Common.Fusion;
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
        /// <summary>
        /// Format verze 2 (4. 10. 2026): soustava gridu (<see cref="Frame"/>) a transformace
        /// lokalni soustava → svet — lp-grid-odometricka-soustava. Verze 1 je vzdy ve svete.
        /// </summary>
        public const int FormatVersion = 2;

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

        /// <summary>
        /// Soustava, ve ktere jsou souradnice: <see cref="LocalFrame.World"/>, nebo
        /// <see cref="LocalFrame.Odom"/> (parametr <c>localframe=</c>). Do sveta je prevadi
        /// <see cref="Transform"/>, resp. <see cref="InWorldFrame"/>.
        /// </summary>
        public LocalFrame Frame;
        /// <summary>Transformace lokalni soustava → svet v case <see cref="TimeStamp"/>: posun X [m].</summary>
        public double FrameDX;
        /// <summary>Transformace lokalni soustava → svet: posun Y [m].</summary>
        public double FrameDY;
        /// <summary>Transformace lokalni soustava → svet: pootoceni [rad].</summary>
        public double FrameDTheta;

        /// <summary>Transformace lokalni soustava → svet (identita u <see cref="LocalFrame.World"/>).</summary>
        public FrameTransform Transform
        {
            get => new FrameTransform(FrameDX, FrameDY, FrameDTheta);
            set { FrameDX = value.DX; FrameDY = value.DY; FrameDTheta = value.DTheta; }
        }

        /// <summary>Cas porizeni = <see cref="TimeStamp"/>.</summary>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public OccupancyGridMsg() : base("OccupancyGridMsg", FormatVersion)
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

        /// <summary>X souradnice stredu bunky [m] v soustave <see cref="Frame"/> (ve svete jen
        /// u identity — vzdy u <see cref="LocalFrame.World"/>; jinak pres <see cref="Transform"/>).</summary>
        public double CenterX(int i) => (OriginX + i + 0.5) * Resolution;
        /// <summary>Y souradnice stredu bunky [m] v soustave <see cref="Frame"/>.</summary>
        public double CenterY(int j) => (OriginY + j + 0.5) * Resolution;

        /// <summary>
        /// Snapshot ve <b>svetove</b> soustave pro zobrazeni a rozbor. Je-li transformace identita
        /// (vzdy u <see cref="LocalFrame.World"/>), vrati <b>tentyz objekt</b>; jinak novy grid
        /// zarovnany s osami sveta, prevzorkovany nejblizsim sousedem (bunka sveta dostane hodnotu
        /// lokalni bunky, do ktere padne jeji stred). Pri pootoceni je tedy o ~1 bunku nepresny
        /// a vetsi (obalka otoceneho ctverce) — na kresleni a statistiky to staci, na planovani ne.
        ///
        /// <para>Prevzorkovani a ne otoceny obrazek proto, aby zobrazeni (web, World pohled,
        /// <c>ARBot.Analyze</c>) zustalo beze zmeny: osove zarovnany raster umi vsichni.</para>
        /// </summary>
        public OccupancyGridMsg InWorldFrame()
        {
            var t = Transform;
            if (t.IsIdentity || Size <= 0 || Occ == null) return this;

            double res = Resolution;
            double lx0 = OriginX * res, ly0 = OriginY * res;
            double lx1 = (OriginX + Size) * res, ly1 = (OriginY + Size) * res;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (cx, cy) in new[] { (lx0, ly0), (lx1, ly0), (lx0, ly1), (lx1, ly1) })
            {
                var (wx, wy) = t.ToWorld(cx, cy);
                minX = Math.Min(minX, wx); maxX = Math.Max(maxX, wx);
                minY = Math.Min(minY, wy); maxY = Math.Max(maxY, wy);
            }

            int ox = (int)Math.Floor(minX / res), oy = (int)Math.Floor(minY / res);
            int n = Math.Max((int)Math.Ceiling(maxX / res) - ox, (int)Math.Ceiling(maxY / res) - oy);
            var w = new OccupancyGridMsg
            {
                Size = n,
                Resolution = res,
                OriginX = ox,
                OriginY = oy,
                Scale = Scale,
                BlockedThreshold = BlockedThreshold,
                FreeThreshold = FreeThreshold,
                TimeStamp = TimeStamp,
                Frame = LocalFrame.World,
                Occ = new sbyte[n * n],
                Road = Road != null ? new sbyte[n * n] : null,
            };

            for (int j = 0; j < n; j++)
            {
                double wy = (oy + j + 0.5) * res;
                for (int i = 0; i < n; i++)
                {
                    var (lx, ly) = t.ToLocal((ox + i + 0.5) * res, wy);
                    int li = (int)Math.Floor(lx / res) - OriginX, lj = (int)Math.Floor(ly / res) - OriginY;
                    if ((uint)li >= (uint)Size || (uint)lj >= (uint)Size) continue;   // mimo = nevim (0)
                    int src = li + lj * Size, dst = i + j * n;
                    w.Occ[dst] = Occ[src];
                    if (w.Road != null) w.Road[dst] = Road[src];
                }
            }
            return w;
        }

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
            if (Verze >= 2)
            {
                bw.Write((byte)Frame);
                bw.Write(FrameDX);
                bw.Write(FrameDY);
                bw.Write(FrameDTheta);
            }
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
            // Verze 1 je vzdy ve svete (Frame = World, transformace identita).
            if (Verze >= 2)
            {
                Frame = (LocalFrame)br.ReadByte();
                FrameDX = br.ReadDouble();
                FrameDY = br.ReadDouble();
                FrameDTheta = br.ReadDouble();
            }
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
