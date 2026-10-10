using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ARBot.Common.Devices;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Kurz vozidla z GPS ve zaznamu</b> (prikaz <c>gpskurz</c>, tema <c>prov-audit-druha-davka</c>):
    /// kolik <see cref="GPSState"/> nese <c>Orientation</c> (kurz VOZIDLA, u-blox <c>headVeh</c>).
    ///
    /// <para><b>Proc:</b> <c>PVTMessage.HeadVeh</c> cte offset 64 - tentyz jako <c>HeadMot</c> (kurz
    /// POHYBU) - misto 84. <c>uBloxGps</c> plni <c>Orientation</c>, kdykoli prijimac nastavi
    /// <c>headVehValid</c>, a <c>DefaultMeasurementMapper</c> mu dava prednost s konstantni sigmou,
    /// takze by obesel prah rychlosti i vylouceni jizdy vzad. Prijimac bez fuzniho rezimu (ADR/UDR)
    /// ten priznak nenastavi nikdy - pak je vada spici. Tenhle prikaz rozhodne, ktery pripad nastal.</para>
    /// </summary>
    public static class GpsKurzReport
    {
        private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

        public static void Run(RecordFile rec)
        {
            long n = 0, sOrient = 0, sDyn = 0;
            var rozdily = new List<double>();
            DateTime prvni = DateTime.MinValue, posledni = DateTime.MinValue;
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "GPSState" || !(rec.Read(e) is GPSState g)) continue;
                n++;
                if (prvni == DateTime.MinValue) prvni = g.TimeStamp;
                posledni = g.TimeStamp;
                if (g.DynamicOrientation.HasValue) sDyn++;
                if (g.Orientation.HasValue)
                {
                    sOrient++;
                    if (g.DynamicOrientation.HasValue)
                    {
                        double d = g.Orientation.Value - g.DynamicOrientation.Value;
                        d = Math.Atan2(Math.Sin(d), Math.Cos(d)) * 180 / Math.PI;
                        rozdily.Add(d);
                    }
                }
            }
            Console.WriteLine(string.Format(Ci, "GPSState {0} ({1:HH:mm:ss} - {2:HH:mm:ss}): s kurzem VOZIDLA (Orientation) {3}, s kurzem pohybu (DynamicOrientation) {4}",
                n, prvni, posledni, sOrient, sDyn));
            if (sOrient == 0)
                Console.WriteLine("  kurz vozidla neprisel ani jednou - prijimac headVehValid nenastavuje, vada offsetu je SPICI");
            else
            {
                rozdily.Sort();
                Console.WriteLine(string.Format(Ci, "  Orientation - DynamicOrientation: p10 {0:F1}°, p50 {1:F1}°, p90 {2:F1}° ({3} vzorku) - kurz vozidla jde do fuze PRED kurzem pohybu",
                    rozdily.Count > 0 ? rozdily[rozdily.Count / 10] : double.NaN,
                    rozdily.Count > 0 ? rozdily[rozdily.Count / 2] : double.NaN,
                    rozdily.Count > 0 ? rozdily[rozdily.Count * 9 / 10] : double.NaN, rozdily.Count));
            }
        }
    }
}
