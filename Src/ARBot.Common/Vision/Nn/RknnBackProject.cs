using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using ARBot.Common.Common;

namespace ARBot.Common.Vision.Nn
{
    /// <summary>Nastaveni <see cref="RknnBackProject"/>.</summary>
    public sealed class RknnBackProjectOptions
    {
        /// <inheritdoc cref="OnnxBackProjectOptions.ChannelOrder"/>
        public NnChannelOrder ChannelOrder { get; set; } = NnChannelOrder.Rgb;

        /// <inheritdoc cref="OnnxBackProjectOptions.TraversableChannel"/>
        public int TraversableChannel { get; set; } = 1;

        /// <summary>
        /// Ktere jadro NPU pouzit. RK3588 ma tri; kazda kamera by mela dostat svoje, jinak si
        /// budou stat frontu. 0 = necha rozhodnout ovladac, jinak bitova maska (1, 2, 4).
        /// </summary>
        public int CoreMask { get; set; } = 0;
    }

    /// <summary>
    /// Semanticka segmentace sjizdnosti na <b>NPU</b> RK3588 (Orange Pi 5 Ultra) pres
    /// <c>librknnrt.so</c> — treti implementace <see cref="IBackProject"/> vedle
    /// <see cref="BackProject"/> (histogram) a <see cref="OnnxBackProject"/> (sit na CPU).
    ///
    /// <para><b>Kontrakt modelu:</b> <c>.rknn</c> prelozeny z float ONNX pres
    /// <c>models/onnx2rknn.py</c>, vstup <c>[1,H,W,3] uint8</c> NHWC, vystup <c>[1,H,W,C]</c>.
    /// <b>Normalizaci dela NPU</b> (<c>mean=0, std=255</c> v konfiguraci prevodu), takze se sem
    /// posilaji SYROVE pixely 0..255 — na rozdil od <see cref="OnnxBackProject"/>, kam jdou
    /// hodnoty 0..1. Vystup se cte pres <c>want_float</c>, tedy uz dekvantizovany.</para>
    ///
    /// <para><b>Proc P/Invoke a ne C++ shim</b> (jak navrhoval starsi zapis v dokumentaci):
    /// z celeho <c>rknn_api</c> jsou potreba ctyri volani a dve male struktury
    /// (<c>rknn_input</c>, <c>rknn_output</c>), ktere se roky nemeni. Shim v <c>libNativeLib.so</c>
    /// by znamenal cross-compile C++ navic, aniz by cokoli zjednodusil. Jedina velka struktura
    /// (<c>rknn_tensor_attr</c>) se pouziva jen pri startu na zjisteni tvaru a jeji nesouhlas se
    /// pozna hned — <see cref="Nhwc"/> zkontroluje, ze rozmery davaji smysl.</para>
    ///
    /// <para><b>Instance neni thread-safe</b> (predalokovane buffery i kontext) — kazda kamera
    /// ma vlastni, stejne jako u ostatnich implementaci.</para>
    ///
    /// <para>Bezi jen na ARM64 s NPU; jinde selze uz <see cref="rknn_init"/> na chybejici
    /// knihovne. Podrobnosti a mereni: doc/semantic-segmentation.md.</para>
    /// </summary>
    public sealed class RknnBackProject : INnBackProject
    {
        private const string Lib = "rknnrt";

        // --- rknn_api ---------------------------------------------------------------------
        // Typy tenzoru (rknn_tensor_type) a formaty (rknn_tensor_format) z rknn_api.h.
        private const int RKNN_TENSOR_UINT8 = 3;
        private const int RKNN_TENSOR_NHWC = 1;
        private const int RKNN_QUERY_INPUT_ATTR = 1;
        private const int RKNN_QUERY_OUTPUT_ATTR = 2;
        private const int RKNN_MAX_DIMS = 16;
        private const int RKNN_MAX_NAME_LEN = 256;

