using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ARBot.Common.Logs;

namespace ARBot.Common.Communication
{
    /// <summary>
    /// Jeden zaznam indexu (sidecar) - popisuje jednu zpravu v datovem souboru.
    /// </summary>
    public struct IndexEntry
    {
        /// <summary>Poradove cislo zpravy (od 0).</summary>
        public long Seq { get; set; }
        /// <summary>Offset zacatku ramce v datovem souboru.</summary>
        public long Offset { get; set; }
        /// <summary>Delka celeho ramce (hlavicka + payload) v bajtech.</summary>
        public int Length { get; set; }
        /// <summary>Cas porizeni (T_in) v tickach (0 = neznamy).</summary>
        public long CaptureTicks { get; set; }
        /// <summary>Cas prichodu na Stream (T_out) v tickach (0 = neznamy). Stampuje <see cref="RecordingTarget"/>.</summary>
        public long ArrivalTicks { get; set; }
        /// <summary>Nazev typu zpravy.</summary>
        public string MsgName { get; set; }
        /// <summary>Jmeno instance zpravy z <see cref="ARBot.Common.Logs.INamedMessage"/> (jinak prazdne).</summary>
        public string Name { get; set; }

        /// <summary>Cas porizeni (T_in) jako <see cref="DateTime"/>. K tomuto okamziku se data uplatnuji.</summary>
        public DateTime CaptureTime => new DateTime(CaptureTicks);
        /// <summary>Cas prichodu (T_out) jako <see cref="DateTime"/>. Okamzik kdy se data dostanou do streamu, ale jejich platnost je zpetne k CaptureTime.</summary>
        public DateTime ArrivalTime => new DateTime(ArrivalTicks);
    }

    /// <summary>
    /// Zapisovac indexu (sidecar <c>*.idx</c>). Append-only, po jednom zaznamu na zpravu.
    /// </summary>
    public sealed class MessageIndexWriter : IDisposable
    {
        private BinaryWriter bw;
        private bool disposed;

        public MessageIndexWriter(Stream s, Encoding encoding)
        {
            bw = new BinaryWriter(s, encoding);
        }

        /// <summary>Zapise jeden zaznam indexu.</summary>
        public void Write(in IndexEntry e)
        {
            bw.Write(e.Seq);
            bw.Write(e.Offset);
            bw.Write(e.Length);
            bw.Write(e.CaptureTicks);
            bw.Write(e.ArrivalTicks);
            bw.Write(e.MsgName ?? string.Empty);
            bw.Write(e.Name ?? string.Empty);
        }

