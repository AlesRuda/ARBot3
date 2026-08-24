using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Vision;

namespace ARBot.Analyze
{
    /// <summary>
    /// Co <b>skutecne</b> vyrobil klasifikator sjizdnosti v BEZICI aplikaci. Cte polarni grid
    /// serializovany ve snimcich (<see cref="CameraFrame.Grid"/>), takze to neni znovu-vypocet
    /// z hloubky — je to presne to, co videlo UI.
    ///
    /// <para><b>Nacpak.</b> Nad knihovnou vychazelo, ze vyvysena trava JE prekazka (nad rovnou
    /// mapou 440 bunek proti 1 volne), ale v aplikaci hlasil autor, ze sjizdnost travu nevidi.
    /// Rozdil mezi knihovnou a behem se nedal vysvetlit hadanim, takze se meri to, co v behu
    /// opravdu vzniklo.</para>
    /// </summary>
    public static class GridReport
    {
        public static void Run(RecordFile rec, int limit, double roadWidth)
        {
            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            if (limit > 0 && limit < entries.Count) entries = entries.Take(limit).ToList();
            Console.WriteLine($"CameraFrame: {entries.Count} (cte cele snimky, chvili to trva)");
            Console.WriteLine($"pulka sirky cesty pro rozpad: {roadWidth / 2:F2} m (--roadwidth)");
            Console.WriteLine();

            int withGrid = 0, withoutGrid = 0;
            var perCam = new Dictionary<string, int[]>();     // [free, obstacle, unknown]
            var onRoad = new Dictionary<string, int[]>();
            var inGrass = new Dictionary<string, int[]>();
            var heightInGrass = new Stats("");
            var heightOnRoad = new Stats("");

            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f)) continue;
                if (f.Grid?.Cells == null) { withoutGrid++; continue; }
                withGrid++;

                string cam = f.Name ?? string.Empty;
                if (!perCam.ContainsKey(cam))
                {
                    perCam[cam] = new int[3]; onRoad[cam] = new int[3]; inGrass[cam] = new int[3];
                }

                foreach (var c in f.Grid.Cells)
                {
                    int k = c.Class == TraversabilityClass.Free ? 0
                          : c.Class == TraversabilityClass.Unknown ? 2 : 1;
                    perCam[cam][k]++;

                    if (c.Count <= 0) continue;
                    bool road = Math.Abs(c.MeanY) <= roadWidth / 2;
                    (road ? onRoad : inGrass)[cam][k]++;
                    (road ? heightOnRoad : heightInGrass).Add(c.MeanZ);
                }
            }

            Console.WriteLine($"snimku s gridem: {withGrid}, bez gridu: {withoutGrid}");
            if (withGrid == 0)
            {
                Console.WriteLine("Zadny snimek nenese grid — bud ho aplikace nepocitala, nebo je");
                Console.WriteLine("zaznam starsi verze formatu. Bez toho nejde rict nic.");
                return;
            }
            Console.WriteLine();

            Console.WriteLine("VSECHNY bunky podle kamery:");
            Console.WriteLine("  kamera        free    obstacle   unknown");
            foreach (var kv in perCam.OrderBy(k => k.Key))
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-10} {1,8} {2,10} {3,9}", kv.Key, kv.Value[0], kv.Value[1], kv.Value[2]));
            Console.WriteLine();

            Console.WriteLine("Bunky S BODY, rozpad podle toho, jestli lezi na ceste nebo v trave:");
            Console.WriteLine("  kamera / kde        free    obstacle   unknown");
            foreach (var cam in perCam.Keys.OrderBy(k => k))
            {
                Print($"{cam} / cesta", onRoad[cam]);
                Print($"{cam} / trava", inGrass[cam]);
            }
            Console.WriteLine();

            // Vyska bunek je to podstatne: kdyz trava v gridu nema vysku, neni to chyba
            // klasifikace, ale toho, co prislo z hloubky.
            Console.WriteLine("Vyska teziste bunek (MeanZ) — RIKA, jestli se trava do gridu vubec dostala:");
            Console.WriteLine("  " + heightOnRoad.Line("m") + "   <- na ceste (ma byt ~0)");
            Console.WriteLine("  " + heightInGrass.Line("m") + "   <- v trave (ma byt ~vyska travy)");
            Console.WriteLine();
            Console.WriteLine("  Kdyz je vyska v trave ~0, hloubka travu vubec neveznamenala a klasifikator");
            Console.WriteLine("  nema z ceho ji poznat. Kdyz je ~vyska travy a presto je Free, je vada");
            Console.WriteLine("  v klasifikaci (referencni rovina / prahy).");
            Console.WriteLine();

            void Print(string name, int[] v)
                => Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  {0,-18} {1,8} {2,10} {3,9}", name, v[0], v[1], v[2]));
        }
    }
}
