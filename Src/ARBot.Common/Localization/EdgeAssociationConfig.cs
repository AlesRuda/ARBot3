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
        /// Kolik nejblizsich hran se posoudi. Obe hrany obousmerne cesty se pocitaji za jednu
        /// (<see cref="Maps.OsmNav.Graph.RoadNetwork.NearestEdges"/>).
        /// </summary>
        public int Candidates = 4;

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
        /// <para>Po oprave magnetometru se podlaha snizi (~3°) a test se <b>zostri sam</b>.</para>
        /// </summary>
        public double SigmaHeadingFloorRad = 10 * System.Math.PI / 180;

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
            if (Candidates < 1)
                throw new System.ArgumentOutOfRangeException(nameof(Candidates), Candidates,
                    "Pocet kandidatu musi byt aspon 1.");
            if (VetoRad <= 0 || VetoRad > System.Math.PI / 2)
                throw new System.ArgumentOutOfRangeException(nameof(VetoRad),
                    Conversions.Rad2Deg(VetoRad),
                    "Veto na azimut musi byt v (0; 90> stupnu - nad 90 nema smysl, primka nema orientaci.");
            if (SigmaLateralFloorM < 0)
                throw new System.ArgumentOutOfRangeException(nameof(SigmaLateralFloorM),
                    SigmaLateralFloorM, "Podlaha sigmy nemuze byt zaporna.");
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
