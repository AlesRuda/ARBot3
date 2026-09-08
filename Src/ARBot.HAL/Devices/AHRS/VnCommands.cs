using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
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

        /// <summary>Registr 21: referenční vektory pole a gravitace (odtud se bere `|B|`).</summary>
        public const int RegMagGravityReference = 21;

        /// <summary>Registr 23: kompenzace magnetometru — <c>m_comp = C · (m_raw − B)</c>.</summary>
        public const int RegMagCompensation = 23;

        /// <summary>Registr 44: řízení palubní HSI kalibrace.</summary>
        public const int RegMagCalControl = 44;

        /// <summary>Registr 47: kalibrace, kterou spočítal <b>sám senzor</b> (nezávislá kontrola).</summary>
        public const int RegCalculatedHsi = 47;

        /// <summary>
        /// Tělo příkazu pro <b>registr 23 (Magnetometer Compensation)</b>.
        ///
        /// <para>Čísla se předávají už zformátovaná
        /// (<c>ARBot.Common.Calibration.MagCalResult.ToVnwrg23</c>), protože právě tam je
        /// zaručeno, že mají desetinnou <b>tečku</b>. ⚠️ Čárka by rozbila příkaz oddělený
        /// čárkami — tatáž past, na kterou naběhl registr 83 (viz hlavička této třídy).</para>
        ///
        /// <para>⚠️ Zápis je <b>nestálý</b>; pro trvalé uložení je potřeba <see cref="SaveToFlash"/>.</para>
        /// </summary>
        public static string MagnetometerCompensation(string dvanactCisel)
        {
            if (string.IsNullOrWhiteSpace(dvanactCisel))
                throw new ArgumentNullException(nameof(dvanactCisel));
            int n = dvanactCisel.Split(',').Length;
            if (n != 12)
                throw new ArgumentException($"Ceka se 12 cisel, prislo {n}.", nameof(dvanactCisel));
            if (dvanactCisel.Contains(' '))
                throw new ArgumentException("Prikaz nesmi obsahovat mezery.", nameof(dvanactCisel));
            return $"VNWRG,{RegMagCompensation},{dvanactCisel}";
        }

        /// <summary>
        /// Tělo příkazu pro <b>registr 44 (Magnetometer Calibration Control)</b>:
        /// <c>HSIMode, HSIOutput, ConvergeRate</c>.
        ///
        /// <para><c>HSIOutput</c> zůstává <b>1 = NoOnboard</b> i při zapnutém <c>Run</c>: senzor
        /// výsledek spočítá do registru 47, ale <b>neaplikuje</b>. Je to <b>nezávislá kontrola</b>
        /// našeho proložení, ne druhá kalibrace, která by se s naší míchala. Viz
        /// doc/plan-vn100-kalibrace.md.</para>
        /// </summary>
        public static string MagCalControl(bool run)
            => $"VNWRG,{RegMagCalControl},{(run ? 1 : 0)},1,5";

        /// <summary>Tělo čtecího příkazu.</summary>
        public static string ReadRegister(int reg)
        {
            if (reg < 0 || reg > 255) throw new ArgumentOutOfRangeException(nameof(reg));
            return $"VNRRG,{reg}";
        }

        /// <summary>
        /// Uložení celé sady registrů do flash.
        ///
        /// <para>⚠️ Ukládá <b>všechno, jak to je právě v RAM</b> — tedy i to, co tam zapsal driver
        /// při startu (ADOR a binární výstup). Je to neškodné (driver si je píše při každém
        /// startu), ale flash se v těch položkách rozejde s referenčním exportem.</para>
        ///
        /// <para>⚠️ Zápis do flash jde ověřit jen zpětným čtením, a to čte z <b>RAM</b>.
        /// <b>Skutečný test je až vypnutí a zapnutí robota.</b></para>
        /// </summary>
        public static string SaveToFlash() => "VNWNV";

        /// <summary>
        /// <b>Vytáhne odpověď na <c>VNRRG,&lt;reg&gt;</c> z BAJTOVÉHO PROUDU.</b>
        ///
        /// <para>⚠️ <b>Proč ne po řádcích.</b> Driver přepne senzor do binárního režimu, takže po
        /// lince teče ~9 kB/s binárních dat a ASCII odpovědi jsou v nich <b>utopené</b> — bajt
        /// <c>0x0A</c> se v binárních datech vyskytuje běžně, takže dělení na řádky rozseká
        /// odpověď uprostřed. Hledá se proto rámec <c>$VN…*XX</c> v celém vzorku.
        /// <c>deploy/vnprobe.sh</c> na tuhle past naběhl a má ji v hlavičce.</para>
        ///
        /// <para>Kontrolní součet se <b>ověřuje</b>: v binárním toku se posloupnost
        /// <c>$…*XX</c> může vyskytnout i náhodou.</para>
        /// </summary>
        /// <param name="buffer">Vzorek bajtů z linky (může obsahovat binární data i smetí).</param>
        /// <param name="reg">Číslo registru, na který se čeká odpověď.</param>
        /// <param name="values">Hodnoty z odpovědi; <c>null</c> při nenalezení.</param>
        public static bool TryParseResponse(byte[] buffer, int reg, out double[] values)
        {
            values = null;
            if (buffer == null || buffer.Length < 8) return false;
            string ocekavano = $"VNRRG,{reg},";

            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i] != (byte)'$') continue;

                int hvezda = -1;
                for (int j = i + 1; j < buffer.Length && j - i < 512; j++)
                {
                    if (buffer[j] == (byte)'*') { hvezda = j; break; }
                    // Bajt mimo tisknutelne ASCII => tohle nebyl zacatek ramce.
                    if (buffer[j] < 0x20 || buffer[j] > 0x7E) break;
                }
                if (hvezda < 0 || hvezda + 2 >= buffer.Length) continue;

                string telo = Encoding.ASCII.GetString(buffer, i + 1, hvezda - i - 1);
                if (!telo.StartsWith(ocekavano, StringComparison.Ordinal)) continue;

                string souctem = Encoding.ASCII.GetString(buffer, hvezda + 1, 2);
                if (!byte.TryParse(souctem, NumberStyles.HexNumber, CultureInfo.InvariantCulture,
                                   out byte prislo)
                    || Checksum(telo) != prislo)
                {
                    // Diagnostika do Trace, ne Debug: v Release na zarizeni by po poruse
                    // nezustala zadna stopa (pravidlo projektu).
                    Trace.WriteLine($"VN100: odpoved na registr {reg} ma vadny kontrolni soucet"
                                    + " - zahozeno.");
                    continue;
                }

                var casti = telo.Substring(ocekavano.Length).Split(',');
                var v = new double[casti.Length];
                for (int k = 0; k < casti.Length; k++)
                    if (!double.TryParse(casti[k], NumberStyles.Float, CultureInfo.InvariantCulture,
                                         out v[k]))
                    {
                        Trace.WriteLine($"VN100: registr {reg} - necitelna hodnota '{casti[k]}'.");
                        return false;
                    }

                values = v;
                return true;
            }
            return false;
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
