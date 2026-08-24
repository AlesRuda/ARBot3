using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ARBot.Analyze
{
    /// <summary>
    /// Offline analyza zaznamu (<c>Records/*.rec</c>). Konzolovy nastroj — <b>zamerne
    /// v repozitari</b>: analyzatory se driv psaly jednorazove mimo projekt a kazde dalsi sezeni
    /// je muselo postavit znovu (viz doc/devlog.md, 23. 8. 2026). Namerene cislo je uzitecne jen
    /// tehdy, kdyz ho jde zopakovat — a u RANSACu, ktery je nedeterministicky, to plati dvojnasob.
    ///
    /// <para>Pouziti: <c>ARBot.Analyze corridor &lt;zaznam.rec&gt; [--old-window=60]</c>.
    /// Bez cesty vezme nejnovejsi zaznam v <c>Records/</c>.</para>
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0) { Usage(); return 1; }

            string cmd = args[0].ToLowerInvariant();
            string path = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? Newest();
            double oldWindow = Arg(args, "--old-window", 60);

            // Syntetika zaznam nepotrebuje — pravdu si generuje sama.
            if (cmd == "corridorfit" && args.Any(a => a == "--synth"))
            {
                CorridorFitReport.Synth((int)Arg(args, "--trials", 300),
                                        (int)Arg(args, "--rep", 12),
                                        Arg(args, "--gross", 0),
                                        Arg(args, "--huberk", 1.5),
                                        (int)Arg(args, "--regate", 2));
                return 0;
            }

            if (path == null) { Console.Error.WriteLine("Zadny zaznam nenalezen."); return 1; }
            Console.WriteLine($"Zaznam: {path}");
            Console.WriteLine();

            using (var rec = new RecordFile(path))
            {
                switch (cmd)
                {
                    case "corridor": CorridorReport.Run(rec, oldWindow); return 0;
                    case "corridorfit":
                        CorridorFitReport.Replay(rec, (int)Arg(args, "--rep", 12),
                                                 (int)Arg(args, "--limit", 400),
                                                 Arg(args, "--huberk", 1.5),
                                                 (int)Arg(args, "--regate", 2),
                                                 Arg(args, "--truewidth", 0),
                                                 Arg(args, "--axisy", double.NaN));
                        return 0;
                    case "edgebias":
                        EdgeBiasReport.Run(rec, Arg(args, "--truewidth", 2.0),
                                           Arg(args, "--axisy", 0),
                                           (int)Arg(args, "--limit", 400));
                        return 0;
                    case "grid":
                        GridReport.Run(rec, (int)Arg(args, "--limit", 400),
                                       Arg(args, "--roadwidth", 2.0));
                        return 0;
                    case "dump": CorridorReport.Dump(rec); return 0;
                    case "occupancy": OccupancyReport.Run(rec); return 0;
                    case "poses": PoseStampReport.Run(rec, (int)Arg(args, "--limit", 400)); return 0;
                    case "types": Types(rec); return 0;
                    default: Usage(); return 1;
                }
            }
        }

        /// <summary>Jake zpravy zaznam obsahuje — prvni vec, kterou clovek u neznameho zaznamu chce.</summary>
        private static void Types(RecordFile rec)
        {
            Console.WriteLine($"Zprav v indexu: {rec.Index.Count}");
            foreach (var g in rec.Index.GroupBy(e => e.MsgName).OrderByDescending(g => g.Count()))
            {
                var names = g.Select(e => e.Name).Where(n => !string.IsNullOrEmpty(n)).Distinct().ToList();
                Console.WriteLine($"  {g.Key,-28} {g.Count(),7}" +
                                  (names.Count > 0 ? "  [" + string.Join(", ", names) + "]" : ""));
            }
        }

        private static double Arg(string[] args, string name, double fallback)
        {
            var a = args.FirstOrDefault(x => x.StartsWith(name + "="));
            return a != null && double.TryParse(a.Substring(name.Length + 1),
                                                NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                   ? v : fallback;
        }

        /// <summary>Nejnovejsi zaznam v <c>Records/</c> (hleda se i o par urovni vys — nastroj se
        /// spousti z vlastniho bin adresare).</summary>
        private static string Newest()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var records = new DirectoryInfo(Path.Combine(dir.FullName, "Records"));
                if (!records.Exists) continue;
                var f = records.GetFiles("*.rec").OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
                if (f != null) return f.FullName;
            }
            return null;
        }

        private static void Usage()
        {
            Console.WriteLine("ARBot.Analyze <prikaz> [zaznam.rec] [prepinace]");
            Console.WriteLine();
            Console.WriteLine("  corridor   rozbor hranove lokalizace (duvody, presnost, zavislost");
            Console.WriteLine("             na parovacim rozestupu snimku)");
            Console.WriteLine("  corridorfit A/B mereni estimatoru prolozeni (osova / ortogonalni / Huber,");
            Console.WriteLine("             s prehradlovanim i bez). --synth meri proti ZNAME pravde,");
            Console.WriteLine("             nad zaznamem pak proti skutecnym bodum");
            Console.WriteLine("  edgebias   odchylka hranicnich bodu od ZNAMEHO okraje vozovky (podle");
            Console.WriteLine("             hranice, kamery a vzdalenosti) - hleda systematickou chybu");
            Console.WriteLine("  dump       CSV radek za kazdy cyklus koridoru (do souboru/rouru)");
            Console.WriteLine("  occupancy  lokalni mapa: cim je ktera bunka blokovana (geometrie/semantika)");
            Console.WriteLine("  poses      poza porizeni ve snimcich + o kolik se hranice kreslila vedle");
            Console.WriteLine("             (cte cele snimky - na velkem zaznamu to trva, viz --limit)");
            Console.WriteLine("  types      jake zpravy zaznam obsahuje a kolik jich je");
            Console.WriteLine();
            Console.WriteLine("  --old-window=<ms>  hranice, na ktere se prijata merenia rozdeli (vychozi 60)");
            Console.WriteLine("  --limit=<n>        kolik snimku precist u poses/corridorfit (vychozi 400, 0 = vse)");
            Console.WriteLine("  --synth            corridorfit nad syntetickymi daty se znamou pravdou");
            Console.WriteLine("  --rep=<n>          kolikrat zopakovat kazdou variantu (vychozi 12; RANSAC");
            Console.WriteLine("                     je nedeterministicky, jedno mereni nic neznamena)");
            Console.WriteLine("  --trials=<n>       kolik syntetickych scen na opakovani (vychozi 300)");
            Console.WriteLine("  --gross=<0..1>     podil hrubych outlieru v syntetice (vychozi 0)");
            Console.WriteLine("  --huberk=<k>       kde zacina Huberovo potlaceni (vychozi 1,5 = nasobek");
            Console.WriteLine("                     tolerance bodu; POZOR, nad 1,0 je to no-op - vsechny");
            Console.WriteLine("                     inliery jsou uz z definice pod 1,0 tolerance)");
            Console.WriteLine("  --regate=<n>       kolik pruchodu prehradlovani u variant s nim (vychozi 2)");
            Console.WriteLine("  --truewidth=<m>    ZNAMA sirka cesty - presnost se pak meri proti ni, ne proti");
            Console.WriteLine("                     filtru sirky z RoadCorridorMsg (ten se z merenii uci, takze");
            Console.WriteLine("                     je mirne kruhovy). Pro OSM/SyntetickyRovny.osm: 2.0");
            Console.WriteLine("  --axisy=<m>        osa cesty je primka y=<m> podel +X v lokalnim ENU; se");
            Console.WriteLine("                     ground truth v zaznamu se tim overi i pricna poloha a kurz.");
            Console.WriteLine("                     Pro OSM/SyntetickyRovny.osm: 0");
            Console.WriteLine();
            Console.WriteLine("Bez cesty se vezme nejnovejsi *.rec v adresari Records/.");
        }
    }
}