        public void Flush() => bw.Flush();

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            bw?.Dispose();
            bw = null;
        }
    }

    /// <summary>
    /// Co se pri nacitani indexu zjistilo a udelalo - aby volajici (UI, analyzator) umel rict,
    /// ze zaznam byl poskozeny a co z nej zbylo. Viz doc/record-replay.md, "Poskozeny zaznam".
    /// </summary>
    public sealed class IndexLoadReport
    {
        /// <summary>Sidecar <c>.idx</c> existoval.</summary>
        public bool SidecarFound;
        /// <summary>Sidecar koncil uprostred zaznamu (useknuty zapis).</summary>
        public bool SidecarTruncated;
        /// <summary>Pocet zaznamu, ktere se ze sidecaru precetly cele.</summary>
        public int SidecarEntries;
        /// <summary>Pocet zaznamu sidecaru, ktere se zahodily (nesouhlasi s daty: mimo soubor, nula, diry).</summary>
        public int SidecarDiscarded;
        /// <summary>Index se (cely nebo od nejakeho mista) postavil znovu ze skenu dat.</summary>
        public bool Rebuilt;
        /// <summary>Od jakeho offsetu dat se skenovalo (0 = cely soubor).</summary>
        public long RebuiltFromOffset;
        /// <summary>Pocet zaznamu doplnenych skenem dat.</summary>
        public int RebuiltEntries;
        /// <summary>Delka datoveho souboru [B].</summary>
        public long DataBytes;
        /// <summary>Bajty na konci dat, ktere netvori cely ramec (useknuty posledni snimek apod.).</summary>
        public long TrailingBytes;
        /// <summary>Vysledny pocet zaznamu indexu.</summary>
        public int Entries;

        /// <summary>Byl zaznam v nejakem ohledu poskozeny (index nebo data)?</summary>
        public bool Damaged => SidecarTruncated || SidecarDiscarded > 0 || Rebuilt || TrailingBytes > 0;

        /// <summary>Jednoradkovy popis pro log / UI.</summary>
        public override string ToString()
        {
            if (!Damaged) return $"index OK ({Entries} zaznamu)";
            var sb = new StringBuilder("POSKOZENY ZAZNAM: ");
            if (!SidecarFound) sb.Append("index chybel; ");
            if (SidecarTruncated) sb.Append("index useknuty; ");
            if (SidecarDiscarded > 0) sb.Append($"{SidecarDiscarded} zaznamu indexu nesouhlasilo s daty; ");
            if (Rebuilt) sb.Append($"skenem dat od offsetu {RebuiltFromOffset} doplneno {RebuiltEntries} zprav; ");
            if (TrailingBytes > 0) sb.Append($"na konci dat {TrailingBytes} B useknuteho ramce (ztraceno); ");
            sb.Append($"k dispozici {Entries} zprav");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Cteni indexu do pameti (pro seek podle casu/typu) - a jeho <b>oprava ze samotnych dat</b>,
    /// kdyz je sidecar poskozeny.
    ///
    /// <para><b>Proc oprava existuje (3. 9. 2026):</b> robotu dosla baterie uprostred zaznamu. Data
    /// (<c>.rec</c>) prezila cela az na posledni useknuty snimek, ale sidecar ne: u jednoho zaznamu
    /// koncil uprostred polozky, u druheho obsahoval nuly a polozky ukazujici za konec dat (zapis
    /// pres page cache pri vypadku napajeni). <see cref="Read"/> na to padal
    /// <see cref="EndOfStreamException"/> a View zaznam neotevrel vubec - pritom ~99 % dat bylo
    /// v poradku. Index je ale jen odvozenina dat: kazdy ramec zacina hlavickou
    /// <c>"Jmeno:delka:verze"</c> (viz <see cref="MessageWriter"/>), takze ho jde ze skenu dat
    /// postavit znovu. Jedine, co sken nezna, je T_out (<see cref="IndexEntry.ArrivalTicks"/>) -
    /// ten se u doplnenych polozek nahradi T_in (viz <see cref="Rebuild"/>).</para>
    /// </summary>
    public static class MessageIndex
    {
        /// <summary>Nejdelsi pripustna hlavicka ramce (jmeno:delka:verze) [B]. Delsi = ne hlavicka, ale smeti.</summary>
        private const int MaxHeaderBytes = 200;

        /// <summary>Nejvetsi pripustny payload ramce [B]. Vetsi = poskozena delka.</summary>
        private const int MaxFrameBytes = 256 * 1024 * 1024;

        /// <summary>
        /// Nacte vsechny zaznamy indexu ze streamu. <b>Toleruje useknuty konec:</b> polozka, ktera
        /// neni cela, se zahodi a cteni skonci (drive to byla vyjimka, ktera znemoznila otevrit
        /// cely zaznam). Jestli se to stalo, rika <see cref="Read(Stream, Encoding, out bool)"/>.
        /// </summary>
        public static List<IndexEntry> Read(Stream s, Encoding encoding) => Read(s, encoding, out _);

        /// <summary>
        /// Nacte vsechny CELE zaznamy indexu ze streamu.
        /// </summary>
        /// <param name="truncated">true, kdyz stream koncil uprostred polozky (useknuty zapis).</param>
        public static List<IndexEntry> Read(Stream s, Encoding encoding, out bool truncated)
        {
            var list = new List<IndexEntry>();
            truncated = false;
            using (var br = new BinaryReader(s, encoding, leaveOpen: true))
            {
                while (s.Position < s.Length)
                {
                    long start = s.Position;
                    try
                    {
                        var e = new IndexEntry
                        {
                            Seq = br.ReadInt64(),
                            Offset = br.ReadInt64(),
                            Length = br.ReadInt32(),
                            CaptureTicks = br.ReadInt64(),
                            ArrivalTicks = br.ReadInt64(),
                            MsgName = br.ReadString(),
                            Name = br.ReadString()
                        };
                        list.Add(e);
                    }
                    catch (EndOfStreamException)
                    {
                        truncated = true;
                        Trace.WriteLine($"MessageIndex: index useknuty uprostred polozky #{list.Count} "
                                        + $"(bajt {start} z {s.Length}); nekompletni polozka zahozena.");
                        break;
                    }
                    catch (IOException ex)
                    {
                        // BinaryReader.ReadString hazi IOException na zapornou delku retezce = smeti
                        // misto polozky (nuly, cizi data). Dal se cist neda.
                        truncated = true;
                        Trace.WriteLine($"MessageIndex: index poskozeny u polozky #{list.Count} "
                                        + $"(bajt {start}): {ex.Message}");
                        break;
                    }
                }
            }
            return list;
        }

        /// <summary>
        /// Nacte index k datovemu souboru a <b>overi ho proti datum</b>; co nesouhlasi nebo chybi,
        /// doplni skenem dat. Vraci vzdy pouzitelny index (i pro zaznam bez sidecaru).
        /// </summary>
        /// <param name="data">Datovy soubor (<c>.rec</c>); pozice se zmeni.</param>
        /// <param name="sidecar">Sidecar (<c>.idx</c>), nebo null, kdyz neexistuje.</param>
        /// <param name="encoding">Kodovani zaznamu.</param>
        /// <param name="prototypes">Prototypy zprav (<see cref="MessageCatalog.ToPrototypeMap"/>) - pro
        /// cas porizeni a jmeno doplnovanych polozek. Neznamy typ dostane cas 0 a prazdne jmeno,
        /// polozka se ale zalozi (ramec v datech je).</param>
        /// <param name="report">Co se zjistilo a udelalo.</param>
        public static List<IndexEntry> Load(Stream data, Stream sidecar, Encoding encoding,
                                            IReadOnlyDictionary<string, Message> prototypes,
                                            out IndexLoadReport report)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            report = new IndexLoadReport { DataBytes = data.Length };

            var list = new List<IndexEntry>();
            if (sidecar != null)
            {
                report.SidecarFound = true;
                list = Read(sidecar, encoding, out bool truncated);
                report.SidecarTruncated = truncated;
                report.SidecarEntries = list.Count;

                // Overeni proti datum: polozky musi na sebe navazovat od nuly a lezet v souboru.
                // Prvni nesouhlasici polozka a vsechno za ni jde pryc (za ni uz neni cemu verit -
                // typicky nuly nebo polozky ukazujici za konec dat po vypadku napajeni).
                int valid = 0;
                long expect = 0;
                foreach (var e in list)
                {
                    if (e.Offset != expect || e.Length <= 0 || string.IsNullOrEmpty(e.MsgName)
                        || e.Offset + e.Length > data.Length)
                        break;
                    expect = e.Offset + e.Length;
                    valid++;
                }
                if (valid < list.Count)
                {
                    report.SidecarDiscarded = list.Count - valid;
                    Trace.WriteLine($"MessageIndex: {report.SidecarDiscarded} polozek indexu od #{valid} "
                                    + "nesouhlasi s daty - zahozeno, zbytek se dopocita skenem dat.");
                    list.RemoveRange(valid, list.Count - valid);
                }
            }

            long from = list.Count > 0 ? list[list.Count - 1].Offset + list[list.Count - 1].Length : 0;
            if (from < data.Length)
            {
                // Index (nebo jeho zbytek) chybi - data ale muzou byt v poradku: sken ramcu.
                report.Rebuilt = true;
                report.RebuiltFromOffset = from;
                long seq = list.Count > 0 ? list[list.Count - 1].Seq + 1 : 0;
                long prevTicks = list.Count > 0 ? list[list.Count - 1].CaptureTicks : 0;
                int before = list.Count;
                report.TrailingBytes = Rebuild(data, from, seq, prevTicks, encoding, prototypes, list);
                report.RebuiltEntries = list.Count - before;
                if (report.RebuiltEntries > 0 || report.TrailingBytes > 0)
                    Trace.WriteLine($"MessageIndex: skenem dat od {from} doplneno {report.RebuiltEntries} "
                                    + $"zprav, na konci {report.TrailingBytes} B useknuteho ramce.");
                // Cely soubor byl jen useknuty ramec na konci (index sam byl uplny) - to neni oprava.
                if (report.RebuiltEntries == 0 && !report.SidecarTruncated && report.SidecarDiscarded == 0)
                    report.Rebuilt = false;
            }

            report.Entries = list.Count;
            return list;
        }

        /// <summary>
        /// Postavi polozky indexu skenem datoveho souboru od <paramref name="fromOffset"/>: cte
        /// hlavicky ramcu <c>"Jmeno:delka:verze"</c>, deserializuje zpravu kvuli casu porizeni a
        /// jmenu a prida polozku. Skonci u prvniho ramce, ktery neni cely nebo nema platnou
        /// hlavicku. Vraci pocet bajtu od toho mista do konce souboru (ztracena data).
        ///
        /// <para><b>T_out doplnenych polozek</b> (<see cref="IndexEntry.ArrivalTicks"/>) sken nezna -
        /// nastavi se na T_in, a kdyz zprava cas porizeni nema (T_in = 0, jako v puvodnim indexu),
        /// na posledni zname T_in, aby casova osa zustala monotonni. Telemetrie tak u doplnenych
        /// zprav ukaze nulove zpozdeni v pipeline; je to poctivejsi nez vymyslet cislo.</para>
        /// </summary>
        public static long Rebuild(Stream data, long fromOffset, long firstSeq, long prevCaptureTicks,
                                   Encoding encoding, IReadOnlyDictionary<string, Message> prototypes,
                                   List<IndexEntry> into)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (into == null) throw new ArgumentNullException(nameof(into));
            encoding = encoding ?? Encoding.UTF8;

            long pos = fromOffset;
            long seq = firstSeq;
            long prevTicks = prevCaptureTicks;
            var br = new BinaryReader(data, encoding, leaveOpen: true);

            while (pos < data.Length)
            {
                data.Position = pos;
                if (!TryReadHeader(data, encoding, out string msgName, out int payloadLen, out int version))
                    break;
                long payloadStart = data.Position;
                long end = payloadStart + payloadLen;
                if (end > data.Length) break;                         // useknuty posledni ramec

                long capture = 0;
                string name = string.Empty;
                if (prototypes != null && prototypes.TryGetValue(msgName, out var proto))
                {
                    try
                    {
                        // Verze ramce je v hlavicce; deserializace potrebuje jen payload.
                        byte[] payload = br.ReadBytes(payloadLen);
                        var msg = proto.Build();
                        msg.Verze = version;
                        msg.FromData(encoding, payload);
                        if (msg is IHasCaptureTime h) capture = h.CaptureTime.Ticks;
                        if (msg is INamedMessage nm) name = nm.Name ?? string.Empty;
                    }
                    catch (Exception ex)
                    {
                        // Ramec je cely, jen ho neumime rozebrat (stara verze, vadny payload):
                        // polozka se zalozi bez casu, at se da zaznam aspon prehrat sekvencne.
                        Trace.WriteLine($"MessageIndex: ramec {msgName} na offsetu {pos} nejde deserializovat: {ex.Message}");
                    }
                }

                // T_in zustava 0 = "nezname" (stejne jako v puvodnim indexu u zprav bez casu porizeni);
                // T_out sken nezna, nahradi se T_in, resp. poslednim znamym T_in, at je osa monotonni.
                if (capture != 0) prevTicks = capture;
                into.Add(new IndexEntry
                {
                    Seq = seq++,
                    Offset = pos,
                    Length = (int)(end - pos),
                    CaptureTicks = capture,
                    ArrivalTicks = capture != 0 ? capture : prevTicks,
                    MsgName = msgName,
                    Name = name,
                });
                pos = end;
            }
            return data.Length - pos;
        }

        /// <summary>
        /// Precte hlavicku ramce (<see cref="BinaryWriter.Write(string)"/>: 7-bitova delka + UTF-8
        /// <c>"Jmeno:delka:verze"</c>) a overi, ze vypada jako hlavicka. Delku cte rucne a stropuje,
        /// aby smeti v datech (nuly, binarni payload) nevedlo k alokaci obri stringu.
        /// </summary>
        private static bool TryReadHeader(Stream s, Encoding encoding, out string msgName, out int payloadLen,
                                          out int version)
        {
            msgName = null;
            payloadLen = 0;
            version = 1;

            int len = 0, shift = 0;
            for (int i = 0; i < 5; i++)
            {
                int b = s.ReadByte();
                if (b < 0) return false;
                len |= (b & 0x7F) << shift;
                shift += 7;
                if ((b & 0x80) == 0) break;
                if (i == 4) return false;
            }
            if (len <= 0 || len > MaxHeaderBytes) return false;

            var buf = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = s.Read(buf, got, len - got);
                if (n <= 0) return false;
                got += n;
            }

            string header;
            try { header = encoding.GetString(buf); }
            catch { return false; }

            var parts = header.Split(':');
            if (parts.Length < 2 || parts.Length > 3) return false;
            string name = parts[0];
            if (name.Length == 0) return false;
            foreach (char c in name)
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '.')) return false;
            if (!int.TryParse(parts[1], out int plen) || plen < 0 || plen > MaxFrameBytes) return false;
            if (parts.Length == 3 && int.TryParse(parts[2], out int v)) version = v;

            msgName = name;
            payloadLen = plen;
            return true;
        }

        /// <summary>
        /// Nacte index k zaznamu na disku (<paramref name="recPath"/> + <c>.idx</c>), overi ho proti
        /// datum a kdyz ho bylo nutne opravit, <b>zapise opraveny sidecar</b> (puvodni prejmenuje
        /// na <c>.idx.bad</c>), aby se pri dalsim otevreni uz neskenovalo.
        /// </summary>
        /// <param name="repairSidecar">Zapsat opraveny <c>.idx</c> na disk (jen kdyz byl vadny).</param>
        public static List<IndexEntry> LoadFile(string recPath, Encoding encoding,
                                                IReadOnlyDictionary<string, Message> prototypes,
                                                bool repairSidecar, out IndexLoadReport report)
        {
            if (recPath == null) throw new ArgumentNullException(nameof(recPath));
            string idxPath = recPath + ".idx";
            List<IndexEntry> list;
            using (var data = new FileStream(recPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16))
            using (var idx = File.Exists(idxPath)
                       ? new FileStream(idxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
                       : null)
            {
                list = Load(data, idx, encoding, prototypes, out report);
            }

            bool sidecarWrong = report.SidecarTruncated || report.SidecarDiscarded > 0 || report.RebuiltEntries > 0;
            if (repairSidecar && sidecarWrong)
            {
                try
                {
                    if (File.Exists(idxPath))
                    {
                        string bad = idxPath + ".bad";
                        if (File.Exists(bad)) File.Delete(bad);
                        File.Move(idxPath, bad);
                    }
                    using (var fs = new FileStream(idxPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var w = new MessageIndexWriter(fs, encoding))
                    {
                        foreach (var e in list) w.Write(e);
                        w.Flush();
                    }
                    Trace.WriteLine($"MessageIndex: opraveny index zapsan do {idxPath} (puvodni jako .idx.bad).");
                }
                catch (Exception ex)
                {
                    // Oprava na disku je jen pohodli - zaznam se otevre i bez ni (index je v pameti).
                    Trace.WriteLine($"MessageIndex: opraveny index se nepodarilo zapsat: {ex.Message}");
                }
            }
            return list;
        }
    }
}
