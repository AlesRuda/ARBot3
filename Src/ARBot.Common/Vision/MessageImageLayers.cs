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
                case ImageMsg m:
                    var layer = FromImageMsg(m);
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

        /// <summary>
        /// Vrstva z <see cref="ImageMsg"/> - druh (Kind) se urcuje z pixel typu neseneho obrazu
        /// (drive z BlobType): barevne (BGR32/RGB/BGR) -&gt; Color, Gray -&gt; Probability, Gray16 -&gt; Depth.
        /// </summary>
        private static ImageLayer FromImageMsg(ImageMsg m)
        {
            try
            {
                switch (m.Image)
                {
                    case null:
                        return null;
                    case Image<BGR32> c:
                        return new ImageLayer { Name = m.Name, Kind = LayerKind.Color, Color = c, TimeStamp = m.TimeStamp };
                    case Image<RGB> rgb:
                        // TODO(NativeComputeUnit): docasne managed Image<T>.ConvertTo (barevne prevody patri na akcelerovany NativeComputeUnit).
                        return new ImageLayer { Name = m.Name, Kind = LayerKind.Color, Color = rgb.ConvertTo<BGR32>((s, d) => { d.R = s.R; d.G = s.G; d.B = s.B; }), TimeStamp = m.TimeStamp };
                    case Image<BGR> bgr:
                        return new ImageLayer { Name = m.Name, Kind = LayerKind.Color, Color = bgr.ConvertTo<BGR32>((s, d) => { d.R = s.R; d.G = s.G; d.B = s.B; }), TimeStamp = m.TimeStamp };
                    case Image<Gray> g:
                        return new ImageLayer { Name = m.Name, Kind = LayerKind.Probability, Gray = g, TimeStamp = m.TimeStamp };
                    case Image<Gray16> d16:
                        return new ImageLayer { Name = m.Name, Kind = LayerKind.Depth, Depth = d16, TimeStamp = m.TimeStamp };
                    default:
                        return null;
                }
            }
            catch
            {
                return null;   // nepodporovana konverze - vrstvu vynechame
            }
        }
    }
}
