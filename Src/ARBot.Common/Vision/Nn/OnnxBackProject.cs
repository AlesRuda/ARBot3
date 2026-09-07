using System;
using System.Drawing;
using System.IO;
using System.Linq;
using ARBot.Common.Common;
using Microsoft.ML.OnnxRuntime;

namespace ARBot.Common.Vision.Nn
{
    /// <summary>
    /// Poradi barevnych kanalu, ve kterem byl model trenovan.
    /// </summary>
    public enum NnChannelOrder
    {
        /// <summary>R, G, B - poradi z ARBot2 (<c>EdgeTPUDll/EdgeTPU.cpp</c>), vychozi.</summary>
        Rgb,
        /// <summary>B, G, R - poradi bajtu v <see cref="BGR32"/>, tedy bez prehozeni.</summary>
        Bgr,
    }

    /// <summary>Nastaveni <see cref="OnnxBackProject"/>.</summary>
    public sealed class OnnxBackProjectOptions
    {
        /// <summary>
        /// Poradi kanalu na vstupu modelu. Vychozi <see cref="NnChannelOrder.Rgb"/> je prevzate
        /// z ARBot2, kde se do tenzoru plnilo <c>src[+2], src[+1], src[+0]</c> nad <see cref="BGR32"/>
        /// (viz <c>EdgeTPUDll/EdgeTPU.cpp</c>, funkce <c>SemanticSegmentation</c>).
        /// </summary>
        public NnChannelOrder ChannelOrder { get; set; } = NnChannelOrder.Rgb;

        /// <summary>
        /// Index kanalu vystupu, ktery znamena SJIZDNO. Vychozi 1 - ARBot2 rozhodovalo
        /// <c>out[0] &lt; out[1] ? 255 : 0</c>, tedy kanal 1 je "cesta".
        /// </summary>
        public int TraversableChannel { get; set; } = 1;

        /// <summary>
        /// Pocet vlaken jedne inference. Vychozi 1: <c>Process</c> bezi SYNCHRONNE na vlakne
        /// kamery a kamery jsou dve, takze rozpustit jednu inferenci pres vsechna jadra by si
        /// kamery jen prebiraly CPU (a bralo by to ridici smycce, viz doc/perf-monitoring.md).
        /// </summary>
        public int Threads { get; set; } = 1;
    }

    /// <summary>
    /// Semanticka segmentace sjizdnosti neuronovou siti pres ONNX Runtime - alternativa
    /// k <see cref="BackProject"/> (zpetna projekce barev z histogramu) za tymz rozhranim
    /// <see cref="IBackProject"/>.
    ///
    /// <para><b>Kontrakt modelu</b> (zajisti ho <c>models/tflite2onnx.py</c>): vstup
    /// <c>[1,H,W,3] float32</c> v rozsahu 0..1, vystup <c>[1,H,W,C] float32</c> s pravdepodobnosti.
    /// Kvantovane I/O se VEDOME nepodporuje - jinak by tahle trida musela znat scale/zero_point
    /// modelu (presne to delal ARBot2 rucne v C++) a spatna konstanta by se projevila jako tise
    /// horsi segmentace, ne jako chyba. Konverzni skript kvantizaci schova dovnitr modelu, takze
    /// vnitrek zustava int8 a na hranici se mluvi v realnych jednotkach.</para>
    ///
    /// <para><b>Instance neni thread-safe</b> - drzi predalokovane buffery, aby
    /// <see cref="Process"/> nealokoval. Kazda kamera ma proto vlastni instanci, stejne jako
    /// dnes ma vlastni <see cref="BackProject"/> (viz <c>ARBotRuntime.BuildVision</c>).</para>
    ///
    /// <para>Podrobnosti a mereni: doc/semantic-segmentation.md.</para>
    /// </summary>
    public sealed class OnnxBackProject : INnBackProject
    {
        private const float Inv255 = 1f / 255f;

        private readonly InferenceSession session;
        private readonly RunOptions runOptions;
        private readonly OrtValue inputValue;
        private readonly OrtValue outputValue;
        private readonly string[] inputNames;
        private readonly string[] outputNames;
        private readonly float[] inputBuffer;
        private readonly float[] outputBuffer;

        /// <summary>Sirka vstupu modelu [px].</summary>
        public int InputWidth { get; }
        /// <summary>Vyska vstupu modelu [px].</summary>
        public int InputHeight { get; }
        /// <summary>Sirka vystupu modelu [px].</summary>
        public int OutputWidth { get; }
        /// <summary>Vyska vystupu modelu [px].</summary>
        public int OutputHeight { get; }
        /// <summary>Pocet kanalu vystupu modelu.</summary>
        public int OutputChannels { get; }
        /// <summary>Cesta k modelu, ze ktereho instance vznikla (pro diagnostiku).</summary>
        public string ModelPath { get; }

