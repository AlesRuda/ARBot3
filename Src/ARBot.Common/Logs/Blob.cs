using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using ARBot.Common.Common;
using SkiaSharp;

namespace ARBot.Common.Logs
{
    [Serializable()]
    public class Blob : Message, INamedMessage, IHasCaptureTime
    {
        public enum BlobType
        {
            Jpeg = 0,
            UVYV = 1,
            HSV = 2,
            RGB = 3,
            Gray = 4,
            Probability = 5,
            Data = 6,
            Fract16 = 7,
            BGR = 8,
            Gray16 = 9,
            BGR32 = 10
        }

        /// <summary>Kvalita JPEG komprese (0-100).</summary>
        public const int JpegQuality = 90;

        public Blob() : base("Blob", 2)
        {
        }

        /// <summary>Cas porizeni (napr. cas snimku, ze ktereho blob vznikl). Serializace od Verze 2.</summary>
        public DateTime TimeStamp;

        /// <inheritdoc/>
        DateTime IHasCaptureTime.CaptureTime => TimeStamp;

        public static Blob FromRGB(string name, int width, int height, byte[] bytes)
        {
            Blob b = new Blob();
            b.Name = name;
            b.Height = height;
            b.Width = width;
            b.Type = Blob.BlobType.RGB;
            b.Data = bytes;

            return b;

        }

        // --- JPEG komprese (SkiaSharp, cross-platform vc. arm64) ---
        // compress=true: Type=Jpeg a Data se spocitaji LINE (lazyData) az pri prvnim pristupu
        // (typicky pri serializaci) - komprese tak neblokuje vlakno kamery. Zdrojove pixely se
        // snapshotuji (klon), aby je kamera mezitim neprepsala.

        public static Blob FromImage(string name, Image<BGR> image, bool compress)
        {
            var b = new Blob { Name = name, Width = image.Width, Height = image.Height };
            if (compress)
            {
                b.Type = BlobType.Jpeg;
                int w = image.Width, h = image.Height;
                byte[] bgra = ExpandToBgra(image.Data, w * h, rIndex: 2, gIndex: 1, bIndex: 0);
                b.lazyData = () => EncodeJpeg(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque), bgra);
            }
            else
            {
                b.Type = BlobType.BGR;
                b.Data = (byte[])image.Data.Clone();
            }
            return b;
        }

