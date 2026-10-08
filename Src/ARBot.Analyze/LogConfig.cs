using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ARBot.Common.Logs;

namespace ARBot.Analyze
{
    /// <summary>
    /// <b>Ucinna konfigurace ze zaznamu</b> - blok „Konfigurace (ucinne hodnoty; ...)", ktery runtime
    /// po pripojeni <c>TraceInfoBridge</c> vypise do <see cref="Info"/> (radek
    /// <c>"  klic=hodnota  (puvod)"</c>, viz doc/record-replay.md).
    ///
    /// <para><b>Proc to je.</b> Meridlo, ktere pocita s vychozi hodnotou z kodu, meri jiny robot nez
    /// ten, ktery jel: <c>wedge</c> pocital VBrake s <c>MaxSpeed</c> 1,2 m/s, ackoli robot 1. 10. 2026
    /// jel s <c>maxspeed=1.7</c>, a <c>corridor</c> psal „dnes 25" u prahu inlieru, ktery je od
    /// 30. 9. 10 % radku. Hodnoty se proto ctou ze zaznamu a default z kodu je jen zaloha pro
    /// zaznamy, ktere vypis jeste nemaji (pred 5. 9. 2026).</para>
    ///
    /// <para>Stejne parsuje <c>FusionReplayReport.ReadConfig</c>; starsi reporty
    /// (<c>envelope</c>, <c>drive</c>, <c>corridorstd</c>) maji vlastni jednodussi variantu a zatim
    /// zustavaji - prepis by menil jejich vystup bez mereni.</para>
    /// </summary>
    public sealed class LogConfig
    {
        private static readonly Regex Radek = new Regex(
            @"^\s+([A-Za-z_0-9]+)=(.*?)\s+\((default|profil|prikazova radka|zvoleno za behu)\)\s*$");

        private readonly Dictionary<string, (string Value, string Origin)> hodnoty
            = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Radek s verzi binarky (<c>ARBot verze: ...</c>), nebo null.</summary>
        public string Version { get; private set; }

        /// <summary>Nese zaznam vypis konfigurace vubec?</summary>
        public bool Any => hodnoty.Count > 0;

        /// <summary>
        /// Precte konfiguraci ze vsech <see cref="Info"/> v zaznamu. Plati PRVNI vyskyt klice (vypis
        /// po startu); pozdejsi „zvoleno za behu" je zmena za jizdy a tu si rozbor musi resit sam.
        /// </summary>
        public static LogConfig Read(RecordFile rec)
        {
            var c = new LogConfig();
            foreach (var e in rec.Index)
            {
                if (e.MsgName != "Info") continue;
                if (!(rec.Read(e) is Info info) || info.Message == null) continue;
                foreach (string radek in info.Message.Split('\n'))
                {
                    string t = radek.TrimEnd('\r');
                    if (c.Version == null && t.StartsWith("ARBot verze:", StringComparison.Ordinal)) c.Version = t;
                    var m = Radek.Match(t);
                    if (m.Success && !c.hodnoty.ContainsKey(m.Groups[1].Value))
                        c.hodnoty[m.Groups[1].Value] = (m.Groups[2].Value.Trim(), m.Groups[3].Value);
                }
            }
            return c;
        }

        /// <summary>Textova hodnota klice; <c>null</c>, kdyz v zaznamu neni (nebo je „(nenastaveno)").</summary>
        public string Text(string key)
            => hodnoty.TryGetValue(key, out var v) && v.Value != "(nenastaveno)" ? v.Value : null;

        /// <summary>Ciselna hodnota klice; <c>null</c>, kdyz v zaznamu neni nebo neni cislo.</summary>
        public double? Num(string key)
            => double.TryParse(Text(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : (double?)null;

        /// <summary>
        /// Hodnota pro rozbor: prepis z prikazove radky meridla (neni-li NaN), jinak ze zaznamu,
        /// jinak <paramref name="fallback"/>. <paramref name="puvod"/> rika, odkud je - patri do
        /// vypisu, aby bylo videt, s cim se pocitalo.
        /// </summary>
        public double Resolve(string key, double prepis, double fallback, out string puvod)
        {
            if (!double.IsNaN(prepis)) { puvod = "--" + key + "= (prepis)"; return prepis; }
            double? z = Num(key);
            if (z.HasValue)
            {
                puvod = "ze zaznamu (" + hodnoty[key].Origin + ")";
                return z.Value;
            }
            puvod = Any ? "v zaznamu neni, default z kodu" : "zaznam nema vypis konfigurace, default z kodu";
            return fallback;
        }
    }
}