        private readonly OnnxBackProjectOptions options;
        private readonly int rOffset, bOffset;
        private Image<BGR32> resizeTemp;

        /// <param name="modelPath">Cesta k <c>.onnx</c> modelu.</param>
        /// <param name="options">Nastaveni; <c>null</c> = vychozi.</param>
        public OnnxBackProject(string modelPath, OnnxBackProjectOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("Cesta k modelu je prazdna.", nameof(modelPath));
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"Model neuralove site nenalezen: {modelPath}", modelPath);

            this.options = options ?? new OnnxBackProjectOptions();
            ModelPath = modelPath;

            // BGR32 ma v pameti poradi B(+0) G(+1) R(+2); model chce trojici v poradi
            // ChannelOrder. Prehozeni se resi jen posunem, ne vetvenim v horke smycce.
            bool rgb = this.options.ChannelOrder == NnChannelOrder.Rgb;
            rOffset = rgb ? 2 : 0;
            bOffset = rgb ? 0 : 2;

            var so = new SessionOptions
            {
                IntraOpNumThreads = Math.Max(1, this.options.Threads),
                InterOpNumThreads = 1,
                ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };

            try
            {
                session = new InferenceSession(modelPath, so);
            }
            catch (Exception e)
            {
                so.Dispose();
                throw new InvalidOperationException(
                    $"Model '{modelPath}' se nepodarilo nacist do ONNX Runtime: {e.Message}", e);
            }

            var input = session.InputMetadata.First();
            var output = session.OutputMetadata.First();
            inputNames = new[] { input.Key };
            outputNames = new[] { output.Key };

            (InputHeight, InputWidth, _) = Nhwc(input.Value.Dimensions, input.Key, "vstup", 3);
            (OutputHeight, OutputWidth, OutputChannels) = Nhwc(output.Value.Dimensions, output.Key, "vystup", 0);

            RequireFloat(input.Value, input.Key, "vstup");
            RequireFloat(output.Value, output.Key, "vystup");

            if (this.options.TraversableChannel < 0 || this.options.TraversableChannel >= OutputChannels)
                throw new InvalidOperationException(
                    $"TraversableChannel={this.options.TraversableChannel} je mimo pocet kanalu vystupu ({OutputChannels}).");

            inputBuffer = new float[InputWidth * InputHeight * 3];
            outputBuffer = new float[OutputWidth * OutputHeight * OutputChannels];

            // OrtValue nad vlastnimi buffery: vstup i vystup se predava BEZ alokace per snimek.
            inputValue = OrtValue.CreateTensorValueFromMemory(
                inputBuffer, new long[] { 1, InputHeight, InputWidth, 3 });
            outputValue = OrtValue.CreateTensorValueFromMemory(
                outputBuffer, new long[] { 1, OutputHeight, OutputWidth, OutputChannels });
            runOptions = new RunOptions();
        }

        /// <summary>
        /// Rozmer pravdepodobnostniho obrazu = rozmer VYSTUPU modelu, bez ohledu na rozmer
        /// vstupniho snimku. Volajici (<c>CameraFrameProcessor</c>) podle toho snimek zmensi;
        /// kdyz se vstupni a vystupni rozmer modelu lisi, dorovna si to <see cref="Process"/> sam.
        /// </summary>
        public Size Size(int width, int height) => new Size(OutputWidth, OutputHeight);

        /// <inheritdoc/>
        public void Process(Image<BGR32> srcImg, Image<Gray> destImg)
        {
            if (srcImg == null) throw new ArgumentNullException(nameof(srcImg));
            if (destImg == null) throw new ArgumentNullException(nameof(destImg));
            if (destImg.Width != OutputWidth || destImg.Height != OutputHeight)
                throw new ArgumentException(
                    $"destImg ma byt {OutputWidth}x{OutputHeight} (vystup modelu), je {destImg.Width}x{destImg.Height}.",
                    nameof(destImg));

            // Vstup modelu ma svuj vlastni rozmer; kdyz snimek neprisel v nem, zmensi se sem.
            var src = srcImg;
            if (src.Width != InputWidth || src.Height != InputHeight)
            {
                if (resizeTemp == null || resizeTemp.Width != InputWidth || resizeTemp.Height != InputHeight)
                    resizeTemp = new Image<BGR32>(InputWidth, InputHeight);
                resizeTemp.Resize(src);
                src = resizeTemp;
            }

            FillInput(src.Data, inputBuffer, rOffset, bOffset);
            session.Run(runOptions, inputNames, new[] { inputValue }, outputNames, new[] { outputValue });
            FillProbability(outputBuffer, destImg.Data, OutputChannels, options.TraversableChannel);
        }

