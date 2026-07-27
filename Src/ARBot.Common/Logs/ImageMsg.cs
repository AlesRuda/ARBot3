using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using ARBot.Common.Common;
using SkiaSharp;

namespace ARBot.Common.Logs
{
    /// <summary>
    /// Zpráva nesoucí jeden obraz (<see cref="Common.Image"/>) s volitelnou kompresí při serializaci.
    /// Nahrazuje původní <c>Blob</c>: místo <c>BlobType</c> + <c>Data</c> drží přímo netypový
    /// <see cref="Common.Image"/> (jeho <see cref="Common.Image.PixelTypeName"/> je identita obrazu)
    /// a <see cref="Compression"/> určuje kompresi v <see cref="ToData"/>.
    /// Verzování dle doc/record-replay.md → Verzování zpráv.
    /// </summary>
    [Serializable()]
    public class ImageMsg : Message, INamedMessage, IHasCaptureTime
    {
        /// <summary>Typ komprese obrazu pro <see cref="Write"/>/<see cref="ReadImage(BinaryReader)"/>.</summary>
        public enum Compression
        {
            /// <summary>Surová data, bezztrátové, libovolný pixel.</summary>
            None = 0,
            /// <summary>Surová data přes DeflateStream, bezztrátové, libovolný pixel.</summary>
            Deflate = 1,
            /// <summary>Ztrátové, jen 8bit (step 1 = Gray8, step 4 = BGRA).</summary>
            Jpeg = 2,
            /// <summary>Bezztrátové, jen 8bit (step 1 / step 4).</summary>
            Png = 3
        }

        /// <summary>Kvalita JPEG komprese (0-100).</summary>
        public const int JpegQuality = 90;

        /// <summary>Verze formátu serializace (viz doc/record-replay.md → Verzování zpráv).</summary>
        public const int FormatVersion = 1;

        public ImageMsg() : base("ImageMsg", FormatVersion)
        {
        }

        /// <summary>Vytvoří zprávu nad obrazem (netypový <see cref="Common.Image"/>).</summary>
        public ImageMsg(Common.Image image, string name = null, Compression compression = Compression.None)
            : this()
        {
            Image = image;
            Name = name;
            Comp = compression;
        }

        /// <summary>Jméno zdroje (pro rozlišení v pipeline a vizualizaci).</summary>
        public string Name { get; set; }

        /// <summary>Nesený obraz (pixel typ = jeho identita).</summary>
        public Common.Image Image { get; set; }

        /// <summary>Komprese použitá při serializaci (<see cref="ToData"/>).</summary>
        public Compression Comp { get; set; } = Compression.None;

        /// <summary>Čas pořízení (např. čas snímku, ze kterého obraz vznikl).</summary>
        public DateTime TimeStamp;

        /// <inheritdoc/>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public override Message Build() => new ImageMsg();

