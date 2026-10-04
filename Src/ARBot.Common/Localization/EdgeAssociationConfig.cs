using ARBot.Common.Common;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Nastaveni <see cref="EdgeAssociator"/> — tedy rozhodnuti „po ktere ceste jedu".
    ///
    /// <para><b>Vahy se tu nenastavuji, protoze zadne nejsou.</b> Odchylka vzdalenosti [m]
    /// a odchylka smeru [rad] se secist nedaji; kdyz se ale kazda vydeli SVOJI sigmou, vznikne
    /// bezrozmerny chi-kvadrat se dvema stupni volnosti — a ten ma zname rozdeleni, takze ani
    /// prah neni odhad (5,99 = 95 %, 9,21 = 99 %). Viz doc/map-correlation-localization.md.</para>
    /// </summary>
    public sealed class EdgeAssociationConfig
    {
        /// <summary>
        /// Vybirat hranu podle chi-kvadratu pres vic kandidatu? <c>false</c> = puvodni chovani
        /// (nejblizsi hrana podle vzdalenosti), tedy A/B se stejnou zatezi.
        /// </summary>
        public bool Enabled = true;

        /// <summary>
        /// Kolik nejblizsich hran se posoudi; <b>0 = vsechny</b> (vychozi od 4. 10. 2026). Obe hrany
        /// obousmerne cesty se pocitaji za jednu.
        /// <para><b>Proc vsechny.</b> Kandidati se radi podle chi-kvadratu, ne podle vzdalenosti,
        /// a test nejednoznacnosti ma smysl jen tehdy, kdyz vidi KAZDEHO soupere, ktery muze
        /// vysledek zmenit. Pevne 4 nejblizsi USEKY (useky mezi sousednimi uzly OSM) to v huste siti
        /// nesplni: 17. 9. 2026 v Hviezdoslavove zabraly ctyri mista kratke spojky napric (veto
        /// azimutu), vyhrala ulice 15 m od pozy jako jediny kandidat a soubezna ulice v 19 m, ktera
        /// by dala Ambiguous, se do vyberu nevesla (<c>lok-assoc-velka-sigma-soubezna-ulice</c>).
        /// Polomer si chi-kvadrat urci sam: hrana dal nez <c>|poloha v koridoru| + √(Chi2Max +
        /// Chi2Margin)·σ</c> vysledek zmenit nemuze. Vypocetne je to zanedbatelne (sit se prochazi
        /// tak jako tak). Kladne cislo = stare chovani (4 nejblizsi) pro A/B nad zaznamy.</para>
        /// </summary>
        public int Candidates = 0;

        /// <summary>
        /// <b>Tvrde veto na azimut</b> [rad]: kandidat, jehoz sklon se od videneho koridoru lisi
        /// o vic, se neposuzuje vubec.
        ///
        /// <para><b>Proc vedle chi-kvadratu.</b> Soucet dovoli, aby vyborna pricna shoda
        /// vykompenzovala spatny uhel — jenze kolma ulice neni „trochu mimo", je to
        /// <b>kategoricky jina cesta</b>. Veto je navic nezavisle na tom, jak kdo nastavi podlahy
        /// sigem.</para>
        ///
        /// <para>✅ <b>Vedlejsi zisk:</b> rozhodnuti „kterym smerem cesta vede" ma nespojitost
        /// prave u 90°, tedy u cesty kolme na kurz. Veto takovy cyklus zamitne driv, nez se
        /// o smyslu vubec rozhoduje.</para>
        /// </summary>
        public double VetoRad = 45 * System.Math.PI / 180;

        /// <summary>
        /// <b>Podlaha</b> sigmy pricne polohy [m] — sklada se s tou z kovariance pozy pres maximum,
        /// ne kvadraticky.
        ///
        /// <para>⚠️ <b>Bez podlahy by prirazeni zdedilo optimismus filtru.</b> Nad
        /// <c>20260916-164926.rec</c> hlasi fuze sigmu pricne p50 1,41 m, pritom poza stoji
        /// 3–4 m od vozovky; chi-kvadrat pricne pak vyjde p50 4,24 i na spravne hrane. Tataz past
        /// jako <c>YprU</c> u kompasu, <c>gpsposstd</c> u GPS a <c>Reject</c> u gatingu korekci.</para>
        /// </summary>
        public double SigmaLateralFloorM = 3.0;

        /// <summary>
        /// <b>Podlaha</b> sigmy kurzu [rad].
        ///
        /// <para>⚠️ Nad <c>20260916-164926.rec</c> hlasi fuze sigmu kurzu <b>1,10°</b>, zatimco
        /// skutecna chyba kurzu je 15–20° (nezkalibrovany magnetometr) — chi-kvadrat kurzu vyjde
        /// <b>p50 220</b> i na spravne hrane a test by zamitl uplne vsechno. S podlahou 10° spadne
        /// na 4,10 a rozdeleni se rozestoupi.</para>
        ///
        /// <para><b>Od 27. 9. 2026 5°</b> (do te doby 10°): po kalibraci magnetometru je chyba kurzu
        /// odhadu za jizdy 2–4° a podlaha 10° nerozlisila segmenty zakrivene cesty (10–20° od sebe)
        /// - ~37 % prolozenych koridoru na Robotouru skoncilo jako nejednoznacne. Prepocet nad
        /// Kolem 3b / 4 (<c>ARBot.Analyze assocreplay</c>): +10 % / +9 % prirazenych cyklu, zadny
        /// novy na pricnou ulici (osa proti kurzu z GPS nad 30° 0,0 %), zmeneny vitez podle kurzu
        /// z GPS 2 : 0 k lepsimu. 3° by pridalo dal, ale je pod chybou kurzu - kdyz kurz ujede,
        /// zacne zamitat i spravnou hranu. Viz doc/map-correlation-localization.md.</para>
        /// </summary>
        public double SigmaHeadingFloorRad = 5 * System.Math.PI / 180;

        /// <summary>
        /// <b>Podlaha</b> sigmy podelne polohy pro <b>podelny presah</b> [m]: kandidat, za jehoz
        /// koncem usecky poza lezi o <c>d</c> metru, dostane k chi-kvadratu <c>(d / σ)²</c>, kde
        /// <c>σ = max(kovariance pozy podel hrany, tahle podlaha)</c> — sklada se pres maximum
        /// jako <see cref="SigmaLateralFloorM"/>. Kandidat, vedle ktereho robot stoji, prirazku
        /// nedostane.
        ///
        /// <para><b>Proc.</b> <see cref="RoadAxis.Relate"/> pocita pricnou polohu z <b>primky</b>
        /// useku. Sousedni usek tehoz asfaltu zalomeny o 1–2° ma ve vzdalenosti 50 m osu o metr
        /// vedle — vic nez <see cref="SameHypothesisLateralM"/> — a pri podlaze pricne sigmy 3 m je
        /// to remiza: 29. 9. 2026 v Modranech 51 % cyklu <c>AmbiguousEdge</c> a do fuze nic,
        /// poza ujela o 10 m. Do 26. 9. to skryval strop <c>MaxEdgeDistanceM</c> = 8 m, v zatackach
        /// (Robotour) to delalo nejednoznacnost i s nim. Prepocet nad 13 zaznamy
        /// (<c>ARBot.Analyze assocwhy</c>): zadny zmeneny vitez, ztrata do 0,4 %. Viz
        /// doc/map-correlation-localization.md.</para>
        ///
        /// <para>⚠️ <b>0 znamena „presah se nepocita"</b> (chovani do 29. 9. 2026 pro A/B), ne
        /// „bez podlahy" jako u <see cref="SigmaLateralFloorM"/>: sigma z fuze (desetiny metru)
        /// by jinak dala prirazky v tisicich a zamitla i spravny usek hned za uzlem.</para>
        /// </summary>
        public double SigmaLongitudinalFloorM = 3.0;

        /// <summary>
        /// Strop chi-kvadratu pro prijeti kandidata. Vychozi 9,21 = 99 % pro 2 stupne volnosti.
        /// </summary>
        public double Chi2Max = 9.21;

        /// <summary>
        /// O kolik musi nejlepsi kandidat porazit druheho, aby bylo prirazeni jednoznacne.
        ///
        /// <para><b>Proc to tu je.</b> Kdyz dve hrany vyjdou podobne, spravna odpoved je
        /// <b>neposlat nic</b>, ne vybrat tu o chlup lepsi — robot ma umet priznat, ze neví, na
        /// ktere ceste je. Rozdil 4 v chi-kvadratu je pomer verohodnosti ~7:1.</para>
        /// </summary>
        public double Chi2Margin = 4.0;

        /// <summary>
        /// Dva kandidati se povazuji za <b>TUZ hypotezu</b>, kdyz se jejich osa lisi min nez
        /// o tohle (pricne [m] a ve sklonu [rad]) — pak spolu o vitezstvi nesoutezi a druhy z nich
        /// se do testu nejednoznacnosti nepocita.
        ///
        /// <para>⚠️ <b>Bez tohohle by test nejednoznacnosti zamitl uplne vsechno na rovne ceste.</b>
        /// OSM cesta je v siti rozdelena na segmenty mezi lomovymi body, takze sousedni segment
        /// tehoz kusu asfaltu je samostatna hrana. Jeho VZDALENOST je vetsi (projekce se orizne na
        /// konec usecky), ale <see cref="RoadAxis.Relate"/> pocita pricnou polohu i sklon
        /// z <b>primky</b>, na ktere segment lezi — u kolinearniho souseda tedy vyjde
        /// <b>presne totez</b> a chi-kvadraty jsou shodne. Druhy kandidat by byl remiza sam se
        /// sebou.</para>
        ///
        /// <para>Kriteriem je schvalne <b>vysledek</b> (osa), ne identita cesty: tatáz ulice muze
        /// pokracovat pod jinym <c>WayId</c> a naopak jedna cesta se muze zalomit.</para>
        /// </summary>
        public double SameHypothesisLateralM = 0.5;

        /// <inheritdoc cref="SameHypothesisLateralM"/>
        public double SameHypothesisHeadingRad = 5 * System.Math.PI / 180;

        /// <summary>Kontrola mezi, at se preklep v profilu pozna pri startu, ne az z dat.</summary>
        public void Validate()
        {
            if (Candidates < 0)
                throw new System.ArgumentOutOfRangeException(nameof(Candidates), Candidates,
                    "Pocet kandidatu nemuze byt zaporny (0 = vsechny).");
            if (VetoRad <= 0 || VetoRad > System.Math.PI / 2)
                throw new System.ArgumentOutOfRangeException(nameof(VetoRad),
                    Conversions.Rad2Deg(VetoRad),
                    "Veto na azimut musi byt v (0; 90> stupnu - nad 90 nema smysl, primka nema orientaci.");
            if (SigmaLateralFloorM < 0)
                throw new System.ArgumentOutOfRangeException(nameof(SigmaLateralFloorM),
                    SigmaLateralFloorM, "Podlaha sigmy nemuze byt zaporna.");
            if (SigmaLongitudinalFloorM < 0 || double.IsNaN(SigmaLongitudinalFloorM))
                throw new System.ArgumentOutOfRangeException(nameof(SigmaLongitudinalFloorM),
                    SigmaLongitudinalFloorM, "Podlaha sigmy nemuze byt zaporna (0 = presah se nepocita).");
            if (SigmaHeadingFloorRad < 0)
                throw new System.ArgumentOutOfRangeException(nameof(SigmaHeadingFloorRad),
                    SigmaHeadingFloorRad, "Podlaha sigmy nemuze byt zaporna.");
            if (Chi2Max <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(Chi2Max), Chi2Max,
                    "Strop chi-kvadratu musi byt kladny.");
            if (Chi2Margin < 0)
                throw new System.ArgumentOutOfRangeException(nameof(Chi2Margin), Chi2Margin,
                    "Odstup od druheho kandidata nemuze byt zaporny.");
            if (SameHypothesisLateralM < 0)
                throw new System.ArgumentOutOfRangeException(nameof(SameHypothesisLateralM),
                    SameHypothesisLateralM, "Tolerance tehoz kandidata nemuze byt zaporna.");
            if (SameHypothesisHeadingRad < 0)
                throw new System.ArgumentOutOfRangeException(nameof(SameHypothesisHeadingRad),
                    SameHypothesisHeadingRad, "Tolerance tehoz kandidata nemuze byt zaporna.");
        }
    }
}
