using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// Cteni zaznamu (<c>*.rec</c>) pro offline analyzu — <b>pres index</b>, po jedne zprave.
    ///
    /// <para><b>Proc pres index a ne sekvencne.</b> <see cref="MessageReader.Read"/> vrati
    /// <c>null</c> u zpravy, kterou katalog nezna, ale ramec uz precte podle delky v hlavicce —
    /// jenze u zprav, ktere se nepodari deserializovat, se stream rozjede a cteni skonci.
    /// V praxi to znamena, ze sekvencni prochazeni zaznamu <b>skonci na prvnim
    /// <see cref="CameraFrame"/></b>. Index (<c>*.rec.idx</c>) nese offset i delku kazdeho
    /// ramce, takze se cte adresne a nezname/nezajimave zpravy jdou preskocit uplne.</para>
    ///
    /// <para><b>Katalog musi znat <see cref="CameraFrame"/> navic</b> — do
    /// <see cref="MessageCatalog.CommonDefaults"/> ho neregistruje Common, ale az HAL/aplikace.
    /// Bez toho by snimky kamer byly „nezname zpravy".</para>
    ///
    /// <para>Viz doc/record-replay.md a doc/map-correlation-localization.md.</para>
    /// </summary>
    public sealed class RecordFile : IDisposable
    {
        private readonly FileStream data;
        private readonly Dictionary<string, Message> prototypes;

        /// <summary>Vsechny zaznamy indexu v poradi, jak se zapisovaly.</summary>
        public List<IndexEntry> Index { get; }

        /// <summary>Cesta k datovemu souboru.</summary>
        public string Path { get; }

        /// <summary>Co se zjistilo pri nacitani indexu (poskozeny zaznam apod.).</summary>
        public IndexLoadReport IndexReport { get; }

        public RecordFile(string recPath)
        {
            Path = recPath;
            if (!File.Exists(recPath)) throw new FileNotFoundException("Zaznam nenalezen", recPath);

            // RecordDefaults, ne CommonDefaults: obsahuje i stavy zarizeni (GPS, motor, snimky).
            // Drive si je tenhle soubor doregistroval sam a seznam se ROZESEL s aplikaci - chybel
            // MotorStateBase, takze analyzator motorova data nikdy neprecetl a tvaril se, ze v
            // zaznamu nejsou. Viz MessageCatalog.RecordDefaults.
            var catalog = MessageCatalog.RecordDefaults();
            prototypes = catalog.ToPrototypeMap();

            // Index se overuje proti datum a pri poskozeni (useknuty / nesmyslny sidecar, chybejici
            // index) se dopocita skenem dat a opraveny zapise vedle zaznamu. Viz MessageIndex.Load.
            Index = MessageIndex.LoadFile(recPath, Encoding.UTF8, prototypes, repairSidecar: true, out var report);
            IndexReport = report;
            if (report.Damaged)
                Console.Error.WriteLine("!! " + report);

            data = new FileStream(recPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                                  1 << 16, FileOptions.RandomAccess);
        }

        /// <summary>
        /// Precte zpravu popsanou zaznamem indexu. Ramec se nacte podle offsetu a delky do pameti
        /// a teprve nad ni jde <see cref="MessageReader"/> — tim se cteni nemuze rozjet a jde
        /// citat zpravy v libovolnem poradi. Vraci <c>null</c>, kdyz zpravu katalog nezna.
        /// </summary>
        public Message Read(in IndexEntry e)
        {
            var buf = new byte[e.Length];
            data.Position = e.Offset;
            int got = 0;
            while (got < buf.Length)
            {
                int n = data.Read(buf, got, buf.Length - got);
                if (n <= 0) break;
                got += n;
            }
            using (var ms = new MemoryStream(buf, 0, got, writable: false))
            using (var r = new MessageReader(ms, Encoding.UTF8, prototypes))
                return r.Read();
        }

        /// <summary>Vycte vsechny zpravy daneho typu (podle <see cref="IndexEntry.MsgName"/>).</summary>
        public IEnumerable<T> ReadAll<T>(string msgName) where T : Message
        {
            foreach (var e in Index)
            {
                if (e.MsgName != msgName) continue;
                if (Read(e) is T t) yield return t;
            }
        }

        public void Dispose() => data?.Dispose();
    }
}
