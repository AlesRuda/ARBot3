using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;
using ARBot.Common.Diagnostics;
using ARBot.Common.Logs;
using ARBot.Common.Models;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Prokluz kol ze zaznamu</b> (prikaz <c>prokluz</c>, tema <c>lok-fuze-rucni-otoceni</c>):
    /// prehraje <see cref="WheelSlipDetector"/> — TYTEZ kod jako runtime — nad <c>IMUState</c>
    /// a <c>MotorStateBase</c> ze zaznamu a vypise epizody, souhrn po druzich a radky do Trace.
    /// S <c>--sweep</c> porovna varianty prahu (vzdy jeden menen kolem vychozich), aby sly prahy
    /// nastavit z dat. Ma-li zaznam <see cref="WheelSlipMsg"/> (z robotu), vypise se pro srovnani.
    /// </summary>
    public static class ProkluzReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;
        private const double Deg = 180.0 / Math.PI;

        private sealed class Vysledek
        {
            public string Jmeno;
            public List<WheelSlipMsg> Konce = new List<WheelSlipMsg>();
            public List<string> Radky = new List<string>();
        }

        public static void Run(RecordFile rec, double abs, double rel, double minOn, double okno, double minUhelDeg, bool sweep, int vypis)
        {
            var msgs = new List<Message>();
            var zRobotu = new List<WheelSlipMsg>();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "IMUState" && e.MsgName != "MotorStateBase" && e.MsgName != "WheelSlipMsg") continue;
                var m = rec.Read(e);
                if (m is WheelSlipMsg w) zRobotu.Add(w);
                else if (m != null) msgs.Add(m);
            }

            // Doba v pohybu (|v| z kol > 0,05 m/s) - jmenovatel "epizod za hodinu jizdy".
            double pohyb = 0, celkem = 0;
            MotorStateBase prev = null;
            int gyroN = 0, odoN = 0;
            foreach (var m in msgs)
            {
                if (m is IMUState imu && imu.HasAbsoluteHeading && imu.AngularVelocity.HasValue) gyroN++;
                if (m is MotorStateBase mo && mo.HasMeasurement)
                {
                    odoN++;
                    if (prev != null)
                    {
                        double dt = (mo.TimeStamp - prev.TimeStamp).TotalSeconds;
                        if (dt > 0 && dt < 1)
                        {
                            celkem += dt;
                            if (Math.Abs(0.5 * (prev.LeftWheelSpeed + prev.RightWheelSpeed)) > 0.05) pohyb += dt;
                        }
                    }
                    prev = mo;
                }
            }
            Console.WriteLine(string.Format(Ci, "vzorku gyra {0}, kol {1}; doba s daty kol {2:F1} min, z toho v pohybu {3:F1} min",
                gyroN, odoN, celkem / 60, pohyb / 60));

            var zakl = new WheelSlipConfig
            {
                AbsThresholdRadS = abs, RelThreshold = rel, MinOnSec = minOn, WindowSec = okno,
                MinAngleRad = minUhelDeg * Math.PI / 180,
            };
            var v0 = Prehraj("vychozi", zakl, msgs, out long mimo);
            Console.WriteLine(string.Format(Ci,
                "prahy: okno {0} s, abs {1} rad/s ({2:F1} deg/s), rel {3}, min. trvani {4} s, min. rozdil {5:F0} deg, konec po {6} s pod polovinou; vzorku s casem pozadu {7}",
                zakl.WindowSec, zakl.AbsThresholdRadS, zakl.AbsThresholdRadS * Deg, zakl.RelThreshold, zakl.MinOnSec,
                zakl.MinAngleRad * Deg, zakl.MinOffSec, mimo));
            Console.WriteLine();

            Console.WriteLine($"1) EPIZODY ({v0.Konce.Count}; vypis nejvetsich {vypis} podle |rozdilu|, serazeno casem)");
            Console.WriteLine("   zacatek        delka  druh          v[m/s]  Skola  Sgyro  rozdil [deg]");
            foreach (var k in v0.Konce.OrderByDescending(k => Math.Abs(k.OdoAngle - k.GyroAngle)).Take(vypis).OrderBy(k => k.TimeStamp))
                Console.WriteLine(string.Format(Ci, "   {0:HH:mm:ss.f}  {1,6:F1}  {2,-12}  {3,6:F2}  {4,5:F0}  {5,5:F0}  {6,6:F0}",
                    k.EpisodeStart, k.EpisodeSec, (WheelSlipKind)k.Kind, k.Speed,
                    k.OdoAngle * Deg, k.GyroAngle * Deg, (k.OdoAngle - k.GyroAngle) * Deg));
            Console.WriteLine();
            Souhrn("2) SOUHRN", v0, pohyb);
            Console.WriteLine($"   radku do Trace (po skrceni): {v0.Radky.Count}");
            foreach (var r in v0.Radky.Take(8)) Console.WriteLine("     " + r);
            if (v0.Radky.Count > 8) Console.WriteLine("     ...");

            if (zRobotu.Count > 0)
            {
                var konce = zRobotu.Where(z => z.Level == 0).ToList();
                Console.WriteLine();
                Console.WriteLine($"WheelSlipMsg ZE ZAZNAMU (robot): {zRobotu.Count} zprav, {konce.Count} skoncenych epizod");
            }

            if (sweep)
            {
                Console.WriteLine();
                Console.WriteLine("3) VARIANTY PRAHU (vzdy jeden zmeneny proti vychozim)");
                Console.WriteLine("   varianta                  epizod  /h jizdy  stoji proti  vic  smyk   sum|rozdil| max|rozdil|  [deg]");
                var varianty = new List<(string N, WheelSlipConfig C)> { ("vychozi", zakl) };
                foreach (double a in new[] { 0.1, 0.2, 0.3 }) varianty.Add(($"abs {a}", Kopie(zakl, c => c.AbsThresholdRadS = a)));
                foreach (double r in new[] { 0.15, 0.4 }) varianty.Add(($"rel {r}", Kopie(zakl, c => c.RelThreshold = r)));
                foreach (double o in new[] { 0.1, 0.5, 1.0 }) varianty.Add(($"min. trvani {o}", Kopie(zakl, c => c.MinOnSec = o)));
                foreach (double w in new[] { 0.3, 1.0 }) varianty.Add(($"okno {w}", Kopie(zakl, c => c.WindowSec = w)));
                foreach (double u in new[] { 0.0, 5.0, 20.0 }) varianty.Add(($"min. uhel {u}", Kopie(zakl, c => c.MinAngleRad = u * Math.PI / 180)));
                foreach (var (n, c) in varianty)
                {
                    var v = n == "vychozi" ? v0 : Prehraj(n, c, msgs, out _);
                    var p = Pocty(v);
                    Console.WriteLine(string.Format(Ci, "   {0,-24} {1,7}  {2,8:F1}  {3,5} {4,5} {5,4} {6,5}   {7,11:F0} {8,11:F0}",
                        n, v.Konce.Count, pohyb > 0 ? v.Konce.Count / (pohyb / 3600) : double.NaN,
                        p[WheelSlipKind.WheelsStill], p[WheelSlipKind.Opposite], p[WheelSlipKind.WheelsMore], p[WheelSlipKind.BodyMore],
                        v.Konce.Sum(k => Math.Abs(k.OdoAngle - k.GyroAngle)) * Deg,
                        v.Konce.Count > 0 ? v.Konce.Max(k => Math.Abs(k.OdoAngle - k.GyroAngle)) * Deg : 0));
                }
            }
            // Strojove citelny radek pro souhrn pres vic zaznamu.
            var pz = Pocty(v0);
            Console.WriteLine(string.Format(Ci, "SOUHRN;{0};{1:F3};{2};{3};{4};{5};{6};{7:F1};{8:F1}",
                System.IO.Path.GetFileName(rec.Path), pohyb / 3600, v0.Konce.Count,
                pz[WheelSlipKind.WheelsStill], pz[WheelSlipKind.Opposite], pz[WheelSlipKind.WheelsMore], pz[WheelSlipKind.BodyMore],
                v0.Konce.Sum(k => Math.Abs(k.OdoAngle - k.GyroAngle)) * Deg,
                v0.Konce.Count > 0 ? v0.Konce.Max(k => Math.Abs(k.OdoAngle - k.GyroAngle)) * Deg : 0));
        }

        private static WheelSlipConfig Kopie(WheelSlipConfig z, Action<WheelSlipConfig> zmena)
        {
            var c = new WheelSlipConfig
            {
                WindowSec = z.WindowSec, AbsThresholdRadS = z.AbsThresholdRadS, RelThreshold = z.RelThreshold,
                MinOnSec = z.MinOnSec, MinOffSec = z.MinOffSec, MinAngleRad = z.MinAngleRad, MaxGapSec = z.MaxGapSec,
                EvalPeriodSec = z.EvalPeriodSec, DataTimeoutSec = z.DataTimeoutSec,
                WheelBase = z.WheelBase, OdoOmegaSign = z.OdoOmegaSign,
                MessagePeriodSec = z.MessagePeriodSec, TracePeriodSec = z.TracePeriodSec, RecentSec = z.RecentSec,
            };
            zmena(c);
            return c;
        }

        private static Vysledek Prehraj(string jmeno, WheelSlipConfig cfg, List<Message> msgs, out long mimo)
        {
            var v = new Vysledek { Jmeno = jmeno };
            var det = new WheelSlipDetector(cfg);
            foreach (var m in msgs)
            {
                var o = det.Feed(m, v.Radky);
                if (o != null && o.Level == (byte)WheelSlipLevel.None) v.Konce.Add(o);
            }
            mimo = det.OutOfOrder;
            return v;
        }

        private static Dictionary<WheelSlipKind, int> Pocty(Vysledek v)
        {
            var p = Enum.GetValues(typeof(WheelSlipKind)).Cast<WheelSlipKind>().ToDictionary(k => k, k => 0);
            foreach (var k in v.Konce) p[(WheelSlipKind)k.Kind]++;
            return p;
        }

        private static void Souhrn(string nadpis, Vysledek v, double pohybSec)
        {
            var p = Pocty(v);
            Console.WriteLine(string.Format(Ci, "{0}: {1} epizod ({2:F1} za hodinu jizdy); kola stoji {3}, proti {4}, kola vic {5}, smyk {6}",
                nadpis, v.Konce.Count, pohybSec > 0 ? v.Konce.Count / (pohybSec / 3600) : double.NaN,
                p[WheelSlipKind.WheelsStill], p[WheelSlipKind.Opposite], p[WheelSlipKind.WheelsMore], p[WheelSlipKind.BodyMore]));
            if (v.Konce.Count == 0) return;
            var d = v.Konce.Select(k => Math.Abs(k.OdoAngle - k.GyroAngle) * Deg).OrderBy(x => x).ToList();
            var t = v.Konce.Select(k => k.EpisodeSec).OrderBy(x => x).ToList();
            Console.WriteLine(string.Format(Ci, "   |rozdil| p50 {0:F1} deg, p90 {1:F1} deg, max {2:F1} deg, soucet {3:F0} deg; delka p50 {4:F1} s, max {5:F1} s",
                d[d.Count / 2], d[(int)(0.9 * (d.Count - 1))], d[d.Count - 1], d.Sum(), t[t.Count / 2], t[t.Count - 1]));
        }
    }
}
