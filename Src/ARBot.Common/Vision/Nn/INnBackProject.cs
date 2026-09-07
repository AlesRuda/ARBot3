using System;
using System.IO;
using ARBot.Common.Common;

namespace ARBot.Common.Vision.Nn
{
    /// <summary>
    /// Segmentace sjizdnosti neuronovou siti — spolecne pro <see cref="OnnxBackProject"/> (CPU)
    /// a <see cref="RknnBackProject"/> (NPU). Proti holemu <see cref="IBackProject"/> navic
    /// vystavuje TVARY modelu, protoze podle nich se ridi mereni i diagnostika: rozliseni site
    /// urcuje, kolik dat dostane occupancy grid.
    /// </summary>
    public interface INnBackProject : IBackProject, IDisposable
    {
        /// <summary>Sirka vstupu modelu [px].</summary>
        int InputWidth { get; }
        /// <summary>Vyska vstupu modelu [px].</summary>
        int InputHeight { get; }
        /// <summary>Sirka vystupu modelu [px].</summary>
        int OutputWidth { get; }
        /// <summary>Vyska vystupu modelu [px].</summary>
        int OutputHeight { get; }
        /// <summary>Pocet kanalu vystupu modelu.</summary>
        int OutputChannels { get; }
        /// <summary>Cesta k modelu, ze ktereho instance vznikla (pro diagnostiku).</summary>
        string ModelPath { get; }
    }

    /// <summary>
    /// Otevre model podle pripony: <c>.rknn</c> jde na NPU, cokoli jineho na ONNX Runtime.
    /// Tim se volajici (runtime i <c>ARBot.Analyze</c>) nemusi rozhodovat sam a hlavne se
    /// nemuze stat, ze A/B mereni porovna dve cesty a jednu z nich omylem pusti pres tu druhou.
    /// </summary>
    public static class NnBackProject
    {
        /// <summary>Pripona, kterou se poznava model pro NPU.</summary>
        public const string RknnExtension = ".rknn";

        /// <summary>Je to model pro NPU (podle pripony)?</summary>
        public static bool IsRknn(string modelPath)
            => modelPath != null
               && Path.GetExtension(modelPath).Equals(RknnExtension, StringComparison.OrdinalIgnoreCase);

        /// <param name="modelPath">Cesta k <c>.onnx</c> nebo <c>.rknn</c> modelu.</param>
        /// <param name="channels">Poradi kanalu na vstupu site.</param>
        /// <param name="traversableChannel">Index kanalu vystupu, ktery znamena sjizdno.</param>
        /// <param name="coreMask">Jen pro NPU: ktere jadro pouzit (0 = necha rozhodnout ovladac).</param>
        public static INnBackProject Open(string modelPath,
                                          NnChannelOrder channels = NnChannelOrder.Rgb,
                                          int traversableChannel = 1,
                                          int coreMask = 0)
        {
            if (IsRknn(modelPath))
                return new RknnBackProject(modelPath, new RknnBackProjectOptions
                {
                    ChannelOrder = channels,
                    TraversableChannel = traversableChannel,
                    CoreMask = coreMask,
                });

            return new OnnxBackProject(modelPath, new OnnxBackProjectOptions
            {
                ChannelOrder = channels,
                TraversableChannel = traversableChannel,
            });
        }
    }
}
