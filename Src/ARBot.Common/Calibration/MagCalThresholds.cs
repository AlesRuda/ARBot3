namespace ARBot.Common.Calibration
{
    /// <summary>
    /// <b>Prahy verdiktu kalibrace magnetometru</b> na jednom miste.
    ///
    /// <para>⚠️ <b>Vsechna cisla jsou ODHAD</b> a naostro se nastavi az podle prvniho skutecneho
    /// mereni na zarizeni — stejna zasada jako u <c>perfwarn=70</c> (viz doc/perf-monitoring.md).
    /// <b>Nestavej na nich zavery</b>, dokud v doc/plan-vn100-kalibrace.md neni napsano, ze jsou
    /// zmerene.</para>
    ///
    /// <para>Ctyri veliciny, ktere verdikt tvori, jsou <b>nezavisle</b> a kazda rika neco jineho:
    /// <see cref="MaxCondition"/> <i>urcenost</i> soustavy (nutna podminka),
    /// kose <i>co ma clovek udelat dal</i>,
    /// <see cref="MaxSdMagnitudeG"/> a <see cref="MaxSdInclinationDeg"/> <i>kvalitu</i>,
    /// <see cref="MaxHalfSplitDeg"/> doplnkovou kontrolu. Viz doc/plan-vn100-kalibrace.md.</para>
    /// </summary>
    public static class MagCalThresholds
    {
        /// <summary>
        /// Podminenost, nad kterou je soustava neurcena.
        ///
        /// <para><b>Zmereno na syntetickych datech</b> (MagCalFitTests, 8. 9. 2026) — oddeleni je
        /// obrovske, pet radu:</para>
        /// <list type="table">
        /// <item><term>jen rovina (bez naklonu)</term><description>2,0 × 10⁸</description></item>
        /// <item><term>dva naklony na JEDNU stranu (0, +0,35)</term><description>4,7 × 10⁷</description></item>
        /// <item><term>tri naklony (0, ±0,35)</term><description>434</description></item>
        /// <item><term>tri velke (0, ±0,6)</term><description>187</description></item>
        /// <item><term>pet naklonu</term><description>209</description></item>
        /// </list>
        ///
        /// <para>⚠️ <b>Puvodni odhad 30 byl o pet radu mimo</b> a odmital i dokonala data. Proto
        /// je tu tabulka — aby to nikdo nehadal znovu.</para>
        ///
        /// <para>⚠️ <b>Na realnych datech se mezera ZUZI:</b> sum vyplni degenerovany smer, takze
        /// rovinna rotace bude mit podminenost mensi nez 10⁸. Tohle cislo se proto musi preverit
        /// pri prvnim mereni na zarizeni. Primarni pokyn pro obsluhu jsou ale <b>kose pokryti</b> —
        /// to je kriterium geometricke, tedy na sumu nezavisle.</para>
        ///
        /// <para>⚠️ <b>Dva naklony nestaci, kdyz jsou na tutéz stranu.</b> Teprve par +/− zlomi
        /// symetrii; hlida to <see cref="MinTiltedGroups"/> spolu s <see cref="MinTiltGroups"/>.</para>
        /// </summary>
        public const double MaxCondition = 1.0e4;

        /// <summary>Pocet azimutovych kosu (po 15 stupnich).</summary>
        public const int AzimuthBins = 24;

        /// <summary>Kolik vzorku musi byt v kazdem azimutovem kosi.</summary>
        public const int MinPerAzimuthBin = 20;

        /// <summary>Kolik naklonovych skupin celkem.</summary>
        public const int MinTiltGroups = 3;

        /// <summary>Z toho kolik odklonenych aspon <see cref="MinTiltDeg"/>.</summary>
        public const int MinTiltedGroups = 2;

        /// <summary>Co se jeste pocita jako naklon [deg].</summary>
        public const double MinTiltDeg = 15.0;

        /// <summary>Rozptyl <c>|B|</c> po korekci [G].</summary>
        public const double MaxSdMagnitudeG = 0.005;

        /// <summary>Rozptyl sklonu po korekci [deg].</summary>
        public const double MaxSdInclinationDeg = 0.5;

        /// <summary>Rozdil v oprave kurzu mezi prvni a druhou polovinou dat [deg].</summary>
        public const double MaxHalfSplitDeg = 2.0;
    }
}