        /// <inheritdoc/>
        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? "ImageMsg");
            ImageMsg.Write(bw, Image, Comp);   // obraz + komprese (self-popisný pixel typ)
            Write(bw, TimeStamp);
        }

        /// <inheritdoc/>
        public override void FromData(BinaryReader br)
        {
            switch (Verze)
            {
                case 1:
                    Name = br.ReadString();
                    Image = ReadImage(br);
                    TimeStamp = ReadDateTime(br);
                    break;

                default:
                    throw new NotSupportedException(
                        $"ImageMsg: nepodporovaná verze {Verze} (aktuální je {FormatVersion}).");
            }
        }

        public override string ToString() => $"{Name} - {Image?.PixelTypeName}";

        // ---------- Statická (de)serializace Image s kompresí ----------

        /// <summary>
        /// Zapíše obraz <paramref name="img"/> do <paramref name="bw"/> s kompresí <paramref name="c"/>.
        /// Formát: <c>[bool je?][string typT][byte komprese][int width][int height][int payloadLen][payload]</c>,
        /// kde <c>typT</c> = <see cref="Common.Image.PixelTypeName"/> - záznam je tím sebe-popisný a
        /// <see cref="ReadImage(BinaryReader)"/> podle něj obraz zrekonstruuje. <paramref name="img"/>
        /// smí být null (zapíše se jen příznak). Jpeg/Png podporují jen 8bit pixely (step 1 = Gray8,
        /// step 4 = BGRA); pro ostatní (např. Gray16) použij None/Deflate.
        /// </summary>
        public static void Write(BinaryWriter bw, Common.Image img, Compression c)
        {
            bw.Write(img != null);
            if (img == null)
                return;

            int step = img.Step, w = img.Width, h = img.Height;
            byte[] payload = c switch
            {
                Compression.None => img.Data,
                Compression.Deflate => Deflate(img.Data),
                Compression.Jpeg => EncodeSkia(step, w, h, img.Data, SKEncodedImageFormat.Jpeg),
                Compression.Png => EncodeSkia(step, w, h, img.Data, SKEncodedImageFormat.Png),
                _ => throw new NotSupportedException($"Neznama komprese {c}.")
            };

            bw.Write(img.PixelTypeName);   // identita pixelu (sebe-popisný záznam)
            bw.Write((byte)c);
            bw.Write(w);
            bw.Write(h);
            bw.Write(payload.Length);
            bw.Write(payload);
        }

        /// <summary>
        /// Načte obraz zapsaný <see cref="Write"/>. Vrací null, pokud byl zapsán null. Pixel typ se
        /// zrekonstruuje z uloženého názvu (<see cref="Common.Image.Create"/>) - statická znalost typu
        /// není potřeba.
        /// </summary>
        public static Common.Image ReadImage(BinaryReader br)
        {
            if (!br.ReadBoolean())
                return null;

            string typeName = br.ReadString();
            var c = (Compression)br.ReadByte();
            int w = br.ReadInt32();
            int h = br.ReadInt32();
            int len = br.ReadInt32();
            byte[] payload = br.ReadBytes(len);

            var img = Common.Image.Create(typeName, w, h);   // rekonstrukce dle uloženého pixel typu
            int step = img.Step;
            int expected = w * h * step;
            byte[] raw = c switch
            {
                Compression.None => payload,
                Compression.Deflate => Inflate(payload, expected),
                Compression.Jpeg => DecodeSkia(step, w, h, payload),
                Compression.Png => DecodeSkia(step, w, h, payload),
                _ => throw new NotSupportedException($"Neznama komprese {c}.")
            };

            img.Data = raw;   // setter ověří délku == w*h*step
            return img;
        }

        /// <summary>
        /// Typovaná varianta <see cref="ReadImage(BinaryReader)"/> - ověří, že uložený pixel typ
        /// odpovídá <typeparamref name="T"/> (jinak <see cref="InvalidDataException"/>).
        /// </summary>
        public static Image<T> ReadImage<T>(BinaryReader br) where T : IPixel, new()
        {
            var img = ReadImage(br);
            if (img == null)
                return null;
            if (img is not Image<T> typed)
                throw new InvalidDataException(
                    $"Nesouhlasí typ pixelu: záznam '{img.PixelTypeName}', požadováno '{typeof(T).Name}'.");
            return typed;
        }

        // ---------- komprese ----------

        private static byte[] Deflate(byte[] src)
        {
            // Fastest: pro real-time zaznam (napr. hloubka ~614 KB/frame) je rychlost dulezitejsi
            // nez posledni procenta uspory; Optimal byl radove pomalejsi a tvoril backlog.
            using var ms = new MemoryStream();
            using (var ds = new DeflateStream(ms, CompressionLevel.Fastest, leaveOpen: true))
                ds.Write(src, 0, src.Length);
            return ms.ToArray();
        }

        private static byte[] Inflate(byte[] src, int expectedLen)
        {
            using var ms = new MemoryStream(src);
            using var ds = new DeflateStream(ms, CompressionMode.Decompress);
            var outBuf = new byte[expectedLen];
            int off = 0, r;
            while (off < expectedLen && (r = ds.Read(outBuf, off, expectedLen - off)) > 0)
                off += r;
            if (off != expectedLen)
                throw new InvalidDataException($"Deflate: očekáváno {expectedLen} B, načteno {off} B.");
            return outBuf;
        }

        // ---------- SkiaSharp pomocné metody ----------

        /// <summary>Pixel step -> SkiaSharp color type pro Jpeg/Png (jen 8bit).</summary>
        private static SKColorType SkiaColorType(int step) => step switch
        {
            1 => SKColorType.Gray8,
            4 => SKColorType.Bgra8888,
            _ => throw new NotSupportedException(
                $"Jpeg/Png podporuje jen 8bit pixely (step 1 nebo 4), ne step {step}. Použij None/Deflate.")
        };

        /// <summary>Zakóduje pixely do JPEG/PNG (SkiaSharp) dle step.</summary>
        private static byte[] EncodeSkia(int step, int w, int h, byte[] pixels, SKEncodedImageFormat fmt)
        {
            var info = new SKImageInfo(w, h, SkiaColorType(step), SKAlphaType.Opaque);
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var img = SKImage.FromPixelCopy(info, handle.AddrOfPinnedObject(), info.RowBytes)
                    ?? throw new Exception("SKImage.FromPixelCopy selhalo");
                int quality = fmt == SKEncodedImageFormat.Jpeg ? JpegQuality : 100;
                using var data = img.Encode(fmt, quality) ?? throw new Exception($"{fmt} enkodovani selhalo");
                return data.ToArray();
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Dekóduje JPEG/PNG (SkiaSharp) zpět na surové pixely dle step.</summary>
        private static byte[] DecodeSkia(int step, int w, int h, byte[] payload)
        {
            var ct = SkiaColorType(step);
            using var dec = SKBitmap.Decode(payload) ?? throw new Exception("Dekodovani obrazu selhalo");
            using var conv = dec.Copy(ct) ?? throw new Exception("Konverze pixelu selhala");
            var dst = new byte[w * h * step];
            CopyPixels(conv, dst, step);
            return dst;
        }

        /// <summary>Zkopíruje pixely z SKBitmap do cílového pole (ošetří row padding), step bajtů/pixel.</summary>
        private static void CopyPixels(SKBitmap bmp, byte[] dst, int step)
        {
            int rowBytes = bmp.RowBytes;
            int tight = bmp.Width * step;
            IntPtr ptr = bmp.GetPixels();
            if (rowBytes == tight)
                Marshal.Copy(ptr, dst, 0, Math.Min(dst.Length, tight * bmp.Height));
            else
                for (int y = 0; y < bmp.Height; y++)
                    Marshal.Copy(IntPtr.Add(ptr, y * rowBytes), dst, y * tight, tight);
        }
    }
}