        [StructLayout(LayoutKind.Sequential)]
        private struct RknnInput
        {
            public uint Index;
            public IntPtr Buf;
            public uint Size;
            public byte PassThrough;
            public int Type;
            public int Fmt;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RknnOutput
        {
            public byte WantFloat;
            public byte IsPrealloc;
            public uint Index;
            public IntPtr Buf;
            public uint Size;
        }

        /// <summary>
        /// <c>rknn_tensor_attr</c>. Pouziva se JEN pri startu (zjisteni tvaru) — kdyby se
        /// v budouci verzi knihovny zmenila, pozna se to hned pri startu, ne az za behu.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct RknnTensorAttr
        {
            public uint Index;
            public uint NDims;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = RKNN_MAX_DIMS)]
            public uint[] Dims;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = RKNN_MAX_NAME_LEN)]
            public byte[] Name;
            public uint NElems;
            public uint Size;
            public int Fmt;
            public int Type;
            public int QntType;
            public sbyte Fl;
            public uint Zp;
            public float Scale;
            public uint WStride;
            public uint SizeWithStride;
            public byte PassThrough;
            public uint HStride;
        }

        [DllImport(Lib, EntryPoint = "rknn_init")]
        private static extern int rknn_init(out IntPtr ctx, byte[] model, uint size, uint flag, IntPtr extend);

        [DllImport(Lib, EntryPoint = "rknn_destroy")]
        private static extern int rknn_destroy(IntPtr ctx);

        [DllImport(Lib, EntryPoint = "rknn_query")]
        private static extern int rknn_query(IntPtr ctx, int cmd, ref RknnTensorAttr attr, uint size);

        [DllImport(Lib, EntryPoint = "rknn_inputs_set")]
        private static extern int rknn_inputs_set(IntPtr ctx, uint nInputs, RknnInput[] inputs);

        [DllImport(Lib, EntryPoint = "rknn_run")]
        private static extern int rknn_run(IntPtr ctx, IntPtr extend);

        [DllImport(Lib, EntryPoint = "rknn_outputs_get")]
        private static extern int rknn_outputs_get(IntPtr ctx, uint nOutputs, RknnOutput[] outputs, IntPtr extend);

        [DllImport(Lib, EntryPoint = "rknn_outputs_release")]
        private static extern int rknn_outputs_release(IntPtr ctx, uint nOutputs, RknnOutput[] outputs);

        [DllImport(Lib, EntryPoint = "rknn_set_core_mask")]
        private static extern int rknn_set_core_mask(IntPtr ctx, int coreMask);

        // --- stav -------------------------------------------------------------------------

        private IntPtr ctx;
        private readonly byte[] inputBuffer;
        private readonly float[] outputBuffer;
        private GCHandle inputPin, outputPin;
        private readonly RknnInput[] inputs = new RknnInput[1];
        private readonly RknnOutput[] outputs = new RknnOutput[1];
        private readonly RknnBackProjectOptions options;
        private readonly int rOffset, bOffset;
        private Image<BGR32> resizeTemp;

        /// <inheritdoc cref="OnnxBackProject.InputWidth"/>
        public int InputWidth { get; }
        /// <inheritdoc cref="OnnxBackProject.InputHeight"/>
        public int InputHeight { get; }
        /// <inheritdoc cref="OnnxBackProject.OutputWidth"/>
        public int OutputWidth { get; }
        /// <inheritdoc cref="OnnxBackProject.OutputHeight"/>
        public int OutputHeight { get; }
        /// <inheritdoc cref="OnnxBackProject.OutputChannels"/>
        public int OutputChannels { get; }
        /// <inheritdoc cref="OnnxBackProject.ModelPath"/>
        public string ModelPath { get; }

        /// <param name="modelPath">Cesta k <c>.rknn</c> modelu.</param>
        /// <param name="options">Nastaveni; <c>null</c> = vychozi.</param>
        public RknnBackProject(string modelPath, RknnBackProjectOptions options = null)
        {
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new ArgumentException("Cesta k modelu je prazdna.", nameof(modelPath));
            if (!File.Exists(modelPath))
                throw new FileNotFoundException($"RKNN model nenalezen: {modelPath}", modelPath);

            this.options = options ?? new RknnBackProjectOptions();
            ModelPath = modelPath;
            bool rgb = this.options.ChannelOrder == NnChannelOrder.Rgb;
            rOffset = rgb ? 2 : 0;
            bOffset = rgb ? 0 : 2;

            byte[] model = File.ReadAllBytes(modelPath);
            int rc;
            try
            {
                rc = rknn_init(out ctx, model, (uint)model.Length, 0, IntPtr.Zero);
            }
            catch (DllNotFoundException e)
            {
                throw new InvalidOperationException(
                    "librknnrt.so se nepodarilo nacist - NPU cesta bezi jen na RK3588 (Orange Pi) "
                    + "a knihovna musi lezet vedle binarky nebo v LD_LIBRARY_PATH. "
                    + "Na ostatnich strojich pouzij backproject=nn (ONNX na CPU).", e);
            }
            if (rc != 0)
                throw new InvalidOperationException(
                    $"rknn_init selhalo ({rc}) pro model '{modelPath}'. Casta pricina: model je "
                    + "prelozeny pro jinou platformu nebo jinou verzi runtime (viz models/onnx2rknn.py).");

            try
            {
                (InputHeight, InputWidth, _) = Nhwc(RKNN_QUERY_INPUT_ATTR, "vstup", 3);
                (OutputHeight, OutputWidth, OutputChannels) = Nhwc(RKNN_QUERY_OUTPUT_ATTR, "vystup", 0);

                if (this.options.TraversableChannel < 0 || this.options.TraversableChannel >= OutputChannels)
                    throw new InvalidOperationException(
                        $"TraversableChannel={this.options.TraversableChannel} je mimo pocet kanalu ({OutputChannels}).");

                // Kazda kamera na svoje jadro NPU - jinak si stoji frontu na jednom.
                if (this.options.CoreMask != 0)
                {
                    int mrc = rknn_set_core_mask(ctx, this.options.CoreMask);
                    if (mrc != 0)
                        throw new InvalidOperationException($"rknn_set_core_mask({this.options.CoreMask}) selhalo ({mrc}).");
                }

                inputBuffer = new byte[InputWidth * InputHeight * 3];
                outputBuffer = new float[OutputWidth * OutputHeight * OutputChannels];
                inputPin = GCHandle.Alloc(inputBuffer, GCHandleType.Pinned);
                outputPin = GCHandle.Alloc(outputBuffer, GCHandleType.Pinned);

                // Popisy se plni jednou; per snimek se uz jen prepisuje obsah bufferu.
                inputs[0] = new RknnInput
                {
                    Index = 0,
                    Buf = inputPin.AddrOfPinnedObject(),
                    Size = (uint)inputBuffer.Length,
                    PassThrough = 0,
                    Type = RKNN_TENSOR_UINT8,
                    Fmt = RKNN_TENSOR_NHWC,
                };
                outputs[0] = new RknnOutput
                {
                    WantFloat = 1,     // dekvantizaci si udela runtime
                    IsPrealloc = 1,    // do naseho bufferu, tedy bez alokace per snimek
                    Index = 0,
                    Buf = outputPin.AddrOfPinnedObject(),
                    Size = (uint)(outputBuffer.Length * sizeof(float)),
                };
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <inheritdoc cref="OnnxBackProject.Size"/>
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

            var src = srcImg;
            if (src.Width != InputWidth || src.Height != InputHeight)
            {
                if (resizeTemp == null || resizeTemp.Width != InputWidth || resizeTemp.Height != InputHeight)
                    resizeTemp = new Image<BGR32>(InputWidth, InputHeight);
                resizeTemp.Resize(src);
                src = resizeTemp;
            }

            FillInput(src.Data, inputBuffer, rOffset, bOffset);

            int rc = rknn_inputs_set(ctx, 1, inputs);
            if (rc != 0) throw new InvalidOperationException($"rknn_inputs_set selhalo ({rc}).");
            rc = rknn_run(ctx, IntPtr.Zero);
            if (rc != 0) throw new InvalidOperationException($"rknn_run selhalo ({rc}).");
            rc = rknn_outputs_get(ctx, 1, outputs, IntPtr.Zero);
            if (rc != 0) throw new InvalidOperationException($"rknn_outputs_get selhalo ({rc}).");

            // Postprocessing je TYZ jako u ONNX cesty (vcetne normalizace souctem kanalu, aby prah
            // 128 znamenal totez rozhodnuti) - proto se nekopiruje, ale voli se primo.
            OnnxBackProject.FillProbability(outputBuffer, destImg.Data, OutputChannels,
                                            options.TraversableChannel);

            // S IsPrealloc runtime nasi pamet neuvolnuje, jen zahodi svoje interni odkazy.
            rknn_outputs_release(ctx, 1, outputs);
        }

        /// <summary>
        /// Naplni vstupni tenzor NHWC <b>uint8</b> z <see cref="BGR32"/> dat. Proti
        /// <see cref="OnnxBackProject.FillInput"/> se NEDELI 255 — normalizaci dela NPU podle
        /// <c>mean</c>/<c>std</c> zadanych pri prevodu modelu (viz <c>models/onnx2rknn.py</c>).
        /// </summary>
        public static void FillInput(byte[] bgr32, byte[] dst, int rOffset, int bOffset)
        {
            int pixels = dst.Length / 3;
            if (bgr32.Length < pixels * 4)
                throw new ArgumentException("Zdrojovy obraz je mensi nez vstup modelu.", nameof(bgr32));

            for (int i = 0, si = 0, di = 0; i < pixels; i++, si += 4, di += 3)
            {
                dst[di] = bgr32[si + rOffset];
                dst[di + 1] = bgr32[si + 1];
                dst[di + 2] = bgr32[si + bOffset];
            }
        }

        /// <summary>Tvar tenzoru z <c>rknn_query</c>; overi, ze je NHWC a ze rozmery davaji smysl.</summary>
        private (int h, int w, int c) Nhwc(int cmd, string kind, int expectedChannels)
        {
            var attr = new RknnTensorAttr { Index = 0, Dims = new uint[RKNN_MAX_DIMS], Name = new byte[RKNN_MAX_NAME_LEN] };
            int rc = rknn_query(ctx, cmd, ref attr, (uint)Marshal.SizeOf<RknnTensorAttr>());
            if (rc != 0)
                throw new InvalidOperationException(
                    $"rknn_query({kind}) selhalo ({rc}). Nejspis nesouhlasi rozlozeni rknn_tensor_attr "
                    + "s verzi librknnrt.so - viz RknnBackProject.");

            if (attr.NDims != 4)
                throw new InvalidOperationException($"{kind} modelu ma {attr.NDims} rozmeru, ocekavaji se 4 (NHWC).");
            int n = (int)attr.Dims[0], h = (int)attr.Dims[1], w = (int)attr.Dims[2], c = (int)attr.Dims[3];
            if (n != 1 || h <= 0 || w <= 0 || c <= 0 || h > 8192 || w > 8192 || c > 4096)
                throw new InvalidOperationException(
                    $"{kind} modelu ma nesmyslny tvar [{n},{h},{w},{c}] — zkontroluj verzi librknnrt.so.");
            if (expectedChannels > 0 && c != expectedChannels)
                throw new InvalidOperationException($"{kind} modelu ma {c} kanalu, ocekava se {expectedChannels}.");
            return (h, w, c);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (ctx != IntPtr.Zero)
            {
                try { rknn_destroy(ctx); } catch { /* pri padu init uz nemusi byt co rusit */ }
                ctx = IntPtr.Zero;
            }
            if (inputPin.IsAllocated) inputPin.Free();
            if (outputPin.IsAllocated) outputPin.Free();
        }
    }
}