        public static Blob FromImage(string name, Image<BGR32> image, bool compress)
        {
            var b = new Blob { Name = name, Width = image.Width, Height = image.Height };
            if (compress)
            {
                b.Type = BlobType.Jpeg;
                int w = image.Width, h = image.Height;
                // BGR32 je uz B,G,R,x -> primo Bgra8888 (alpha se ignoruje, Opaque).
                byte[] src = (byte[])image.Data.Clone();
                b.lazyData = () => EncodeJpeg(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque), src);
            }
            else
            {
                b.Type = BlobType.BGR32;
                b.Data = (byte[])image.Data.Clone();
            }
            return b;
        }

        public static Blob FromImage(string name, Image<RGB> image, bool compress)
        {
            var b = new Blob { Name = name, Width = image.Width, Height = image.Height };
            if (compress)
            {
                b.Type = BlobType.Jpeg;
                int w = image.Width, h = image.Height;
                byte[] bgra = ExpandToBgra(image.Data, w * h, rIndex: 0, gIndex: 1, bIndex: 2);
                b.lazyData = () => EncodeJpeg(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque), bgra);
            }
            else
            {
                b.Type = BlobType.RGB;
                b.Data = (byte[])image.Data.Clone();
            }
            return b;
        }

        public static Blob FromImage(string name, Image<Gray16> image, bool compress)
        {
            var b = new Blob { Name = name, Width = image.Width, Height = image.Height };
            if (compress)
            {
                // Pozn.: JPEG je 8bit - komprese hloubky (Gray16) ZTRACI presnost (bere horni bajt).
                // Pro verny zaznam hloubky pouzij bezztratovy Type=Gray16 (compress=false).
                b.Type = BlobType.Jpeg;
                int w = image.Width, h = image.Height;
                byte[] gray8 = Gray16ToGray8(image.Data, w * h);
                b.lazyData = () => EncodeJpeg(new SKImageInfo(w, h, SKColorType.Gray8, SKAlphaType.Opaque), gray8);
            }
            else
            {
                b.Type = BlobType.Gray16;
                b.Data = (byte[])image.Data.Clone();
            }
            return b;
        }


        public static Blob FromImage(string name, Image<Gray> image)
        {
            Blob b = new Blob();
            b.Name = name;
            b.Height = image.Height;
            b.Width = image.Width;
            b.Type = Blob.BlobType.Probability;
            b.Data = image.Data;

            return b;

        }

        public static Blob FromProbability(string name, int width, int height, byte[] bytes)
        {
            Blob b = new Blob();
            b.Name = name;
            b.Height = height;
            b.Width = width;
            b.Type = Blob.BlobType.Probability;
            b.Data = bytes;

            return b;

        }

        public Image<Gray> ToGrayImage()
        {
            if (Type != BlobType.Probability && Type != BlobType.Gray)
                throw new Exception("Nepodporovany typ");
            Image<Gray> i = new Image<Gray>(Width, Height);
            i.Data = Data.Clone() as byte[];
            return i;
        }

        public Image<Gray16> ToGray16Image()
        {
            if (Type != BlobType.Gray16)
                throw new Exception("Nepodporovany typ");
            Image<Gray16> i = new Image<Gray16>(Width, Height);
            i.Data = Data.Clone() as byte[];
            return i;
        }

        public Image<RGB> ToRGBImage()
        {
            if (Type == BlobType.Probability || Type == BlobType.Gray)
            {
                Image<Gray> i = new Image<Gray>(Width, Height);
                Gray p = new Gray();
                p.Data = Data;
                Image<RGB> irgb = new Image<RGB>(Width, Height);
                RGB rgb = new RGB();
                rgb.Data = irgb.Data;
                int k = 0;
                for(int j=0;j<i.DataLength;j++, k+=rgb.Count)
                {
                    p.Index = j;
                    rgb.Index = k;

                    byte v = p.Value;
                    if (v < 128)
                        rgb.R = v;
                    else
                        rgb.G = v;
                }

                return irgb;
            }
            if (Type == BlobType.RGB)
            {
                Image<RGB> i = new Image<RGB>(Width, Height);
                i.Data = Data.Clone() as byte[];
                return i;
            }
            throw new Exception("Nepodporovany typ");
        }



        public Image<BGR32> ToBGR32Image()
        {
            if (Type == BlobType.Jpeg)
            {
                using var dec = SKBitmap.Decode(Data) ?? throw new Exception("Dekodovani JPEG selhalo");
                using var bgra = dec.Copy(SKColorType.Bgra8888) ?? throw new Exception("Konverze na BGRA selhala");
                var img = new Image<BGR32>(bgra.Width, bgra.Height);
                CopyPixels(bgra, img.Data);
                return img;
            }
            if (Type == BlobType.BGR32)
            {
                Image<BGR32> i = new Image<BGR32>(Width, Height);
                i.Data = Data.Clone() as byte[];
                return i;
            }
            throw new Exception("Nepodporovany typ");
        }

        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public BlobType Type { get; set; }
        private byte[] locData;
        protected Func<byte[]> lazyData;
        public byte[] Data
        {
            get
            {
                if (locData == null && lazyData != null)
                {
                    lock(this)
                        if(locData==null)
                            locData = lazyData();
                }
                return locData;
            }
            set
            {
                lazyData = null;
                locData = value;
            }
        }
        public override void ToData(BinaryWriter bw)
        {
            bw.Write(Name ?? "Blob");
            bw.Write(Width);
            bw.Write(Height);
            bw.Write((int)Type);
            bw.Write(Data.Length);
            bw.Write(Data);
            Write(bw, TimeStamp);   // Verze 2
        }

        public override void FromData(BinaryReader br)
        {
            Name = br.ReadString();
            Width = br.ReadInt32();
            Height = br.ReadInt32();
            Type = (BlobType)br.ReadInt32();
            int len = br.ReadInt32();
            Data = br.ReadBytes(len);
            if (Verze >= 2)
                TimeStamp = ReadDateTime(br);
        }

        public override Message Build()
        {
            return new Blob();
        }

        public override string ToString()
        {
            return string.Format("{0} - {1}", Name, Type);
        }

        // ---------- SkiaSharp pomocne metody ----------

        /// <summary>Zakoduje pixely do JPEG (SkiaSharp). Delka pixels musi odpovidat info.</summary>
        private static byte[] EncodeJpeg(SKImageInfo info, byte[] pixels)
        {
            var handle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                using var img = SKImage.FromPixelCopy(info, handle.AddrOfPinnedObject(), info.RowBytes)
                    ?? throw new Exception("SKImage.FromPixelCopy selhalo");
                using var data = img.Encode(SKEncodedImageFormat.Jpeg, JpegQuality)
                    ?? throw new Exception("JPEG enkodovani selhalo");
                return data.ToArray();
            }
            finally
            {
                handle.Free();
            }
        }

        /// <summary>Rozsiri 3bajtovy pixel na BGRA (4 bajty, alpha=255) podle indexu kanalu ve zdroji.</summary>
        private static byte[] ExpandToBgra(byte[] src, int pixelCount, int rIndex, int gIndex, int bIndex)
        {
            var dst = new byte[pixelCount * 4];
            for (int i = 0; i < pixelCount; i++)
            {
                int s = i * 3, d = i * 4;
                dst[d + 0] = src[s + bIndex]; // B
                dst[d + 1] = src[s + gIndex]; // G
                dst[d + 2] = src[s + rIndex]; // R
                dst[d + 3] = 255;             // A
            }
            return dst;
        }

        /// <summary>Prevede 16bit gray (little-endian) na 8bit (horni bajt).</summary>
        private static byte[] Gray16ToGray8(byte[] src, int pixelCount)
        {
            var dst = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
                dst[i] = src[i * 2 + 1];
            return dst;
        }

        /// <summary>Zkopiruje pixely z SKBitmap (Bgra8888) do ciloveho pole (osetruje row padding).</summary>
        private static void CopyPixels(SKBitmap bgra, byte[] dst)
        {
            int rowBytes = bgra.RowBytes;
            int tight = bgra.Width * 4;
            IntPtr ptr = bgra.GetPixels();
            if (rowBytes == tight)
            {
                Marshal.Copy(ptr, dst, 0, dst.Length);
            }
            else
            {
                for (int y = 0; y < bgra.Height; y++)
                    Marshal.Copy(IntPtr.Add(ptr, y * rowBytes), dst, y * tight, tight);
            }
        }
    }
}
