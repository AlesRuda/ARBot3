using System;

namespace ARBot.Common.Localization
{
    /// <summary>
    /// Proc korelace (ne)poslala korekci. Viz doc/map-correlation-localization.md.
    /// Jde do zpravy, aby bylo v telemetrii videt PROC se nekorigovalo.
    /// </summary>
    public enum MapCorrelationReason : byte
    {
        /// <summary>Shoda je pouzitelna; co se posle, rozhoduji stropy sigma.</summary>
        Ok = 0,

        /// <summary>Prilis malo dukaznich bunek - kamera jeste nedodala dost semantiky.</summary>
        TooFewEvidence = 1,

        /// <summary>Skore pod prahem - robot nejspis neni na mapovane ceste.</summary>
        LowScore = 2,

        /// <summary>Vzdaleny konkurent je skore blizko maxima (soubezna cesta, symetricka scena).</summary>
        Ambiguous = 3,

        /// <summary>Nalezeny posun je vetsi, nez se poza smi mylit - hlasi se ztrata lokalizace.</summary>
        OffsetTooLarge = 4,

        /// <summary>Zakriveni skore neodpovida maximu (plocha, sedlo, sum).</summary>
        NoPeak = 5,
    }

    /// <summary>
    /// Vysledek jednoho cyklu korelace gridu s mapou vcetne rozhodnuti, co poslat do fuze.
    /// Viz doc/map-correlation-localization.md.
    /// </summary>
    public sealed class MapCorrelationResult
    {
        /// <summary>Cas, ke kteremu vysledek plati (cas snapshotu gridu).</summary>
        public DateTime TimeStamp;

        /// <summary>Nalezeny posun na vychod [m]: skutecna poloha = odhad + Dx.</summary>
        public double Dx;

        /// <summary>Nalezeny posun na sever [m].</summary>
        public double Dy;

        /// <summary>Nalezena chyba kurzu [rad]: skutecny kurz = odhad + Phi.</summary>
        public double Phi;

        /// <summary>Skore shody v maximu (-1..1); zaroven metrika kvality.</summary>
        public double Score;

        /// <summary>Skore konkurenta podel URCENE osy (viz <see cref="CorrelationScorer.BestRivalAlongAxis"/>).</summary>
        public double SecondBestScore;

        /// <summary>
        /// Skore konkurenta podel VOLNE (kolme) osy; <c>-inf</c>, kdyz se nemeril (sigma volne osy
        /// nad stropem, nebo zadne maximum).
        ///
        /// <para><b>Proc je i tohle ve vysledku a ve zprave:</b> bez nej se v telemetrii nepozna,
        /// jestli se volna osa nepostala kvuli stropu sigma (zdravy, bezny stav na prime ceste),
        /// nebo protoze ji zamitl prave tenhle konkurent - a to je priznak zaparkovane vady
        /// "falesna podelna jistota" (doc/map-correlation-localization.md). Na realnych sikmych
        /// cestach je ocekavany prave druhy pripad, takze je to hlavni otazka faze 4.</para>
        /// </summary>
        public double SecondBestScoreLoose;

        /// <summary>Sigma lepe urcene osy posunu [m].</summary>
        public double SigmaTight;

        /// <summary>Sigma hore urcene osy posunu [m].</summary>
        public double SigmaLoose;

        /// <summary>Smer lepe urcene osy [rad], matematicky.</summary>
        public double TightAxisAngle;

        /// <summary>Sigma kurzu [rad].</summary>
        public double SigmaPhi;

        /// <summary>Kolik bunek gridu vstoupilo do korelace.</summary>
        public int EvidenceCells;

        /// <summary>Vaha dukazu, ktery ROZLISUJE mezi kandidaty (diagnostika k „honestni sigme").</summary>
        public double InformativeWeight;

        /// <summary>Kolik kandidatu se vyhodnotilo (diagnostika ceny).</summary>
        public int Candidates;

