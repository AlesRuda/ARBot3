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
        /// <para>⚠️ <b>NAD REALNYMI DATY JE TENHLE PRAH NEUCINNY — zmereno 8. 9. 2026.</b>
        /// Nad 452 s venkovni jizdy (<c>records/test/20260907-170728.rec</c>, 45 185 vzorku VN100)
        /// vysla podminenost <b>352,2</b>, tedy hluboko POD prahem — ackoli naklony v datech
        /// nejsou vubec (0 z 2 odklonenych skupin) a vysledek je nesmysl: <c>C[2,2] = 38,0</c>
        /// misto ~1,1, <c>sd(|B|)</c> po korekci 27× nad prahem, <c>sd(sklonu)</c> 70×. Jizda po
        /// nerovnem terenu degenerovany smer vyplni, ale <b>sumem</b>: soustava je numericky
        /// resitelna a statisticky porad podurcena. Mezera proti syntetice (2,0 × 10⁸) se tedy
        /// zuzila o <b>pet radu</b>.</para>
        ///
        /// <para>✅ <b>Brana pritom drzela — jen ji nedrzela podminenost:</b> verdikt
        /// NEPOUZITELNE vysel z <b>kosu pokryti</b> a ze <b>zbytku</b>. Primarni kriterium jsou
        /// proto kose (geometricke, na sumu nezavisle) a zbytky, ne tohle cislo. Naostro se
        /// nastavi az podle rotacniho testu na zarizeni (Task 10); snizovat ho podle jednoho
        /// jizdniho zaznamu by bylo hadani.</para>
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

        /// <summary>
        /// Rozptyl <c>|B|</c> po korekci <b>samotnou koulí</b> [G] — nad tím se pokládá za
        /// prokázané, že se <b>měnilo pole</b>, ne že chybí náklon.
        ///
        /// <para><b>Musí být řádově volnější než <see cref="MaxSdMagnitudeG"/>, a není to
        /// změkčení kritéria</b>: koule neopravuje měkké železo, takže i nad dokonalými daty
        /// nechá zbytek úměrný jeho velikosti. S maticí z referenčního exportu senzoru
        /// (diagonála 1,222 / 1,175 / 1,081, tedy rozptyl ~13 %) je ten zbytek v desítkách mG.
        /// Kdyby se sem dal práh elipsoidy, hlásila by mise „pole se měnilo" pokaždé — tedy
        /// právě tam, kde je kalibrace nejvíc potřeba.</para>
        ///
        /// <para><b>Změřeno na syntetice 10. 9. 2026</b> (<c>MagCalFitTests</c>) — mezi tím, co
        /// nechá stát měkké železo, a tím, co udělá posun pole, je překryv:</para>
        /// <list type="table">
        /// <item><term>měkké železo 1,222/1,175/1,081 (referenční export), pole konstantní</term><description>0,0022 G</description></item>
        /// <item><term>měkké železo 1,5/1,0/0,8 (patologické), pole konstantní</term><description>0,0179 G</description></item>
        /// <item><term>posun pole o 0,02 G uprostřed měření</term><description>0,0101 G</description></item>
        /// <item><term>posun pole o 0,05 G</term><description>0,0263 G</description></item>
        /// <item><term>posun pole o 0,10 G</term><description>0,0589 G</description></item>
        /// </list>
        ///
        /// <para>⚠️ Z toho plyne <b>skutečná mez, ne volba ladění</b>: posun pole menší než
        /// zhruba <b>0,05 G</b> se pod měkkým železem schová a rozpoznat ho takhle nelze.
        /// Práh je nad patologickým měkkým železem, aby mise nehlásila „pole se měnilo" tam,
        /// kde je kalibrace nejvíc potřeba.</para>
        ///
        /// <para>⚠️ Naostro se nastaví podle prvního běhu v poli, stejně jako ostatní prahy
        /// v téhle třídě.</para>
        /// </summary>
        public const double MaxSphereSdMagnitudeG = 0.030;

        /// <summary>
        /// Kolikrát nejvýš se smí lišit poloměr proložené koule od referenčního <c>|B|</c>,
        /// než se výsledek zahodí.
        ///
        /// <para>⚠️ <b>Bez téhle brány projde rovinná rotace a vrátí nesmysl</b> — změřeno
        /// 10. 9. 2026: rotace na rovině s měkkým železem dá podmíněnost <b>538</b> (pod prahem)
        /// a <c>sd(|B|)</c> <b>0,0000</b> (taky pod prahem), ale bias vedle o <b>476 787 G</b>.
        /// Proložením rovinné elipsy je totiž koule o poloměru v řádu 10⁶ — tedy skoro rovina —
        /// a protože se měřítko normuje právě tím poloměrem, zbytek se srovná k nule.
        /// <b>Podmíněnost ani zbytek to tedy nechytí, jen velikost poloměru.</b></para>
        ///
        /// <para>Trojka je s rezervou: realistické měkké železo dá měřítko ~1,24, patologické
        /// nanejvýš ~1,6. Odchylka nad trojnásobek už není kalibrace, ale vadný senzor.</para>
        /// </summary>
        public const double MaxSphereScale = 3.0;

        /// <summary>Rozdil v oprave kurzu mezi prvni a druhou polovinou dat [deg].</summary>
        public const double MaxHalfSplitDeg = 2.0;
    }
}
