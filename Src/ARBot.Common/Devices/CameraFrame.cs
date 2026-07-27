using ARBot.Common.Common;
using ARBot.Common.Logs;
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
        public const int FormatVersion = 1;

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
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            // Verze byla nastavena MessageReaderem na verzi ulozenou v zaznamu - vetvime podle ni.
            switch (Verze)
            {
                case 1:
                    ReadMeta(br);
                    Name = br.ReadString();
                    ImageRGB = ImageMsg.ReadImage<BGR32>(br);
                    ImageProbability = ImageMsg.ReadImage<Gray>(br);
                    ImageDepth = ImageMsg.ReadImage<Gray16>(br);
                    RGBTimeStamp = ReadDateTime(br);
                    DepthTimeStamp = ReadDateTime(br);
                    break;

                default:
                    throw new NotSupportedException(
                        $"CameraFrame: nepodporovana verze zaznamu {Verze} (aktualni je {FormatVersion}).");
            }
        }
    }
}