        /// <summary>Poslat merenie podel lepe urcene osy?</summary>
        public bool EmitTightAxis;

        /// <summary>Poslat merenie podel hore urcene osy?</summary>
        public bool EmitLooseAxis;

        /// <summary>Poslat korekci kurzu?</summary>
        public bool EmitHeading;

        /// <summary>Proc se (ne)korigovalo.</summary>
        public MapCorrelationReason Reason;

        /// <summary>Doba vypoctu cyklu.</summary>
        public TimeSpan ProcessingTime;

        /// <summary>
        /// Kolik korekci z korelace uz fuze zahodila jako starsi nez okno historie (kumulativne).
        /// Doplnuje korelator po odeslani; sama korelace to nespocita, je to zpetna vazba z fuze.
        /// Viz <see cref="Logs.MapCorrelationMsg.DroppedByFusion"/>.
        /// </summary>
        public long DroppedByFusion;

        /// <summary>Poslalo se aspon neco?</summary>
        public bool Emitted => EmitTightAxis || EmitLooseAxis || EmitHeading;

        /// <summary>
        /// Slozi vysledek a rozhodne, co poslat.
        ///
        /// <para><b>Poradi pravidel je soucast kontraktu</b> (a je testovane): malo dukazu -&gt;
        /// nizke skore -&gt; prilis velky posun -&gt; zadne maximum -&gt; nejednoznacnost. Diky tomu
        /// se v telemetrii nesplete "nemam data" s "mam data a nesouhlasi". Nejednoznacnost je
        /// POSLEDNI schvalne: konkurent se meri podel URCENE osy, a ta bez maxima neexistuje.</para>
        ///
        /// <para>Stropy sigma se posuzuji PER OSU, takze bezny cyklus na prime ceste posle jen
        /// pricnou korekci a podelnou vynecha.</para>
        /// </summary>
        /// <param name="rivalAlongTight">Skore nejlepsiho konkurenta posunuteho PODEL URCENE osy
        /// (<see cref="CorrelationScorer.BestRivalAlongAxis"/>). Blizky konkurent tady znamena, ze
        /// registrace muze sedet na JINE ceste - potlaci se cely cyklus. Bez maxima predej
        /// <c>double.NegativeInfinity</c>.</param>
        /// <param name="rivalAlongLoose">Skore konkurenta podel VOLNE (kolme) osy. Blizky konkurent
        /// tady znamena jen to, ze TA JEDNA osa je nespolehliva - potlaci se pouze ona, zbytek
        /// cyklu jde dal. Kdyz se volna osa neposila (sigma nad stropem), muze byt
        /// <c>double.NegativeInfinity</c>.</param>
        public static MapCorrelationResult From(DateTime t, ScanResult scan, CorrelationCovariance cov,
                                                int evidenceCells, double rivalAlongTight,
                                                double rivalAlongLoose, MapCorrelatorConfig cfg)
        {
            if (scan == null) throw new ArgumentNullException(nameof(scan));
            if (cfg == null) throw new ArgumentNullException(nameof(cfg));

            var r = new MapCorrelationResult
            {
                TimeStamp = t,
                Dx = scan.Dx,
                Dy = scan.Dy,
                Phi = scan.Phi,
                Score = scan.Score,
                SecondBestScore = rivalAlongTight,
                SecondBestScoreLoose = rivalAlongLoose,
                SigmaTight = cov.SigmaTight,
                SigmaLoose = cov.SigmaLoose,
                TightAxisAngle = cov.TightAxisAngle,
                SigmaPhi = cov.SigmaPhi,
                EvidenceCells = evidenceCells,
                InformativeWeight = cov.InformativeWeight,
                Candidates = scan.Candidates,
            };

            if (evidenceCells < cfg.MinEvidenceCells)
            {
                r.Reason = MapCorrelationReason.TooFewEvidence;
                return r;
            }
            if (scan.Score < cfg.MinScore)
            {
                r.Reason = MapCorrelationReason.LowScore;
                return r;
            }
            if (Math.Sqrt(scan.Dx * scan.Dx + scan.Dy * scan.Dy) > cfg.MaxOffsetM)
            {
                r.Reason = MapCorrelationReason.OffsetTooLarge;
                return r;
            }
            if (!cov.HasPeak)
            {
                r.Reason = MapCorrelationReason.NoPeak;
                return r;
            }
            // Nejednoznacnost se posuzuje AZ ZA NoPeak, protoze konkurent se meri podel osy - a ta
            // bez maxima neexistuje. Konkurent podel URCENE osy potlaci CELY cyklus: kdyz je vedle
            // stejne dobre reseni ve smeru, kde si myslime, ze polohu zname, muze registrace sedet
            // na jine ceste.
            // Referencni skore je NALEZENE maximum se stride nejhrubsi urovne, ne skore hrube
            // urovne: konkurent se vyhodnocuje kolem jemneho maxima, takze operandy prahu musi byt
            // z tehoz bodu. Viz ScanResult.CoarseStrideScoreAtPeak.
            double threshold = scan.CoarseStrideScoreAtPeak - cfg.AmbiguityMargin;
            if (rivalAlongTight > threshold)
            {
                r.Reason = MapCorrelationReason.Ambiguous;
                return r;
            }

            r.Reason = MapCorrelationReason.Ok;
            r.EmitTightAxis = cov.SigmaTight <= cfg.SigmaCeilingM;
            r.EmitHeading = cov.SigmaPhi <= cfg.SigmaCeilingHeadingRad;

            // Volna osa se posila jen kdyz projde stropem A NEMA blizkeho konkurenta. Konkurent
            // podel volne osy nediskvalifikuje cely cyklus - rika jen, ze prave tahle osa je
            // nespolehliva, takze se vynecha samostatne, stejne jako pri prekroceni stropu.
            //
            // Proc to tady vubec je: kdyz vyjde SigmaLoose omylem konecna (viz otevreny ukol
            // "falesna podelna jistota" v doc/map-correlation-localization.md), sla by do fuze
            // podelna korekce, kterou by NEHLIDAL zadny test nejednoznacnosti - konkurent se totiz
            // meri podel URCENE osy. Na prime ceste zarovnane s osami gridu je SigmaLoose nekonecna,
            // takze se tato podminka vubec neuplatni a falesna nejednoznacnost se nemuze vratit.
            r.EmitLooseAxis = cov.SigmaLoose <= cfg.SigmaCeilingM
                              && !(rivalAlongLoose > threshold);
            return r;
        }

        /// <summary>
        /// Snapshot vysledku jako zprava pro telemetrii a zaznam. Konverzi vlastni domena -
        /// zprava zustava pasivni DTO (viz CLAUDE.md).
        /// </summary>
        public Logs.MapCorrelationMsg ToLogMessage()
            => new Logs.MapCorrelationMsg
            {
                Dx = Dx,
                Dy = Dy,
                Phi = Phi,
                Score = Score,
                SecondBestScore = SecondBestScore,
                SecondBestScoreLoose = SecondBestScoreLoose,
                SigmaTight = SigmaTight,
                SigmaLoose = SigmaLoose,
                TightAxisAngle = TightAxisAngle,
                SigmaPhi = SigmaPhi,
                EvidenceCells = EvidenceCells,
                InformativeWeight = InformativeWeight,
                Candidates = Candidates,
                Emitted = Emitted,
                EmitTightAxis = EmitTightAxis,
                EmitLooseAxis = EmitLooseAxis,
                EmitHeading = EmitHeading,
                Reason = (byte)Reason,
                ProcessingMs = ProcessingTime.TotalMilliseconds,
                TimeStamp = TimeStamp,
                DroppedByFusion = DroppedByFusion,
            };
    }
}
