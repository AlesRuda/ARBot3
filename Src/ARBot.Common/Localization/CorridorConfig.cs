using System;
using ARBot.Common.Common;

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

        /// <summary>
        /// Cim se prokladá <b>konsenzualni sada</b> (ne hypoteza - tu vzdy dela RANSAC).
        ///
        /// <para><b>Proc to bylo podezrele.</b> Puvodni <c>Line2D.LinearRegesion</c> minimalizuje
        /// rezidua podel jedne osy, zatimco hradlovani inlieru i vysledna sigma se meri KOLMOU
        /// vzdalenosti - estimator tedy neminimalizoval to, co se pak vyhodnocuje. Podrobne
        /// v <see cref="LineFit"/>.</para>
        ///
        /// <para><b>Zmereno 24. 8. 2026: nezalezi na tom.</b> Ortogonalni prolozeni (TLS) dalo nad
        /// ctyrmi zaznamy chybu sirky proti mape <b>o chlup HORSI</b> nez osove (p50 0,0080 vs
        /// 0,0076 / 0,0078 vs 0,0077 / 0,0325 vs 0,0321 / 0,0379 vs 0,0377 m) - tedy zadny prinos.
        /// Ta nespojitost u 45 stupnu je sice skutecna, ale <b>numericky bezvyznamna</b>: na sweepu
        /// 35-55 stupnu se nejhorsi chyba smeru lisi 0,021 vs 0,018 stupne (test
        /// <c>PriUhluKolem45_jeOrtogonalniStabilnejsiNezOsova</c>). Duvod: pri zakladne radu metru
        /// a sumu radu centimetru daji oba estimatory skoro tutez primku, takze se prepina mezi
        /// dvema temer totoznymi prolozenimi. Vychozi stav proto zustava osovy; prepinac tu je,
        /// aby to nekdo nemusel merit znovu od nuly.</para>
        /// </summary>
        public LineFitMode FitMode = LineFitMode.LeastSquares;

        /// <summary>
        /// Kolikrat se po prolozeni konsenzualni sady <b>zopakuje hradlovani</b> uz proti prolozene
        /// primce (0 = puvodni chovani, jeden pruchod bez prehradlovani).
        ///
        /// <para><b>Nacpak to je.</b> Konsenzualni sada vznikne proti hypoteze z
        /// <see cref="ModelSamplePoints"/> bodu, tedy proti primce, ktera nese jejich sum. Pak se
        /// prolozi cela sada - primka je teď lepsi, ale sada uz se s ni neprehradluje. Body, ktere
        /// hruba hypoteza minula, tedy zustanou venku. Prehradlovani je pribere a prolozi znovu.</para>
        ///
        /// <para><b>Vychozi je 0, tedy vypnuto</b> — a stalo to jeden den omylu, takze to stoji za
        /// prectení cele. Rano to bylo zapnute na zaklade merení proti mape:</para>
        ///
        /// <code>
        ///   zaznam            |sirka-mapa| p90        p50
        ///   20260822-104759   0,0526 -> 0,0446  (-15 %)  0,0377 -> 0,0368
        ///   20260822-104827   0,0453 -> 0,0400  (-12 %)  0,0321 -> 0,0315
        ///   20260822-105031   0,0192 -> 0,0175  (-9 %)   0,0076 -> 0,0072
        ///   20260822-105003   0,0181 -> 0,0166  (-8 %)   0,0077 -> 0,0070
        /// </code>
        ///
        /// <para>Ctyri zaznamy ze ctyr, 6 opakovani na variantu, <b>bez selekcniho efektu</b> (pocet
        /// prijatych cyklu je u vsech variant totozny, takze se neporovnavaji jine snimky). Cena je
        /// 0,072 -> 0,087 ms na dvojici.</para>
        ///
        /// <para><b>POZOR, tohle mereni je slabsi, nez vypada</b> (dohledano tyz den nad novou mapou
        /// <c>OSM/SyntetickyRovny.osm</c>). Referenci mu delal <c>RoadCorridorMsg.MapWidth</c>, a to
        /// neni sirka z mapy, ale vystup <c>RoadWidthFilter.Estimate</c> — filtr, ktery se z merenii
        /// UCI. Merit presnost proti necemu, co merenie samo ovlivnuje, je mirne kruhove. Nad rovnou
        /// mapou, kde je sirka ZNAMA (2,000 m) a jde tedy merit proti pravde, je efekt
        /// prehradlovani <b>presne nulovy</b> — vsech sest variant estimatoru vyjde bit za bit
        /// stejne. Duvod: inlieru je 270 pri 265-270 bodech, tedy inliery jsou VSECHNY body,
        /// konsenzualni krok je no-op a kazda varianta se redukuje na "prolož primku vsemi body".</para>
        ///
        /// <para><b>VYSLEDEK: je to no-op a vychozi hodnota je proto zpatky na nule.</b> Doměřeno
        /// tyz den nad hlucnymi daty (<c>grassrough=0.12</c>, rezidua 0,0853 m), tedy presne tam, kde
        /// melo prehradlovani smysl mit: <b>vsechny varianty estimatoru vyjdou s prehradlovanim
        /// i bez nej stejne</b> (LS vychyleni sirky 0,0544 vs 0,0544; L1 0,0010 vs 0,0010; Tukey
        /// 0,0016 vs 0,0016).</para>
        ///
        /// <para><b>A je jasne, PROC to nikdy zabrat nemuze:</b> prah inlieru je
        /// <c>0,10 + 0,15·r</c>, tedy na 5 m 0,85 m — <b>10x volnejsi nez typicka rezidua</b>
        /// (0,085 m); na 8 m dokonce 15x. Hradlovani proto nema co vylucit, konsenzualni sada je
        /// vzdy skoro vsechny body (266 z ~270) a druhy pruchod prokladá tytez body znovu. Zabralo
        /// by to jen pri <b>hrubych outlierech</b> nebo po utazeni prahu.</para>
        ///
        /// <para><b>Pozor, rezidua se pritom mirne ZHORSI</b> (0,0713 -> 0,0720 m) - a presnost
        /// proti mape se zlepsi. Je to ukazka toho, ze rezidua nejsou presnost: primka prolozena
        /// pres vic bodu jimi prochazi o chlup dal, ale odpovida realite lip.</para>
        ///
        /// <para><b>Otevrena vyhrada.</b> Nad zaznamem <c>20260822-100403</c> (jediny, kde koridor
        /// skutecne propada - 112 z 258 Ok) prehradlovani <b>zhorsilo nerovnobeznost</b> 3,45° ->
        /// 4,7° pri stejnem poctu prijatych. Ten zaznam ale <b>nema mapovou referenci</b>
        /// (RoadCorridorMsg v nem neni), takze nejde rict, jestli je to ztrata presnosti, nebo jen
        /// jina self-konzistence. Otevreny ukol: zmerit prehradlovani na zaznamu, ktery je zaroven
        /// tezky A ma mapu (nebo ground truth).</para>
        /// </summary>
        public int RegatePasses = 0;

        /// <summary>
        /// Kde zacina Huberovo potlaceni [nasobek tolerance bodu]. Plati jen pro
        /// <see cref="LineFitMode.OrthogonalHuber"/>.
        ///
        /// <para><b>POZOR, nad 1,0 je Huber no-op</b> - a to z principu, ne omylem: hradlovani
        /// pousti do konsenzualni sady jen body s reziduem pod 1,0 nasobku vlastni tolerance,
        /// takze pri <c>k = 1,5</c> maji uvnitr sady vsechny body vahu presne 1. Zmereno to tak
        /// i vyslo (24. 8. 2026: nad zaznamy vysledek k nerozeznani od nevazeneho, jen 1,4x drazsi).
        /// Aby vaha vubec zabrala, musi byt <c>k</c> pod 1,0.</para>
        ///
        /// <para><b>A i pak to neni prinos.</b> Pri <c>k = 0,4</c> a prehradlovani vysla nad
        /// <c>20260822-100403</c> nerovnobeznost 1,75° misto 3,39°, ale <b>za cenu 90 prijatych
        /// cyklu misto 112</b>. To je vymena vytezku za self-konzistenci, ne dukaz presnosti -
        /// na zaznamech s mapovou referenci Huber presnost nezlepsil vubec. Proto zustava vypnuty
        /// (<see cref="FitMode"/> = <see cref="LineFitMode.LeastSquares"/>); hodnota je tu pro
        /// pripad, ze by se nasel tezky zaznam s mapou, kde to smysl da.</para>
        /// </summary>
        public double HuberK = 1.5;

        /// <summary>
        /// Meri Huber rezidua v jednotkach <b>tolerance bodu</b> (<c>true</c>, puvodni chovani),
        /// nebo si meritko vezme z <b>MAD rezidui</b> (<c>false</c>)?
        ///
        /// <para><b>Proc to je prepinac.</b> Tolerance je zamerne velmi volna (na 5 m je
        /// 0,10 + 0,15*5 = 0,85 m), aby prah pustil i vzdalene body. Rezidua jsou proti tomu
        /// radove centimetrove, takze standardizovane rezidum vyjde radove 0,05 - hluboko pod
        /// <see cref="HuberK"/>, a vaha <b>nikdy nezabere</b>. Zmereno 24. 8. 2026: Huber
        /// s toleranci je k nerozeznani od nevazeneho i pri <c>k = 0,25</c>.</para>
        ///
        /// <para>S meritkem z MAD je Huber skutecne robustni odhad. Nacpak to je potreba: rozdeleni
        /// odchylek hranicnich bodu od skutecneho okraje je <b>zesikmene</b> - median sedi na okraji
        /// (leva −1,8 mm, prava +0,9 mm), ale PRUMER je posunuty ven (+2,4 a +10,9 mm), protoze ma
        /// dlouhy chvost ven z cesty. Prolozeni nejmensimi kvadraty sleduje prumer, takze hlasi
        /// cestu sirsi, nez je - odtud systematicka odchylka sirky. Merit to umi
        /// <c>ARBot.Analyze edgebias</c>.</para>
        /// </summary>
        public bool HuberUsesTolerance = true;

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