        /// <summary>
        /// Naplni vstupni tenzor (NHWC, float 0..1) z dat <see cref="BGR32"/> obrazu.
        /// Oddelene od <see cref="Process"/>, aby slo testovat bez modelu.
        /// </summary>
        /// <param name="bgr32">Data zdrojoveho obrazu (4 bajty na pixel, poradi B G R X).</param>
        /// <param name="dst">Cilovy tenzor, delka = pocet pixelu * 3.</param>
        /// <param name="rOffset">Posun bajtu, ktery patri na prvni kanal (2 = R, tedy RGB).</param>
        /// <param name="bOffset">Posun bajtu, ktery patri na treti kanal (0 = B, tedy RGB).</param>
        public static void FillInput(byte[] bgr32, float[] dst, int rOffset, int bOffset)
        {
            int pixels = dst.Length / 3;
            if (bgr32.Length < pixels * 4)
                throw new ArgumentException("Zdrojovy obraz je mensi nez vstup modelu.", nameof(bgr32));

            for (int i = 0, si = 0, di = 0; i < pixels; i++, si += 4, di += 3)
            {
                dst[di] = bgr32[si + rOffset] * Inv255;
                dst[di + 1] = bgr32[si + 1] * Inv255;
                dst[di + 2] = bgr32[si + bOffset] * Inv255;
            }
        }

        /// <summary>
        /// Prevede vystup modelu na sedy pravdepodobnostni obraz 0..255 (jako
        /// <see cref="BackProject"/>, ktery take vraci spojitou hodnotu, ne 0/255).
        ///
        /// <para>Pri vice kanalech se hodnota NORMALIZUJE souctem kanalu. Neni to kosmetika:
        /// ARBot2 rozhodovalo <c>out[0] &lt; out[1]</c>, kdezto prah 128 nad surovym kanalem 1
        /// by dal jiny vysledek - model konci sigmoidou, takze soucet kanalu neni presne 1
        /// (nameren rozsah 0,85 az 1,18). Po normalizaci odpovida prah 128 presne tomu
        /// puvodnimu rozhodnuti a mezilehle hodnoty nesou duveru, kterou occupancy fuze
        /// (log-odds) umi vyuzit.</para>
        /// </summary>
        /// <param name="output">Vystup modelu (NHWC).</param>
        /// <param name="dst">Cilova data <see cref="Gray"/> obrazu, delka = pocet pixelu.</param>
        /// <param name="channels">Pocet kanalu vystupu.</param>
        /// <param name="traversableChannel">Index kanalu se sjizdnosti.</param>
        public static void FillProbability(float[] output, byte[] dst, int channels, int traversableChannel)
        {
            int pixels = dst.Length;
            if (output.Length < pixels * channels)
                throw new ArgumentException("Vystup modelu je mensi nez cilovy obraz.", nameof(output));

            for (int i = 0, oi = 0; i < pixels; i++, oi += channels)
            {
                float v = output[oi + traversableChannel];
                if (channels > 1)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++) sum += output[oi + c];
                    v = sum > 1e-6f ? v / sum : 0f;
                }
                int q = (int)(v * 255f + 0.5f);
                dst[i] = q <= 0 ? (byte)0 : q >= 255 ? (byte)255 : (byte)q;
            }
        }

        /// <summary>Rozlozi tvar NHWC a overi, ze je staticky (davka smi byt neurcena).</summary>
        private static (int h, int w, int c) Nhwc(int[] dims, string name, string kind, int expectedChannels)
        {
            if (dims == null || dims.Length != 4)
                throw new InvalidOperationException(
                    $"{kind} '{name}' modelu ma mit 4 rozmery (NHWC), ma {(dims == null ? 0 : dims.Length)}.");
            if (dims[0] > 1)
                throw new InvalidOperationException($"{kind} '{name}' modelu ma davku {dims[0]}, ocekava se 1.");
            for (int i = 1; i < 4; i++)
                if (dims[i] <= 0)
                    throw new InvalidOperationException(
                        $"{kind} '{name}' modelu ma neurceny rozmer na pozici {i}. Model musi mit pevne tvary "
                        + "- prevod resi models/tflite2onnx.py.");
            if (expectedChannels > 0 && dims[3] != expectedChannels)
                throw new InvalidOperationException(
                    $"{kind} '{name}' modelu ma {dims[3]} kanalu, ocekava se {expectedChannels}.");
            return (dims[1], dims[2], dims[3]);
        }

        private static void RequireFloat(NodeMetadata meta, string name, string kind)
        {
            if (meta.ElementType != typeof(float))
                throw new InvalidOperationException(
                    $"{kind} '{name}' modelu je typu {meta.ElementType.Name}, ocekava se float32. "
                    + "Kvantovane I/O se nepodporuje - prevedi model pres models/tflite2onnx.py, "
                    + "ktery kvantizaci schova dovnitr modelu.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            inputValue?.Dispose();
            outputValue?.Dispose();
            runOptions?.Dispose();
            session?.Dispose();
        }
    }
}
