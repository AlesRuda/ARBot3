using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Vecne nesmyslne ramce motorove jednotky</b> (prikaz <c>motory</c>, tema <c>prov-audit-druha-davka</c>):
    /// driver <c>SDC2160Ex</c> nekontroluje prefixy radku, takze by do fuze mohl pustit PLATNY ramec
    /// s nesmyslnym obsahem (hodnota jine veliciny v cizim poli). Pocita se rychlost kola nad
    /// <c>--maxv</c>, napeti mimo 9-14,6 V, proud nad <c>--maxi</c>, skok enkoderu tam a zpet
    /// (jednovzorkovy vystrelek) a ramce bez mereni; vse i za hodinu zaznamu.
    /// </summary>
    public static class MotoryReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public static void Run(RecordFile rec, double maxV, double maxI, int vypis)
        {
            var r = new List<MotorStateBase>();
            foreach (var e in rec.Index)
                if (e.MsgName == "MotorStateBase" && rec.Read(e) is MotorStateBase m) r.Add(m);
            if (r.Count == 0) { Console.WriteLine("zadny MotorStateBase"); return; }
            double hodin = Math.Max(1e-9, (r[r.Count - 1].TimeStamp - r[0].TimeStamp).TotalHours);

            long bezMereni = 0, rychlost = 0, napeti = 0, proud = 0, vystrelek = 0, davka = 0;
            int vypsano = 0;
            void Vypis(string co, MotorStateBase m)
            {
                if (vypsano++ >= vypis) return;
                Console.WriteLine(string.Format(Ci, "  {0:HH:mm:ss.fff} {1,-22} v {2:F2}/{3:F2} m/s, enc {4:F3}/{5:F3} m, U {6:F2} V, I {7:F1}/{8:F1} A",
                    m.TimeStamp, co, m.LeftWheelSpeed, m.RightWheelSpeed, m.LeftEncoder, m.RightEncoder, m.Voltage,
                    m.LeftMotorCurrent, m.RightMotorCurrent));
            }
            for (int i = 0; i < r.Count; i++)
            {
                var m = r[i];
                if (!m.HasMeasurement) { bezMereni++; continue; }
                if (i > 0 && (m.TimeStamp - r[i - 1].TimeStamp).TotalMilliseconds < 1) davka++;
                if (Math.Abs(m.LeftWheelSpeed) > maxV || Math.Abs(m.RightWheelSpeed) > maxV)
                {
                    rychlost++;
                    if (vypsano < vypis && i > 0)
                    {
                        var p = r[i - 1];
                        Console.WriteLine(string.Format(Ci, "  {0:HH:mm:ss.fff} rychlost kola: dt zaznamu {1:F1} ms, dt zarizeni {2} ms, d enc {3:F4}/{4:F4} m -> {5:F2}/{6:F2} m/s (z enkoderu a casu zarizeni {7:F2}/{8:F2})",
                            m.TimeStamp, (m.TimeStamp - p.TimeStamp).TotalMilliseconds,
                            m.HasDeviceTime && p.HasDeviceTime ? (m.DeviceTimeMs - p.DeviceTimeMs).ToString(Ci) : "?",
                            m.LeftEncoder - p.LeftEncoder, m.RightEncoder - p.RightEncoder, m.LeftWheelSpeed, m.RightWheelSpeed,
                            m.HasDeviceTime && p.HasDeviceTime && m.DeviceTimeMs != p.DeviceTimeMs ? (m.LeftEncoder - p.LeftEncoder) * 1000.0 / (m.DeviceTimeMs - p.DeviceTimeMs) : double.NaN,
                            m.HasDeviceTime && p.HasDeviceTime && m.DeviceTimeMs != p.DeviceTimeMs ? (m.RightEncoder - p.RightEncoder) * 1000.0 / (m.DeviceTimeMs - p.DeviceTimeMs) : double.NaN));
                        vypsano++;
                    }
                }
                if (m.Voltage < 9 || m.Voltage > 14.6) { napeti++; Vypis("napeti", m); }
                if (Math.Abs(m.LeftMotorCurrent) > maxI || Math.Abs(m.RightMotorCurrent) > maxI) { proud++; Vypis("proud", m); }
                // Vystrelek enkoderu: skok o vic nez 0,2 m a hned zpet (soucet obou kroku pod desetinou skoku).
                if (i > 0 && i + 1 < r.Count && r[i - 1].HasMeasurement && r[i + 1].HasMeasurement)
                {
                    foreach (var (a, b, c) in new[] { (r[i - 1].LeftEncoder, m.LeftEncoder, r[i + 1].LeftEncoder),
                                                      (r[i - 1].RightEncoder, m.RightEncoder, r[i + 1].RightEncoder) })
                    {
                        double d1 = b - a, d2 = c - b;
                        if (Math.Abs(d1) > 0.2 && Math.Abs(d1 + d2) < 0.1 * Math.Abs(d1)) { vystrelek++; Vypis("vystrelek enkoderu", m); break; }
                    }
                }
            }
            Console.WriteLine(string.Format(Ci,
                "MotorStateBase {0} ({1:HH:mm:ss} - {2:HH:mm:ss}, {3:F2} h): bez mereni {4}; nesmyslne: rychlost kola > {5} m/s {6}, napeti mimo 9-14,6 V {7}, proud > {8} A {9}, vystrelek enkoderu {10} (za hodinu {11:F1}); ramcu < 1 ms po predchozim {12}",
                r.Count, r[0].TimeStamp, r[r.Count - 1].TimeStamp, hodin, bezMereni, maxV, rychlost, napeti, maxI, proud,
                vystrelek, (rychlost + napeti + proud + vystrelek) / hodin, davka));
            var u = r.Where(m => m.HasMeasurement).Select(m => m.Voltage).OrderBy(x => x).ToList();
            var v = r.Where(m => m.HasMeasurement).Select(m => Math.Max(Math.Abs(m.LeftWheelSpeed), Math.Abs(m.RightWheelSpeed))).OrderBy(x => x).ToList();
            if (u.Count > 0)
                Console.WriteLine(string.Format(Ci, "  napeti min/p50/max {0:F2}/{1:F2}/{2:F2} V; rychlost kola p99/max {3:F2}/{4:F2} m/s",
                    u[0], u[u.Count / 2], u[u.Count - 1], v[v.Count * 99 / 100], v[v.Count - 1]));
        }
    }
}
