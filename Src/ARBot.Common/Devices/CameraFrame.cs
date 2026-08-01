using ARBot.Common.Common;
using ARBot.Common.Logs;
using ARBot.Common.Vision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ARBot.Common.Devices
{
    /// <summary>
    /// Snimek kamery
    /// </summary>
    public class CameraFrame: SensorStateBase, INamedMessage
    {
        /// <summary>
        /// Verze formatu serializace. Pri KAZDE zmene obsahu (poradi/typy poli v
        /// <see cref="ToData"/>/<see cref="FromData"/>) zvys o 1 a v <see cref="FromData"/>
        /// pridej cteci vetev pro predchozi verzi (viz doc/record-replay.md → Verzovani zprav).
        /// </summary>
        public const int FormatVersion = 2;

        public CameraFrame() : base(FormatVersion)
        {
        }

        /// <summary>Jmeno zdroje (napr. kamera Left/Right) - pro rozliseni v pipeline a vizualizaci.</summary>
        public string Name { get; set; }

        /// <summary>
        /// Barevny obrazek
        /// </summary>
        public Image<BGR32> ImageRGB { get; set; }
        /// <summary>
        /// Sjizdnost
        /// </summary>
        public Image<Gray> ImageProbability { get; set; }
        /// <summary>
        /// 3D obraz
        /// </summary>
        public Image<Gray16> ImageDepth { get; set; }

        public DateTime RGBTimeStamp;
        public DateTime DepthTimeStamp;

        /// <summary>
        /// Polarni grid sjizdnosti dopocteny z <see cref="ImageDepth"/> (per kamera; muze byt null,
        /// dokud geometrie/projekce nedovoli grid sestavit). Pocita ho synchronne
        /// <see cref="ARBot.Common.Vision.ICameraFrameProcessor"/> na vlakne kamery a je soucasti
        /// ramce (serializuje se s nim - viz <see cref="ToData"/>).
        /// </summary>
        public PolarTraversabilityGrid Grid { get; set; }

        /// <inheritdoc/>
        public override Message Build() => new CameraFrame();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            // Zapisuje VZDY aktualni layout (FormatVersion). Obrazy pres ImageMsg.Write BEZ komprese
            // (None) - setri CPU (zadne Jpeg/Png/Deflate kodovani); ~1,8 GB/min pri 2 kamerach @10 Hz
            // se na disk (NVMe) vejde na hodiny, coz staci i pro soutezni jizdu. Kdyz bude potreba
            // setrit misto, staci u vrstev zmenit kompresi (a bumpnout FormatVersion, pokud se meni obsah).
            WriteMeta(bw);
            bw.Write(Name ?? string.Empty);
            ImageMsg.Write(bw, ImageRGB, ImageMsg.Compression.None);
            ImageMsg.Write(bw, ImageProbability, ImageMsg.Compression.None);
            ImageMsg.Write(bw, ImageDepth, ImageMsg.Compression.None);
            Write(bw, RGBTimeStamp);
            Write(bw, DepthTimeStamp);
            WriteGrid(bw, Grid);   // od verze 2 (diagnosticke ComputeMs se NEserializuje)
        }

        /// <summary>Zapise polarni grid: flag "ma grid", a pokud ano geometrii + bunky.</summary>
        private static void WriteGrid(BinaryWriter bw, PolarTraversabilityGrid g)
        {
            bw.Write(g != null);
            if (g == null) return;

            bw.Write(g.AzimuthCount);
            bw.Write(g.ColumnsPerCell);

            int ne = g.RadialEdges?.Length ?? 0;
            bw.Write(ne);
            for (int i = 0; i < ne; i++)
            {
                bw.Write(g.RadialEdges[i].Range);
                bw.Write(g.RadialEdges[i].Row);
            }

            int nc = g.Cells?.Length ?? 0;
            bw.Write(nc);
            for (int i = 0; i < nc; i++)
            {
                var c = g.Cells[i];
                bw.Write(c.Count);
                bw.Write(c.MeanX);
                bw.Write(c.MeanY);
                bw.Write(c.MeanZ);
                bw.Write(c.StdZ);
                bw.Write(c.MaxZ);
                bw.Write(c.EdgeRange);
                bw.Write(c.Confidence);
                bw.Write((byte)c.Class);
            }
        }

        /// <summary>Nacte polarni grid zapsany <see cref="WriteGrid"/>; vraci null, kdyz "ma grid" == false.</summary>
        private static PolarTraversabilityGrid ReadGrid(BinaryReader br)
        {
            if (!br.ReadBoolean()) return null;

            var g = new PolarTraversabilityGrid
            {
                AzimuthCount = br.ReadInt32(),
                ColumnsPerCell = br.ReadInt32(),
            };

            int ne = br.ReadInt32();
            g.RadialEdges = new RadialEdge[ne];
            for (int i = 0; i < ne; i++)
                g.RadialEdges[i] = new RadialEdge(br.ReadSingle(), br.ReadInt32());

            int nc = br.ReadInt32();
            g.Cells = new PolarCell[nc];
            for (int i = 0; i < nc; i++)
            {
                g.Cells[i] = new PolarCell
                {
                    Count = br.ReadInt32(),
                    MeanX = br.ReadSingle(),
                    MeanY = br.ReadSingle(),
                    MeanZ = br.ReadSingle(),
                    StdZ = br.ReadSingle(),
                    MaxZ = br.ReadSingle(),
                    EdgeRange = br.ReadSingle(),
                    Confidence = br.ReadSingle(),
                    Class = (TraversabilityClass)br.ReadByte(),
                };
            }
            return g;
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            // Verze byla nastavena MessageReaderem na verzi ulozenou v zaznamu - vetvime podle ni.
            switch (Verze)
            {
                case 1:
                    // Stary layout beze gridu (grid byl samostatna zprava PolarTraversabilityGridMsg).
                    ReadMeta(br);
                    Name = br.ReadString();
                    ImageRGB = ImageMsg.ReadImage<BGR32>(br);
                    ImageProbability = ImageMsg.ReadImage<Gray>(br);
                    ImageDepth = ImageMsg.ReadImage<Gray16>(br);
                    RGBTimeStamp = ReadDateTime(br);
                    DepthTimeStamp = ReadDateTime(br);
                    Grid = null;
                    break;

                case 2:
                    ReadMeta(br);
                    Name = br.ReadString();
                    ImageRGB = ImageMsg.ReadImage<BGR32>(br);
                    ImageProbability = ImageMsg.ReadImage<Gray>(br);
                    ImageDepth = ImageMsg.ReadImage<Gray16>(br);
                    RGBTimeStamp = ReadDateTime(br);
                    DepthTimeStamp = ReadDateTime(br);
                    Grid = ReadGrid(br);
                    break;

                default:
                    throw new NotSupportedException(
                        $"CameraFrame: nepodporovana verze zaznamu {Verze} (aktualni je {FormatVersion}).");
            }
        }
    }
}
