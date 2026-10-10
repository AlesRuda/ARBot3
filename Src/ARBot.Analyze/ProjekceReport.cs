using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using ARBot.Common.Coordinates;
using ARBot.Common.Devices;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Popis projekce kamer ze zaznamu</b> (prikaz <c>projekce</c>, tema <c>vid-zpetna-projekce-hloubka</c>):
    /// intrinsika hloubky a barvy, extrinsiky barva &lt;-&gt; hloubka a orientace kamery v ramci robotu,
    /// jak je driver ulozil do <see cref="CameraFrame.Projection"/>, a jejich stalost v zaznamu.
    ///
    /// <para><b>Proc:</b> leva D435 ma <c>Swap = true</c> - driver obraci poradi pixelu (otoceni o 180°),
    /// takze hlavni bod se musi prevratit spolu s nim na <c>(W−1−PPx, H−1−PPy)</c>. <c>CreateProjector</c>
    /// ho prevraci v intrinsice i v jeji inverzi, jenze <c>Intrinsics.Inverse()</c> vraci pro model bez
    /// zkresleni TENTYZ objekt, takze se prevraceni provede dvakrat a zrusi. Kolik to dela, zalezi na tom,
    /// jak daleko od stredu obrazu hlavni bod lezi: vypisuje se odsazeni a uhel, o ktery jsou paprsky
    /// leve kamery pootocene (vodorovne ~ kurz, svisle ~ sklon), proti pravidlu pixelovych stredu
    /// i proti konvenci <c>W − PPx</c> pouzite v kodu (lisi se o 1 px).</para>
    /// </summary>
    public static class ProjekceReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private const double Deg = 180.0 / Math.PI;

        public static void Run(RecordFile rec, int vzorku)
        {
            var snimky = rec.Index.Where(e => e.MsgName == "CameraFrame").ToList();
            if (vzorku < 1) vzorku = 1;
            var podleKamery = new Dictionary<string, List<(DateTime t, CameraProjectionInfo p)>>();
            var prvni = new Dictionary<string, CameraProjectionInfo>();
            var nalezene = new HashSet<string>();
            // Projdi rovnomerne rozlozene snimky (kazda kamera aspon jednou) - kvuli stalosti v zaznamu.
            int krok = Math.Max(1, snimky.Count / (vzorku * 2));
            for (int i = 0; i < snimky.Count; i += krok)
            {
                if (!(rec.Read(snimky[i]) is CameraFrame f) || f.Projection == null || f.Projection.Intrinsics == null) continue;
                string jm = (f.Name ?? "").Split(' ')[0];
                if (!podleKamery.TryGetValue(jm, out var l)) podleKamery[jm] = l = new List<(DateTime, CameraProjectionInfo)>();
                l.Add((f.TimeStamp, f.Projection));
                if (!prvni.ContainsKey(jm)) prvni[jm] = f.Projection;
            }
            if (podleKamery.Count == 0) { Console.WriteLine("zadny snimek s popisem projekce (CameraFrame < v4?)"); return; }

            foreach (var kv in podleKamery.OrderBy(k => k.Key))
            {
                var p = prvni[kv.Key];
                Console.WriteLine();
                Console.WriteLine($"=== {kv.Key} (vzorku {kv.Value.Count}, {kv.Value.First().t:HH:mm:ss} - {kv.Value.Last().t:HH:mm:ss}) ===");
                Intr("hloubka", p.Intrinsics);
                if (p.InverseIntrinsics != null && !Shodne(p.Intrinsics, p.InverseIntrinsics)) Intr("hloubka inverze (LISI SE)", p.InverseIntrinsics);
                if (p.ColorIntrinsics != null) Intr("barva", p.ColorIntrinsics);
                Mat("barva -> hloubka", p.ColorToDepth);
                Orientace(p.Transformation);

                // Stalost: lisi se nektery vzorek od prvniho?
                int ruznych = kv.Value.Count(v => !Shodne(v.p.Intrinsics, p.Intrinsics)
                                                  || (p.ColorIntrinsics != null && !Shodne(v.p.ColorIntrinsics, p.ColorIntrinsics))
                                                  || v.p.Transformation != p.Transformation);
                Console.WriteLine(ruznych == 0 ? "  stalost: vsechny vzorky stejne" : $"  stalost: {ruznych} vzorku se LISI od prvniho");
            }
        }

        private static bool Shodne(Intrinsics a, Intrinsics b)
            => b != null && a.Width == b.Width && a.Height == b.Height && a.PPx == b.PPx && a.PPy == b.PPy
               && a.Fx == b.Fx && a.Fy == b.Fy && a.Model == b.Model;

        private static void Intr(string co, Intrinsics i)
        {
            bool nulove = i.Coeffs == null || i.Coeffs.All(c => c == 0);
            // Odsazeni hlavniho bodu od stredu obrazu (pixelove stredy na celych cislech: stred = (W-1)/2).
            double ox = i.PPx - (i.Width - 1) / 2.0, oy = i.PPy - (i.Height - 1) / 2.0;
            // Nepreklopeny hlavni bod u obrazu otoceneho o 180°: paprsky pootocene o 2*odsazeni/f.
            double ex = 2 * ox / i.Fx * Deg, ey = 2 * oy / i.Fy * Deg;
            // Totez proti konvenci kodu (W - PPx), kterou by spravne prevraceni dalo.
            double ex2 = (2 * i.PPx - i.Width) / i.Fx * Deg, ey2 = (2 * i.PPy - i.Height) / i.Fy * Deg;
            Console.WriteLine(string.Format(Ci,
                "  {0,-24} {1}x{2}  PP ({3:F2}, {4:F2})  f ({5:F2}, {6:F2})  model {7}{8}",
                co, i.Width, i.Height, i.PPx, i.PPy, i.Fx, i.Fy, i.Model, nulove ? " (koeficienty 0)" : ""));
            Console.WriteLine(string.Format(Ci,
                "  {0,-24} odsazeni PP od stredu ({1:+0.00;-0.00}, {2:+0.00;-0.00}) px -> neprevraceny PP pootoci paprsky o {3:+0.00;-0.00}° vodorovne, {4:+0.00;-0.00}° svisle (proti W−PPx: {5:+0.00;-0.00}° / {6:+0.00;-0.00}°)",
                "", ox, oy, ex, ey, ex2, ey2));
        }

        private static void Mat(string co, Matrix4x4 m)
        {
            var t = m.Translation;
            // Odchylka rotace od jednotkove: uhel z traci.
            double tr = m.M11 + m.M22 + m.M33;
            double uhel = Math.Acos(Math.Max(-1, Math.Min(1, (tr - 1) / 2))) * Deg;
            Console.WriteLine(string.Format(Ci, "  {0,-24} posun ({1:+0.0;-0.0}, {2:+0.0;-0.0}, {3:+0.0;-0.0}) mm, rotace {4:F2}°",
                co, t.X * 1000, t.Y * 1000, t.Z * 1000, uhel));
        }

        /// <summary>Orientace kamery v ramci robotu (radky = obrazy bazi kamery: X vpravo, Y dolu, Z vpred).</summary>
        private static void Orientace(Matrix4x4 m)
        {
            var vpred = new Vector3(m.M31, m.M32, m.M33);
            var vpravo = new Vector3(m.M11, m.M12, m.M13);
            double kurz = Math.Atan2(vpred.Y, vpred.X) * Deg;
            double sklon = Math.Asin(Math.Max(-1, Math.Min(1, vpred.Z))) * Deg;
            double naklon = Math.Asin(Math.Max(-1, Math.Min(1, vpravo.Z))) * Deg;
            var t = m.Translation;
            Console.WriteLine(string.Format(Ci, "  {0,-24} kurz {1:+0.00;-0.00}°, sklon {2:+0.00;-0.00}°, naklon {3:+0.00;-0.00}°, poloha ({4:F3}, {5:F3}, {6:F3}) m",
                "kamera v ramci robotu", kurz, sklon, naklon, t.X, t.Y, t.Z));
        }
    }
}
