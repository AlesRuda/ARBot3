using System;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.Common.Missions
{
    /// <summary>
    /// Parser cile ve formatu <c>geo:</c>. Viz doc/robotour-mission.md.
    ///
    /// <para>Format je prevzaty 1:1 z predchozi generace robotu (ARBot2, <c>ReadQRLLA</c>), ktera ho
    /// v soutezi pouzivala a osvedcil se:</para>
    /// <code>
    /// geo:49.2103,16.5991           →  N 49,2103°  E 16,5991°
    /// geo: 49.2103 N, 16.5991 E     →  totez (mezery i sufixy jsou pripustne)
    /// geo:12.34S,45.67W             →  zaporna sirka i delka
    /// </code>
    ///
    /// <para><b>Cisla se parsuji vzdy <see cref="CultureInfo.InvariantCulture"/></b> — desetinna
    /// tecka bez ohledu na locale stroje. Je to jedina chyba, kterou lze v tomhle retezu udelat
    /// TICHE a FATALNE: pod ceskym locale by <c>double.Parse("49.2103")</c> dalo 492103 (platne
    /// cislo!) a robot by odjel do vesmiru.</para>
    /// </summary>
    public sealed class GeoUriTargetParser : IMissionTargetParser
    {
        private const string Prefix = "geo:";

        /// <inheritdoc/>
        public LLA Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;

            // Case-insensitive: kod muze byt vysazeny jakkoli.
            string body = text.Trim();
            if (!body.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return null;
            body = body.Substring(Prefix.Length);

            // Presne dve slozky. Tri (napr. s vyskou) se zamitaji zamerne: format souteze je
            // dvouslozkovy a "necekane navic" znamena, ze kod nerozumim.
            string[] parts = body.Split(',');
            if (parts.Length != 2) return null;

            if (!TryParseComponent(parts[0], 'n', 's', out double latDeg)) return null;
            if (!TryParseComponent(parts[1], 'e', 'w', out double lonDeg)) return null;

            // Rozsah Zeme. Bez teto kontroly by preklep (chybejici desetinna tecka) prosel jako
            // platny cil — a stroj ma zamitat vsechno, co zamitnout umi, protoze druha pojistka
            // (potvrzeni obsluhou) uz je clovek.
            if (latDeg < -90 || latDeg > 90) return null;
            if (lonDeg < -180 || lonDeg > 180) return null;

            return new LLA(Conversions.Deg2Rad(latDeg), Conversions.Deg2Rad(lonDeg));
        }

        /// <summary>
        /// Jedna slozka: cislo s volitelnym sufixem svetove strany. <paramref name="positive"/> a
        /// <paramref name="negative"/> jsou pismena pro dany smer (n/s pro sirku, e/w pro delku).
        ///
        /// <para>Bez sufixu se bere hodnota, jak je — tedy i s minusem.</para>
        /// </summary>
        private static bool TryParseComponent(string raw, char positive, char negative, out double deg)
        {
            deg = 0;
            if (raw == null) return false;

            string s = raw.Trim().ToLowerInvariant();
            if (s.Length == 0) return false;

            int sign = 1;
            char last = s[s.Length - 1];
            if (last == positive || last == negative)
            {
                if (last == negative) sign = -1;
                s = s.Substring(0, s.Length - 1).Trim();
            }

            if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return false;

            deg = sign * value;
            return true;
        }
    }
}
