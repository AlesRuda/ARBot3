using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Nastaveni hledani koridoru. Vychozi hodnoty vzesly z merení nad zaznamem
    /// <c>records/20260821-095328.rec</c> (21. 8. 2026) — viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class CorridorConfig
    {
        /// <summary>Prah inlieru pro RANSAC prolozeni hranice primkou [m] - <b>slozka nezavisla
        /// na vzdalenosti</b> (chyba detekce hranice v blizkem poli).</summary>
        public double InlierThresholdM = 0.10;

        /// <summary>
        /// Prirustek prahu inlieru <b>na metr vzdalenosti</b> bodu od robotu [m/m].
        ///
        /// <para><b>Proc to je.</b> RANSAC meril vsechny body tymz metrem, coz plati jen kdyby
        /// mely stejnou nejistotu. Nameřeno 23. 8. 2026 (12 631 bodu proti mape): medián sedi na
        /// okraji vozovky v kazde vzdalenosti, ale rozptyl roste z ±5 cm na 1 m na −0,63/+0,40 m
        /// na 10 m. S jednim prahem jsou vzdalene body bud vsechny outliery, nebo je prah tak
        /// volny, ze projde i nesmysl - a RANSAC se pak chyti nahodneho zarovnani dvou
        /// rozptylenych bodu. Odtud <c>NotParallel</c> na rovnem useku.</para>
        ///
        /// <para>Vysledny prah pro bod ve vzdalenosti <c>r</c> je
        /// <c>InlierThresholdM + InlierThresholdPerMeter · r</c>. Nula = puvodni chovani
        /// (jeden prah pro vsechny).</para>
        ///
        /// <para><b>Vychozi 0,15 je namerena.</b> Sweep nad TYMIZ daty (421 dvojic snimku, zaznam
        /// 20260823-084807), <b>12 opakovani na variantu</b> - RANSAC je nedeterministicky
        /// (neseedovany <c>Random</c>), takze jedno mereni na variantu nestaci:</para>
        ///
        /// <code>
        ///   prirustek   Ok (rozpeti)      NotParallel
        ///   0 (puvodni) 158,9 (156-162)   244,8 (242-249)
        ///   0,05        166,4 (162-171)   236,7 (233-243)
        ///   0,10        169,2 (167-173)   234,6 (232-237)
        ///   0,15        175,8 (171-180)   230,9 (227-235)   &lt;- optimum
        ///   0,20        169,7 (165-174)   242,2 (237-246)
        ///   0,30        155,2 (151-158)   259,2 (256-264)
        /// </code>
        ///
        /// <para>Prinos je <b>+11 % prijatych, −6 % NotParallel</b> - rozpeti se s puvodnim stavem
        /// neprekryvaji, takze to neni sum. Nad 0,20 se to prudce lame: prah uz je tak volny, ze
        /// projde i nesmysl. Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public double InlierThresholdPerMeter = 0.15;

        /// <summary>
        /// Minimalni pocet inlieru na kazde strane.
        ///
        /// <para><b>Toto je ten podstatny gate.</b> Nad zaznamem zahodil 282 z 559 snimku a bez nej
        /// se do statistiky michaly primky prolozene 3–6 body, ktere vyjdou kolmo na cestu
        /// (sirka az 10 m, smer −88°). S nim: sirka sd 0,45 m misto 3,3 m.</para>
        /// </summary>
        public int MinInliers = 25;

        /// <summary>Minimalni pocet vstupnich bodu, aby se prokladani vubec zkousilo.</summary>
        public int MinPoints = 6;

        /// <summary>
        /// Z kolika bodu RANSAC pocita jednu HYPOTEZU primky.
        ///
        /// <para><b>Proc to neni minimalni vzorek.</b> Klasicky RANSAC losuje minimum (u primky
        /// dva body, tady se historicky braly tri) a spoleha, ze aspon jeden vzorek trefi same
        /// inliery. To dava smysl, kdyz jsou data "presne body + hrube outliery". Nase data ale
        /// takova nejsou: nameřeno 23. 8. 2026 medián hranicnich bodu <b>sedi na okraji vozovky
        /// v kazde vzdalenosti</b> a roste jen rozptyl. Zadne hrube outliery tedy pravidlem
        /// nejsou - je to <b>nevychyleny sum</b>. A na nevychyleny sum je vzorek ze dvou tri bodu
        /// to nejhorsi mozne: hypoteza nese cely jejich sum, takze RANSAC vybira mezi stovkami
        /// spatnych primek tu, ktera nahodou nasbira nejvic inlieru.</para>
        ///
        /// <para>Vic bodu ve vzorku znamena, ze uz sama hypoteza je prumerem - sum se v ni
        /// vykrati. Cena je mensi odolnost proti skutecnym outlierum (vetsi sance, ze se do
        /// vzorku nejaky dostane), proto se hodnota <b>meri</b>, ne hada.</para>
        /// </summary>
        public int ModelSamplePoints = 3;

        /// <summary>Nejvetsi pripustna odchylka smeru obou hranic [rad]; nad tim to neni koridor.</summary>
        public double MaxParallelErrorRad = 10 * Math.PI / 180;

        /// <summary>Rozumny rozsah sirky cesty [m] - mimo nej se koridor zahodi.</summary>
        public double MinWidthM = 0.5;

        /// <summary>Rozumny rozsah sirky cesty [m].</summary>
        public double MaxWidthM = 8.0;

        /// <summary>
        /// Dolni hranice sigma pricne polohy [m]. Odpovida <b>namerene opakovatelnosti</b> nad
        /// zaznamem (sd 3 cm, 21. 8. 2026) — tedy systematice, kterou rezidua prolozeni nevidi
        /// vubec (kalibrace kamer, rozliseni hloubky, definice "kde presne cesta konci").
        /// Bez podlahy by u cistych dat vysla milimetrova jistota, kterou si merenie nezaslouzi.
        /// </summary>
        public double SigmaFloorM = 0.03;

        /// <summary>Dolni hranice sigma smeru [rad].</summary>
        public double SigmaFloorRad = 0.5 * Math.PI / 180;
    }
}
