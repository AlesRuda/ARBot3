using System;
using System.Collections.Generic;
using System.IO;

namespace ARBot.Diagnostics
{
    /// <summary>
    /// Minimalistický zapisovač animovaného GIF89a (bez externí závislosti) - pro self-test video do
    /// deníčku, když není k dispozici ffmpeg. Snímky jsou RGB (3 bajty/pixel), stejný rozměr. Kvantizace
    /// na 256 barev globální paletou přes median-cut; komprese standardním GIF LZW; nekonečná smyčka.
    /// </summary>
    public static class GifWriter
    {
        /// <summary>Zapíše animovaný GIF. <paramref name="frames"/> = RGB snímky (w*h*3). delayMs = doba snímku.</summary>
        public static bool Save(IReadOnlyList<byte[]> frames, int w, int h, int delayMs, string path)
        {
            try
            {
                if (frames == null || frames.Count == 0 || w <= 0 || h <= 0) return false;

                // 1) Globální paleta (median-cut) ze vzorku pixelů všech snímků.
                byte[] palette = BuildPalette(frames, w, h, 256, out int paletteCount);

                // 2) Mapování barva->index (cache + nejbližší) pro každý snímek.
                var indexCache = new Dictionary<int, byte>();
                var indexed = new List<byte[]>(frames.Count);
                foreach (var f in frames)
                    indexed.Add(MapToIndices(f, palette, paletteCount, indexCache));

                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using var s = File.Create(path);

                WriteHeader(s, w, h);           // GIF89a + Logical Screen Descriptor + Global Color Table
                WritePalette(s, palette);       // 256 položek RGB
                WriteLoopExtension(s);          // NETSCAPE2.0 - nekonečná smyčka

                int delayCs = Math.Max(2, delayMs / 10);   // 1/100 s
                foreach (var idx in indexed)
                {
                    WriteGraphicControl(s, delayCs);
                    WriteImageDescriptor(s, w, h);
                    WriteLzwImageData(s, idx, 8);   // minCodeSize 8 (256 barev)
                }

                s.WriteByte(0x3B);   // trailer
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("GifWriter.Save: " + ex);
                return false;
            }
        }

        // ---------- GIF struktura ----------

        private static void WriteHeader(Stream s, int w, int h)
        {
            foreach (char c in "GIF89a") s.WriteByte((byte)c);
            WriteU16(s, w); WriteU16(s, h);
            s.WriteByte(0xF7);   // packed: GCT=1, colorRes=7, sort=0, GCT size=7 -> 256 barev
            s.WriteByte(0);      // bg color index
            s.WriteByte(0);      // pixel aspect ratio
        }

        private static void WritePalette(Stream s, byte[] palette)
        {
            // Vždy 256 položek (3 bajty). palette může být kratší -> dopadneme nulami.
            s.Write(palette, 0, palette.Length);
            for (int i = palette.Length; i < 256 * 3; i++) s.WriteByte(0);
        }

        private static void WriteLoopExtension(Stream s)
        {
            s.WriteByte(0x21); s.WriteByte(0xFF); s.WriteByte(0x0B);
            foreach (char c in "NETSCAPE2.0") s.WriteByte((byte)c);
            s.WriteByte(0x03); s.WriteByte(0x01);
            WriteU16(s, 0);      // 0 = nekonečná smyčka
            s.WriteByte(0x00);
        }

        private static void WriteGraphicControl(Stream s, int delayCs)
        {
            s.WriteByte(0x21); s.WriteByte(0xF9); s.WriteByte(0x04);
            s.WriteByte(0x00);           // packed: bez průhlednosti, disposal 0
            WriteU16(s, delayCs);
            s.WriteByte(0x00);           // transparent color index (nepoužit)
            s.WriteByte(0x00);           // block terminator
        }

        private static void WriteImageDescriptor(Stream s, int w, int h)
        {
            s.WriteByte(0x2C);
            WriteU16(s, 0); WriteU16(s, 0);   // left, top
            WriteU16(s, w); WriteU16(s, h);
            s.WriteByte(0x00);                // bez lokální palety, bez prokládání
        }

