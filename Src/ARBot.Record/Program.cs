using System;
using System.IO;
using System.Text;
using System.Threading;
using ARBot.Common.Common;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.HAL.Devices.Camera;
using ARBot.Common.Vision;

namespace ARBot.Record
{
    /// <summary>
    /// HW runner: z D435 bere RGB snimky, aplikuje BackProject (MessageProcessor), vysledek
    /// (+ zdrojovy RGB jako JPEG) posle jako Blob a cely beh zaznamena do souboru. Nasledne
    /// zaznam nacte a vypise statistiku.
    ///
    /// Pouziti: ARBot.Record [vystupni.rec] [delka_s]
    /// </summary>
    internal static class Program
    {
        private static readonly Encoding Enc = Encoding.UTF8;

        private static int Main(string[] args)
        {
            string outPath = args.Length > 0 ? args[0] : "capture.rec";
            string idxPath = Path.ChangeExtension(outPath, ".idx");
            double seconds = args.Length > 1 && double.TryParse(args[1], out var s) ? s : 5.0;

            Console.WriteLine($"Zaznam do: {Path.GetFullPath(outPath)}  (+ index {Path.GetFileName(idxPath)}), delka {seconds:F1} s");

            var bp = new BackProject(BackProject.RoadProbability);

            int frameCount = 0, blobCount = 0;

            using (var camera = new D435Camera())
            {
                var camSource = new SensorMessageSource<CameraFrame>(camera, controlSensor: true);
                var proc = new BackProjectProcessor(bp, includeSourceRgb: true);
                var frameCounter = new CountingSink(_ => Interlocked.Increment(ref frameCount));
                var blobCounter = new CountingSink(_ => Interlocked.Increment(ref blobCount));

                using (var dataFs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
                using (var idxFs = new FileStream(idxPath, FileMode.Create, FileAccess.Write))
                {
                    var rec = new RecordingTarget(dataFs, idxFs, Enc);

                    // graf: kamera -> [pocitadlo snimku, BackProject]; BackProject.Output -> [zaznam, pocitadlo blobu]
                    camSource.Connect(frameCounter);
                    camSource.Connect(proc);
                    proc.Output.Connect(rec);
                    proc.Output.Connect(blobCounter);

                    // start: cile pred zdroji
                    rec.Start();
                    proc.Start();
                    camSource.Start();

                    var end = DateTime.UtcNow.AddSeconds(seconds);
                    while (DateTime.UtcNow < end)
                    {
                        Thread.Sleep(500);
                        Console.WriteLine($"  ... snimky={frameCount}, bloby zapsane={rec.Count}  (kamera {(camera.IsError ? "NEPRIPOJENA" : "OK")})");
                    }

                    // stop: zdroj -> procesor -> zaznam (drain)
                    camSource.Stop();
                    proc.Stop();
                    rec.Stop();

                    Console.WriteLine($"Zaznam hotov: snimku={frameCount}, blobu={blobCount}, zapsano zprav={rec.Count}");
                }
            }

            if (frameCount == 0)
            {
                Console.WriteLine("VAROVANI: nedosel zadny snimek z kamery (nepripojena / spatny SDK?).");
            }

            // --- REPLAY: nacteni zaznamu a statistika ---
            Console.WriteLine("Nacitam zaznam zpet ...");
            var catalog = MessageCatalog.CommonDefaults();
            int rgb = 0, backproj = 0; DateTime firstTs = default, lastTs = default; bool haveTs = false;
            var reader = new ReplayCollector(m =>
            {
                if (m is ImageMsg b)
                {
                    if (b.Name == "rgb") rgb++;
                    else if (b.Name == "backproject") backproj++;
                    if (!haveTs) { firstTs = b.TimeStamp; haveTs = true; }
                    lastTs = b.TimeStamp;
                }
            });
            reader.Start();
            using (var readFs = new FileStream(outPath, FileMode.Open, FileAccess.Read))
            {
                var src = new FileMessageSource(readFs, Enc, catalog);
                src.Connect(reader);
                src.RunToEnd();
            }
            reader.Stop();

            var index = File.Exists(idxPath)
                ? MessageIndex.Read(new FileStream(idxPath, FileMode.Open, FileAccess.Read), Enc)
                : new System.Collections.Generic.List<IndexEntry>();

            Console.WriteLine($"Precteno: rgb={rgb}, backproject={backproj}, index zaznamu={index.Count}");
            if (haveTs)
                Console.WriteLine($"Casovy rozsah snimku: {firstTs:HH:mm:ss.fff} .. {lastTs:HH:mm:ss.fff}");

            long dataSize = new FileInfo(outPath).Length;
            Console.WriteLine($"Velikost zaznamu: {dataSize / 1024.0:F1} kB");

            return frameCount > 0 ? 0 : 2;
        }

        /// <summary>Sink, ktery jen zavola delegat pro kazdou zpravu (bez fronty/vlakna).</summary>
        private sealed class CountingSink : IMessageSink
        {
            private readonly Action<Message> onMsg;
            public CountingSink(Action<Message> onMsg) => this.onMsg = onMsg;
            public void Post(Message msg) => onMsg(msg);
        }

        /// <summary>Cil s vlastnim vláknem, ktery pro kazdou zpravu zavola delegat.</summary>
        private sealed class ReplayCollector : MessageTarget
        {
            private readonly Action<Message> onMsg;
            public ReplayCollector(Action<Message> onMsg) : base(OverflowPolicy.Block) => this.onMsg = onMsg;
            protected override void Consume(Message msg) => onMsg(msg);
        }
    }
}
