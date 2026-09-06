using System;
using System.Globalization;
using ARBot.Common.Common;
using ARBot.Common.Coordinates;

namespace ARBot.HAL.Devices.AHRS
{
    /// <summary>
    /// <b>Sestavování příkazů pro VectorNav VN-100</b> — jedno místo pro obě verze driveru
    /// (ASCII <see cref="VN100IMU"/> i binární <see cref="VN100IMUBinary"/>).
    ///
    /// <para><b>Nač to je.</b> Oba drivery si dosud rámovaly příkazy samy a měly vlastní kopii
    /// kontrolního součtu. Když pak vznikne příkaz jen v jednom z nich, druhý ho nemá — přesně
    /// tak dopadl <c>SetModelParams</c>, který existoval jen v ASCII verzi (a k tomu nefungoval,
    /// viz níž), zatímco na robotu běží ta binární. Sestavení příkazu je proto tady a jde
    /// <b>otestovat bez hardwaru</b>: výsledkem je řetězec, který se dá porovnat znak po znaku.</para>
    ///
    /// <para><b>Dvě chyby, které se tím opravily</b> (nalezené 6. 9. 2026):</para>
    /// <list type="bullet">
    /// <item><b><c>VNRRG</c> místo <c>VNWRG</c>.</b> Původní <c>SetModelParams</c> posílal
    ///   <c>$VNRRG,83,…</c> — což je <b>čtecí</b> příkaz. Nic nenastavil a nikdy nemohl;
    ///   registr 83 byl na robotu pořád v továrním stavu (model pole vypnutý).</item>
    /// <item><b>Formát <c>N</c> místo <c>F</c>.</b> <c>{0:N3}</c> vkládá <b>oddělovač tisíců</b>,
    ///   takže nadmořská výška 1234,5 m se zapsala jako <c>1,234.500</c> — čárka doprostřed
    ///   čárkami odděleného příkazu. Na malé výšce to nebylo vidět, na tisícovce by to rozbilo
    ///   celý rámec. Proto se všude používá <c>F</c> a <see cref="CultureInfo.InvariantCulture"/>.</item>
    /// </list>
    ///
    /// <para><b>Třetí past, kterou tenhle kód řeší: jednotky.</b> <see cref="LLA"/> drží
    /// zeměpisné souřadnice v <b>radiánech</b> (pravidlo projektu, viz CLAUDE.md), VN je čeká
    /// ve <b>stupních</b>. Původní kód posílal radiány, takže i po opravě příkazu by senzor
    /// dostal polohu někde u rovníku.</para>
    /// </summary>
    public static class VnCommands
    {
        /// <summary>Registr 83 — Reference Vector Configuration (model pole a gravitace).</summary>
        public const int RegReferenceVectorConfig = 83;

        /// <summary>
        /// Tělo příkazu pro <b>registr 83 (Reference Vector Configuration)</b> — zapne v senzoru
        /// model magnetického a gravitačního pole pro dané místo a datum.
        ///
        /// <para><b>K čemu to je.</b> Bez modelu drží VN referenční pole natvrdo v registru 21
        /// a na robotu tam je <c>(0,234; 0; 0,4212)</c> — <b>východní složka nula</b>, tedy
        /// <b>bez deklinace</b>. Hlášený kurz je pak azimut k <b>magnetickému</b> severu, ne
        /// k pravému. Se zapnutým modelem si VN referenční vektor (a s ním deklinaci) dopočítá
        /// sám z polohy a data. Viz doc/imu-and-frames.md.</para>
        ///
        /// <para><b>Pořadí polí</b> (ověřeno proti odpovědi senzoru
        /// <c>$VNRRG,83,0,0,0,0,1000,0.000,+00.00000000,+000.00000000,+00000.000</c>):
        /// <c>UseMagModel, UseGravityModel, Resv1, Resv2, RecalcThreshold, Year, Lat, Lon, Alt</c>.</para>
        ///
        /// <para>⚠️ Zápis do registru je <b>nestálý</b> — po vypnutí senzoru je pryč. Pro trvalé
        /// uložení je potřeba <c>VNWNV</c>, a to je <b>vědomý ruční krok</b> (viz
        /// <c>deploy/vnrestore.sh</c>), ne něco, co má driver dělat při každém startu.</para>
        /// </summary>
        /// <param name="lla">Poloha robota; <b>zeměpisné souřadnice v RADIÁNECH</b> (převedou se).</param>
        /// <param name="cas">Datum pro model pole — pole se s časem mění, takže na roku záleží.</param>
        /// <param name="useMagModel">Zapnout model magnetického pole (kvůli deklinaci to je ten podstatný).</param>
        /// <param name="useGravityModel">Zapnout model gravitace.</param>
        /// <param name="recalcThresholdM">O kolik metrů se robot musí posunout, než se model přepočítá.</param>
        public static string ReferenceVectorConfig(LLA lla, DateTime cas,
                                                   bool useMagModel = true,
                                                   bool useGravityModel = true,
                                                   int recalcThresholdM = 1000)
        {
            if (lla == null) throw new ArgumentNullException(nameof(lla));

            // Desetinny rok, jak ho model pole ceka (2026,5 = polovina roku 2026). Den v roce
            // se deli 365,25 kvuli prestupnym rokum; na deklinaci, ktera se meni radove
            // 0,1 stupne za rok, je to stejne jedno - jde o to nemit tam nesmysl.
            double rok = cas.Year + (cas.DayOfYear - 1) / 365.25;

            return string.Format(CultureInfo.InvariantCulture,
                "VNWRG,{0},{1},{2},0,0,{3},{4:F3},{5:F8},{6:F8},{7:F3}",
                RegReferenceVectorConfig,
                useMagModel ? 1 : 0,
                useGravityModel ? 1 : 0,
                recalcThresholdM,
                rok,
                Conversions.Rad2Deg(lla.Latitude),
                Conversions.Rad2Deg(lla.Longitude),
                lla.Altitude);
        }

        /// <summary>Orámuje tělo příkazu do tvaru <c>$telo*XX</c> (XX = 8bitový XOR součet).</summary>
        public static string Frame(string body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            string s = "$" + body;
            return s + "*" + Checksum(s).ToString("X2", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 8bitový kontrolní součet VN (XOR bajtů mezi <c>$</c> a <c>*</c>) — tentýž algoritmus,
        /// jaký měly oba drivery ve své vlastní kopii.
        /// </summary>
        public static byte Checksum(string packet)
        {
            if (packet == null) throw new ArgumentNullException(nameof(packet));
            byte num = 0;
            for (int i = packet.Length > 0 && packet[0] == '$' ? 1 : 0;
                 i < packet.Length && packet[i] != '*'; i++)
                num ^= (byte)packet[i];
            return num;
        }
    }
}
