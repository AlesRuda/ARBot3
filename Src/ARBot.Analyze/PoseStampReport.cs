using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// Kontrola <b>pozy porizeni ve snimcich</b> (<see cref="CameraFrame.PoseAtCaptureX"/>) — a hlavne
    /// vycisleni toho, <b>o kolik se hranice kreslila vedle</b>, dokud se vsechno promitalo jednou
    /// „posledni znamou" pozou.
    ///
    /// <para><b>Jak se to meri.</b> Pro kazdou dvojici po sobe jdoucich snimku RUZNYCH kamer se
    /// vezme rozdil jejich poz: to je presne posun, ktery se do obrazku dostal tim, ze starsi sada
    /// bodu byla promitnuta pozou novejsiho snimku. K posunu se pripocita <b>rotacni</b> slozka
    /// prepocitana na dosah 8 m — na te zalezi vic, protoze chyba kurzu se s dalkou nasobi.</para>
    ///
    /// <para><b>Pozor, cte cele snimky</b> (obrazy), takze na gigabajtovem zaznamu to trva. Ostatni
    /// prikazy si vystaci s indexem.</para>
    /// </summary>
    public static class PoseStampReport
    {
        /// <summary>Dosah, na ktery se prepocitava chyba kurzu [m] — typicka delka prolozeni hranice.</summary>
        private const double ReachM = 8.0;

        public static void Run(RecordFile rec, int limit)
        {
            var entries = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            Console.WriteLine($"CameraFrame v indexu: {entries.Count}"
                              + (limit > 0 && limit < entries.Count ? $" (cte se prvnich {limit})" : ""));
            if (entries.Count == 0) return;
            if (limit > 0) entries = entries.Take(limit).ToList();

            var poseFromState = new PoseTrack(rec);

            int withPose = 0, read = 0, reconstructed = 0;
            var frames = new List<(string Cam, DateTime T, bool Has, double X, double Y, double Th)>();
            foreach (var e in entries)
            {
                if (!(rec.Read(e) is CameraFrame f)) continue;
                read++;
                if (f.HasPose)
                {
                    withPose++;
                    frames.Add((f.Name ?? string.Empty, f.TimeStamp, true,
                                f.PoseAtCaptureX, f.PoseAtCaptureY, f.PoseAtCaptureTheta));
                    continue;
                }

                // ZALOHA pro zaznamy verze < 6: poza se dohleda v nejblizsim tiku RobotStateMsg.
                // Je to jen priblizne (tiky jsou 10 Hz, tedy az +-50 ms), a hlavne to velikost
                // problemu SPIS PODHODNOCUJE - dva snimky blizko sebe muzou spadnout na tentyz tik
                // a rozdil pak vyjde nulovy. I tak to da spravny rad velikosti.
                var p = poseFromState.Nearest(f.TimeStamp);
                if (p == null) continue;
                reconstructed++;
                frames.Add((f.Name ?? string.Empty, f.TimeStamp, true, p.X, p.Y, p.Theta));
            }

            Console.WriteLine($"precteno {read}, pozu nese {withPose}"
                              + (withPose == 0 ? "  <- zaznam je z verze < 6 (nebo fuze pozu neznala)" : ""));
            if (reconstructed > 0)
                Console.WriteLine($"u {reconstructed} snimku dohledana z RobotStateMsg (priblizne, "
                                  + "spis podhodnocuje - viz komentar v kodu)");
            Console.WriteLine();

            var shift = new Stats("posun mezi pozami kamer [m]");
            var turn = new Stats("rozdil kurzu kamer [deg]");
            var total = new Stats($"chyba na {ReachM:F0} m (posun + rotace) [m]");
            var gap = new Stats("casovy rozestup dvojice [ms]");

            for (int i = 1; i < frames.Count; i++)
            {
                var a = frames[i - 1];
                var b = frames[i];
                if (a.Cam == b.Cam || !a.Has || !b.Has) continue;

                double dx = b.X - a.X, dy = b.Y - a.Y;
                double d = Math.Sqrt(dx * dx + dy * dy);
                double dth = Math.Atan2(Math.Sin(b.Th - a.Th), Math.Cos(b.Th - a.Th));

                shift.Add(d);
                turn.Add(Math.Abs(dth) * 180 / Math.PI);
                total.Add(d + Math.Abs(dth) * ReachM);
                gap.Add(Math.Abs((b.T - a.T).TotalMilliseconds));
            }

            Console.WriteLine("O kolik se hranice kreslila vedle, kdyz se pouzila JEDNA poza pro obe kamery:");
            Console.WriteLine("  " + gap.Line());
            Console.WriteLine("  " + shift.Line());
            Console.WriteLine("  " + turn.Line());
            Console.WriteLine("  " + total.Line());
            Console.WriteLine();
            Console.WriteLine("  (Posledni radek je horni odhad: posun pozy plus rotacni slozka na dosahu");
            Console.WriteLine("   prolozeni. Rotace je ta zradnejsi - 1,4 stupne je na 8 m 0,2 m.)");
            Console.WriteLine();

            if (withPose == 0)
            {
                Console.WriteLine("Kontrola proti RobotStateMsg se u dohledanych poz nedela (byla by nulova).");
                return;
            }

            var vsState = new Stats("|poza snimku - nejblizsi RobotStateMsg| [m]");
            foreach (var f in frames)
            {
                if (!f.Has) continue;
                var p = poseFromState.Nearest(f.T);
                if (p == null) continue;
                vsState.Add(Math.Sqrt((f.X - p.X) * (f.X - p.X) + (f.Y - p.Y) * (f.Y - p.Y)));
            }
            Console.WriteLine("Kontrola: poza ve snimku proti nejblizsimu tiku RobotStateMsg");
            Console.WriteLine("  " + vsState.Line());
            Console.WriteLine("  (Nenulove je to spravne - snimky prichazeji mezi tiky. Je to prave ten");
            Console.WriteLine("   rozdil, ktery by vrstva delala, kdyby si pozu dohledavala v historii.)");
            Console.WriteLine();
        }
    }
}
