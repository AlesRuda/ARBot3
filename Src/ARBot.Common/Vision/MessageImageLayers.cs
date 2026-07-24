using System;
using System.Collections.Generic;
using ARBot.Common.Common;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Common.Vision
{
    /// <summary>
    /// Rozklad zpravy na pojmenovane obrazove vrstvy (<see cref="ImageLayer"/>).
    /// Sjednocuje <see cref="Blob"/> (1 vrstva dle jmena) a <see cref="CameraFrame"/>
    /// (vrstvy "&lt;Name&gt;/RGB", "&lt;Name&gt;/Probability", "&lt;Name&gt;/Depth").
    /// </summary>
    public static class MessageImageLayers
    {
        public static IEnumerable<ImageLayer> Extract(Message msg)
        {
            switch (msg)
            {
                case Blob b:
                    var layer = FromBlob(b);
                    if (layer != null) yield return layer;
                    break;

                case CameraFrame f:
                    string src = string.IsNullOrEmpty(f.Name) ? "Camera" : f.Name;
                    if (f.ImageRGB != null)
                        yield return new ImageLayer { Name = src + "/RGB", Kind = LayerKind.Color, Color = f.ImageRGB, TimeStamp = f.TimeStamp };
                    if (f.ImageProbability != null)
                        yield return new ImageLayer { Name = src + "/Probability", Kind = LayerKind.Probability, Gray = f.ImageProbability, TimeStamp = f.TimeStamp };
                    if (f.ImageDepth != null)
                        yield return new ImageLayer { Name = src + "/Depth", Kind = LayerKind.Depth, Depth = f.ImageDepth, TimeStamp = f.TimeStamp };
                    break;
            }
        }

        private static ImageLayer FromBlob(Blob b)
        {
            try
            {
                switch (b.Type)
                {
                    case Blob.BlobType.Jpeg:
                    case Blob.BlobType.BGR32:
                        return Color(b, b.ToBGR32Image());
                    case Blob.BlobType.RGB:
                        // TODO(NativeComputeUnit): docasne managed Image<T>.ConvertTo. Barevne prevody
                        // patri na akcelerovany NativeComputeUnit (SIMD/HW) - viz CopyRGB24ToBGR32 /
                        // CopyBGR24ToBGR32 (zatim jen typova pole / IntPtr). Doplnit byte[]->byte[] variantu.
                        return Color(b, b.ToRGBImage().ConvertTo<BGR32>((s, d) => { d.R = s.R; d.G = s.G; d.B = s.B; }));
                    case Blob.BlobType.BGR:
                        var bgr = new Image<BGR>(b.Width, b.Height) { Data = (byte[])b.Data.Clone() };
                        return Color(b, bgr.ConvertTo<BGR32>((s, d) => { d.R = s.R; d.G = s.G; d.B = s.B; }));
                    case Blob.BlobType.Gray:
                    case Blob.BlobType.Probability:
                        return new ImageLayer { Name = b.Name, Kind = LayerKind.Probability, Gray = b.ToGrayImage(), TimeStamp = b.TimeStamp };
                    case Blob.BlobType.Gray16:
                        return new ImageLayer { Name = b.Name, Kind = LayerKind.Depth, Depth = b.ToGray16Image(), TimeStamp = b.TimeStamp };
                    default:
                        return null;
                }
            }
            catch
            {
                return null;   // nepodporovana konverze - vrstvu vynechame
            }
        }

        private static ImageLayer Color(Blob b, Image<BGR32> img)
            => new ImageLayer { Name = b.Name, Kind = LayerKind.Color, Color = img, TimeStamp = b.TimeStamp };
    }
}
