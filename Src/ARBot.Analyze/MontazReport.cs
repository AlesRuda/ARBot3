using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using ARBot.Common.Communication;
using ARBot.Common.Devices;
using ARBot.Common.Logs;
using ARBot.Common.Models;
using ARBot.Common.Vision;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Montaz obou D435 (vyska, sklon, naklon) z roviny zeme</b> (prikaz <c>montaz</c>, tema
    /// <c>vid-kalibrace-kamer-bias</c>, bod 7 pruzkumu, 13. 10. 2026). Z <c>ImageDepth</c> a projekce, kterou
    /// nese kazdy snimek (od 2. 9. 2026), prolozi rovinu zeme v ramci kamery a z jeji normaly spocte montaz
    /// (<see cref="GroundPlaneMount"/>); porovna ji s montazi, se kterou runtime v te jizde pocital
    /// (<c>Projection.Transformation</c> = <c>Profile</c>). Yaw ani posun X/Y z roviny urcit nejde.
    ///
    /// <para><b>Varianty hlavniho bodu</b> (vid-leva-kamera-hlavni-bod), jen u skutecne leve D435:
    /// <b>A</b> = PP ze zaznamu, tedy jak pocital runtime; <b>B</b> = <c>(W−1−PPx, H−1−PPy)</c> - JEDINY
    /// spravny hlavni bod obrazu otoceneho o 180° (RealSense i <c>CameraProjection</c> maji stred pixelu na
    /// celem cisle; overeno simulaci v revizi 13. 10. 2026); <b>C</b> = <c>(W−PPx, H−PPy)</c>, tedy vzorec,
    /// ktery dnes v driveru je - o 1 px vedle (~0,21° ve sklonu). Vsechny tri pocitaji z TYCHZ pixelu, takze
    /// rozdil je cisty vliv PP. ⚠️ Zaznamy PRED opravou prevraceni nesou surovy PP: A = dnesni stav,
    /// B = po oprave (C, kdyby oprava jen zprovoznila dnesni vzorec). Zaznamy PO oprave nesou uz otoceny
    /// PP - pak se pousti s <c>--pp-opraveno</c> a plati jen A.</para>
    ///
    /// <para><b>Co se meri:</b> montaz vuci zemi 0,5–2,5 m pred robotem, ne vuci gravitaci - presne to,
    /// co predpoklada grid (zem z = 0 v tele). Snimky se berou jen za jizdy rovne (vyradi rucni naklapeni
    /// pri kalibraci magnetometru i stani v depu, ktere by opakovalo tutez scenu); zamrzly stream (stejna
    /// hloubka po sobe) se preskakuje. ⚠️ <b>Nahodny</b> sklon terenu se medianem vyprumeruje, ale
    /// <b>systematicky</b> pricny sklon cesty vuci telu (vyklenuta vozovka, po ktere se jezdi vzdy vpravo)
    /// ne: da leve a prave kamere opacny posun sklonu (±φ·sin 29°) a obema stejny posun naklonu (≈ −0,875·φ)
    /// - proto souhrn tiskne spolecnou slozku naklonu. ⚠️ Absolutni sklon a naklon leve kamery zavisi na
    /// prahu inlieru a zacatku pasma radove o 0,2° (revize 13. 10. 2026); rozdil variant A/B ne.</para>
    ///
    /// <para><b>VN100</b>: u kazdeho platneho snimku se vezme naklon z nejblizsiho <c>IMUState</c> VN100
    /// (<c>YPR().Pitch</c> <b>kladne = nos NAHORU</b>, <c>Roll</c> = do strany; overeno proti gravitaci
    /// 22. 9. 2026 a <c>bumps</c> blok 1). Sklopeni tela nosem dolu o δ by tedy posunulo klopeni VN100
    /// o −δ a sklon obou kamer o −δ·cos 29°. Absolutni hodnote se neveri (akcelerometr +6,9 %)
    /// a ⚠️ ani median za jizdu neni montaz: zavisi na smeru trasy a sklonu terenu (vliv teploty senzoru z dat oddelit nejde)
    /// (29. 9. 2026 trasa na sever −0,91°, na jih +1,97°, kamery se lisily o mene nez 0,4°). Telo od
    /// drzaku kamer rozlisi jen srovnani tychz useku ve stejnem smeru.</para>
    /// </summary>
    public static class MontazReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        /// <summary>Nastaveni mereni (prepinace prikazu).</summary>
        public sealed class Nastaveni
        {
            public int Snimku = 150;          // na kameru a jizdu
            public double VMin = 0.2;         // m/s
            public double WMax = 0.3;         // rad/s
            public bool Vse;                  // bez filtru jizdy
            public double RMin = 0.5, RMax = 2.5, MaxZ = 0.15;
            public int KrokPx = 3;
            public double Prah = 0.03;        // m, prah inlieru
            public int MinInlieru = 300;
            public int MinSnimkuJizdy = 20;   // jizda s mene platnymi snimky se do souhrnu nebere
            public bool PpOpraveno;           // zaznamy uz nesou otoceny PP leve kamery (po oprave)
            public DateTime? Od, Do;          // filtr podle zacatku zaznamu
            public string Csv;
        }

        private static readonly string[] JmenaVariant = { "A ze zaznamu", "B W-1-PP", "C W-PP" };

        private sealed class Kamera
        {
            public string Jmeno;              // prvni slovo jmena (+ „(virt)" u virtualni)
            public bool Otocena, Virtualni;
            public (double Yaw, double Pitch, double Roll, double H) Montaz = (double.NaN, double.NaN, double.NaN, double.NaN);
            public double PpX = double.NaN, PpY = double.NaN, F = double.NaN;
            public int W, H;
            public readonly List<double>[] Pitch = { new(), new(), new() };
            public readonly List<double>[] Roll = { new(), new(), new() };
            public readonly List<double>[] Vyska = { new(), new(), new() };
            public readonly List<double> PitchBlizko = new(), PitchDaleko = new(), Inl = new(), Rms = new();
            public readonly List<double> ImuPitch = new(), ImuRoll = new();
            public int Vybrano, BezProjekce, Zamrzle, Neproslo;
            public int Variant => Otocena ? 3 : 1;
        }

        private sealed class Jizda
        {
            public string Zaznam;
            public DateTime Zacatek;
            public readonly Dictionary<string, Kamera> Kamery = new();
        }

        /// <summary>
        /// Seznam zaznamu: jeden soubor, nebo vsechny <c>*.rec</c> v adresarich (oddelenych <c>;</c>).
        /// Vynechaji se polozky <paramref name="vynechat"/> (carkami; jmeno s priponou i bez, relativni nebo
        /// absolutni cesta) a duplikaty se stejnou velikosti souboru (kopie jizd v jinem adresari) - ⚠️ vcetne
        /// KOPIE vynechaneho zaznamu pod jinym jmenem (napr. <c>test/20260919-100414</c> = <c>Robotour2026/Kolo1a</c>),
        /// jinak by se zakazany zaznam otevrel pres kopii. Chybejici soubor/adresar je <see cref="IOException"/>.
        /// </summary>
        public static List<string> Zaznamy(string jeden, string adresare, string vynechat)
        {
            var bez = (vynechat ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                      .Select(b => b.Replace('\\', '/')).ToList();
            var pouzite = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var vysledek = new List<string>();
            List<string> kandidati;
            if (adresare == null)
            {
                if (jeden == null) return vysledek;
                if (Directory.Exists(jeden)) throw new IOException($"'{jeden}' je adresar - pouzij --adresar={jeden}");
                if (!File.Exists(jeden)) throw new FileNotFoundException($"zaznam '{jeden}' neexistuje");
                kandidati = new List<string> { jeden };
            }
            else
            {
                kandidati = new List<string>();
                foreach (var d in adresare.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Directory.Exists(d)) throw new DirectoryNotFoundException($"adresar '{d}' neexistuje");
                    kandidati.AddRange(Directory.GetFiles(d, "*.rec").OrderBy(x => x, StringComparer.Ordinal));
                }
            }

            // Nejdriv vynechane a jejich velikosti, az pak ostatni - kopie vynechaneho muze byt v poradi drive.
            var vynechane = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var velikosti = new HashSet<long>();
            foreach (var f in kandidati)
            {
                string plna = Path.GetFullPath(f).Replace('\\', '/');
                string jmeno = Path.GetFileName(f), kmen = Path.GetFileNameWithoutExtension(f);
                var shoda = bez.FirstOrDefault(b => b.Equals(kmen, StringComparison.OrdinalIgnoreCase)
                                                    || b.Equals(jmeno, StringComparison.OrdinalIgnoreCase)
                                                    || plna.Equals(Path.GetFullPath(b).Replace('\\', '/'), StringComparison.OrdinalIgnoreCase)
                                                    || ("/" + plna).EndsWith("/" + b.TrimStart('.', '/'), StringComparison.OrdinalIgnoreCase));
                if (shoda == null) continue;
                pouzite.Add(shoda);
                vynechane.Add(f);
                velikosti.Add(new FileInfo(f).Length);
                Console.WriteLine($"vynechano (--vynechat): {f}");
            }
            var vynechaneVelikosti = new HashSet<long>(velikosti);
            foreach (var f in kandidati)
            {
                if (vynechane.Contains(f)) continue;
                long len = new FileInfo(f).Length;
                if (!velikosti.Add(len))
                {
                    Console.WriteLine(vynechaneVelikosti.Contains(len)
                        ? $"vynechano (kopie zaznamu z --vynechat - stejna velikost): {f}"
                        : $"vynechano (duplikat - stejna velikost jako jiny zaznam): {f}");
                    continue;
                }
                vysledek.Add(f);
            }
            foreach (var b in bez.Where(b => !pouzite.Contains(b)))
                Console.WriteLine($"POZOR --vynechat: polozka '{b}' nesedi na zadny zaznam");
            return vysledek;
        }

        public static void Run(IReadOnlyList<string> zaznamy, Nastaveni n)
        {
            n.MinSnimkuJizdy = Math.Max(1, n.MinSnimkuJizdy);
            Console.WriteLine(string.Format(Ci,
                "montaz: snimku na kameru {0}, filtr jizdy {1}, pasmo {2:F2}-{3:F2} m, |z| <= {4:F2} m (predvyber), krok {5} px, prah inlieru {6:F3} m{7}",
                n.Snimku, n.Vse ? "VYPNUTY" : string.Format(Ci, "|v| >= {0:F2} m/s, |w| <= {1:F2} rad/s", n.VMin, n.WMax),
                n.RMin, n.RMax, n.MaxZ, n.KrokPx, n.Prah, n.PpOpraveno ? ", PP leve kamery uz OPRAVENY (jen varianta A)" : ""));
            if (!n.PpOpraveno)
            {
                Console.WriteLine("Varianty hlavniho bodu (jen skutecna leva D435): A = ze zaznamu (jak pocital runtime),");
                Console.WriteLine("  B = W-1-PP (spravny pro obraz otoceny o 180 deg), C = W-PP (dnesni vzorec driveru, o 1 px vedle).");
                Console.WriteLine("  Zaznamy do opravy vid-leva-kamera-hlavni-bod: A = dnesni stav, B = po oprave; po oprave --pp-opraveno.");
            }
            Console.WriteLine();
            if (n.Csv != null) ZalozCsv(n.Csv);   // prepise - opakovany beh nesmi radky zdvojit

            var jizdy = new List<Jizda>();
            foreach (var cesta in zaznamy)
            {
                Console.WriteLine($"=== {cesta}");
                try
                {
                    using var rec = new RecordFile(cesta);
                    var j = Jedna(rec, n);
                    if (j == null) { Console.WriteLine(); continue; }
                    jizdy.Add(j);
                    Vypis(j, n);
                    if (n.Csv != null) PripisCsv(n.Csv, j);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  CHYBA: {ex.Message}");
                }
                Console.WriteLine();
            }
            if (jizdy.Count > 1) Souhrn(jizdy, n);
        }

        private static Jizda Jedna(RecordFile rec, Nastaveni n)
        {
            var j = new Jizda { Zaznam = Path.GetFileName(rec.Path) };
            long t0 = rec.Index.Where(e => e.CaptureTicks > 0).Select(e => e.CaptureTicks).DefaultIfEmpty(0).Min();
            j.Zacatek = t0 > 0 ? new DateTime(t0) : DateTime.MinValue;
            if ((n.Od.HasValue && j.Zacatek < n.Od.Value) || (n.Do.HasValue && j.Zacatek >= n.Do.Value))
            {
                Console.WriteLine(string.Format(Ci, "  zacatek {0:yyyy-MM-dd HH:mm} mimo --od/--do - preskoceno", j.Zacatek));
                return null;
            }

            // Stav robotu pro filtr jizdy.
            var stavy = new List<(DateTime T, double V, double W)>();
            if (!n.Vse)
                foreach (var e in rec.Index)
                    if (e.MsgName == "RobotStateMsg" && rec.Read(e) is RobotStateMsg s)
                        stavy.Add((s.TimeStamp, s.V, s.Omega));
            stavy.Sort((a, b) => a.T.CompareTo(b.T));
            bool filtr = !n.Vse && stavy.Count > 0;
            if (!n.Vse && stavy.Count == 0) Console.WriteLine("  bez RobotStateMsg - filtr jizdy vypnuty");

            // VN100 (jen z indexu; cte se jen u platnych snimku).
            var imu = rec.Index.Where(e => e.MsgName == "IMUState" && (e.Name ?? "").Contains("VN100"))
                               .OrderBy(e => e.CaptureTicks).ToList();

            var poKamerach = rec.Index.Where(e => e.MsgName == "CameraFrame")
                                      .GroupBy(e => KameraJmeno(e.Name) + ((e.Name ?? "").Contains(' ') ? "" : " (virt)"))
                                      .OrderBy(g => g.Key, StringComparer.Ordinal);
            var px = new List<GroundPlaneMount.GroundPixel>();
            var pts = new List<Vector3>();
            var blizko = new List<Vector3>();
            var daleko = new List<Vector3>();
            foreach (var g in poKamerach)
            {
                // Otoceny obraz (Swap) ma jen skutecna leva D435 - ta ma ve jmenu seriove cislo
                // („Left 740112071040"); virtualni kamera vraci hole „Left" a obraz neotaci.
                bool skutecna = !g.Key.EndsWith(" (virt)");
                var k = new Kamera
                {
                    Jmeno = g.Key, Virtualni = !skutecna,
                    Otocena = skutecna && !n.PpOpraveno && g.Key.Equals("Left", StringComparison.OrdinalIgnoreCase),
                };
                j.Kamery[k.Jmeno] = k;
                var vhodne = g.Where(e => !filtr || Jede(stavy, e.CaptureTime, n)).OrderBy(e => e.CaptureTicks).ToList();
                k.Vybrano = vhodne.Count;
                if (vhodne.Count == 0) continue;
                int pocet = Math.Min(n.Snimku, vhodne.Count);
                ulong posledniOtisk = 0;
                for (int i = 0; i < pocet; i++)
                {
                    var e = vhodne[(int)((long)i * vhodne.Count / pocet)];
                    if (!(rec.Read(e) is CameraFrame f) || f.Projection?.InverseIntrinsics == null
                        || f.ImageDepth?.Data == null) { k.BezProjekce++; continue; }
                    var intr = f.Projection.InverseIntrinsics;
                    int w = intr.Width, h = intr.Height;
                    if (f.ImageDepth.Width != w || f.ImageDepth.Height != h || f.ImageDepth.Data.Length < w * h * 2)
                    { k.BezProjekce++; continue; }
                    ulong otisk = Otisk(f.ImageDepth.Data);
                    if (otisk == posledniOtisk) { k.Zamrzle++; continue; }
                    posledniOtisk = otisk;

                    var m = f.Projection.Transformation;
                    if (double.IsNaN(k.Montaz.Pitch))
                    {
                        k.Montaz = GroundPlaneMount.Decompose(m);
                        k.PpX = intr.PPx; k.PpY = intr.PPy; k.F = intr.Fx; k.W = w; k.H = h;
                    }
                    var up = GroundPlaneMount.UpInCamera(m);
                    GroundPlaneMount.SelectGroundPixels(f.ImageDepth.Data, w, h, intr.Fx, intr.Fy, intr.PPx, intr.PPy,
                                                        m, n.KrokPx, n.RMin, n.RMax, n.MaxZ, px);
                    var pp = new List<(double X, double Y)> { (intr.PPx, intr.PPy) };
                    if (k.Otocena) { pp.Add((w - 1 - intr.PPx, h - 1 - intr.PPy)); pp.Add((w - intr.PPx, h - intr.PPy)); }

                    var fity = new GroundPlaneMount.Fit[pp.Count];
                    bool ok = true;
                    for (int v = 0; v < pp.Count && ok; v++)
                    {
                        GroundPlaneMount.Deproject(px, intr.Fx, intr.Fy, pp[v].X, pp[v].Y, pts);
                        ok = GroundPlaneMount.TryFit(pts, up, n.Prah, 200, 1, 10 * Math.PI / 180, n.MinInlieru, out fity[v]);
                    }
                    if (!ok) { k.Neproslo++; continue; }
                    for (int v = 0; v < pp.Count; v++)
                    {
                        k.Pitch[v].Add(fity[v].Pitch); k.Roll[v].Add(fity[v].Roll); k.Vyska[v].Add(fity[v].HeightM);
                    }
                    k.Inl.Add((double)fity[0].Inliers / Math.Max(1, fity[0].Points));
                    k.Rms.Add(fity[0].RmsM);

                    // Blizko / daleko (varianta A).
                    GroundPlaneMount.Deproject(px, intr.Fx, intr.Fy, intr.PPx, intr.PPy, pts);
                    double rMid = 0.5 * (n.RMin + n.RMax);
                    blizko.Clear(); daleko.Clear();
                    foreach (var p in pts)
                    {
                        var b = Vector3.Transform(p, m);
                        (Math.Sqrt((double)b.X * b.X + (double)b.Y * b.Y) < rMid ? blizko : daleko).Add(p);
                    }
                    if (GroundPlaneMount.TryFit(blizko, up, n.Prah, 200, 1, 10 * Math.PI / 180, n.MinInlieru / 2, out var fb))
                        k.PitchBlizko.Add(fb.Pitch);
                    if (GroundPlaneMount.TryFit(daleko, up, n.Prah, 200, 1, 10 * Math.PI / 180, n.MinInlieru / 2, out var fd))
                        k.PitchDaleko.Add(fd.Pitch);

                    // VN100 v case snimku.
                    var ypr = NejblizsiImu(rec, imu, e.CaptureTicks);
                    if (ypr != null) { k.ImuPitch.Add(ypr.Pitch); k.ImuRoll.Add(ypr.Roll); }
                }
            }
            return j;
        }

        private static YawPitchRoll NejblizsiImu(RecordFile rec, List<IndexEntry> imu, long ticks)
        {
            if (imu.Count == 0) return null;
            int lo = 0, hi = imu.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (imu[mid].CaptureTicks < ticks) lo = mid + 1; else hi = mid; }
            int best = -1; long bd = TimeSpan.FromSeconds(0.1).Ticks;
            for (int k = Math.Max(0, lo - 1); k <= Math.Min(imu.Count - 1, lo); k++)
            {
                long d = Math.Abs(imu[k].CaptureTicks - ticks);
                if (d <= bd) { bd = d; best = k; }
            }
            if (best < 0) return null;
            return rec.Read(imu[best]) is IMUState s && s.HasAbsoluteHeading ? s.YPR() : null;
        }

        private static void Vypis(Jizda j, Nastaveni n)
        {
            Console.WriteLine(string.Format(Ci, "  zacatek {0:yyyy-MM-dd HH:mm}", j.Zacatek));
            foreach (var k in j.Kamery.Values)
            {
                Console.WriteLine(string.Format(Ci,
                    "  {0}: snimku za jizdy {1}, platnych {2} (bez projekce/hloubky {3}, zamrzle {4}, rovina neprosla {5}){6}{7}",
                    k.Jmeno, k.Vybrano, k.Pitch[0].Count, k.BezProjekce, k.Zamrzle, k.Neproslo,
                    double.IsNaN(k.PpX) ? "" : string.Format(Ci, "; PP ({0:F2}; {1:F2}) f {2:F2} {3}x{4}", k.PpX, k.PpY, k.F, k.W, k.H),
                    k.Virtualni ? "  (virtualni kamera - obraz neotaci, jen varianta A)" : ""));
                if (k.Pitch[0].Count == 0) continue;
                Console.WriteLine(string.Format(Ci,
                    "    runtime (Profile ze zaznamu): sklon {0} deg  naklon {1} deg  vyska {2:F3} m  (yaw {3:F1} deg z roviny neurcitelny)",
                    D(k.Montaz.Pitch), D(k.Montaz.Roll), k.Montaz.H, Deg(k.Montaz.Yaw)));
                for (int v = 0; v < k.Variant; v++)
                    Console.WriteLine(string.Format(Ci,
                        "    {0,-13} sklon {1} [{2}; {3}]  naklon {4} [{5}; {6}]  vyska {7:F3} [{8:F3}; {9:F3}] m"
                        + "   proti runtime: sklon {10} naklon {11} deg  vyska {12} mm",
                        JmenaVariant[v],
                        D(P(k.Pitch[v], 50)), D(P(k.Pitch[v], 10)), D(P(k.Pitch[v], 90)),
                        D(P(k.Roll[v], 50)), D(P(k.Roll[v], 10)), D(P(k.Roll[v], 90)),
                        P(k.Vyska[v], 50), P(k.Vyska[v], 10), P(k.Vyska[v], 90),
                        S(Deg(P(k.Pitch[v], 50) - k.Montaz.Pitch)), S(Deg(P(k.Roll[v], 50) - k.Montaz.Roll)),
                        S(1000 * (P(k.Vyska[v], 50) - k.Montaz.H), "0")));
                Console.WriteLine(string.Format(Ci,
                    "    varianta A po pasmech: sklon blizko {0} / daleko {1} deg (rozdil {2}); inliery p50 {3:P0}, RMS p50 {4:F1} mm;"
                    + " VN100 klopeni {5} / naklon {6} deg (n {7})",
                    D(P(k.PitchBlizko, 50)), D(P(k.PitchDaleko, 50)), S(Deg(P(k.PitchDaleko, 50) - P(k.PitchBlizko, 50))),
                    P(k.Inl, 50), 1000 * P(k.Rms, 50), D(P(k.ImuPitch, 50)), D(P(k.ImuRoll, 50)), k.ImuPitch.Count));
            }
        }

        private static void Souhrn(List<Jizda> jizdy, Nastaveni n)
        {
            Console.WriteLine("=================== SOUHRN PRES JIZDY ===================");

            // Tabulka jizd podle casu - skok v montazi (napr. zmena na robotu) je videt na prvni pohled.
            var kamery = jizdy.SelectMany(x => x.Kamery.Keys).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
            Console.WriteLine(string.Format(Ci, "Jizdy podle casu (varianta A, mediany; jen kamery s alespon {0} snimky):", n.MinSnimkuJizdy));
            int sirka = Math.Max(30, kamery.Select(k => (k + " sklon/naklon/vyska").Length).DefaultIfEmpty(0).Max());
            Console.WriteLine("  zacatek           zaznam                       "
                              + string.Join("  ", kamery.Select(k => (k + " sklon/naklon/vyska").PadRight(sirka)))
                              + "  VN100 klopeni/naklon (+ = nos nahoru)");
            foreach (var j in jizdy.OrderBy(x => x.Zacatek))
            {
                var bunky = kamery.Select(jm => j.Kamery.TryGetValue(jm, out var k) && k.Pitch[0].Count >= n.MinSnimkuJizdy
                    ? string.Format(Ci, "{0,7} {1,6} {2:F3}", D(P(k.Pitch[0], 50)), D(P(k.Roll[0], 50)), P(k.Vyska[0], 50)).PadRight(sirka)
                    : "-".PadRight(sirka));
                // VN100 jen ze snimku kamer, ktere do tabulky prosly - median z par snimku by vypadal jako mereni.
                var platneKamery = j.Kamery.Values.Where(k => k.Pitch[0].Count >= n.MinSnimkuJizdy).ToList();
                var imuP = platneKamery.SelectMany(k => k.ImuPitch).ToList();
                var imuR = platneKamery.SelectMany(k => k.ImuRoll).ToList();
                Console.WriteLine(string.Format(Ci, "  {0:yyyy-MM-dd HH:mm}  {1,-28} {2}  {3}",
                    j.Zacatek, j.Zaznam, string.Join("  ", bunky),
                    imuP.Count == 0 ? "-" : D(P(imuP, 50)) + " / " + D(P(imuR, 50))));
            }
            Console.WriteLine();

            Console.WriteLine(string.Format(Ci, "Souhrn (jizdy s alespon {0} platnymi snimky dane kamery; median medianu jizd, v zavorce rozpeti medianu jizd):", n.MinSnimkuJizdy));
            var dRoll = new Dictionary<string, double>();
            var dPitch = new Dictionary<string, double>();
            foreach (var jm in kamery)
            {
                var platne = jizdy.Where(x => x.Kamery.TryGetValue(jm, out var k) && k.Pitch[0].Count >= n.MinSnimkuJizdy)
                                  .Select(x => x.Kamery[jm]).ToList();
                if (platne.Count == 0) { Console.WriteLine($"  {jm}: zadna jizda s dost snimky"); continue; }
                var ref0 = platne[0].Montaz;
                bool rozdilnaMontaz = platne.Any(k => Math.Abs(k.Montaz.Pitch - ref0.Pitch) > 1e-6
                                                      || Math.Abs(k.Montaz.Roll - ref0.Roll) > 1e-6
                                                      || Math.Abs(k.Montaz.H - ref0.H) > 1e-6);
                Console.WriteLine(string.Format(Ci,
                    "  {0}: jizd {1}, snimku {2}; runtime: sklon {3} naklon {4} deg vyska {5:F3} m{6}",
                    jm, platne.Count, platne.Sum(k => k.Pitch[0].Count), D(ref0.Pitch), D(ref0.Roll), ref0.H,
                    rozdilnaMontaz ? "  POZOR: montaz v Profile se mezi jizdami LISI - porovnani proti prvni" : ""));
                int variant = platne.Min(k => k.Variant);
                for (int v = 0; v < variant; v++)
                {
                    var mp = platne.Select(k => P(k.Pitch[v], 50)).ToList();
                    var mr = platne.Select(k => P(k.Roll[v], 50)).ToList();
                    var mh = platne.Select(k => P(k.Vyska[v], 50)).ToList();
                    var vp = platne.SelectMany(k => k.Pitch[v]).ToList();
                    if (v == 0) { dRoll[jm] = P(mr, 50) - ref0.Roll; dPitch[jm] = P(mp, 50) - ref0.Pitch; }
                    Console.WriteLine(string.Format(Ci,
                        "    {0,-13} sklon {1} ({2}..{3})  naklon {4} ({5}..{6})  vyska {7:F3} ({8:F3}..{9:F3}) m"
                        + "   proti runtime: sklon {10} naklon {11} deg vyska {12} mm;  vsechny snimky: sklon p50 {13}",
                        JmenaVariant[v], D(P(mp, 50)), D(mp.Min()), D(mp.Max()),
                        D(P(mr, 50)), D(mr.Min()), D(mr.Max()),
                        P(mh, 50), mh.Min(), mh.Max(),
                        S(Deg(P(mp, 50) - ref0.Pitch)), S(Deg(P(mr, 50) - ref0.Roll)), S(1000 * (P(mh, 50) - ref0.H), "0"),
                        D(P(vp, 50))));
                }
                var bl = platne.Select(k => P(k.PitchBlizko, 50)).Where(x => !double.IsNaN(x)).ToList();
                var da = platne.Select(k => P(k.PitchDaleko, 50)).Where(x => !double.IsNaN(x)).ToList();
                Console.WriteLine(string.Format(Ci, "    varianta A po pasmech: sklon blizko {0} / daleko {1} deg", D(P(bl, 50)), D(P(da, 50))));
                string[] popisy = platne[0].Virtualni ? new[] { "virtualni kamera, varianta A" }
                                 : variant == 3 ? new[] { "do opravy PP, varianta A", "po oprave PP spravne (W-1-PP), varianta B",
                                                          "kdyby oprava jen zprovoznila dnesni vzorec W-PP, varianta C" }
                                 : new[] { n.PpOpraveno && jm == "Left" ? "PP uz opraveny, varianta A" : "varianta A" };
                for (int v = 0; v < popisy.Length; v++)
                    Console.WriteLine(string.Format(Ci, "    pro Profile.cs ({0}): pitch {1}, roll {2} deg, vyska Z {3:F3} m",
                        popisy[v],
                        D(P(platne.Select(k => P(k.Pitch[v], 50)).ToList(), 50), "F1"),
                        D(P(platne.Select(k => P(k.Roll[v], 50)).ToList(), 50), "F1"),
                        P(platne.Select(k => P(k.Vyska[v], 50)).ToList(), 50)));
            }
            // Spolecna slozka obou kamer (yaw ±29°): pricny sklon cesty/tela φ da obema stejny posun naklonu
            // (≈ −0,875·φ) a opacny posun sklonu; klopeni tela δ da obema stejny posun sklonu (≈ δ·cos 29°)
            // a opacny posun naklonu. Montaz jedne kamery se projevi jen u ni.
            foreach (var pripona in new[] { "", " (virt)" })
            {
                if (!dRoll.TryGetValue("Left" + pripona, out var rl) || !dRoll.TryGetValue("Right" + pripona, out var rr)) continue;
                Console.WriteLine(string.Format(Ci,
                    "Spolecna slozka{0} (varianta A proti runtime): naklon (L+P)/2 {1} deg (pricny sklon ~ {2} deg), sklon (L+P)/2 {3} deg, rozdil naklonu (L-P)/2 {4} deg",
                    pripona, S(Deg((rl + rr) / 2)), S(Deg(-(rl + rr) / 2 / 0.875)),
                    S(Deg((dPitch["Left" + pripona] + dPitch["Right" + pripona]) / 2)), S(Deg((rl - rr) / 2))));
            }
        }

        private static void ZalozCsv(string cesta)
        {
            using var w = new StreamWriter(cesta, append: false);
            w.WriteLine("zaznam;zacatek;kamera;virtualni;varianta;pp_x;pp_y;snimku;sklon_p50;sklon_p10;sklon_p90;naklon_p50;naklon_p10;naklon_p90;"
                            + "vyska_p50;vyska_p10;vyska_p90;sklon_blizko;sklon_daleko;runtime_sklon;runtime_naklon;runtime_vyska;vn100_klopeni;vn100_naklon");
        }

        private static void PripisCsv(string cesta, Jizda j)
        {
            using var w = new StreamWriter(cesta, append: true);
            foreach (var k in j.Kamery.Values)
            {
                if (k.Pitch[0].Count == 0) continue;
                for (int v = 0; v < k.Variant; v++)
                {
                    double ppx = v == 0 ? k.PpX : v == 1 ? k.W - 1 - k.PpX : k.W - k.PpX;
                    double ppy = v == 0 ? k.PpY : v == 1 ? k.H - 1 - k.PpY : k.H - k.PpY;
                    w.WriteLine(string.Format(Ci,
                        "{0};{1:yyyy-MM-dd HH:mm};{2};{3};{4};{5:F2};{6:F2};{7};{8:F3};{9:F3};{10:F3};{11:F3};{12:F3};{13:F3};{14:F4};{15:F4};{16:F4};{17};{18};{19:F3};{20:F3};{21:F4};{22};{23}",
                        j.Zaznam, j.Zacatek, k.Jmeno, k.Virtualni ? 1 : 0, "ABC"[v], ppx, ppy, k.Pitch[v].Count,
                        Deg(P(k.Pitch[v], 50)), Deg(P(k.Pitch[v], 10)), Deg(P(k.Pitch[v], 90)),
                        Deg(P(k.Roll[v], 50)), Deg(P(k.Roll[v], 10)), Deg(P(k.Roll[v], 90)),
                        P(k.Vyska[v], 50), P(k.Vyska[v], 10), P(k.Vyska[v], 90),
                        v == 0 ? Deg(P(k.PitchBlizko, 50)).ToString("F3", Ci) : "",
                        v == 0 ? Deg(P(k.PitchDaleko, 50)).ToString("F3", Ci) : "",
                        Deg(k.Montaz.Pitch), Deg(k.Montaz.Roll), k.Montaz.H,
                        Deg(P(k.ImuPitch, 50)).ToString("F3", Ci), Deg(P(k.ImuRoll, 50)).ToString("F3", Ci)));
                }
            }
        }

        private static bool Jede(List<(DateTime T, double V, double W)> s, DateTime t, Nastaveni n)
        {
            int lo = 0, hi = s.Count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (s[mid].T < t) lo = mid + 1; else hi = mid; }
            int best = -1; double bd = 0.3;
            for (int k = Math.Max(0, lo - 1); k <= Math.Min(s.Count - 1, lo); k++)
            {
                double d = Math.Abs((s[k].T - t).TotalSeconds);
                if (d <= bd) { bd = d; best = k; }
            }
            return best >= 0 && Math.Abs(s[best].V) >= n.VMin && Math.Abs(s[best].W) <= n.WMax;
        }

        /// <summary>Otisk hloubky (FNV-1a nad kazdym 61. bajtem) - pozna zamrzly stream.</summary>
        private static ulong Otisk(byte[] d)
        {
            ulong hsh = 14695981039346656037UL;
            for (int i = 0; i < d.Length; i += 61) { hsh ^= d[i]; hsh *= 1099511628211UL; }
            return hsh == 0 ? 1 : hsh;
        }

        private static string KameraJmeno(string jmeno)
        {
            if (string.IsNullOrEmpty(jmeno)) return "?";
            int i = jmeno.IndexOf(' ');
            return i > 0 ? jmeno.Substring(0, i) : jmeno;
        }

        private static double P(List<double> v, double p)
        {
            if (v == null || v.Count == 0) return double.NaN;
            var s = v.Where(x => !double.IsNaN(x)).OrderBy(x => x).ToList();
            if (s.Count == 0) return double.NaN;
            double idx = p / 100.0 * (s.Count - 1);
            int lo = (int)Math.Floor(idx), hi = (int)Math.Ceiling(idx);
            return lo == hi ? s[lo] : s[lo] + (s[hi] - s[lo]) * (idx - lo);
        }

        private static double Deg(double rad) => rad * 180.0 / Math.PI;

        /// <summary>Uhel [rad] ve stupnich, bez „-0.00".</summary>
        private static string D(double rad, string fmt = "F2") => Bez0(Deg(rad), fmt);

        /// <summary>Hodnota se znamenkem (+/-), bez „-0".</summary>
        private static string S(double x, string fmt = "0.00")
        {
            string t = Bez0(x, "F" + (fmt.Contains('.') ? fmt.Length - fmt.IndexOf('.') - 1 : 0));
            return t.StartsWith("-") || t == "NaN" ? t : "+" + t;
        }

        private static string Bez0(double x, string fmt)
        {
            if (double.IsNaN(x)) return "NaN";
            string t = x.ToString(fmt, Ci);
            return t.TrimStart('-').Trim('0', '.').Length == 0 ? t.TrimStart('-') : t;
        }
    }
}