        private static void WriteU16(Stream s, int v) { s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)((v >> 8) & 0xFF)); }

        // ---------- LZW (GIF varianta) ----------

        // "Nekomprimovaný" GIF LZW: žádný slovník ani změna šířky kódu (zdroj chyb) - jen literály
        // šířky minCodeSize+1 a periodický Clear code, aby slovník DEKODÉRU nepřerostl 9 bitů. Jednoduché
        // a prokazatelně korektní; větší soubor, ale pro krátké dev video stačí (viz doc/selftest.md).
        private static void WriteLzwImageData(Stream s, byte[] indices, int minCodeSize)
        {
            s.WriteByte((byte)minCodeSize);            // 8

            int codeSize = minCodeSize + 1;            // 9
            int clearCode = 1 << minCodeSize;          // 256
            int endCode = clearCode + 1;               // 257

            var bits = new BitPacker(s);
            bits.Write(clearCode, codeSize);

            // Dekodér přidává entry na každý přečtený kód; po 254 datových kódech od Clear by dosáhl 512
            // (10 bitů). Vkládáme Clear po 250 kódech (rezerva) -> dekodér zůstane na 9 bitech.
            int sinceClear = 0;
            foreach (byte idx in indices)
            {
                bits.Write(idx, codeSize);
                if (++sinceClear >= 250) { bits.Write(clearCode, codeSize); sinceClear = 0; }
            }

            bits.Write(endCode, codeSize);
            bits.Flush();
            s.WriteByte(0x00);   // block terminator
        }

        /// <summary>Balí kódy LSB-first do bajtů a zapisuje je do sub-bloků GIF (max 255 bajtů + délka).</summary>
        private sealed class BitPacker
        {
            private readonly Stream s;
            private readonly byte[] block = new byte[255];
            private int blockLen;
            private int bitBuffer, bitCount;
            public BitPacker(Stream s) => this.s = s;

            public void Write(int code, int codeSize)
            {
                bitBuffer |= code << bitCount;
                bitCount += codeSize;
                while (bitCount >= 8)
                {
                    block[blockLen++] = (byte)(bitBuffer & 0xFF);
                    bitBuffer >>= 8;
                    bitCount -= 8;
                    if (blockLen == 255) FlushBlock();
                }
            }

            public void Flush()
            {
                if (bitCount > 0)
                {
                    block[blockLen++] = (byte)(bitBuffer & 0xFF);
                    bitBuffer = 0; bitCount = 0;
                    if (blockLen == 255) FlushBlock();
                }
                FlushBlock();
            }

            private void FlushBlock()
            {
                if (blockLen == 0) return;
                s.WriteByte((byte)blockLen);
                s.Write(block, 0, blockLen);
                blockLen = 0;
            }
        }

        // ---------- Kvantizace (median-cut) ----------

        private static byte[] BuildPalette(IReadOnlyList<byte[]> frames, int w, int h, int maxColors, out int count)
        {
            // Vzorek pixelů (podvzorkovaně) přes všechny snímky pro stavbu palety.
            var sample = new List<int>(1 << 16);
            int px = w * h;
            int step = Math.Max(1, (px * frames.Count) / 100000);   // ~100k vzorků celkem
            int global = 0;
            foreach (var f in frames)
                for (int i = 0; i < px; i++, global++)
                    if (global % step == 0)
                        sample.Add((f[i * 3] << 16) | (f[i * 3 + 1] << 8) | f[i * 3 + 2]);

            if (sample.Count == 0) { count = 1; return new byte[] { 0, 0, 0 }; }

            var boxes = new List<List<int>> { sample };
            while (boxes.Count < maxColors)
            {
                int bi = PickBoxToSplit(boxes);
                if (bi < 0) break;
                var box = boxes[bi];
                int channel = LongestChannel(box);
                box.Sort((a, b) => Channel(a, channel).CompareTo(Channel(b, channel)));
                int mid = box.Count / 2;
                var lo = box.GetRange(0, mid);
                var hi = box.GetRange(mid, box.Count - mid);
                boxes[bi] = lo;
                boxes.Add(hi);
            }

            count = boxes.Count;
            var palette = new byte[count * 3];
            for (int i = 0; i < count; i++)
            {
                long r = 0, g = 0, b = 0;
                var box = boxes[i];
                foreach (int c in box) { r += (c >> 16) & 0xFF; g += (c >> 8) & 0xFF; b += c & 0xFF; }
                int n = Math.Max(1, box.Count);
                palette[i * 3] = (byte)(r / n);
                palette[i * 3 + 1] = (byte)(g / n);
                palette[i * 3 + 2] = (byte)(b / n);
            }
            return palette;
        }

        private static int PickBoxToSplit(List<List<int>> boxes)
        {
            int best = -1, bestRange = 0;
            for (int i = 0; i < boxes.Count; i++)
            {
                if (boxes[i].Count < 2) continue;
                int r = ChannelRange(boxes[i], out _);
                if (r > bestRange) { bestRange = r; best = i; }
            }
            return best;
        }

        private static int LongestChannel(List<int> box) { ChannelRange(box, out int ch); return ch; }

        private static int ChannelRange(List<int> box, out int channel)
        {
            int rmin = 255, rmax = 0, gmin = 255, gmax = 0, bmin = 255, bmax = 0;
            foreach (int c in box)
            {
                int r = (c >> 16) & 0xFF, g = (c >> 8) & 0xFF, b = c & 0xFF;
                if (r < rmin) rmin = r; if (r > rmax) rmax = r;
                if (g < gmin) gmin = g; if (g > gmax) gmax = g;
                if (b < bmin) bmin = b; if (b > bmax) bmax = b;
            }
            int rr = rmax - rmin, gr = gmax - gmin, br = bmax - bmin;
            if (rr >= gr && rr >= br) { channel = 0; return rr; }
            if (gr >= br) { channel = 1; return gr; }
            channel = 2; return br;
        }

        private static int Channel(int c, int ch) => ch == 0 ? (c >> 16) & 0xFF : ch == 1 ? (c >> 8) & 0xFF : c & 0xFF;

        private static byte[] MapToIndices(byte[] rgb, byte[] palette, int paletteCount, Dictionary<int, byte> cache)
        {
            int n = rgb.Length / 3;
            var idx = new byte[n];
            for (int i = 0; i < n; i++)
            {
                int key = (rgb[i * 3] << 16) | (rgb[i * 3 + 1] << 8) | rgb[i * 3 + 2];
                if (!cache.TryGetValue(key, out byte pi))
                {
                    pi = Nearest(rgb[i * 3], rgb[i * 3 + 1], rgb[i * 3 + 2], palette, paletteCount);
                    cache[key] = pi;
                }
                idx[i] = pi;
            }
            return idx;
        }

        private static byte Nearest(int r, int g, int b, byte[] palette, int count)
        {
            int best = 0, bestD = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                int dr = r - palette[i * 3], dg = g - palette[i * 3 + 1], db = b - palette[i * 3 + 2];
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = i; if (d == 0) break; }
            }
            return (byte)best;
        }
    }
}
